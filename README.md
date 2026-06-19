# Rhino IfcLite Importer

An example showing how to consume the [**ifc-lite**](https://github.com/LTplus-AG/ifc-lite)
library's .NET DLL inside **Rhino 8** — a small, clean, MIT-licensed plug-in that
imports IFC models, complete with an Eto dialog, property baking and coordinate
selection.

> Built by [LINK Arkitektur](https://linkarkitektur.com) as a public reference
> for working with the ifc-lite native library from RhinoCommon.

---

## Features

- **Eto import window** with a live progress bar and a status bar so large models
  give continuous feedback.
- **`IfcLiteImporter` command** — opens the import window.
- **`IfcLiteImport` command** — headless file import (useful for scripting and
  batch workflows).
- **Bakes IFC element properties** (type, `GlobalId`, name, property-set values,
  ...) onto the Rhino objects as **user strings**, so the BIM data travels with
  the geometry.
- **Joins objects that share identical properties**, keeping the document tidy
  instead of producing thousands of tiny fragments.
- **Choose Shared vs Project coordinates** when importing, to line the model up
  with either the site/shared origin or the project origin.

---

## How it works

The plug-in is a thin Rhino-facing layer on top of the native ifc-lite parser.
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
        │  build meshes, bake user strings, join by properties
        ▼
IfcLiteImporter.Rhino  (Rhino 8 plug-in / .rhp — adds objects to the document)
```

1. **`external/ifc-lite`** is included as a git submodule. Building its FFI crate
   (`cargo build --profile server-release -p ifc-lite-ffi`) produces the native
   **`ifc_lite_ffi`** library.
2. **`IfcLite.Net`** P/Invokes that native library and deserializes the JSON it
   returns into typed meshes and metadata. It has no RhinoCommon dependency, so
   it can be reused in tests and tools.
3. **`IfcLiteImporter.Rhino`** builds Rhino meshes from those typed results, bakes
   the IFC properties as user strings, joins objects that share identical
   properties, and adds the results to the active document.

---

## Build from source

> Requirements: **Rhino 8**, the **.NET SDK** (8.0 SDK is fine — it targets
> net7.0), and a **Rust toolchain** for the native library.

```bash
# 1. Clone with the ifc-lite submodule.
git clone --recursive https://github.com/linkarkitektur/Rhino-IfcLiteImporter.git
cd Rhino-IfcLiteImporter

# (If you already cloned without --recursive:)
git submodule update --init --recursive
```

Build the native ifc-lite library:

```powershell
# Windows
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
`.rhp` is ready to load in Rhino 8.

---

## Install

Grab a prebuilt package from the [GitHub Releases](https://github.com/linkarkitektur/Rhino-IfcLiteImporter/releases)
page (releases are produced by the manual **Release** workflow). You can either:

- download the **`.yak`** and **drag it onto an open Rhino 8 window**, or use the
  **Package Manager** (`PackageManager` command, search for `ifcliteimporter`); or
- download **`IfcLiteImporter-Setup.exe`** and run the installer, which calls
  Rhino's `yak` to install the package for you.

---

## Usage

1. Run the **`IfcLiteImporter`** command in Rhino 8.
2. Pick an `.ifc` file.
3. Choose **Shared** or **Project** coordinates.
4. Click **Import**.

For scripting or batch jobs, use **`IfcLiteImport`** to import a file headlessly
(without opening the window).

---

## Licensing

- This repository (the importer plug-in and `IfcLite.Net` wrapper) is licensed
  under the **MIT License** — see [`LICENSE`](LICENSE).
- The bundled **ifc-lite** submodule is licensed under the **MPL-2.0**. Its native
  library is built from source from the submodule; see [`NOTICE`](NOTICE) for
  attribution details.

The LINK Arkitektur name and logo are brand assets of LINK Arkitektur.
