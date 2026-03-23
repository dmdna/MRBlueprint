# MR Blueprint — Project Contract

## 1. Project Overview

MR Blueprint is a Unity-based mixed reality application for Meta Quest 3 that allows users to author physics behaviors in real space using the Logitech MX Ink stylus.

Core interaction:
- Place rigidbody objects in the real environment
- Draw gestures in 3D space
- Convert gestures into physics behaviors (spring, impulse)

---

## 2. Core Pipeline

All features must follow this pipeline:

InputManager  
→ StrokeRecorder  
→ GestureInterpreter  
→ InteractionResolver  
→ PhysicsAuthoringSystem  
→ SceneStateManager + Visualization

No system may bypass this pipeline.

---

## 3. Canonical Systems (DO NOT DUPLICATE)

These are the only top-level systems allowed.

### InputManager
Responsibilities:
- Read stylus pose, pressure, and contact state
- Provide normalized input to other systems

Rules:
- Only source of stylus input
- No physics or scene logic

---

### StrokeRecorder
Responsibilities:
- Convert raw input into StrokeData
- Handle sampling, smoothing, and storage

Rules:
- Does not interpret gestures
- Does not interact with scene objects

---

### GestureInterpreter
Responsibilities:
- Convert StrokeData → GestureResult

Rules:
- Pure interpretation layer
- Must not modify scene or physics
- Must not query Unity scene directly

---

### InteractionResolver
Responsibilities:
- Map GestureResult → TargetResolutionResult
- Identify relevant objects in the scene

Rules:
- Only resolves targets
- Does not apply physics

---

### PhysicsAuthoringSystem
Responsibilities:
- Create physics behaviors:
  - springs
  - impulses
  - (optional) walls / hinges

Rules:
- Only system allowed to modify physics relationships
- Must not read raw input directly
- Must use GestureResult + TargetResolutionResult

---

### ObjectPlacementManager
Responsibilities:
- Spawn and manage rigidbody objects
- Maintain registry of active objects

Rules:
- All interactable objects must be registered here

---

### SceneStateManager
Responsibilities:
- Track all runtime-created objects and behaviors
- Handle reset / cleanup

Rules:
- Every runtime-created object must be registered
- Reset must return scene to initial state

---

## 4. Shared Data Models (LOCKED)

These models must be reused exactly. Do not redefine them.

```csharp
public enum GestureType
{
    Unknown,
    Line,
    Flick,
    Boundary
}

public struct StrokePoint
{
    public Vector3 Position;
    public float Pressure;
    public float Timestamp;
}

public class StrokeData
{
    public List<StrokePoint> Points;
    public float Duration;
    public float AveragePressure;
}

public class GestureResult
{
    public GestureType Type;
    public StrokeData Stroke;
    public Vector3 Direction;
    public float Confidence;
}

public class TargetResolutionResult
{
    public Rigidbody PrimaryObject;
    public Rigidbody SecondaryObject;
    public Vector3 HitPoint;
}