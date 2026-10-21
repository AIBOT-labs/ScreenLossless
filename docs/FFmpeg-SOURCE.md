# FFmpeg 对应源码下载与再分发

本软件包含自行编译的 FFmpeg 9.0.2-ScreenLossless，采用 GPL-3.0-or-later。完整对应源码文件名：

**ScreenLossless_FFmpeg_9.0.2_Source.zip**

该源码 ZIP 必须与 Standalone EXE 及 Portable / Compact 软件 ZIP 在同一个 GitHub Release 或同一下载页提供，且免费、无需额外授权或额外访问限制；下载页必须在软件链接旁直接提供对应源码链接。对应源码下载：https://github.com/AIBOT-labs/ScreenLossless/releases/download/v1.0.0/ScreenLossless_FFmpeg_9.0.2_Source.zip 。

源码包内包含 7 个依赖源码归档、SHA256SUMS、Dockerfile、构建脚本、编译参数、修改说明、工具链版本及许可证。软件目录（Standalone 为组件缓存目录）中的 FFmpeg-BUILD-RECORD.json 与源码包中的 BUILD-RECORD.json 应完全一致，包含当前 ffmpeg.exe / ffprobe.exe 的 SHA256。

源码包单独提供是为了避免在每个运行包内重复携带约 42 MB 源码，不是取消源码提供义务。如果离线转交、刻盘或用其他方式再次分发，请将该源码 ZIP 一并交付，并保留 LICENSE、licenses/ 和 ffmpeg-licenses/；只有运行 EXE 或 ZIP 不构成这里准备的完整离线分发套件。Standalone 将许可证、构建记录和本说明随组件内嵌并自动释放，位置见 [单 EXE 版说明](Standalone-README.md)。

发布前核对 Standalone EXE、两个运行 ZIP、一个对应源码 ZIP 及各自 SHA256。对应源码构建记录必须与分发的二进制哈希一致。

依据：GPLv3 第 6(d) 节关于同一位置的等价源码访问；原文随 ffmpeg-licenses/ 提供，官方说明：https://ffmpeg.org/legal.html 。
