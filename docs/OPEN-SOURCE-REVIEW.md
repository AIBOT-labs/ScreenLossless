# 开源与分发说明

项目应用源码采用根目录 MIT 许可证，第三方组件保留各自许可证。应用源码由 ChatGPT 生成；图标来源见 ASSETS.md。

FFmpeg 为独立命令行程序，应用通过文件、标准输入与进度输出调用。当前使用自建 FFmpeg 9.0.2-ScreenLossless，完整对应源码、依赖归档、固定版本、哈希和构建方法包含于 ScreenLossless_FFmpeg_9.0.2_Source.zip。二进制必须与 BUILD-RECORD.json 中的哈希匹配。

GitHub 源码仓库包含 src、tests、scripts、assets、docs、licenses、third_party 和构建配置，不提交 .tools、releases 或测试产物。

发布运行包时保留 licenses/、ffmpeg-licenses/、FFmpeg-BUILD-RECORD.json 与 FFmpeg-SOURCE.md。Standalone 单 EXE 版将这些材料内嵌并随组件释放到当前用户缓存，外置对应源码 ZIP 的提供方式相同。在同一 Release 或下载页同时提供完整对应源码 ZIP，并在运行包链接旁明确链接源码；离线转交时一并交付。具体见 FFmpeg-SOURCE.md。

应用和 FFmpeg 分别保留 MIT 与 GPL 许可。分进程不是所有情况下免除 GPL 义务的绝对规则；上述工程材料也不构成所有地区的专利、商标或著作权保证。

许可依据：[FFmpeg 法律说明](https://ffmpeg.org/legal.html)、[GPLv3](https://gcc.gnu.org/onlinedocs/gcc/Copying.html)。NAudio 与 .NET 许可在 licenses/ 中。项目仓库地址： https://github.com/AIBOT-labs/ScreenLossless 。

发布操作见 [GITHUB-PUBLISH.md](GITHUB-PUBLISH.md)，其中区分源码仓库与 Release 附件。使用 `python scripts/prepare-github.py` 生成当前工作目录的源码包；生成包会包含新增、修改的代码并排除已删除内容和本机产物。此脚本的常见密钥格式扫描是发布辅助检查，不是完整的安全审计。
