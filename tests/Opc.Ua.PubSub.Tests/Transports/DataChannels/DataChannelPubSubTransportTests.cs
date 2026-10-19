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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Transports.DataChannels;
using Opc.Ua.Tests;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Transports.DataChannels
{
    /// <summary>
    /// Proves exact bytes, flow-owned credit and failure cleanup using actual Core establishment and message paths.
    /// </summary>
    [TestFixture]
    [CancelAfter(30000)]
    public sealed class DataChannelPubSubTransportTests
    {
        /// <summary>
        /// Carries complete UADP messages without queue expiry or changes to their optional and secured fields.
        /// </summary>
        [Test]
        public async Task CompleteOptionalAndSecuredUadpBytesSurviveEveryCarrierUnchanged(
            [Values] DataChannelCarrierKind carrier,
            [Values] bool secured)
        {
            await using TransportPair pair = await TransportPair.CreateAsync(carrier).ConfigureAwait(false);
            ByteString bytes = await DataChannelPubSubTestData.EncodeUadpAsync(secured).ConfigureAwait(false);

            await pair.Sender.SendAsync(bytes.Memory, cancellationToken: TestContext.CurrentContext.CancellationToken)
                .ConfigureAwait(false);
            await using IAsyncEnumerator<PubSubTransportFrame> receiver = pair.Receiver.ReceiveAsync(
                TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator();
            Assert.That(await receiver.MoveNextAsync().ConfigureAwait(false), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(receiver.Current.Payload.Span.SequenceEqual(bytes.Span), Is.True,
                    "The adapter must deliver every encoded byte, not a re-encoded equivalent object.");
                Assert.That(pair.Core.LeftCarrier.Payloads.Single(), Is.EqualTo(bytes),
                    "The committed common-record body must contain only the original NetworkMessage.");
                Assert.That(pair.Sender.Channel!.Parameters.AllowQueueExpiry, Is.False);
                Assert.That(pair.Receiver.Channel!.Parameters.AllowQueueExpiry, Is.False);
                Assert.That(pair.Receiver.EncodingProfileUri, Is.EqualTo(Profiles.PubSubUdpUadpTransport));
                Assert.That(UadpDecoder.TryReadPrefix(bytes.Memory, out UadpPrefixInfo prefix), Is.True);
                Assert.That(prefix.SecurityEnabled, Is.EqualTo(secured));
                Assert.That(prefix.PublisherId, Is.EqualTo(PublisherId.FromUInt32(73001)));
                Assert.That(prefix.WriterGroupId, Is.EqualTo(42));
            });
            if (!secured)
            {
                PubSubNetworkMessage? decoded = await new UadpDecoder().TryDecodeAsync(
                    receiver.Current.Payload, DataChannelPubSubTestData.CreateMessageContext()).ConfigureAwait(false);
                Assert.That(decoded, Is.TypeOf<UadpNetworkMessage>());
                var uadp = (UadpNetworkMessage)decoded!;
                Assert.Multiple(() =>
                {
                    Assert.That(uadp.GroupVersion, Is.EqualTo(0x10203040));
                    Assert.That(uadp.NetworkMessageNumber, Is.EqualTo(9));
                    Assert.That(uadp.SequenceNumber, Is.EqualTo(65530));
                    Assert.That(uadp.Timestamp, Is.EqualTo(DataChannelPubSubTestData.Timestamp));
                    Assert.That(uadp.PicoSeconds, Is.EqualTo(1234));
                    Assert.That(uadp.PromotedFields[0].Value, Is.EqualTo(new Variant(73)));
                    Assert.That((uadp.DataSetMessages.ToArray() ?? []).Select(message => message.DataSetWriterId),
                        Is.EqualTo(new ushort[] { 1001, 1002 }));
                    Assert.That(((UadpDataSetMessage)uadp.DataSetMessages[0]).PicoSeconds, Is.EqualTo(321));
                    Assert.That(uadp.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(-1234567)));
                });
            }
        }

        /// <summary>
        /// Keeps JSON payload bytes and optional headers intact with queue expiry disabled.
        /// </summary>
        [Test]
        public async Task JsonWhitespaceEscapesAndOptionalHeadersAreNeverReencoded(
            [Values] DataChannelCarrierKind carrier)
        {
            const string json = /*lang=json,strict*/ """
                { "MessageId":"exact-17", "MessageType":"ua-data", "PublisherId":"73001",
                  "WriterGroupName":"WG 42", "ReplyTo":["urn:test:pubsub:reply"],
                  "Messages":[{"DataSetWriterId":1001,"SequenceNumber":27,
                    "Timestamp":"2026-10-16T12:34:56Z","MessageType":"ua-keyframe",
                    "Payload":{"Text":{"UaType":12,"Value":"kept \u03a9"}}}] }
                """;
            var bytes = ByteString.From(System.Text.Encoding.UTF8.GetBytes(json));
            await using TransportPair pair = await TransportPair.CreateAsync(
                carrier, DataChannelProfiles.JsonPayload).ConfigureAwait(false);

            await pair.Sender.SendAsync(bytes.Memory).ConfigureAwait(false);
            await using IAsyncEnumerator<PubSubTransportFrame> receiver = pair.Receiver.ReceiveAsync(
                TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator();
            Assert.That(await receiver.MoveNextAsync().ConfigureAwait(false), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(receiver.Current.Payload.Span.SequenceEqual(bytes.Span), Is.True);
                Assert.That(pair.Core.LeftCarrier.Payloads.Single(), Is.EqualTo(bytes));
                Assert.That(pair.Sender.Channel!.Parameters.AllowQueueExpiry, Is.False);
                Assert.That(pair.Receiver.Channel!.Parameters.AllowQueueExpiry, Is.False);
                Assert.That(pair.Receiver.EncodingProfileUri, Is.EqualTo(Profiles.PubSubMqttJsonTransport));
                Assert.That(pair.Receiver.PayloadProtocol, Is.EqualTo(DataChannelProfiles.JsonPayload));
                Assert.That(pair.Receiver.TransportProfileUri,
                    Is.EqualTo(DataChannelPubSubConfiguration.GetTransportProfile(carrier)));
                Assert.That(receiver.Current.Topic, Is.Null);
            });
        }

        [Test]
        public async Task ReceiveCreditRemainsOwnedUntilTheConsumerFinishesEvenAfterClose()
        {
            await using TransportPair pair = await TransportPair.CreateAsync().ConfigureAwait(false);
            var bytes = ByteString.From(new byte[] { 1, 2, 3, 4, 5 });
            await pair.Sender.SendAsync(bytes.Memory).ConfigureAwait(false);
            IAsyncEnumerator<PubSubTransportFrame> receiver = pair.Receiver.ReceiveAsync(
                TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator();
            try
            {
                Assert.That(await receiver.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(pair.Core.RightBudget.Usage.Messages, Is.EqualTo(1));
                Assert.That(pair.Core.RightBudget.Usage.BufferedBytes, Is.GreaterThanOrEqualTo(bytes.Length + 17));

                await pair.Receiver.CloseAsync().ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(pair.Core.RightBudget.Usage.Messages, Is.EqualTo(1),
                        "Closing must not steal a message still owned by the PubSub consumer.");
                    Assert.That(receiver.Current.Payload.Span.SequenceEqual(bytes.Span), Is.True);
                });
            }
            finally
            {
                await receiver.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(pair.Core.RightBudget.Usage.Messages, Is.Zero);
        }

        [Test]
        public async Task CancelledReceiveReleasesThePreviousMessageAndAllowsANewConsumer()
        {
            await using TransportPair pair = await TransportPair.CreateAsync().ConfigureAwait(false);
            await pair.Sender.SendAsync(new byte[] { 7 }).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            IAsyncEnumerator<PubSubTransportFrame> receiver = pair.Receiver.ReceiveAsync(
                cancellation.Token).GetAsyncEnumerator();
            Assert.That(await receiver.MoveNextAsync().ConfigureAwait(false), Is.True);
            Task<bool> pending = receiver.MoveNextAsync().AsTask();
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(() => pending,
                Throws.InstanceOf<OperationCanceledException>());
            await receiver.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(pair.Core.RightBudget.Usage.Messages, Is.Zero);
                Assert.That(pair.Receiver.IsConnected, Is.True);
            });
            await pair.Sender.SendAsync(new byte[] { 8 }).ConfigureAwait(false);
            await using IAsyncEnumerator<PubSubTransportFrame> next = pair.Receiver.ReceiveAsync(
                TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator();
            Assert.That(await next.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(next.Current.Payload.ToArray(), Is.EqualTo(new byte[] { 8 }));
        }

        [Test]
        public async Task MessageLimitRejectsTheAdjacentOversizeWithoutSendingIt()
        {
            DataChannelParametersDataType parameters = DataChannelPubSubConfigurationTests.CreateParameters();
            parameters.MaxMessageSize = 128;
            parameters.MaxReceiveBytes = 512;
            await using TransportPair pair = await TransportPair.CreateAsync(parameters: parameters)
                .ConfigureAwait(false);
            byte[] boundary = [.. Enumerable.Range(0, 128).Select(index => (byte)index)];

            await pair.Sender.SendAsync(boundary).ConfigureAwait(false);
            await using IAsyncEnumerator<PubSubTransportFrame> receiver = pair.Receiver.ReceiveAsync(
                TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator();
            Assert.That(await receiver.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(receiver.Current.Payload.ToArray(), Is.EqualTo(boundary));
            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(
                async () => await pair.Sender.SendAsync(new byte[129]).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.TooLarge));
                Assert.That(pair.Core.LeftCarrier.Payloads, Has.Count.EqualTo(1));
                Assert.That(pair.Sender.IsConnected, Is.True);
            });
        }

        [Test]
        public async Task BoundedSendQueueAndCancellationDoNotReturnAConsumersCreditEarly()
        {
            DataChannelParametersDataType parameters = DataChannelPubSubConfigurationTests.CreateParameters();
            parameters.MaxMessageSize = 128;
            parameters.MaxReceiveBytes = 145;
            await using TransportPair pair = await TransportPair.CreateAsync(
                parameters: parameters, options: new DataChannelOptions { MaxQueueMessages = 1 }).ConfigureAwait(false);
            byte[] payload = new byte[128];
            await pair.Sender.SendAsync(payload).ConfigureAwait(false);
            IAsyncEnumerator<PubSubTransportFrame> receiver = pair.Receiver.ReceiveAsync(
                TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator();
            try
            {
                Assert.That(await receiver.MoveNextAsync().ConfigureAwait(false), Is.True);
                using var cancellation = new CancellationTokenSource();
                Task waiting = pair.Sender.SendAsync(payload, cancellationToken: cancellation.Token).AsTask();
                Assert.That(waiting.IsCompleted, Is.False);
                DataChannelException? overflow = await Assert.ThrowsAsync<DataChannelException>(
                    async () => await pair.Sender.SendAsync(payload).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(overflow!.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Assert.ThatAsync(() => waiting,
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(await OnlyChannel(pair.Core.Left).EndAsync(
                    TestContext.CurrentContext.CancellationToken).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));

                // Application accounting also includes asynchronously released END/CREDIT records.
                await Assert.ThatAsync(() => Task.FromResult(pair.Core.LeftBudget.Usage.Messages),
                    Is.Zero.After(5000).PollEvery(10)).ConfigureAwait(false);
                await Assert.ThatAsync(() => Task.FromResult(pair.Core.RightBudget.Usage.Messages),
                    Is.EqualTo(1).After(5000).PollEvery(10)).ConfigureAwait(false);
                Assert.That(pair.Core.LeftCarrier.Payloads, Has.Count.EqualTo(1));
            }
            finally
            {
                await receiver.DisposeAsync().ConfigureAwait(false);
            }
            await Assert.ThatAsync(() => Task.FromResult(pair.Core.RightBudget.Usage.Messages),
                Is.Zero.After(5000).PollEvery(10)).ConfigureAwait(false);
        }

        [Test]
        public async Task CallerCancellationAfterCommitCannotRetractOrMutateTheOwnedNetworkMessage()
        {
            await using TransportPair pair = await TransportPair.CreateAsync().ConfigureAwait(false);
            ByteString expected = await DataChannelPubSubTestData.EncodeUadpAsync(false).ConfigureAwait(false);
            byte[] callerBuffer = expected.ToArray();
            var committed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.Core.LeftCarrier.DataCommitted = committed;
            pair.Core.LeftCarrier.ReleaseCommittedData = release.Task;
            using var cancellation = new CancellationTokenSource();
            try
            {
                Task sending = pair.Sender.SendAsync(callerBuffer, cancellationToken: cancellation.Token).AsTask();
                await committed.Task.WaitAsync(TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Assert.ThatAsync(() => sending,
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Array.Clear(callerBuffer, 0, callerBuffer.Length);
                Assert.That(pair.Core.LeftBudget.Usage.Messages, Is.EqualTo(1),
                    "The cancelled waiter does not own a carrier-committed record.");
                release.TrySetResult(true);

                await using IAsyncEnumerator<PubSubTransportFrame> receiver = pair.Receiver.ReceiveAsync(
                    TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator();
                Assert.That(await receiver.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(receiver.Current.Payload.Span.SequenceEqual(expected.Span), Is.True);
                    Assert.That(pair.Core.LeftCarrier.Payloads.Single(), Is.EqualTo(expected));
                    Assert.That(pair.Sender.IsConnected, Is.True);
                });
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// Preserves complete messages on native datagram paths without enabling queue expiry.
        /// </summary>
        [Test]
        public async Task NativeDatagramPayloadsUseTheRealCoreDatagramPath(
            [Values(DataChannelCarrierKind.Quic, DataChannelCarrierKind.WebTransport)] DataChannelCarrierKind carrier,
            [Values(DataChannelProfiles.UadpPayload, DataChannelProfiles.JsonPayload)] string protocol)
        {
            DataChannelParametersDataType parameters = DataChannelPubSubConfigurationTests.CreateParameters(protocol);
            parameters.Delivery = DataChannelDeliveryMode.Datagram;
            parameters.MaxMessageSize = 512;
            parameters.MaxReceiveBytes = 2048;
            parameters.MaxRate = 1000;
            await using TransportPair pair = await TransportPair.CreateAsync(
                carrier, protocol, parameters).ConfigureAwait(false);
            ByteString payload = protocol == DataChannelProfiles.UadpPayload
                ? await DataChannelPubSubTestData.EncodeUadpAsync(false).ConfigureAwait(false)
                : ByteString.From(System.Text.Encoding.UTF8.GetBytes(
                    /*lang=json,strict*/
                                         """{"MessageId":"datagram","MessageType":"ua-data","Messages":[]}"""));
            await pair.Sender.SendAsync(payload.Memory).ConfigureAwait(false);
            await using IAsyncEnumerator<PubSubTransportFrame> receiver = pair.Receiver.ReceiveAsync(
                TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator();
            Assert.That(await receiver.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(receiver.Current.Payload.Span.SequenceEqual(payload.Span), Is.True);
                Assert.That(pair.Core.LeftCarrier.Payloads.Single(), Is.EqualTo(payload));
                Assert.That(pair.Sender.Channel!.Parameters.AllowQueueExpiry, Is.False);
                Assert.That(pair.Receiver.Channel!.Parameters.AllowQueueExpiry, Is.False);
                Assert.That(OnlyChannel(pair.Core.Left).Parameters.Delivery,
                    Is.EqualTo(DataChannelDeliveryMode.Datagram));
                Assert.That(pair.Core.RightCarrier.Failures, Is.Empty);
            });
        }

        [Test]
        public async Task PublisherCanBeTheFlowAcceptorAndTransportAcceptor()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            await using var opener = new DataChannelContextPubSubFlowProvider(core.Left, true);
            await using var acceptor = new DataChannelContextPubSubFlowProvider(core.Right, false);
            DataChannelParametersDataType parameters = DataChannelPubSubConfigurationTests.CreateParameters();
            parameters.Direction = DataChannelDirection.AcceptorToOpener;
            await using DataChannelPubSubTransport subscriber = CreateTransport(
                core.Left.CarrierKind, opener, PubSubTransportDirection.Receive, parameters);
            await using DataChannelPubSubTransport publisher = CreateTransport(
                core.Right.CarrierKind, acceptor, PubSubTransportDirection.Send, parameters);
            await Task.WhenAll(subscriber.OpenAsync().AsTask(), publisher.OpenAsync().AsTask()).ConfigureAwait(false);

            await publisher.SendAsync(new byte[] { 4, 2 }).ConfigureAwait(false);
            await using IAsyncEnumerator<PubSubTransportFrame> receiver = subscriber.ReceiveAsync(
                TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator();
            Assert.That(await receiver.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(OnlyChannel(core.Right).IsLocalOpener, Is.False);
                Assert.That(core.Right.Identity.Peer.IsTransportInitiator, Is.False);
                Assert.That(receiver.Current.Payload.ToArray(), Is.EqualTo(new byte[] { 4, 2 }));
                Assert.That(core.RightCarrier.Payloads.Single(), Is.EqualTo(ByteString.From(new byte[] { 4, 2 })));
            });
        }

        [Test]
        public async Task ReceiveOnlySubscriberKeepsAHalfOpenDuplexFlowConnectedUntilLocalEnd()
        {
            DataChannelParametersDataType parameters = DataChannelPubSubConfigurationTests.CreateParameters();
            parameters.Direction = DataChannelDirection.Duplex;
            await using TransportPair pair = await TransportPair.CreateAsync(parameters: parameters).ConfigureAwait(false);
            DataChannel outgoing = pair.Sender.Channel!;
            DataChannel incoming = pair.Receiver.Channel!;
            await pair.Sender.SendAsync(new byte[] { 8, 4, 2 }).ConfigureAwait(false);
            Assert.That(await outgoing.EndAsync(TestContext.CurrentContext.CancellationToken).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await using (IAsyncEnumerator<PubSubTransportFrame> messages = pair.Receiver.ReceiveAsync(
                TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator())
            {
                Assert.That(await messages.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(messages.Current.Payload.ToArray(), Is.EqualTo(new byte[] { 8, 4, 2 }));
                Assert.That(await messages.MoveNextAsync().ConfigureAwait(false), Is.False);
            }
            Assert.Multiple(() =>
            {
                Assert.That(pair.Receiver.IsConnected, Is.True);
                Assert.That(incoming.GetStatus().OpenerEnded, Is.True);
                Assert.That(incoming.GetStatus().AcceptorEnded, Is.False);
                Assert.That(incoming.State, Is.EqualTo(DataChannelState.Open));
            });
            Assert.That(await incoming.PingAsync(TestContext.CurrentContext.CancellationToken).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.Receiver.StateChanged += (_, args) =>
            {
                if (!args.IsConnected)
                {
                    disconnected.TrySetResult(true);
                }
            };
            Assert.That(await incoming.EndAsync(TestContext.CurrentContext.CancellationToken).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await disconnected.Task.WaitAsync(TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
            Assert.That(pair.Receiver.IsConnected, Is.False);
        }

        [Test]
        public async Task LiveRevisionChangesTheMessageBudgetWithoutChangingPayloadOrChannelIdentity()
        {
            await using TransportPair pair = await TransportPair.CreateAsync().ConfigureAwait(false);
            DataChannel channel = OnlyChannel(pair.Core.Left);
            uint channelId = channel.ChannelId;
            ByteString bytes = await DataChannelPubSubTestData.EncodeUadpAsync(false).ConfigureAwait(false);
            await pair.Sender.SendAsync(bytes.Memory).ConfigureAwait(false);
            await using (IAsyncEnumerator<PubSubTransportFrame> first = pair.Receiver.ReceiveAsync(
                TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator())
            {
                Assert.That(await first.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(first.Current.Payload.Span.SequenceEqual(bytes.Span), Is.True);
            }
            DataChannelParametersDataType revised = channel.Parameters;
            revised.MaxMessageSize = (uint)bytes.Length;
            revised.MaxReceiveBytes = 4 * ((uint)bytes.Length + 17);
            Assert.That(await channel.UpdateAsync(revised,
                cancellationToken: TestContext.CurrentContext.CancellationToken).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.Multiple(() =>
            {
                Assert.That(channel.ChannelId, Is.EqualTo(channelId));
                Assert.That(channel.Revision, Is.EqualTo(2));
                Assert.That(pair.Sender.MaxNetworkMessageSize, Is.EqualTo(bytes.Length));
                Assert.That(pair.Sender.PayloadProtocol, Is.EqualTo(DataChannelProfiles.UadpPayload));
            });
            await pair.Sender.SendAsync(bytes.Memory).ConfigureAwait(false);
            await using IAsyncEnumerator<PubSubTransportFrame> next = pair.Receiver.ReceiveAsync(
                TestContext.CurrentContext.CancellationToken).GetAsyncEnumerator();
            Assert.That(await next.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(next.Current.Payload.Span.SequenceEqual(bytes.Span), Is.True);
            Assert.That(pair.Core.LeftCarrier.Payloads.ToArray(), Is.EqualTo([bytes, bytes]));
            DataChannelException? tooLarge = await Assert.ThrowsAsync<DataChannelException>(
                async () => await pair.Sender.SendAsync(new byte[bytes.Length + 1]).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(tooLarge!.Result, Is.EqualTo(DataChannelResult.TooLarge));
        }

        [Test]
        public async Task AReceivingOnlyFlowCannotStartAPublisher()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            await using DataChannel sender = await core.Left.OpenAsync(
                DataChannelPubSubConfigurationTests.CreateParameters()).ConfigureAwait(false);
            DataChannel receiver = (await core.Right.AcceptAsync().ConfigureAwait(false))!;
            var provider = new SingleFlowProvider(new DataChannelPubSubFlow(receiver, core.Right.CarrierKind));
            await using DataChannelPubSubTransport transport = CreateTransport(
                core.Right.CarrierKind, provider, PubSubTransportDirection.Send);

            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(
                async () => await transport.OpenAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                Assert.That(transport.IsConnected, Is.False);
                Assert.That(receiver.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(core.RightBudget.Usage.Flows, Is.Zero);
            });
        }

        [Test]
        public async Task NegotiatedProtocolMismatchReleasesTheActualFlowBeforeConnecting()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            DataChannel sender = await core.Left.OpenAsync(
                DataChannelPubSubConfigurationTests.CreateParameters()).ConfigureAwait(false);
            await using DataChannel receiver = (await core.Right.AcceptAsync().ConfigureAwait(false))!;
            var provider = new SingleFlowProvider(new DataChannelPubSubFlow(sender, core.Left.CarrierKind));
            await using DataChannelPubSubTransport transport = CreateTransport(
                core.Left.CarrierKind, provider, PubSubTransportDirection.Send,
                DataChannelPubSubConfigurationTests.CreateParameters(DataChannelProfiles.JsonPayload));

            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(
                async () => await transport.OpenAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                Assert.That(transport.IsConnected, Is.False);
                Assert.That(sender.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
                Assert.That(core.LeftCarrier.Payloads, Is.Empty);
            });
        }

        /// <summary>
        /// Disposes an unsafe provider-selected flow before allowing the Publisher to send any bytes.
        /// </summary>
        [Test]
        public async Task ProviderCannotEnableQueueExpiryBeforeConnecting()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            DataChannelParametersDataType parameters = DataChannelPubSubConfigurationTests.CreateParameters();
            parameters.AllowQueueExpiry = true;
            DataChannel sender = await core.Left.OpenAsync(parameters).ConfigureAwait(false);
            await using DataChannel receiver = (await core.Right.AcceptAsync().ConfigureAwait(false))!;
            var provider = new SingleFlowProvider(new DataChannelPubSubFlow(sender, core.Left.CarrierKind));
            await using DataChannelPubSubTransport transport = CreateTransport(
                core.Left.CarrierKind, provider, PubSubTransportDirection.Send);

            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(
                async () => await transport.OpenAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                Assert.That(transport.IsConnected, Is.False);
                Assert.That(transport.Channel, Is.Null);
                Assert.That(sender.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
                Assert.That(core.LeftCarrier.Payloads, Is.Empty);
            });
        }

        /// <summary>
        /// Rejects negotiated descriptor mismatches before the transport can begin publishing.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [TestCase("Resource")]
        [TestCase("ContentType")]
        [TestCase("Direction")]
        [TestCase("Delivery")]
        [TestCase("Protection")]
        [TestCase("Priority")]
        [TestCase("MessageSize")]
        [TestCase("ReceiveBytes")]
        [TestCase("Rate")]
        public async Task EveryNegotiatedDescriptorMismatchIsRejectedBeforeConnecting(string mismatch)
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync(
                DataChannelCarrierKind.Quic).ConfigureAwait(false);
            DataChannelParametersDataType parameters = DataChannelPubSubConfigurationTests.CreateParameters();
            parameters.MaxRate = 1000;
            DataChannel sender = await core.Left.OpenAsync(parameters).ConfigureAwait(false);
            await using DataChannel receiver = (await core.Right.AcceptAsync().ConfigureAwait(false))!;
            DataChannelParametersDataType expected = sender.Parameters;
            switch (mismatch)
            {
                case "Resource":
                    expected.ResourceUri = "urn:test:pubsub:other";
                    break;
                case "ContentType":
                    expected.ContentType = "application/different";
                    break;
                case "Direction":
                    expected.Direction = DataChannelDirection.Duplex;
                    break;
                case "Delivery":
                    expected.Delivery = DataChannelDeliveryMode.Datagram;
                    break;
                case "Protection":
                    expected.Protection = DataChannelProtectionMode.Integrity;
                    break;
                case "Priority":
                    expected.Priority++;
                    break;
                case "MessageSize":
                    expected.MaxMessageSize--;
                    break;
                case "ReceiveBytes":
                    expected.MaxReceiveBytes--;
                    break;
                case "Rate":
                    expected.MaxRate--;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mismatch));
            }
            var provider = new SingleFlowProvider(new DataChannelPubSubFlow(sender, core.Left.CarrierKind));
            await using DataChannelPubSubTransport transport = CreateTransport(
                core.Left.CarrierKind, provider, PubSubTransportDirection.Send, expected);

            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(
                async () => await transport.OpenAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                Assert.That(transport.IsConnected, Is.False);
                Assert.That(sender.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
            });
        }

        [Test]
        public async Task AdmittedNarrowerBoundsBecomeTheActualTransportBudget()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            DataChannelParametersDataType admitted = DataChannelPubSubConfigurationTests.CreateParameters();
            admitted.MaxMessageSize = 256;
            admitted.MaxReceiveBytes = 1024;
            DataChannel sender = await core.Left.OpenAsync(admitted).ConfigureAwait(false);
            await using DataChannel receiver = (await core.Right.AcceptAsync().ConfigureAwait(false))!;
            var provider = new SingleFlowProvider(new DataChannelPubSubFlow(sender, core.Left.CarrierKind));
            await using DataChannelPubSubTransport transport = CreateTransport(
                core.Left.CarrierKind, provider, PubSubTransportDirection.Send);
            await transport.OpenAsync().ConfigureAwait(false);
            Assert.That(transport.MaxNetworkMessageSize, Is.EqualTo(256));
            await transport.SendAsync(new byte[256]).ConfigureAwait(false);
            using DataChannelMessage? message = await receiver.ReadAsync(
                TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
            Assert.That(message!.Length, Is.EqualTo(256));
            DataChannelException? tooLarge = await Assert.ThrowsAsync<DataChannelException>(
                async () => await transport.SendAsync(new byte[257]).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(tooLarge!.Result, Is.EqualTo(DataChannelResult.TooLarge));
        }

        [Test]
        public async Task FailedOpenNotificationStillReleasesTheFlowAndClearsConnectedState()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            DataChannel sender = await core.Left.OpenAsync(
                DataChannelPubSubConfigurationTests.CreateParameters()).ConfigureAwait(false);
            await using DataChannel receiver = (await core.Right.AcceptAsync().ConfigureAwait(false))!;
            var provider = new SingleFlowProvider(new DataChannelPubSubFlow(sender, core.Left.CarrierKind));
            await using DataChannelPubSubTransport transport = CreateTransport(
                core.Left.CarrierKind, provider, PubSubTransportDirection.Send);
            transport.StateChanged += (_, _) => throw new InvalidOperationException("Consumer notification failed.");

            await Assert.ThatAsync(async () => await transport.OpenAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(transport.IsConnected, Is.False);
                Assert.That(sender.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
            });
        }

        [Test]
        public async Task ConcurrentCloseAndDisposeAwaitTheOwnedLifetimeExactlyOnce()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            DataChannel sender = await core.Left.OpenAsync(
                DataChannelPubSubConfigurationTests.CreateParameters()).ConfigureAwait(false);
            await using DataChannel receiver = (await core.Right.AcceptAsync().ConfigureAwait(false))!;
            var owner = new BlockingFlowOwner();
            var provider = new SingleFlowProvider(new DataChannelPubSubFlow(sender, core.Left.CarrierKind, owner));
            DataChannelPubSubTransport transport = CreateTransport(
                core.Left.CarrierKind, provider, PubSubTransportDirection.Send);
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
                Task firstClose = transport.CloseAsync().AsTask();
                await owner.Entered.Task.WaitAsync(TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
                Task secondClose = transport.CloseAsync().AsTask();
                Task disposal = transport.DisposeAsync().AsTask();
                Assert.That(disposal.IsCompleted, Is.False);
                owner.Release.TrySetResult(true);
                await Task.WhenAll(firstClose, secondClose, disposal).ConfigureAwait(false);
                await transport.CloseAsync().ConfigureAwait(false);
                await transport.DisposeAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(owner.DisposeCount, Is.EqualTo(1));
                    Assert.That(transport.IsConnected, Is.False);
                    Assert.That(core.Left.IsReady, Is.True);
                    Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
                });
            }
            finally
            {
                owner.Release.TrySetResult(true);
                await transport.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PendingOpenCanBeCancelledByCloseAndRetriedWithoutLosingTheNextFlow()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            await using var provider = new DataChannelContextPubSubFlowProvider(core.Right, false);
            await using DataChannelPubSubTransport transport = CreateTransport(
                core.Right.CarrierKind, provider, PubSubTransportDirection.Receive);
            Assert.That(transport.Channel, Is.Null);
            Task opening = transport.OpenAsync().AsTask();
            Assert.That(opening.IsCompleted, Is.False);

            await transport.CloseAsync().ConfigureAwait(false);
            await Assert.ThatAsync(() => opening,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(transport.IsConnected, Is.False);
            Task retry = transport.OpenAsync().AsTask();
            await using DataChannel sender = await core.Left.OpenAsync(
                DataChannelPubSubConfigurationTests.CreateParameters()).ConfigureAwait(false);
            await retry.ConfigureAwait(false);
            Assert.That(transport.IsConnected, Is.True);
        }

        [Test]
        public async Task BoundedAcceptWaitersRejectExcessAndReleaseCapacityOnCancellation()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            await using var provider = new DataChannelContextPubSubFlowProvider(core.Right, false, 1);
            using var cancellation = new CancellationTokenSource();
            Task<DataChannelPubSubFlow> first = provider.OpenAsync(
                new DataChannelPubSubFlowRequest(DataChannelPubSubConfigurationTests.CreateConnection()),
                cancellation.Token).AsTask();
            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await provider.OpenAsync(new DataChannelPubSubFlowRequest(
                    DataChannelPubSubConfigurationTests.CreateConnection(resource: "urn:test:pubsub:other")))
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.WouldBlock));

            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(() => first, Throws.InstanceOf<OperationCanceledException>());
            Task<DataChannelPubSubFlow> retry = provider.OpenAsync(new DataChannelPubSubFlowRequest(
                DataChannelPubSubConfigurationTests.CreateConnection())).AsTask();
            await using DataChannel sender = await core.Left.OpenAsync(
                DataChannelPubSubConfigurationTests.CreateParameters()).ConfigureAwait(false);
            await using DataChannelPubSubFlow received = await retry.ConfigureAwait(false);
            Assert.That(received.Channel.ChannelId, Is.EqualTo(sender.ChannelId));
        }

        [Test]
        public async Task ExactResourceAndProtocolRoutingDoesNotStealAnotherConnectionsFlow()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            await using var provider = new DataChannelContextPubSubFlowProvider(core.Right, false);
            var jsonRequest = new DataChannelPubSubFlowRequest(DataChannelPubSubConfigurationTests.CreateConnection(
                protocol: DataChannelProfiles.JsonPayload, resource: "urn:test:pubsub:json"));
            var uadpRequest = new DataChannelPubSubFlowRequest(DataChannelPubSubConfigurationTests.CreateConnection(
                resource: "urn:test:pubsub:uadp"));
            Task<DataChannelPubSubFlow> json = provider.OpenAsync(jsonRequest).AsTask();
            Task<DataChannelPubSubFlow> uadp = provider.OpenAsync(uadpRequest).AsTask();

            await using DataChannel uadpSender = await core.Left.OpenAsync(uadpRequest.Parameters)
                .ConfigureAwait(false);
            await using DataChannel jsonSender = await core.Left.OpenAsync(jsonRequest.Parameters)
                .ConfigureAwait(false);
            await using DataChannelPubSubFlow jsonReceiver = await json.ConfigureAwait(false);
            await using DataChannelPubSubFlow uadpReceiver = await uadp.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(jsonReceiver.Channel.ChannelId, Is.EqualTo(jsonSender.ChannelId));
                Assert.That(uadpReceiver.Channel.ChannelId, Is.EqualTo(uadpSender.ChannelId));
                Assert.That(jsonReceiver.Channel.ChannelId, Is.Not.EqualTo(uadpReceiver.Channel.ChannelId));
                Assert.That(jsonReceiver.Channel.Parameters.Protocol, Is.EqualTo(DataChannelProfiles.JsonPayload));
                Assert.That(uadpReceiver.Channel.Parameters.Protocol, Is.EqualTo(DataChannelProfiles.UadpPayload));
            });
        }

        [Test]
        public async Task ClosingOneResourceLeavesTheSiblingFlowAndSharedContextUsable()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            DataChannel firstSender = await core.Left.OpenAsync(
                DataChannelPubSubConfigurationTests.CreateParameters(resource: "urn:test:pubsub:first"))
                .ConfigureAwait(false);
            await using DataChannel firstReceiver = (await core.Right.AcceptAsync().ConfigureAwait(false))!;
            DataChannel secondSender = await core.Left.OpenAsync(
                DataChannelPubSubConfigurationTests.CreateParameters(resource: "urn:test:pubsub:second"))
                .ConfigureAwait(false);
            await using DataChannel secondReceiver = (await core.Right.AcceptAsync().ConfigureAwait(false))!;
            await using DataChannelPubSubTransport first = CreateTransport(core.Left.CarrierKind,
                new SingleFlowProvider(new DataChannelPubSubFlow(firstSender, core.Left.CarrierKind)),
                PubSubTransportDirection.Send, firstSender.Parameters);
            await using DataChannelPubSubTransport second = CreateTransport(core.Left.CarrierKind,
                new SingleFlowProvider(new DataChannelPubSubFlow(secondSender, core.Left.CarrierKind)),
                PubSubTransportDirection.Send, secondSender.Parameters);
            await first.OpenAsync().ConfigureAwait(false);
            await second.OpenAsync().ConfigureAwait(false);
            await first.CloseAsync().ConfigureAwait(false);
            await second.SendAsync(new byte[] { 5, 4, 3 }).ConfigureAwait(false);
            using DataChannelMessage? message = await secondReceiver.ReadAsync(
                TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.IsConnected, Is.False);
                Assert.That(second.IsConnected, Is.True);
                Assert.That(core.Left.IsReady, Is.True);
                Assert.That(core.Right.IsReady, Is.True);
                Assert.That(core.LeftBudget.Usage.Flows, Is.EqualTo(1));
                Assert.That(message!.Payload, Is.EqualTo(ByteString.From(new byte[] { 5, 4, 3 })));
            });
        }

        [Test]
        public async Task RemoteResetNotifiesAnIdlePublisherAndReopenUsesAFreshChannel()
        {
            await using TransportPair pair = await TransportPair.CreateAsync().ConfigureAwait(false);
            DataChannel originalChannel = OnlyChannel(pair.Core.Left);
            uint original = originalChannel.ChannelId;
            Assert.That(pair.Sender.Channel, Is.SameAs(originalChannel));
            var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.Sender.StateChanged += (_, args) =>
            {
                if (!args.IsConnected)
                {
                    disconnected.TrySetResult(true);
                }
            };

            await OnlyChannel(pair.Core.Right).ResetAsync(DataChannelResult.NotAuthorized).ConfigureAwait(false);
            await disconnected.Task.WaitAsync(TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
            Assert.That(pair.Sender.IsConnected, Is.False);
            await pair.Sender.CloseAsync().ConfigureAwait(false);
            await pair.Receiver.CloseAsync().ConfigureAwait(false);
            Assert.That(pair.Sender.Channel, Is.Null);
            await Task.WhenAll(pair.Sender.OpenAsync().AsTask(), pair.Receiver.OpenAsync().AsTask())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(pair.Sender.IsConnected, Is.True);
                Assert.That(OnlyChannel(pair.Core.Left).ChannelId, Is.Not.EqualTo(original));
                Assert.That(pair.Sender.Channel, Is.SameAs(OnlyChannel(pair.Core.Left)));
                Assert.That(pair.Core.LeftBudget.Usage.Flows, Is.EqualTo(1));
                Assert.That(pair.Core.RightBudget.Usage.Flows, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task RefusedConfiguredOpenLeavesNoFlowOrPayload()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            core.LeftAuthorizer.Deny = true;
            await using var provider = new DataChannelContextPubSubFlowProvider(core.Left, true);
            await using DataChannelPubSubTransport transport = CreateTransport(
                core.Left.CarrierKind, provider, PubSubTransportDirection.Send);

            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(
                async () => await transport.OpenAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
                Assert.That(transport.IsConnected, Is.False);
                Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
                Assert.That(core.LeftBudget.Usage.Operations, Is.Zero);
                Assert.That(core.LeftCarrier.Payloads, Is.Empty);
            });
        }

        [Test]
        public async Task CancelledSessionGrantReleasesItsReservationWithoutReusingTheIdentifier()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            var grants = new CancelledGrantProvider();
            await using var provider = new DataChannelContextPubSubFlowProvider(
                core.Left, true, maximumPendingFlows: 1, grantProvider: grants);
            using var cancellation = new CancellationTokenSource();
            Task<DataChannelPubSubFlow> opening = provider.OpenAsync(
                new DataChannelPubSubFlowRequest(DataChannelPubSubConfigurationTests.CreateConnection()),
                cancellation.Token).AsTask();
            await grants.Entered.Task.WaitAsync(TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(() => opening,
                Throws.InstanceOf<OperationCanceledException>());
            Assert.Multiple(() =>
            {
                Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
                Assert.That(core.LeftBudget.Usage.Operations, Is.Zero);
            });
            uint next = core.Left.ReserveChannelId();
            Assert.Multiple(() =>
            {
                Assert.That(next, Is.Not.EqualTo(grants.ChannelId));
                Assert.That(core.LeftBudget.Usage.Flows, Is.EqualTo(1));
                Assert.That(core.LeftCarrier.Payloads, Is.Empty);
                Assert.That(core.Left.ReleaseChannelId(next), Is.True);
                Assert.That(core.LeftBudget.Usage.Operations, Is.Zero);
                Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
            });
        }

        [Test]
        public async Task GrantProviderReceivesTheFreshBindingAndSelectsSessionOpening()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            var grants = new FixedGrantProvider(Guid.NewGuid());
            core.LeftAuthorizer.SessionGrant = grants.Grant;
            core.RightAuthorizer.SessionGrant = grants.Grant;
            await using var provider = new DataChannelContextPubSubFlowProvider(core.Left, true, grantProvider: grants);
            await using DataChannelPubSubFlow flow = await provider.OpenAsync(
                new DataChannelPubSubFlowRequest(DataChannelPubSubConfigurationTests.CreateConnection()))
                .ConfigureAwait(false);
            await using DataChannel remote = (await core.Right.AcceptAsync().ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(grants.ContextId, Is.EqualTo(core.Left.Identity.ContextId));
                Assert.That(grants.ChannelId, Is.EqualTo(flow.Channel.ChannelId));
                Assert.That(grants.ResourceUri, Is.EqualTo("urn:test:pubsub:data"));
                Assert.That(core.LeftAuthorizer.Requests, Is.Not.Empty);
                Assert.That(core.RightAuthorizer.Requests, Is.Not.Empty);
                Assert.That(core.LeftAuthorizer.Requests.All(request =>
                    request.Mode == DataChannelAuthorizationMode.Session && request.GrantId == grants.Grant), Is.True);
                Assert.That(core.RightAuthorizer.Requests.All(request =>
                    request.Mode == DataChannelAuthorizationMode.Session && request.GrantId == grants.Grant), Is.True);
                Assert.That(flow.Channel.GetStatus().SessionId, Is.EqualTo(new NodeId(99, 1)));
            });
        }

        [Test]
        public async Task EmptyGrantCannotSilentlyDowngradeToConfiguredAuthorization()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            await using var provider = new DataChannelContextPubSubFlowProvider(
                core.Left, true, grantProvider: new FixedGrantProvider(Guid.Empty));
            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await provider.OpenAsync(new DataChannelPubSubFlowRequest(
                    DataChannelPubSubConfigurationTests.CreateConnection())).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
                Assert.That(core.LeftAuthorizer.Requests, Is.Empty);
                Assert.That(core.LeftBudget.Usage.Operations, Is.Zero);
                Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
            });
        }

        /// <summary>
        /// Builds the real factory/transport for a chosen configured role.
        /// </summary>
        internal static DataChannelPubSubTransport CreateTransport(
            DataChannelCarrierKind carrier,
            IDataChannelPubSubFlowProvider provider,
            PubSubTransportDirection direction,
            DataChannelParametersDataType? parameters = null)
        {
            PubSubConnectionDataType configuration = DataChannelPubSubConfigurationTests.CreateConnection(
                carrier, direction: direction);
            DataChannelPubSubConfiguration.Configure(configuration, carrier,
                parameters ?? DataChannelPubSubConfigurationTests.CreateParameters());
            return (DataChannelPubSubTransport)new DataChannelPubSubTransportFactory(carrier, provider).Create(
                configuration, NUnitTelemetryContext.Create(), TimeProvider.System);
        }

        private static DataChannel OnlyChannel(DataChannelContext context)
        {
            ArrayOf<DataChannel> channels = context.Channels;
            Assert.That(channels, Has.Count.EqualTo(1));
            return channels[0];
        }

        private sealed class SingleFlowProvider(DataChannelPubSubFlow flow) : IDataChannelPubSubFlowProvider
        {
            public ValueTask<DataChannelPubSubFlow> OpenAsync(
                DataChannelPubSubFlowRequest request,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<DataChannelPubSubFlow>(flow);
            }
        }

        private sealed class CancelledGrantProvider : IDataChannelPubSubGrantProvider
        {
            public uint ChannelId { get; private set; }

            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async ValueTask<Guid> AcquireAsync(
                DataChannelContext context,
                uint channelId,
                DataChannelParametersDataType parameters,
                CancellationToken cancellationToken = default)
            {
                ChannelId = channelId;
                Entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return Guid.Empty;
            }
        }

        private sealed class FixedGrantProvider(Guid grant) : IDataChannelPubSubGrantProvider
        {
            public Guid Grant { get; } = grant;

            public Guid ContextId { get; private set; }

            public uint ChannelId { get; private set; }

            public string ResourceUri { get; private set; } = string.Empty;

            public ValueTask<Guid> AcquireAsync(
                DataChannelContext context,
                uint channelId,
                DataChannelParametersDataType parameters,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ContextId = context.Identity.ContextId;
                ChannelId = channelId;
                ResourceUri = parameters.ResourceUri ?? throw new ArgumentException("Missing test resource.");
                return new ValueTask<Guid>(Grant);
            }
        }

        private sealed class BlockingFlowOwner : IAsyncDisposable
        {
            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int DisposeCount { get; private set; }

            public async ValueTask DisposeAsync()
            {
                DisposeCount++;
                Entered.TrySetResult(true);
                await Release.Task.ConfigureAwait(false);
            }
        }

        private sealed class TransportPair : IAsyncDisposable
        {
            private TransportPair(
                DataChannelTestPair core,
                DataChannelContextPubSubFlowProvider senderProvider,
                DataChannelContextPubSubFlowProvider receiverProvider,
                DataChannelPubSubTransport sender,
                DataChannelPubSubTransport receiver)
            {
                Core = core;
                m_senderProvider = senderProvider;
                m_receiverProvider = receiverProvider;
                Sender = sender;
                Receiver = receiver;
            }

            public DataChannelTestPair Core { get; }

            public DataChannelPubSubTransport Sender { get; }

            public DataChannelPubSubTransport Receiver { get; }

            public static async Task<TransportPair> CreateAsync(
                DataChannelCarrierKind carrier = DataChannelCarrierKind.SharedUasc,
                string protocol = DataChannelProfiles.UadpPayload,
                DataChannelParametersDataType? parameters = null,
                DataChannelOptions? options = null)
            {
                DataChannelTestPair core = await DataChannelTestPair.CreateAsync(
                    carrier, options, TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
                var senderProvider = new DataChannelContextPubSubFlowProvider(core.Left, true);
                var receiverProvider = new DataChannelContextPubSubFlowProvider(core.Right, false);
                parameters ??= DataChannelPubSubConfigurationTests.CreateParameters(protocol);
                DataChannelPubSubTransport sender = CreateTransport(
                    carrier, senderProvider, PubSubTransportDirection.Send, parameters);
                DataChannelPubSubTransport receiver = CreateTransport(
                    carrier, receiverProvider, PubSubTransportDirection.Receive, parameters);
                var pair = new TransportPair(core, senderProvider, receiverProvider, sender, receiver);
                try
                {
                    await Task.WhenAll(
                        sender.OpenAsync(TestContext.CurrentContext.CancellationToken).AsTask(),
                        receiver.OpenAsync(TestContext.CurrentContext.CancellationToken).AsTask())
                        .ConfigureAwait(false);
                    return pair;
                }
                catch
                {
                    await pair.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public async ValueTask DisposeAsync()
            {
                await Sender.DisposeAsync().ConfigureAwait(false);
                await Receiver.DisposeAsync().ConfigureAwait(false);
                await m_senderProvider.DisposeAsync().ConfigureAwait(false);
                await m_receiverProvider.DisposeAsync().ConfigureAwait(false);
                await Core.DisposeAsync().ConfigureAwait(false);
            }

            private readonly DataChannelContextPubSubFlowProvider m_senderProvider;
            private readonly DataChannelContextPubSubFlowProvider m_receiverProvider;
        }
    }
}
