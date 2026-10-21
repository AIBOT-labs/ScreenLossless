# ScreenLossless FFmpeg 9.0.2 源码与构建材料

这是 ScreenLossless 自行编译的 Windows x64 FFmpeg，不是 Gyan 或 BtbN 二进制的重新命名。FFmpeg 保持 GPLv3-or-later；应用本体的 MIT 不覆盖这里的第三方源码。

## 随包提供的内容

- sources/：FFmpeg 及本次使用的全部非系统依赖源码归档，附 SHA256SUMS。
- sources.lock.json：每个来源 URL、版本/提交、归档 SHA256 和大小。
- Dockerfile、mingw-toolchain.cmake、build.sh：Windows 交叉编译工具链和完整构建步骤。
- record-build.py：生成实际二进制哈希、工具链版本及构建配置记录。
- BUILD-RECORD.json、build-config.mak、toolchain-packages.txt：实际编译完成后写入的对应关系记录。
- licenses/：实际构建提取的第三方许可及工具链版权说明，编译完成后写入。

## 构建方法

需要 Linux Docker（Docker Desktop 可用）、约 3 GB 临时空间。源码随包齐全，构建时不需要再下载这些源码；首次构建工具链时会从 Debian 下载编译工具。

在此目录运行：

```sh
docker build -t screenlossless-ffmpeg-toolchain:20261012 .
mkdir -p output
docker run --rm --cpus=6 --mount "type=bind,source=$PWD,target=/sources,readonly" --mount "type=bind,source=$PWD/output,target=/output" screenlossless-ffmpeg-toolchain:20261012 bash /sources/build.sh
```

在完整项目的 PowerShell 中可运行 `./scripts/build-ffmpeg.ps1`。如果源码归档丢失，运行 `python scripts/prepare-ffmpeg-sources.py`，它按锁文件恢复并校验；`--verify-only` 只校验。

AMF 只使用公开头文件：归档包含固定提交的全部 amf/public/include 及根 LICENSE，不包含未链接或未使用的 SDK 二进制、样例和 Thirdparty。该筛选是构建材料的一部分，生成规则在 prepare-ffmpeg-sources.py 中。AMD 的实际编码运行库由用户显卡驱动提供，没有打包到本应用。

libvpl 在 MinGW 下只修改一行条件编译 guard：`#if _MSC_VER < 1400` 改为 `#if defined(_MSC_VER) && _MSC_VER < 1400`。完整修改命令在 build.sh 中；FFmpeg 和其余依赖不修改源码。FFmpeg 通过 configure 的 extra-version 标记 ScreenLossless 构建。

此构建保留应用所用的 x264/x264rgb、FFV1、MPEG-4、MJPEG、H.264/HEVC NVENC、H.264 QSV/AMF、DXGI ddagrab、GDI、PNG、FLAC/AAC、Matroska/MP4 等功能。这里不包含 Gyan 全功能包的其他可选外部库，例如 libx265、libaom；应用不使用这些外部编码器。

使用固定版本 MinGW-w64 Windows SDK 头文件，确保现代 gfxcapture 接口可用；其完整来源和版权保留在对应源码归档中。

CPU 编码、NVIDIA 实际编码以及录制链路由项目测试验证。QSV/AMF 编译存在不代表已经完成 Intel/AMD 实机验收，仍以 HARDWARE-VALIDATION.md 为准。

## 版权与发布

FFmpeg、x264、zlib、nv-codec-headers、AMF 头文件和 libvpl 的原始版权/许可保留在各自源码归档中及 licenses/。MinGW/GCC 运行时与构建工具的声明另行保留；常规未修改编译工具和符合 GPL 系统库例外的部分不当作本应用自有源码。

交叉编译流程参考 BtbN/FFmpeg-Builds 的公开构建方法（提交 63ac3265b0789ea16c7b1f8351cd2ad195a28722），其 MIT 版权声明见 BtbN-MIT.txt。本项目提供独立的精简配方，只编译本软件需要的库。

完整对应源码独立提供为 ScreenLossless_FFmpeg_9.0.2_Source.zip。发布者必须将源码 ZIP 与运行包放在同一 Release / 下载页，并在运行包下载链接旁提供源码链接（GPLv3 第 6(d) 节）；离线分发时一并交付源码 ZIP。保留运行包的 ffmpeg-licenses/ 和 FFmpeg-BUILD-RECORD.json。此材料只对应 BUILD-RECORD.json 中标明哈希的自建二进制，不适用于旧版 Gyan 便携包或用户另外指定的 FFmpeg。

## 体积优化配方

构建使用 --disable-everything 后显式启用本软件需要的编码、解码、输入、封装、协议、滤镜和测试信号。完整清单在 build.sh 中；保留 H.264/HEVC、FFV1、MPEG-4、MJPEG、PNG、FLAC/AAC/PCM、GDI/DXGI/现代窗口抓屏、分段、混音、缩放及完整回归所需链路。它不是通用全格式转码包；额外命令行用途应另行选择 FFmpeg。对应源码材料仍完整保留。
