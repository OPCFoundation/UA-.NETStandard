/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Identifies a verified OCI blob by image, digest, media type, byte length, and local relative path.
    /// </summary>
    /// <param name="Image">The OCI image repository identifier.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    /// <param name="MediaType">The OCI media type declared for the verified blob.</param>
    /// <param name="Size">The length of the referenced content in bytes.</param>
    /// <param name="Path">The request-relative path locating the verified blob bytes.</param>
    internal sealed record OciBlobReference(
        string Image, string Digest, string MediaType, long Size, string Path);

    /// <summary>
    /// Binds an OCI referrer manifest and artifact type to an image subject digest.
    /// </summary>
    /// <param name="Image">The OCI image repository identifier.</param>
    /// <param name="SubjectDigest">The digest of the OCI subject described by the attestation or referrer.</param>
    /// <param name="ManifestDigest">
    /// The digest of the OCI manifest containing the referenced attestation or artifact.
    /// </param>
    /// <param name="ArtifactType">The OCI artifact type under which the referrer must be discoverable.</param>
    internal sealed record OciReferrerBinding(
        string Image, string SubjectDigest, string ManifestDigest, string ArtifactType);

    /// <summary>
    /// Contains an image's root digest, complete referenced blob set, and authenticated referrer bindings.
    /// </summary>
    /// <param name="Image">The OCI image repository identifier.</param>
    /// <param name="RootDigest">The immutable digest of the requested OCI image root.</param>
    /// <param name="Blobs">The complete set of verified blobs referenced by the image and its referrers.</param>
    /// <param name="Referrers">
    /// The authenticated subject-to-referrer relationships required for the image closure.
    /// </param>
    internal sealed record OciImageClosure(
        string Image, string RootDigest, OciBlobReference[] Blobs, OciReferrerBinding[] Referrers);

    /// <summary>
    /// Reads bounded OCI blob closures and checks their expected subjects and authenticated referrer relationships.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    internal sealed class OciClosureReader(EvidenceFiles files)
    {
        /// <summary>
        /// Reads an OCI closure whose subjects, referrers, and signature-bundle bytes match authenticated evidence.
        /// </summary>
        public async Task<OciImageClosure[]> ReadAuthenticatedAsync(
            string requestPath,
            string evidencePath,
            EvidenceEnvelope envelope,
            VerifiedClaims verified,
            CancellationToken cancellationToken)
        {
            string evidenceDigest = await files.DigestAsync(evidencePath, cancellationToken).ConfigureAwait(false);
            if (verified.Findings.Any(f => f.Code is not
                    ("ASSURANCE_COMPLETE" or "INVENTORY_COMPLETE" or "INPUT_IDENTITY" or
                        "PUBLIC_EVIDENCE_SAFE" or "PRODUCER_NOT_READY")) ||
                !verified.Records.TryGetValue("producer", out VerificationRecord? producer) ||
                !verified.Records.TryGetValue("artifact-signatures", out VerificationRecord? signatures) ||
                producer.EvidenceDigest != evidenceDigest || signatures.EvidenceDigest != evidenceDigest ||
                producer.Source != envelope.Source || producer.Release != envelope.Release ||
                producer.PolicyDigest != envelope.Policy.Digest ||
                producer.ArtifactSetDigest != VerificationControls.ArtifactSetDigest(envelope.Artifacts) ||
                signatures.ArtifactSetDigest != producer.ArtifactSetDigest ||
                signatures.ArtifactSignatures == null)
            {
                throw new InvalidDataException("OCI closure requires current authenticated group/signature records.");
            }
            string root = Path.GetDirectoryName(Path.GetFullPath(evidencePath))!;
            OciReferrerBinding[]? referrers = null;
            foreach (DocumentRecord document in envelope.Documents.Where(d =>
                d.Type == "producer-record" && d.Format == "json" && d.Version == "1"))
            {
                string path = EvidenceFiles.Confined(root, document.Path);
                FrozenFile[] bound = producer.Documents.Where(d =>
                    d.Path == document.Path && d.Digest == document.Digest).ToArray();
                if (bound.Length != 1 || new FileInfo(path).Length != bound[0].Size ||
                    await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != document.Digest)
                {
                    throw new InvalidDataException("OCI referrer context differs from its signed document binding.");
                }
                using JsonDocument json = await files.ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
                if (!json.RootElement.TryGetProperty("kind", out JsonElement kind) ||
                    kind.GetString() != "oci-referrer-context")
                {
                    continue;
                }
                if (referrers != null)
                {
                    throw new InvalidDataException("OCI referrer context must be unique.");
                }
                referrers = ParseReferrers(json.RootElement);
            }
            if (referrers == null)
            {
                throw new InvalidDataException("The authenticated OCI referrer context is missing.");
            }
            var expected = new EvaluationExpectation(
                envelope.Source, envelope.Producer, envelope.Release, envelope.Policy.Digest, envelope.Artifacts);
            OciImageClosure[] closures = await ReadAsync(requestPath, expected, referrers, cancellationToken)
                .ConfigureAwait(false);
            string requestRoot = Path.GetDirectoryName(Path.GetFullPath(requestPath))!;
            foreach (OciImageClosure closure in closures)
            {
                foreach (OciReferrerBinding referrer in closure.Referrers)
                {
                    ArtifactSignatureProof[] proofs = signatures.ArtifactSignatures.Where(p =>
                        p.Id == closure.Image && p.ArtifactDigest == referrer.SubjectDigest).ToArray();
                    OciBlobReference manifest = closure.Blobs.Single(b => b.Digest == referrer.ManifestDigest);
                    using JsonDocument json = await files.ReadJsonAsync(
                        EvidenceFiles.Confined(requestRoot, manifest.Path), cancellationToken).ConfigureAwait(false);
                    if (proofs.Length != 1 || !json.RootElement.GetProperty("layers").EnumerateArray()
                        .Any(layer => layer.GetProperty("digest").GetString() == proofs[0].SignatureDigest))
                    {
                        throw new InvalidDataException(
                            "OCI referrer does not retain the exact independently verified signature bundle bytes.");
                    }
                }
            }
            return closures;
        }

        /// <summary>
        /// Maps a verified OCI subject and its supporting closure into a candidate-relative promotion member.
        /// </summary>
        public static PromotionMember ToPromotionMember(
            OciImageClosure closure,
            ArtifactRecord artifact,
            string requestPath,
            string candidateRoot,
            string destination,
            PromotionFile[] additionalEvidence,
            string? alias = null,
            string? expectedAliasDigest = null)
        {
            string requestRoot = Path.GetDirectoryName(Path.GetFullPath(requestPath))!;
            candidateRoot = Path.GetFullPath(candidateRoot);
            _ = EvidenceFiles.Confined(candidateRoot,
                Path.GetRelativePath(candidateRoot, Path.GetFullPath(requestPath)).Replace('\\', '/'));

            PromotionFile ConvertBlob(OciBlobReference blob)
            {
                string path = EvidenceFiles.Confined(requestRoot, blob.Path);
                string relative = Path.GetRelativePath(candidateRoot, path).Replace('\\', '/');
                _ = EvidenceFiles.Confined(candidateRoot, relative);
                return new PromotionFile(relative, blob.Digest);
            }

            OciBlobReference[] roots = closure.Blobs.Where(b => b.Digest == closure.RootDigest).ToArray();
            if (roots.Length != 1 || !OciNativeValidation.IsSha256(closure.RootDigest) ||
                closure.Blobs.Any(b => b.Image != closure.Image) ||
                closure.Referrers.Any(r => r.Image != closure.Image ||
                    !closure.Blobs.Any(b => b.Digest == r.ManifestDigest) ||
                    !closure.Blobs.Any(b => b.Digest == r.SubjectDigest)))
            {
                throw new InvalidDataException("OCI promotion mapping requires one exact root and image closure.");
            }
            OciBlobReference[] content = closure.Blobs.Where(b => b.Digest == artifact.Digest).ToArray();
            if (artifact.Id != closure.Image || content.Length != 1 ||
                !closure.Referrers.Any(r => r.SubjectDigest == artifact.Digest) ||
                artifact.Kind is not ("oci-index" or "oci-manifest") ||
                (artifact.Kind == "oci-index"
                    ? artifact.Scopes.Platforms.Length != 0 ||
                        content[0].MediaType is not ("application/vnd.oci.image.index.v1+json" or
                            "application/vnd.docker.distribution.manifest.list.v2+json")
                    : artifact.Scopes.Platforms.Length != 1 ||
                        string.IsNullOrWhiteSpace(artifact.Scopes.Platforms[0]) ||
                        content[0].MediaType is not ("application/vnd.oci.image.manifest.v1+json" or
                            "application/vnd.docker.distribution.manifest.v2+json")) ||
                (artifact.Digest != closure.RootDigest && (alias != null || expectedAliasDigest != null)))
            {
                throw new InvalidDataException(
                    "OCI member requires an exact typed subject; only the image root may have a tag alias.");
            }
            PromotionFile selected = ConvertBlob(content[0]);
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            paths.Add(selected.Path, selected.Digest);
            var evidence = new Dictionary<string, PromotionFile>(StringComparer.Ordinal);
            foreach (PromotionFile file in closure.Blobs.Where(b => b.Digest != artifact.Digest)
                .Select(ConvertBlob).Concat(additionalEvidence)
                .OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                _ = EvidenceFiles.Confined(candidateRoot, file.Path);
                if (!OciNativeValidation.IsSha256(file.Digest) ||
                    (paths.TryGetValue(file.Path, out string? digest) && digest != file.Digest))
                {
                    throw new InvalidDataException("OCI promotion evidence has invalid or conflicting path identity.");
                }
                paths[file.Path] = file.Digest;
                if (file.Digest != artifact.Digest)
                {
                    evidence.TryAdd(file.Digest, file);
                }
            }
            if (evidence.Count is 0 or > 4096)
            {
                throw new InvalidDataException("OCI closure exceeds the promotion member evidence scope.");
            }
            return new PromotionMember(artifact.Id, artifact.Version, destination,
                selected,
                [.. evidence.Values.OrderBy(e => e.Digest, StringComparer.Ordinal)], alias, expectedAliasDigest,
                artifact.Kind, artifact.Scopes.Platforms.SingleOrDefault());
        }

        /// <summary>
        /// Reads the requested OCI closures and checks their complete artifact group and authenticated referrer scope.
        /// </summary>
        public async Task<OciImageClosure[]> ReadAsync(
            string requestPath,
            EvaluationExpectation independentlyExpected,
            OciReferrerBinding[] authenticatedReferrers,
            CancellationToken cancellationToken)
        {
            OciRequest request = await files.ReadModelAsync(
                requestPath, EvidenceJsonContext.Default.OciRequest, cancellationToken).ConfigureAwait(false);
            string root = Path.GetDirectoryName(Path.GetFullPath(requestPath))!;
            ArtifactRecord[] subjects = independentlyExpected.Artifacts.Where(a =>
                a.Kind is "oci-index" or "oci-manifest").ToArray();
            if (request.Images.Length is 0 or > 64 ||
                request.Images.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != request.Images.Length ||
                authenticatedReferrers.Length > 1024 ||
                authenticatedReferrers.Distinct().Count() != authenticatedReferrers.Length ||
                subjects.Length == 0 || independentlyExpected.Release.Group is not ("containers" or "pump"))
            {
                throw new InvalidDataException("OCI closure requires a unique, bounded expected artifact group.");
            }
            foreach (ArtifactRecord subject in subjects)
            {
                if (!authenticatedReferrers.Any(r => r.Image == subject.Id && r.SubjectDigest == subject.Digest))
                {
                    throw new InvalidDataException("An expected OCI root/platform has no bound signature referrer.");
                }
            }
            foreach (OciReferrerBinding referrer in authenticatedReferrers)
            {
                if (!subjects.Any(s => s.Id == referrer.Image && s.Digest == referrer.SubjectDigest) ||
                    string.IsNullOrWhiteSpace(referrer.ArtifactType))
                {
                    throw new InvalidDataException("A referrer is outside the independently expected subject set.");
                }
            }
            var result = new List<OciImageClosure>();
            foreach (OciImageInput image in request.Images)
            {
                if (!subjects.Any(s => s.Id == image.Id && s.Digest == image.RootDigest))
                {
                    throw new InvalidDataException(
                        "OCI requested root differs from the independently expected group.");
                }
                string layout = EvidenceFiles.Confined(root, image.Layout);
                var reader = new ImageReader(files, image.Id, root, layout);
                await reader.VisitAsync(image.RootDigest, null, null, true, [], cancellationToken)
                    .ConfigureAwait(false);
                foreach (ArtifactRecord subject in subjects.Where(s => s.Id == image.Id))
                {
                    if (!reader.Blobs.ContainsKey(subject.Digest))
                    {
                        throw new InvalidDataException(
                            "An expected platform is absent from its root descriptor graph.");
                    }
                }
                OciReferrerBinding[] referrers = authenticatedReferrers.Where(r => r.Image == image.Id)
                    .OrderBy(r => r.SubjectDigest, StringComparer.Ordinal)
                    .ThenBy(r => r.ManifestDigest, StringComparer.Ordinal).ToArray();
                foreach (OciReferrerBinding referrer in referrers)
                {
                    await reader.VisitAsync(referrer.ManifestDigest, null, null, true, [], cancellationToken)
                        .ConfigureAwait(false);
                    string path = OciReconciler.BlobPath(layout, referrer.ManifestDigest);
                    using JsonDocument document = await files.ReadJsonAsync(path, cancellationToken)
                        .ConfigureAwait(false);
                    JsonElement manifest = document.RootElement;
                    JsonElement subject = manifest.GetProperty("subject");
                    OciBlobReference boundSubject = reader.Blobs[referrer.SubjectDigest];
                    if (manifest.GetProperty("mediaType").GetString() !=
                        "application/vnd.oci.image.manifest.v1+json" ||
                        manifest.GetProperty("artifactType").GetString() != referrer.ArtifactType ||
                        subject.GetProperty("digest").GetString() != referrer.SubjectDigest ||
                        subject.GetProperty("size").GetInt64() != boundSubject.Size ||
                        subject.GetProperty("mediaType").GetString() != boundSubject.MediaType)
                    {
                        throw new InvalidDataException(
                            "OCI referrer manifest does not bind the expected subject/type.");
                    }
                }
                result.Add(new OciImageClosure(image.Id, image.RootDigest,
                    [.. reader.Blobs.Values.OrderBy(b => b.Digest, StringComparer.Ordinal)], referrers));
            }
            if (!subjects.Select(s => s.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .SequenceEqual(request.Images.Select(i => i.Id).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                throw new InvalidDataException("OCI closure request omits part of the independently expected group.");
            }
            return [.. result];
        }

        /// <summary>
        /// Parses a closed, bounded referrer context and rejects malformed or duplicate referrer identities.
        /// </summary>
        internal static OciReferrerBinding[] ParseReferrers(JsonElement value)
        {
            CheckProperties(value, ["schemaVersion", "kind", "referrers"]);
            if (value.GetProperty("schemaVersion").GetInt32() != 1 ||
                value.GetProperty("kind").GetString() != "oci-referrer-context" ||
                value.GetProperty("referrers").GetArrayLength() is 0 or > 1024)
            {
                throw new InvalidDataException("Unsupported or unbounded OCI referrer context.");
            }
            var result = new List<OciReferrerBinding>();
            foreach (JsonElement referrer in value.GetProperty("referrers").EnumerateArray())
            {
                CheckProperties(referrer, ["image", "subjectDigest", "manifestDigest", "artifactType"]);
                var binding = new OciReferrerBinding(
                    referrer.GetProperty("image").GetString()!,
                    referrer.GetProperty("subjectDigest").GetString()!,
                    referrer.GetProperty("manifestDigest").GetString()!,
                    referrer.GetProperty("artifactType").GetString()!);
                if (string.IsNullOrWhiteSpace(binding.Image) || binding.Image.Length > 2048 ||
                    string.IsNullOrWhiteSpace(binding.ArtifactType) || binding.ArtifactType.Length > 256 ||
                    !OciNativeValidation.IsSha256(binding.SubjectDigest) ||
                    !OciNativeValidation.IsSha256(binding.ManifestDigest) || result.Contains(binding))
                {
                    throw new InvalidDataException("Invalid or duplicate OCI referrer identity.");
                }
                result.Add(binding);
            }
            return [.. result];
        }

        private static void CheckProperties(JsonElement value, string[] names)
        {
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != names.Length ||
                value.EnumerateObject().Any(p => !names.Contains(p.Name, StringComparer.Ordinal)))
            {
                throw new InvalidDataException("Unknown or missing OCI referrer-context property.");
            }
        }

        private sealed class ImageReader(EvidenceFiles files, string image, string root, string layout)
        {
            /// <summary>
            /// Gets the verified blobs visited for the image, indexed by their content digest.
            /// </summary>
            public Dictionary<string, OciBlobReference> Blobs { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Visits a blob and its manifest descendants while checking descriptor consistency, digests, and resource
            /// bounds.
            /// </summary>
            public async Task VisitAsync(
                string digest,
                string? mediaType,
                long? size,
                bool manifest,
                HashSet<string> ancestors,
                CancellationToken cancellationToken)
            {
                if (ancestors.Count > 32 || ancestors.Contains(digest))
                {
                    throw new InvalidDataException("OCI closure descriptor graph is cyclic or exceeds depth limits.");
                }
                if (Blobs.TryGetValue(digest, out OciBlobReference? previous))
                {
                    if ((mediaType != null && previous.MediaType != mediaType) ||
                        (size != null && previous.Size != size))
                    {
                        throw new InvalidDataException("OCI shared-blob descriptors disagree.");
                    }
                    return;
                }
                string path = OciReconciler.BlobPath(layout, digest);
                if (!File.Exists(path) || Blobs.Count >= 8192)
                {
                    throw new InvalidDataException(
                        "OCI closure blob is missing or descriptor count exceeds the bound.");
                }
                long length = new FileInfo(path).Length;
                m_totalBytes = checked(m_totalBytes + length);
                if (length > 4L * 1024 * 1024 * 1024 || (manifest && length > 64 * 1024 * 1024) ||
                    m_totalBytes > 16L * 1024 * 1024 * 1024 || (size != null && size != length) ||
                    await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != digest)
                {
                    throw new InvalidDataException("OCI closure blob hash/size differs or exceeds the byte bound.");
                }
                if (!manifest)
                {
                    if (string.IsNullOrWhiteSpace(mediaType) || mediaType.Length > 256)
                    {
                        throw new InvalidDataException("OCI closure leaf media type is missing or invalid.");
                    }
                    Blobs.Add(digest, new OciBlobReference(
                        image, digest, mediaType, length, Path.GetRelativePath(root, path).Replace('\\', '/')));
                    return;
                }
                using JsonDocument document = await files.ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
                JsonElement value = document.RootElement;
                string actualType = value.GetProperty("mediaType").GetString()!;
                if (value.GetProperty("schemaVersion").GetInt32() != 2 ||
                    (mediaType != null && actualType != mediaType))
                {
                    throw new InvalidDataException("OCI closure manifest type/schema differs from its descriptor.");
                }
                Blobs.Add(digest, new OciBlobReference(
                    image, digest, actualType, length, Path.GetRelativePath(root, path).Replace('\\', '/')));
                var nested = new HashSet<string>(ancestors, StringComparer.Ordinal) { digest };
                if (actualType is "application/vnd.oci.image.index.v1+json" or
                    "application/vnd.docker.distribution.manifest.list.v2+json")
                {
                    JsonElement children = value.GetProperty("manifests");
                    if (children.GetArrayLength() is 0 or > 1024)
                    {
                        throw new InvalidDataException("OCI closure index has an unsupported child count.");
                    }
                    foreach (JsonElement child in children.EnumerateArray())
                    {
                        await VisitDescriptorAsync(child, true, nested, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (actualType is "application/vnd.oci.image.manifest.v1+json" or
                    "application/vnd.docker.distribution.manifest.v2+json")
                {
                    JsonElement layers = value.GetProperty("layers");
                    if (layers.GetArrayLength() > 256)
                    {
                        throw new InvalidDataException("OCI closure manifest layer count exceeds the bound.");
                    }
                    await VisitDescriptorAsync(value.GetProperty("config"), false, nested, cancellationToken)
                        .ConfigureAwait(false);
                    foreach (JsonElement layer in layers.EnumerateArray())
                    {
                        await VisitDescriptorAsync(layer, false, nested, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    throw new InvalidDataException("Unsupported OCI closure manifest media type.");
                }
            }

            private Task VisitDescriptorAsync(
                JsonElement descriptor, bool manifest, HashSet<string> ancestors, CancellationToken cancellationToken)
            {
                return VisitAsync(descriptor.GetProperty("digest").GetString()!,
                    descriptor.GetProperty("mediaType").GetString(), descriptor.GetProperty("size").GetInt64(),
                    manifest, ancestors, cancellationToken);
            }

            private long m_totalBytes;
        }
    }
}
