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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Aas.Server.Packaging;

namespace Opc.Ua.Aas.Server.Packaging
{
    /// <summary>
    /// Computes and validates AAS package integrity metadata.
    /// </summary>
    public static class AasPackageIntegrity
    {
        /// <summary>
        /// Computes the lower-case hexadecimal package digest without an algorithm prefix.
        /// </summary>
        public static string ComputeDigest(ByteString blob, string digestAlg)
        {
            ValidateDigestAlgorithm(digestAlg);
            return AasDigest.ComputeHex(blob.Span, digestAlg);
        }

        /// <summary>
        /// Computes the OCI manifest digest with its lower-case algorithm prefix.
        /// </summary>
        public static string ComputeManifestDigest(ByteString manifestBytes, string ociAlgorithm)
        {
            string digestAlg = MapOciAlgorithm(ociAlgorithm);
            return ociAlgorithm + ":" + AasDigest.ComputeHex(manifestBytes.Span, digestAlg);
        }

        /// <summary>
        /// Verifies Consumer-side package metadata before a package is parsed or materialized.
        /// </summary>
        public static AasPackageIntegrityResult VerifyConsumerBlob(
            ByteString blob,
            string digestAlg,
            string digest)
        {
            return VerifyBlob(blob, digestAlg, digest);
        }

        /// <summary>
        /// Verifies the four-step OCI manifest-to-package binding.
        /// </summary>
        public static AasPackageIntegrityResult VerifyOciBinding(
            ByteString manifestBytes,
            string manifestDigest,
            ByteString blob,
            string digestAlg,
            string digest)
        {
            AasPackageIntegrityResult manifestResult = VerifyManifest(manifestBytes, manifestDigest);
            if (!manifestResult.Succeeded)
            {
                return manifestResult;
            }

            AasOciDescriptorResult descriptorResult = FindPackageLayer(manifestBytes);
            if (!descriptorResult.Succeeded)
            {
                return AasPackageIntegrityResult.Fail(descriptorResult.Message);
            }

            string descriptorAlgorithm;
            string descriptorDigest;
            if (!TrySplitOciDigest(descriptorResult.Digest, out descriptorAlgorithm, out descriptorDigest))
            {
                return AasPackageIntegrityResult.Fail("OCI package layer digest is not a lower-case prefixed digest.");
            }

            // TrySplitOciDigest accepts only sha256, sha384 and sha512, which
            // MapOciAlgorithm maps without exception, so the algorithm is known
            // to be one of the three by the time it is mapped.
            string descriptorDigestAlg = MapOciAlgorithm(descriptorAlgorithm);

            if (!string.Equals(descriptorDigestAlg, digestAlg, StringComparison.Ordinal) ||
                !string.Equals(descriptorDigest, digest, StringComparison.Ordinal))
            {
                return AasPackageIntegrityResult.Fail(
                    "OCI package layer digest does not match DigestAlg and Digest.");
            }

            return VerifyBlob(blob, digestAlg, digest);
        }

        /// <summary>
        /// Maps an OCI descriptor algorithm to the case-sensitive AAS DigestAlg value.
        /// </summary>
        public static string MapOciAlgorithm(string ociAlgorithm)
        {
            if (ociAlgorithm is null)
            {
                throw new ArgumentNullException(nameof(ociAlgorithm));
            }

            if (string.Equals(ociAlgorithm, "sha256", StringComparison.Ordinal))
            {
                return Sha256;
            }
            if (string.Equals(ociAlgorithm, "sha384", StringComparison.Ordinal))
            {
                return Sha384;
            }
            if (string.Equals(ociAlgorithm, "sha512", StringComparison.Ordinal))
            {
                return Sha512;
            }

            throw new ArgumentException(
                "OCI descriptor algorithm must be exactly sha256, sha384 or sha512.",
                nameof(ociAlgorithm));
        }

        /// <summary>
        /// Validates the case-sensitive AAS DigestAlg value.
        /// </summary>
        public static void ValidateDigestAlgorithm(string digestAlg)
        {
            if (digestAlg is null)
            {
                throw new ArgumentNullException(nameof(digestAlg));
            }

            if (!string.Equals(digestAlg, Sha256, StringComparison.Ordinal) &&
                !string.Equals(digestAlg, Sha384, StringComparison.Ordinal) &&
                !string.Equals(digestAlg, Sha512, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "DigestAlg must be exactly Sha256, Sha384 or Sha512.",
                    nameof(digestAlg));
            }
        }

        internal static AasPackageIntegrityResult VerifyBlob(
            ByteString blob,
            string digestAlg,
            string digest)
        {
            try
            {
                ValidateDigestAlgorithm(digestAlg);
            }
            catch (ArgumentException ex)
            {
                return AasPackageIntegrityResult.Fail(ex.Message);
            }

            if (!IsLowerHex(digest))
            {
                return AasPackageIntegrityResult.Fail(
                    "Digest must be lower-case hexadecimal without an algorithm prefix.");
            }

            string actual = ComputeDigest(blob, digestAlg);
            return string.Equals(actual, digest, StringComparison.Ordinal)
                ? AasPackageIntegrityResult.Success()
                : AasPackageIntegrityResult.Fail("Package blob digest does not match the published Digest.");
        }

        /// <summary>
        /// Constructs the always-hashed OCI VersionId from the exact ManifestDigest value.
        /// </summary>
        public static string VersionIdFromManifestDigest(string manifestDigest)
        {
            if (manifestDigest is null)
            {
                throw new ArgumentNullException(nameof(manifestDigest));
            }

            return "oci." + AasDigest.ComputeHex(Encoding.UTF8.GetBytes(manifestDigest), AasDigest.Sha256Name);
        }

        private static AasPackageIntegrityResult VerifyManifest(ByteString manifestBytes, string manifestDigest)
        {
            string algorithm;
            string digest;
            if (!TrySplitOciDigest(manifestDigest, out algorithm, out digest))
            {
                return AasPackageIntegrityResult.Fail(
                    "ManifestDigest must be a lower-case algorithm prefix and lower-case hexadecimal digest.");
            }

            string expected = ComputeManifestDigest(manifestBytes, algorithm);
            return string.Equals(expected, manifestDigest, StringComparison.Ordinal)
                ? AasPackageIntegrityResult.Success()
                : AasPackageIntegrityResult.Fail("OCI manifest bytes do not match ManifestDigest.");
        }

        private static AasOciDescriptorResult FindPackageLayer(ByteString manifestBytes)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(manifestBytes.Memory);
                if (!document.RootElement.TryGetProperty("layers", out JsonElement layers) ||
                    layers.ValueKind != JsonValueKind.Array)
                {
                    return AasOciDescriptorResult.Fail("OCI manifest does not contain a layers array.");
                }

                string digest = string.Empty;
                int count = 0;
                foreach (JsonElement layer in layers.EnumerateArray())
                {
                    if (layer.ValueKind != JsonValueKind.Object ||
                        !layer.TryGetProperty("digest", out JsonElement digestElement) ||
                        digestElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    count++;
                    digest = digestElement.GetString() ?? string.Empty;
                }

                if (count != 1)
                {
                    return AasOciDescriptorResult.Fail(
                        "OCI manifest must contain exactly one package-layer descriptor.");
                }

                return AasOciDescriptorResult.Success(digest);
            }
            catch (JsonException ex)
            {
                return AasOciDescriptorResult.Fail("OCI manifest is not valid JSON: " + ex.Message);
            }
        }

        private static bool TrySplitOciDigest(
            string value,
            out string algorithm,
            out string digest)
        {
            algorithm = string.Empty;
            digest = string.Empty;
            if (value is null)
            {
                return false;
            }

            int separator = IndexOfColon(value);
            if (separator <= 0 || separator == value.Length - 1)
            {
                return false;
            }

            algorithm = value.Substring(0, separator);
            digest = value.Substring(separator + 1);
            return IsLowerHex(digest) &&
                (string.Equals(algorithm, "sha256", StringComparison.Ordinal) ||
                string.Equals(algorithm, "sha384", StringComparison.Ordinal) ||
                string.Equals(algorithm, "sha512", StringComparison.Ordinal));
        }

        private static bool IsLowerHex(string value)
        {
            return !ContainsColon(value) && AasDigest.IsHex(value);
        }

        private static int IndexOfColon(string value)
        {
            for (int ii = 0; ii < value.Length; ii++)
            {
                if (value[ii] == ':')
                {
                    return ii;
                }
            }

            return -1;
        }

        private static bool ContainsColon(string value)
        {
            return IndexOfColon(value) >= 0;
        }

        /// <summary>
        /// The case-sensitive AAS spelling for SHA-256.
        /// </summary>
        public const string Sha256 = AasDigest.Sha256Name;

        /// <summary>
        /// The case-sensitive AAS spelling for SHA-384.
        /// </summary>
        public const string Sha384 = AasDigest.Sha384Name;

        /// <summary>
        /// The case-sensitive AAS spelling for SHA-512.
        /// </summary>
        public const string Sha512 = AasDigest.Sha512Name;
    }

    /// <summary>
    /// Result of an AAS package integrity check.
    /// </summary>
    public readonly struct AasPackageIntegrityResult : IEquatable<AasPackageIntegrityResult>
    {
        /// <summary>
        /// Initializes a package integrity result.
        /// </summary>
        public AasPackageIntegrityResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
        }

        /// <summary>
        /// Whether the integrity check succeeded.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Diagnostic text for a failed integrity check.
        /// </summary>
        public string Message { get; }

        /// <inheritdoc/>
        public bool Equals(AasPackageIntegrityResult other)
        {
            return Succeeded == other.Succeeded &&
                string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is AasPackageIntegrityResult other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Succeeded ? 1 : 0) * 397) ^ StringComparer.Ordinal.GetHashCode(Message);
            }
        }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static AasPackageIntegrityResult Success()
        {
            return new AasPackageIntegrityResult(true, string.Empty);
        }

        /// <summary>
        /// Creates a failed result.
        /// </summary>
        public static AasPackageIntegrityResult Fail(string message)
        {
            return new AasPackageIntegrityResult(false, message);
        }

        /// <summary>
        /// Compares two integrity results for equality.
        /// </summary>
        public static bool operator ==(AasPackageIntegrityResult left, AasPackageIntegrityResult right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two integrity results for inequality.
        /// </summary>
        public static bool operator !=(AasPackageIntegrityResult left, AasPackageIntegrityResult right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Immutable AAS package version metadata and exact readable blob.
    /// </summary>
    public sealed class AasPackageVersion
    {
        /// <summary>
        /// Initializes an immutable package version.
        /// </summary>
        public AasPackageVersion(
            string versionId,
            ByteString blob,
            string digest,
            string digestAlg,
            string? manifestDigest,
            string? ociTag)
        {
            VersionId = versionId ?? throw new ArgumentNullException(nameof(versionId));
            Blob = blob.Copy();
            Digest = digest ?? throw new ArgumentNullException(nameof(digest));
            DigestAlg = digestAlg ?? throw new ArgumentNullException(nameof(digestAlg));
            ManifestDigest = manifestDigest;
            OciTag = ociTag;
        }

        /// <summary>
        /// Immutable VersionId.
        /// </summary>
        public string VersionId { get; }

        /// <summary>
        /// Exact package blob bytes returned by FileType Read.
        /// </summary>
        public ByteString Blob { get; }

        /// <summary>
        /// Lower-case hexadecimal package blob digest without an algorithm prefix.
        /// </summary>
        public string Digest { get; }

        /// <summary>
        /// Case-sensitive DigestAlg value.
        /// </summary>
        public string DigestAlg { get; }

        /// <summary>
        /// OCI manifest digest with lower-case algorithm prefix when the version is OCI-backed.
        /// </summary>
        public string? ManifestDigest { get; }

        /// <summary>
        /// Mutable Resource-level tag that located this immutable version, if any.
        /// </summary>
        public string? OciTag { get; }
    }

    /// <summary>
    /// Request for publishing a verified AAS package blob version.
    /// </summary>
    public sealed class AasPackagePublishRequest
    {
        /// <summary>
        /// Initializes a package publish request.
        /// </summary>
        public AasPackagePublishRequest(
            string packageIdentifier,
            string versionId,
            ByteString blob,
            string digest,
            string digestAlg)
        {
            PackageIdentifier = packageIdentifier ?? throw new ArgumentNullException(nameof(packageIdentifier));
            VersionId = versionId ?? throw new ArgumentNullException(nameof(versionId));
            Blob = blob;
            Digest = digest ?? throw new ArgumentNullException(nameof(digest));
            DigestAlg = digestAlg ?? throw new ArgumentNullException(nameof(digestAlg));
        }

        /// <summary>
        /// PackageIdentifier value of the AASPackageFileType resource.
        /// </summary>
        public string PackageIdentifier { get; }

        /// <summary>
        /// Immutable VersionId.
        /// </summary>
        public string VersionId { get; }

        /// <summary>
        /// Exact package blob bytes that FileType Read will return.
        /// </summary>
        public ByteString Blob { get; }

        /// <summary>
        /// Published Digest property.
        /// </summary>
        public string Digest { get; }

        /// <summary>
        /// Published DigestAlg property.
        /// </summary>
        public string DigestAlg { get; }
    }

    /// <summary>
    /// Request for publishing an OCI-backed AAS package version.
    /// </summary>
    public sealed class AasOciPackagePublishRequest
    {
        /// <summary>
        /// Initializes an OCI package publish request.
        /// </summary>
        public AasOciPackagePublishRequest(
            string packageIdentifier,
            ByteString manifestBytes,
            string manifestDigest,
            ByteString blob,
            string digest,
            string digestAlg,
            string? tag)
        {
            PackageIdentifier = packageIdentifier ?? throw new ArgumentNullException(nameof(packageIdentifier));
            ManifestBytes = manifestBytes;
            ManifestDigest = manifestDigest ?? throw new ArgumentNullException(nameof(manifestDigest));
            Blob = blob;
            Digest = digest ?? throw new ArgumentNullException(nameof(digest));
            DigestAlg = digestAlg ?? throw new ArgumentNullException(nameof(digestAlg));
            Tag = tag;
        }

        /// <summary>
        /// PackageIdentifier value of the AASPackageFileType resource.
        /// </summary>
        public string PackageIdentifier { get; }

        /// <summary>
        /// Exact OCI manifest bytes.
        /// </summary>
        public ByteString ManifestBytes { get; }

        /// <summary>
        /// Published ManifestDigest property.
        /// </summary>
        public string ManifestDigest { get; }

        /// <summary>
        /// Exact package blob bytes that FileType Read will return.
        /// </summary>
        public ByteString Blob { get; }

        /// <summary>
        /// Published Digest property.
        /// </summary>
        public string Digest { get; }

        /// <summary>
        /// Published DigestAlg property.
        /// </summary>
        public string DigestAlg { get; }

        /// <summary>
        /// Mutable Resource-level tag that located the manifest.
        /// </summary>
        public string? Tag { get; }
    }

    /// <summary>
    /// Stores immutable AAS package versions after integrity verification.
    /// </summary>
    public interface IAasPackageStore
    {
        /// <summary>
        /// Publishes a raw package version after verifying Digest and DigestAlg.
        /// </summary>
        ValueTask<AasPackageVersion> PublishAsync(
            AasPackagePublishRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes an OCI-backed package version after the four-step binding checks.
        /// </summary>
        ValueTask<AasPackageVersion> PublishOciAsync(
            AasOciPackagePublishRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads an already verified immutable package version.
        /// </summary>
        ValueTask<ByteString> ReadAsync(
            string packageIdentifier,
            string versionId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists retained immutable versions for a package resource.
        /// </summary>
        ValueTask<ArrayOf<AasPackageVersion>> ListVersionsAsync(
            string packageIdentifier,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records a separate referrer resource without mutating the package version collection.
        /// </summary>
        ValueTask AddReferrerAsync(
            string packageIdentifier,
            AasPackageReferrerResource referrer,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Separate immutable OCI referrer or attestation resource.
    /// </summary>
    public sealed class AasPackageReferrerResource
    {
        /// <summary>
        /// Initializes a referrer resource hint.
        /// </summary>
        public AasPackageReferrerResource(string resourceId, string artifactType, string digest)
        {
            ResourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
            ArtifactType = artifactType ?? throw new ArgumentNullException(nameof(artifactType));
            Digest = digest ?? throw new ArgumentNullException(nameof(digest));
        }

        /// <summary>
        /// Separate resource identifier.
        /// </summary>
        public string ResourceId { get; }

        /// <summary>
        /// Referrer artifact type.
        /// </summary>
        public string ArtifactType { get; }

        /// <summary>
        /// Referrer artifact digest.
        /// </summary>
        public string Digest { get; }
    }

    /// <summary>
    /// In-memory AAS package store with fail-closed integrity publication.
    /// </summary>
    public sealed class InMemoryAasPackageStore : IAasPackageStore
    {
        /// <summary>
        /// Initializes an empty in-memory package store.
        /// </summary>
        public InMemoryAasPackageStore()
        {
        }

        /// <summary>
        /// Monotonic package Resource epoch.
        /// </summary>
        public ulong Epoch { get; private set; }

        /// <summary>
        /// Last package Resource modification time.
        /// </summary>
        public DateTimeOffset ModifiedAt { get; private set; }

        /// <inheritdoc/>
        public ValueTask<AasPackageVersion> PublishAsync(
            AasPackagePublishRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();
            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyBlob(
                request.Blob, request.DigestAlg, request.Digest);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }

            var version = new AasPackageVersion(
                request.VersionId,
                request.Blob,
                request.Digest,
                request.DigestAlg,
                manifestDigest: null,
                ociTag: null);
            AddVersion(request.PackageIdentifier, version);
            return new ValueTask<AasPackageVersion>(version);
        }

        /// <inheritdoc/>
        public ValueTask<AasPackageVersion> PublishOciAsync(
            AasOciPackagePublishRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (request.Tag is not null && !IsValidOciTag(request.Tag))
            {
                throw new InvalidOperationException("OCI tag is not valid for an AAS package Resource alias.");
            }

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                request.ManifestBytes,
                request.ManifestDigest,
                request.Blob,
                request.DigestAlg,
                request.Digest);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }

            string versionId = AasPackageIntegrity.VersionIdFromManifestDigest(request.ManifestDigest);
            var version = new AasPackageVersion(
                versionId,
                request.Blob,
                request.Digest,
                request.DigestAlg,
                request.ManifestDigest,
                request.Tag);
            AddVersion(request.PackageIdentifier, version);
            if (request.Tag is not null)
            {
                m_tags[request.PackageIdentifier + "\n" + request.Tag] = versionId;
            }

            return new ValueTask<AasPackageVersion>(version);
        }

        /// <inheritdoc/>
        public ValueTask<ByteString> ReadAsync(
            string packageIdentifier,
            string versionId,
            CancellationToken cancellationToken = default)
        {
            if (packageIdentifier is null)
            {
                throw new ArgumentNullException(nameof(packageIdentifier));
            }
            if (versionId is null)
            {
                throw new ArgumentNullException(nameof(versionId));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (m_versions.TryGetValue(packageIdentifier, out List<AasPackageVersion>? versions))
            {
                foreach (AasPackageVersion version in versions)
                {
                    if (string.Equals(version.VersionId, versionId, StringComparison.Ordinal))
                    {
                        return new ValueTask<ByteString>(version.Blob.Copy());
                    }
                }
            }

            throw new KeyNotFoundException("Package version was not found.");
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<AasPackageVersion>> ListVersionsAsync(
            string packageIdentifier,
            CancellationToken cancellationToken = default)
        {
            if (packageIdentifier is null)
            {
                throw new ArgumentNullException(nameof(packageIdentifier));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!m_versions.TryGetValue(packageIdentifier, out List<AasPackageVersion>? versions))
            {
                return new ValueTask<ArrayOf<AasPackageVersion>>(new ArrayOf<AasPackageVersion>());
            }

            return new ValueTask<ArrayOf<AasPackageVersion>>(
                new ArrayOf<AasPackageVersion>(versions.ToArray()));
        }

        /// <inheritdoc/>
        public ValueTask AddReferrerAsync(
            string packageIdentifier,
            AasPackageReferrerResource referrer,
            CancellationToken cancellationToken = default)
        {
            if (packageIdentifier is null)
            {
                throw new ArgumentNullException(nameof(packageIdentifier));
            }
            if (referrer is null)
            {
                throw new ArgumentNullException(nameof(referrer));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!m_referrers.TryGetValue(packageIdentifier, out List<AasPackageReferrerResource>? referrers))
            {
                referrers = new List<AasPackageReferrerResource>();
                m_referrers.Add(packageIdentifier, referrers);
            }

            referrers.Add(referrer);
            return default;
        }

        private void AddVersion(string packageIdentifier, AasPackageVersion version)
        {
            if (!m_versions.TryGetValue(packageIdentifier, out List<AasPackageVersion>? versions))
            {
                versions = new List<AasPackageVersion>();
                m_versions.Add(packageIdentifier, versions);
            }

            foreach (AasPackageVersion existing in versions)
            {
                if (string.Equals(existing.VersionId, version.VersionId, StringComparison.Ordinal))
                {
                    return;
                }
            }

            versions.Add(version);
            Epoch++;
            ModifiedAt = DateTimeOffset.UtcNow;
        }

        private static bool IsValidOciTag(string tag)
        {
            if (tag.Length == 0 || tag.Length > 128 || !IsOciTagFirst(tag[0]))
            {
                return false;
            }

            for (int ii = 1; ii < tag.Length; ii++)
            {
                if (!IsOciTagRest(tag[ii]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsOciTagFirst(char c)
        {
            return IsAsciiLetterOrDigit(c) || c == '_';
        }

        private static bool IsOciTagRest(char c)
        {
            return IsAsciiLetterOrDigit(c) || c == '_' || c == '.' || c == '-';
        }

        private static bool IsAsciiLetterOrDigit(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
        }

        private readonly Dictionary<string, List<AasPackageVersion>> m_versions =
            new Dictionary<string, List<AasPackageVersion>>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> m_tags =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<string, List<AasPackageReferrerResource>> m_referrers =
            new Dictionary<string, List<AasPackageReferrerResource>>(StringComparer.Ordinal);
    }

    internal readonly struct AasOciDescriptorResult
    {
        public AasOciDescriptorResult(bool succeeded, string digest, string message)
        {
            Succeeded = succeeded;
            Digest = digest ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }

        public string Digest { get; }

        public string Message { get; }

        public static AasOciDescriptorResult Success(string digest)
        {
            return new AasOciDescriptorResult(true, digest, string.Empty);
        }

        public static AasOciDescriptorResult Fail(string message)
        {
            return new AasOciDescriptorResult(false, string.Empty, message);
        }
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers AAS package integrity services.
    /// </summary>
    public static class AasPackageServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the default AAS package store.
        /// </summary>
        public static IServiceCollection AddAasPackageStore(this IServiceCollection services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<IAasPackageStore, InMemoryAasPackageStore>();
            return services;
        }
    }
}
