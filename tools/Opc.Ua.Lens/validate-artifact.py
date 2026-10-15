#!/usr/bin/env python3
# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
# See http://opcfoundation.org/License/MIT/1.00/.

"""Launch a shipped UaLens artifact on a real desktop with bounded lifetime.

Uses only the Python standard library. On Linux, run this under xvfb-run or
provide an existing DISPLAY. This does not build or reference the test project.
The optional native viewport gate also requires fresh, valid, changed PNGs.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import zlib


def native_png_evidence(image):
    if not image.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError("Native image artifact is not a PNG.")
    chunks = []
    offset = 8
    while offset < len(image):
        if len(chunks) == 3:
            raise ValueError("Native PNG contains unexpected trailing data.")
        if offset + 12 > len(image):
            raise ValueError("Native PNG has a truncated chunk.")
        length = int.from_bytes(image[offset:offset + 4], "big")
        end = offset + 8 + length
        if end + 4 > len(image):
            raise ValueError("Native PNG chunk exceeds the image bounds.")
        kind = image[offset + 4:offset + 8]
        data = image[offset + 8:end]
        crc = int.from_bytes(image[end:end + 4], "big")
        if zlib.crc32(kind + data) != crc:
            raise ValueError("Native PNG checksum is invalid.")
        chunks.append((kind, data))
        offset = end + 4
    if [kind for kind, _ in chunks] != [b"IHDR", b"IDAT", b"IEND"] or chunks[2][1]:
        raise ValueError("Native PNG does not match the capture encoder's chunk contract.")
    header = chunks[0][1]
    if len(header) != 13 or header[8:] != b"\x08\x06\0\0\0":
        raise ValueError("Native PNG must be non-interlaced RGBA8.")
    width = int.from_bytes(header[:4], "big")
    height = int.from_bytes(header[4:8], "big")
    if not 1 <= width <= 1024 or not 1 <= height <= 1024:
        raise ValueError("Native image dimensions are outside the capture budget.")
    stride = width * 4 + 1
    expected = stride * height
    decoder = zlib.decompressobj()
    pixels = decoder.decompress(chunks[1][1], expected + 1)
    if len(pixels) != expected or not decoder.eof or decoder.unused_data or decoder.unconsumed_tail:
        raise ValueError("Native PNG pixel storage does not match its dimensions.")
    if any(pixels[row * stride] != 0 for row in range(height)):
        raise ValueError("Native PNG does not match the capture encoder's row-filter contract.")
    first = pixels[1:5]
    if not any(
        pixels[row * stride + 1 + column * 4:row * stride + 5 + column * 4] != first
        for row in range(height) for column in range(width)
    ):
        raise ValueError("Native capture contains only a uniform image.")
    return width, height, hashlib.sha256(pixels).hexdigest()


def native_image_evidence(logs):
    frames = []
    try:
        for name in ("native-before.png", "native-after.png"):
            path = logs / name
            if path.is_symlink() or not path.is_file():
                raise ValueError(f"Missing regular native image artifact: {name}")
            if not 33 < path.stat().st_size <= 4 * 1024 * 1024 + 16384:
                raise ValueError(f"Native image artifact is outside its byte budget: {name}")
            with path.open("rb") as stream:
                image = stream.read(4 * 1024 * 1024 + 16385)
            if len(image) > 4 * 1024 * 1024 + 16384:
                raise ValueError(f"Native image artifact grew beyond its byte budget: {name}")
            width, height, pixel_hash = native_png_evidence(image)
            frames.append({
                "file": name, "width": width, "height": height,
                "sha256": hashlib.sha256(image).hexdigest(),
                "pixelSha256": pixel_hash,
            })
        if frames[0]["pixelSha256"] == frames[1]["pixelSha256"]:
            raise ValueError("The native stage update did not produce a different image.")
        if (frames[0]["width"], frames[0]["height"]) != (frames[1]["width"], frames[1]["height"]):
            raise ValueError("The native stage update unexpectedly changed capture dimensions.")
    except (OSError, ValueError, zlib.error) as error:
        return {"passed": False, "error": str(error), "frames": frames}
    return {"passed": True, "frames": frames}


def artifact_environment(home):
    environment = os.environ.copy()
    if "XAUTHORITY" not in environment:
        authority = Path.home() / ".Xauthority"
        if authority.is_file():
            environment["XAUTHORITY"] = str(authority)
    # Preserve display authorization and DOTNET_ROOT, not a developer's user state.
    for variable, child in {
        "HOME": "home",
        "USERPROFILE": "home",
        "APPDATA": "roaming",
        "LOCALAPPDATA": "local",
        "XDG_CONFIG_HOME": "config",
        "XDG_DATA_HOME": "data",
        "XDG_CACHE_HOME": "cache",
        "DOTNET_CLI_HOME": "dotnet",
    }.items():
        path = home / child
        path.mkdir(exist_ok=True)
        environment[variable] = str(path)
    environment["DOTNET_NOLOGO"] = "true"
    return environment


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", required=True, type=Path)
    parser.add_argument("--log-directory", required=True, type=Path)
    parser.add_argument("--timeout-seconds", default=90, type=int)
    parser.add_argument("--native-viewport", action="store_true")
    parser.add_argument("--allow-software-gl", action="store_true")
    parser.add_argument("--x11-vulkan", action="store_true")
    args = parser.parse_args()
    executable = args.executable.resolve(strict=True)
    logs = args.log_directory.resolve()
    logs.mkdir(parents=True, exist_ok=True)
    if args.timeout_seconds <= 0:
        parser.error("--timeout-seconds must be positive")
    if sys.platform.startswith("linux") and not os.environ.get("DISPLAY"):
        parser.error("A real X11 display is required; use xvfb-run for CI.")
    if args.native_viewport:
        for name in ("native-before.png", "native-after.png"):
            (logs / name).unlink(missing_ok=True)

    with tempfile.TemporaryDirectory(prefix="ualens-artifact-") as directory:
        home = Path(directory)
        environment = artifact_environment(home)
        command = [str(executable), "--smoke-test"]
        if args.allow_software_gl:
            command.append("--allow-software-gl")
        if args.x11_vulkan:
            command.append("--x11-vulkan")
        if args.native_viewport:
            command.extend([
                "--smoke-native-viewport", "--smoke-artifact-directory", str(logs)
            ])

        try:
            result = subprocess.run(
                command,
                cwd=directory,
                env=environment,
                capture_output=True,
                timeout=args.timeout_seconds,
                check=False,
            )
            stdout, stderr = result.stdout, result.stderr
            exit_code = result.returncode
            timed_out = False
        except subprocess.TimeoutExpired as error:
            stdout, stderr = error.stdout or b"", error.stderr or b""
            exit_code = None
            timed_out = True
        except OSError as error:
            stdout, stderr = b"", str(error).encode("utf-8")
            exit_code = None
            timed_out = False

    (logs / "stdout.txt").write_bytes(stdout)
    (logs / "stderr.txt").write_bytes(stderr)
    output = stdout.decode("utf-8", errors="replace")
    required_markers = ["UALENS_DESKTOP_SMOKE_PASS"]
    if args.native_viewport:
        required_markers.append("UALENS_NATIVE_VIEWPORT_SMOKE_PASS")
    passed = (
        not timed_out
        and exit_code == 0
        and all(marker in output.splitlines() for marker in required_markers)
    )
    native_evidence = native_image_evidence(logs) if args.native_viewport else None
    if native_evidence is not None:
        passed = passed and native_evidence["passed"]
    summary = {
        "executable": str(executable),
        "timeoutSeconds": args.timeout_seconds,
        "timedOut": timed_out,
        "exitCode": exit_code,
        "nativeViewport": args.native_viewport,
        "allowSoftwareGl": args.allow_software_gl,
        "x11Vulkan": args.x11_vulkan,
        "nativeEvidence": native_evidence,
        "passed": passed,
    }
    (logs / "result.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(output, end="")
    print(stderr.decode("utf-8", errors="replace"), end="", file=sys.stderr)
    print(json.dumps(summary))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
