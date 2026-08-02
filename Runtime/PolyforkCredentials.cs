using System;
using System.IO;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// Resolves the Polyfork API key from somewhere that is not the scene file.
    ///
    /// A key typed into the inspector is serialised into the .unity asset and ships with
    /// the repo, so it is supported but treated as the last resort. The earlier sources
    /// keep the key out of version control while still working in a device build.
    ///
    /// Order:
    ///   1. POLYFORK_API_KEY environment variable  (editor / desktop / CI)
    ///   2. StreamingAssets/polyfork.key           (survives into an Android build)
    ///   3. persistentDataPath/polyfork.key        (side-loadable onto a headset)
    ///   4. the inspector value                    (convenient, but it is committed)
    /// </summary>
    public static class PolyforkCredentials
    {
        public const string EnvironmentVariable = "POLYFORK_API_KEY";
        public const string KeyFileName = "polyfork.key";

        /// <summary>Where the resolved key came from, for logging.</summary>
        public enum Source
        {
            None,
            Environment,
            StreamingAssets,
            PersistentData,
            Inspector
        }

        public static string Resolve(string inspectorValue, out Source source)
        {
            source = Source.None;

            var env = SafeEnvironment();
            if (!string.IsNullOrWhiteSpace(env))
            {
                source = Source.Environment;
                return env.Trim();
            }

            var streaming = ReadKeyFile(Path.Combine(Application.streamingAssetsPath, KeyFileName));
            if (streaming != null)
            {
                source = Source.StreamingAssets;
                return streaming;
            }

            var persistent = ReadKeyFile(Path.Combine(Application.persistentDataPath, KeyFileName));
            if (persistent != null)
            {
                source = Source.PersistentData;
                return persistent;
            }

            if (!string.IsNullOrWhiteSpace(inspectorValue))
            {
                source = Source.Inspector;
                return inspectorValue.Trim();
            }

            return null;
        }

        public static string Resolve(string inspectorValue) => Resolve(inspectorValue, out _);

        static string SafeEnvironment()
        {
            try
            {
                return Environment.GetEnvironmentVariable(EnvironmentVariable);
            }
            catch (Exception)
            {
                // Some platforms deny environment access; that is not an error here.
                return null;
            }
        }

        /// <summary>
        /// Reads a key file. On Android StreamingAssets lives inside the APK and is not a
        /// real path, so that read is expected to fail and simply falls through.
        /// </summary>
        static string ReadKeyFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var text = File.ReadAllText(path).Trim();
                return string.IsNullOrEmpty(text) ? null : text;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Masks a key for logs: "pf_live_abc…4f21".</summary>
        public static string Redact(string key)
        {
            if (string.IsNullOrEmpty(key)) return "(none)";
            if (key.Length <= 10) return new string('*', key.Length);
            return $"{key.Substring(0, 6)}…{key.Substring(key.Length - 4)}";
        }
    }
}
