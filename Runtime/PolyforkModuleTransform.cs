using System;
using System.Collections.Generic;
using System.Text;

namespace Polyfork
{
    /// <summary>
    /// Rewrites a Polyfork asset module from ES module syntax into a plain script.
    ///
    /// Asset modules are ESM, but running them through a JS engine's module system would
    /// mean a resolver per asset for no benefit: there is exactly one dependency and it is
    /// the same bundle every time. Rewriting instead lets each module be evaluated inside a
    /// function scope with THREE injected, which also keeps assets from colliding.
    ///
    /// Deliberately narrow rather than a general transpiler. It handles the forms the
    /// catalogue actually publishes, and says so loudly when it meets anything else.
    /// </summary>
    public static class PolyforkModuleTransform
    {
        /// <summary>
        /// Produces script that builds an `__exports` object. Imports are dropped, since the
        /// only one is three, which the host supplies.
        /// </summary>
        public static string ToScript(string source)
        {
            if (string.IsNullOrEmpty(source)) return "var __exports = {};";

            var sb = new StringBuilder(source.Length + 256);
            sb.AppendLine("var __exports = {};");

            // Exported names are assigned after the whole body, never inline. A declaration
            // such as `export const params = {` spans many lines, so an assignment injected
            // straight after the opening line lands inside the object literal and the module
            // fails to parse.
            var exported = new List<string>();

            foreach (var raw in source.Split('\n'))
            {
                var line = raw;
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("import ") || trimmed.StartsWith("import("))
                {
                    // three is provided by the host. Anything else would be a dependency the
                    // catalogue does not use, so leave a marker rather than failing silently.
                    if (!trimmed.Contains("three"))
                        sb.AppendLine("/* polyfork: dropped unsupported import */");
                    continue;
                }

                if (trimmed.StartsWith("export default "))
                {
                    line = line.Replace("export default ", "__exports.default = ");
                }
                else if (trimmed.StartsWith("export {"))
                {
                    line = ExpandExportList(trimmed);
                }
                else if (trimmed.StartsWith("export "))
                {
                    var rest = trimmed.Substring("export ".Length);
                    var name = DeclaredName(rest);
                    line = rest;
                    if (name != null) exported.Add(name);
                }

                sb.AppendLine(line);
            }

            foreach (var name in exported) sb.AppendLine($"__exports.{name} = {name};");

            return sb.ToString();
        }

        /// <summary>Turns `export { A, B as C };` into assignments onto __exports.</summary>
        static string ExpandExportList(string line)
        {
            var open = line.IndexOf('{');
            var close = line.IndexOf('}');
            if (open < 0 || close < open) return "";

            var sb = new StringBuilder();
            foreach (var raw in line.Substring(open + 1, close - open - 1).Split(','))
            {
                var entry = raw.Trim();
                if (entry.Length == 0) continue;

                var parts = entry.Split(new[] { " as " }, StringSplitOptions.RemoveEmptyEntries);
                var local = parts[0].Trim();
                var exportedName = parts.Length > 1 ? parts[1].Trim() : local;

                if (local.Length > 0 && exportedName.Length > 0)
                    sb.AppendLine($"__exports.{exportedName} = {local};");
            }
            return sb.ToString();
        }

        /// <summary>Name declared by `const x =`, `function f(`, `class C`, or `let`/`var`.</summary>
        static string DeclaredName(string declaration)
        {
            var d = declaration.TrimStart();

            // Longest first: "async function " also starts with neither const nor function.
            foreach (var keyword in new[] { "async function ", "function ", "const ", "let ", "var ", "class " })
            {
                if (!d.StartsWith(keyword)) continue;

                var after = d.Substring(keyword.Length).TrimStart();
                var end = after.IndexOfAny(new[] { ' ', '=', '(', '{', ';', '\r', '\n', ':' });
                var name = end < 0 ? after : after.Substring(0, end);
                return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
            return null;
        }
    }
}
