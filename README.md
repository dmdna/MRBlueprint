
# MR Blueprint
<img width="1000" height="667" alt="MRblueprint Thumbnail (5)" src="https://github.com/user-attachments/assets/32f7f9b8-0354-4fdc-ad5c-2480136eb992" />
<p align="center">
  <strong>MR Blueprint: Draw Physics into Reality</strong>
</p>

<div align="center">
  
[![Stars](https://img.shields.io/github/stars/dmdna/MRBlueprint)](https://github.com/dmdna/MRBlueprint/stargazers)
[![Issues](https://img.shields.io/github/issues/dmdna/MRBlueprint)](https://github.com/dmdna/MRBlueprint/issues)
[![License](https://img.shields.io/github/license/dmdna/MRBlueprint)](https://github.com/dmdna/MRBlueprint/blob/main/LICENSE)

</div>

<p align="center">
  <a href="#demo-video">Demo Video</a> •
  <a href="#controls">Controls</a> •
  <a href="#features">Features</a> •
  <a href="#how-it-works">Additional Links</a>
</p>


[Devstudio 2026 by Logitech Hackathon](https://devpost.com/software/mr-blueprint-draw-physics-into-reality-g1r50h?ref_content=my-projects-tab&ref_feature=my_projects) Semifinalist submission. 

<table width="100%">
  <tr>
    <td width="33.33%" align="center">
      <img src="Recordings/controller_detection.gif" alt="gif 1" width="100%">
      <img width="300" height="200" alt="hand controller gif" src="https://github.com/user-attachments/assets/1d69c287-3715-4fab-9cb2-18ef6de77474" />
      <sub><b>Hand + Controller + Stylus Detection</b></sub>
    </td>
    <td width="33.33%" align="center">
      <img src="Recordings/space_drawing.gif" alt="gif 2" width="100%">
      <img width="300" height="200" alt="SpatialDrawing" src="https://github.com/user-attachments/assets/570e8513-0245-4d98-a092-0b18e9e61e2e" />
      <sub><b>Trigger Pressure Sensitive Spatial Drawing</b></sub>
    </td>
    <td width="33.33%" align="center">
      <img src="Recordings/surface_drawing.gif" alt="gif 3" width="100%">
      <img width="300" height="200" alt="PressureSensitiveDrawing" src="https://github.com/user-attachments/assets/dd049c5a-970d-462c-874c-3f308b2fe149" />
      <sub><b>Tip Pressure Sensitive Surface Drawing</b></sub>
    </td>
  </tr>
</table>


---
## Overview
MR Blueprint is a mixed reality physics-authoring sandbox for Meta Quest. Users enter a world-space XR environment, spawn 3D objects, arrange them in space, adjust properties like mass, friction, restitution, scale, gravity, rotation, and color, and then run simulations to see what happens. With MX Ink, users can switch between edit and draw workflows, create custom scene elements, and interact in a way that feels natural inside the headset. The experience includes a home flow, toolbar, content drawer, inspector, transform gizmo, simulation controls, help overlay, audio feedback, MX Ink connection status indicator, and live visual analysis tools such as vectors, motion paths, and real-time graphing. Rather than just showing motion, MR Blueprint helps users understand motion.

## Demo Video
<div align="center">
  
[![Demo Video](https://img.youtube.com/vi/ggg8-Duyzn4/0.jpg)](https://www.youtube.com/watch?v=ggg8-Duyzn4)

</div>


---
## Requirements

### Hardware
- Meta Quest 3 / 3S
- Logitech MX Ink Stylus
- Windows PC
- USB-C Cable

### Software
- Unity 6000.3.11f1
- Android Build Support
- OpenXR
- XR Interaction Toolkit
- Meta XR SDK
- Logitech MX Ink SDK


## Setup

1. Enable Developer Mode on your Meta Quest headset  
2. Connect headset via USB-C  
3. Allow USB debugging when prompted  
4. Open the project in Unity  
5. Install required XR / Meta / Logitech packages  
6. Switch Build Target to Android  
7. Pair Logitech MX Ink stylus  
8. Build & Run to Meta Quest  
9. Enable passthrough permissions if prompted  
---
## Control Schema
<img width="1800" height="1200" alt="control" src="https://github.com/user-attachments/assets/b11c373a-aa79-4907-8703-59d73b048957" />


## Controls

### Left Controller

| Input | Action | Description |
|---|---|---|
| Joystick Up / Down | Drag | Move selected object or adjust values |
| Joystick Click | Reset Orientation | Reset selected object's rotation |
| Trigger | Select | Select object / interact with UI |
| Grip | Grab | Grab and move objects |
| X Button | Toolbar | Open / close toolbar |
| Y Button | Simulate | Enter simulation mode |
| Menu Button | Options | Open options menu |

### Logitech MX Ink Stylus

| Input | Action | Description |
|---|---|---|
| Tip Press | Draw | Draw on surfaces |
| Side Button | Select | Select objects / interact in draw mode |
| Side Button Press | Undo | Undo last stroke while in draw mode |
| Side Button Hold | Clear | Clear all strokes while in draw mode |
| Grip / Hold | Grab | Grab and move objects |
| Trigger Pressure | Spatial Draw | Pressure-sensitive 3D drawing in space |
---
## Features

### Room Space
support for ar and vr and room randomization
  
<table width="100%">
  <tr>
    <td width="33.33%" align="center">
      <img width="300" height="200" alt="AR_Demo" src="https://github.com/user-attachments/assets/0717bead-2d1a-43c4-9362-0979f48058e6" />
      <sub><b>AR Room</b></sub>
    </td>
    <td width="33.33%" align="center">
      <img width="300" height="200" alt="VR_Demo" src="https://github.com/user-attachments/assets/3fe37514-6e76-4df5-a2b4-6669184decd4" />
      <sub><b>VR Room</b></sub>
    </td>
    <td width="33.33%" align="center">
      <img width="300" height="200" alt="random_room" src="https://github.com/user-attachments/assets/2dfcb279-fa15-4c4a-916d-55562addf4ca" />
      <sub><b>Random Room</b></sub>
    </td>
  </tr>
</table>  

### Edit Mode

Edit Mode allows users to place, select, move, rotate, scale, and configure objects before simulation.

#### Includes:
- Spawn objects from content drawer
- Move / rotate / scale objects
- Transform gizmo controls
- Object selection highlights
- Inspector tools

#### Spawnable Objects:
- Cube
- Sphere
- Pyramid
- Cone
- Cylinder
- Torus
- Hemisphere
- Triangular Prism
- Octahedron
- Hexagonal Prism
- Buckyball
- Pen Tool / Custom Drawing Tool

#### Editable Properties:
- Mass
- Scale
- Friction
- Restitution
- Gravity Toggle
- Rotation
- Color
- Delete

<img width="800" height="450" alt="editmode" src="https://github.com/user-attachments/assets/7b345c82-6839-4049-a9eb-be975749cfed" />


### Draw Mode
Draw Mode transforms the Logitech MX Ink stylus into a physics creation tool, allowing users to build custom objects and scene elements directly inside mixed reality.

Instead of being limited to preset shapes, users can freely sketch their own assets in real time and immediately use them in the sandbox.

#### Features

- Freehand drawing in 3D space
- Surface drawing on detected room geometry
- Pressure-sensitive strokes
- Create unlimited custom assets
- Build ramps, walls, barriers, tracks, tools, and abstract shapes
- Undo last stroke
- Clear all strokes
- Select completed drawings
- Move and reposition custom assets
- Use custom drawings during simulation

#### Snapshot Restore System

When simulation ends, the scene restores to its original authored state. This allows users to experiment freely without losing their setup or rebuilding the scene from scratch.

   <img width="800" height="450" alt="Draw_Mode" src="https://github.com/user-attachments/assets/959d1a7b-635a-41d7-961a-0459d9ac0cff" />


### Simulate Mode

Simulate Mode brings the sandbox to life by allowing users to test their creations with real-time physics.

After building a scene in Edit Mode or Draw Mode, users can enter Simulate Mode to observe motion, collisions, gravity, and interactions between all objects in the environment.

#### Features

- Start simulation instantly
- Real-time physics interactions
- Gravity, momentum, and collision responses
- Pause simulation at any time
- Resume from paused state
- Restart simulation
- Exit simulation safely
- Works with spawned objects and custom drawn assets
- Supports experimentation through rapid iteration
- PhysicsLens

#### PhysicsLens

PhysicsLens is a real-time visualization system that helps users understand physics through motion paths, vectors, trajectories, and live simulation data.

   <img width="800" height="450" alt="PhysicsLensSimulate_Mode" src="https://github.com/user-attachments/assets/5b5a3e9f-308c-4f27-9686-8882e91b5aa0" />


## Future Developments and Features
(edit / reformat)
* 3D Grid
* Co-Op / Collaborative Mode
* Share worlds
* More visualization Tools
* Pre-set worlds
* More kinds of physics interactions to play with
* Puzzle mode: Learn physics by solving puzzles, adding the interactions needed to solve puzzles.
* Custom graphs
* Data tracking and export


## Additional Links
- [DevStudio 2026 by Logitech Hackathon](https://devstudiologitech2026.devpost.com/)
- [MR Blueprint – Original Pitch Submission](https://devpost.com/software/mr-blueprint-draw-physics-into-reality)
- [MR Blueprint – Semifinalist Submission](https://devpost.com/software/mr-blueprint-draw-physics-into-reality-g1r50h?ref_content=my-projects-tab&ref_feature=my_projects)


