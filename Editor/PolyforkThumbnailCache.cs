using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Lazily downloads gallery thumbnails and keeps them on disk, so reopening the window
    /// is instant and scrolling never re-hits the network.
    /// </summary>
    public sealed class PolyforkThumbnailCache : IDisposable
    {
        /// <summary>
        /// Downloads at once. Scrolling asks for whatever came into view, and without a cap a
        /// flick down the catalogue opens a request per card - which does not make any of
        /// them arrive sooner, it just makes the one you are looking at queue behind ninety
        /// you have already scrolled past.
        /// </summary>
        const int MaxConcurrent = 6;

        /// <summary>
        /// Textures built per editor tick. LoadImage decodes a PNG on the main thread, so a
        /// batch landing together decodes together and drops a frame. Two a tick is about
        /// 120 a second, faster than anyone scrolls, and never a visible stall.
        /// </summary>
        const int DecodesPerTick = 2;

        /// <summary>Minimum gap between repaints. A repaint re-renders the 3D preview too, so
        /// one per arriving thumbnail was most of the choppiness.</summary>
        const double RepaintInterval = 0.08d;

        readonly PolyforkClient _client;
        readonly string _dir;
        readonly Dictionary<string, Texture2D> _textures = new();
        readonly HashSet<string> _known = new();
        readonly CancellationTokenSource _cts = new();

        /// <summary>Requested but not started. A stack, not a queue: the newest request is
        /// the one on screen, and the oldest is somewhere the user scrolled past.</summary>
        readonly List<string> _pending = new();

        /// <summary>Downloaded, waiting for a turn to become a texture.</summary>
        readonly Queue<(string url, byte[] bytes)> _decoded = new();

        int _active;
        bool _dirty;
        double _lastRepaint;

        /// <summary>Raised when a thumbnail arrives, so the window can repaint.</summary>
        public event Action Changed;

        public PolyforkThumbnailCache(PolyforkClient client)
        {
            _client = client;
            _dir = Path.Combine(Path.GetTempPath(), "polyfork-thumbs");
            Directory.CreateDirectory(_dir);

            EditorApplication.update += Tick;
        }

        /// <summary>
        /// Turns finished downloads into textures, a couple at a time, and repaints no more
        /// often than the eye needs. This is the whole of what makes scrolling smooth: the
        /// downloads were always asynchronous, but everything they triggered on arrival -
        /// decode and repaint - happened at once and on the main thread.
        /// </summary>
        void Tick()
        {
            for (var i = 0; i < DecodesPerTick && _decoded.Count > 0; i++)
            {
                var (url, bytes) = _decoded.Dequeue();
                if (_cts.IsCancellationRequested) return;

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                if (tex.LoadImage(bytes))
                {
                    tex.filterMode = FilterMode.Bilinear;
                    _textures[url] = tex;
                    _dirty = true;
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }

            Pump();

            if (!_dirty || EditorApplication.timeSinceStartup - _lastRepaint < RepaintInterval) return;

            _dirty = false;
            _lastRepaint = EditorApplication.timeSinceStartup;
            Changed?.Invoke();
        }

        /// <summary>Starts whatever the concurrency cap has room for, newest first.</summary>
        void Pump()
        {
            while (_active < MaxConcurrent && _pending.Count > 0)
            {
                var last = _pending.Count - 1;
                var url = _pending[last];
                _pending.RemoveAt(last);

                _active++;
                _ = FetchAsync(url);
            }
        }

        /// <summary>
        /// The cached texture, or null - never starts a download.
        ///
        /// For cards that are laid out but off screen: they should draw whatever has already
        /// arrived and ask for nothing, so scrolling decides what gets fetched rather than
        /// catalogue size.
        /// </summary>
        public Texture2D Peek(string url)
            => !string.IsNullOrEmpty(url) && _textures.TryGetValue(url, out var tex) ? tex : null;

        /// <summary>
        /// The thumbnail if it has arrived, otherwise null, having asked for it. Safe to call
        /// every repaint: a URL is only ever queued once.
        /// </summary>
        public Texture2D Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (_textures.TryGetValue(url, out var tex)) return tex;

            if (_known.Add(url))
            {
                _pending.Add(url);
                Pump();
            }

            return null;
        }

        async Task FetchAsync(string url)
        {
            try
            {
                var path = Path.Combine(_dir, Key(url) + ".png");
                byte[] bytes = null;

                if (File.Exists(path))
                {
                    try { bytes = await File.ReadAllBytesAsync(path, _cts.Token); }
                    catch (IOException) { bytes = null; }
                }

                if (bytes == null || bytes.Length == 0)
                {
                    bytes = await _client.GetBytesAsync(url, _cts.Token);
                    try { await File.WriteAllBytesAsync(path, bytes, CancellationToken.None); }
                    catch (IOException) { /* cache is best-effort */ }
                }

                if (_cts.IsCancellationRequested) return;

                // Handed to the tick rather than decoded here, so several finishing together
                // cannot decode together.
                if (bytes is { Length: > 0 }) _decoded.Enqueue((url, bytes));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // A missing thumbnail just renders as a placeholder.
            }
            finally
            {
                _active--;
                Pump();          // a slot just freed up
            }
        }

        static string Key(string url)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
            var sb = new StringBuilder(24);
            for (var i = 0; i < 12; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        public void Dispose()
        {
            EditorApplication.update -= Tick;
            _cts.Cancel();
            _pending.Clear();
            _decoded.Clear();
            foreach (var tex in _textures.Values)
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
            _textures.Clear();
            _cts.Dispose();
        }
    }
}
