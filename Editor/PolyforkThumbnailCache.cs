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
        readonly PolyforkClient _client;
        readonly string _dir;
        readonly Dictionary<string, Texture2D> _textures = new();
        readonly HashSet<string> _inFlight = new();
        readonly CancellationTokenSource _cts = new();

        /// <summary>Raised when a thumbnail arrives, so the window can repaint.</summary>
        public event Action Changed;

        public PolyforkThumbnailCache(PolyforkClient client)
        {
            _client = client;
            _dir = Path.Combine(Path.GetTempPath(), "polyfork-thumbs");
            Directory.CreateDirectory(_dir);
        }

        /// <summary>
        /// Returns the thumbnail if available, otherwise null and starts fetching it.
        /// Safe to call every repaint.
        /// </summary>
        /// <summary>
        /// The cached texture, or null - never starts a download.
        ///
        /// For cards that are laid out but off screen: they should draw whatever has already
        /// arrived and ask for nothing, so scrolling decides what gets fetched rather than
        /// catalogue size.
        /// </summary>
        public Texture2D Peek(string url)
            => !string.IsNullOrEmpty(url) && _textures.TryGetValue(url, out var tex) ? tex : null;

        public Texture2D Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (_textures.TryGetValue(url, out var tex)) return tex;
            if (_inFlight.Contains(url)) return null;

            _inFlight.Add(url);
            _ = FetchAsync(url);
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

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                if (tex.LoadImage(bytes))
                {
                    tex.filterMode = FilterMode.Bilinear;
                    _textures[url] = tex;
                    Changed?.Invoke();
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
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
                _inFlight.Remove(url);
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
            _cts.Cancel();
            foreach (var tex in _textures.Values)
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
            _textures.Clear();
            _cts.Dispose();
        }
    }
}
