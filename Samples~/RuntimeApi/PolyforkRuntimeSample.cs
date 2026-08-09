using System.Threading;
using UnityEngine;

namespace Polyfork.Samples
{
    /// <summary>
    /// The whole runtime API in one file: wait for the catalogue, spawn an asset, drive a
    /// knob from script.
    ///
    /// Use this path when you want Polyfork assets in a running game - streamed, remixed
    /// while the player watches. If instead you want them as project assets, don't use any
    /// of this: open Window > Polyfork > Browse Assets and import a .glb.
    ///
    /// Drop this on an empty GameObject alongside a PolyforkCatalog and press Play.
    /// </summary>
    [AddComponentMenu("Polyfork/Samples/Polyfork Runtime Sample")]
    [RequireComponent(typeof(PolyforkCatalog))]
    public sealed class PolyforkRuntimeSample : MonoBehaviour
    {
        [Header("Placement")]
        [Tooltip("Where the spawned asset goes. Defaults to this object.")]
        [SerializeField] Transform spawnParent;

        [Tooltip("Metres. Catalogue assets are authored at real-world scale, so a chair really " +
                 "is chair-sized; this scales it to something visible next to the camera.")]
        [SerializeField] float displaySize = 0.5f;

        [Header("Remix")]
        [Tooltip("Cycle the first range knob back and forth, to show geometry rebuilding live.")]
        [SerializeField] bool animateFirstKnob = true;

        [Tooltip("Seconds between knob changes. Each one is a network round trip unless local " +
                 "baking is on, and the endpoint is quota-limited - don't make this tiny.")]
        [SerializeField] float knobInterval = 2.5f;

        PolyforkCatalog _catalog;
        PolyforkRemixable _spawned;
        CancellationTokenSource _cts;

        float _nextKnobAt;
        bool _knobRising = true;

        void Awake()
        {
            _cts = new CancellationTokenSource();
            _catalog = GetComponent<PolyforkCatalog>();
            if (spawnParent == null) spawnParent = transform;
        }

        void OnEnable()
        {
            _catalog.Loaded += OnCatalogueLoaded;

            // Loaded is a plain event, so it does not replay for anything that subscribed
            // late. Being on the same GameObject means OnEnable beats the catalogue's Start
            // in practice, but that is an ordering accident and not worth depending on.
            if (_catalog.Ready) OnCatalogueLoaded();
        }

        void OnDisable() => _catalog.Loaded -= OnCatalogueLoaded;

        void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        /// <summary>The catalogue loads itself on Start; this just waits for it.</summary>
        async void OnCatalogueLoaded()
        {
            if (_spawned != null) return;   // guard against a second call from the Ready check

            if (_catalog.Assets.Count == 0)
            {
                Debug.LogWarning("[Polyfork] catalogue loaded but no assets passed the filters. " +
                                 "Relax remixableOnly or maxTriangles on the catalog component.");
                return;
            }

            var asset = _catalog.Next();
            Debug.Log($"[Polyfork] spawning {asset.Title} ({asset.Triangles} tri)");

            _spawned = await PolyforkSpawner.SpawnAsync(_catalog, asset, spawnParent, _cts.Token);
            if (_spawned == null) return;

            // Assets arrive at real-world metres with the origin on the ground. Scaling is a
            // presentation choice, so the connector never does it for you.
            PolyforkSpawner.FitToSize(_spawned.gameObject, displaySize);

            _spawned.ModelChanged += _ => Debug.Log("[Polyfork] geometry rebuilt");

            Debug.Log($"[Polyfork] {_spawned.RemixableKnobs.Count} remixable knob(s): " +
                      string.Join(", ", System.Linq.Enumerable.Select(_spawned.RemixableKnobs, k => k.Name)));
        }

        void Update()
        {
            if (!animateFirstKnob || _spawned == null || _spawned.IsBusy) return;
            if (_spawned.RemixableKnobs.Count == 0) return;
            if (Time.time < _nextKnobAt) return;

            _nextKnobAt = Time.time + knobInterval;

            // The first knob is whatever the asset's own schema lists first - this sample
            // does not assume a name, because every asset publishes a different set.
            var knob = _spawned.RemixableKnobs[0];
            if (knob.Type != PolyforkKnobType.Range || !knob.HasRange) return;

            var target = _knobRising ? knob.Max : knob.Min;
            _knobRising = !_knobRising;

            _spawned.SetRange(knob.Name, target);
        }
    }
}
