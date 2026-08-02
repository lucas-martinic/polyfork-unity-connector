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

        public string[] Palette = Array.Empty<string>();

        public override string ToString() => $"{Title} [{Id}] {Triangles}tri kit={Kit}";

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
                a.Palette = pal.Select(t => (string)t).Where(s => s != null).ToArray();

            return a;
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
