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
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Exercises profile selection and complete-message flows over genuinely authenticated sockets.
    /// </summary>
    [TestFixture]
    [Category("DataChannelSockets")]
    [NonParallelizable]
    public sealed class UascDataChannelSocketTests
    {
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc)]
#if NET8_0_OR_GREATER
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

            ByteString payload = new(new byte[] { 0, 1, 0xFE, 0xFF, 0, 0x7F });
            Assert.That(
                await outgoing.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
            Assert.That(received.Payload, Is.EqualTo(payload));
            Assert.That(
                await incoming.SendAsync(ByteString.Empty, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage empty = (await outgoing.ReadAsync(harness.Token).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(empty.Payload.Length, Is.Zero);
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
                DataChannelException refusal = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await connection.Context.OpenAsync(Parameters(), cancellationToken: harness.Token)
                        .ConfigureAwait(false)).ConfigureAwait(false))!;
                Assert.That(refusal.Result, Is.EqualTo(DataChannelResult.InvalidState));
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

        [Test]
        [CancelAfter(60000)]
        public async Task SharedCreditExhaustionDoesNotBlockServicesControlsOrAnotherFlowAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType small = Parameters();
            small.MaxMessageSize = 15;
            small.MaxReceiveBytes = 32;
            await using DataChannel stalled = await connection.Context.OpenAsync(
                small, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel stalledPeer = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            await using DataChannel independent = await connection.Context.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel independentPeer = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);

            ByteString fullWindow = new(new byte[15]);
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
            Assert.That(
                await independent.SendAsync(new ByteString(new byte[] { 9 }), cancellationToken: harness.Token)
                    .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage other = (await independentPeer.ReadAsync(harness.Token).ConfigureAwait(false))!;
            Assert.That(other.Payload.ToArray(), Is.EqualTo(new byte[] { 9 }));
            if (blocked.IsCompleted)
            {
                Assert.That(await blocked.ConfigureAwait(false), Is.EqualTo(DataChannelResult.WouldBlock));
            }
            else
            {
                cancelled.Cancel();
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await blocked.WaitAsync(harness.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }
            Assert.That(connection.Context.IsReady, Is.True);
            Assert.That(held.Payload, Is.EqualTo(fullWindow));
        }

        [Test]
        [CancelAfter(60000)]
        public async Task ADiscoveredButUntrustedCertificateCannotCreateAClientContextAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp,
                DataChannelCarrierKind.DedicatedUasc,
                trustServer: false).ConfigureAwait(false);

            ServiceResultException failure = (await Assert.CatchAsync<ServiceResultException>(async () =>
                await harness.ConnectAsync().ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.Not.EqualTo(StatusCodes.Good));
            Assert.That(harness.ClientFactory.Contexts, Is.Empty);
            Assert.That(harness.ServiceRequests, Is.Zero);
        }

        [Test]
        [CancelAfter(60000)]
        public async Task ExpectedApplicationMismatchCannotCreateAClientContextAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc).ConfigureAwait(false);

            ServiceResultException failure = (await Assert.CatchAsync<ServiceResultException>(async () =>
                await harness.ConnectAsync("urn:opcfoundation:tests:wrong-server").ConfigureAwait(false))
                    .ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.Not.EqualTo(StatusCodes.Good));
            Assert.That(harness.ClientFactory.Contexts, Is.Empty);
            Assert.That(harness.ServiceRequests, Is.Zero);
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

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [CancelAfter(60000)]
        public async Task LiveRevisionCommitsOnBothPeersWithoutReopeningTheTransportAsync(DataChannelCarrierKind kind)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(CarrierTransport.Tcp, kind)
                .ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await using DataChannel outgoing = await connection.Context.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            Guid contextId = connection.Context.Identity.ContextId;
            DataChannelParametersDataType revised = outgoing.Parameters;
            revised.MaxMessageSize = 1024;
            revised.MaxReceiveBytes = 4096;
            revised.Priority = 5;

            Assert.That(await outgoing.UpdateAsync(revised, 7, harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.Multiple(() =>
            {
                Assert.That(outgoing.Revision, Is.EqualTo(2));
                Assert.That(incoming.Revision, Is.EqualTo(2));
                Assert.That(incoming.Parameters.MaxMessageSize, Is.EqualTo(1024));
                Assert.That(incoming.Parameters.MaxReceiveBytes, Is.EqualTo(4096));
                Assert.That(incoming.Parameters.Priority, Is.EqualTo(5));
                Assert.That(connection.Context.Identity.ContextId, Is.EqualTo(contextId));
                Assert.That(harness.ServerFactory.Contexts, Has.Count.EqualTo(1));
            });
            Assert.That(
                await outgoing.SendAsync(new ByteString(new byte[1024]), cancellationToken: harness.Token)
                    .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage message = (await incoming.ReadAsync(harness.Token)
                .ConfigureAwait(false))!;
            Assert.That(message.Payload.Length, Is.EqualTo(1024));
            Assert.That(
                await outgoing.SendAsync(new ByteString(new byte[1025]), cancellationToken: harness.Token)
                    .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
        }

        [Test]
        [CancelAfter(60000)]
        public async Task UnknownConfiguredResourceIsRefusedWithoutAffectingSharedServicesAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc).ConfigureAwait(false);
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

        [Test]
        [CancelAfter(60000)]
        public async Task ResettingOneSharedFlowPreservesServicesAndAnotherFlowAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc).ConfigureAwait(false);
            await using UascDataChannelConnection connection = await harness.ConnectAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            await using DataChannel reset = await connection.Context.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel resetPeer = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            await using DataChannel surviving = await connection.Context.OpenAsync(
                Parameters(), cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel survivingPeer = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            uint channelId = connection.SecureChannelId;

            await reset.ResetAsync(DataChannelResult.InvalidArgument, harness.Token).ConfigureAwait(false);
            Assert.That(await surviving.SendAsync(
                new ByteString(new byte[] { 0x33 }), cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage message = (await survivingPeer.ReadAsync(harness.Token).ConfigureAwait(false))!;
            IServiceResponse service = await connection.Services!.SendRequestAsync(Read(77), harness.Token)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(message.Payload.ToArray(), Is.EqualTo(new byte[] { 0x33 }));
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
                    Is.EqualTo(kind == DataChannelCarrierKind.DedicatedUasc ? 1u : 2u));
            });
            ByteString payload = new(new byte[] { 0x80, 0x81 });
            Assert.That(
                await openedByServer.SendAsync(payload, cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received =
                (await acceptedByClient.ReadAsync(harness.Token).ConfigureAwait(false))!;
            Assert.That(received.Payload, Is.EqualTo(payload));
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc, 8118)]
        [TestCase(DataChannelCarrierKind.SharedUasc, 8118)]
        [CancelAfter(60000)]
        public async Task MaximumNegotiatedRecordUsesOneEncryptedFinalChunkAsync(
            DataChannelCarrierKind kind,
            int maximumPayload)
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(CarrierTransport.Tcp, kind)
                .ConfigureAwait(false);
            harness.ServerFactory.MaxMessageSize = 65536;
            harness.ClientFactory.MaxMessageSize = 65536;
            using ProbeClientChannel client = await harness.ConnectProbeAsync().ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.MaxMessageSize = 65536;
            parameters.MaxReceiveBytes = 131072;
            await using DataChannel outgoing = await client.DataChannels!.OpenAsync(
                parameters, cancellationToken: harness.Token).ConfigureAwait(false);
            await using DataChannel incoming = await AcceptAsync(remote, harness.Token).ConfigureAwait(false);
            Assert.That(outgoing.Parameters.MaxMessageSize, Is.EqualTo(maximumPayload));
            Assert.That(await client.DataChannels.PingAsync(harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            byte[] payload = Enumerable.Range(0, maximumPayload).Select(index => (byte)(index % 256)).ToArray();

            Assert.That(await outgoing.SendAsync(new ByteString(payload), cancellationToken: harness.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
            byte[] chunk = client.WireChunks.Last(value =>
                (BinaryPrimitives.ReadUInt32LittleEndian(value) & TcpMessageType.MessageTypeMask) ==
                    TcpMessageType.Flow);

            Assert.Multiple(() =>
            {
                Assert.That(received.Payload.ToArray(), Is.EqualTo(payload));
                Assert.That(chunk, Has.Length.EqualTo(8192));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(chunk), Is.EqualTo(0x464F4C46u));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4)), Is.EqualTo(8192));
            });
            Assert.That(await outgoing.SendAsync(
                new ByteString(new byte[maximumPayload + 1]), cancellationToken: harness.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(client.DataChannels.IsReady, Is.True);
        }

#if NET8_0_OR_GREATER
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
                new ReadOnlyMemory<byte>(new byte[] { 0x41 }), WebSocketMessageType.Text, true, harness.Token)
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

        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.RequestId)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.RequestId)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.TruncatedRecord)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.TruncatedRecord)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.Abort)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.Abort)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.Intermediate)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.Intermediate)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.ServiceEnvelope)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.ServiceEnvelope)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.WrongMessageType)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.OrdinaryService)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.OldStreamMessage)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.OldStreamMessage)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.MissingChannelId)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.MissingChannelId)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.MultipleRecords)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.MultipleRecords)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.InvalidDedicatedAddress)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, InvalidFlowChunk.Signature)]
        [TestCase(DataChannelCarrierKind.SharedUasc, InvalidFlowChunk.Signature)]
        [CancelAfter(60000)]
        public async Task AuthenticatedMalformedFlowChunksCannotReachApplicationDispatchAsync(
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
            uint messageType = TcpMessageType.Flow;
            uint requestId = 0;
            const int prefix = 4;
            int completeBodySize = fault == InvalidFlowChunk.Intermediate
                ? client.GetMaxDataChannelRecordSize(kind) + prefix
                : prefix + 13;
            byte[] record = new byte[completeBodySize + (fault == InvalidFlowChunk.Intermediate ? 16 : 0)];
            BinaryPrimitives.WriteUInt32LittleEndian(record, outgoing.ChannelId);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(prefix), (uint)(completeBodySize - prefix - 4));
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(prefix + 5), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(prefix + 9), 1);
            switch (fault)
            {
                case InvalidFlowChunk.RequestId:
                    requestId = 99;
                    break;
                case InvalidFlowChunk.TruncatedRecord:
                    record = [1, 2, 3];
                    break;
                case InvalidFlowChunk.ServiceEnvelope:
                    record = BinaryEncoder.EncodeMessage(Read(123), ServiceMessageContext.Create(harness.Telemetry));
                    break;
                case InvalidFlowChunk.WrongMessageType:
                    messageType = TcpMessageType.Message;
                    break;
                case InvalidFlowChunk.OrdinaryService:
                    messageType = TcpMessageType.Message;
                    requestId = 123;
                    record = BinaryEncoder.EncodeMessage(Read(123), ServiceMessageContext.Create(harness.Telemetry));
                    break;
                case InvalidFlowChunk.OldStreamMessage:
                    messageType = 0x00525453;
                    break;
                case InvalidFlowChunk.MissingChannelId:
                    record = record.AsSpan(prefix).ToArray();
                    break;
                case InvalidFlowChunk.MultipleRecords:
                    record = record.Concat(record.Skip(prefix)).ToArray();
                    break;
                case InvalidFlowChunk.InvalidDedicatedAddress:
                    BinaryPrimitives.WriteUInt32LittleEndian(record, 3);
                    break;
                case InvalidFlowChunk.Signature:
                    client.Recorder.Transport!.CorruptNextSecuredChunk();
                    break;
                default:
                    break;
            }
            try
            {
                await client.SendAuthenticatedBodyAsync(
                    messageType, requestId, record, fault == InvalidFlowChunk.Abort, harness.Token)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // The peer may close after the first authenticated non-final chunk.
            }
            DataChannelResult result = await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), harness.Token)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(DataChannelResult.Good));
                Assert.That(remote.IsReady, Is.False);
                Assert.That(remote.Channels.IsEmpty, Is.True);
                Assert.That(incoming.GetDiagnostics().MessagesReceived, Is.Zero,
                    "Even a complete first DATA record in a non-final chunk must not be admitted.");
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

        [Test]
        [CancelAfter(60000)]
        public async Task ConfiguredContextRevalidatesPeerTrustWithoutDependingOnASessionAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp, DataChannelCarrierKind.SharedUasc).ConfigureAwait(false);
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

        [Test]
        [CancelAfter(60000)]
        public async Task CertificateExpiryRetiresAnEmptyContextBeforeItsSecurityTokenExpiresAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp,
                DataChannelCarrierKind.DedicatedUasc,
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

        [Test]
        [CancelAfter(60000)]
        public async Task SharedServicesAndFlowsRemainOrderedAcrossRealTokenRenewalAsync()
        {
            await using SocketHarness harness = await SocketHarness.StartAsync(
                CarrierTransport.Tcp,
                DataChannelCarrierKind.SharedUasc,
                mode: MessageSecurityMode.Sign).ConfigureAwait(false);
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            using ProbeClientChannel client = await harness.ConnectProbeAsync(clock: clock).ConfigureAwait(false);
            DataChannelContext remote = await harness.ServerFactory.Created.Task
                .WaitAsync(harness.Token).ConfigureAwait(false);
            DataChannelParametersDataType parameters = Parameters();
            parameters.Protection = DataChannelProtectionMode.Integrity;
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

            Assert.That(
                await outgoing.SendAsync(new ByteString(new byte[] { 4, 5, 6 }), cancellationToken: harness.Token)
                    .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage message =
                (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
            IServiceResponse after = await client.SendRequestAsync(Read(102), 10000, harness.Token)
                .ConfigureAwait(false);
            Assert.That(message.Payload.ToArray(), Is.EqualTo(new byte[] { 4, 5, 6 }));
            Assert.That(after.ResponseHeader.RequestHandle, Is.EqualTo(102));
            Assert.That(client.DataChannels.IsReady, Is.True);
            await client.CloseAsync(1000, harness.Token).ConfigureAwait(false);
            Assert.That(
                BinaryPrimitives.ReadUInt32LittleEndian(client.WireChunks.Last()) & TcpMessageType.MessageTypeMask,
                Is.EqualTo(TcpMessageType.Close));
            AssertWireSequence(client.WireChunks);
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
                    ByteString payload = new(BitConverter.GetBytes(index));
                    Assert.That(await outgoing.SendAsync(payload, cancellationToken: harness.Token)
                        .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                    using DataChannelMessage message = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
                    Assert.That(message.Payload, Is.EqualTo(payload));
                }
            }

            await Task.WhenAll(ServicesAsync(), FlowsAsync()).WaitAsync(harness.Token).ConfigureAwait(false);

            Assert.That(harness.ServiceRequests, Is.EqualTo(kMessagesPerKind));
            Assert.That(client.WireChunks.Count(chunk =>
                (BinaryPrimitives.ReadUInt32LittleEndian(chunk) & TcpMessageType.MessageTypeMask) ==
                    TcpMessageType.Message), Is.EqualTo(kMessagesPerKind));
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
                new ByteString(new byte[] { 0xCA, 0xFE }), TimeSpan.FromSeconds(1), harness.Token).AsTask();
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
                    new ByteString(new byte[] { 1 }), cancellationToken: harness.Token)
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                using DataChannelMessage delivered = (await incoming.ReadAsync(harness.Token).ConfigureAwait(false))!;
                Assert.That(delivered.Payload.ToArray(), Is.EqualTo(new byte[] { 1 }));
                AssertWireSequence(client.WireChunks);
                Assert.That(pool.DuplicateReturns, Is.Zero);
            }
            finally
            {
                barrier.Release();
                await service.ConfigureAwait(false);
            }
        }

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
            WriteBarrier barrier = client.Recorder.Transport!.PauseNext(TcpMessageType.Flow, dataOnly: true);
            using var cancellation = new CancellationTokenSource();
            ByteString payload = new(new byte[] { 0x12, 0x34, 0x56, 0x78 });
            Task<DataChannelResult> sending = outgoing.SendAsync(payload, cancellationToken: cancellation.Token)
                .AsTask();
            try
            {
                await barrier.Entered.Task.WaitAsync(harness.Token).ConfigureAwait(false);
                Assert.That(barrier.Buffer, Is.Not.Null);
                Assert.That(pool.Owns(barrier.Buffer!), Is.True);
                cancellation.Cancel();
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
                (BinaryPrimitives.ReadUInt32LittleEndian(chunk) & TcpMessageType.MessageTypeMask) ==
                    TcpMessageType.Flow && chunk.Length > 32 && chunk[32] == 0);
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
                if (type is not (TcpMessageType.Message or TcpMessageType.Flow or TcpMessageType.Close))
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
        /// Authenticated outer chunks with invalid flow mapping fields.
        /// </summary>
        public enum InvalidFlowChunk
        {
            /// <summary>
            /// A wrong carrier RequestId.
            /// </summary>
            RequestId,
            /// <summary>
            /// An incomplete common record.
            /// </summary>
            TruncatedRecord,
            /// <summary>
            /// An abort flag on a flow chunk.
            /// </summary>
            Abort,
            /// <summary>
            /// A continuation flag on a flow chunk.
            /// </summary>
            Intermediate,
            /// <summary>
            /// A Service envelope where a raw flow record is required.
            /// </summary>
            ServiceEnvelope,
            /// <summary>
            /// MSG instead of FLO on a dedicated endpoint.
            /// </summary>
            WrongMessageType,
            /// <summary>
            /// An ordinary Service request on a dedicated endpoint.
            /// </summary>
            OrdinaryService,
            /// <summary>
            /// The removed STR message type on either UASC profile.
            /// </summary>
            OldStreamMessage,
            /// <summary>
            /// A common record without its mandatory FLO ChannelId prefix.
            /// </summary>
            MissingChannelId,
            /// <summary>
            /// More than one common record in a single FLO body.
            /// </summary>
            MultipleRecords,
            /// <summary>
            /// A dedicated address other than management zero or flow one.
            /// </summary>
            InvalidDedicatedAddress,
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
            /// <summary>
            /// Raw TCP sockets.
            /// </summary>
            Tcp,

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
                Protocol = DataChannelProfiles.OpaquePayload,
                ContentType = "application/octet-stream",
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
        /// Owns real certificate-manager trust stores, endpoints and live transports for one test.
        /// </summary>
        private sealed class SocketHarness : IAsyncDisposable
        {
            private SocketHarness(
                CarrierTransport transport,
                DataChannelCarrierKind kind,
                MessageSecurityMode mode,
                TimeSpan? certificateLifetime,
                int tokenLifetime)
            {
                Telemetry = NUnitTelemetryContext.Create(logLevel: Microsoft.Extensions.Logging.LogLevel.Warning);
                m_transport = transport;
                m_kind = kind;
                m_mode = mode;
                m_pkiRoot = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory, "DataChannelPki", Guid.NewGuid().ToString("N"));
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
                string scheme = transport == CarrierTransport.Wss ? Utils.UriSchemeOpcWss : Utils.UriSchemeOpcTcp;
                int port = GetFreePort();
                m_baseUrl = new Uri($"{scheme}://localhost:{port}/");
                string resource = kind == DataChannelCarrierKind.DedicatedUasc
                    ? DataChannelProfiles.DedicatedResource
                    : DataChannelProfiles.SharedResource;
                m_url = new Uri(m_baseUrl, resource);
                m_listener = transport switch
                {
#if NET8_0_OR_GREATER
                    CarrierTransport.KestrelTcp => new KestrelTcpTransportListener(Telemetry),
#endif
                    CarrierTransport.Wss => new HttpsTransportListener(Utils.UriSchemeOpcWss, Telemetry),
                    _ => new TcpTransportListener(Telemetry)
                };
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
            /// Starts an authenticated live listener without constructing a Server or Session.
            /// </summary>
            public static async Task<SocketHarness> StartAsync(
                CarrierTransport transport,
                DataChannelCarrierKind kind,
                bool trustServer = true,
                MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
                TimeSpan? certificateLifetime = null,
                int tokenLifetime = 60000)
            {
                var harness = new SocketHarness(transport, kind, mode, certificateLifetime, tokenLifetime);
                try
                {
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
                                Results = [new DataValue(new Variant(ObjectIds.Server))]
                            });
                        });
                    await harness.m_listener.OpenAsync(
                        harness.m_baseUrl,
                        new TransportListenerSettings
                        {
                            Descriptions = [harness.CreateEndpoint()],
                            Configuration = harness.m_configuration,
                            CertificateValidator = harness.m_serverCertificates,
                            ServerCertificates = harness.m_serverCertificates,
                            NamespaceUris = new NamespaceTable(),
                            Factory = EncodeableFactory.Create(),
                            DataChannels = harness.ServerFactory,
                            AllowDataChannelIntegrity = mode == MessageSecurityMode.Sign,
                            HttpsMutualTls = true,
                            MaxChannelCount = 16
                        },
                        callback.Object,
                        harness.Token).ConfigureAwait(false);
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
            public ValueTask<UascDataChannelConnection> ConnectAsync(string? expectedServerUri = null)
            {
                EndpointDescription endpoint = CreateEndpoint();
                if (expectedServerUri != null)
                {
                    endpoint.Server.ApplicationUri = expectedServerUri;
                }
                ITransportChannelFactory transportFactory = m_transport == CarrierTransport.Wss
                    ? new OpcWssTransportChannelFactory()
                    : new TcpTransportChannelFactory();
                var client = new UascDataChannelClient(transportFactory, Telemetry);
                return client.ConnectAsync(
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
                    Token);
            }

            /// <summary>
            /// Connects a real low-level client for authenticated wire-negative and ordering tests.
            /// </summary>
            public async Task<ProbeClientChannel> ConnectProbeAsync(
                bool confirmProfile = true,
                TimeProvider? clock = null,
                MessageSecurityMode? requestedMode = null,
                ArrayPool<byte>? pool = null)
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
                var recorder = new RecordingTransportFactory(wire);
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
                        client.ConfigureDataChannels(ClientFactory, m_mode == MessageSecurityMode.Sign);
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
                await m_listener.CloseAsync().ConfigureAwait(false);
                await m_listener.DisposeAsync().ConfigureAwait(false);
                foreach (DataChannelContext context in ClientFactory.Contexts.Concat(ServerFactory.Contexts))
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                }
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

            private EndpointDescription CreateEndpoint()
            {
                return new EndpointDescription
                {
                    EndpointUrl = m_url.AbsoluteUri,
                    TransportProfileUri = m_kind == DataChannelCarrierKind.DedicatedUasc
                        ? DataChannelProfiles.DedicatedUasc
                        : DataChannelProfiles.SharedUasc,
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

            /// <summary>The client identity in its application certificate.</summary>
            public const string ClientUri = "urn:opcfoundation:tests:flow-client";

            private readonly CarrierTransport m_transport;
            private readonly DataChannelCarrierKind m_kind;
            private readonly MessageSecurityMode m_mode;
            private readonly string m_pkiRoot;
            private readonly Uri m_url;
            private readonly Uri m_baseUrl;
            private readonly ITransportListener m_listener;
            private readonly Certificate m_clientCertificate;
            private readonly CertificateManager m_serverCertificates;
            private readonly CertificateManager m_clientCertificates;
            private readonly EndpointConfiguration m_configuration;
            private readonly CancellationTokenSource m_deadline = new(TimeSpan.FromSeconds(45));
            private int m_serviceRequests;
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
            /// Gets or sets the explicitly controlled runtime clock for deadline tests.
            /// </summary>
            public TimeProvider Clock { get; set; } = TimeProvider.System;

            /// <summary>
            /// Gets or sets the application maximum; UASC must independently clamp it to actual chunk capacity.
            /// </summary>
            public int MaxMessageSize { get; set; } = 4096;

            /// <inheritdoc/>
            public DataChannelContext Create(IDataChannelCarrier carrier)
            {
                var context = new DataChannelContext(
                    carrier,
                    this,
                    new DataChannelOptions
                    {
                        MaxMessageSize = MaxMessageSize,
                        MaxControlBytes = 4096,
                        AllowIntegrity = m_allowIntegrity
                    },
                    m_budget,
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
                    parameters.ResourceUri == SocketHarness.Resource &&
                    parameters.Protocol == DataChannelProfiles.OpaquePayload &&
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
            private readonly DataChannelApplicationBudget m_budget = new();
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
        }

        /// <summary>
        /// Observes each real TCP submission without altering or coalescing chunks.
        /// </summary>
        private sealed class RecordingTransportFactory(ConcurrentQueue<byte[]> chunks) : IUaSCByteTransportFactory
        {
            /// <summary>
            /// Gets the actual observed socket transport after connection creation.
            /// </summary>
            public RecordingTransport? Transport { get; private set; }

            /// <inheritdoc/>
            public string Implementation => "Recorded UA-TCP";

            /// <inheritdoc/>
            public IUaSCByteTransport Create(
                BufferManager bufferManager, int receiveBufferSize, ITelemetryContext telemetry)
            {
                Transport = new RecordingTransport(
                    new TcpByteTransportFactory(telemetry).Create(bufferManager, receiveBufferSize, telemetry),
                    chunks);
                return Transport;
            }
        }

        /// <summary>
        /// Delegates network traffic while retaining immutable wire evidence for assertions.
        /// </summary>
        private sealed class RecordingTransport(
            IUaSCByteTransport inner,
            ConcurrentQueue<byte[]> chunks) : IUaSCByteTransport, IUaSCByteTransportLimits
        {
            /// <inheritdoc/>
            public EndPoint? LocalEndpoint => inner.LocalEndpoint;

            /// <inheritdoc/>
            public EndPoint? RemoteEndpoint => inner.RemoteEndpoint;

            /// <inheritdoc/>
            public TransportChannelFeatures Features => inner.Features;

            /// <inheritdoc/>
            public string Implementation => inner.Implementation;

            /// <summary>
            /// Pauses one selected write after security commitment but before actual socket submission.
            /// </summary>
            public WriteBarrier PauseNext(uint messageType, bool dataOnly)
            {
                var barrier = new WriteBarrier(messageType, dataOnly);
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
                chunks.Enqueue(chunk);
                await inner.SendChunkAsync(buffers, ct).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                return inner.ReceiveChunkAsync(ct);
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
                if (barrier != null && barrier.Matches(chunk.Span) &&
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
        private sealed class WriteBarrier(uint messageType, bool dataOnly)
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
                return !dataOnly || (messageType == TcpMessageType.Flow && chunk.Length > 32 && chunk[32] == 0);
            }
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
    }
}
