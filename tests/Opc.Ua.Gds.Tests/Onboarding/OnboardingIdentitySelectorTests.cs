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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Tests ticket precedence, composite membership and certificate selection independently of PKI.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class OnboardingIdentitySelectorTests
    {
        /// <summary>
        /// Administrator-supplied tickets take precedence, without interpreting unrelated SAN URIs.
        /// </summary>
        [Test]
        public async Task OutOfBandTicketTakesPrecedenceAndUnrelatedSanUrisAreIgnored()
        {
            using Certificate certificate = CreateIdentity(kProductUri, "urn:test:unrelated");
            var store = new MemoryTicketStore();
            await AddStoredAsync(store, "stored").ConfigureAwait(false);
            var validated = new Dictionary<string, ValidatedOnboardingTicket>
            {
                ["stored"] = DeviceTicket("stored"),
                ["supplied"] = DeviceTicket("supplied")
            };
            (OnboardingIdentitySelector selector, Mock<IOnboardingTicketValidator> validator,
                Mock<IOnboardingIdentityTrustProvider> trust) = CreateSelector(store, validated);

            OnboardingIdentitySelection selection = await selector.SelectAsync(
                [ByteString.From(certificate.RawData.AsSpan())], [], ["supplied"]).ConfigureAwait(false);

            Assert.That(selection.ProductInstanceUri, Is.EqualTo(kProductUri));
            Assert.That(selection.MatchingTicket.SerialNumber, Is.EqualTo("stored"));
            Assert.That(selection.SelectedIdentity.ToArray(), Is.EqualTo(certificate.RawData));
            Assert.That(selection.EncodedTicket, Is.EqualTo("stored"));
            Assert.That(selection.EncodedComposite, Is.Empty);
            validator.Verify(value => value.ValidateAsync(
                "stored", OnboardingTicketSource.OutOfBand, It.IsAny<CancellationToken>()), Times.Once);
            validator.Verify(value => value.ValidateAsync(
                "supplied", OnboardingTicketSource.Device, It.IsAny<CancellationToken>()), Times.Once);
            trust.Verify(value => value.IsTrustedAsync(
                It.IsAny<Certificate>(), It.IsAny<CertificateCollection>(),
                It.Is<BaseTicketType>(ticket => ticket.SerialNumber == "stored"),
                It.IsAny<CancellationToken>()), Times.Once);
            trust.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Malformed identities and issuers cannot prevent a separately verifiable identity from being selected.
        /// </summary>
        [Test]
        public async Task MalformedIdentityAndIssuerEntriesDoNotBlockValidIdentity()
        {
            using Certificate certificate = CreateIdentity(kProductUri);
            var store = new MemoryTicketStore();
            var validated = new Dictionary<string, ValidatedOnboardingTicket> { ["ticket"] = DeviceTicket("device") };
            (OnboardingIdentitySelector selector, _, Mock<IOnboardingIdentityTrustProvider> trust) =
                CreateSelector(store, validated);
            int issuerCount = -1;
            trust.Setup(value => value.IsTrustedAsync(
                It.IsAny<Certificate>(), It.IsAny<CertificateCollection>(),
                It.IsAny<BaseTicketType>(), It.IsAny<CancellationToken>()))
                .Callback<Certificate, CertificateCollection, BaseTicketType, CancellationToken>(
                    (_, issuers, _, _) => issuerCount = issuers.Count)
                .ReturnsAsync(true);

            OnboardingIdentitySelection selection = await selector.SelectAsync(
                [
                    default, ByteString.From(new byte[] { 1, 2, 3 }),
                    ByteString.From(certificate.RawData.AsSpan())
                ],
                [
                    default, ByteString.From(new byte[] { 1, 2, 3 }),
                    ByteString.From(certificate.RawData.AsSpan())
                ],
                ["invalid", "ticket"]).ConfigureAwait(false);

            Assert.That(selection.SelectedIdentity.ToArray(), Is.EqualTo(certificate.RawData));
            trust.Verify(value => value.IsTrustedAsync(
                It.IsAny<Certificate>(),
                It.IsAny<CertificateCollection>(),
                It.IsAny<BaseTicketType>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(issuerCount, Is.EqualTo(1));
        }

        /// <summary>
        /// An externally visible device in nested composites uses only the outermost composite URI.
        /// </summary>
        [TestCase(kOuterUri, true)]
        [TestCase(kInnerUri, false)]
        [TestCase("urn:test:unrelated", false)]
        public async Task NestedCompositeRequiresOutermostUri(string compositeUri, bool allowed)
        {
            using Certificate certificate = CreateIdentity(kProductUri, compositeUri);
            var store = new MemoryTicketStore();
            var validated = new Dictionary<string, ValidatedOnboardingTicket>
            {
                ["device"] = DeviceTicket("device"),
                ["inner"] = CompositeTicket(kInnerUri, [kProductUri], []),
                ["outer"] = CompositeTicket(kOuterUri, [], [kInnerUri])
            };
            (OnboardingIdentitySelector selector, _, Mock<IOnboardingIdentityTrustProvider> trust) =
                CreateSelector(store, validated);
            if (allowed)
            {
                OnboardingIdentitySelection selection = await selector.SelectAsync(
                    [ByteString.From(certificate.RawData.AsSpan())], [], ["device", "inner", "outer"])
                    .ConfigureAwait(false);

                Assert.That(selection.ProductInstanceUri, Is.EqualTo(kProductUri));
                Assert.That(selection.MatchingTicket, Is.TypeOf<DeviceIdentityTicketType>());
                Assert.That(((DeviceIdentityTicketType)selection.MatchingTicket).ProductInstanceUri,
                    Is.EqualTo(kProductUri));
                Assert.That(selection.EncodedTicket, Is.EqualTo("device"));
                Assert.That(selection.EncodedComposite, Is.EqualTo("outer"));
                trust.Verify(value => value.IsTrustedAsync(
                    It.IsAny<Certificate>(), It.IsAny<CertificateCollection>(),
                    It.Is<BaseTicketType>(ticket => ticket is DeviceIdentityTicketType),
                    It.IsAny<CancellationToken>()), Times.Once);
                trust.VerifyNoOtherCalls();
            }
            else
            {
                await AssertStatusAsync(StatusCodes.BadCertificateInvalid, async () =>
                    await selector.SelectAsync(
                        [ByteString.From(certificate.RawData.AsSpan())], [], ["device", "inner", "outer"])
                        .ConfigureAwait(false));
                trust.VerifyNoOtherCalls();
            }
        }

        /// <summary>
        /// A composite membership list cannot replace the device's own signed identity ticket or its authorities.
        /// </summary>
        [Test]
        public async Task CompositeWithoutMatchingDeviceTicketCannotAuthorizeCertificate()
        {
            using Certificate certificate = CreateIdentity(kProductUri, kOuterUri);
            var store = new MemoryTicketStore();
            var validated = new Dictionary<string, ValidatedOnboardingTicket>
            {
                ["composite"] = CompositeTicket(kOuterUri, [kProductUri], [])
            };
            (OnboardingIdentitySelector selector, _, Mock<IOnboardingIdentityTrustProvider> trust) =
                CreateSelector(store, validated);

            await AssertStatusAsync(StatusCodes.BadNotFound, async () =>
                await selector.SelectAsync(
                    [ByteString.From(certificate.RawData.AsSpan())], [], ["composite"]).ConfigureAwait(false));

            trust.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A trusted composite's authority cannot authorize a certificate rejected by its matching device ticket.
        /// </summary>
        [Test]
        public async Task CompositeAuthorityCannotReplaceMatchingDeviceTicketAuthorities()
        {
            using Certificate certificate = CreateIdentity(kProductUri, kOuterUri);
            var store = new MemoryTicketStore();
            var validated = new Dictionary<string, ValidatedOnboardingTicket>
            {
                ["device"] = DeviceTicket("device"),
                ["composite"] = CompositeTicket(kOuterUri, [kProductUri], [])
            };
            (OnboardingIdentitySelector selector, _, Mock<IOnboardingIdentityTrustProvider> trust) =
                CreateSelector(store, validated, trusted: false);
            trust.Setup(value => value.IsTrustedAsync(
                It.IsAny<Certificate>(), It.IsAny<CertificateCollection>(),
                It.Is<BaseTicketType>(ticket => ticket is CompositeIdentityTicketType),
                It.IsAny<CancellationToken>())).Returns(new ValueTask<bool>(true));

            await AssertStatusAsync(StatusCodes.BadCertificateInvalid, async () =>
                await selector.SelectAsync(
                    [ByteString.From(certificate.RawData.AsSpan())], [], ["device", "composite"])
                    .ConfigureAwait(false));

            trust.Verify(value => value.IsTrustedAsync(
                It.IsAny<Certificate>(), It.IsAny<CertificateCollection>(),
                It.Is<BaseTicketType>(ticket => ticket is DeviceIdentityTicketType),
                It.IsAny<CancellationToken>()), Times.Once);
            trust.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A trusted device-supplied composite also constrains an out-of-band device ticket.
        /// </summary>
        [Test]
        public async Task CompositeMembershipRejectsProductOnlyIdentityEvenWithStoredDeviceTicket()
        {
            using Certificate certificate = CreateIdentity(kProductUri);
            var store = new MemoryTicketStore();
            await AddStoredAsync(store, "device").ConfigureAwait(false);
            var validated = new Dictionary<string, ValidatedOnboardingTicket>
            {
                ["device"] = DeviceTicket("device"),
                ["composite"] = CompositeTicket(kOuterUri, [kProductUri], [])
            };
            (OnboardingIdentitySelector selector, _, Mock<IOnboardingIdentityTrustProvider> trust) =
                CreateSelector(store, validated);

            await AssertStatusAsync(StatusCodes.BadCertificateInvalid, async () =>
                await selector.SelectAsync(
                    [ByteString.From(certificate.RawData.AsSpan())], [], ["composite"]).ConfigureAwait(false));

            trust.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A composite builder's protected opc-uri also requires its URI in the identity.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ProtectedCompositeUriConstrainsDeviceTicket(bool includeCompositeUri)
        {
            using Certificate certificate = CreateIdentity(
                includeCompositeUri ? [kProductUri, kOuterUri] : [kProductUri]);
            var store = new MemoryTicketStore();
            var validated = new Dictionary<string, ValidatedOnboardingTicket>
            {
                ["device"] = new(new DeviceIdentityTicketType
                {
                    ProductInstanceUri = kProductUri,
                    ManufacturerName = "Test manufacturer"
                }, kOuterUri)
            };
            (OnboardingIdentitySelector selector, _, Mock<IOnboardingIdentityTrustProvider> trust) =
                CreateSelector(store, validated);
            if (includeCompositeUri)
            {
                OnboardingIdentitySelection selection = await selector.SelectAsync(
                    [ByteString.From(certificate.RawData.AsSpan())], [], ["device"]).ConfigureAwait(false);
                Assert.That(selection.ProductInstanceUri, Is.EqualTo(kProductUri));
            }
            else
            {
                await AssertStatusAsync(StatusCodes.BadCertificateInvalid, async () =>
                    await selector.SelectAsync(
                        [ByteString.From(certificate.RawData.AsSpan())], [], ["device"]).ConfigureAwait(false));
                trust.VerifyNoOtherCalls();
            }
        }

        /// <summary>
        /// A cycle cannot make a composite member eligible for standalone identity selection.
        /// </summary>
        [Test]
        public async Task CyclicCompositeMembershipDoesNotAuthorizeProductOnlyIdentity()
        {
            using Certificate certificate = CreateIdentity(kProductUri, kOuterUri, kInnerUri);
            var store = new MemoryTicketStore();
            var validated = new Dictionary<string, ValidatedOnboardingTicket>
            {
                ["device"] = DeviceTicket("device"),
                ["inner"] = CompositeTicket(kInnerUri, [kProductUri], [kOuterUri]),
                ["outer"] = CompositeTicket(kOuterUri, [], [kInnerUri])
            };
            (OnboardingIdentitySelector selector, _, Mock<IOnboardingIdentityTrustProvider> trust) =
                CreateSelector(store, validated);

            await AssertStatusAsync(StatusCodes.BadCertificateInvalid, async () =>
                await selector.SelectAsync(
                    [ByteString.From(certificate.RawData.AsSpan())], [], ["device", "inner", "outer"])
                    .ConfigureAwait(false));

            trust.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Missing, unusable and cryptographically rejected inputs have distinct method statuses.
        /// </summary>
        [Test]
        public async Task SelectionReportsSpecifiedMissingTicketAndCertificateStatuses()
        {
            using Certificate certificate = CreateIdentity(kProductUri);
            var store = new MemoryTicketStore();
            var validated = new Dictionary<string, ValidatedOnboardingTicket> { ["ticket"] = DeviceTicket("device") };
            (OnboardingIdentitySelector selector, _, _) = CreateSelector(store, validated, trusted: false);

            await AssertStatusAsync(StatusCodes.BadCertificateInvalid, async () =>
                await selector.SelectAsync([], [], []).ConfigureAwait(false));
            await AssertStatusAsync(StatusCodes.BadTicketRequired, async () =>
                await selector.SelectAsync(
                    [ByteString.From(certificate.RawData.AsSpan())], [], []).ConfigureAwait(false));
            await AssertStatusAsync(StatusCodes.BadNotFound, async () =>
                await selector.SelectAsync(
                    [ByteString.From(certificate.RawData.AsSpan())], [], ["invalid"]).ConfigureAwait(false));
            await AssertStatusAsync(StatusCodes.BadCertificateInvalid, async () =>
                await selector.SelectAsync(
                    [ByteString.From(certificate.RawData.AsSpan())], [], ["ticket"]).ConfigureAwait(false));
        }

        /// <summary>
        /// Cancellation before the request avoids even ticket-store access.
        /// </summary>
        [Test]
        public async Task CancellationBeforeSelectionPerformsNoProviderWork()
        {
            var store = new Mock<ITicketStore>(MockBehavior.Strict);
            var validator = new Mock<IOnboardingTicketValidator>(MockBehavior.Strict);
            var trust = new Mock<IOnboardingIdentityTrustProvider>(MockBehavior.Strict);
            var selector = new OnboardingIdentitySelector(store.Object, validator.Object, trust.Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await selector.SelectAsync([], [], [], cancellation.Token).ConfigureAwait(false));

            store.VerifyNoOtherCalls();
            validator.VerifyNoOtherCalls();
            trust.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Infrastructure failures must not be reclassified as an invalid ticket.
        /// </summary>
        [Test]
        public async Task ProviderConfigurationFailurePropagates()
        {
            using Certificate certificate = CreateIdentity(kProductUri);
            var store = new MemoryTicketStore();
            var validator = new Mock<IOnboardingTicketValidator>(MockBehavior.Strict);
            validator.Setup(value => value.ValidateAsync(
                "ticket", OnboardingTicketSource.Device, It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadConfigurationError));
            var trust = new Mock<IOnboardingIdentityTrustProvider>(MockBehavior.Strict);
            var selector = new OnboardingIdentitySelector(store, validator.Object, trust.Object);

            await AssertStatusAsync(StatusCodes.BadConfigurationError, async () =>
                await selector.SelectAsync(
                    [ByteString.From(certificate.RawData.AsSpan())], [], ["ticket"]).ConfigureAwait(false));

            trust.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Selection results retain independent snapshots of caller-owned data.
        /// </summary>
        [Test]
        public void SelectionSnapshotsCertificateAndTicket()
        {
            byte[] bytes = [1, 2, 3];
            var ticket = new DeviceIdentityTicketType { ProductInstanceUri = kProductUri, SerialNumber = "original" };
            var result = new OnboardingIdentitySelection(ByteString.From(bytes), ticket, kProductUri);
            bytes[0] = 9;
            ticket.SerialNumber = "changed";
            result.MatchingTicket.SerialNumber = "changed again";

            Assert.That(result.SelectedIdentity.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(result.MatchingTicket.SerialNumber, Is.EqualTo("original"));
        }

        /// <summary>
        /// Creates a selector with explicit envelope and identity-trust collaborators.
        /// </summary>
        private static (
            OnboardingIdentitySelector Selector,
            Mock<IOnboardingTicketValidator> Validator,
            Mock<IOnboardingIdentityTrustProvider> Trust) CreateSelector(
                ITicketStore store,
                Dictionary<string, ValidatedOnboardingTicket> validated,
                bool trusted = true)
        {
            var validator = new Mock<IOnboardingTicketValidator>(MockBehavior.Strict);
            validator.Setup(value => value.ValidateAsync(
                It.IsAny<string>(), It.IsAny<OnboardingTicketSource>(), It.IsAny<CancellationToken>()))
                .Returns<string, OnboardingTicketSource, CancellationToken>((encoded, _, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!validated.TryGetValue(encoded, out ValidatedOnboardingTicket? ticket))
                    {
                        throw new ServiceResultException(StatusCodes.BadTicketInvalid);
                    }
                    return new ValueTask<ValidatedOnboardingTicket>(ticket);
                });
            var trust = new Mock<IOnboardingIdentityTrustProvider>(MockBehavior.Strict);
            trust.Setup(value => value.IsTrustedAsync(
                It.IsAny<Certificate>(), It.IsAny<CertificateCollection>(),
                It.IsAny<BaseTicketType>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(trusted));
            return (new OnboardingIdentitySelector(store, validator.Object, trust.Object), validator, trust);
        }

        /// <summary>
        /// Creates a device ticket whose envelope validation is represented by the mock.
        /// </summary>
        private static ValidatedOnboardingTicket DeviceTicket(string serial)
        {
            return new ValidatedOnboardingTicket(new DeviceIdentityTicketType
            {
                ProductInstanceUri = kProductUri,
                SerialNumber = serial,
                ManufacturerName = "Test manufacturer"
            }, string.Empty);
        }

        /// <summary>
        /// Creates a validated composite membership declaration for matching tests.
        /// </summary>
        private static ValidatedOnboardingTicket CompositeTicket(
            string uri, ArrayOf<string> devices, ArrayOf<string> composites)
        {
            return new ValidatedOnboardingTicket(new CompositeIdentityTicketType
            {
                CompositeInstanceUri = uri,
                ManufacturerName = "Test manufacturer",
                Devices = devices,
                Composites = composites
            }, string.Empty);
        }

        /// <summary>
        /// Stores an encoded ticket independently of its validated fixture.
        /// </summary>
        private static ValueTask AddStoredAsync(MemoryTicketStore store, string encoded)
        {
            return store.AddAsync(
                encoded, Encoding.UTF8.GetBytes(encoded),
                new TicketMetadata(TicketKind.DeviceIdentity, "Test manufacturer", string.Empty, encoded, kProductUri));
        }

        /// <summary>
        /// Creates a real certificate containing the exact SAN URI names under test.
        /// </summary>
        private static Certificate CreateIdentity(params string[] uris)
        {
            return CertificateBuilder.Create("CN=Onboarding identity selection")
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .AddExtension(new X509SubjectAltNameExtension(uris, []))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Checks the exact method-level status rather than just the exception type.
        /// </summary>
        private static async Task AssertStatusAsync(StatusCode status, Func<Task> action)
        {
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(action);
            Assert.That(error!.StatusCode, Is.EqualTo(status));
        }

        /// <summary>
        /// The product URI being matched.
        /// </summary>
        private const string kProductUri = "urn:test:onboarding:device";

        /// <summary>
        /// The inner composite URI.
        /// </summary>
        private const string kInnerUri = "urn:test:onboarding:inner";

        /// <summary>
        /// The outermost composite URI.
        /// </summary>
        private const string kOuterUri = "urn:test:onboarding:outer";
    }
}
