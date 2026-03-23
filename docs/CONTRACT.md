# MR Blueprint — Pipeline Examples

This file shows how systems interact end-to-end.

---

## Example 1: Spring Creation

1. InputManager detects stylus drawing
2. StrokeRecorder collects StrokeData
3. GestureInterpreter classifies as:
   → GestureType.Line
4. InteractionResolver finds:
   → two nearby rigidbodies
5. PhysicsAuthoringSystem:
   → CreateSpring(targets, gesture)
6. SceneStateManager:
   → registers created spring + visuals

---

## Example 2: Impulse (Flick)

1. StrokeRecorder captures fast stroke
2. GestureInterpreter:
   → GestureType.Flick
3. InteractionResolver:
   → finds nearest rigidbody
4. PhysicsAuthoringSystem:
   → ApplyImpulse(targets, gesture)

---

## Example 3: Reset

1. User triggers reset
2. SceneStateManager:
   → destroys all runtime objects
   → clears constraints
   → clears visuals
3. System returns to initial state