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
using System.Collections.Generic;
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
    public sealed class OnboardingTicketValidatorTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.CreateEmpty(m_telemetry);
            m_rsaCertificate = CreateRsaCertificate(2048, "CN=Part 21 ticket signer");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_rsaCertificate?.Dispose();
            (m_telemetry as IDisposable)?.Dispose();
        }

        [Test]
        public async Task ValidateAsyncAcceptsRsaPkcs1AndPssSha2Signatures()
        {
            foreach ((string algorithm, HashAlgorithmName hash, RSASignaturePadding padding) in new[]
            {
                ("RS256", HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                ("RS384", HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1),
                ("RS512", HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1),
                ("PS256", HashAlgorithmName.SHA256, RSASignaturePadding.Pss),
                ("PS384", HashAlgorithmName.SHA384, RSASignaturePadding.Pss),
                ("PS512", HashAlgorithmName.SHA512, RSASignaturePadding.Pss)
            })
            {
                var trust = TrustedProvider();
                var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
                string encoded = CreateTicket(CreateDeviceTicket(), m_rsaCertificate, algorithm, hash, padding);

                ValidatedOnboardingTicket result = await validator.ValidateAsync(
                    encoded, OnboardingTicketSource.OutOfBand).ConfigureAwait(false);

                Assert.That(result.Ticket, Is.TypeOf<DeviceIdentityTicketType>(), algorithm);
                Assert.That(result.Metadata.SerialNumber, Is.EqualTo("SN-42"), algorithm);
                trust.Verify(
                    provider => provider.IsTrustedAsync(
                        It.IsAny<CertificateCollection>(),
                        OnboardingTicketSource.OutOfBand,
                        It.IsAny<CancellationToken>()),
                    Times.Once,
                    algorithm);
            }
        }

        [TestCase("ES256", 256)]
        [TestCase("ES384", 384)]
        [TestCase("ES512", 521)]
        public async Task ValidateAsyncAcceptsEcdsaJws(string algorithm, int curveBits)
        {
            ECCurve curve = curveBits switch
            {
                256 => ECCurve.NamedCurves.nistP256,
                384 => ECCurve.NamedCurves.nistP384,
                521 => ECCurve.NamedCurves.nistP521,
                _ => throw new ArgumentOutOfRangeException(nameof(curveBits))
            };
            using Certificate certificate = CreateEcdsaCertificate(curve, $"CN=Ticket {algorithm} signer");
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string encoded = CreateTicket(
                CreateDeviceTicket(), certificate, algorithm, HashFor(algorithm));

            ValidatedOnboardingTicket result = await validator.ValidateAsync(
                encoded, OnboardingTicketSource.Device).ConfigureAwait(false);

            Assert.That(result.Metadata.ProductInstanceUri, Is.EqualTo("urn:acme:device:42"));
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    OnboardingTicketSource.Device,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestCase("""{"protected":"not-base64url","signature":"AA"}""")]
        [TestCase("""{"protected":"a","protected":"b","signature":"AA"}""")]
        [TestCase("null")]
        public async Task ValidateAsyncAcceptsOneGoodSignatureAfterMalformedSignature(string malformedSignature)
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string encoded = CreateTicketWithEntries(
                CreateDeviceTicket(),
                [
                    malformedSignature,
                    CreateSignatureEntry(CreateDeviceTicket(), m_rsaCertificate, "RS256")
                ]);

            ValidatedOnboardingTicket result = await validator.ValidateAsync(
                encoded, OnboardingTicketSource.OutOfBand).ConfigureAwait(false);

            Assert.That(result.Metadata.SerialNumber, Is.EqualTo("SN-42"));
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    OnboardingTicketSource.OutOfBand,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ValidateAsyncAcceptsTrustedSignatureAfterAnotherCryptographicallyValidButUntrustedSignature()
        {
            using Certificate untrustedSigner = CreateEcdsaCertificate(
                ECCurve.NamedCurves.nistP256, "CN=Untrusted ticket signer");
            var trust = new Mock<IOnboardingTicketTrustProvider>(MockBehavior.Strict);
            int trustChecks = 0;
            trust.Setup(provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    OnboardingTicketSource.OutOfBand,
                    It.IsAny<CancellationToken>()))
                .Returns((CertificateCollection _, OnboardingTicketSource _, CancellationToken _) =>
                    new ValueTask<bool>(++trustChecks == 2));
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            DeviceIdentityTicketType ticket = CreateDeviceTicket();
            string encoded = CreateTicketWithEntries(
                ticket,
                [
                    CreateSignatureEntry(ticket, untrustedSigner, "ES256"),
                    CreateSignatureEntry(ticket, m_rsaCertificate, "RS256")
                ]);

            ValidatedOnboardingTicket result = await validator.ValidateAsync(
                encoded, OnboardingTicketSource.OutOfBand).ConfigureAwait(false);

            Assert.That(result.Metadata.SerialNumber, Is.EqualTo("SN-42"));
            Assert.That(trustChecks, Is.EqualTo(2));
        }

        [Test]
        public async Task ValidateAsyncReturnsExpectedDeviceAndCompositeMetadataAndClonesTicket()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string deviceEncoded = CreateTicket(
                CreateDeviceTicket(), m_rsaCertificate, "RS256", compositeUri: null);
            string compositeEncoded = CreateTicket(
                CreateCompositeTicket(), m_rsaCertificate, "RS256", compositeUri: "urn:acme:assembly:7");
            string compositeWithoutHeaderUri = CreateTicket(
                CreateCompositeTicket(), m_rsaCertificate, "RS256");

            ValidatedOnboardingTicket device = await validator.ValidateAsync(
                deviceEncoded, OnboardingTicketSource.OutOfBand).ConfigureAwait(false);
            ValidatedOnboardingTicket composite = await validator.ValidateAsync(
                compositeEncoded, OnboardingTicketSource.OutOfBand).ConfigureAwait(false);
            ValidatedOnboardingTicket compositeWithoutUri = await validator.ValidateAsync(
                compositeWithoutHeaderUri, OnboardingTicketSource.OutOfBand).ConfigureAwait(false);
            BaseTicketType returnedTicket = device.Ticket;
            ((DeviceIdentityTicketType)returnedTicket).SerialNumber = "mutated";

            Assert.That(device.Metadata.Kind, Is.EqualTo(TicketKind.DeviceIdentity));
            Assert.That(device.Metadata.ManufacturerName, Is.EqualTo("Acme"));
            Assert.That(device.Metadata.ModelName, Is.EqualTo("Pump"));
            Assert.That(device.Metadata.SerialNumber, Is.EqualTo("SN-42"));
            Assert.That(device.Metadata.ProductInstanceUri, Is.EqualTo("urn:acme:device:42"));
            Assert.That(((DeviceIdentityTicketType)device.Ticket).SerialNumber, Is.EqualTo("SN-42"));
            Assert.That(device.CompositeInstanceUri, Is.Empty);
            Assert.That(composite.Metadata.Kind, Is.EqualTo(TicketKind.CompositeIdentity));
            Assert.That(composite.Metadata.SerialNumber, Is.EqualTo("ASSEMBLY-7"));
            Assert.That(composite.Metadata.ProductInstanceUri, Is.EqualTo("urn:acme:assembly:7"));
            Assert.That(composite.CompositeInstanceUri, Is.EqualTo("urn:acme:assembly:7"));
            Assert.That(compositeWithoutUri.CompositeInstanceUri, Is.Empty);
        }

        [Test]
        public async Task ValidateAsyncRejectsTamperedPayloadProtectedHeaderAndSignature()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string original = CreateTicket(CreateDeviceTicket(), m_rsaCertificate, "RS256");
            using JsonDocument document = JsonDocument.Parse(original);
            string payload = document.RootElement.GetProperty("payload").GetString()!;
            JsonElement signature = document.RootElement.GetProperty("signatures")[0];
            string protectedText = signature.GetProperty("protected").GetString()!;
            string signatureText = signature.GetProperty("signature").GetString()!;
            string tamperedPayload = EncodeEnvelope(
                Base64Url(Encoding.UTF8.GetBytes("tampered payload")), protectedText, signatureText);
            byte[] protectedBytes = DecodeBase64Url(protectedText);
            using JsonDocument headerDocument = JsonDocument.Parse(protectedBytes);
            var changedHeader = new Dictionary<string, object?>
            {
                ["alg"] = "RS384",
                ["x5c"] = new[] { Convert.ToBase64String(m_rsaCertificate.RawData) },
                ["cty"] = headerDocument.RootElement.GetProperty("cty").GetString()
            };
            string tamperedHeader = EncodeEnvelope(
                payload, Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(changedHeader))), signatureText);
            char replacement = signatureText[^1] == 'A' ? 'B' : 'A';
            string tamperedSignature = EncodeEnvelope(
                payload, protectedText, signatureText[..^1] + replacement);

            await AssertInvalidAsync(validator, tamperedPayload).ConfigureAwait(false);
            await AssertInvalidAsync(validator, tamperedHeader).ConfigureAwait(false);
            await AssertInvalidAsync(validator, tamperedSignature).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<OnboardingTicketSource>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestCase("none")]
        [TestCase("HS256")]
        [TestCase("RS1")]
        public async Task ValidateAsyncRejectsUnknownOrNonSha2Algorithm(string algorithm)
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string encoded = CreateTicket(CreateDeviceTicket(), m_rsaCertificate, algorithm);

            await AssertInvalidAsync(validator, encoded).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<OnboardingTicketSource>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ValidateAsyncRejectsWrongKeyTypeAndWeakRsaKey()
        {
            using Certificate ecdsa = CreateEcdsaCertificate(
                ECCurve.NamedCurves.nistP256, "CN=Wrong key type");
            using Certificate weakRsa = CreateRsaCertificate(1024, "CN=Weak ticket signer");
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);

            await AssertInvalidAsync(
                validator, CreateTicket(
                    CreateDeviceTicket(), ecdsa, "RS256", signingAlgorithm: "ES256")).ConfigureAwait(false);
            await AssertInvalidAsync(
                validator, CreateTicket(CreateDeviceTicket(), weakRsa, "RS256")).ConfigureAwait(false);

            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<OnboardingTicketSource>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ValidateAsyncRejectsInvalidUtf8AfterAValidTrustedSignature()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string encoded = CreateRawTicket(
                [0xff, 0xfe], m_rsaCertificate, "RS256", "opc-ticket+json;type=DeviceIdentityTicketType");

            await AssertInvalidAsync(validator, encoded).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    OnboardingTicketSource.OutOfBand,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ValidateAsyncRejectsUnsupportedContentTypeAndMalformedHeaderOrCertificate()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string rawPayload = EncodeTicketPayload(CreateDeviceTicket());
            byte[] bytes = Encoding.UTF8.GetBytes(rawPayload);
            string unsupported = CreateRawTicket(
                bytes, m_rsaCertificate, "RS256", "application/json");
            string noContentType = CreateRawTicket(
                bytes, m_rsaCertificate, "RS256", null);
            string badCertificate = CreateTicketWithHeader(
                rawPayload, """{"alg":"RS256","x5c":["%%%"],"cty":"opc-ticket+json;type=DeviceIdentityTicketType"}""");
            string invalidDerCertificate = CreateTicketWithHeader(
                rawPayload, """{"alg":"RS256","x5c":["AQID"],"cty":"opc-ticket+json;type=DeviceIdentityTicketType"}""");
            string malformedHeader = CreateTicketWithHeader(rawPayload, "{");

            await AssertInvalidAsync(validator, unsupported).ConfigureAwait(false);
            await AssertInvalidAsync(validator, noContentType).ConfigureAwait(false);
            await AssertInvalidAsync(validator, badCertificate).ConfigureAwait(false);
            await AssertInvalidAsync(validator, invalidDerCertificate).ConfigureAwait(false);
            await AssertInvalidAsync(validator, malformedHeader).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<OnboardingTicketSource>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ValidateAsyncRejectsPemEncodedSigner()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            byte[] pem = Encoding.ASCII.GetBytes(
                "-----BEGIN CERTIFICATE-----\n" +
                Convert.ToBase64String(m_rsaCertificate.RawData, Base64FormattingOptions.InsertLineBreaks) +
                "\n-----END CERTIFICATE-----");
            string header = JsonSerializer.Serialize(new
            {
                alg = "RS256",
                x5c = new[] { Convert.ToBase64String(pem) },
                cty = "opc-ticket+json;type=DeviceIdentityTicketType"
            });
            string encoded = CreateTicketWithHeader(EncodeTicketPayload(CreateDeviceTicket()), header);

            await AssertInvalidAsync(validator, encoded).ConfigureAwait(false);

            trust.VerifyNoOtherCalls();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ValidateAsyncRejectsPemEncodedAuthorityOrIssuer(bool issuer)
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            var pem = ByteString.From(Encoding.ASCII.GetBytes(
                "-----BEGIN CERTIFICATE-----\n" +
                Convert.ToBase64String(m_rsaCertificate.RawData, Base64FormattingOptions.InsertLineBreaks) +
                "\n-----END CERTIFICATE-----"));
            DeviceIdentityTicketType ticket = CreateDeviceTicket();
            if (issuer)
            {
                ticket.Authorities[0].IssuerCertificates = [pem];
            }
            else
            {
                ticket.Authorities[0].AuthorityCertificate = pem;
            }

            await AssertInvalidAsync(
                validator, CreateTicket(ticket, m_rsaCertificate, "RS256")).ConfigureAwait(false);

            trust.Verify(value => value.IsTrustedAsync(
                It.IsAny<CertificateCollection>(), OnboardingTicketSource.OutOfBand,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ValidateAsyncRequiresIdentityUriAndAtLeastOneAuthority()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);

            await AssertInvalidAsync(
                validator, CreateTicket(CreateDeviceTicket(identityUri: string.Empty), m_rsaCertificate, "RS256"))
                .ConfigureAwait(false);
            await AssertInvalidAsync(
                validator, CreateTicket(CreateDeviceTicket(includeAuthority: false), m_rsaCertificate, "RS256"))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ValidateAsyncRejectsMissingManufacturerAndMalformedAuthorityOrIssuerDer()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            DeviceIdentityTicketType missingManufacturer = CreateDeviceTicket(manufacturerName: "  ");
            DeviceIdentityTicketType invalidAuthority = CreateDeviceTicket();
            invalidAuthority.Authorities[0].AuthorityCertificate = new ByteString(new byte[] { 1, 2, 3 });
            DeviceIdentityTicketType invalidIssuer = CreateDeviceTicket();
            invalidIssuer.Authorities[0].IssuerCertificates =
                [new ByteString(new byte[] { 1, 2, 3 })];

            await AssertInvalidAsync(
                validator, CreateTicket(missingManufacturer, m_rsaCertificate, "RS256")).ConfigureAwait(false);
            await AssertInvalidAsync(
                validator, CreateTicket(invalidAuthority, m_rsaCertificate, "RS256")).ConfigureAwait(false);
            await AssertInvalidAsync(
                validator, CreateTicket(invalidIssuer, m_rsaCertificate, "RS256")).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    OnboardingTicketSource.OutOfBand,
                    It.IsAny<CancellationToken>()),
                Times.Exactly(3));
        }

        [Test]
        public async Task ValidateAsyncAllowsOptionalSerialRevisionAndDateFieldsToBeAbsent()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            DeviceIdentityTicketType ticket = CreateDeviceTicket();
            ticket.ModelName = string.Empty;
            ticket.SerialNumber = string.Empty;
            ticket.ModelVersion = string.Empty;
            ticket.HardwareRevision = string.Empty;
            ticket.SoftwareRevision = string.Empty;
            ticket.ManufactureDate = DateTime.MinValue;
            string encoded = CreateTicket(ticket, m_rsaCertificate, "RS256");

            ValidatedOnboardingTicket result = await validator.ValidateAsync(
                encoded, OnboardingTicketSource.OutOfBand).ConfigureAwait(false);

            Assert.That(result.Metadata.ManufacturerName, Is.EqualTo("Acme"));
            Assert.That(result.Metadata.SerialNumber, Is.Empty);
            Assert.That(result.Metadata.ModelName, Is.Empty);
        }

        [TestCase(new[] { "relative-device" }, new[] { "urn:acme:assembly:8" })]
        [TestCase(new[] { "urn:acme:device:42" }, new[] { "relative-composite" })]
        public async Task ValidateAsyncRejectsRelativeCompositeDeviceOrCompositeReferences(
            string[] devices,
            string[] composites)
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);

            await AssertInvalidAsync(
                validator,
                CreateTicket(CreateCompositeTicket(devices, composites), m_rsaCertificate, "RS256"))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ValidateAsyncRejectsDuplicateNamesAndNoncanonicalPayloadBase64Url()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string duplicateRoot =
                """{"payload":"AA","payload":"AA","signatures":[]}""";
            string noncanonicalPayload = """{"payload":"AB","signatures":[{}]}""";

            await AssertInvalidAsync(validator, duplicateRoot).ConfigureAwait(false);
            await AssertInvalidAsync(validator, noncanonicalPayload).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<OnboardingTicketSource>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ValidateAsyncRejectsNoncanonicalProtectedAndSignatureBase64Url()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string valid = CreateTicket(CreateDeviceTicket(), m_rsaCertificate, "RS256");
            using JsonDocument document = JsonDocument.Parse(valid);
            string payload = document.RootElement.GetProperty("payload").GetString()!;
            JsonElement entry = document.RootElement.GetProperty("signatures")[0];
            string protectedText = entry.GetProperty("protected").GetString()!;
            string signatureText = entry.GetProperty("signature").GetString()!;
            string invalidProtected = EncodeEnvelope(payload, "AB", signatureText);
            string invalidSignature = EncodeEnvelope(payload, protectedText, "AB");

            await AssertInvalidAsync(validator, invalidProtected).ConfigureAwait(false);
            await AssertInvalidAsync(validator, invalidSignature).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<OnboardingTicketSource>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ValidateAsyncAcceptsTheMaximumEnvelopeSignatureAndCertificateCounts()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string ticketJson = EncodeTicketPayload(CreateDeviceTicket());
            string payload = Base64Url(Encoding.UTF8.GetBytes(ticketJson));
            var certificateChain = new string[16];
            for (int i = 0; i < certificateChain.Length; i++)
            {
                certificateChain[i] = Convert.ToBase64String(m_rsaCertificate.RawData);
            }
            string header = JsonSerializer.Serialize(new
            {
                alg = "RS256",
                x5c = certificateChain,
                cty = "opc-ticket+json;type=DeviceIdentityTicketType"
            });
            string validEnvelope = CreateTicketWithHeader(ticketJson, header);
            using JsonDocument validDocument = JsonDocument.Parse(validEnvelope);
            string validSignatureEntry = validDocument.RootElement.GetProperty("signatures")[0].GetRawText();
            var signatures = new string[64];
            for (int i = 0; i < signatures.Length - 1; i++)
            {
                signatures[i] = "{}";
            }
            signatures[^1] = validSignatureEntry;
            string envelope = $$"""{"payload":"{{payload}}","signatures":[{{string.Join(",", signatures)}}]}""";
            envelope = envelope.PadRight(1024 * 1024, ' ');

            ValidatedOnboardingTicket result = await validator.ValidateAsync(
                envelope, OnboardingTicketSource.OutOfBand).ConfigureAwait(false);

            Assert.That(result.Metadata.SerialNumber, Is.EqualTo("SN-42"));
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    OnboardingTicketSource.OutOfBand,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ValidateAsyncRejectsFlattenedSerializationAndInvalidCompositeUri()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string flattened = """{"payload":"AA","protected":"AA","signature":"AA"}""";
            string invalidCompositeUri = CreateTicket(
                CreateCompositeTicket(),
                m_rsaCertificate,
                "RS256",
                compositeUri: "not-an-absolute-uri");

            await AssertInvalidAsync(validator, flattened).ConfigureAwait(false);
            await AssertInvalidAsync(validator, invalidCompositeUri).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<OnboardingTicketSource>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ValidateAsyncRejectsDuplicateProtectedHeaderNames()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string payload = EncodeTicketPayload(CreateDeviceTicket());
            string duplicateHeader =
                """
                {"alg":"RS256","alg":"RS256","x5c":["x"],"cty":"opc-ticket+json;type=DeviceIdentityTicketType"}
                """;
            string encoded = CreateTicketWithHeader(payload, duplicateHeader);

            await AssertInvalidAsync(validator, encoded).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<OnboardingTicketSource>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ValidateAsyncRejectsDuplicateNamesInsideVerifiedPayload()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string validPayload = EncodeTicketPayload(CreateDeviceTicket());
            int manufacturerProperty = validPayload.IndexOf("\"ManufacturerName\"", StringComparison.Ordinal);
            Assert.That(manufacturerProperty, Is.GreaterThanOrEqualTo(0));
            string duplicatePayload = validPayload.Insert(
                manufacturerProperty, "\"ManufacturerName\":\"Spoofed\",");
            string encoded = CreateRawTicket(
                Encoding.UTF8.GetBytes(duplicatePayload),
                m_rsaCertificate,
                "RS256",
                "opc-ticket+json;type=DeviceIdentityTicketType");

            await AssertInvalidAsync(validator, encoded).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    OnboardingTicketSource.OutOfBand,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ValidateAsyncEnforcesEnvelopeSignatureAndCertificateBounds()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string tooLarge = new('x', 1024 * 1024 + 1);
            string payload = EncodeTicketPayload(CreateDeviceTicket());
            string validSignature = CreateSignatureEntry(CreateDeviceTicket(), m_rsaCertificate, "RS256");
            var repeatedSignatures = new List<string>(65);
            for (int i = 0; i < 65; i++)
            {
                repeatedSignatures.Add(validSignature);
            }
            string tooManySignatures = CreateTicketWithEntries(
                CreateDeviceTicket(), repeatedSignatures);
            string[] tooManyCertificates = new string[17];
            for (int i = 0; i < tooManyCertificates.Length; i++)
            {
                tooManyCertificates[i] = Convert.ToBase64String(m_rsaCertificate.RawData);
            }
            string tooManyCertsHeader = JsonSerializer.Serialize(new
            {
                alg = "RS256",
                x5c = tooManyCertificates,
                cty = "opc-ticket+json;type=DeviceIdentityTicketType"
            });
            string tooManyCerts = CreateTicketWithHeader(payload, tooManyCertsHeader);

            await AssertInvalidAsync(validator, tooLarge).ConfigureAwait(false);
            await AssertInvalidAsync(validator, tooManySignatures).ConfigureAwait(false);
            await AssertInvalidAsync(validator, tooManyCerts).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<OnboardingTicketSource>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ValidateAsyncRejectsPerSignatureAndUnprotectedHeaderAmbiguity()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            string payload = Base64Url(Encoding.UTF8.GetBytes(EncodeTicketPayload(CreateDeviceTicket())));
            string protectedText = ProtectedHeader(
                m_rsaCertificate, "RS256", "opc-ticket+json;type=DeviceIdentityTicketType");
            string malformedEntry =
                $$"""{"protected":"{{protectedText}}","protected":"{{protectedText}}","signature":"AA"}""";
            string encoded = $$"""{"payload":"{{payload}}","signatures":[{{malformedEntry}}]}""";

            await AssertInvalidAsync(validator, encoded).ConfigureAwait(false);
            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<OnboardingTicketSource>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ConstructorAndValidateAsyncRejectNullAndInvalidSource()
        {
            var trust = TrustedProvider();
            Assert.That(
                () => new OnboardingTicketValidator(null!, m_messageContext),
                Throws.ArgumentNullException);
            Assert.That(
                () => new OnboardingTicketValidator(trust.Object, null!),
                Throws.ArgumentNullException);
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);

            await Assert.ThatAsync(
                async () => await validator.ValidateAsync(null!, OnboardingTicketSource.Device).ConfigureAwait(false),
                Throws.ArgumentNullException);
            await Assert.ThatAsync(
                async () => await validator.ValidateAsync("{}", (OnboardingTicketSource)99).ConfigureAwait(false),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task ValidateAsyncPropagatesCancellationBeforeCallingTrustProvider()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThatAsync(
                async () => await validator.ValidateAsync(
                    "{}", OnboardingTicketSource.Device, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            trust.VerifyNoOtherCalls();
        }

        [Test]
        public async Task ValidateAsyncForwardsCancellationTokenToTrustProvider()
        {
            var trust = TrustedProvider();
            var validator = new OnboardingTicketValidator(trust.Object, m_messageContext);
            using var cancellation = new CancellationTokenSource();
            string encoded = CreateTicket(CreateDeviceTicket(), m_rsaCertificate, "RS256");

            await validator.ValidateAsync(
                encoded, OnboardingTicketSource.Device, cancellation.Token).ConfigureAwait(false);

            trust.Verify(
                provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    OnboardingTicketSource.Device,
                    cancellation.Token),
                Times.Once);
        }

        private Mock<IOnboardingTicketTrustProvider> TrustedProvider(bool isTrusted = true)
        {
            var trust = new Mock<IOnboardingTicketTrustProvider>(MockBehavior.Strict);
            trust.Setup(provider => provider.IsTrustedAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<OnboardingTicketSource>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(isTrusted));
            return trust;
        }

        private DeviceIdentityTicketType CreateDeviceTicket(
            string identityUri = "urn:acme:device:42",
            bool includeAuthority = true,
            string manufacturerName = "Acme")
        {
            var ticket = new DeviceIdentityTicketType
            {
                ManufacturerName = manufacturerName,
                ModelName = "Pump",
                ModelVersion = "1.2",
                HardwareRevision = "A",
                SoftwareRevision = "2.0",
                SerialNumber = "SN-42",
                ProductInstanceUri = identityUri
            };
            if (includeAuthority)
            {
                ticket.Authorities = [CreateCertificateAuthority()];
            }
            return ticket;
        }

        private CompositeIdentityTicketType CreateCompositeTicket(
            string[]? devices = null,
            string[]? composites = null)
        {
            var ticket = new CompositeIdentityTicketType
            {
                ManufacturerName = "Acme",
                ModelName = "Assembly",
                SerialNumber = "ASSEMBLY-7",
                CompositeInstanceUri = "urn:acme:assembly:7",
                Devices = devices ?? ["urn:acme:device:42"],
                Composites = composites ?? ["urn:acme:assembly:8"]
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
                    new ByteString((ReadOnlyMemory<byte>)m_rsaCertificate.RawData)
                ]
            };
        }

        private string CreateTicket(
            IEncodeable ticket,
            Certificate certificate,
            string algorithm,
            HashAlgorithmName? hash = null,
            RSASignaturePadding? padding = null,
            string? compositeUri = null,
            string? signingAlgorithm = null)
        {
            string contentType = ticket is DeviceIdentityTicketType
                ? "opc-ticket+json;type=DeviceIdentityTicketType"
                : "opc-ticket+json;type=CompositeIdentityTicketType";
            string header = ProtectedHeader(certificate, algorithm, contentType, compositeUri);
            string payload = EncodeTicketPayload(ticket);
            string protectedText = Base64Url(Encoding.UTF8.GetBytes(header));
            byte[] input = Encoding.ASCII.GetBytes(protectedText + "." + Base64Url(Encoding.UTF8.GetBytes(payload)));
            byte[] signature = Sign(certificate, signingAlgorithm ?? algorithm, input, hash, padding);
            return EncodeEnvelope(
                Base64Url(Encoding.UTF8.GetBytes(payload)), protectedText, Base64Url(signature));
        }

        private string CreateRawTicket(
            byte[] payloadBytes,
            Certificate certificate,
            string algorithm,
            string? contentType)
        {
            string header = ProtectedHeader(certificate, algorithm, contentType);
            string protectedText = Base64Url(Encoding.UTF8.GetBytes(header));
            string payload = Base64Url(payloadBytes);
            byte[] input = Encoding.ASCII.GetBytes(protectedText + "." + payload);
            byte[] signature = Sign(certificate, algorithm, input, null, null);
            return EncodeEnvelope(payload, protectedText, Base64Url(signature));
        }

        private string CreateTicketWithEntries(
            IEncodeable ticket,
            IReadOnlyList<string> signatureEntries)
        {
            string payload = Base64Url(Encoding.UTF8.GetBytes(EncodeTicketPayload(ticket)));
            return $$"""{"payload":"{{payload}}","signatures":[{{string.Join(",", signatureEntries)}}]}""";
        }

        private string CreateTicketWithHeader(string ticketPayload, string headerJson)
        {
            string protectedText = Base64Url(Encoding.UTF8.GetBytes(headerJson));
            byte[] input = Encoding.ASCII.GetBytes(protectedText + "." +
                Base64Url(Encoding.UTF8.GetBytes(ticketPayload)));
            byte[] signature = Sign(m_rsaCertificate, "RS256", input, null, null);
            return EncodeEnvelope(
                Base64Url(Encoding.UTF8.GetBytes(ticketPayload)),
                protectedText,
                Base64Url(signature));
        }

        private string CreateSignatureEntry(IEncodeable ticket, Certificate certificate, string algorithm)
        {
            string contentType = ticket is DeviceIdentityTicketType
                ? "opc-ticket+json;type=DeviceIdentityTicketType"
                : "opc-ticket+json;type=CompositeIdentityTicketType";
            string header = ProtectedHeader(certificate, algorithm, contentType);
            string protectedText = Base64Url(Encoding.UTF8.GetBytes(header));
            string payload = Base64Url(Encoding.UTF8.GetBytes(EncodeTicketPayload(ticket)));
            byte[] input = Encoding.ASCII.GetBytes(protectedText + "." + payload);
            string signature = Base64Url(Sign(certificate, algorithm, input, null, null));
            return $$"""{"protected":"{{protectedText}}","signature":"{{signature}}"}""";
        }

        private string EncodeTicketPayload(IEncodeable ticket)
        {
            using var encoder = new JsonEncoder(m_messageContext);
            encoder.WriteEncodeable("ticket", ticket, ticket.TypeId);
            using JsonDocument document = JsonDocument.Parse(encoder.CloseAndReturnText());
            return document.RootElement.GetProperty("ticket").GetRawText();
        }

        private static string ProtectedHeader(
            Certificate certificate,
            string algorithm,
            string? contentType,
            string? compositeUri = null)
        {
            var header = new Dictionary<string, object?>
            {
                ["alg"] = algorithm,
                ["x5c"] = new[] { Convert.ToBase64String(certificate.RawData) }
            };
            if (contentType != null)
            {
                header["cty"] = contentType;
            }
            if (compositeUri != null)
            {
                header["opc-uri"] = compositeUri;
            }
            return JsonSerializer.Serialize(header);
        }

        private static string EncodeEnvelope(string payload, string protectedText, string signatureText)
        {
            return $$"""{"payload":"{{payload}}","signatures":[""" +
                $$"""{"protected":"{{protectedText}}","signature":"{{signatureText}}"}]}""";
        }

        private static byte[] Sign(
            Certificate certificate,
            string algorithm,
            byte[] input,
            HashAlgorithmName? hashOverride,
            RSASignaturePadding? paddingOverride)
        {
            HashAlgorithmName hash = hashOverride ?? HashFor(algorithm);
            if (algorithm.StartsWith("ES", StringComparison.Ordinal))
            {
                using ECDsa key = certificate.GetECDsaPrivateKey()!;
#if NET5_0_OR_GREATER
                return key.SignData(input, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
#else
                return key.SignData(input, hash);
#endif
            }
            using RSA rsa = certificate.GetRSAPrivateKey()!;
            RSASignaturePadding padding = paddingOverride ??
                (algorithm.StartsWith("PS", StringComparison.Ordinal)
                    ? RSASignaturePadding.Pss
                    : RSASignaturePadding.Pkcs1);
            return rsa.SignData(input, hash, padding);
        }

        private static HashAlgorithmName HashFor(string algorithm)
        {
            return algorithm.EndsWith("256", StringComparison.Ordinal)
                ? HashAlgorithmName.SHA256
                : algorithm.EndsWith("384", StringComparison.Ordinal)
                    ? HashAlgorithmName.SHA384
                    : HashAlgorithmName.SHA512;
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

        private static string Base64Url(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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

        private static async Task AssertInvalidAsync(OnboardingTicketValidator validator, string encoded)
        {
            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await validator.ValidateAsync(
                    encoded, OnboardingTicketSource.OutOfBand).ConfigureAwait(false))!;
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadTicketInvalid));
        }

        private IServiceMessageContext m_messageContext = null!;
        private Certificate m_rsaCertificate = null!;
        private ITelemetryContext m_telemetry = null!;
    }
}
