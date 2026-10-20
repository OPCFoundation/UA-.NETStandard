/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.PlasticsRubber.Client;
using Opc.Ua.Tests;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.PlasticsRubber.Runtime.Tests
{
    /// <summary>
    /// Exercises the public common facade against a caller-owned, in-memory UA service boundary.
    /// </summary>
    [TestFixture]
    public sealed class PlasticsRubberClientBoundaryTests
    {
        /// <summary>
        /// Every capability selects its own generated proxy while retaining the caller's identity.
        /// </summary>
        [TestCaseSource(nameof(Capabilities))]
        public void FacadeBindsExactGeneratedCapabilityToExistingSession(
            Func<PlasticsRubberClient, NodeId, ObjectTypeClient> bind, Type expectedType)
        {
            var peer = new CommonClientPeer();
            var client = new PlasticsRubberClient(peer.Session.Object, peer.Telemetry);
            NodeId instance = new("machine/capability", 1);

            ObjectTypeClient proxy = bind(client, instance);

            Assert.Multiple(() =>
            {
                Assert.That(proxy.GetType(), Is.EqualTo(expectedType));
                Assert.That(proxy.ObjectId, Is.EqualTo(instance));
                Assert.That(proxy.Session, Is.SameAs(peer.Session.Object));
                Assert.That(proxy.Telemetry, Is.SameAs(peer.Telemetry));
                Assert.That(client.Session, Is.SameAs(peer.Session.Object));
                Assert.That(client.IsSupported, Is.True);
                Assert.That(peer.Context.Factory.ContainsEncodeableType(DataTypeIds.PIDParametersDataType), Is.True);
            });
        }

        /// <summary>
        /// Invalid identities and missing model namespaces fail before any service request.
        /// </summary>
        [TestCaseSource(nameof(Capabilities))]
        public void FacadeRejectsNullInstancesAndUnsupportedNamespaces(
            Func<PlasticsRubberClient, NodeId, ObjectTypeClient> bind, Type expectedType)
        {
            var peer = new CommonClientPeer(includeCommonNamespace: false);
            var client = new PlasticsRubberClient(peer.Session.Object, peer.Telemetry);
            Assert.That(client.IsSupported, Is.False, expectedType.Name);
            Assert.Throws<ArgumentException>(() => bind(client, NodeId.Null));
            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => bind(client, new NodeId(600u, 1)));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(peer.Session.Invocations.All(call => call.Method.Name.StartsWith("get_",
                StringComparison.Ordinal)), Is.True);
        }

        /// <summary>
        /// Registration is idempotent and never replaces an already registered common encoding.
        /// </summary>
        [Test]
        public void FacadeValidatesDependenciesAndReusesRegisteredEncodings()
        {
            var peer = new CommonClientPeer();
            Assert.Throws<ArgumentNullException>(() => new PlasticsRubberClient(null!, peer.Telemetry));
            Assert.Throws<ArgumentNullException>(() => new PlasticsRubberClient(peer.Session.Object, null!));
            _ = new PlasticsRubberClient(peer.Session.Object, peer.Telemetry);
            Assert.That(peer.Context.Factory.TryGetEncodeableType(
                DataTypeIds.PIDParametersDataType, out IEncodeableType? encoding), Is.True);
            var second = new PlasticsRubberClient(peer.Session.Object, peer.Telemetry);
            Assert.That(peer.Context.Factory.TryGetEncodeableType(
                DataTypeIds.PIDParametersDataType, out IEncodeableType? reused), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reused, Is.SameAs(encoding));
                Assert.That(second.Session, Is.SameAs(peer.Session.Object));
            });
        }

        /// <summary>
        /// The generated method sends the exact instance, declaration and typed input and decodes page bytes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FacadePageWorkflowUsesCorrectGeneratedCallAndPreservesErrorsAsync(bool fail)
        {
            var peer = new CommonClientPeer();
            NodeId instance = new(612u, 1);
            NodeId method = ExpandedNodeId.ToNodeId(MethodIds.MachineConfigurationType_GetPage,
                peer.Context.NamespaceUris);
            using var cancellation = new CancellationTokenSource();
            peer.Session.Setup(session => session.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), cancellation.Token))
                .Returns((RequestHeader _, ArrayOf<CallMethodRequest> calls, CancellationToken _) =>
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(calls.Count, Is.EqualTo(1));
                        Assert.That(calls[0].ObjectId, Is.EqualTo(instance));
                        Assert.That(calls[0].MethodId, Is.EqualTo(method));
                        Assert.That(calls[0].InputArguments.ToArray(), Is.EqualTo(new[] { Variant.From("overview") }));
                    });
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = fail ? StatusCodes.BadUserAccessDenied : StatusCodes.Good,
                                OutputArguments = [Variant.From(ByteString.From(new byte[] { 0x89, 0x50, 0x4e, 0x47 }))]
                            }
                        ]
                    });
                });
            var client = new PlasticsRubberClient(peer.Session.Object, peer.Telemetry);
            if (fail)
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await client.MachineConfiguration(instance).GetPageAsync("overview",
                        cancellation.Token).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            else
            {
                ByteString page = await client.MachineConfiguration(instance)
                    .GetPageAsync("overview", cancellation.Token).ConfigureAwait(false);
                Assert.That(page.Span.ToArray(), Is.EqualTo(new byte[] { 0x89, 0x50, 0x4e, 0x47 }));
            }
            peer.Session.Verify(session => session.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), cancellation.Token), Times.Once);
            Assert.That(peer.Session.Invocations.Any(call => call.Method.Name.StartsWith("Dispose",
                StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// Public alarm and logbook streams decode fixed fields and verified subtypes, not raw decoder calls.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PublicLogbookAndAlarmStreamsPreservePayloadSubtypeAndOwnershipAsync(bool alarm, bool subtype)
        {
            var peer = new CommonClientPeer();
            var client = new PlasticsRubberClient(peer.Session.Object, peer.Telemetry);
            NodeId declared = ExpandedNodeId.ToNodeId(
                alarm ? ObjectTypeIds.HelpOffNormalAlarmType : ObjectTypeIds.LogbookEventType,
                    peer.Context.NamespaceUris);
            NodeId actual = subtype ? new NodeId(990u, 1) : declared;
            NodeId notifier = new(710u, 1);
            var options = new MonitoringOptions();
            using var cancellation = new CancellationTokenSource();
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            peer.Session.SetupGet(session => session.NodeCache).Returns(cache.Object);
            if (subtype)
            {
                cache.Setup(value => value.IsTypeOfAsync(actual, declared, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<bool>(true));
            }
            bool disposed = false;
            var stream = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            stream.Setup(value => value.SubscribeEventsAsync(
                notifier, It.IsAny<EventFilter>(), options, cancellation.Token))
                .Returns((NodeId _, EventFilter filter, MonitoringOptions _, CancellationToken token) =>
                    Notifications(EventFields(filter, actual, notifier), () => disposed = true, token));

            IAsyncEnumerable<BaseEventTypeRecord> records = alarm
                ? client.ObserveAlarmsAsync(notifier, stream.Object, options, cancellation.Token)
                : client.ObserveLogbookAsync(notifier, stream.Object, options, cancellation.Token);
            await using (IAsyncEnumerator<BaseEventTypeRecord> iterator =
                records.GetAsyncEnumerator(cancellation.Token))
            {
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                BaseEventTypeRecord record = iterator.Current;
                Assert.Multiple(() =>
                {
                    Assert.That(record.GetType(), Is.EqualTo(alarm
                        ? typeof(HelpOffNormalAlarmTypeRecord) : typeof(LogbookEventTypeRecord)));
                    Assert.That(record.EventType, Is.EqualTo(actual));
                    Assert.That(record.SourceNode, Is.EqualTo(notifier));
                    Assert.That(record.EventId.Span.ToArray(), Is.EqualTo(new byte[] { 5, 7, 11 }));
                    Assert.That(record.Time, Is.EqualTo(EventTime));
                    Assert.That(record.Message.Text, Is.EqualTo("Pressure warning"));
                    Assert.That(record.Severity, Is.EqualTo(650));
                    if (alarm)
                    {
                        Assert.That(((HelpOffNormalAlarmTypeRecord)record).HelpText.Text,
                            Is.EqualTo("Inspect the pressure sensor"));
                    }
                });
                Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.False);
            }
            Assert.That(disposed, Is.True);
            cache.Verify(value => value.IsTypeOfAsync(actual, declared, It.IsAny<CancellationToken>()),
                subtype ? Times.Once() : Times.Never());
            stream.Verify(value => value.DisposeAsync(), Times.Never);
            stream.Verify(value => value.SubscribeEventsAsync(
                notifier, It.IsAny<EventFilter>(), options, cancellation.Token), Times.Once);
            Assert.That(peer.Session.Invocations.Any(call =>
                call.Method.Name.StartsWith("Close", StringComparison.Ordinal) ||
                call.Method.Name.StartsWith("Dispose", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// Absent or unrelated event types cannot masquerade as the requested generated record.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PublicStreamsRejectMissingAndUnrelatedEventTypesAsync(bool alarm, bool missing)
        {
            var peer = new CommonClientPeer();
            var client = new PlasticsRubberClient(peer.Session.Object, peer.Telemetry);
            NodeId declared = ExpandedNodeId.ToNodeId(
                alarm ? ObjectTypeIds.HelpOffNormalAlarmType : ObjectTypeIds.LogbookEventType,
                    peer.Context.NamespaceUris);
            NodeId actual = missing ? NodeId.Null : new NodeId(991u, 1);
            NodeId notifier = new(711u, 1);
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            peer.Session.SetupGet(session => session.NodeCache).Returns(cache.Object);
            if (!missing)
            {
                cache.Setup(value => value.IsTypeOfAsync(actual, declared, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<bool>(false));
            }
            bool disposed = false;
            var stream = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            stream.Setup(value => value.SubscribeEventsAsync(
                notifier, It.IsAny<EventFilter>(), null, It.IsAny<CancellationToken>()))
                .Returns((NodeId _, EventFilter filter, MonitoringOptions _, CancellationToken token) =>
                    Notifications(EventFields(filter, actual, notifier), () => disposed = true, token));
            IAsyncEnumerable<BaseEventTypeRecord> records = alarm
                ? client.ObserveAlarmsAsync(notifier, stream.Object)
                : client.ObserveLogbookAsync(notifier, stream.Object);
            await using IAsyncEnumerator<BaseEventTypeRecord> iterator = records.GetAsyncEnumerator();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await iterator.MoveNextAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(disposed, Is.True);
            cache.Verify(value => value.IsTypeOfAsync(actual, declared, It.IsAny<CancellationToken>()),
                missing ? Times.Never() : Times.Once());
            stream.Verify(value => value.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// A missing stream is explicit, and cancellation disposes only the subscription enumerator.
        /// </summary>
        [Test]
        public async Task PublicStreamsValidateSubscriptionAndHonorCancellationAsync()
        {
            var peer = new CommonClientPeer();
            var client = new PlasticsRubberClient(peer.Session.Object, peer.Telemetry);
            NodeId notifier = new(720u, 1);
            Assert.Throws<InvalidOperationException>(() => client.ObserveLogbookAsync(notifier));
            Assert.Throws<InvalidOperationException>(() => client.ObserveAlarmsAsync(notifier));
            Assert.Throws<ArgumentException>(() => client.ObserveLogbookAsync(NodeId.Null));
            using var cancellation = new CancellationTokenSource();
            var stream = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            bool disposed = false;
            stream.Setup(value => value.SubscribeEventsAsync(
                notifier, It.IsAny<EventFilter>(), null, cancellation.Token))
                .Returns((NodeId _, EventFilter _, MonitoringOptions _, CancellationToken token) =>
                    Notifications([], () => disposed = true, token));
            cancellation.Cancel();
            await using IAsyncEnumerator<LogbookEventTypeRecord> iterator =
                client.ObserveLogbookAsync(notifier, stream.Object, cancellationToken: cancellation.Token)
                    .GetAsyncEnumerator(cancellation.Token);
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await iterator.MoveNextAsync().ConfigureAwait(false));
            Assert.That(disposed, Is.True);
            stream.Verify(value => value.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// DI retains one factory/delegate and forwards the original token and failure without owning a session.
        /// </summary>
        [Test]
        public async Task HostingRegistrationIsIdempotentAndForwardsCallerCancellationAndFailure()
        {
            var telemetry = NUnitTelemetryContext.Create();
            using var cancellation = new CancellationTokenSource();
            var failure = new InvalidOperationException("Session acquisition failed");
            int calls = 0;
            Task<ManagedSession> SessionFactory(CancellationToken token)
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                calls++;
                return Task.FromException<ManagedSession>(failure);
            }
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(telemetry);
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(SessionFactory);
            var builder = new Mock<IOpcUaClientBuilder>(MockBehavior.Strict);
            builder.SetupGet(value => value.Services).Returns(services);

            Assert.That(builder.Object.AddPlasticsRubberClient(), Is.SameAs(builder.Object));
            builder.Object.AddPlasticsRubberClient();
            using ServiceProvider provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<PlasticsRubberClientFactory>();
            var create = provider.GetRequiredService<Func<CancellationToken, Task<PlasticsRubberClient>>>();
            Assert.Multiple(() =>
            {
                Assert.That(services.Count(value => value.ServiceType == typeof(PlasticsRubberClientFactory)),
                    Is.EqualTo(1));
                Assert.That(services.Count(value => value.ServiceType == typeof(Func<CancellationToken,
                    Task<PlasticsRubberClient>>)), Is.EqualTo(1));
                Assert.That(provider.GetRequiredService<PlasticsRubberClientFactory>(), Is.SameAs(factory));
                Assert.That(provider.GetRequiredService<Func<CancellationToken, Task<PlasticsRubberClient>>>(),
                    Is.SameAs(create));
            });
            Assert.That(await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await create(cancellation.Token).ConfigureAwait(false)), Is.SameAs(failure));
            Assert.That(calls, Is.EqualTo(1));
            Assert.Throws<ArgumentNullException>(() => new PlasticsRubberClientFactory(null!, telemetry));
            Assert.Throws<ArgumentNullException>(() => new PlasticsRubberClientFactory(SessionFactory, null!));
            Assert.Throws<ArgumentNullException>(()
                => OpcUaPlasticsRubberClientExtensions.AddPlasticsRubberClient(null!));
        }

        private static IEnumerable<TestCaseData> Capabilities()
        {
            yield return Capability<StartDeviceTypeClient>((client, id) => client.StartDevice(id));
            yield return Capability<MonitoredParameterTypeClient>((client, id) => client.MonitoredParameter(id));
            yield return Capability<ControlledParameterTypeClient>((client, id) => client.ControlledParameter(id));
            yield return Capability<ClosedLoopControlTypeClient>((client, id) => client.ClosedLoopControl(id));
            yield return Capability<MaintenanceTypeClient>((client, id) => client.Maintenance(id));
            yield return Capability<DiagnosticsTypeClient>((client, id) => client.Diagnostics(id));
            yield return Capability<IdentificationTypeClient>((client, id) => client.Identification(id));
            yield return Capability<MachineInformationTypeClient>((client, id) => client.MachineInformation(id));
            yield return Capability<MachineConfigurationTypeClient>((client, id) => client.MachineConfiguration(id));
            yield return Capability<MachineStatusTypeClient>((client, id) => client.MachineStatus(id));
            yield return Capability<MachineMESConfigurationTypeClient>((client, id) => client.MesConfiguration(id));
            yield return Capability<MachineMESStatusTypeClient>((client, id) => client.MesStatus(id));
            yield return Capability<ProductionControlTypeClient>((client, id) => client.ProductionControl(id));
            yield return Capability<JobsTypeClient>((client, id) => client.Jobs(id));
            yield return Capability<ActiveJobValuesTypeClient>((client, id) => client.ActiveJob(id));
            yield return Capability<ActiveCyclicJobValuesTypeClient>((client, id) => client.ActiveCyclicJob(id));
            yield return Capability<ProductionDatasetManagementTypeClient>((client, id)
                => client.ProductionDatasets(id));
            yield return Capability<MaterialListTypeClient>((client, id) => client.Materials(id));
            yield return Capability<DriveTypeClient>((client, id) => client.Drive(id));
            yield return Capability<MeasuringDeviceTypeClient>((client, id) => client.MeasuringDevice(id));
            yield return Capability<EnergyTypeClient>((client, id) => client.Energy(id));
            yield return Capability<TemperatureZoneTypeClient>((client, id) => client.TemperatureZone(id));
        }

        private static TestCaseData Capability<T>(Func<PlasticsRubberClient, NodeId, ObjectTypeClient> bind)
            where T : ObjectTypeClient
        {
            return new TestCaseData(bind, typeof(T)).SetArgDisplayNames("binding", typeof(T).Name);
        }

        private static Variant[] EventFields(EventFilter filter, NodeId type, NodeId source)
        {
            return filter.SelectClauses.ToArray()!.Select(clause =>
            {
                string? name = clause.BrowsePath.Count == 1 ? clause.BrowsePath[0].Name : null;
                return name switch
                {
                    Opc.Ua.BrowseNames.EventType => Variant.From(type),
                    Opc.Ua.BrowseNames.SourceNode => Variant.From(source),
                    Opc.Ua.BrowseNames.EventId => Variant.From(ByteString.From(new byte[] { 5, 7, 11 })),
                    Opc.Ua.BrowseNames.Time => Variant.From(EventTime),
                    Opc.Ua.BrowseNames.Message => Variant.From(new LocalizedText("Pressure warning")),
                    Opc.Ua.BrowseNames.Severity => Variant.From((ushort)650),
                    BrowseNames.HelpText => Variant.From(new LocalizedText("Inspect the pressure sensor")),
                    _ => Variant.Null
                };
            }).ToArray();
        }

        private static async IAsyncEnumerable<EventNotification> Notifications(
            Variant[] fields, Action disposed, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.CompletedTask.ConfigureAwait(false);
                yield return new EventNotification(null, fields.ToArrayOf());
            }
            finally
            {
                disposed();
            }
        }

        private static DateTimeUtc EventTime =>
            (DateTimeUtc)new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        private sealed class CommonClientPeer
        {
            internal CommonClientPeer(bool includeCommonNamespace = true)
            {
                Telemetry = NUnitTelemetryContext.Create();
                Context = ServiceMessageContext.Create(Telemetry);
                Context.NamespaceUris.GetIndexOrAppend("urn:common-client:instances");
                if (includeCommonNamespace)
                {
                    Context.NamespaceUris.GetIndexOrAppend(Namespaces.PlasticsRubber);
                }
                Session.SetupGet(value => value.MessageContext).Returns(Context);
                Session.SetupGet(value => value.NamespaceUris).Returns(Context.NamespaceUris);
                Session.SetupGet(value => value.Factory).Returns(Context.Factory);
            }

            internal ITelemetryContext Telemetry { get; }
            internal ServiceMessageContext Context { get; }
            internal Mock<ISession> Session { get; } = new(MockBehavior.Strict);
        }
    }
}
