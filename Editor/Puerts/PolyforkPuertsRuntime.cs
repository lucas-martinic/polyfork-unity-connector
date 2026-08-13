#if POLYFORK_PUERTS
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
        JsEnv _env;
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

            // DefaultLoader, not a null one: Puerts resolves its own bootstrap scripts
            // through the loader while constructing JsEnv, so a loader that answers nothing
            // fails there before any of our code runs. We never ask it for anything
            // ourselves - every script the connector runs is evaluated from a string.
            _env = new JsEnv(new DefaultLoader(), -1, BackendType.QuickJS, IntPtr.Zero, IntPtr.Zero);

            // IL2CPP generates delegate wrappers ahead of time, so every signature crossing
            // the boundary has to be declared before it is used or the call fails on device.
            _env.UsingFunc<string, string, string>();
            _env.UsingFunc<string, string>();
            _env.UsingFunc<string, bool>();
            _env.UsingAction<string, string>();

            // QuickJS has no btoa; the bridge base64-encodes its buffers with it.
            _env.Eval(Base64Polyfill, "polyfork-base64.js");

            // The bundle is built as an IIFE assigning `var THREE`, so it needs no rewriting -
            // only promotion to globalThis, since `var` at eval scope is not guaranteed to
            // land there. Bundling as ESM instead would have meant parsing export-list syntax.
            _env.Eval(threeBundle, "three-trimmed.js");
            _env.Eval("globalThis.THREE = THREE;", "three-global.js");

            _env.Eval(bridgeScript, "polyfork-bridge.js");

            _bake = _env.Eval<Func<string, string, string>>("__polyfork.bake");
            _describe = _env.Eval<Func<string, string>>("__polyfork.describe");
            _has = _env.Eval<Func<string, bool>>("__polyfork.has");
            _register = _env.Eval<Action<string, string>>(
                "(function(id, src){ globalThis.__polyfork.__registerSource(id, src); })");

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

        /// <summary>QuickJS ships no btoa, and the bridge needs one for its typed arrays.</summary>
        const string Base64Polyfill = @"
globalThis.__btoa = function (input) {
  var chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
  var out = '', i = 0;
  while (i < input.length) {
    var c1 = input.charCodeAt(i++) & 0xff;
    var c2 = input.charCodeAt(i++) & 0xff;
    var c3 = input.charCodeAt(i++) & 0xff;
    out += chars.charAt(c1 >> 2);
    out += chars.charAt(((c1 & 3) << 4) | (c2 >> 4));
    out += isNaN(c2) ? '=' : chars.charAt(((c2 & 15) << 2) | (c3 >> 6));
    out += isNaN(c3) ? '=' : chars.charAt(c3 & 63);
  }
  return out;
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
#endif
