# GitHub 发布 / Publishing to GitHub

源码仓库和运行包分别上传。当前版本是 **v1.0.0**，目标地址为 `https://github.com/AIBOT-labs/ScreenLossless`；若使用其他账户或仓库名，先更新两个 README、FFmpeg-SOURCE.md 和发行说明中的链接。

## 1. 上传源码 / Upload source

在项目根目录运行（需要 Git 和 Python 3.9+）：

```powershell
python scripts/prepare-github.py
```

生成 `releases/packages/ScreenLossless_v1.0.0_Source.zip` 和 SHA256。它包含当前工作目录里的有效源码，包括尚未提交的新文件及修改；不包含已删除文件、`.git/`、`.tools/`、`releases/`、测试产物或旧日志。脚本校验文档本地链接、常见密钥格式、FFmpeg 源码哈希，并逐文件核对生成的 ZIP。

解压后将 **ScreenLossless 文件夹里面的内容** 上传到仓库根目录，保留 `.github/`、`.gitignore` 和 `.gitattributes` 等隐藏内容。不要只把源码 ZIP 当成仓库内的一个文件上传，也不要使用尚未更新的 `git archive HEAD`，它会遗漏未提交的修改。

也可以直接在当前 Git 工作目录检查 `git status`、`git diff`，将需要的新增、修改和删除统一提交后推送。`.gitignore` 只限制源码提交，不会自动上传 Release 附件。当前源码最大单文件小于 GitHub 浏览器上传限制，不需要 Git LFS。

**English:** Run the command above in the Git checkout. Extract the source ZIP and upload its inner contents to the repository root, including hidden configuration files. The ZIP reflects the current working tree, including uncommitted changes and new files. Local caches, application packages and test output are excluded. Alternatively, review and commit the current working tree before pushing. Do not upload only the source ZIP as the repository contents.

### 网页分两批上传 / Browser upload in two batches

当前源码共 124 个文件。GitHub 网页每次最多上传 100 个文件，单文件不超过 25 MiB，因此分两批提交。不要把整个工作目录直接拖入网页，网页上传不会按本机 `.gitignore` 自动筛掉缓存。

本次在 `releases/github-upload/` 准备了源码快照，排除了 `.git/`、`.tools/`、`releases/` 和测试产物：

1. 空仓库首页点击 **uploading an existing file**。打开 `01-source/`，把里面的全部内容拖入网页（52 个文件），提交说明填写 `Add project source`，点击页面底部的 **Commit changes**。
2. 返回仓库首页，点击 **Add file → Upload files**。打开 `02-source/`，把里面的 `.github/`、`src/`、`tests/` 三个文件夹拖进去（72 个文件），再提交一次。
3. 拖入的是两个批次文件夹里面的内容，不是 `01-source` / `02-source` 文件夹本身。完成后仓库根目录应直接看到 `README.md`、`src/`、`docs/` 等。

这两个目录是当前版本的上传快照。后续修改源码后，应重新生成源码 ZIP 或通过 Git 提交更新，不能继续使用旧快照。也可直接使用更新的源码 ZIP：解压后，先上传根目录文件以及 `assets/`、`docs/`、`licenses/`、`scripts/`、`third_party/`，提交后再上传 `.github/`、`src/`、`tests/`。

**English:** Browser uploads allow up to 100 files at once, with a 25 MiB limit per file. The prepared `releases/github-upload/01-source/` snapshot contains 52 files; `02-source/` contains `.github/`, `src/` and `tests/` (72 files). Upload the contents of each batch to the repository root in two commits. Do not upload the batch folder names, local `.git/`, caches or application packages. These are snapshots; use current source or Git for later updates.

## 2. 创建 Release / Create the release

先构建并检查：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -ExecutionPolicy Bypass -File tests/Test-ReleasePackages.ps1 -Edition All
```

创建 `v1.0.0` Release，发行说明使用 [Release-v1.0.0.md](Release-v1.0.0.md)。将 `releases/packages/` 中以下八个文件作为附件上传：

| 文件 / File | 用途 / Purpose |
| --- | --- |
| `ScreenLossless_v1.0.0_Win64_Standalone.exe` | 单文件直接运行 / Single executable, open directly |
| `ScreenLossless_v1.0.0_Win64_Standalone.exe.sha256` | 校验 / Checksum |
| `ScreenLossless_v1.0.0_Win64_Portable.zip` | 内置运行环境 / Runtime included |
| `ScreenLossless_v1.0.0_Win64_Portable.zip.sha256` | 校验 / Checksum |
| `ScreenLossless_v1.0.0_Win64_Compact.zip` | 需 .NET 8 Desktop Runtime x64 / Installed runtime required |
| `ScreenLossless_v1.0.0_Win64_Compact.zip.sha256` | 校验 / Checksum |
| `ScreenLossless_FFmpeg_9.0.2_Source.zip` | 与三个运行版本匹配的 FFmpeg 对应源码 / Matching FFmpeg source |
| `ScreenLossless_FFmpeg_9.0.2_Source.zip.sha256` | 校验 / Checksum |

GitHub 仓库右侧的 Releases → Create a new release，选择或新建 `v1.0.0` 标签，填写标题和发行说明，将上表文件拖入附件区后发布。发布后这些文件显示在 **Assets** 下，和截图中的下载区一样。普通用户选 Standalone EXE；不需要下载源码或 SHA256 校验文件才能运行。

应用源码 ZIP 是手动上传源码的便利副本；Release 会自动生成仓库源码下载，因此不要求再添加这个副本。自动生成的仓库源码 ZIP **不能替代**含实际构建记录的 FFmpeg 对应源码 ZIP。

**English:** Create the `v1.0.0` release and attach the eight files listed above. Use the linked bilingual release notes. Provide the matching FFmpeg source download beside the application links. GitHub's automatically generated source archives do not replace the separate FFmpeg corresponding-source bundle with its actual build record.

## 3. 发布后检查 / After publishing

- 确认中英文 README 可切换，运行包和对应源码的下载链接都能打开。
- 下载附件，检查 SHA256；Standalone 直接打开，Portable 解压启动，Compact 在安装 .NET 8 桌面运行环境后启动。
- 查看 Actions 的 FFmpeg 构建、Windows 媒体回归与发行包校验。云端结果不替代真实桌面及各型号显卡、声卡、多屏的实机验收。

**English:** Verify both README languages and all downloads, check the downloaded SHA256 values, and smoke-test all three editions. Review the GitHub Actions results. Hosted CI does not replace interactive desktop and hardware validation.

分发义务见 [FFmpeg-SOURCE.md](FFmpeg-SOURCE.md) 和 [第三方声明](THIRD-PARTY-NOTICES.md)。GitHub 文件限制参考 [GitHub 官方文档](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github)。
