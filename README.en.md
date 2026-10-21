# ScreenLossless v1.0.0

[中文](README.md) | **English**

A screen recorder for Windows 10/11 x64, with full-screen, region and window capture. Choose RGB lossless or lightweight compressed encoding, with optional system audio and microphone capture. Built with C# / WinForms, FFmpeg and NAudio.

## Download

[Open the v1.0.0 release](https://github.com/AIBOT-labs/ScreenLossless/releases/tag/v1.0.0)

| Edition | Download and checksum | Requirements |
| --- | --- | --- |
| Standalone · about 79.6 MiB | [Download EXE](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Standalone.exe) · [SHA256](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Standalone.exe.sha256) | Windows 10/11 x64; runtime and recording tools included; open directly |
| Portable · about 74.0 MiB | [Download ZIP](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Portable.zip) · [SHA256](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Portable.zip.sha256) | Windows 10/11 x64; .NET runtime included |
| Compact · about 11.1 MiB | [Download ZIP](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Compact.zip) · [SHA256](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Compact.zip.sha256) | Windows 10/11 x64; .NET 8 Desktop Runtime x64 required |
| Complete corresponding FFmpeg source · about 41.6 MiB | [Download source ZIP](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_FFmpeg_9.0.2_Source.zip) · [SHA256](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_FFmpeg_9.0.2_Source.zip.sha256) | Source and build materials matching all three editions |

Choose **Standalone** for a single download: open the EXE directly, with no separate .NET installation. On first launch it prepares its embedded recording components in the current user's cache and reuses them later. No adjacent files are required. See the [standalone guide](docs/Standalone-README.md).

For Portable, extract the complete application ZIP and open `ScreenLossless.exe`. Keep FFmpeg, ffprobe and the other required files alongside the application. Compact requires .NET 8 Desktop Runtime x64. All three editions provide the same features.

The GitHub source repository and automatically generated **Source code ZIP** do not contain a compiled application. Build the source before running it, or download the Standalone EXE or an application ZIP from Releases.

## Quick start

1. Choose Full screen, Region or Window. The first launch defaults to full-screen capture; later launches restore the previous mode and a valid saved region.
2. In Video, choose a frame rate, preset, codec and save location. CPU codecs also work without a discrete GPU.
3. Enable system audio, microphone capture and mixing in Audio as needed. In Output, choose dimensions, MKV/MP4 and segmentation rules.
4. Click Start recording. Use the floating controls to pause, resume or stop, then wait for finalization to finish.
5. Open the result through Open latest or Open folder. Use Recover to process retained material after an interruption or failed finalization.

In an already-built local project, you can also run `Start-ScreenLossless.bat`, which launches `releases/v1.0.0/Portable/ScreenLossless.exe`.

## Interface language

The selector beside the window buttons offers System default, 中文 and English:

- The default follows the Windows display language: Chinese on Simplified or Traditional Chinese systems, English on all other systems.
- A manual selection is remembered and applies immediately while idle. Choose System default to return to automatic selection.
- Main-window text, settings, codec choices, window selection, recovery, floating controls and application messages support both languages. Device names, window titles, filenames and paths keep their original text.
- Language switching is unavailable during recording, a countdown or file processing. Switching preserves recording preferences, region geometry, the selected settings tab, the latest file and window state.

This is the full English project README. Use **中文** at the top to return to the Chinese version.

## Day and night modes

A single theme button sits beside the language selector. The moon switches to night mode; the sun switches back to day mode. The first launch uses day mode, and your choice is remembered. The main window, settings pages, menus, dialogs, scrollbars and floating controls share the active palette. Switching during recording or pause does not restart the encoder.

## Features

- **Capture areas:** full screen, region and window; remembered capture modes and regions, with movable and resizable outer dashed borders with contrasting dark/light layers and mouse input passing through the interior.
- **Floating controls:** countdown, start time and active recording duration, with pause, resume, stop and return-to-main actions.
- **Encoding choices:** CPU lossless and lightweight compression, NVIDIA H.264/HEVC, Intel QSV and AMD AMF; hardware availability is checked through actual trial encodes.
- **Audio capture:** system audio and microphone, mixed or separate tracks, with silence filling disconnected intervals and reconnection attempts.
- **Output and protection:** MKV/MP4, scaling, continuous protective chunks, timed stopping, disk-space protection and interrupted-recording recovery.
- **Desktop tools:** still-frame previews, screenshots, customizable global hotkeys, multiple displays and mixed-DPI support.

## Encoding and image quality

| Encoding option | Use and quality |
| --- | --- |
| H.264 x264rgb / FFV1 · CPU | Preserves RGB pixels at original dimensions for pixel-exact recording; no discrete GPU required |
| H.264 NVIDIA · 4:4:4 lossless | Preserves encoded YUV data; RGB color conversion is not guaranteed to be pixel-reversible |
| HEVC NVIDIA · 10-bit 4:4:4 lossless encoding | Separately verifies Rext, 10-bit, 4:4:4, BT.709 full range and identical YUV before and after encoding; does not imply exact original RGB or HDR capture |
| H.264 CPU / MPEG-4 CPU | Lightweight lossy compression for slower PCs; can be combined with a lower frame rate or smaller output dimensions |
| Standard NVIDIA H.264/HEVC, Intel QSV and AMD AMF | Hardware lossy compression; availability depends on the hardware, driver and FFmpeg build |

Auto selection stays at the top. Available options are ordered from lightweight compression to higher-fidelity lossless modes, with unavailable options at the bottom. Capture currently uses an **8-bit SDR** source: 10-bit encoding does not imply native 10-bit desktop or HDR capture. Scaling changes the original pixels. See the [user guide](docs/UserGuide.md) for details.

## Scope and limitations

Designed for local screen recording with adjustable regions, pause/resume, explicit lossless codec choices and CPU fallback.

- Compatible capture and modern window capture have different requirements. When a target window closes, is minimized or changes size, the application attempts to stop and save existing content.
- Automatic frame-rate reduction restarts the encoder and may introduce a brief gap; it does not automatically lower resolution. Segment size is a grouping target, not a strict limit for every file.
- Different NVIDIA / Intel / AMD models, physical audio-device hot-plugging, hours of A/V synchronization and mixed-DPI setups still require validation on the actual hardware. The project does not promise zero dropped frames or universal hardware support. See the [hardware validation guide](docs/HARDWARE-VALIDATION.md).

## Documentation

The detailed guides below are currently in Chinese. This README contains the English setup, feature, encoding and development overview.

- [Full user guide](docs/UserGuide.md)
- [Runtime resources and performance (Chinese)](docs/PERFORMANCE.md)
- [Hardware validation scope](docs/HARDWARE-VALIDATION.md)
- [Open-source review](docs/OPEN-SOURCE-REVIEW.md)
- [Third-party notices](docs/THIRD-PARTY-NOTICES.md)
- [Icon provenance](docs/ASSETS.md)
- [Development and directory maintenance](docs/DEVELOPMENT.md)
- [Corresponding FFmpeg source and distribution](docs/FFmpeg-SOURCE.md)
- [GitHub publishing steps and upload files](docs/GITHUB-PUBLISH.md)

## Development

Requires the **.NET 8 SDK**. The project file is `src/ScreenLossless.csproj`. The GitHub source repository excludes local `releases/` and `.tools/` directories. Build scripts reuse an existing local SDK and dependency cache when available.

| Task | Command |
| --- | --- |
| Build the application and release packages | `scripts/build-windows-x64.bat` |
| Run from source | `scripts/run-source-windows.bat` |
| Obtain the matching FFmpeg build | `scripts/download-ffmpeg.bat` |
| Run functional regressions | `powershell -ExecutionPolicy Bypass -File tests/Run-Regression.ps1` |
| Check localization and preserved settings | Add `-Localization` to the regression command above |
| Check repeated language switching and the main message loop | Add `-LanguageContext` to the regression command above |
| Check day/night modes, persistence and switching while recording | Add `-Themes` to the regression command above |
| Check the main window and floating controls | Add `-MainContext` to the regression command above |

You can also run `powershell -ExecutionPolicy Bypass -File scripts/build.ps1`. Release outputs go to `releases/v1.0.0/` and `releases/packages/`. Intermediate build files are stored in `.tools/build/`, and test recordings and screenshots in `tests/artifacts/`; these generated files are ignored by Git.

Building FFmpeg yourself requires Linux Docker and Python. These development tools are not needed to use an existing application package.

## Directory layout

| Directory | Contents |
| --- | --- |
| `src/` | Application source and project files, grouped by UI, recording, audio, encoding, capture and other modules |
| `releases/v1.0.0/` | Local Standalone / Portable / Compact application directories, excluded from the source repository |
| `releases/packages/` | Local application ZIPs, corresponding FFmpeg source ZIP and SHA256 files |
| `docs/` | User guides, validation procedures, open-source and distribution documentation |
| `scripts/` | Build, source-launch and FFmpeg acquisition/build scripts |
| `tests/` | Regression scripts, test project and test source under `Cases/` |
| `assets/` | Application icons and source images |
| `licenses/` | Third-party licenses, including .NET and NAudio |
| `third_party/` | Pinned FFmpeg source materials and build instructions |
| `.tools/` | Local SDK, dependencies and build cache, excluded from the source repository |
| `.github/` | GitHub Actions build configuration |

## License and distribution

Application source is licensed under [MIT](LICENSE). FFmpeg runs as a separate GPL-3.0-or-later program. NAudio, .NET and other components retain their own licenses; see [third-party notices](docs/THIRD-PARTY-NOTICES.md).

Standalone combines the runtime and recording tools into one EXE. Portable includes a compressed .NET runtime; Compact uses an installed runtime. FFmpeg is built with the modules needed by this application. Its corresponding source is provided as a separate ZIP to avoid duplicating it in each application package.

When publishing a GitHub Release, provide **the Standalone EXE, both application ZIPs, the complete corresponding FFmpeg source ZIP and their SHA256 files** together. Put the source download beside the application download links. Include the source ZIP when redistributing offline, and retain licenses and attribution. See the [FFmpeg source distribution instructions](docs/FFmpeg-SOURCE.md) for the prepared materials and distribution process.
