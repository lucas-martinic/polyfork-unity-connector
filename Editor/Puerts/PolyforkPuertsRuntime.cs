using System;
using System.Collections.Generic;
using Puerts;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// Runs asset modules on QuickJS via Puerts.
    ///
    /// QuickJS is the backend that works on Quest: a small C engine with Unity Android
    /// ARM64 binaries that needs no JIT, so it survives IL2CPP's ahead-of-time compilation.
    /// Roughly 8.3 MB of native lands in the APK (6.3 QuickJS + 2.1 Puerts core), against
    /// about 15.5 MB for the V8 backend.
    ///
    /// Asset modules are ES modules importing 'three', but they are registered here as
    /// plain sources wrapped into a factory rather than loaded through the module system.
    /// QuickJS module resolution would need a loader per asset and gives nothing back:
    /// there is exactly one dependency, and it is the same bundle every time.
    /// </summary>
    public sealed class PolyforkPuertsRuntime : IPolyforkJsRuntime
    {
        /* Puerts 3.x marks JsEnv obsolete in favour of ScriptEnv, and this deliberately stays
         * on JsEnv. It is not a rename: its constructor checks the native papi version against
         * the one this managed code expects, and calls PuertsNative.SetLogCallback, which is
         * what puts a JS console.log and a JS exception into Unity's Console. Constructing a
         * ScriptEnv directly skips both, so the warning would be traded for silent JS errors
         * and a version mismatch that surfaces as a crash instead of a message.
         *
         * Suppressed here rather than project-wide, and only around the two lines that need it,
         * so the day this type actually goes away we get told. */
#pragma warning disable 618
        JsEnv _env;
#pragma warning restore 618

        Func<string, string, string> _bake;
        Func<string, string> _describe;
        Func<string, bool> _has;
        Action<string, string> _register;

        readonly HashSet<string> _modules = new();

        public bool IsReady => _env != null && _bake != null;

        public void Initialise(string threeBundle, string bridgeScript)
        {
            if (_env != null) return;

            if (string.IsNullOrEmpty(threeBundle))
                throw new ArgumentException("The trimmed three.js bundle is required.", nameof(threeBundle));
            if (string.IsNullOrEmpty(bridgeScript))
                throw new ArgumentException("The bake bridge is required.", nameof(bridgeScript));

            /* Each step is named so a failure says which one broke. Puerts reports a null
             * script as "String reference not set to an instance of a String. Parameter
             * name: s" - that is Encoding.UTF8.GetBytes(chunk) inside ScriptEnv.Eval - and
             * on its own that message identifies neither the script nor the step. */
            var step = "locating the JavaScript bootstrap";
            try
            {
                /* Our own loader, not Puerts's DefaultLoader: that one reads through
                 * Resources.Load, and the bootstrap is deliberately not in a Resources folder
                 * - see PolyforkPuertsLoader for why. Checked before the engine is built,
                 * because a loader that answers nothing fails inside native, and native
                 * reports it as a null string with no filename attached. */
                var loader = new PolyforkPuertsLoader();
                if (!loader.Verify(out var problem))
                    throw new InvalidOperationException(problem);

                step = "creating the QuickJS environment";
#pragma warning disable 618
                _env = new JsEnv(loader, -1, BackendType.QuickJS, IntPtr.Zero, IntPtr.Zero);
#pragma warning restore 618

                // QuickJS has no btoa; the bridge base64-encodes its buffers with it.
                step = "evaluating the base64 polyfill";
                _env.Eval(Base64Polyfill, "polyfork-base64.js");

                // The bundle is built as an IIFE assigning `var THREE`, so it needs no
                // rewriting - only promotion to globalThis, since `var` at eval scope is not
                // guaranteed to land there.
                step = $"evaluating three.js ({threeBundle.Length} chars)";
                _env.Eval(threeBundle, "three-trimmed.js");

                step = "promoting THREE to globalThis";
                _env.Eval("globalThis.THREE = THREE;", "three-global.js");

                step = $"evaluating the bake bridge ({bridgeScript.Length} chars)";
                _env.Eval(bridgeScript, "polyfork-bridge.js");

                step = "binding __polyfork.bake";
                _bake = _env.Eval<Func<string, string, string>>("__polyfork.bake");

                step = "binding __polyfork.describe";
                _describe = _env.Eval<Func<string, string>>("__polyfork.describe");

                step = "binding __polyfork.has";
                _has = _env.Eval<Func<string, bool>>("__polyfork.has");

                step = "binding __polyfork.__registerSource";
                _register = _env.Eval<Action<string, string>>(
                    "(function(id, src){ globalThis.__polyfork.__registerSource(id, src); })");
            }
            catch (Exception e)
            {
                // Named, and with the original attached rather than flattened to its message.
                throw new InvalidOperationException($"failed while {step}: {e.Message}", e);
            }

            Debug.Log("[Polyfork] QuickJS runtime ready.");
        }

        public void LoadModule(string moduleId, string source)
        {
            if (!IsReady) throw new InvalidOperationException("Initialise the runtime first.");
            if (string.IsNullOrEmpty(source)) return;

            _register(moduleId, PolyforkModuleTransform.ToScript(source));
            _modules.Add(moduleId);
        }

        public bool HasModule(string moduleId)
            => _modules.Contains(moduleId) && _has != null && _has(moduleId);

        public string Bake(string moduleId, string paramsJson)
        {
            if (!IsReady) throw new InvalidOperationException("Initialise the runtime first.");
            return _bake(moduleId, paramsJson ?? "{}");
        }

        public string Describe(string moduleId)
            => IsReady ? _describe(moduleId) : null;

        /// <summary>
        /// QuickJS ships no btoa, and the bridge needs one for its typed arrays.
        ///
        /// Written against a chunked array rather than by appending to a string. The obvious
        /// version does four `out +=` per three bytes, which for a mesh is tens of thousands
        /// of appends and is the kind of thing an interpreter without rope strings charges
        /// full price for. A mesh is the whole payload here, so this sits directly on how
        /// long a local bake takes.
        /// </summary>
        const string Base64Polyfill = @"
globalThis.__btoa = function (input) {
  var chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
  var len = input.length;
  var parts = [];
  var buf = new Array(4096);
  var n = 0;

  for (var i = 0; i < len; i += 3) {
    var c1 = input.charCodeAt(i) & 0xff;
    var has2 = i + 1 < len, has3 = i + 2 < len;
    var c2 = has2 ? input.charCodeAt(i + 1) & 0xff : 0;
    var c3 = has3 ? input.charCodeAt(i + 2) & 0xff : 0;

    buf[n++] = chars.charAt(c1 >> 2);
    buf[n++] = chars.charAt(((c1 & 3) << 4) | (c2 >> 4));
    buf[n++] = has2 ? chars.charAt(((c2 & 15) << 2) | (c3 >> 6)) : '=';
    buf[n++] = has3 ? chars.charAt(c3 & 63) : '=';

    // Flush a chunk at a time: one join of 4096 beats 4096 appends, and keeps the
    // intermediate array from growing to the size of the mesh.
    if (n === 4096) { parts.push(buf.join('')); n = 0; }
  }

  if (n > 0) parts.push(buf.slice(0, n).join(''));
  return parts.join('');
};";

        public void Dispose()
        {
            _bake = null;
            _describe = null;
            _has = null;
            _register = null;
            _modules.Clear();

            _env?.Dispose();
            _env = null;
        }

    }
}
