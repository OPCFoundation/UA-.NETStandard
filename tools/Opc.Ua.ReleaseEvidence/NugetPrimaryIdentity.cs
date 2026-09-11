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
using System.Formats.Asn1;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Identifies the primary NuGet signature bytes and signer certificate without authenticating the signature.
    /// </summary>
    /// <param name="SignatureDigest">The digest of the complete primary signature entry stored in the package.</param>
    /// <param name="CertificateDigest">The SHA-256 digest of the primary signer's encoded certificate.</param>
    internal sealed record NugetPrimaryIdentity(string SignatureDigest, string CertificateDigest)
    {
        /// <summary>
        /// Reads bounded primary-signature identity metadata, returning null when a unique signer cannot be identified.
        /// </summary>
        public static async Task<NugetPrimaryIdentity?> ReadAsync(string path, CancellationToken cancellationToken)
        {
            EvidenceFiles.RejectLinks(path);
            using ZipArchive archive = ZipFile.OpenRead(path);
            PackageReconciler.ValidateEntries(archive);
            ZipArchiveEntry? entry = archive.GetEntry(".signature.p7s");
            if (entry == null)
            {
                return null;
            }
            if (entry.Length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("Primary NuGet signature exceeds its size bound.");
            }
            using Stream stream = entry.Open();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            byte[] bytes = buffer.ToArray();
            try
            {
                var reader = new AsnReader(bytes, AsnEncodingRules.BER);
                AsnReader content = reader.ReadSequence();
                if (content.ReadObjectIdentifier() != "1.2.840.113549.1.7.2")
                {
                    return null;
                }
                AsnReader tagged = content.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                AsnReader signed = tagged.ReadSequence();
                signed.ReadInteger();
                signed.ReadSetOf();
                signed.ReadEncodedValue();
                if (!signed.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                {
                    return null;
                }
                AsnReader certificates = signed.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                var encoded = new List<byte[]>();
                while (certificates.HasData)
                {
                    if (encoded.Count == 64)
                    {
                        throw new InvalidDataException("Primary NuGet signature has excessive certificate scope.");
                    }
                    encoded.Add(certificates.ReadEncodedValue().ToArray());
                }
                if (signed.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
                {
                    signed.ReadEncodedValue();
                }
                AsnReader signers = signed.ReadSetOf();
                AsnReader signer = signers.ReadSequence();
                signers.ThrowIfNotEmpty();
                if (signer.ReadInteger() != BigInteger.One)
                {
                    return null;
                }
                AsnReader identifier = signer.ReadSequence();
                byte[] issuer = identifier.ReadEncodedValue().ToArray();
                BigInteger serial = identifier.ReadInteger();
                identifier.ThrowIfNotEmpty();
                string? fingerprint = null;
                foreach (byte[] certificateBytes in encoded)
                {
                    using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
                    if (certificate.IssuerName.RawData.AsSpan().SequenceEqual(issuer) &&
                        new BigInteger(certificate.GetSerialNumber(), isUnsigned: true) == serial)
                    {
                        if (fingerprint != null)
                        {
                            return null;
                        }
                        fingerprint = EvidenceFiles.Digest(certificate.RawData);
                    }
                }
                return fingerprint == null ? null : new NugetPrimaryIdentity(EvidenceFiles.Digest(bytes), fingerprint);
            }
            catch (Exception ex) when (ex is AsnContentException or CryptographicException)
            {
                return null;
            }
        }
    }
}
