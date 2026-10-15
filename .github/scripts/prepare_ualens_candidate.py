# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
# See http://opcfoundation.org/License/MIT/1.00/.

"""Stage immutable OpenUSD candidate inputs without changing production pins."""

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile


REPOSITORY = Path(__file__).resolve().parents[2]
SOURCE_REPOSITORY = "marcschier/openusd-dotnet"
RIDS = {"win-x64", "linux-x64", "osx-arm64"}
OPENUSD_PACKAGES = (
    "OpenUsd", "OpenUsd.Viewer", "OpenUsd.Rendering", "OpenUsd.Rendering.Silk",
    "OpenUsd.Rendering.Silk.D3D12", "OpenUsd.Rendering.Silk.Vulkan",
    "OpenUsd.Runtime.Core", "OpenUsd.Runtime.Imaging",
)
AVALONIA_PACKAGES = (
    "Avalonia", "Avalonia.Desktop", "Avalonia.Fonts.Inter", "Avalonia.Themes.Fluent",
)
MAX_ARCHIVE_BYTES = 512 * 1024 * 1024
MAX_NUSPEC_BYTES = 2 * 1024 * 1024


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def read_manifest(path):
    require(path.stat().st_size <= 64 * 1024, "Candidate manifest exceeds 64 KiB.")
    manifest = json.loads(path.read_text(encoding="utf-8"))
    require(manifest.get("schemaVersion") == 1, "Unsupported candidate manifest schema.")
    require(manifest.get("repository") == SOURCE_REPOSITORY, "Unexpected candidate repository.")
    require(re.fullmatch(r"[0-9a-f]{40}", manifest.get("sourceSha", "")), "Invalid source SHA.")
    require(
        re.fullmatch(r"[0-9a-f]{64}", manifest.get("sourceManifestSha256", "")),
        "Invalid original provenance-manifest SHA256.",
    )
    for name in ("version", "avaloniaVersion"):
        require(
            re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?", manifest.get(name, "")),
            f"Invalid {name}.",
        )
    for name in ("packageRunId", "nativeProducerRunId"):
        require(type(manifest.get(name)) is int and manifest[name] > 0, f"Invalid {name}.")
    artifacts = manifest.get("artifacts", [])
    require(len(artifacts) == 3 and {item["rid"] for item in artifacts} == RIDS, "All three RIDs are required.")
    require(len({item["id"] for item in artifacts}) == 3, "Duplicate candidate artifact ID.")
    for artifact in artifacts:
        require(type(artifact["id"]) is int and artifact["id"] > 0, "Invalid artifact ID.")
        require(
            artifact["name"] == f"openusd-viewer-input-nupkg-{artifact['rid']}",
            "Unexpected candidate artifact name.",
        )
    packages = manifest.get("packages", [])
    require(1 <= len(packages) <= 64, "Candidate must contain 1-64 package identities.")
    require(len({item["id"].lower() for item in packages}) == len(packages), "Duplicate candidate package ID.")
    for package in packages:
        require(re.fullmatch(r"OpenUsd(?:\.[A-Za-z0-9-]+)*", package["id"]), "Unexpected package ID.")
        require(
            package["file"] == f"{package['id']}.{manifest['version']}.nupkg",
            "Candidate filename must match its package identity.",
        )
        require(
            package["repositoryUrl"] == f"https://github.com/{SOURCE_REPOSITORY}"
            or (package["id"] == "OpenUsd.Viewer" and package["repositoryUrl"] is None),
            "Unexpected package source URL.",
        )
    required = set(OPENUSD_PACKAGES) | {"OpenUsd.Rendering.Silk.Metal"}
    required.update(f"OpenUsd.Runtime.{kind}.{rid}" for kind in ("Core", "Imaging") for rid in RIDS)
    require(required <= {item["id"] for item in packages}, "Candidate is missing required managed or RID packages.")
    for item in [*artifacts, *packages]:
        require(re.fullmatch(r"[0-9a-f]{64}", item["sha256"]), "Invalid candidate SHA256.")
        require(type(item["bytes"]) is int and 0 < item["bytes"] <= MAX_ARCHIVE_BYTES, "Invalid candidate size.")
    return manifest


def github_json(endpoint):
    result = subprocess.run(
        ["gh", "api", endpoint], stdout=subprocess.PIPE, text=True, check=True, timeout=60,
    )
    return json.loads(result.stdout)


def verify_run(run, run_id, source_sha):
    require(
        run["id"] == run_id
        and run["repository"]["full_name"] == SOURCE_REPOSITORY
        and run["head_repository"]["full_name"] == SOURCE_REPOSITORY
        and run["head_sha"] == source_sha,
        "Candidate run does not match the expected repository and source.",
    )


def verify_artifact(metadata, expected, manifest):
    require(
        metadata["id"] == expected["id"]
        and metadata["name"] == expected["name"]
        and metadata["digest"] == f"sha256:{expected['sha256']}"
        and metadata["size_in_bytes"] == expected["bytes"]
        and metadata["expired"] is False
        and metadata["workflow_run"]["id"] == manifest["packageRunId"]
        and metadata["workflow_run"]["head_sha"] == manifest["sourceSha"],
        "Candidate artifact identity, source, digest, size, or availability differs.",
    )


def verify_package(path, expected, manifest):
    require(path.stat().st_size == expected["bytes"], f"Wrong package size: {path.name}")
    require(sha256(path) == expected["sha256"], f"Wrong package SHA256: {path.name}")
    with zipfile.ZipFile(path) as archive:
        specs = [item for item in archive.infolist() if "/" not in item.filename and item.filename.endswith(".nuspec")]
        require(len(specs) == 1 and specs[0].file_size <= MAX_NUSPEC_BYTES, f"Invalid nuspec: {path.name}")
        metadata = ET.fromstring(archive.read(specs[0])).find("{*}metadata")
        require(metadata is not None, f"Missing package metadata: {path.name}")
        source = metadata.find("{*}repository")
        require(
            metadata.findtext("{*}id") == expected["id"]
            and metadata.findtext("{*}version") == manifest["version"]
            and source is not None
            and source.get("commit") == manifest["sourceSha"]
            and source.get("url") == expected["repositoryUrl"],
            f"Wrong package identity or source: {path.name}",
        )


def stage_archive(path, artifact, manifest, feed):
    require(path.stat().st_size == artifact["bytes"], "Downloaded artifact has the wrong size.")
    require(sha256(path) == artifact["sha256"], "Downloaded artifact has the wrong SHA256.")
    expected = {item["file"]: item for item in manifest["packages"]}
    staged = set()
    with zipfile.ZipFile(path) as archive:
        for member in archive.infolist():
            if not member.filename.endswith(".nupkg"):
                continue
            # ZipInfo normalizes backslashes on Windows; inspect the original archive name.
            name = PurePosixPath(member.orig_filename)
            require(
                not name.is_absolute() and ".." not in name.parts and "\\" not in member.orig_filename
                and stat.S_IFMT(member.external_attr >> 16) in (0, stat.S_IFREG),
                "Unsafe candidate package archive entry.",
            )
            require(name.name in expected and name.name not in staged, "Unexpected or duplicate archived package.")
            package = expected[name.name]
            require(member.file_size == package["bytes"], f"Wrong archived package size: {name.name}")
            target = feed / name.name
            if not target.exists():
                with archive.open(member) as source, target.open("xb") as output:
                    shutil.copyfileobj(source, output, 1024 * 1024)
            else:
                with archive.open(member) as source:
                    digest = hashlib.sha256()
                    for block in iter(lambda: source.read(1024 * 1024), b""):
                        digest.update(block)
                    require(digest.hexdigest() == package["sha256"], "Conflicting duplicate candidate bytes.")
            verify_package(target, package, manifest)
            staged.add(name.name)
    return staged


def write_overrides(root, feed, manifest, repository=REPOSITORY):
    props = ET.Element("Project")
    ET.SubElement(props, "Import", Project=str(repository / "Directory.Packages.props"))
    items = ET.SubElement(props, "ItemGroup")
    for package in OPENUSD_PACKAGES:
        ET.SubElement(items, "PackageVersion", Update=package, Version=manifest["version"])
    for package in AVALONIA_PACKAGES:
        ET.SubElement(items, "PackageVersion", Update=package, Version=manifest["avaloniaVersion"])
    props_path = root / "Directory.Packages.props"
    ET.ElementTree(props).write(props_path, encoding="utf-8", xml_declaration=True)

    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="candidate", value=str(feed))
    ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
    mapping = ET.SubElement(config, "packageSourceMapping")
    ET.SubElement(mapping, "clear")
    candidate = ET.SubElement(mapping, "packageSource", key="candidate")
    ET.SubElement(candidate, "package", pattern="OpenUsd*")
    public = ET.SubElement(mapping, "packageSource", key="nuget.org")
    ET.SubElement(public, "package", pattern="*")
    audit = ET.SubElement(config, "auditSources")
    ET.SubElement(audit, "clear")
    ET.SubElement(audit, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
    config_path = root / "NuGet.config"
    ET.ElementTree(config).write(config_path, encoding="utf-8", xml_declaration=True)
    return {
        "DirectoryPackagesPropsPath": str(props_path),
        "RestoreConfigFile": str(config_path),
        "RestorePackagesPath": str(root / "packages"),
        "NUGET_PACKAGES": str(root / "packages"),
        "NUGET_HTTP_CACHE_PATH": str(root / "http-cache"),
    }


def prepare(manifest_path, root, evidence, github_env=None):
    manifest = read_manifest(manifest_path)
    root.mkdir(parents=True, exist_ok=False)
    feed = root / "feed"
    feed.mkdir()
    for run_id in (manifest["packageRunId"], manifest["nativeProducerRunId"]):
        verify_run(
            github_json(f"repos/{SOURCE_REPOSITORY}/actions/runs/{run_id}"),
            run_id, manifest["sourceSha"],
        )
    staged = set()
    for artifact in manifest["artifacts"]:
        endpoint = f"repos/{SOURCE_REPOSITORY}/actions/artifacts/{artifact['id']}"
        verify_artifact(github_json(endpoint), artifact, manifest)
        archive = root / f"{artifact['id']}.zip"
        with archive.open("xb") as output:
            subprocess.run(["gh", "api", endpoint + "/zip"], stdout=output, check=True, timeout=300)
        staged.update(stage_archive(archive, artifact, manifest, feed))
        archive.unlink()
    require(staged == {item["file"] for item in manifest["packages"]}, "Candidate feed is incomplete.")
    environment = write_overrides(root, feed, manifest)
    consumer_sha = subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=REPOSITORY, capture_output=True, text=True, check=True, timeout=30,
    ).stdout.strip()
    record = {
        "acceptance": "Unpublished compatibility inputs only; not production or release qualification.",
        "consumerSourceSha": consumer_sha,
        "inputManifestSha256": sha256(manifest_path),
        "candidate": manifest,
        "environment": environment,
        "verifiedPackageCount": len(staged),
        "metadataFindings": [
            f"{item['id']} nuspec omits its repository URL."
            for item in manifest["packages"] if item["repositoryUrl"] is None
        ],
    }
    evidence.parent.mkdir(parents=True, exist_ok=True)
    evidence.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    if github_env is not None:
        require(all("\n" not in value and "\r" not in value for value in environment.values()), "Invalid output path.")
        with github_env.open("a", encoding="utf-8") as output:
            for name, value in environment.items():
                output.write(f"{name}={value}\n")
    print(f"Verified {len(staged)} candidate packages at {manifest['sourceSha']}; production pins are unchanged.")
    for finding in record["metadataFindings"]:
        print(f"Candidate metadata finding (not release-approved): {finding}")
    return record


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--work-directory", required=True, type=Path)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--github-env", type=Path)
    arguments = parser.parse_args()
    manifest = arguments.manifest.resolve(strict=True)
    require(manifest.is_relative_to(REPOSITORY), "Candidate manifest must be in the current checkout.")
    prepare(
        manifest, arguments.work_directory.resolve(), arguments.evidence.resolve(), arguments.github_env,
    )


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, OSError, subprocess.SubprocessError, zipfile.BadZipFile, ET.ParseError) as error:
        print(f"Candidate preparation failed: {error}", file=sys.stderr)
        sys.exit(1)
