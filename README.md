# Rhino IfcLite Importer

An example showing how to consume the [**ifc-lite**](https://github.com/LTplus-AG/ifc-lite)
library's .NET DLL inside **Rhino 8** — a small, clean, MIT-licensed plug-in and 
Grasshopper component that imports IFC models, complete with an Eto dialog, 
property baking, coordinate selection, and smart caching.

> Built by [LINK Arkitektur](https://linkarkitektur.com) as a public reference
> for working with the ifc-lite native library from RhinoCommon and Grasshopper.

---

## Features

### User Interface & Commands
- **Interactive Viewports:** Viewport navigation (zoom, pan, rotate) remains fully active during imports, preventing the application interface from locking up. Pressing `ESC` instantly cancels the operation.
- **Native Status Bar Progress:** Visual progress tracking is displayed directly on the standard Rhino status bar progress meter at the bottom of the screen.
- **Eto Import Window:** A live modeless dialog (`IfcLiteImporter` command) that stays on top of the document for interactive imports.
- **Headless Import:** A command-line pathway (`IfcLiteImport` command) for scripting and batch workflows.

### Geometry & Document Organization
- **File-Based Layer Structure:** Instead of placing all imported objects under a generic parent layer, they are organized under a root layer dynamically named after the source file, keeping your layer panel clean.
- **Simplified Mesh Faces (Coplanar Merging):** Adjacent flat surfaces (like walls, roofs, and slabs) are automatically simplified into single flat faces (n-gons). This reduces face counts and keeps files lighter while keeping flat shading and material colors intact.
- **Bakes IFC Element Properties:** Identity attributes and custom properties are baked directly onto the Rhino objects as **user strings** so that the BIM data travels with the geometry.
- **Joins Objects by Identical Properties:** Merges meshes that share the same properties into a single Rhino object, keeping the document tidy instead of producing thousands of tiny fragments.
- **Shared vs Project Coordinates:** Choose between site-local (Project) coordinates or real-world (Shared) coordinates via the `IfcSite` placement transform.

### Grasshopper Support (`.gha`)
A native Grasshopper component (**IFC Import**) is included, which allows reading IFC files directly inside your definitions:
- **Direct Geometry Previews:** Read and preview meshes, colors, types, and metadata directly in Grasshopper without cluttering your active Rhino document.
- **Layer-Matching Sort Order:** Geometries are sorted alphabetically by `IfcType` so the list order matches the Rhino layer tree.
- **Data Tree Metadata:** Outputs all IFC attributes and custom properties as structured data trees matching the index of each output mesh.
- **Dual-Level Caching:** 
  * *Level 1 Cache:* Retains the parsed IFC model in memory, re-parsing only if the file path or the file's disk modification timestamp changes.
  * *Level 2 Cache:* Retains the converted and simplified Rhino meshes, bypassing geometry regeneration when only toggling "Join by Properties" for instant recalculations.

---

## How it works

The plug-in is a thin Rhino-facing and Grasshopper-facing layer on top of the native ifc-lite parser.
The data flows in one direction:

```
external/ifc-lite  (git submodule, Rust, MPL-2.0)
        │
        │  cargo build --profile server-release -p ifc-lite-ffi
        ▼
ifc_lite_ffi  (native library, e.g. ifc_lite_ffi.dll)
        │
        │  P/Invoke + JSON deserialization
        ▼
IfcLite.Net  (managed wrapper — typed meshes + metadata, no Rhino dependency)
        │
        ├── build meshes, bake user strings, join by properties
        │   ▼
        ├── IfcLiteImporter.Rhino  (Rhino 8 plug-in / .rhp — adds objects to the document)
        │
        └── IfcLiteImporter.Grasshopper  (Grasshopper plug-in / .gha — outputs geometry and data)
```

1. **`external/ifc-lite`** is included as a git submodule. Building its FFI crate
   (`cargo build --profile server-release -p ifc-lite-ffi`) produces the native
   **`ifc_lite_ffi`** library.
2. **`IfcLite.Net`** P/Invokes that native library and deserializes the JSON it
   returns into typed meshes and metadata. It has no RhinoCommon dependency, so
   it can be reused in other assemblies, tests, and tools.
3. **`IfcLiteImporter.Rhino`** builds Rhino meshes, applies the coplanar face merge, bakes properties as user strings, joins duplicate objects, and adds the results to the document.
4. **`IfcLiteImporter.Grasshopper`** performs the same geometric processes but outputs the data directly as native Grasshopper parameters.

---

## Build from source

> Requirements: **Rhino 8**, the **.NET 8.0 SDK** (required for compiling the solution), and a **Rust toolchain** for the native library.

```bash
# 1. Clone with the ifc-lite submodule.
git clone --recursive https://github.com/linkarkitektur/Rhino-IfcLiteImporter.git
cd Rhino-IfcLiteImporter

# (If you already cloned without --recursive:)
git submodule update --init --recursive
```

Build the native ifc-lite library:

```bat
:: Windows (Command Prompt)
build\build-native.bat
```

```powershell
# Windows (PowerShell)
build\build-native.ps1
```

```bash
# macOS / Linux
build/build-native.sh
```

Then build the managed solution:

```bash
dotnet build IfcLiteImporter.sln -c Release
```

The native library is copied next to the plug-in automatically, so the resulting
`.rhp` and `.gha` files are ready to load in Rhino 8. If you forget to build the native library first, the managed build **fails with an error** pointing you back to the `build-native` scripts.

---

## Debugging in Visual Studio

Pressing **F5** builds the plug-in and launches Rhino with the debugger attached.

1. **Set `IfcLiteImporter.Rhino` as the startup project.** It is a class library
   that compiles to a `.rhp`, so trying to "run" a plain library gives *"cannot run a library type project"*. The plug-in ships a launch profile (`Properties/launchSettings.json`) that starts Rhino instead of trying to run the DLL:

   ```json
   {
     "profiles": {
       "Rhino 8": {
         "commandName": "Executable",
         "executablePath": "C:\\Program Files\\Rhino 8\\System\\Rhino.exe"
       }
     }
   }
   ```

   If Rhino is installed somewhere else, edit `executablePath` to match.

2. **Build the native library once** (`build\build-native.bat` or
   `build\build-native.ps1`) so `ifc_lite_ffi.dll` is copied next to the `.rhp`.

Then:

- Press **F5** — Rhino 8 starts under the debugger.
- **The first time only**, point Rhino at the freshly built plug-in: run the
  `PluginManager` command → **Install**, and pick
  `src\IfcLiteImporter.Rhino\bin\Debug\net8.0\IfcLiteImporter.Rhino.rhp`
  (or drag that `.rhp` onto the Rhino window). Rhino remembers the path, so every
  later F5 rebuilds it in place and reloads it automatically.
- To use the Grasshopper component, it is automatically deployed to your `%APPDATA%\Grasshopper\Libraries` directory upon compilation.
- Run `IfcLiteImporter` in Rhino, or open Grasshopper and set breakpoints in the code — they bind once Rhino loads the plug-in assemblies.

---

## Install

Grab a prebuilt package from the [GitHub Releases](https://github.com/linkarkitektur/Rhino-IfcLiteImporter/releases) page. You can either:

- download the **`.yak`** and **drag it onto an open Rhino 8 window**, or use the
  **Package Manager** (`PackageManager` command, search for `ifcliteimporter`); or
- download **`IfcLiteImporter-Setup.exe`** and run the installer, which calls
  Rhino's `yak` to install the package for you.

---

## Usage

### In Rhino (Plug-in)
1. Run the **`IfcLiteImporter`** command in Rhino 8.
2. Pick an `.ifc` file.
3. Choose **Shared** or **Project** coordinates.
4. Click **Import**.

For scripting or batch jobs, use **`IfcLiteImport`** to import a file headlessly (without opening the window).

### In Grasshopper
1. Open Grasshopper inside Rhino 8.
2. Go to the **IfcLite** tab and drag the **IFC Import** component onto the canvas.
3. Connect the file path and configure your desired coordinate, coplanar-merging, and joining settings.

---

## Licensing

- This repository (the importer plug-in, Grasshopper component, and `IfcLite.Net` wrapper) is licensed under the **MIT License** — see [`LICENSE`](LICENSE).
- The bundled **ifc-lite** submodule is licensed under the **MPL-2.0**. Its native
  library is built from source from the submodule; see [`NOTICE`](NOTICE) for
  attribution details.

The LINK Arkitektur name and logo are brand assets of LINK Arkitektur.
