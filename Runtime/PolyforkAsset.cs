using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

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

        /// <summary>
        /// The plan this asset belongs to: "free", "pro", ... Read this rather than a price.
        /// The catalogue retired price_usd and now returns null for every paid asset, with
        /// price_note explaining that paid assets are not sold separately.
        /// </summary>
        public string Plan;

        /// <summary>True when this connection has already licensed the asset.</summary>
        public bool Owned;

        /// <summary>
        /// Visible in the catalogue, but not this connection's to use: a paid asset nobody
        /// has bought here. The public preview GLB still loads - that is what makes the
        /// store browsable - but it is not a licence to ship the mesh.
        /// </summary>
        public bool Locked => !Free && !Owned;

        public bool Remixable;
        public bool HasRig;
        public bool HasNight;
        public string Page;
        public string Thumbnail;
        public string PreviewGlb;
        public string Style;

        /// <summary>
        /// Real-world footprint in metres. The catalogue publishes this as {x,y,z} on the
        /// detail endpoint and omits it from the list, so it is null while browsing.
        /// </summary>
        public Vector3? SizeMeters;

        /// <summary>
        /// Files this connection may fetch directly, or null when it may not.
        ///
        /// This is what decides whether an asset can be baked locally: the module is the
        /// program, so without it the only option is asking the server to rebuild a mesh.
        /// Free assets publish it to everyone; paid assets need a key.
        /// </summary>
        public PolyforkDownload Download;

        /// <summary>True when the asset's createAsset() module is fetchable by this caller.</summary>
        public bool HasModule => !string.IsNullOrEmpty(Download?.Mjs);

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
                Plan = (string)o["plan"],
                Owned = o["owned"]?.Type == JTokenType.Boolean && o["owned"].Value<bool>(),
                Remixable = o["remixable"]?.Type == JTokenType.Boolean && o["remixable"].Value<bool>(),
                HasRig = o["has_rig"]?.Type == JTokenType.Boolean && o["has_rig"].Value<bool>(),
                HasNight = o["has_night"]?.Type == JTokenType.Boolean && o["has_night"].Value<bool>(),
                Page = (string)o["page"],
                Thumbnail = (string)o["thumbnail"],
                PreviewGlb = (string)o["preview_glb"],
                Style = (string)o["style"]
            };

            a.SizeMeters = ParseSize(o["size_m"]);
            a.Download = PolyforkDownload.Parse(o["download"]);

            if (o["palette"] is JArray pal)
                a.Palette = pal.Select(PolyforkSwatch.Parse).Where(s => s != null).ToArray();

            return a;
        }

        /// <summary>
        /// Reads size_m, which is an {x,y,z} object. An older shape used a single number,
        /// so a scalar is treated as a uniform extent rather than dropped.
        /// </summary>
        static Vector3? ParseSize(JToken token)
        {
            switch (token?.Type)
            {
                case JTokenType.Object:
                    var o = (JObject)token;
                    return new Vector3(
                        o["x"]?.Value<float>() ?? 0f,
                        o["y"]?.Value<float>() ?? 0f,
                        o["z"]?.Value<float>() ?? 0f);

                case JTokenType.Float:
                case JTokenType.Integer:
                    var v = token.Value<float>();
                    return new Vector3(v, v, v);

                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Direct file URLs for an asset, present only when this connection is allowed them.
    /// </summary>
    public sealed class PolyforkDownload
    {
        public string Glb;

        /// <summary>The createAsset() program. Its presence is what enables local baking.</summary>
        public string Mjs;

        /// <summary>"none" when no key is needed; otherwise what the caller must present.</summary>
        public string Auth;

        internal static PolyforkDownload Parse(JToken token)
        {
            if (token is not JObject o) return null;

            var d = new PolyforkDownload
            {
                Glb = (string)o["glb"],
                Mjs = (string)o["mjs"],
                Auth = (string)o["auth"]
            };
            return d.Glb == null && d.Mjs == null ? null : d;
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
