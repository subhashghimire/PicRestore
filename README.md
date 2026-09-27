# PicRestore

Windows application to restore old images — Polaroids in particular — as faithfully as possible to
their presumed original condition, without redrawing, beautifying, or reinterpreting anything that
survived.

The full design plan (goals, architecture, pipeline, roadmap) lives at
[claude.ai/code/artifact/720ab6b9-f8c8-4165-8aa1-757eaeca7bc8](https://claude.ai/code/artifact/720ab6b9-f8c8-4165-8aa1-757eaeca7bc8).
This README covers the code in this repository; the sections below summarise the parts that matter for
building and contributing.

## What's here

PicRestore follows the archival restoration protocol in [docs/RESTORATION_PROTOCOL.md](docs/RESTORATION_PROTOCOL.md)
(also shown in the app's About tab): intact areas are ground truth, repairs are localized, faces and
patterns are locked, colour is corrected from intact areas, and a final ~10% sharpening pass is applied.

The pipeline: colour & tone -> localized reconstruction (LaMa) -> grain matching -> opt-in AI face
enhancement (off) -> subtle sharpening. Damage is found by a learned detector that you can keep
training in-app from before/after pairs (Settings -> Damage detection model), with faces locked by an
automatic face detector (YuNet).

| Project | Targets | What it does |
| --- | --- | --- |
| `PicRestore.Core` | net8.0, cross-platform | Domain models (`RasterImage`, `DamageMask`, `ProcessingSettings`, `RestorationProject`) and the abstractions (`IDamageDetector`, `IColorRestorer`, `IInpainter`, `IGrainMatcher`, `IFaceIdentityGuard`, `IFaceEnhancer`) every other project codes against. No external dependencies. |
| `PicRestore.Imaging` | net8.0, cross-platform | Loads/saves JPEG, JPG, PNG, BMP and TIFF via SixLabors.ImageSharp and converts to/from `RasterImage`. |
| `PicRestore.Restoration` | net8.0, cross-platform | The pipeline: damage detection (learned MLP + the classical rule-based detector), colour/tonal restoration, LaMa tiling/compositing with a diffusion fallback, grain matching, an identity guard, and the `RestorationPipeline` orchestrator. No external dependencies. |
| `PicRestore.Ml` | net8.0 | ONNX Runtime host for learned models: `OnnxLamaModel` and `LamaModelStore`, which downloads the ~92 MB LaMa model (OpenCV Zoo, Apache-2.0) once to `%LOCALAPPDATA%\PicRestore\Models` and verifies its SHA-256 (for an offline machine, place `inpainting_lama_2025jan.onnx` there yourself); and `OnnxFaceDetector`, running the bundled 232 KB YuNet face model (MIT). |
| `PicRestore.Tests` | net8.0, cross-platform | xUnit tests for the pipeline above, using small synthetic images. |
| `PicRestore.App` | net8.0-windows10.0.19041.0 (WinUI 3) | The Windows UI: Import → Mask Editor → Compare → Export, all sharing one `RestorationViewModel`. |
| `tools/damage-model` | Python | Builds detector training data from (damaged, restored) photo pairs, trains the detector and regenerates `LearnedDamageModel.g.cs`. See its README. |

## AI-enhance mode

`ProcessingSettings.EnableAiEnhance` is **off by default** for every new project. When off, the
restoration pipeline never calls into generative face-restoration (GFPGAN/CodeFormer/GPEN-style
"enhance"); it uses only the conservative, structure-aware reconstruction path. A user can turn it on
per project from the same settings that gate the rest of the pipeline. `OnnxFaceEnhancer`
(`PicRestore.Restoration/Enhancement/OnnxFaceEnhancer.cs`) is the place to wire in a real exported
model — right now it throws on purpose rather than silently doing nothing, so an enabled-but-unwired
project fails loudly instead of shipping an untouched image labelled as enhanced.

## Building

**Cross-platform libraries and tests** (`PicRestore.Core`, `PicRestore.Imaging`, `PicRestore.Restoration`,
`PicRestore.Tests`) only need the .NET 8 SDK, on Windows, macOS or Linux:

```bash
dotnet restore
dotnet build src/PicRestore.Core src/PicRestore.Imaging src/PicRestore.Restoration src/PicRestore.Tests
dotnet test src/PicRestore.Tests
```

**The WinUI 3 app** (`PicRestore.App`) additionally needs Windows 10 (1809+) or Windows 11, Visual
Studio 2022 with the ".NET Desktop Development" and "Windows application development" workloads, and
the Windows App SDK. Open `PicRestore.sln` in Visual Studio, or from a Windows machine:

```powershell
dotnet build src\PicRestore.App
```

`PicRestore.App` is unpackaged (`WindowsPackageType=None`) for a simple build/run inner loop.

## Installer and portable build

```powershell
build\package.cmd
```

This runs the tests, publishes the app, bundles the AI repair model, checks that the exe starts, and
writes to `artifacts\`:

- `PicRestore-Setup-<version>-x64.exe`: the Windows installer (needs [Inno Setup 6](https://jrsoftware.org/isinfo.php): `winget install --id JRSoftware.InnoSetup -e`).
- `PicRestore-<version>-win-x64-portable.zip` and `publish\...\PicRestore.App.exe`: runs without installing.

See [docs/PACKAGING.md](docs/PACKAGING.md) for options.

## Status & known limitations

- The built-in detector is trained on five before/after pairs. On held-out regions of those photos its
  damage-map overlap (IoU) is 0.52 for the heavily damaged Polaroid and 0.02-0.19 for the lightly
  damaged prints; on a photo it has never seen it is only 0.02-0.09. Five pairs is not enough to
  generalise, and busy texture (foliage, soil) can be mistaken for damage - hence the conservative
  defaults (sensitivity 0.2, face lock, locked intact pixels) and the Mask Editor review step. Adding
  your own pairs under Settings trains it further; a new model is only adopted if it scores at least as
  well on held-out regions and keeps what the built-in model knew.
- LaMa rebuilds texture and structure from the surroundings; it does not invent content that is
  completely gone (e.g. a face hidden under a blotch). Tools that do (diffusion-based "restorers")
  are generating new detail, which is a different trade-off from this app's "restore, don't reinvent"
  default.
- `SimpleFaceIdentityGuard` is still a size-based placeholder with no face detection. It is advisory
  by default (`ProcessingSettings.StrictIdentityGuard = false`): a failed check is logged as a warning
  instead of discarding the reconstruction.
- `OnnxFaceEnhancer` is a stub - see "AI-enhance mode" above.

## License

MIT - see `LICENSE`.
