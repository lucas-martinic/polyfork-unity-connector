using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// The shared clip pack every imported character animates from.
    ///
    /// Characters ship a rig and no clips, which is the right call - baking one opinionated
    /// walk into every character would be the store deciding how your game moves - but it
    /// leaves a rigged model standing perfectly still on arrival, which reads as broken.
    /// So the connector fetches the pack polyfork.dev publishes for exactly this, once per
    /// project, and every character imported afterwards comes in already idling.
    ///
    /// Downloaded rather than shipped in the package: 2.8 MB of Mixamo clips is a lot to put
    /// in every consumer's project, most of which import no characters at all.
    /// </summary>
    static class PolyforkAnimationPack
    {
        const string Url = "https://polyfork.dev/anim/xbot.glb";

        /// <summary>Inside Assets, because a clip referenced by a prefab has to be an asset.</summary>
        const string Folder = "Assets/Polyfork/Animations";
        const string Path = Folder + "/polyfork-clips.glb";

        public static bool IsInstalled => File.Exists(Path);

        /// <summary>
        /// Fetches the pack before anything is instantiated.
        ///
        /// Downloading 2.8 MB is not a thing to do while a staging object sits in the user's
        /// open scene: the object is visible for however long the network takes, which is
        /// what turned "a frame of flicker" into a model that appeared, waited around and
        /// then vanished.
        /// </summary>
        public static Task PrewarmAsync() => LoadAsync();

        /// <summary>
        /// The clips, fetching the pack first if this project has not got it.
        ///
        /// Returns an empty array rather than throwing: a character that imports without
        /// animation is worse than one with, and much better than an import that failed.
        /// </summary>
        public static async Task<AnimationClip[]> LoadAsync()
        {
            if (!IsInstalled && !await DownloadAsync()) return Array.Empty<AnimationClip>();

            return AssetDatabase.LoadAllAssetsAtPath(Path)
                .OfType<AnimationClip>()
                // glTFast leaves a __preview__ clip behind on some imports; it is not one.
                .Where(c => c != null && !c.name.StartsWith("__", StringComparison.Ordinal))
                .OrderBy(c => c.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static async Task<bool> DownloadAsync()
        {
            try
            {
                Directory.CreateDirectory(Folder);

                using var req = UnityWebRequest.Get(Url);
                req.timeout = 120;

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Polyfork] could not fetch the animation pack ({req.error}). " +
                                     "Characters will import without clips.");
                    return false;
                }

                File.WriteAllBytes(Path, req.downloadHandler.data);
                AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);

                Debug.Log($"[Polyfork] animation pack saved to {Path}.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Polyfork] could not install the animation pack ({e.Message}).");
                return false;
            }
        }

        /// <summary>
        /// Gives a freshly imported character an Animator and a set of clips bound to its own
        /// skeleton, idling.
        ///
        /// Saved beside the model rather than into a shared folder: the curves address THIS
        /// hierarchy by path, so a set retargeted for one character is only correct for
        /// another by coincidence.
        /// </summary>
        public static async Task<bool> SetUpAsync(GameObject prefabInstance, string assetId, string folder)
        {
            if (prefabInstance == null) return false;

            // Nothing to animate if nothing is skinned.
            if (prefabInstance.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) return false;

            var source = await LoadAsync();
            if (source.Length == 0) return false;

            var dir = $"{folder}/{assetId}-animations";
            Directory.CreateDirectory(dir);

            var bound = new System.Collections.Generic.List<AnimationClip>();

            foreach (var clip in source)
            {
                var rebound = PolyforkClipRetarget.Rebind(clip, prefabInstance.transform);
                if (rebound == null) continue;

                var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{clip.name}.anim");
                AssetDatabase.CreateAsset(rebound, path);
                bound.Add(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
            }

            if (bound.Count == 0)
            {
                AssetDatabase.DeleteAsset(dir);
                return false;
            }

            /* Never ?? a UnityEngine.Object.
             *
             * Unity overloads == to report a destroyed or missing object as null, and ??
             * does not go through that overload - it tests the reference. So the coalesce
             * kept a component that "exists" only as far as C# is concerned, and the next
             * line dereferenced it:
             *
             *   There is no 'Animator' attached to the "Village-engineer" game object
             *
             * which surfaced as "could not write a prefab", because the throw took the whole
             * prefab with it. */
            var animator = prefabInstance.GetComponent<Animator>();
            if (animator == null) animator = prefabInstance.AddComponent<Animator>();
            animator.applyRootMotion = false;

            var player = prefabInstance.GetComponent<PolyforkCharacterAnimation>();
            if (player == null) player = prefabInstance.AddComponent<PolyforkCharacterAnimation>();

            player.clips = bound.ToArray();
            player.current = -1;      // -1 means "the default", which is found by name

            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
