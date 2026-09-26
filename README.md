# PicRestore

Windows application to restore old images — Polaroids in particular — as faithfully as possible to
their presumed original condition, without redrawing, beautifying, or reinterpreting anything that
survived.

The full design plan (goals, architecture, pipeline, roadmap) lives at
[claude.ai/code/artifact/720ab6b9-f8c8-4165-8aa1-757eaeca7bc8](https://claude.ai/code/artifact/720ab6b9-f8c8-4165-8aa1-757eaeca7bc8).
This README covers the code in this repository; the sections below summarise the parts that matter for
building and contributing.

## What's here

This is the Phase-1 MVP from the roadmap: a classical (non-ML) restoration pipeline plus a WinUI 3
shell, built so the workflow is usable and testable end to end before any model gets trained. Phase 2
(a learned damage-segmentation model and a LaMa-based reconstruction model, both via ONNX Runtime +
DirectML) slots into the same interfaces without changing the app around it.

| Project | Targets | What it does |
| --- | --- | --- |
| `PicRestore.Core` | net8.0, cross-platform | Domain models (`RasterImage`, `DamageMask`, `ProcessingSettings`, `RestorationProject`) and the abstractions (`IDamageDetector`, `IColorRestorer`, `IInpainter`, `IGrainMatcher`, `IFaceIdentityGuard`, `IFaceEnhancer`) every other project codes against. No external dependencies. |
| `PicRestore.Imaging` | net8.0, cross-platform | Loads/saves JPEG, JPG, PNG, BMP and TIFF via SixLabors.ImageSharp and converts to/from `RasterImage`. |
| `PicRestore.Restoration` | net8.0, cross-platform | The Phase-1 classical pipeline: damage detection, colour/tonal restoration, diffusion-based inpainting, grain matching, a conservative identity guard, and the `RestorationPipeline` orchestrator. |
| `PicRestore.Tests` | net8.0, cross-platform | xUnit tests for the pipeline above, using small synthetic images. |
| `PicRestore.App` | net8.0-windows10.0.19041.0 (WinUI 3) | The Windows UI: Import → Mask Editor → Compare → Export, all sharing one `RestorationViewModel`. |

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

`PicRestore.App` is unpackaged (`WindowsPackageType=None`) for a simple build/run inner loop; MSIX
packaging for distribution is a Phase-3 roadmap item.

## Status & known limitations

- This code was generated in a Linux sandbox without network access to the .NET SDK, so the
  cross-platform libraries and tests have been carefully reviewed but not yet compiled here — please
  run `dotnet build`/`dotnet test` as your first step and file an issue for anything that doesn't come
  up clean. The WinUI 3 app can only really be verified on Windows in any case.
- `CompositeDamageDetector` and `SimpleFaceIdentityGuard` are deliberately simple, classical/heuristic
  Phase-1 implementations (see the code comments in `PicRestore.Restoration`), not the trained models
  described for Phase 2 in the design plan. They exist to make the end-to-end workflow real and
  testable now.
- `OnnxFaceEnhancer` is a stub - see "AI-enhance mode" above.

## License

MIT - see `LICENSE`.
