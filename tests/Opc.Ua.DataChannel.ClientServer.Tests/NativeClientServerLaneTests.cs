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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebTransport.Tests;
using Opc.Ua.Client;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.DataChannels.ClientServer.Tests
{
    /// <summary>
    /// Required live acceptance over the production native QUIC and HTTP/3 WebTransport backends.
    /// Missing reviewed assets fail setup rather than skip the native gate.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("DataChannelClientServerNative")]
    public sealed class NativeClientServerLaneTests
    {
        /// <summary>
        /// Loads caller-selected, digest-verified actual native images.
        /// </summary>
        [OneTimeSetUp]
        public async Task LoadAsync()
        {
            string corePath = typeof(DataChannelClientServerClient).Assembly.Location;
            string coreHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(corePath)
                .ConfigureAwait(false)));
            await TestContext.Out.WriteLineAsync($"Loaded Core SHA256={coreHash}; path={corePath}").ConfigureAwait(false);
            (m_backend, m_http3) = await WebTransportTestEnvironment.LoadAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Requires every test to release actual native handles, jobs, shared budgets and UASC buffers.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_harness != null)
            {
                await m_harness.DisposeAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    DataChannelResourceAssertions.AreReleased(m_harness.Environment.ClientBudget.Usage);
                    DataChannelResourceAssertions.AreReleased(m_harness.Environment.ServerBudget.Usage);
                    Assert.That(m_harness.Buffers.Outstanding, Is.Zero);
                });
                m_harness = null;
            }
            NativeQuicCounts counts = default;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                counts = await m_backend!.GetCountsAsync().ConfigureAwait(false);
                if (counts == default)
                {
                    break;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            Assert.That(counts, Is.Default);
        }

        /// <summary>
        /// Proves real discovery, Session activation, Read and Publish over a distinct inner UASC lane,
        /// while the same native context retains an independently authorized resource after lane closure.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic, MessageSecurityMode.SignAndEncrypt, true)]
        [TestCase(DataChannelCarrierKind.WebTransport, MessageSecurityMode.SignAndEncrypt, true)]
        [TestCase(DataChannelCarrierKind.Quic, MessageSecurityMode.Sign, true)]
        [TestCase(DataChannelCarrierKind.WebTransport, MessageSecurityMode.Sign, true)]
        [TestCase(DataChannelCarrierKind.Quic, MessageSecurityMode.SignAndEncrypt, false)]
        [TestCase(DataChannelCarrierKind.WebTransport, MessageSecurityMode.SignAndEncrypt, false)]
        public async Task NativeLaneRunsDiscoverySessionReadPublishAndPreservesIndependentFlow(
            DataChannelCarrierKind carrier, MessageSecurityMode mode, bool sessionControlIntegration)
        {
            m_harness = await NativeLaneHarness.CreateAsync(
                m_backend!, m_http3!, carrier, mode, sessionControlIntegration: sessionControlIntegration)
                .ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            Assert.Multiple(() =>
            {
                Assert.That(harness.ClientContext.Channels, Is.Empty, "A native connection must not open a lane.");
                Assert.That(harness.ServerContext.Channels, Is.Empty);
                Assert.That(harness.NativeClient.Peer.HandshakeConfirmed, Is.True);
                Assert.That(harness.NativeServer.Peer.HandshakeConfirmed, Is.True);
                Assert.That(harness.NativeClient.Peer.MutualTlsAuthenticated, Is.True);
                Assert.That(harness.NativeServer.Peer.MutualTlsAuthenticated, Is.True);
                Assert.That(harness.NativeClient.Peer.Used0Rtt, Is.False);
                Assert.That(harness.NativeClient.Peer.ApplicationProtocol,
                    Is.EqualTo(carrier == DataChannelCarrierKind.Quic ? "opcua-flow/1" : "h3"));
                Assert.That(harness.ClientContext.Identity.Peer.PeerApplicationUri,
                    Is.EqualTo(WebTransportTestEnvironment.ServerUri));
                Assert.That(harness.ClientContext.Identity.Peer.PeerKeyThumbprint,
                    Is.EqualTo(harness.ServerContext.Identity.Peer.LocalKeyThumbprint));
                Assert.That(harness.Server.EnableDataChannelModel, Is.False);
            });

            (DataChannel independent, DataChannel independentPeer) =
                await harness.OpenIndependentAsync().ConfigureAwait(false);
            await using DataChannel independentLifetime = independent;
            await using DataChannel peerLifetime = independentPeer;
            await AssertIndependentPayloadAsync(independent, independentPeer, [11, 0, 255], harness.Token)
                .ConfigureAwait(false);

            await using DataChannelClientServerConnection lane = await harness.OpenLaneAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(lane.SecureChannelId, Is.Not.Zero);
                Assert.That(lane.Flow.Parameters.ResourceUri, Is.EqualTo(DataChannelProfiles.ClientServerResource));
                Assert.That(lane.Flow.Parameters.Protocol, Is.EqualTo(DataChannelProfiles.OpaquePayload));
                Assert.That(lane.Flow.Parameters.Direction, Is.EqualTo(DataChannelDirection.Duplex));
                Assert.That(lane.Flow.Parameters.Delivery, Is.EqualTo(DataChannelDeliveryMode.Ordered));
                Assert.That(lane.Flow.Parameters.Protection, Is.EqualTo(DataChannelProtectionMode.Confidential));
                Assert.That(lane.Flow.Parameters.AllowQueueExpiry, Is.False);
                Assert.That(lane.Services.EndpointDescription.SecurityMode, Is.EqualTo(mode));
                Assert.That(lane.Services.EndpointDescription.TransportProfileUri, Is.EqualTo(Profiles.UaTcpTransport));
            });
            IServiceResponse discovery = await lane.Services.SendRequestAsync(new GetEndpointsRequest
            {
                RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, RequestHandle = 10 },
                EndpointUrl = harness.ServiceEndpoint.AbsoluteUri
            }, harness.Token).ConfigureAwait(false);
            Assert.That(discovery, Is.TypeOf<GetEndpointsResponse>());
            var endpoints = (GetEndpointsResponse)discovery;
            Assert.Multiple(() =>
            {
                Assert.That(endpoints.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(endpoints.Endpoints.ToArray(), Has.Some.Matches<EndpointDescription>(endpoint =>
                    endpoint.EndpointUrl == harness.ServiceEndpoint.AbsoluteUri &&
                    endpoint.SecurityMode == mode &&
                    endpoint.Server.ApplicationUri == WebTransportTestEnvironment.ServerUri));
            });
            using (ISession session = await harness.OpenSessionAsync(lane).ConfigureAwait(false))
            {
                Assert.That(session.Connected, Is.True);
                Assert.That(session.SessionId.IsNull, Is.False);
                ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [
                    new ReadValueId { NodeId = VariableIds.Server_ServerStatus_CurrentTime, AttributeId = Attributes.Value }
                ], harness.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(read.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(read.Results.Count, Is.EqualTo(1));
                    Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(read.Results[0].WrappedValue.TryGetValue(out DateTimeUtc _), Is.True);
                });
                CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                    null, 20, 100, 10, 10, true, 0, harness.Token).ConfigureAwait(false);
                CreateMonitoredItemsResponse items = await session.CreateMonitoredItemsAsync(
                    null, subscription.SubscriptionId, TimestampsToReturn.Both,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                                AttributeId = Attributes.Value
                            },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 123,
                                SamplingInterval = 20,
                                QueueSize = 2,
                                DiscardOldest = true
                            }
                        }
                    ], harness.Token).ConfigureAwait(false);
                Assert.That(items.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                PublishResponse publish = await session.PublishAsync(null, [], harness.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(publish.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(publish.SubscriptionId, Is.EqualTo(subscription.SubscriptionId));
                    Assert.That(publish.NotificationMessage.SequenceNumber, Is.EqualTo(1));
                    Assert.That(publish.NotificationMessage.NotificationData.Count, Is.EqualTo(1));
                });
                Assert.That(publish.NotificationMessage.NotificationData[0]
                    .TryGetValue(out DataChangeNotification? notification), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(notification!.MonitoredItems.Count, Is.InRange(1, 2));
                    foreach (MonitoredItemNotification item in notification.MonitoredItems)
                    {
                        Assert.That(item.ClientHandle, Is.EqualTo(123));
                        Assert.That(item.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(item.Value.WrappedValue.TryGetValue(out DateTimeUtc _), Is.True);
                    }
                });
                DeleteSubscriptionsResponse deleted = await session.DeleteSubscriptionsAsync(
                    null, [subscription.SubscriptionId], harness.Token).ConfigureAwait(false);
                Assert.That(deleted.Results[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(await session.CloseAsync(10000, false, harness.Token).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));
            }
            await lane.DisposeAsync().ConfigureAwait(false);
            Assert.That(lane.Completion.IsCompletedSuccessfully, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(harness.ClientContext.IsReady, Is.True,
                    $"Client context termination: {string.Join(",", harness.ClientClosures)}; " +
                    $"transport failure: {harness.ClientTransportFailure}");
                Assert.That(harness.ServerContext.IsReady, Is.True,
                    $"Server context termination: {string.Join(",", harness.ServerClosures)}; " +
                    $"transport failure: {harness.ServerTransportFailure}");
            });
            await AssertIndependentPayloadAsync(independent, independentPeer, [99, 0, 128], harness.Token)
                .ConfigureAwait(false);
            await AssertIndependentPayloadAsync(independentPeer, independent, [31, 7], harness.Token)
                .ConfigureAwait(false);
            Assert.That(await harness.ClientContext.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await harness.ServerContext.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(harness.Buffers.Rents, Is.GreaterThan(10));

            List<ByteString> outgoing = UacpChunks(harness.Environment.ClientBackend.Writes);
            List<ByteString> incoming = UacpChunks(harness.Environment.ServerBackend.Writes);
            Assert.Multiple(() =>
            {
                Assert.That(outgoing.Select(Type), Does.Contain("HEL"));
                Assert.That(outgoing.Select(Type), Does.Contain("OPN"));
                Assert.That(outgoing.Select(Type), Does.Contain("MSG"));
                Assert.That(outgoing.Select(Type), Does.Contain("CLO"));
                Assert.That(incoming.Select(Type), Does.Contain("ACK"));
                Assert.That(incoming.Select(Type), Does.Contain("OPN"));
                Assert.That(incoming.Select(Type), Does.Contain("MSG"));
            });
            foreach (ByteString chunk in outgoing.Concat(incoming))
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(chunk.Span[4..]), Is.EqualTo(chunk.Length),
                    "One native DATA contains one complete UACP chunk, not coalesced or flow-fragmented chunks.");
            }
            if (mode == MessageSecurityMode.Sign)
            {
                uint[] sequence = [.. outgoing.Where(chunk => Type(chunk) == "MSG").Select(chunk => BinaryPrimitives.ReadUInt32LittleEndian(chunk.Span[16..]))];
                Assert.That(sequence, Has.Length.GreaterThan(5));
                for (int index = 1; index < sequence.Length; index++)
                {
                    Assert.That(sequence[index], Is.EqualTo(sequence[index - 1] + 1), "No UASC sequence gaps.");
                }
            }
        }

        /// <summary>
        /// Cancelling after an actual HEL, before any UASC acceptor attaches, retires only the partial lane.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task CancelledOpenReleasesOnlyItsLane(DataChannelCarrierKind carrier)
        {
            m_harness = await NativeLaneHarness.CreateAsync(
                m_backend!, m_http3!, carrier, routeLanes: false).ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            (DataChannel other, DataChannel peer) = await harness.OpenIndependentAsync().ConfigureAwait(false);
            await using DataChannel otherLifetime = other;
            await using DataChannel peerLifetime = peer;
            using var cancellation = new CancellationTokenSource();
            Task<DataChannelClientServerConnection> opening = harness.OpenLaneAsync(cancellationToken: cancellation.Token);
            await WaitUntilAsync(
                () => UacpChunks(harness.Environment.ClientBackend.Writes).Any(chunk => Type(chunk) == "HEL"),
                harness.Token).ConfigureAwait(false);
            Assert.That(opening.IsCompleted, Is.False, "The ordinary UASC handshake must await its ACK.");
            await cancellation.CancelAsync().ConfigureAwait(false);
            Exception? failure = Assert.CatchAsync(async () => await opening.ConfigureAwait(false));
            Assert.That(IsCancellation(failure), Is.True, $"Unexpected cancellation result: {failure}");
            await WaitUntilAsync(
                () => harness.Environment.ClientBudget.Usage.Flows == 1 &&
                    harness.Environment.ServerBudget.Usage.Flows == 1 &&
                    harness.Buffers.Outstanding == 0,
                harness.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.ClientContext.IsReady, Is.True);
                Assert.That(harness.ServerContext.IsReady, Is.True);
                Assert.That(UacpChunks(harness.Environment.ServerBackend.Writes), Is.Empty);
                Assert.That(harness.NativeClient.Completion.IsCompleted, Is.False);
            });
            await AssertIndependentPayloadAsync(other, peer, [4, 0, 42], harness.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// A correct TLS peer cannot forward a control application claiming a different intended Server identity.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task LaneRejectsDifferentExpectedInnerApplication(DataChannelCarrierKind carrier)
        {
            m_harness = await NativeLaneHarness.CreateAsync(m_backend!, m_http3!, carrier).ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.OpenLaneAsync(expectedApplicationUri: "urn:opcfoundation:forwarded-application")
                    .ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
                Assert.That(harness.ClientContext.Channels, Is.Empty);
                Assert.That(harness.ServerContext.Channels, Is.Empty);
                Assert.That(UacpChunks(harness.Environment.ClientBackend.Writes), Is.Empty);
                Assert.That(harness.ClientContext.IsReady, Is.True);
            });
        }

        /// <summary>
        /// A separately trusted application cannot forward its signed UASC handshake through another native peer.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic, false)]
        [TestCase(DataChannelCarrierKind.Quic, true)]
        [TestCase(DataChannelCarrierKind.WebTransport, false)]
        [TestCase(DataChannelCarrierKind.WebTransport, true)]
        public async Task ListenerRejectsAForwardedTrustedInnerApplication(
            DataChannelCarrierKind carrier, bool matchingApplicationUri)
        {
            m_harness = await NativeLaneHarness.CreateAsync(m_backend!, m_http3!, carrier).ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            string applicationUri = matchingApplicationUri
                ? WebTransportTestEnvironment.ClientUri : "urn:opcfoundation:native-lane-tests:forwarded";
            using Certificate forwarded = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                applicationUri, "Forwarded test application",
                "CN=Forwarded test application", ["localhost", "127.0.0.1"])
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-1))
                .SetNotAfter(DateTime.UtcNow.AddMinutes(30))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using (ICertificateStore trusted = harness.Environment.ServerManager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await trusted.AddAsync(forwarded, ct: harness.Token).ConfigureAwait(false);
            }
            CertificateValidationResult validation = await harness.Environment.ServerManager
                .ValidateAsync(forwarded, ct: harness.Token).ConfigureAwait(false);
            Assert.That(validation.IsValid, Is.True, "The negative identity is trusted, but is not the native peer.");
            Assert.That(DataChannelCertificateIdentity.GetKeyThumbprint(forwarded),
                Is.Not.EqualTo(harness.ServerContext.Identity.Peer.PeerKeyThumbprint));
            (DataChannel independent, DataChannel peerFlow) = await harness.OpenIndependentAsync().ConfigureAwait(false);
            await using DataChannel independentLifetime = independent;
            await using DataChannel peerLifetime = peerFlow;
            DataChannelParametersDataType parameters = WebTransportTestEnvironment.Parameters();
            parameters.ResourceUri = DataChannelProfiles.ClientServerResource;
            parameters.MaxMessageSize = 32768;
            parameters.MaxReceiveBytes = 262144;
            DataChannel flow = await harness.ClientContext.OpenAsync(parameters, cancellationToken: harness.Token)
                .ConfigureAwait(false);
            await using var factory = new UnboundLaneFactory(flow);
            using var uasc = new UaSCUaBinaryTransportChannel(
                factory, harness.Environment.Telemetry, null, harness.Buffers);
            using CertificateEntry server = harness.Environment.ServerManager
                .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256)!;
            ServiceMessageContext context = harness.ClientConfiguration.CreateMessageContext();
            var settings = new TransportChannelSettings
            {
                Description = harness.Description(server.Certificate),
                Configuration = EndpointConfiguration.Create(harness.ClientConfiguration),
                ClientCertificate = forwarded.AddRef(),
                ServerCertificate = server.Certificate.AddRef(),
                CertificateValidator = harness.Environment.ClientManager,
                Factory = context.Factory,
                NamespaceUris = context.NamespaceUris
            };
            ServiceResultException? failure = Assert.CatchAsync<ServiceResultException>(async () =>
                await uasc.OpenAsync(harness.ServiceEndpoint, settings, harness.Token).ConfigureAwait(false));
            Assert.That(failure!.StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityChecksFailed).Or.EqualTo(StatusCodes.BadCertificateUriInvalid)
                    .Or.EqualTo(StatusCodes.BadConnectionClosed));
            Assert.That(harness.CertificateAudits.Any(audit =>
                audit.Key == DataChannelCertificateIdentity.GetKeyThumbprint(forwarded) &&
                audit.Status == (matchingApplicationUri
                    ? StatusCodes.BadSecurityChecksFailed : StatusCodes.BadCertificateUriInvalid)), Is.True,
                "The real UASC validator rejected the trusted URI/key mismatch, even if RESET overtook ERR.");
            Assert.That(UacpChunks(harness.Environment.ClientBackend.Writes)
                .Any(chunk => Type(chunk) == "OPN" && chunk.Span.IndexOf(forwarded.RawData) >= 0), Is.True,
                "The rejected inner certificate actually traveled in the native flow's OPN.");
            await factory.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.ClientContext.IsReady, Is.True);
                Assert.That(harness.ServerContext.IsReady, Is.True);
                Assert.That(harness.ServerContext.Identity.Peer.PeerApplicationUri,
                    Is.EqualTo(WebTransportTestEnvironment.ClientUri));
            });
            await AssertIndependentPayloadAsync(independent, peerFlow, [17, 0, 89], harness.Token).ConfigureAwait(false);
            Assert.That(await harness.ClientContext.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await harness.ServerContext.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
        }

        /// <summary>
        /// Outer confidentiality never silently selects or relaxes the inner signed-identity policy.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task OuterTlsDoesNotAuthorizeAnInnerNonePolicy(DataChannelCarrierKind carrier)
        {
            m_harness = await NativeLaneHarness.CreateAsync(m_backend!, m_http3!, carrier).ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = harness.ServiceEndpoint.AbsoluteUri,
                    TransportProfileUri = Profiles.UaTcpTransport,
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                }
            };
            ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Client.ConnectAsync(harness.ClientContext, harness.ServiceEndpoint, settings, harness.Token)
                    .ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
                Assert.That(harness.ClientContext.Channels, Is.Empty);
                Assert.That(harness.ClientContext.Identity.Peer.Protection,
                    Is.EqualTo(DataChannelProtectionMode.Confidential));
            });
        }

        /// <summary>
        /// An arbitrary opaque flow is rejected before any UASC parsing, without disturbing another authorized flow.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task ListenerNeverDispatchesAnOrdinaryOpaqueResource(DataChannelCarrierKind carrier)
        {
            m_harness = await NativeLaneHarness.CreateAsync(m_backend!, m_http3!, carrier).ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            (DataChannel ordinary, DataChannel ordinaryPeer) =
                await harness.OpenIndependentAsync(laneSized: true).ConfigureAwait(false);
            await using DataChannel ordinaryLifetime = ordinary;
            (DataChannel survivor, DataChannel survivorPeer) = await harness.OpenIndependentAsync().ConfigureAwait(false);
            await using DataChannel survivorLifetime = survivor;
            await using DataChannel survivorPeerLifetime = survivorPeer;
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await harness.Listener.AcceptAsync(ordinaryPeer, harness.Token).ConfigureAwait(false));
            Assert.That(UacpChunks(harness.Environment.ServerBackend.Writes), Is.Empty);
            Assert.That(harness.ServerContext.IsReady, Is.True);
            await AssertIndependentPayloadAsync(survivor, survivorPeer, [7, 0, 21], harness.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// An authorized but UACP-incompatible size rejects only that flow, not the reserved-resource consumer.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task RejectedTooSmallLaneDoesNotStopReservedResourceRouter(DataChannelCarrierKind carrier)
        {
            m_harness = await NativeLaneHarness.CreateAsync(m_backend!, m_http3!, carrier).ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            DataChannelParametersDataType parameters = WebTransportTestEnvironment.Parameters();
            parameters.ResourceUri = DataChannelProfiles.ClientServerResource;
            await using DataChannel small = await harness.ClientContext.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await WaitUntilAsync(() => harness.ClientContext.Channels.Count == 0 &&
                harness.ServerContext.Channels.Count == 0, harness.Token).ConfigureAwait(false);
            Assert.That(harness.ClientContext.IsReady, Is.True);
            await using DataChannelClientServerConnection valid = await harness.OpenLaneAsync().ConfigureAwait(false);
            IServiceResponse response = await valid.Services.SendRequestAsync(new FindServersRequest
            {
                RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, RequestHandle = 444 },
                EndpointUrl = harness.ServiceEndpoint.AbsoluteUri
            }, harness.Token).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(444));
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Client and listener lane reservations remain finite, while closure releases capacity for the next lane.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task LaneLimitsRejectExcessAndReleaseCapacityAfterClose(DataChannelCarrierKind carrier)
        {
            var options = new DataChannelClientServerOptions { MaxLanes = 1 };
            m_harness = await NativeLaneHarness.CreateAsync(
                m_backend!, m_http3!, carrier, options: options).ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            await using DataChannelClientServerConnection first = await harness.OpenLaneAsync().ConfigureAwait(false);
            ServiceResultException? local = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.OpenLaneAsync().ConfigureAwait(false));
            Assert.That(local!.StatusCode, Is.EqualTo(StatusCodes.BadTcpNotEnoughResources));
            var secondClient = new DataChannelClientServerClient(harness.Environment.Telemetry, options, harness.Buffers);
            Exception? remote = Assert.CatchAsync(async () =>
                await harness.OpenLaneAsync(client: secondClient).ConfigureAwait(false));
            Assert.That(remote, Is.Not.Null, "A second client instance cannot bypass listener admission.");
            IServiceResponse response = await first.Services.SendRequestAsync(new FindServersRequest
            {
                RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, RequestHandle = 31 },
                EndpointUrl = harness.ServiceEndpoint.AbsoluteUri
            }, harness.Token).ConfigureAwait(false);
            Assert.That(response, Is.TypeOf<FindServersResponse>());
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(31));
            await first.DisposeAsync().ConfigureAwait(false);
            await WaitUntilAsync(() => harness.Environment.ServerBudget.Usage.Flows == 0, harness.Token)
                .ConfigureAwait(false);
            await using DataChannelClientServerConnection replacement = await harness.OpenLaneAsync().ConfigureAwait(false);
            Assert.That(replacement.SecureChannelId, Is.Not.Zero);
            Assert.That(harness.ClientContext.IsReady, Is.True);
        }

        /// <summary>
        /// Remote per-lane closure completes the client owner without requiring disposal of the shared context.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task RemoteLaneCloseCompletesClientAndPreservesAnotherFlow(DataChannelCarrierKind carrier)
        {
            m_harness = await NativeLaneHarness.CreateAsync(
                m_backend!, m_http3!, carrier,
                options: new DataChannelClientServerOptions { MaxLanes = 1 }, routeLanes: false).ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            (DataChannel independent, DataChannel peer) = await harness.OpenIndependentAsync().ConfigureAwait(false);
            await using DataChannel independentLifetime = independent;
            await using DataChannel peerLifetime = peer;
            Task<DataChannelClientServerLane?> accept = harness.Listener.AcceptAsync(harness.Provider, harness.Token)
                .AsTask();
            Task<DataChannelClientServerConnection> opening = harness.OpenLaneAsync();
            await Task.WhenAll(opening, accept).ConfigureAwait(false);
            await using DataChannelClientServerConnection lane = await opening.ConfigureAwait(false);
            await using DataChannelClientServerLane serverLane =
                await accept.ConfigureAwait(false) ?? throw new InvalidOperationException("The native context closed.");
            await serverLane.DisposeAsync().ConfigureAwait(false);
            await lane.Completion.WaitAsync(TimeSpan.FromSeconds(5), harness.Token).ConfigureAwait(false);
            Assert.That(harness.NativeClient.Completion.IsCompleted, Is.False);
            await AssertIndependentPayloadAsync(independent, peer, [12, 0, 34], harness.Token).ConfigureAwait(false);
            Assert.That(harness.ClientContext.Channels.ToArray(), Has.All.Matches<DataChannel>(channel =>
                channel.Parameters.ResourceUri != DataChannelProfiles.ClientServerResource));
            Task<DataChannelClientServerLane?> acceptedReplacement = harness.Listener
                .AcceptAsync(harness.Provider, harness.Token).AsTask();
            Task<DataChannelClientServerConnection> openedReplacement = harness.OpenLaneAsync();
            await Task.WhenAll(openedReplacement, acceptedReplacement).ConfigureAwait(false);
            await using DataChannelClientServerConnection replacement = await openedReplacement.ConfigureAwait(false);
            await using DataChannelClientServerLane serverReplacement =
                await acceptedReplacement.ConfigureAwait(false) ?? throw new InvalidOperationException("Context closed.");
            Assert.That(replacement.SecureChannelId, Is.Not.Zero, "Remote closure releases both lane reservations.");
        }

        /// <summary>
        /// Invalid ordinary channel operations do not silently reconnect or close the shared native context.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task ClientChannelValidatesOperationsAndDisposesOnlyItsLane(DataChannelCarrierKind carrier)
        {
            m_harness = await NativeLaneHarness.CreateAsync(m_backend!, m_http3!, carrier).ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            await using DataChannelClientServerConnection lane = await harness.OpenLaneAsync().ConfigureAwait(false);
            Assert.That(lane.Services.SupportedFeatures, Is.EqualTo(TransportChannelFeatures.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => lane.Services.OperationTimeout = 0);
            lane.Services.OperationTimeout = 2000;
            Assert.That(lane.Services.OperationTimeout, Is.EqualTo(2000));
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await lane.SendRequestAsync(null!, harness.Token).ConfigureAwait(false));
            Assert.ThrowsAsync<NotSupportedException>(async () =>
                await lane.Services.ReconnectAsync(ct: harness.Token).ConfigureAwait(false));
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync().ConfigureAwait(false);
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await lane.Services.ReconnectAsync(ct: cancelled.Token).ConfigureAwait(false));
            var request = new FindServersRequest
            {
                RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, RequestHandle = 678 },
                EndpointUrl = harness.ServiceEndpoint.AbsoluteUri
            };
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await lane.SendRequestAsync(request, cancelled.Token).ConfigureAwait(false));
            IServiceResponse response = await lane.SendRequestAsync(request, harness.Token).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(678));
            lane.Services.Dispose();
            await lane.Completion.WaitAsync(harness.Token).ConfigureAwait(false);
            ServiceResultException? closed = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lane.SendRequestAsync(request, harness.Token).ConfigureAwait(false));
            Assert.That(closed!.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
            Assert.That(harness.ClientContext.IsReady, Is.True,
                $"Client context termination: {string.Join(",", harness.ClientClosures)}; " +
                $"transport failure: {harness.ClientTransportFailure}");
            Assert.That(harness.NativeClient.Completion.IsCompleted, Is.False);
        }

        /// <summary>
        /// A paused positive policy decision must retain a secured response, rather than discard its sequence number.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task PositiveReauthorizationWaitsWithoutDroppingASecuredResponse(DataChannelCarrierKind carrier)
        {
            m_harness = await NativeLaneHarness.CreateAsync(
                m_backend!, m_http3!, carrier, MessageSecurityMode.Sign).ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            (DataChannel independent, DataChannel peer) = await harness.OpenIndependentAsync().ConfigureAwait(false);
            await using DataChannel independentLifetime = independent;
            await using DataChannel peerLifetime = peer;
            await using DataChannelClientServerConnection lane = await harness.OpenLaneAsync().ConfigureAwait(false);
            harness.Responses.Arm();
            Task recheck = Task.CompletedTask;
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(harness.Token);
            Task<IServiceResponse> response = lane.Services.SendRequestAsync(new FindServersRequest
            {
                RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, RequestHandle = 1234 },
                EndpointUrl = harness.ServiceEndpoint.AbsoluteUri
            }, requestCancellation.Token).AsTask();
            try
            {
                await harness.Responses.Entered.WaitAsync(harness.Token).ConfigureAwait(false);
                harness.Rechecks.Arm();
                recheck = harness.ServerContext.RecheckAuthorizationAsync(harness.Token).AsTask();
                await harness.Rechecks.Entered.WaitAsync(harness.Token).ConfigureAwait(false);
                harness.Sends.Arm();
                harness.Responses.Release();
                bool completedEarly = await harness.Sends.Attempted.WaitAsync(harness.Token).ConfigureAwait(false);
                if (completedEarly)
                {
                    StatusCode early = await harness.Sends.Outcome.WaitAsync(harness.Token).ConfigureAwait(false);
                    await TestContext.Out.WriteLineAsync($"Observed transport refusal during positive recheck: {early}")
                        .ConfigureAwait(false);
                }
                Assert.That(completedEarly, Is.False, "A secured response must await temporary admission.");
                harness.Rechecks.Release();
                await recheck.ConfigureAwait(false);
                IServiceResponse first = await response.ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(first, Is.TypeOf<FindServersResponse>());
                    Assert.That(first.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(first.ResponseHeader.RequestHandle, Is.EqualTo(1234));
                });
                IServiceResponse second = await lane.Services.SendRequestAsync(new FindServersRequest
                {
                    RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, RequestHandle = 1235 },
                    EndpointUrl = harness.ServiceEndpoint.AbsoluteUri
                }, harness.Token).ConfigureAwait(false);
                Assert.That(second.ResponseHeader.RequestHandle, Is.EqualTo(1235));
                uint[] sequence = [.. UacpChunks(harness.Environment.ServerBackend.Writes)
                    .Where(chunk => Type(chunk) == "MSG")
                    .Select(chunk => BinaryPrimitives.ReadUInt32LittleEndian(chunk.Span[16..]))];
                Assert.That(sequence, Has.Length.EqualTo(2));
                Assert.That(sequence[1], Is.EqualTo(sequence[0] + 1));
                await AssertIndependentPayloadAsync(independent, peer, [3, 0, 9], harness.Token).ConfigureAwait(false);
            }
            finally
            {
                harness.Responses.Release();
                harness.Rechecks.Release();
                await recheck.ConfigureAwait(false);
                await requestCancellation.CancelAsync().ConfigureAwait(false);
                try
                {
                    await response.ConfigureAwait(false);
                }
                catch (Exception exception) when (IsCancellation(exception))
                {
                    // The original assertion is preserved after the real pending request has been observed.
                }
            }
        }

        /// <summary>
        /// A real held Publish consumes one bounded operation; cancellation does not retract numbered UASC chunks.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task CancelledServiceReleasesOperationWithoutSequenceGaps(DataChannelCarrierKind carrier)
        {
            var options = new DataChannelClientServerOptions { MaxPendingRequests = 1 };
            m_harness = await NativeLaneHarness.CreateAsync(
                m_backend!, m_http3!, carrier, MessageSecurityMode.Sign, options).ConfigureAwait(false);
            NativeLaneHarness harness = m_harness;
            await using DataChannelClientServerConnection lane = await harness.OpenLaneAsync().ConfigureAwait(false);
            using ISession session = await harness.OpenSessionAsync(lane).ConfigureAwait(false);
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 1000, 100, 10, 1, true, 0, harness.Token).ConfigureAwait(false);
            int before = UacpChunks(harness.Environment.ClientBackend.Writes).Count(chunk => Type(chunk) == "MSG");
            using var cancellation = new CancellationTokenSource();
            Task<PublishResponse> held = session.PublishAsync(null, [], cancellation.Token).AsTask();
            await WaitUntilAsync(
                () => UacpChunks(harness.Environment.ClientBackend.Writes).Count(chunk => Type(chunk) == "MSG") > before,
                harness.Token).ConfigureAwait(false);
            Assert.That(held.IsCompleted, Is.False);
            ServiceResultException? bounded = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lane.Services.SendRequestAsync(new ReadRequest
                {
                    RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, RequestHandle = 999 },
                    NodesToRead =
                    [
                        new ReadValueId
                        {
                            NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                            AttributeId = Attributes.Value
                        }
                    ]
                }, harness.Token).ConfigureAwait(false));
            Assert.That(bounded!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            await cancellation.CancelAsync().ConfigureAwait(false);
            Exception? cancelled = Assert.CatchAsync(async () => await held.ConfigureAwait(false));
            Assert.That(IsCancellation(cancelled), Is.True, $"Unexpected Service cancellation result: {cancelled}");
            ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
            [
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_CurrentTime, AttributeId = Attributes.Value }
            ], harness.Token).ConfigureAwait(false);
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out DateTimeUtc _), Is.True);
            DeleteSubscriptionsResponse deleted = await session.DeleteSubscriptionsAsync(
                null, [subscription.SubscriptionId], harness.Token).ConfigureAwait(false);
            Assert.That(deleted.Results[0], Is.EqualTo(StatusCodes.Good));
            uint[] sequence = [.. UacpChunks(harness.Environment.ClientBackend.Writes)
                .Where(chunk => Type(chunk) == "MSG")
                .Select(chunk => BinaryPrimitives.ReadUInt32LittleEndian(chunk.Span[16..]))];
            Assert.That(sequence, Has.Length.GreaterThan(5));
            for (int index = 1; index < sequence.Length; index++)
            {
                Assert.That(sequence[index], Is.EqualTo(sequence[index - 1] + 1));
            }
            Assert.That(await session.CloseAsync(10000, false, harness.Token).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Recognizes the caller's cancellation result, never a generic protocol or timeout failure.
        /// </summary>
        private static bool IsCancellation(Exception? failure)
        {
            return failure is OperationCanceledException ||
                (failure is ServiceResultException service && service.StatusCode == StatusCodes.BadRequestInterrupted);
        }

        /// <summary>
        /// Waits only for an externally observed wire/lifetime fact, with a finite deadline.
        /// </summary>
        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (!condition())
            {
                await Task.Delay(10, deadline.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks one genuine common DATA header and retains only complete ordinary UACP chunks.
        /// </summary>
        private static List<ByteString> UacpChunks(IEnumerable<RecordedStreamWrite> writes)
        {
            var chunks = new List<ByteString>();
            foreach (RecordedStreamWrite write in writes)
            {
                ReadOnlySpan<byte> bytes = write.Payload.Span;
                if (bytes.Length < 21 ||
                    bytes[4] != 0 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes) != bytes.Length - 4)
                {
                    continue;
                }
                ByteString body = write.Payload[13..];
                if (Type(body) is "HEL" or "ACK" or "OPN" or "MSG" or "CLO")
                {
                    chunks.Add(body);
                }
            }
            return chunks;
        }

        /// <summary>
        /// Reads only the standard UACP message discriminator, without decoding a Service envelope.
        /// </summary>
        private static string Type(ByteString chunk)
        {
            return Encoding.ASCII.GetString(chunk.Span[..3]);
        }

        /// <summary>
        /// Requires a normal unrelated native message to remain intact and independently routable.
        /// </summary>
        private static async Task AssertIndependentPayloadAsync(
            DataChannel sender, DataChannel receiver, byte[] payload, CancellationToken token)
        {
            Assert.That(await sender.SendAsync(new ByteString(payload), cancellationToken: token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await receiver.ReadAsync(token).ConfigureAwait(false);
            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(payload));
        }

        private INativeQuicBackend? m_backend;
        private NativeHttp3Library? m_http3;
        private NativeLaneHarness? m_harness;
    }
}
