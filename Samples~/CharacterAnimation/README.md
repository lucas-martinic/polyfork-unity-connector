# Character Animation

A dropdown of animations on a Polyfork character, starting on idle.

## Why the clips are not in the character

Polyfork characters ship a **rig and no animation**, which is deliberate rather than
missing. Each one carries a Mixamo skeleton with the prefix stripped — `Hips`, `Spine`,
`Spine1`, `Neck`, `Head`, `LeftArm` — so any humanoid clip retargets onto it. Baking one
opinionated walk into every character would be the store deciding how your game moves, and
you would throw it away.

The catalogue also publishes the joints it expects you to drive, per asset, as
`rigged_parts`: each part with an axis and a range, e.g. `LeftArm` rotating on `z` from 0 to
−55 degrees. That is for posing. This sample is the other half — playing clips.

## Setup

1. **Import a character.** Any asset with `has_rig`. Select the imported model, and in
   **Rig ▸ Animation Type** choose **Humanoid**.
2. **Get a clip pack.** polyfork.dev publishes two:

   | Pack | Clips |
   | --- | --- |
   | [`/anim/xbot.glb`](https://polyfork.dev/anim/xbot.glb) | idle, walk, run, agree, headShake, sad_pose, sneak_pose |
   | [`/anim/soldier.glb`](https://polyfork.dev/anim/soldier.glb) | Idle, Walk, Run, TPose |

   Drop it into `Assets`, and set **its** Rig to **Humanoid** as well.
3. **Add the component.** Put `PolyforkCharacterAnimation` on the character, drag the clips
   into its list, and press play.

> **Both rigs must be Humanoid.** The packs use `mixamorig:` bone names and the characters
> do not, so the paths do not line up and a clip cannot bind directly. Humanoid retargets
> through the avatar, which is what makes any clip work on any of these characters
> regardless of naming. Set only one of them and you get a character standing still with no
> error to explain it.

## Using it

The Inspector shows a dropdown of clip names. Changing it in play mode switches animation,
blending over `blend` seconds.

From script:

```csharp
var anim = character.GetComponent<PolyforkCharacterAnimation>();
anim.Play("walk");      // by name, case-insensitive
anim.Play(2);           // or by index
```

**Idle is chosen by name, not by position.** The two packs disagree on capitalisation —
`idle` in xbot, `Idle` in soldier — and on ordering, so anything positional would start a
different animation depending on which pack you dragged in.

## How it plays them

Through a `PlayableGraph` rather than an `AnimatorController`. A sample should not require
you to author a controller asset, wire up states and add parameters before anything moves;
a graph plays an arbitrary clip in three lines. The mixer has exactly two inputs, because a
blend is only ever between the clip leaving and the clip arriving — more than that is a
state machine, which is the thing this avoids needing.

For a real game, an `AnimatorController` is still the right tool. This is the shortest path
from an imported character to something moving.
