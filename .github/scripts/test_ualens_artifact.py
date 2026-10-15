# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
# See http://opcfoundation.org/License/MIT/1.00/.

"""Validate artifact acceptance without launching a desktop process."""

from contextlib import redirect_stderr, redirect_stdout
import importlib.util
from io import StringIO
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zlib


REPOSITORY = Path(__file__).resolve().parents[2]
sys.dont_write_bytecode = True
SPEC = importlib.util.spec_from_file_location(
    "ualens_artifact", REPOSITORY / "tools" / "Opc.Ua.Lens" / "validate-artifact.py"
)
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
DIAGNOSTIC_SPEC = importlib.util.spec_from_file_location(
    "ualens_macos_diagnostic", Path(__file__).with_name("diagnose_ualens_macos.py")
)
DIAGNOSTIC = importlib.util.module_from_spec(DIAGNOSTIC_SPEC)
DIAGNOSTIC_SPEC.loader.exec_module(DIAGNOSTIC)


class ArtifactValidationTests(unittest.TestCase):
    def invoke(self, stdout, *, native=False, software_gl=False, x11_vulkan=False, exit_code=0,
               images=None, stale_images=None, error=None):
        with tempfile.TemporaryDirectory(prefix="ualens-artifact-guard-") as directory:
            root = Path(directory)
            executable = root / "artifact"
            executable.write_bytes(b"Not an executable: subprocess is mocked.")
            logs = root / "logs"
            logs.mkdir()
            for name, image in (stale_images or {}).items():
                (logs / name).write_bytes(image)
            arguments = [
                "validate-artifact.py", "--executable", str(executable),
                "--log-directory", str(logs),
            ]
            if native:
                arguments.append("--native-viewport")
            if software_gl:
                arguments.append("--allow-software-gl")
            if x11_vulkan:
                arguments.append("--x11-vulkan")
            result = subprocess.CompletedProcess([], exit_code, stdout, b"")

            def run(*_arguments, **_keywords):
                if error is not None:
                    raise error
                for name, image in (images or {}).items():
                    (logs / name).write_bytes(image)
                return result

            with (
                patch.object(sys, "argv", arguments),
                patch.dict(RUNNER.os.environ, {"DISPLAY": ":guard-only"}),
                patch.object(RUNNER.subprocess, "run", side_effect=run) as launch,
                redirect_stdout(StringIO()),
                redirect_stderr(StringIO()),
            ):
                code = RUNNER.main()
            summary = json.loads((logs / "result.json").read_text(encoding="utf-8"))
            return code, summary, launch.call_args

    def test_default_mode_preserves_desktop_acceptance(self):
        code, summary, launch = self.invoke(b"UALENS_DESKTOP_SMOKE_PASS\n")
        self.assertEqual(code, 0)
        self.assertTrue(summary["passed"])
        self.assertEqual(launch.args[0][1:], ["--smoke-test"])
        self.assertFalse(summary["allowSoftwareGl"])
        self.assertFalse(summary["x11Vulkan"])

    def test_vulkan_compositor_requires_explicit_selection(self):
        code, summary, launch = self.invoke(
            b"UALENS_DESKTOP_SMOKE_PASS\n", x11_vulkan=True
        )
        self.assertEqual(code, 0)
        self.assertTrue(summary["x11Vulkan"])
        self.assertFalse(summary["allowSoftwareGl"])
        self.assertEqual(launch.args[0][1:], ["--smoke-test", "--x11-vulkan"])

    def test_software_gl_requires_an_explicit_opt_in(self):
        code, summary, launch = self.invoke(
            b"UALENS_DESKTOP_SMOKE_PASS\n", software_gl=True
        )
        self.assertEqual(code, 0)
        self.assertTrue(summary["allowSoftwareGl"])
        self.assertEqual(launch.args[0][1:], ["--smoke-test", "--allow-software-gl"])

    def test_software_gl_does_not_replace_native_image_evidence(self):
        code, summary, _ = self.invoke(
            b"UALENS_DESKTOP_SMOKE_PASS\nUALENS_NATIVE_VIEWPORT_SMOKE_PASS\n",
            native=True, software_gl=True,
        )
        self.assertEqual(code, 1)
        self.assertFalse(summary["passed"])
        self.assertFalse(summary["nativeEvidence"]["passed"])

    def test_native_mode_rejects_startup_only(self):
        code, summary, launch = self.invoke(
            b"UALENS_DESKTOP_SMOKE_PASS\n", native=True
        )
        self.assertEqual(code, 1)
        self.assertFalse(summary["passed"])
        self.assertIn("--smoke-native-viewport", launch.args[0])

    def test_native_mode_requires_image_evidence_even_with_both_markers(self):
        code, summary, _ = self.invoke(
            b"UALENS_DESKTOP_SMOKE_PASS\nUALENS_NATIVE_VIEWPORT_SMOKE_PASS\n",
            native=True,
        )
        self.assertEqual(code, 1)
        self.assertFalse(summary["passed"])

    @staticmethod
    def png(color, compression=6):
        def chunk(kind, data):
            return (
                struct.pack(">I", len(data)) + kind + data
                + struct.pack(">I", zlib.crc32(kind + data))
            )

        header = struct.pack(">IIBBBBB", 2, 1, 8, 6, 0, 0, 0)
        pixels = b"\0" + bytes(color) + b"\0\0\0\xff"
        return (
            b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(pixels, compression)) + chunk(b"IEND", b"")
        )

    def test_native_mode_accepts_fresh_distinct_images_and_drained_markers(self):
        code, summary, _ = self.invoke(
            b"UALENS_DESKTOP_SMOKE_PASS\nUALENS_NATIVE_VIEWPORT_SMOKE_PASS\n",
            native=True,
            images={
                "native-before.png": self.png((255, 0, 0, 255)),
                "native-after.png": self.png((0, 255, 0, 255)),
            },
        )
        self.assertEqual(code, 0)
        self.assertTrue(summary["nativeEvidence"]["passed"])
        self.assertEqual(
            [(frame["width"], frame["height"]) for frame in summary["nativeEvidence"]["frames"]],
            [(2, 1), (2, 1)],
        )

    def test_native_mode_rejects_corrupted_png_data(self):
        corrupted = bytearray(self.png((255, 0, 0, 255)))
        corrupted[44] ^= 1
        code, summary, _ = self.invoke(
            b"UALENS_DESKTOP_SMOKE_PASS\nUALENS_NATIVE_VIEWPORT_SMOKE_PASS\n",
            native=True,
            images={
                "native-before.png": bytes(corrupted),
                "native-after.png": self.png((0, 255, 0, 255)),
            },
        )
        self.assertEqual(code, 1)
        self.assertFalse(summary["nativeEvidence"]["passed"])

    def test_native_mode_rejects_same_pixels_with_different_compression(self):
        code, summary, _ = self.invoke(
            b"UALENS_DESKTOP_SMOKE_PASS\nUALENS_NATIVE_VIEWPORT_SMOKE_PASS\n",
            native=True,
            images={
                "native-before.png": self.png((255, 0, 0, 255), 1),
                "native-after.png": self.png((255, 0, 0, 255), 9),
            },
        )
        self.assertEqual(code, 1)
        self.assertIn("different image", summary["nativeEvidence"]["error"])

    def test_native_mode_rejects_a_uniform_capture(self):
        code, summary, _ = self.invoke(
            b"UALENS_DESKTOP_SMOKE_PASS\nUALENS_NATIVE_VIEWPORT_SMOKE_PASS\n",
            native=True,
            images={
                "native-before.png": self.png((0, 0, 0, 255)),
                "native-after.png": self.png((0, 255, 0, 255)),
            },
        )
        self.assertEqual(code, 1)
        self.assertIn("uniform image", summary["nativeEvidence"]["error"])

    def test_native_mode_rejects_images_left_by_a_previous_run(self):
        code, summary, _ = self.invoke(
            b"UALENS_DESKTOP_SMOKE_PASS\nUALENS_NATIVE_VIEWPORT_SMOKE_PASS\n",
            native=True,
            stale_images={
                "native-before.png": self.png((255, 0, 0, 255)),
                "native-after.png": self.png((0, 255, 0, 255)),
            },
        )
        self.assertEqual(code, 1)
        self.assertIn("Missing regular", summary["nativeEvidence"]["error"])

    def test_native_mode_rejects_failed_shutdown_despite_rendered_images(self):
        code, summary, _ = self.invoke(
            b"UALENS_DESKTOP_SMOKE_PASS\nUALENS_NATIVE_VIEWPORT_SMOKE_PASS\n",
            native=True,
            exit_code=1,
            images={
                "native-before.png": self.png((255, 0, 0, 255)),
                "native-after.png": self.png((0, 255, 0, 255)),
            },
        )
        self.assertEqual(code, 1)
        self.assertFalse(summary["passed"])
        self.assertTrue(summary["nativeEvidence"]["passed"])

    def test_timeout_is_a_failure_even_when_a_marker_was_written(self):
        code, summary, _ = self.invoke(
            b"",
            error=subprocess.TimeoutExpired(
                ["artifact"], 90, output=b"UALENS_DESKTOP_SMOKE_PASS\n"
            ),
        )
        self.assertEqual(code, 1)
        self.assertTrue(summary["timedOut"])
        self.assertIsNone(summary["exitCode"])

    def test_crash_collection_matches_the_exact_executable(self):
        with tempfile.TemporaryDirectory(prefix="ualens-crash-guard-") as directory:
            root = Path(directory)
            executable = root / "UaLens"
            crash = root / "owned.ips"
            crash.write_text(
                json.dumps({"app_name": "UaLens"}) + "\n"
                + json.dumps({"procPath": str(executable), "pid": 123}),
                encoding="utf-8",
            )
            self.assertTrue(DIAGNOSTIC.owned_ips(crash, executable))
            self.assertFalse(DIAGNOSTIC.owned_ips(crash, root / "another" / "UaLens"))

    def test_crash_collection_rejects_non_ips_text(self):
        with tempfile.TemporaryDirectory(prefix="ualens-crash-guard-") as directory:
            root = Path(directory)
            crash = root / "invalid.ips"
            crash.write_text("Not an IPS JSON header\nNot a JSON body", encoding="utf-8")
            with self.assertRaises(ValueError):
                DIAGNOSTIC.owned_ips(crash, root / "UaLens")


if __name__ == "__main__":
    unittest.main()
