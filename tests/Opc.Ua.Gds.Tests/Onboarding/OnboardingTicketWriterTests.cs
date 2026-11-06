/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    [TestFixture]
    [Category("Onboarding")]
    public sealed class OnboardingTicketWriterTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.CreateEmpty(m_telemetry);
            m_rsaCertificate = CreateRsaCertificate(2048, "CN=Ticket writer RSA key");
            m_issuerCertificate = CreateRsaCertificate(2048, "CN=Ticket issuer");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_rsaCertificate?.Dispose();
            m_issuerCertificate?.Dispose();
            (m_telemetry as IDisposable)?.Dispose();
        }

        [Test]
        public async Task WriteAsyncCreatesCompactReversibleSignedGeneralJwsAndPreservesProviderOwnership()
        {
            var identifier = new CertificateIdentifier { Thumbprint = m_rsaCertificate.Thumbprint };
            var provider = PrivateKeyProvider(m_rsaCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            var ticket = DeviceTicket();
            ArrayOf<ByteString> issuers = [new ByteString((ReadOnlyMemory<byte>)m_issuerCertificate.RawData)];
            string encoded = await writer.WriteAsync(ticket, identifier, issuers).ConfigureAwait(false);

            using JsonDocument envelope = JsonDocument.Parse(encoded);
            JsonElement root = envelope.RootElement;
            Assert.That(root.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(s_envelopeProperties));
            string payloadText = root.GetProperty("payload").GetString()!;
            JsonElement signatureEntry = root.GetProperty("signatures")[0];
            string protectedText = signatureEntry.GetProperty("protected").GetString()!;
            using JsonDocument header = JsonDocument.Parse(DecodeBase64Url(protectedText));
            JsonElement protectedHeader = header.RootElement;
            Assert.That(root.GetProperty("signatures").GetArrayLength(), Is.EqualTo(1));
            Assert.That(protectedHeader.GetProperty("alg").GetString(), Is.EqualTo("RS256"));
            Assert.That(protectedHeader.GetProperty("cty").GetString(),
                Is.EqualTo("opc-ticket+json;type=DeviceIdentityTicketType"));
            Assert.That(protectedHeader.GetProperty("x5c").GetArrayLength(), Is.EqualTo(2));
            Assert.That(Convert.FromBase64String(protectedHeader.GetProperty("x5c")[0].GetString()!),
                Is.EqualTo(m_rsaCertificate.RawData));
            Assert.That(Convert.FromBase64String(protectedHeader.GetProperty("x5c")[1].GetString()!),
                Is.EqualTo(m_issuerCertificate.RawData));
            Assert.That(payloadText, Does.Not.Contain("="));
            Assert.That(payloadText, Does.Not.Contain("UaTypeId"));

            byte[] signingInput = Encoding.ASCII.GetBytes(
                protectedText + "." + payloadText);
            byte[] signature = DecodeBase64Url(signatureEntry.GetProperty("signature").GetString()!);
            using RSA publicKey = m_rsaCertificate.GetRSAPublicKey()!;
            Assert.That(publicKey.VerifyData(
                signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);

            using var decoder = new JsonDecoder(
                Encoding.UTF8.GetString(DecodeBase64Url(payloadText)), m_messageContext);
            var decoded = new DeviceIdentityTicketType();
            decoded.Decode(decoder);
            Assert.That(decoded.ManufacturerName, Is.EqualTo("Acme"));
            Assert.That(decoded.ModelName, Is.EqualTo("Pump"));
            Assert.That(decoded.SerialNumber, Is.EqualTo("SN-WRITER"));
            Assert.That(decoded.ProductInstanceUri, Is.EqualTo("urn:acme:writer:1"));

            using RSA retainedPrivateKey = m_rsaCertificate.GetRSAPrivateKey()!;
            Assert.That(retainedPrivateKey.KeySize, Is.EqualTo(2048));
            provider.Verify(p => p.GetPrivateKeyCertificateAsync(
                identifier, null, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase(256, "ES256")]
        [TestCase(384, "ES384")]
        [TestCase(521, "ES512")]
        public async Task WriteAsyncSelectsEcdsaAlgorithmByCurve(int curveBits, string expectedAlgorithm)
        {
            ECCurve curve = curveBits switch
            {
                256 => ECCurve.NamedCurves.nistP256,
                384 => ECCurve.NamedCurves.nistP384,
                521 => ECCurve.NamedCurves.nistP521,
                _ => throw new ArgumentOutOfRangeException(nameof(curveBits))
            };
            using Certificate certificate = CreateEcdsaCertificate(curve, $"CN=Ticket writer {expectedAlgorithm}");
            var provider = PrivateKeyProvider(certificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            string encoded = await writer.WriteAsync(DeviceTicket(), Identifier(certificate)).ConfigureAwait(false);
            using JsonDocument envelope = JsonDocument.Parse(encoded);
            string protectedText = envelope.RootElement.GetProperty("signatures")[0]
                .GetProperty("protected").GetString()!;
            using JsonDocument header = JsonDocument.Parse(DecodeBase64Url(protectedText));

            Assert.That(header.RootElement.GetProperty("alg").GetString(), Is.EqualTo(expectedAlgorithm));
            string payload = envelope.RootElement.GetProperty("payload").GetString()!;
            string signatureText = envelope.RootElement.GetProperty("signatures")[0]
                .GetProperty("signature").GetString()!;
#if NET5_0_OR_GREATER
            using ECDsa publicKey = certificate.GetECDsaPublicKey()!;
            Assert.That(publicKey.VerifyData(
                Encoding.ASCII.GetBytes(protectedText + "." + payload),
                DecodeBase64Url(signatureText),
                HashFor(expectedAlgorithm),
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation), Is.True);
#else
            using ECDsa publicKey = certificate.GetECDsaPublicKey()!;
            Assert.That(publicKey.VerifyData(
                Encoding.ASCII.GetBytes(protectedText + "." + payload),
                DecodeBase64Url(signatureText),
                HashFor(expectedAlgorithm)), Is.True);
#endif
        }

        [Test]
        public async Task WriteAsyncAddsCompositeUriOnlyForCompositeTicket()
        {
            var provider = PrivateKeyProvider(m_rsaCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            var ticket = new CompositeIdentityTicketType
            {
                ManufacturerName = "Acme",
                ModelName = "Line",
                SerialNumber = "LINE-4",
                CompositeInstanceUri = "urn:acme:line:4",
                Devices = ["urn:acme:writer:1"],
                Composites = ["urn:acme:line:5"]
            };
            ticket.Authorities = [CreateCertificateAuthority()];

            string encoded = await writer.WriteAsync(
                ticket, Identifier(m_rsaCertificate), compositeInstanceUri: "urn:acme:line:4")
                .ConfigureAwait(false);

            using JsonDocument envelope = JsonDocument.Parse(encoded);
            string protectedText = envelope.RootElement.GetProperty("signatures")[0]
                .GetProperty("protected").GetString()!;
            using JsonDocument header = JsonDocument.Parse(DecodeBase64Url(protectedText));
            Assert.That(header.RootElement.GetProperty("cty").GetString(),
                Is.EqualTo("opc-ticket+json;type=CompositeIdentityTicketType"));
            Assert.That(header.RootElement.GetProperty("opc-uri").GetString(), Is.EqualTo("urn:acme:line:4"));

            string deviceEncoded = await writer.WriteAsync(
                DeviceTicket(), Identifier(m_rsaCertificate)).ConfigureAwait(false);
            using JsonDocument deviceEnvelope = JsonDocument.Parse(deviceEncoded);
            string deviceProtectedText = deviceEnvelope.RootElement.GetProperty("signatures")[0]
                .GetProperty("protected").GetString()!;
            using JsonDocument deviceHeader = JsonDocument.Parse(DecodeBase64Url(deviceProtectedText));
            Assert.That(deviceHeader.RootElement.TryGetProperty("opc-uri", out _), Is.False);
        }

        [Test]
        public async Task WriteAsyncRejectsNullArgumentsAndInvalidCompositeUri()
        {
            var provider = PrivateKeyProvider(m_rsaCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            Assert.That(
                () => new OnboardingTicketWriter(null!, m_messageContext),
                Throws.ArgumentNullException);
            Assert.That(
                () => new OnboardingTicketWriter(provider.Object, null!),
                Throws.ArgumentNullException);

            await Assert.ThatAsync(
                async () => await writer.WriteAsync(null!, Identifier(m_rsaCertificate)).ConfigureAwait(false),
                Throws.ArgumentNullException);
            await Assert.ThatAsync(
                async () => await writer.WriteAsync(DeviceTicket(), null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
            await Assert.ThatAsync(
                async () => await writer.WriteAsync(
                    DeviceTicket(), Identifier(m_rsaCertificate), compositeInstanceUri: null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
            await Assert.ThatAsync(
                async () => await writer.WriteAsync(
                    DeviceTicket(), Identifier(m_rsaCertificate), compositeInstanceUri: "not-an-absolute-uri")
                    .ConfigureAwait(false),
                Throws.ArgumentException);
            provider.VerifyNoOtherCalls();
        }

        [Test]
        public async Task WriteAsyncRejectsMissingPrivateKeyCertificate()
        {
            var provider = new Mock<ICertificateProvider>(MockBehavior.Strict);
            provider.Setup(p => p.GetPrivateKeyCertificateAsync(
                    It.IsAny<CertificateIdentifier>(),
                    It.IsAny<ICertificatePasswordProvider>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Certificate?>((Certificate?)null));
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);

            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await writer.WriteAsync(DeviceTicket(), Identifier(m_rsaCertificate))
                    .ConfigureAwait(false))!;

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            provider.Verify(p => p.GetPrivateKeyCertificateAsync(
                It.IsAny<CertificateIdentifier>(),
                It.IsAny<ICertificatePasswordProvider>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task WriteAsyncRejectsWeakRsaSigningKey()
        {
            using Certificate weakCertificate = CreateRsaCertificate(1024, "CN=Weak ticket writer key");
            var provider = PrivateKeyProvider(weakCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);

            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await writer.WriteAsync(DeviceTicket(), Identifier(weakCertificate))
                    .ConfigureAwait(false))!;

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            using RSA weakKey = weakCertificate.GetRSAPrivateKey()!;
            Assert.That(weakKey.KeySize, Is.EqualTo(1024));
        }

        [Test]
        public async Task WriteAsyncHonorsCancellationBeforeCertificateLookupAndAfterLookup()
        {
            using var preCancelled = new CancellationTokenSource();
            preCancelled.Cancel();
            var provider = PrivateKeyProvider(m_rsaCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            await Assert.ThatAsync(
                async () => await writer.WriteAsync(
                    DeviceTicket(), Identifier(m_rsaCertificate), ct: preCancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            provider.VerifyNoOtherCalls();

            using var postCancelled = new CancellationTokenSource();
            var cancellingProvider = new Mock<ICertificateProvider>(MockBehavior.Strict);
            cancellingProvider.Setup(p => p.GetPrivateKeyCertificateAsync(
                    It.IsAny<CertificateIdentifier>(),
                    It.IsAny<ICertificatePasswordProvider>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((CertificateIdentifier _, ICertificatePasswordProvider? _, string? _,
                    CancellationToken _) =>
                {
                    postCancelled.Cancel();
                    return new ValueTask<Certificate?>(m_rsaCertificate.AddRef());
                });
            var cancellingWriter = new OnboardingTicketWriter(cancellingProvider.Object, m_messageContext);
            await Assert.ThatAsync(
                async () => await cancellingWriter.WriteAsync(
                    DeviceTicket(), Identifier(m_rsaCertificate), ct: postCancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            using RSA retainedKey = m_rsaCertificate.GetRSAPrivateKey()!;
            Assert.That(retainedKey.KeySize, Is.EqualTo(2048));
            cancellingProvider.Verify(p => p.GetPrivateKeyCertificateAsync(
                It.IsAny<CertificateIdentifier>(),
                It.IsAny<ICertificatePasswordProvider>(),
                It.IsAny<string>(),
                postCancelled.Token), Times.Once);
        }

        /// <summary>
        /// A composite builder adds its signature without changing signed inputs or discarding vendor metadata.
        /// </summary>
        [Test]
        public async Task AppendSignaturePreservesPayloadExistingSignaturesAndMetadataAndValidatesNewSigner()
        {
            var originalWriter = new OnboardingTicketWriter(
                PrivateKeyProvider(m_rsaCertificate).Object, m_messageContext);
            string original = await originalWriter.WriteAsync(
                DeviceTicket(), Identifier(m_rsaCertificate)).ConfigureAwait(false);
            original = original.Replace(
                "\"signatures\":[", "\"vendor\":{\"reference\":\"shipment-7\"},\"signatures\":[",
                StringComparison.Ordinal);
            original = original.Replace(
                "\"signature\":", "\"header\":{\"timestamp\":\"retained-unverified-metadata\"},\"signature\":",
                StringComparison.Ordinal);
            var trust = new Mock<IOnboardingTicketTrustProvider>(MockBehavior.Strict);
            trust.Setup(t => t.IsTrustedAsync(
                It.IsAny<CertificateCollection>(), OnboardingTicketSource.Device, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            var provider = PrivateKeyProvider(m_issuerCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            int validations = 0;
            string appended = await writer.AppendSignatureAsync(
                original, Identifier(m_issuerCertificate), async (encoded, ct) =>
                {
                    Assert.That(encoded, Is.EqualTo(original));
                    provider.VerifyNoOtherCalls();
                    validations++;
                    return (await validator.ValidateAsync(encoded, OnboardingTicketSource.Device, ct)
                        .ConfigureAwait(false)).Ticket;
                }, compositeInstanceUri: "urn:composite:shipment:7").ConfigureAwait(false);

            Assert.That(validations, Is.EqualTo(1));
            using JsonDocument before = JsonDocument.Parse(original);
            using JsonDocument after = JsonDocument.Parse(appended);
            JsonElement root = after.RootElement;
            Assert.That(root.GetProperty("payload").GetString(),
                Is.EqualTo(before.RootElement.GetProperty("payload").GetString()));
            Assert.That(root.GetProperty("vendor").GetRawText(),
                Is.EqualTo(before.RootElement.GetProperty("vendor").GetRawText()));
            JsonElement signatures = root.GetProperty("signatures");
            Assert.That(signatures.GetArrayLength(), Is.EqualTo(2));
            Assert.That(signatures[0].GetRawText(),
                Is.EqualTo(before.RootElement.GetProperty("signatures")[0].GetRawText()));
            string protectedText = signatures[1].GetProperty("protected").GetString()!;
            using JsonDocument header = JsonDocument.Parse(DecodeBase64Url(protectedText));
            Assert.That(header.RootElement.GetProperty("opc-uri").GetString(), Is.EqualTo("urn:composite:shipment:7"));
            Assert.That(header.RootElement.GetProperty("cty").GetString(),
                Is.EqualTo("opc-ticket+json;type=DeviceIdentityTicketType"));
            using RSA publicKey = m_issuerCertificate.GetRSAPublicKey()!;
            Assert.That(publicKey.VerifyData(
                Encoding.ASCII.GetBytes(protectedText + "." + root.GetProperty("payload").GetString()),
                DecodeBase64Url(signatures[1].GetProperty("signature").GetString()!),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);

            trust.Reset();
            trust.Setup(t => t.IsTrustedAsync(
                It.Is<CertificateCollection>(chain => chain[0].Thumbprint == m_rsaCertificate.Thumbprint),
                OnboardingTicketSource.Device, It.IsAny<CancellationToken>())).ReturnsAsync(false);
            trust.Setup(t => t.IsTrustedAsync(
                It.Is<CertificateCollection>(chain => chain[0].Thumbprint == m_issuerCertificate.Thumbprint),
                OnboardingTicketSource.Device, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            ValidatedOnboardingTicket accepted = await validator.ValidateAsync(
                appended, OnboardingTicketSource.Device).ConfigureAwait(false);
            Assert.That(accepted.CompositeInstanceUri, Is.EqualTo("urn:composite:shipment:7"));
            Assert.That(((DeviceIdentityTicketType)accepted.Ticket).ProductInstanceUri,
                Is.EqualTo(DeviceTicket().ProductInstanceUri));
            using RSA retainedKey = m_issuerCertificate.GetRSAPrivateKey()!;
            Assert.That(retainedKey.KeySize, Is.EqualTo(2048));
        }

        /// <summary>
        /// Each supply-chain signature is retained when a later actor adds another one.
        /// </summary>
        [Test]
        public async Task AppendSignatureCanExtendAnAlreadyCountersignedCompositeTicket()
        {
            var writer = new OnboardingTicketWriter(PrivateKeyProvider(m_rsaCertificate).Object, m_messageContext);
            var ticket = new CompositeIdentityTicketType
            {
                CompositeInstanceUri = "urn:composite:inner",
                ManufacturerName = "Builder",
                ModelName = "Assembly",
                SerialNumber = "7",
                Authorities = [CreateCertificateAuthority()],
                Devices = ["urn:acme:writer:1"]
            };
            string original = await writer.WriteAsync(ticket, Identifier(m_rsaCertificate)).ConfigureAwait(false);
            var trust = new Mock<IOnboardingTicketTrustProvider>();
            trust.Setup(t => t.IsTrustedAsync(
                It.IsAny<CertificateCollection>(), OnboardingTicketSource.OutOfBand, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            async ValueTask<BaseTicketType> ValidateAsync(string encoded, CancellationToken ct)
            {
                return (await validator.ValidateAsync(encoded, OnboardingTicketSource.OutOfBand, ct)
                    .ConfigureAwait(false)).Ticket;
            }
            string second = await writer.AppendSignatureAsync(
                original, Identifier(m_rsaCertificate), ValidateAsync,
                compositeInstanceUri: "urn:composite:outer").ConfigureAwait(false);
            string third = await writer.AppendSignatureAsync(
                second, Identifier(m_rsaCertificate), ValidateAsync).ConfigureAwait(false);
            using JsonDocument before = JsonDocument.Parse(second);
            using JsonDocument after = JsonDocument.Parse(third);
            Assert.That(after.RootElement.GetProperty("payload").GetString(),
                Is.EqualTo(before.RootElement.GetProperty("payload").GetString()));
            JsonElement signatures = after.RootElement.GetProperty("signatures");
            Assert.That(signatures.GetArrayLength(), Is.EqualTo(3));
            for (int i = 0; i < 2; i++)
            {
                Assert.That(signatures[i].GetRawText(),
                    Is.EqualTo(before.RootElement.GetProperty("signatures")[i].GetRawText()));
            }
            using JsonDocument header = JsonDocument.Parse(
                DecodeBase64Url(signatures[2].GetProperty("protected").GetString()!));
            Assert.That(header.RootElement.GetProperty("cty").GetString(),
                Is.EqualTo("opc-ticket+json;type=CompositeIdentityTicketType"));
            Assert.That(header.RootElement.TryGetProperty("opc-uri", out _), Is.False);
        }

        /// <summary>
        /// An actor must not endorse a ticket when existing-signature validation rejects it.
        /// </summary>
        [Test]
        public async Task AppendSignaturePropagatesValidationFailureBeforeAccessingSigningKey()
        {
            var provider = PrivateKeyProvider(m_rsaCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            var rejected = new ServiceResultException(StatusCodes.BadTicketInvalid);
            ServiceResultException? actual = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await writer.AppendSignatureAsync(
                    "untrusted", Identifier(m_rsaCertificate),
                    (_, _) => throw rejected).ConfigureAwait(false));
            Assert.That(actual, Is.SameAs(rejected));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Cancellation before or during validation cannot obtain the supply-chain signing key.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AppendSignatureHonorsCancellationBeforeAndAfterValidation(bool preCancelled)
        {
            using var cancellation = new CancellationTokenSource();
            if (preCancelled)
            {
                cancellation.Cancel();
            }
            var provider = PrivateKeyProvider(m_rsaCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            int validations = 0;
            OperationCanceledException? error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await writer.AppendSignatureAsync(
                    "ticket", Identifier(m_rsaCertificate), (_, ct) =>
                    {
                        Assert.That(ct, Is.EqualTo(cancellation.Token));
                        validations++;
                        cancellation.Cancel();
                        return new ValueTask<BaseTicketType>(DeviceTicket());
                    }, ct: cancellation.Token).ConfigureAwait(false));
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(validations, Is.EqualTo(preCancelled ? 0 : 1));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Missing validation and malformed arguments are rejected without accessing private keys.
        /// </summary>
        [Test]
        public async Task AppendSignatureRequiresValidationAndValidArguments()
        {
            var provider = PrivateKeyProvider(m_rsaCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            ValueTask<BaseTicketType> ValidateAsync(string _, CancellationToken ct) =>
                new(DeviceTicket());
            await Assert.ThatAsync(async () => await writer.AppendSignatureAsync(
                null!, Identifier(m_rsaCertificate), ValidateAsync).ConfigureAwait(false),
                Throws.ArgumentNullException);
            await Assert.ThatAsync(async () => await writer.AppendSignatureAsync(
                "ticket", null!, ValidateAsync).ConfigureAwait(false), Throws.ArgumentNullException);
            await Assert.ThatAsync(async () => await writer.AppendSignatureAsync(
                "ticket", Identifier(m_rsaCertificate), null!).ConfigureAwait(false), Throws.ArgumentNullException);
            await Assert.ThatAsync(async () => await writer.AppendSignatureAsync(
                "ticket", Identifier(m_rsaCertificate), ValidateAsync, compositeInstanceUri: null!)
                .ConfigureAwait(false), Throws.ArgumentNullException);
            await Assert.ThatAsync(async () => await writer.AppendSignatureAsync(
                "ticket", Identifier(m_rsaCertificate), ValidateAsync, compositeInstanceUri: "relative")
                .ConfigureAwait(false), Throws.ArgumentException);
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Appending the last supported signature succeeds; exceeding the reader's limit cannot create a ticket.
        /// </summary>
        [TestCase(63, true)]
        [TestCase(64, false)]
        public async Task AppendSignatureSharesTheReadersSignatureCountBoundary(int count, bool allowed)
        {
            var originalWriter = new OnboardingTicketWriter(
                PrivateKeyProvider(m_rsaCertificate).Object, m_messageContext);
            string original = await originalWriter.WriteAsync(
                DeviceTicket(), Identifier(m_rsaCertificate)).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(original);
            string payload = document.RootElement.GetProperty("payload").GetString()!;
            string signature = document.RootElement.GetProperty("signatures")[0].GetRawText();
            string input = "{\"payload\":\"" + payload + "\",\"signatures\":[" +
                string.Join(",", Enumerable.Repeat(signature, count)) + "]}";
            var provider = PrivateKeyProvider(m_rsaCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            var trust = new Mock<IOnboardingTicketTrustProvider>();
            trust.Setup(t => t.IsTrustedAsync(
                It.IsAny<CertificateCollection>(), OnboardingTicketSource.Device, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            async ValueTask<BaseTicketType> ValidateAsync(string encoded, CancellationToken ct)
            {
                return (await validator.ValidateAsync(encoded, OnboardingTicketSource.Device, ct)
                    .ConfigureAwait(false)).Ticket;
            }
            if (allowed)
            {
                string appended = await writer.AppendSignatureAsync(
                    input, Identifier(m_rsaCertificate), ValidateAsync).ConfigureAwait(false);
                using JsonDocument result = JsonDocument.Parse(appended);
                Assert.That(result.RootElement.GetProperty("signatures").GetArrayLength(),
                    Is.EqualTo(OnboardingTicketWriter.MaximumSignatureCount));
                ValidatedOnboardingTicket validated = await validator.ValidateAsync(
                    appended, OnboardingTicketSource.Device).ConfigureAwait(false);
                Assert.That(((DeviceIdentityTicketType)validated.Ticket).ProductInstanceUri,
                    Is.EqualTo(DeviceTicket().ProductInstanceUri));
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await writer.AppendSignatureAsync(
                        input, Identifier(m_rsaCertificate), ValidateAsync).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTicketInvalid));
                provider.VerifyNoOtherCalls();
            }
        }

        /// <summary>
        /// The certificate-chain limit includes the signing certificate itself.
        /// </summary>
        [TestCase(15, true)]
        [TestCase(16, false)]
        public async Task WriteAsyncBoundsIssuerChainBeforeAccessingPrivateKey(int count, bool allowed)
        {
            var provider = PrivateKeyProvider(m_rsaCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            ArrayOf<ByteString> issuers = Enumerable.Repeat(
                ByteString.From(m_issuerCertificate.RawData.AsSpan()), count).ToArray().ToArrayOf();
            if (allowed)
            {
                string encoded = await writer.WriteAsync(
                    DeviceTicket(), Identifier(m_rsaCertificate), issuers).ConfigureAwait(false);
                using JsonDocument envelope = JsonDocument.Parse(encoded);
                using JsonDocument header = JsonDocument.Parse(DecodeBase64Url(
                    envelope.RootElement.GetProperty("signatures")[0].GetProperty("protected").GetString()!));
                Assert.That(header.RootElement.GetProperty("x5c").GetArrayLength(),
                    Is.EqualTo(OnboardingTicketWriter.MaximumCertificateCount));
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await writer.WriteAsync(
                        DeviceTicket(), Identifier(m_rsaCertificate), issuers).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTicketInvalid));
                provider.VerifyNoOtherCalls();
            }
        }

        /// <summary>
        /// An oversized incoming envelope is rejected before either approval or private-key retrieval.
        /// </summary>
        [Test]
        public async Task AppendSignatureRejectsOversizedInputBeforeValidationOrSigning()
        {
            var provider = PrivateKeyProvider(m_rsaCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            int validations = 0;
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await writer.AppendSignatureAsync(
                    new string('x', OnboardingTicketWriter.MaximumEncodedLength + 1),
                    Identifier(m_rsaCertificate), (_, _) =>
                    {
                        validations++;
                        return new ValueTask<BaseTicketType>(DeviceTicket());
                    }).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTicketInvalid));
            Assert.That(validations, Is.Zero);
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Adding a signature must not return an envelope too large for the default reader.
        /// </summary>
        [Test]
        public async Task AppendSignatureRejectsOutputWhichExceedsTheEncodedLengthLimit()
        {
            var provider = PrivateKeyProvider(m_rsaCertificate);
            var writer = new OnboardingTicketWriter(provider.Object, m_messageContext);
            string original = await writer.WriteAsync(
                DeviceTicket(), Identifier(m_rsaCertificate)).ConfigureAwait(false);
            const string prefix = "\"padding\":\"";
            const string suffix = "\",";
            int paddingLength = OnboardingTicketWriter.MaximumEncodedLength -
                original.Length - prefix.Length - suffix.Length;
            string padded = original.Insert(1, prefix + new string('x', paddingLength) + suffix);
            Assert.That(padded, Has.Length.EqualTo(OnboardingTicketWriter.MaximumEncodedLength));
            var trust = new Mock<IOnboardingTicketTrustProvider>();
            trust.Setup(t => t.IsTrustedAsync(
                It.IsAny<CertificateCollection>(), OnboardingTicketSource.Device, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await writer.AppendSignatureAsync(
                    padded, Identifier(m_rsaCertificate), async (encoded, ct) =>
                        (await validator.ValidateAsync(encoded, OnboardingTicketSource.Device, ct)
                            .ConfigureAwait(false)).Ticket).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTicketInvalid));
        }

        private static Mock<ICertificateProvider> PrivateKeyProvider(Certificate certificate)
        {
            var provider = new Mock<ICertificateProvider>(MockBehavior.Strict);
            provider.Setup(p => p.GetPrivateKeyCertificateAsync(
                    It.IsAny<CertificateIdentifier>(),
                    It.IsAny<ICertificatePasswordProvider>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((CertificateIdentifier _, ICertificatePasswordProvider? _, string? _,
                    CancellationToken _) => new ValueTask<Certificate?>(certificate.AddRef()));
            return provider;
        }

        private static CertificateIdentifier Identifier(Certificate certificate)
        {
            return new CertificateIdentifier { Thumbprint = certificate.Thumbprint };
        }

        private DeviceIdentityTicketType DeviceTicket()
        {
            var ticket = new DeviceIdentityTicketType
            {
                ManufacturerName = "Acme",
                ModelName = "Pump",
                SerialNumber = "SN-WRITER",
                ProductInstanceUri = "urn:acme:writer:1"
            };
            ticket.Authorities = [CreateCertificateAuthority()];
            return ticket;
        }

        private CertificateAuthorityType CreateCertificateAuthority()
        {
            return new CertificateAuthorityType
            {
                AuthorityCertificate = new ByteString((ReadOnlyMemory<byte>)m_rsaCertificate.RawData),
                IssuerCertificates =
                [
                    new ByteString((ReadOnlyMemory<byte>)m_issuerCertificate.RawData)
                ]
            };
        }

        private static HashAlgorithmName HashFor(string algorithm)
        {
            return algorithm switch
            {
                "ES384" => HashAlgorithmName.SHA384,
                "ES512" => HashAlgorithmName.SHA512,
                _ => HashAlgorithmName.SHA256
            };
        }

        private static byte[] DecodeBase64Url(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => string.Empty
            };
            return Convert.FromBase64String(padded);
        }

        private static Certificate CreateRsaCertificate(int keySize, string subject)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetRSAKeySize(checked((ushort)keySize))
                .CreateForRSA();
        }

        private static Certificate CreateEcdsaCertificate(ECCurve curve, string subject)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetECCurve(curve)
                .CreateForECDsa();
        }

        private static readonly string[] s_envelopeProperties = ["payload", "signatures"];

        private IServiceMessageContext m_messageContext = null!;
        private Certificate m_rsaCertificate = null!;
        private Certificate m_issuerCertificate = null!;
        private ITelemetryContext m_telemetry = null!;
    }
}
