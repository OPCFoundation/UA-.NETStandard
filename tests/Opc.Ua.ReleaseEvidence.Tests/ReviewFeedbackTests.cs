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
using System.Formats.Asn1;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.ReleaseEvidence.Tests
{
    /// <summary>
    /// Reproduces input-boundary issues identified during review of release verification.
    /// </summary>
    [TestFixture]
    public sealed class ReviewFeedbackTests
    {
        /// <summary>
        /// Keeps the checked-in contract active without treating missing publication approval as verified.
        /// </summary>
        [Test]
        public async Task CheckedInPolicyRequiresStableEvidenceWithoutInventingPublisherApprovalAsync()
        {
            string path = Path.Combine(FindRepositoryRoot(), ".azurepipelines", "release", "policy.json");
            using JsonDocument document = await new EvidenceFiles().ReadJsonAsync(path, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(document.RootElement.GetProperty("stage").GetString(), Is.EqualTo("required"));
            Assert.That(document.RootElement.GetProperty("requiredChannel").GetString(), Is.EqualTo("stable"));
            Assert.That(document.RootElement.GetProperty("publisherBoundaryVerified").GetBoolean(), Is.False);
        }

        /// <summary>
        /// Rejects an unknown schema through the documented invalid-input exception flow.
        /// </summary>
        [Test]
        public async Task UnknownSchemaIsInvalidInputRatherThanAnUncaughtDictionaryLookupAsync()
        {
            VerificationSchemas schemas = await VerificationSchemas.LoadAsync(
                FindRepositoryRoot(),
                new EvidenceFiles(), CancellationToken.None).ConfigureAwait(false);
            using var document = JsonDocument.Parse("{}");
            Assert.That(() => schemas.Validate("unknown.schema.json", document.RootElement),
                Throws.TypeOf<InvalidDataException>());
        }

        /// <summary>
        /// Resolves an unqualified archive path before enforcing the independent-verifier directory boundary.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RelativeArchivePathStillRejectsACandidateControlledVerifierAsync(bool dotRelative)
        {
            string fileName = "review-signature-" + Guid.NewGuid().ToString("N") + ".nupkg";
            string fullPath = Path.Combine(Environment.CurrentDirectory, fileName);
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Ephemeral review fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
            try
            {
                using (ZipArchive archive = ZipFile.Open(fullPath, ZipArchiveMode.Create))
                {
                    using Stream signature = archive.CreateEntry(".signature.p7s").Open();
                    await signature.WriteAsync(CreateSignature(certificate, key)).ConfigureAwait(false);
                }
                NugetPrimaryIdentity identity = await NugetPrimaryIdentity.ReadAsync(fullPath, CancellationToken.None)
                    .ConfigureAwait(false) ?? throw new InvalidDataException("The synthetic primary signature is missing.");
                string digest = await new EvidenceFiles().DigestAsync(fullPath, CancellationToken.None)
                    .ConfigureAwait(false);
                var proof = new ArtifactSignatureProof(
                    "nuget-package", "Fixture", digest, "unused.sigstore.json",
                    identity.SignatureDigest, identity.CertificateDigest);
                var pin = new VerifierToolPin(
                    Path.Combine(Environment.CurrentDirectory, "candidate-verifier.exe"), digest, "10.0.0");
                var policy = new TrustedPolicySnapshot(
                    1, "isolated-review-fixture", "required", 1, DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow.AddMinutes(10), digest, [], new("nuget", "2.0.0", "stable"),
                    digest, [], [], [], pin, pin, pin, [identity.CertificateDigest]);
                var verifier = new NugetAuthorSignatureVerifier(new EvidenceFiles(), new ProcessRunner());
                string relative = dotRelative ? "." + Path.DirectorySeparatorChar + fileName : fileName;
                Assert.That(() => verifier.VerifyAsync(
                    relative, proof, Path.Combine(Environment.CurrentDirectory, "proofs"),
                    policy, CancellationToken.None), Throws.TypeOf<InvalidDataException>()
                        .With.Message.EqualTo("Trust material or verifier is inside candidate inputs."));
            }
            finally
            {
                File.Delete(fullPath);
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? root = new(TestContext.CurrentContext.TestDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "UA.slnx")))
            {
                root = root.Parent;
            }
            return root?.FullName ?? throw new DirectoryNotFoundException("Repository root is required.");
        }

        private static byte[] CreateSignature(X509Certificate2 certificate, RSA key)
        {
            byte[] payload = "Synthetic signature identity for a path-boundary test."u8.ToArray();
            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.2.840.113549.1.7.2");
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                using (writer.PushSequence())
                {
                    writer.WriteInteger(1);
                    using (writer.PushSetOf())
                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
                    }
                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier("1.2.840.113549.1.7.1");
                        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                        {
                            writer.WriteOctetString(payload);
                        }
                    }
                    using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                    {
                        writer.WriteEncodedValue(certificate.RawData);
                    }
                    using (writer.PushSetOf())
                    using (writer.PushSequence())
                    {
                        writer.WriteInteger(1);
                        using (writer.PushSequence())
                        {
                            writer.WriteEncodedValue(certificate.IssuerName.RawData);
                            writer.WriteInteger(new BigInteger(certificate.GetSerialNumber(), isUnsigned: true));
                        }
                        using (writer.PushSequence())
                        {
                            writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
                        }
                        using (writer.PushSequence())
                        {
                            writer.WriteObjectIdentifier("1.2.840.113549.1.1.1");
                            writer.WriteNull();
                        }
                        writer.WriteOctetString(key.SignData(
                            payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                    }
                }
            }
            return writer.Encode();
        }
    }
}
