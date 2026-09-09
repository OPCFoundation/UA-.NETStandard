// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed record DeliveryContentEntry(string Path, long Size, string Digest);

    internal sealed record NugetDeliveryContentReport(
        int SchemaVersion,
        string Status,
        string PackageId,
        string Version,
        string AuthorArchiveDigest,
        string DeliveredArchiveDigest,
        string AuthorContentDigest,
        string DeliveredContentDigest,
        bool ContentPreserved,
        bool AuthorSignaturePreserved,
        bool SignatureVerificationPerformed,
        DeliveryContentEntry[] Content,
        string[] UnmetControls,
        Finding[] Findings);

    /// <summary>
    /// Compares delivery content without treating a preserved CMS signature as an authenticated signature.
    /// </summary>
    internal sealed class NugetDeliveryVerifier(EvidenceFiles files)
    {
        public async Task<int> VerifyAsync(
            string authorPath,
            string deliveredPath,
            string output,
            CancellationToken cancellationToken)
        {
            if (!authorPath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                !deliveredPath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Delivery comparison supports normal NuGet packages only; symbols remain unresolved.");
            }
            PackageContent author = await ReadPackageAsync(authorPath, cancellationToken).ConfigureAwait(false);
            PackageContent delivered = await ReadPackageAsync(deliveredPath, cancellationToken).ConfigureAwait(false);
            bool sameIdentity = string.Equals(author.Id, delivered.Id, StringComparison.OrdinalIgnoreCase) &&
                Versions.Equal(author.Version, delivered.Version);
            bool sameContent = sameIdentity && author.Entries.SequenceEqual(delivered.Entries);
            bool sameSignature = author.Signature != null &&
                delivered.Signature != null &&
                author.Signature.AsSpan().SequenceEqual(delivered.Signature);
            var findings = new List<Finding>();
            if (!sameIdentity)
            {
                findings.Add(new Finding(
                    "ARTIFACT_INTEGRITY", "Delivered package identity differs from the author archive."));
            }
            if (!sameContent)
            {
                findings.Add(new Finding(
                    "ARTIFACT_INTEGRITY", "Delivered package has different, missing, or added uncompressed content."));
            }
            if (!sameSignature)
            {
                findings.Add(new Finding(
                    "SIGNATURE_VERIFIED", "The original primary CMS signed content and signer are not preserved."));
            }
            findings.Add(new Finding(
                "SIGNATURE_VERIFIED",
                "Content comparison does not authenticate archives; independently verify approved NuGet signatures."));
            var report = new NugetDeliveryContentReport(
                1, sameContent && sameSignature ? "content-matched" : "content-mismatch",
                author.Id, author.Version, author.ArchiveDigest, delivered.ArchiveDigest,
                ContentDigest(author.Entries), ContentDigest(delivered.Entries),
                sameContent, sameSignature, false, author.Entries,
                [.. findings.Select(f => f.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                [.. findings]);
            await files.WriteModelAsync(
                output, report, DeliveryJsonContext.Default.NugetDeliveryContentReport, cancellationToken)
                .ConfigureAwait(false);
            return sameContent && sameSignature ? 0 : 1;
        }

        private async Task<PackageContent> ReadPackageAsync(string path, CancellationToken cancellationToken)
        {
            EvidenceFiles.RejectLinks(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            string archiveDigest = "sha256:" + Convert.ToHexStringLower(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
            PackageReconciler.ValidateEntries(archive);
            ZipArchiveEntry[] nuspecs = [.. archive.Entries.Where(e =>
                !e.FullName.Contains('/', StringComparison.Ordinal) &&
                e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))];
            if (nuspecs.Length != 1)
            {
                throw new InvalidDataException("A package must contain exactly one root nuspec.");
            }
            XElement metadata = NuspecMetadata.Read(
                await ReadMetadataAsync(nuspecs[0], cancellationToken).ConfigureAwait(false));
            string id = NuspecMetadata.Required(metadata, "id");
            string version = NuspecMetadata.Required(metadata, "version");
            if (id.Length > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
            {
                throw new InvalidDataException("Invalid embedded NuGet package ID.");
            }
            Versions.Parse(version);
            var entries = new List<DeliveryContentEntry>();
            byte[]? signature = null;
            foreach (ZipArchiveEntry entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (entry.FullName.EndsWith('/'))
                {
                    continue;
                }
                if (string.Equals(entry.FullName, ".signature.p7s", StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.FullName != ".signature.p7s")
                    {
                        throw new InvalidDataException("The NuGet signature entry has noncanonical casing.");
                    }
                    signature = ReadPrimarySignature(
                        await ReadMetadataAsync(entry, cancellationToken).ConfigureAwait(false));
                    continue;
                }
                using Stream content = entry.Open();
                byte[] hash = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
                entries.Add(new DeliveryContentEntry(
                    entry.FullName, entry.Length, "sha256:" + Convert.ToHexStringLower(hash)));
            }
            return new PackageContent(id, version, archiveDigest, [.. entries], signature);
        }

        private static async Task<byte[]> ReadMetadataAsync(
            ZipArchiveEntry entry,
            CancellationToken cancellationToken)
        {
            if (entry.Length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("NuGet signature or nuspec exceeds the metadata-size limit.");
            }
            using Stream content = entry.Open();
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }

        private static byte[] ReadPrimarySignature(ReadOnlyMemory<byte> bytes)
        {
            try
            {
                var input = new AsnReader(bytes, AsnEncodingRules.BER);
                AsnReader contentInfo = input.ReadSequence();
                if (contentInfo.ReadObjectIdentifier() != "1.2.840.113549.1.7.2")
                {
                    throw new InvalidDataException("The NuGet signature is not CMS SignedData.");
                }
                AsnReader explicitContent = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                AsnReader signedData = explicitContent.ReadSequence();
                signedData.ReadInteger();
                signedData.ReadSetOf();
                ReadOnlyMemory<byte> encapsulatedContent = signedData.ReadEncodedValue();
                if (signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                {
                    signedData.ReadEncodedValue();
                }
                if (signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
                {
                    signedData.ReadEncodedValue();
                }
                AsnReader signers = signedData.ReadSetOf();
                AsnReader signer = signers.ReadSequence();
                var writer = new AsnWriter(AsnEncodingRules.DER);
                using (writer.PushSequence())
                {
                    writer.WriteEncodedValue(encapsulatedContent.Span);
                    writer.WriteInteger(signer.ReadInteger());
                    writer.WriteEncodedValue(signer.ReadEncodedValue().Span);
                    writer.WriteEncodedValue(signer.ReadEncodedValue().Span);
                    if (signer.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                    {
                        writer.WriteEncodedValue(signer.ReadEncodedValue().Span);
                    }
                    writer.WriteEncodedValue(signer.ReadEncodedValue().Span);
                    writer.WriteOctetString(signer.ReadOctetString());
                }
                // Repository countersigning changes unsigned attributes, not the primary signed identity.
                if (signer.HasData)
                {
                    signer.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 1, true));
                }
                signer.ThrowIfNotEmpty();
                signers.ThrowIfNotEmpty();
                signedData.ThrowIfNotEmpty();
                explicitContent.ThrowIfNotEmpty();
                contentInfo.ThrowIfNotEmpty();
                input.ThrowIfNotEmpty();
                return writer.Encode();
            }
            catch (AsnContentException exception)
            {
                throw new InvalidDataException("The NuGet primary CMS signature is malformed.", exception);
            }
        }

        private static string ContentDigest(DeliveryContentEntry[] entries)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (DeliveryContentEntry entry in entries)
            {
                // Length-delimited fields avoid ambiguity from package-controlled names.
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                    entry, DeliveryJsonContext.Default.DeliveryContentEntry);
                hash.AppendData(Encoding.ASCII.GetBytes(
                    bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                hash.AppendData(":"u8);
                hash.AppendData(bytes);
            }
            return "sha256:" + Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        private sealed record PackageContent(
            string Id,
            string Version,
            string ArchiveDigest,
            DeliveryContentEntry[] Entries,
            byte[]? Signature);
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true)]
    [JsonSerializable(typeof(NugetDeliveryContentReport))]
    [JsonSerializable(typeof(DeliveryContentEntry))]
    internal sealed partial class DeliveryJsonContext : JsonSerializerContext;
}
