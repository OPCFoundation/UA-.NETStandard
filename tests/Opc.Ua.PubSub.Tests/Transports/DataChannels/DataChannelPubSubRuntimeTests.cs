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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transcoding;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Transports.DataChannels;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transports.DataChannels
{
    /// <summary>
    /// Exercises the actual application, groups, codecs, metadata, security and sinks across Core flow contexts.
    /// </summary>
    [TestFixture]
    [CancelAfter(30000)]
    public sealed class DataChannelPubSubRuntimeTests
    {
        [Test]
        public Task PublisherSubscriberRuntimePreservesIdsMetadataAndMultiwriterBytes(
            [Values] DataChannelCarrierKind carrier)
        {
            return VerifyRuntimeAsync(carrier, DataChannelProfiles.UadpPayload, MessageSecurityMode.None);
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public async Task MultiwriterMessageSpanningResourcesIsNotPartiallyDelivered(
            DataChannelCarrierKind carrier)
        {
            CancellationToken cancellationToken = TestContext.CurrentContext.CancellationToken;
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync(
                carrier, cancellationToken: cancellationToken).ConfigureAwait(false);
            await using var publishingFlows = new DataChannelContextPubSubFlowProvider(core.Left, true);
            await using var subscribingFlows = new DataChannelContextPubSubFlowProvider(core.Right, false);
            var publishingFactory = new RecordingFactory(
                new DataChannelPubSubTransportFactory(carrier, publishingFlows));
            var subscribingFactory = new RecordingFactory(
                new DataChannelPubSubTransportFactory(carrier, subscribingFlows));
            var first = new CollectingSink();
            var second = new CollectingSink();
            var third = new CollectingSink();
            var firstSource = new TestSource("TelemetryA", "alpha");
            var secondSource = new TestSource("TelemetryB", "beta");
            await using IPubSubApplication publisher = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(CreateCrossResourcePublisherConfiguration(carrier))
                .UseAllStandardEncoders()
                .AddTransportFactory(publishingFactory)
                .AddDataSetSource("TelemetryA", firstSource)
                .AddDataSetSource("TelemetryB", secondSource)
                .Build();
            await using IPubSubApplication subscriber = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(CreateCrossResourceSubscriberConfiguration(carrier))
                .UseAllStandardEncoders()
                .AddTransportFactory(subscribingFactory)
                .AddSubscribedDataSetSink("ReaderA", first)
                .AddSubscribedDataSetSink("ReaderB", second)
                .AddSubscribedDataSetSink("ReaderC", third)
                .Build();
            await Task.WhenAll(
                publisher.StartAsync(cancellationToken).AsTask(),
                subscriber.StartAsync(cancellationToken).AsTask()).ConfigureAwait(false);
            int prePublishMessageCount = publishingFactory.Transports.Values
                .Sum(transport => transport.Sent.Count);
            DataChannelResult[] prePublishResults = new DataChannelResult[prePublishMessageCount];
            for (int i = 0; i < prePublishResults.Length; i++)
            {
                prePublishResults[i] = await core.LeftCarrier.DataResults.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            var writer = (WriterGroup)publisher.Connections[0].WriterGroups[0];
            await writer.PublishOnceAsync(cancellationToken).ConfigureAwait(false);
            DataChannelResult receiveResult = await core.LeftCarrier.DataResults.ReadAsync(cancellationToken)
                .ConfigureAwait(false);
            ByteString[] sent = [.. publishingFactory.Transports["PublisherFirst"].Sent];
            ByteString[] crossResourceMessages = [.. sent.Where(IsTwoWriterGroupMessage)];
            string headerSummary = string.Join(";", sent.Select(message =>
                UadpDecoder.TryReadPrefix(message.Memory, out UadpPrefixInfo prefix)
                    ? $"{prefix.WriterGroupId}:{prefix.PayloadCount}:{prefix.IsDataSetMessage}"
                    : "invalid"));

            Assert.Multiple(() =>
            {
                Assert.That(prePublishResults, Has.Exactly(2).EqualTo(DataChannelResult.Good),
                    $"Metadata for the two resource-matched writers must be admitted; " +
                    $"results: {string.Join(",", prePublishResults)}.");
                Assert.That(prePublishResults, Has.Exactly(1).EqualTo(DataChannelResult.UnknownChannel),
                    "Writer B metadata must not cross from its RequestId-selected first resource to the second.");
                Assert.That(crossResourceMessages, Has.Length.EqualTo(1),
                    $"The input must be one two-writer NetworkMessage; emitted headers: {headerSummary}; " +
                    $"pre-publish receive results: {string.Join(",", prePublishResults)}.");
                Assert.That(firstSource.Samples, Is.EqualTo(1));
                Assert.That(secondSource.Samples, Is.EqualTo(1));
                Assert.That(first.Count, Is.Zero,
                    "The writer mapped to the first resource must not be delivered on a cross-resource message.");
                Assert.That(second.Count, Is.Zero,
                    "The writer mapped to the second resource must not be delivered on a cross-resource message.");
                Assert.That(third.Count, Is.Zero);
                Assert.That(receiveResult, Is.EqualTo(DataChannelResult.UnknownChannel));
                Assert.That(core.LeftCarrier.Failures.Concat(core.RightCarrier.Failures),
                    Does.Contain(DataChannelResult.UnknownChannel));
            });
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public async Task DuplexPubSubAuthorizesPublisherIdsIndependentlyPerDirection(
            DataChannelCarrierKind carrier)
        {
            CancellationToken cancellationToken = TestContext.CurrentContext.CancellationToken;
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync(
                carrier, cancellationToken: cancellationToken).ConfigureAwait(false);
            await using var leftFlows = new DataChannelContextPubSubFlowProvider(core.Left, true);
            await using var rightFlows = new DataChannelContextPubSubFlowProvider(core.Right, false);
            var leftFactory = new RecordingFactory(new DataChannelPubSubTransportFactory(carrier, leftFlows));
            var rightFactory = new RecordingFactory(new DataChannelPubSubTransportFactory(carrier, rightFlows));
            var leftSink = new CollectingSink();
            var rightSink = new CollectingSink();
            var leftSource = new TestSource("TelemetryLeft", "left");
            var rightSource = new TestSource("TelemetryRight", "right");
            await using IPubSubApplication left = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(CreateDuplexConfiguration(
                    carrier, 42, 42, 1001, 17, 43, 2001, "TelemetryLeft", "ReaderRight"))
                .UseAllStandardEncoders()
                .AddTransportFactory(leftFactory)
                .AddDataSetSource("TelemetryLeft", leftSource)
                .AddSubscribedDataSetSink("ReaderRight", leftSink)
                .Build();
            await using IPubSubApplication right = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(CreateDuplexConfiguration(
                    carrier, 17, 43, 2001, 42, 42, 1001, "TelemetryRight", "ReaderLeft"))
                .UseAllStandardEncoders()
                .AddTransportFactory(rightFactory)
                .AddDataSetSource("TelemetryRight", rightSource)
                .AddSubscribedDataSetSink("ReaderLeft", rightSink)
                .Build();
            await Task.WhenAll(
                left.StartAsync(cancellationToken).AsTask(),
                right.StartAsync(cancellationToken).AsTask()).ConfigureAwait(false);

            int leftStartupMessages = leftFactory.Transports.Values.Sum(transport => transport.Sent.Count);
            int rightStartupMessages = rightFactory.Transports.Values.Sum(transport => transport.Sent.Count);
            DataChannelResult[] leftStartupResults = new DataChannelResult[leftStartupMessages];
            DataChannelResult[] rightStartupResults = new DataChannelResult[rightStartupMessages];
            for (int i = 0; i < leftStartupResults.Length; i++)
            {
                leftStartupResults[i] = await core.LeftCarrier.DataResults.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            for (int i = 0; i < rightStartupResults.Length; i++)
            {
                rightStartupResults[i] = await core.RightCarrier.DataResults.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            var wrongDirectionMetadata = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromUInt32(17),
                DataSetWriterId = 2001,
                DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                DataSetMetaData = new TestSource("Unexpected", "ignored").BuildMetaData(),
                StatusCode = StatusCodes.Good
            };
            await ((PubSubConnection)left.Connections[0]).SendMetaDataAnnouncementAsync(
                wrongDirectionMetadata, null, cancellationToken).ConfigureAwait(false);
            DataChannelResult refused = await core.LeftCarrier.DataResults.ReadAsync(cancellationToken)
                .ConfigureAwait(false);

            await Task.WhenAll(
                ((WriterGroup)left.Connections[0].WriterGroups[0]).PublishOnceAsync(cancellationToken).AsTask(),
                ((WriterGroup)right.Connections[0].WriterGroups[0]).PublishOnceAsync(cancellationToken).AsTask())
                .ConfigureAwait(false);
            ArrayOf<DataSetField> leftFields = await leftSink.First.WaitAsync(cancellationToken).ConfigureAwait(false);
            ArrayOf<DataSetField> rightFields = await rightSink.First.WaitAsync(cancellationToken).ConfigureAwait(false);
            DataChannelResult leftDelivery = await core.LeftCarrier.DataResults.ReadAsync(cancellationToken)
                .ConfigureAwait(false);
            DataChannelResult rightDelivery = await core.RightCarrier.DataResults.ReadAsync(cancellationToken)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(leftStartupResults, Is.All.EqualTo(DataChannelResult.Good));
                Assert.That(rightStartupResults, Is.All.EqualTo(DataChannelResult.Good));
                Assert.That(refused, Is.EqualTo(DataChannelResult.UnknownChannel),
                    "The left-to-right flow must not borrow the right-to-left PublisherId association.");
                Assert.That(leftDelivery, Is.EqualTo(DataChannelResult.Good));
                Assert.That(rightDelivery, Is.EqualTo(DataChannelResult.Good));
                Assert.That(leftFields[1].Value, Is.EqualTo(new Variant("right \u03a9")),
                    "The left reader accepts the PublisherId configured for the reverse direction.");
                Assert.That(rightFields[1].Value, Is.EqualTo(new Variant("left \u03a9")),
                    "The right reader accepts the PublisherId configured for the forward direction.");
                Assert.That(leftSink.Count, Is.EqualTo(1));
                Assert.That(rightSink.Count, Is.EqualTo(1));
                Assert.That(leftSource.Samples, Is.EqualTo(1));
                Assert.That(rightSource.Samples, Is.EqualTo(1));
                Assert.That(core.LeftCarrier.Failures, Does.Contain(DataChannelResult.UnknownChannel));
                Assert.That(core.RightCarrier.Failures, Is.Empty);
            });
        }

        [TestCase(MessageSecurityMode.Sign)]
        [TestCase(MessageSecurityMode.SignAndEncrypt)]
        public Task ExistingPart14MessageSecurityWorksOnCarrierProtectedFlowsWithoutABroker(MessageSecurityMode mode)
        {
            return VerifyRuntimeAsync(DataChannelCarrierKind.SharedUasc, DataChannelProfiles.UadpPayload, mode);
        }

        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.SignAndEncrypt)]
        public Task ExistingPart14ChunkingAndReassemblyHonorTheNegotiatedFlowLimit(MessageSecurityMode mode)
        {
            return VerifyRuntimeAsync(DataChannelCarrierKind.SharedUasc, DataChannelProfiles.UadpPayload,
                mode, chunked: true);
        }

        [Test]
        public async Task NegotiatedMismatchNeverStartsAWriterOrSamplesItsSource()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            DataChannel channel = await core.Left.OpenAsync(
                DataChannelPubSubConfigurationTests.CreateParameters()).ConfigureAwait(false);
            await using DataChannel accepted = (await core.Right.AcceptAsync().ConfigureAwait(false))!;
            var provider = new Mock<IDataChannelPubSubFlowProvider>(MockBehavior.Strict);
            provider.Setup(instance => instance.OpenAsync(
                It.IsAny<DataChannelPubSubFlowRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DataChannelPubSubFlow>(
                    new DataChannelPubSubFlow(channel, DataChannelCarrierKind.SharedUasc)));
            var source = new TestSource("TelemetryA", "alpha");
            PubSubConfigurationDataType configuration = CreatePublisherConfiguration(
                DataChannelCarrierKind.SharedUasc, DataChannelProfiles.UadpPayload, MessageSecurityMode.None);
            PubSubConnectionDataType configuredConnection = configuration.Connections[0];
            DataChannelParametersDataType requested = DataChannelPubSubConfiguration.GetParameters(
                configuredConnection);
            requested.MaxMessageSize /= 2;
            requested.MaxReceiveBytes /= 2;
            DataChannelPubSubConfiguration.Configure(
                configuredConnection, DataChannelCarrierKind.SharedUasc, requested);
            await using IPubSubApplication application = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(configuration)
                .UseAllStandardEncoders()
                .UseDataChannelTransport(DataChannelCarrierKind.SharedUasc, provider.Object)
                .AddDataSetSource("TelemetryA", source)
                .AddDataSetSource("TelemetryB", new TestSource("TelemetryB", "beta"))
                .Build();

            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(
                async () => await application.StartAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                Assert.That(source.Samples, Is.Zero);
                Assert.That(application.Connections[0].WriterGroups[0].State.State,
                    Is.Not.EqualTo(PubSubState.Operational));
                Assert.That(channel.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
                Assert.That(core.LeftCarrier.Payloads, Is.Empty);
            });
        }

        [Test]
        public async Task FlowFailurePausesTheActualWriterAndDisableReleasesAllFlowResources()
        {
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync().ConfigureAwait(false);
            await using var flows = new DataChannelContextPubSubFlowProvider(core.Left, true);
            var source = new TestSource("TelemetryA", "alpha");
            await using IPubSubApplication publisher = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithTimeProvider(new FakeTimeProvider())
                .UseConfiguration(CreatePublisherConfiguration(
                    core.Left.CarrierKind, DataChannelProfiles.UadpPayload, MessageSecurityMode.None))
                .UseAllStandardEncoders()
                .UseDataChannelTransport(core.Left.CarrierKind, flows)
                .AddDataSetSource("TelemetryA", source)
                .AddDataSetSource("TelemetryB", new TestSource("TelemetryB", "beta"))
                .Build();
            await publisher.StartAsync().ConfigureAwait(false);
            await using DataChannel remote = (await core.Right.AcceptAsync().ConfigureAwait(false))!;
            IPubSubConnection connection = publisher.Connections[0];
            var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.State.StateChanged += (_, args) =>
            {
                if (connection.State.State == PubSubState.Error)
                {
                    failed.TrySetResult(true);
                }
            };

            await remote.ResetAsync(DataChannelResult.NotAuthorized).ConfigureAwait(false);
            await failed.Task.WaitAsync(TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(connection.State.State, Is.EqualTo(PubSubState.Error));
                Assert.That(connection.State.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(connection.WriterGroups[0].State.State, Is.EqualTo(PubSubState.Paused));
                Assert.That(source.Samples, Is.Zero);
            });
            await publisher.StopAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
                Assert.That(core.LeftBudget.Usage.Messages, Is.Zero);
                Assert.That(connection.State.State, Is.EqualTo(PubSubState.Disabled));
            });
        }

        [TestCase(6, true)]
        [TestCase(1, false)]
        public Task OrderlyEndPreservesTheFinalPendingReaderDispatch(int cycles, bool revise)
        {
            return VerifyPendingReaderDispatchAsync(cycles, revise, DataChannelResult.Good);
        }

        [Test]
        public Task RevocationStopsThePendingReaderDispatch()
        {
            return VerifyPendingReaderDispatchAsync(1, false, DataChannelResult.NotAuthorized);
        }

        private static async Task VerifyPendingReaderDispatchAsync(
            int cycles,
            bool revise,
            DataChannelResult terminalResult)
        {
            CancellationToken cancellationToken = TestContext.CurrentContext.CancellationToken;
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync(
                DataChannelCarrierKind.DedicatedUasc, cancellationToken: cancellationToken).ConfigureAwait(false);
            await using var publishingFlows = new DataChannelContextPubSubFlowProvider(core.Left, true);
            await using var subscribingFlows = new DataChannelContextPubSubFlowProvider(core.Right, false);
            var publishingFactory = new RecordingFactory(
                new DataChannelPubSubTransportFactory(core.Left.CarrierKind, publishingFlows));
            var subscribingFactory = new RecordingFactory(
                new DataChannelPubSubTransportFactory(core.Right.CarrierKind, subscribingFlows));
            var first = new CollectingSink();
            var second = new CollectingSink();
            await using IPubSubApplication publisher = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithTimeProvider(new FakeTimeProvider())
                .UseConfiguration(CreatePublisherConfiguration(
                    core.Left.CarrierKind, DataChannelProfiles.UadpPayload, MessageSecurityMode.None))
                .UseAllStandardEncoders()
                .AddTransportFactory(publishingFactory)
                .AddDataSetSource("TelemetryA", new TestSource("TelemetryA", "alpha"))
                .AddDataSetSource("TelemetryB", new TestSource("TelemetryB", "beta"))
                .Build();
            await using IPubSubApplication subscriber = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithTimeProvider(new FakeTimeProvider())
                .UseConfiguration(CreateSubscriberConfiguration(
                    core.Right.CarrierKind, DataChannelProfiles.UadpPayload, MessageSecurityMode.None))
                .UseAllStandardEncoders()
                .AddTransportFactory(subscribingFactory)
                .AddSubscribedDataSetSink("ReaderA", first)
                .AddSubscribedDataSetSink("ReaderB", second)
                .Build();
            var observer = new BlockingMessageObserver(cycles);
            using IDisposable registration = ((PubSubConnection)subscriber.Connections[0])
                .RegisterReceivedNetworkMessageSink(observer);
            await Task.WhenAll(publisher.StartAsync(cancellationToken).AsTask(),
                subscriber.StartAsync(cancellationToken).AsTask()).ConfigureAwait(false);
            DataChannel outgoing = core.Left.Channels[0];
            DataChannel incoming = core.Right.Channels[0];
            var remoteClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            incoming.StateChanged += change =>
            {
                if (change.Status.State is DataChannelState.Closed or DataChannelState.Faulted)
                {
                    remoteClosed.TrySetResult(true);
                }
            };
            try
            {
                var writer = (WriterGroup)publisher.Connections[0].WriterGroups[0];
                for (int cycle = 0; cycle < cycles; cycle++)
                {
                    await writer.PublishOnceAsync(cancellationToken).ConfigureAwait(false);
                    if (revise && cycle == 2)
                    {
                        DataChannelParametersDataType revised = outgoing.Parameters;
                        revised.MaxMessageSize = 16384;
                        revised.MaxReceiveBytes = 131072;
                        Assert.That(await outgoing.UpdateAsync(revised, cancellationToken: cancellationToken).ConfigureAwait(false),
                            Is.EqualTo(DataChannelResult.Good));
                    }
                }
                await observer.Entered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(first.Count, Is.EqualTo(cycles - 1));
                    Assert.That(second.Count, Is.EqualTo(cycles - 1));
                    Assert.That(subscribingFactory.Transport!.Received, Has.Count.EqualTo(cycles + 2));
                    Assert.That(outgoing.Revision, Is.EqualTo(revise ? 2 : 1));
                });

                if (terminalResult == DataChannelResult.Good)
                {
                    Assert.That(await outgoing.EndAsync(cancellationToken).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                }
                else
                {
                    await outgoing.ResetAsync(terminalResult, cancellationToken).ConfigureAwait(false);
                }
                await remoteClosed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                Assert.That(subscriber.Connections[0].ReaderGroups[0].State.State,
                    Is.EqualTo(terminalResult == DataChannelResult.Good
                        ? PubSubState.Operational : PubSubState.Paused),
                    "Only orderly closure may drain delivery; authorization faults remain fail-closed.");
                observer.Release.TrySetResult(true);
                await subscribingFactory.Transport!.ReceiveCompleted.WaitAsync(cancellationToken).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    int expected = terminalResult == DataChannelResult.Good ? cycles : cycles - 1;
                    Assert.That(first.Count, Is.EqualTo(expected));
                    Assert.That(second.Count, Is.EqualTo(expected));
                    Assert.That(subscribingFactory.Transport.Received.ToArray(),
                        Is.EqualTo(publishingFactory.Transport!.Sent.ToArray()));
                });
            }
            finally
            {
                observer.Release.TrySetResult(true);
            }
        }

        private static async Task VerifyRuntimeAsync(
            DataChannelCarrierKind carrier,
            string protocol,
            MessageSecurityMode security,
            bool chunked = false)
        {
            CancellationToken cancellationToken = TestContext.CurrentContext.CancellationToken;
            await using DataChannelTestPair core = await DataChannelTestPair.CreateAsync(
                carrier, cancellationToken: cancellationToken).ConfigureAwait(false);
            await using var publishingFlows = new DataChannelContextPubSubFlowProvider(core.Left, true);
            await using var subscribingFlows = new DataChannelContextPubSubFlowProvider(core.Right, false);
            var publishingFactory = new RecordingFactory(
                new DataChannelPubSubTransportFactory(carrier, publishingFlows));
            var subscribingFactory = new RecordingFactory(
                new DataChannelPubSubTransportFactory(carrier, subscribingFlows));
            var first = new CollectingSink();
            var second = new CollectingSink();
            var unmatched = new CollectingSink();
            string firstLabel = "alpha" + (chunked ? new string('x', 6000) : string.Empty);
            string secondLabel = "beta" + (chunked ? new string('y', 6000) : string.Empty);
            var firstSource = new TestSource("TelemetryA", firstLabel);
            var secondSource = new TestSource("TelemetryB", secondLabel);
            var clock = new FakeTimeProvider();
            using PubSubSecurityKeyRing keys = DataChannelPubSubTestData.CreateKeyRing();
            PubSubApplicationBuilder publisherBuilder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithTimeProvider(clock)
                .UseAllStandardEncoders()
                .UseConfiguration(ConfigureFlowLimit(
                    CreatePublisherConfiguration(carrier, protocol, security), chunked))
                .AddTransportFactory(publishingFactory)
                .AddDataSetSource("TelemetryA", firstSource)
                .AddDataSetSource("TelemetryB", secondSource);
            PubSubApplicationBuilder subscriberBuilder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithTimeProvider(clock)
                .UseAllStandardEncoders()
                .UseConfiguration(ConfigureFlowLimit(
                    CreateSubscriberConfiguration(carrier, protocol, security), chunked))
                .AddTransportFactory(subscribingFactory)
                .AddSubscribedDataSetSink("ReaderA", first)
                .AddSubscribedDataSetSink("ReaderB", second)
                .AddSubscribedDataSetSink("Unmatched", unmatched);
            if (security != MessageSecurityMode.None)
            {
                publisherBuilder.AddSecurityKeyProvider(
                    new StaticSecurityKeyProvider(DataChannelPubSubTestData.SecurityGroup, keys));
                subscriberBuilder.AddSecurityKeyProvider(
                    new StaticSecurityKeyProvider(DataChannelPubSubTestData.SecurityGroup, keys));
            }
            await using IPubSubApplication publisher = publisherBuilder.Build();
            await using IPubSubApplication subscriber = subscriberBuilder.Build();
            await Task.WhenAll(publisher.StartAsync(cancellationToken).AsTask(),
                subscriber.StartAsync(cancellationToken).AsTask()).ConfigureAwait(false);

            var publishingConnection = (PubSubConnection)publisher.Connections[0];
            await publishingConnection.SendMetaDataAnnouncementAsync(
                CreateUnexpectedMetaData(protocol), null, cancellationToken).ConfigureAwait(false);
            var writer = (WriterGroup)publishingConnection.WriterGroups[0];
            await writer.PublishOnceAsync(cancellationToken).ConfigureAwait(false);
            ArrayOf<DataSetField> firstFields = await first.First.WaitAsync(cancellationToken).ConfigureAwait(false);
            ArrayOf<DataSetField> secondFields = await second.First.WaitAsync(cancellationToken).ConfigureAwait(false);
            AssertFields(firstFields, firstLabel);
            AssertFields(secondFields, secondLabel);

            ByteString[] sent = [.. publishingFactory.Transport!.Sent];
            ByteString[] received = [.. subscribingFactory.Transport!.Received];
            ByteString[] commonRecordBodies = [.. core.LeftCarrier.Payloads];
            bool hasConfiguredUascPublisherRouting = protocol == DataChannelProfiles.UadpPayload &&
                carrier is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc or
                    DataChannelCarrierKind.QuicUasc or DataChannelCarrierKind.WebTransportUasc;
            ByteString[] rejectedPublisherMessages = hasConfiguredUascPublisherRouting
                ? [.. sent.Where(IsUnexpectedPublisherMessage)]
                : [];
            ByteString[] expectedDelivered = hasConfiguredUascPublisherRouting
                ? [.. sent.Where(frame => !IsUnexpectedPublisherMessage(frame))]
                : sent;
            var firstKey = new DataSetMetaDataKey(PublisherId.FromString(PublisherName), 0, 1001, Uuid.Empty, 1);
            var secondKey = new DataSetMetaDataKey(PublisherId.FromString(PublisherName), 0, 1002, Uuid.Empty, 1);
            IDataSetMetaDataRegistry inboundMetaData =
                ((PubSubConnection)subscriber.Connections[0]).InboundMetaDataRegistry;
            Assert.Multiple(() =>
            {
                if (chunked)
                {
                    Assert.That(sent, Has.Length.GreaterThan(4));
                    Assert.That(sent.All(frame => frame.Length <= 2048), Is.True);
                }
                else
                {
                    Assert.That(sent, Has.Length.EqualTo(4),
                        "Two metadata messages, one filtered announcement and one data message.");
                }
                Assert.That(rejectedPublisherMessages, Has.Length.EqualTo(
                    hasConfiguredUascPublisherRouting ? 1 : 0),
                    "The configured UADP association rejects the unconfigured PublisherId.");
                Assert.That(received, Is.EqualTo(expectedDelivered),
                    "Admitted NetworkMessages must arrive byte-for-byte unchanged.");
                Assert.That(commonRecordBodies, Is.EqualTo(sent),
                    "No payload envelope may be added inside a Core DATA record.");
                Assert.That(first.Count, Is.EqualTo(1));
                Assert.That(second.Count, Is.EqualTo(1));
                Assert.That(unmatched.Count, Is.Zero);
                Assert.That(((PubSubApplication)publisher).Diagnostics.Read(
                    PubSubDiagnosticsCounterKind.SentDataSetMessages), Is.EqualTo(2));
                Assert.That(((PubSubApplication)subscriber).Diagnostics.Read(
                    PubSubDiagnosticsCounterKind.ReceivedDataSetMessages), Is.EqualTo(2));
                Assert.That(core.Left.Channels, Has.Count.EqualTo(1), "Both writers share the configured flow.");
                Assert.That(publishingConnection.PublisherId, Is.EqualTo(PublisherId.FromString(PublisherName)));
                Assert.That(writer.WriterGroupId, Is.EqualTo(42));
                Assert.That((writer.DataSetWriters.ToArray() ?? []).Select(item => item.DataSetWriterId),
                    Is.EqualTo(new ushort[] { 1001, 1002 }));
                Assert.That(inboundMetaData.TryGet(in firstKey, out DataSetMetaDataType? firstMeta),
                    Is.EqualTo(MetaDataMatchResult.Match));
                Assert.That(firstMeta!.Name, Is.EqualTo("TelemetryA"),
                    "Must use metadata actually received, not the reader seed.");
                Assert.That(inboundMetaData.TryGet(in secondKey, out DataSetMetaDataType? secondMeta),
                    Is.EqualTo(MetaDataMatchResult.Match));
                Assert.That(secondMeta!.Name, Is.EqualTo("TelemetryB"));
                Assert.That((inboundMetaData.Keys.ToArray() ?? []).All(
                    key => key.PublisherId != PublisherId.FromString("unexpected")),
                    Is.True, "Metadata from an unconfigured publisher must not be registered.");
                Assert.That(subscriber.MetaDataRegistry.TryGet(in firstKey, out _),
                    Is.EqualTo(MetaDataMatchResult.NotFound),
                    "Received metadata must remain scoped to its connection.");
                Assert.That(core.LeftAuthorizer.Requests, Is.Not.Empty);
                Assert.That(core.RightAuthorizer.Requests, Is.Not.Empty);
                Assert.That(core.LeftAuthorizer.Requests.All(
                    request => request.Mode == DataChannelAuthorizationMode.Configured),
                    Is.True);
                Assert.That(core.RightAuthorizer.Requests.All(
                    request => request.Mode == DataChannelAuthorizationMode.Configured),
                    Is.True);
                Assert.That(core.LeftCarrier.Failures, Is.EqualTo(
                    rejectedPublisherMessages.Length == 0
                        ? Array.Empty<DataChannelResult>()
                        : [DataChannelResult.UnknownChannel]),
                    "Only the deliberately unmatched UADP PublisherId is rejected.");
                Assert.That(core.RightCarrier.Failures, Is.Empty);
            });
            if (protocol == DataChannelProfiles.UadpPayload)
            {
                Assert.That(UadpDecoder.TryReadPrefix(sent[^1].Memory, out UadpPrefixInfo prefix), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(prefix.SecurityEnabled, Is.EqualTo(security != MessageSecurityMode.None));
                    Assert.That(prefix.PublisherId, Is.EqualTo(PublisherId.FromString(PublisherName)));
                    Assert.That(prefix.WriterGroupId, Is.EqualTo(42));
                    Assert.That(prefix.ChunkMessage, Is.EqualTo(chunked));
                });
            }
            else
            {
                string data = System.Text.Encoding.UTF8.GetString(sent[^1].ToArray());
                Assert.Multiple(() =>
                {
                    Assert.That(data, Does.Contain("\"MessageType\":\"ua-data\""));
                    Assert.That(data, Does.Contain("\"DataSetWriterId\":1001"));
                    Assert.That(data, Does.Contain("\"DataSetWriterId\":1002"));
                    Assert.That(System.Text.Encoding.UTF8.GetString(sent[0].ToArray()), Does.Contain("ua-metadata"));
                });
            }
            await subscriber.StopAsync(cancellationToken).ConfigureAwait(false);
            await publisher.StopAsync(cancellationToken).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(core.LeftBudget.Usage.Flows, Is.Zero);
                Assert.That(core.RightBudget.Usage.Flows, Is.Zero);
            });
            // Shared Core contexts can still own close-control reservations after the PubSub flows stop.
            await core.Left.DisposeAsync().ConfigureAwait(false);
            await core.Right.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(core.LeftBudget.Usage.Messages, Is.Zero);
                Assert.That(core.RightBudget.Usage.Messages, Is.Zero);
            });
        }

        private static PubSubConfigurationDataType ConfigureFlowLimit(
            PubSubConfigurationDataType configuration,
            bool chunked)
        {
            if (chunked)
            {
                PubSubConnectionDataType connection = configuration.Connections[0];
                DataChannelParametersDataType parameters = DataChannelPubSubConfiguration.GetParameters(connection);
                parameters.MaxMessageSize = 2048;
                parameters.MaxReceiveBytes = 8192;
                DataChannelPubSubConfiguration.Configure(connection, DataChannelCarrierKind.SharedUasc, parameters);
            }
            return configuration;
        }

        private static PubSubConfigurationDataType CreatePublisherConfiguration(
            DataChannelCarrierKind carrier,
            string protocol,
            MessageSecurityMode security)
        {
            bool json = protocol == DataChannelProfiles.JsonPayload;
            return PubSubConfigurationBuilder.Create()
                .AddPublishedDataSet("TelemetryA", AddFields)
                .AddPublishedDataSet("TelemetryB", AddFields)
                .AddConnection("Publisher", connection => connection
                    .WithPublisherId(new Variant(PublisherName))
                    .WithDataChannel(carrier, DataChannelPubSubConfigurationTests.CreateParameters(protocol))
                    .AddWriterGroup("Group42", group =>
                    {
                        group.WithWriterGroupId(42)
                            .WithPublishingInterval(3600000)
                            .WithMessageSettings(json
                                ? new JsonWriterGroupMessageDataType
                                {
                                    NetworkMessageContentMask = (uint)(
                                        JsonNetworkMessageContentMask.NetworkMessageHeader |
                                        JsonNetworkMessageContentMask.DataSetMessageHeader |
                                        JsonNetworkMessageContentMask.PublisherId |
                                        JsonNetworkMessageContentMask.DataSetClassId)
                                }
                                : new UadpWriterGroupMessageDataType
                                {
                                    GroupVersion = 5,
                                    NetworkMessageContentMask = (uint)(
                                        UadpNetworkMessageContentMask.PublisherId |
                                        UadpNetworkMessageContentMask.GroupHeader |
                                        UadpNetworkMessageContentMask.WriterGroupId |
                                        UadpNetworkMessageContentMask.GroupVersion |
                                        UadpNetworkMessageContentMask.SequenceNumber |
                                        UadpNetworkMessageContentMask.NetworkMessageNumber |
                                        UadpNetworkMessageContentMask.Timestamp |
                                        UadpNetworkMessageContentMask.PayloadHeader)
                                });
                        if (security != MessageSecurityMode.None)
                        {
                            group.WithSecurity(security, DataChannelPubSubTestData.SecurityGroup,
                                "opc.tcp://localhost:4840/unused-key-provider");
                        }
                        group.AddDataSetWriter("WriterA", writer => ConfigureWriter(writer, "TelemetryA", 1001, json))
                            .AddDataSetWriter("WriterB", writer => ConfigureWriter(writer, "TelemetryB", 1002, json));
                    }))
                .Build();
        }

        private static PubSubConfigurationDataType CreateSubscriberConfiguration(
            DataChannelCarrierKind carrier,
            string protocol,
            MessageSecurityMode security)
        {
            return PubSubConfigurationBuilder.Create()
                .AddConnection("Subscriber", connection => connection
                    .WithDataChannel(carrier, DataChannelPubSubConfigurationTests.CreateParameters(protocol))
                    .AddReaderGroup("Readers", group =>
                    {
                        if (security != MessageSecurityMode.None)
                        {
                            group.WithSecurity(security, DataChannelPubSubTestData.SecurityGroup,
                                "opc.tcp://localhost:4840/unused-key-provider");
                        }
                        group.AddDataSetReader("ReaderA", reader => ConfigureReader(reader, "SeedA", 1001))
                            .AddDataSetReader("ReaderB", reader => ConfigureReader(reader, "SeedB", 1002))
                            .AddDataSetReader("Unmatched", reader => ConfigureReader(reader, "Unmatched", 1999));
                    }))
                .Build();
        }

        private static PubSubConfigurationDataType CreateCrossResourcePublisherConfiguration(
            DataChannelCarrierKind carrier)
        {
            return PubSubConfigurationBuilder.Create()
                .AddPublishedDataSet("TelemetryA", AddFields)
                .AddPublishedDataSet("TelemetryB", AddFields)
                .AddConnection("PublisherFirst", connection => connection
                    .WithPublisherId(new Variant(PublisherName))
                    .WithDataChannel(carrier, DataChannelPubSubConfigurationTests.CreateParameters(
                        resource: "urn:test:pubsub:first"))
                    .AddWriterGroup("Group42", group => group
                        .WithWriterGroupId(42)
                        .WithPublishingInterval(3600000)
                        .WithMessageSettings(new UadpWriterGroupMessageDataType
                        {
                            GroupVersion = 5,
                            NetworkMessageContentMask = (uint)(
                                UadpNetworkMessageContentMask.PublisherId |
                                UadpNetworkMessageContentMask.GroupHeader |
                                UadpNetworkMessageContentMask.WriterGroupId |
                                UadpNetworkMessageContentMask.GroupVersion |
                                UadpNetworkMessageContentMask.SequenceNumber |
                                UadpNetworkMessageContentMask.NetworkMessageNumber |
                                UadpNetworkMessageContentMask.Timestamp |
                                UadpNetworkMessageContentMask.PayloadHeader)
                        })
                        .AddDataSetWriter("WriterA", writer => ConfigureWriter(writer, "TelemetryA", 1001, false))
                        .AddDataSetWriter("WriterB", writer => ConfigureWriter(writer, "TelemetryB", 1002, false))))
                .AddConnection("PublisherSecond", connection => connection
                    .WithPublisherId(new Variant(PublisherName))
                    .WithDataChannel(carrier, DataChannelPubSubConfigurationTests.CreateParameters(
                        resource: "urn:test:pubsub:second"))
                    .AddWriterGroup("Group43", group => group
                        .WithWriterGroupId(43)
                        .WithPublishingInterval(3600000)
                        .WithMessageSettings(new UadpWriterGroupMessageDataType
                        {
                            GroupVersion = 5,
                            NetworkMessageContentMask = (uint)(
                                UadpNetworkMessageContentMask.PublisherId |
                                UadpNetworkMessageContentMask.GroupHeader |
                                UadpNetworkMessageContentMask.WriterGroupId |
                                UadpNetworkMessageContentMask.GroupVersion |
                                UadpNetworkMessageContentMask.SequenceNumber |
                                UadpNetworkMessageContentMask.NetworkMessageNumber |
                                UadpNetworkMessageContentMask.Timestamp |
                                UadpNetworkMessageContentMask.PayloadHeader)
                        })
                        .AddDataSetWriter("WriterC", writer => ConfigureWriter(writer, "TelemetryA", 1003, false))))
                .Build();
        }

        private static PubSubConfigurationDataType CreateCrossResourceSubscriberConfiguration(
            DataChannelCarrierKind carrier)
        {
            return PubSubConfigurationBuilder.Create()
                .AddConnection("SubscriberFirst", connection => connection
                    .WithDataChannel(carrier, DataChannelPubSubConfigurationTests.CreateParameters(
                        resource: "urn:test:pubsub:first"))
                    .AddReaderGroup("ReadersFirst", group => group
                        .AddDataSetReader("ReaderA", reader => ConfigureReader(reader, "SeedA", 1001))))
                .AddConnection("SubscriberSecond", connection => connection
                    .WithDataChannel(carrier, DataChannelPubSubConfigurationTests.CreateParameters(
                        resource: "urn:test:pubsub:second"))
                    .AddReaderGroup("ReadersSecond", group => group
                        .AddDataSetReader("ReaderB", reader => ConfigureReader(reader, "SeedB", 1002))
                        .AddDataSetReader("ReaderC", reader => ConfigureReader(reader, "SeedC", 1003))))
                .Build();
        }

        private static PubSubConfigurationDataType CreateDuplexConfiguration(
            DataChannelCarrierKind carrier,
            uint publisherId,
            ushort writerGroupId,
            ushort writerId,
            uint readerPublisherId,
            ushort readerWriterGroupId,
            ushort readerWriterId,
            string dataSetName,
            string readerName)
        {
            string seedName = $"{readerName}Seed";
            DataChannelParametersDataType parameters = DataChannelPubSubConfigurationTests.CreateParameters(
                resource: "urn:test:pubsub:duplex");
            parameters.Direction = DataChannelDirection.Duplex;
            return PubSubConfigurationBuilder.Create()
                .AddPublishedDataSet(dataSetName, AddFields)
                .AddConnection($"Duplex{publisherId}", connection => connection
                    .WithPublisherId(new Variant(publisherId))
                    .WithDataChannel(carrier, parameters)
                    .AddWriterGroup($"WriterGroup{writerGroupId}", group => group
                        .WithWriterGroupId(writerGroupId)
                        .WithPublishingInterval(3600000)
                        .WithMessageSettings(new UadpWriterGroupMessageDataType
                        {
                            GroupVersion = 5,
                            NetworkMessageContentMask = (uint)(
                                UadpNetworkMessageContentMask.PublisherId |
                                UadpNetworkMessageContentMask.GroupHeader |
                                UadpNetworkMessageContentMask.WriterGroupId |
                                UadpNetworkMessageContentMask.GroupVersion |
                                UadpNetworkMessageContentMask.SequenceNumber |
                                UadpNetworkMessageContentMask.NetworkMessageNumber |
                                UadpNetworkMessageContentMask.Timestamp |
                                UadpNetworkMessageContentMask.PayloadHeader)
                        })
                        .AddDataSetWriter($"Writer{writerId}",
                            writer => ConfigureWriter(writer, dataSetName, writerId, false)))
                    .AddReaderGroup($"ReaderGroup{readerWriterGroupId}", group =>
                        group.AddDataSetReader(readerName, reader => ConfigureReader(
                            reader, seedName, readerWriterId, new Variant(readerPublisherId), readerWriterGroupId))))
                .Build();
        }

        private static void ConfigureWriter(DataSetWriterBuilder writer, string dataSet, ushort id, bool json)
        {
            writer.WithDataSetWriterId(id).WithDataSetName(dataSet).WithKeyFrameCount(1)
                .WithMessageSettings(json
                    ? new JsonDataSetWriterMessageDataType
                    {
                        DataSetMessageContentMask = (uint)(
                            JsonDataSetMessageContentMask.DataSetWriterId |
                            JsonDataSetMessageContentMask.SequenceNumber |
                            JsonDataSetMessageContentMask.Timestamp |
                            JsonDataSetMessageContentMask.Status |
                            JsonDataSetMessageContentMask.MessageType |
                            JsonDataSetMessageContentMask.MetaDataVersion)
                    }
                    : new UadpDataSetWriterMessageDataType
                    {
                        DataSetMessageContentMask = (uint)(
                            UadpDataSetMessageContentMask.SequenceNumber |
                            UadpDataSetMessageContentMask.Timestamp |
                            UadpDataSetMessageContentMask.Status |
                            UadpDataSetMessageContentMask.MajorVersion |
                            UadpDataSetMessageContentMask.MinorVersion)
                    });
        }

        private static void ConfigureReader(DataSetReaderBuilder reader, string seedName, ushort writerId)
        {
            ConfigureReader(reader, seedName, writerId, new Variant(PublisherName), 42);
        }

        private static void ConfigureReader(
            DataSetReaderBuilder reader,
            string seedName,
            ushort writerId,
            Variant publisherId,
            ushort writerGroupId)
        {
            reader.WithFilter(publisherId, writerGroupId, writerId)
                .WithMessageReceiveTimeout(60000)
                .WithMirrorSubscribedDataSet(seedName)
                .WithDataSetMetaData(seedName, metadata =>
                {
                    metadata.WithoutFieldIds();
                    AddFields(metadata);
                });
        }

        private static void AddFields(PublishedDataSetBuilder builder)
        {
            builder.AddField("Counter", (byte)BuiltInType.Int32, DataTypeIds.Int32)
                .AddField("Label", (byte)BuiltInType.String, DataTypeIds.String)
                .AddField("Enabled", (byte)BuiltInType.Boolean, DataTypeIds.Boolean)
                .AddField("Value", (byte)BuiltInType.Double, DataTypeIds.Double);
        }

        private static void AssertFields(ArrayOf<DataSetField> fields, string label)
        {
            Assert.Multiple(() =>
            {
                Assert.That(fields, Has.Count.EqualTo(4));
                Assert.That(fields[0].Value, Is.EqualTo(new Variant(1)));
                Assert.That(fields[1].Value, Is.EqualTo(new Variant(label + " \u03a9")));
                Assert.That(fields[2].Value, Is.EqualTo(new Variant(true)));
                Assert.That(fields[3].Value, Is.EqualTo(new Variant(3.25)));
            });
        }

        private static PubSubNetworkMessage CreateUnexpectedMetaData(string protocol)
        {
            DataSetMetaDataType metadata = new TestSource("Unexpected", "ignored").BuildMetaData();
            return protocol == DataChannelProfiles.JsonPayload
                ? new JsonMetaDataMessage
                {
                    PublisherId = PublisherId.FromString("unexpected"),
                    DataSetWriterId = 1001,
                    MessageId = "unexpected-metadata",
                    MetaDataPayload = metadata,
                    MetaData = metadata
                }
                : new UadpDiscoveryResponseMessage
                {
                    PublisherId = PublisherId.FromString("unexpected"),
                    DataSetWriterId = 1001,
                    DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                    DataSetMetaData = metadata,
                    StatusCode = StatusCodes.Good
                };
        }

        private static bool IsUnexpectedPublisherMessage(ByteString networkMessage)
        {
                return UadpDecoder.TryReadPrefix(networkMessage.Memory, out UadpPrefixInfo prefix) &&
                    prefix.PublisherId == PublisherId.FromString("unexpected");
        }

        private static bool IsTwoWriterGroupMessage(ByteString networkMessage)
        {
            return UadpDecoder.TryReadPrefix(networkMessage.Memory, out UadpPrefixInfo prefix) &&
                prefix.WriterGroupId == 42 && prefix.PayloadCount == 2;
        }

        private sealed class TestSource(string name, string label) : IPublishedDataSetSource
        {
            public int Samples => Volatile.Read(ref m_samples);

            public DataSetMetaDataType BuildMetaData()
            {
                PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                    .AddPublishedDataSet(name, AddFields).Build();
                return configuration.PublishedDataSets[0].DataSetMetaData;
            }

            public ValueTask<PublishedDataSetSnapshot> SampleAsync(
                DataSetMetaDataType metaData,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int sample = Interlocked.Increment(ref m_samples);
                ArrayOf<DataSetField> fields =
                [
                    new DataSetField { Name = "Counter", Value = new Variant(sample) },
                    new DataSetField { Name = "Label", Value = new Variant(label + " \u03a9") },
                    new DataSetField { Name = "Enabled", Value = new Variant(true) },
                    new DataSetField { Name = "Value", Value = new Variant(3.25) }
                ];
                return new ValueTask<PublishedDataSetSnapshot>(new PublishedDataSetSnapshot(
                    metaData.ConfigurationVersion, fields, DataChannelPubSubTestData.Timestamp));
            }

            private int m_samples;
        }

        private sealed class CollectingSink : ISubscribedDataSetSink
        {
            public Task<ArrayOf<DataSetField>> First => m_first.Task;

            public int Count => Volatile.Read(ref m_count);

            public ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_count);
                m_first.TrySetResult(new ArrayOf<DataSetField>(fields.ToArray()));
                return default;
            }

            private readonly TaskCompletionSource<ArrayOf<DataSetField>> m_first =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_count;
        }

        private sealed class BlockingMessageObserver(int pauseAt) : IReceivedNetworkMessageSink
        {
            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async ValueTask OnReceivedAsync(
                ReceivedNetworkMessage received,
                CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref m_count) == pauseAt)
                {
                    Entered.TrySetResult(true);
                    await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            private int m_count;
        }

        private sealed class RecordingFactory(IPubSubTransportFactory inner) : IPubSubTransportFactory
        {
            public string TransportProfileUri => inner.TransportProfileUri;

            public RecordingTransport? Transport { get; private set; }

            public ConcurrentDictionary<string, RecordingTransport> Transports { get; } = new();

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                Transport = new RecordingTransport(inner.Create(connection, telemetry, timeProvider));
                Transports[connection.Name ??
                    throw new InvalidOperationException("A recorded connection must have a name.")] = Transport;
                return Transport;
            }
        }

        private sealed class RecordingTransport : IPubSubTransport, IPubSubEncodingTransport
        {
            public RecordingTransport(IPubSubTransport inner)
            {
                m_inner = inner;
                m_inner.StateChanged += OnStateChanged;
            }

            public ConcurrentQueue<ByteString> Sent { get; } = new();

            public ConcurrentQueue<ByteString> Received { get; } = new();

            public Task ReceiveCompleted => m_receiveCompleted.Task;

            public string TransportProfileUri => m_inner.TransportProfileUri;

            public PubSubTransportDirection Direction => m_inner.Direction;

            public bool IsConnected => m_inner.IsConnected;

            public string EncodingProfileUri => ((IPubSubEncodingTransport)m_inner).EncodingProfileUri;

            public uint MaxNetworkMessageSize => ((IPubSubEncodingTransport)m_inner).MaxNetworkMessageSize;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                return m_inner.OpenAsync(cancellationToken);
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                return m_inner.CloseAsync(cancellationToken);
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                Sent.Enqueue(ByteString.From(payload));
                return m_inner.SendAsync(payload, topic, cancellationToken);
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                try
                {
                    await foreach (PubSubTransportFrame frame
                        in m_inner.ReceiveAsync(cancellationToken).ConfigureAwait(false))
                    {
                        Received.Enqueue(ByteString.From(frame.Payload));
                        yield return frame;
                    }
                }
                finally
                {
                    m_receiveCompleted.TrySetResult(true);
                }
            }

            public async ValueTask DisposeAsync()
            {
                m_inner.StateChanged -= OnStateChanged;
                await m_inner.DisposeAsync().ConfigureAwait(false);
            }

            private void OnStateChanged(object? sender, PubSubTransportStateChangedEventArgs args)
            {
                StateChanged?.Invoke(this, args);
            }

            private readonly IPubSubTransport m_inner;

            private readonly TaskCompletionSource<bool> m_receiveCompleted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private const string PublisherName = "publisher-73001";
    }
}
