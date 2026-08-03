using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Polyfork
{
    /// <summary>Creates ready-to-use Polyfork instances: GLB + knob schema + remix state.</summary>
    public static class PolyforkSpawner
    {
        /// <summary>
        /// Loads an asset and returns a GameObject carrying a configured
        /// <see cref="PolyforkRemixable"/>. The GLB sits as a child so the outer object
        /// can own physics and interaction without being replaced on every rebuild.
        /// </summary>
        public static async Task<PolyforkRemixable> SpawnAsync(
            PolyforkCatalog catalog,
            PolyforkAsset asset,
            Transform parent = null,
            CancellationToken ct = default)
        {
            if (catalog == null || asset == null) return null;

            var url = catalog.BaseGlbUrl(asset);
            if (string.IsNullOrEmpty(url)) return null;

            var container = new GameObject($"Polyfork_{asset.Id}");
            if (parent != null) container.transform.SetParent(parent, false);

            try
            {
                var model = await catalog.Loader.LoadAsync(url, container.transform, ct);
                var schema = asset.Remixable ? await catalog.GetParamsAsync(asset.Id, ct) : null;

                var remixable = container.AddComponent<PolyforkRemixable>();
                remixable.Initialise(catalog, asset, schema, model);
                return remixable;
            }
            catch
            {
                if (container != null) Object.Destroy(container);
                throw;
            }
        }

        /// <summary>
        /// The uniform scale that would bring an instance's largest dimension to
        /// <paramref name="targetSize"/> metres, without applying it.
        ///
        /// Polyfork publishes at true real-world metres, so a building is four metres tall
        /// and a doorknob a few centimetres. Anything that animates localScale per frame
        /// needs this as a factor rather than setting scale outright, or the asset snaps
        /// back to life size - which, centred on a palm, looks like nothing rendered at all.
        /// </summary>
        public static float MeasureFitScale(GameObject instance, float targetSize)
        {
            var bounds = CalculateBounds(instance);
            var largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest <= Mathf.Epsilon) return 1f;

            // CalculateBounds is world-space, so divide out any scale already applied.
            var lossy = instance.transform.lossyScale;
            var applied = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z));
            if (applied <= Mathf.Epsilon) return 1f;

            return targetSize / (largest / applied);
        }

        /// <summary>
        /// Moves children so the instance is centred on its own transform.
        ///
        /// Assets are authored with the origin on the ground (minY = 0), which is right for
        /// placing something on a floor and wrong for orbiting a point: uncentred, an asset
        /// hangs below the ring by half its height.
        /// </summary>
        public static void CentreOnPivot(GameObject instance)
        {
            var bounds = CalculateBounds(instance);
            if (bounds.size == Vector3.zero) return;

            var offset = instance.transform.position - bounds.center;
            foreach (Transform child in instance.transform) child.position += offset;
        }

        /// <summary>
        /// Uniformly scales an instance so its largest dimension is
        /// <paramref name="targetSize"/> metres, and recentres it on its own bounds.
        ///
        /// Only safe when nothing else writes localScale afterwards; use
        /// <see cref="MeasureFitScale"/> when the scale is animated.
        /// </summary>
        public static Bounds FitToSize(GameObject instance, float targetSize, bool centre = true)
        {
            var bounds = CalculateBounds(instance);
            if (bounds.size == Vector3.zero) return bounds;

            var largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest <= Mathf.Epsilon) return bounds;

            var scale = targetSize / largest;
            instance.transform.localScale *= scale;

            if (centre)
            {
                // Recompute after scaling so the offset is in the new space.
                var scaled = CalculateBounds(instance);
                var offset = instance.transform.position - scaled.center;
                foreach (Transform child in instance.transform) child.position += offset;
            }

            return CalculateBounds(instance);
        }

        /// <summary>World-space bounds of every renderer under an object.</summary>
        public static Bounds CalculateBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return new Bounds(instance.transform.position, Vector3.zero);

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
