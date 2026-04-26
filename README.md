# MR Blueprint

[Devstudio 2026 by Logitech Hackathon](https://devpost.com/software/mr-blueprint-draw-physics-into-reality-g1r50h?ref_content=my-projects-tab&ref_feature=my_projects) Semifinalist submission. 

REPLACE: make + add gifs
<table width="100%">
  <tr>
    <td width="33.33%" align="center">
      <img src="Recordings/controller_detection.gif" alt="gif 1" width="100%">
      <img width="300" height="200" alt="gif hand" src="https://github.com/user-attachments/assets/820b8cc7-19c9-417e-b5a7-f26c1d49caee" />
      <sub><b>Hand + Controller + Stylus Detection</b></sub>
    </td>
    <td width="33.33%" align="center">
      <img src="Recordings/space_drawing.gif" alt="gif 2" width="100%">
      <img width="300" height="200" alt="gif draw (1)" src="https://github.com/user-attachments/assets/bbfab1bf-7b8d-45a0-b1de-d105592f4ce8" />
      <sub><b>Trigger Pressure Sensitive Spatial Drawing</b></sub>
    </td>
    <td width="33.33%" align="center">
      <img src="Recordings/surface_drawing.gif" alt="gif 3" width="100%">
      <img width="300" height="200" alt="gif draw 2" src="https://github.com/user-attachments/assets/ba594c3f-c586-4fd7-b618-c898144753f2" />
      <sub><b>Tip Pressure Sensitive Surface Drawing</b></sub>
    </td>
  </tr>
</table>


## Demo Video
[![Demo Video](https://img.youtube.com/vi/ggg8-Duyzn4/0.jpg)](https://www.youtube.com/watch?v=ggg8-Duyzn4)
---
## Overview
MR Blueprint is a mixed reality physics-authoring sandbox for Meta Quest. Users enter a world-space XR environment, spawn 3D objects, arrange them in space, adjust properties like mass, friction, restitution, scale, gravity, rotation, and color, and then run simulations to see what happens. With MX Ink, users can switch between edit and draw workflows, create custom scene elements, and interact in a way that feels natural inside the headset. The experience includes a home flow, toolbar, content drawer, inspector, transform gizmo, simulation controls, help overlay, audio feedback, MX Ink connection status indicator, and live visual analysis tools such as vectors, motion paths, and real-time graphing. Rather than just showing motion, MR Blueprint helps users understand motion.

## Requirements

### Hardware
- Meta Quest 3 or Meta Quest 3S
- Logitech MX Ink Stylus
- VR-ready development PC
- USB-C cable for headset build deployment

### Software
- Unity 2022.3 LTS or newer
- Android Build Support for Unity
- XR Plugin Management
- XR Interaction Toolkit
- Meta Quest / OpenXR setup
- Logitech MX Ink SDK
- Git

### Recommended Setup
- Enable Developer Mode on the Meta Quest headset
- Allow USB debugging when prompted
- Build target set to Android in Unity
- Passthrough permissions enabled for mixed reality mode
- Headset and stylus paired before launching the app


## Setup

1. Open project in Unity
2. Install required XR packages
3. Switch platform to Android
4. Build & Run to Meta Quest
5. Enable passthrough permissions if needed
---

## Control Schema
<img width="1800" height="1200" alt="control" src="https://github.com/user-attachments/assets/b11c373a-aa79-4907-8703-59d73b048957" />

KEYBINDS IN TEXT HERE (add a table), possibly add descriptions for each

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

## Features

INCLUDE IMAGES, SCREENSHOTS, EXAMPLES!!!!

### Room Space
support for ar and vr and room randomization

### Edit Mode

### Draw Mode
can draw your own assets. will be able to edit asset properties same as you can edit other content drawer objects. Can build an object out as much as you'd like, and then select it or exit draw mode to "complete" the asset. supports as many seperate custom assets as you'd like

### Simulate Mode


#### PhysLens (check name, mightve changed it to physicslens)


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

## License

Add MIT license info here

## Additional Links

INCLUDE DEVPOST SUBMISSIONS 1 AND 2, DEMO VIDEO LINK, ORIGINAL DEVPOST HACKATHON PAGE MAYBE
