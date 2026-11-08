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
using Opc.Ua.Client.Tests.SessionContexts;
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
                await Validator(identities).ValidateAsync(session.Object, new NodeId("device", 0))
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            identities.VerifyNoOtherCalls();
            session.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Successful admission retains the same real encrypted generation for later generated-client operations.
        /// Local disposal never closes the original session.
        /// </summary>
        [TestCase("raw")]
        [TestCase("managed")]
        public async Task CaptureRetainsValidatedGenerationWithoutTakingConnectionOwnership(string shape)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            fixture.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_peer.RawData);
            fixture.Handler = (request, _) => new ValueTask<IServiceResponse>(Respond(request));
            Mock<IOnboardingIdentitySelector> identities = AcceptingSelector();
            ISession session = shape == "raw" ? fixture.InnerSession : fixture.ManagedSession;
            var deviceId = new NodeId("push-device", 1);
            await using OnboardingPushSession admitted = await Validator(identities).CaptureAsync(session, deviceId)
                .ConfigureAwait(false);

            Assert.That(admitted.Identity.SelectedIdentity, Is.EqualTo(ByteString.From(m_peer.RawData.AsSpan())));
            Assert.That(admitted.Invalidated.IsCompleted, Is.False);
            var device = new ProvisionableDeviceTypeClient(admitted.Session, deviceId, fixture.Telemetry);
            ArrayOf<string> tickets = await device.RequestTicketsAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(tickets.Count, Is.EqualTo(1));
            Assert.That(tickets[0], Is.EqualTo("signed-ticket"));
            int calls = 0;
            foreach (IServiceRequest request in fixture.Requests)
            {
                if (request is CallRequest call)
                {
                    calls++;
                    Assert.That(call.RequestHeader.AuthenticationToken, Is.EqualTo(new NodeId("original-token", 0)));
                    Assert.That(call.MethodsToCall[0].ObjectId, Is.EqualTo(deviceId));
                }
            }
            Assert.That(calls, Is.EqualTo(2));
            await admitted.DisposeAsync().ConfigureAwait(false);
            await admitted.DisposeAsync().ConfigureAwait(false);
            Assert.That(admitted.Invalidated.IsCompleted, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = admitted.Session);
            Assert.That(fixture.InnerSession.SessionId, Is.EqualTo(new NodeId("original-session", 0)));
            Assert.That(fixture.Requests.Exists(request => request is CloseSessionRequest), Is.False);
            identities.VerifyAll();
            identities.Verify(selector => selector.SelectAsync(
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Neither a replacement session or physical channel nor changed namespaces or encryption can receive
        /// a follow-up push request authorized by an earlier device admission.
        /// </summary>
        [TestCase("recreate")]
        [TestCase("transport")]
        [TestCase("namespace")]
        [TestCase("downgrade")]
        public async Task CapturedAdmissionRejectsChangedDispatchContextBeforeFollowUpCall(string mutation)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            fixture.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_peer.RawData);
            fixture.Handler = (request, _) => new ValueTask<IServiceResponse>(Respond(request));
            Mock<IOnboardingIdentitySelector> identities = AcceptingSelector();
            var deviceId = new NodeId("push-device", 1);
            await using OnboardingPushSession admitted = await Validator(identities)
                .CaptureAsync(fixture.ManagedSession, deviceId).ConfigureAwait(false);
            var device = new ProvisionableDeviceTypeClient(admitted.Session, deviceId, fixture.Telemetry);
            int admittedRequestCount = fixture.Requests.Count;
            var replacement = new Mock<ITransportChannel>();
            switch (mutation)
            {
                case "recreate":
                    fixture.Recreate();
                    break;
                case "transport":
                    fixture.ReplaceTransport(replacement.Object);
                    break;
                case "namespace":
                    fixture.InnerSession.NamespaceUris.Append("urn:changed-after-admission");
                    break;
                case "downgrade":
                    fixture.Channel.Object.EndpointDescription.SecurityMode = MessageSecurityMode.Sign;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await device.RequestTicketsAsync(CancellationToken.None).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(mutation == "downgrade"
                ? StatusCodes.BadSecurityModeInsufficient : StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Has.Count.EqualTo(admittedRequestCount));
            replacement.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Replacing the originating session during ticket evaluation prevents returning a usable push context.
        /// </summary>
        [Test]
        public async Task CaptureRejectsGenerationChangeDuringTicketSelectionWithoutClosingOwner()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            fixture.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_peer.RawData);
            fixture.Handler = (request, _) => new ValueTask<IServiceResponse>(Respond(request));
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            identities.Setup(selector => selector.SelectAsync(
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Callback(fixture.Recreate)
                .ReturnsAsync(Selection(m_peer));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Validator(identities).CaptureAsync(fixture.ManagedSession, new NodeId("push-device", 1))
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.InnerSession.SessionId, Is.EqualTo(new NodeId("replacement-session", 0)));
            Assert.That(fixture.Requests.Exists(request => request is CloseSessionRequest), Is.False);
            identities.VerifyAll();
        }

        /// <summary>
        /// Missing required inputs are rejected before the session or ticket policy is accessed.
        /// </summary>
        [Test]
        public async Task CaptureRejectsMissingInputsBeforeBorrowingSession()
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            var session = new Mock<ISession>(MockBehavior.Strict);
            OnboardingPushIdentityValidator validator = Validator(identities);

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await validator.CaptureAsync(null!, new NodeId("push-device", 0)).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await validator.CaptureAsync(session.Object, NodeId.Null).ConfigureAwait(false));

            session.VerifyNoOtherCalls();
            identities.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Cancellation before capture cannot dispatch ticket reads or invoke ticket validation.
        /// </summary>
        [Test]
        public async Task CaptureHonorsCancellationBeforeReadingTickets()
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            var session = new Mock<ISession>(MockBehavior.Strict);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await Validator(identities).CaptureAsync(
                    session.Object, new NodeId("push-device", 0), cancellation.Token).ConfigureAwait(false));

            session.VerifyNoOtherCalls();
            identities.VerifyNoOtherCalls();
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
        /// Returns a ticket selection for the fixture's actual peer and checks the generated ticket output.
        /// </summary>
        private Mock<IOnboardingIdentitySelector> AcceptingSelector()
        {
            var identities = new Mock<IOnboardingIdentitySelector>(MockBehavior.Strict);
            identities.Setup(selector => selector.SelectAsync(
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.Is<ArrayOf<string>>(tickets => tickets.Count == 1 && tickets[0] == "signed-ticket"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Selection(m_peer));
            return identities;
        }

        /// <summary>
        /// Answers generated method resolution and RequestTickets on a real stack session without a socket.
        /// </summary>
        private static IServiceResponse Respond(IServiceRequest request)
        {
            ArrayOf<string> tickets = ["signed-ticket"];
            return request switch
            {
                BrowseRequest => new BrowseResponse { Results = [new BrowseResult { References = [] }] },
                CallRequest => new CallResponse
                {
                    Results = [new CallMethodResult { OutputArguments = [Variant.From(tickets)] }]
                },
                _ => throw new InvalidOperationException("Unexpected push admission request.")
            };
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
