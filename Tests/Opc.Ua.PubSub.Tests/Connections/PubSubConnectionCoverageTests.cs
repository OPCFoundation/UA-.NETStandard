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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;
using PubSubEncodingPublisherId = Opc.Ua.PubSub.Encoding.PublisherId;
using PubSubJsonActionNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonActionNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Connections
{
    /// <summary>
    /// Deterministic gap-filling coverage for <see cref="PubSubConnection"/>
    /// receive, discovery, and Action paths driven end to end through a
    /// controllable in-memory transport (no network, no reflection). Every
    /// test exercises a real code path and asserts on the concrete values,
    /// StatusCodes, counts, or diagnostics counters the connection produces.
    /// </summary>
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class PubSubConnectionCoverageTests
    {
        private const string UadpProfile = Profiles.PubSubUdpUadpTransport;
        private const string JsonProfile = Profiles.PubSubMqttJsonTransport;

        private static readonly string[] s_nonMatchingProfiles = ["urn:does-not-match"];

        [Test]
        public async Task RequestDiscoveryAsyncWithNullRequestThrowsAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(new DatagramHarnessTransport(UadpProfile), UadpProfile),
                NoEncoders(), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await connection.RequestDiscoveryAsync(null!, TimeSpan.Zero).ConfigureAwait(false));
        }

        [Test]
        public async Task RequestDiscoveryAsyncWithNegativeTimeoutThrowsAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(new DatagramHarnessTransport(UadpProfile), UadpProfile),
                NoEncoders(), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await connection.RequestDiscoveryAsync(
                    new PubSubDiscoveryRequest { DiscoveryType = UadpDiscoveryType.ApplicationInformation },
                    TimeSpan.FromMilliseconds(-1)).ConfigureAwait(false));
        }

        [Test]
        public async Task RequestDiscoveryAsyncWhenNotEnabledThrowsAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(new DatagramHarnessTransport(UadpProfile), UadpProfile),
                NoEncoders(), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            InvalidOperationException? error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await connection.RequestDiscoveryAsync(
                    new PubSubDiscoveryRequest { DiscoveryType = UadpDiscoveryType.ApplicationInformation },
                    TimeSpan.Zero).ConfigureAwait(false));
            Assert.That(error!.Message, Does.Contain("enabled"));
        }

        [Test]
        public async Task InvokeActionAsyncWithNullRequestThrowsAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(new DatagramHarnessTransport(UadpProfile), UadpProfile),
                NoEncoders(), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await connection.InvokeActionAsync(null!, TimeSpan.Zero).ConfigureAwait(false));
        }

        [Test]
        public async Task InvokeActionAsyncWithNegativeTimeoutThrowsAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(new DatagramHarnessTransport(UadpProfile), UadpProfile),
                NoEncoders(), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await connection.InvokeActionAsync(
                    new PubSubActionRequest
                    {
                        Target = new PubSubActionTarget { DataSetWriterId = 1, ActionTargetId = 1 }
                    },
                    TimeSpan.FromSeconds(-1)).ConfigureAwait(false));
        }

        [Test]
        public async Task InvokeActionAsyncWhenNotEnabledThrowsAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(new DatagramHarnessTransport(UadpProfile), UadpProfile),
                NoEncoders(), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            InvalidOperationException? error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await connection.InvokeActionAsync(
                    new PubSubActionRequest
                    {
                        Target = new PubSubActionTarget { DataSetWriterId = 1, ActionTargetId = 1 }
                    },
                    TimeSpan.Zero).ConfigureAwait(false));
            Assert.That(error!.Message, Does.Contain("enabled"));
        }

        [Test]
        public async Task RegisterActionHandlerWithNullTargetThrowsAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(new DatagramHarnessTransport(UadpProfile), UadpProfile),
                NoEncoders(), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            Assert.Throws<ArgumentNullException>(() =>
                connection.RegisterActionHandler(null!, new DelegatePubSubActionHandler(GoodHandler)));
        }

        [Test]
        public async Task RegisterActionHandlerWithNullHandlerThrowsAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(new DatagramHarnessTransport(UadpProfile), UadpProfile),
                NoEncoders(), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            Assert.Throws<ArgumentNullException>(() =>
                connection.RegisterActionHandler(
                    new PubSubActionTarget { DataSetWriterId = 1, ActionTargetId = 1 }, null!));
        }

        [Test]
        public async Task EnableAsyncWhenTransportFactoryThrowsFaultsStateAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new ThrowingCreateTransportFactory(),
                NoEncoders(), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await connection.EnableAsync().ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(connection.State.State, Is.EqualTo(PubSubState.Error));
                Assert.That(connection.CurrentTransport, Is.Null);
            });
        }

        [Test]
        public async Task EnableAsyncWhenTransportOpenThrowsFaultsStateAsync()
        {
            var transport = new OpenThrowingHarnessTransport(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                NoEncoders(), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await connection.EnableAsync().ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(connection.State.State, Is.EqualTo(PubSubState.Error));
                Assert.That(connection.CurrentTransport, Is.Null);
                Assert.That(transport.DisposeCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task DisableAsyncWhenTransportCloseThrowsStillDisablesAsync()
        {
            var transport = new CloseThrowingHarnessTransport(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                NoEncoders(), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            await connection.EnableAsync().ConfigureAwait(false);
            Assert.That(connection.State.State, Is.EqualTo(PubSubState.Operational));

            await connection.DisableAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(connection.State.State, Is.EqualTo(PubSubState.Disabled));
                Assert.That(connection.CurrentTransport, Is.Null);
                Assert.That(transport.DisposeCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task EnableAsyncWithLastWillTransportConfiguresLastWillAsync()
        {
            var transport = new LastWillHarnessTransport(JsonProfile);
            var encoder = new CapturingEncoder(JsonProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(JsonProfile), new SingleTransportFactory(transport, JsonProfile),
                EncMap(JsonProfile, encoder), NoDecoders(), new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            await connection.EnableAsync().ConfigureAwait(false);
            await connection.DisableAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(transport.LastWillConfigured, Is.True);
                Assert.That(transport.LastWillRetain, Is.True);
                Assert.That(transport.LastWillTopic, Does.StartWith("disc/"));
                Assert.That(transport.LastWillPayloadLength, Is.GreaterThan(0));
            });
        }

        [Test]
        public async Task EnableAsyncWithAnnouncementTransportRunsPeriodicSchedulerAsync()
        {
            var transport = new AnnouncementHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var scheduler = new ImmediateScheduler();
            await using (PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), NoDecoders(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low), scheduler: scheduler))
            {
                await connection.EnableAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(scheduler.ScheduleCalled, Is.True);
                    Assert.That(scheduler.CallbackInvoked, Is.True);
                    Assert.That(transport.AnnouncementCount, Is.GreaterThanOrEqualTo(3));
                });

                await connection.DisableAsync().ConfigureAwait(false);
                Assert.That(scheduler.DisposeCalled, Is.True);
            }
        }

        [Test]
        public async Task ReceiveLoopRespondsToApplicationInformationRequestAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, new UadpDiscoveryRequestMessage
            {
                DiscoveryType = UadpDiscoveryType.ApplicationInformation
            });

            await AwaitBoundedAsync(transport.WaitUntilSentAsync(1), "application information response")
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(transport.SentPayloads, Has.Count.EqualTo(1));
                Assert.That(encoder.Messages, Has.Length.EqualTo(1));
                Assert.That(((UadpDiscoveryResponseMessage)encoder.Messages[0]).DiscoveryType,
                    Is.EqualTo(UadpDiscoveryType.ApplicationInformation));
            });
        }

        [Test]
        public async Task ReceiveLoopThrottlesDuplicateApplicationInformationResponseAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low), timeProvider: new FixedTimeProvider());

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, new UadpDiscoveryRequestMessage
            {
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                DataSetWriterIds = new ushort[] { 1 }
            });
            Deliver(transport, decoder, new UadpDiscoveryRequestMessage
            {
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                DataSetWriterIds = new ushort[] { 2 }
            });

            await AwaitBoundedAsync(transport.WaitUntilProcessedAsync(2), "two discovery requests")
                .ConfigureAwait(false);
            Assert.That(transport.SentPayloads, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ReceiveLoopDiscardsDuplicateDiscoveryRequestAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low), timeProvider: new FixedTimeProvider());

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, new UadpDiscoveryRequestMessage
            {
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                DataSetWriterIds = new ushort[] { 7 }
            });
            Deliver(transport, decoder, new UadpDiscoveryRequestMessage
            {
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                DataSetWriterIds = new ushort[] { 7 }
            });

            await AwaitBoundedAsync(transport.WaitUntilProcessedAsync(2), "two identical discovery requests")
                .ConfigureAwait(false);
            Assert.That(transport.SentPayloads, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ReceiveLoopRespondsToGenericProbeWithoutFilterAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, new UadpDiscoveryRequestMessage
            {
                DiscoveryType = UadpDiscoveryType.Probe
            });

            await AwaitBoundedAsync(transport.WaitUntilSentAsync(3), "generic probe responses")
                .ConfigureAwait(false);
            UadpDiscoveryType[] types = [.. encoder.Messages
                .Cast<UadpDiscoveryResponseMessage>()
                .Select(m => m.DiscoveryType)];
            Assert.Multiple(() =>
            {
                Assert.That(transport.SentPayloads, Has.Count.EqualTo(3));
                Assert.That(types, Is.EquivalentTo(
                [
                    UadpDiscoveryType.ApplicationInformation,
                    UadpDiscoveryType.PublisherEndpoints,
                    UadpDiscoveryType.PubSubConnection
                ]));
            });
        }

        [Test]
        public async Task ReceiveLoopRespondsToProbeWithWriterGroupFilterAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, new UadpDiscoveryRequestMessage
            {
                DiscoveryType = UadpDiscoveryType.Probe,
                ProbeFilter = new UadpDiscoveryProbeFilter { WriterGroupId = 42, IncludeDataSetWriters = true }
            });

            await AwaitBoundedAsync(transport.WaitUntilProcessedAsync(1), "writer-group probe request")
                .ConfigureAwait(false);
            Assert.That(transport.SentPayloads, Is.Empty);
        }

        [Test]
        public async Task ReceiveLoopRespondsToPubSubConnectionRequestWithMatchingFilterAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            PubSubConnectionDataType config = Config(UadpProfile);
            config.WriterGroups = new[]
            {
                new WriterGroupDataType
                {
                    Name = "wg1",
                    WriterGroupId = 9,
                    DataSetWriters = new[] { new DataSetWriterDataType { Name = "w1", DataSetWriterId = 1 } }
                }
            };
            await using PubSubConnection connection = CreateConnection(
                config, new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, new UadpDiscoveryRequestMessage
            {
                DiscoveryType = UadpDiscoveryType.PubSubConnection,
                ProbeFilter = new UadpDiscoveryProbeFilter
                {
                    TransportProfileUris = new[] { UadpProfile },
                    IncludeWriterGroups = true,
                    IncludeDataSetWriters = false
                }
            });

            await AwaitBoundedAsync(transport.WaitUntilSentAsync(1), "pubsub connection response")
                .ConfigureAwait(false);
            var response = (UadpDiscoveryResponseMessage)encoder.Messages[0];
            Assert.Multiple(() =>
            {
                Assert.That(transport.SentPayloads, Has.Count.EqualTo(1));
                Assert.That(response.DiscoveryType, Is.EqualTo(UadpDiscoveryType.PubSubConnection));
                Assert.That(response.Connection!.WriterGroups, Has.Count.EqualTo(1));
                Assert.That(response.Connection.WriterGroups[0].DataSetWriters, Is.Empty);
            });
        }

        [Test]
        public async Task ReceiveLoopSkipsPubSubConnectionResponseWhenFilterDoesNotMatchAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, new UadpDiscoveryRequestMessage
            {
                DiscoveryType = UadpDiscoveryType.PubSubConnection,
                ProbeFilter = new UadpDiscoveryProbeFilter
                {
                    TransportProfileUris = s_nonMatchingProfiles
                }
            });

            await AwaitBoundedAsync(transport.WaitUntilProcessedAsync(1), "non-matching connection request")
                .ConfigureAwait(false);
            Assert.That(transport.SentPayloads, Is.Empty);
        }

        [Test]
        public async Task ReceiveLoopUadpActionRequestInvokesHandlerAndSendsResponseAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            var handlerSignal = new TaskCompletionSource<PubSubActionInvocation>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.RegisterActionHandler(
                new PubSubActionTarget { DataSetWriterId = 5, ActionTargetId = 3 },
                new DelegatePubSubActionHandler((invocation, _) =>
                {
                    handlerSignal.TrySetResult(invocation);
                    return new ValueTask<PubSubActionHandlerResult>(
                        new PubSubActionHandlerResult { StatusCode = StatusCodes.Good });
                }),
                allowUnsecured: true);

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, new UadpActionRequestMessage
            {
                DataSetWriterId = 5,
                ActionTargetId = 3,
                RequestId = 11,
                ResponseAddress = string.Empty
            });

            await AwaitBoundedAsync(transport.WaitUntilSentAsync(1), "uadp action response")
                .ConfigureAwait(false);
            PubSubActionInvocation invocation = await handlerSignal.Task.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(invocation.Target.DataSetWriterId, Is.EqualTo((ushort)5));
                Assert.That(invocation.Target.ActionTargetId, Is.EqualTo((ushort)3));
                Assert.That(invocation.RequestId, Is.EqualTo((ushort)11));
                Assert.That(transport.SentPayloads, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task ReceiveLoopUadpActionRequestWithoutResponderDropsAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            var handlerSignal = new TaskCompletionSource<PubSubActionInvocation>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.RegisterActionHandler(
                new PubSubActionTarget { DataSetWriterId = 7, ActionTargetId = 7 },
                new DelegatePubSubActionHandler((invocation, _) =>
                {
                    handlerSignal.TrySetResult(invocation);
                    return new ValueTask<PubSubActionHandlerResult>(
                        new PubSubActionHandlerResult { StatusCode = StatusCodes.Good });
                }),
                allowUnsecured: true);

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, new UadpActionRequestMessage
            {
                DataSetWriterId = 5,
                ActionTargetId = 3,
                RequestId = 1,
                ResponseAddress = string.Empty
            });
            Deliver(transport, decoder, new UadpActionRequestMessage
            {
                DataSetWriterId = 7,
                ActionTargetId = 7,
                RequestId = 2,
                ResponseAddress = string.Empty
            });

            await AwaitBoundedAsync(transport.WaitUntilSentAsync(1), "second action response")
                .ConfigureAwait(false);
            PubSubActionInvocation invocation = await handlerSignal.Task.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(invocation.Target.DataSetWriterId, Is.EqualTo((ushort)7));
                Assert.That(transport.SentPayloads, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task InvokeActionAsyncRoundTripsUadpResponseAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            await connection.EnableAsync().ConfigureAwait(false);
            Task<PubSubActionResponse> invoke = connection.InvokeActionAsync(
                new PubSubActionRequest { Target = new PubSubActionTarget { DataSetWriterId = 5, ActionTargetId = 3 } },
                TimeSpan.FromSeconds(10)).AsTask();

            await AwaitBoundedAsync(encoder.WaitUntilCountAsync(1), "outbound action request").ConfigureAwait(false);
            var sentRequest = (UadpActionRequestMessage)encoder.Messages[0];
            Deliver(transport, decoder, new UadpActionResponseMessage
            {
                PublisherId = sentRequest.PublisherId,
                DataSetWriterId = sentRequest.DataSetWriterId,
                ActionTargetId = sentRequest.ActionTargetId,
                RequestId = sentRequest.RequestId,
                CorrelationData = sentRequest.CorrelationData,
                Status = StatusCodes.BadTimeout,
                ActionState = ActionState.Executing
            });

            PubSubActionResponse response =
                await AwaitBoundedResultAsync(invoke, "action response").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.RequestId, Is.EqualTo(sentRequest.RequestId));
                Assert.That(response.StatusCode.Code, Is.EqualTo(StatusCodes.BadTimeout));
                Assert.That(response.ActionState, Is.EqualTo(ActionState.Executing));
                Assert.That(response.Target.DataSetWriterId, Is.EqualTo((ushort)5));
                Assert.That(response.Target.ActionTargetId, Is.EqualTo((ushort)3));
            });
        }

        [Test]
        public async Task ReceiveLoopJsonActionRequestInvokesHandlerAndSendsResponseAsync()
        {
            var transport = new DatagramHarnessTransport(JsonProfile);
            var encoder = new CapturingEncoder(JsonProfile);
            var decoder = new QueueDecoder(JsonProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(JsonProfile), new SingleTransportFactory(transport, JsonProfile),
                EncMap(JsonProfile, encoder), DecMap(JsonProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            var handlerSignal = new TaskCompletionSource<PubSubActionInvocation>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.RegisterActionHandler(
                new PubSubActionTarget { DataSetWriterId = 5, ActionTargetId = 3 },
                new DelegatePubSubActionHandler((invocation, _) =>
                {
                    handlerSignal.TrySetResult(invocation);
                    return new ValueTask<PubSubActionHandlerResult>(
                        new PubSubActionHandlerResult { StatusCode = StatusCodes.Good });
                }),
                allowUnsecured: true);

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, NewJsonActionRequest(5, 3, requestId: 21));

            await AwaitBoundedAsync(transport.WaitUntilSentAsync(1), "json action response")
                .ConfigureAwait(false);
            PubSubActionInvocation invocation = await handlerSignal.Task.ConfigureAwait(false);
            var response = (PubSubJsonActionNetworkMessage)encoder.Messages[^1];
            Assert.That(response.Messages[0].TryGetValue(out IEncodeable? body), Is.True);
            var responseBody = (JsonActionResponseMessage)body!;
            Assert.Multiple(() =>
            {
                Assert.That(invocation.RequestId, Is.EqualTo((ushort)21));
                Assert.That(transport.SentPayloads, Has.Count.EqualTo(1));
                Assert.That(responseBody.Status.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(responseBody.ActionState, Is.EqualTo(ActionState.Done));
            });
        }

        [Test]
        public async Task ReceiveLoopJsonActionRequestWithoutAllowUnsecuredRecordsSecurityFailureAsync()
        {
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            var transport = new DatagramHarnessTransport(JsonProfile);
            var encoder = new CapturingEncoder(JsonProfile);
            var decoder = new QueueDecoder(JsonProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(JsonProfile), new SingleTransportFactory(transport, JsonProfile),
                EncMap(JsonProfile, encoder), DecMap(JsonProfile, decoder), diagnostics);

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, NewJsonActionRequest(5, 3, requestId: 1));

            await AwaitBoundedAsync(transport.WaitUntilProcessedAsync(1), "unsecured json action request")
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(transport.SentPayloads, Is.Empty);
                Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.SecurityTokenErrors), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ReceiveLoopJsonActionRequestWithoutResponderDropsAsync()
        {
            var transport = new DatagramHarnessTransport(JsonProfile);
            var encoder = new CapturingEncoder(JsonProfile);
            var decoder = new QueueDecoder(JsonProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(JsonProfile), new SingleTransportFactory(transport, JsonProfile),
                EncMap(JsonProfile, encoder), DecMap(JsonProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            var handlerSignal = new TaskCompletionSource<PubSubActionInvocation>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.RegisterActionHandler(
                new PubSubActionTarget { DataSetWriterId = 8, ActionTargetId = 8 },
                new DelegatePubSubActionHandler((invocation, _) =>
                {
                    handlerSignal.TrySetResult(invocation);
                    return new ValueTask<PubSubActionHandlerResult>(
                        new PubSubActionHandlerResult { StatusCode = StatusCodes.Good });
                }),
                allowUnsecured: true);

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, NewJsonActionRequest(5, 3, requestId: 1));
            Deliver(transport, decoder, NewJsonActionRequest(8, 8, requestId: 2));

            await AwaitBoundedAsync(transport.WaitUntilSentAsync(1), "second json action response")
                .ConfigureAwait(false);
            PubSubActionInvocation invocation = await handlerSignal.Task.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(invocation.Target.DataSetWriterId, Is.EqualTo((ushort)8));
                Assert.That(transport.SentPayloads, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task ReceiveLoopJsonActionRequestOutOfPolicyAddressRecordsSecurityFailureAsync()
        {
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            var transport = new TopicHarnessTransport(JsonProfile);
            var encoder = new CapturingEncoder(JsonProfile);
            var decoder = new QueueDecoder(JsonProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(JsonProfile), new SingleTransportFactory(transport, JsonProfile),
                EncMap(JsonProfile, encoder), DecMap(JsonProfile, decoder), diagnostics);

            bool handlerInvoked = false;
            connection.RegisterActionHandler(
                new PubSubActionTarget { DataSetWriterId = 5, ActionTargetId = 3 },
                new DelegatePubSubActionHandler((_, _) =>
                {
                    handlerInvoked = true;
                    return new ValueTask<PubSubActionHandlerResult>(
                        new PubSubActionHandlerResult { StatusCode = StatusCodes.Good });
                }),
                allowUnsecured: true);

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder,
                NewJsonActionRequest(5, 3, requestId: 1, responseAddress: "attacker/evil/topic"));

            await AwaitBoundedAsync(transport.WaitUntilProcessedAsync(1), "out-of-policy json action request")
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(handlerInvoked, Is.False);
                Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.SecurityTokenErrors), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ReceiveLoopJsonActionHandlerThrowsReturnsBadStatusAsync()
        {
            var transport = new DatagramHarnessTransport(JsonProfile);
            var encoder = new CapturingEncoder(JsonProfile);
            var decoder = new QueueDecoder(JsonProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(JsonProfile), new SingleTransportFactory(transport, JsonProfile),
                EncMap(JsonProfile, encoder), DecMap(JsonProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            connection.RegisterActionHandler(
                new PubSubActionTarget { DataSetWriterId = 5, ActionTargetId = 3 },
                new DelegatePubSubActionHandler((_, _) =>
                    throw new InvalidOperationException("handler failure")),
                allowUnsecured: true);

            await connection.EnableAsync().ConfigureAwait(false);
            Deliver(transport, decoder, NewJsonActionRequest(5, 3, requestId: 33));

            await AwaitBoundedAsync(transport.WaitUntilSentAsync(1), "failed handler response")
                .ConfigureAwait(false);
            var response = (PubSubJsonActionNetworkMessage)encoder.Messages[^1];
            Assert.That(response.Messages[0].TryGetValue(out IEncodeable? body), Is.True);
            var responseBody = (JsonActionResponseMessage)body!;
            Assert.Multiple(() =>
            {
                Assert.That(transport.SentPayloads, Has.Count.EqualTo(1));
                Assert.That(responseBody.RequestId, Is.EqualTo((ushort)33));
                Assert.That(responseBody.Status.Code, Is.EqualTo(StatusCodes.BadUnexpectedError));
            });
        }

        [Test]
        public async Task InvokeActionAsyncRoundTripsJsonResponseAsync()
        {
            var transport = new DatagramHarnessTransport(JsonProfile);
            var encoder = new CapturingEncoder(JsonProfile);
            var decoder = new QueueDecoder(JsonProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(JsonProfile), new SingleTransportFactory(transport, JsonProfile),
                EncMap(JsonProfile, encoder), DecMap(JsonProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            await connection.EnableAsync().ConfigureAwait(false);
            Task<PubSubActionResponse> invoke = connection.InvokeActionAsync(
                new PubSubActionRequest { Target = new PubSubActionTarget { DataSetWriterId = 6, ActionTargetId = 4 } },
                TimeSpan.FromSeconds(10)).AsTask();

            await AwaitBoundedAsync(encoder.WaitUntilCountAsync(1), "outbound json action request")
                .ConfigureAwait(false);
            var sentRequest = (PubSubJsonActionNetworkMessage)encoder.Messages[0];
            Assert.That(sentRequest.Messages[0].TryGetValue(out IEncodeable? requestBody), Is.True);
            ushort requestId = ((JsonActionRequestMessage)requestBody!).RequestId;

            var responseMessage = new PubSubJsonActionNetworkMessage
            {
                MessageId = Guid.NewGuid().ToString("N"),
                PublisherId = sentRequest.PublisherId,
                CorrelationData = sentRequest.CorrelationData,
                Messages =
                [
                    new ExtensionObject(new JsonActionResponseMessage
                    {
                        DataSetWriterId = 6,
                        ActionTargetId = 4,
                        MessageType = "ua-action-response",
                        RequestId = requestId,
                        ActionState = ActionState.Done,
                        Status = StatusCodes.GoodClamped
                    })
                ]
            };
            Deliver(transport, decoder, responseMessage);

            PubSubActionResponse response = await AwaitBoundedResultAsync(invoke, "json action response")
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.RequestId, Is.EqualTo(requestId));
                Assert.That(response.StatusCode.Code, Is.EqualTo(StatusCodes.GoodClamped));
                Assert.That(response.ActionState, Is.EqualTo(ActionState.Done));
                Assert.That(response.Target.DataSetWriterId, Is.EqualTo((ushort)6));
            });
        }

        [Test]
        public async Task RequestDiscoveryAsyncCollectsMetaDataResponseAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            await connection.EnableAsync().ConfigureAwait(false);
            Task<PubSubDiscoveryResult> discovery = connection.RequestDiscoveryAsync(
                new PubSubDiscoveryRequest { DiscoveryType = UadpDiscoveryType.DataSetMetaData },
                TimeSpan.FromMilliseconds(300)).AsTask();

            await AwaitBoundedAsync(transport.WaitUntilSentAsync(1), "discovery request send").ConfigureAwait(false);
            Deliver(transport, decoder, new UadpDiscoveryResponseMessage
            {
                PublisherId = PubSubEncodingPublisherId.FromUInt16(7),
                DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                DataSetWriterId = 99,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "MetaOne",
                    ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 2, MinorVersion = 1 }
                }
            });

            PubSubDiscoveryResult result = await AwaitBoundedResultAsync(discovery, "discovery result")
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.DataSetMetaDataEntries, Has.Count.EqualTo(1));
                Assert.That(result.DataSetMetaDataEntries[0].DataSetWriterId, Is.EqualTo((ushort)99));
                Assert.That(result.DataSetMetaDataEntries[0].DataSetMetaData!.Name, Is.EqualTo("MetaOne"));
            });
        }

        [Test]
        public async Task RequestDiscoveryAsyncProbeSendsWithBackoffAsync()
        {
            var transport = new DatagramHarnessTransport(UadpProfile);
            var encoder = new CapturingEncoder(UadpProfile);
            var decoder = new QueueDecoder(UadpProfile);
            await using PubSubConnection connection = CreateConnection(
                Config(UadpProfile), new SingleTransportFactory(transport, UadpProfile),
                EncMap(UadpProfile, encoder), DecMap(UadpProfile, decoder),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            await connection.EnableAsync().ConfigureAwait(false);
            PubSubDiscoveryResult result = await connection.RequestDiscoveryAsync(
                new PubSubDiscoveryRequest { DiscoveryType = UadpDiscoveryType.Probe },
                TimeSpan.FromMilliseconds(700)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(transport.SentPayloads, Is.Not.Empty);
                Assert.That(result.DataSetMetaDataEntries, Is.Empty);
            });
        }

        private static PubSubActionHandlerResult GoodHandlerResult()
        {
            return new PubSubActionHandlerResult { StatusCode = StatusCodes.Good };
        }

        private static ValueTask<PubSubActionHandlerResult> GoodHandler(
            PubSubActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            return new ValueTask<PubSubActionHandlerResult>(GoodHandlerResult());
        }

        private static PubSubJsonActionNetworkMessage NewJsonActionRequest(
            ushort dataSetWriterId,
            ushort actionTargetId,
            ushort requestId,
            string responseAddress = "")
        {
            return new PubSubJsonActionNetworkMessage
            {
                MessageId = Guid.NewGuid().ToString("N"),
                PublisherId = PubSubEncodingPublisherId.FromUInt16(1),
                CorrelationData = new ByteString(new byte[] { 9, 8, 7 }),
                ResponseAddress = responseAddress,
                Messages =
                [
                    new ExtensionObject(new JsonActionRequestMessage
                    {
                        DataSetWriterId = dataSetWriterId,
                        ActionTargetId = actionTargetId,
                        MessageType = "ua-action-request",
                        RequestId = requestId,
                        ActionState = ActionState.Executing
                    })
                ]
            };
        }

        private static PubSubConnectionDataType Config(string profile)
        {
            return new PubSubConnectionDataType { Name = "cov-conn", TransportProfileUri = profile };
        }

        private static Dictionary<string, INetworkMessageEncoder> EncMap(string profile, INetworkMessageEncoder encoder)
        {
            return new Dictionary<string, INetworkMessageEncoder> { [profile] = encoder };
        }

        private static Dictionary<string, INetworkMessageDecoder> DecMap(string profile, INetworkMessageDecoder decoder)
        {
            return new Dictionary<string, INetworkMessageDecoder> { [profile] = decoder };
        }

        private static Dictionary<string, INetworkMessageEncoder> NoEncoders()
        {
            return [];
        }

        private static Dictionary<string, INetworkMessageDecoder> NoDecoders()
        {
            return [];
        }

        private static PubSubConnection CreateConnection(
            PubSubConnectionDataType configuration,
            IPubSubTransportFactory transportFactory,
            IReadOnlyDictionary<string, INetworkMessageEncoder> encoders,
            IReadOnlyDictionary<string, INetworkMessageDecoder> decoders,
            PubSubDiagnostics diagnostics,
            IPubSubScheduler? scheduler = null,
            TimeProvider? timeProvider = null)
        {
            return new PubSubConnection(
                configuration,
                transportFactory,
                encoders,
                decoders,
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                new DataSetMetaDataRegistry(),
                diagnostics,
                NUnitTelemetryContext.Create(),
                timeProvider ?? TimeProvider.System,
                securityWrapper: null,
                UadpSecurityWrapOptions.SignAndEncrypt,
                maxNetworkMessageSize: 0,
                MessageSecurityMode.None,
                scheduler);
        }

        private static void Deliver(
            HarnessTransport transport,
            QueueDecoder decoder,
            PubSubNetworkMessage message)
        {
            decoder.Enqueue(message);
            transport.PushFrame([1]);
        }

        private static async Task AwaitBoundedAsync(Task task, string what)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(task), $"Timed out waiting for {what}.");
            await task.ConfigureAwait(false);
        }

        private static async Task<T> AwaitBoundedResultAsync<T>(Task<T> task, string what)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(task), $"Timed out waiting for {what}.");
            return await task.ConfigureAwait(false);
        }

        private sealed class CountLatch
        {
            private readonly object m_sync = new();
            private readonly List<KeyValuePair<int, TaskCompletionSource<bool>>> m_waiters = [];
            private int m_count;

            public int Count
            {
                get
                {
                    lock (m_sync)
                    {
                        return m_count;
                    }
                }
            }

            public void Increment()
            {
                lock (m_sync)
                {
                    m_count++;
                    for (int i = m_waiters.Count - 1; i >= 0; i--)
                    {
                        if (m_count >= m_waiters[i].Key)
                        {
                            m_waiters[i].Value.TrySetResult(true);
                            m_waiters.RemoveAt(i);
                        }
                    }
                }
            }

            public Task WaitForAsync(int threshold)
            {
                lock (m_sync)
                {
                    if (m_count >= threshold)
                    {
                        return Task.CompletedTask;
                    }
                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_waiters.Add(new KeyValuePair<int, TaskCompletionSource<bool>>(threshold, tcs));
                    return tcs.Task;
                }
            }
        }

        private sealed class SingleTransportFactory : IPubSubTransportFactory
        {
            private readonly IPubSubTransport m_transport;

            public SingleTransportFactory(IPubSubTransport transport, string profile)
            {
                m_transport = transport;
                TransportProfileUri = profile;
            }

            public string TransportProfileUri { get; }

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                return m_transport;
            }
        }

        private sealed class ThrowingCreateTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                throw new InvalidOperationException("transport creation failed");
            }
        }

        private abstract class HarnessTransport : IPubSubTransport
        {
            private readonly Channel<byte[]> m_inbound =
                Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

            private readonly object m_sync = new();
            private readonly List<byte[]> m_sentPayloads = [];
            private readonly List<string> m_sentTopics = [];
            private readonly CountLatch m_sentLatch = new();
            private readonly CountLatch m_processedLatch = new();
            private int m_connected;
            private int m_disposeCount;

            protected HarnessTransport(string profile)
            {
                TransportProfileUri = profile;
            }

            public string TransportProfileUri { get; }

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected => Volatile.Read(ref m_connected) == 1;

            public int DisposeCount => Volatile.Read(ref m_disposeCount);

            public IReadOnlyList<byte[]> SentPayloads
            {
                get
                {
                    lock (m_sync)
                    {
                        return m_sentPayloads.ToArray();
                    }
                }
            }

            public IReadOnlyList<string> SentTopics
            {
                get
                {
                    lock (m_sync)
                    {
                        return m_sentTopics.ToArray();
                    }
                }
            }

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public void PushFrame(byte[] frame)
            {
                m_inbound.Writer.TryWrite(frame);
            }

            public Task WaitUntilSentAsync(int count)
            {
                return m_sentLatch.WaitForAsync(count);
            }

            public Task WaitUntilProcessedAsync(int count)
            {
                return m_processedLatch.WaitForAsync(count);
            }

            public virtual ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                Volatile.Write(ref m_connected, 1);
                return default;
            }

            public virtual ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                Volatile.Write(ref m_connected, 0);
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                lock (m_sync)
                {
                    m_sentPayloads.Add(payload.ToArray());
                    m_sentTopics.Add(topic ?? string.Empty);
                }
                m_sentLatch.Increment();
                return default;
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                while (await m_inbound.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (m_inbound.Reader.TryRead(out byte[]? frame))
                    {
                        yield return new PubSubTransportFrame(frame, null, DateTimeUtc.From(DateTime.UtcNow));
                        m_processedLatch.Increment();
                    }
                }
            }

            public ValueTask DisposeAsync()
            {
                Volatile.Write(ref m_connected, 0);
                Interlocked.Increment(ref m_disposeCount);
                return default;
            }
        }

        private sealed class DatagramHarnessTransport : HarnessTransport
        {
            public DatagramHarnessTransport(string profile)
                : base(profile)
            {
            }
        }

        private sealed class TopicHarnessTransport : HarnessTransport, IPubSubTopicProvider
        {
            public TopicHarnessTransport(string profile)
                : base(profile)
            {
            }

            public string BuildMetaDataTopic(
                PubSubEncodingPublisherId publisherId,
                ushort writerGroupId,
                ushort dataSetWriterId)
            {
                return $"meta/{writerGroupId}/{dataSetWriterId}";
            }

            public string BuildDataTopic(
                PubSubEncodingPublisherId publisherId,
                WriterGroupDataType writerGroup,
                ushort? dataSetWriterId)
            {
                return "data";
            }

            public string BuildDiscoveryTopic(PubSubEncodingPublisherId publisherId, string messageTypeSegment)
            {
                return $"disc/{messageTypeSegment}";
            }
        }

        private sealed class LastWillHarnessTransport
            : HarnessTransport, IPubSubTopicProvider, IPubSubLastWillConfigurator
        {
            public LastWillHarnessTransport(string profile)
                : base(profile)
            {
            }

            public bool LastWillConfigured { get; private set; }

            public string LastWillTopic { get; private set; } = string.Empty;

            public int LastWillPayloadLength { get; private set; }

            public bool LastWillRetain { get; private set; }

            public string BuildMetaDataTopic(
                PubSubEncodingPublisherId publisherId,
                ushort writerGroupId,
                ushort dataSetWriterId)
            {
                return $"meta/{writerGroupId}/{dataSetWriterId}";
            }

            public string BuildDataTopic(
                PubSubEncodingPublisherId publisherId,
                WriterGroupDataType writerGroup,
                ushort? dataSetWriterId)
            {
                return "data";
            }

            public string BuildDiscoveryTopic(PubSubEncodingPublisherId publisherId, string messageTypeSegment)
            {
                return $"disc/{messageTypeSegment}";
            }

            public void ConfigureLastWill(string topic, ReadOnlyMemory<byte> payload, bool retain)
            {
                LastWillConfigured = true;
                LastWillTopic = topic;
                LastWillPayloadLength = payload.Length;
                LastWillRetain = retain;
            }
        }

        private sealed class AnnouncementHarnessTransport : HarnessTransport, IPubSubDiscoveryAnnouncementTransport
        {
            private int m_announcements;

            public AnnouncementHarnessTransport(string profile)
                : base(profile)
            {
            }

            public uint DiscoveryAnnounceRate => 1000;

            public int AnnouncementCount => Volatile.Read(ref m_announcements);

            public ValueTask SendDiscoveryAnnouncementAsync(
                ReadOnlyMemory<byte> payload,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_announcements);
                return default;
            }
        }

        private sealed class OpenThrowingHarnessTransport : HarnessTransport
        {
            public OpenThrowingHarnessTransport(string profile)
                : base(profile)
            {
            }

            public override ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("transport open failed");
            }
        }

        private sealed class CloseThrowingHarnessTransport : HarnessTransport
        {
            public CloseThrowingHarnessTransport(string profile)
                : base(profile)
            {
            }

            public override ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("transport close failed");
            }
        }

        private sealed class CapturingEncoder : INetworkMessageEncoder
        {
            private readonly object m_sync = new();
            private readonly List<PubSubNetworkMessage> m_messages = [];
            private readonly CountLatch m_latch = new();

            public CapturingEncoder(string profile)
            {
                TransportProfileUri = profile;
            }

            public string TransportProfileUri { get; }

            public int EstimatedHeaderOverhead => 0;

            public PubSubNetworkMessage[] Messages
            {
                get
                {
                    lock (m_sync)
                    {
                        return [.. m_messages];
                    }
                }
            }

            public Task WaitUntilCountAsync(int count)
            {
                return m_latch.WaitForAsync(count);
            }

            public ValueTask<ReadOnlyMemory<byte>> EncodeAsync(
                PubSubNetworkMessage networkMessage,
                PubSubNetworkMessageContext context,
                CancellationToken cancellationToken = default)
            {
                lock (m_sync)
                {
                    m_messages.Add(networkMessage);
                }
                m_latch.Increment();
                return new ValueTask<ReadOnlyMemory<byte>>(new ReadOnlyMemory<byte>([1]));
            }
        }

        private sealed class QueueDecoder : INetworkMessageDecoder
        {
            private readonly object m_sync = new();
            private readonly Queue<PubSubNetworkMessage> m_queue = new();

            public QueueDecoder(string profile)
            {
                TransportProfileUri = profile;
            }

            public string TransportProfileUri { get; }

            public void Enqueue(PubSubNetworkMessage message)
            {
                lock (m_sync)
                {
                    m_queue.Enqueue(message);
                }
            }

            public ValueTask<PubSubNetworkMessage?> TryDecodeAsync(
                ReadOnlyMemory<byte> frame,
                PubSubNetworkMessageContext context,
                CancellationToken cancellationToken = default)
            {
                lock (m_sync)
                {
                    PubSubNetworkMessage? message = m_queue.Count > 0 ? m_queue.Dequeue() : null;
                    return new ValueTask<PubSubNetworkMessage?>(message);
                }
            }
        }

        private sealed class ImmediateScheduler : IPubSubScheduler
        {
            private readonly object m_sync = new();

            public bool ScheduleCalled { get; private set; }

            public bool CallbackInvoked { get; private set; }

            public bool DisposeCalled { get; private set; }

            public async ValueTask<IAsyncDisposable> ScheduleAsync(
                PubSubSchedule schedule,
                Func<CancellationToken, ValueTask> action,
                CancellationToken cancellationToken = default)
            {
                lock (m_sync)
                {
                    ScheduleCalled = true;
                }
                await action(cancellationToken).ConfigureAwait(false);
                lock (m_sync)
                {
                    CallbackInvoked = true;
                }
                return new Registration(this);
            }

            private void MarkDisposed()
            {
                lock (m_sync)
                {
                    DisposeCalled = true;
                }
            }

            private sealed class Registration : IAsyncDisposable
            {
                private readonly ImmediateScheduler m_owner;

                public Registration(ImmediateScheduler owner)
                {
                    m_owner = owner;
                }

                public ValueTask DisposeAsync()
                {
                    m_owner.MarkDisposed();
                    return default;
                }
            }
        }

        private sealed class FixedTimeProvider : TimeProvider
        {
            private readonly long m_timestamp;
            private readonly DateTimeOffset m_now;

            public FixedTimeProvider()
            {
                m_timestamp = base.GetTimestamp();
                m_now = base.GetUtcNow();
            }

            public override long GetTimestamp()
            {
                return m_timestamp;
            }

            public override DateTimeOffset GetUtcNow()
            {
                return m_now;
            }
        }
    }
}
