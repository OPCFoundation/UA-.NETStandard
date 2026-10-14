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
using Opc.Ua.AMB.Client;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Client;
using Opc.Ua.GranulateDrying.Client;
using Opc.Ua.GranulateDrying.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PlasticsRubber;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Opc.Ua.GranulateDrying.Tests
{
    /// <summary>
    /// Tests the public runtime and generated client operations over an actual encrypted transport.
    /// </summary>
    [TestFixture(false)]
    [TestFixture(true)]
    [NonParallelizable]
    [Category("GranulateDrying")]
    public sealed partial class GranulateDryingClientServerTests
    {
        /// <summary>
        /// Selects standalone registration or composition on an existing Machinery owner.
        /// </summary>
        public GranulateDryingClientServerTests(bool composed)
        {
            m_host = new GranulateDryingTestHost(composed);
        }

        /// <summary>
        /// Starts the actual endpoint once for this registration route.
        /// </summary>
        [OneTimeSetUp]
        public Task StartAsync() => m_host.StartAsync();

        /// <summary>
        /// Releases the fixture's sessions and owned server resources.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync() => await m_host.DisposeAsync().ConfigureAwait(false);

        /// <summary>
        /// Proves concrete standard types, mandatory placeholders, namespace ownership, and AMB composition.
        /// </summary>
        [Test]
        public async Task BasicDeviceIsRealDiscoverableAndAssetCompatibleAsync()
        {
            var authority = new DryingAuthority();
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.BasicOptions(Name()), authority).ConfigureAwait(false);
            GranulateDryingDeviceClient client = m_host.Client.Device(device.NodeId);
            ArrayOf<GranulateDryingDeviceEntry> discovered = await m_host.Client.DiscoverDevicesAsync()
                .ConfigureAwait(false);
            ArrayOf<GranulateDryingBinClient> bins = await client.BinsAsync().ConfigureAwait(false);
            GranulateDryingReading<double> temperature = await bins[0].Temperature.ReadAsync().ConfigureAwait(false);
            GranulateDryingReading<EUInformation> units =
                await bins[0].Temperature.ReadEngineeringUnitsAsync().ConfigureAwait(false);
            GranulateDryingReading<Range> range = await bins[0].Temperature.ReadRangeAsync().ConfigureAwait(false);
            Assert.That(units.HasValue, Is.True, $"EngineeringUnits: {units.NodeId}, {units.DataValue}");
            Assert.That(range.HasValue, Is.True, $"EURange: {range.NodeId}, {range.DataValue}");
            Assert.Multiple(() =>
            {
                Assert.That(discovered.Span.ToArray().Count(entry => entry.NodeId == device.NodeId), Is.EqualTo(1));
                Assert.That(bins.Count, Is.EqualTo(1));
                Assert.That(device.Bin(1).BrowseName.Name, Is.EqualTo("Bin_001"));
                Assert.That(device.State.TypeDefinitionId, Is.EqualTo(ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.GranulateDryingDeviceType, m_host.Session.NamespaceUris)));
                Assert.That(m_host.Runtime.Context.TypeTable.IsTypeOf(device.State.TypeDefinitionId,
                    ExpandedNodeId.ToNodeId(Di.ObjectTypeIds.DeviceType, m_host.Session.NamespaceUris)), Is.False);
                Assert.That(device.State.Operation!.DeviceMappingNumber!.DataType,
                    Is.EqualTo(Opc.Ua.DataTypeIds.UInt32));
                Assert.That(device.State.Dryer!.DesiccantBeds, Is.Null);
                Assert.That(device.State.UserLogin, Is.Null);
                Assert.That(temperature.HasValue, Is.True);
                Assert.That(temperature.Value, Is.EqualTo(75.0));
                Assert.That(temperature.DataValue.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(temperature.DataValue.SourceTimestamp, Is.EqualTo(DryingAuthority.Time(1)));
                Assert.That(units.Value.UnitId, Is.EqualTo(0x43454C));
                Assert.That(units.Value.NamespaceUri, Is.EqualTo("http://www.opcfoundation.org/UA/units/un/cefact"));
                Assert.That(range.Value.Low, Is.EqualTo(-20));
                Assert.That(range.Value.High, Is.EqualTo(250));
            });
            AssertOwned(device.State);
            IAssetHandle asset = await m_host.Assets.RegisterAssetAsync(
                device.AsNode(), builder => builder.WithConfigurableAssetId(device.Options.SerialNumber))
                .ConfigureAwait(false);
            AmbClient assets = m_host.Session.AssetManagement(m_host.Telemetry);
            ArrayOf<NodeId> registered = await assets.DiscoverAssetsAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(asset.IsRegistered, Is.True);
                Assert.That(registered.Contains(device.NodeId), Is.True);
                Assert.That(m_host.Runtime.Manager.CreateGranulateDryingRuntime(), Is.SameAs(m_host.Runtime));
            });
            Assert.That((await
                assets.ReadAssetAsync(device.NodeId).ConfigureAwait(false)).Identification.ProductInstanceUri,
                Is.EqualTo(device.Options.ProductInstanceUri));
        }

        /// <summary>
        /// Proves one owner per model URI even when standalone hosting internally uses Machinery.
        /// </summary>
        [Test]
        public void AllDependencyNamespacesHaveOneOwner()
        {
            string[] namespaces =
            [
                Di.Namespaces.OpcUaDi,
                Machinery.Namespaces.Machinery,
                PlasticsRubber.Namespaces.PlasticsRubber,
                Namespaces.GranulateDrying
            ];
            foreach (string uri in namespaces)
            {
                Assert.That(m_host.Runtime.Manager.Server.NodeManager.AsyncNodeManagers.Count(
                    owner => owner.NamespaceUris != null && owner.NamespaceUris.Contains(uri)), Is.EqualTo(1), uri);
            }
        }

        /// <summary>
        /// Verifies that each control reaches its exact provider verb and exposes requested, not optimistic ON, status.
        /// </summary>
        [TestCase(GranulateDryingOperation.ActivateDryingProtection, "DryingProtectionStatus", true)]
        [TestCase(GranulateDryingOperation.DeactivateDryingProtection, "DryingProtectionStatus", false)]
        [TestCase(GranulateDryingOperation.ActivateMaterialFeederDryer, "MaterialFeederDryerStatus", true)]
        [TestCase(GranulateDryingOperation.DeactivateMaterialFeederDryer, "MaterialFeederDryerStatus", false)]
        [TestCase(GranulateDryingOperation.ActivateMaterialFeederMachine, "MaterialFeederMachineStatus", true)]
        [TestCase(GranulateDryingOperation.DeactivateMaterialFeederMachine, "MaterialFeederMachineStatus", false)]
        [TestCase(GranulateDryingOperation.ActivateAllFeeders, "AllFeedersStatus", true)]
        [TestCase(GranulateDryingOperation.DeactivateAllFeeders, "AllFeedersStatus", false)]
        [TestCase(GranulateDryingOperation.ActivateTemperatureController, "TemperatureControllerStatus", true)]
        [TestCase(GranulateDryingOperation.DeactivateTemperatureController, "TemperatureControllerStatus", false)]
        [TestCase(GranulateDryingOperation.ActivateDryingProtectionAirVolumeFlowReduction,
            "DryingProtectionAirVolumeFlowMode", true)]
        [TestCase(GranulateDryingOperation.DeactivateDryingProtectionAirVolumeFlowReduction,
            "DryingProtectionAirVolumeFlowMode", false)]
        public async Task BinControlsUseExactTypedOperationsAsync(
            GranulateDryingOperation operation, string response, bool activating)
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            GranulateDryingBinClient bin = (await m_host.Client.Device(device.NodeId).BinsAsync()
                .ConfigureAwait(false))[0];
            FunctionStatusEnumeration requested = activating
                ? FunctionStatusEnumeration.REQUESTED_ON : FunctionStatusEnumeration.REQUESTED_OFF;
            authority.Execute = (_, _) => new ValueTask<GranulateDryingResult>(new GranulateDryingResult(
                StatusCodes.Good, authority.Next(DryingAuthority.Value(
                    GranulateDryingTarget.Bin(1), response, Variant.From(requested), seconds: 2))));

            await CallBinAsync(bin.Proxy, operation).ConfigureAwait(false);

            GranulateDryingCommand command = authority.LastCommand!;
            DataValue status = device.ReadCommittedValue(GranulateDryingTarget.Bin(1), response);
            Assert.Multiple(() =>
            {
                Assert.That(command.Operation, Is.EqualTo(operation));
                Assert.That(command.Target, Is.EqualTo(GranulateDryingTarget.Bin(1)));
                Assert.That(command.SessionId, Is.EqualTo(m_host.Session.SessionId));
                Assert.That(status.WrappedValue.TryGetValue(out FunctionStatusEnumeration actual), Is.True);
                Assert.That(actual, Is.EqualTo(requested));
                Assert.That(status.SourceTimestamp, Is.EqualTo(DryingAuthority.Time(2)));
                Assert.That(device.ControlOwner, Is.EqualTo(m_host.Session.SessionId));
            });
        }

        /// <summary>
        /// Verifies exact generated desiccant-controller operations.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task DesiccantControllerUsesItsOwnStatusAsync(bool activate)
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            GranulateDryingBedClient bed = (await m_host.Client.Device(device.NodeId).DesiccantBedsAsync()
                .ConfigureAwait(false))[0];
            FunctionStatusEnumeration requested = activate
                ? FunctionStatusEnumeration.REQUESTED_ON : FunctionStatusEnumeration.REQUESTED_OFF;
            authority.Execute = (_, _) => new ValueTask<GranulateDryingResult>(new GranulateDryingResult(
                StatusCodes.Good, authority.Next(DryingAuthority.Value(GranulateDryingTarget.Bed(1),
                    "DesiccantBedControllerStatus", Variant.From(requested), seconds: 2))));
            if (activate)
            {
                await bed.Proxy.ActivateDesiccantBedControllerAsync().ConfigureAwait(false);
            }
            else
            {
                await bed.Proxy.DeactivateDesiccantBedControllerAsync().ConfigureAwait(false);
            }
            GranulateDryingReading<FunctionStatusEnumeration> status =
                await bed.DesiccantBedControllerStatus.ReadAsync().ConfigureAwait(false);
            Assert.That(status.Value, Is.EqualTo(requested));
            Assert.That(authority.LastCommand!.Operation, Is.EqualTo(activate
                ? GranulateDryingOperation.ActivateDesiccantBedController
                : GranulateDryingOperation.DeactivateDesiccantBedController));
        }

        /// <summary>
        /// Checks a write's committed timestamp and protects observers and the second authorized controller.
        /// </summary>
        [Test]
        public async Task ObserversRemainConnectedWhileOneAuthorizedSessionControlsAsync()
        {
            var authority = new DryingAuthority();
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.BasicOptions(Name()), authority).ConfigureAwait(false);
            authority.Execute = (command, _) => new ValueTask<GranulateDryingResult>(new GranulateDryingResult(
                StatusCodes.Good, authority.Next(DryingAuthority.Value(
                    command.Target, command.Member, command.Value, seconds: 9))));
            await using ManagedSession observer = await m_host.OpenSessionAsync(false).ConfigureAwait(false);
            await using ManagedSession second = await m_host.OpenSessionAsync(true).ConfigureAwait(false);
            GranulateDryingOperationClient control = await m_host.Client.Device(device.NodeId).OperationAsync()
                .ConfigureAwait(false);
            GranulateDryingOperationClient readOnly = await new GranulateDryingClient(observer, m_host.Telemetry)
                .Device(device.NodeId).OperationAsync().ConfigureAwait(false);
            GranulateDryingOperationClient other = await new GranulateDryingClient(second, m_host.Telemetry)
                .Device(device.NodeId).OperationAsync().ConfigureAwait(false);
            await control.DeviceMappingNumber.WriteAsync(23).ConfigureAwait(false);
            AssertUaFailure(async () => await readOnly.DeviceMappingNumber.WriteAsync(24).ConfigureAwait(false),
                StatusCodes.BadUserAccessDenied);
            AssertUaFailure(async () => await other.DeviceMappingNumber.WriteAsync(25).ConfigureAwait(false),
                StatusCodes.BadUserAccessDenied);
            GranulateDryingReading<uint> observed = await
                readOnly.DeviceMappingNumber.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(observed.Value, Is.EqualTo(23u));
                Assert.That(observed.DataValue.SourceTimestamp, Is.EqualTo(DryingAuthority.Time(9)));
                Assert.That(observer.Connected, Is.True);
                Assert.That(second.Connected, Is.True);
                Assert.That(device.ControlOwner, Is.EqualTo(m_host.Session.SessionId));
            });
        }

        /// <summary>
        /// Verifies an unsupported device operation has its real UA failure and no fabricated response state.
        /// </summary>
        [Test]
        public async Task UnsupportedAndInvalidProviderOutcomesDoNotChangeProcessStateAsync()
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            GranulateDryingBinClient bin = (await m_host.Client.Device(device.NodeId).BinsAsync()
                .ConfigureAwait(false))[0];
            AssertUaFailure(async () => await bin.Proxy.ActivateTemperatureControllerAsync().ConfigureAwait(false),
                StatusCodes.BadNotSupported);
            authority.Execute = (_, _) => new ValueTask<GranulateDryingResult>(
                new GranulateDryingResult(StatusCodes.Good));
            AssertUaFailure(async () => await bin.Proxy.ActivateTemperatureControllerAsync().ConfigureAwait(false),
                StatusCodes.BadInvalidState);
            authority.Execute = (_, _) => new ValueTask<GranulateDryingResult>(
                new GranulateDryingResult(StatusCodes.Good, authority.Next()));
            AssertUaFailure(async () => await bin.Proxy.ActivateTemperatureControllerAsync().ConfigureAwait(false),
                StatusCodes.BadInvalidState);
            Assert.Multiple(() =>
            {
                Assert.That(device.Revision, Is.EqualTo(1));
                Assert.That(device.ControlOwner.IsNull, Is.True);
            });
            Assert.That((await bin.TemperatureControllerStatus.ReadAsync().ConfigureAwait(false)).Value,
                Is.EqualTo(FunctionStatusEnumeration.OFF));
        }

        /// <summary>
        /// Tests revision idempotency, stale rejection, and all-or-nothing validation through the public publisher.
        /// </summary>
        [Test]
        public async Task AbsolutePublicationsPreserveQualityAndRejectPartialInvalidBatchesAsync()
        {
            var authority = new DryingAuthority();
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.BasicOptions(Name()), authority).ConfigureAwait(false);
            GranulateDryingCommit committed = authority.Next(DryingAuthority.Value(
                GranulateDryingTarget.Bin(1), "Temperature/ActualValue", Variant.From(81.25), 17,
                StatusCodes.UncertainLastUsableValue));
            await device.PublishAsync(committed).ConfigureAwait(false);
            await device.PublishAsync(committed).ConfigureAwait(false);
            Assert.That(device.Revision, Is.EqualTo(2));
            GranulateDryingBinClient bin = (await m_host.Client.Device(device.NodeId).BinsAsync()
                .ConfigureAwait(false))[0];
            GranulateDryingReading<double> observed = await bin.Temperature.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(observed.Value, Is.EqualTo(81.25));
                Assert.That(observed.DataValue.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.UncertainLastUsableValue));
                Assert.That(observed.DataValue.SourceTimestamp, Is.EqualTo(DryingAuthority.Time(17)));
            });
            AssertUaFailure(async ()
                => await device.PublishAsync(committed with { Revision = 1 }).ConfigureAwait(false),
                StatusCodes.BadInvalidState);
            AssertUaFailure(async () => await device.PublishAsync(committed with
            {
                Values = [DryingAuthority.Value(GranulateDryingTarget.Bin(1),
                    "Temperature/ActualValue", Variant.From(90.0), 17)]
            }).ConfigureAwait(false), StatusCodes.BadInvalidState);
            GranulateDryingCommit invalid = authority.Next(
                DryingAuthority.Value(GranulateDryingTarget.Bin(1), "Temperature/ActualValue", Variant.From(99.0)),
                DryingAuthority.Value(GranulateDryingTarget.Bin(1), "DryingMode", Variant.From(99)));
            AssertUaFailure(async () => await device.PublishAsync(invalid).ConfigureAwait(false),
                StatusCodes.BadOutOfRange);
            Assert.That((await bin.Temperature.ReadAsync().ConfigureAwait(false)).Value, Is.EqualTo(81.25));
            Assert.That(device.Revision, Is.EqualTo(2));
        }

        /// <summary>
        /// Mutating a structure returned to a local adapter cannot change the authoritative publication.
        /// </summary>
        [Test]
        public async Task CommittedStructuredReadsDoNotExposeSnapshotPayloadsAsync()
        {
            var authority = new DryingAuthority();
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.BasicOptions(Name()), authority).ConfigureAwait(false);
            GranulateDryingCommit committed = authority.Next(DryingAuthority.Value(
                GranulateDryingTarget.Device, "MachineConfiguration/TimeZoneOffset",
                Variant.FromStructure(new TimeZoneDataType { Offset = 60, DaylightSavingInOffset = false }),
                17, StatusCodes.UncertainLastUsableValue));
            await device.PublishAsync(committed).ConfigureAwait(false);
            GranulateDryingConfigurationClient configuration = m_host.Client.Configuration(
                await m_host.Client.Device(device.NodeId).ConfigurationAsync().ConfigureAwait(false));

            DataValue exported = device.ReadCommittedValue(
                GranulateDryingTarget.Device, "MachineConfiguration/TimeZoneOffset");
            Assert.That(exported.WrappedValue.TryGetStructure<TimeZoneDataType>(out TimeZoneDataType? editable),
                Is.True);
            editable!.Offset = 120;
            editable.DaylightSavingInOffset = true;

            DataValue retained = device.ReadCommittedValue(
                GranulateDryingTarget.Device, "MachineConfiguration/TimeZoneOffset");
            Assert.That(retained.WrappedValue.TryGetStructure<TimeZoneDataType>(out TimeZoneDataType? timezone),
                Is.True);
            GranulateDryingReading<TimeZoneDataType> observed = await configuration.TimeZoneOffset.ReadAsync()
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(timezone!.Offset, Is.EqualTo(60));
                Assert.That(timezone.DaylightSavingInOffset, Is.False);
                Assert.That(retained.StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainLastUsableValue));
                Assert.That(retained.SourceTimestamp, Is.EqualTo(DryingAuthority.Time(17)));
                Assert.That(retained.ServerTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
                Assert.That(observed.IsPresent, Is.True);
                Assert.That(observed.HasValue, Is.True);
                Assert.That(observed.Value.Offset, Is.EqualTo(60));
                Assert.That(observed.Value.DaylightSavingInOffset, Is.False);
                Assert.That(observed.DataValue.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.UncertainLastUsableValue));
                Assert.That(observed.DataValue.SourceTimestamp, Is.EqualTo(DryingAuthority.Time(17)));
                Assert.That(device.Revision, Is.EqualTo(committed.Revision));
            });
        }

        /// <summary>
        /// Tests energy-limit bounds before hardware access and exact language/mapping commits.
        /// </summary>
        [Test]
        public async Task ScalarControlsValidateBeforeCallingHardwareAsync()
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            GranulateDryingDeviceClient client = m_host.Client.Device(device.NodeId);
            GranulateDryingOperationClient operation = await client.OperationAsync().ConfigureAwait(false);
            AssertUaFailure(async () => await operation.EnergyConsumptionLimit.WriteAsync(-1).ConfigureAwait(false),
                StatusCodes.BadOutOfRange);
            AssertUaFailure(async () => await operation.EnergyConsumptionLimit.WriteAsync(201).ConfigureAwait(false),
                StatusCodes.BadOutOfRange);
            AssertUaFailure(async ()
                => await operation.EnergyConsumptionLimit.WriteAsync(double.NaN).ConfigureAwait(false),
                StatusCodes.BadOutOfRange);
            Assert.That(authority.LastCommand, Is.Null);
            authority.Execute = (command, _) => new ValueTask<GranulateDryingResult>(
                new GranulateDryingResult(StatusCodes.Good,
                    authority.Next(DryingAuthority.Value(command.Target, command.Member, command.Value, seconds: 8))));
            await operation.EnergyConsumptionLimit.WriteAsync(120).ConfigureAwait(false);
            await client.DisplayLanguage.WriteAsync("de").ConfigureAwait(false);
            await
                operation.MainFailureBehaviour.WriteAsync(MainFailureBehaviourEnumeration.CONTINUE)
                    .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(authority.LastCommand!.Member, Is.EqualTo("Operation/MainFailureBehaviour"));
                Assert.That(authority.LastCommand.Value.TryGetValue(out MainFailureBehaviourEnumeration behavior),
                    Is.True);
                Assert.That(behavior, Is.EqualTo(MainFailureBehaviourEnumeration.CONTINUE));
            });
            Assert.That((await client.DisplayLanguage.ReadAsync().ConfigureAwait(false)).Value, Is.EqualTo("de"));
            Assert.That((await operation.EnergyConsumptionLimit.ReadAsync().ConfigureAwait(false)).Value,
                Is.EqualTo(120));
        }

        /// <summary>
        /// The one energy reset covers device, bin, and bed counters but does not change lifetime consumption.
        /// </summary>
        [Test]
        public async Task EnergyResetRequiresAllSelectedComponentCountersAsync()
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            GranulateDryingOperationClient operation = await m_host.Client.Device(device.NodeId).OperationAsync()
                .ConfigureAwait(false);
            authority.Execute = (_, _) => new ValueTask<GranulateDryingResult>(new GranulateDryingResult(
                StatusCodes.Good, authority.Next(DryingAuthority.Value(
                    GranulateDryingTarget.Device, "Operation/EnergyConsumptionResettable", Variant.From(0.0)))));
            AssertUaFailure(async () => await operation.Proxy.ResetEnergyMeasurementAsync().ConfigureAwait(false),
                StatusCodes.BadInvalidState);
            Assert.That((await operation.EnergyConsumptionResettable.ReadAsync().ConfigureAwait(false)).Value,
                Is.EqualTo(12.0));
            authority.Execute = (_, _) => new ValueTask<GranulateDryingResult>(new GranulateDryingResult(
                StatusCodes.Good, authority.Next(
                    DryingAuthority.Value(GranulateDryingTarget.Device, "Operation/EnergyConsumptionResettable",
                        Variant.From(0.0), seconds: 5),
                    DryingAuthority.Value(GranulateDryingTarget.Bin(1), "EnergyConsumptionResettable",
                        Variant.From(0.0), seconds: 5),
                    DryingAuthority.Value(GranulateDryingTarget.Bed(1), "EnergyConsumptionResettable",
                        Variant.From(0.0), seconds: 5))));
            await operation.Proxy.ResetEnergyMeasurementAsync().ConfigureAwait(false);
            Assert.That((await operation.EnergyConsumptionResettable.ReadAsync().ConfigureAwait(false)).Value, Is.Zero);
            Assert.That(device.ReadCommittedValue(GranulateDryingTarget.Device,
                "Operation/TotalEnergy/PowerConsumption").WrappedValue.TryGetValue(out double lifetime), Is.True);
            Assert.That(lifetime, Is.EqualTo(2000.0));
        }

        /// <summary>
        /// A provider-controlled timer reset publishes the initial remaining duration and its receipt timestamp.
        /// </summary>
        [Test]
        public async Task DryingTimerResetUsesCommittedPresetAndReceiptTimeAsync()
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            GranulateDryingBinClient bin = (await m_host.Client.Device(device.NodeId).BinsAsync()
                .ConfigureAwait(false))[0];
            authority.Execute = (_, _) => new ValueTask<GranulateDryingResult>(new GranulateDryingResult(
                StatusCodes.Good, authority.Next(
                    DryingAuthority.Value(GranulateDryingTarget.Bin(1), "RemainingMaterialDryingTime",
                        Variant.From(60000.0), seconds: 12),
                    DryingAuthority.Value(GranulateDryingTarget.Bin(1), "ResetDryingTimeResponseStatus",
                        Variant.From(DryingAuthority.Time(12)), seconds: 12))));
            await bin.Proxy.ResetDryingTimeAsync().ConfigureAwait(false);
            Assert.That((await bin.RemainingMaterialDryingTime.ReadAsync().ConfigureAwait(false)).Value,
                Is.EqualTo(60000.0));
            Assert.That((await bin.ResetDryingTimeResponseStatus.ReadAsync().ConfigureAwait(false)).Value,
                Is.EqualTo(DryingAuthority.Time(12)));
        }

        /// <summary>
        /// User-login arguments retain their exact semantics and do not grant UA control permissions.
        /// </summary>
        [Test]
        public async Task HmiLoginAndLogoutUseGeneratedTypedArgumentsAsync()
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            GranulateDryingUserClient user = (await m_host.Client.Device(device.NodeId).UsersAsync()
                .ConfigureAwait(false))!;
            authority.Execute = (command, _) => new ValueTask<GranulateDryingResult>(new GranulateDryingResult(
                StatusCodes.Good, authority.Next() with
                {
                    User = command.Operation == GranulateDryingOperation.LoginMachineUser
                        ? command.User : new GranulateDryingUser { IsPresent = false }
                }));
            await user.Proxy.LoginMachineUserAsync("operator-id", "Operator", "card-id", "level", "role", "en")
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(authority.LastCommand!.User!.CardUid, Is.EqualTo("card-id"));
                Assert.That(authority.LastCommand.User.UserLevel, Is.EqualTo("level"));
                Assert.That(authority.LastCommand.User.UserRole, Is.EqualTo("role"));
                Assert.That(authority.LastCommand.User.Language, Is.EqualTo("en"));
            });
            Assert.That((await user.IsPresent.ReadAsync().ConfigureAwait(false)).Value, Is.True);
            Assert.That((await user.Id.ReadAsync().ConfigureAwait(false)).Value, Is.EqualTo("operator-id"));
            await user.Proxy.LogoutMachineUserAsync().ConfigureAwait(false);
            Assert.That((await user.IsPresent.ReadAsync().ConfigureAwait(false)).Value, Is.False);
        }

        /// <summary>
        /// Maintenance keeps its attributed reset, preserved lifetime counter, and stack-backed read-only file.
        /// </summary>
        [Test]
        public async Task MaintenanceInstructionsAndAttributedResetWorkEndToEndAsync()
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            GranulateDryingMaintenanceClient maintenance = (await m_host.Client.Device(device.NodeId).MaintenanceAsync()
                .ConfigureAwait(false))[0];
            ByteString instructions = await maintenance.ReadInstructionsAsync().ConfigureAwait(false);
            Assert.That(instructions.Span.ToArray(), Is.EqualTo(new byte[] { 1, 3, 5, 7 }));
            AssertUaFailure(async () => await maintenance.ReadInstructionsAsync(3).ConfigureAwait(false),
                StatusCodes.BadEncodingLimitsExceeded);
            Assert.That(device.Maintenance(1).MaintenanceInstructions!.OpenCount!.Value, Is.Zero);
            AssertUaFailure(async () => await maintenance.Proxy.ResetAsync().ConfigureAwait(false),
                StatusCodes.BadNotSupported);
            authority.Execute = (_, _) => new ValueTask<GranulateDryingResult>(new GranulateDryingResult(
                StatusCodes.Good, authority.Next(
                    DryingAuthority.Value(GranulateDryingTarget.Maintenance(1), "Status",
                        Variant.From(MaintenanceStatusEnumeration.NOT_DUE), seconds: 3),
                    DryingAuthority.Value(GranulateDryingTarget.Maintenance(1), "RemainingInterval",
                        Variant.From(240.0), seconds: 3))));
            await maintenance.Proxy.GDDMaintenanceResetAsync("Technician", "Machine manufacturer", "M-17")
                .ConfigureAwait(false);
            Assert.That(authority.LastCommand!.Maintenance,
                Is.EqualTo(new GranulateDryingMaintenanceReset("Technician", "Machine manufacturer", "M-17")));
            Assert.That((await maintenance.TotalOperation.ReadAsync().ConfigureAwait(false)).Value, Is.EqualTo(10000));
            Assert.That((await
                maintenance.RemainingInterval.ReadEngineeringUnitsAsync().ConfigureAwait(false)).Value.UnitId,
                Is.EqualTo(0x485552));
        }

        /// <summary>
        /// Cancels in-flight hardware work before disposal waits for it.
        /// </summary>
        [Test]
        public async Task DisposalCancelsPendingHardwareWithoutPublishingAnOptimisticStateAsync()
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            authority.Execute = async (_, ct) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                throw new InvalidOperationException("Cancellation must finish the pending operation.");
            };
            GranulateDryingBinClient bin = (await m_host.Client.Device(device.NodeId).BinsAsync()
                .ConfigureAwait(false))[0];
            Task operation = bin.Proxy.ActivateTemperatureControllerAsync().AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await device.DisposeAsync().ConfigureAwait(false);
            AssertUaFailure(async () => await operation.ConfigureAwait(false), StatusCodes.BadShutdown);
            Assert.That((await bin.TemperatureControllerStatus.ReadAsync().ConfigureAwait(false)).Value,
                Is.EqualTo(FunctionStatusEnumeration.OFF));
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await device.PublishAsync(authority.Next()).ConfigureAwait(false));
            await device.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Failed or cancelled creation leaves no published device and permits a same-name retry.
        /// </summary>
        [Test]
        public async Task CreationFailureAndCancellationRollBackThePublicGraphAsync()
        {
            string name = Name();
            var authority = new DryingAuthority
            {
                ReadFailure = new ServiceResultException(StatusCodes.BadResourceUnavailable)
            };
            GranulateDryingDeviceOptions options = GranulateDryingTestHost.BasicOptions(name);
            AssertUaFailure(async () => await m_host.CreateAsync(options, authority).ConfigureAwait(false),
                StatusCodes.BadResourceUnavailable);
            Assert.That((await m_host.Client.DiscoverDevicesAsync().ConfigureAwait(false))
                .FindIndex(entry => entry.BrowseName.Name == name), Is.EqualTo(-1));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await m_host.CreateAsync(options, new DryingAuthority(), cancellationToken: cancelled.Token)
                    .ConfigureAwait(false));
            GranulateDryingDevice device = await m_host.CreateAsync(options,
                new DryingAuthority()).ConfigureAwait(false);
            Assert.That(device.Options.BrowseName, Is.EqualTo(name));
            AssertUaFailure(async () => await m_host.CreateAsync(options, new DryingAuthority()).ConfigureAwait(false),
                StatusCodes.BadBrowseNameDuplicated);
        }

        /// <summary>
        /// Dispatches only through the generated BinTypeClient methods under test.
        /// </summary>
        private static ValueTask CallBinAsync(BinTypeClient bin, GranulateDryingOperation operation)
        {
            return operation switch
            {
                GranulateDryingOperation.ActivateDryingProtection => bin.ActivateDryingProtectionAsync(),
                GranulateDryingOperation.DeactivateDryingProtection => bin.DeactivateDryingProtectionAsync(),
                GranulateDryingOperation.ActivateMaterialFeederDryer => bin.ActivateMaterialFeederDryerAsync(),
                GranulateDryingOperation.DeactivateMaterialFeederDryer => bin.DeactivateMaterialFeederDryerAsync(),
                GranulateDryingOperation.ActivateMaterialFeederMachine => bin.ActivateMaterialFeederMachineAsync(),
                GranulateDryingOperation.DeactivateMaterialFeederMachine => bin.DeactivateMaterialFeederMachineAsync(),
                GranulateDryingOperation.ActivateAllFeeders => bin.ActivateAllFeedersAsync(),
                GranulateDryingOperation.DeactivateAllFeeders => bin.DeactivateAllFeedersAsync(),
                GranulateDryingOperation.ActivateTemperatureController => bin.ActivateTemperatureControllerAsync(),
                GranulateDryingOperation.DeactivateTemperatureController => bin.DeactivateTemperatureControllerAsync(),
                GranulateDryingOperation.ActivateDryingProtectionAirVolumeFlowReduction =>
                    bin.ActivateDryingProtectionAirVolumeFlowReductionAsync(),
                GranulateDryingOperation.DeactivateDryingProtectionAirVolumeFlowReduction =>
                    bin.DeactivateDryingProtectionAirVolumeFlowReductionAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        /// <summary>
        /// Checks exact UA failures rather than merely accepting any exception.
        /// </summary>
        private static void AssertUaFailure(Func<Task> operation, StatusCode code)
        {
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(operation)!;
            Assert.That(error.StatusCode, Is.EqualTo(code));
        }

        /// <summary>
        /// Checks namespace ownership through the publicly exposed generated graph.
        /// </summary>
        private void AssertOwned(NodeState root)
        {
            var visited = new HashSet<NodeState>(RefEqualityComparer.Default);
            var pending = new Stack<NodeState>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                NodeState node = pending.Pop();
                if (!visited.Add(node))
                {
                    continue;
                }
                Assert.That(node.NodeId.NamespaceIndex, Is.EqualTo(m_host.Runtime.InstanceNamespaceIndex));
                Assert.That(m_host.Runtime.Manager.FindPredefinedNode(node.NodeId), Is.SameAs(node));
                var children = new List<BaseInstanceState>();
                node.GetChildren(m_host.Runtime.Context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
        }

        /// <summary>
        /// Creates unique generic device names on the shared fixture endpoint.
        /// </summary>
        private string Name() => "Dryer" + Interlocked.Increment(ref m_nextDevice);

        /// <summary>
        /// The public runtime/client test fixture.
        /// </summary>
        private readonly GranulateDryingTestHost m_host;

        /// <summary>
        /// The next fixture-owned device suffix.
        /// </summary>
        private int m_nextDevice;
    }

    /// <summary>
    /// Client-only discovery and ownership contracts; no server fixture or endpoint is started.
    /// </summary>
    [TestFixture]
    public sealed class GranulateDryingClientBoundaryTests
    {
        [Test]
        public async Task GddFacadeEnforcesProxyOwnershipAndClientSpecificGuardsAsync()
        {
            var peer = new GranulateDryingClientPeer();
            var foreign = new GranulateDryingClientPeer();
            Assert.That(Assert.Throws<ArgumentNullException>(() => new GranulateDryingClient(null!, peer.Telemetry))!
                .ParamName, Is.EqualTo("session"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new GranulateDryingClient(peer.Session.Object,
                null!))!
                .ParamName, Is.EqualTo("telemetry"));
            Assert.That(Assert.Throws<ArgumentException>(() => peer.Client.Device(NodeId.Null))!
                .ParamName, Is.EqualTo("nodeId"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => peer.Client.Parameter(null!))!.ParamName,
                Is.EqualTo("proxy"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => peer.Client.Energy(null!))!.ParamName,
                Is.EqualTo("proxy"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => peer.Client.Configuration(null!))!.ParamName,
                Is.EqualTo("proxy"));
            NodeId id = GranulateDryingClientPeer.DeviceId;
            Assert.That(Assert.Throws<ArgumentException>(() => peer.Client.Parameter(
                new MonitoredParameterTypeClient(foreign.Session.Object, id, peer.Telemetry)))!.ParamName,
                    Is.EqualTo("proxy"));
            Assert.That(Assert.Throws<ArgumentException>(() => peer.Client.Energy(
                new EnergyTypeClient(foreign.Session.Object, id, peer.Telemetry)))!.ParamName, Is.EqualTo("proxy"));
            Assert.That(Assert.Throws<ArgumentException>(() => peer.Client.Configuration(
                new MachineConfigurationTypeClient(foreign.Session.Object, id, peer.Telemetry)))!.ParamName,
                    Is.EqualTo("proxy"));
            peer.VerifyNoServices();
            foreign.VerifyNoServices();
            NodeId actual = peer.ExpectPath(id, peer.Common("ActualValue"));
            peer.ExpectRead(actual, GranulateDryingClientPeer.Sample(Variant.From(73.25)));
            GranulateDryingClientPeer.AssertReading(await peer.Client.Parameter(new MonitoredParameterTypeClient(
                peer.Session.Object, id, peer.Telemetry)).ActualValue.ReadAsync().ConfigureAwait(false), actual, 73.25);
            NodeId power = peer.ExpectPath(id, peer.Common("ActualPower"));
            peer.ExpectRead(power, GranulateDryingClientPeer.Sample(Variant.From(4.75)));
            GranulateDryingClientPeer.AssertReading(await peer.Client.Energy(new EnergyTypeClient(
                peer.Session.Object, id, peer.Telemetry)).ActualPower.ReadAsync().ConfigureAwait(false), power, 4.75);
            NodeId name = peer.ExpectPath(id, peer.Common("UserMachineName"));
            peer.ExpectRead(name, GranulateDryingClientPeer.Sample(Variant.From("Dryer North")));
            GranulateDryingClientPeer.AssertReading(await peer.Client.Configuration(new MachineConfigurationTypeClient(
                peer.Session.Object, id, peer.Telemetry)).UserMachineName.ReadAsync().ConfigureAwait(false), name,
                    "Dryer North");
            Assert.That(peer.Client.Device(id).Proxy.Session, Is.SameAs(peer.Session.Object));
            Assert.That(peer.Client.Session, Is.SameAs(peer.Session.Object));
            peer.Verify();
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task GddUnsupportedModelsOrMissingDefaultRootAvoidDiscoveryServicesAsync(bool gdd, bool machinery)
        {
            var peer = new GranulateDryingClientPeer(includeGdd: gdd, includeMachinery: machinery);
            GranulateDryingDeviceClient device = peer.Client.Device(GranulateDryingClientPeer.DeviceId);
            Assert.Multiple(() =>
            {
                Assert.That(device.Proxy.ObjectId, Is.EqualTo(GranulateDryingClientPeer.DeviceId));
                Assert.That(device.Proxy.Session, Is.SameAs(peer.Session.Object));
                Assert.That(peer.Client.IsSupported, Is.EqualTo(gdd));
            });
            Assert.That((await peer.Client.DiscoverDevicesAsync().ConfigureAwait(false)).Count, Is.Zero);
            peer.VerifyNoServices();
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void GddDiscoveryRejectsNonpositiveBoundsBeforeServices(int maximum)
        {
            var peer = new GranulateDryingClientPeer();
            ArgumentOutOfRangeException? error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await peer.Client.DiscoverDevicesAsync(maximumNodes: maximum).ConfigureAwait(false));
            Assert.That(error!.ParamName, Is.EqualTo("maximumNodes"));
            peer.VerifyNoServices();
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task GddDiscoveryCountsUniqueSeenNodesIncludingRootAsync(int maximum)
        {
            var peer = new GranulateDryingClientPeer();
            NodeId root = new("root", 1);
            NodeId container = new("collection", 1);
            NodeId device = new("dryer/vendor-4", 1);
            NodeId vendor = new(127u, 2);
            NodeId expectedType = peer.Type(ObjectTypeIds.GranulateDryingDeviceType);
            peer.ExpectBrowse(root,
                GranulateDryingClientPeer.Reference(container, Opc.Ua.ObjectTypeIds.FolderType, "Collection"),
                GranulateDryingClientPeer.Reference(root, Opc.Ua.ObjectTypeIds.FolderType, "Cycle"),
                GranulateDryingClientPeer.Reference(container, Opc.Ua.ObjectTypeIds.FolderType, "Duplicate"));
            peer.ExpectType(Opc.Ua.ObjectTypeIds.FolderType, expectedType, false);
            peer.ExpectBrowse(container,
                GranulateDryingClientPeer.Reference(root, Opc.Ua.ObjectTypeIds.FolderType, "Back"),
                GranulateDryingClientPeer.Reference(device, vendor, "Vendor dryer"),
                GranulateDryingClientPeer.Reference(device, expectedType, "Duplicate"));
            if (maximum == 2)
            {
                GranulateDryingClientPeer.AssertFailure(async () =>
                    await peer.Client.DiscoverDevicesAsync(root, maximum).ConfigureAwait(false),
                    StatusCodes.BadEncodingLimitsExceeded);
                Assert.That(peer.Cache.Invocations, Has.Count.EqualTo(1), "Over-limit nodes must not be classified.");
            }
            else
            {
                peer.ExpectType(vendor, expectedType, true);
                ArrayOf<GranulateDryingDeviceEntry> found = await peer.Client.DiscoverDevicesAsync(root, maximum)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(found.Count, Is.EqualTo(1));
                    Assert.That(found[0].NodeId, Is.EqualTo(device));
                    Assert.That(found[0].TypeDefinition, Is.EqualTo(vendor));
                    Assert.That(found[0].BrowseName, Is.EqualTo(new QualifiedName("Vendor dryer", 1)));
                    Assert.That(found[0].DisplayName, Is.EqualTo(new LocalizedText("en", "Vendor dryer")));
                });
            }
            peer.Verify();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GddDiscoveryTreatsExplicitRootAsCollectionAndSkipsSelfReferencesAsync(bool selfReference)
        {
            var peer = new GranulateDryingClientPeer();
            NodeId root = GranulateDryingClientPeer.DeviceId;
            peer.ExpectBrowse(root, selfReference
                ? [GranulateDryingClientPeer.Reference(root, peer.Type(ObjectTypeIds.GranulateDryingDeviceType))]
                : []);
            Assert.That((await peer.Client.DiscoverDevicesAsync(root, 1).ConfigureAwait(false)).Count, Is.Zero);
            Assert.That(peer.Cache.Invocations, Is.Empty);
            peer.Verify();
        }

        [Test]
        public async Task GddDiscoveryPreservesSubtypesAndSkipsInvalidReferencesAsync()
        {
            var peer = new GranulateDryingClientPeer();
            NodeId root = peer.Type(Machinery.ObjectIds.Machines);
            NodeId expectedType = peer.Type(ObjectTypeIds.GranulateDryingDeviceType);
            NodeId vendor = new("DryerVariant", 2);
            NodeId baseDevice = new("base-device", 1);
            NodeId vendorDevice = new("vendor-device", 1);
            NodeId unknownTypeContainer = new("container/no-type", 1);
            NodeId unresolvedTypeContainer = new("container/unknown-uri", 1);
            peer.ExpectBrowse(root,
                GranulateDryingClientPeer.Reference(baseDevice, expectedType, "Base"),
                GranulateDryingClientPeer.Reference(vendorDevice, vendor, "Vendor"),
                GranulateDryingClientPeer.Reference(vendorDevice, expectedType, "Ignored duplicate"),
                GranulateDryingClientPeer.Reference(NodeId.Null, expectedType, "Null ID"),
                GranulateDryingClientPeer.Reference(new ExpandedNodeId(98u, "urn:unresolvable"), expectedType,
                    "Foreign ID"),
                GranulateDryingClientPeer.Reference(unknownTypeContainer, NodeId.Null, "Unknown container"),
                GranulateDryingClientPeer.Reference(unresolvedTypeContainer,
                    new ExpandedNodeId(99u, "urn:unresolvable"), "Unresolved container"));
            peer.ExpectType(expectedType, expectedType, true);
            peer.ExpectType(vendor, expectedType, true);
            peer.ExpectBrowse(unknownTypeContainer);
            peer.ExpectBrowse(unresolvedTypeContainer);
            ArrayOf<GranulateDryingDeviceEntry> found = await peer.Client.DiscoverDevicesAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(found.ToArray()!.Select(entry => entry.NodeId), Is.EqualTo(new[] { baseDevice,
                    vendorDevice }));
                Assert.That(found.ToArray()!.Select(entry => entry.TypeDefinition), Is.EqualTo(new[] { expectedType,
                    vendor }));
                Assert.That(found.ToArray()!.Select(entry => entry.BrowseName),
                    Is.EqualTo(new[] { new QualifiedName("Base", 1), new QualifiedName("Vendor", 1) }));
                Assert.That(found[0].DisplayName, Is.EqualTo(new LocalizedText("en", "Base")));
                Assert.That(found[1].DisplayName, Is.EqualTo(new LocalizedText("en", "Vendor")));
            });
            peer.Verify();
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(1)]
        public void GddBrowseFailuresRemainObservable(int count)
        {
            var peer = new GranulateDryingClientPeer();
            NodeId root = GranulateDryingClientPeer.DeviceId;
            peer.ExpectBrowseResults(root, Enumerable.Repeat(
                new BrowseResult { StatusCode = StatusCodes.BadUserAccessDenied }, count).ToArray().ToArrayOf());
            GranulateDryingClientPeer.AssertFailure(async () =>
                await peer.Client.DiscoverDevicesAsync(root).ConfigureAwait(false),
                count == 1 ? StatusCodes.BadUserAccessDenied : StatusCodes.BadUnexpectedError);
            Assert.That(peer.Cache.Invocations, Is.Empty);
            peer.Verify();
        }

        [TestCase("BeforeBrowse")]
        [TestCase("Browse")]
        [TestCase("TypeCache")]
        public void GddBrowseFailuresAndCancellationRemainObservable(string cancelAt)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new GranulateDryingClientPeer(token: cancellation.Token);
            NodeId root = GranulateDryingClientPeer.DeviceId;
            NodeId expectedType = peer.Type(ObjectTypeIds.GranulateDryingDeviceType);
            if (cancelAt == "BeforeBrowse")
            {
                cancellation.Cancel();
            }
            else
            {
                peer.ExpectBrowseResults(root,
                [
                    new BrowseResult
                    {
                        References = [GranulateDryingClientPeer.Reference(new NodeId("candidate", 1), expectedType)]
                    }
                ], cancelAt == "Browse" ? cancellation.Cancel : null);
                if (cancelAt == "TypeCache")
                {
                    peer.ExpectType(expectedType, expectedType, true, beforeReturn: cancellation.Cancel);
                }
            }
            OperationCanceledException? error = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await peer.Client.DiscoverDevicesAsync(root,
                    cancellationToken: cancellation.Token).ConfigureAwait(false));
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            if (cancelAt == "BeforeBrowse")
            {
                peer.VerifyNoServices();
            }
            peer.Verify();
        }

        [TestCase("Bins")]
        [TestCase("DesiccantBeds")]
        [TestCase("Maintenance")]
        public async Task GddPlaceholdersFilterTypesAndKeepFirstValidReferencesAsync(string group)
        {
            var peer = new GranulateDryingClientPeer();
            (NodeId parent, NodeId expectedType, Type proxyType) = PlaceholderGroup(peer, group);
            NodeId vendor = new("vendor-" + group, 2);
            NodeId first = new("first", 1);
            NodeId derived = new("derived", 1);
            NodeId wrong = new("wrong-first", 1);
            NodeId recovered = new("missing-type-first", 1);
            NodeId recoveredUri = new("unresolvable-type-first", 1);
            peer.ExpectBrowse(parent,
                GranulateDryingClientPeer.Reference(first, expectedType),
                GranulateDryingClientPeer.Reference(derived, vendor),
                GranulateDryingClientPeer.Reference(wrong, Opc.Ua.ObjectTypeIds.FolderType),
                GranulateDryingClientPeer.Reference(wrong, expectedType),
                GranulateDryingClientPeer.Reference(first, vendor),
                GranulateDryingClientPeer.Reference(NodeId.Null, expectedType),
                GranulateDryingClientPeer.Reference(new ExpandedNodeId(25u, "urn:not-loaded"), vendor),
                GranulateDryingClientPeer.Reference(recovered, NodeId.Null),
                GranulateDryingClientPeer.Reference(recovered, expectedType),
                GranulateDryingClientPeer.Reference(recoveredUri, new ExpandedNodeId(26u, "urn:not-loaded")),
                GranulateDryingClientPeer.Reference(recoveredUri, vendor));
            peer.ExpectType(expectedType, expectedType, true, count: 2);
            peer.ExpectType(vendor, expectedType, true, count: 2);
            peer.ExpectType(Opc.Ua.ObjectTypeIds.FolderType, expectedType, false);
            ObjectTypeClient[] proxies = await PlaceholdersAsync(
                peer.Client.Device(GranulateDryingClientPeer.DeviceId), group, peer.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(proxies.Select(proxy => proxy.ObjectId), Is.EqualTo(new[] { first, derived, recovered,
                    recoveredUri }));
                Assert.That(proxies.Select(proxy => proxy.GetType()), Is.All.EqualTo(proxyType));
                Assert.That(proxies.Select(proxy => proxy.Session), Is.All.SameAs(peer.Session.Object));
            });
            peer.Verify();
        }

        [TestCase("Bins")]
        [TestCase("DesiccantBeds")]
        [TestCase("Maintenance")]
        public async Task GddPlaceholderGroupsDistinguishMandatoryAndOptionalAbsenceAsync(string group)
        {
            var peer = new GranulateDryingClientPeer();
            PlaceholderGroup(peer, group, false);
            GranulateDryingDeviceClient device = peer.Client.Device(GranulateDryingClientPeer.DeviceId);
            if (group == "Bins")
            {
                GranulateDryingClientPeer.AssertFailure(async () =>
                    await device.BinsAsync().ConfigureAwait(false), StatusCodes.BadNotSupported);
            }
            else
            {
                Assert.That(await PlaceholdersAsync(device, group, peer.Token).ConfigureAwait(false), Is.Empty);
            }
            Assert.That(peer.Cache.Invocations, Is.Empty);
            peer.Verify();
        }

        [TestCase("Dryer")]
        [TestCase("Operation")]
        [TestCase("Identification")]
        [TestCase("MachineConfiguration")]
        public void GddMandatoryGeneratedChildrenReportNotSupported(string missing)
        {
            var peer = new GranulateDryingClientPeer();
            peer.ExpectChild(GranulateDryingClientPeer.DeviceId,
                missing == "Identification" ? peer.Di(missing) : peer.Gdd(missing), false);
            GranulateDryingDeviceClient device = peer.Client.Device(GranulateDryingClientPeer.DeviceId);
            GranulateDryingClientPeer.AssertFailure(async () =>
            {
                switch (missing)
                {
                    case "Dryer": await device.BinsAsync().ConfigureAwait(false); break;
                    case "Operation": await device.MaintenanceAsync().ConfigureAwait(false); break;
                    case "Identification": await device.IdentificationValuesAsync().ConfigureAwait(false); break;
                    case "MachineConfiguration": await device.ConfigurationAsync().ConfigureAwait(false); break;
                    default: throw new ArgumentOutOfRangeException(nameof(missing));
                }
            }, StatusCodes.BadNotSupported);
            Assert.That(peer.Cache.Invocations, Is.Empty);
            peer.Verify();
        }

        private static (NodeId Parent, NodeId ExpectedType, Type ProxyType) PlaceholderGroup(
            GranulateDryingClientPeer peer, string group, bool present = true)
        {
            if (group == "Maintenance")
            {
                NodeId operation = peer.ExpectChild(GranulateDryingClientPeer.DeviceId, peer.Gdd("Operation"));
                return (peer.ExpectChild(operation, peer.Gdd("MaintenanceInformation"), present),
                    peer.Type(ObjectTypeIds.GDDMaintenanceType), typeof(GDDMaintenanceTypeClient));
            }
            NodeId dryer = peer.ExpectChild(GranulateDryingClientPeer.DeviceId, peer.Gdd("Dryer"));
            return (peer.ExpectChild(dryer, peer.Gdd(group), present),
                peer.Type(group == "Bins" ? ObjectTypeIds.BinType : ObjectTypeIds.DesiccantBedType),
                group == "Bins" ? typeof(BinTypeClient) : typeof(DesiccantBedTypeClient));
        }

        private static async Task<ObjectTypeClient[]> PlaceholdersAsync(
            GranulateDryingDeviceClient device, string group, CancellationToken token)
        {
            return group switch
            {
                "Bins" => (await device.BinsAsync(token).ConfigureAwait(false)).ToArray()!
                    .Select(bin => (ObjectTypeClient)bin.Proxy).ToArray(),
                "DesiccantBeds" => (await device.DesiccantBedsAsync(token).ConfigureAwait(false)).ToArray()!
                    .Select(bed => (ObjectTypeClient)bed.Proxy).ToArray(),
                "Maintenance" => (await device.MaintenanceAsync(token).ConfigureAwait(false)).ToArray()!
                    .Select(maintenance => (ObjectTypeClient)maintenance.Proxy).ToArray(),
                _ => throw new ArgumentOutOfRangeException(nameof(group))
            };
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task GddHostingPreservesSingletonRegistrationsAsync(bool existingFactory, bool existingDelegate)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new GranulateDryingClientPeer(token: cancellation.Token);
            var failure = new InvalidOperationException("The supplied managed-session factory failed.");
            int acquisitions = 0;
            Task<ManagedSession> AcquireAsync(CancellationToken token)
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                acquisitions++;
                return Task.FromException<ManagedSession>(failure);
            }
            var suppliedFactory = new GranulateDryingClientFactory(AcquireAsync, peer.Telemetry);
            Func<CancellationToken, Task<GranulateDryingClient>> suppliedDelegate = token =>
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                return Task.FromResult(peer.Client);
            };
            var services = new ServiceCollection();
            services.AddSingleton(peer.Telemetry);
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(AcquireAsync);
            if (existingFactory)
            {
                services.AddSingleton(suppliedFactory);
            }
            if (existingDelegate)
            {
                services.AddSingleton(suppliedDelegate);
            }
            var builder = new Mock<IOpcUaClientBuilder>(MockBehavior.Strict);
            builder.SetupGet(value => value.Services).Returns(services);
            Assert.That(builder.Object.AddGranulateDryingClient(), Is.SameAs(builder.Object));
            Assert.That(builder.Object.AddGranulateDryingClient(), Is.SameAs(builder.Object));
            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                GranulateDryingClientFactory factory = provider.GetRequiredService<GranulateDryingClientFactory>();
                var create = provider.GetRequiredService<Func<CancellationToken, Task<GranulateDryingClient>>>();
                Assert.Multiple(() =>
                {
                    Assert.That(services.Count(item => item.ServiceType == typeof(GranulateDryingClientFactory)),
                        Is.EqualTo(1));
                    Assert.That(services.Count(item => item.ServiceType == typeof(Func<CancellationToken,
                        Task<GranulateDryingClient>>)),
                        Is.EqualTo(1));
                    Assert.That(services.Single(item
                        => item.ServiceType == typeof(GranulateDryingClientFactory)).Lifetime,
                        Is.EqualTo(ServiceLifetime.Singleton));
                    Assert.That(services.Single(item => item.ServiceType == typeof(Func<CancellationToken,
                        Task<GranulateDryingClient>>)).Lifetime,
                        Is.EqualTo(ServiceLifetime.Singleton));
                    Assert.That(provider.GetRequiredService<GranulateDryingClientFactory>(), Is.SameAs(factory));
                    Assert.That(provider.GetRequiredService<Func<CancellationToken, Task<GranulateDryingClient>>>(),
                        Is.SameAs(create));
                });
                if (existingFactory)
                {
                    Assert.That(factory, Is.SameAs(suppliedFactory));
                }
                if (existingDelegate)
                {
                    Assert.That(create, Is.SameAs(suppliedDelegate));
                    GranulateDryingClient facade = await create(cancellation.Token).ConfigureAwait(false);
                    Assert.That(facade, Is.SameAs(peer.Client));
                    NodeId language = peer.ExpectPath(GranulateDryingClientPeer.DeviceId, peer.Gdd("DisplayLanguage"));
                    peer.ExpectRead(language, GranulateDryingClientPeer.Sample(Variant.From("fr-FR")));
                    GranulateDryingClientPeer.AssertReading(await facade.Device(GranulateDryingClientPeer.DeviceId)
                        .DisplayLanguage.ReadAsync(cancellation.Token).ConfigureAwait(false), language, "fr-FR");
                }
                else
                {
                    Assert.That(create.Target, Is.SameAs(factory));
                    Assert.That(Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await create(cancellation.Token).ConfigureAwait(false)), Is.SameAs(failure));
                }
            }
            Assert.That(acquisitions, Is.EqualTo(existingDelegate ? 0 : 1));
            peer.Verify();
        }

        [TestCase("Fault", false)]
        [TestCase("Fault", true)]
        [TestCase("Throw", false)]
        [TestCase("CancelledTask", true)]
        public void GddFactoryValidatesDependenciesAndForwardsTokensAndFailures(string outcome, bool preCancelled)
        {
            using var cancellation = new CancellationTokenSource();
            if (preCancelled)
            {
                cancellation.Cancel();
            }
            var peer = new GranulateDryingClientPeer();
            var failure = new InvalidOperationException("Session acquisition was refused.");
            int calls = 0;
            Task<ManagedSession> AcquireAsync(CancellationToken token)
            {
                calls++;
                Assert.That(token, Is.EqualTo(cancellation.Token));
                return outcome switch
                {
                    "Fault" => Task.FromException<ManagedSession>(failure),
                    "Throw" => throw failure,
                    "CancelledTask" => Task.FromCanceled<ManagedSession>(token),
                    _ => throw new ArgumentOutOfRangeException(nameof(outcome))
                };
            }
            Assert.That(Assert.Throws<ArgumentNullException>(() => new GranulateDryingClientFactory(null!,
                peer.Telemetry))!
                .ParamName, Is.EqualTo("sessionFactory"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new GranulateDryingClientFactory(AcquireAsync,
                null!))!
                .ParamName, Is.EqualTo("telemetry"));
            Assert.That(Assert.Throws<ArgumentNullException>(()
                => GranulateDryingClientHostingExtensions.AddGranulateDryingClient(null!))!
                .ParamName, Is.EqualTo("builder"));
            var factory = new GranulateDryingClientFactory(AcquireAsync, peer.Telemetry);
            if (outcome == "CancelledTask")
            {
                TaskCanceledException? error = Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await factory.CreateAsync(cancellation.Token).ConfigureAwait(false));
                Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            }
            else
            {
                Assert.That(Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await factory.CreateAsync(cancellation.Token).ConfigureAwait(false)), Is.SameAs(failure));
            }
            Assert.That(calls, Is.EqualTo(1));
            peer.VerifyNoServices();
        }

        [Test]
        public async Task GddFacadeRetainsCallerOwnedSessionAfterContainerDisposalAsync()
        {
            var peer = new GranulateDryingClientPeer();
            var services = new ServiceCollection();
            services.AddSingleton(_ => new GranulateDryingClient(peer.Session.Object, peer.Telemetry));
            NodeId language = peer.ExpectPath(GranulateDryingClientPeer.DeviceId, 2, peer.Gdd("DisplayLanguage"));
            peer.ExpectRead(language, GranulateDryingClientPeer.Sample(Variant.From("sv-SE")));
            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                GranulateDryingClient client = provider.GetRequiredService<GranulateDryingClient>();
                Assert.That(client.Session, Is.SameAs(peer.Client.Session));
                GranulateDryingClientPeer.AssertReading(await client.Device(GranulateDryingClientPeer.DeviceId)
                    .DisplayLanguage.ReadAsync().ConfigureAwait(false), language, "sv-SE");
            }
            peer.ExpectWrite(language, value => Assert.That(value, Is.EqualTo(Variant.From("de-DE"))),
                StatusCodes.GoodClamped);
            Assert.That(await peer.Client.Device(GranulateDryingClientPeer.DeviceId).DisplayLanguage.WriteAsync("de-DE")
                .ConfigureAwait(false), Is.EqualTo(StatusCodes.GoodClamped));
            peer.Verify();
        }

        [TestCase("Bins")]
        [TestCase("DesiccantBeds")]
        [TestCase("Maintenance")]
        public async Task GddPresentEmptyPlaceholderGroupsBrowseOnceWithoutInventingChildrenAsync(string group)
        {
            var peer = new GranulateDryingClientPeer();
            (NodeId parent, _, _) = PlaceholderGroup(peer, group);
            peer.ExpectBrowse(parent);
            Assert.That(await PlaceholdersAsync(peer.Client.Device(GranulateDryingClientPeer.DeviceId),
                group, peer.Token).ConfigureAwait(false), Is.Empty);
            Assert.That(peer.Cache.Invocations, Is.Empty);
            peer.Verify();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GddPlaceholderCancellationReachesBrowseAndTypeCache(bool duringTypeCheck)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new GranulateDryingClientPeer(token: cancellation.Token);
            (NodeId parent, NodeId expectedType, _) = PlaceholderGroup(peer, "Bins");
            NodeId vendor = new("vendor/bin", 2);
            peer.ExpectBrowseResults(parent,
            [
                new BrowseResult
                {
                    References =
                    [
                        GranulateDryingClientPeer.Reference(new NodeId("candidate-1", 1), vendor),
                        GranulateDryingClientPeer.Reference(new NodeId("candidate-2", 1), expectedType)
                    ]
                }
            ], duringTypeCheck ? null : cancellation.Cancel);
            if (duringTypeCheck)
            {
                peer.ExpectType(vendor, expectedType, true, beforeReturn: cancellation.Cancel);
            }
            OperationCanceledException? error = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await peer.Client.Device(GranulateDryingClientPeer.DeviceId).BinsAsync(cancellation.Token)
                    .ConfigureAwait(false));
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(peer.Cache.Invocations, Has.Count.EqualTo(duringTypeCheck ? 1 : 0),
                "Cancellation must stop before classifying the second child.");
            peer.Verify();
        }
    }
}
