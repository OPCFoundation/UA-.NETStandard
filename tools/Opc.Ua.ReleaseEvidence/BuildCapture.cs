// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed class BuildCapture(EvidenceFiles files, ProcessRunner runner)
    {
        public async Task<int> CaptureAsync(
            string repositoryRoot,
            string requestPath,
            string output,
            CancellationToken cancellationToken)
        {
            repositoryRoot = Path.GetFullPath(repositoryRoot);
            EvidenceFiles.RejectLinks(repositoryRoot);
            CaptureRequest request = await files.ReadModelAsync(
                requestPath, EvidenceJsonContext.Default.CaptureRequest, cancellationToken).ConfigureAwait(false);
            Versions.ValidateIdentity(request.Source, request.Producer);
            Versions.Parse(request.Version);
            if (request.Configuration is not ("Release" or "Debug") || request.Projects.Length == 0)
            {
                throw new InvalidDataException("Capture requires Release/Debug and an explicit nonempty project set.");
            }
            await ValidateCheckoutAsync(repositoryRoot, request.Source, cancellationToken).ConfigureAwait(false);
            EvidenceFiles.RejectLinks(output);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            {
                throw new InvalidDataException("Capture output must be empty; frozen inputs are immutable.");
            }
            Directory.CreateDirectory(output);
            var queue = new Queue<string>(request.Projects);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mappings = new List<ProjectMapping>();
            while (queue.TryDequeue(out string? relative))
            {
                if (!seen.Add(relative))
                {
                    continue;
                }
                string project = EvidenceFiles.Confined(repositoryRoot, relative);
                using JsonDocument evaluation = await EvaluateAsync(
                    repositoryRoot, project, request.Configuration, null, cancellationToken).ConfigureAwait(false);
                JsonElement properties = evaluation.RootElement.GetProperty("Properties");
                if (IsTrue(properties, "IsPackable") &&
                    !Versions.Equal(Property(properties, "PackageVersion"), request.Version))
                {
                    throw new InvalidDataException("Evaluated package version differs from the capture request.");
                }
                var references = new List<string>();
                foreach (JsonElement reference in evaluation.RootElement.GetProperty("Items")
                    .GetProperty("ProjectReference").EnumerateArray())
                {
                    string referencePath = Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(project)!, reference.GetProperty("Identity").GetString()!));
                    string referenced = Path.GetRelativePath(repositoryRoot, referencePath).Replace('\\', '/');
                    EvidenceFiles.ValidateRelative(referenced);
                    queue.Enqueue(referenced);
                    references.Add(referenced);
                }
                string assetsPath = Property(properties, "ProjectAssetsFile");
                if (string.IsNullOrWhiteSpace(assetsPath))
                {
                    throw new InvalidDataException("An evaluated project has no restored assets path.");
                }
                string relativeAssets = Path.GetRelativePath(repositoryRoot,
                    Path.GetFullPath(assetsPath, Path.GetDirectoryName(project)!)).Replace('\\', '/');
                assetsPath = EvidenceFiles.Confined(repositoryRoot, relativeAssets);
                byte[] assetsBytes = await files.ReadAsync(assetsPath, cancellationToken).ConfigureAwait(false);
                string assetsDigest = EvidenceFiles.Digest(assetsBytes);
                string frozenPath = "graphs/" + assetsDigest[7..] + ".assets.json";
                Directory.CreateDirectory(Path.Combine(output, "graphs"));
                await File.WriteAllBytesAsync(
                    EvidenceFiles.Confined(output, frozenPath), assetsBytes, cancellationToken)
                    .ConfigureAwait(false);
                using JsonDocument assets = await files.ReadJsonAsync(assetsPath, cancellationToken)
                    .ConfigureAwait(false);
                (ResolvedComponent[] components, PayloadCandidate[] payloads) =
                    await ReadAssetsAsync(assets.RootElement, cancellationToken).ConfigureAwait(false);
                string[] tfms = [.. (Property(properties, "TargetFrameworks") +
                    ";" +
                    Property(properties, "TargetFramework"))
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
                var candidates = new List<PayloadCandidate>(payloads);
                foreach (string tfm in tfms)
                {
                    using JsonDocument target = await EvaluateAsync(
                        repositoryRoot, project, request.Configuration, tfm, cancellationToken).ConfigureAwait(false);
                    JsonElement targetProperties = target.RootElement.GetProperty("Properties");
                    string binary = Property(targetProperties, "TargetPath");
                    if (!string.IsNullOrWhiteSpace(binary))
                    {
                        string fullBinary = EvidenceFiles.Confined(repositoryRoot, Path.GetRelativePath(
                            repositoryRoot, Path.GetFullPath(binary, Path.GetDirectoryName(project)!))
                            .Replace('\\', '/'));
                        if (File.Exists(fullBinary))
                        {
                            candidates.Add(new PayloadCandidate(
                                Path.GetFileName(fullBinary),
                                await files.DigestAsync(fullBinary, cancellationToken).ConfigureAwait(false),
                                Property(targetProperties, "AssemblyName"), request.Version,
                                "project:" + relative, tfm,
                                NullIfEmpty(Property(targetProperties, "RoslynApiVersion"))));
                            string pdb = Path.ChangeExtension(fullBinary, ".pdb");
                            if (File.Exists(pdb))
                            {
                                candidates.Add(new PayloadCandidate(
                                    Path.GetFileName(pdb),
                                    await files.DigestAsync(pdb, cancellationToken).ConfigureAwait(false),
                                    Property(targetProperties, "AssemblyName"), request.Version, "project:" + relative,
                                    tfm, NullIfEmpty(Property(targetProperties, "RoslynApiVersion"))));
                            }
                        }
                    }
                }
                if (await files.DigestAsync(assetsPath, cancellationToken).ConfigureAwait(false) != assetsDigest)
                {
                    throw new InvalidDataException(
                        "Assets changed during capture. Capture only after the final restore.");
                }
                mappings.Add(new ProjectMapping(
                    relative, request.Configuration, Property(properties, "PackageId"),
                    Property(properties, "PackageVersion"), IsTrue(properties, "IsPackable"),
                    IsTrue(properties, "IncludeSymbols"), Property(properties, "SymbolPackageFormat"),
                    tfms,
                    [.. references.Order(StringComparer.Ordinal)],
                    new FrozenFile(frozenPath, assetsDigest, assetsBytes.LongLength),
                    [.. components.Select(c => c with { Project = relative })],
                    [.. candidates.Distinct().OrderBy(p => p.Target, StringComparer.Ordinal)
                        .ThenBy(p => p.Owner, StringComparer.Ordinal)
                        .ThenBy(p => p.File, StringComparer.Ordinal)],
                    NullIfEmpty(Property(properties, "RoslynApiVersion"))));
            }
            var contracts = new List<FrozenFile>();
            foreach (string relative in new[]
            {
                ".azurepipelines/release-policy.json", ".azurepipelines/release-artifacts.json",
                ".azurepipelines/assurance-profiles.json", ".azurepipelines/release-evidence.schema.json",
                ".azurepipelines/expected-packages.txt"
            })
            {
                byte[] bytes = await files.ReadAsync(
                    EvidenceFiles.Confined(repositoryRoot, relative), cancellationToken).ConfigureAwait(false);
                string path = "contracts/" + Path.GetFileName(relative);
                Directory.CreateDirectory(Path.GetDirectoryName(EvidenceFiles.Confined(output, path))!);
                await File.WriteAllBytesAsync(EvidenceFiles.Confined(output, path), bytes, cancellationToken)
                    .ConfigureAwait(false);
                contracts.Add(new FrozenFile(path, EvidenceFiles.Digest(bytes), bytes.LongLength));
            }
            var bundle = new FrozenBundle(
                1, request.Source, request.Producer, request.Version, request.Configuration,
                [.. mappings.OrderBy(m => m.Project, StringComparer.Ordinal)], [.. contracts], []);
            await ValidateCheckoutAsync(repositoryRoot, request.Source, cancellationToken).ConfigureAwait(false);
            await files.WriteModelAsync(
                Path.Combine(output, "build-inputs.json"), bundle,
                EvidenceJsonContext.Default.FrozenBundle, cancellationToken)
                .ConfigureAwait(false);
            return 0;
        }

        private async Task ValidateCheckoutAsync(
            string root, SourceRecord source, CancellationToken cancellationToken)
        {
            string sha = (await runner.RunAsync(root, "git", ["rev-parse", "HEAD"], cancellationToken)
                .ConfigureAwait(false)).Trim();
            string referenceSha = (await runner.RunAsync(
                root, "git", ["rev-parse", "--verify", "--end-of-options", source.ActualRef + "^{commit}"],
                cancellationToken).ConfigureAwait(false)).Trim();
            string status = await runner.RunAsync(
                root, "git", ["status", "--porcelain", "--untracked-files=no"], cancellationToken)
                .ConfigureAwait(false);
            if (sha != source.ActualSha ||
                referenceSha != sha ||
                string.IsNullOrWhiteSpace(status) != source.TrackedClean)
            {
                throw new InvalidDataException("Capture identity differs from the actual tracked checkout.");
            }
        }

        private async Task<JsonDocument> EvaluateAsync(
            string root, string project, string configuration, string? tfm, CancellationToken cancellationToken)
        {
            var arguments = new List<string>
            {
                "msbuild", project, "-nologo",
                "-getProperty:PackageId,PackageVersion,IsPackable,IncludeSymbols,SymbolPackageFormat," +
                "TargetFramework,TargetFrameworks,ProjectAssetsFile,TargetPath,AssemblyName,RoslynApiVersion",
                "-getItem:ProjectReference", "-property:Configuration=" + configuration
            };
            if (tfm != null)
            {
                arguments.Add("-property:TargetFramework=" + tfm);
            }
            string result = await runner.RunAsync(root, "dotnet", [.. arguments], cancellationToken)
                .ConfigureAwait(false);
            return JsonDocument.Parse(result);
        }

        private async Task<(ResolvedComponent[] Components, PayloadCandidate[] Payloads)> ReadAssetsAsync(
            JsonElement assets, CancellationToken cancellationToken)
        {
            if (assets.GetProperty("version").GetInt32() != 3)
            {
                throw new InvalidDataException("Unsupported NuGet assets format.");
            }
            var components = new List<ResolvedComponent>();
            var payloads = new List<PayloadCandidate>();
            JsonElement libraries = assets.GetProperty("libraries");
            string[] roots = [.. assets.GetProperty("packageFolders").EnumerateObject().Select(p => p.Name)];
            foreach (JsonProperty target in assets.GetProperty("targets").EnumerateObject())
            {
                foreach (JsonProperty library in target.Value.EnumerateObject())
                {
                    int separator = library.Name.LastIndexOf('/');
                    if (separator <= 0)
                    {
                        throw new InvalidDataException("Malformed restored library identity.");
                    }
                    string id = library.Name[..separator];
                    string version = library.Name[(separator + 1)..];
                    Versions.Parse(version);
                    string type = library.Value.GetProperty("type").GetString()!;
                    var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (library.Value.TryGetProperty("dependencies", out JsonElement dependencyObject))
                    {
                        foreach (JsonProperty dependency in dependencyObject.EnumerateObject())
                        {
                            dependencies.Add(dependency.Name, dependency.Value.GetString()!);
                        }
                    }
                    LicenseRecord[] licenses = [];
                    if (type == "package")
                    {
                        JsonElement metadata = libraries.GetProperty(library.Name);
                        string libraryPath = metadata.GetProperty("path").GetString()!;
                        string? directory = roots.Select(root => EvidenceFiles.Confined(root, libraryPath))
                            .FirstOrDefault(Directory.Exists) ??
                            throw new InvalidDataException("Restored package cache content is missing.");
                        string[] contents =
                            [.. metadata.GetProperty("files").EnumerateArray().Select(f => f.GetString()!)];
                        string? nuspec = contents.SingleOrDefault(f =>
                            f.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                        if (nuspec != null)
                        {
                            XElement xml = NuspecMetadata.Read(await files.ReadAsync(
                                EvidenceFiles.Confined(directory, nuspec), cancellationToken).ConfigureAwait(false));
                            if (!string.Equals(
                                id, NuspecMetadata.Required(xml, "id"), StringComparison.OrdinalIgnoreCase) ||
                                !Versions.Equal(version, NuspecMetadata.Required(xml, "version")))
                            {
                                throw new InvalidDataException(
                                    "Package cache nuspec does not match the restored identity.");
                            }
                            licenses = NuspecMetadata.Licenses(xml);
                            for (int i = 0; i < licenses.Length; i++)
                            {
                                if (licenses[i].Kind == "file")
                                {
                                    string license = EvidenceFiles.Confined(directory, licenses[i].Value);
                                    if (File.Exists(license))
                                    {
                                        licenses[i] = licenses[i] with
                                        {
                                            Digest = await files.DigestAsync(license, cancellationToken)
                                                .ConfigureAwait(false)
                                        };
                                    }
                                }
                            }
                        }
                        foreach (string section in new[]
                        {
                            "compile", "runtime", "native", "runtimeTargets", "resource"
                        })
                        {
                            if (!library.Value.TryGetProperty(section, out JsonElement entries))
                            {
                                continue;
                            }
                            foreach (JsonProperty entry in entries.EnumerateObject())
                            {
                                if (entry.Name.EndsWith("/_._", StringComparison.Ordinal))
                                {
                                    continue;
                                }
                                string path = EvidenceFiles.Confined(directory, entry.Name);
                                payloads.Add(new PayloadCandidate(
                                    entry.Name, await files.DigestAsync(path, cancellationToken).ConfigureAwait(false),
                                    id, version, "package", target.Name));
                            }
                        }
                    }
                    components.Add(new ResolvedComponent(id, version, type, target.Name, dependencies, licenses));
                }
            }
            return (
                components.OrderBy(c => c.Target, StringComparer.Ordinal)
                    .ThenBy(c => c.Id, StringComparer.Ordinal).ToArray(),
                payloads.Distinct().ToArray());
        }

        private static string Property(JsonElement properties, string name)
        {
            return properties.TryGetProperty(name, out JsonElement value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool IsTrue(JsonElement properties, string name)
        {
            return string.Equals(Property(properties, name), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string? NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
