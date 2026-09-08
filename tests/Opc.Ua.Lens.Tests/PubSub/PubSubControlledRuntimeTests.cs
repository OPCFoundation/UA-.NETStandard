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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Adapter.Subscriber;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.PubSub.Transports;
using UaLens.Plugins.PubSub;

namespace UaLens.Tests.PubSub;

[TestFixture]
public sealed class PubSubControlledRuntimeTests
{
    [Test]
    public async Task RealFactoryBuildsAnOfflineDocumentOwnedApplicationWithALocalSink()
    {
        var factory = new PubSubRuntimeFactory(DefaultTelemetry.Create(static _ => { }));
        var store = new PubSubObservationStore(2);
        PubSubRuntimeHandle runtime = await factory.CreateAsync(
            PubSubTestRuntime.Configuration, PubSubTestRuntime.ReceiveAuthorization,
            null, store, CancellationToken.None)
            .ConfigureAwait(false);
        await using (runtime.ConfigureAwait(false))
        {
            Assert.That(runtime.Application.State.State, Is.EqualTo(PubSubState.Disabled));
            Assert.That(runtime.Application.Connections, Has.Count.EqualTo(1));
            Assert.That(runtime.Application.Connections[0].ReaderGroups[0].DataSetReaders[0].Sink, Is.SameAs(store));
            Assert.That(runtime.Application.Connections[0].WriterGroups.Count, Is.Zero);
            Assert.That(runtime.Application.MetaDataRegistry.Keys.Count, Is.GreaterThan(0));
            Assert.That(runtime.Source, Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SyntheticUadpRuntimeAppliesPublishedDataSetsToTheLocalSink(bool useUdp)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var transport = new EncodedLoopbackTransport();
        await using (transport.ConfigureAwait(false))
        {
            var provider = new ConfiguredPubSubTransportProvider(
                "encoded-loopback",
                [PubSubProfile.UdpUadp],
                static _ => new PubSubPrerequisite("Transport", PubSubReadiness.Ready, "Encoded-byte loopback."),
                (_, _, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(new PubSubTransportLease(transport));
                });
            PubSubConfiguration configuration = new()
            {
                Profile = PubSubProfile.UdpUadp,
                Endpoint = useUdp ? CreateUdpLoopbackEndpoint() : "opc.udp://127.0.0.1:62661",
                NetworkInterface = "127.0.0.1",
                TransportProviderId = useUdp ? string.Empty : provider.Id,
                SecurityMode = MessageSecurityMode.None,
                LocalPublisherId = 2,
                PublisherFilter = 2,
                WriterGroupId = 100,
                DataSetWriterId = 1,
                ReceiveEnabled = true,
                Publication = PubSubPublication.Synthetic,
                PublishingIntervalMs = 250,
                DurationSeconds = 8,
                MaxPublishedMessages = 12
            };
            configuration = PubSubStateCodec.Parse(PubSubStateCodec.Format(configuration));
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            var factory = new PubSubRuntimeFactory(telemetry, transportProviders: [provider]);
            var workspace = new PubSubWorkspace(factory, telemetry);
            await using (workspace.ConfigureAwait(false))
            {
                await workspace.ConfigureAsync(configuration, deadline.Token).ConfigureAwait(false);
                await workspace.StartAsync(
                    new PubSubStartAuthorization(AllowUnsecured: true, AllowPublication: true), deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(workspace.Snapshot().Phase, Is.EqualTo(PubSubDocumentPhase.Running));

                PubSubWorkspaceSnapshot snapshot;
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token).ConfigureAwait(false);
                    snapshot = workspace.Snapshot();
                }
                while (snapshot.Phase is PubSubDocumentPhase.Starting or PubSubDocumentPhase.Running or
                    PubSubDocumentPhase.Stopping);
                await workspace.StopAsync().ConfigureAwait(false);
                snapshot = workspace.Snapshot();
                string diagnostics = PubSubStateCodec.Format(configuration) + Environment.NewLine +
                    DescribeRoundTrip(snapshot);

                Assert.That(snapshot.Observations.AcceptedDataSets, Is.EqualTo(12), diagnostics);
                Assert.That(snapshot.Observations.RejectedDataSets, Is.Zero, diagnostics);
                Assert.That(snapshot.SourceSamples, Is.EqualTo(12), diagnostics);
                Assert.That(snapshot.Phase, Is.EqualTo(PubSubDocumentPhase.Offline), diagnostics);
                if (!useUdp)
                {
                    Assert.That(transport.SentFrames, Is.GreaterThanOrEqualTo(12), diagnostics);
                }
                Assert.That(snapshot.Counters.ToList().Single(row => row.Counter == "SentDataSetMessages").Application,
                    Is.EqualTo(12), diagnostics);
                Assert.That(snapshot.Counters.ToList().Single(row => row.Counter == "ReceivedDataSetMessages")
                    .Application, Is.EqualTo(12), diagnostics);
                Assert.That(snapshot.Observations.Metadata, Has.Count.GreaterThan(0), diagnostics);
                Assert.That(snapshot.Observations.Values, Has.Count.EqualTo(3), diagnostics);

                PubSubFieldValue toggleField = snapshot.Observations.Values.ToList()
                    .Single(field => field.Name == "BoolToggle");
                PubSubFieldValue sequenceField = snapshot.Observations.Values.ToList()
                    .Single(field => field.Name == "Int32");
                PubSubFieldValue timeField = snapshot.Observations.Values.ToList()
                    .Single(field => field.Name == "DateTime");
                Assert.That(toggleField.Value.TryGetValue(out bool toggle), Is.True);
                Assert.That(toggle, Is.True);
                Assert.That(sequenceField.Value.TryGetValue(out int sequence), Is.True);
                Assert.That(sequence, Is.EqualTo(12));
                Assert.That(timeField.Value.TryGetValue(out DateTimeUtc timestamp), Is.True);
                Assert.That(timestamp, Is.Not.EqualTo(DateTimeUtc.MinValue));
                foreach (PubSubFieldValue field in snapshot.Observations.Values)
                {
                    Assert.That(field.Status, Is.EqualTo(StatusCodes.Good));
                }
            }
        }
    }

    [TestCase((int)PubSubProfile.MqttJson, "mqtts://broker.example:8883")]
    [TestCase((int)PubSubProfile.MqttUadp, "mqtts://broker.example:8883")]
    [TestCase((int)PubSubProfile.KafkaJson, "kafkas://broker.example:9093")]
    [TestCase((int)PubSubProfile.KafkaUadp, "kafkas://broker.example:9093")]
    public async Task InstalledBrokerFactoriesCanBeAcquiredWithoutOpeningAnyConnection(
        int profile,
        string endpoint)
    {
        var provider = new BuiltInPubSubTransportProvider();
        var store = new PubSubObservationStore(2);
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            Profile = (PubSubProfile)profile,
            Endpoint = endpoint,
            Topic = "sample",
            BrokerAuthentication = PubSubBrokerAuthentication.Anonymous
        };
        PubSubTransportLease lease = await provider.AcquireAsync(configuration,
            new PubSubProviderContext(
                DefaultTelemetry.Create(static _ => { }), TimeProvider.System, store.TransportDiagnostics),
            CancellationToken.None).ConfigureAwait(false);
        await using (lease.ConfigureAwait(false))
        {
            Assert.That(lease.Factory.TransportProfileUri, Is.EqualTo(configuration.TransportProfileUri));
            Assert.That(provider.Inspect(configuration).Readiness, Is.EqualTo(PubSubReadiness.Ready));
        }
    }

    [Test]
    public async Task AConfiguredCredentialCannotFallBackToTheAnonymousBrokerBinding()
    {
        var provider = new BuiltInPubSubTransportProvider();
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            Profile = PubSubProfile.MqttJson,
            Endpoint = "mqtts://broker.example:8883",
            Topic = "sample",
            BrokerAuthentication = PubSubBrokerAuthentication.Provider,
            CredentialReference = "broker-login"
        };
        var store = new PubSubObservationStore(2);
        await Assert.ThatAsync(async () => await provider.AcquireAsync(configuration,
            new PubSubProviderContext(
                DefaultTelemetry.Create(static _ => { }), TimeProvider.System, store.TransportDiagnostics),
            CancellationToken.None).ConfigureAwait(false), Throws.InvalidOperationException).ConfigureAwait(false);
        Assert.That(provider.Inspect(configuration).Readiness, Is.EqualTo(PubSubReadiness.RequiresConfiguration));
    }

    [Test]
    public async Task AFailedConfiguredFactoryReleasesItsAcquiredOwner()
    {
        var transport = new Mock<IPubSubTransportFactory>();
        transport.SetupGet(factory => factory.TransportProfileUri).Returns(Profiles.PubSubMqttJsonTransport);
        var owner = new CountingOwner();
        var provider = new ConfiguredPubSubTransportProvider("configured", [PubSubProfile.UdpUadp],
            _ => new PubSubPrerequisite("Test", PubSubReadiness.Ready, "Installed test binding."),
            (_, _, _) => ValueTask.FromResult(new PubSubTransportLease(transport.Object, owner)));
        var factory = new PubSubRuntimeFactory(
            DefaultTelemetry.Create(static _ => { }), transportProviders: [provider]);
        await Assert.ThatAsync(async () => await factory.CreateAsync(
            PubSubTestRuntime.Configuration with { TransportProviderId = "configured" },
            PubSubTestRuntime.ReceiveAuthorization, null, new PubSubObservationStore(2), CancellationToken.None)
            .ConfigureAwait(false), Throws.InvalidOperationException).ConfigureAwait(false);
        Assert.That(owner.Disposals, Is.EqualTo(1));
        transport.Verify(item => item.Create(
            It.IsAny<PubSubConnectionDataType>(), It.IsAny<ITelemetryContext>(), It.IsAny<TimeProvider>()),
            Times.Never);
    }

    [Test]
    public async Task SyntheticSourceHasTypedMonotonicValuesAndAnIndependentHardSampleLimit()
    {
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            Publication = PubSubPublication.Synthetic
        };
        var clock = new PubSubTestClock();
        var bounded = new PubSubBoundedSource(new PubSubSyntheticSource(configuration, clock), 3);
        for (int expected = 1; expected <= 3; expected++)
        {
            PublishedDataSetSnapshot sample = await bounded.SampleAsync(bounded.BuildMetaData()).ConfigureAwait(false);
            Assert.That(sample.Fields, Has.Count.EqualTo(3));
            Assert.That(sample.Fields[1].Value.TryGetValue(out int actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(sample.Fields[0].Value.TryGetValue(out bool toggle), Is.True);
            Assert.That(toggle, Is.EqualTo(expected % 2 == 0));
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.That(bounded.Completed.IsCompletedSuccessfully, Is.True);
        Assert.That(bounded.Samples, Is.EqualTo(3));
        await Assert.ThatAsync(async () => await bounded.SampleAsync(bounded.BuildMetaData()).ConfigureAwait(false),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
        Assert.That(bounded.Samples, Is.EqualTo(3));
    }

    [Test]
    public async Task WriteBackRequiresCompleteFramesAndIsBoundedByRateAndCount()
    {
        var target = new Mock<ISubscribedDataSetSink>();
        target.Setup(sink => sink.WriteAsync(It.IsAny<IReadOnlyList<DataSetField>>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var clock = new PubSubTestClock();
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            MaxPublishedMessages = 2,
            Fields = [new PubSubFieldConfiguration { Name = "Value" }]
        };
        var store = new PubSubObservationStore(2);
        var sink = new PubSubControlledWriteBack(target.Object, store, configuration, clock);
        await using (sink.ConfigureAwait(false))
        {
            await sink.WriteAsync([PubSubTestRuntime.Field(1)]).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await sink.WriteAsync([PubSubTestRuntime.Field(2)])
                .ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(1));
            await sink.WriteAsync([PubSubTestRuntime.Field(3)]).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(1));
            await Assert.ThatAsync(async () => await sink.WriteAsync([PubSubTestRuntime.Field(4)])
                .ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

            target.Verify(item => item.WriteAsync(
                It.IsAny<IReadOnlyList<DataSetField>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            Assert.That(store.Snapshot().AcceptedDataSets, Is.EqualTo(2));
            Assert.That(store.Snapshot().Values[0].Value.TryGetValue(out int latest), Is.True);
            Assert.That(latest, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task AFailedUaWriteCannotMasqueradeAsASuccessfulLocalCommit()
    {
        var session = new Mock<IServerSession>();
        session.SetupGet(value => value.IsConnected).Returns(true);
        session.Setup(value => value.ResolveNodeIdAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
            .Returns((NodeId node, CancellationToken _) => ValueTask.FromResult(node));
        session.Setup(value => value.WriteAsync(It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ArrayOf<StatusCode>>([StatusCodes.BadUserAccessDenied]));
        var local = new PubSubObservationStore(2);
        await local.WriteAsync([PubSubTestRuntime.Field(7)]).ConfigureAwait(false);
        var targets = new TargetVariablesDataType
        {
            TargetVariables =
            [
                new FieldTargetDataType { TargetNodeId = new NodeId("Target", 0), AttributeId = Attributes.Value }
            ]
        };
        var target = new TargetVariablesSink(targets, new PubSubVerifiedTargetWriter(
            new ServerTargetVariableWriter(session.Object, DefaultTelemetry.Create(static _ => { })), local));
        var controlled = new PubSubControlledWriteBack(target, local,
            PubSubTestRuntime.Configuration with { Fields = [new PubSubFieldConfiguration { Name = "Value" }] },
            TimeProvider.System);
        await using (controlled.ConfigureAwait(false))
        {
            await Assert.ThatAsync(async () => await controlled.WriteAsync([PubSubTestRuntime.Field(8)])
                .ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(local.Snapshot().AcceptedDataSets, Is.EqualTo(1));
            Assert.That(local.Snapshot().Values[0].Value.TryGetValue(out int retained), Is.True);
            Assert.That(retained, Is.EqualTo(7));
            Assert.That(local.Snapshot().Evidence.Contains(
                entry => entry.Status.Code == StatusCodes.BadUserAccessDenied),
                Is.True);
        }
    }

    [Test]
    public async Task DeactivatingAWriteBackBindingKeepsReceptionLocalAndDoesNotInvokeTheServer()
    {
        var target = new Mock<ISubscribedDataSetSink>(MockBehavior.Strict);
        var local = new PubSubObservationStore(2);
        var sink = new PubSubControlledWriteBack(
            target.Object, local, PubSubTestRuntime.Configuration, TimeProvider.System);
        await using (sink.ConfigureAwait(false))
        {
            sink.Deactivate();
            await sink.WriteAsync([PubSubTestRuntime.Field(9)]).ConfigureAwait(false);
            Assert.That(local.Snapshot().AcceptedDataSets, Is.EqualTo(1));
            target.VerifyNoOtherCalls();
        }
    }

    [Test]
    public async Task GuardedUaBindingsCancelAndDrainWithoutDisposingABorrowedPrimarySession()
    {
        var session = new Mock<IServerSession>(MockBehavior.Strict);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Setup(value => value.ReadAsync(It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ArrayOf<ReadValueId> _, CancellationToken token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return ArrayOf<DataValue>.Empty;
            });
        var guard = new PubSubGuardedServerSession(session.Object);
        Task<ArrayOf<DataValue>> read = guard.ReadAsync([new ReadValueId { NodeId = new NodeId("Source", 0) }])
            .AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await guard.DisposeAsync().ConfigureAwait(false);
        await Assert.ThatAsync(() => read, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        await Assert.ThatAsync(async () => await guard.ReadAsync([]).ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        session.Verify(value => value.DisposeAsync(), Times.Never);
    }

    [Test]
    public async Task ActionResponderUsesACountAndRateBudgetBeforeInvokingTheUaAdapter()
    {
        var handler = new Mock<IPubSubActionHandler>();
        handler.Setup(value => value.HandleAsync(It.IsAny<PubSubActionInvocation>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new PubSubActionHandlerResult { StatusCode = StatusCodes.Good }));
        var clock = new PubSubTestClock();
        var controlled = new PubSubControlledActionHandler(handler.Object, new PubSubObservationStore(2), 2, clock);
        await using (controlled.ConfigureAwait(false))
        {
            Assert.That((await controlled.HandleAsync(new PubSubActionInvocation())
                .ConfigureAwait(false)).StatusCode.Code,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await controlled.HandleAsync(new PubSubActionInvocation())
                .ConfigureAwait(false)).StatusCode.Code,
                Is.EqualTo(StatusCodes.BadTooManyOperations));
            clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert.That((await controlled.HandleAsync(new PubSubActionInvocation())
                .ConfigureAwait(false)).StatusCode.Code,
                Is.EqualTo(StatusCodes.Good));
            clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert.That((await controlled.HandleAsync(new PubSubActionInvocation())
                .ConfigureAwait(false)).StatusCode.Code,
                Is.EqualTo(StatusCodes.BadTooManyOperations));
            handler.Verify(value => value.HandleAsync(
                It.IsAny<PubSubActionInvocation>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }

    [Test]
    public async Task AKeyProviderForAnotherGroupFailsClosedAndReleasesItsOwner()
    {
        var keys = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
        keys.SetupGet(provider => provider.SecurityGroupId).Returns("other-group");
        var owner = new CountingOwner();
        var resolver = new ConfiguredPubSubKeyProvider("keys",
            (_, _) => ValueTask.FromResult(new PubSubKeyProviderLease(keys.Object, owner)));
        var factory = new PubSubRuntimeFactory(DefaultTelemetry.Create(static _ => { }), keyProviders: [resolver]);
        PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityGroupId = "selected-group",
            SecurityProviderId = "keys",
            SecurityKeyServiceEndpoint = "opc.tcp://localhost:4840/Sks"
        };
        await Assert.ThatAsync(async () => await factory.CreateAsync(
            configuration, new PubSubStartAuthorization(), null,
            new PubSubObservationStore(2), CancellationToken.None).ConfigureAwait(false),
            Throws.InvalidOperationException).ConfigureAwait(false);
        Assert.That(owner.Disposals, Is.EqualTo(1));
        keys.Verify(provider => provider.GetCurrentKeyAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static string CreateUdpLoopbackEndpoint()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        if (socket.LocalEndPoint is not IPEndPoint endpoint)
        {
            throw new InvalidOperationException("The loopback socket did not receive an endpoint.");
        }
        return string.Create(CultureInfo.InvariantCulture, $"opc.udp://127.0.0.1:{endpoint.Port}");
    }

    private static string DescribeRoundTrip(PubSubWorkspaceSnapshot snapshot)
    {
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture,
                $"Phase={snapshot.Phase}; source={snapshot.SourceSamples}")
            .AppendLine(CultureInfo.InvariantCulture, $"Applied={snapshot.Observations.AcceptedDataSets}; " +
                $"rejected={snapshot.Observations.RejectedDataSets}")
            .AppendLine(CultureInfo.InvariantCulture,
                $"Metadata={snapshot.Observations.Metadata.Count}; status={snapshot.Status}");
        foreach (PubSubCounterRow counter in snapshot.Counters)
        {
            if (counter.Application != 0 || counter.Transport != 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"{counter.Counter}: application={counter.Application}, transport={counter.Transport}");
            }
        }
        foreach (PubSubMessageRow message in snapshot.Observations.Messages)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Wire publisher={message.Publisher}, group={message.WriterGroup}, writer={message.Writer}, " +
                $"sequence={message.Sequence}, fields={message.FieldCount}, metadata={message.MetadataVersion}");
        }
        foreach (PubSubFieldValue field in snapshot.Observations.Values)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Local {field.Name} ({field.Type})={field.Text}; status={field.Status}");
        }
        foreach (PubSubEvidence evidence in snapshot.Observations.Evidence)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{evidence.Area}: {evidence.Status}: {evidence.Detail}");
        }
        return text.ToString();
    }

    /// <summary>
    /// Loops independent copies of encoded frames through the runtime's real receive path.
    /// </summary>
    private sealed class EncodedLoopbackTransport : IPubSubTransportFactory, IPubSubTransport
    {
        public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

        public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

        public bool IsConnected => Volatile.Read(ref m_connected) != 0;

        public long SentFrames => Interlocked.Read(ref m_sentFrames);

        public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;

        public IPubSubTransport Create(
            PubSubConnectionDataType connection,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(telemetry);
            ArgumentNullException.ThrowIfNull(timeProvider);
            m_clock = timeProvider;
            return this;
        }

        public ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref m_connected, 1) == 0)
            {
                StateChanged?.Invoke(this,
                    new PubSubTransportStateChangedEventArgs(true, StatusCodes.Good, "Loopback opened."));
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            m_frames.Writer.TryComplete();
            if (Interlocked.Exchange(ref m_connected, 0) != 0)
            {
                StateChanged?.Invoke(this,
                    new PubSubTransportStateChangedEventArgs(false, StatusCodes.Good, "Loopback closed."));
            }
            return ValueTask.CompletedTask;
        }

        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsConnected)
            {
                throw new ServiceResultException(StatusCodes.BadNotConnected);
            }
            var frame = new PubSubTransportFrame(
                payload.ToArray(), topic, DateTimeUtc.From(m_clock.GetUtcNow()));
            await m_frames.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref m_sentFrames);
        }

        public IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            return m_frames.Reader.ReadAllAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return CloseAsync();
        }

        private readonly Channel<PubSubTransportFrame> m_frames = Channel.CreateBounded<PubSubTransportFrame>(
            new BoundedChannelOptions(32)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        private TimeProvider m_clock = TimeProvider.System;
        private int m_connected;
        private long m_sentFrames;
    }

    private sealed class CountingOwner : IAsyncDisposable
    {
        public int Disposals { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
