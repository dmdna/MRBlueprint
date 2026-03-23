# MR Blueprint — AI Build Contract

## 1. Project Summary

MR Blueprint is a Unity (C#) mixed reality application for Meta Quest 3 using passthrough and Logitech MX Ink stylus input.

Users:
- place rigidbody objects in their real environment
- draw gestures in 3D space
- create physics behaviors (spring, impulse)

---

## 2. Core System Pipeline (MANDATORY)

All features MUST follow this pipeline:

InputManager  
→ StrokeRecorder  
→ GestureInterpreter  
→ InteractionResolver  
→ PhysicsAuthoringSystem  
→ SceneStateManager

No system may bypass this pipeline.

---

## 3. Canonical Systems (DO NOT DUPLICATE)

These are the ONLY allowed top-level systems.

### InputManager
- Provides stylus position, rotation, pressure, draw state
- ONLY source of input data

### StrokeRecorder
- Converts input into StrokeData
- Handles sampling, smoothing

### GestureInterpreter
- Converts StrokeData → GestureResult
- PURE LOGIC (no Unity scene access, no physics)

### InteractionResolver
- Converts GestureResult → TargetResolutionResult
- Finds relevant rigidbodies

### PhysicsAuthoringSystem
- Creates physics behaviors:
  - springs
  - impulses
- ONLY system allowed to modify physics

### ObjectPlacementManager
- Spawns and tracks rigidbody objects

### SceneStateManager
- Tracks all runtime-created objects
- Handles reset / cleanup

---

## 4. Shared Data Models (LOCKED — DO NOT MODIFY)

All systems MUST use these exact definitions.

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
5. Required API Contracts (DO NOT RENAME)
InputManager
Vector3 GetStylusPosition()
Quaternion GetStylusRotation()
float GetPressure()
bool IsDrawing()
StrokeRecorder
void BeginStroke()
void UpdateStroke()
StrokeData EndStroke()
GestureInterpreter
GestureResult Classify(StrokeData stroke)
InteractionResolver
TargetResolutionResult Resolve(GestureResult gesture)
PhysicsAuthoringSystem
void CreateSpring(TargetResolutionResult targets, GestureResult gesture)
void ApplyImpulse(TargetResolutionResult targets, GestureResult gesture)
ObjectPlacementManager
Rigidbody SpawnObject(Vector3 position)
List<Rigidbody> GetAllObjects()
SceneStateManager
void RegisterObject(GameObject obj)
void ResetScene()
6. System Invariants (STRICT)

The following MUST always be true:

InputManager is the ONLY source of input
GestureInterpreter MUST NOT:
access Unity scene
access rigidbodies
modify objects
PhysicsAuthoringSystem is the ONLY system that:
creates constraints
applies forces
All runtime-created objects MUST:
be registered with SceneStateManager
be removable on reset
Systems MUST communicate ONLY through shared data models
NO duplicate managers or alternate pipelines
NO direct coupling between non-adjacent systems
7. Allowed Data Flow

Valid:
Input → Stroke → Gesture → Target → Physics

Invalid:

Input → Physics ❌
Gesture → Rigidbody ❌
Stroke → ObjectPlacement ❌
8. Feature Requirements (MVP)

The system MUST support:

Required
Object placement (rigidbodies)
Stroke drawing in 3D
Gesture: Line → Spring
Gesture: Flick → Impulse
Visual feedback (lines/arrows)
Reset system
Optional
Boundary walls
Hinge constraints
9. Parameter Mapping Rules

Use these mappings:

pressure → spring stiffness
stroke length → rest length
stroke direction → impulse direction
stroke velocity → impulse magnitude

All values MUST:

be normalized
be clamped to safe ranges
10. Reset / Cleanup Contract (MANDATORY)

All runtime-created elements MUST be tracked and removable:

Includes:

springs / constraints
line renderers
temporary visuals
colliders

SceneStateManager must fully restore initial state.

11. Code Generation Rules (FOR AI)

When generating code:

MUST
Use existing systems and APIs
Reuse shared data models exactly
Integrate into the pipeline (no shortcuts)
Support reset/cleanup
Keep public APIs minimal
Use Unity-compatible C#
MUST NOT
Create new managers unless explicitly requested
Duplicate functionality of existing systems
Access Unity scene from GestureInterpreter
Modify physics outside PhysicsAuthoringSystem
12. Required Output Format (FOR AI TASKS)

Every generated feature MUST include:

Assumptions
Dependencies on existing systems
New public methods (if any)
Integration location (which GameObject / system)
Step-by-step integration instructions
Code
Inspector setup instructions
Manual test plan (Quest device)
13. Definition of Done

A feature is complete when:

It follows the pipeline
It uses shared data models
It integrates with existing systems
It supports reset/cleanup
It works on-device (Quest 3)
It produces clear visual feedback
14. Example Flow — Spring
InputManager → stylus drawing
StrokeRecorder → StrokeData
GestureInterpreter → GestureType.Line
InteractionResolver → 2 rigidbodies
PhysicsAuthoringSystem → CreateSpring(...)
SceneStateManager → register created objects
15. Example Flow — Impulse
StrokeRecorder → fast stroke
GestureInterpreter → GestureType.Flick
InteractionResolver → nearest rigidbody
PhysicsAuthoringSystem → ApplyImpulse(...)

---

# How to use this (important)

For every AI prompt:

- paste **Sections 2, 3, 4, 5, 6**
- then add your task

---

# Why this works

This document:
- eliminates architectural drift
- prevents duplicate systems
- locks interfaces
- enforces clean pipelines
- ensures all generated code integrates correctly