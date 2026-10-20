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
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Server;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Reproduces the reviewed provider-selection, accepted-state and construction-ownership boundaries.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class WmtpReviewRegressionTests
    {
        /// <summary>
        /// Starts the real namespace owner; each test removes its own peripheral registrations.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Drains the namespace owner and its isolated transport resources.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Two omitted selections must not silently bind one application-wide multipurpose equipment adapter.
        /// </summary>
        [Test]
        public async Task OmittedProviderRejectsAmbientAdapterForBothPeripheralsAsync()
        {
            var equipment = new WmtpTestEquipment();
            equipment.FullSnapshot();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(7d));
            equipment.Transfers.Setup(provider => provider.TransferAsync(
                    It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<StatusCode>(StatusCodes.Good));
            IWmtpDeviceProvider shared = MultipurposeProvider(equipment);
            Mock<IMachineryBuildContext> context = AmbientContext(shared, equipment);
            var failures = new List<StatusCode>();
            var peripherals = new List<WmtpPeripheral>();
            try
            {
                foreach (string name in new[] { "AmbientFirst", "AmbientSecond" })
                {
                    try
                    {
                        peripherals.Add(await Builder(context.Object, name)
                            .WithFeatures(WmtpFeatures.Calibration | WmtpFeatures.Firmware)
                            .AddMeasurement(WmtpTestEquipment.Definition()).BuildAsync().ConfigureAwait(false));
                    }
                    catch (ServiceResultException error)
                    {
                        failures.Add(error.StatusCode);
                    }
                }
                foreach (WmtpPeripheral peripheral in peripherals)
                {
                    await peripheral.Measurements[0].SetTriggerAsync(25, new WmtpRequest()).ConfigureAwait(false);
                    await peripheral.SetCalibrationAsync(1, new WmtpRequest()).ConfigureAwait(false);
                    await peripheral.TransferAsync(WmtpTransferKind.Firmware, new byte[] { 1 }.ToByteString(),
                        new WmtpRequest()).ConfigureAwait(false);
                }
                Assert.That(failures, Is.EqualTo(new[] { StatusCodes.BadConfigurationError,
                    StatusCodes.BadConfigurationError }),
                    $"Implicitly built {peripherals.Count} runtimes using the same unkeyed adapter.");
                context.Verify(owner => owner.GetService<IWmtpDeviceProvider>(), Times.Never);
                equipment.Device.Verify(provider => provider.ReadAsync(It.IsAny<CancellationToken>()), Times.Never);
                equipment.Measurement.Verify(provider => provider.ConfigureAsync(
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()),
                    Times.Never);
                equipment.Calibration.Verify(provider => provider.SetCalibrationAsync(
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
                equipment.Transfers.Verify(provider => provider.TransferAsync(
                    It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                foreach (WmtpPeripheral peripheral in peripherals)
                {
                    await peripheral.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Explicit adapters reuse their own additional interfaces, never the ambient adapter or another instance.
        /// </summary>
        [Test]
        public async Task ExplicitProvidersKeepMeasurementCalibrationAndTransferIsolatedAsync()
        {
            var ambient = new WmtpTestEquipment();
            Mock<IMachineryBuildContext> context = AmbientContext(MultipurposeProvider(ambient), ambient);
            var first = new WmtpTestEquipment();
            var second = new WmtpTestEquipment();
            first.FullSnapshot();
            second.FullSnapshot();
            first.Samples["Temperature"] = first.Sample(Variant.From(11d));
            second.Samples["Temperature"] = second.Sample(Variant.From(22d));
            ByteString transferred = default;
            first.Transfers.Setup(provider => provider.TransferAsync(
                    It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Callback((WmtpTransferKind _, ByteString payload, CancellationToken _) =>
                    transferred = payload.ToArray().ToByteString())
                .Returns(new ValueTask<StatusCode>(StatusCodes.Good));
            await using WmtpPeripheral firstPeripheral = await Builder(context.Object, "ExplicitFirst")
                .WithProvider(MultipurposeProvider(first))
                .WithFeatures(WmtpFeatures.Calibration | WmtpFeatures.Firmware)
                .AddMeasurement(WmtpTestEquipment.Definition()).BuildAsync().ConfigureAwait(false);
            await using WmtpPeripheral secondPeripheral = await Builder(context.Object, "ExplicitSecond")
                .WithProvider(MultipurposeProvider(second))
                .WithFeatures(WmtpFeatures.Calibration | WmtpFeatures.Firmware)
                .AddMeasurement(WmtpTestEquipment.Definition()).BuildAsync().ConfigureAwait(false);
            await firstPeripheral.Measurements[0].SetTriggerAsync(25, new WmtpRequest()).ConfigureAwait(false);
            await firstPeripheral.SetCalibrationAsync(1, new WmtpRequest()).ConfigureAwait(false);
            ByteString payload = new byte[] { 1, 2 }.ToByteString();
            await firstPeripheral.TransferAsync(WmtpTransferKind.Firmware, payload, new WmtpRequest())
                .ConfigureAwait(false);
            Assert.That(firstPeripheral.NodeId, Is.Not.EqualTo(secondPeripheral.NodeId));
            Assert.That(firstPeripheral.Measurements[0].Sample.WrappedValue, Is.EqualTo(Variant.From(11d)));
            Assert.That(secondPeripheral.Measurements[0].Sample.WrappedValue, Is.EqualTo(Variant.From(22d)));
            Assert.That(secondPeripheral.Measurements[0].TriggerMilliseconds, Is.EqualTo(1000));
            first.Measurement.Verify(provider => provider.ConfigureAsync(
                "Temperature", 25, Variant.From(0d), It.IsAny<CancellationToken>()), Times.Once);
            first.Calibration.Verify(provider => provider.SetCalibrationAsync(
                true, It.IsAny<CancellationToken>()), Times.Once);
            first.Transfers.Verify(provider => provider.TransferAsync(
                WmtpTransferKind.Firmware, It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(transferred, Is.EqualTo(payload));
            second.Measurement.Verify(provider => provider.ConfigureAsync(
                It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()), Times.Never);
            second.Calibration.Verify(provider => provider.SetCalibrationAsync(
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            second.Transfers.Verify(provider => provider.TransferAsync(
                It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Never);
            context.Verify(owner => owner.GetService<IWmtpDeviceProvider>(), Times.Never);
            ambient.Device.Verify(provider => provider.ReadAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Deliberately shared read-only equipment remains caller-owned when either runtime is removed.
        /// </summary>
        [Test]
        public async Task ExplicitlySharedProviderRemainsCallerOwnedWhenOnePeripheralDisposesAsync()
        {
            var equipment = new WmtpTestEquipment();
            Mock<IAsyncDisposable> resource = equipment.Device.As<IAsyncDisposable>();
            resource.Setup(provider => provider.DisposeAsync()).Returns(default(ValueTask));
            await using WmtpPeripheral first = await equipment.Builder(m_fixture.Context, "SharedFirst")
                .BuildAsync().ConfigureAwait(false);
            await using WmtpPeripheral second = await equipment.Builder(m_fixture.Context, "SharedSecond")
                .BuildAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
            await second.RefreshAsync().ConfigureAwait(false);
            Assert.That(m_fixture.Context.Manager.FindPredefinedNode(first.NodeId), Is.Null);
            Assert.That(m_fixture.Context.Manager.FindPredefinedNode(second.NodeId), Is.SameAs(second.State));
            equipment.Device.Verify(provider => provider.ReadAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
            resource.Verify(provider => provider.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// Accepted hardware settings commit despite cancellation and become the unchanged argument of the next call.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AcceptedMeasurementConfigurationCommitsDespiteCallerCancellationAsync(bool setDelta)
        {
            var equipment = new WmtpTestEquipment();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(4d));
            await using WmtpPeripheral peripheral = await equipment.Builder(m_fixture.Context)
                .AddMeasurement(WmtpTestEquipment.Definition()).BuildAsync().ConfigureAwait(false);
            WmtpMeasurement measurement = peripheral.Measurements[0];
            using var cancellation = new CancellationTokenSource();
            uint deviceTrigger = 1000;
            Variant deviceDelta = Variant.From(0d);
            int acceptances = 0;
            equipment.Measurement.Setup(provider => provider.ConfigureAsync(
                    "Temperature", It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .Returns((string _, uint trigger, Variant delta, CancellationToken _) =>
                {
                    deviceTrigger = trigger;
                    deviceDelta = delta;
                    if (++acceptances == 1)
                    {
                        cancellation.Cancel();
                    }
                    return new ValueTask<StatusCode>(StatusCodes.Good);
                });
            OperationCanceledException? rejectedAcceptance = null;
            try
            {
                if (setDelta)
                {
                    await measurement.SetDeltaAsync(Variant.From(2d), new WmtpRequest(), cancellation.Token)
                        .ConfigureAwait(false);
                }
                else
                {
                    await measurement.SetTriggerAsync(25, new WmtpRequest(), cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException error)
            {
                rejectedAcceptance = error;
            }
            uint acceptedTrigger = measurement.TriggerMilliseconds;
            Variant acceptedDelta = measurement.Delta;
            double publishedDelta = measurement.State.DeltaCondition!.Value;
            uint firstDeviceTrigger = deviceTrigger;
            Variant firstDeviceDelta = deviceDelta;
            if (setDelta)
            {
                await measurement.SetTriggerAsync(25, new WmtpRequest()).ConfigureAwait(false);
            }
            else
            {
                await measurement.SetDeltaAsync(Variant.From(2d), new WmtpRequest()).ConfigureAwait(false);
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(cancellation.IsCancellationRequested, Is.True);
                Assert.That(rejectedAcceptance, Is.Null);
                Assert.That(acceptedTrigger, Is.EqualTo(firstDeviceTrigger));
                Assert.That(acceptedDelta, Is.EqualTo(firstDeviceDelta));
                Assert.That(publishedDelta, Is.EqualTo(setDelta ? 2d : 0d));
                Assert.That(deviceTrigger, Is.EqualTo(25));
                Assert.That(deviceDelta, Is.EqualTo(Variant.From(2d)));
                Assert.That(measurement.TriggerMilliseconds, Is.EqualTo(25));
                Assert.That(measurement.Delta, Is.EqualTo(Variant.From(2d)));
                Assert.That(measurement.State.DeltaCondition.Value, Is.EqualTo(2d));
                Assert.That(acceptances, Is.EqualTo(2));
            }
        }

        /// <summary>
        /// Cancellation and failure in the final catalog publish roll back nodes without taking caller resources.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedConformancePublicationRetainsCallerOwnershipAndCanRetryAsync(bool cancel)
        {
            var equipment = new WmtpTestEquipment();
            Mock<IAsyncDisposable> resource = equipment.Device.As<IAsyncDisposable>();
            bool disposed = false;
            resource.Setup(provider => provider.DisposeAsync()).Callback(() => disposed = true)
                .Returns(default(ValueTask));
            equipment.Device.Setup(provider => provider.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken _) => disposed ? throw new ObjectDisposedException("equipment") :
                    new ValueTask<WmtpDeviceSnapshot>(equipment.Snapshot));
            const string name = "PublicationRetry";
            WmtpPeripheralBuilder builder = equipment.Builder(m_fixture.Context, name)
                .WithOwnedResource(resource.Object);
            using var cancellation = new CancellationTokenSource();
            var server = (ServerInternalData)m_fixture.Context.Manager.Server;
            ConformanceUnitsManager catalog = server.ConformanceUnitsManager!;
            var contributor = new Mock<IConformanceContributor>(MockBehavior.Strict);
            bool armed = true;
            NodeId stagedId = NodeId.Null;
            contributor.SetupGet(source => source.ConformanceUnits).Returns(() =>
            {
                BaseInstanceState? root = m_fixture.Context.MachinesFolder.FindChild(m_fixture.Context.Context,
                    new QualifiedName(name, m_fixture.Context.InstanceNamespaceIndex));
                if (armed && root != null &&
                    ReferenceEquals(m_fixture.Context.Manager.FindPredefinedNode(root.NodeId), root))
                {
                    armed = false;
                    stagedId = root.NodeId;
                    if (cancel)
                    {
                        cancellation.Cancel();
                    }
                    else
                    {
                        throw new InvalidOperationException("Injected conformance publication failure.");
                    }
                }
                return ArrayOf<QualifiedName>.Empty;
            });
            contributor.SetupGet(source => source.ServerProfiles).Returns(ArrayOf<string>.Empty);
            catalog.Register(contributor.Object);
            try
            {
                if (cancel)
                {
                    await Assert.CatchAsync<OperationCanceledException>(async () =>
                        await builder.BuildAsync(cancellation.Token).ConfigureAwait(false));
                }
                else
                {
                    InvalidOperationException error = (await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await builder.BuildAsync(cancellation.Token).ConfigureAwait(false)))!;
                    Assert.That(error.Message, Is.EqualTo("Injected conformance publication failure."));
                }
                Assert.That(stagedId.IsNull, Is.False);
                Assert.That(m_fixture.Context.Manager.FindPredefinedNode(stagedId), Is.Null);
                Assert.That(m_fixture.Context.MachinesFolder.FindChild(m_fixture.Context.Context,
                    new QualifiedName(name, m_fixture.Context.InstanceNamespaceIndex)), Is.Null);
                resource.Verify(provider => provider.DisposeAsync(), Times.Never);
                await using WmtpPeripheral retried = await builder.BuildAsync().ConfigureAwait(false);
                Assert.That(m_fixture.Context.Manager.FindPredefinedNode(retried.NodeId), Is.SameAs(retried.State));
                Assert.That(retried.ServerProfiles.ToArray(), Does.Contain(
                    "http://opcfoundation.org/UA-Profile/WMTP/Server/WMTPBasicServerFacet"));
                await retried.DisposeAsync().ConfigureAwait(false);
                resource.Verify(provider => provider.DisposeAsync(), Times.Once);
            }
            finally
            {
                catalog.Unregister(contributor.Object);
            }
        }

        /// <summary>
        /// Timer creation is fallible initialization too; caller resources must survive it for a same-builder retry.
        /// </summary>
        [Test]
        public async Task FailedTimerInitializationRetainsCallerOwnershipAndCanRetryAsync()
        {
            var equipment = new WmtpTestEquipment();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(3d));
            var resource = new Mock<IAsyncDisposable>(MockBehavior.Strict);
            resource.Setup(owned => owned.DisposeAsync()).Returns(default(ValueTask));
            var clock = new Mock<TimeProvider> { CallBase = true };
            NodeId stagedId = NodeId.Null;
            clock.Setup(provider => provider.CreateTimer(
                    It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Callback(() =>
                {
                    BaseInstanceState? root = m_fixture.Context.MachinesFolder.FindChild(m_fixture.Context.Context,
                        new QualifiedName("TimerRetry", m_fixture.Context.InstanceNamespaceIndex));
                    Assert.That(root, Is.Not.Null);
                    stagedId = root!.NodeId;
                    Assert.That(m_fixture.Context.Manager.FindPredefinedNode(stagedId), Is.SameAs(root));
                })
                .Throws(new InvalidOperationException("Injected timer initialization failure."));
            WmtpPeripheralBuilder builder = equipment.Builder(m_fixture.Context, "TimerRetry")
                .WithOwnedResource(resource.Object).WithClock(clock.Object)
                .AddMeasurement(WmtpTestEquipment.Definition());
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await builder.BuildAsync().ConfigureAwait(false));
            Assert.That(stagedId.IsNull, Is.False);
            Assert.That(m_fixture.Context.Manager.FindPredefinedNode(stagedId), Is.Null);
            resource.Verify(owned => owned.DisposeAsync(), Times.Never);
            await using WmtpPeripheral retried = await builder.WithClock(equipment.Clock).BuildAsync()
                .ConfigureAwait(false);
            await retried.DisposeAsync().ConfigureAwait(false);
            resource.Verify(owned => owned.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Creates a valid nameplate without selecting an equipment adapter.
        /// </summary>
        private static WmtpPeripheralBuilder Builder(IMachineryBuildContext context, string name)
        {
            return context.AddWirelessPeripheral(name).WithIdentification(identity =>
            {
                identity.Manufacturer = new LocalizedText("WMTP review equipment");
                identity.SerialNumber = name;
            });
        }

        /// <summary>
        /// Reuses one physical mock identity for every optional equipment contract.
        /// </summary>
        private static IWmtpDeviceProvider MultipurposeProvider(WmtpTestEquipment equipment)
        {
            equipment.Device.As<IWmtpMeasurementProvider>().Setup(provider => provider.ReadAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string sensor, CancellationToken token) =>
                    equipment.Measurement.Object.ReadAsync(sensor, token));
            equipment.Device.As<IWmtpMeasurementProvider>().Setup(provider => provider.ConfigureAsync(
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .Returns((string sensor, uint trigger, Variant delta, CancellationToken token) =>
                    equipment.Measurement.Object.ConfigureAsync(sensor, trigger, delta, token));
            equipment.Device.As<IWmtpCalibrationProvider>().Setup(provider => provider.SetCalibrationAsync(
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((bool enabled, CancellationToken token) =>
                    equipment.Calibration.Object.SetCalibrationAsync(enabled, token));
            equipment.Device.As<IWmtpTransferProvider>().Setup(provider => provider.TransferAsync(
                    It.IsAny<WmtpTransferKind>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((WmtpTransferKind kind, ByteString payload, CancellationToken token) =>
                    equipment.Transfers.Object.TransferAsync(kind, payload, token));
            return equipment.Device.Object;
        }

        /// <summary>
        /// Forwards real namespace construction while exposing an unkeyed application-wide provider registration.
        /// </summary>
        private Mock<IMachineryBuildContext> AmbientContext(IWmtpDeviceProvider provider, WmtpTestEquipment equipment)
        {
            var context = new Mock<IMachineryBuildContext>(MockBehavior.Strict);
            context.SetupGet(owner => owner.Context).Returns(m_fixture.Context.Context);
            context.SetupGet(owner => owner.Manager).Returns(m_fixture.Context.Manager);
            context.SetupGet(owner => owner.MachinesFolder).Returns(m_fixture.Context.MachinesFolder);
            context.SetupGet(owner => owner.InstanceNamespaceIndex).Returns(m_fixture.Context.InstanceNamespaceIndex);
            context.Setup(owner => owner.GetService<IWmtpDeviceProvider>()).Returns(provider);
            context.Setup(owner => owner.GetService<IWmtpAuthorization>()).Returns(equipment.Authorization.Object);
            context.Setup(owner => owner.GetService<TimeProvider>()).Returns(equipment.Clock);
            context.Setup(owner => owner.AddMachine(
                    It.IsAny<WirelessMachineToolPeripheralState>(), It.IsAny<QualifiedName>()))
                .Returns((WirelessMachineToolPeripheralState state, QualifiedName name) =>
                    m_fixture.Context.AddMachine(state, name));
            return context;
        }

        /// <summary>The real generated-model host shared by this nonparallel fixture.</summary>
        private readonly WmtpTestFixture m_fixture = new();
    }
}
