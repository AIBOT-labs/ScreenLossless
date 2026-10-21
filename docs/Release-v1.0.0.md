# ScreenLossless v1.0.0

Windows 10/11 x64 屏幕录制工具，提供中英文界面、全屏/区域/窗口录制、暂停继续、浮动控制、系统声音与麦克风、分段保存和中断恢复。

A Windows 10/11 x64 screen recorder with Chinese and English interfaces, full-screen/region/window capture, pause/resume, floating controls, system audio and microphone recording, segmentation and interrupted-recording recovery.

| 下载 / Download | 校验 / Checksum | 说明 / Notes |
| --- | --- | --- |
| [Standalone EXE](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Standalone.exe) | [SHA256](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Standalone.exe.sha256) | 约 79.6 MiB，直接打开，内置运行环境与编码组件 / Open directly; runtime and tools included |
| [Portable](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Portable.zip) | [SHA256](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Portable.zip.sha256) | 约 74.0 MiB，内置 .NET / Runtime included |
| [Compact](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Compact.zip) | [SHA256](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_v1.0.0_Win64_Compact.zip.sha256) | 约 11.1 MiB，需 .NET 8 Desktop Runtime x64 / Installed runtime required |
| [FFmpeg 对应源码 / Corresponding source](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_FFmpeg_9.0.2_Source.zip) | [SHA256](https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_FFmpeg_9.0.2_Source.zip.sha256) | 约 41.6 MiB，与三个版本匹配 / Matches all three editions |

单 EXE 版直接打开，首次运行自动准备内置组件并复用缓存，不需要另外安装 .NET。Portable / Compact 需完整解压后打开 ScreenLossless.exe，保留 FFmpeg、ffprobe 和许可证目录。中文 Windows 默认中文，其他系统默认英文，可在标题栏手动切换。

Open the Standalone EXE directly; it prepares embedded components on first launch and reuses its cache. No separate .NET installation is needed. Extract Portable / Compact completely and launch ScreenLossless.exe, keeping FFmpeg, ffprobe and the license directories. Chinese Windows systems default to Chinese; all others default to English. The title-bar selector changes the language.

标题栏只需一个太阳/月亮按钮即可切换白天与黑夜模式，并记住选择。菜单、弹窗、滚动条和录制浮窗同步换色，录制中切换不会重启编码器。

A single sun/moon button switches between remembered day and night modes. Menus, dialogs, scrollbars and floating controls follow the palette; switching while recording does not restart the encoder.

CPU x264rgb / FFV1 在原始尺寸下保留 RGB 像素。NVIDIA H.264 4:4:4 与 HEVC 10-bit 4:4:4 无损方案保留编码后的 YUV 数据，不保证原始 RGB 逐像素一致；当前捕获为 8-bit SDR，不是 HDR。另有轻量 CPU、NVIDIA、Intel QSV 和 AMD AMF 有损压缩方案，实际可用性以本机试编码为准。

At original dimensions, CPU x264rgb / FFV1 preserve RGB pixels. NVIDIA H.264 4:4:4 and HEVC 10-bit 4:4:4 lossless modes preserve encoded YUV data, without guaranteeing identical original RGB pixels. Capture is currently 8-bit SDR, not HDR. Lightweight CPU, NVIDIA, Intel QSV and AMD AMF lossy modes are also available, subject to trial encoding on the local machine.

应用源码为 MIT，FFmpeg 为 GPL-3.0-or-later，其他组件保留各自许可证。对应源码包含本次 FFmpeg 和依赖源码、构建方法、许可证及二进制哈希。再分发时同时提供匹配的对应源码并保留声明。

Application source uses MIT; FFmpeg uses GPL-3.0-or-later. Other components retain their licenses. The corresponding-source bundle contains the matching FFmpeg/dependency sources, build instructions, notices and binary hashes. Provide it with redistributions and retain the notices.

各型号显卡、实体声卡热插拔、数小时音画同步和混合 DPI 多屏仍需要对应实机验证。自动降帧重启编码器时可能有短暂间隔，分段大小不是严格上限。

GPU models, physical audio-device hot-plugging, hours of A/V synchronization and mixed-DPI displays still need validation on the actual hardware. Automatic frame-rate reduction may introduce a gap when restarting the encoder; segment sizes are not strict per-file limits.
