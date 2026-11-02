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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.PADIM.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using DataTypeIds = Opc.Ua.Types.DataTypeIds;

namespace Opc.Ua.PADIM.Tests
{
    /// <summary>
    /// Verifies standalone provider behavior and generated family contracts.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed partial class PadimRuntimeTests
    {
        /// <summary>
        /// Starts the shared server and standalone DI owner with PA-DIM models.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            StandardServer server = await m_fixture.StartAsync();
            m_manager = new DiNodeManager(
                server.CurrentInstance, m_fixture.Config, null, [new PadimModelProvider()]);
            await m_manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>());
        }

        /// <summary>
        /// Disposes provider resources before stopping the shared server.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_manager != null)
            {
                await m_manager.DisposeAsync();
            }
            if (m_fixture != null)
            {
                await m_fixture.StopAsync();
            }
        }

        /// <summary>
        /// Checks each device factory against the published type and required nameplate members.
        /// </summary>
        [Test]
        public async Task EveryDeviceFamilyUsesItsPublishedTypeAndNameplate(
            [Values] PadimDeviceKind kind)
        {
            var builder = await m_manager.CreatePadimDeviceAsync(Name($"Device{kind}"), kind);
            PADIMState state = builder.Device;
            string publishedName = kind == PadimDeviceKind.Device ? "PADIMType" : $"{kind}Type";
            NodeId publishedType = PublishedType(publishedName);
            Assert.Multiple(() =>
            {
                Assert.That(state.TypeDefinitionId, Is.EqualTo(publishedType));
                Assert.That(state.Manufacturer, Is.Not.Null);
                Assert.That(state.ManufacturerUri, Is.Not.Null);
                Assert.That(state.Model, Is.Not.Null);
                Assert.That(state.SerialNumber, Is.Not.Null);
                Assert.That(state.ProductInstanceUri, Is.Not.Null);
                Assert.That(state.ProductCode, Is.Not.Null);
                Assert.That(state.HardwareRevision, Is.Not.Null);
                Assert.That(state.SoftwareRevision, Is.Not.Null);
                Assert.That(state.RevisionCounter, Is.Not.Null);
                Assert.That(state.AssetId, Is.Not.Null);
                Assert.That(state.DeviceHealth, Is.Not.Null);
                Assert.That(state.FactoryReset, Is.Null, "Optional operations must not be enabled without providers.");
            });
        }

        /// <summary>
        /// Checks signal identity, acquisition defaults and the declared wire type for every family.
        /// </summary>
        [Test]
        public async Task EverySignalFamilyPreservesItsDeclaredValueType(
            [Values] PadimSignalKind kind)
        {
            PadimDevice device = await CreateDeviceAsync($"Signals{kind}");
            SignalState signal = await device.AddSignalAsync(Name($"Signal{kind}"), kind, $"Tag{kind}");
            string memberName = kind switch
            {
                PadimSignalKind.Control or PadimSignalKind.TwoStateControl or PadimSignalKind.MultiStateControl =>
                    "ControlSignal",
                PadimSignalKind.Discrete => "DiscreteSignal",
                PadimSignalKind.TwoStateDiscrete => "TwoStateDiscreteSignal",
                PadimSignalKind.MultiStateDiscrete => "MultiStateDiscreteSignal",
                _ => "AnalogSignal"
            };
            string typeName = kind switch
            {
                PadimSignalKind.TwoStateControl => "TwoStateDiscreteControlSignalType",
                PadimSignalKind.MultiStateControl => "MultiStateDiscreteControlSignalType",
                _ => $"{kind}SignalType"
            };
            BaseVariableState? value = signal.FindChild(device.Context, ModelName(memberName)) as BaseVariableState;
            Assert.That(value, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(signal.TypeDefinitionId, Is.EqualTo(PublishedType(typeName)));
                Assert.That(signal.SignalTag!.Value, Is.EqualTo($"Tag{kind}"));
                Assert.That(value!.TypeDefinitionId.IsNull, Is.False);
                Assert.That(value.NodeId.NamespaceIndex, Is.EqualTo(m_manager.InstanceNamespaceIndex));
                Assert.That(m_manager.FindPredefinedNode<NodeState>(value.NodeId), Is.SameAs(value));
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
                Assert.That(value.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            });
            NodeId expected = kind switch
            {
                PadimSignalKind.TwoStateDiscrete or PadimSignalKind.TwoStateControl => DataTypeIds.Boolean,
                PadimSignalKind.MultiStateDiscrete or PadimSignalKind.MultiStateControl => DataTypeIds.UInt32,
                PadimSignalKind.Discrete => DataTypeIds.BaseDataType,
                PadimSignalKind.Analog => DataTypeIds.Number,
                _ => DataTypeIds.Float
            };
            Assert.That(value!.DataType, Is.EqualTo(expected));
        }

        /// <summary>
        /// Preserves measurement data types, ranks and generated engineering-unit members.
        /// </summary>
        [Test]
        public async Task EveryMeasurementFamilyKeepsItsPublishedNumericRestriction(
            [Values] PadimMeasurementKind kind)
        {
            PadimDevice device = await CreateDeviceAsync($"Measurement{kind}");
            AnalogSignalObjectState signal = await device.AddMeasurementAsync(Name("Value"), kind, "PV-001");
            NodeId expectedDataType = kind is PadimMeasurementKind.Analog or PadimMeasurementKind.Totalizer
                ? DataTypeIds.Number
                : DataTypeIds.Float;
            Assert.Multiple(() =>
            {
                Assert.That(signal.AnalogSignal!.DataType, Is.EqualTo(expectedDataType));
                Assert.That(signal.AnalogSignal.EngineeringUnits, Is.Not.Null);
                Assert.That(signal.AnalogSignal.ValueRank,
                    Is.EqualTo(kind == PadimMeasurementKind.Pat ? ValueRanks.Scalar : ValueRanks.Any));
                Assert.That(signal.AnalogSignal.BrowseName.NamespaceIndex,
                    Is.EqualTo(m_manager.SystemContext.NamespaceUris.GetIndex(Namespaces.PADIM)));
            });
        }

        /// <summary>
        /// Restores the latest acquired sample, including uncertain quality, after simulation is disabled.
        /// </summary>
        [Test]
        public async Task SimulationRestoresLatestAcquiredValueAndOriginalQuality()
        {
            (PadimDevice device, BaseVariableState value) = await CreateAnalogAsync("Simulation");
            var provider = new Mock<IPadimValueProvider>();
            DateTimeUtc firstTime = new(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            DateTimeUtc secondTime = new(new DateTime(2026, 1, 2, 0, 0, 1, DateTimeKind.Utc));
            provider.SetupSequence(p => p.ReadAsync("temperature", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From(10f), StatusCodes.Good, firstTime))
                .ReturnsAsync(new DataValue(Variant.From(12f), StatusCodes.UncertainSensorNotAccurate, secondTime));
            var writer = new Mock<IPadimWriteProvider>();
            writer.Setup(p => p.WriteAsync(It.IsAny<string>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StatusCode(StatusCodes.Good));
            var authorizer = new Mock<IPadimAuthorizer>();
            authorizer.Setup(p => p.AuthorizeAsync(
                It.IsAny<ISystemContext>(), It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            PadimValueBinding binding = device.BindValue(value, "temperature", provider.Object)
                .WithSimulation(Variant.From(99f), writer.Object, authorizer.Object);
            var analog = (AnalogSignalVariableState)value;
            await device.RegisterAsync();
            await binding.RefreshAsync();
            await analog.SimulationState!.OnSimpleWriteValueAsync!(
                device.Context, analog.SimulationState, Variant.From(true), CancellationToken.None);
            await binding.RefreshAsync();
            Assert.Multiple(() =>
            {
                Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(99f)));
                Assert.That(analog.ActualValue!.WrappedValue, Is.EqualTo(Variant.From(12f)));
                Assert.That(analog.ActualValue.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
                Assert.That(analog.ActualValue.Timestamp, Is.EqualTo(secondTime));
            });
            await analog.SimulationState.OnSimpleWriteValueAsync!(
                device.Context, analog.SimulationState, Variant.From(false), CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(12f)));
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
                Assert.That(value.Timestamp, Is.EqualTo(secondTime));
            });
        }

        /// <summary>
        /// Retains the specified damping default when the provider rejects a configuration write.
        /// </summary>
        [Test]
        public async Task DampingStartsAtOneSecondAndRequiresProviderCommit()
        {
            (PadimDevice device, BaseVariableState value) = await CreateAnalogAsync("Damping");
            var provider = new Mock<IPadimValueProvider>();
            var writer = new Mock<IPadimWriteProvider>();
            writer.Setup(p => p.WriteAsync("temperature/Damping", Variant.From(2f), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StatusCode(StatusCodes.BadOutOfRange));
            var authorizer = new Mock<IPadimAuthorizer>();
            authorizer.Setup(p => p.AuthorizeAsync(
                It.IsAny<ISystemContext>(), It.IsAny<NodeId>(), "temperature/Damping", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            device.BindValue(value, "temperature", provider.Object)
                .WithDamping(provider.Object, writer.Object, authorizer.Object);
            BaseVariableState damping = ((AnalogSignalVariableState)value).Damping!;
            Assert.That(damping.WrappedValue, Is.EqualTo(Variant.From(1f)));
            AttributeWriteResult rejected = await damping.OnSimpleWriteValueAsync!(
                device.Context, damping, Variant.From(2f), CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.Result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(damping.WrappedValue, Is.EqualTo(Variant.From(1f)));
            });
        }

        /// <summary>
        /// Acquisition failures preserve both views during simulation and do not block later acquisition or reversal.
        /// </summary>
        [Test]
        public async Task FailedAcquisitionPreservesSimulationAndAllowsRecovery()
        {
            (PadimDevice device, BaseVariableState value) = await CreateAnalogAsync("FailedAcquisitionRecovery");
            DateTimeUtc firstTime = new(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            DateTimeUtc recoveredTime = new(new DateTime(2026, 1, 2, 0, 0, 2, DateTimeKind.Utc));
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            provider.SetupSequence(p => p.ReadAsync("temperature", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From(10f), StatusCodes.Good, firstTime))
                .ThrowsAsync(new InvalidOperationException("The acquisition transport is unavailable."))
                .ReturnsAsync(new DataValue(
                    Variant.From(12f), StatusCodes.UncertainSensorNotAccurate, recoveredTime));
            var writer = new Mock<IPadimWriteProvider>(MockBehavior.Strict);
            writer.Setup(p => p.WriteAsync(
                "temperature/SimulationState", It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StatusCode(StatusCodes.Good));
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                device.Context, It.IsAny<NodeId>(), "temperature/SimulationState",
                It.IsAny<CancellationToken>())).ReturnsAsync(true);
            PadimValueBinding binding = device.BindValue(value, "temperature", provider.Object)
                .WithSimulation(Variant.From(99f), writer.Object, authorizer.Object);
            var analog = (AnalogSignalVariableState)value;
            await binding.RefreshAsync();
            await analog.SimulationState!.OnSimpleWriteValueAsync!(
                device.Context, analog.SimulationState, Variant.From(true), CancellationToken.None);
            DateTimeUtc simulatedTime = value.Timestamp;

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await binding.RefreshAsync());

            Assert.Multiple(() =>
            {
                Assert.That(error!.Message, Is.EqualTo("The acquisition transport is unavailable."));
                Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(99f)));
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.Timestamp, Is.EqualTo(simulatedTime));
                Assert.That(analog.ActualValue!.WrappedValue, Is.EqualTo(Variant.From(10f)));
                Assert.That(analog.ActualValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(analog.ActualValue.Timestamp, Is.EqualTo(firstTime));
            });
            await binding.RefreshAsync();
            Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(99f)));
            await analog.SimulationState.OnSimpleWriteValueAsync!(
                device.Context, analog.SimulationState, Variant.From(false), CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(12f)));
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
                Assert.That(value.Timestamp, Is.EqualTo(recoveredTime));
                Assert.That(analog.ActualValue!.WrappedValue, Is.EqualTo(Variant.From(12f)));
            });
            provider.Verify(p => p.ReadAsync("temperature", It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        /// <summary>
        /// Retains the provider's Float value, quality and source timestamp during acquisition.
        /// </summary>
        [Test]
        public async Task RefreshPreservesTypeQualityAndTimestamp()
        {
            (PadimDevice device, BaseVariableState value) = await CreateAnalogAsync("Refresh");
            DateTimeUtc timestamp = new(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            var sample = new DataValue(Variant.From(12.25f), StatusCodes.UncertainSensorNotAccurate, timestamp);
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            provider.Setup(p => p.ReadAsync("temperature", It.IsAny<CancellationToken>())).ReturnsAsync(sample);
            PadimValueBinding binding = device.BindValue(value, "temperature", provider.Object);
            await binding.RefreshAsync();
            Assert.Multiple(() =>
            {
                Assert.That(value.WrappedValue, Is.EqualTo(sample.WrappedValue));
                Assert.That(value.StatusCode, Is.EqualTo(sample.StatusCode));
                Assert.That(value.Timestamp, Is.EqualTo(timestamp));
            });
            provider.VerifyAll();
        }

        /// <summary>
        /// Rejects an incompatible provider sample without replacing the prior measurement.
        /// </summary>
        [Test]
        public async Task WrongProviderTypeDoesNotChangePublishedValue()
        {
            (PadimDevice device, BaseVariableState value) = await CreateAnalogAsync("WrongType");
            value.WrappedValue = Variant.From(42f);
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            provider.Setup(p => p.ReadAsync("temperature", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From("not a number")));
            PadimValueBinding binding = device.BindValue(value, "temperature", provider.Object);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await binding.RefreshAsync());
            Assert.Multiple(() =>
            {
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(42f)));
            });
        }

        /// <summary>
        /// Changes the cache only when both authorization and the write provider accept the value.
        /// </summary>
        [TestCase(true, true)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task RejectedWritesDoNotChangePublishedValue(bool allowed, bool providerAccepts)
        {
            uint providerStatus = providerAccepts ? StatusCodes.Good : StatusCodes.BadOutOfRange;
            (PadimDevice device, BaseVariableState value) = await CreateAnalogAsync($"Write{allowed}{providerStatus}");
            value.WrappedValue = Variant.From(42f);
            var provider = new Mock<IPadimValueProvider>();
            var writer = new Mock<IPadimWriteProvider>(MockBehavior.Strict);
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                device.Context, value.NodeId, "temperature", It.IsAny<CancellationToken>()))
                .ReturnsAsync(allowed);
            if (allowed)
            {
                writer.Setup(p => p.WriteAsync("temperature", Variant.From(99f), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new StatusCode(providerStatus));
            }
            device.BindValue(value, "temperature", provider.Object).WithWrites(writer.Object, authorizer.Object);
            AttributeWriteResult result = await value.OnSimpleWriteValueAsync!(
                device.Context, value, Variant.From(99f), CancellationToken.None);
            bool accepted = allowed && StatusCode.IsGood(providerStatus);
            Assert.Multiple(() =>
            {
                Assert.That(result.Result.StatusCode,
                    Is.EqualTo(allowed ? providerStatus : StatusCodes.BadUserAccessDenied));
                Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(accepted ? 99f : 42f)));
                Assert.That(result.SuppressCacheUpdate, Is.True);
            });
            authorizer.VerifyAll();
            writer.VerifyAll();
        }

        /// <summary>
        /// Invalid reconfiguration leaves the installed writer and authorization policy together.
        /// </summary>
        [Test]
        public async Task RejectedWriteReconfigurationPreservesTheExistingProvider()
        {
            (PadimDevice device, BaseVariableState value) = await CreateAnalogAsync("InvalidWriteReconfiguration");
            var acquisition = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            var original = new Mock<IPadimWriteProvider>(MockBehavior.Strict);
            original.Setup(p => p.WriteAsync("value", Variant.From(42f), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StatusCode(StatusCodes.Good));
            var replacement = new Mock<IPadimWriteProvider>(MockBehavior.Strict);
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                device.Context, value.NodeId, "value", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            PadimValueBinding binding = device.BindValue(value, "value", acquisition.Object)
                .WithWrites(original.Object, authorizer.Object);

            Assert.Throws<ArgumentNullException>(() => binding.WithWrites(replacement.Object, null!));

            AttributeWriteResult result = await value.OnSimpleWriteValueAsync!(
                device.Context, value, Variant.From(42f), CancellationToken.None);
            Assert.That(result.Result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(42f)));
            original.Verify(p => p.WriteAsync("value", Variant.From(42f), It.IsAny<CancellationToken>()), Times.Once);
            replacement.VerifyNoOtherCalls();
            acquisition.VerifyNoOtherCalls();
            authorizer.Verify(p => p.AuthorizeAsync(
                device.Context, value.NodeId, "value", It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Accepts only the three published device-reset modes before invoking the provider.
        /// </summary>
        [TestCase(1, true)]
        [TestCase(2712, true)]
        [TestCase(2713, true)]
        [TestCase(0, false)]
        [TestCase(2, false)]
        [TestCase(2714, false)]
        public async Task FactoryResetAcceptsOnlyPublishedModes(int mode, bool valid)
        {
            PadimDevice device = await CreateDeviceAsync($"Reset{mode}");
            device.State.AddFactoryReset(device.Context);
            var provider = new Mock<IPadimOperationProvider>(MockBehavior.Strict);
            var authorizer = new Mock<IPadimAuthorizer>();
            authorizer.Setup(p => p.AuthorizeAsync(
                It.IsAny<ISystemContext>(), It.IsAny<NodeId>(), "FactoryReset", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            if (valid)
            {
                provider.Setup(p => p.FactoryResetAsync((ResetModeEnum)mode, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new StatusCode(StatusCodes.Good));
            }
            device.WithFactoryReset(device.State.FactoryReset!, provider.Object, authorizer.Object);
            ServiceResult result = await device.State.FactoryReset!.OnCallMethod2Async!(
                device.Context, device.State.FactoryReset, device.State.NodeId,
                [Variant.From(mode)], [], CancellationToken.None);
            Assert.That(result.StatusCode, Is.EqualTo(valid ? StatusCodes.Good : StatusCodes.BadInvalidArgument));
            provider.VerifyAll();
        }

        /// <summary>
        /// Cancels an admitted acquisition and prevents additional work after device disposal.
        /// </summary>
        [Test]
        public async Task ShutdownCancelsAndJoinsProvider()
        {
            (PadimDevice device, BaseVariableState value) = await CreateAnalogAsync("Shutdown");
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IPadimValueProvider>();
            provider.Setup(p => p.ReadAsync("temperature", It.IsAny<CancellationToken>()))
                .Returns(async (string _, CancellationToken ct) =>
                {
                    started.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct);
                    return DataValue.Null;
                });
            PadimValueBinding binding = device.BindValue(value, "temperature", provider.Object);
            Task refresh = binding.RefreshAsync().AsTask();
            await started.Task;
            await device.DisposeAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await refresh);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await binding.RefreshAsync());
            await device.DisposeAsync();
        }

        /// <summary>
        /// Retains family-specific calibration metadata without sharing mutable instance children.
        /// </summary>
        [Test]
        public async Task PhCalibrationAndConditionsKeepDeclaredMetadataAndIndependentInstances()
        {
            PadimDevice device = await CreateDeviceAsync("PhGroups");
            var signal = (PhSignalState)await device.AddSignalAsync(Name("Ph"), PadimSignalKind.Ph, "AT-001");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            BaseObjectState conditions = await device.ConfigureSignalConditionsAsync(signal, group =>
                group.Bind(ModelName("SensingElementImpedance"), "impedance", provider.Object));
            BaseObjectState first = await device.AddCalibrationAsync(signal, Name("Calibration1"), Configure);
            BaseObjectState second = await device.AddCalibrationAsync(signal, Name("Calibration2"), Configure);
            var slope = first.FindChild(device.Context, ModelName("SensorSlope")) as BaseVariableState;
            var secondSlope = second.FindChild(device.Context, ModelName("SensorSlope")) as BaseVariableState;
            var timestamp = first.FindChild(device.Context, ModelName("CalibrationTimestamp")) as BaseVariableState;
            var impedance = conditions.FindChild(device.Context, ModelName("SensingElementImpedance"))
                as BaseVariableState;
            Assert.Multiple(() =>
            {
                Assert.That(slope, Is.Not.Null);
                Assert.That(secondSlope, Is.Not.Null);
                Assert.That(timestamp, Is.Not.Null, "Inherited calibration metadata must remain selectable.");
                Assert.That(impedance, Is.Not.Null);
                Assert.That(slope!.DataType, Is.EqualTo(DataTypeIds.Float));
                Assert.That(slope.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(timestamp!.DataType, Is.EqualTo(DataTypeIds.DateTime));
                Assert.That(impedance!.DataType, Is.EqualTo(DataTypeIds.Float));
                Assert.That(slope.NodeId.NamespaceIndex, Is.EqualTo(m_manager.InstanceNamespaceIndex));
                Assert.That(slope.NodeId, Is.Not.EqualTo(secondSlope!.NodeId));
                Assert.That(first.FindChild(device.Context, ModelName("SensorT90")), Is.Null);
                Assert.That(slope.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
                Assert.That(m_manager.FindPredefinedNode<NodeState>(slope.NodeId), Is.SameAs(slope));
            });
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await device.AddCalibrationAsync(signal, Name("Calibration1"), Configure));
            Assert.That(first.FindChild(device.Context, ModelName("SensorSlope")), Is.SameAs(slope));

            void Configure(PadimGroupBuilder group)
            {
                group.Bind(ModelName("SensorSlope"), "slope", provider.Object);
                group.Bind(ModelName("CalibrationTimestamp"), "calibratedAt", provider.Object);
            }
        }

        /// <summary>
        /// Rolls back a rejected group selection so its bindings and name can be configured again.
        /// </summary>
        [Test]
        public async Task FailedGroupSelectionRemovesBindingsAndDoesNotMutateTheDeclaration()
        {
            PadimDevice device = await CreateDeviceAsync("GroupRollback");
            var signal = (PhSignalState)await device.AddSignalAsync(Name("Ph"), PadimSignalKind.Ph, "AT-002");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await device.AddCalibrationAsync(signal, Name("Calibration"), group =>
                {
                    group.Bind(ModelName("SensorSlope"), "slope", provider.Object);
                    group.Bind(ModelName("NotAPublishedMember"), "invalid", provider.Object);
                }));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(signal.FindChild(device.Context, Name("Calibration")), Is.Null);
            await device.RefreshAsync();
            BaseObjectState recovered = await device.AddCalibrationAsync(signal, Name("Calibration"), group =>
                group.Bind(ModelName("SensorSlope"), "slope", provider.Object));
            Assert.That(recovered.FindChild(device.Context, ModelName("SensorSlope")), Is.Not.Null);
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Accepts only the published positioner Start and Abort argument values.
        /// </summary>
        [TestCase(2, true)]
        [TestCase(255, true)]
        [TestCase(1, false)]
        [TestCase(3, false)]
        [TestCase(254, false)]
        [TestCase(256, false)]
        public async Task PositionerAcceptsOnlyPublishedStartAndAbortModes(int mode, bool valid)
        {
            PadimDevice device = await CreateDeviceAsync($"Positioner{mode}");
            var signal = (ControlSignalObjectState)await device.AddSignalAsync(
                Name("Position"), PadimSignalKind.Control, "CV-001");
            var provider = new Mock<IPadimOperationProvider>(MockBehavior.Strict);
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                device.Context, It.IsAny<NodeId>(), "position", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            if (valid)
            {
                provider.Setup(p => p.AutoAdjustPositionerAsync(
                    "position", (ExecutionModeEnum)mode, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new StatusCode(StatusCodes.Good));
            }
            device.WithAutoAdjustPositioner(signal, "position", provider.Object, authorizer.Object);
            MethodState method = signal.AutoAdjustPositioner!;
            ServiceResult result = await method.OnCallMethod2Async!(
                device.Context, method, signal.NodeId, [Variant.From(mode)], [], CancellationToken.None);
            Assert.That(result.StatusCode, Is.EqualTo(valid ? StatusCodes.Good : StatusCodes.BadInvalidArgument));
            provider.VerifyAll();
            authorizer.VerifyAll();
        }

        /// <summary>
        /// Rejects an unauthorized reset without calling the device provider.
        /// </summary>
        [Test]
        public async Task UnauthorizedOperationDoesNotReachTheDeviceProvider()
        {
            PadimDevice device = await CreateDeviceAsync("UnauthorizedReset");
            var provider = new Mock<IPadimOperationProvider>(MockBehavior.Strict);
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                device.Context, It.IsAny<NodeId>(), "FactoryReset", It.IsAny<CancellationToken>())).ReturnsAsync(false);
            device.WithFactoryReset(provider.Object, authorizer.Object);
            MethodState method = device.State.FactoryReset!;
            ServiceResult result = await method.OnCallMethod2Async!(
                device.Context, method, device.State.NodeId, [Variant.From(1)], [], CancellationToken.None);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            provider.VerifyNoOtherCalls();
            authorizer.VerifyAll();
        }

        /// <summary>
        /// Removes a rejected signal and its bindings before allowing the same name to be reused.
        /// </summary>
        [Test]
        public async Task FailedSignalConfigurationRemovesItsAcquisitionBindings()
        {
            PadimDevice device = await CreateDeviceAsync("SignalRollback");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await device.AddSignalAsync(Name("Signal"), PadimSignalKind.Analog, "TT-001", signal =>
                {
                    device.BindValue(((AnalogSignalObjectState)signal).AnalogSignal!, "temperature", provider.Object);
                    throw new InvalidOperationException("Rejected sample configuration.");
                }));
            Assert.That(device.State.SignalSet_no_!.FindChild(device.Context, Name("Signal")), Is.Null);
            await device.RefreshAsync();
            SignalState replacement = await device.AddSignalAsync(
                Name("Signal"), PadimSignalKind.Analog, "TT-002");
            Assert.That(replacement.SignalTag!.Value, Is.EqualTo("TT-002"));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Preserves active acknowledged conditions across bad-quality acquisition and normal recovery.
        /// </summary>
        [TestCase(1, "Failure")]
        [TestCase(2, "CheckFunction")]
        [TestCase(3, "OffSpec")]
        [TestCase(4, "MaintenanceRequired")]
        public async Task HealthConditionsRetainActiveAcknowledgedAlarmsAndReverseWithoutInventingHealth(
            int category,
            string alarmName)
        {
            PadimDevice device = await CreateDeviceAsync($"Health{category}");
            DateTimeUtc firstTime = new(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            DateTimeUtc secondTime = new(new DateTime(2026, 1, 2, 0, 0, 1, DateTimeKind.Utc));
            DateTimeUtc thirdTime = new(new DateTime(2026, 1, 2, 0, 0, 2, DateTimeKind.Utc));
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            provider.SetupSequence(p => p.ReadAsync("health", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From(category), StatusCodes.Good, firstTime))
                .ReturnsAsync(new DataValue(Variant.From(category), StatusCodes.Good, firstTime))
                .ReturnsAsync(new DataValue(Variant.Null, StatusCodes.BadNoCommunication, secondTime))
                .ReturnsAsync(new DataValue(Variant.From(0), StatusCodes.Good, thirdTime));
            PadimValueBinding binding = device.WithHealthAlarms("health", provider.Object);
            await device.RegisterAsync();
            var children = new List<BaseInstanceState>();
            device.State.GetChildren(device.Context, children);
            AlarmConditionState[] alarms = [.. children.OfType<AlarmConditionState>()];
            Assert.That(alarms, Has.Length.EqualTo(4));
            AlarmConditionState selected = alarms.Single(alarm => alarm.BrowseName.Name == alarmName);
            await binding.RefreshAsync();
            Assert.Multiple(() =>
            {
                Assert.That(selected.ActiveState!.Id!.Value, Is.True);
                Assert.That(selected.AckedState!.Id!.Value, Is.False);
                Assert.That(selected.Retain!.Value, Is.True);
                Assert.That(selected.SourceNode!.Value, Is.EqualTo(device.State.NodeId));
                Assert.That(selected.EventId!.Value.IsNull, Is.False);
                Assert.That(alarms.Count(alarm => alarm.ActiveState!.Id!.Value), Is.EqualTo(1));
            });
            ByteString firstEvent = selected.EventId!.Value;
            await binding.RefreshAsync();
            Assert.That(selected.EventId.Value, Is.EqualTo(firstEvent),
                "Unchanged acquisition must not emit a transition.");
            selected.SetAcknowledgedState(device.Context, true);
            Assert.That(selected.Retain!.Value, Is.True, "An active alarm remains retained after acknowledgement.");
            await binding.RefreshAsync();
            Assert.Multiple(() =>
            {
                Assert.That(selected.ActiveState!.Id!.Value, Is.True);
                Assert.That(selected.Quality!.Value, Is.EqualTo(StatusCodes.BadNoCommunication));
                Assert.That(selected.ActiveState.Timestamp, Is.EqualTo(firstTime));
                Assert.That(selected.EventId.Value, Is.Not.EqualTo(firstEvent));
            });
            await binding.RefreshAsync();
            Assert.Multiple(() =>
            {
                Assert.That(selected.ActiveState!.Id!.Value, Is.False);
                Assert.That(selected.Retain!.Value, Is.False);
                Assert.That(selected.Quality!.Value, Is.EqualTo(StatusCodes.Good));
                Assert.That(device.State.DeviceHealth!.Value, Is.EqualTo(Opc.Ua.Di.DeviceHealthEnumeration.NORMAL));
            });
            provider.VerifyAll();
        }

        /// <summary>
        /// Updates shared enum text and IRDI entries for acquisition and accepted control-parameter writes.
        /// </summary>
        [Test]
        public async Task ControlConfigurationUsesSharedEnumValidationAndPublishedDictionaryRows()
        {
            PadimDevice device = await CreateDeviceAsync("ControlEnums");
            var signal = (ControlSignalObjectState)await device.AddSignalAsync(
                Name("Position"), PadimSignalKind.Control, "CV-002");
            BaseVariableState actuator = (BaseVariableState)signal.ControlSignal!.FindChild(
                device.Context, ModelName("ActuatorType"))!;
            var text = (BaseVariableState)actuator.FindChild(
                device.Context, new QualifiedName(Opc.Ua.BrowseNames.ValueAsText))!;
            var entries = (BaseVariableState)actuator.FindChild(
                device.Context, new QualifiedName(Opc.Ua.BrowseNames.ValueAsDictionaryEntries))!;
            Assert.That(actuator.AccessLevel & AccessLevels.CurrentWrite, Is.Zero,
                "Unbound parameters cannot accept cache-only writes.");
            var acquisition = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            acquisition.SetupSequence(p => p.ReadAsync("actuator", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From(1u)))
                .ReturnsAsync(new DataValue(Variant.From(3u)));
            var writer = new Mock<IPadimWriteProvider>(MockBehavior.Strict);
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                device.Context, actuator.NodeId, "actuator", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            PadimValueBinding binding = device.BindValue(actuator, "actuator", acquisition.Object)
                .WithWrites(writer.Object, authorizer.Object);
            await binding.RefreshAsync();
            NodeId expected = new("0112/2///61987#ABN146#001",
                (ushort)device.Context.NamespaceUris.GetIndex(IRDI.Namespaces.IRDI));
            Assert.Multiple(() =>
            {
                Assert.That(actuator.WrappedValue, Is.EqualTo(Variant.From(1u)));
                Assert.That(text.WrappedValue.GetLocalizedText().Text, Is.EqualTo("quarter-turn rotary"));
                Assert.That(entries.WrappedValue, Is.EqualTo(Variant.From((ArrayOf<NodeId>)[expected])));
            });
            writer.Setup(p => p.WriteAsync("actuator", Variant.From(0u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StatusCode(StatusCodes.Good));
            ServiceResult accepted = await actuator.WriteAttributeAsync(
                device.Context, Attributes.Value, NumericRange.Null, new DataValue(Variant.From(0u)));
            NodeId linear = new("0112/2///61987#ABN145#001",
                (ushort)device.Context.NamespaceUris.GetIndex(IRDI.Namespaces.IRDI));
            Assert.Multiple(() =>
            {
                Assert.That(accepted.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(actuator.WrappedValue, Is.EqualTo(Variant.From(0u)));
                Assert.That(text.WrappedValue.GetLocalizedText().Text, Is.EqualTo("sliding-stem linear"));
                Assert.That(entries.WrappedValue, Is.EqualTo(Variant.From((ArrayOf<NodeId>)[linear])));
            });
            writer.Verify(p => p.WriteAsync("actuator", Variant.From(0u), It.IsAny<CancellationToken>()), Times.Once);
            AttributeWriteResult rejected = await actuator.OnSimpleWriteValueAsync!(
                device.Context, actuator, Variant.From(3u), CancellationToken.None);
            Assert.That(rejected.Result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await binding.RefreshAsync());
            Assert.Multiple(() =>
            {
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(actuator.WrappedValue, Is.EqualTo(Variant.From(0u)));
                Assert.That(text.WrappedValue.GetLocalizedText().Text, Is.EqualTo("sliding-stem linear"));
                Assert.That(entries.WrappedValue, Is.EqualTo(Variant.From((ArrayOf<NodeId>)[linear])));
            });
            writer.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Restores each discrete family's acquired value and derived enum fields after simulation.
        /// </summary>
        [TestCase(PadimSignalKind.Discrete)]
        [TestCase(PadimSignalKind.TwoStateDiscrete)]
        [TestCase(PadimSignalKind.MultiStateDiscrete)]
        public async Task DiscreteSimulationRestoresAcquisitionAndTracksEnumMetadata(PadimSignalKind kind)
        {
            PadimDevice device = await CreateDeviceAsync($"DiscreteSimulation{kind}");
            SignalState signal = await device.AddSignalAsync(Name("Input"), kind, "DI-001");
            string member = kind switch
            {
                PadimSignalKind.TwoStateDiscrete => "TwoStateDiscreteSignal",
                PadimSignalKind.MultiStateDiscrete => "MultiStateDiscreteSignal",
                _ => "DiscreteSignal"
            };
            var value = (BaseVariableState)signal.FindChild(device.Context, ModelName(member))!;
            Variant actual = kind switch
            {
                PadimSignalKind.TwoStateDiscrete => Variant.From(false),
                PadimSignalKind.MultiStateDiscrete => Variant.From(1u),
                _ => Variant.From("acquired")
            };
            Variant simulated = kind switch
            {
                PadimSignalKind.TwoStateDiscrete => Variant.From(true),
                PadimSignalKind.MultiStateDiscrete => Variant.From(2u),
                _ => Variant.From("simulated")
            };
            if (value is MultiStateValueDiscreteState enumeration)
            {
                enumeration.EnumValues!.Value =
                [
                    new EnumValueType { Value = 1, DisplayName = "acquired" },
                    new EnumValueType { Value = 2, DisplayName = "simulated" }
                ];
                var dictionary = (BaseVariableState)value.FindChild(
                    device.Context, new QualifiedName(Opc.Ua.BrowseNames.EnumDictionaryEntries))!;
                dictionary.WrappedValue = Variant.From((MatrixOf<NodeId>)new NodeId[,]
                {
                    { new("acquired-entry", m_manager.InstanceNamespaceIndex) },
                    { new("simulated-entry", m_manager.InstanceNamespaceIndex) }
                });
            }
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            provider.Setup(p => p.ReadAsync("input", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(actual, StatusCodes.UncertainSensorNotAccurate));
            var writer = new Mock<IPadimWriteProvider>();
            writer.Setup(p => p.WriteAsync(It.IsAny<string>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StatusCode(StatusCodes.Good));
            var authorizer = new Mock<IPadimAuthorizer>();
            authorizer.Setup(p => p.AuthorizeAsync(
                It.IsAny<ISystemContext>(), It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            PadimValueBinding binding = device.BindValue(value, "input", provider.Object)
                .WithSimulation(simulated, writer.Object, authorizer.Object);
            await device.RegisterAsync();
            await binding.RefreshAsync();
            var state = (BaseVariableState)value.FindChild(device.Context, ModelName("SimulationState"))!;
            await state.OnSimpleWriteValueAsync!(device.Context, state, Variant.From(true), CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(value.WrappedValue, Is.EqualTo(simulated));
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(((BaseVariableState)value.FindChild(
                    device.Context, ModelName("ActualValue"))!).WrappedValue, Is.EqualTo(actual));
                if (value is MultiStateValueDiscreteState multiState)
                {
                    Assert.That(multiState.ValueAsText!.Value.Text, Is.EqualTo("simulated"));
                }
            });
            await state.OnSimpleWriteValueAsync!(device.Context, state, Variant.From(false), CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(value.WrappedValue, Is.EqualTo(actual));
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
                if (value is MultiStateValueDiscreteState multiState)
                {
                    Assert.That(multiState.ValueAsText!.Value.Text, Is.EqualTo("acquired"));
                }
            });
        }

        /// <summary>
        /// Simulation authorization and provider rejection preserve both configuration and acquired readback.
        /// </summary>
        [TestCase("SimulationValue", false)]
        [TestCase("SimulationState", false)]
        [TestCase("SimulationValue", true)]
        [TestCase("SimulationState", true)]
        public async Task RejectedSimulationWritesPreserveConfigurationAndReadback(string member, bool allowed)
        {
            (PadimDevice device, BaseVariableState value) =
                await CreateAnalogAsync($"SimulationDenied{member}{allowed}");
            var acquisition = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            acquisition.Setup(p => p.ReadAsync("temperature", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From(12f)));
            var writer = new Mock<IPadimWriteProvider>(MockBehavior.Strict);
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            string key = $"temperature/{member}";
            Variant requested = member == "SimulationState" ? Variant.From(true) : Variant.From(20f);
            authorizer.Setup(p => p.AuthorizeAsync(
                device.Context, It.IsAny<NodeId>(), key, It.IsAny<CancellationToken>())).ReturnsAsync(allowed);
            if (allowed)
            {
                writer.Setup(p => p.WriteAsync(key, requested, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new StatusCode(StatusCodes.BadOutOfRange));
            }
            PadimValueBinding binding = device.BindValue(value, "temperature", acquisition.Object)
                .WithSimulation(Variant.From(99f), writer.Object, authorizer.Object);
            await binding.RefreshAsync();
            var parameter = (BaseVariableState)value.FindChild(device.Context, ModelName(member))!;

            AttributeWriteResult result = await parameter.OnSimpleWriteValueAsync!(
                device.Context, parameter, requested, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Result.StatusCode,
                    Is.EqualTo(allowed ? StatusCodes.BadOutOfRange : StatusCodes.BadUserAccessDenied));
                Assert.That(result.SuppressCacheUpdate, Is.True);
                Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(12f)));
                Assert.That(((AnalogSignalVariableState)value).SimulationState!.Value, Is.False);
                Assert.That(((AnalogSignalVariableState)value).SimulationValue!.WrappedValue,
                    Is.EqualTo(Variant.From(99f)));
            });
            writer.VerifyAll();
            writer.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Administration writes require provider acceptance and do not invent an authoritative change timestamp.
        /// </summary>
        [TestCase(true, true)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task AdministrationPreservesProviderOwnershipOfConfigurationAndChangeTime(
            bool allowed,
            bool providerAccepts)
        {
            uint providerStatus = providerAccepts ? StatusCodes.Good : StatusCodes.BadOutOfRange;
            PadimDevice device = await CreateDeviceAsync($"Administration{allowed}{providerStatus}");
            DateTimeUtc changed = new(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            var acquisition = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            acquisition.Setup(p => p.ReadAsync("displayLanguage", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From("en-US")));
            acquisition.Setup(p => p.ReadAsync("dateOfLastChange", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From(changed)));
            var writer = new Mock<IPadimWriteProvider>(MockBehavior.Strict);
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                device.Context, It.IsAny<NodeId>(), "displayLanguage", It.IsAny<CancellationToken>()))
                .ReturnsAsync(allowed);
            if (allowed)
            {
                writer.Setup(p => p.WriteAsync("displayLanguage", Variant.From("de-DE"), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new StatusCode(providerStatus));
            }
            device.WithAdministration(acquisition.Object, writer.Object, authorizer.Object);
            await device.RefreshAsync();
            BaseVariableState language = device.State.DisplayLanguage!;

            AttributeWriteResult result = await language.OnSimpleWriteValueAsync!(
                device.Context, language, Variant.From("de-DE"), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Result.StatusCode,
                    Is.EqualTo(allowed ? providerStatus : StatusCodes.BadUserAccessDenied));
                Assert.That(language.WrappedValue,
                    Is.EqualTo(Variant.From(allowed && providerStatus == StatusCodes.Good ? "de-DE" : "en-US")));
                Assert.That(device.State.DateOfLastChange!.WrappedValue, Is.EqualTo(Variant.From(changed)));
                Assert.That(device.State.DateOfLastChange.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            });
            writer.VerifyAll();
            writer.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Duplicate direct construction cannot dispose or replace the fluent owner's provider pipeline.
        /// </summary>
        [Test]
        public async Task DuplicateDirectHandlePreservesTheExistingFluentOwner()
        {
            var builder = await m_manager.CreatePadimDeviceAsync(Name("CanonicalHandle"));
            PadimDevice device = builder.BindPadim();
            Assert.That(builder.BindPadim(), Is.SameAs(device));
            Assert.Throws<ArgumentException>(() => new PadimDevice(m_manager, builder.Device));
            var signal = (AnalogSignalObjectState)await device.AddSignalAsync(
                Name("Value"), PadimSignalKind.Analog, "TT-OWNER");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            provider.Setup(p => p.ReadAsync("value", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From(42f)));
            PadimValueBinding binding = device.BindValue(signal.AnalogSignal!, "value", provider.Object);

            await binding.RefreshAsync();

            Assert.Multiple(() =>
            {
                Assert.That(builder.BindPadim(), Is.SameAs(device));
                Assert.That(signal.AnalogSignal!.WrappedValue, Is.EqualTo(Variant.From(42f)));
            });
            provider.Verify(p => p.ReadAsync("value", It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Resolves an expected type from the original NodeSet independently of generated identifiers.
        /// </summary>
        private NodeId PublishedType(string name)
        {
            XNamespace ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
            XElement root = XDocument.Load(Path.Combine(
                AppContext.BaseDirectory, "Model", "Opc.Ua.PADIM.NodeSet2.xml")).Root!;
            XElement type = root.Elements(ns + "UAObjectType")
                .Single(element => element.Attribute("BrowseName")!.Value == $"3:{name}");
            string sourceId = type.Attribute("NodeId")!.Value;
            Assert.That(sourceId, Does.StartWith("ns=3;i="));
            Assert.That(NodeId.Parse(sourceId).TryGetValue(out uint identifier), Is.True);
            return new NodeId(identifier, ModelName(name).NamespaceIndex);
        }

        /// <summary>
        /// Resolves a model BrowseName through the live namespace table.
        /// </summary>
        private QualifiedName ModelName(string name)
        {
            return new QualifiedName(name, (ushort)m_manager.SystemContext.NamespaceUris.GetIndex(Namespaces.PADIM));
        }

        /// <summary>
        /// Creates an instance BrowseName in the node manager's namespace.
        /// </summary>
        private QualifiedName Name(string name)
        {
            return new QualifiedName(name, m_manager.InstanceNamespaceIndex);
        }

        /// <summary>
        /// Creates a base device and obtains its canonical provider owner.
        /// </summary>
        private async ValueTask<PadimDevice> CreateDeviceAsync(string name)
        {
            var builder = await m_manager.CreatePadimDeviceAsync(Name(name));
            return builder.BindPadim();
        }

        /// <summary>
        /// Creates an analytical signal whose primary variable accepts Float measurements.
        /// </summary>
        private async ValueTask<(PadimDevice Device, BaseVariableState Value)> CreateAnalogAsync(string name)
        {
            PadimDevice device = await CreateDeviceAsync(name);
            SignalState state = await device.AddSignalAsync(
                Name("Temperature"), PadimSignalKind.Analytical, "temperature");
            return (device, ((AnalyticalSignalState)state).AnalogSignal!);
        }

        /// <summary>The server lifetime shared by the nonparallel runtime fixture.</summary>
        private ServerFixture<StandardServer>? m_fixture;

        /// <summary>The standalone DI address-space owner under test.</summary>
        private DiNodeManager m_manager = null!;
    }
}
