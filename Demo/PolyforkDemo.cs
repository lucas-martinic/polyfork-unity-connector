using UnityEngine;

namespace Polyfork.Demo
{
    /// <summary>
    /// The setup steps, written into the demo scene rather than into a file beside it.
    ///
    /// Polyfork has nothing to put in a scene until you have picked something: the catalogue
    /// is fetched at edit time and the models arrive when you import one, so a demo scene
    /// cannot ship pre-populated the way a prop pack's can. What it can do is stand where the
    /// work starts and say what to press, which is what the Asset Store guidance asks of an
    /// editor extension - "a demo scene showcasing the asset or showing setup steps in the
    /// scene".
    ///
    /// Selecting this object puts the steps in the Inspector, with a button that opens the
    /// gallery. Import a model from there and it lands in this scene, which is the demo.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class PolyforkDemo : MonoBehaviour
    {
        [TextArea(2, 4)]
        public string step1 = "Open the gallery: Polyfork > Browse Assets, or press the button below.";

        [TextArea(2, 4)]
        public string step2 = "Pick any model. Free ones need no account — no sign-in, no key.";

        [TextArea(2, 4)]
        public string step3 = "Press Remix to turn its knobs: sliders reshape it, colourways recolour it.";

        [TextArea(2, 4)]
        public string step4 = "Press Import. The model drops into this scene, and its knobs stay "
                              + "editable in the Inspector afterwards.";

        [Space]
        [TextArea(2, 5)]
        public string note = "Optional: Polyfork > Setup adds a JavaScript engine so models rebuild "
                             + "inside the editor instead of on the server — instant, and it spends "
                             + "no allowance. Editor-only; it never reaches a player build.";
    }
}
