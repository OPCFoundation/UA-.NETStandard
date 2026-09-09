// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Json.Schema;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed record OciMaterial(string Uri, string Algorithm, string Digest);

    internal sealed record OciBuildExpectation(
        string BuilderId,
        string InvocationId,
        string SourceUri,
        string SourceSha,
        string Dockerfile,
        string ScannerCreator,
        string ScannerDigest,
        string BuildkitDigest,
        OciMaterial[] Materials);

    internal static class OciNativeValidation
    {
        public static async Task<string> CheckSpdxAsync(
            EvidenceFiles files,
            JsonElement predicate,
            Dictionary<string, OciFileEvidence>? filesystem,
            OciBuildExpectation? expected,
            List<Finding> findings,
            List<Finding> pendingFindings,
            CancellationToken cancellationToken)
        {
            const string version = "SPDX-2.3";
            CheckDescribedSubject(predicate, findings);
            string schemaPath = Path.Combine(AppContext.BaseDirectory, "Schemas", "spdx-2.3.schema.json");
            if (!File.Exists(schemaPath))
            {
                findings.Add(new Finding("INVENTORY_COMPLETE", "The frozen SPDX 2.3 schema is unavailable."));
                return version;
            }
            byte[] schemaBytes = await files.ReadAsync(schemaPath, cancellationToken).ConfigureAwait(false);
            if (EvidenceFiles.Digest(schemaBytes) !=
                "sha256:239208b7ac287b3cf5d9a9af23f9d69863971102a5e1587a27a398b43490b89b")
            {
                findings.Add(new Finding("INVENTORY_COMPLETE", "The frozen SPDX 2.3 schema identity has changed."));
                return version;
            }
            using JsonDocument schemaDocument = JsonDocument.Parse(schemaBytes);
            var schema = JsonSchema.FromText(schemaDocument.RootElement.GetRawText(), new BuildOptions
            {
                SchemaRegistry = new SchemaRegistry
                {
                    Fetch = (_, _) => throw new InvalidDataException("Unregistered offline SPDX schema.")
                }
            });
            if (!schema.Evaluate(predicate, new EvaluationOptions
            {
                OutputFormat = OutputFormat.Flag,
                RequireFormatValidation = true
            }).IsValid || predicate.GetProperty("spdxVersion").GetString() != version ||
                predicate.GetProperty("SPDXID").GetString() != "SPDXRef-DOCUMENT" ||
                predicate.GetProperty("dataLicense").GetString() != "CC0-1.0")
            {
                findings.Add(new Finding("INVENTORY_COMPLETE", "Native SPDX fails the frozen 2.3 schema."));
                return version;
            }
            JsonElement creation = predicate.GetProperty("creationInfo");
            if (!creation.GetProperty("created").TryGetDateTimeOffset(out _) ||
                creation.GetProperty("creators").GetArrayLength() == 0 ||
                !Uri.TryCreate(predicate.GetProperty("documentNamespace").GetString(), UriKind.Absolute, out _))
            {
                findings.Add(new Finding("INVENTORY_COMPLETE", "SPDX creation or namespace identity is invalid."));
            }
            if (expected == null)
            {
                pendingFindings.Add(new Finding("INVENTORY_COMPLETE",
                    "Scanner identity/tool digest requires independently verified build evidence."));
            }
            else if (string.IsNullOrWhiteSpace(expected.ScannerCreator) ||
                !creation.GetProperty("creators").EnumerateArray().Any(c => c.GetString() == expected.ScannerCreator) ||
                !IsSha256(expected.ScannerDigest) || !IsSha256(expected.BuildkitDigest))
            {
                findings.Add(new Finding("INVENTORY_COMPLETE",
                    "Scanner identity/tool digest has not been bound by independently verified build evidence."));
            }
            var ids = new HashSet<string>(StringComparer.Ordinal) { "SPDXRef-DOCUMENT" };
            var packages = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var fileElements = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (string collection in new[] { "packages", "files", "snippets" })
            {
                if (!predicate.TryGetProperty(collection, out JsonElement elements))
                {
                    continue;
                }
                foreach (JsonElement element in elements.EnumerateArray())
                {
                    string id = element.GetProperty("SPDXID").GetString()!;
                    if (!id.StartsWith("SPDXRef-", StringComparison.Ordinal) || !ids.Add(id))
                    {
                        findings.Add(new Finding("INVENTORY_COMPLETE", "SPDX element IDs are invalid or duplicated."));
                        continue;
                    }
                    if (collection == "packages")
                    {
                        packages.Add(id, element);
                    }
                    if (collection == "files")
                    {
                        fileElements.Add(id, element);
                    }
                }
            }
            var ownership = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (predicate.TryGetProperty("relationships", out JsonElement relationships))
            {
                foreach (JsonElement relationship in relationships.EnumerateArray())
                {
                    string from = relationship.GetProperty("spdxElementId").GetString()!;
                    string to = relationship.GetProperty("relatedSpdxElement").GetString()!;
                    string kind = relationship.GetProperty("relationshipType").GetString()!;
                    if (!ids.Contains(from) || (!ids.Contains(to) && to is not ("NONE" or "NOASSERTION")))
                    {
                        findings.Add(new Finding("INVENTORY_COMPLETE", "SPDX relationship has unresolved identity."));
                    }
                    string? package = kind == "CONTAINS" ? from : kind == "CONTAINED_BY" ? to : null;
                    string file = kind == "CONTAINS" ? to : from;
                    if (package != null && packages.ContainsKey(package) && fileElements.ContainsKey(file))
                    {
                        if (!ownership.TryGetValue(file, out HashSet<string>? owners))
                        {
                            owners = [];
                            ownership.Add(file, owners);
                        }
                        owners.Add(package);
                    }
                }
            }
            if (filesystem == null)
            {
                findings.Add(new Finding("INVENTORY_COMPLETE", "Final-image filesystem evidence is unavailable."));
                return version;
            }
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string id, JsonElement file) in fileElements)
            {
                string path = OciLayerInventory.Normalize(file.GetProperty("fileName").GetString()!.TrimStart('/'));
                if (!claimed.Add(path) || !filesystem.TryGetValue(path, out OciFileEvidence? actual) ||
                    actual.Kind != "file" ||
                    !file.TryGetProperty("checksums", out JsonElement checksums) ||
                    checksums.EnumerateArray().Count(c =>
                        c.GetProperty("algorithm").GetString() == "SHA256" &&
                        "sha256:" + c.GetProperty("checksumValue").GetString() == actual.Digest) != 1 ||
                    !ownership.TryGetValue(id, out HashSet<string>? owners) || owners.Count != 1 ||
                    !HasPackageSource(packages[owners.Single()]))
                {
                    findings.Add(new Finding("INVENTORY_COMPLETE",
                        "SPDX file bytes or package ownership/source do not reconcile to the final image."));
                }
            }
            if (filesystem.Values.Any(f => f.Kind == "file" && !claimed.Contains(f.Path)))
            {
                findings.Add(new Finding("INVENTORY_COMPLETE",
                    "Unclaimed final-image payload remains; headers/signatures do not establish inventory coverage."));
            }
            return version;
        }

        internal static void CheckDescribedSubject(JsonElement predicate, List<Finding> findings)
        {
            var packages = new HashSet<string>(StringComparer.Ordinal);
            var described = new HashSet<string>(StringComparer.Ordinal);
            if (predicate.TryGetProperty("packages", out JsonElement elements) &&
                elements.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement package in elements.EnumerateArray())
                {
                    if (package.ValueKind == JsonValueKind.Object &&
                        package.TryGetProperty("SPDXID", out JsonElement id) && id.ValueKind == JsonValueKind.String)
                    {
                        packages.Add(id.GetString()!);
                    }
                }
            }
            if (predicate.TryGetProperty("documentDescribes", out JsonElement describes) &&
                describes.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement id in describes.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.String)
                    {
                        described.Add(id.GetString()!);
                    }
                }
            }
            if (predicate.TryGetProperty("relationships", out JsonElement relationships) &&
                relationships.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement relationship in relationships.EnumerateArray())
                {
                    if (relationship.ValueKind != JsonValueKind.Object ||
                        !relationship.TryGetProperty("spdxElementId", out JsonElement from) ||
                        !relationship.TryGetProperty("relatedSpdxElement", out JsonElement to) ||
                        !relationship.TryGetProperty("relationshipType", out JsonElement kind) ||
                        from.ValueKind != JsonValueKind.String || to.ValueKind != JsonValueKind.String ||
                        kind.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }
                    if (from.GetString() == "SPDXRef-DOCUMENT" && kind.GetString() == "DESCRIBES")
                    {
                        described.Add(to.GetString()!);
                    }
                    if (to.GetString() == "SPDXRef-DOCUMENT" && kind.GetString() == "DESCRIBED_BY")
                    {
                        described.Add(from.GetString()!);
                    }
                }
            }
            if (described.Count == 0 || described.Any(id => !packages.Contains(id)))
            {
                findings.Add(new Finding("INVENTORY_COMPLETE", "SPDX document describes an undefined subject."));
            }
        }

        public static void CheckProvenance(
            JsonElement predicate,
            string predicateType,
            OciBuildExpectation? expected,
            List<Finding> findings,
            List<Finding> pendingFindings)
        {
            bool v1 = predicateType == "https://slsa.dev/provenance/v1";
            if (!v1 && predicateType != "https://slsa.dev/provenance/v0.2")
            {
                findings.Add(new Finding("PROVENANCE_VERIFIED", "Unsupported native provenance predicate."));
                return;
            }
            JsonElement definition = v1 ? predicate.GetProperty("buildDefinition") : predicate;
            JsonElement run = v1 ? predicate.GetProperty("runDetails") : predicate;
            JsonElement parameters = definition.GetProperty(v1 ? "externalParameters" : "invocation");
            JsonElement source = parameters.GetProperty("configSource");
            JsonElement metadata = run.GetProperty("metadata");
            JsonElement materials = definition.GetProperty(v1 ? "resolvedDependencies" : "materials");
            string buildType = v1
                ? "https://github.com/moby/buildkit/blob/master/docs/attestations/slsa-definitions.md"
                : "https://mobyproject.org/buildkit@v1";
            if (definition.GetProperty("buildType").GetString() != buildType ||
                !metadata.GetProperty(v1 ? "startedOn" : "buildStartedOn")
                    .TryGetDateTimeOffset(out DateTimeOffset start) ||
                !metadata.GetProperty(v1 ? "finishedOn" : "buildFinishedOn")
                    .TryGetDateTimeOffset(out DateTimeOffset end) ||
                end < start || materials.GetArrayLength() == 0 || materials.GetArrayLength() > 4096)
            {
                findings.Add(new Finding("PROVENANCE_VERIFIED", "Malformed native BuildKit provenance."));
                return;
            }
            var actualMaterials = new HashSet<OciMaterial>();
            foreach (JsonElement material in materials.EnumerateArray())
            {
                string uri = material.GetProperty("uri").GetString()!;
                JsonElement hashes = material.GetProperty("digest");
                if (string.IsNullOrWhiteSpace(uri) || hashes.EnumerateObject().Count() != 1)
                {
                    findings.Add(new Finding("PROVENANCE_VERIFIED", "Invalid or ambiguous material identity."));
                    continue;
                }
                JsonProperty hash = hashes.EnumerateObject().Single();
                string value = hash.Value.GetString()!;
                int expectedLength = hash.Name switch { "sha256" => 64, "sha1" => 40, _ => 0 };
                if (expectedLength == 0 || value.Length != expectedLength ||
                    value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
                    (hash.Name == "sha1" && !uri.StartsWith("https://github.com/", StringComparison.Ordinal)) ||
                    !actualMaterials.Add(new OciMaterial(uri, hash.Name, value)))
                {
                    findings.Add(new Finding("PROVENANCE_VERIFIED", "Invalid or duplicate material digest identity."));
                }
            }
            if (expected == null)
            {
                pendingFindings.Add(new Finding("PROVENANCE_VERIFIED",
                    "Native build claims lack an independently authenticated expected build context."));
                return;
            }
            bool sourceBound = source.TryGetProperty("uri", out JsonElement sourceUri) &&
                sourceUri.GetString() == expected.SourceUri &&
                source.TryGetProperty("digest", out JsonElement sourceDigest) &&
                sourceDigest.EnumerateObject().Count() == 1 &&
                sourceDigest.EnumerateObject().Single().Value.GetString() == expected.SourceSha &&
                sourceDigest.EnumerateObject().Single().Name == (expected.SourceSha.Length == 40 ? "sha1" : "sha256");
            // Local-context VCS annotations are hints, not evidence that BuildKit consumed that checkout.
            if (!sourceBound ||
                !actualMaterials.Any(m => m.Uri == expected.SourceUri && m.Digest == expected.SourceSha) ||
                !actualMaterials.SetEquals(expected.Materials) ||
                run.GetProperty("builder").GetProperty("id").GetString() != expected.BuilderId ||
                string.IsNullOrWhiteSpace(expected.BuilderId) ||
                metadata.GetProperty(v1 ? "invocationId" : "buildInvocationID").GetString() != expected.InvocationId ||
                string.IsNullOrWhiteSpace(expected.InvocationId) ||
                source.GetProperty(v1 ? "path" : "entryPoint").GetString() != expected.Dockerfile ||
                !IsSha256(expected.ScannerDigest) || !IsSha256(expected.BuildkitDigest))
            {
                findings.Add(new Finding("PROVENANCE_VERIFIED",
                    "BuildKit source/builder/invocation/material/tool identity differs from authenticated evidence."));
            }
        }

        internal static bool IsSha256(string? digest)
        {
            return digest is { Length: 71 } && digest.StartsWith("sha256:", StringComparison.Ordinal) &&
                digest[7..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        }

        internal static bool MatchesBuildContext(OciBuildExpectation build, EvaluationExpectation expected)
        {
            string repository = "https://github.com/" + expected.Source.Repository;
            string[] source = build.SourceUri.Split('#');
            return build.SourceSha == expected.Source.ActualSha &&
                (source[0] == repository || source[0] == repository + ".git") &&
                source.Length <= 2 &&
                (source.Length == 1 ||
                    source[1] == expected.Source.ActualRef || source[1] == expected.Source.ActualSha) &&
                expected.Producer.Tools.Any(t => t.Id == "buildkit" && t.Digest == build.BuildkitDigest) &&
                expected.Producer.Tools.Any(t =>
                    t.Id == "buildkit-syft-scanner" && t.Digest == build.ScannerDigest);
        }

        private static bool HasPackageSource(JsonElement package)
        {
            if (!package.TryGetProperty("versionInfo", out JsonElement version) ||
                string.IsNullOrWhiteSpace(version.GetString()) ||
                !package.TryGetProperty("externalRefs", out JsonElement references))
            {
                return false;
            }
            return references.EnumerateArray().Any(reference =>
                reference.GetProperty("referenceType").GetString() == "purl" &&
                reference.GetProperty("referenceCategory").GetString() == "PACKAGE-MANAGER" &&
                reference.GetProperty("referenceLocator").GetString() is string locator &&
                MatchesPackage(locator, package.GetProperty("name").GetString()!, version.GetString()!));
        }

        private static bool MatchesPackage(string locator, string name, string version)
        {
            if (!locator.StartsWith("pkg:", StringComparison.Ordinal))
            {
                return false;
            }
            int at = locator.LastIndexOf('@');
            int slash = at > 0 ? locator.LastIndexOf('/', at) : -1;
            if (at < 0 || slash < 0 || slash >= at - 1)
            {
                return false;
            }
            int end = locator.IndexOfAny(['?', '#'], at + 1);
            string actualVersion = end < 0 ? locator[(at + 1)..] : locator[(at + 1)..end];
            return Uri.UnescapeDataString(actualVersion) == version &&
                Uri.UnescapeDataString(locator[(slash + 1)..at]) == name;
        }
    }
}
