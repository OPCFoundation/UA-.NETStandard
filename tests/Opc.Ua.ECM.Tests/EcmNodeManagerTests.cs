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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.ECM;
using Opc.Ua.ECM.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using static Opc.Ua.ECM.Server.EcmModelNodes;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Exercises ECM graph creation and manager disposal with in-memory providers.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EcmNodeManagerTests
    {
        /// <summary>
        /// The shared measurement builder creates a typed scalar child and reuses it on refresh.
        /// </summary>
        [Test]
        public async Task SharedMeasurementBuilderCreatesAndReusesTypedMetadata()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var context = manager.SystemContext;
            EnergyMeasurementState measurementPoint = context.CreateInstanceOfEnergyMeasurementType(
                browseName: new QualifiedName("Meter", manager.InstanceNamespaceIndex));
            var browseName = new QualifiedName("Power", manager.InstanceNamespaceIndex);
            var engineeringUnits = new EUInformation
            {
                DisplayName = new LocalizedText("W")
            };

            EnergyMeasurementValueState first = EcmMeasurementNodeBuilder.AddMeasurement(
                context,
                measurementPoint,
                browseName,
                Variant.From(12.5f),
                Opc.Ua.DataTypeIds.Float,
                Opc.Ua.ReferenceTypeIds.HasComponent,
                engineeringUnits,
                measurementId: 17);
            EnergyMeasurementValueState refreshed = EcmMeasurementNodeBuilder.AddMeasurement(
                context,
                measurementPoint,
                browseName,
                Variant.From(19.25f),
                Opc.Ua.DataTypeIds.Float,
                Opc.Ua.ReferenceTypeIds.HasComponent,
                engineeringUnits,
                measurementId: 17);

            Assert.That(refreshed, Is.SameAs(first));
            Assert.That(first.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Float));
            Assert.That(first.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.Scalar));
            Assert.That(first.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(first.WrappedValue.TryGetValue(out float value), Is.True);
            Assert.That(value, Is.EqualTo(19.25f));

            var children = new List<BaseInstanceState>();
            measurementPoint.GetChildren(context, children);
            Assert.That(
                children.FindAll(child => child is EnergyMeasurementValueState &&
                    child.BrowseName == browseName),
                Has.Count.EqualTo(1));
            Assert.That(first.EngineeringUnits!.Value.DisplayName.Text, Is.EqualTo("W"));

            BaseInstanceState? measurementIdNode = null;
            var measurementChildren = new List<BaseInstanceState>();
            first.GetChildren(context, measurementChildren);
            foreach (BaseInstanceState child in measurementChildren)
            {
                if (string.Equals(child.BrowseName.Name, "MeasurementID", StringComparison.Ordinal))
                {
                    measurementIdNode = child;
                    break;
                }
            }
            Assert.That(measurementIdNode, Is.InstanceOf<BaseVariableState>());
            Assert.That(
                ((BaseVariableState)measurementIdNode!).WrappedValue.TryGetValue(out ushort measurementId),
                Is.True);
            Assert.That(measurementId, Is.EqualTo((ushort)17));
        }

        /// <summary>
        /// Published accuracy-domain NodeIds do not retain provider-owned opaque bytes.
        /// </summary>
        [Test]
        public async Task SharedMeasurementBuilderCopiesOpaqueAccuracyDomainIdentifier()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var context = manager.SystemContext;
            ushort ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);
            EnergyMeasurementState measurementPoint = context.CreateInstanceOfEnergyMeasurementType(
                browseName: new QualifiedName("Meter", manager.InstanceNamespaceIndex));
            byte[] identifier = [1, 2, 3];
            var accuracyDomain = new NodeId(new ByteString(identifier.AsMemory()), ecmNamespace);
            NodeId expected = new(ByteString.From(identifier.AsSpan()), ecmNamespace);

            EnergyMeasurementValueState measurement = EcmMeasurementNodeBuilder.AddMeasurement(
                context,
                measurementPoint,
                new QualifiedName("Power", manager.InstanceNamespaceIndex),
                Variant.From(12.5f),
                Opc.Ua.DataTypeIds.Float,
                Opc.Ua.ReferenceTypeIds.HasComponent,
                accuracyDomain: accuracyDomain);

            identifier[0] = 9;

            Assert.That(measurement.AccuracyDomain!.Value, Is.EqualTo(expected));
        }

        /// <summary>
        /// Shutdown joins provider acquisition, closes new admission and releases completed bindings.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposalJoinsProviderRefreshAndGraphRegistration(bool cancelCreation)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            SimulatedEcmPowerProvider simulated = CreateProvider();
            EcmPowerSnapshot snapshot = await simulated.ReadAsync();
            provider.Setup(service => service.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => ReadSnapshotAsync(token));
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);
            Task<EcmPowerBinding> creation = manager.CreatePowerAsync(
                new QualifiedName("Power", manager.InstanceNamespaceIndex), provider.Object, parent,
                cancellationToken: cancellation.Token).AsTask();
            Task? disposing = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                disposing = manager.DisposeAsync().AsTask();
                Assert.That(disposing.IsCompleted, Is.False);
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await manager.CreatePowerAsync(
                        new QualifiedName("Late", manager.InstanceNamespaceIndex), provider.Object, parent));
                if (cancelCreation)
                {
                    cancellation.Cancel();
                }
            }
            finally
            {
                release.TrySetResult(true);
                if (cancelCreation)
                {
                    await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                        await creation.WaitAsync(TimeSpan.FromSeconds(10)));
                    Assert.That(parent.FindChild(manager.SystemContext,
                        new QualifiedName("Power", manager.InstanceNamespaceIndex)), Is.Null);
                }
                else
                {
                    EcmPowerBinding binding = await creation.WaitAsync(TimeSpan.FromSeconds(10));
                    if (disposing != null)
                    {
                        await disposing.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    Assert.That(Required(binding.Standby.StartPause).OnCallAsync, Is.Null);
                    Assert.That(Required(binding.Standby.PauseTime).OnSimpleWriteValueAsync, Is.Null);
                }
                if (disposing != null)
                {
                    await disposing.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            provider.Verify(service => service.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
            provider.VerifyNoOtherCalls();

            async ValueTask<EcmPowerSnapshot> ReadSnapshotAsync(CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return snapshot;
            }
        }

        /// <summary>
        /// Admitted queued creation finishes during shutdown and duplicate names do not acquire a provider.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ShutdownDrainsQueuedCreationAndPreservesDuplicateCheck(bool duplicateName)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            EcmPowerSnapshot snapshot = await CreateProvider().ReadAsync();
            provider.Setup(service => service.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => ReadSnapshotAsync(token));
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);
            var firstName = new QualifiedName("FirstPower", manager.InstanceNamespaceIndex);
            Task<EcmPowerBinding> first = manager.CreatePowerAsync(firstName, provider.Object, parent).AsTask();
            Task<EcmPowerBinding> second;
            Task disposing;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                second = manager.CreatePowerAsync(duplicateName
                    ? firstName
                    : new QualifiedName("SecondPower", manager.InstanceNamespaceIndex),
                    provider.Object, parent).AsTask();
                Assert.That(second.IsCompleted, Is.False);
                disposing = manager.DisposeAsync().AsTask();
                Assert.That(disposing.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }

            EcmPowerBinding firstBinding = await first.WaitAsync(TimeSpan.FromSeconds(10));
            EcmPowerBinding? secondBinding = null;
            if (duplicateName)
            {
                ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await second.WaitAsync(TimeSpan.FromSeconds(10))))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
            }
            else
            {
                secondBinding = await second.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.That(secondBinding.Standby.NodeId, Is.Not.EqualTo(firstBinding.Standby.NodeId));
            }
            await disposing.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(Required(firstBinding.Standby.StartPause).OnCallAsync, Is.Null);
            Assert.That(Required(firstBinding.Standby.PauseTime).OnSimpleWriteValueAsync, Is.Null);
            if (secondBinding != null)
            {
                Assert.That(Required(secondBinding.Standby.StartPause).OnCallAsync, Is.Null);
                Assert.That(Required(secondBinding.Standby.PauseTime).OnSimpleWriteValueAsync, Is.Null);
            }
            provider.Verify(service => service.ReadAsync(It.IsAny<CancellationToken>()),
                Times.Exactly(duplicateName ? 1 : 2));
            provider.VerifyNoOtherCalls();

            async ValueTask<EcmPowerSnapshot> ReadSnapshotAsync(CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return snapshot;
            }
        }

        /// <summary>
        /// Rejected creation inputs do not call the borrowed provider.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public async Task InvalidBrowseNameDoesNotAcquireProvider(string? name)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);

            await Assert.ThrowsAsync<ArgumentException>(async () => await manager.CreatePowerAsync(
                new QualifiedName(name, manager.InstanceNamespaceIndex), provider.Object));

            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Invalid providers and cancelled requests are rejected before resolving an address-space parent.
        /// </summary>
        [Test]
        public async Task InvalidOrCancelledCreationDoesNotResolveGraph()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            var name = new QualifiedName("Power", manager.InstanceNamespaceIndex);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await manager.CreatePowerAsync(name, null!));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await manager.CreatePowerAsync(name, provider.Object, cancellationToken: cancellation.Token));

            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A failed provider acquisition detaches the staged graph so the same name can be retried.
        /// </summary>
        [Test]
        public async Task FailedCreationLeavesNameAvailableForRetry()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            EcmPowerSnapshot snapshot = await CreateProvider().ReadAsync();
            provider.SetupSequence(service => service.ReadAsync(It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Acquisition unavailable."))
                .Returns(new ValueTask<EcmPowerSnapshot>(snapshot));
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);
            var name = new QualifiedName("Power", manager.InstanceNamespaceIndex);

            InvalidOperationException error = (await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.CreatePowerAsync(name, provider.Object, parent)))!;

            Assert.That(error.Message, Is.EqualTo("Acquisition unavailable."));
            Assert.That(parent.FindChild(manager.SystemContext, name), Is.Null);
            EcmPowerBinding binding = await manager.CreatePowerAsync(name, provider.Object, parent);
            Assert.That(parent.FindChild(manager.SystemContext, name), Is.SameAs(binding.Standby));
            Assert.That(manager.FindPredefinedNode(binding.Standby.NodeId), Is.SameAs(binding.Standby));
            Assert.That(binding.Standby.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(Required(binding.Standby.StandbyManagementStatus).Value,
                Is.EqualTo((byte)EcmStandbyStatus.ReadyToOperate));
            provider.Verify(service => service.ReadAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A caller-supplied parent's cleanup failure does not replace the provider acquisition failure.
        /// </summary>
        [Test]
        public async Task DetachFailurePreservesOriginalAcquisitionError()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            provider.Setup(service => service.ReadAsync(It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Acquisition unavailable."));
            var parent = new ThrowingDetachParent
            {
                NodeId = new NodeId("device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);
            var name = new QualifiedName("Power", manager.InstanceNamespaceIndex);
            parent.ThrowOnRemove = true;
            InvalidOperationException error;
            try
            {
                error = (await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.CreatePowerAsync(name, provider.Object, parent)))!;
            }
            finally
            {
                parent.ThrowOnRemove = false;
            }

            Assert.That(error.Message, Is.EqualTo("Acquisition unavailable."));
            Assert.That(parent.RemoveCount, Is.EqualTo(1));
            Assert.That(parent.FindChild(manager.SystemContext, name), Is.Null);
            provider.Verify(service => service.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// WOL-only creation publishes no standby graph, rejects duplicate names and joins manager disposal.
        /// </summary>
        [Test]
        public async Task StandaloneWolCreationRegistersAndReleasesBinding()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);
            var name = new QualifiedName("Sleep", manager.InstanceNamespaceIndex);
            EcmWolBinding binding = await manager.CreateWolAsync(name, CreateProvider(), parent);

            Assert.That(parent.FindChild(manager.SystemContext, name), Is.SameAs(binding.PowerOff));
            Assert.That(manager.FindPredefinedNode(binding.PowerOff.NodeId), Is.SameAs(binding.PowerOff));
            Assert.That(binding.PowerOff.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(binding.PowerOff.TypeDefinitionId, Is.EqualTo(NodeId.Create(
                ObjectTypes.EnergyDevicePowerOffType, Namespaces.ECM, manager.SystemContext.NamespaceUris)));
            Assert.That(Required(binding.PowerOff.SwitchOffWOL).OnCallAsync, Is.Not.Null);
            ServiceResultException duplicate = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await manager.CreateWolAsync(name, CreateProvider(), parent));
            Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));

            await manager.DisposeAsync();

            Assert.That(Required(binding.PowerOff.SwitchOffWOL).OnCallAsync, Is.Null);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await manager.CreateWolAsync(name, CreateProvider(), parent));
        }

        /// <summary>
        /// Failed WOL metadata leaves no published child and permits retry with the same BrowseName.
        /// </summary>
        [Test]
        public async Task StandaloneWolMetadataFailureRollsBackBeforeRetry()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queues = new MonitoredItemQueueFactory(telemetry);
            await using EcmNodeManager manager = CreateManager(telemetry, queues);
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("device", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Device", manager.InstanceNamespaceIndex),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await manager.AddPredefinedNodeAsync(parent);
            var provider = new Mock<IEcmWolProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.MacAddress).Throws(new InvalidOperationException("No MAC available."));
            var name = new QualifiedName("Sleep", manager.InstanceNamespaceIndex);

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.CreateWolAsync(name, provider.Object, parent));

            Assert.That(error.Message, Is.EqualTo("No MAC available."));
            Assert.That(parent.FindChild(manager.SystemContext, name), Is.Null);
            EcmWolBinding retried = await manager.CreateWolAsync(name, CreateProvider(), parent);
            Assert.That(manager.FindPredefinedNode(retried.PowerOff.NodeId), Is.SameAs(retried.PowerOff));
            Assert.That(parent.FindChild(manager.SystemContext, name), Is.SameAs(retried.PowerOff));
            provider.VerifyGet(value => value.MacAddress, Times.Once);
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Creates a reference provider that changes no host or network state.
        /// </summary>
        private static SimulatedEcmPowerProvider CreateProvider()
        {
            return new SimulatedEcmPowerProvider(
                [new EcmEnergySavingMode(1, "Pause", 7000, 2000, 2000, 60000, 3000, 1, 0.1f, 0.2f)],
                ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan()));
        }

        /// <summary>
        /// Supplies only in-memory server dependencies; the test does not bind a network endpoint.
        /// </summary>
        private static EcmNodeManager CreateManager(
            ITelemetryContext telemetry,
            MonitoredItemQueueFactory queues)
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            IEncodeableFactory factory = EncodeableFactory.Create();
            factory.Builder.AddOpcUaECM().Commit();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(EcmTestTypeTable.Create(namespaces));
            server.SetupGet(value => value.Factory).Returns(factory);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            server.SetupGet(value => value.NodeManager).Returns(Mock.Of<IMasterNodeManager>());
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return new EcmNodeManager(server.Object, new ApplicationConfiguration
            {
                ApplicationUri = "urn:ecm-tests:lifetime",
                ApplicationName = "EcmLifetime",
                ServerConfiguration = new ServerConfiguration()
            });
        }

        /// <summary>
        /// Simulates an application parent that detaches its child and then reports cleanup failure.
        /// </summary>
        private sealed class ThrowingDetachParent : BaseObjectState
        {
            /// <summary>
            /// Creates an unattached application parent.
            /// </summary>
            public ThrowingDetachParent()
                : base(null)
            {
            }

            /// <summary>
            /// Gets or sets whether detachment reports an application error.
            /// </summary>
            public bool ThrowOnRemove { get; set; }

            /// <summary>
            /// Gets the number of attempted detachments.
            /// </summary>
            public int RemoveCount { get; private set; }

            /// <inheritdoc/>
            public override void RemoveChild(BaseInstanceState child)
            {
                RemoveCount++;
                base.RemoveChild(child);
                if (ThrowOnRemove)
                {
                    throw new InvalidOperationException("Parent detachment failed.");
                }
            }
        }
    }
}
