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
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Checks push identity admission against authenticated transport evidence rather than discovery metadata.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class OnboardingPushIdentityValidatorTests
    {
        /// <summary>
        /// Creates independent public identities without opening certificate stores or network connections.
        /// </summary>
        [OneTimeSetUp]
        public void SetUp()
        {
            m_peer = CertificateBuilder.Create("CN=Push Device")
                .SetRSAKeySize(2048).CreateForRSA();
            m_other = CertificateBuilder.Create("CN=Other Device")
                .SetRSAKeySize(2048).CreateForRSA();
        }

        /// <summary>
        /// Releases fixture-owned certificate handles.
        /// </summary>
        [OneTimeTearDown]
        public void TearDown()
        {
            m_peer.Dispose();
            m_other.Dispose();
        }

        /// <summary>
        /// A discovery certificate never replaces the actual channel peer supplied to ticket validation.
        /// </summary>
        [Test]
        public async Task ValidationUsesActualPeerAndDetachedTicketsRatherThanDiscoveryCertificate()
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            Mock<ITransportChannel> channel = Channel();
            string[] sourceTickets = ["signed-ticket"];
            ByteString peer = ByteString.From(m_peer.RawData.AsSpan());
            OnboardingIdentitySelection accepted = Selection(m_peer);
            identities.Setup(selector => selector.SelectAsync(
                    It.Is<ArrayOf<ByteString>>(values =>
                        values.Count == 1 && values[0] == peer),
                    It.Is<ArrayOf<ByteString>>(values => values.Count == 0),
                    It.Is<ArrayOf<string>>(values => values.Count == 1 && values[0] == "signed-ticket"),
                    It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<ByteString>, ArrayOf<ByteString>, ArrayOf<string>, CancellationToken>(
                    (_, _, tickets, _) =>
                    {
                        sourceTickets[0] = "changed";
                        Assert.That(tickets[0], Is.EqualTo("signed-ticket"));
                    })
                .ReturnsAsync(accepted);
            var validator = Validator(identities);

            OnboardingIdentitySelection result = await validator.ValidatePeerAsync(channel.Object, sourceTickets)
                .ConfigureAwait(false);

            Assert.That(result, Is.SameAs(accepted));
            Assert.That(result.SelectedIdentity, Is.EqualTo(ByteString.From(m_peer.RawData.AsSpan())));
            identities.VerifyAll();
            identities.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Missing encryption, security policy, or actual peer evidence never reaches ticket selection.
        /// </summary>
        [TestCase("none")]
        [TestCase("sign")]
        [TestCase("missing-policy")]
        [TestCase("none-policy")]
        [TestCase("missing-peer")]
        public async Task ValidationRejectsUnprotectedOrUnidentifiedChannelBeforeSelectingTickets(string scenario)
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            Mock<ITransportChannel> channel = Channel();
            EndpointDescription endpoint = channel.Object.EndpointDescription;
            switch (scenario)
            {
                case "none":
                    endpoint.SecurityMode = MessageSecurityMode.None;
                    break;
                case "sign":
                    endpoint.SecurityMode = MessageSecurityMode.Sign;
                    break;
                case "missing-policy":
                    endpoint.SecurityPolicyUri = string.Empty;
                    break;
                case "none-policy":
                    endpoint.SecurityPolicyUri = SecurityPolicies.None;
                    break;
                case "missing-peer":
                    channel.SetupGet(value => value.ServerChannelCertificate).Returns(Array.Empty<byte>());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Validator(identities).ValidatePeerAsync(channel.Object, []).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(scenario == "missing-peer"
                ? StatusCodes.BadCertificateInvalid : StatusCodes.BadSecurityModeInsufficient));
            identities.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A selector cannot substitute another valid identity for the authenticated peer.
        /// </summary>
        [Test]
        public async Task ValidationRejectsSelectorSubstitution()
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            identities.Setup(selector => selector.SelectAsync(
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Selection(m_other));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Validator(identities).ValidatePeerAsync(Channel().Object, []).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            identities.VerifyAll();
        }

        /// <summary>
        /// A peer replacement during asynchronous ticket checks invalidates the earlier selection.
        /// </summary>
        [Test]
        public async Task ValidationRejectsPeerChangeDuringSelection()
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            Mock<ITransportChannel> channel = Channel();
            identities.Setup(selector => selector.SelectAsync(
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Callback(() => channel.SetupGet(value => value.ServerChannelCertificate).Returns(m_other.RawData))
                .ReturnsAsync(Selection(m_peer));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Validator(identities).ValidatePeerAsync(channel.Object, []).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
            identities.VerifyAll();
        }

        /// <summary>
        /// Ticket or identity trust rejection propagates instead of returning a discovery-derived identity.
        /// </summary>
        [Test]
        public async Task ValidationPreservesTicketTrustFailure()
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            var denied = new ServiceResultException(StatusCodes.BadTicketInvalid);
            identities.Setup(selector => selector.SelectAsync(
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(denied);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Validator(identities).ValidatePeerAsync(Channel().Object, []).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(denied));
            identities.VerifyAll();
        }

        /// <summary>
        /// Unsupported custom sessions cannot read tickets through an unpinned transport.
        /// </summary>
        [Test]
        public async Task ValidationRejectsCustomSessionBeforeReadingTickets()
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            var session = new Mock<ISession>(MockBehavior.Strict);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Validator(identities).ValidateAsync(session.Object, new NodeId("device"))
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            identities.VerifyNoOtherCalls();
            session.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The peer chain's additional certificates are issuers, never alternative selectable device identities.
        /// </summary>
        [Test]
        public async Task ValidationPassesOnlyLeafAsIdentityAndRemainingChannelCertificatesAsIssuers()
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            Mock<ITransportChannel> channel = Channel();
            byte[] leafBytes = m_peer.RawData;
            byte[] issuerBytes = m_other.RawData;
            byte[] chain = new byte[leafBytes.Length + issuerBytes.Length];
            leafBytes.CopyTo(chain, 0);
            issuerBytes.CopyTo(chain, leafBytes.Length);
            channel.SetupGet(value => value.ServerChannelCertificate).Returns(chain);
            ByteString leaf = ByteString.From(leafBytes.AsSpan());
            ByteString issuer = ByteString.From(issuerBytes.AsSpan());
            identities.Setup(selector => selector.SelectAsync(
                    It.Is<ArrayOf<ByteString>>(values => values.Count == 1 && values[0] == leaf),
                    It.Is<ArrayOf<ByteString>>(values => values.Count == 1 && values[0] == issuer),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Selection(m_peer));

            OnboardingIdentitySelection result = await Validator(identities).ValidatePeerAsync(channel.Object, [])
                .ConfigureAwait(false);

            Assert.That(result.SelectedIdentity, Is.EqualTo(leaf));
            identities.VerifyAll();
            identities.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Malformed transport evidence cannot be replaced with a syntactically valid discovery certificate.
        /// </summary>
        [Test]
        public async Task ValidationRejectsMalformedPeerBeforeSelectingTickets()
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            Mock<ITransportChannel> channel = Channel();
            channel.SetupGet(value => value.ServerChannelCertificate).Returns(new byte[] { 1, 2, 3 });

            await Assert.ThrowsAsync<CryptographicException>(async () =>
                await Validator(identities).ValidatePeerAsync(channel.Object, []).ConfigureAwait(false));

            identities.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A canceled operation does not read channel properties or invoke the selector.
        /// </summary>
        [Test]
        public async Task ValidationHonorsCancellationBeforeInspectingPeer()
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            var channel = new Mock<ITransportChannel>(MockBehavior.Strict);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await Validator(identities).ValidatePeerAsync(channel.Object, [], cancellation.Token)
                    .ConfigureAwait(false));

            identities.VerifyNoOtherCalls();
            channel.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Supplies a real channel identity and different untrusted discovery metadata.
        /// </summary>
        private Mock<ITransportChannel> Channel()
        {
            var channel = new Mock<ITransportChannel>(MockBehavior.Strict);
            channel.SetupGet(value => value.EndpointDescription).Returns(new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                ServerCertificate = ByteString.From(m_other.RawData.AsSpan())
            });
            channel.SetupGet(value => value.ServerChannelCertificate).Returns(m_peer.RawData);
            return channel;
        }

        /// <summary>
        /// Creates the production validator with the same selector used by registrar identity admission.
        /// </summary>
        private static OnboardingPushIdentityValidator Validator(Mock<IOnboardingIdentitySelector> identities)
        {
            return new OnboardingPushIdentityValidator(identities.Object, NUnitTelemetryContext.Create());
        }

        /// <summary>
        /// Creates an accepted ticket selection for the specified exact certificate.
        /// </summary>
        private static OnboardingIdentitySelection Selection(Certificate certificate)
        {
            return new OnboardingIdentitySelection(
                ByteString.From(certificate.RawData.AsSpan()),
                new DeviceIdentityTicketType(),
                "urn:device:push-test");
        }

        /// <summary>The authenticated device certificate owned by the fixture.</summary>
        private Certificate m_peer = null!;
        /// <summary>A different certificate used in discovery and substitution cases.</summary>
        private Certificate m_other = null!;
    }
}
