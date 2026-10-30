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
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

#if NET8_0_OR_GREATER
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using BenchmarkDotNet.Attributes;
#endif

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Exercises profile selection and complete-message flows over genuinely authenticated sockets.
    /// </summary>
    [TestFixture]
    [Category("DataChannelSockets")]
    [NonParallelizable]
#if NET8_0
    [RequiresPreviewFeatures("The real managed QUIC baseline uses System.Net.Quic on .NET 8.")]
#endif
    public sealed class UascDataChannelSocketTests
    {
        /// <summary>
        /// Real encrypted UASC authenticates management; paired fake native streams retain all flow traffic.
        /// This is composition evidence, not browser/native interoperability or retention qualification.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task WebTransportManagementReadinessAndNativeFlowPathsNeedNoExporterAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.InMemoryReliableStream, DataChannelCarrierKind.WebTransport).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task.WaitAsync(harness.Token)
                .ConfigureAwait(false);
            await remote.WaitForReadyAsync(harness.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(connection.Services, Is.Null);
                Assert.That(connection.SecureChannelId, Is.Not.Zero);
                Assert.That(connection.Context.IsReady, Is.True);
                Assert.That(remote.IsReady, Is.True);
                Assert.That(connection.Context.CarrierKind, Is.EqualTo(DataChannelCarrierKind.WebTransport));
                Assert.That(remote.CarrierKind, Is.EqualTo(DataChannelCarrierKind.WebTransport));
                Assert.That(connection.Context.Identity.Peer,
                    Is.SameAs(harness.ClientManagement.ManagementCarrier!.Identity));
                Assert.That(remote.Identity.Peer, Is.SameAs(harness.ServerManagement.ManagementCarrier!.Identity));
                Assert.That(connection.Context.Identity.Peer.PeerKeyThumbprint,
                    Is.EqualTo(DataChannelCertificateIdentity.GetKeyThumbprint(harness.ServerCertificate)));
                Assert.That(connection.Context.Identity.Peer.Protection, Is.EqualTo(DataChannelProtectionMode.Confidential));
                Assert.That(connection.Context.Identity.ContextId, Is.EqualTo(remote.Identity.ContextId));
                Assert.That(connection.Context.Identity.LocalChallenge, Is.EqualTo(remote.Identity.PeerChallenge));
                Assert.That(connection.Context.Identity.PeerChallenge, Is.EqualTo(remote.Identity.LocalChallenge));
                Assert.That(connection.Context.Identity.LocalChallenge.Length, Is.EqualTo(32));
                Assert.That(connection.Context.Identity.PeerChallenge.Length, Is.EqualTo(32));
                Assert.That((object)harness.ClientManagement, Is.Not.InstanceOf<IDataChannelDirectBindingProvider>());
            });

            await using DataChannel outgoing = await connection.Context.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            ByteString payload = UadpPayload(ByteString.FromHexString("00feff7f"));
            Assert.That(await outgoing.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage received = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!)
            {
                Assert.That(received.Payload, Is.EqualTo(payload));
                Assert.That(received.Revision, Is.EqualTo(1u));
            }
            ByteString reply = UadpPayload(ByteString.FromHexString("12345678"));
            Assert.That(await incoming.SendAsync(reply, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage received = (await outgoing.ReadAsync(harness.Token).ConfigureAwait(false))!)
            {
                Assert.That(received.Payload, Is.EqualTo(reply));
            }
            DataChannelParametersDataType updated = outgoing.Parameters;
            updated.MaxMessageSize = 128;
            Assert.That(await outgoing.UpdateAsync(updated, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await outgoing.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage received = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!)
            {
                Assert.That(received.Payload, Is.EqualTo(payload));
                Assert.That(received.Revision, Is.EqualTo(2u));
            }
            Assert.That(await outgoing.EndAsync(harness.Token).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await incoming.ReadAsync(harness.Token).ConfigureAwait(false), Is.Null);
            await harness.ServerManagement.WaitForPeerFinAsync(4, harness.Token).ConfigureAwait(false);
            Task finish = harness.ClientManagement.FinishFlowAsync(4, harness.Token).AsTask();
            Assert.That(finish.IsCompleted, Is.False, "The opposite direction has not sent FIN yet.");
            Assert.Multiple(() =>
            {
                Assert.That(harness.ClientManagement.OpenedStreams, Has.Count.EqualTo(1));
                Assert.That(harness.ClientManagement.OpenedStreams.ContainsKey(4), Is.True);
                Assert.That(harness.ClientManagement.Bindings[4], Is.EqualTo(outgoing.ChannelId));
                Assert.That(harness.ServerManagement.Bindings[4], Is.EqualTo(incoming.ChannelId));
                Assert.That(harness.ClientManagement.Writes.Count(write => write.IsData), Is.EqualTo(2));
                Assert.That(harness.ServerManagement.Writes.Count(write => write.IsData), Is.EqualTo(1));
                Assert.That(harness.ServiceRequests, Is.Zero);
            });
            foreach (ManagementProvider provider in new[] { harness.ClientManagement, harness.ServerManagement })
            {
                CapturedNativeWrite[] management = [.. provider.Writes.Where(write => write.Path == DataChannelPath.Management)];
                Assert.That(management, Is.Not.Empty);
                Assert.That(management.Any(write => FlowControlCodec.Decode(
                    FlowRecordCodec.DecodeExact(write.Payload, new()), new()).Operation == FlowOperation.Hello), Is.True);
                foreach (CapturedNativeWrite write in provider.Writes)
                {
                    FlowRecord record = FlowRecordCodec.DecodeExact(write.Payload, new());
                    if (write.Path == DataChannelPath.Management)
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(write.IsData, Is.False);
                            Assert.That(record.Kind, Is.EqualTo(FlowRecordKind.Control));
                            Assert.That(write.StreamId, Is.Zero);
                            Assert.That(write.RequestId, Is.EqualTo(record.MessageId));
                            Assert.That(write.RequestId, Is.Not.Zero);
                        });
                    }
                    else
                    {
                        Assert.That(write.Path, Is.EqualTo(DataChannelPath.Flow));
                        Assert.That(write.StreamId, Is.EqualTo(4UL));
                        Assert.That(write.ChannelId, Is.EqualTo(outgoing.ChannelId));
                    }
                }
            }
            Assert.That(await incoming.EndAsync(harness.Token).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await outgoing.ReadAsync(harness.Token).ConfigureAwait(false), Is.Null);
            await finish.ConfigureAwait(false);
            await incoming.DisposeAsync().ConfigureAwait(false);
            await outgoing.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.ClientManagement.FinishedStreams.ContainsKey(4), Is.True);
                Assert.That(harness.ServerManagement.FinishedStreams.ContainsKey(4), Is.True);
                Assert.That(harness.ClientManagement.FinsSent, Is.EqualTo(1));
                Assert.That(harness.ServerManagement.FinsSent, Is.EqualTo(1));
                Assert.That(harness.ClientManagement.FinsObserved, Is.EqualTo(1));
                Assert.That(harness.ServerManagement.FinsObserved, Is.EqualTo(1));
                Assert.That(harness.ClientManagement.Bindings, Is.Empty);
                Assert.That(harness.ServerManagement.Bindings, Is.Empty);
            });
        }

        /// <summary>
        /// Native management refuses Services locally, before allocating a secured Service write.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task WebTransportManagementRefusesServiceSendBeforeWritingAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.InMemoryReliableStream, DataChannelCarrierKind.WebTransport).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            await client.DataChannels!.WaitForReadyAsync(harness.Token).ConfigureAwait(false);
            int before = client.WireChunks.Count;
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.SendRequestAsync(Read(71), 10000, harness.Token).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
                Assert.That(client.WireChunks, Has.Count.EqualTo(before));
                Assert.That(harness.ServiceRequests, Is.Zero);
                Assert.That(client.DataChannels.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Management's UASC writer cannot be used as a substitute native DATA/flow/datagram path.
        /// </summary>
        [TestCase(DataChannelPath.Management, true)]
        [TestCase(DataChannelPath.Flow, false)]
        [TestCase(DataChannelPath.Datagram, false)]
        [CancelAfter(60000)]
        public async Task WebTransportManagementWriterRefusesNonManagementTrafficAsync(DataChannelPath path, bool isData)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.InMemoryReliableStream, DataChannelCarrierKind.WebTransport).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            await client.DataChannels!.WaitForReadyAsync(harness.Token).ConfigureAwait(false);
            int before = client.WireChunks.Count;
            bool committed = false;
            var write = new DataChannelWrite(path, 1, 4, 11,
                ByteString.FromHexString("0e00000001010000000b0000000a01000000"), isData, 128,
                () =>
                {
                    committed = true;
                    return DataChannelResult.Good;
                });
            DataChannelResult result = await client.SendDataChannelWriteAsync(write, harness.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(DataChannelResult.Unsupported));
                Assert.That(committed, Is.False);
                Assert.That(write.IsCommitted, Is.False);
                Assert.That(client.WireChunks, Has.Count.EqualTo(before));
                Assert.That(harness.ServiceRequests, Is.Zero);
                Assert.That(client.DataChannels.IsReady, Is.True);
            });
        }

        /// <summary>
        /// The public verified ingress accepts MSG too; a FLO/FLC-only whitelist would prevent HELLO readiness.
        /// This direct API test uses fake verified identity, not native authentication evidence.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task WebTransportManagementPublicIngressAcceptsMsgHelloAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var identity = new DataChannelPeerIdentity(
                "urn:ingress:local", "urn:ingress:peer",
                ByteString.FromHexString(new string('1', 64)), ByteString.FromHexString(new string('2', 64)),
                DataChannelProtectionMode.Confidential, false);
            var carrier = new Mock<IDataChannelCarrier>(MockBehavior.Strict);
            carrier.SetupGet(value => value.Kind).Returns(DataChannelCarrierKind.WebTransport);
            carrier.SetupGet(value => value.Identity).Returns(identity);
            carrier.SetupGet(value => value.MaxRecordSize).Returns(8192);
            carrier.SetupGet(value => value.MaxDatagramSize).Returns(0);
            carrier.SetupGet(value => value.ManagementStreamId).Returns(0UL);
            var replies = new ConcurrentQueue<CapturedNativeWrite>();
            carrier.Setup(value => value.SendAsync(It.IsAny<DataChannelWrite>(), It.IsAny<CancellationToken>()))
                .Returns((DataChannelWrite write, CancellationToken _) =>
                {
                    replies.Enqueue(new CapturedNativeWrite(
                        write.Path, write.ChannelId, write.StreamId, write.RequestId, write.Payload.Copy(), write.IsData));
                    return new ValueTask<DataChannelResult>(write.TryCommit() ? DataChannelResult.Good : write.Result);
                });
            carrier.Setup(value => value.CloseAsync(It.IsAny<DataChannelResult>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var factory = new ContextFactory(telemetry, identity.PeerApplicationUri, identity.PeerKeyThumbprint, false);
            await using DataChannelContext context = factory.Create(carrier.Object);
            var contextId = new Guid("11112233-4455-6677-8899-aabbccddeeff");
            var challenge = ByteString.FromHexString(new string('3', 64));
            FlowRecord hello = FlowControlCodec.Encode(
                new FlowHello(1, FlowCarrier.WebTransport, identity.PeerApplicationUri, contextId, 7,
                    challenge, FlowWireProtection.Confidential, 4096, 8192, 4), 0, 0x80010001, new());
            DataChannelResult result = await context.ReceiveUascAsync(
                DataChannelUascMessageKind.Msg, (byte)'F', 0x80010001, FlowRecordCodec.Encode(in hello, new()),
                TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(DataChannelResult.Good));
            await context.WaitForReadyAsync(TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(context.IsReady, Is.True);
                Assert.That(context.Identity.Peer, Is.SameAs(identity));
                Assert.That(context.Identity.ContextId, Is.EqualTo(contextId));
                Assert.That(context.Identity.Generation, Is.EqualTo(7u));
                Assert.That(context.Identity.PeerChallenge, Is.EqualTo(challenge));
                Assert.That(factory.Authorizations, Is.Zero, "HELLO does not authorize a resource.");
                Assert.That(replies, Has.Count.EqualTo(1));
            });
            CapturedNativeWrite response = replies.Single();
            UascFlowMessage decoded = UascFlowMessageCodec.DecodeManagement(
                DataChannelUascMessageKind.Msg, (byte)'F', response.RequestId, response.Payload, new());
            var confirmation = (FlowHello)decoded.Control!;
            Assert.Multiple(() =>
            {
                Assert.That(response.Path, Is.EqualTo(DataChannelPath.Management));
                Assert.That(response.IsData, Is.False);
                Assert.That(confirmation.Application, Is.EqualTo(identity.LocalApplicationUri));
                Assert.That(confirmation.Context, Is.EqualTo(contextId));
                Assert.That(confirmation.Generation, Is.EqualTo(7u));
                Assert.That(confirmation.Challenge, Is.EqualTo(context.Identity.LocalChallenge));
            });
        }

        /// <summary>
        /// Malformed MSG bodies are authenticated, then refused by the flow-only dispatcher, never Services.
        /// </summary>
        [TestCase("service", DataChannelResult.TooLarge)]
        [TestCase("data", DataChannelResult.InvalidArgument)]
        [TestCase("request-id", DataChannelResult.InvalidArgument)]
        [TestCase("trailing-record", DataChannelResult.InvalidArgument)]
        [TestCase("abort", DataChannelResult.InvalidArgument)]
        [TestCase("continuation", DataChannelResult.InvalidArgument)]
        [TestCase("flc", DataChannelResult.Closed)]
        [CancelAfter(60000)]
        public async Task WebTransportManagementRefusesAuthenticatedInvalidInputAsync(
            string fault, DataChannelResult expected)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.InMemoryReliableStream, DataChannelCarrierKind.WebTransport).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task.WaitAsync(harness.Token)
                .ConfigureAwait(false);
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            var closed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            remote.Closed += result => closed.TrySetResult(result);
            FlowRecord end = FlowControlCodec.Encode(
                new FlowSignal(FlowOperation.End, outgoing.ChannelId), 1, 0x80010001, new());
            byte[] body = UascFlowMessageCodec.EncodeManagement(
                outgoing.ChannelId, in end, new(), out uint requestId).ToArray();
            uint messageType = TcpMessageType.Message;
            switch (fault)
            {
                case "service":
                    using (var encoder = new BinaryEncoder(ServiceMessageContext.Create(harness.Telemetry)))
                    {
                        encoder.EncodeMessage(Read(72));
                        body = encoder.CloseAndReturnBuffer()!;
                    }
                    break;
                case "data":
                    var data = new FlowRecord(FlowRecordKind.Data, 1, requestId, UadpPayload(ByteString.Empty));
                    body = FlowRecordCodec.Encode(in data, new()).ToArray();
                    break;
                case "request-id":
                    requestId ^= 0x80000000;
                    break;
                case "trailing-record":
                    body = [.. body, .. body];
                    break;
                case "continuation":
                    Array.Resize(ref body, client.GetMaxDataChannelRecordSize(DataChannelCarrierKind.WebTransport) + 16);
                    break;
                case "flc":
                    messageType = TcpMessageType.FlowControl;
                    break;
            }
            try
            {
                await client.SendAuthenticatedBodyAsync(
                    messageType, requestId, body, fault == "abort", harness.Token).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // The peer can close as soon as the first secured continuation is received.
            }
            DataChannelResult result = await closed.Task.WaitAsync(harness.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(expected));
                Assert.That(remote.IsReady, Is.False);
                Assert.That(remote.Channels.IsEmpty, Is.True);
                Assert.That(incoming.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(harness.ServiceRequests, Is.Zero);
                Assert.That(client.WireChunks.Any(chunk =>
                    (BinaryPrimitives.ReadUInt32LittleEndian(chunk) & TcpMessageType.MessageTypeMask) == messageType),
                    Is.True, "The invalid body must actually enter the authenticated writer.");
            });
        }

        /// <summary>
        /// The ordinary Part32 profile carries canonical channel MSG and Services on the identical stream adapter.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task WebTransportManagementDoesNotReplacePart32BaselineAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.InMemoryReliableStream, DataChannelCarrierKind.WebTransportUasc).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task.WaitAsync(harness.Token)
                .ConfigureAwait(false);
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            ByteString payload = UadpPayload(ByteString.FromHexString("aabbccdd"));
            Assert.That(await outgoing.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
            IServiceResponse response = await client.SendRequestAsync(Read(73), 10000, harness.Token).ConfigureAwait(false);
            uint[] types = [.. client.WireChunks.Select(chunk =>
                BinaryPrimitives.ReadUInt32LittleEndian(chunk) & TcpMessageType.MessageTypeMask)];
            Assert.Multiple(() =>
            {
                Assert.That(received.Payload, Is.EqualTo(payload));
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(73u));
                Assert.That(client.DataChannels.CarrierKind, Is.EqualTo(DataChannelCarrierKind.WebTransportUasc));
                Assert.That(types, Does.Not.Contain(TcpMessageType.FlowControl));
                Assert.That(types, Does.Not.Contain(TcpMessageType.FlowData));
                Assert.That(types, Does.Contain(TcpMessageType.Message));
                Assert.That(harness.ClientManagement.Attachments, Is.Zero);
                Assert.That(harness.ServerManagement.Attachments, Is.Zero);
                Assert.That(harness.ServiceRequests, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// After a genuine OPN, native attachment cannot omit its provider or replace verified identity evidence.
        /// </summary>
        [TestCase(false, false, DataChannelResult.Unsupported)]
        [TestCase(true, true, DataChannelResult.InvalidState)]
        [CancelAfter(60000)]
        public async Task WebTransportManagementAttachmentRejectsMissingProviderOrIdentityReplacementAsync(
            bool supplyProvider, bool replaceIdentity, DataChannelResult expected)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.InMemoryReliableStream, DataChannelCarrierKind.WebTransportUasc,
                enableDataChannels: false).ConfigureAwait(false);
            harness.SupplyManagementProvider = supplyProvider;
            harness.ClientManagement.ReplaceIdentity = replaceIdentity;
            using ProbeClientChannel client = await harness.ConnectProbeAsync(enableDataChannels: false).ConfigureAwait(false);
            DataChannelPeerIdentity identity = harness.CreateClientIdentity();
            DataChannelException failure = Assert.Throws<DataChannelException>(() => client.AttachDataChannels(
                DataChannelCarrierKind.WebTransport, identity, true, harness.ClientFactory))!;
            Assert.Multiple(() =>
            {
                Assert.That(failure.Result, Is.EqualTo(expected));
                Assert.That(client.DataChannels, Is.Null);
                Assert.That(harness.ClientManagement.Attachments, Is.EqualTo(supplyProvider ? 1 : 0));
                Assert.That(harness.ServiceRequests, Is.Zero);
            });
        }

        /// <summary>
        /// Repeated attachment is refused before the provider can replace the live native context.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task WebTransportManagementAttachmentIsSingleUseAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.InMemoryReliableStream, DataChannelCarrierKind.WebTransport).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext original = client.DataChannels!;
            await original.WaitForReadyAsync(harness.Token).ConfigureAwait(false);
            DataChannelException failure = Assert.Throws<DataChannelException>(() => client.AttachDataChannels(
                DataChannelCarrierKind.WebTransport, original.Identity.Peer, true, harness.ClientFactory))!;
            Assert.Multiple(() =>
            {
                Assert.That(failure.Result, Is.EqualTo(DataChannelResult.InvalidState));
                Assert.That(client.DataChannels, Is.SameAs(original));
                Assert.That(original.IsReady, Is.True);
                Assert.That(harness.ClientManagement.Attachments, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Native endpoint selection requires its factory, exact opc.wt address and confidential UASC policy.
        /// </summary>
        [Test]
        public void WebTransportManagementEndpointSelectionDoesNotFallBackToServices()
        {
            var url = new Uri("opc.wt://localhost:4840/uasc?resource=pubsub");
            var endpoint = new EndpointDescription
            {
                EndpointUrl = url.AbsoluteUri,
                TransportProfileUri = DataChannelProfiles.WebTransport,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            IDataChannelContextFactory factory = Mock.Of<IDataChannelContextFactory>();
            Assert.That(UascDataChannelEndpoint.GetKind(endpoint.TransportProfileUri),
                Is.EqualTo(DataChannelCarrierKind.WebTransport));
            Assert.That(UascDataChannelEndpoint.Validate(url, endpoint, factory, false),
                Is.EqualTo(DataChannelCarrierKind.WebTransport));
            ServiceResultException missingFactory = Assert.Throws<ServiceResultException>(() =>
                UascDataChannelEndpoint.Validate(url, endpoint, null, false))!;
            Assert.That(missingFactory.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            ServiceResultException wrongScheme = Assert.Throws<ServiceResultException>(() =>
                UascDataChannelEndpoint.Validate(new Uri("https://localhost:4840/uasc?resource=pubsub"),
                    endpoint, factory, false))!;
            Assert.That(wrongScheme.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            foreach (MessageSecurityMode mode in new[] { MessageSecurityMode.None, MessageSecurityMode.Sign })
            {
                endpoint.SecurityMode = mode;
                ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                    UascDataChannelEndpoint.Validate(url, endpoint, factory, true))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
            }
            endpoint.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            endpoint.SecurityPolicyUri = SecurityPolicies.None;
            ServiceResultException policyFailure = Assert.Throws<ServiceResultException>(() =>
                UascDataChannelEndpoint.Validate(url, endpoint, factory, false))!;
            Assert.That(policyFailure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.DedicatedUasc)]
        /// <summary>
        /// Independent visible-header fixtures retain their typed identity and opaque bodies over secured MSG.
        /// </summary>
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task SharedUascPreservesIndependentTypedAndSecuredUadpHeadersAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(transport, kind).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task.WaitAsync(harness.Token)
                .ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.ResourceUri = UadpFirstResource;
            parameters.Protocol = DataChannelProfiles.UadpPayload;
            await using DataChannel sender = await connection.Context.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel receiver = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            (Variant Publisher, string Hex)[] fixtures =
            [
                (new Variant((byte)42), "91002adead"),
                (new Variant((ushort)42), "91012a00dead"),
                (new Variant(42u), "91022a000000dead"),
                (new Variant(42ul), "91032a00000000000000dead"),
                (new Variant("pub"), "910403000000707562dead"),
                (new Variant(42u), "91122a000000deadbeef"),
                (new Variant("publisher-73001"),
                    "f1a4010f0000007075626c69736865722d373330303101341245230000000000000000dead"),
                (new Variant(42u), "f182002a000000010c000222002300dead")
            ];
            for (int index = 0; index < fixtures.Length; index++)
            {
                (Variant publisher, string hex) = fixtures[index];
                using IDisposable route = remote.RegisterUascPublisherRoute(UadpFirstResource, publisher);
                var payload = ByteString.FromHexString(hex);
                Assert.That(await sender.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good), hex);
                using DataChannelMessage received = (await receiver.ReadAsync(harness.Token).ConfigureAwait(false))!;
                Assert.Multiple(() =>
                {
                    Assert.That(received.Payload, Is.EqualTo(payload), hex);
                    Assert.That(receiver.GetDiagnostics().MessagesReceived, Is.EqualTo(index + 1), hex);
                    Assert.That(remote.IsReady, Is.True);
                });
            }
        }

        /// <summary>
        /// Writer sets cannot span authorized resources, while discovery bytes remain owned by the selected reader.
        /// </summary>
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task SharedUascRejectsCrossResourceWriterSetsAndPreservesDiscoveryAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(transport, kind).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task.WaitAsync(harness.Token)
                .ConfigureAwait(false);
            DataChannelParametersDataType firstParameters = Parameters();
            firstParameters.ResourceUri = UadpFirstResource;
            firstParameters.Protocol = DataChannelProfiles.UadpPayload;
            DataChannelParametersDataType secondParameters = Parameters();
            secondParameters.ResourceUri = UadpSecondResource;
            secondParameters.Protocol = DataChannelProfiles.UadpPayload;
            await using DataChannel firstSender = await connection.Context.OpenAsync(
                firstParameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel firstReceiver = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            await using DataChannel secondSender = await connection.Context.OpenAsync(
                secondParameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel secondReceiver = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            using IDisposable firstRoute = remote.RegisterUascPublisherRoute(
                UadpFirstResource, new Variant(42u), null, 34);
            using IDisposable secondRoute = remote.RegisterUascPublisherRoute(
                UadpSecondResource, new Variant(42u), null, 35);
            var crossResource = ByteString.FromHexString("f182002a000000010c000222002300dead");
            Assert.That(await firstSender.SendAsync(crossResource, cancellationToken: harness.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await connection.Context.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.Multiple(() =>
            {
                Assert.That(firstReceiver.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(secondReceiver.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(remote.IsReady, Is.True);
            });
            string[] fixtures =
            [
                "f182002a000000010c00012200dead",
                "9182042a0000000100000000",
                "9182042a00000002010000002200",
                "9182042a00000003010000002200",
                "9182082a00000001010000000000000000",
                "9182082a0000000201002200dead",
                "9182082a000000030100010000002200dead"
            ];
            for (int index = 0; index < fixtures.Length; index++)
            {
                var payload = ByteString.FromHexString(fixtures[index]);
                Assert.That(await firstSender.SendAsync(payload, cancellationToken: harness.Token)
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good), fixtures[index]);
                using DataChannelMessage received =
                    (await firstReceiver.ReadAsync(harness.Token).ConfigureAwait(false))!;
                Assert.Multiple(() =>
                {
                    Assert.That(received.Payload, Is.EqualTo(payload), fixtures[index]);
                    Assert.That(firstReceiver.GetDiagnostics().MessagesReceived, Is.EqualTo(index + 1));
                    Assert.That(secondReceiver.GetDiagnostics().MessagesReceived, Is.Zero);
                });
            }
            IServiceResponse service = await connection.Services!.SendRequestAsync(Read(118), harness.Token)
                .ConfigureAwait(false);
            Assert.That(service.ResponseHeader.RequestHandle, Is.EqualTo(118));
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
        [TestCase(CarrierTransport.ReliableStream, DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(CarrierTransport.ReliableStream, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
        [TestCase(CarrierTransport.KestrelTcp, DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(CarrierTransport.KestrelTcp, DataChannelCarrierKind.SharedUasc)]
        [TestCase(CarrierTransport.Wss, DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(CarrierTransport.Wss, DataChannelCarrierKind.SharedUasc)]
#endif
        [CancelAfter(60000)]
        public async Task ConfiguredFlowCarriesExactDuplexMessagesWithoutASessionAsync(
            CarrierTransport transport,
            DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(transport, kind).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await using DataChannel outgoing = await connection.Context.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);

            ByteString payload = UadpPayload(new ByteString(new byte[] { 0, 1, 0xFE, 0xFF, 0, 0x7F }));
            Assert.That(
                await outgoing.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
            Assert.That(received.Payload, Is.EqualTo(payload));
            Assert.That(
                await incoming.SendAsync(UadpPayload(ByteString.Empty), cancellationToken: harness.Token)
                    .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage empty = (await outgoing.ReadAsync(harness.Token).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(empty.Payload.ToArray(), Is.EqualTo(UadpPayload(ByteString.Empty).ToArray()));
                Assert.That(connection.Context.CarrierKind, Is.EqualTo(kind));
                Assert.That(connection.SecureChannelId, Is.Not.Zero);
                Assert.That(remote.CarrierKind, Is.EqualTo(kind));
                Assert.That(remote.Identity.ContextId, Is.EqualTo(connection.Context.Identity.ContextId));
                Assert.That(connection.Context.Identity.Peer.Protection,
                    Is.EqualTo(DataChannelProtectionMode.Confidential));
                Assert.That(connection.Context.Identity.Peer.PeerApplicationUri, Is.EqualTo(SocketHarness.ServerUri));
                Assert.That(remote.Identity.Peer.PeerApplicationUri, Is.EqualTo(SocketHarness.ClientUri));
                Assert.That(connection.Context.Identity.Peer.PeerKeyThumbprint,
                    Is.EqualTo(DataChannelCertificateIdentity.GetKeyThumbprint(harness.ServerCertificate)));
                Assert.That(harness.ServiceRequests, Is.Zero, "No CreateSession or other Service establishes a flow.");
                Assert.That(harness.ClientFactory.Authorizations, Is.GreaterThanOrEqualTo(1));
                Assert.That(harness.ServerFactory.Authorizations, Is.GreaterThanOrEqualTo(1));
            });
            if (kind == DataChannelCarrierKind.DedicatedUasc)
            {
                Assert.That(outgoing.ChannelId, Is.EqualTo(1));
                Assert.That(connection.Services, Is.Null);
                await using DataChannel nextOutgoing = await connection.Context.OpenAsync(
                    Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
                await using DataChannel nextIncoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
                Assert.That(nextOutgoing.ChannelId, Is.EqualTo(3));
            }
            else
            {
                IServiceResponse response = await connection.Services!.SendRequestAsync(
                    Read(7), harness.Token).ConfigureAwait(false);
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(7));
                Assert.That(harness.ServiceRequests, Is.EqualTo(1));
            }

            Assert.That(await outgoing.EndAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await incoming.ReadAsync(harness.Token).ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// Real TCP Reverse Connect preserves physical context roles and independent UASC encryption roles.
        /// </summary>
        [TestCase(MessageSecurityMode.SignAndEncrypt, DataChannelDirection.Duplex, false)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, DataChannelDirection.AcceptorToOpener, false)]
        [TestCase(MessageSecurityMode.Sign, DataChannelDirection.Duplex, false)]
        [TestCase(MessageSecurityMode.Sign, DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(MessageSecurityMode.Sign, DataChannelDirection.AcceptorToOpener, false)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, DataChannelDirection.Duplex, true)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, DataChannelDirection.OpenerToAcceptor, true)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, DataChannelDirection.AcceptorToOpener, true)]
        [TestCase(MessageSecurityMode.Sign, DataChannelDirection.Duplex, true)]
        [TestCase(MessageSecurityMode.Sign, DataChannelDirection.OpenerToAcceptor, true)]
        [TestCase(MessageSecurityMode.Sign, DataChannelDirection.AcceptorToOpener, true)]
        [CancelAfter(60000)]
        public async Task ReverseTcpKeepsPhysicalRolesIndependentOfUascAndFlowRolesAsync(
            MessageSecurityMode securityMode,
            DataChannelDirection direction,
            bool streaming)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc,
                mode: securityMode, reverse: true).ConfigureAwait(false);
            string protocol = streaming
                ? DataChannelProfiles.UadpOpaqueApplicationPayload : DataChannelProfiles.UadpPayload;
            harness.ClientFactory.PayloadProtocol = protocol;
            harness.ServerFactory.PayloadProtocol = protocol;
            await using UascDataChannelConnection connection =
                await harness.ConnectReverseAsync().ConfigureAwait(false);
            DataChannelContext server = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await server.WaitForReadyAsync(harness.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(server.Identity.Peer.IsTransportInitiator, Is.True);
                Assert.That(connection.Context.Identity.Peer.IsTransportInitiator, Is.False);
                Assert.That(server.Identity.Peer.IsContextInitiator, Is.False);
                Assert.That(connection.Context.Identity.Peer.IsContextInitiator, Is.True);
                Assert.That(server.Identity.ContextId, Is.EqualTo(connection.Context.Identity.ContextId));
                Assert.That(server.Identity.Generation, Is.EqualTo(connection.Context.Identity.Generation));
                Assert.That(server.Identity.LocalChallenge, Is.EqualTo(connection.Context.Identity.PeerChallenge));
                Assert.That(server.Identity.PeerChallenge, Is.EqualTo(connection.Context.Identity.LocalChallenge));
                Assert.That(server.Identity.LocalChallenge.IsEmpty, Is.False);
                Assert.That(server.Identity.PeerChallenge.IsEmpty, Is.False);
                Assert.That(server.Identity.Peer.PeerApplicationUri, Is.EqualTo(SocketHarness.ClientUri));
                Assert.That(connection.Context.Identity.Peer.PeerApplicationUri, Is.EqualTo(SocketHarness.ServerUri));
                Assert.That(connection.Context.Identity.Peer.Protection,
                    Is.EqualTo(securityMode == MessageSecurityMode.Sign
                        ? DataChannelProtectionMode.Integrity
                        : DataChannelProtectionMode.Confidential));
            });
            IServiceResponse before = await connection.Services!.SendRequestAsync(Read(501), harness.Token)
                .ConfigureAwait(false);
            Assert.That(before.ResponseHeader.RequestHandle, Is.EqualTo(501));
            using IDisposable clientRoute = connection.Context.RegisterUascPublisherRoute(
                SocketHarness.Resource, new Variant(42u));
            using IDisposable serverRoute = server.RegisterUascPublisherRoute(SocketHarness.Resource, new Variant(42u));
            foreach (bool serverOpens in new[] { true, false })
            {
                DataChannelContext opener = serverOpens ? server : connection.Context;
                DataChannelContext acceptor = serverOpens ? connection.Context : server;
                DataChannelParametersDataType parameters = Parameters();
                parameters.Protocol = protocol;
                parameters.ContentType = streaming ? "application/octet-stream" : "application/opcua+uadp";
                parameters.Protection = connection.Context.Identity.Peer.Protection;
                parameters.Direction = direction;
                await using DataChannel outgoing = await opener.OpenAsync(
                    parameters, cancellationToken: harness.Token).ConfigureAwait(false);
                await using DataChannel incoming = await AcceptAsync(acceptor, harness.Token).ConfigureAwait(false);
                Assert.That(outgoing.ChannelId, Is.EqualTo(serverOpens ? 2u : 1u));
                if (streaming)
                {
                    DataChannelException failure = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                        await opener.OpenAsync(parameters, cancellationToken: harness.Token)
                            .ConfigureAwait(false)).ConfigureAwait(false))!;
                    Assert.That(failure.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                }
                ByteString payload = streaming
                    ? UadpApplicationPayloadHeaderCodec.Encode(
                        new Variant(42u), ByteString.FromHexString("00feff7f"), 128)
                    : UadpPayload(ByteString.FromHexString("00feff7f"));
                if (direction != DataChannelDirection.AcceptorToOpener)
                {
                    Assert.That(await outgoing.SendAsync(
                        payload, cancellationToken: harness.Token).ConfigureAwait(false),
                        Is.EqualTo(DataChannelResult.Good));
                    using DataChannelMessage received =
                        (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
                    Assert.That(received.Payload, Is.EqualTo(payload));
                }
                if (direction != DataChannelDirection.OpenerToAcceptor)
                {
                    Assert.That(await incoming.SendAsync(
                        payload, cancellationToken: harness.Token).ConfigureAwait(false),
                        Is.EqualTo(DataChannelResult.Good));
                    using DataChannelMessage received =
                        (await outgoing.ReadAsync(harness.Token).ConfigureAwait(false))!;
                    Assert.That(received.Payload, Is.EqualTo(payload));
                }
                DataChannelParametersDataType updated = outgoing.Parameters;
                updated.MaxMessageSize = 128;
                Assert.That(await outgoing.UpdateAsync(updated, cancellationToken: harness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                Assert.That(outgoing.Revision, Is.EqualTo(2u));
                DataChannel sender = direction == DataChannelDirection.AcceptorToOpener ? incoming : outgoing;
                DataChannel receiver = direction == DataChannelDirection.AcceptorToOpener ? outgoing : incoming;
                Assert.That(await sender.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                using (DataChannelMessage received = (await receiver.ReadAsync(harness.Token).ConfigureAwait(false))!)
                {
                    Assert.That(received.Payload, Is.EqualTo(payload));
                    Assert.That(received.Revision, Is.EqualTo(2u));
                }
                IServiceResponse during = await connection.Services.SendRequestAsync(
                    Read(serverOpens ? 502u : 503u), harness.Token).ConfigureAwait(false);
                Assert.That(during.ResponseHeader.RequestHandle, Is.EqualTo(serverOpens ? 502u : 503u));
                if (direction != DataChannelDirection.AcceptorToOpener)
                {
                    Assert.That(await outgoing.EndAsync(harness.Token).ConfigureAwait(false),
                        Is.EqualTo(DataChannelResult.Good));
                    Assert.That(await incoming.ReadAsync(harness.Token).ConfigureAwait(false), Is.Null);
                }
                if (direction != DataChannelDirection.OpenerToAcceptor)
                {
                    Assert.That(await incoming.EndAsync(harness.Token).ConfigureAwait(false),
                        Is.EqualTo(DataChannelResult.Good));
                    Assert.That(await outgoing.ReadAsync(harness.Token).ConfigureAwait(false), Is.Null);
                }
            }
            IServiceResponse after = await connection.Services.SendRequestAsync(Read(504), harness.Token)
                .ConfigureAwait(false);
            Assert.That(after.ResponseHeader.RequestHandle, Is.EqualTo(504));
            Assert.That(harness.ServiceRequests, Is.EqualTo(4));
            await connection.CloseAsync(harness.Token).ConfigureAwait(false);
            await server.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.ClientFactory.Budget.Usage, Is.Default);
                Assert.That(harness.ServerFactory.Budget.Usage, Is.Default);
            });
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task SharedUascRoutesLiveUadpPublisherIdsToTheirConfiguredFlowsAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType firstParameters = Parameters();
            firstParameters.ResourceUri = UadpFirstResource;
            firstParameters.Protocol = DataChannelProfiles.UadpPayload;
            firstParameters.AllowQueueExpiry = false;
            DataChannelParametersDataType secondParameters = Parameters();
            secondParameters.ResourceUri = UadpSecondResource;
            secondParameters.Protocol = DataChannelProfiles.UadpPayload;
            secondParameters.AllowQueueExpiry = false;
            await using DataChannel firstSender = await connection.Context.OpenAsync(
                firstParameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel firstReceiver = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            await using DataChannel secondSender = await connection.Context.OpenAsync(
                secondParameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel secondReceiver = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            using IDisposable firstRoute = remote.RegisterUascPublisherRoute(UadpFirstResource, new Variant(17u));
            using IDisposable secondRoute = remote.RegisterUascPublisherRoute(UadpSecondResource, new Variant(42u));
            var firstPayload = ByteString.FromHexString("910211000000dead");
            var secondPayload = ByteString.FromHexString("91022a000000beef");

            Assert.That(await firstSender.SendAsync(firstPayload, cancellationToken: harness.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage firstMessage = (await firstReceiver.ReadAsync(harness.Token)
                .ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(firstMessage.Payload, Is.EqualTo(firstPayload));
                Assert.That(firstReceiver.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(secondReceiver.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(remote.IsReady, Is.True);
            });

            Assert.That(await secondSender.SendAsync(secondPayload, cancellationToken: harness.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage secondMessage = (await secondReceiver.ReadAsync(harness.Token)
                .ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(secondMessage.Payload, Is.EqualTo(secondPayload));
                Assert.That(firstReceiver.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(secondReceiver.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(connection.Context.IsReady, Is.True);
            });
            IServiceResponse service = await connection.Services!.SendRequestAsync(Read(117), harness.Token)
                .ConfigureAwait(false);
            Assert.That(service.ResponseHeader.RequestHandle, Is.EqualTo(117));
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task SharedCreditExhaustionDoesNotBlockServicesOrControlsAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType small = Parameters();
            small.MaxMessageSize = 15;
            small.MaxReceiveBytes = 15;
            await using DataChannel stalled = await connection.Context.OpenAsync(
                small, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel stalledPeer = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);

            ByteString fullWindow = UadpPayload(new ByteString(new byte[8]));
            Assert.That(
                await stalled.SendAsync(fullWindow, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage held = (await stalledPeer.ReadAsync(harness.Token).ConfigureAwait(false))!;
            using var cancelled = new CancellationTokenSource();
            Task<DataChannelResult> blocked = stalled.SendAsync(fullWindow, cancellationToken: cancelled.Token)
                .AsTask();

            IServiceResponse response = await connection.Services!.SendRequestAsync(Read(42), harness.Token)
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(42));
            Assert.That(await connection.Context.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            if (blocked.IsCompleted)
            {
                Assert.That(await blocked.ConfigureAwait(false), Is.EqualTo(DataChannelResult.WouldBlock));
            }
            else
            {
                await cancelled.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await blocked.WaitAsync(harness.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }
            Assert.That(connection.Context.IsReady, Is.True);
            Assert.That(held.Payload.ToArray(), Is.EqualTo(fullWindow.ToArray()));
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.DedicatedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task ADiscoveredButUntrustedCertificateCannotCreateAClientContextAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport,
                kind,
                trustServer: false).ConfigureAwait(false);

            ServiceResultException failure = (await Assert.CatchAsync<ServiceResultException>(async () =>
                await harness.ConnectAsync().ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.Not.EqualTo(StatusCodes.Good));
            Assert.That(harness.ClientFactory.Contexts, Is.Empty);
            Assert.That(harness.ServiceRequests, Is.Zero);
#if NET8_0_OR_GREATER
            if (transport == CarrierTransport.ManagedWebTransport)
            {
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            }
#endif
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task ExpectedApplicationMismatchCannotCreateAClientContextAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind).ConfigureAwait(false);
            string expectedApplication = "urn:opcfoundation:tests:wrong-server";
#if NET8_0_OR_GREATER
            if (transport == CarrierTransport.ManagedWebTransport)
            {
                expectedApplication = SocketHarness.TlsServerUri;
            }
#endif

            ServiceResultException failure = (await Assert.CatchAsync<ServiceResultException>(async () =>
                await harness.ConnectAsync(expectedApplication).ConfigureAwait(false))
                    .ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.Not.EqualTo(StatusCodes.Good));
            Assert.That(harness.ClientFactory.Contexts, Is.Empty);
            Assert.That(harness.ServiceRequests, Is.Zero);
#if NET8_0_OR_GREATER
            if (transport == CarrierTransport.ManagedWebTransport)
            {
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
            }
#endif
        }

        [Test]
        [CancelAfter(60000)]
        public async Task ReconnectRetiresTheOldContextAndCreatesFreshBindingChallengesAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext old = connection.Context;
            Guid oldId = old.Identity.ContextId;
            uint oldSecureChannelId = connection.SecureChannelId;
            ByteString oldChallenge = old.Identity.LocalChallenge;
            var closed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            old.Closed += result => closed.TrySetResult(result);

            await connection.Services!.ReconnectAsync(null, harness.Token).ConfigureAwait(false);
            await closed.Task.WaitAsync(harness.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(old.IsReady, Is.False);
                Assert.That(connection.Context, Is.Not.SameAs(old));
                Assert.That(connection.SecureChannelId, Is.Not.EqualTo(oldSecureChannelId));
                Assert.That(connection.Context.Identity.ContextId, Is.Not.EqualTo(oldId));
                Assert.That(connection.Context.Identity.LocalChallenge, Is.Not.EqualTo(oldChallenge));
                Assert.That(harness.ClientFactory.Contexts, Has.Count.EqualTo(2));
            });
            IServiceResponse response = await connection.Services.SendRequestAsync(Read(99), harness.Token)
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(99));
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task LiveRevisionCommitsOnBothPeersWithoutReopeningTheTransportAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(transport, kind)
                .ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await using DataChannel outgoing = await connection.Context.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            Guid contextId = connection.Context.Identity.ContextId;
            int headerLength = UadpPayload(ByteString.Empty).Length;
            ByteString oldMessage = UadpPayload(new ByteString(new byte[2048 - headerLength]));
            Assert.That(await outgoing.SendAsync(oldMessage, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await incoming.SendAsync(oldMessage, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            DataChannelParametersDataType revised = outgoing.Parameters;
            revised.MaxMessageSize = 1024;
            revised.MaxReceiveBytes = 4096;
            revised.Priority = 5;

            Assert.That(await outgoing.UpdateAsync(revised, 7, harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage oldIncoming = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
            using DataChannelMessage oldOutgoing = (await outgoing.ReadAsync(harness.Token).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(outgoing.Revision, Is.EqualTo(2));
                Assert.That(incoming.Revision, Is.EqualTo(2));
                Assert.That(incoming.Parameters.MaxMessageSize, Is.EqualTo(1024));
                Assert.That(incoming.Parameters.MaxReceiveBytes, Is.EqualTo(4096));
                Assert.That(incoming.Parameters.Priority, Is.EqualTo(5));
                Assert.That(connection.Context.Identity.ContextId, Is.EqualTo(contextId));
                Assert.That(harness.ServerFactory.Contexts, Has.Count.EqualTo(1));
                Assert.That(oldIncoming.Revision, Is.EqualTo(1));
                Assert.That(oldOutgoing.Revision, Is.EqualTo(1));
                Assert.That(oldIncoming.Payload, Is.EqualTo(oldMessage));
                Assert.That(oldOutgoing.Payload, Is.EqualTo(oldMessage));
            });
            ByteString maximumMessage = UadpPayload(new ByteString(new byte[1024 - headerLength]));
            Assert.That(
                await outgoing.SendAsync(maximumMessage, cancellationToken: harness.Token)
                    .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage message = (await incoming.ReadAsync(harness.Token)
                .ConfigureAwait(false))!;
            Assert.That(message.Payload.Length, Is.EqualTo(maximumMessage.Length));
            Assert.That(message.Revision, Is.EqualTo(2));
            Assert.That(await incoming.SendAsync(maximumMessage, cancellationToken: harness.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage reply = (await outgoing.ReadAsync(harness.Token).ConfigureAwait(false))!;
            Assert.That(reply.Payload, Is.EqualTo(maximumMessage));
            Assert.That(reply.Revision, Is.EqualTo(2));
            Assert.That(
                await outgoing.SendAsync(UadpPayload(new ByteString(new byte[1025 - headerLength])),
                    cancellationToken: harness.Token)
                    .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(await incoming.SendAsync(
                UadpPayload(new ByteString(new byte[1025 - headerLength])), cancellationToken: harness.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.TooLarge));
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task UnknownConfiguredResourceIsRefusedWithoutAffectingSharedServicesAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.ResourceUri = "urn:opcfoundation:tests:not-authorized";

            DataChannelException refusal = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                await connection.Context.OpenAsync(parameters, cancellationToken: harness.Token)
                    .ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(refusal.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
            Assert.That(connection.Context.Channels.IsEmpty, Is.True);
            IServiceResponse response = await connection.Services!.SendRequestAsync(Read(88), harness.Token)
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(88));
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task ResettingOneSharedFlowPreservesSharedServicesAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await using DataChannel reset = await connection.Context.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel resetPeer = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            uint channelId = connection.SecureChannelId;

            await reset.ResetAsync(DataChannelResult.InvalidArgument, harness.Token).ConfigureAwait(false);
            IServiceResponse service = await connection.Services!.SendRequestAsync(Read(77), harness.Token)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(service.ResponseHeader.RequestHandle, Is.EqualTo(77));
                Assert.That(connection.SecureChannelId, Is.EqualTo(channelId));
                Assert.That(connection.Context.IsReady, Is.True);
                Assert.That(remote.IsReady, Is.True);
            });
        }

        [Test]
        [CancelAfter(60000)]
        public async Task ClosingAfterSharedSessionTransportDisposalIsIdempotentAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext context = connection.Context;

            connection.Services!.Dispose();
            await connection.CloseAsync(harness.Token).ConfigureAwait(false);
            await connection.CloseAsync(harness.Token).ConfigureAwait(false);

            Assert.That(context.IsReady, Is.False);
            Assert.That(connection.SecureChannelId, Is.Zero);
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [CancelAfter(60000)]
        public async Task AcceptorCanOpenAndSendWithoutBeingTheTransportInitiatorAsync(DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(CarrierTransport.Tcp, kind)
                .ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await using DataChannel openedByServer = await remote.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel acceptedByClient = await AcceptAsync(connection.Context, harness.Token)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(remote.Identity.Peer.IsTransportInitiator, Is.False);
                Assert.That(openedByServer.IsLocalOpener, Is.True);
                Assert.That(acceptedByClient.IsLocalOpener, Is.False);
                Assert.That(openedByServer.ChannelId,
                    Is.EqualTo(2u));
            });
            ByteString payload = UadpPayload(new ByteString(new byte[] { 0x80, 0x81 }));
            Assert.That(
                await openedByServer.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received =
                (await acceptedByClient.ReadAsync(harness.Token).ConfigureAwait(false))!;
            Assert.That(received.Payload, Is.EqualTo(payload));
        }

        /// <summary>
        /// Negotiated UADP capacity spans contiguous encrypted MSG chunks without charging its TypeId as data.
        /// </summary>
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.DedicatedUasc, DataChannelProfiles.UadpPayload)]
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc, DataChannelProfiles.UadpPayload)]
        [TestCase(CarrierTransport.ReliableStream, DataChannelCarrierKind.DedicatedUasc,
            DataChannelProfiles.UadpPayload)]
        [TestCase(CarrierTransport.ReliableStream, DataChannelCarrierKind.SharedUasc,
            DataChannelProfiles.UadpPayload)]
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.DedicatedUasc,
            DataChannelProfiles.UadpOpaqueApplicationPayload)]
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc,
            DataChannelProfiles.UadpOpaqueApplicationPayload)]
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.DedicatedUasc,
            DataChannelProfiles.UadpAvApplicationPayload)]
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc,
            DataChannelProfiles.UadpAvApplicationPayload)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc, DataChannelProfiles.UadpPayload)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc,
            DataChannelProfiles.UadpPayload)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc,
            DataChannelProfiles.UadpOpaqueApplicationPayload)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc,
            DataChannelProfiles.UadpAvApplicationPayload)]
#endif
        [CancelAfter(60000)]
        public async Task MaximumNegotiatedMsgUsesContiguousEncryptedChunksAsync(
            CarrierTransport transport, DataChannelCarrierKind kind, string protocol)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(transport, kind)
                .ConfigureAwait(false);
            harness.ServerFactory.MaxMessageSize = 65536;
            harness.ClientFactory.MaxMessageSize = 65536;
            harness.ServerFactory.PayloadProtocol = protocol;
            harness.ClientFactory.PayloadProtocol = protocol;
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            bool streaming = protocol != DataChannelProfiles.UadpPayload;
            using IDisposable? localRoute = streaming ? client.DataChannels!.RegisterUascPublisherRoute(
                SocketHarness.Resource, new Variant(42u), null, null, 3) : null;
            using IDisposable? remoteRoute = streaming ? remote.RegisterUascPublisherRoute(
                SocketHarness.Resource, new Variant(42u), null, null, 3) : null;
            int maximumPayload = Math.Min(65536,
                client.GetMaxDataChannelRecordSize(kind) - UascFlowMessageCodec.TypeIdSize);
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protocol = protocol;
            if (streaming)
            {
                parameters.ContentType = protocol == DataChannelProfiles.UadpAvApplicationPayload
                    ? "video/h264" : "application/octet-stream";
            }
            parameters.MaxMessageSize = 65536;
            parameters.MaxReceiveBytes = 131072;
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            Assert.That(outgoing.Parameters.MaxMessageSize, Is.EqualTo(maximumPayload));
            Assert.That(await client.DataChannels.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            int headerLength = UadpPayload(ByteString.Empty).Length;
            byte[] payload =
                [.. Enumerable.Range(0, maximumPayload - headerLength).Select(index => (byte)(index % 256))];
            ByteString networkMessage = streaming
                ? UadpApplicationPayloadHeaderCodec.Encode(new Variant(42u), new ByteString(payload), int.MaxValue)
                : UadpPayload(new ByteString(payload));

            int before = client.WireChunks.Count;
            Assert.That(await outgoing.SendAsync(networkMessage, cancellationToken: harness.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
            byte[][] chunks = [.. client.WireChunks.Skip(before)];
            var assembled = new List<byte>();
            uint previousSequence = 0;
            for (int index = 0; index < chunks.Length; index++)
            {
                byte[] chunk = chunks[index];
                (ByteString Body, uint RequestId, uint Sequence) = await client.CanonicalPart30DecodeAsync(
                    chunk, true, harness.Token)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(chunk, Has.Length.LessThanOrEqualTo(8192));
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(chunk) & TcpMessageType.MessageTypeMask,
                        Is.EqualTo(TcpMessageType.Message));
                    Assert.That(chunk[3], Is.EqualTo(index == chunks.Length - 1 ? (byte)'F' : (byte)'C'));
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4)),
                        Is.EqualTo(chunk.Length));
                    Assert.That(RequestId, Is.EqualTo(0x80000000u | outgoing.ChannelId));
                });
                if (index > 0)
                {
                    Assert.That(Sequence, Is.EqualTo(previousSequence + 1));
                }
                previousSequence = Sequence;
                assembled.AddRange(Body.ToArray());
            }
            Assert.Multiple(() =>
            {
                Assert.That(received.Payload.ToArray(), Is.EqualTo(networkMessage.ToArray()));
                Assert.That(chunks, Has.Length.GreaterThan(1));
                Assert.That(assembled.Take(2), Is.EqualTo(new byte[] { 0, 254 }));
                Assert.That(assembled.Skip(2), Is.EqualTo(networkMessage.ToArray()));
            });
            byte[] oversized = new byte[maximumPayload + 1];
            networkMessage.Span.CopyTo(oversized);
            Assert.That(await outgoing.SendAsync(new ByteString(oversized),
                cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(client.DataChannels.IsReady, Is.True);
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// A successful TLS transport cannot select a foreign QUIC application protocol.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public async Task ManagedQuicRejectsForeignAlpnBeforeCreatingUascContextAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc).ConfigureAwait(false);

            await Assert.ThrowsAsync<AuthenticationException>(async () =>
            {
                await using QuicConnection connection = await harness.ConnectRawQuicClientAsync(
                    new SslApplicationProtocol("foreign-uasc-protocol")).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.ClientFactory.Contexts, Is.Empty);
                Assert.That(harness.ServerFactory.Contexts, Is.Empty);
                Assert.That(harness.ServiceRequests, Is.Zero);
            });
        }

        /// <summary>
        /// An authenticated QUIC connection without stream zero is retired by the finite admission deadline.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public async Task ManagedQuicBaselineRequiresFirstStreamWithinDeadlineAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc).ConfigureAwait(false);
            await using QuicConnection connection = await harness.ConnectRawQuicClientAsync(
                new SslApplicationProtocol(DataChannelProfiles.UascQuicAlpn)).ConfigureAwait(false);

            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await harness.WaitForQuicAcceptAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.ClientFactory.Contexts, Is.Empty);
                Assert.That(harness.ServerFactory.Contexts, Is.Empty);
                Assert.That(harness.ServiceRequests, Is.Zero);
            });
        }

        /// <summary>
        /// The accepted baseline consumes the sole inbound bidirectional stream allowance.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public async Task ManagedQuicBaselineDoesNotAdmitAnAdditionalPeerStreamAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc).ConfigureAwait(false);
            await using QuicConnection connection = await harness.ConnectRawQuicClientAsync(
                new SslApplicationProtocol(DataChannelProfiles.UascQuicAlpn)).ConfigureAwait(false);
            await using QuicStream baseline = await connection.OpenOutboundStreamAsync(
                QuicStreamType.Bidirectional, harness.Token).ConfigureAwait(false);
            await baseline.WriteAsync("H"u8.ToArray(), harness.Token).ConfigureAwait(false);
            await harness.WaitForQuicAcceptAsync().ConfigureAwait(false);

            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                await using QuicStream additional = await connection.OpenOutboundStreamAsync(
                    QuicStreamType.Bidirectional, deadline.Token).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(baseline.CanWrite, Is.True);
        }

        /// <summary>
        /// A successful QUIC handshake cannot authorize a HEL naming another baseline endpoint.
        /// </summary>
        [TestCase("authority")]
        [TestCase("port")]
        [TestCase("path")]
        [TestCase("query")]
        [TestCase("scheme")]
        [CancelAfter(60000)]
        public async Task ManagedQuicBaselineRejectsSubstitutedHelloEndpointAsync(string mismatch)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc).ConfigureAwait(false);

            StatusCode error = await harness.SendQuicHelloWithEndpointMismatchAsync(mismatch).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(error, Is.EqualTo(StatusCodes.BadTcpEndpointUrlInvalid));
                Assert.That(harness.ClientFactory.Contexts, Is.Empty);
                Assert.That(harness.ServerFactory.Contexts, Is.Empty);
                Assert.That(harness.ServiceRequests, Is.Zero);
            });
        }

        /// <summary>
        /// Reconnect cannot claim the existing baseline again or reverse its fixed transport roles.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task ManagedQuicBaselineCannotBeReclaimedAfterOpnAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await harness.ReclaimQuicClientStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Throws<InvalidOperationException>(() => harness.CreateClientFromAcceptedQuicBaseline());

            IServiceResponse response = await connection.Services!.SendRequestAsync(Read(325), harness.Token)
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(325));
            Assert.That(connection.Context.IsReady, Is.True);
        }

        /// <summary>
        /// Losing the physical association closes both contexts and does not preserve flow readiness.
        /// </summary>
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
        [CancelAfter(60000)]
        public async Task ManagedConnectionLossRetiresBothFlowContextsAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext local = connection.Context;
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await using DataChannel outgoing = await connection.Context.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            var localClosed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var remoteClosed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            local.Closed += result => localClosed.TrySetResult(result);
            remote.Closed += result => remoteClosed.TrySetResult(result);

            await harness.CloseManagedClientAsync().ConfigureAwait(false);
            await localClosed.Task.WaitAsync(harness.Token).ConfigureAwait(false);
            await remoteClosed.Task.WaitAsync(harness.Token).ConfigureAwait(false);

            Assert.That(local.IsReady, Is.False);
            Assert.That(remote.IsReady, Is.False);
            ServiceResultException missing = Assert.Throws<ServiceResultException>(() => _ = connection.Context)!;
            Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            await Assert.CatchAsync<ServiceResultException>(async () =>
                await connection.Services!.SendRequestAsync(Read(326), harness.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Both directions preserve ordinary UASC continuation/final chunks on managed reliable baselines.
        /// </summary>
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
        [CancelAfter(60000)]
        public async Task ManagedServicesBaselineReassemblesChunkedRequestsAndResponsesAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind, enableDataChannels: false)
                .ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync(enableDataChannels: false)
                .ConfigureAwait(false);
            ReadRequest request = Read(322);
            request.NodesToRead = new ArrayOf<ReadValueId>(Enumerable.Range(0, 2048)
                .Select(_ => new ReadValueId { NodeId = ObjectIds.Server, AttributeId = Attributes.NodeId })
                .ToArray().AsMemory());

            IServiceResponse response = await client.SendRequestAsync(request, 10000, harness.Token)
                .ConfigureAwait(false);

            Assert.That(response, Is.TypeOf<ReadResponse>());
            var read = (ReadResponse)response;
            Assert.Multiple(() =>
            {
                Assert.That(read.ResponseHeader.RequestHandle, Is.EqualTo(322));
                Assert.That(read.Results.Count, Is.EqualTo(request.NodesToRead.Count));
                foreach (DataValue result in read.Results.Span)
                {
                    Assert.That(result.WrappedValue, Is.EqualTo(new Variant(ObjectIds.Server)));
                }
                Assert.That(client.WireChunks.Any(chunk =>
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk) == TcpMessageType.MessageIntermediate), Is.True);
                Assert.That(client.WireChunks.Any(chunk =>
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk) == TcpMessageType.MessageFinal), Is.True);
                Assert.That(client.Recorder.Transport!.ReceivedChunks.Any(chunk =>
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk) == TcpMessageType.MessageIntermediate), Is.True);
                Assert.That(client.Recorder.Transport!.ReceivedChunks.Any(chunk =>
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk) == TcpMessageType.MessageFinal), Is.True);
                Assert.That(harness.ServiceRequests, Is.EqualTo(1));
                Assert.That(client.DataChannels, Is.Null);
            });
            IServiceResponse following = await client.SendRequestAsync(Read(323), 10000, harness.Token)
                .ConfigureAwait(false);
            Assert.That(following.ResponseHeader.RequestHandle, Is.EqualTo(323));
        }

        /// <summary>
        /// An authenticated MSG abort discards its partial request, not the baseline or subsequent Services.
        /// </summary>
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
        [CancelAfter(60000)]
        public async Task ManagedServicesBaselineAbortsPartialRequestWithoutClosingAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind, enableDataChannels: false)
                .ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync(enableDataChannels: false)
                .ConfigureAwait(false);
            // The legacy management capacity is one security chunk, not the negotiated multipart message limit.
            int capacity = client.GetMaxDataChannelRecordSize(DataChannelCarrierKind.WebTransport);
            byte[] partialAndAbort = new byte[capacity + 8];
            BinaryPrimitives.WriteUInt32LittleEndian(
                partialAndAbort.AsSpan(capacity), StatusCodes.BadRequestCancelledByClient.Code);

            await client.SendAuthenticatedBodyAsync(
                TcpMessageType.Message, 777, partialAndAbort, abort: true, harness.Token).ConfigureAwait(false);
            IServiceResponse following = await client.SendRequestAsync(Read(324), 10000, harness.Token)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(following.ResponseHeader.RequestHandle, Is.EqualTo(324));
                Assert.That(harness.ServiceRequests, Is.EqualTo(1), "The aborted request must never be dispatched.");
                Assert.That(client.WireChunks.Any(chunk =>
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk) == TcpMessageType.MessageIntermediate), Is.True);
                Assert.That(client.WireChunks.Count(chunk =>
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk) ==
                        (TcpMessageType.Message | TcpMessageType.Abort)), Is.EqualTo(1));
                Assert.That(client.DataChannels, Is.Null);
            });
        }

        /// <summary>
        /// Proves the Services baselines without a Data Channel factory, Session or model.
        /// </summary>
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
        [CancelAfter(60000)]
        public async Task ManagedServicesBaselineDoesNotCreateAnOptionalDataContextAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind, enableDataChannels: false)
                .ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync(enableDataChannels: false)
                .ConfigureAwait(false);
            IServiceResponse response = await client.SendRequestAsync(Read(321), 10000, harness.Token)
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(321));
            Assert.That(client.DataChannels, Is.Null);
            Assert.That(harness.ServiceRequests, Is.EqualTo(1));
            Assert.That(harness.ClientFactory.Contexts, Is.Empty);
            Assert.That(harness.ServerFactory.Contexts, Is.Empty);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(client.WireChunks.First()),
                Is.EqualTo(TcpMessageType.Hello));
            await client.CloseAsync(1000, harness.Token).ConfigureAwait(false);
            Assert.That(
                BinaryPrimitives.ReadUInt32LittleEndian(client.WireChunks.Last()) & TcpMessageType.MessageTypeMask,
                Is.EqualTo(TcpMessageType.Close));
        }

        /// <summary>
        /// Unavailable QUIC exporter binding refuses direct delivery without disabling baseline FLO or Services.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task ManagedQuicExporterUnavailablePreservesBaselineFlowsAndServicesAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext local = connection.Context;
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);

            DataChannelResult[] bindingResults = await Task.WhenAll(
                local.WaitForDirectBindingAsync(harness.Token).AsTask(),
                remote.WaitForDirectBindingAsync(harness.Token).AsTask()).ConfigureAwait(false);
            Assert.That(bindingResults, Is.All.EqualTo(DataChannelResult.Unsupported));
            Assert.That(local.IsReady && remote.IsReady, Is.True);

            await using DataChannel outgoing = await local.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            ByteString payload = UadpPayload(new ByteString(new byte[] { 0x01, 0xFE, 0x7F }));
            Assert.That(
                await outgoing.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
            Assert.That(received.Payload, Is.EqualTo(payload));

            IServiceResponse response = await connection.Services!.SendRequestAsync(Read(327), harness.Token)
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(327));
            Assert.That(local.IsReady && remote.IsReady, Is.True);
        }

        /// <summary>
        /// Exercises listener-side rejection even when a client bypasses its local profile validation.
        /// </summary>
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
        [CancelAfter(60000)]
        public async Task ManagedListenerRejectsIntegrityOnlyOpnWithoutADataContextAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind, enableDataChannels: false)
                .ConfigureAwait(false);
            ServiceResultException failure = (await Assert.CatchAsync<ServiceResultException>(async () =>
                await harness.ConnectProbeAsync(
                    confirmProfile: false, requestedMode: MessageSecurityMode.Sign, enableDataChannels: false)
                    .ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.Not.EqualTo(StatusCodes.Good));
            Assert.That(harness.ServerFactory.Contexts, Is.Empty);
            Assert.That(harness.ClientFactory.Contexts, Is.Empty);
            Assert.That(harness.ServiceRequests, Is.Zero);
        }

        [TestCase(Profiles.OpcUaWsSubProtocolUaJson)]
        [TestCase(Profiles.OpcUaWsSubProtocolOpenApi)]
        [CancelAfter(60000)]
        public async Task WssFlowResourceRefusesNonBinarySubprotocolBeforeApplicationDispatchAsync(string protocol)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Wss, DataChannelCarrierKind.DedicatedUasc).ConfigureAwait(false);

            await Assert.ThrowsAsync<WebSocketException>(async () =>
            {
                using RawWebSocket connection = await harness.OpenRawWebSocketAsync(protocol).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(harness.ServerFactory.Contexts, Is.Empty);
            Assert.That(harness.ServiceRequests, Is.Zero);
        }

        [Test]
        [CancelAfter(60000)]
        public async Task BinaryWssFlowRejectsTextFramesBeforeCreatingAContextAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Wss, DataChannelCarrierKind.SharedUasc).ConfigureAwait(false);
            using RawWebSocket connection = await harness.OpenRawWebSocketAsync(Profiles.OpcUaWsSubProtocolUacp)
                .ConfigureAwait(false);
            await connection.Socket.SendAsync(
                new ReadOnlyMemory<byte>([0x41]), WebSocketMessageType.Text, true, harness.Token)
                .ConfigureAwait(false);
            bool closed;
            try
            {
                System.Net.WebSockets.ValueWebSocketReceiveResult result = await connection.Socket.ReceiveAsync(
                    new Memory<byte>(new byte[128]), harness.Token).ConfigureAwait(false);
                closed = result.MessageType == WebSocketMessageType.Close;
            }
            catch (WebSocketException)
            {
                closed = true;
            }

            Assert.That(closed, Is.True);
            Assert.That(harness.ServerFactory.Contexts, Is.Empty);
            Assert.That(harness.ServiceRequests, Is.Zero);
        }
#endif

        /// <summary>
        /// A never-admitted DATA identifier receives an advisory without closing the secured context or other flows.
        /// </summary>
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task SecuredUnknownChannelStatusPreservesContextFlowsAndServicesAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(transport, kind).ConfigureAwait(false);
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            harness.ServerFactory.Clock = clock;
            using ProbeClientChannel client = await harness.ConnectProbeAsync(clock: clock).ConfigureAwait(false);
            DataChannelContext local = client.DataChannels!;
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await using DataChannel outgoing = await local.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            Assert.That(await incoming.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            int before = client.Recorder.Transport!.ReceivedChunks.Count;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                await client.SendAuthenticatedBodyAsync(
                    TcpMessageType.Message, 0x80000063, [0, 0xFE, 1, 0], false, harness.Token)
                    .ConfigureAwait(false);
            }
            Assert.That(await outgoing.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));

            int advisoryCount = 0;
            foreach (byte[] chunk in client.Recorder.Transport.ReceivedChunks.Skip(before))
            {
                (ByteString Body, uint RequestId, uint Sequence) = await client.CanonicalPart30DecodeAsync(
                    chunk, false, harness.Token)
                    .ConfigureAwait(false);
                UascFlowMessage message = UascFlowMessageCodec.Decode(
                    FlowModel.ToWire(kind), DataChannelUascMessageKind.Msg, (byte)'F',
                    RequestId, Body, new());
                if (message.Control is FlowOutcome { Status: FlowWireStatus.UnknownChannel } status)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(status.Operation, Is.EqualTo(FlowOperation.Status));
                        Assert.That(status.Flow, Is.Zero);
                        Assert.That(status.RelatedOperation, Is.Zero);
                        Assert.That(RequestId, Is.Zero);
                    });
                    advisoryCount++;
                }
            }
            Assert.Multiple(() =>
            {
                Assert.That(advisoryCount, Is.EqualTo(1));
                Assert.That(local.IsReady && remote.IsReady, Is.True);
                Assert.That(local.Channels.Count, Is.EqualTo(1));
                Assert.That(remote.Channels.Count, Is.EqualTo(1));
                Assert.That(outgoing.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(incoming.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(incoming.GetDiagnostics().MessagesReceived, Is.Zero);
            });
            ByteString payload = UadpPayload(ByteString.FromHexString("deadbeef"));
            Assert.That(await outgoing.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
            Assert.That(received.Payload, Is.EqualTo(payload));
            if (kind != DataChannelCarrierKind.DedicatedUasc)
            {
                IServiceResponse response = await client.SendRequestAsync(Read(328), 10000, harness.Token)
                    .ConfigureAwait(false);
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(328));
                Assert.That(harness.ServiceRequests, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Both context and flow PINGs receive one exact correlated refusal when the peer rate is exceeded.
        /// </summary>
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task SecuredPingRateRefusalIsCorrelatedWithoutControlLoopsAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(transport, kind).ConfigureAwait(false);
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            harness.ServerFactory.Clock = clock;
            harness.ClientFactory.Clock = clock;
            using ProbeClientChannel client = await harness.ConnectProbeAsync(clock: clock).ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task.WaitAsync(harness.Token)
                .ConfigureAwait(false);
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            // Flush initial CREDIT before injected operation IDs advance the peer high-water.
            Assert.That(await client.DataChannels.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await remote.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            uint[] targets = [0, outgoing.ChannelId];
            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                uint target = targets[targetIndex];
                uint revision = target == 0 ? 0u : 1u;
                clock.Advance(TimeSpan.FromSeconds(2));
                int before = client.Recorder.Transport!.ReceivedChunks.Count;
                int beforeWrites = client.WireChunks.Count;
                for (uint operation = 100; operation <= 101; operation++)
                {
                    FlowRecord record = FlowControlCodec.Encode(
                        new FlowProbe(FlowOperation.Ping, target, operation), revision, operation, new());
                    byte[] body = UascFlowMessageCodec.Encode(FlowModel.ToWire(kind), target,
                        in record, new(), out uint requestId).ToArray();
                    await client.SendAuthenticatedBodyAsync(
                        TcpMessageType.Message, requestId, body, false, harness.Token).ConfigureAwait(false);
                }
                var controls = new List<FlowControl>();
                int consumed = 0;
                while (controls.Count < 2)
                {
                    foreach (byte[] chunk in client.Recorder.Transport.ReceivedChunks.Skip(before + consumed))
                    {
                        consumed++;
                        (ByteString body, uint requestId, uint _) = await client.CanonicalPart30DecodeAsync(
                            chunk, false, harness.Token).ConfigureAwait(false);
                        UascFlowMessage message = UascFlowMessageCodec.Decode(FlowModel.ToWire(kind),
                            DataChannelUascMessageKind.Msg, (byte)'F', requestId, body, new());
                        if (message.Control is not FlowCredit)
                        {
                            controls.Add(message.Control!);
                        }
                    }
                    if (controls.Count < 2)
                    {
                        await Task.Delay(1, harness.Token).ConfigureAwait(false);
                    }
                }
                Assert.That(controls, Has.Count.EqualTo(2), string.Join(", ", controls.Select(c =>
                    c is FlowOutcome outcome
                        ? $"{outcome.Operation}: {outcome.Status}, flow {outcome.Flow}, " +
                            $"related {outcome.RelatedOperation}"
                        : $"{c.Operation}: flow {c.Flow}")));
                Assert.That(controls[0], Is.TypeOf<FlowProbe>());
                Assert.That(controls[1], Is.TypeOf<FlowOutcome>());
                var pong = (FlowProbe)controls[0];
                var status = (FlowOutcome)controls[1];
                Assert.Multiple(() =>
                {
                    Assert.That(pong.Operation, Is.EqualTo(FlowOperation.Pong));
                    Assert.That(pong.Flow, Is.EqualTo(target));
                    Assert.That(pong.Timestamp, Is.EqualTo(100));
                    Assert.That(status.Operation, Is.EqualTo(FlowOperation.Status));
                    Assert.That(status.Flow, Is.EqualTo(target));
                    Assert.That(status.Status, Is.EqualTo(FlowWireStatus.WouldBlock));
                    Assert.That(status.RelatedOperation, Is.EqualTo(101));
                });
                IServiceResponse service = await client.SendRequestAsync(
                    Read((uint)targetIndex + 120), 10000, harness.Token).ConfigureAwait(false);
                int sentPings = 0;
                foreach (byte[] chunk in client.WireChunks.Skip(beforeWrites))
                {
                    (ByteString body, uint requestId, uint _) = await client.CanonicalPart30DecodeAsync(
                        chunk, true, harness.Token).ConfigureAwait(false);
                    if (body.Length >= 2 && body[0] == 0 && body[1] == 255)
                    {
                        UascFlowMessage message = UascFlowMessageCodec.Decode(FlowModel.ToWire(kind),
                            DataChannelUascMessageKind.Msg, (byte)'F', requestId, body, new());
                        Assert.That(message.Control!.Operation, Is.AnyOf(FlowOperation.Ping, FlowOperation.Credit));
                        if (message.Control.Operation == FlowOperation.Ping)
                        {
                            sentPings++;
                        }
                    }
                }
                Assert.Multiple(() =>
                {
                    Assert.That(service.ResponseHeader.RequestHandle, Is.EqualTo((uint)targetIndex + 120));
                    Assert.That(sentPings, Is.EqualTo(2));
                    Assert.That(incoming.State, Is.EqualTo(DataChannelState.Open));
                    Assert.That(remote.IsReady, Is.True);
                });
            }
        }

        /// <summary>
        /// Retired DATA returns only aggregate credit; late controls cannot provoke STATUS or RESET loops.
        /// </summary>
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task SecuredRetiredDataAndControlsAreSilentAndReleaseOnlyAggregateCreditAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(transport, kind)
                .ConfigureAwait(false);
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            harness.ServerFactory.Clock = clock;
            harness.ClientFactory.Clock = clock;
            using ProbeClientChannel client = await harness.ConnectProbeAsync(clock: clock).ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await using DataChannel retired = await client.DataChannels!.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel retiredPeer = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            await using DataChannel survivor = await client.DataChannels.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel survivorPeer = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            await retired.ResetAsync(DataChannelResult.Closed, harness.Token).ConfigureAwait(false);
            Assert.That(await survivorPeer.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await survivor.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            clock.Advance(TimeSpan.FromSeconds(1));
            int before = client.Recorder.Transport!.ReceivedChunks.Count;
            await client.SendAuthenticatedBodyAsync(
                TcpMessageType.Message, 0x80000000u | retired.ChannelId,
                [0, 0xFE, 1, 0], false, harness.Token).ConfigureAwait(false);
            FlowControl[] late =
            [
                new FlowProbe(FlowOperation.Ping, retired.ChannelId, 42),
                new FlowCredit(retired.ChannelId, 1, 1),
                new FlowOutcome(FlowOperation.Status, retired.ChannelId, FlowWireStatus.WouldBlock, 1),
                new FlowOutcome(FlowOperation.Reset, retired.ChannelId, FlowWireStatus.InvalidArgument, 0)
            ];
            for (int index = 0; index < late.Length; index++)
            {
                FlowRecord record = FlowControlCodec.Encode(late[index], 1, (uint)index + 100, new());
                byte[] body = UascFlowMessageCodec.Encode(FlowModel.ToWire(kind), retired.ChannelId,
                    in record, new(), out uint requestId).ToArray();
                await client.SendAuthenticatedBodyAsync(
                    TcpMessageType.Message, requestId, body, false, harness.Token).ConfigureAwait(false);
            }
            Assert.That(await client.DataChannels.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            while (client.Recorder.Transport.ReceivedChunks.Count < before + 2)
            {
                await Task.Delay(1, harness.Token).ConfigureAwait(false);
            }
            uint credit = 0;
            foreach (byte[] chunk in client.Recorder.Transport.ReceivedChunks.Skip(before))
            {
                (ByteString body, uint requestId, uint _) = await client.CanonicalPart30DecodeAsync(
                    chunk, false, harness.Token).ConfigureAwait(false);
                UascFlowMessage message = UascFlowMessageCodec.Decode(FlowModel.ToWire(kind),
                    DataChannelUascMessageKind.Msg, (byte)'F', requestId, body, new());
                Assert.That(message.Control!.Operation, Is.AnyOf(FlowOperation.Credit, FlowOperation.Pong));
                if (message.Control is FlowCredit returned)
                {
                    Assert.That(returned.Flow, Is.Zero);
                    Assert.That(returned.ChannelCredit, Is.Zero);
                    credit += returned.ConnectionCredit;
                }
            }
            Assert.That(credit, Is.EqualTo(2));
            Assert.That(survivorPeer.GetDiagnostics().MessagesReceived, Is.Zero);
            ByteString payload = UadpPayload(ByteString.FromHexString("deadbeef"));
            Assert.That(await survivor.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage delivered = (await survivorPeer.ReadAsync(harness.Token).ConfigureAwait(false))!;
            Assert.That(delivered.Payload, Is.EqualTo(payload));
            Assert.That(survivorPeer.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(remote.IsReady, Is.True);
        }

        /// <summary>
        /// Actual UASC ingress distinguishes recoverable typed fields from an unknown control opcode.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [CancelAfter(60000)]
        public async Task CanonicalMsgMalformedControlUsesStatusOrExactServerErrorAsync(bool recoverable)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            Assert.That(await incoming.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            FlowRecord record = FlowControlCodec.Encode(
                new FlowProbe(FlowOperation.Ping, outgoing.ChannelId, 42), 1, 91, new());
            byte[] body = UascFlowMessageCodec.Encode(FlowCarrier.SharedUasc, outgoing.ChannelId,
                in record, new(), out _).ToArray();
            if (recoverable)
            {
                body = [.. body.Take(body.Length - 1)];
            }
            else
            {
                body[10] = 255;
            }
            int before = client.Recorder.Transport!.ReceivedChunks.Count;
            await client.SendAuthenticatedBodyAsync(
                TcpMessageType.Message, 0, body, false, harness.Token).ConfigureAwait(false);
            while (client.Recorder.Transport.ReceivedChunks.Count <= before)
            {
                await Task.Delay(1, harness.Token).ConfigureAwait(false);
            }
            byte[] response = client.Recorder.Transport.ReceivedChunks.Skip(before).First();
            if (recoverable)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(response) & TcpMessageType.MessageTypeMask,
                    Is.EqualTo(TcpMessageType.Message));
                (ByteString Body, uint RequestId, uint Sequence) = await client.CanonicalPart30DecodeAsync(
                    response, false, harness.Token)
                    .ConfigureAwait(false);
                UascFlowMessage message = UascFlowMessageCodec.Decode(FlowCarrier.SharedUasc,
                    DataChannelUascMessageKind.Msg, (byte)'F', RequestId, Body, new());
                Assert.That(message.Control, Is.TypeOf<FlowOutcome>());
                var status = (FlowOutcome)message.Control!;
                Assert.Multiple(() =>
                {
                    Assert.That(status.Operation, Is.EqualTo(FlowOperation.Status));
                    Assert.That(status.Status, Is.EqualTo(FlowWireStatus.InvalidArgument));
                    Assert.That(status.RelatedOperation, Is.EqualTo(91));
                    Assert.That(status.Flow, Is.EqualTo(outgoing.ChannelId));
                    Assert.That(remote.IsReady, Is.True);
                    Assert.That(incoming.State, Is.EqualTo(DataChannelState.Open));
                });
            }
            else
            {
                Assert.Multiple(() =>
                {
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(response), Is.EqualTo(TcpMessageType.Error));
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(8)),
                        Is.EqualTo(StatusCodes.BadDecodingError));
                    Assert.That(harness.ServiceRequests, Is.Zero);
                });
            }
        }

        /// <summary>
        /// The control limit excludes its ten-byte prefix and changes from advisory to fatal at exactly one byte over.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc, false)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, true)]
        [TestCase(DataChannelCarrierKind.SharedUasc, false)]
        [TestCase(DataChannelCarrierKind.SharedUasc, true)]
        [CancelAfter(60000)]
        public async Task CanonicalMsgControlLimitUsesExactPlaintextBoundaryAsync(
            DataChannelCarrierKind kind, bool oversized)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(CarrierTransport.Tcp, kind)
                .ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task.WaitAsync(harness.Token)
                .ConfigureAwait(false);
            byte[] body = new byte[remote.MaxUascControlBodyBytes + (oversized ? 1 : 0)];
            body[1] = 255;
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(6), 91);
            body[10] = (byte)FlowOperation.Ping;
            var closed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            remote.Closed += result => closed.TrySetResult(result);
            int before = client.Recorder.Transport!.ReceivedChunks.Count;
            await client.SendAuthenticatedBodyAsync(
                TcpMessageType.Message, 0, body, false, harness.Token).ConfigureAwait(false);
            while (client.Recorder.Transport.ReceivedChunks.Count <= before)
            {
                await Task.Delay(1, harness.Token).ConfigureAwait(false);
            }
            byte[] response = client.Recorder.Transport.ReceivedChunks.Skip(before).First();
            if (oversized)
            {
                CanonicalPart30AssertError(response, StatusCodes.BadEncodingLimitsExceeded);
                await closed.Task.WaitAsync(harness.Token).ConfigureAwait(false);
                Assert.That(remote.IsReady, Is.False);
            }
            else
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(response),
                    Is.EqualTo(TcpMessageType.MessageFinal));
                (ByteString Body, uint RequestId, uint Sequence) = await client.CanonicalPart30DecodeAsync(
                    response, false, harness.Token)
                    .ConfigureAwait(false);
                UascFlowMessage message = UascFlowMessageCodec.Decode(FlowModel.ToWire(kind),
                    DataChannelUascMessageKind.Msg, (byte)'F', RequestId, Body, new());
                Assert.That(message.Control, Is.TypeOf<FlowOutcome>());
                var status = (FlowOutcome)message.Control!;
                Assert.Multiple(() =>
                {
                    Assert.That(status.Operation, Is.EqualTo(FlowOperation.Status));
                    Assert.That(status.Status, Is.EqualTo(FlowWireStatus.InvalidArgument));
                    Assert.That(status.Flow, Is.Zero);
                    Assert.That(status.RelatedOperation, Is.EqualTo(91));
                    Assert.That(remote.IsReady, Is.True,
                        "Trailing typed bytes at the size limit are recoverable, not an oversize context failure.");
                });
            }
            Assert.That(harness.ServiceRequests, Is.Zero);
        }

        /// <summary>
        /// A genuine encrypted C/A exchange abandons partial DATA without delivery, outcome or credit charge.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [CancelAfter(60000)]
        public async Task CanonicalMsgAbortDiscardsPartialDataWithoutChargingCreditAsync(DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(CarrierTransport.Tcp, kind)
                .ConfigureAwait(false);
            harness.ClientFactory.MaxMessageSize = 65536;
            harness.ServerFactory.MaxMessageSize = 65536;
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task.WaitAsync(harness.Token)
                .ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.MaxMessageSize = 65536;
            parameters.MaxReceiveBytes = 131072;
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            Assert.That(await incoming.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Lock gate = CanonicalPart30PrivateField<Lock>(remote, typeof(DataChannelContext), "m_gate");
            uint flowCreditBefore;
            uint connectionCreditBefore;
            lock (gate)
            {
                flowCreditBefore = incoming.StateData.ReceiveCredit;
                connectionCreditBefore = CanonicalPart30PrivateField<uint>(
                    remote, typeof(DataChannelContext), "m_receiveConnectionCredit");
            }
            int beforeRequests = client.WireChunks.Count;
            int beforeResponses = client.Recorder.Transport!.ReceivedChunks.Count;
            int capacity = client.GetMaxDataChannelRecordSize(DataChannelCarrierKind.WebTransport);
            byte[] partialAndAbort = new byte[capacity + 8];
            partialAndAbort[1] = 254;
            BinaryPrimitives.WriteUInt32LittleEndian(
                partialAndAbort.AsSpan(capacity), StatusCodes.BadRequestCancelledByClient.Code);
            BinaryPrimitives.WriteInt32LittleEndian(partialAndAbort.AsSpan(capacity + 4), -1);
            await client.SendAuthenticatedBodyAsync(
                TcpMessageType.Message, 0x80000000u | outgoing.ChannelId,
                partialAndAbort, abort: true, harness.Token).ConfigureAwait(false);
            Assert.That(await outgoing.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            lock (gate)
            {
                Assert.That(incoming.StateData.ReceiveCredit, Is.EqualTo(flowCreditBefore));
                Assert.That(CanonicalPart30PrivateField<uint>(
                    remote, typeof(DataChannelContext), "m_receiveConnectionCredit"),
                    Is.EqualTo(connectionCreditBefore));
            }
            byte[][] abandoned = [.. client.WireChunks.Skip(beforeRequests).Take(2)];
            Assert.That(abandoned, Has.Length.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(abandoned[0]),
                    Is.EqualTo(TcpMessageType.MessageIntermediate));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(abandoned[1]),
                    Is.EqualTo(TcpMessageType.Message | TcpMessageType.Abort));
                Assert.That(incoming.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(incoming.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(remote.IsReady, Is.True);
            });
            foreach (byte[] chunk in abandoned)
            {
                (ByteString Body, uint RequestId, uint Sequence) = await client.CanonicalPart30DecodeAsync(
                    chunk, true, harness.Token)
                    .ConfigureAwait(false);
                Assert.That(RequestId, Is.EqualTo(0x80000000u | outgoing.ChannelId));
            }
            foreach (byte[] chunk in client.Recorder.Transport.ReceivedChunks.Skip(beforeResponses))
            {
                if (!TcpMessageType.IsType(BinaryPrimitives.ReadUInt32LittleEndian(chunk), TcpMessageType.Message))
                {
                    continue;
                }
                (ByteString Body, uint RequestId, uint Sequence) = await client.CanonicalPart30DecodeAsync(
                    chunk, false, harness.Token).ConfigureAwait(false);
                UascFlowMessage message = UascFlowMessageCodec.Decode(FlowModel.ToWire(kind),
                    DataChannelUascMessageKind.Msg, (byte)'F', RequestId, Body, new());
                Assert.That(message.Control!.Operation, Is.EqualTo(FlowOperation.Pong),
                    "Only the following explicit PING is answered; abort produces no outcome or replenishment.");
            }
        }

        /// <summary>
        /// A correctly signed Service, CONTROL or CLO cannot interrupt an unfinished DATA message.
        /// </summary>
        [TestCase(DataChannelCarrierKind.SharedUasc, false, false)]
        [TestCase(DataChannelCarrierKind.SharedUasc, true, false)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, true, false)]
        [TestCase(DataChannelCarrierKind.SharedUasc, false, true)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, false, true)]
        [CancelAfter(60000)]
        public async Task SecuredPartialDataRejectsInterleavedMessagesBeforeDispatchAsync(
            DataChannelCarrierKind kind, bool control, bool close)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, kind, mode: MessageSecurityMode.Sign).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = DataChannelProtectionMode.Integrity;
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            Assert.That(await incoming.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            var closed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            remote.Closed += result => closed.TrySetResult(result);
            byte[] body;
            uint requestId;
            if (control)
            {
                FlowRecord record = FlowControlCodec.Encode(
                    new FlowProbe(FlowOperation.Ping, outgoing.ChannelId, 42), 1, 91, new());
                body = UascFlowMessageCodec.Encode(FlowModel.ToWire(kind), outgoing.ChannelId,
                    in record, new(), out requestId).ToArray();
            }
            else
            {
                using var encoder = new BinaryEncoder(ServiceMessageContext.Create(harness.Telemetry));
                if (close)
                {
                    encoder.EncodeMessage(new CloseSecureChannelRequest
                    {
                        RequestHeader = new RequestHeader { RequestHandle = 329 }
                    });
                }
                else
                {
                    encoder.EncodeMessage(Read(329));
                }
                body = encoder.CloseAndReturnBuffer()!;
                requestId = 99;
            }
            int beforeRequests = client.WireChunks.Count;
            int beforeResponses = client.Recorder.Transport!.ReceivedChunks.Count;
            await client.SendSignedPartAsync(TcpMessageType.MessageIntermediate,
                0x80000000u | outgoing.ChannelId, [0, 0xFE, 1, 0], harness.Token).ConfigureAwait(false);
            uint secondType = close ? TcpMessageType.Close | TcpMessageType.Final : TcpMessageType.MessageFinal;
            await client.SendSignedPartAsync(secondType, requestId, body, harness.Token)
                .ConfigureAwait(false);
            await closed.Task.WaitAsync(harness.Token).ConfigureAwait(false);
            while (client.Recorder.Transport.ReceivedChunks.Count <= beforeResponses)
            {
                await Task.Delay(1, harness.Token).ConfigureAwait(false);
            }
            CanonicalPart30AssertError(
                client.Recorder.Transport.ReceivedChunks.Skip(beforeResponses).First(),
                StatusCodes.BadDecodingError);
            byte[][] sent = [.. client.WireChunks.Skip(beforeRequests)];
            Assert.That(sent, Has.Length.EqualTo(2));
            while (harness.ServerFactory.Budget.Usage.BufferedBytes != 0)
            {
                await Task.Delay(1, harness.Token).ConfigureAwait(false);
            }
            Assert.Multiple(() =>
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(sent[0]),
                    Is.EqualTo(TcpMessageType.MessageIntermediate));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(sent[1]),
                    Is.EqualTo(secondType));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(sent[0].AsSpan(20)),
                    Is.EqualTo(0x80000000u | outgoing.ChannelId));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(sent[1].AsSpan(20)),
                    Is.EqualTo(requestId));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(sent[1].AsSpan(16)),
                    Is.EqualTo(BinaryPrimitives.ReadUInt32LittleEndian(sent[0].AsSpan(16)) + 1));
                Assert.That(remote.IsReady, Is.False);
                Assert.That(incoming.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(harness.ServiceRequests, Is.Zero);
                Assert.That(harness.ServerFactory.Budget.Usage.BufferedBytes, Is.Zero);
            });
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.RequestId)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.RequestId)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.Abort)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.Abort)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.Oversized)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.Oversized)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.TruncatedControl)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.TruncatedControl)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.EmptyTypeId)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.EmptyTypeId)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.TruncatedTypeId)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.TruncatedTypeId)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.NonzeroNamespaceTypeId)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.NonzeroNamespaceTypeId)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.UnknownTypeId)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.UnknownTypeId)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.ReversedTypeId)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.ReversedTypeId)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.LegacyFlc)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.LegacyFlc)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.LegacyFlo)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.LegacyFlo)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.OldStreamMessage)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.OldStreamMessage)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.Signature)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.Signature)]
        [CancelAfter(60000)]
        public async Task AuthenticatedInvalidCanonicalMsgCannotReachApplicationDispatchAsync(
            DataChannelCarrierKind kind,
            InvalidFlowChunk fault)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, kind).ConfigureAwait(false);
            harness.ClientFactory.MaxMessageSize = 65536;
            harness.ServerFactory.MaxMessageSize = 65536;
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.MaxMessageSize = 65536;
            parameters.MaxReceiveBytes = 131072;
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            Assert.That(await incoming.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            var closed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            remote.Closed += result => closed.TrySetResult(result);
            uint messageType = TcpMessageType.Message;
            uint requestId = 0;
            byte[] body = [0x00, 0xFE, 0x91, 0x80, 0x0C, 0x01];
            if (fault == InvalidFlowChunk.Oversized)
            {
                body = new byte[client.DataChannels!.MaxUascDataBodyBytes + 1];
                body[1] = 0xFE;
            }
            switch (fault)
            {
                case InvalidFlowChunk.RequestId:
                    requestId = 99;
                    break;
                case InvalidFlowChunk.TruncatedControl:
                    body = new byte[8];
                    body[1] = 0xFF;
                    break;
                case InvalidFlowChunk.EmptyTypeId:
                    body = [];
                    break;
                case InvalidFlowChunk.TruncatedTypeId:
                    body = [0];
                    break;
                case InvalidFlowChunk.NonzeroNamespaceTypeId:
                    body = [1, 0xFE];
                    break;
                case InvalidFlowChunk.UnknownTypeId:
                    body = [0, 0xFD];
                    break;
                case InvalidFlowChunk.ReversedTypeId:
                    body = [0xFE, 0];
                    break;
                case InvalidFlowChunk.LegacyFlc:
                    messageType = TcpMessageType.FlowControl;
                    break;
                case InvalidFlowChunk.LegacyFlo:
                    messageType = TcpMessageType.FlowData;
                    break;
                case InvalidFlowChunk.OldStreamMessage:
                    messageType = 0x00525453;
                    break;
                case InvalidFlowChunk.Signature:
                    client.Recorder.Transport!.CorruptNextSecuredChunk();
                    break;
            }
            int beforeResponses = client.Recorder.Transport!.ReceivedChunks.Count;
            try
            {
                await client.SendAuthenticatedBodyAsync(
                    messageType, requestId, body, fault == InvalidFlowChunk.Abort, harness.Token)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // Oversize admission can close the peer before the final secured chunk is sent.
            }
            DataChannelResult result = await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), harness.Token)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(DataChannelResult.Good));
                Assert.That(remote.IsReady, Is.False);
                Assert.That(remote.Channels.IsEmpty, Is.True);
                Assert.That(incoming.GetDiagnostics().MessagesReceived, Is.Zero,
                    "Invalid framing must not deliver DATA, including oversized multipart bodies.");
                Assert.That(harness.ServiceRequests, Is.Zero);
            });
            if (fault is InvalidFlowChunk.RequestId or InvalidFlowChunk.EmptyTypeId or
                InvalidFlowChunk.TruncatedTypeId or InvalidFlowChunk.NonzeroNamespaceTypeId or
                InvalidFlowChunk.UnknownTypeId or InvalidFlowChunk.ReversedTypeId)
            {
                while (client.Recorder.Transport.ReceivedChunks.Count <= beforeResponses)
                {
                    await Task.Delay(1, harness.Token).ConfigureAwait(false);
                }
                CanonicalPart30AssertError(
                    client.Recorder.Transport.ReceivedChunks.Skip(beforeResponses).First(),
                    StatusCodes.BadDecodingError);
            }
        }

        /// <summary>
        /// A sign-only endpoint advertisement does not opt either application into integrity-only contexts.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task DefaultConfidentialityRejectsAdvertisedSignOnlyEndpointAsync()
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            EndpointDescription endpoint = harness.Endpoint();
            endpoint.SecurityMode = MessageSecurityMode.Sign;
            ServiceResultException error = Assert.Throws<ServiceResultException>(() => harness.Open([endpoint]))!;
            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
                Assert.That(harness.ClientFactory.Contexts, Is.Empty);
                Assert.That(harness.ServerFactory.Contexts, Is.Empty);
                Assert.That(harness.ClientFactory.Authorizations, Is.Zero);
                Assert.That(harness.ServerFactory.Authorizations, Is.Zero);
                Assert.That(harness.ServiceHandles, Is.Empty);
            });
        }

        /// <summary>
        /// Explicit integrity-only permission cannot satisfy a flow's confidentiality requirement.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [CancelAfter(60000)]
        public async Task IntegrityOnlyCarrierCannotAdmitConfidentialFlowAsync(DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, kind, mode: MessageSecurityMode.Sign).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelException error = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                await client.DataChannels!.OpenAsync(Parameters(), cancellationToken: harness.Token)
                    .ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(error.Result, Is.EqualTo(DataChannelResult.SecurityFailure));
                Assert.That(client.DataChannels!.IsReady, Is.True);
                Assert.That(remote.IsReady, Is.True);
                Assert.That(client.DataChannels.Channels.IsEmpty, Is.True);
                Assert.That(remote.Channels.IsEmpty, Is.True);
                Assert.That(harness.ClientFactory.Authorizations, Is.Zero);
                Assert.That(harness.ServerFactory.Authorizations, Is.Zero);
            });
        }

        /// <summary>
        /// A minimum-sized secured DATA message exhausts the message-count bound before byte credit.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [CancelAfter(60000)]
        public async Task SecuredReceiveMessageCountResetsOnlyTheSaturatedFlowAsync(DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, kind).ConfigureAwait(false);
            harness.ServerFactory.MaxQueueMessages = 1;
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            ByteString payload = UadpPayload(ByteString.Empty);
            Assert.That(await outgoing.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage retained = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
            while (harness.ServerFactory.Budget.Usage.Messages > 1)
            {
                await Task.Delay(1, harness.Token).ConfigureAwait(false);
            }
            Assert.That(harness.ServerFactory.Budget.Usage.Messages, Is.EqualTo(1));
            Assert.That(await outgoing.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            while (incoming.State != DataChannelState.Faulted)
            {
                await Task.Delay(1, harness.Token).ConfigureAwait(false);
            }
            await incoming.StateData.Released.Task.WaitAsync(harness.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(incoming.GetStatus().Result, Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(incoming.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(harness.ServerFactory.Budget.Usage.Messages, Is.EqualTo(1));
                Assert.That(retained.Payload, Is.EqualTo(payload));
                Assert.That(remote.IsReady, Is.True);
            });
            Assert.That(await client.DataChannels.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            if (kind == DataChannelCarrierKind.SharedUasc)
            {
                IServiceResponse response = await client.SendRequestAsync(Read(103), 10000, harness.Token)
                    .ConfigureAwait(false);
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(103));
            }
            retained.Dispose();
            while (harness.ServerFactory.Budget.Usage.Messages != 0 ||
                harness.ServerFactory.Budget.Usage.BufferedBytes != 0)
            {
                await Task.Delay(1, harness.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Both real UASC receivers reject tampered signed or encrypted DATA before application dispatch.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc, MessageSecurityMode.Sign, false)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, MessageSecurityMode.Sign, true)]
        [TestCase(DataChannelCarrierKind.SharedUasc, MessageSecurityMode.Sign, false)]
        [TestCase(DataChannelCarrierKind.SharedUasc, MessageSecurityMode.Sign, true)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, MessageSecurityMode.SignAndEncrypt, false)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, MessageSecurityMode.SignAndEncrypt, true)]
        [TestCase(DataChannelCarrierKind.SharedUasc, MessageSecurityMode.SignAndEncrypt, false)]
        [TestCase(DataChannelCarrierKind.SharedUasc, MessageSecurityMode.SignAndEncrypt, true)]
        [CancelAfter(60000)]
        public async Task TamperedSecuredDataClosesTheActualReceivingContextAsync(
            DataChannelCarrierKind kind,
            MessageSecurityMode mode,
            bool receivingClient)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, kind, mode: mode, recordServerTransport: true).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext local = client.DataChannels!;
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = mode == MessageSecurityMode.Sign
                ? DataChannelProtectionMode.Integrity : DataChannelProtectionMode.Confidential;
            await using DataChannel outgoing = await local.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            Assert.That(await incoming.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await outgoing.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            DataChannelContext receiver = receivingClient ? local : remote;
            DataChannel receivedFlow = receivingClient ? outgoing : incoming;
            DataChannel sender = receivingClient ? incoming : outgoing;
            RecordingTransport transport = receivingClient ? harness.ServerTransport! : client.Recorder.Transport!;
            var closed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            receiver.Closed += result => closed.TrySetResult(result);
            transport.CorruptNextSecuredChunk();
            await sender.SendAsync(UadpPayload(ByteString.FromHexString("deadbeef")),
                cancellationToken: harness.Token).ConfigureAwait(false);
            DataChannelResult result = await closed.Task.WaitAsync(harness.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(DataChannelResult.Good));
                Assert.That(receiver.IsReady, Is.False);
                Assert.That(receivedFlow.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(receivedFlow.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(harness.ServiceRequests, Is.Zero);
            });
        }

        [Test]
        [CancelAfter(60000)]
        public async Task SharedServicesBeforeProtectedHelloAreRejectedAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync(confirmProfile: false)
                .ConfigureAwait(false);

            ServiceResultException error = (await Assert.CatchAsync<ServiceResultException>(async () =>
                await client.SendRequestAsync(Read(1), 10000, harness.Token).ConfigureAwait(false))
                    .ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.Not.EqualTo(StatusCodes.Good));
            Assert.That(harness.ServiceRequests, Is.Zero);
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [CancelAfter(60000)]
        public async Task ListenerRefusesAnActualUnsecuredOpnOnAFlowResourceAsync(DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(CarrierTransport.Tcp, kind)
                .ConfigureAwait(false);

            ServiceResultException error = (await Assert.CatchAsync<ServiceResultException>(async () =>
            {
                using ProbeClientChannel client = await harness.ConnectProbeAsync(
                    confirmProfile: false, requestedMode: MessageSecurityMode.None).ConfigureAwait(false);
            }).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.Not.EqualTo(StatusCodes.Good));
            Assert.That(harness.ServerFactory.Contexts, Is.Empty);
            Assert.That(harness.ServiceRequests, Is.Zero);
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [CancelAfter(60000)]
        public async Task OmittedHelloEndpointCannotBypassDataProfileSelectionAsync(DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(CarrierTransport.Tcp, kind)
                .ConfigureAwait(false);

            uint firstMessage = await harness.SendHelloWithoutEndpointAsync().ConfigureAwait(false);

            Assert.That(firstMessage, Is.AnyOf(0u, TcpMessageType.Error),
                "An omitted endpoint must be refused, never acknowledged into ordinary Service dispatch.");
            Assert.That(harness.ServerFactory.Contexts, Is.Empty);
            Assert.That(harness.ServiceRequests, Is.Zero);
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task ConfiguredContextRevalidatesPeerTrustWithoutDependingOnASessionAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext context = connection.Context;
            var closed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.Closed += result => closed.TrySetResult(result);
            Guid originalContext = context.Identity.ContextId;

            await harness.RemoveServerTrustAsync(TrustListIdentifier.Users).ConfigureAwait(false);
            Assert.That(await context.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(context.Identity.ContextId, Is.EqualTo(originalContext));
            Assert.That(context.IsReady, Is.True,
                "User/control trust is independent of the application data context.");

            await harness.RemoveServerTrustAsync(TrustListIdentifier.Peers).ConfigureAwait(false);
            await closed.Task.WaitAsync(harness.Token).ConfigureAwait(false);

            Assert.That(context.IsReady, Is.False);
            Assert.That(harness.ServiceRequests, Is.Zero);
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.DedicatedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task CertificateExpiryRetiresAnEmptyContextBeforeItsSecurityTokenExpiresAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport,
                kind,
                certificateLifetime: TimeSpan.FromMinutes(2),
                tokenLifetime: 600000).ConfigureAwait(false);
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            using ProbeClientChannel client = await harness.ConnectProbeAsync(clock: clock).ConfigureAwait(false);
            DataChannelContext context = client.DataChannels!;
            Assert.That(client.CurrentToken!.Lifetime, Is.EqualTo(600000));
            Assert.That(context.Channels.IsEmpty, Is.True);
            var closed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.Closed += result => closed.TrySetResult(result);

            clock.Advance(TimeSpan.FromMinutes(3));
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), harness.Token).ConfigureAwait(false);

            Assert.That(context.IsReady, Is.False);
            Assert.That(harness.ServiceRequests, Is.Zero);
            Assert.That(harness.ClientFactory.Authorizations, Is.Zero,
                "Certificate lifetime is a transport obligation even before resource admission or a Session exists.");
        }

        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
        [TestCase(CarrierTransport.ManagedQuic, DataChannelCarrierKind.QuicUasc)]
        [TestCase(CarrierTransport.ManagedWebTransport, DataChannelCarrierKind.WebTransportUasc)]
#endif
        [CancelAfter(60000)]
        public async Task SharedServicesAndFlowsRemainOrderedAcrossRealTokenRenewalAsync(
            CarrierTransport transport, DataChannelCarrierKind kind)
        {
            MessageSecurityMode mode = transport == CarrierTransport.Tcp
                ? MessageSecurityMode.Sign : MessageSecurityMode.SignAndEncrypt;
            await using SocketHarness harness = await SocketHarness.StartAsync(
                transport, kind, mode: mode).ConfigureAwait(false);
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            using ProbeClientChannel client = await harness.ConnectProbeAsync(clock: clock).ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = mode == MessageSecurityMode.Sign
                ? DataChannelProtectionMode.Integrity : DataChannelProtectionMode.Confidential;
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            uint originalToken = client.CurrentToken!.TokenId;
            var renewed = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.TokenActivatedCallback = (current, previous) =>
            {
                if (current != null && previous != null)
                {
                    renewed.TrySetResult(current.TokenId);
                }
            };
            IServiceResponse before = await client.SendRequestAsync(Read(101), 10000, harness.Token)
                .ConfigureAwait(false);
            Assert.That(before.ResponseHeader.RequestHandle, Is.EqualTo(101));
            clock.Advance(TimeSpan.FromSeconds(50));
            uint renewedToken = await renewed.Task.WaitAsync(harness.Token).ConfigureAwait(false);
            Assert.That(renewedToken, Is.Not.EqualTo(originalToken));

            ByteString payload = UadpPayload(new ByteString(new byte[] { 4, 5, 6 }));
            Assert.That(
                await outgoing.SendAsync(payload, cancellationToken: harness.Token)
                    .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage message =
                (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
            IServiceResponse after = await client.SendRequestAsync(Read(102), 10000, harness.Token)
                .ConfigureAwait(false);
            Assert.That(message.Payload.ToArray(), Is.EqualTo(payload.ToArray()));
            Assert.That(after.ResponseHeader.RequestHandle, Is.EqualTo(102));
            Assert.That(client.DataChannels.IsReady, Is.True);
            Assert.That(remote.IsReady, Is.True);
            await client.CloseAsync(1000, harness.Token).ConfigureAwait(false);
            Assert.That(
                BinaryPrimitives.ReadUInt32LittleEndian(client.WireChunks.Last()) & TcpMessageType.MessageTypeMask,
                Is.EqualTo(TcpMessageType.Close),
                string.Join(" ", client.WireChunks.Select(chunk => Encoding.ASCII.GetString(chunk, 0, 4))));
            if (mode == MessageSecurityMode.Sign)
            {
                // Encrypted sequence headers are verified by the actual peer, not read as plaintext here.
                AssertWireSequence(client.WireChunks);
            }
        }

        /// <summary>
        /// Service and flow producers still share the real single writer after removal of the old upgrade API.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task ConcurrentClientRequestsAndStreamRecordsReachWireInSequenceOrderAsync()
        {
            const int kMessagesPerKind = 2000;
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp,
                DataChannelCarrierKind.SharedUasc,
                mode: MessageSecurityMode.Sign).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = DataChannelProtectionMode.Integrity;
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);

            async Task ServicesAsync()
            {
                for (uint index = 1; index <= kMessagesPerKind; index++)
                {
                    IServiceResponse response = await client.SendRequestAsync(Read(index), 10000, harness.Token)
                        .ConfigureAwait(false);
                    Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(index));
                }
            }
            async Task FlowsAsync()
            {
                for (int index = 0; index < kMessagesPerKind; index++)
                {
                    ByteString payload = UadpPayload(new ByteString(BitConverter.GetBytes(index)));
                    Assert.That(await outgoing.SendAsync(payload, cancellationToken: harness.Token)
                        .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                    using DataChannelMessage message = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
                    Assert.That(message.Payload, Is.EqualTo(payload));
                }
            }

            await Task.WhenAll(ServicesAsync(), FlowsAsync()).WaitAsync(harness.Token).ConfigureAwait(false);

            Assert.That(harness.ServiceRequests, Is.EqualTo(kMessagesPerKind));
            Assert.That(client.WireChunks.Count(chunk =>
                TcpMessageType.IsType(BinaryPrimitives.ReadUInt32LittleEndian(chunk), TcpMessageType.Message) &&
                BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(20)) is > 0 and < 0x80000000),
                Is.EqualTo(kMessagesPerKind));
            Assert.That(CountDataChunks(client.WireChunks), Is.EqualTo(kMessagesPerKind));
            await client.CloseAsync(1000, harness.Token).ConfigureAwait(false);
            AssertWireSequence(client.WireChunks);
        }

        [Test]
        [CancelAfter(60000)]
        public async Task ExpiryBehindAServiceWriteIsDecidedBeforeSecurityCommitmentAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc, mode: MessageSecurityMode.Sign)
                .ConfigureAwait(false);
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            harness.ClientFactory.Clock = clock;
            var pool = new OwnershipArrayPool();
            using ProbeClientChannel client = await harness.ConnectProbeAsync(clock: clock, pool: pool)
                .ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = DataChannelProtectionMode.Integrity;
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            Assert.That(await client.DataChannels.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            WriteBarrier barrier = client.Recorder.Transport!.PauseNext(TcpMessageType.Message, dataOnly: false);
            Task<IServiceResponse> service = client.SendRequestAsync(Read(21), 10000, harness.Token).AsTask();
            await barrier.Entered.Task.WaitAsync(harness.Token).ConfigureAwait(false);
            int before = pool.Outstanding;
            int dataBefore = CountDataChunks(client.WireChunks);
            Task<DataChannelResult> expiring = outgoing.SendAsync(
                UadpPayload(new ByteString(new byte[] { 0xCA, 0xFE })),
                TimeSpan.FromSeconds(1), harness.Token).AsTask();
            try
            {
                while (pool.Outstanding <= before)
                {
                    await Task.Delay(1, harness.Token).ConfigureAwait(false);
                }
                clock.Advance(TimeSpan.FromSeconds(2));
                barrier.Release();
                Assert.That((await service.ConfigureAwait(false)).ResponseHeader.RequestHandle, Is.EqualTo(21));
                Assert.That(await expiring.WaitAsync(harness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Timeout));
                Assert.That(CountDataChunks(client.WireChunks),
                    Is.EqualTo(dataBefore));
                Assert.That(await outgoing.SendAsync(
                    UadpPayload(new ByteString(new byte[] { 1 })), cancellationToken: harness.Token)
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                using DataChannelMessage delivered = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
                Assert.That(delivered.Payload.ToArray(),
                    Is.EqualTo(UadpPayload(new ByteString(new byte[] { 1 })).ToArray()));
                AssertWireSequence(client.WireChunks);
                Assert.That(pool.DuplicateReturns, Is.Zero);
            }
            finally
            {
                barrier.Release();
                await service.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A verified shared-channel Service response restarts the local idle PING deadline.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task SharedServiceTrafficRestartsTheLocalIdlePingDeadlineAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp,
                DataChannelCarrierKind.SharedUasc,
                mode: MessageSecurityMode.Sign).ConfigureAwait(false);
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            harness.ClientFactory.Clock = clock;
            using ProbeClientChannel client = await harness.ConnectProbeAsync(clock: clock).ConfigureAwait(false);
            DataChannelContext context = client.DataChannels!;
            WriteBarrier pingBarrier = client.Recorder.Transport!.PauseNextControl(FlowOperation.Ping);

            clock.Advance(TimeSpan.FromSeconds(4));
            IServiceResponse response = await client.SendRequestAsync(
                Read(21), 10000, harness.Token).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(21));

            clock.Advance(TimeSpan.FromSeconds(4));
            Assert.That(pingBarrier.Entered.Task.IsCompleted, Is.False,
                "The original idle deadline must not fire after verified Service traffic.");

            clock.Advance(TimeSpan.FromSeconds(1));
            await pingBarrier.Entered.Task.WaitAsync(harness.Token).ConfigureAwait(false);
            pingBarrier.Release();

            await Task.Delay(50, harness.Token).ConfigureAwait(false);
            Assert.That(context.IsReady, Is.True);
        }

        /// <summary>
        /// A live authenticated MSG blocked before socket submission closes its data context at PingTimeout.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [CancelAfter(60000)]
        public async Task UascSendWithoutSocketProgressClosesContextAtPingTimeoutAsync(
            DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, kind, mode: MessageSecurityMode.Sign).ConfigureAwait(false);
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            harness.ClientFactory.Clock = clock;
            using ProbeClientChannel client = await harness.ConnectProbeAsync(clock: clock).ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelContext context = client.DataChannels!;
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = DataChannelProtectionMode.Integrity;
            await using DataChannel outgoing = await context.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            var closed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.Closed += result => closed.TrySetResult(result);

            WriteBarrier barrier = client.Recorder.Transport!.PauseNext(TcpMessageType.Message, dataOnly: true);
            try
            {
                Task<DataChannelResult> send = outgoing.SendAsync(
                    UadpPayload(new ByteString(new byte[] { 1, 2, 3 })),
                    cancellationToken: harness.Token).AsTask();
                await barrier.Entered.Task.WaitAsync(harness.Token).ConfigureAwait(false);

                clock.Advance(TimeSpan.FromSeconds(10));

                Assert.That(await closed.Task.WaitAsync(harness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Timeout));
                Assert.Multiple(() =>
                {
                    Assert.That(context.IsReady, Is.False);
                    Assert.That(outgoing.State, Is.EqualTo(DataChannelState.Faulted));
                    Assert.That(outgoing.GetStatus().Result, Is.EqualTo(DataChannelResult.Timeout));
                });
                barrier.Release();
                await send.WaitAsync(harness.Token).ConfigureAwait(false);
                if (kind == DataChannelCarrierKind.SharedUasc)
                {
                    ServiceResultException error = (await Assert.CatchAsync<ServiceResultException>(async () =>
                        await client.SendRequestAsync(Read(22), 10000, harness.Token).ConfigureAwait(false))
                            .ConfigureAwait(false))!;
                    Assert.That(error.StatusCode, Is.Not.EqualTo(StatusCodes.Good));
                }
            }
            finally
            {
                barrier.Release();
            }
        }

        /// <summary>
        /// A secured OPEN whose ACCEPT is committed but lost cannot survive physical context loss.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task LostSecuredOpenResponseClosesContextAndFailsOpenAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp,
                DataChannelCarrierKind.SharedUasc,
                mode: MessageSecurityMode.Sign,
                recordServerTransport: true).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext local = client.DataChannels!;
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            Task<DataChannel?> accepting = remote.AcceptAsync(harness.Token).AsTask();
            WriteBarrier response = harness.ServerTransport!.PauseNextControl(FlowOperation.Accept);
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = DataChannelProtectionMode.Integrity;
            Task<DataChannel> opening = local.OpenAsync(
                parameters, cancellationToken: harness.Token).AsTask();

            try
            {
                await response.Entered.Task.WaitAsync(harness.Token).ConfigureAwait(false);
                Assert.That(response.Buffer, Is.Not.Null,
                    "The ACCEPT must be secured and committed before the socket write is paused.");
                Assert.That(local.IsReady, Is.True);
                Assert.That(remote.IsReady, Is.True);

                client.Recorder.Transport!.Close();
                response.Release();

                DataChannelException failure = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await opening.WaitAsync(harness.Token).ConfigureAwait(false)).ConfigureAwait(false))!;
                Assert.That(failure.Result, Is.EqualTo(DataChannelResult.Closed));
                Assert.Multiple(() =>
                {
                    Assert.That(local.IsReady, Is.False);
                    Assert.That(remote.IsReady, Is.False);
                });
                Assert.That(await accepting.WaitAsync(harness.Token).ConfigureAwait(false), Is.Null);
            }
            finally
            {
                response.Release();
            }
        }

        /// <summary>
        /// A secured revision whose ACCEPT-UPDATE is committed but lost cannot survive context loss.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task LostSecuredRevisionResponseClosesContextAndFailsUpdateAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp,
                DataChannelCarrierKind.SharedUasc,
                mode: MessageSecurityMode.Sign,
                recordServerTransport: true).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext local = client.DataChannels!;
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            Task<DataChannel?> accepting = remote.AcceptAsync(harness.Token).AsTask();
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = DataChannelProtectionMode.Integrity;
            await using DataChannel outgoing = await local.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = (await accepting.WaitAsync(harness.Token).ConfigureAwait(false))!;

            WriteBarrier response = harness.ServerTransport!.PauseNextControl(FlowOperation.AcceptUpdate);
            DataChannelParametersDataType proposal = outgoing.Parameters;
            proposal.MaxMessageSize = 128;
            Task<DataChannelResult> updating = outgoing.UpdateAsync(
                proposal, 1, harness.Token).AsTask();

            try
            {
                await response.Entered.Task.WaitAsync(harness.Token).ConfigureAwait(false);
                Assert.That(response.Buffer, Is.Not.Null,
                    "ACCEPT-UPDATE must be secured and committed before the socket write is paused.");
                Assert.That(outgoing.Revision, Is.EqualTo(1u));

                client.Recorder.Transport!.Close();
                response.Release();

                Assert.That(await updating.WaitAsync(harness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Closed));
                Assert.Multiple(() =>
                {
                    Assert.That(local.IsReady, Is.False);
                    Assert.That(remote.IsReady, Is.False);
                    Assert.That(outgoing.State, Is.EqualTo(DataChannelState.Faulted));
                    Assert.That(incoming.State, Is.EqualTo(DataChannelState.Faulted));
                    Assert.That(outgoing.Revision, Is.EqualTo(1u));
                });
            }
            finally
            {
                response.Release();
            }
        }

        /// <summary>
        /// A secured READY lost after ACCEPT prevents either endpoint from continuing that flow.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task LostSecuredReadyClosesContextAndFailsOpenAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp,
                DataChannelCarrierKind.SharedUasc,
                mode: MessageSecurityMode.Sign).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext local = client.DataChannels!;
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            Task<DataChannel?> accepting = remote.AcceptAsync(harness.Token).AsTask();
            WriteBarrier ready = client.Recorder.Transport!.PauseNextControl(FlowOperation.Ready);
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = DataChannelProtectionMode.Integrity;
            Task<DataChannel> opening = local.OpenAsync(
                parameters, cancellationToken: harness.Token).AsTask();

            try
            {
                await ready.Entered.Task.WaitAsync(harness.Token).ConfigureAwait(false);
                Assert.That(ready.Buffer, Is.Not.Null,
                    "READY must be secured and committed before the socket write is paused.");
                Assert.That(local.IsReady, Is.True);
                Assert.That(remote.IsReady, Is.True);
                Assert.That(opening.IsCompleted, Is.False);

                client.Recorder.Transport.Close();
                ready.Release();

                DataChannelException failure = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await opening.WaitAsync(harness.Token).ConfigureAwait(false)).ConfigureAwait(false))!;
                Assert.That(failure.Result, Is.EqualTo(DataChannelResult.Closed));
                Assert.That(await accepting.WaitAsync(harness.Token).ConfigureAwait(false), Is.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(local.IsReady, Is.False);
                    Assert.That(remote.IsReady, Is.False);
                });
            }
            finally
            {
                ready.Release();
            }
        }

        /// <summary>
        /// A secured COMMIT lost at the transport boundary faults the flow with no revision resumption.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task LostSecuredRevisionCommitClosesContextAndRequiresFreshAdmissionAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp,
                DataChannelCarrierKind.SharedUasc,
                mode: MessageSecurityMode.Sign).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext local = client.DataChannels!;
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            Task<DataChannel?> accepting = remote.AcceptAsync(harness.Token).AsTask();
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = DataChannelProtectionMode.Integrity;
            await using DataChannel outgoing = await local.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = (await accepting.WaitAsync(harness.Token).ConfigureAwait(false))!;

            WriteBarrier commit = client.Recorder.Transport!.PauseNextControl(FlowOperation.Commit);
            DataChannelParametersDataType proposal = outgoing.Parameters;
            proposal.MaxMessageSize = 128;
            Task<DataChannelResult> updating = outgoing.UpdateAsync(
                proposal, 1, harness.Token).AsTask();

            try
            {
                await commit.Entered.Task.WaitAsync(harness.Token).ConfigureAwait(false);
                Assert.That(commit.Buffer, Is.Not.Null,
                    "COMMIT must be secured and committed before the socket write is paused.");
                Assert.That(outgoing.Revision, Is.EqualTo(2u),
                    "The local revision changes at the ordered UASC writer commitment boundary.");
                Assert.That(incoming.Revision, Is.EqualTo(1u),
                    "The remote revision cannot change before receiving COMMIT.");

                client.Recorder.Transport.Close();
                commit.Release();

                Assert.That(await updating.WaitAsync(harness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Closed));
                DataChannelException? remoteFailure = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await incoming.ReadAsync(harness.Token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(remoteFailure!.Result, Is.EqualTo(DataChannelResult.Closed));
                Assert.Multiple(() =>
                {
                    Assert.That(local.IsReady, Is.False);
                    Assert.That(remote.IsReady, Is.False);
                    Assert.That(outgoing.State, Is.EqualTo(DataChannelState.Faulted));
                    Assert.That(incoming.State, Is.EqualTo(DataChannelState.Faulted));
                    Assert.That(outgoing.Revision, Is.EqualTo(2u));
                    Assert.That(incoming.Revision, Is.EqualTo(1u));
                });
            }
            finally
            {
                commit.Release();
            }
        }

        /// <summary>
        /// A secured revision ACK lost at the transport boundary faults the contexts and update.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task LostSecuredRevisionAckClosesContextAndFailsUpdateAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp,
                DataChannelCarrierKind.SharedUasc,
                mode: MessageSecurityMode.Sign,
                recordServerTransport: true).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext local = client.DataChannels!;
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            Task<DataChannel?> accepting = remote.AcceptAsync(harness.Token).AsTask();
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = DataChannelProtectionMode.Integrity;
            await using DataChannel outgoing = await local.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = (await accepting.WaitAsync(harness.Token).ConfigureAwait(false))!;
            var localClosed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var remoteClosed = new TaskCompletionSource<DataChannelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            local.Closed += result => localClosed.TrySetResult(result);
            remote.Closed += result => remoteClosed.TrySetResult(result);

            WriteBarrier acknowledgment = harness.ServerTransport!.PauseNextControl(FlowOperation.Ack);
            DataChannelParametersDataType proposal = outgoing.Parameters;
            proposal.MaxMessageSize = 128;
            Task<DataChannelResult> updating = outgoing.UpdateAsync(
                proposal, 1, harness.Token).AsTask();

            try
            {
                await acknowledgment.Entered.Task.WaitAsync(harness.Token).ConfigureAwait(false);
                Assert.That(acknowledgment.Buffer, Is.Not.Null,
                    "ACK must be secured and committed before the socket write is paused.");
                Assert.That(updating.IsCompleted, Is.False);

                client.Recorder.Transport!.Close();
                acknowledgment.Release();

                Assert.That(await updating.WaitAsync(harness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Closed));
                await localClosed.Task.WaitAsync(harness.Token).ConfigureAwait(false);
                await remoteClosed.Task.WaitAsync(harness.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(local.IsReady, Is.False);
                    Assert.That(remote.IsReady, Is.False);
                    Assert.That(outgoing.State, Is.EqualTo(DataChannelState.Faulted));
                    Assert.That(incoming.State, Is.EqualTo(DataChannelState.Faulted));
                });
            }
            finally
            {
                acknowledgment.Release();
            }
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// A lost QUIC association during secured READY retires both contexts and the pending OPEN.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task ManagedQuicLossDuringSecuredReadyFailsOpenAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.ManagedQuic,
                DataChannelCarrierKind.QuicUasc).ConfigureAwait(false);
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext local = client.DataChannels!;
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            Task<DataChannel?> accepting = remote.AcceptAsync(harness.Token).AsTask();
            WriteBarrier ready = client.Recorder.Transport!.PauseNext(
                TcpMessageType.Message, dataOnly: false, skipMatches: 1);
            DataChannelParametersDataType parameters = Parameters();
            Task<DataChannel> opening = local.OpenAsync(
                parameters, cancellationToken: harness.Token).AsTask();

            try
            {
                await ready.Entered.Task.WaitAsync(harness.Token).ConfigureAwait(false);
                Assert.That(ready.Buffer, Is.Not.Null);
                Assert.That(opening.IsCompleted, Is.False);

                await harness.CloseManagedClientAsync().ConfigureAwait(false);
                ready.Release();

                DataChannelException failure = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await opening.WaitAsync(harness.Token).ConfigureAwait(false)).ConfigureAwait(false))!;
                Assert.That(failure.Result, Is.EqualTo(DataChannelResult.Closed));
                Assert.That(await accepting.WaitAsync(harness.Token).ConfigureAwait(false), Is.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(local.IsReady, Is.False);
                    Assert.That(remote.IsReady, Is.False);
                });
            }
            finally
            {
                ready.Release();
            }
        }
#endif

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [CancelAfter(60000)]
        public async Task CancellationAfterCommitRetainsTheActualSocketBufferAndSequenceAsync(
            DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, kind, mode: MessageSecurityMode.Sign).ConfigureAwait(false);
            var pool = new OwnershipArrayPool();
            using ProbeClientChannel client = await harness.ConnectProbeAsync(pool: pool).ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = DataChannelProtectionMode.Integrity;
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            WriteBarrier barrier = client.Recorder.Transport!.PauseNext(TcpMessageType.Message, dataOnly: true);
            using var cancellation = new CancellationTokenSource();
            ByteString payload = UadpPayload(new ByteString(new byte[] { 0x12, 0x34, 0x56, 0x78 }));
            Task<DataChannelResult> sending = outgoing.SendAsync(payload, cancellationToken: cancellation.Token)
                .AsTask();
            try
            {
                await barrier.Entered.Task.WaitAsync(harness.Token).ConfigureAwait(false);
                Assert.That(barrier.Buffer, Is.Not.Null);
                Assert.That(pool.Owns(barrier.Buffer!), Is.True);
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await sending.WaitAsync(harness.Token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(pool.Owns(barrier.Buffer!), Is.True,
                    "Abandoning the caller wait must not return a numbered socket buffer to the pool.");
                Task<IServiceResponse>? following = kind == DataChannelCarrierKind.SharedUasc
                    ? client.SendRequestAsync(Read(22), 10000, harness.Token).AsTask()
                    : null;
                barrier.Release();
                using DataChannelMessage received = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
                Assert.That(received.Payload, Is.EqualTo(payload),
                    "A premature return would poison/reuse the real buffer before its socket write.");
                if (following != null)
                {
                    Assert.That((await following.ConfigureAwait(false)).ResponseHeader.RequestHandle, Is.EqualTo(22));
                }
                AssertWireSequence(client.WireChunks);
                Assert.That(pool.DuplicateReturns, Is.Zero);
            }
            finally
            {
                barrier.Release();
            }
        }

        private static int CountDataChunks(IEnumerable<byte[]> chunks)
        {
            return chunks.Count(chunk =>
                TcpMessageType.IsType(BinaryPrimitives.ReadUInt32LittleEndian(chunk), TcpMessageType.Message) &&
                chunk.Length >= 26 &&
                chunk[24] == 0 &&
                chunk[25] == 254);
        }

        private static void AssertWireSequence(IEnumerable<byte[]> chunks)
        {
            uint? previous = null;
            uint asymmetricSincePrevious = 0;
            foreach (byte[] chunk in chunks)
            {
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(chunk) & TcpMessageType.MessageTypeMask;
                if (type == TcpMessageType.Open)
                {
                    asymmetricSincePrevious++;
                    continue;
                }
                if (type is not (TcpMessageType.Message or TcpMessageType.FlowData or
                    TcpMessageType.FlowControl or TcpMessageType.Close))
                {
                    continue;
                }
                uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(16));
                if (previous.HasValue)
                {
                    Assert.That(sequence, Is.EqualTo(previous.Value + asymmetricSincePrevious + 1),
                        "No Service, flow, renewal or cancelled producer may create a numbered gap or reorder.");
                }
                previous = sequence;
                asymmetricSincePrevious = 0;
            }
            Assert.That(previous.HasValue, Is.True, "The assertion must observe actual secured records.");
        }

        /// <summary>
        /// Authenticated UASC chunks with invalid flow framing or carrier metadata.
        /// </summary>
        public enum InvalidFlowChunk
        {
            /// <summary>
            /// A wrong carrier RequestId.
            /// </summary>
            RequestId,

            /// <summary>
            /// CONTROL without a recoverable OperationId and opcode.
            /// </summary>
            TruncatedControl,

            /// <summary>
            /// A channel MSG with no TypeId bytes.
            /// </summary>
            EmptyTypeId,

            /// <summary>
            /// A channel MSG with only one TypeId byte.
            /// </summary>
            TruncatedTypeId,

            /// <summary>
            /// A channel MSG with a nonzero namespace index in its TypeId.
            /// </summary>
            NonzeroNamespaceTypeId,

            /// <summary>
            /// A channel MSG with an unknown TypeId.
            /// </summary>
            UnknownTypeId,

            /// <summary>
            /// A channel MSG with the canonical TypeId bytes reversed.
            /// </summary>
            ReversedTypeId,

            /// <summary>
            /// The superseded FLC message identifier.
            /// </summary>
            LegacyFlc,

            /// <summary>
            /// An abort without an incomplete message and bounded Part 6 abort body.
            /// </summary>
            Abort,

            /// <summary>
            /// A multipart body beyond the admitted complete-message bound.
            /// </summary>
            Oversized,

            /// <summary>
            /// The superseded FLO message identifier.
            /// </summary>
            LegacyFlo,

            /// <summary>
            /// The removed STR message type on either UASC profile.
            /// </summary>
            OldStreamMessage,

            /// <summary>
            /// A tampered authenticated-security footer.
            /// </summary>
            Signature
        }

        /// <summary>
        /// The real listening transport, independently of dedicated/shared UASC mode.
        /// </summary>
        public enum CarrierTransport
        {
#if NET8_0_OR_GREATER
            /// <summary>
            /// Real managed QUIC with the Part 31 UASC baseline profile and ALPN.
            /// </summary>
            ManagedQuic,

            /// <summary>
            /// Managed HTTP/3 capsules carrying the Part 32 UASC baseline.
            /// </summary>
            ManagedWebTransport,
#endif
            /// <summary>
            /// Raw TCP sockets.
            /// </summary>
            Tcp,

            /// <summary>
            /// Externally established streams feeding the shared UACP/UASC adapter on both endpoints.
            /// </summary>
            ReliableStream,

            /// <summary>
            /// The same external-stream UASC adapter without binding a socket or loading a native library.
            /// </summary>
            InMemoryReliableStream,

            /// <summary>
            /// Kestrel's TCP connection pipeline.
            /// </summary>
            KestrelTcp,

            /// <summary>
            /// Binary opcua+uacp WebSockets over TLS.
            /// </summary>
            Wss
        }

        private static DataChannelParametersDataType Parameters()
        {
            return new DataChannelParametersDataType
            {
                ResourceUri = SocketHarness.Resource,
                Protocol = DataChannelProfiles.UadpPayload,
                ContentType = "application/opcua+uadp",
                Direction = DataChannelDirection.Duplex,
                Delivery = DataChannelDeliveryMode.Ordered,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = 4096,
                MaxReceiveBytes = 8192,
                MaxRate = 0,
                Priority = 128,
                AllowQueueExpiry = true
            };
        }

        private static ByteString UadpPayload(ByteString body)
        {
            byte[] networkMessage = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(42u), body, int.MaxValue).ToArray();
            networkMessage[2] = 0;
            return new ByteString(networkMessage);
        }

        private const string UadpFirstResource = "urn:opcfoundation:tests:publisher-first";
        private const string UadpSecondResource = "urn:opcfoundation:tests:publisher-second";

        private static ReadRequest Read(uint handle)
        {
            return new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = handle, TimeoutHint = 10000 },
                NodesToRead = [new ReadValueId { NodeId = ObjectIds.Server, AttributeId = Attributes.NodeId }]
            };
        }

        private static async ValueTask<DataChannel> AcceptAsync(DataChannelContext context, CancellationToken token)
        {
            return await context.AcceptAsync(token).ConfigureAwait(false) ??
                throw new AssertionException("The authenticated context closed instead of accepting its flow.");
        }

        /// <summary>
        /// Immutable evidence captured before the borrowed writer buffer can be released.
        /// </summary>
        private readonly record struct CapturedNativeWrite(
            DataChannelPath Path, uint ChannelId, ulong StreamId, uint RequestId, ByteString Payload, bool IsData);

        /// <summary>
        /// One fake native session endpoint, with UASC-only management and separately associated flow streams.
        /// It deliberately has no exporter, direct-binding interface or native-library dependency.
        /// </summary>
        private sealed class ManagementProvider(bool initiator) :
            IDataChannelUascManagementProvider, IDataChannelCarrier, IDataChannelFlowFinisher
        {
            public ManagementProvider Peer { get; set; } = null!;
            public IDataChannelCarrier? ManagementCarrier { get; private set; }
            public DataChannelContext? Context { get; private set; }
            public bool ReplaceIdentity { get; set; }
            public int Attachments { get; private set; }
            public ConcurrentDictionary<ulong, bool> OpenedStreams { get; } = new();
            public ConcurrentDictionary<ulong, uint> Bindings { get; } = new();
            public ConcurrentQueue<CapturedNativeWrite> Writes { get; } = new();
            public ConcurrentDictionary<ulong, bool> FinishedStreams { get; } = new();
            public int FinsSent => m_streams.Values.Sum(stream => stream.FinsSent);
            public int FinsObserved => m_streams.Values.Sum(stream => stream.FinsObserved);
            public DataChannelCarrierKind Kind => DataChannelCarrierKind.WebTransport;
            public DataChannelPeerIdentity Identity { get; private set; } = null!;
            public int MaxRecordSize => ManagementCarrier!.MaxRecordSize;
            public int MaxDatagramSize => 0;
            public ulong ManagementStreamId => 0;

            public DataChannelContext CreateContext(
                IDataChannelCarrier managementCarrier, IDataChannelContextFactory contextFactory)
            {
                if (ManagementCarrier != null)
                {
                    throw new DataChannelException(DataChannelResult.InvalidState);
                }
                Attachments++;
                ManagementCarrier = managementCarrier;
                DataChannelPeerIdentity verified = managementCarrier.Identity;
                Identity = ReplaceIdentity
                    ? new DataChannelPeerIdentity(
                        verified.LocalApplicationUri, verified.PeerApplicationUri,
                        verified.LocalKeyThumbprint, verified.PeerKeyThumbprint,
                        verified.Protection, verified.IsTransportInitiator, verified.IsContextInitiator)
                    : verified;
                Context = contextFactory.Create(this);
                return Context;
            }

            public ValueTask<ulong> OpenStreamAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ulong streamId = checked(((ulong)Interlocked.Increment(ref m_streamCount) * 4) + (initiator ? 0UL : 1UL));
                Assert.That(OpenedStreams.TryAdd(streamId, true), Is.True);
                var local = new NativeStream(this, streamId);
                var remote = new NativeStream(Peer, streamId);
                local.Peer = remote;
                remote.Peer = local;
                Assert.That(m_streams.TryAdd(streamId, local), Is.True);
                Assert.That(Peer.m_streams.TryAdd(streamId, remote), Is.True);
                local.Start();
                remote.Start();
                return new ValueTask<ulong>(streamId);
            }

            public ValueTask<bool> BindStreamAsync(
                ulong streamId, uint channelId, bool locallyOpened, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ManagementProvider owner = locallyOpened ? this : Peer;
                return new ValueTask<bool>(
                    streamId != ManagementStreamId &&
                    owner.OpenedStreams.ContainsKey(streamId) &&
                    (Bindings.TryAdd(streamId, channelId) || Bindings[streamId] == channelId));
            }

            public async ValueTask<DataChannelResult> SendAsync(
                DataChannelWrite write, CancellationToken cancellationToken = default)
            {
                Writes.Enqueue(new CapturedNativeWrite(
                    write.Path, write.ChannelId, write.StreamId, write.RequestId, write.Payload.Copy(), write.IsData));
                if (write.Path == DataChannelPath.Management)
                {
                    return await ManagementCarrier!.SendAsync(write, cancellationToken).ConfigureAwait(false);
                }
                if (write.Path != DataChannelPath.Flow || !m_streams.TryGetValue(write.StreamId, out NativeStream? stream))
                {
                    return DataChannelResult.Unsupported;
                }
                return await stream.SendAsync(write, cancellationToken).ConfigureAwait(false);
            }

            public async ValueTask CloseFlowAsync(
                ulong streamId, DataChannelResult result, CancellationToken cancellationToken = default)
            {
                if (m_streams.TryGetValue(streamId, out NativeStream? stream) && stream.Stop())
                {
                    stream.Peer.Stop();
                    await Peer.Context!.ReceiveStreamResetAsync(streamId, result, cancellationToken).ConfigureAwait(false);
                }
                Bindings.TryRemove(streamId, out _);
                Peer.Bindings.TryRemove(streamId, out _);
            }

            public async ValueTask FinishFlowAsync(ulong streamId, CancellationToken cancellationToken = default)
            {
                if (m_streams.TryGetValue(streamId, out NativeStream? stream) && !FinishedStreams.ContainsKey(streamId))
                {
                    await stream.FinishAsync(cancellationToken).ConfigureAwait(false);
                    FinishedStreams.TryAdd(streamId, true);
                    Bindings.TryRemove(streamId, out _);
                }
            }

            public Task WaitForPeerFinAsync(ulong streamId, CancellationToken cancellationToken)
            {
                return m_streams[streamId].WaitForPeerFinAsync(cancellationToken);
            }

            public async ValueTask CloseAsync(DataChannelResult result, CancellationToken cancellationToken = default)
            {
                foreach (NativeStream stream in m_streams.Values)
                {
                    stream.Stop();
                    stream.Peer.Stop();
                }
                await ManagementCarrier!.CloseAsync(result, cancellationToken).ConfigureAwait(false);
            }

            public async ValueTask DisposeStreamsAsync()
            {
                foreach (NativeStream stream in m_streams.Values)
                {
                    stream.Stop();
                }
                foreach (NativeStream stream in m_streams.Values)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }

            /// <summary>
            /// One direction's bounded receive queue. Admission completes a send; peer parsing runs independently.
            /// FIN is queued behind bytes and observed only after the receiver has consumed that queue entry.
            /// </summary>
            private sealed class NativeStream(ManagementProvider owner, ulong streamId) : IAsyncDisposable
            {
                public NativeStream Peer { get; set; } = null!;
                public int FinsSent => Volatile.Read(ref m_finsSent);
                public int FinsObserved => Volatile.Read(ref m_finsObserved);

                public void Start()
                {
                    m_receiveTask = Task.Run(ReceiveAsync);
                }

                public async ValueTask<DataChannelResult> SendAsync(DataChannelWrite write, CancellationToken token)
                {
                    await m_writer.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        if (m_stopped.IsCancellationRequested ||
                            FinsSent != 0 ||
                            !await Peer.m_incoming.Writer.WaitToWriteAsync(token).ConfigureAwait(false))
                        {
                            return DataChannelResult.Closed;
                        }
                        if (!write.TryCommit())
                        {
                            return write.Result;
                        }
                        if (!Peer.m_incoming.Writer.TryWrite(new StreamFrame(write.Payload.Copy(), write.EndsDirection)))
                        {
                            return DataChannelResult.Closed;
                        }
                        if (write.EndsDirection)
                        {
                            Interlocked.Increment(ref m_finsSent);
                        }
                        return DataChannelResult.Good;
                    }
                    finally
                    {
                        m_writer.Release();
                    }
                }

                public async ValueTask FinishAsync(CancellationToken token)
                {
                    await m_writer.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        if (FinsSent == 0)
                        {
                            if (m_stopped.IsCancellationRequested ||
                                !await Peer.m_incoming.Writer.WaitToWriteAsync(token).ConfigureAwait(false) ||
                                !Peer.m_incoming.Writer.TryWrite(new StreamFrame(ByteString.Empty, true)))
                            {
                                throw new DataChannelException(DataChannelResult.Closed);
                            }
                            Interlocked.Increment(ref m_finsSent);
                        }
                    }
                    finally
                    {
                        m_writer.Release();
                    }
                    await WaitForPeerFinAsync(token).ConfigureAwait(false);
                }

                public async Task WaitForPeerFinAsync(CancellationToken token)
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, m_stopped.Token);
                    await m_peerFin.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                }

                public bool Stop()
                {
                    if (Interlocked.Exchange(ref m_closed, 1) != 0)
                    {
                        return false;
                    }
                    m_stopped.Cancel();
                    m_incoming.Writer.TryComplete();
                    return true;
                }

                private async Task ReceiveAsync()
                {
                    try
                    {
                        while (await m_incoming.Reader.WaitToReadAsync(m_stopped.Token).ConfigureAwait(false))
                        {
                            StreamFrame frame = await m_incoming.Reader.ReadAsync(m_stopped.Token).ConfigureAwait(false);
                            if (!frame.Payload.IsEmpty)
                            {
                                DataChannelResult received = await owner.Context!.ReceiveStreamAsync(
                                    streamId, frame.Payload, cancellationToken: m_stopped.Token).ConfigureAwait(false);
                                if (received != DataChannelResult.Good)
                                {
                                    // A peer protocol refusal resets the native stream; it does not retract a send.
                                    await owner.CloseFlowAsync(streamId, received).ConfigureAwait(false);
                                    return;
                                }
                            }
                            if (frame.Fin)
                            {
                                // Logical END/RESET may already have retired Core's decoder. EOF is still
                                // a real transport observation, independent of that peer processing result.
                                await owner.Context!.ReceiveStreamAsync(
                                    streamId, ByteString.Empty, endOfStream: true, cancellationToken: m_stopped.Token)
                                    .ConfigureAwait(false);
                                Interlocked.Increment(ref m_finsObserved);
                                m_peerFin.TrySetResult(true);
                                return;
                            }
                        }
                    }
                    catch (OperationCanceledException) when (m_stopped.IsCancellationRequested)
                    {
                        // Explicit native RESET/whole-session termination cancels a pending reader.
                    }
                }

                public async ValueTask DisposeAsync()
                {
                    Stop();
                    await m_receiveTask.ConfigureAwait(false);
                    m_stopped.Dispose();
                    m_writer.Dispose();
                }

                private readonly record struct StreamFrame(ByteString Payload, bool Fin);
                private readonly Channel<StreamFrame> m_incoming = Channel.CreateBounded<StreamFrame>(32);
                private readonly SemaphoreSlim m_writer = new(1);
                private readonly CancellationTokenSource m_stopped = new();

                private readonly TaskCompletionSource<bool> m_peerFin =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                private Task m_receiveTask = Task.CompletedTask;
                private int m_finsSent;
                private int m_finsObserved;
                private int m_closed;
            }

            private readonly ConcurrentDictionary<ulong, NativeStream> m_streams = new();
            private long m_streamCount;
        }

        /// <summary>
        /// Bounded byte loopback for the existing reliable-stream adapter; never a native carrier.
        /// </summary>
        private sealed class MemoryDuplexStream(Channel<byte[]> incoming, Channel<byte[]> outgoing) : Stream
        {
            public override bool CanRead => true;
            public override bool CanWrite => true;
            public override bool CanSeek => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadCoreAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }

            public override Task WriteAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return outgoing.Writer.WriteAsync(buffer.AsSpan(offset, count).ToArray(), cancellationToken).AsTask();
            }

#if NETCOREAPP3_1_OR_GREATER
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return ReadCoreAsync(buffer, cancellationToken);
            }

            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return outgoing.Writer.WriteAsync(buffer.ToArray(), cancellationToken);
            }
#endif

            private async ValueTask<int> ReadCoreAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            {
                if (buffer.Length == 0)
                {
                    return 0;
                }
                if (m_remaining.IsEmpty)
                {
                    if (!await incoming.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return 0;
                    }
                    m_remaining = await incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                int count = Math.Min(buffer.Length, m_remaining.Length);
                m_remaining[..count].CopyTo(buffer);
                m_remaining = m_remaining[count..];
                return count;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    incoming.Writer.TryComplete();
                    outgoing.Writer.TryComplete();
                }
                base.Dispose(disposing);
            }

            public override void Flush()
            {
            }
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            private Memory<byte> m_remaining;
        }

        /// <summary>
        /// Owns real certificate-manager trust stores, endpoints and live transports for one test.
        /// </summary>
        private sealed class SocketHarness : IAsyncDisposable
        {
            private SocketHarness(
                CarrierTransport transport,
                DataChannelCarrierKind kind,
                MessageSecurityMode mode,
                TimeSpan? certificateLifetime,
                int tokenLifetime,
                bool recordServerTransport,
                string? workingDirectory)
            {
                Telemetry = NUnitTelemetryContext.Create(logLevel: Microsoft.Extensions.Logging.LogLevel.Warning);
                m_transport = transport;
                m_kind = kind;
                m_mode = mode;
                m_recordServerTransport = recordServerTransport;
                m_pkiRoot = Path.Combine(
                    workingDirectory ??
                    (transport == CarrierTransport.InMemoryReliableStream
                        ? Path.GetTempPath() : TestContext.CurrentContext.WorkDirectory),
                    "DataChannelPki", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(m_pkiRoot);
                ServerCertificate = CreateCertificate(ServerUri, "FlowServer", certificateLifetime);
                m_clientCertificate = CreateCertificate(ClientUri, "FlowClient", certificateLifetime);
                m_serverCertificates = new CertificateManager(Telemetry, maxRejectedCertificates: -1);
                m_clientCertificates = new CertificateManager(Telemetry, maxRejectedCertificates: -1);
                ServerFactory = new ContextFactory(
                    Telemetry, ClientUri, DataChannelCertificateIdentity.GetKeyThumbprint(m_clientCertificate),
                    mode == MessageSecurityMode.Sign);
                ClientFactory = new ContextFactory(
                    Telemetry, ServerUri, DataChannelCertificateIdentity.GetKeyThumbprint(ServerCertificate),
                    mode == MessageSecurityMode.Sign);
                m_configuration = EndpointConfiguration.Create();
                m_configuration.OperationTimeout = 10000;
                m_configuration.MaxBufferSize = 8192;
                m_configuration.SecurityTokenLifetime = tokenLifetime;
                m_configuration.ChannelLifetime = 60000;
                string scheme = kind switch
                {
                    DataChannelCarrierKind.QuicUasc => "opc.quic",
                    DataChannelCarrierKind.WebTransportUasc => "https",
                    DataChannelCarrierKind.WebTransport => "opc.wt",
                    _ => transport == CarrierTransport.Wss ? Utils.UriSchemeOpcWss : Utils.UriSchemeOpcTcp
                };
                int port = transport switch
                {
                    CarrierTransport.InMemoryReliableStream => 4840,
#if NET8_0_OR_GREATER
                    CarrierTransport.ManagedQuic or CarrierTransport.ManagedWebTransport => 0,
#endif
                    _ => GetFreePort()
                };
                string host = "localhost";
#if NET8_0_OR_GREATER
                if (transport is CarrierTransport.ManagedQuic or CarrierTransport.ManagedWebTransport)
                {
                    host = "127.0.0.1";
                }
#endif
                m_baseUrl = new Uri($"{scheme}://{host}:{port}/");
                string resource = kind switch
                {
                    DataChannelCarrierKind.DedicatedUasc => DataChannelProfiles.DedicatedResource,
                    DataChannelCarrierKind.SharedUasc => DataChannelProfiles.SharedResource,
                    _ => "/uasc"
                };
                m_url = new Uri(m_baseUrl, resource);
                m_listener = transport switch
                {
#if NET8_0_OR_GREATER
                    CarrierTransport.KestrelTcp => new KestrelTcpTransportListener(Telemetry),
#endif
                    CarrierTransport.Wss => new HttpsTransportListener(Utils.UriSchemeOpcWss, Telemetry),
                    _ => new TcpTransportListener(Telemetry)
                };
                ClientManagement.Peer = ServerManagement;
                ServerManagement.Peer = ClientManagement;
            }

            /// <summary>
            /// Gets the server's real application certificate.
            /// </summary>
            public Certificate ServerCertificate { get; }

            /// <summary>
            /// Gets the independent server authorization/context owner.
            /// </summary>
            public ContextFactory ServerFactory { get; }

            /// <summary>
            /// Gets the independent client authorization/context owner.
            /// </summary>
            public ContextFactory ClientFactory { get; }

            /// <summary>
            /// Gets the test telemetry.
            /// </summary>
            public ITelemetryContext Telemetry { get; }

            /// <summary>
            /// Gets the finite test deadline.
            /// </summary>
            public CancellationToken Token => m_deadline.Token;

            /// <summary>
            /// Gets the ordinary Service callback count.
            /// </summary>
            public int ServiceRequests => Volatile.Read(ref m_serviceRequests);

            /// <summary>
            /// Gets the fake native endpoints belonging to this one harness session.
            /// </summary>
            public ManagementProvider ClientManagement { get; } = new(true);
            public ManagementProvider ServerManagement { get; } = new(false);

            /// <summary>
            /// Gets the server-side transport decorator when recording is enabled.
            /// </summary>
            public RecordingTransport? ServerTransport { get; private set; }

            /// <summary>
            /// Gets or sets whether the byte adapter exposes the fake native management provider.
            /// </summary>
            public bool SupplyManagementProvider { get; set; } = true;

            /// <summary>
            /// Starts an authenticated live listener without constructing a Server or Session.
            /// </summary>
            public static async Task<SocketHarness> StartAsync(
                CarrierTransport transport,
                DataChannelCarrierKind kind,
                bool trustServer = true,
                MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
                TimeSpan? certificateLifetime = null,
                int tokenLifetime = 60000,
                bool enableDataChannels = true,
                bool reverse = false,
                bool recordServerTransport = false,
                TimeSpan? operationTimeout = null,
                string? workingDirectory = null)
            {
                var harness = new SocketHarness(
                    transport, kind, mode, certificateLifetime, tokenLifetime, recordServerTransport, workingDirectory);
                try
                {
                    if (operationTimeout.HasValue)
                    {
                        harness.m_deadline.CancelAfter(operationTimeout.Value);
                    }
                    await harness.ConfigureCertificatesAsync(trustServer).ConfigureAwait(false);
                    var callback = new Mock<ITransportListenerCallback>();
                    callback.Setup(value => value.ProcessRequestAsync(
                            It.IsAny<SecureChannelContext>(), It.IsAny<IServiceRequest>(),
                            It.IsAny<CancellationToken>()))
                        .Returns((SecureChannelContext _, IServiceRequest request, CancellationToken _) =>
                        {
                            Interlocked.Increment(ref harness.m_serviceRequests);
                            return new ValueTask<IServiceResponse>(new ReadResponse
                            {
                                ResponseHeader = new ResponseHeader
                                {
                                    RequestHandle = request.RequestHeader.RequestHandle
                                },
                                Results = request is ReadRequest read
                                    ? new ArrayOf<DataValue>(Enumerable.Range(0, read.NodesToRead.Count)
                                        .Select(_ => new DataValue(new Variant(ObjectIds.Server)))
                                        .ToArray().AsMemory())
                                    : [new DataValue(new Variant(ObjectIds.Server))]
                            });
                        });
                    var settings = new TransportListenerSettings
                    {
                        Descriptions = [harness.CreateEndpoint()],
                        Configuration = harness.m_configuration,
                        CertificateValidator = harness.m_serverCertificates,
                        ServerCertificates = harness.m_serverCertificates,
                        NamespaceUris = new NamespaceTable(),
                        Factory = EncodeableFactory.Create(),
                        DataChannels = enableDataChannels ? harness.ServerFactory : null,
                        AllowDataChannelIntegrity = mode == MessageSecurityMode.Sign,
                        HttpsMutualTls = true,
                        AcceptedTransportDecorator = harness.m_recordServerTransport
                            ? transport => harness.ServerTransport = new RecordingTransport(
                                transport, harness.m_serverWireChunks)
                            : null,
                        MaxChannelCount = 16
                    };
                    if (transport == CarrierTransport.InMemoryReliableStream)
                    {
                        ((TcpTransportListener)harness.m_listener).OpenExternal(
                            harness.m_baseUrl, settings, callback.Object);
                    }
                    else if (transport == CarrierTransport.ReliableStream)
                    {
                        ((TcpTransportListener)harness.m_listener).OpenExternal(
                            harness.m_baseUrl, settings, callback.Object);
                        harness.m_streamListener = new TcpListener(IPAddress.Loopback, harness.m_baseUrl.Port);
                        harness.m_streamListener.Start();
                        harness.m_streamAccept = harness.AcceptReliableStreamAsync();
                    }
#if NET8_0_OR_GREATER
                    else if (transport == CarrierTransport.ManagedQuic && CanUseQuic)
                    {
                        harness.m_tlsIdentity = harness.ServerCertificate.AsX509Certificate2();
                        harness.m_quicListener = await new QuicUascTransport().ListenAsync(
                            harness.m_url,
                            new SslServerAuthenticationOptions
                            {
                                ApplicationProtocols = [new SslApplicationProtocol(DataChannelProfiles.UascQuicAlpn)],
                                ServerCertificate = harness.m_tlsIdentity,
                                ClientCertificateRequired = false
                            },
                            harness.Token).ConfigureAwait(false);
                        harness.m_url = harness.m_quicListener.EndpointUrl;
                        harness.m_baseUrl = new UriBuilder(harness.m_url) { Path = "/", Query = string.Empty }.Uri;
                        settings.Descriptions = [harness.CreateEndpoint()];
                        ((TcpTransportListener)harness.m_listener).OpenExternal(
                            harness.m_baseUrl, settings, callback.Object);
                        harness.m_streamAccept = harness.AcceptQuicStreamAsync();
                    }
                    else if (transport == CarrierTransport.ManagedWebTransport && CanUseQuic)
                    {
                        using Certificate tlsCertificate = CreateCertificate(TlsServerUri, "FlowTls", null);
                        using (ICertificateStore https = harness.m_clientCertificates.OpenTrustedStore(
                            TrustListIdentifier.Https))
                        {
                            await https.AddAsync(tlsCertificate, ct: harness.Token).ConfigureAwait(false);
                        }
                        using (CertificateCollection issuers = [])
                        {
                            await harness.m_serverCertificates.UpdateApplicationCertificateAsync(
                                ObjectTypeIds.HttpsCertificateType, tlsCertificate, issuers, harness.Token)
                                .ConfigureAwait(false);
                        }
                        CertificateValidationResult validated = await harness.m_clientCertificates.ValidateAsync(
                            tlsCertificate, TrustListIdentifier.Https, harness.Token).ConfigureAwait(false);
                        Assert.That(validated.IsValid, Is.True);
                        harness.m_tlsIdentity = tlsCertificate.AsX509Certificate2();
                        harness.m_webTransportListener = await new ManagedWebTransportUascServer(
                            harness.ServerFactory.Budget).ListenAsync(
                            harness.m_url, new IPEndPoint(IPAddress.Loopback, 0),
                            new SslServerAuthenticationOptions
                            {
                                ApplicationProtocols = [new SslApplicationProtocol("h3")],
                                ServerCertificate = harness.m_tlsIdentity
                            },
                            (_, _) => ValueTask.FromResult(true),
                            cancellationToken: harness.Token).ConfigureAwait(false);
                        harness.m_url = harness.m_webTransportListener.EndpointUrl;
                        harness.m_baseUrl = new UriBuilder(harness.m_url) { Path = "/", Query = string.Empty }.Uri;
                        settings.Descriptions = [harness.CreateEndpoint()];
                        ((TcpTransportListener)harness.m_listener).OpenExternal(
                            harness.m_baseUrl, settings, callback.Object);
                        harness.m_streamAccept = harness.AcceptWebTransportStreamAsync();
                    }
                    else if (transport is CarrierTransport.ManagedQuic or CarrierTransport.ManagedWebTransport)
                    {
                        Assert.That(Environment.GetEnvironmentVariable("OPCUA_REQUIRE_MANAGED_QUIC"),
                            Is.Not.EqualTo("true"), "This validation lane requires the operating system QUIC runtime.");
                        Assert.Ignore("The real managed QUIC runtime is unavailable.");
                    }
#endif
                    else
                    {
                        await harness.m_listener.OpenAsync(
                            reverse ? harness.m_url : harness.m_baseUrl,
                            settings, callback.Object, harness.Token).ConfigureAwait(false);
                    }
                    return harness;
                }
                catch
                {
                    await harness.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            /// <summary>
            /// Opens the selected flow endpoint through the public client factory.
            /// </summary>
            public async ValueTask<UascDataChannelConnection> ConnectAsync(string? expectedServerUri = null)
            {
                EndpointDescription endpoint = CreateEndpoint();
                if (expectedServerUri != null)
                {
                    endpoint.Server.ApplicationUri = expectedServerUri;
                }
                ITransportChannelFactory transportFactory = m_transport switch
                {
                    CarrierTransport.InMemoryReliableStream => new ReliableStreamChannelFactory(
                        m_url.Scheme, CreateMemoryTransportFactory()),
                    CarrierTransport.ReliableStream => new ReliableStreamChannelFactory(m_url.Scheme),
                    CarrierTransport.Wss => new OpcWssTransportChannelFactory(),
                    _ => new TcpTransportChannelFactory()
                };
#if NET8_0_OR_GREATER
                if (m_transport == CarrierTransport.ManagedQuic && CanUseQuic)
                {
                    m_quicClient = await ConnectQuicClientAsync().ConfigureAwait(false);
                    transportFactory = m_quicClient;
                }
                else if (m_transport == CarrierTransport.ManagedWebTransport && CanUseQuic)
                {
                    m_webTransportClient = await ConnectWebTransportClientAsync().ConfigureAwait(false);
                    transportFactory = m_webTransportClient;
                }
#endif
                var client = new UascDataChannelClient(transportFactory, Telemetry);
                return await client.ConnectAsync(
                    m_url,
                    new TransportChannelSettings
                    {
                        Description = endpoint,
                        Configuration = m_configuration,
                        ClientCertificate = m_clientCertificate.AddRef(),
                        ServerCertificate = ServerCertificate.AddRef(),
                        CertificateValidator = m_clientCertificates,
                        NamespaceUris = new NamespaceTable(),
                        Factory = EncodeableFactory.Create(),
                        DataChannels = ClientFactory,
                        AllowDataChannelIntegrity = m_mode == MessageSecurityMode.Sign
                    },
                    Token).ConfigureAwait(false);
            }

            /// <summary>
            /// Takes a real RHE handoff without opening a second outbound client connection.
            /// </summary>
            public async ValueTask<UascDataChannelConnection> ConnectReverseAsync()
            {
                var waiting = new TaskCompletionSource<ITransportWaitingConnection>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                m_listener.ConnectionStatusChanged += (_, args) =>
                {
                    if (ServiceResult.IsBad(args.ChannelStatus))
                    {
                        waiting.TrySetException(new ServiceResultException(args.ChannelStatus));
                    }
                };
                m_reverseHost = new ReverseConnectHost(Telemetry);
                m_reverseHost.CreateListener(
                    new Uri($"opc.tcp://localhost:{GetFreePort()}/"),
                    (_, args) =>
                    {
                        args.Accepted = true;
                        waiting.TrySetResult(args);
                        return Task.CompletedTask;
                    },
                    (_, args) =>
                    {
                        if (ServiceResult.IsBad(args.ChannelStatus))
                        {
                            waiting.TrySetException(new ServiceResultException(args.ChannelStatus));
                        }
                    });
                await m_reverseHost.OpenAsync(Token).ConfigureAwait(false);
                m_listener.CreateReverseConnection(m_reverseHost.Url!, 10000);
                ITransportWaitingConnection accepted = await waiting.Task.WaitAsync(Token).ConfigureAwait(false);
                Assert.That(accepted.ServerUri, Is.EqualTo(ServerUri));
                Assert.That(accepted.EndpointUrl, Is.EqualTo(m_url));
                var client = new UascDataChannelClient(new TcpTransportChannelFactory(), Telemetry);
                return await client.ConnectAsync(accepted, new TransportChannelSettings
                {
                    Description = CreateEndpoint(),
                    Configuration = m_configuration,
                    ClientCertificate = m_clientCertificate.AddRef(),
                    ServerCertificate = ServerCertificate.AddRef(),
                    CertificateValidator = m_clientCertificates,
                    NamespaceUris = new NamespaceTable(),
                    Factory = EncodeableFactory.Create(),
                    DataChannels = ClientFactory,
                    AllowDataChannelIntegrity = m_mode == MessageSecurityMode.Sign
                }, Token).ConfigureAwait(false);
            }

            /// <summary>
            /// Connects a real low-level client for authenticated wire-negative and ordering tests.
            /// </summary>
            public async Task<ProbeClientChannel> ConnectProbeAsync(
                bool confirmProfile = true,
                TimeProvider? clock = null,
                MessageSecurityMode? requestedMode = null,
                ArrayPool<byte>? pool = null,
                bool enableDataChannels = true)
            {
                EndpointDescription endpoint = CreateEndpoint();
                if (requestedMode.HasValue)
                {
                    endpoint.SecurityMode = requestedMode.Value;
                    if (requestedMode == MessageSecurityMode.None)
                    {
                        endpoint.SecurityPolicyUri = SecurityPolicies.None;
                    }
                }
                if (!confirmProfile)
                {
                    endpoint.TransportProfileUri = Profiles.UaTcpTransport;
                }
                var quotas = new ChannelQuotas(ServiceMessageContext.Create(Telemetry))
                {
                    CertificateValidator = m_clientCertificates,
                    MaxBufferSize = 8192,
                    MaxMessageSize = 1024 * 1024,
                    SecurityTokenLifetime = m_configuration.SecurityTokenLifetime,
                    ChannelLifetime = 60000
                };
                var wire = new ConcurrentQueue<byte[]>();
                IUaSCByteTransportFactory? physicalTransport = null;
                if (m_transport == CarrierTransport.InMemoryReliableStream)
                {
                    physicalTransport = CreateMemoryTransportFactory();
                }
#if NET8_0_OR_GREATER
                if (m_transport == CarrierTransport.ManagedQuic && CanUseQuic)
                {
                    m_quicClient = await ConnectQuicClientAsync().ConfigureAwait(false);
                    physicalTransport = m_quicClient;
                }
                else if (m_transport == CarrierTransport.ManagedWebTransport && CanUseQuic)
                {
                    m_webTransportClient = await ConnectWebTransportClientAsync().ConfigureAwait(false);
                    physicalTransport = m_webTransportClient;
                }
#endif
                var recorder = new RecordingTransportFactory(wire, physicalTransport);
                var client = new ProbeClientChannel(
                    pool == null
                        ? new BufferManager("flow-probe", 8192, Telemetry)
                        : new BufferManager("flow-probe", 8192, Telemetry, pool),
                    recorder,
                    quotas,
                    m_clientCertificate.AddRef(),
                    ServerCertificate.AddRef(),
                    endpoint,
                    wire,
                    Telemetry,
                    clock);
                try
                {
                    if (confirmProfile)
                    {
                        client.ConfigureDataChannels(
                            enableDataChannels ? ClientFactory : null, m_mode == MessageSecurityMode.Sign);
                    }
                    await client.ConnectAsync(m_url, 10000, Token).ConfigureAwait(false);
                    return client;
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Changes the real trust store and emits the manager's scoped lifecycle notification.
            /// </summary>
            public async Task RemoveServerTrustAsync(TrustListIdentifier trustList)
            {
                await using ITrustListTransaction transaction = await m_clientCertificates
                    .BeginUpdateAsync(trustList, Token).ConfigureAwait(false);
                await transaction.RemoveTrustedCertificateAsync(ServerCertificate.Thumbprint, Token)
                    .ConfigureAwait(false);
                await transaction.CommitAsync(Token).ConfigureAwait(false);
            }

            /// <summary>
            /// Sends a real HEL with a missing resource and returns its first response type, or zero for closure.
            /// </summary>
            public async Task<uint> SendHelloWithoutEndpointAsync()
            {
                using var client = new TcpClient(AddressFamily.InterNetwork);
                await client.ConnectAsync(IPAddress.Loopback, m_url.Port).WaitAsync(Token).ConfigureAwait(false);
                using NetworkStream stream = client.GetStream();
                byte[] hello = new byte[32];
                BinaryPrimitives.WriteUInt32LittleEndian(hello, TcpMessageType.Hello);
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(4), 32);
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(12), 8192);
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(16), 8192);
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(20), 65536);
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(24), 8);
#if NET5_0_OR_GREATER
                await stream.WriteAsync(hello.AsMemory(), Token).ConfigureAwait(false);
#else
                await stream.WriteAsync(hello, 0, hello.Length, Token).ConfigureAwait(false);
#endif
                byte[] header = new byte[8];
                int received = 0;
                while (received < header.Length)
                {
#if NET5_0_OR_GREATER
                    int count = await stream.ReadAsync(header.AsMemory(received), Token).ConfigureAwait(false);
#else
                    int count = await stream.ReadAsync(header, received, header.Length - received, Token)
                        .ConfigureAwait(false);
#endif
                    if (count == 0)
                    {
                        return 0;
                    }
                    received += count;
                }
                return BinaryPrimitives.ReadUInt32LittleEndian(header);
            }

#if NET8_0_OR_GREATER
            /// <summary>
            /// Sends a real, framed HEL over independently validated TLS while substituting only its endpoint.
            /// </summary>
            /// <exception cref="PlatformNotSupportedException">The QUIC runtime is unavailable.</exception>
            /// <exception cref="ArgumentOutOfRangeException">The endpoint mismatch is not defined.</exception>
            public async Task<StatusCode> SendQuicHelloWithEndpointMismatchAsync(string mismatch)
            {
                if (!CanUseQuic)
                {
                    throw new PlatformNotSupportedException();
                }
                var endpoint = new UriBuilder(m_url);
                switch (mismatch)
                {
                    case "authority":
                        endpoint.Host = "not-configured.example";
                        break;
                    case "port":
                        endpoint.Port = m_url.Port == 4840 ? 4841 : 4840;
                        break;
                    case "path":
                        endpoint.Path = "/not-configured";
                        break;
                    case "query":
                        endpoint.Query = "not-configured=1";
                        break;
                    case "scheme":
                        endpoint.Scheme = "opc.tcp";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mismatch));
                }
                byte[] url = Encoding.UTF8.GetBytes(endpoint.Uri.AbsoluteUri);
                byte[] hello = new byte[32 + url.Length];
                BinaryPrimitives.WriteUInt32LittleEndian(hello, TcpMessageType.Hello);
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(4), (uint)hello.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(12), 8192);
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(16), 8192);
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(20), 65536);
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(24), 8);
                BinaryPrimitives.WriteInt32LittleEndian(hello.AsSpan(28), url.Length);
                url.CopyTo(hello, 32);
                m_quicClient = await ConnectQuicClientAsync().ConfigureAwait(false);
                var buffers = new BufferManager("quic-hello", 8192, Telemetry);
                IUaSCByteTransport transport = m_quicClient.Create(buffers, 8192, Telemetry);
                try
                {
                    await transport.ConnectAsync(m_url, Token).ConfigureAwait(false);
                    await transport.SendChunkAsync(hello.AsMemory(), Token).ConfigureAwait(false);
                    ArraySegment<byte> response = await transport.ReceiveChunkAsync(Token).ConfigureAwait(false);
                    try
                    {
                        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan()),
                            Is.EqualTo(TcpMessageType.Error));
                        return new StatusCode(BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(8)));
                    }
                    finally
                    {
                        buffers.ReturnBuffer(response.Array, nameof(SendQuicHelloWithEndpointMismatchAsync));
                    }
                }
                finally
                {
                    transport.Close();
                }
            }

            /// <summary>
            /// Tries to reuse the real, already claimed initiator stream through the public byte-factory path.
            /// </summary>
            /// <exception cref="PlatformNotSupportedException">The QUIC runtime is unavailable.</exception>
            public async ValueTask ReclaimQuicClientStreamAsync()
            {
                if (!CanUseQuic)
                {
                    throw new PlatformNotSupportedException();
                }
                IUaSCByteTransport transport = m_quicClient!.Create(
                    new BufferManager("quic-reclaim", 8192, Telemetry), 8192, Telemetry);
                try
                {
                    await transport.ConnectAsync(m_url, Token).ConfigureAwait(false);
                }
                finally
                {
                    transport.Close();
                }
            }

            /// <summary>
            /// Tries to reverse the accepted baseline's role through the public channel-factory path.
            /// </summary>
            /// <exception cref="PlatformNotSupportedException">The QUIC runtime is unavailable.</exception>
            public ITransportChannel CreateClientFromAcceptedQuicBaseline()
            {
                if (!CanUseQuic)
                {
                    throw new PlatformNotSupportedException();
                }
                return m_quicServer!.Create(Telemetry);
            }

            /// <summary>
            /// Closes the physical initiator owner without sending a graceful UASC CLO.
            /// </summary>
            /// <exception cref="PlatformNotSupportedException">The QUIC runtime is unavailable.</exception>
            public ValueTask CloseManagedClientAsync()
            {
                if (!CanUseQuic)
                {
                    throw new PlatformNotSupportedException();
                }
                return m_transport == CarrierTransport.ManagedWebTransport
                    ? m_webTransportClient!.DisposeAsync()
                    : m_quicClient!.DisposeAsync();
            }

            /// <summary>
            /// Opens TLS with a previously store-validated exact server certificate, for WebSocket negative tests.
            /// </summary>
            public async Task<RawWebSocket> OpenRawWebSocketAsync(string protocol)
            {
                CertificateValidationResult validated = await m_clientCertificates.ValidateAsync(
                    ServerCertificate, TrustListIdentifier.Https, Token).ConfigureAwait(false);
                Assert.That(validated.IsValid, Is.True);
                byte[] expectedCertificate = ServerCertificate.RawData;
                var socket = new ClientWebSocket();
                X509Certificate2 clientCertificate = m_clientCertificate.AsX509Certificate2();
                try
                {
                    socket.Options.AddSubProtocol(protocol);
                    socket.Options.ClientCertificates ??= [];
                    socket.Options.ClientCertificates.Add(clientCertificate);
                    socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                        certificate != null &&
                        (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0 &&
                        certificate.GetRawCertData().AsSpan().SequenceEqual(expectedCertificate);
                    var endpoint = new UriBuilder(m_url) { Scheme = "wss" };
                    await socket.ConnectAsync(endpoint.Uri, Token).ConfigureAwait(false);
                    return new RawWebSocket(socket, clientCertificate);
                }
                catch
                {
                    socket.Dispose();
                    clientCertificate.Dispose();
                    throw;
                }
            }
#endif

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (m_streamAccept != null)
                {
                    await m_deadline.CancelAsync().ConfigureAwait(false);
                    m_streamListener?.Stop();
                    try
                    {
                        await m_streamAccept.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (Token.IsCancellationRequested)
                    {
                        // Listener shutdown cancels a pending external accept.
                    }
                    catch (SocketException) when (Token.IsCancellationRequested)
                    {
                        // Stopping the listener interrupts the physical accept.
                    }
                }
                await m_listener.CloseAsync().ConfigureAwait(false);
                await m_listener.DisposeAsync().ConfigureAwait(false);
                if (m_reverseHost != null)
                {
                    await m_reverseHost.DisposeAsync().ConfigureAwait(false);
                }
#if NET8_0_OR_GREATER
                if (CanUseQuic)
                {
                    if (m_quicListener != null)
                    {
                        await m_quicListener.DisposeAsync().ConfigureAwait(false);
                    }
                    if (m_quicClient != null)
                    {
                        await m_quicClient.DisposeAsync().ConfigureAwait(false);
                    }
                    if (m_quicServer != null)
                    {
                        await m_quicServer.DisposeAsync().ConfigureAwait(false);
                    }
                    if (m_webTransportListener != null)
                    {
                        await m_webTransportListener.DisposeAsync().ConfigureAwait(false);
                    }
                    if (m_webTransportClient != null)
                    {
                        await m_webTransportClient.DisposeAsync().ConfigureAwait(false);
                    }
                    if (m_webTransportServer != null)
                    {
                        await m_webTransportServer.DisposeAsync().ConfigureAwait(false);
                    }
                }
                m_tlsIdentity?.Dispose();
#endif
                foreach (DataChannelContext context in ClientFactory.Contexts.Concat(ServerFactory.Contexts))
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                }
                await ClientManagement.DisposeStreamsAsync().ConfigureAwait(false);
                await ServerManagement.DisposeStreamsAsync().ConfigureAwait(false);
                m_clientCertificates.Dispose();
                m_serverCertificates.Dispose();
                m_clientCertificate.Dispose();
                ServerCertificate.Dispose();
                m_deadline.Dispose();
                if (Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, true);
                }
            }

            /// <summary>
            /// Attaches one external stream to the ordinary authenticated UASC dispatcher.
            /// </summary>
            private async Task AcceptReliableStreamAsync()
            {
                Socket socket = await m_streamListener!.AcceptSocketAsync().WaitAsync(Token).ConfigureAwait(false);
                var stream = new NetworkStream(socket, ownsSocket: true);
                try
                {
                    ((TcpTransportListener)m_listener).AcceptTransport(
                        new AcceptedStreamFactory(stream, CreateEndpoint().TransportProfileUri ?? string.Empty),
                        m_serverCertificates, m_configuration.MaxBufferSize);
                }
                catch
                {
#if NETCOREAPP3_1_OR_GREATER
                    await stream.DisposeAsync().ConfigureAwait(false);
#else
                    stream.Dispose();
#endif
                    throw;
                }
            }

#if NET8_0_OR_GREATER
            /// <summary>
            /// Requires actual managed QUIC support rather than substituting a socket or fake stream.
            /// </summary>
            [SupportedOSPlatformGuard("windows")]
            [SupportedOSPlatformGuard("linux")]
            [SupportedOSPlatformGuard("osx")]
            private static bool CanUseQuic =>
                (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) &&
                QuicConnection.IsSupported &&
                QuicListener.IsSupported;

            /// <summary>
            /// Attaches the one real QUIC baseline directly to the ordinary UASC listener.
            /// </summary>
            /// <exception cref="PlatformNotSupportedException">The QUIC runtime is unavailable.</exception>
            private async Task AcceptQuicStreamAsync()
            {
                if (!CanUseQuic)
                {
                    throw new PlatformNotSupportedException();
                }
                m_quicServer = await m_quicListener!.AcceptAsync(Token).ConfigureAwait(false);
                ((TcpTransportListener)m_listener).AcceptTransport(
                    m_quicServer, m_serverCertificates, m_configuration.MaxBufferSize);
            }

            /// <summary>
            /// Attaches stream zero from an authorized HTTP/3 CONNECT to the ordinary UASC listener.
            /// </summary>
            /// <exception cref="PlatformNotSupportedException">The QUIC runtime is unavailable.</exception>
            private async Task AcceptWebTransportStreamAsync()
            {
                if (!CanUseQuic)
                {
                    throw new PlatformNotSupportedException();
                }
                m_webTransportServer = await m_webTransportListener!.AcceptAsync(Token).ConfigureAwait(false);
                ((TcpTransportListener)m_listener).AcceptTransport(
                    m_webTransportServer, m_serverCertificates, m_configuration.MaxBufferSize);
            }

            /// <summary>
            /// Pins the TLS transport certificate independently of the UASC application trust store.
            /// </summary>
            /// <exception cref="PlatformNotSupportedException">The QUIC runtime is unavailable.</exception>
            private async Task<ManagedWebTransportUascConnection> ConnectWebTransportClientAsync()
            {
                if (!CanUseQuic)
                {
                    throw new PlatformNotSupportedException();
                }
                return await new ManagedWebTransportUascTransport(ClientFactory.Budget).ConnectAsync(
                    m_url,
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = m_url.DnsSafeHost,
                        ApplicationProtocols = [new SslApplicationProtocol("h3")],
                        RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                            certificate != null &&
                            errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateChainErrors &&
                            certificate.GetRawCertData().AsSpan().SequenceEqual(m_tlsIdentity!.RawData)
                    },
                    Token).ConfigureAwait(false);
            }

            /// <summary>
            /// Uses an exact TLS test certificate pin independently of the UASC application trust store.
            /// </summary>
            /// <exception cref="PlatformNotSupportedException">The QUIC runtime is unavailable.</exception>
            private async Task<QuicUascConnection> ConnectQuicClientAsync()
            {
                if (!CanUseQuic)
                {
                    throw new PlatformNotSupportedException();
                }
                return await new QuicUascTransport().ConnectAsync(
                    m_url,
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = m_url.DnsSafeHost,
                        ApplicationProtocols = [new SslApplicationProtocol(DataChannelProfiles.UascQuicAlpn)],
                        RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                            certificate != null &&
                            errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateChainErrors &&
                            certificate.GetRawCertData().AsSpan().SequenceEqual(ServerCertificate.RawData)
                    },
                    Token).ConfigureAwait(false);
            }

            /// <summary>
            /// Opens a raw QUIC connection with only the supplied ALPN for handshake-negative tests.
            /// </summary>
            /// <exception cref="PlatformNotSupportedException">The QUIC runtime is unavailable.</exception>
            [SupportedOSPlatform("windows")]
            [SupportedOSPlatform("linux")]
            [SupportedOSPlatform("macos")]
            public async Task<QuicConnection> ConnectRawQuicClientAsync(SslApplicationProtocol protocol)
            {
                if (!CanUseQuic)
                {
                    throw new PlatformNotSupportedException();
                }
                return await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
                {
                    RemoteEndPoint = new DnsEndPoint(m_url.DnsSafeHost, m_url.Port),
                    ClientAuthenticationOptions = new SslClientAuthenticationOptions
                    {
                        TargetHost = m_url.DnsSafeHost,
                        ApplicationProtocols = [protocol],
                        RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                            certificate != null &&
                            errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateChainErrors &&
                            certificate.GetRawCertData().AsSpan().SequenceEqual(ServerCertificate.RawData)
                    },
                    DefaultCloseErrorCode = 0x100,
                    DefaultStreamErrorCode = 0x100,
                    IdleTimeout = TimeSpan.FromMinutes(1)
                }, Token).ConfigureAwait(false);
            }

            /// <summary>
            /// Waits for the external UASC listener to accept the QUIC baseline stream.
            /// </summary>
            /// <exception cref="InvalidOperationException">The QUIC accept operation has not started.</exception>
            public async Task WaitForQuicAcceptAsync()
            {
                await (m_streamAccept ??
                    throw new InvalidOperationException(
                        "The managed QUIC accept operation has not started."))
                    .WaitAsync(Token).ConfigureAwait(false);
            }
#endif

            private async Task ConfigureCertificatesAsync(bool trustServer)
            {
                m_clientCertificates.RegisterTrustList(
                    TrustListIdentifier.Users, Path.Combine(m_pkiRoot, "client-users"));
                using (ICertificateStore users = m_clientCertificates.OpenTrustedStore(TrustListIdentifier.Users))
                {
                    await users.AddAsync(ServerCertificate, ct: Token).ConfigureAwait(false);
                }
                foreach (TrustListIdentifier list in new[] { TrustListIdentifier.Peers, TrustListIdentifier.Https })
                {
                    string suffix = list == TrustListIdentifier.Peers ? "peers" : "https";
                    m_serverCertificates.RegisterTrustList(list, Path.Combine(m_pkiRoot, "server-" + suffix));
                    m_clientCertificates.RegisterTrustList(list, Path.Combine(m_pkiRoot, "client-" + suffix));
                    using ICertificateStore serverStore = m_serverCertificates.OpenTrustedStore(list);
                    await serverStore.AddAsync(m_clientCertificate, ct: Token).ConfigureAwait(false);
                    if (trustServer)
                    {
                        using ICertificateStore clientStore = m_clientCertificates.OpenTrustedStore(list);
                        await clientStore.AddAsync(ServerCertificate, ct: Token).ConfigureAwait(false);
                    }
                }
                using CertificateCollection issuers = [];
                await m_serverCertificates.UpdateApplicationCertificateAsync(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType, ServerCertificate, issuers, Token)
                    .ConfigureAwait(false);
                await m_serverCertificates.UpdateApplicationCertificateAsync(
                    ObjectTypeIds.HttpsCertificateType, ServerCertificate, issuers, Token).ConfigureAwait(false);
            }

            /// <summary>
            /// Returns the exact certificate evidence for direct post-OPN attachment checks.
            /// </summary>
            public DataChannelPeerIdentity CreateClientIdentity()
            {
                return new DataChannelPeerIdentity(ClientUri, ServerUri,
                    DataChannelCertificateIdentity.GetKeyThumbprint(m_clientCertificate),
                    DataChannelCertificateIdentity.GetKeyThumbprint(ServerCertificate),
                    DataChannelProtectionMode.Confidential, true);
            }

            /// <summary>
            /// Reuses the external accepted-stream path with two in-process byte streams.
            /// </summary>
            private UascStreamByteTransportFactory CreateMemoryTransportFactory()
            {
                return new UascStreamByteTransportFactory(
                    (_, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        var toServer = Channel.CreateBounded<byte[]>(32);
                        var toClient = Channel.CreateBounded<byte[]>(32);
                        var client = new MemoryDuplexStream(toClient, toServer);
                        var server = new MemoryDuplexStream(toServer, toClient);
                        ((TcpTransportListener)m_listener).AcceptTransport(
                            new AcceptedStreamFactory(server, CreateEndpoint().TransportProfileUri!,
                                SupplyManagementProvider ? ServerManagement : null),
                            m_serverCertificates, m_configuration.MaxBufferSize);
                        return new ValueTask<Stream>(client);
                    },
                    "Test-InMemory-Reliable-Stream", CreateEndpoint().TransportProfileUri!,
                    managementProvider: SupplyManagementProvider ? ClientManagement : null);
            }

            private EndpointDescription CreateEndpoint()
            {
                return new EndpointDescription
                {
                    EndpointUrl = m_url.AbsoluteUri,
                    TransportProfileUri = m_kind switch
                    {
                        DataChannelCarrierKind.DedicatedUasc => DataChannelProfiles.DedicatedUasc,
                        DataChannelCarrierKind.SharedUasc => DataChannelProfiles.SharedUasc,
                        DataChannelCarrierKind.QuicUasc => DataChannelProfiles.UascQuic,
                        DataChannelCarrierKind.WebTransportUasc => DataChannelProfiles.UascWebTransport,
                        DataChannelCarrierKind.WebTransport => DataChannelProfiles.WebTransport,
                        _ => throw new ArgumentOutOfRangeException(nameof(m_kind))
                    },
                    SecurityMode = m_mode,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    ServerCertificate = new ByteString(ServerCertificate.RawData),
                    Server = new ApplicationDescription
                    {
                        ApplicationUri = ServerUri,
                        ApplicationType = ApplicationType.Server,
                        ApplicationName = new LocalizedText("FlowServer")
                    }
                };
            }

            private static Certificate CreateCertificate(string applicationUri, string name, TimeSpan? lifetime)
            {
                ICertificateBuilder builder = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                    applicationUri, name, "CN=" + name, ["localhost", "127.0.0.1"]);
                if (lifetime.HasValue)
                {
                    builder.SetNotAfter(DateTime.UtcNow.Add(lifetime.Value));
                }
                return builder.SetRSAKeySize(2048).CreateForRSA();
            }

            private static int GetFreePort()
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)socket.LocalEndPoint!).Port;
            }

            /// <summary>The configured resource authorized by both peers.</summary>
            public const string Resource = "urn:opcfoundation:tests:flow-socket";

            /// <summary>The server identity in the certificate and EndpointDescription.</summary>
            public const string ServerUri = "urn:opcfoundation:tests:flow-server";

            /// <summary>The HTTPS certificate identity, distinct from the UASC application.</summary>
            public const string TlsServerUri = ServerUri + ":tls";

            /// <summary>The client identity in its application certificate.</summary>
            public const string ClientUri = "urn:opcfoundation:tests:flow-client";

            private readonly CarrierTransport m_transport;
            private readonly DataChannelCarrierKind m_kind;
            private readonly MessageSecurityMode m_mode;
            private readonly string m_pkiRoot;
#if NET8_0_OR_GREATER
            private Uri m_url;
            private Uri m_baseUrl;
#else
            private readonly Uri m_url;
            private readonly Uri m_baseUrl;
#endif
            private readonly ITransportListener m_listener;
            private readonly Certificate m_clientCertificate;
            private readonly CertificateManager m_serverCertificates;
            private readonly CertificateManager m_clientCertificates;
            private readonly EndpointConfiguration m_configuration;
            private readonly CancellationTokenSource m_deadline = new(TimeSpan.FromSeconds(45));
            private readonly bool m_recordServerTransport;
            private readonly ConcurrentQueue<byte[]> m_serverWireChunks = new();
            private TcpListener? m_streamListener;
            private Task? m_streamAccept;
            private ReverseConnectHost? m_reverseHost;
#if NET8_0_OR_GREATER
            private QuicUascListener? m_quicListener;
            private ManagedWebTransportUascListener? m_webTransportListener;
            private ManagedWebTransportUascConnection? m_webTransportClient;
            private ManagedWebTransportUascConnection? m_webTransportServer;
            private QuicUascConnection? m_quicClient;
            private QuicUascConnection? m_quicServer;
            private X509Certificate2? m_tlsIdentity;
#endif
            private int m_serviceRequests;
        }

        /// <summary>
        /// Connects through the public stream factory without a Data Channel or native management exchange.
        /// </summary>
        private sealed class ReliableStreamChannelFactory(
            string scheme, IUaSCByteTransportFactory? suppliedFactory = null) : ITransportChannelFactory
        {
            /// <inheritdoc/>
            public string UriScheme => scheme;

            /// <inheritdoc/>
            public ITransportChannel Create(ITelemetryContext telemetry)
            {
                return new UaSCUaBinaryTransportChannel(
                    suppliedFactory ??
                    new UascStreamByteTransportFactory(ConnectAsync, "Test-Reliable-Stream", scheme switch
                    {
                        "opc.quic" => DataChannelProfiles.UascQuic,
                        "https" => DataChannelProfiles.UascWebTransport,
                        _ => string.Empty
                    }), telemetry);
            }

            /// <summary>
            /// Supplies a real network stream solely for shared-adapter and protected UASC composition evidence.
            /// </summary>
            private static async ValueTask<Stream> ConnectAsync(Uri endpoint, CancellationToken ct)
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new DnsEndPoint(endpoint.Host, endpoint.Port), ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        }

        /// <summary>
        /// Transfers an accepted stream into the listener's own buffer and security ownership.
        /// </summary>
        private sealed class AcceptedStreamFactory(
            Stream stream, string profile, IDataChannelUascManagementProvider? managementProvider = null) :
            IUaSCByteTransportFactory, IUaSCTransportProfile
        {
            /// <inheritdoc/>
            public string Implementation => "Test-Reliable-Stream";

            /// <inheritdoc/>
            public string TransportProfileUri => profile;

            /// <inheritdoc/>
            public IUaSCByteTransport Create(
                BufferManager bufferManager, int receiveBufferSize, ITelemetryContext telemetry)
            {
                return new UascStreamByteTransport(
                    stream, bufferManager, receiveBufferSize, Implementation, managementProvider: managementProvider);
            }
        }

        /// <summary>
        /// Authorizes an explicit resource, payload and authenticated peer key on each endpoint.
        /// </summary>
        private sealed class ContextFactory : IDataChannelContextFactory, IDataChannelAuthorizer
        {
            /// <summary>
            /// Creates a per-application factory with explicit peer authorization.
            /// </summary>
            public ContextFactory(
                ITelemetryContext telemetry,
                string peerUri,
                ByteString peerKey,
                bool allowIntegrity)
            {
                m_telemetry = telemetry;
                m_peerUri = peerUri;
                m_peerKey = peerKey;
                m_allowIntegrity = allowIntegrity;
            }

            /// <summary>
            /// Gets the first authenticated context, before its protected HELLO is started.
            /// </summary>
            public TaskCompletionSource<DataChannelContext> Created { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets every authenticated association produced by the actual transport.
            /// </summary>
            public ConcurrentQueue<DataChannelContext> Contexts { get; } = new();

            /// <summary>
            /// Gets the number of resource-policy checks.
            /// </summary>
            public int Authorizations => Volatile.Read(ref m_authorizations);

            /// <summary>
            /// Gets the application budget to verify release after physical context closure.
            /// </summary>
            public DataChannelApplicationBudget Budget { get; } = new();

            /// <summary>
            /// Gets or sets the explicitly configured protocol for the primary resource.
            /// </summary>
            public string PayloadProtocol { get; set; } = DataChannelProfiles.UadpPayload;

            /// <summary>
            /// Gets or sets the explicitly controlled runtime clock for deadline tests.
            /// </summary>
            public TimeProvider Clock { get; set; } = TimeProvider.System;

            /// <summary>
            /// Gets or sets the application maximum; UASC must independently clamp it to actual chunk capacity.
            /// </summary>
            public int MaxMessageSize { get; set; } = 4096;

            /// <summary>
            /// Gets or sets the independent bound on retained messages.
            /// </summary>
            public int MaxQueueMessages { get; set; } = 1024;

            /// <inheritdoc/>
            public DataChannelContext Create(IDataChannelCarrier carrier)
            {
                var context = new DataChannelContext(
                    carrier,
                    this,
                    new DataChannelOptions
                    {
                        MaxMessageSize = MaxMessageSize,
                        MaxQueueMessages = MaxQueueMessages,
                        MaxControlBytes = 4096,
                        AllowIntegrity = m_allowIntegrity
                    },
                    Budget,
                    Clock,
                    m_telemetry);
                Contexts.Enqueue(context);
                Created.TrySetResult(context);
                return context;
            }

            /// <inheritdoc/>
            public ValueTask<DataChannelAuthorization> AuthorizeAsync(
                DataChannelAuthorizationRequest request,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_authorizations);
                DataChannelParametersDataType parameters = request.Parameters;
                bool allowed = request.Context.Peer.PeerApplicationUri == m_peerUri &&
                    request.Context.Peer.PeerKeyThumbprint == m_peerKey &&
                    request.Mode == DataChannelAuthorizationMode.Configured &&
                    ((parameters.ResourceUri == SocketHarness.Resource &&
                        parameters.Protocol == PayloadProtocol) ||
                        (parameters.ResourceUri is UadpFirstResource or UadpSecondResource &&
                            parameters.Protocol == DataChannelProfiles.UadpPayload)) &&
                    parameters.Delivery == DataChannelDeliveryMode.Ordered &&
                    parameters.Protection == (m_allowIntegrity
                        ? DataChannelProtectionMode.Integrity
                        : DataChannelProtectionMode.Confidential);
                return new ValueTask<DataChannelAuthorization>(allowed
                    ? new DataChannelAuthorization(DataChannelResult.Good, parameters)
                    : new DataChannelAuthorization(DataChannelResult.NotAuthorized));
            }

            private readonly ITelemetryContext m_telemetry;
            private readonly string m_peerUri;
            private readonly ByteString m_peerKey;
            private readonly bool m_allowIntegrity;
            private int m_authorizations;
        }

        /// <summary>
        /// Exposes the existing security encoder for deliberately malformed, genuinely authenticated wire traffic.
        /// </summary>
        private sealed class ProbeClientChannel : UaSCUaBinaryClientChannel
        {
            /// <summary>
            /// Creates a probe that still performs the complete production HEL/ACK/OPN handshake.
            /// </summary>
            public ProbeClientChannel(
                BufferManager buffers,
                RecordingTransportFactory transportFactory,
                ChannelQuotas quotas,
                Certificate client,
                Certificate server,
                EndpointDescription endpoint,
                ConcurrentQueue<byte[]> wireChunks,
                ITelemetryContext telemetry,
                TimeProvider? clock)
                : base(
                    "flow-probe", buffers, transportFactory, quotas, client, null, server, endpoint, telemetry, clock)
            {
                WireChunks = wireChunks;
                Recorder = transportFactory;
            }

            /// <summary>
            /// Gets exact chunks seen at the actual socket submission boundary.
            /// </summary>
            public ConcurrentQueue<byte[]> WireChunks { get; }

            /// <summary>
            /// Gets the socket submission barrier used by deterministic writer tests.
            /// </summary>
            public RecordingTransportFactory Recorder { get; }

            /// <summary>
            /// Sends arbitrary plaintext through the normal ordered security writer.
            /// </summary>
            /// <exception cref="ServiceResultException">The channel closed before the write was admitted.</exception>
            public async ValueTask SendAuthenticatedBodyAsync(
                uint messageType,
                uint requestId,
                byte[] plaintext,
                bool abort,
                CancellationToken cancellationToken)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (await Gate.EnterAsync(cancellationToken).ConfigureAwait(false))
                {
                    EncodedSymmetricMessage encoded = EncodeSymmetricMessage(
                        messageType, requestId, new ArraySegment<byte>(plaintext), true, out bool limitsExceeded);
                    Assert.That(limitsExceeded, Is.False);
                    if (abort)
                    {
                        encoded = new EncodedSymmetricMessage(messageType, requestId, true, true, encoded.Chunks);
                    }
                    if (!BeginWriteSymmetricMessage(encoded, completion))
                    {
                        encoded.Chunks.Release(BufferManager, nameof(SendAuthenticatedBodyAsync));
                        throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                    }
                }
                await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Signs one literal chunk with consecutive channel sequence numbers for adversarial peer tests.
            /// </summary>
            public async ValueTask SendSignedPartAsync(
                uint chunkType, uint requestId, byte[] body, CancellationToken cancellationToken)
            {
                using (await Gate.EnterAsync(cancellationToken).ConfigureAwait(false))
                using (await SendOrder.EnterAsync(cancellationToken).ConfigureAwait(false))
                {
                    Assert.That(SecurityMode, Is.EqualTo(MessageSecurityMode.Sign));
                    ChannelToken token = CurrentToken!;
                    SecurityPolicyInfo policy = token.SecurityPolicy!;
                    byte[] chunk = new byte[24 + body.Length + policy.SymmetricSignatureLength];
                    BinaryPrimitives.WriteUInt32LittleEndian(chunk, chunkType);
                    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)chunk.Length);
                    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(8), ChannelId);
                    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(12), token.TokenId);
                    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(16), GetNewSequenceNumber());
                    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(20), requestId);
                    body.CopyTo(chunk, 24);
                    ArraySegment<byte> signed = CryptoUtils.SymmetricEncryptAndSign(
                        new ArraySegment<byte>(chunk, 16, 8 + body.Length), policy,
                        token.ClientEncryptingKey!, token.ClientInitializationVector!,
                        token.ClientSigningKey, token.ClientHmac, signOnly: true);
                    Assert.That(signed.Offset + signed.Count, Is.EqualTo(chunk.Length));
                    await Recorder.Transport!.SendChunkAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }

            /// <inheritdoc/>
            protected override void HandleWriteComplete(
                BufferCollection? buffers,
                object? state,
                int bytesWritten,
                ServiceResult result)
            {
                base.HandleWriteComplete(buffers, state, bytesWritten, result);
                if (state is TaskCompletionSource<bool> completion)
                {
                    if (ServiceResult.IsBad(result))
                    {
                        completion.TrySetException(new ServiceResultException(result));
                    }
                    else
                    {
                        completion.TrySetResult(true);
                    }
                }
            }

            /// <summary>
            /// Decodes an owned capture under the security gate, without replaying it or verifying/changing
            /// live sequence state. Tests use a frozen clock and the initial token, never a renewal.
            /// </summary>
            public async Task<(ByteString Body, uint RequestId, uint Sequence)> CanonicalPart30DecodeAsync(
                byte[] capture, bool isRequest, CancellationToken cancellationToken)
            {
                byte[] copy = (byte[])capture.Clone();
                using (await Gate.EnterAsync(cancellationToken).ConfigureAwait(false))
                {
                    ChannelToken current = CurrentToken!;
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(12)),
                        Is.EqualTo(current.TokenId));
                    ArraySegment<byte> plaintext = new(copy);
                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        // Capture inspection must not race the live token's reusable HMAC or cipher.
                        plaintext = CryptoUtils.SymmetricDecryptAndVerify(
                            new ArraySegment<byte>(copy, 16, copy.Length - 16),
                            current.SecurityPolicy!,
                            (isRequest ? current.ClientEncryptingKey : current.ServerEncryptingKey)!,
                            (isRequest ? current.ClientInitializationVector : current.ServerInitializationVector)!,
                            isRequest ? current.ClientSigningKey : current.ServerSigningKey,
                            SecurityMode == MessageSecurityMode.Sign,
                            current.TokenId,
                            0,
                            null,
                            null);
                    }
                    uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(16));
                    uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(20));
                    var body = new ArraySegment<byte>(copy, 24, plaintext.Count - 24);
                    Assert.That(CurrentToken, Is.SameAs(current));
                    Assert.That(body.Offset, Is.EqualTo(24),
                        "The plaintext body starts after the 16-byte UASC header and 8-byte SequenceHeader.");
                    return (new ByteString(body.ToArray()), requestId, sequence);
                }
            }

            /// <summary>
            /// An adversarial peer may withhold its HELLO while still sending a secured Service.
            /// False preserves every existing probe's behavior.
            /// </summary>
            public bool CanonicalPart30WithholdFlowHello { get; set; }

            public bool CanonicalPart30VerifyWithheldHello { get; set; }

            public ConcurrentQueue<(uint Sequence, byte Carrier)> CanonicalPart30VerifiedHellos { get; } = new();

            protected override async ValueTask<bool> HandleIncomingMessageAsync(
                uint messageType, ArraySegment<byte> messageChunk, CancellationToken ct)
            {
                if (CanonicalPart30WithholdFlowHello && TcpMessageType.IsType(messageType, TcpMessageType.Message))
                {
                    using (await Gate.EnterAsync(ct).ConfigureAwait(false))
                    {
                        byte[] copy = [.. messageChunk];
                        ArraySegment<byte> body = ReadSymmetricMessage(new ArraySegment<byte>(copy), false,
                            out ChannelToken _, out uint requestId, out uint sequence);
                        if (requestId == 0 &&
                            body.Count >= UascFlowMessageCodec.ControlPrefixSize + 1 &&
                            body.AsSpan()[0] == 0 &&
                            body.AsSpan()[1] == 255 &&
                            body.AsSpan()[10] == (byte)FlowOperation.Hello)
                        {
                            Assert.That(VerifySequenceNumber(sequence, nameof(CanonicalPart30VerifyWithheldHello)), Is.True);
                            if (CanonicalPart30VerifyWithheldHello)
                            {
                                Assert.That(body, Has.Count.GreaterThanOrEqualTo(16));
                                CanonicalPart30VerifiedHellos.Enqueue((sequence, body.AsSpan()[15]));
                            }
                            // The normal receive loop returns the original encrypted buffer.
                            return false;
                        }
                    }
                }
                return await base.HandleIncomingMessageAsync(messageType, messageChunk, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Observes each real TCP submission without altering or coalescing chunks.
        /// </summary>
        private sealed class RecordingTransportFactory(
            ConcurrentQueue<byte[]> chunks,
            IUaSCByteTransportFactory? physicalTransport = null) : IUaSCByteTransportFactory, IUaSCTransportProfile
        {
            /// <summary>
            /// Gets the actual observed socket transport after connection creation.
            /// </summary>
            public RecordingTransport? Transport { get; private set; }

            /// <inheritdoc/>
            public string Implementation => "Recorded " + (physicalTransport?.Implementation ?? "UA-TCP");

            /// <inheritdoc/>
            public string TransportProfileUri =>
                (physicalTransport as IUaSCTransportProfile)?.TransportProfileUri ?? string.Empty;

            /// <inheritdoc/>
            public IUaSCByteTransport Create(
                BufferManager bufferManager, int receiveBufferSize, ITelemetryContext telemetry)
            {
                Transport = new RecordingTransport(
                    (physicalTransport ?? new TcpByteTransportFactory(telemetry))
                        .Create(bufferManager, receiveBufferSize, telemetry),
                    chunks);
                return Transport;
            }
        }

        /// <summary>
        /// Delegates network traffic while retaining immutable wire evidence for assertions.
        /// </summary>
        private sealed class RecordingTransport(
            IUaSCByteTransport inner,
            ConcurrentQueue<byte[]> chunks) :
            IUaSCByteTransport,
            IUaSCByteTransportLimits,
            IUaSCByteTransportSendProgress,
            IDataChannelUascManagementProvider
        {
            /// <inheritdoc/>
            public DataChannelContext CreateContext(
                IDataChannelCarrier managementCarrier, IDataChannelContextFactory contextFactory)
            {
                return inner is IDataChannelUascManagementProvider provider
                    ? provider.CreateContext(managementCarrier, contextFactory)
                    : throw new DataChannelException(DataChannelResult.Unsupported);
            }

            /// <inheritdoc/>
            public EndPoint? LocalEndpoint => inner.LocalEndpoint;

            /// <inheritdoc/>
            public EndPoint? RemoteEndpoint => inner.RemoteEndpoint;

            /// <inheritdoc/>
            public TransportChannelFeatures Features => inner.Features;

            /// <inheritdoc/>
            public string Implementation => inner.Implementation;

            /// <inheritdoc/>
            public void SetSendProgressCallback(Action<int>? callback)
            {
                if (inner is IUaSCByteTransportSendProgress progress)
                {
                    progress.SetSendProgressCallback(callback);
                }
            }

            /// <summary>
            /// Gets immutable received UACP chunks before their security headers are consumed.
            /// </summary>
            public ConcurrentQueue<byte[]> ReceivedChunks { get; } = new();

            /// <summary>
            /// Pauses a selected write after security commitment, optionally skipping matching writes.
            /// </summary>
            /// <exception cref="ArgumentOutOfRangeException">The number of skipped writes is negative.</exception>
            /// <exception cref="InvalidOperationException">A write barrier is already armed.</exception>
            public WriteBarrier PauseNext(uint messageType, bool dataOnly, int skipMatches = 0)
            {
                if (skipMatches < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(skipMatches));
                }
                var barrier = new WriteBarrier(messageType, dataOnly, skipMatches: skipMatches);
                if (Interlocked.CompareExchange(ref m_barrier, barrier, null) != null)
                {
                    throw new InvalidOperationException("A write barrier is already armed.");
                }
                return barrier;
            }

            /// <summary>
            /// Pauses one canonical CONTROL message with the selected opcode.
            /// </summary>
            /// <exception cref="InvalidOperationException">A write barrier is already armed.</exception>
            public WriteBarrier PauseNextControl(FlowOperation operation)
            {
                var barrier = new WriteBarrier(
                    TcpMessageType.Message,
                    dataOnly: false,
                    controlOpcode: (byte)operation);
                if (Interlocked.CompareExchange(ref m_barrier, barrier, null) != null)
                {
                    throw new InvalidOperationException("A write barrier is already armed.");
                }
                return barrier;
            }

            /// <summary>
            /// Corrupts exactly one already secured test chunk so the real receiver must verify its signature.
            /// </summary>
            public void CorruptNextSecuredChunk()
            {
                Volatile.Write(ref m_corruptNext, 1);
            }

            /// <inheritdoc/>
            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                return inner.ConnectAsync(url, ct);
            }

            /// <inheritdoc/>
            public async ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                await WaitAtBarrierAsync(chunk, ct).ConfigureAwait(false);
                if (Interlocked.Exchange(ref m_corruptNext, 0) != 0)
                {
                    byte[] corrupt = chunk.ToArray();
                    corrupt[^1] ^= 0x80;
                    chunk = corrupt;
                }
                chunks.Enqueue(chunk.ToArray());
                await inner.SendChunkAsync(chunk, ct).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public async ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                if (buffers.Count == 1)
                {
                    await SendChunkAsync(buffers[0].AsMemory(), ct).ConfigureAwait(false);
                    return;
                }
                byte[] chunk = new byte[buffers.Sum(buffer => buffer.Count)];
                int offset = 0;
                foreach (ArraySegment<byte> buffer in buffers)
                {
                    buffer.AsSpan().CopyTo(chunk.AsSpan(offset));
                    offset += buffer.Count;
                }
                await WaitAtBarrierAsync(chunk, ct).ConfigureAwait(false);
                int recorded = 0;
                while (recorded < chunk.Length)
                {
                    int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(recorded + 4)));
                    Assert.That(size, Is.InRange(9, chunk.Length - recorded));
                    chunks.Enqueue(chunk.AsSpan(recorded, size).ToArray());
                    recorded += size;
                }
                await inner.SendChunkAsync(buffers, ct).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                ArraySegment<byte> chunk = await inner.ReceiveChunkAsync(ct).ConfigureAwait(false);
                ReceivedChunks.Enqueue([.. chunk]);
                return chunk;
            }

            /// <inheritdoc/>
            public void Close()
            {
                Interlocked.Exchange(ref m_barrier, null)?.Release();
                inner.Close();
            }

            /// <inheritdoc/>
            public void SetReceiveBufferSize(int receiveBufferSize)
            {
                if (inner is IUaSCByteTransportLimits limits)
                {
                    limits.SetReceiveBufferSize(receiveBufferSize);
                }
            }

            private async ValueTask WaitAtBarrierAsync(ReadOnlyMemory<byte> chunk, CancellationToken token)
            {
                WriteBarrier? barrier = Volatile.Read(ref m_barrier);
                if (barrier != null &&
                    barrier.Matches(chunk.Span) &&
                    ReferenceEquals(Interlocked.CompareExchange(ref m_barrier, null, barrier), barrier))
                {
                    if (MemoryMarshal.TryGetArray(chunk, out ArraySegment<byte> buffer))
                    {
                        barrier.Buffer = buffer.Array;
                    }
                    barrier.Entered.TrySetResult(true);
                    await barrier.Released.Task.WaitAsync(token).ConfigureAwait(false);
                }
            }

            private WriteBarrier? m_barrier;
            private int m_corruptNext;
        }

        /// <summary>
        /// A deterministic boundary between security commitment and actual wire submission.
        /// </summary>
        private sealed class WriteBarrier(
            uint messageType,
            bool dataOnly,
            byte? controlOpcode = null,
            int skipMatches = 0)
        {
            /// <summary>
            /// Gets the barrier-entry signal.
            /// </summary>
            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the barrier-release signal.
            /// </summary>
            public TaskCompletionSource<bool> Released { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets or sets the real borrowed socket buffer, never a test copy.
            /// </summary>
            public byte[]? Buffer { get; set; }

            /// <summary>
            /// Releases the blocked socket write.
            /// </summary>
            public void Release()
            {
                Released.TrySetResult(true);
            }

            /// <summary>
            /// Selects the outer message and, optionally, its unencrypted signed DATA kind.
            /// </summary>
            public bool Matches(ReadOnlySpan<byte> chunk)
            {
                if (chunk.Length < 4 ||
                    (BinaryPrimitives.ReadUInt32LittleEndian(chunk) & TcpMessageType.MessageTypeMask) != messageType)
                {
                    return false;
                }
                bool matches;
                if (controlOpcode.HasValue)
                {
                    matches = messageType == TcpMessageType.Message &&
                        chunk.Length >= 35 &&
                        chunk[24] == 0 &&
                        chunk[25] == 255 &&
                        chunk[34] == controlOpcode.Value;
                }
                else
                {
                    matches = !dataOnly ||
                        (messageType == TcpMessageType.Message &&
                            chunk.Length >= 26 &&
                            chunk[24] == 0 &&
                            chunk[25] == 254);
                }
                if (!matches)
                {
                    return false;
                }
                int remaining;
                do
                {
                    remaining = Volatile.Read(ref m_skipMatches);
                    if (remaining == 0)
                    {
                        return true;
                    }
                }
                while (Interlocked.CompareExchange(ref m_skipMatches, remaining - 1, remaining) != remaining);
                return false;
            }

            private int m_skipMatches = skipMatches;
        }

        /// <summary>
        /// Poisons returned buffers and records duplicate returns at the real client buffer-manager boundary.
        /// </summary>
        private sealed class OwnershipArrayPool : ArrayPool<byte>
        {
            /// <summary>
            /// Gets outstanding real transport rentals.
            /// </summary>
            public int Outstanding => m_owned.Count;

            /// <summary>
            /// Gets attempts to return one rental more than once.
            /// </summary>
            public int DuplicateReturns => Volatile.Read(ref m_duplicateReturns);

            /// <inheritdoc/>
            public override byte[] Rent(int minimumLength)
            {
                byte[] buffer = Shared.Rent(minimumLength);
                Assert.That(m_owned.TryAdd(buffer, 0), Is.True);
                return buffer;
            }

            /// <inheritdoc/>
            public override void Return(byte[] array, bool clearArray = false)
            {
                if (!m_owned.TryRemove(array, out _))
                {
                    Interlocked.Increment(ref m_duplicateReturns);
                    return;
                }
                array.AsSpan().Fill(0xDD);
                Shared.Return(array, clearArray);
            }

            /// <summary>
            /// Checks ownership without altering the buffer or its pool lifetime.
            /// </summary>
            public bool Owns(byte[] buffer)
            {
                return m_owned.ContainsKey(buffer);
            }

            private readonly ConcurrentDictionary<byte[], byte> m_owned = new();
            private int m_duplicateReturns;
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Retains the TLS client certificate until the associated WebSocket has closed.
        /// </summary>
        private sealed class RawWebSocket(ClientWebSocket socket, X509Certificate2 certificate) : IDisposable
        {
            /// <summary>
            /// Gets the live TLS WebSocket.
            /// </summary>
            public ClientWebSocket Socket => socket;

            /// <inheritdoc/>
            public void Dispose()
            {
                socket.Dispose();
                certificate.Dispose();
            }
        }
#endif

        [Test]
        [CancelAfter(60000)]
        public async Task CanonicalPart30ListenerDetectsFlowAdvertisementsAndPreservesFactoryConfiguration(
            [Values(0, 1, 2, 3, 4)] int selection, [Values] bool allowIntegrity)
        {
            List<EndpointDescription> endpoints = selection switch
            {
                0 => [],
                1 => [CanonicalPart30Endpoint(Profiles.UaTcpTransport, "/services")],
                2 => [CanonicalPart30Endpoint(DataChannelProfiles.PubSubUasc, "/pubsub/uasc")],
                3 => [CanonicalPart30Endpoint(DataChannelProfiles.SharedUasc, "/flow/v1/shared")],
                _ => [CanonicalPart30Endpoint(Profiles.UaTcpTransport, "/services"),
                    CanonicalPart30Endpoint(DataChannelProfiles.SharedUasc, "/flow/v1/shared"),
                    CanonicalPart30Endpoint(DataChannelProfiles.PubSubUasc, "/pubsub/uasc")]
            };
            using var channel = new CanonicalPart30SelectionChannel(endpoints);
            var factory = new Mock<IDataChannelContextFactory>(MockBehavior.Strict);
            Assert.That(channel.HasDataChannelEndpoints, Is.EqualTo(selection >= 2));
            Assert.That(channel.Select("opc.tcp://localhost/pubsub/uasc"), Is.False);
            channel.ConfigureDataChannels(factory.Object, allowIntegrity);
            string resource = selection == 3 ? "/flow/v1/shared" : "/pubsub/uasc";
            Assert.That(channel.Select("opc.tcp://localhost" + resource), Is.EqualTo(selection >= 2));
            Assert.Multiple(() =>
            {
                Assert.That(channel.AllowDataChannelIntegrity, Is.EqualTo(allowIntegrity));
                Assert.That(channel.DataChannels, Is.Null, "Selection alone must not authenticate a context.");
                Assert.That(endpoints, Has.Count.EqualTo(selection == 0 ? 0 : selection == 4 ? 3 : 1));
            });
            factory.VerifyNoOtherCalls();
            if (selection >= 2)
            {
                // Observe the configuration through authenticated OPN/context creation, not just a stored flag.
                await using var harness = new CanonicalPart30InMemoryHarness(allowIntegrity);
                ProbeClientChannel client = await harness.ConnectAsync(mode: allowIntegrity
                    ? MessageSecurityMode.Sign : MessageSecurityMode.SignAndEncrypt).ConfigureAwait(false);
                DataChannelContext remote = await harness.ServerFactory.Created.Task.ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(remote.IsReady, Is.True);
                    Assert.That(client.DataChannels!.IsReady, Is.True);
                    Assert.That(remote.Identity.Peer.Protection, Is.EqualTo(allowIntegrity
                        ? DataChannelProtectionMode.Integrity : DataChannelProtectionMode.Confidential));
                    Assert.That(harness.Accepted!.DataChannelFactory, Is.SameAs(harness.ServerFactory));
                });
                Assert.Throws<InvalidOperationException>(() =>
                    harness.Accepted!.ConfigureDataChannels(null, !allowIntegrity));
                await CanonicalPart30ServiceAsync(harness, 31).ConfigureAwait(false);
                Assert.That(harness.ServiceHandles, Is.EqualTo(new uint[] { 31 }));
            }
        }

        [Test]
        public void CanonicalPart30ListenerSelectionRequiresExactHelloAndConfiguredResourceProfiles(
            [Values("opc.tcp", "opc.wss")] string scheme,
            [Values("opc.tcp", "opc.wss")] string configuredScheme,
            [Values] bool canonicalProfile,
            [Values] bool canonicalConfiguredResource,
            [Values] bool canonicalHelloResource)
        {
            string profile = canonicalProfile ? DataChannelProfiles.PubSubUasc : DataChannelProfiles.SharedUasc;
            EndpointDescription endpoint = CanonicalPart30Endpoint(
                profile, canonicalConfiguredResource ? "/pubsub/uasc" : "/flow/v1/shared", configuredScheme);
            using var channel = new CanonicalPart30SelectionChannel([endpoint]);
            var factory = new Mock<IDataChannelContextFactory>(MockBehavior.Strict);
            channel.ConfigureDataChannels(factory.Object, false);
            bool expected = canonicalProfile == canonicalConfiguredResource &&
                canonicalProfile == canonicalHelloResource &&
                scheme == configuredScheme;

            Assert.That(channel.Select($"{scheme}://localhost" +
                (canonicalHelloResource ? "/pubsub/uasc" : "/flow/v1/shared")), Is.EqualTo(expected));
            Assert.Multiple(() =>
            {
                Assert.That(channel.EndpointDescription, expected ? Is.SameAs(endpoint) : Is.Null);
                Assert.That(channel.SelectedDataChannelKind,
                    expected ? Is.EqualTo(DataChannelCarrierKind.SharedUasc) : Is.Null);
                Assert.That(channel.Candidates, Has.Length.EqualTo(expected ? 1 : 0));
                Assert.That(endpoint.TransportProfileUri, Is.EqualTo(profile));
                Assert.That(endpoint.EndpointUrl, Does.EndWith(
                    canonicalConfiguredResource ? "/pubsub/uasc" : "/flow/v1/shared"));
            });
            factory.VerifyNoOtherCalls();
        }

        [Test]
        public void CanonicalPart30ListenerSelectionKeepsOnlyRequestedExactProfile(
            [Values] bool reverse,
            [Values("/pubsub/uasc", "/flow/v1/shared", "/services")] string resource,
            [Values("mixed", "singleton", "missing")] string population)
        {
            EndpointDescription canonical = CanonicalPart30Endpoint(DataChannelProfiles.PubSubUasc, "/pubsub/uasc");
            EndpointDescription legacy = CanonicalPart30Endpoint(DataChannelProfiles.SharedUasc, "/flow/v1/shared");
            EndpointDescription ordinary = CanonicalPart30Endpoint(Profiles.UaTcpTransport, "/services");
            EndpointDescription expected = resource == "/pubsub/uasc" ? canonical :
                resource == "/flow/v1/shared" ? legacy : ordinary;
            List<EndpointDescription> endpoints = population switch
            {
                "singleton" => [expected],
                "missing" => [resource == "/pubsub/uasc" ? legacy : canonical],
                _ => reverse ? [ordinary, legacy, canonical] : [canonical, legacy, ordinary]
            };
            EndpointDescription[] original = [.. endpoints];
            using var channel = new CanonicalPart30SelectionChannel(endpoints);
            channel.ConfigureDataChannels(new Mock<IDataChannelContextFactory>(MockBehavior.Strict).Object, false);

            Assert.That(channel.Select("opc.tcp://localhost" + resource), Is.EqualTo(population != "missing"));
            Assert.Multiple(() =>
            {
                Assert.That(channel.Candidates, population == "missing" ? Is.Empty : Is.EqualTo(new[] { expected }));
                Assert.That(channel.EndpointDescription, population == "missing" ? Is.Null : Is.SameAs(expected));
                Assert.That(channel.SelectedDataChannelKind, population == "missing" || resource == "/services"
                    ? Is.Null : Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(endpoints, Is.EqualTo(original), "One accepted channel must not prune listener settings.");
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        [CancelAfter(60000)]
        public async Task CanonicalPart30ActualHelloRejectsCrossProfileSelectionAsync(bool canonicalAdvertisement)
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            harness.Open([harness.Endpoint(
                canonicalAdvertisement ? DataChannelProfiles.PubSubUasc : DataChannelProfiles.SharedUasc,
                canonicalAdvertisement ? "/pubsub/uasc" : "/flow/v1/shared")]);
            byte[] response = await harness.ExchangeHelloAsync(CanonicalPart30Hello(
                "opc.tcp://localhost" + (canonicalAdvertisement ? "/flow/v1/shared" : "/pubsub/uasc")))
                .ConfigureAwait(false);
            CanonicalPart30AssertError(response, StatusCodes.BadTcpEndpointUrlInvalid);
            harness.AssertNoDispatch();
        }

        [TestCase("opc.tcp://localhost/pubsub")]
        [TestCase("opc.tcp://localhost/pubsub/")]
        [TestCase("opc.tcp://localhost/pubsub/other")]
        [TestCase("opc.tcp://localhost/pubsub//")]
        [TestCase("opc.tcp://localhost/pubsub/uasc/")]
        [TestCase("opc.tcp://localhost/pubsub/uasc/child")]
        [TestCase("opc.tcp://localhost/pubsub/uasc?fallback=true")]
        [TestCase("opc.tcp://localhost/pubsub/uasc#fallback")]
        [TestCase("opc.tcp://user@localhost/pubsub/uasc")]
        [CancelAfter(60000)]
        public async Task CanonicalPart30ActualHelloRejectsMalformedReservedResourceAsync(string helloUrl)
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            harness.Open([
                harness.Endpoint(Profiles.UaTcpTransport, "/services"),
                harness.Endpoint()]);

            byte[] response = await harness.ExchangeHelloAsync(CanonicalPart30Hello(helloUrl))
                .ConfigureAwait(false);

            CanonicalPart30AssertError(response, StatusCodes.BadTcpEndpointUrlInvalid);
            harness.AssertNoDispatch();
        }

        [Test]
        [CancelAfter(60000)]
        public async Task CanonicalPart30ActualHelloWithoutEndpointCannotSelectServicesAsync(
            [Values(0, -1)] int endpointLength, [Values] bool mixed)
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            List<EndpointDescription> endpoints = [harness.Endpoint()];
            if (mixed)
            {
                endpoints.Insert(0, harness.Endpoint(Profiles.UaTcpTransport, "/services"));
            }
            harness.Open(endpoints);
            byte[] response = await harness.ExchangeHelloAsync(CanonicalPart30Hello(null, endpointLength))
                .ConfigureAwait(false);
            CanonicalPart30AssertError(response, StatusCodes.BadTcpEndpointUrlInvalid);
            harness.AssertNoDispatch();
        }

        [TestCase(true)]
        [TestCase(false)]
        [CancelAfter(60000)]
        public async Task CanonicalPart30WssRejectsRequestProfileMismatchBeforeUpgradeAsync(bool canonicalRequest)
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            await using HttpsTransportListener listener = harness.CreateHttpsListener([
                harness.Endpoint(canonicalRequest ? DataChannelProfiles.SharedUasc : DataChannelProfiles.PubSubUasc,
                    canonicalRequest ? "/flow/v1/shared" : "/pubsub/uasc", "opc.wss")]);
            using var socket = new CanonicalPart30WebSocket();
            var feature = new CanonicalPart30WebSocketFeature(socket);
            DefaultHttpContext http = CanonicalPart30Http(feature, canonicalRequest ? "/pubsub/uasc" : "/flow/v1/shared");
            using Stream responseBody = http.Response.Body;

            await listener.AcceptWebSocketAsync(http).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(http.Response.StatusCode, Is.EqualTo(404));
                Assert.That(http.Response.Body.Length, Is.GreaterThan(0));
                Assert.That(feature.Upgrades, Is.Zero);
                Assert.That(socket.Sent, Is.Empty);
            });
            harness.AssertNoDispatch();
        }

        [TestCase("/pubsub", "", true, true, 404)]
        [TestCase("/pubsub/", "", true, true, 404)]
        [TestCase("/pubsub/child", "", true, true, 404)]
        [TestCase("/pubsub//", "", true, true, 404)]
        [TestCase("/pubsub/uasc/", "", true, true, 404)]
        [TestCase("/pubsub/uasc/child", "", true, true, 404)]
        [TestCase("/pubsub/uasc", "?route=other", true, true, 404)]
        [TestCase("/pubsub/uasc", "", false, true, 404)]
        [TestCase("/pubsub/uasc", "", true, false, 400)]
        [CancelAfter(60000)]
        public async Task CanonicalPart30WssRejectsInvalidReservedRequestBeforeUpgradeAsync(
            string path, string query, bool flowConfigured, bool uacp, int status)
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            await using HttpsTransportListener listener = harness.CreateHttpsListener(
                [flowConfigured ? harness.Endpoint(scheme: "opc.wss") :
                    harness.Endpoint(Profiles.UaWssTransport, "/services", "opc.wss")], flowConfigured);
            using var socket = new CanonicalPart30WebSocket();
            var feature = new CanonicalPart30WebSocketFeature(socket);
            DefaultHttpContext http = CanonicalPart30Http(feature, path, query, uacp);
            using Stream responseBody = http.Response.Body;

            await listener.AcceptWebSocketAsync(http).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(http.Response.StatusCode, Is.EqualTo(status));
                Assert.That(http.Response.Body.Length, Is.GreaterThan(0));
                Assert.That(feature.Upgrades, Is.Zero);
                Assert.That(socket.Sent, Is.Empty);
            });
            harness.AssertNoDispatch();
        }

        [TestCase("/pubsub/uasc")]
        [TestCase("/flow/v1/shared")]
        [TestCase("/services")]
        [CancelAfter(60000)]
        public async Task CanonicalPart30WssAcceptsExactRequestAndHelloResourceAsync(string resource)
        {
            await CanonicalPart30WssHelloAsync(resource, resource, true).ConfigureAwait(false);
        }

        [TestCase("/pubsub/uasc", "/flow/v1/shared")]
        [TestCase("/flow/v1/shared", "/pubsub/uasc")]
        [TestCase("/pubsub/uasc", "/services")]
        [TestCase("/services", "/pubsub/uasc")]
        [CancelAfter(60000)]
        public async Task CanonicalPart30WssRejectsRequestResourceAndHelloResourceMismatchAsync(
            string requestResource, string helloResource)
        {
            await CanonicalPart30WssHelloAsync(requestResource, helloResource, false).ConfigureAwait(false);
        }

        private static async Task CanonicalPart30WssHelloAsync(
            string requestResource, string helloResource, bool accepted)
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            await using HttpsTransportListener listener = harness.CreateHttpsListener([
                harness.Endpoint(DataChannelProfiles.SharedUasc, "/flow/v1/shared", "opc.wss"),
                harness.Endpoint(Profiles.UaWssTransport, "/services", "opc.wss"),
                harness.Endpoint(scheme: "opc.wss")]);
            using var socket = new CanonicalPart30WebSocket();
            var feature = new CanonicalPart30WebSocketFeature(socket);
            DefaultHttpContext http = CanonicalPart30Http(feature, requestResource);
            using Stream responseBody = http.Response.Body;
            socket.Enqueue(CanonicalPart30Hello("opc.wss://localhost" + helloResource));
            Task handling = listener.AcceptWebSocketAsync(http);
            try
            {
                byte[] response = await socket.FirstSent.Task.WaitAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                if (accepted)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(response),
                            Is.EqualTo(TcpMessageType.Acknowledge));
                        Assert.That(response, Has.Length.EqualTo(28));
                        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(12)), Is.EqualTo(8192));
                    });
                }
                else
                {
                    CanonicalPart30AssertError(response, StatusCodes.BadTcpEndpointUrlInvalid);
                }
                Assert.Multiple(() =>
                {
                    Assert.That(feature.Upgrades, Is.EqualTo(1));
                    Assert.That(feature.SubProtocol, Is.EqualTo("opcua+uacp"));
                    Assert.That(http.Response.StatusCode, Is.EqualTo(101));
                });
            }
            finally
            {
                socket.CompletePeer();
                await handling.WaitAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            }
            Assert.That(socket.Sent.Select(chunk => BinaryPrimitives.ReadUInt32LittleEndian(chunk)),
                Is.EqualTo([accepted ? TcpMessageType.Acknowledge : TcpMessageType.Error]));
            harness.AssertNoDispatch();
        }

        private static DefaultHttpContext CanonicalPart30Http(
            CanonicalPart30WebSocketFeature feature, string path, string query = "", bool uacp = true)
        {
            var http = new DefaultHttpContext();
            http.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpWebSocketFeature>(feature);
            http.Request.Scheme = "https";
            http.Request.Protocol = "HTTP/1.1";
            http.Request.Path = path;
            http.Request.QueryString = new QueryString(query);
#if NET8_0_OR_GREATER
            http.Request.Headers.SecWebSocketProtocol = uacp ? "opcua+uacp" : "unsupported";
#else
            http.Request.Headers["Sec-WebSocket-Protocol"] = uacp ? "opcua+uacp" : "unsupported";
#endif
            http.Response.Body = new MemoryStream();
            feature.Context = http;
            return http;
        }

        private sealed class CanonicalPart30WebSocketFeature(CanonicalPart30WebSocket socket) :
            Microsoft.AspNetCore.Http.Features.IHttpWebSocketFeature
        {
            public bool IsWebSocketRequest => true;
            public int Upgrades { get; private set; }
            public string? SubProtocol { get; private set; }
            public HttpContext Context { get; set; } = null!;

            public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
            {
                Upgrades++;
                SubProtocol = context.SubProtocol;
                Context.Response.StatusCode = 101;
                return Task.FromResult<WebSocket>(socket);
            }
        }

        /// <summary>
        /// Only binary in-process frames; no WebSocket client, host, socket or TLS implementation.
        /// </summary>
        private sealed class CanonicalPart30WebSocket : WebSocket
        {
            public override WebSocketCloseStatus? CloseStatus => WebSocketCloseStatus.NormalClosure;
            public override string CloseStatusDescription => "managed peer completed";
            public override WebSocketState State => m_state;
            public override string SubProtocol => "opcua+uacp";
            public ConcurrentQueue<byte[]> Sent { get; } = new();

            public TaskCompletionSource<byte[]> FirstSent { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Enqueue(byte[] frame)
            {
                Assert.That(m_incoming.Writer.TryWrite(frame), Is.True);
            }

            public void CompletePeer()
            {
                m_incoming.Writer.TryComplete();
            }

            public override async Task<WebSocketReceiveResult> ReceiveAsync(
                ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                if (!await m_incoming.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    m_state = WebSocketState.CloseReceived;
                    return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
                }
                byte[] frame = await m_incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                Assert.That(frame, Has.Length.LessThanOrEqualTo(buffer.Count));
                frame.AsSpan().CopyTo(buffer.AsSpan());
                return new WebSocketReceiveResult(frame.Length, WebSocketMessageType.Binary, true);
            }

            public override Task SendAsync(
                ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.That(messageType, Is.EqualTo(WebSocketMessageType.Binary));
                Assert.That(endOfMessage, Is.True);
                byte[] copy = [.. buffer];
                Sent.Enqueue(copy);
                FirstSent.TrySetResult(copy);
                return Task.CompletedTask;
            }

            public override Task CloseAsync(
                WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            {
                Abort();
                return Task.CompletedTask;
            }

            public override Task CloseOutputAsync(
                WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            {
                return CloseAsync(closeStatus, statusDescription, cancellationToken);
            }

            public override void Abort()
            {
                m_state = WebSocketState.Closed;
                CompletePeer();
            }

            public override void Dispose()
            {
                Abort();
            }

            private readonly Channel<byte[]> m_incoming = Channel.CreateBounded<byte[]>(4);
            private WebSocketState m_state = WebSocketState.Open;
        }

        private static EndpointDescription CanonicalPart30Endpoint(
            string profile, string resource, string scheme = "opc.tcp")
        {
            return new EndpointDescription
            {
                EndpointUrl = $"{scheme}://localhost{resource}",
                TransportProfileUri = profile,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
        }

        private sealed class CanonicalPart30SelectionChannel(List<EndpointDescription> endpoints) :
            TcpListenerChannel(
                "canonical-selection",
                new Mock<ITcpChannelListener>(MockBehavior.Strict).Object,
                new BufferManager("canonical-selection", 8192, NUnitTelemetryContext.Create()),
                new ChannelQuotas(ServiceMessageContext.Create(NUnitTelemetryContext.Create())),
                new Mock<ICertificateRegistry>().Object,
                endpoints,
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider())
        {
            public bool Select(string url)
            {
                return SetEndpointUrl(url);
            }

            public EndpointDescription[] Candidates =>
                [.. CanonicalPart30PrivateField<List<EndpointDescription>>(this, typeof(TcpListenerChannel),
                    "m_endpointCandidates")];
        }

        private static T CanonicalPart30PrivateField<T>(object instance, Type type, string name)
        {
            System.Reflection.FieldInfo? field = type.GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"The narrow observation seam {type.Name}.{name} must exist.");
            return (T)field!.GetValue(instance)!;
        }

        private static byte[] CanonicalPart30Hello(string? url, int omittedLength = 0)
        {
            byte[] resource = url == null ? [] : Encoding.UTF8.GetBytes(url);
            byte[] hello = new byte[32 + resource.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(hello, TcpMessageType.Hello);
            BinaryPrimitives.WriteInt32LittleEndian(hello.AsSpan(4), hello.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(12), 8192);
            BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(16), 8192);
            BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(20), 65536);
            BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(24), 8);
            BinaryPrimitives.WriteInt32LittleEndian(hello.AsSpan(28), url == null ? omittedLength : resource.Length);
            resource.CopyTo(hello, 32);
            return hello;
        }

        private static void CanonicalPart30AssertError(byte[] response, StatusCode status)
        {
            Assert.Multiple(() =>
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(response), Is.EqualTo(TcpMessageType.Error));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(4)), Is.EqualTo(response.Length));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(8)), Is.EqualTo((uint)status));
            });
        }

        /// <summary>
        /// Owns only managed in-memory UASC, certificate stores and application budgets.
        /// It never constructs SocketHarness or a default/network connector.
        /// </summary>
        private sealed class CanonicalPart30InMemoryHarness : IAsyncDisposable
        {
            public CanonicalPart30InMemoryHarness(bool allowIntegrity = false)
            {
                m_allowIntegrity = allowIntegrity;
                Telemetry = NUnitTelemetryContext.Create(logLevel: Microsoft.Extensions.Logging.LogLevel.Warning);
                Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
                m_stores = new InMemoryStoreProvider();
                ServerCertificates = new CertificateManager(Telemetry, [m_stores], -1, null, Clock);
                ClientCertificates = new CertificateManager(Telemetry, [m_stores], -1, null, Clock);
                m_serverCertificate = CreateCertificate(ServerUri, "CanonicalServer");
                m_clientCertificate = CreateCertificate(ClientUri, "CanonicalClient");
                ServerFactory = new ContextFactory(Telemetry, ClientUri,
                    DataChannelCertificateIdentity.GetKeyThumbprint(m_clientCertificate), allowIntegrity)
                { Clock = Clock };
                ClientFactory = new ContextFactory(Telemetry, ServerUri,
                    DataChannelCertificateIdentity.GetKeyThumbprint(m_serverCertificate), allowIntegrity)
                { Clock = Clock };
                Listener = new TcpTransportListener(Telemetry, Clock);
                Callback = new Mock<ITransportListenerCallback>(MockBehavior.Strict);
                Callback.Setup(callback => callback.ReportAuditOpenSecureChannelEvent(
                    It.IsAny<string>(), It.IsAny<EndpointDescription>(), It.IsAny<OpenSecureChannelRequest>(),
                    It.IsAny<Certificate>(), It.IsAny<Exception>()));
                Callback.Setup(callback => callback.ProcessRequestAsync(
                        It.IsAny<SecureChannelContext>(), It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                    .Returns((SecureChannelContext _, IServiceRequest request, CancellationToken _) =>
                    {
                        ServiceHandles.Enqueue(request.RequestHeader.RequestHandle);
                        Assert.That(request, Is.TypeOf<ReadRequest>());
                        return new ValueTask<IServiceResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader
                            {
                                RequestHandle = request.RequestHeader.RequestHandle,
                                ServiceResult = StatusCodes.Good
                            },
                            Results = [new DataValue(new Variant(ObjectIds.Server))]
                        });
                    });
            }

            public ITelemetryContext Telemetry { get; }
            public FakeTimeProvider Clock { get; }
            public CertificateManager ServerCertificates { get; }
            public CertificateManager ClientCertificates { get; }
            public ContextFactory ServerFactory { get; }
            public ContextFactory ClientFactory { get; }
            public TcpTransportListener Listener { get; }
            public Mock<ITransportListenerCallback> Callback { get; }
            public ConcurrentQueue<uint> ServiceHandles { get; } = new();
            public ConcurrentQueue<byte[]> ServerWire { get; } = new();
            public static CancellationToken Token => TestContext.CurrentContext.CancellationToken;
            public ProbeClientChannel? Client { get; private set; }
            public TcpListenerChannel? Accepted { get; private set; }
            public ConcurrentQueue<byte[]> ReverseClientWire { get; } = new();
            public (Guid ContextId, ulong Generation) ReverseInitialServerIdentity { get; private set; }
            public (Guid ContextId, ulong Generation) ReverseInitialClientIdentity { get; private set; }

            public EndpointDescription Endpoint(
                string profile = DataChannelProfiles.PubSubUasc,
                string resource = "/pubsub/uasc", string scheme = "opc.tcp")
            {
                EndpointDescription endpoint = CanonicalPart30Endpoint(profile, resource, scheme);
                endpoint.SecurityMode = m_allowIntegrity ? MessageSecurityMode.Sign : MessageSecurityMode.SignAndEncrypt;
                endpoint.ServerCertificate = new ByteString(m_serverCertificate.RawData);
                endpoint.Server = new ApplicationDescription
                {
                    ApplicationUri = ServerUri,
                    ApplicationType = ApplicationType.Server,
                    ApplicationName = new LocalizedText("CanonicalServer")
                };
                return endpoint;
            }

            public void Open(List<EndpointDescription>? descriptions = null)
            {
                Listener.OpenExternal(new Uri("opc.tcp://localhost/"),
                    Settings(descriptions ?? [Endpoint()]), Callback.Object);
            }

            public HttpsTransportListener CreateHttpsListener(
                List<EndpointDescription> descriptions, bool dataChannels = true)
            {
                var listener = new HttpsTransportListener("opc.wss", Telemetry);
                System.Reflection.MethodInfo? initialize = typeof(HttpsTransportListener).GetMethod(
                    "Initialize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, [typeof(Uri), typeof(TransportListenerSettings), typeof(ITransportListenerCallback)], null);
                Assert.That(initialize, Is.Not.Null);
                try
                {
                    initialize!.Invoke(listener, [
                        new Uri("opc.wss://localhost/"), Settings(descriptions, dataChannels), Callback.Object]);
                    return listener;
                }
                catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
            }

            public async Task<ProbeClientChannel> ConnectAsync(
                bool confirmProfile = true, MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
                string profile = DataChannelProfiles.PubSubUasc, string resource = "/pubsub/uasc")
            {
                await TrustAsync(ServerCertificates, "server", m_clientCertificate).ConfigureAwait(false);
                await TrustAsync(ClientCertificates, "client", m_serverCertificate).ConfigureAwait(false);
                using CertificateCollection issuers = [];
                await ServerCertificates.UpdateApplicationCertificateAsync(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType, m_serverCertificate, issuers, Token)
                    .ConfigureAwait(false);
                Open([Endpoint(profile, resource)]);
                EndpointDescription endpoint = Endpoint(profile, resource);
                if (!confirmProfile)
                {
                    endpoint.TransportProfileUri = Profiles.UaTcpTransport;
                }
                endpoint.SecurityMode = mode;
                if (mode == MessageSecurityMode.None)
                {
                    endpoint.SecurityPolicyUri = SecurityPolicies.None;
                }
                var wire = new ConcurrentQueue<byte[]>();
                var transport = new UascStreamByteTransportFactory(
                    (_, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        return new ValueTask<Stream>(CreatePeer(profile));
                    }, "Canonical-Managed-Memory", profile);
                var recorder = new RecordingTransportFactory(wire, transport);
                Client = new ProbeClientChannel(new BufferManager("canonical-client", 8192, Telemetry),
                    recorder, new ChannelQuotas(ServiceMessageContext.Create(Telemetry))
                    {
                        CertificateValidator = ClientCertificates,
                        MaxBufferSize = 8192,
                        MaxMessageSize = 1024 * 1024,
                        SecurityTokenLifetime = 60000,
                        ChannelLifetime = 60000
                    }, m_clientCertificate.AddRef(), m_serverCertificate.AddRef(), endpoint, wire, Telemetry, Clock)
                {
                    CanonicalPart30WithholdFlowHello = !confirmProfile
                };
                if (confirmProfile)
                {
                    Client.ConfigureDataChannels(ClientFactory, m_allowIntegrity);
                }
                await Client.ConnectAsync(new Uri("opc.tcp://localhost" + resource), 10000, Token).ConfigureAwait(false);
                if (confirmProfile)
                {
                    await (await ServerFactory.Created.Task.WaitAsync(Token).ConfigureAwait(false))
                        .WaitForReadyAsync(Token).ConfigureAwait(false);
                }
                return Client;
            }

            public async Task<UascDataChannelConnection> ConnectReverseAsync()
            {
                await TrustAsync(ServerCertificates, "server", m_clientCertificate).ConfigureAwait(false);
                await TrustAsync(ClientCertificates, "client", m_serverCertificate).ConfigureAwait(false);
                using CertificateCollection issuers = [];
                await ServerCertificates.UpdateApplicationCertificateAsync(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType, m_serverCertificate, issuers, Token)
                    .ConfigureAwait(false);
                Listener.OpenExternal(new Uri("opc.tcp://localhost/pubsub/uasc"),
                    Settings([Endpoint()]), Callback.Object);
                ChannelQuotas quotas = CanonicalPart30PrivateField<ChannelQuotas>(
                    Listener, typeof(TcpTransportListener), "m_quotas");
                System.Reflection.MethodInfo? create = typeof(TcpTransportListener).GetMethod(
                    "CreateAcceptedChannel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, [typeof(ChannelQuotas)], null);
                Assert.That(create, Is.Not.Null);
                var server = (TcpServerChannel)create!.Invoke(Listener, [quotas])!;
                Accepted = server;

                var waiting = new TaskCompletionSource<ITransportWaitingConnection>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var reverseUrl = new Uri("opc.tcp://localhost/reverse");
                m_reverseListener = new TcpTransportListener(Telemetry, Clock);
                TransportListenerSettings reverseSettings = Settings([], dataChannels: false);
                reverseSettings.ReverseConnectListener = true;
                reverseSettings.AcceptedTransportDecorator = transport => new RecordingTransport(transport, ReverseClientWire);
                m_reverseListener.ConnectionWaiting += (_, args) =>
                {
                    args.Accepted = true;
                    waiting.TrySetResult(args);
                    return Task.CompletedTask;
                };
                m_reverseListener.OpenExternal(reverseUrl, reverseSettings, Callback.Object);
                var toServer = Channel.CreateBounded<byte[]>(32);
                var toClient = Channel.CreateBounded<byte[]>(32);
                var connector = new UascStreamByteTransportFactory((url, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Assert.That(url, Is.EqualTo(reverseUrl));
                    m_reverseListener.AcceptTransport(
                        new AcceptedStreamFactory(new MemoryDuplexStream(toClient, toServer), DataChannelProfiles.PubSubUasc),
                        ClientCertificates, 8192);
                    return new ValueTask<Stream>(new MemoryDuplexStream(toServer, toClient));
                }, "Canonical-Reverse-Managed-Memory", DataChannelProfiles.PubSubUasc);
                BufferManager buffers = CanonicalPart30PrivateField<BufferManager>(
                    Listener, typeof(TcpTransportListener), "m_bufferManager");
                var outbound = new RecordingTransport(connector.Create(buffers, 8192, Telemetry), ServerWire);
                WriteBarrier hello = outbound.PauseNext(TcpMessageType.Message, dataOnly: false);
                server.BeginReverseConnect(1, reverseUrl, outbound, result =>
                {
                    try
                    {
                        server.EndReverseConnect(result);
                        completed.TrySetResult(true);
                    }
                    catch (Exception exception)
                    {
                        completed.TrySetException(exception);
                    }
                }, server, 10000);
                ITransportWaitingConnection accepted = await waiting.Task.WaitAsync(Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(accepted.ServerUri, Is.EqualTo(ServerUri));
                    Assert.That(accepted.EndpointUrl, Is.EqualTo(new Uri("opc.tcp://localhost/pubsub/uasc")));
                });
                var acceptedOnly = new UascStreamByteTransportFactory(
                    (_, _) => throw new InvalidOperationException("Reverse Connect must use the actual RHE handoff."),
                    "Canonical-Reverse-Accepted-Only", DataChannelProfiles.PubSubUasc);
                var client = new UascDataChannelClient(new CanonicalPart30ReverseChannelFactory(acceptedOnly, Clock), Telemetry);
                Task<UascDataChannelConnection> connecting = client.ConnectAsync(accepted, new TransportChannelSettings
                {
                    Description = Endpoint(),
                    Configuration = EndpointConfiguration.Create(),
                    ClientCertificate = m_clientCertificate.AddRef(),
                    ServerCertificate = m_serverCertificate.AddRef(),
                    CertificateValidator = ClientCertificates,
                    NamespaceUris = new NamespaceTable(),
                    Factory = EncodeableFactory.Create(),
                    DataChannels = ClientFactory
                }, Token).AsTask();
                try
                {
                    await hello.Entered.Task.WaitAsync(Token).ConfigureAwait(false);
                    DataChannelContext remote = await ServerFactory.Created.Task.WaitAsync(Token).ConfigureAwait(false);
                    DataChannelContext local = await ClientFactory.Created.Task.WaitAsync(Token).ConfigureAwait(false);
                    ReverseInitialServerIdentity = CanonicalPart30UnconfirmedIdentity(remote);
                    ReverseInitialClientIdentity = CanonicalPart30UnconfirmedIdentity(local);
                    Assert.Multiple(() =>
                    {
                        Assert.That(remote.IsReady, Is.False);
                        Assert.That(local.IsReady, Is.False);
                        Assert.That(ReverseClientWire.Any(chunk => TcpMessageType.IsType(
                            BinaryPrimitives.ReadUInt32LittleEndian(chunk), TcpMessageType.Message)), Is.False,
                            "The accepting client must wait for the physical initiator's HELLO.");
                    });
                }
                finally
                {
                    hello.Release();
                    m_reverseConnection = await connecting.ConfigureAwait(false);
                }
                await completed.Task.WaitAsync(Token).ConfigureAwait(false);
                return await connecting.ConfigureAwait(false);
            }

            public async Task<byte[]> ExchangeHelloAsync(byte[] hello)
            {
                using Stream peer = CreatePeer();
#if NETCOREAPP3_1_OR_GREATER
                await peer.WriteAsync(hello.AsMemory(), Token).ConfigureAwait(false);
#else
                await peer.WriteAsync(hello, 0, hello.Length, Token).ConfigureAwait(false);
#endif
                byte[] header = new byte[8];
                await ReadExactlyAsync(peer, header).ConfigureAwait(false);
                int size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
                Assert.That(size, Is.InRange(12, 8192));
                byte[] body = new byte[size - 8];
                await ReadExactlyAsync(peer, body).ConfigureAwait(false);
                byte[] chunk = [.. header, .. body];
                Assert.That(ServerWire.Select(bytes => BinaryPrimitives.ReadUInt32LittleEndian(bytes)),
                    Is.EqualTo([TcpMessageType.Error]));
                return chunk;
            }

            public void AssertNoDispatch()
            {
                Assert.Multiple(() =>
                {
                    Assert.That(ServerFactory.Contexts, Is.Empty);
                    Assert.That(ClientFactory.Contexts, Is.Empty);
                    Assert.That(ServiceHandles, Is.Empty);
                    Assert.That(ServerFactory.Budget.Usage, Is.Default);
                    Assert.That(ClientFactory.Budget.Usage, Is.Default);
                });
                Callback.VerifyNoOtherCalls();
            }

            public async ValueTask DisposeAsync()
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                foreach (DataChannelContext context in ClientFactory.Contexts.Concat(ServerFactory.Contexts))
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                }
                Client?.Dispose();
                if (m_reverseConnection != null)
                {
                    await m_reverseConnection.DisposeAsync().ConfigureAwait(false);
                }
                Accepted?.Dispose();
                if (m_reverseListener != null)
                {
                    await m_reverseListener.DisposeAsync().ConfigureAwait(false);
                }
                await Listener.DisposeAsync().ConfigureAwait(false);
                ServerCertificates.Dispose();
                ClientCertificates.Dispose();
                m_stores.Dispose();
                m_serverCertificate.Dispose();
                m_clientCertificate.Dispose();
            }

            private TransportListenerSettings Settings(List<EndpointDescription> descriptions, bool dataChannels = true)
            {
                var configuration = EndpointConfiguration.Create();
                configuration.MaxBufferSize = 8192;
                configuration.OperationTimeout = 10000;
                configuration.SecurityTokenLifetime = 60000;
                configuration.ChannelLifetime = 60000;
                return new TransportListenerSettings
                {
                    Descriptions = descriptions,
                    Configuration = configuration,
                    CertificateValidator = ServerCertificates,
                    ServerCertificates = ServerCertificates,
                    NamespaceUris = new NamespaceTable(),
                    Factory = EncodeableFactory.Create(),
                    DataChannels = dataChannels ? ServerFactory : null,
                    AllowDataChannelIntegrity = m_allowIntegrity,
                    AcceptedTransportDecorator = transport => new RecordingTransport(transport, ServerWire),
                    MaxChannelCount = 4
                };
            }

            private MemoryDuplexStream CreatePeer(string profile = DataChannelProfiles.PubSubUasc)
            {
                var toServer = Channel.CreateBounded<byte[]>(32);
                var toClient = Channel.CreateBounded<byte[]>(32);
                var client = new MemoryDuplexStream(toClient, toServer);
                var server = new MemoryDuplexStream(toServer, toClient);
                Accepted = Listener.AcceptTransport(
                    new AcceptedStreamFactory(server, profile), ServerCertificates, 8192);
                return client;
            }

            private static async Task ReadExactlyAsync(Stream stream, byte[] bytes)
            {
                int offset = 0;
                while (offset < bytes.Length)
                {
#if NETCOREAPP3_1_OR_GREATER
                    int read = await stream.ReadAsync(bytes.AsMemory(offset), Token).ConfigureAwait(false);
#else
                    int read = await stream.ReadAsync(bytes, offset, bytes.Length - offset, Token).ConfigureAwait(false);
#endif
                    Assert.That(read, Is.GreaterThan(0), "A framed reply must arrive, not a handshake timeout.");
                    offset += read;
                }
            }

            private static async Task TrustAsync(CertificateManager manager, string name, Certificate certificate)
            {
                manager.RegisterTrustList(TrustListIdentifier.Peers, "InMemory:" + name);
                using ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers);
                await store.AddAsync(certificate, ct: Token).ConfigureAwait(false);
            }

            private static Certificate CreateCertificate(string applicationUri, string name)
            {
                return DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                    applicationUri, name, "CN=" + name, ["localhost"])
                    .SetNotBefore(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                    .SetNotAfter(new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                    .SetRSAKeySize(2048).CreateForRSA();
            }

            private const string ServerUri = "urn:opcfoundation:tests:canonical-server";
            private const string ClientUri = "urn:opcfoundation:tests:canonical-client";
            private readonly InMemoryStoreProvider m_stores;
            private readonly Certificate m_serverCertificate;
            private readonly Certificate m_clientCertificate;
            private readonly bool m_allowIntegrity;
            private TcpTransportListener? m_reverseListener;
            private UascDataChannelConnection? m_reverseConnection;
            private bool m_disposed;
        }

        private sealed class CanonicalPart30ReverseChannelFactory(
            IUaSCByteTransportFactory factory, TimeProvider clock) : ITransportChannelFactory
        {
            public string UriScheme => "opc.tcp";

            public ITransportChannel Create(ITelemetryContext telemetry)
            {
                return new UaSCUaBinaryTransportChannel(factory, telemetry, clock);
            }
        }

        private static (Guid ContextId, ulong Generation) CanonicalPart30UnconfirmedIdentity(DataChannelContext context)
        {
            Lock gate = CanonicalPart30PrivateField<Lock>(context, typeof(DataChannelContext), "m_gate");
            lock (gate)
            {
                return (CanonicalPart30PrivateField<Guid>(context, typeof(DataChannelContext), "m_contextId"),
                    CanonicalPart30PrivateField<ulong>(context, typeof(DataChannelContext), "m_generation"));
            }
        }

        [Test]
        [CancelAfter(60000)]
        public async Task CanonicalPart30ProtectedHandshakePublishesLiteralHelloOpcodeAsync()
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            ProbeClientChannel client = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task.ConfigureAwait(false);
            CanonicalPart30AssertReady(harness, client.DataChannels!, remote);
            byte[][] requests = [.. client.WireChunks];
            byte[][] responses = [.. client.Recorder.Transport!.ReceivedChunks];
            var carriers = new List<byte>();
            Assert.Multiple(() =>
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(requests[0]), Is.EqualTo(TcpMessageType.Hello));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(responses[0]), Is.EqualTo(TcpMessageType.Acknowledge));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(requests[1]),
                    Is.EqualTo(TcpMessageType.Open | TcpMessageType.Final));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(responses[1]),
                    Is.EqualTo(TcpMessageType.Open | TcpMessageType.Final));
                Assert.That(harness.ServiceHandles, Is.Empty);
            });
            foreach (bool isRequest in new[] { true, false })
            {
                byte[] capture = (isRequest ? requests : responses).First(chunk =>
                    TcpMessageType.IsType(BinaryPrimitives.ReadUInt32LittleEndian(chunk), TcpMessageType.Message));
                byte[] original = (byte[])capture.Clone();
                (ByteString Body, uint RequestId, uint Sequence) = await client.CanonicalPart30DecodeAsync(
                    capture, isRequest, CanonicalPart30InMemoryHarness.Token)
                    .ConfigureAwait(false);
                carriers.Add(Body.Span[15]);
                Assert.Multiple(() =>
                {
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(capture),
                        Is.EqualTo(TcpMessageType.Message | TcpMessageType.Final));
                    Assert.That(RequestId, Is.Zero, "UASC RequestId is not the control operation id.");
                    Assert.That(Sequence, Is.GreaterThan(0));
                    Assert.That(Body.Span[0], Is.Zero);
                    Assert.That(Body.Span[1], Is.EqualTo(255));
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(Body.Span[2..]), Is.Zero);
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(Body.Span[6..]), Is.EqualTo(1));
                    Assert.That(Body.Span[10], Is.Zero, "The compact HELLO opcode is literal 0.");
                    Assert.That(capture, Is.EqualTo(original), "Decryption must not mutate a recorded or live buffer.");
                });
                var record = new FlowRecord(
                    FlowRecordKind.Control,
                    BinaryPrimitives.ReadUInt32LittleEndian(Body.Span[2..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(Body.Span[6..]),
                    Body[10..]);
                FlowControl control = FlowControlCodec.Decode(in record, new());
                Assert.That(control, Is.TypeOf<FlowHello>());
                var hello = (FlowHello)control;
                Assert.Multiple(() =>
                {
                    Assert.That(hello.Context, Is.EqualTo(remote.Identity.ContextId));
                    Assert.That(hello.Version, Is.EqualTo(1));
                    Assert.That(hello.Application, Is.EqualTo(isRequest
                        ? client.DataChannels!.Identity.Peer.LocalApplicationUri
                        : remote.Identity.Peer.LocalApplicationUri));
                    Assert.That(hello.Protection, Is.EqualTo(FlowWireProtection.Confidential));
                });
            }
            await CanonicalPart30ServiceAsync(harness, 101).ConfigureAwait(false);
            Assert.That(carriers, Is.EqualTo(new byte[] { 0, 0 }),
                "Both actual HELLO bodies must use ordinary UASC carrier discriminator 0 (Part30 0.3.0).");
        }

        [TestCase((byte)0, false)]
        [TestCase((byte)1, false)]
        [TestCase((byte)0, true)]
        [CancelAfter(60000)]
        public async Task CanonicalPart30IncomingHelloRequiresCanonicalCarrierAndVersionAsync(
            byte carrier, bool unsupportedVersion)
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            ProbeClientChannel client = await harness.ConnectAsync(confirmProfile: false).ConfigureAwait(false);
            client.CanonicalPart30VerifyWithheldHello = true;
            var transportClosed = new TaskCompletionSource<ServiceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.SetStateChangedCallback((_, state, result) =>
            {
                if (state == TcpChannelState.Closed)
                {
                    transportClosed.TrySetResult(result);
                }
            });
            DataChannelContext remote = await harness.ServerFactory.Created.Task.WaitAsync(CanonicalPart30InMemoryHarness.Token)
                .ConfigureAwait(false);
            var closed = new TaskCompletionSource<DataChannelResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            remote.Closed += result => closed.TrySetResult(result);
            Assert.Multiple(() =>
            {
                Assert.That(harness.Accepted!.EndpointDescription!.TransportProfileUri,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/pubsub-uasc"));
                Assert.That(harness.Accepted.EndpointDescription.EndpointUrl, Does.EndWith("/pubsub/uasc"));
                Assert.That(remote.CarrierKind, Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(remote.IsReady, Is.False);
                Assert.That(harness.ServerFactory.Contexts, Has.Count.EqualTo(1));
                Assert.That(harness.ServiceHandles, Is.Empty);
            });

            // Each negative case changes only the carrier or version of an otherwise valid HELLO.
            var contextId = new Guid("11112233-4455-6677-8899-aabbccddeeff");
            FlowRecord record = FlowControlCodec.Encode(new FlowHello(
                1, (FlowCarrier)carrier, "urn:opcfoundation:tests:canonical-client", contextId, 1,
                ByteString.FromHexString(new string('3', 64)), FlowWireProtection.Confidential,
                4096, 8192, 4), 0, 1, new());
            ByteString body = UascFlowMessageCodec.Encode(
                (FlowCarrier)carrier, 0, in record, new(), out uint requestId);
            byte[] literalBody = body.ToArray();
            literalBody[15] = carrier;
            literalBody[11] = unsupportedVersion ? (byte)2 : (byte)1;
            body = new ByteString(literalBody);
            Assert.Multiple(() =>
            {
                Assert.That(requestId, Is.Zero);
                Assert.That(body.Span[10], Is.Zero);
                Assert.That(body.Span[15], Is.EqualTo(carrier));
            });
            await client.SendAuthenticatedBodyAsync(
                TcpMessageType.Message, requestId, body.ToArray(), false, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            Assert.That(client.WireChunks.Count(chunk =>
                TcpMessageType.IsType(BinaryPrimitives.ReadUInt32LittleEndian(chunk), TcpMessageType.Message)),
                Is.EqualTo(1), "The HELLO must traverse the real encrypted UASC writer and server ingress.");

            if (carrier == 0 && !unsupportedVersion)
            {
                await remote.WaitForReadyAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                await CanonicalPart30ServiceAsync(harness, 701).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(remote.IsReady, Is.True);
                    Assert.That(remote.Identity.ContextId, Is.EqualTo(contextId));
                    Assert.That(remote.Identity.PeerChallenge, Is.EqualTo(ByteString.FromHexString(new string('3', 64))));
                    Assert.That(remote.Identity.Peer.Protection, Is.EqualTo(DataChannelProtectionMode.Confidential));
                    Assert.That(harness.ServiceHandles, Is.EqualTo(new uint[] { 701 }));
                    Assert.That(client.CanonicalPart30VerifiedHellos, Is.EqualTo([(2u, (byte)0)]));
                });
            }
            else
            {
                DataChannelException refused = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await remote.WaitForReadyAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false)).ConfigureAwait(false))!;
                Assert.That(refused.Result, Is.EqualTo(DataChannelResult.Closed));
                Assert.That(await closed.Task.WaitAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Closed));
                Assert.That(await remote.ReceiveUascAsync(
                    DataChannelUascMessageKind.Msg, (byte)'F', 0x80000001,
                    ByteString.FromHexString("00fe910211000000dead"),
                    CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Closed));
                // Context closure precedes asynchronous carrier shutdown. Observe that shutdown before probing Services.
                ServiceResult transportFailure = await transportClosed.Task.WaitAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                Assert.That(transportFailure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                CanonicalPart30AssertError(harness.ServerWire.Single(chunk =>
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk) == TcpMessageType.Error),
                    StatusCodes.BadDecodingError);
                ServiceResultException serviceRefused = (await Assert.CatchAsync<ServiceResultException>(async () =>
                    await client.SendRequestAsync(Read(702), 10000, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
                Assert.That(serviceRefused.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
                Assert.Multiple(() =>
                {
                    Assert.That(client.CanonicalPart30VerifiedHellos, Is.Empty);
                    Assert.That(harness.ServiceHandles, Is.Empty);
                    Assert.That(harness.ServerFactory.Contexts.All(context => !context.IsReady), Is.True);
                    Assert.That(harness.ServerFactory.Contexts.All(context => context.Channels.IsEmpty), Is.True);
                });
            }
            Assert.Multiple(() =>
            {
                Assert.That(remote.Channels.IsEmpty, Is.True);
                Assert.That(harness.ServerFactory.Authorizations, Is.Zero);
                Assert.That(harness.ServerFactory.Budget.Usage.Messages, Is.Zero);
                Assert.That(harness.ClientFactory.Contexts, Is.Empty);
            });
            await harness.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(remote.IsReady, Is.False);
                Assert.That(harness.ClientFactory.Budget.Usage, Is.Default);
                Assert.That(harness.ServerFactory.Budget.Usage, Is.Default);
            });
        }

        [Test]
        [CancelAfter(60000)]
        public async Task CanonicalMsgLegacySharedProfileUsesCanonicalHelloCarrierZeroAsync()
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            ProbeClientChannel client = await harness.ConnectAsync(
                profile: DataChannelProfiles.SharedUasc, resource: "/flow/v1/shared").ConfigureAwait(false);
            DataChannelContext local = client.DataChannels!;
            DataChannelContext remote = await harness.ServerFactory.Created.Task.WaitAsync(CanonicalPart30InMemoryHarness.Token)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.Accepted!.EndpointDescription!.TransportProfileUri,
                    Is.EqualTo(DataChannelProfiles.SharedUasc));
                Assert.That(harness.Accepted.EndpointDescription.EndpointUrl,
                    Is.EqualTo("opc.tcp://localhost/flow/v1/shared"));
                Assert.That(local.IsReady, Is.True);
                Assert.That(remote.IsReady, Is.True);
                Assert.That(local.CarrierKind, Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(remote.CarrierKind, Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(local.Identity.ContextId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(remote.Identity.ContextId, Is.EqualTo(local.Identity.ContextId));
                Assert.That(local.Identity.Peer.Protection, Is.EqualTo(DataChannelProtectionMode.Confidential));
                Assert.That(remote.Identity.Peer.Protection, Is.EqualTo(DataChannelProtectionMode.Confidential));
            });
            foreach (bool isRequest in new[] { true, false })
            {
                byte[] capture = (isRequest ? client.WireChunks : client.Recorder.Transport!.ReceivedChunks)
                    .First(chunk => TcpMessageType.IsType(
                        BinaryPrimitives.ReadUInt32LittleEndian(chunk), TcpMessageType.Message));
                byte[] original = (byte[])capture.Clone();
                (ByteString Body, uint RequestId, uint Sequence) = await client.CanonicalPart30DecodeAsync(
                    capture, isRequest, CanonicalPart30InMemoryHarness.Token)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(capture),
                        Is.EqualTo(TcpMessageType.Message | TcpMessageType.Final));
                    Assert.That(RequestId, Is.Zero);
                    Assert.That(Body.Span[0], Is.Zero);
                    Assert.That(Body.Span[1], Is.EqualTo(255));
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(Body.Span[2..]), Is.Zero);
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(Body.Span[6..]), Is.EqualTo(1));
                    Assert.That(Body.Span[10], Is.Zero, "The HELLO opcode remains 0.");
                    Assert.That(Body.Span[15], Is.Zero,
                        "A legacy endpoint profile does not select a different canonical MSG HELLO table.");
                    Assert.That(capture, Is.EqualTo(original));
                });
            }
            await CanonicalPart30ServiceAsync(harness, 711).ConfigureAwait(false);
            await using CanonicalPart30FlowPair flow = await CanonicalPart30OpenFlowAsync(
                local, remote, UadpFirstResource, 17, 32, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            var payload = ByteString.FromHexString("910211000000dead");
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await CanonicalPart30ReadAsync(flow.Remote, payload, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            Assert.That(await flow.Remote.SendAsync(payload, cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await CanonicalPart30ReadAsync(flow.Local, payload, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(harness.ServiceHandles, Is.EqualTo(new uint[] { 711 }));
                Assert.That(local.IsReady, Is.True);
                Assert.That(remote.IsReady, Is.True);
            });
            await flow.DisposeAsync().ConfigureAwait(false);
            await harness.DisposeAsync().ConfigureAwait(false);
            CanonicalPart30AssertReleased(harness, local, remote);
        }

        [Test]
        [CancelAfter(60000)]
        public async Task CanonicalPart30ReverseConnectServerOwnsContextAndOddFlowIdsAsync()
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            UascDataChannelConnection connection = await harness.ConnectReverseAsync().ConfigureAwait(false);
            DataChannelContext local = connection.Context;
            DataChannelContext remote = await harness.ServerFactory.Created.Task.WaitAsync(CanonicalPart30InMemoryHarness.Token)
                .ConfigureAwait(false);
            await remote.WaitForReadyAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            CanonicalPart30AssertReady(harness, local, remote);
            Assert.Multiple(() =>
            {
                Assert.That(remote.Identity.Peer.IsTransportInitiator, Is.True);
                Assert.That(remote.Identity.Peer.IsContextInitiator, Is.True);
                Assert.That(local.Identity.Peer.IsTransportInitiator, Is.False);
                Assert.That(local.Identity.Peer.IsContextInitiator, Is.False);
                Assert.That(harness.ReverseInitialServerIdentity.ContextId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(harness.ReverseInitialServerIdentity.Generation, Is.GreaterThan(0));
                Assert.That(harness.ReverseInitialClientIdentity, Is.EqualTo((Guid.Empty, 0UL)));
                Assert.That(remote.Identity.ContextId, Is.EqualTo(harness.ReverseInitialServerIdentity.ContextId));
                Assert.That(remote.Identity.Generation, Is.EqualTo(harness.ReverseInitialServerIdentity.Generation));
                Assert.That(local.Identity.Generation, Is.EqualTo(remote.Identity.Generation));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(harness.ServerWire.First()),
                    Is.EqualTo(TcpMessageType.ReverseHello));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(harness.ReverseClientWire.First()),
                    Is.EqualTo(TcpMessageType.Hello));
                Assert.That(CanonicalPart30PrivateField<bool>(
                    harness.Accepted!, typeof(UaSCUaBinaryChannel), "m_dataChannelIsUascInitiator"), Is.False,
                    "The physical initiator remains the UASC server, not the cryptographic requester.");
            });
            foreach (bool serverOpens in new[] { true, false })
            {
                DataChannelContext opener = serverOpens ? remote : local;
                DataChannelContext acceptor = serverOpens ? local : remote;
                await using CanonicalPart30FlowPair flow = await CanonicalPart30OpenFlowAsync(
                    opener, acceptor, serverOpens ? UadpFirstResource : UadpSecondResource,
                    serverOpens ? 17u : 42u, 32, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                Assert.That(flow.Local.ChannelId, Is.EqualTo(serverOpens ? 1u : 2u));
                var payload = ByteString.FromHexString(serverOpens ? "910211000000dead" : "91022a000000beef");
                Assert.That(await flow.Local.SendAsync(payload, cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                await CanonicalPart30ReadAsync(flow.Remote, payload, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                Assert.That(await flow.Remote.SendAsync(payload, cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                await CanonicalPart30ReadAsync(flow.Local, payload, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                uint handle = serverOpens ? 721u : 722u;
                IServiceResponse response = await connection.Services!.SendRequestAsync(Read(handle), CanonicalPart30InMemoryHarness.Token)
                    .ConfigureAwait(false);
                Assert.That(response, Is.TypeOf<ReadResponse>());
                var read = (ReadResponse)response;
                Assert.Multiple(() =>
                {
                    Assert.That(read.ResponseHeader.RequestHandle, Is.EqualTo(handle));
                    Assert.That(read.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(read.Results.Count, Is.EqualTo(1));
                    Assert.That(read.Results[0].WrappedValue, Is.EqualTo(new Variant(ObjectIds.Server)));
                    Assert.That(flow.Local.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                    Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                    Assert.That(local.IsReady, Is.True);
                    Assert.That(remote.IsReady, Is.True);
                });
            }
            Assert.That(harness.ServiceHandles, Is.EqualTo(new uint[] { 721, 722 }));
            await harness.DisposeAsync().ConfigureAwait(false);
            CanonicalPart30AssertReleased(harness, local, remote);
        }

        [Test]
        [CancelAfter(60000)]
        public async Task CanonicalPart30SharedServicesAreCorrelatedAfterProtectedHelloAsync()
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            ProbeClientChannel client = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task.ConfigureAwait(false);
            Task first = CanonicalPart30ServiceAsync(harness, 103);
            Task second = CanonicalPart30ServiceAsync(harness, 907);
            await Task.WhenAll(first, second).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.ServiceHandles, Is.EquivalentTo(new uint[] { 103, 907 }));
                Assert.That(client.DataChannels!.Channels.IsEmpty, Is.True);
                Assert.That(remote.Channels.IsEmpty, Is.True, "Services must not create a flow.");
            });
            CanonicalPart30AssertReady(harness, client.DataChannels!, remote);
        }

        /// <summary>
        /// Ordinary Service request identifiers wrap before the reserved DATA namespace and skip zero.
        /// </summary>
        [TestCase(0x7FFFFFFEu, 0x7FFFFFFFu)]
        [TestCase(0x7FFFFFFFu, 1u)]
        [TestCase(0xFFFFFFFEu, 1u)]
        [TestCase(0xFFFFFFFFu, 1u)]
        [CancelAfter(60000)]
        public async Task CanonicalMsgServiceRequestIdWrapSkipsZeroAndPreservesCorrelationAsync(
            uint previousId,
            uint firstExpectedId)
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            ProbeClientChannel client = await harness.ConnectAsync().ConfigureAwait(false);
            System.Reflection.FieldInfo? counter = typeof(UaSCUaBinaryClientChannel).GetField(
                "m_lastRequestId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(counter, Is.Not.Null);
            counter!.SetValue(client, previousId);
            int before = client.WireChunks.Count;
            await CanonicalPart30ServiceAsync(harness, 201).ConfigureAwait(false);
            await CanonicalPart30ServiceAsync(harness, 202).ConfigureAwait(false);
            await CanonicalPart30ServiceAsync(harness, 203).ConfigureAwait(false);
            var requestIds = new List<uint>();
            foreach (byte[] chunk in client.WireChunks.Skip(before).Where(chunk =>
                TcpMessageType.IsType(BinaryPrimitives.ReadUInt32LittleEndian(chunk), TcpMessageType.Message)))
            {
                (ByteString Body, uint RequestId, uint Sequence) = await client.CanonicalPart30DecodeAsync(
                    chunk, true, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                requestIds.Add(RequestId);
            }
            Assert.Multiple(() =>
            {
                Assert.That(requestIds, Is.EqualTo(firstExpectedId == 0x7FFFFFFF
                    ? new uint[] { 0x7FFFFFFF, 1, 2 }
                    : [1, 2, 3]));
                Assert.That(harness.ServiceHandles, Is.EqualTo(new uint[] { 201, 202, 203 }));
                Assert.That(client.DataChannels!.IsReady, Is.True);
                Assert.That(client.DataChannels.Channels, Is.Empty);
            });
        }

        [Test]
        [CancelAfter(60000)]
        public async Task CanonicalPart30SharedFlowsPreserveUadpPayloadsAndPublisherRoutingAsync()
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            ProbeClientChannel client = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task.ConfigureAwait(false);
            await using CanonicalPart30FlowPair first = await CanonicalPart30OpenFlowAsync(
                client.DataChannels!, remote, UadpFirstResource, 17, 32, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            await using CanonicalPart30FlowPair second = await CanonicalPart30OpenFlowAsync(
                client.DataChannels!, remote, UadpSecondResource, 42, 32, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            var a = ByteString.FromHexString("910211000000dead");
            var b = ByteString.FromHexString("91022a000000beef");
            var a2 = ByteString.FromHexString("910211000000cafe");
            var b2 = ByteString.FromHexString("91022a000000babe");
            foreach (bool reverse in new[] { false, true })
            {
                DataChannel firstSender = reverse ? first.Remote : first.Local;
                DataChannel secondSender = reverse ? second.Remote : second.Local;
                DataChannel firstReceiver = reverse ? first.Local : first.Remote;
                DataChannel secondReceiver = reverse ? second.Local : second.Remote;
                Assert.That(await firstSender.SendAsync(a, cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                await CanonicalPart30ReadAsync(firstReceiver, a, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                Assert.That(secondReceiver.GetDiagnostics().MessagesReceived, Is.Zero);
                foreach ((DataChannel, ByteString) send in new[] { (secondSender, b), (firstSender, a2), (secondSender, b2), (firstSender, a) })
                {
                    Assert.That(await send.Item1.SendAsync(send.Item2, cancellationToken: CanonicalPart30InMemoryHarness.Token)
                        .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                }
                await CanonicalPart30ReadAsync(firstReceiver, a2, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                await CanonicalPart30ReadAsync(firstReceiver, a, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                await CanonicalPart30ReadAsync(secondReceiver, b, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                await CanonicalPart30ReadAsync(secondReceiver, b2, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(firstReceiver.GetDiagnostics().MessagesReceived, Is.EqualTo(3));
                    Assert.That(firstReceiver.GetDiagnostics().BytesReceived, Is.EqualTo(24));
                    Assert.That(secondReceiver.GetDiagnostics().MessagesReceived, Is.EqualTo(2));
                    Assert.That(secondReceiver.GetDiagnostics().BytesReceived, Is.EqualTo(16));
                });
            }
            await CanonicalPart30ServiceAsync(harness, 117).ConfigureAwait(false);
            Assert.That(harness.ServiceHandles, Is.EqualTo(new uint[] { 117 }));
            CanonicalPart30AssertReady(harness, client.DataChannels!, remote);
        }

        /// <summary>
        /// One operation proves the requested intersection: protected HELLO, two routed UADP flows,
        /// exact exhausted credit, concurrent Services/control, replenishment, scoped reset and cleanup.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task CanonicalPart30SharedCreditExhaustionLeavesServicesAndControlsResponsiveAsync()
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            ProbeClientChannel client = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext local = client.DataChannels!;
            DataChannelContext remote = await harness.ServerFactory.Created.Task.ConfigureAwait(false);
            CanonicalPart30AssertReady(harness, local, remote);
            await using CanonicalPart30FlowPair first = await CanonicalPart30OpenFlowAsync(
                local, remote, UadpFirstResource, 17, 16, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            await using CanonicalPart30FlowPair second = await CanonicalPart30OpenFlowAsync(
                local, remote, UadpSecondResource, 42, 16, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            Assert.That(await local.PingAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(CanonicalPart30Credit(first.Local), Is.EqualTo((16u, 16u, 0L, 0L)));
            var a = ByteString.FromHexString("910211000000dead");
            var a2 = ByteString.FromHexString("910211000000cafe");
            var b = ByteString.FromHexString("91022a000000beef");
            Assert.That(await first.Local.SendAsync(a, cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage held = (await first.Remote.ReadAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false))!;
            Assert.That(await first.Local.SendAsync(a2, cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage held2 = (await first.Remote.ReadAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(held.Payload, Is.EqualTo(a));
                Assert.That(held2.Payload, Is.EqualTo(a2));
                Assert.That(CanonicalPart30Credit(first.Local).Send, Is.Zero);
                Assert.That(CanonicalPart30Credit(first.Remote).Receive, Is.Zero);
                Assert.That(harness.ServerFactory.Budget.Usage.Messages, Is.EqualTo(2));
                Assert.That(harness.ServerFactory.Budget.Usage.BufferedBytes, Is.GreaterThanOrEqualTo(16));
                Assert.That(harness.ServerFactory.Budget.Usage.Flows, Is.EqualTo(2));
            });
            Task<DataChannelResult> pending = first.Local.SendAsync(a, cancellationToken: CanonicalPart30InMemoryHarness.Token).AsTask();

            // These round trips are explicit queue-progress barriers, not elapsed-time responsiveness checks.
            await CanonicalPart30ServiceAsync(harness, 421).ConfigureAwait(false);
            // Satisfy the documented per-context Ping rate limit with the injected clock, never a real wait.
            harness.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(await local.PingAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await second.Remote.SendAsync(b, cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await CanonicalPart30ReadAsync(second.Local, b, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pending.IsCompleted, Is.False, "No credit may be invented by Services or Ping.");
                Assert.That(CanonicalPart30Credit(first.Local).Send, Is.Zero);
                Assert.That(CanonicalPart30Credit(first.Local).SendBytes, Is.EqualTo(8));
                Assert.That(first.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(2));
                Assert.That(first.Local.GetDiagnostics().CreditStalls, Is.GreaterThan(0));
                Assert.That(harness.ServiceHandles, Is.EqualTo(new uint[] { 421 }));
            });

            held.Dispose();
            Assert.That(await pending.WaitAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage resumed = (await first.Remote.ReadAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(resumed.Payload, Is.EqualTo(a));
                Assert.That(first.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(3));
                Assert.That(CanonicalPart30Credit(first.Local).Send, Is.Zero);
                Assert.That(CanonicalPart30Credit(first.Remote).Receive, Is.Zero);
                Assert.That(harness.ServerFactory.Budget.Usage.Messages, Is.EqualTo(2));
            });
            held2.Dispose();
            resumed.Dispose();
            Assert.That(await remote.PingAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(CanonicalPart30Credit(first.Local).Send, Is.EqualTo(8),
                "Credit grants are coalesced at the half-window threshold, not eagerly restored to the full window.");
            Assert.That(CanonicalPart30Credit(first.Remote), Is.EqualTo((16u, 8u, 0L, 0L)));
            Assert.That(harness.ServerFactory.Budget.Usage.Messages, Is.Zero);

            await first.Local.ResetAsync(cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            Assert.That(await first.Remote.ReadAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false), Is.Null);
            Assert.That(await second.Local.SendAsync(b, cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await CanonicalPart30ReadAsync(second.Remote, b, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            await CanonicalPart30ServiceAsync(harness, 422).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Local.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(first.Remote.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(second.Local.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(second.Local.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(second.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(harness.ServiceHandles, Is.EqualTo(new uint[] { 421, 422 }));
            });
            await first.DisposeAsync().ConfigureAwait(false);
            await second.DisposeAsync().ConfigureAwait(false);
            await harness.DisposeAsync().ConfigureAwait(false);
            CanonicalPart30AssertReleased(harness, local, remote);
            Assert.That(await second.Local.SendAsync(b, cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Closed));
            Assert.That(harness.ServiceHandles, Is.EqualTo(new uint[] { 421, 422 }));
        }

        [Test]
        [CancelAfter(60000)]
        public async Task CanonicalPart30SharedFlowResetAndDisposalReleaseBudgetsAsync()
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            ProbeClientChannel client = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext local = client.DataChannels!;
            DataChannelContext remote = await harness.ServerFactory.Created.Task.ConfigureAwait(false);
            await using CanonicalPart30FlowPair first = await CanonicalPart30OpenFlowAsync(
                local, remote, UadpFirstResource, 17, 32, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            await using CanonicalPart30FlowPair second = await CanonicalPart30OpenFlowAsync(
                local, remote, UadpSecondResource, 42, 32, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            var b = ByteString.FromHexString("91022a000000beef");
            await first.Local.ResetAsync(cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            Assert.That(await first.Remote.ReadAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false), Is.Null);
            Assert.That(await first.Local.SendAsync(ByteString.FromHexString("910211000000dead"),
                cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Closed));
            Assert.That(await second.Remote.SendAsync(b, cancellationToken: CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await CanonicalPart30ReadAsync(second.Local, b, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            await CanonicalPart30ServiceAsync(harness, 508).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(local.Channels.ToArray()!.Select(flow => flow.ChannelId),
                    Is.EqualTo([second.Local.ChannelId]));
                Assert.That(second.Local.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(harness.ClientFactory.Budget.Usage.Flows, Is.EqualTo(1));
            });
            await harness.DisposeAsync().ConfigureAwait(false);
            CanonicalPart30AssertReleased(harness, local, remote);
            DataChannelException readClosed = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                await second.Local.ReadAsync(CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(readClosed.Result, Is.EqualTo(DataChannelResult.Closed));
            DataChannelException closed = Assert.Throws<DataChannelException>(() =>
                remote.RegisterUascPublisherRoute(UadpFirstResource, new Variant(17u)))!;
            Assert.That(closed.Result, Is.EqualTo(DataChannelResult.Closed));
            Assert.That(harness.ServiceHandles, Is.EqualTo(new uint[] { 508 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(60000)]
        public async Task CanonicalPart30ServicesBeforeProtectedHelloAndUnsecuredOpnAreRejectedAsync(bool unsecured)
        {
            await using var harness = new CanonicalPart30InMemoryHarness();
            ServiceResultException failure = (await Assert.CatchAsync<ServiceResultException>(async () =>
            {
                ProbeClientChannel client = await harness.ConnectAsync(false,
                    unsecured ? MessageSecurityMode.None : MessageSecurityMode.SignAndEncrypt).ConfigureAwait(false);
                await client.SendRequestAsync(Read(901), 10000, CanonicalPart30InMemoryHarness.Token).ConfigureAwait(false);
            }).ConfigureAwait(false))!;
            StatusCode expected = unsecured ? StatusCodes.BadSecurityModeRejected : StatusCodes.BadDecodingError;
            Assert.That(failure.StatusCode, Is.EqualTo(unsecured ? expected : StatusCodes.BadSecureChannelClosed));
            Assert.Multiple(() =>
            {
                Assert.That(harness.ServiceHandles, Is.Empty);
                Assert.That(harness.ClientFactory.Contexts, Is.Empty);
                Assert.That(harness.ServerFactory.Contexts.All(context => !context.IsReady), Is.True);
                Assert.That(harness.Client!.WireChunks.Any(chunk =>
                    TcpMessageType.IsType(BinaryPrimitives.ReadUInt32LittleEndian(chunk), TcpMessageType.Open)), Is.True);
                Assert.That(harness.Client.WireChunks.Any(chunk =>
                    TcpMessageType.IsType(BinaryPrimitives.ReadUInt32LittleEndian(chunk), TcpMessageType.Message)),
                    Is.EqualTo(!unsecured), "The premature Service must reach real wire ingress.");
            });
            byte[] error = harness.ServerWire.Single(chunk =>
                BinaryPrimitives.ReadUInt32LittleEndian(chunk) == TcpMessageType.Error);
            CanonicalPart30AssertError(error, expected);
            await harness.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.ClientFactory.Budget.Usage, Is.Default);
                Assert.That(harness.ServerFactory.Budget.Usage, Is.Default);
                Assert.That(harness.ServiceHandles, Is.Empty);
            });
        }

        private static void CanonicalPart30AssertReady(
            CanonicalPart30InMemoryHarness harness, DataChannelContext local, DataChannelContext remote)
        {
            Assert.Multiple(() =>
            {
                Assert.That(local.IsReady, Is.True);
                Assert.That(remote.IsReady, Is.True);
                Assert.That(local.CarrierKind, Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(remote.CarrierKind, Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(local.Identity.ContextId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(remote.Identity.ContextId, Is.EqualTo(local.Identity.ContextId));
                Assert.That(remote.Identity.Peer.Protection, Is.EqualTo(DataChannelProtectionMode.Confidential));
                Assert.That(local.Identity.Peer.Protection, Is.EqualTo(DataChannelProtectionMode.Confidential));
                Assert.That(local.Identity.Peer.PeerApplicationUri, Is.EqualTo("urn:opcfoundation:tests:canonical-server"));
                Assert.That(remote.Identity.Peer.PeerApplicationUri, Is.EqualTo("urn:opcfoundation:tests:canonical-client"));
                Assert.That(local.Identity.Peer.PeerKeyThumbprint, Is.EqualTo(remote.Identity.Peer.LocalKeyThumbprint));
                Assert.That(remote.Identity.Peer.PeerKeyThumbprint, Is.EqualTo(local.Identity.Peer.LocalKeyThumbprint));
                Assert.That(local.Identity.LocalChallenge, Is.EqualTo(remote.Identity.PeerChallenge));
                Assert.That(local.Identity.PeerChallenge, Is.EqualTo(remote.Identity.LocalChallenge));
                Assert.That(local.Identity.LocalChallenge, Is.Not.EqualTo(local.Identity.PeerChallenge));
                Assert.That(harness.Accepted!.EndpointDescription!.TransportProfileUri,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/pubsub-uasc"));
                Assert.That(harness.Accepted.EndpointDescription.EndpointUrl, Does.EndWith("/pubsub/uasc"));
                Assert.That(harness.ClientFactory.Contexts, Has.Count.EqualTo(1));
                Assert.That(harness.ServerFactory.Contexts, Has.Count.EqualTo(1));
            });
        }

        private static void CanonicalPart30AssertReleased(
            CanonicalPart30InMemoryHarness harness, DataChannelContext local, DataChannelContext remote)
        {
            Assert.Multiple(() =>
            {
                Assert.That(local.IsReady, Is.False);
                Assert.That(remote.IsReady, Is.False);
                Assert.That(local.Channels.IsEmpty, Is.True);
                Assert.That(remote.Channels.IsEmpty, Is.True);
                Assert.That(harness.ClientFactory.Budget.Usage, Is.Default);
                Assert.That(harness.ServerFactory.Budget.Usage, Is.Default);
            });
        }

        private static async Task CanonicalPart30ServiceAsync(CanonicalPart30InMemoryHarness harness, uint handle)
        {
            IServiceResponse response = await harness.Client!.SendRequestAsync(Read(handle), 10000, CanonicalPart30InMemoryHarness.Token)
                .ConfigureAwait(false);
            Assert.That(response, Is.TypeOf<ReadResponse>());
            var read = (ReadResponse)response;
            Assert.Multiple(() =>
            {
                Assert.That(read.ResponseHeader.RequestHandle, Is.EqualTo(handle));
                Assert.That(read.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results.Count, Is.EqualTo(1));
                Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results[0].WrappedValue, Is.EqualTo(new Variant(ObjectIds.Server)));
            });
        }

        private static async Task CanonicalPart30ReadAsync(
            DataChannel flow, ByteString expected, CancellationToken token)
        {
            using DataChannelMessage? message = await flow.ReadAsync(token).ConfigureAwait(false);
            Assert.That(message, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload, Is.EqualTo(expected));
                Assert.That(flow.State, Is.EqualTo(DataChannelState.Open));
            });
        }

        private static (uint Send, uint Receive, long SendBytes, long ReceiveBytes) CanonicalPart30Credit(DataChannel flow)
        {
            Lock gate = CanonicalPart30PrivateField<Lock>(flow.Context, typeof(DataChannelContext), "m_gate");
            lock (gate)
            {
                return (flow.StateData.SendCredit, flow.StateData.ReceiveCredit,
                    flow.StateData.SendBytes, flow.StateData.ReceiveBytes);
            }
        }

        private static async Task<CanonicalPart30FlowPair> CanonicalPart30OpenFlowAsync(
            DataChannelContext local, DataChannelContext remote, string resource, uint publisher,
            uint window, CancellationToken token)
        {
            IDisposable localRoute = local.RegisterUascPublisherRoute(resource, new Variant(publisher));
            IDisposable remoteRoute = remote.RegisterUascPublisherRoute(resource, new Variant(publisher));
            DataChannelParametersDataType parameters = Parameters();
            parameters.ResourceUri = resource;
            parameters.AllowQueueExpiry = false;
            parameters.MaxMessageSize = 8;
            parameters.MaxReceiveBytes = window;
            DataChannel sender = await local.OpenAsync(parameters, cancellationToken: token).ConfigureAwait(false);
            DataChannel receiver = await AcceptAsync(remote, token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(receiver.ChannelId, Is.EqualTo(sender.ChannelId));
                Assert.That(receiver.Parameters.ResourceUri, Is.EqualTo(resource));
                Assert.That(receiver.Parameters.MaxReceiveBytes, Is.EqualTo(window));
                Assert.That(sender.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(receiver.State, Is.EqualTo(DataChannelState.Open));
            });
            return new CanonicalPart30FlowPair(sender, receiver, localRoute, remoteRoute);
        }

        private sealed class CanonicalPart30FlowPair(
            DataChannel local, DataChannel remote, IDisposable localRoute, IDisposable remoteRoute) : IAsyncDisposable
        {
            public DataChannel Local => local;
            public DataChannel Remote => remote;

            public async ValueTask DisposeAsync()
            {
                await local.DisposeAsync().ConfigureAwait(false);
                await remote.DisposeAsync().ConfigureAwait(false);
                localRoute.Dispose();
                remoteRoute.Dispose();
            }
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Measures encrypted TCP send-to-consumption cost, including queues, copies, credit and socket scheduling.
        /// Connection establishment and flow admission are excluded.
        /// </summary>
        [MemoryDiagnoser]
        public class SecuredIngressBenchmarks
        {
            /// <summary>
            /// Gets or sets the complete NetworkMessage size.
            /// </summary>
            [Params(64, 32768)]
            public int NetworkMessageBytes { get; set; }

            /// <summary>
            /// Gets or sets the numeric or string PublisherId representation.
            /// </summary>
            [Params(BuiltInType.UInt32, BuiltInType.String)]
            public BuiltInType PublisherIdType { get; set; }

            /// <summary>
            /// Establishes a SignAndEncrypt UASC channel and one admitted duplex flow.
            /// </summary>
            [GlobalSetup]
            public async Task SetupAsync()
            {
                m_harness = await SocketHarness.StartAsync(
                    CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc,
                    operationTimeout: TimeSpan.FromMinutes(10), workingDirectory: Path.GetTempPath())
                    .ConfigureAwait(false);
                try
                {
                    m_harness.ClientFactory.MaxMessageSize = 32768;
                    m_harness.ServerFactory.MaxMessageSize = 32768;
                    m_connection = await m_harness.ConnectAsync().ConfigureAwait(false);
                    DataChannelContext remote = await m_harness.ServerFactory.Created.Task
                        .WaitAsync(m_harness.Token).ConfigureAwait(false);
                    DataChannelParametersDataType parameters = Parameters();
                    parameters.MaxMessageSize = 32768;
                    parameters.MaxReceiveBytes = 131072;
                    m_sender = await m_connection.Context.OpenAsync(
                        parameters, cancellationToken: m_harness.Token).ConfigureAwait(false);
                    m_receiver = await AcceptAsync(remote, m_harness.Token).ConfigureAwait(false);
                    Variant publisher = PublisherIdType == BuiltInType.String
                        ? new Variant("publisher-with-string-identity") : new Variant(42U);
                    int headerBytes = UadpApplicationPayloadHeaderCodec.Encode(
                        publisher, ByteString.Empty, 32768).Length;
                    byte[] message = UadpApplicationPayloadHeaderCodec.Encode(
                        publisher, new ByteString(new byte[NetworkMessageBytes - headerBytes]), 32768).ToArray();
                    message[2] = 0;
                    m_payload = new ByteString(message);
                    await SendAndConsumeAsync().ConfigureAwait(false);
                }
                catch
                {
                    await CleanupAsync().ConfigureAwait(false);
                    throw;
                }
            }

            /// <summary>
            /// Encrypts, sends, verifies, reassembles and consumes one unchanged NetworkMessage.
            /// </summary>
            /// <exception cref="InvalidOperationException">The benchmark send or receive failed.</exception>
            [Benchmark]
            public async Task<int> SendAndConsumeAsync()
            {
                DataChannelResult result = await m_sender!.SendAsync(
                    m_payload, cancellationToken: m_harness!.Token).ConfigureAwait(false);
                if (result != DataChannelResult.Good)
                {
                    throw new InvalidOperationException($"Secured benchmark send failed: {result}.");
                }
                using DataChannelMessage received = await m_receiver!.ReadAsync(m_harness.Token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Secured benchmark flow closed before delivery.");
                if (received.Payload != m_payload)
                {
                    throw new InvalidOperationException("Secured benchmark changed the NetworkMessage bytes.");
                }
                return received.Payload.Length;
            }

            /// <summary>
            /// Releases flow leases, the authenticated connection and its certificate stores.
            /// </summary>
            [GlobalCleanup]
            public async Task CleanupAsync()
            {
                if (m_sender != null)
                {
                    await m_sender.DisposeAsync().ConfigureAwait(false);
                    m_sender = null;
                }
                if (m_receiver != null)
                {
                    await m_receiver.DisposeAsync().ConfigureAwait(false);
                    m_receiver = null;
                }
                if (m_connection != null)
                {
                    await m_connection.DisposeAsync().ConfigureAwait(false);
                    m_connection = null;
                }
                if (m_harness != null)
                {
                    await m_harness.DisposeAsync().ConfigureAwait(false);
                    m_harness = null;
                }
            }

            /// <summary>The authenticated socket harness.</summary>
            private SocketHarness? m_harness;

            /// <summary>The initiating UASC connection.</summary>
            private UascDataChannelConnection? m_connection;

            /// <summary>The admitted sending flow.</summary>
            private DataChannel? m_sender;

            /// <summary>The admitted receiving flow.</summary>
            private DataChannel? m_receiver;

            /// <summary>The immutable complete NetworkMessage.</summary>
            private ByteString m_payload;
        }
#endif
    }
}
