"""Fetch and verify the exact FFmpeg sources used by ScreenLossless. Python 3.9+."""
from pathlib import Path
import argparse, concurrent.futures, gzip, hashlib, io, json, shutil, tarfile, tempfile, urllib.request

ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOT = ROOT / "third_party/ffmpeg/screenlossless-9.0.2"

def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest() if hasattr(hashlib, "file_digest") else hashlib.sha256(stream.read()).hexdigest()

def prepare(entry, verify_only):
    target = SOURCE_ROOT / "sources" / entry["file"]
    if target.is_file() and digest(target) == entry["sha256"]:
        return "Verified " + entry["name"]
    if verify_only:
        raise RuntimeError("Missing or modified source: " + str(target))
    target.parent.mkdir(parents=True, exist_ok=True)
    # TemporaryDirectory removes only its own temporary download, never the user's source tree.
    with tempfile.TemporaryDirectory(prefix="screenlossless-source-") as tmp:
        downloaded = Path(tmp) / "upstream.tar"
        request = urllib.request.Request(entry["url"], headers={"User-Agent": "ScreenLossless-source-fetcher"})
        with urllib.request.urlopen(request, timeout=300) as response, downloaded.open("wb") as output:
            shutil.copyfileobj(response, output)
        expected = entry.get("upstreamArchiveSha256", entry["sha256"])
        if digest(downloaded) != expected:
            raise RuntimeError("Upstream checksum mismatch: " + entry["name"])
        if entry.get("sourceSubset"):
            records = []
            with tarfile.open(downloaded) as archive:
                for member in archive.getmembers():
                    if not member.isfile():
                        continue
                    relative = "/".join(member.name.split("/")[1:])
                    if relative.startswith("amf/public/include/") or ("/" not in relative and any(term in relative.lower() for term in ("license", "copyright", "notice"))):
                        records.append((member.name, archive.extractfile(member).read()))
            normalized = Path(tmp) / "headers.tar.gz"
            with normalized.open("wb") as raw, gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as compressed, tarfile.open(fileobj=compressed, mode="w") as archive:
                for name, data in sorted(records):
                    member = tarfile.TarInfo(name)
                    member.size, member.mode, member.mtime = len(data), 0o644, 0
                    archive.addfile(member, io.BytesIO(data))
            downloaded = normalized
        if digest(downloaded) != entry["sha256"]:
            raise RuntimeError("Prepared source checksum mismatch: " + entry["name"])
        shutil.copyfile(downloaded, target)
    return "Downloaded and verified " + entry["name"]

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()
    manifest = json.loads((SOURCE_ROOT / "sources.lock.json").read_text("utf-8"))
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        for message in pool.map(lambda entry: prepare(entry, args.verify_only), manifest["dependencies"]):
            print(message)
