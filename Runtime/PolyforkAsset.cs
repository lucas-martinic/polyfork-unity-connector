using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Polyfork
{
    /// <summary>One record from https://polyfork.dev/api/assets.</summary>
    public sealed class PolyforkAsset
    {
        public string Id;
        public string Title;
        public string Class;
        public string Kit;
        public int Triangles;
        public bool Free;
        public bool Remixable;
        public bool HasRig;
        public bool HasNight;
        public string Page;
        public string Thumbnail;
        public string PreviewGlb;
        public string Style;

        /// <summary>Real-world size in metres. Null when Polyfork has not published one.</summary>
        public float? SizeMeters;

        /// <summary>
        /// The asset's dominant colours, most-used first.
        ///
        /// This is a summary of what the model actually looks like, not the kit's full
        /// palette: a handful of weighted swatches rather than every colour in the range.
        /// </summary>
        public PolyforkSwatch[] Palette = Array.Empty<PolyforkSwatch>();

        public override string ToString() => $"{Title} [{Id}] {Triangles}tri kit={Kit}";

        /// <summary>
        /// Parses one asset record from raw catalogue JSON. Public so callers (and tests)
        /// can work with a stored payload without taking a dependency on the JSON library.
        /// </summary>
        public static PolyforkAsset FromJson(string json) =>
            string.IsNullOrWhiteSpace(json) ? null : Parse(JObject.Parse(json));

        internal static PolyforkAsset Parse(JObject o)
        {
            if (o == null) return null;
            var a = new PolyforkAsset
            {
                Id = (string)o["id"],
                Title = (string)o["title"],
                Class = (string)o["class"],
                Kit = (string)o["kit"],
                Triangles = o["triangles"]?.Type is JTokenType.Integer ? o["triangles"].Value<int>() : 0,
                Free = o["free"]?.Type == JTokenType.Boolean && o["free"].Value<bool>(),
                Remixable = o["remixable"]?.Type == JTokenType.Boolean && o["remixable"].Value<bool>(),
                HasRig = o["has_rig"]?.Type == JTokenType.Boolean && o["has_rig"].Value<bool>(),
                HasNight = o["has_night"]?.Type == JTokenType.Boolean && o["has_night"].Value<bool>(),
                Page = (string)o["page"],
                Thumbnail = (string)o["thumbnail"],
                PreviewGlb = (string)o["preview_glb"],
                Style = (string)o["style"]
            };

            var size = o["size_m"];
            if (size != null && size.Type is JTokenType.Float or JTokenType.Integer)
                a.SizeMeters = size.Value<float>();

            if (o["palette"] is JArray pal)
                a.Palette = pal.Select(PolyforkSwatch.Parse).Where(s => s != null).ToArray();

            return a;
        }
    }

    /// <summary>One entry of an asset's dominant-colour summary.</summary>
    public sealed class PolyforkSwatch
    {
        public string Hex;

        /// <summary>Roughly how much of the model wears this colour, 0..1.</summary>
        public float Share;

        public override string ToString() => $"{Hex} ({Share:P0})";

        /// <summary>
        /// Reads either the current object form ({"hex":"#479","share":0.75}) or the older
        /// plain-string form, so a client works against both shapes of the catalogue.
        /// </summary>
        internal static PolyforkSwatch Parse(JToken token)
        {
            switch (token?.Type)
            {
                case JTokenType.String:
                    return new PolyforkSwatch { Hex = token.Value<string>(), Share = 0f };

                case JTokenType.Object:
                    var hex = (string)token["hex"];
                    if (string.IsNullOrEmpty(hex)) return null;
                    var share = token["share"];
                    return new PolyforkSwatch
                    {
                        Hex = hex,
                        Share = share?.Type is JTokenType.Float or JTokenType.Integer
                            ? share.Value<float>()
                            : 0f
                    };

                default:
                    return null;
            }
        }
    }

    /// <summary>One record from https://polyfork.dev/api/kits.</summary>
    public sealed class PolyforkKit
    {
        public string Id;
        public string Title;
        public int Count;

        internal static PolyforkKit Parse(JObject o) => o == null
            ? null
            : new PolyforkKit
            {
                Id = (string)o["id"] ?? (string)o["slug"],
                Title = (string)o["title"] ?? (string)o["name"],
                Count = o["count"]?.Type is JTokenType.Integer ? o["count"].Value<int>() : 0
            };
    }

    public sealed class PolyforkPage
    {
        public int Total;
        public int Page;
        public int PerPage;
        public List<PolyforkAsset> Assets = new();

        public bool HasMore => Page * PerPage < Total;
    }
}
