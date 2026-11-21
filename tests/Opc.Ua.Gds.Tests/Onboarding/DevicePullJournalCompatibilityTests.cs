/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Formats.Asn1;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OnboardingDevice;
using OnboardingRegistrar;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the linked journal and checkpoint writer on every supported test target.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DevicePullJournalCompatibilityTests
    {
        /// <summary>
        /// Creates RSA identities shared by this fixture.
        /// </summary>
        [OneTimeSetUp]
        public void CreateCertificates()
        {
            m_signing = CreateIdentity("CN=Journal Signing", kApplicationUri);
            m_other = CreateIdentity("CN=Other Signing", kApplicationUri);
            m_registrar = CreateIdentity("CN=Journal Registrar", kRegistrarUri);
            m_binding = new PullDcaSigningBinding(
                new PullDcaRegistrar(
                    new Uri("opc.tcp://localhost:4840"),
                    new NodeId("registrar", 1),
                    new NodeId("directory", 1)),
                new NodeId("device", 1),
                new NodeId("request", 1),
                new PullDcaCertificateProfile(
                    new NodeId("group", 1), Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                CreateSigningRequest(m_signing),
                Encoded(m_signing),
                Encoded(m_signing),
                Encoded(m_registrar),
                kRegistrarUri,
                [Opc.Ua.Namespaces.OpcUa, "urn:journal-compatibility"],
                Guid.NewGuid());
        }

        /// <summary>
        /// Releases fixture-owned certificate handles.
        /// </summary>
        [OneTimeTearDown]
        public void DisposeCertificates()
        {
            m_signing?.Dispose();
            m_other?.Dispose();
            m_registrar?.Dispose();
        }

        /// <summary>
        /// Creates isolated state for each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(
                Path.GetTempPath(), nameof(DevicePullJournalCompatibilityTests), Guid.NewGuid().ToString("N"));
            m_telemetry = NUnitTelemetryContext.Create();
        }

        /// <summary>
        /// Releases telemetry and removes this test's checkpoint directory.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            (m_telemetry as IDisposable)?.Dispose();
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        /// <summary>
        /// A matching CSR and issued key remain valid after the public checkpoint is reopened.
        /// </summary>
        [Test]
        public async Task MatchingSigningAndIssuedKeysSurviveWriterRestart()
        {
            var request = new Pkcs10CertificationRequest(m_binding.SigningRequest.ToArray());
            Assert.That(request.Verify(), Is.True);
#if NET6_0_OR_GREATER
            using (RSA publicKey = m_signing.GetRSAPublicKey()!)
            {
                Assert.That(request.SubjectPublicKeyInfo, Is.EqualTo(publicKey.ExportSubjectPublicKeyInfo()));
            }
#endif
            using Certificate issuer = CertificateBuilder.Create("CN=Journal Issuer")
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate issued = CertificateBuilder.Create("CN=Journal Reissued Device")
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .AddExtension(new X509SubjectAltNameExtension(kApplicationUri, ["localhost"]))
                .SetIssuer(issuer)
                .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                .CreateForRSA();
            ByteString issuedBytes = Encoded(issued);
            Assert.That(issuedBytes, Is.Not.EqualTo(Encoded(m_signing)));
            using (var files = new PushRegistrarCheckpointFiles(m_directory))
            {
                DevicePullJournal journal = CreateJournal(files);
                await RecordStartedAsync(journal).ConfigureAwait(false);
                var receipt = new PullDcaCertificateReceipt(issuedBytes, [Encoded(issuer)]);
                PullDcaCertificateReceipt received = await journal.GetOrCreateAsync(
                    m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                    _ => new ValueTask<PullDcaCertificateReceipt>(receipt)).ConfigureAwait(false);
                Assert.That(received.Certificate, Is.EqualTo(receipt.Certificate));
            }

            using var reopened = new PushRegistrarCheckpointFiles(m_directory);
            DevicePullJournalCheckpoint? checkpoint = await CreateJournal(reopened).ReadAsync().ConfigureAwait(false);
            Assert.That(checkpoint, Is.Not.Null);
            Assert.That(checkpoint!.Stage, Is.EqualTo(DevicePullJournalStage.CertificateReceived));
            Assert.That(checkpoint.Binding.SigningRequest, Is.EqualTo(m_binding.SigningRequest));
            Assert.That(checkpoint.Receipt!.Certificate, Is.EqualTo(issuedBytes));
            Assert.That(checkpoint.Receipt.IssuerCertificates.Count, Is.EqualTo(1));
            Assert.That(checkpoint.Receipt.IssuerCertificates[0], Is.EqualTo(Encoded(issuer)));
        }

        /// <summary>
        /// A valid CSR signed by a different key cannot claim the retained signing certificate.
        /// </summary>
        [Test]
        public async Task DifferentSigningKeyCannotPublishDispatch()
        {
            ByteString otherRequest = CreateSigningRequest(m_other);
            Assert.That(new Pkcs10CertificationRequest(otherRequest.ToArray()).Verify(), Is.True);
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            DevicePullJournal journal = CreateJournal(files);
            PullDcaSigningBinding binding = m_binding with
            {
                RequestId = NodeId.Null,
                SigningRequest = otherRequest
            };

            await Assert.ThatAsync(async () => await journal.RecordDispatchAsync(binding).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(await journal.ReadAsync().ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// A valid signature does not make an RSA CSR without canonical algorithm parameters acceptable.
        /// </summary>
        [Test]
        public async Task SignedRequestWithoutRsaNullParameterCannotPublishDispatch()
        {
            byte[] csr = CreateRequestWithoutRsaNull(m_signing);
            Assert.That(new Pkcs10CertificationRequest(csr).Verify(), Is.True);
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            DevicePullJournal journal = CreateJournal(files);
            PullDcaSigningBinding binding = m_binding with
            {
                RequestId = NodeId.Null,
                SigningRequest = ByteString.From(csr.AsSpan())
            };

            await Assert.ThatAsync(async () => await journal.RecordDispatchAsync(binding).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(await journal.ReadAsync().ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// An issued certificate for another key is not retained as the signing request's receipt.
        /// </summary>
        [Test]
        public async Task DifferentIssuedKeyCannotPublishReceipt()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            DevicePullJournal journal = CreateJournal(files);
            await RecordStartedAsync(journal).ConfigureAwait(false);
            var receipt = new PullDcaCertificateReceipt(Encoded(m_other), []);

            await Assert.ThatAsync(async () => await journal.GetOrCreateAsync(
                m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                _ => new ValueTask<PullDcaCertificateReceipt>(receipt)).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
            DevicePullJournalCheckpoint? checkpoint = await journal.ReadAsync().ConfigureAwait(false);
            Assert.That(checkpoint, Is.Not.Null);
            Assert.That(checkpoint!.Stage, Is.EqualTo(DevicePullJournalStage.Receiving));
            Assert.That(checkpoint.Receipt, Is.Null);
        }

        /// <summary>
        /// Reset evidence is found by its digest after reopening the checkpoint writer.
        /// </summary>
        [Test]
        public async Task RetiredRequestDigestSurvivesWriterRestart()
        {
            using (var files = new PushRegistrarCheckpointFiles(m_directory))
            {
                DevicePullJournal journal = CreateJournal(files);
                await RecordStartedAsync(journal).ConfigureAwait(false);
                await journal.BeginResetAsync().ConfigureAwait(false);
                await journal.CompleteResetAsync().ConfigureAwait(false);
            }

            using var reopened = new PushRegistrarCheckpointFiles(m_directory);
            DevicePullJournal restored = CreateJournal(reopened);
            Assert.That(await restored.ReadAsync().ConfigureAwait(false), Is.Null);
            ArrayOf<DevicePullJournalCheckpoint> retired = await restored.ReadRetiredAsync().ConfigureAwait(false);
            Assert.That(retired.Count, Is.EqualTo(1));
            Assert.That(retired[0].Stage, Is.EqualTo(DevicePullJournalStage.Started));
            Assert.That(retired[0].Binding.RequestId, Is.EqualTo(m_binding.RequestId));
        }

        /// <summary>
        /// Bounded stream operations preserve slices and the established uppercase digest filename across restarts.
        /// </summary>
        [Test]
        public async Task CheckpointSlicesAndDigestFilenameSurviveWriterRestart()
        {
            byte[] bytes = [9, 1, 2, 3, 8, 7];
            using (var files = new PushRegistrarCheckpointFiles(m_directory))
            {
                await files.WriteAsync("abc", bytes.AsMemory(1, 3), CancellationToken.None).ConfigureAwait(false);
                Assert.That(await files.ReadAsync("abc", CancellationToken.None).ConfigureAwait(false),
                    Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(await files.CompareExchangeAsync(
                    "abc", new byte[] { 1, 2, 3 }, bytes.AsMemory(4, 2).ToArray(), CancellationToken.None)
                    .ConfigureAwait(false), Is.True);
            }

            using var reopened = new PushRegistrarCheckpointFiles(m_directory);
            Assert.That(await reopened.ReadAsync("abc", CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 8, 7 }));
            string[] checkpoints = Directory.GetFiles(m_directory, "*.bin");
            Assert.That(checkpoints, Has.Length.EqualTo(1));
            Assert.That(Path.GetFileName(checkpoints[0]), Is.EqualTo(
                "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD.bin"));
            Assert.That(Directory.GetFiles(m_directory, "*.pending"), Is.Empty);
        }

        /// <summary>
        /// The checkpoint directory guard preserves null and whitespace argument errors.
        /// </summary>
        [Test]
        public void CheckpointDirectoryMustBeNonblank()
        {
            Assert.That(() => new PushRegistrarCheckpointFiles(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("directory"));
            Assert.That(() => new PushRegistrarCheckpointFiles(" "),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("directory"));
        }

        /// <summary>
        /// Creates a journal bound to this fixture's device and registrar.
        /// </summary>
        private DevicePullJournal CreateJournal(PushRegistrarCheckpointFiles files)
        {
            return new DevicePullJournal(files, kApplicationUri, kRegistrarUri, m_telemetry);
        }

        /// <summary>
        /// Records the original dispatch and returned request identifier.
        /// </summary>
        private async Task RecordStartedAsync(DevicePullJournal journal)
        {
            await journal.RecordDispatchAsync(m_binding with { RequestId = NodeId.Null }).ConfigureAwait(false);
            await journal.RecordStartedAsync(m_binding).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates an RSA identity with the specified application URI.
        /// </summary>
        private static Certificate CreateIdentity(string subject, string applicationUri)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Generates a CSR using the supplied certificate's key.
        /// </summary>
        private static ByteString CreateSigningRequest(Certificate certificate)
        {
            return ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(certificate).AsSpan());
        }

        /// <summary>
        /// Copies the certificate's public DER.
        /// </summary>
        private static ByteString Encoded(Certificate certificate)
        {
            return ByteString.From(certificate.RawData.AsSpan());
        }

        /// <summary>
        /// Resigns a CSR after omitting the RSA AlgorithmIdentifier's NULL parameter.
        /// </summary>
        private static byte[] CreateRequestWithoutRsaNull(Certificate certificate)
        {
            byte[] original = DefaultCertificateFactory.Instance.CreateSigningRequest(certificate);
            var reader = new AsnReader(original, AsnEncodingRules.DER);
            AsnReader request = reader.ReadSequence();
            AsnReader originalInfo = new AsnReader(request.ReadEncodedValue(), AsnEncodingRules.DER).ReadSequence();
            ReadOnlyMemory<byte> signatureAlgorithm = request.ReadEncodedValue();
            ReadOnlyMemory<byte> version = originalInfo.ReadEncodedValue();
            ReadOnlyMemory<byte> subject = originalInfo.ReadEncodedValue();
            AsnReader publicKey = new AsnReader(originalInfo.ReadEncodedValue(), AsnEncodingRules.DER).ReadSequence();
            AsnReader algorithm = publicKey.ReadSequence();
            string oid = algorithm.ReadObjectIdentifier();
            algorithm.ReadNull();
            algorithm.ThrowIfNotEmpty();
            byte[] key = publicKey.ReadBitString(out int padding);
            if (oid != Oids.Rsa || padding != 0)
            {
                throw new InvalidDataException("The generated CSR does not contain a canonical RSA public key.");
            }
            publicKey.ThrowIfNotEmpty();

            var info = new AsnWriter(AsnEncodingRules.DER);
            info.PushSequence();
            info.WriteEncodedValue(version.Span);
            info.WriteEncodedValue(subject.Span);
            info.PushSequence();
            info.PushSequence();
            info.WriteObjectIdentifier(oid);
            info.PopSequence();
            info.WriteBitString(key);
            info.PopSequence();
            while (originalInfo.HasData)
            {
                info.WriteEncodedValue(originalInfo.ReadEncodedValue().Span);
            }
            info.PopSequence();
            byte[] encodedInfo = info.Encode();
            using RSA privateKey = certificate.GetRSAPrivateKey()!;
            byte[] signature = privateKey.SignData(encodedInfo, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var result = new AsnWriter(AsnEncodingRules.DER);
            result.PushSequence();
            result.WriteEncodedValue(encodedInfo);
            result.WriteEncodedValue(signatureAlgorithm.Span);
            result.WriteBitString(signature);
            result.PopSequence();
            return result.Encode();
        }

        /// <summary>The device application URI.</summary>
        private const string kApplicationUri = "urn:opcfoundation:tests:journal-compatibility";

        /// <summary>The registrar application URI.</summary>
        private const string kRegistrarUri = "urn:opcfoundation:tests:journal-compatibility-registrar";

        /// <summary>The signing identity.</summary>
        private Certificate m_signing = null!;

        /// <summary>A different RSA key for negative cases.</summary>
        private Certificate m_other = null!;

        /// <summary>The registrar identity.</summary>
        private Certificate m_registrar = null!;

        /// <summary>The retained request evidence.</summary>
        private PullDcaSigningBinding m_binding = null!;

        /// <summary>The isolated checkpoint directory.</summary>
        private string m_directory = null!;

        /// <summary>The telemetry context.</summary>
        private ITelemetryContext m_telemetry = null!;
    }
}
