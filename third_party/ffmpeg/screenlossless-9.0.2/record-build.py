from pathlib import Path
import hashlib, json, shutil, sys, tarfile, re
output, source = map(Path, sys.argv[1:3])
manifest = json.loads((source / "sources.lock.json").read_text())
headers = next(item for item in manifest["dependencies"] if item["name"] == "nv-codec-headers")
notices = []
with tarfile.open(source / "sources" / headers["file"]) as archive:
    for member in archive.getmembers():
        if member.isfile() and member.name.endswith(".h"):
            text = archive.extractfile(member).read().decode("utf-8", errors="replace")
            blocks = [block for block in re.findall(r"/\*[\s\S]*?\*/", text[:12000]) if "copyright" in block.lower() and "permission" in block.lower()]
            if blocks:
                notices.append(member.name + "\n" + "\n".join(blocks))
if not notices:
    raise RuntimeError("nv-codec header copyright notices not found")
(output / "licenses/nv-codec-headers-NOTICES.txt").write_text("\n\n".join(notices) + "\n", encoding="utf-8")
shutil.copyfile(source / "BtbN-MIT.txt", output / "licenses/BtbN-MIT.txt")
target = output / "ffmpeg-source"
shutil.copytree(source, target, dirs_exist_ok=True, ignore=shutil.ignore_patterns("output"))
shutil.copytree(output / "licenses", target / "licenses", dirs_exist_ok=True)
for name in ("config.mak", "config.log", "toolchain-packages.txt"):
    shutil.copyfile(output / name, target / ("build-" + name if name.startswith("config.") else name))
record = {"build": "FFmpeg 9.0.2 ScreenLossless Windows x64", "license": "GPL-3.0-or-later", "sourceCompleteForThisBuild": True, "sources": json.loads((source / "sources.lock.json").read_text()), "binaries": {}}
for name in ("ffmpeg.exe", "ffprobe.exe"):
    with (output / name).open("rb") as stream:
        record["binaries"][name] = {"sha256": hashlib.file_digest(stream, "sha256").hexdigest(), "bytes": (output / name).stat().st_size}
(target / "BUILD-RECORD.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
shutil.copyfile(source / "BtbN-MIT.txt", output / "licenses/BtbN-MIT.txt")
print("Corresponding sources, licenses and binary hashes exported.")
