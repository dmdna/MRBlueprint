# MR Blueprint — Architecture

## 1. Overview

MR Blueprint is a Unity-based mixed reality application for Meta Quest 3 that allows users to author physics behaviors in real space using the Logitech MX Ink stylus.

Users:
- Place rigidbody objects in their environment
- Draw gestures in 3D space
- Convert gestures into physics behaviors (spring, impulse, etc.)

---

## 2. Core Pipeline

InputManager  
→ StrokeRecorder  
→ GestureInterpreter  
→ InteractionResolver  
→ PhysicsAuthoringSystem  
→ Visualization / SceneStateManager

---

## 3. Canonical Systems

These systems are the **only allowed top-level managers**.

### InputManager
- Reads stylus pose, pressure, and contact state
- Only source of input data

### StrokeRecorder
- Converts input into StrokeData
- Handles sampling, smoothing, and storage

### GestureInterpreter
- Converts StrokeData → GestureResult
- Does NOT interact with physics or scene objects

### InteractionResolver
- Maps GestureResult → TargetResolutionResult
- Determines which objects are affected

### PhysicsAuthoringSystem
- Creates physics behaviors (spring, impulse, etc.)
- Only system allowed to modify physics relationships

### ObjectPlacementManager
- Handles spawning and tracking of objects
- Maintains registry of active objects

### SceneStateManager
- Tracks all runtime-created objects and behaviors
- Responsible for reset / cleanup

---

## 4. Shared Data Models (LOCKED)

These models must be reused across all systems.

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