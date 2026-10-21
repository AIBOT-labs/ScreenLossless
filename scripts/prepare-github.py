"""Verify and package the current Git working tree for GitHub (Python 3.9+)."""
from pathlib import Path, PurePosixPath
import argparse
import hashlib
import json
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
BINARY_SUFFIXES = (".png", ".ico", ".tar.gz", ".tar.xz")
PRIVATE_PATTERNS = {
    "private key": r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
    "GitHub token": r"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{50,})\b",
    "OpenAI key": r"\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{40,}\b",
    "AWS key": r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b",
}

def git(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args])

def upload_files():
    if Path(git("rev-parse", "--show-toplevel").decode().strip()).resolve() != ROOT:
        raise RuntimeError("Run this script from a ScreenLossless Git checkout.")
    ignored = git("ls-files", "-ci", "--exclude-standard").decode().strip()
    if ignored:
        raise RuntimeError("Ignored files are still tracked; review the Git index before publishing.")
    # git archive HEAD would omit uncommitted fixes and newly added source files.
    names = git("ls-files", "-z", "--cached", "--others", "--exclude-standard").decode("utf-8").split("\0")
    files = sorted({name for name in names if name and (ROOT / name).is_file()})
    if not files:
        raise RuntimeError("The source file list is empty.")
    for name in files:
        parts = PurePosixPath(name).parts
        if any(part in {".git", ".tools", "releases", "artifacts", "bin", "obj", "__pycache__"} for part in parts):
            raise RuntimeError("Generated/private directory in source list: " + name)
        if name.lower().endswith((".exe", ".dll", ".pdb", ".log", ".zip")) or name.lower().startswith(".env"):
            raise RuntimeError("Unexpected generated/private file in source list: " + name)
        path = ROOT / name
        if not path.resolve().is_relative_to(ROOT) or path.is_symlink():
            raise RuntimeError("Source file resolves outside the project or is a link: " + name)
        if path.stat().st_size >= 25 * 1024 * 1024:
            raise RuntimeError("Source file exceeds GitHub browser-upload size: " + name)
    return files

def verify(files):
    available = set(files)
    version = ET.parse(ROOT / "src/ScreenLossless.csproj").findtext("./PropertyGroup/Version")
    required = {"LICENSE", "README.md", "README.en.md", ".gitignore", ".gitattributes",
                ".github/workflows/windows-build.yml", "src/Program.cs", "src/Localization/en.json",
                "assets/ScreenLossless.ico", "docs/FFmpeg-SOURCE.md", "docs/THIRD-PARTY-NOTICES.md"}
    if not required <= available:
        raise RuntimeError("Missing required source files: " + ", ".join(sorted(required - available)))
    problems = []
    for name in files:
        if name.lower().endswith(BINARY_SUFFIXES):
            continue
        try:
            text = (ROOT / name).read_text("utf-8-sig")
        except UnicodeDecodeError:
            raise RuntimeError("Unexpected binary source file: " + name)
        for kind, pattern in PRIVATE_PATTERNS.items():
            if re.search(pattern, text):
                problems.append(kind + " in " + name)  # Do not print credential values.
        if name.endswith(".md"):
            for target in re.findall(r"!?\[[^\]]*\]\(([^\s)]+)(?:\s+[^)]*)?\)", text):
                if re.match(r"(?:[a-zA-Z][a-zA-Z0-9+.-]*:|#)", target):
                    continue
                path = (ROOT / name).parent / target.split("#", 1)[0]
                try:
                    relative = path.resolve().relative_to(ROOT).as_posix()
                except ValueError:
                    relative = ""
                if relative not in available:
                    problems.append("Broken/unpackaged link in " + name + ": " + target)
    source_root = ROOT / "third_party/ffmpeg/screenlossless-9.0.2"
    lock = json.loads((source_root / "sources.lock.json").read_text("utf-8"))
    for dependency in lock["dependencies"]:
        path = source_root / "sources" / dependency["file"]
        if path.relative_to(ROOT).as_posix() not in available or sha256(path) != dependency["sha256"]:
            problems.append("Missing/modified corresponding source: " + dependency["name"])
    if problems:
        raise RuntimeError("\n".join(problems))
    print(f"PASS {len(files)} source files, local links, common secret patterns and pinned FFmpeg sources")
    return version

def sha256(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()

def package(files, version):
    output = ROOT / "releases/packages"
    output.mkdir(parents=True, exist_ok=True)
    target = output / f"ScreenLossless_v{version}_Source.zip"
    hashes = {name: sha256(ROOT / name) for name in files}
    # A temporary archive leaves the last valid package intact if verification fails.
    with tempfile.TemporaryDirectory(prefix="source-package-", dir=output) as temporary:
        archive = Path(temporary) / target.name
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
            for name in files:
                bundle.write(ROOT / name, "ScreenLossless/" + name)
        with zipfile.ZipFile(archive) as bundle:
            if bundle.testzip() is not None or len(bundle.namelist()) != len(files):
                raise RuntimeError("Source ZIP integrity/file-count check failed.")
            for name, expected in hashes.items():
                if hashlib.sha256(bundle.read("ScreenLossless/" + name)).hexdigest() != expected:
                    raise RuntimeError("Source ZIP differs from the working tree: " + name)
        archive.replace(target)
    (output / (target.name + ".sha256")).write_text(sha256(target) + "  " + target.name + "\n", "ascii")
    print(f"PASS source ZIP matches the working tree: {target.name} ({target.stat().st_size / 1048576:.2f} MiB)")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify-only", action="store_true", help="check without generating a ZIP")
    options = parser.parse_args()
    try:
        selected = upload_files()
        release_version = verify(selected)
        if not options.verify_only:
            package(selected, release_version)
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print("FAIL: " + str(error), file=sys.stderr)
        sys.exit(1)
