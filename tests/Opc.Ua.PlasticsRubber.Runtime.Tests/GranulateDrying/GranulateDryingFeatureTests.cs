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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GranulateDrying.Client;
using Opc.Ua.GranulateDrying.Server;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.GranulateDrying.Tests
{
    public sealed partial class GranulateDryingClientServerTests
    {
        /// <summary>
        /// Checks every independent bin option against the published model's member catalogue.
        /// </summary>
        [TestCase(GranulateDryingBinFeatures.MaterialIdentification, "MaterialIdentification")]
        [TestCase(GranulateDryingBinFeatures.BatchNumber, "BatchNumber")]
        [TestCase(GranulateDryingBinFeatures.MaterialDryingTime, "MaterialDryingTime")]
        [TestCase(GranulateDryingBinFeatures.RemainingMaterialDryingTime, "RemainingMaterialDryingTime")]
        [TestCase(GranulateDryingBinFeatures.ExpiredDryingTime, "ExpiredDryingTime")]
        [TestCase(GranulateDryingBinFeatures.TemperatureReduction, "TemperatureReduction")]
        [TestCase(GranulateDryingBinFeatures.DryingProtection, "ActivateDryingProtection")]
        [TestCase(GranulateDryingBinFeatures.MaterialAvailableDryer, "MaterialAvailableDryer")]
        [TestCase(GranulateDryingBinFeatures.MaterialAvailableMachine, "MaterialAvailableMachine")]
        [TestCase(GranulateDryingBinFeatures.MaterialFeeder, "ActivateMaterialFeederDryer")]
        [TestCase(GranulateDryingBinFeatures.MachineFeeder, "ActivateMaterialFeederMachine")]
        [TestCase(GranulateDryingBinFeatures.AllFeeders, "ActivateAllFeeders")]
        [TestCase(GranulateDryingBinFeatures.TemperatureController, "ActivateTemperatureController")]
        [TestCase(GranulateDryingBinFeatures.DustRemovalPulses, "DustRemovalPulses")]
        [TestCase(GranulateDryingBinFeatures.DryingLevel, "DryingLevel")]
        [TestCase(GranulateDryingBinFeatures.AirFlowMode, "AirFlowMode")]
        [TestCase(GranulateDryingBinFeatures.IntervalTimeOn, "IntervalTimeOn")]
        [TestCase(GranulateDryingBinFeatures.IntervalTimeOff, "IntervalTimeOff")]
        [TestCase(GranulateDryingBinFeatures.AirFlowInterval, "AirFlowMode")]
        [TestCase(GranulateDryingBinFeatures.InitialFillingActive, "InitialFillingActive")]
        [TestCase(GranulateDryingBinFeatures.ResetDryingTime, "ResetDryingTimeResponseStatus")]
        [TestCase(GranulateDryingBinFeatures.AirSource, "AirSource")]
        [TestCase(GranulateDryingBinFeatures.AirFlow, "AirFlow")]
        [TestCase(GranulateDryingBinFeatures.TotalAirConsumption, "TotalAirConsumption")]
        [TestCase(GranulateDryingBinFeatures.DewPoint, "DewPoint")]
        [TestCase(GranulateDryingBinFeatures.DryingProtectionAirVolumeFlowReduction,
            "ActivateDryingProtectionAirVolumeFlowReduction")]
        [TestCase(GranulateDryingBinFeatures.DryAirTemperature, "DryAirTemperature")]
        [TestCase(GranulateDryingBinFeatures.AmbientAirTemperature, "AmbientAirTemperature")]
        [TestCase(GranulateDryingBinFeatures.TopTemperature, "TopTemperature")]
        [TestCase(GranulateDryingBinFeatures.ConeTemperature, "ConeTemperature")]
        [TestCase(GranulateDryingBinFeatures.CylinderTemperature, "CylinderTemperature")]
        [TestCase(GranulateDryingBinFeatures.LooseningAid, "LooseningAid")]
        [TestCase(GranulateDryingBinFeatures.TotalEnergy, "TotalEnergy")]
        [TestCase(GranulateDryingBinFeatures.EnergyConsumptionResettable, "EnergyConsumptionResettable")]
        public async Task EveryBinOptionCanBeSelectedIndependentlyAsync(
            GranulateDryingBinFeatures feature, string expectedMember)
        {
            GranulateDryingDeviceOptions options = GranulateDryingTestHost.BasicOptions(Name()) with
            {
                Bins = [new GranulateDryingBinOptions { Features = feature }]
            };
            GranulateDryingDevice device = await m_host.CreateAsync(options,
                new DryingAuthority()).ConfigureAwait(false);
            BinState bin = device.Bin(1);
            Assert.Multiple(() =>
            {
                Assert.That(Find(bin, expectedMember), Is.Not.Null);
                Assert.That(bin.Temperature, Is.Not.Null);
                Assert.That(bin.DryingMode, Is.Not.Null);
                Assert.That(bin.BatchNumber != null, Is.EqualTo(feature == GranulateDryingBinFeatures.BatchNumber));
                Assert.That(bin.MaterialIdentification != null,
                    Is.EqualTo(feature == GranulateDryingBinFeatures.MaterialIdentification));
                Assert.That(device.State.Dryer!.DesiccantBeds, Is.Null);
            });
            if (feature == GranulateDryingBinFeatures.AirFlowInterval)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(bin.IntervalTimeOn, Is.Not.Null);
                    Assert.That(bin.IntervalTimeOff, Is.Not.Null);
                });
            }
        }

        /// <summary>
        /// Checks every desiccant-bed capability independently of other optional hardware.
        /// </summary>
        [TestCase(GranulateDryingBedFeatures.DewPoint, "DewPoint")]
        [TestCase(GranulateDryingBedFeatures.AirFlow, "AirFlow")]
        [TestCase(GranulateDryingBedFeatures.TemperatureOfDesiccantBed, "TemperatureOfDesiccantBed")]
        [TestCase(GranulateDryingBedFeatures.TemperatureDesiccantBedOut, "TemperatureDesiccantBedOut")]
        [TestCase(GranulateDryingBedFeatures.TemperatureBackToDesiccantBed, "TemperatureBackToDesiccantBed")]
        [TestCase(GranulateDryingBedFeatures.EnergyConsumptionResettable, "EnergyConsumptionResettable")]
        [TestCase(GranulateDryingBedFeatures.TotalEnergy, "TotalEnergy")]
        [TestCase(GranulateDryingBedFeatures.Controller, "DesiccantBedControllerStatus")]
        public async Task EveryBedOptionCanBeSelectedIndependentlyAsync(
            GranulateDryingBedFeatures feature, string expectedMember)
        {
            GranulateDryingDeviceOptions options = GranulateDryingTestHost.BasicOptions(Name()) with
            {
                DesiccantBeds = [new GranulateDryingBedOptions { Features = feature }]
            };
            GranulateDryingDevice device = await m_host.CreateAsync(options,
                new DryingAuthority()).ConfigureAwait(false);
            ArrayOf<GranulateDryingBedClient> beds =
                await m_host.Client.Device(device.NodeId).DesiccantBedsAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(beds.Count, Is.EqualTo(1));
                Assert.That(device.Bed(1).BrowseName.Name, Is.EqualTo("DesiccantBed_001"));
                Assert.That(Find(device.Bed(1), expectedMember), Is.Not.Null);
                Assert.That(device.Bin(1).AirFlow, Is.Null);
            });
        }

        /// <summary>
        /// Checks every optional device feature without forcing a full device configuration.
        /// </summary>
        [TestCase(GranulateDryingDeviceFeatures.DisplayLanguage, "DisplayLanguage")]
        [TestCase(GranulateDryingDeviceFeatures.Users, "UserLogin")]
        [TestCase(GranulateDryingDeviceFeatures.IdentifyDevice, "Operation/IdentifyDevice")]
        [TestCase(GranulateDryingDeviceFeatures.ResetAllErrors, "Operation/ResetAllErrors")]
        [TestCase(GranulateDryingDeviceFeatures.ResetErrorById, "Operation/ResetErrorById")]
        [TestCase(GranulateDryingDeviceFeatures.ResetEnergyMeasurement, "Operation/ResetEnergyMeasurement")]
        [TestCase(GranulateDryingDeviceFeatures.MainFailureBehaviour, "Operation/MainFailureBehaviour")]
        [TestCase(GranulateDryingDeviceFeatures.TotalEnergy, "Operation/TotalEnergy")]
        [TestCase(GranulateDryingDeviceFeatures.EnergyConsumptionResettable, "Operation/EnergyConsumptionResettable")]
        [TestCase(GranulateDryingDeviceFeatures.EnergyConsumptionLimit, "Operation/EnergyConsumptionLimit")]
        [TestCase(GranulateDryingDeviceFeatures.OperationMode, "MachineryBuildingBlocks/MachineryOperationMode")]
        [TestCase(GranulateDryingDeviceFeatures.ConfigurationPages, "MachineConfiguration/GetPage")]
        public async Task EveryDeviceOptionCanBeSelectedIndependentlyAsync(
            GranulateDryingDeviceFeatures feature, string expectedMember)
        {
            GranulateDryingDeviceOptions options = GranulateDryingTestHost.BasicOptions(Name()) with { Features =
                feature };
            GranulateDryingDevice device = await m_host.CreateAsync(options,
                new DryingAuthority()).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(Find(device.State, expectedMember), Is.Not.Null);
                Assert.That(device.State.Operation!.MaintenanceInformation, Is.Null);
                Assert.That(device.State.Dryer!.DesiccantBeds, Is.Null);
                Assert.That(device.Bin(1).ActivateAllFeeders, Is.Null);
            });
        }

        /// <summary>
        /// Independent optional variable types and unit metadata are not inferred from display labels.
        /// </summary>
        [Test]
        public async Task MetadataUsesNormativeTypesReferencesUnitsAndStatusAsync()
        {
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), new DryingAuthority(full: true)).ConfigureAwait(false);
            GranulateDryingDeviceClient client = m_host.Client.Device(device.NodeId);
            GranulateDryingBinClient bin = (await client.BinsAsync().ConfigureAwait(false))[0];
            GranulateDryingBedClient bed = (await client.DesiccantBedsAsync().ConfigureAwait(false))[0];
            Assert.Multiple(() =>
            {
                Assert.That(device.Bin(1).MaterialDryingTime!.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Duration));
                Assert.That(device.Bin(1).MaterialDryingTime!.ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasProperty));
                Assert.That(device.Bin(1).ResetDryingTimeResponseStatus!.DataType,
                    Is.EqualTo(Opc.Ua.DataTypeIds.UtcTime));
                Assert.That(device.Bin(1).DryingMode!.DataType, Is.EqualTo(ExpandedNodeId.ToNodeId(
                    DataTypeIds.DryingModeEnumeration, m_host.Session.NamespaceUris)));
                Assert.That(device.State.Operation!.ActiveErrors!.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(device.State.Identification!.DeviceClass!.Value, Is.EqualTo("Granulate Drying Device"));
            });
            Assert.That((await bin.AirFlow.ReadEngineeringUnitsAsync().ConfigureAwait(false)).Value.UnitId,
                Is.EqualTo(0x4D5148));
            Assert.That((await bed.AirFlow.ReadEngineeringUnitsAsync().ConfigureAwait(false)).Value.UnitId,
                Is.EqualTo(0x473531));
            Assert.That((await bin.TotalAirConsumption.ReadEngineeringUnitsAsync().ConfigureAwait(false)).Value.UnitId,
                Is.EqualTo(0x4D5451));
            Assert.That((await bin.TemperatureReduction.ReadEngineeringUnitsAsync().ConfigureAwait(false)).Value.UnitId,
                Is.EqualTo(0x4B454C));
            Assert.That((await
                bed.EnergyConsumptionResettable.ReadEngineeringUnitsAsync().ConfigureAwait(false)).Value.UnitId,
                Is.EqualTo(0x4B5748));
            GranulateDryingReading<double> unavailable = await bin.ConeTemperature.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(unavailable.IsPresent, Is.True);
                Assert.That(unavailable.DataValue.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadWaitingForInitialData));
            });
        }

        /// <summary>
        /// Proves optional absence is distinct from a present but unavailable measurement.
        /// </summary>
        [Test]
        public async Task MissingOptionalGroupsRemainAbsentToTypedClientsAsync()
        {
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.BasicOptions(Name()), new DryingAuthority()).ConfigureAwait(false);
            GranulateDryingDeviceClient client = m_host.Client.Device(device.NodeId);
            GranulateDryingBinClient bin = (await client.BinsAsync().ConfigureAwait(false))[0];
            GranulateDryingReading<double> airflow = await bin.AirFlow.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(airflow.IsPresent, Is.False);
                Assert.That(airflow.HasValue, Is.False);
                Assert.That(airflow.NodeId.IsNull, Is.True);
                Assert.That(airflow.DataValue.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            });
            Assert.That((await client.DesiccantBedsAsync().ConfigureAwait(false)).Count, Is.Zero);
            Assert.That(await client.UsersAsync().ConfigureAwait(false), Is.Null);
            Assert.That((await client.MaintenanceAsync().ConfigureAwait(false)).Count, Is.Zero);
            AssertUaFailure(async () => await client.DisplayLanguage.WriteAsync("en").ConfigureAwait(false),
                StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// A persistently disabled remote interface stays readable but globally nonwritable/nonexecutable.
        /// </summary>
        [Test]
        public async Task RemoteDisableDoesNotDisconnectObserversAndCannotBeBypassedAsync()
        {
            var authority = new DryingAuthority(full: true);
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.FullOptions(Name()), authority).ConfigureAwait(false);
            await device.PublishAsync(authority.Next() with { RemoteControlEnabled = false }).ConfigureAwait(false);
            GranulateDryingDeviceClient client = m_host.Client.Device(device.NodeId);
            GranulateDryingOperationClient operation = await client.OperationAsync().ConfigureAwait(false);
            Assert.That((await operation.RemoteControlOpcUa.ReadAsync().ConfigureAwait(false)).Value, Is.False);
            AssertUaFailure(async () => await operation.DeviceMappingNumber.WriteAsync(7).ConfigureAwait(false),
                StatusCodes.BadNotWritable);
            AssertUaFailure(async () => await operation.Proxy.IdentifyDeviceAsync().ConfigureAwait(false),
                StatusCodes.BadNotExecutable);
            Assert.Multiple(() =>
            {
                Assert.That(device.State.Operation!.IdentifyDevice!.Executable, Is.False);
                Assert.That(device.State.Operation.DeviceMappingNumber!.AccessLevel,
                    Is.EqualTo(AccessLevels.CurrentRead));
                Assert.That(device.ControlOwner.IsNull, Is.True);
                Assert.That(m_host.Session.Connected, Is.True);
                Assert.That(authority.LastCommand, Is.Null);
            });
        }

        /// <summary>
        /// Closing the actual control session releases its per-device lease for another authorized session.
        /// </summary>
        [Test]
        public async Task ClosingControllerReleasesOnlyItsDeviceLeaseAsync()
        {
            var authority = new DryingAuthority();
            GranulateDryingDevice device = await m_host.CreateAsync(
                GranulateDryingTestHost.BasicOptions(Name()), authority).ConfigureAwait(false);
            authority.Execute = (command, _) => new ValueTask<GranulateDryingResult>(
                new GranulateDryingResult(StatusCodes.Good,
                    authority.Next(DryingAuthority.Value(command.Target, command.Member, command.Value))));
            ManagedSession temporary = await m_host.OpenSessionAsync(true).ConfigureAwait(false);
            try
            {
                GranulateDryingOperationClient first = await new GranulateDryingClient(temporary, m_host.Telemetry)
                    .Device(device.NodeId).OperationAsync().ConfigureAwait(false);
                await first.DeviceMappingNumber.WriteAsync(41).ConfigureAwait(false);
                Assert.That(device.ControlOwner, Is.EqualTo(temporary.SessionId));
            }
            finally
            {
                await temporary.DisposeAsync().ConfigureAwait(false);
            }
            GranulateDryingOperationClient next = await m_host.Client.Device(device.NodeId).OperationAsync()
                .ConfigureAwait(false);
            await next.DeviceMappingNumber.WriteAsync(42).ConfigureAwait(false);
            Assert.That(device.ControlOwner, Is.EqualTo(m_host.Session.SessionId));
        }

        /// <summary>
        /// Automatic parameter limits reuse OPC 40083's signed lower-band semantics.
        /// </summary>
        [TestCase(188.0)]
        [TestCase(189.0)]
        public async Task CommonParameterBindingUsesSignedBandsAndCommittedWritesAsync(double observedSetpoint)
        {
            GranulateDryingDeviceOptions options = GranulateDryingTestHost.BasicOptions(Name()) with
            {
                Bins =
                [
                    new GranulateDryingBinOptions
                    {
                        OptionalMembers =
                        [
                            "Temperature/SetValue", "Temperature/LowerTolerance", "Temperature/UpperTolerance",
                            "Temperature/LowerTolerance2", "Temperature/UpperTolerance2", "Temperature/Status",
                            "Temperature/ResetMonitoring", "Temperature/MonitoringSensitivity"
                        ]
                    }
                ]
            };
            var authority = new DryingAuthority();
            GranulateDryingTarget binTarget = GranulateDryingTarget.Bin(1);
            authority.Current = authority.Current with
            {
                Values = authority.Current.Values.Span.ToArray()
                    .Where(value => value.Member != "Temperature/ActualValue").ToArrayOf().AddItems(
                        (ArrayOf<GranulateDryingValue>)
                        [
                            DryingAuthority.Value(binTarget, "Temperature/ActualValue", Variant.From(181.0)),
                            DryingAuthority.Value(binTarget, "Temperature/SetValue", Variant.From(190.0)),
                            DryingAuthority.Value(binTarget, "Temperature/LowerTolerance", Variant.From(-8.0)),
                            DryingAuthority.Value(binTarget, "Temperature/UpperTolerance", Variant.From(8.0)),
                            DryingAuthority.Value(binTarget, "Temperature/LowerTolerance2", Variant.From(-12.0)),
                            DryingAuthority.Value(binTarget, "Temperature/UpperTolerance2", Variant.From(12.0))
                        ])
            };
            var parameters = new Mock<IMonitoredParameterProvider>(MockBehavior.Strict);
            PlasticsRubberRequestContext? observed = null;
            parameters.Setup(provider => provider.WriteSettingAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), MonitoredParameterSetting.SetValue, 188.0,
                    It.IsAny<CancellationToken>()))
                .Returns((PlasticsRubberRequestContext request, MonitoredParameterSetting _, double value,
                    CancellationToken _) =>
                {
                    observed = request;
                    authority.Next(DryingAuthority.Value(
                        binTarget, "Temperature/SetValue", Variant.From(observedSetpoint), 7));
                    return default;
                });
            GranulateDryingDevice device = await m_host.CreateAsync(
                options, authority, new GranulateDryingCommonProviders { Parameters = parameters.Object })
                .ConfigureAwait(false);
            GranulateDryingBinClient bin = (await m_host.Client.Device(device.NodeId).BinsAsync()
                .ConfigureAwait(false))[0];
            GranulateDryingParameterClient parameter = m_host.Client.Parameter(
                (await bin.TemperatureParametersAsync().ConfigureAwait(false))!);
            Assert.That((await parameter.Status.ReadAsync().ConfigureAwait(false)).Value, Is.EqualTo((ushort)4),
                "181 is below the first signed lower band 190-8, but above the outer band 190-12");
            await parameter.SetValue.WriteAsync(188).ConfigureAwait(false);
            GranulateDryingReading<double> written = await parameter.SetValue.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(written.Value, Is.EqualTo(observedSetpoint));
                Assert.That(written.DataValue.SourceTimestamp, Is.EqualTo(DryingAuthority.Time(7)));
                Assert.That(device.Bin(1).Temperature!.SetValue!.Value, Is.EqualTo(observedSetpoint));
                Assert.That(device.Bin(1).Temperature!.SetValue!.Timestamp, Is.EqualTo(DryingAuthority.Time(7)));
                Assert.That(observed!.Value.TargetId, Is.EqualTo(device.Bin(1).Temperature!.NodeId));
                Assert.That((observed.Value.SystemContext as ISessionSystemContext)!.SessionId,
                    Is.EqualTo(m_host.Session.SessionId));
            });
            AssertUaFailure(async () => await parameter.LowerTolerance.WriteAsync(3).ConfigureAwait(false),
                StatusCodes.BadOutOfRange);
            AssertUaFailure(async () => await parameter.MonitoringSensitivity.WriteAsync(3).ConfigureAwait(false),
                StatusCodes.BadOutOfRange);
            parameters.Verify(provider => provider.WriteSettingAsync(It.IsAny<PlasticsRubberRequestContext>(),
                MonitoredParameterSetting.SetValue, 188.0, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Common setting adapters preserve the actual standard member path at every authorization check.
        /// </summary>
        [TestCase(MonitoredParameterSetting.Minimum, "MinValue")]
        [TestCase(MonitoredParameterSetting.Maximum, "MaxValue")]
        public async Task CommonSettingAuthorizationUsesTheDeclaredMemberAsync(
            MonitoredParameterSetting setting,
            string member)
        {
            GranulateDryingTarget target = GranulateDryingTarget.Bin(1);
            string path = "Temperature/" + member;
            GranulateDryingDeviceOptions options = GranulateDryingTestHost.BasicOptions(Name()) with
            {
                Bins = [new GranulateDryingBinOptions { OptionalMembers = [path] }]
            };
            var authority = new DryingAuthority();
            var policy = new Mock<IGranulateDryingAuthorization>(MockBehavior.Strict);
            policy.Setup(value => value.CanControl(It.IsAny<ISystemContext>())).Returns(true);
            policy.Setup(value => value.AuthorizeAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<GranulateDryingCommand>(), It.IsAny<CancellationToken>()))
                .Returns((ISystemContext _, GranulateDryingCommand command, CancellationToken _) =>
                    new ValueTask<StatusCode>(command.Target == target && command.Member == path
                        ? StatusCodes.Good : StatusCodes.BadUserAccessDenied));
            var parameters = new Mock<IMonitoredParameterProvider>(MockBehavior.Strict);
            parameters.Setup(value => value.WriteSettingAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), setting, 5.0, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    authority.Next(DryingAuthority.Value(target, path, Variant.From(5.0), 7));
                    return default;
                });
            GranulateDryingDevice device = await m_host.Runtime.CreateDeviceAsync(
                options, authority.Provider, policy.Object,
                new GranulateDryingCommonProviders { Parameters = parameters.Object }).ConfigureAwait(false);
            GranulateDryingBinClient bin = (await m_host.Client.Device(device.NodeId).BinsAsync()
                .ConfigureAwait(false))[0];
            GranulateDryingParameterClient parameter = m_host.Client.Parameter(
                (await bin.TemperatureParametersAsync().ConfigureAwait(false))!);
            GranulateDryingVariable<double> variable = setting == MonitoredParameterSetting.Minimum
                ? parameter.MinValue : parameter.MaxValue;

            await variable.WriteAsync(5).ConfigureAwait(false);

            GranulateDryingReading<double> actual = await variable.ReadAsync().ConfigureAwait(false);
            Assert.That(actual.Value, Is.EqualTo(5));
            Assert.That(actual.DataValue.SourceTimestamp, Is.EqualTo(DryingAuthority.Time(7)));
            parameters.Verify(value => value.WriteSettingAsync(
                It.IsAny<PlasticsRubberRequestContext>(), setting, 5.0, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Device page reads and clock changes reuse the common provider without changing the host clock.
        /// </summary>
        [Test]
        public async Task CommonConfigurationUsesTypedDeviceClockAndPageOperationsAsync()
        {
            var authority = new DryingAuthority(full: true);
            var provider = new Mock<IMachineConfigurationProvider>(MockBehavior.Strict);
            ByteString image = ByteString.From(new byte[] { 0x89, 0x50, 0x4E, 0x47, 13, 10, 26, 10 });
            provider.Setup(value => value.GetPageAsync(It.IsAny<PlasticsRubberRequestContext>(), "status",
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ByteString>(image));
            var timezone = new TimeZoneDataType { Offset = 60, DaylightSavingInOffset = false };
            provider.Setup(value => value.SetMachineTimeAsync(It.IsAny<PlasticsRubberRequestContext>(),
                    DryingAuthority.Time(31), It.IsAny<TimeZoneDataType>(), It.IsAny<CancellationToken>()))
                .Returns((PlasticsRubberRequestContext _, DateTimeUtc _, TimeZoneDataType zone, CancellationToken _) =>
                {
                    authority.Next(DryingAuthority.Value(GranulateDryingTarget.Device,
                        "MachineConfiguration/TimeZoneOffset", Variant.FromStructure(zone), 31));
                    return default;
                });
            GranulateDryingDevice device = await m_host.CreateAsync(GranulateDryingTestHost.FullOptions(Name()),
                authority,
                    new GranulateDryingCommonProviders { Configuration = provider.Object }).ConfigureAwait(false);
            GranulateDryingConfigurationClient configuration = m_host.Client.Configuration(
                await m_host.Client.Device(device.NodeId).ConfigurationAsync().ConfigureAwait(false));
            Assert.That((await configuration.Proxy.GetPageAsync("status").ConfigureAwait(false)).Span.ToArray(),
                Is.EqualTo(image.Span.ToArray()));
            Assert.That(device.ControlOwner.IsNull, Is.True, "read-only image retrieval does not claim control");
            await configuration.Proxy.SetMachineTimeAsync(DryingAuthority.Time(31), timezone).ConfigureAwait(false);
            GranulateDryingReading<TimeZoneDataType> observed = await configuration.TimeZoneOffset.ReadAsync()
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(observed.Value.Offset, Is.EqualTo(60));
                Assert.That(observed.DataValue.SourceTimestamp, Is.EqualTo(DryingAuthority.Time(31)));
            });
        }

        /// <summary>
        /// An invalid interval-airflow request is rejected before invoking the device.
        /// </summary>
        [Test]
        public async Task IntervalAirflowRequiresBothSelectedIntervalVariablesAsync()
        {
            var authority = new DryingAuthority();
            GranulateDryingDeviceOptions options = GranulateDryingTestHost.BasicOptions(Name()) with
            {
                Bins = [new GranulateDryingBinOptions { Features = GranulateDryingBinFeatures.AirFlowMode }]
            };
            GranulateDryingDevice device = await m_host.CreateAsync(options, authority).ConfigureAwait(false);
            GranulateDryingBinClient bin = (await m_host.Client.Device(device.NodeId).BinsAsync()
                .ConfigureAwait(false))[0];
            AssertUaFailure(async () => await bin.AirFlowMode.WriteAsync(AirFlowModeEnumeration.INTERVAL)
                .ConfigureAwait(false), StatusCodes.BadNotSupported);
            Assert.That(authority.LastCommand, Is.Null);
        }

        /// <summary>
        /// Placeholder cardinality, feature masks, identifiers, and unknown optional paths are not silently repaired.
        /// </summary>
        [Test]
        public async Task InvalidSelectionsFailWithoutPublishingDevicesAsync()
        {
            GranulateDryingDeviceOptions basic = GranulateDryingTestHost.BasicOptions(Name());
            Assert.Throws<ArgumentException>(() => (basic with { Bins = [] }).Validate());
            Assert.Throws<ArgumentException>(() => (basic with
            {
                DesiccantBeds = [new GranulateDryingBedOptions { Features = GranulateDryingBedFeatures.None }]
            }).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                (basic with { Features = (GranulateDryingDeviceFeatures)(1 << 30) }).Validate());
            Assert.Throws<ArgumentException>(() => (basic with
            {
                Maintenance =
                [
                    new GranulateDryingMaintenanceOptions { Id = "same", DisplayName = new LocalizedText("First") },
                    new GranulateDryingMaintenanceOptions { Id = "same", DisplayName = new LocalizedText("Second") }
                ]
            }).Validate());
            AssertUaFailure(async () => await m_host.CreateAsync(
                basic with { OptionalMembers = ["Operation/NotAStandardMember"] }, new DryingAuthority())
                .ConfigureAwait(false), StatusCodes.BadBrowseNameInvalid);
            Assert.That((await m_host.Client.DiscoverDevicesAsync().ConfigureAwait(false))
                .FindIndex(device => device.BrowseName.Name == basic.BrowseName), Is.EqualTo(-1));
        }

        /// <summary>
        /// Traverses a published generated graph by unambiguous standard browse names for metadata assertions.
        /// </summary>
        private BaseInstanceState? Find(NodeState root, string path)
        {
            NodeState current = root;
            foreach (string name in path.Split('/'))
            {
                var children = new List<BaseInstanceState>();
                current.GetChildren(m_host.Runtime.Context, children);
                BaseInstanceState? child = children.SingleOrDefault(node => node.BrowseName.Name == name);
                if (child == null)
                {
                    return null;
                }
                current = child;
            }
            return current as BaseInstanceState;
        }
    }

    /// <summary>
    /// Public typed readers over a strict UA peer, independent of the transport fixtures above.
    /// </summary>
    [TestFixture]
    public sealed class GranulateDryingTypedReaderTests
    {
        [Test]
        public async Task GddIdentificationUsesDiAndMachineryTypesAndPathsAsync()
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new GranulateDryingClientPeer(token: cancellation.Token);
            NodeId identification = peer.ExpectChild(GranulateDryingClientPeer.DeviceId, peer.Di("Identification"));
            GranulateDryingIdentificationClient values = await peer.Client.Device(GranulateDryingClientPeer.DeviceId)
                .IdentificationValuesAsync(cancellation.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(values.Proxy.ObjectId, Is.EqualTo(identification));
                Assert.That(values.Proxy.Session, Is.SameAs(peer.Session.Object));
                Assert.That(peer.Context.NamespaceUris.GetIndex(Namespaces.GranulateDrying), Is.EqualTo(5));
            });

            await ReadValueAsync(peer, values.Manufacturer, identification, peer.Di("Manufacturer"),
                Variant.From(new LocalizedText("de", "Trockner GmbH")), new LocalizedText("de", "Trockner GmbH"))
                .ConfigureAwait(false);
            await ReadValueAsync(peer, values.Model, identification, peer.Di("Model"),
                Variant.From(new LocalizedText("en", "GD-270")), new LocalizedText("en", "GD-270"))
                .ConfigureAwait(false);
            await ReadValueAsync(peer, values.SerialNumber, identification, peer.Di("SerialNumber"),
                Variant.From("SN-60231"), "SN-60231").ConfigureAwait(false);
            await ReadValueAsync(peer, values.ProductInstanceUri, identification, peer.Di("ProductInstanceUri"),
                Variant.From("urn:dryer:60231"), "urn:dryer:60231").ConfigureAwait(false);
            await ReadValueAsync(peer, values.DeviceClass, identification, peer.Di("DeviceClass"),
                Variant.From("Granulate Drying Device"), "Granulate Drying Device").ConfigureAwait(false);
            await ReadValueAsync(peer, values.ManufacturerUri, identification, peer.Di("ManufacturerUri"),
                Variant.From("urn:maker:27"), "urn:maker:27").ConfigureAwait(false);
            await ReadValueAsync(peer, values.ProductCode, identification, peer.Di("ProductCode"),
                Variant.From("GD-EU-4"), "GD-EU-4").ConfigureAwait(false);
            await ReadValueAsync(peer, values.SoftwareRevision, identification, peer.Di("SoftwareRevision"),
                Variant.From("7.4-service"), "7.4-service", StatusCodes.BadSensorFailure).ConfigureAwait(false);
            peer.ExpectTranslation(identification,
                [new BrowsePathResult { StatusCode = StatusCodes.BadNotFound }], false, peer.Di("HardwareRevision"));
            GranulateDryingClientPeer.AssertMissing(
                await values.HardwareRevision.ReadAsync(cancellation.Token).ConfigureAwait(false));
            await ReadValueAsync(peer, values.Location, identification, peer.Machinery("Location"),
                Variant.From("Hall 3 / line 7"), "Hall 3 / line 7").ConfigureAwait(false);
            DateTimeUtc initial = (DateTimeUtc)new DateTime(2021, 6, 15, 12, 30, 0, DateTimeKind.Utc);
            await ReadValueAsync(peer, values.InitialOperationDate, identification,
                peer.Machinery("InitialOperationDate"),
                Variant.From(initial), initial).ConfigureAwait(false);
            await ReadValueAsync(peer, values.YearOfConstruction, identification, peer.Machinery("YearOfConstruction"),
                Variant.From((ushort)2021), (ushort)2021).ConfigureAwait(false);
            await ReadValueAsync(peer, values.MonthOfConstruction, identification,
                peer.Machinery("MonthOfConstruction"),
                Variant.From((byte)11), (byte)11).ConfigureAwait(false);
            peer.Verify();
        }

        [Test]
        public async Task GddIdentificationWritesAssetAndComponentNamesWithExactTypesAsync()
        {
            var peer = new GranulateDryingClientPeer();
            NodeId identification = peer.ExpectChild(GranulateDryingClientPeer.DeviceId, peer.Di("Identification"));
            GranulateDryingIdentificationClient values = await peer.Client.Device(GranulateDryingClientPeer.DeviceId)
                .IdentificationValuesAsync().ConfigureAwait(false);
            NodeId asset = peer.ExpectPath(identification, peer.Di("AssetId"));
            peer.ExpectWrite(asset, value => Assert.That(value, Is.EqualTo(Variant.From("asset-275/B"))),
                StatusCodes.GoodClamped);
            NodeId component = peer.ExpectPath(identification, peer.Di("ComponentName"));
            peer.ExpectWrite(component, value =>
            {
                Assert.That(value.TryGetValue(out LocalizedText name), Is.True);
                Assert.That(name.Locale, Is.EqualTo("fr"));
                Assert.That(name.Text, Is.EqualTo("Sécheur B"));
            }, StatusCodes.Good);
            Assert.That(await values.AssetId.WriteAsync("asset-275/B").ConfigureAwait(false),
                Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(await values.ComponentName.WriteAsync(new LocalizedText("fr",
                "Sécheur B")).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            GranulateDryingClientPeer.AssertFailure(async () =>
                await values.SerialNumber.WriteAsync("replacement").ConfigureAwait(false), StatusCodes.BadNotWritable);
            peer.ExpectTranslation(identification, [new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }],
                false, peer.Machinery("Location"));
            GranulateDryingClientPeer.AssertFailure(async () =>
                await values.Location.WriteAsync("absent").ConfigureAwait(false), StatusCodes.BadNotSupported);
            peer.Verify();
        }

        [TestCaseSource(nameof(DecodableSamples))]
        public async Task GddReadsDistinguishAbsenceQualityAndDecodingAsync(StatusCode status, double expected)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new GranulateDryingClientPeer(token: cancellation.Token);
            var proxy = new MonitoredParameterTypeClient(peer.Session.Object, GranulateDryingClientPeer.DeviceId,
                peer.Telemetry);
            GranulateDryingVariable<double> value = peer.Client.Parameter(proxy).ActualValue;
            await ReadValueAsync(peer, value, proxy.ObjectId, peer.Common("ActualValue"),
                Variant.From(expected), expected, status).ConfigureAwait(false);
            Assert.That(value.IsWritable, Is.False);
            peer.Verify();
        }

        [TestCaseSource(nameof(UndecodableSamples))]
        public async Task GddUndecodablePayloadsRetainPresenceAndOriginalQualityAsync(StatusCode status,
            bool nullPayload)
        {
            var peer = new GranulateDryingClientPeer();
            NodeId owner = GranulateDryingClientPeer.DeviceId;
            NodeId node = peer.ExpectPath(owner, peer.Common("ActualValue"));
            peer.ExpectRead(node, GranulateDryingClientPeer.Sample(
                nullPayload ? Variant.Null : Variant.From("not a Double"), status));
            GranulateDryingReading<double> reading = await peer.Client.Parameter(
                new MonitoredParameterTypeClient(peer.Session.Object, owner, peer.Telemetry))
                .ActualValue.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(reading.NodeId, Is.EqualTo(node));
                Assert.That(reading.IsPresent, Is.True);
                Assert.That(reading.HasValue, Is.False);
                Assert.That(reading.DataValue.StatusCode, Is.EqualTo(status));
                Assert.That(reading.DataValue.SourceTimestamp, Is.EqualTo(GranulateDryingClientPeer.SourceTime));
                Assert.That(reading.DataValue.ServerTimestamp, Is.EqualTo(GranulateDryingClientPeer.ServerTime));
                Assert.That(reading.DataValue.WrappedValue,
                    Is.EqualTo(nullPayload ? Variant.Null : Variant.From("not a Double")));
            });
            peer.Verify();
        }

        [Test]
        public void GddGoodPayloadWithWrongNativeTypeIsRejected()
        {
            var peer = new GranulateDryingClientPeer();
            NodeId owner = GranulateDryingClientPeer.DeviceId;
            NodeId node = peer.ExpectPath(owner, peer.Common("ActualValue"));
            peer.ExpectRead(node, GranulateDryingClientPeer.Sample(Variant.From(27)));
            GranulateDryingClientPeer.AssertFailure(async () =>
                await peer.Client.Parameter(new MonitoredParameterTypeClient(peer.Session.Object, owner,
                    peer.Telemetry))
                    .ActualValue.ReadAsync().ConfigureAwait(false), StatusCodes.BadTypeMismatch);
            peer.Verify();
        }

        [TestCaseSource(nameof(MissingStatuses))]
        public async Task GddAbsentValuesDoNotIssueReadRequestsAsync(StatusCode status)
        {
            var peer = new GranulateDryingClientPeer();
            peer.ExpectTranslation(GranulateDryingClientPeer.DeviceId,
                [new BrowsePathResult { StatusCode = status }], false, peer.Gdd("DisplayLanguage"));
            GranulateDryingClientPeer.AssertMissing(await peer.Client.Device(GranulateDryingClientPeer.DeviceId)
                .DisplayLanguage.ReadAsync().ConfigureAwait(false));
            peer.Verify();
        }

        [Test]
        public async Task GddMissingNamespaceAvoidsTranslationAndReadAsync()
        {
            var peer = new GranulateDryingClientPeer(includeGdd: false);
            GranulateDryingClientPeer.AssertMissing(await peer.Client.Device(GranulateDryingClientPeer.DeviceId)
                .DisplayLanguage.ReadAsync().ConfigureAwait(false));
            Assert.That(peer.Client.IsSupported, Is.False);
            peer.VerifyNoServices();
        }

        [TestCase(0)]
        [TestCase(2)]
        public void GddReadResponseCountsMustMatchTheOneRequestedValue(int count)
        {
            var peer = new GranulateDryingClientPeer();
            NodeId node = peer.ExpectPath(GranulateDryingClientPeer.DeviceId, peer.Gdd("DisplayLanguage"));
            peer.ExpectRead(node, Enumerable.Repeat(
                GranulateDryingClientPeer.Sample(Variant.From("de-DE")), count).ToArray());
            GranulateDryingClientPeer.AssertFailure(async () =>
                await peer.Client.Device(GranulateDryingClientPeer.DeviceId).DisplayLanguage.ReadAsync()
                    .ConfigureAwait(false), StatusCodes.BadUnexpectedError);
            peer.Verify();
        }

        [TestCase("NoResults")]
        [TestCase("TwoResults")]
        [TestCase("NoTargets")]
        [TestCase("TwoTargets")]
        [TestCase("Incomplete")]
        [TestCase("AccessDenied")]
        public void GddPathsUseQualifiedNamesAndValidateTranslationShapes(string shape)
        {
            var peer = new GranulateDryingClientPeer();
            StatusCode expectedStatus = shape is "NoResults" or "TwoResults"
                ? StatusCodes.BadUnexpectedError
                : shape == "AccessDenied" ? StatusCodes.BadUserAccessDenied : StatusCodes.BadBrowseNameInvalid;
            BrowsePathResult result = GranulateDryingClientPeer.Target(new NodeId(919u, 1));
            ArrayOf<BrowsePathResult> results = [result];
            switch (shape)
            {
                case "NoResults": results = []; break;
                case "TwoResults": results = [result, result]; break;
                case "NoTargets": result.Targets = []; break;
                case "TwoTargets": result.Targets = [result.Targets[0], result.Targets[0]]; break;
                case "Incomplete": result.Targets[0].RemainingPathIndex = 0; break;
                case "AccessDenied": result.StatusCode = StatusCodes.BadUserAccessDenied; break;
                default: throw new ArgumentOutOfRangeException(nameof(shape));
            }
            peer.ExpectTranslation(GranulateDryingClientPeer.DeviceId, results, false, peer.Gdd("DisplayLanguage"));
            GranulateDryingClientPeer.AssertFailure(async () =>
                await peer.Client.Device(GranulateDryingClientPeer.DeviceId).DisplayLanguage.ReadAsync()
                    .ConfigureAwait(false), expectedStatus);
            peer.Verify();
        }

        private static async Task ReadValueAsync<T>(
            GranulateDryingClientPeer peer, GranulateDryingVariable<T> variable, NodeId owner,
            QualifiedName name, Variant payload, T expected, StatusCode status = default)
        {
            NodeId node = peer.ExpectPath(owner, name);
            peer.ExpectRead(node, GranulateDryingClientPeer.Sample(payload, status));
            GranulateDryingClientPeer.AssertReading(
                await variable.ReadAsync(peer.Token).ConfigureAwait(false), node, expected, status);
        }

        private static IEnumerable<TestCaseData> DecodableSamples()
        {
            yield return new TestCaseData(StatusCodes.Good, 10.25).SetArgDisplayNames("Good", "10.25");
            yield return new TestCaseData(StatusCodes.UncertainLastUsableValue, -4.75)
                .SetArgDisplayNames("Uncertain", "-4.75");
            yield return new TestCaseData(StatusCodes.BadSensorFailure, 6.75).SetArgDisplayNames("Bad", "6.75");
        }

        private static IEnumerable<TestCaseData> UndecodableSamples()
        {
            yield return new TestCaseData(StatusCodes.Good, true).SetArgDisplayNames("Good", "Null");
            yield return new TestCaseData(StatusCodes.BadSensorFailure, true).SetArgDisplayNames("Bad", "Null");
            yield return new TestCaseData(StatusCodes.BadSensorFailure, false).SetArgDisplayNames("Bad", "String");
        }

        private static IEnumerable<TestCaseData> MissingStatuses()
        {
            yield return new TestCaseData(StatusCodes.BadNoMatch).SetArgDisplayNames("BadNoMatch");
            yield return new TestCaseData(StatusCodes.BadNotFound).SetArgDisplayNames("BadNotFound");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GddCommonParameterSettingsPreserveSignedValuesAndCapabilitiesAsync(bool controlled)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new GranulateDryingClientPeer(token: cancellation.Token);
            var common = new PlasticsRubber.Client.PlasticsRubberClient(peer.Session.Object, peer.Telemetry);
            NodeId id = new("temperature/bed-3", 1);
            MonitoredParameterTypeClient proxy = controlled
                ? common.ControlledParameter(id) : common.MonitoredParameter(id);
            GranulateDryingParameterClient parameter = peer.Client.Parameter(proxy);
            Assert.Multiple(() =>
            {
                Assert.That(parameter.Proxy, Is.SameAs(proxy));
                Assert.That(parameter.Proxy.Session, Is.SameAs(common.Session));
                Assert.That(parameter.Proxy.ObjectId, Is.EqualTo(id));
            });
            await ReadValueAsync(peer, parameter.ActualValue, id, peer.Common("ActualValue"),
                Variant.From(67.25), 67.25).ConfigureAwait(false);
            await ReadValueAsync(peer, parameter.SetValue, id, peer.Common("SetValue"),
                Variant.From(70.5), 70.5).ConfigureAwait(false);
            await ReadValueAsync(peer, parameter.MinValue, id, peer.Common("MinValue"),
                Variant.From(-40.25), -40.25).ConfigureAwait(false);
            await ReadValueAsync(peer, parameter.MaxValue, id, peer.Common("MaxValue"),
                Variant.From(180.75), 180.75).ConfigureAwait(false);
            await ReadValueAsync(peer, parameter.LowerTolerance, id, peer.Common("LowerTolerance"),
                Variant.From(-2.5), -2.5).ConfigureAwait(false);
            await ReadValueAsync(peer, parameter.UpperTolerance, id, peer.Common("UpperTolerance"),
                Variant.From(3.25), 3.25).ConfigureAwait(false);
            await ReadValueAsync(peer, parameter.LowerTolerance2, id, peer.Common("LowerTolerance2"),
                Variant.From(-8.5), -8.5).ConfigureAwait(false);
            await ReadValueAsync(peer, parameter.UpperTolerance2, id, peer.Common("UpperTolerance2"),
                Variant.From(11.75), 11.75).ConfigureAwait(false);
            await ReadValueAsync(peer, parameter.Status, id, peer.Common("Status"),
                Variant.From((ushort)5), (ushort)5, StatusCodes.UncertainLastUsableValue).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, parameter.AutomaticMonitoring, id, peer.Common("AutomaticMonitoring"),
                Variant.From(false), false, true, Variant.From(true)).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, parameter.MonitoringSensitivity, id, peer.Common("MonitoringSensitivity"),
                Variant.From((ushort)1), (ushort)1, (ushort)2, Variant.From((ushort)2)).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, parameter.AlarmSuppression, id, peer.Common("AlarmSuppression"),
                Variant.From((ushort)2), (ushort)2, (ushort)0, Variant.From((ushort)0)).ConfigureAwait(false);
            if (controlled)
            {
                await ReadAndWriteAsync(peer, parameter.SetRampUp, id, peer.Common("SetRampUp"),
                    Variant.From(0.75), 0.75, 1.5, Variant.From(1.5)).ConfigureAwait(false);
                await ReadAndWriteAsync(peer, parameter.SetRampDown, id, peer.Common("SetRampDown"),
                    Variant.From(2.25), 2.25, 3.5, Variant.From(3.5)).ConfigureAwait(false);
            }
            else
            {
                await ReadValueAsync(peer, parameter.SetRampUp, id, peer.Common("SetRampUp"),
                    Variant.From(0.75), 0.75).ConfigureAwait(false);
                await ReadValueAsync(peer, parameter.SetRampDown, id, peer.Common("SetRampDown"),
                    Variant.From(2.25), 2.25).ConfigureAwait(false);
                GranulateDryingClientPeer.AssertFailure(async () =>
                    await parameter.SetRampUp.WriteAsync(1.5, peer.Token).ConfigureAwait(false),
                        StatusCodes.BadNotWritable);
                GranulateDryingClientPeer.AssertFailure(async () =>
                    await parameter.SetRampDown.WriteAsync(3.5, peer.Token).ConfigureAwait(false),
                        StatusCodes.BadNotWritable);
            }
            GranulateDryingClientPeer.AssertFailure(async () =>
                await parameter.Status.WriteAsync(1, peer.Token).ConfigureAwait(false), StatusCodes.BadNotWritable);
            peer.Verify();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GddClosedLoopPlainAndMissingChildrenReturnNullWithoutReadingAsync(bool controlled)
        {
            var peer = new GranulateDryingClientPeer();
            NodeId id = GranulateDryingClientPeer.DeviceId;
            MonitoredParameterTypeClient proxy = controlled
                ? new ControlledParameterTypeClient(peer.Session.Object, id, peer.Telemetry)
                : new MonitoredParameterTypeClient(peer.Session.Object, id, peer.Telemetry);
            if (controlled)
            {
                peer.ExpectChild(id, peer.Common("ClosedLoopControl"), false);
            }
            Assert.That(await peer.Client.Parameter(proxy).ClosedLoopAsync().ConfigureAwait(false), Is.Null);
            if (!controlled)
            {
                peer.VerifyNoServices();
            }
            peer.Verify();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task GddClosedLoopReadersDistinguishPlainMissingAndStructuredValuesAsync(int count)
        {
            var peer = new GranulateDryingClientPeer();
            NodeId id = GranulateDryingClientPeer.DeviceId;
            NodeId child = peer.ExpectChild(id, peer.Common("ClosedLoopControl"));
            GranulateDryingClosedLoopClient? loop = await peer.Client.Parameter(
                new ControlledParameterTypeClient(peer.Session.Object, id, peer.Telemetry))
                .ClosedLoopAsync().ConfigureAwait(false);
            Assert.That(loop, Is.Not.Null);
            Assert.That(loop!.Proxy.ObjectId, Is.EqualTo(child));
            Assert.That(loop.Proxy.Session, Is.SameAs(peer.Session.Object));
            PIDParametersDataType[] coefficients =
            [
                new() { P = -1.25, I = 0, D = 3.5 },
                new() { P = 7.75, I = -8.25, D = 9.5 }
            ];
            NodeId pid = peer.ExpectPath(child, 2, peer.Common("PIDParameters"));
            peer.ExpectRead(pid, GranulateDryingClientPeer.Sample(
                Variant.FromStructure(coefficients.Take(count).ToArray().ToArrayOf())));
            GranulateDryingReading<ArrayOf<PIDParametersDataType>> reading =
                await loop.PIDParameters.ReadAsync().ConfigureAwait(false);
            (double, double, double)[] expected = [(-1.25, 0, 3.5), (7.75, -8.25, 9.5)];
            Assert.Multiple(() =>
            {
                Assert.That(reading.NodeId, Is.EqualTo(pid));
                Assert.That(reading.HasValue, Is.True);
                Assert.That(reading.Value.ToArray()!.Select(item => (item.P, item.I, item.D)),
                    Is.EqualTo(expected.Take(count)));
                Assert.That(reading.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(reading.DataValue.SourceTimestamp, Is.EqualTo(GranulateDryingClientPeer.SourceTime));
                Assert.That(reading.DataValue.ServerTimestamp, Is.EqualTo(GranulateDryingClientPeer.ServerTime));
            });
            peer.ExpectWrite(pid, value =>
            {
                Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));
                Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(value.TryGetStructure(out ArrayOf<PIDParametersDataType> decoded), Is.True);
                Assert.That(decoded.ToArray()!.Select(item => (item.P, item.I, item.D)),
                    Is.EqualTo(new[] { (-9.5, 4.25, 0.0), (2.75, -6.5, 1.125) }));
            }, StatusCodes.GoodClamped);
            Assert.That(await loop.PIDParameters.WriteAsync(
            [
                new PIDParametersDataType { P = -9.5, I = 4.25, D = 0 },
                new PIDParametersDataType { P = 2.75, I = -6.5, D = 1.125 }
            ]).ConfigureAwait(false), Is.EqualTo(StatusCodes.GoodClamped));
            await ReadAndWriteAsync(peer, loop.AutomaticControllerMode, child, peer.Common("AutomaticControllerMode"),
                Variant.From(false), false, true, Variant.From(true)).ConfigureAwait(false);
            await ReadValueAsync(peer, loop.AutoTuningActive, child, peer.Common("AutoTuningActive"),
                Variant.From(true), true).ConfigureAwait(false);
            GranulateDryingClientPeer.AssertFailure(async () =>
                await loop.AutoTuningActive.WriteAsync(false).ConfigureAwait(false), StatusCodes.BadNotWritable);
            peer.Verify();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public async Task GddEnergyAndConfigurationReadersPreserveStructuredValuesAsync(int pageCount)
        {
            var peer = new GranulateDryingClientPeer();
            NodeId energyId = new("dryer/energy", 1);
            NodeId configurationId = new("dryer/configuration", 1);
            var common = new PlasticsRubber.Client.PlasticsRubberClient(peer.Session.Object, peer.Telemetry);
            GranulateDryingEnergyClient energy = peer.Client.Energy(common.Energy(energyId));
            GranulateDryingConfigurationClient configuration = peer.Client.Configuration(
                common.MachineConfiguration(configurationId));
            await ReadValueAsync(peer, energy.PowerConsumption, energyId, peer.Common("PowerConsumption"),
                Variant.From(12975.5), 12975.5).ConfigureAwait(false);
            await ReadValueAsync(peer, energy.ActualPower, energyId, peer.Common("ActualPower"),
                Variant.From(18.25), 18.25).ConfigureAwait(false);
            await ReadValueAsync(peer, energy.ActualSpecificEnergy, energyId, peer.Common("ActualSpecificEnergy"),
                Variant.From(0.875), 0.875, StatusCodes.BadSensorFailure).ConfigureAwait(false);
            await ReadValueAsync(peer, energy.PowerFactor, energyId, peer.Common("PowerFactor"),
                Variant.From(0.91), 0.91).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, configuration.UserMachineName, configurationId,
                peer.Common("UserMachineName"),
                Variant.From("Dryer B"), "Dryer B", "Dryer East", Variant.From("Dryer East")).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, configuration.LocationName, configurationId, peer.Common("LocationName"),
                Variant.From("Line 2"), "Line 2", "Hall 4", Variant.From("Hall 4")).ConfigureAwait(false);
            NodeId timezone = peer.ExpectPath(configurationId, 2, peer.Common("TimeZoneOffset"));
            peer.ExpectRead(timezone, GranulateDryingClientPeer.Sample(
                Variant.FromStructure(new TimeZoneDataType { Offset = -210, DaylightSavingInOffset = true })));
            GranulateDryingReading<TimeZoneDataType> zone = await configuration.TimeZoneOffset.ReadAsync()
                .ConfigureAwait(false);
            GranulateDryingClientPeer.AssertReading(zone, timezone,
                new TimeZoneDataType { Offset = -210, DaylightSavingInOffset = true });
            peer.ExpectWrite(timezone, value =>
            {
                Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));
                Assert.That(value.TryGetStructure<TimeZoneDataType>(out TimeZoneDataType? decoded), Is.True);
                Assert.That(decoded!.Offset, Is.EqualTo(345));
                Assert.That(decoded.DaylightSavingInOffset, Is.False);
            }, StatusCodes.Good);
            Assert.That(await configuration.TimeZoneOffset.WriteAsync(
                new TimeZoneDataType { Offset = 345, DaylightSavingInOffset = false }).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            PageEntryDataType[] pages =
            [
                new() { Id = "overview", Title = new LocalizedText("en", "Overview") },
                new() { Id = "sécurité", Title = new LocalizedText("fr", "Sécurité") },
                new() { Id = "service/27", Title = new LocalizedText("de", "Wartung") }
            ];
            NodeId directory = peer.ExpectPath(configurationId, peer.Common("PageDirectory"));
            peer.ExpectRead(directory, GranulateDryingClientPeer.Sample(
                Variant.FromStructure(pages.Take(pageCount).ToArray().ToArrayOf())));
            GranulateDryingReading<ArrayOf<PageEntryDataType>> catalogue = await configuration.PageDirectory.ReadAsync()
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(catalogue.NodeId, Is.EqualTo(directory));
                Assert.That(catalogue.HasValue, Is.True);
                Assert.That(catalogue.Value.ToArray()!.Select(page => (page.Id, page.Title.Locale, page.Title.Text)),
                    Is.EqualTo(new[] { ("overview", "en", "Overview"), ("sécurité", "fr", "Sécurité"),
                        ("service/27", "de", "Wartung") }.Take(pageCount)));
                Assert.That(catalogue.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(catalogue.DataValue.SourceTimestamp, Is.EqualTo(GranulateDryingClientPeer.SourceTime));
                Assert.That(catalogue.DataValue.ServerTimestamp, Is.EqualTo(GranulateDryingClientPeer.ServerTime));
            });
            GranulateDryingClientPeer.AssertFailure(async () =>
                await configuration.PageDirectory.WriteAsync([]).ConfigureAwait(false), StatusCodes.BadNotWritable);
            Assert.That(energy.Proxy.ObjectId, Is.EqualTo(energyId));
            Assert.That(configuration.Proxy.ObjectId, Is.EqualTo(configurationId));
            peer.Verify();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GddUnitsAndRangesPreserveNativeMetadataAsync(bool badUnits)
        {
            var peer = new GranulateDryingClientPeer();
            NodeId owner = GranulateDryingClientPeer.DeviceId;
            GranulateDryingVariable<double> variable = peer.Client.Parameter(
                new MonitoredParameterTypeClient(peer.Session.Object, owner, peer.Telemetry)).ActualValue;
            NodeId units = peer.ExpectPath(owner, peer.Common("ActualValue"),
                new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits));
            StatusCode status = badUnits ? StatusCodes.BadSensorFailure : StatusCodes.Good;
            peer.ExpectRead(units, GranulateDryingClientPeer.Sample(Variant.FromStructure(new EUInformation
            {
                UnitId = 0x4d5148,
                NamespaceUri = "urn:units:test",
                DisplayName = new LocalizedText("en", "m³/h"),
                Description = new LocalizedText("en", "cubic metres per hour")
            }), status));
            GranulateDryingReading<EUInformation> unit = await
                variable.ReadEngineeringUnitsAsync().ConfigureAwait(false);
            GranulateDryingClientPeer.AssertReading(unit, units, new EUInformation
            {
                UnitId = 0x4d5148,
                NamespaceUri = "urn:units:test",
                DisplayName = new LocalizedText("en", "m³/h"),
                Description = new LocalizedText("en", "cubic metres per hour")
            }, status);
            NodeId range = peer.ExpectPath(owner, peer.Common("ActualValue"),
                new QualifiedName(Opc.Ua.BrowseNames.EURange));
            peer.ExpectRead(range, GranulateDryingClientPeer.Sample(
                Variant.FromStructure(new Opc.Ua.Range { Low = -35.5, High = 170.25 })));
            GranulateDryingClientPeer.AssertReading(await variable.ReadRangeAsync().ConfigureAwait(false),
                range, new Opc.Ua.Range { Low = -35.5, High = 170.25 });
            peer.Verify();
        }

        [Test]
        public async Task GddMissingEngineeringMetadataIsNotInventedAsync()
        {
            var peer = new GranulateDryingClientPeer();
            GranulateDryingVariable<double> variable = peer.Client.Parameter(new MonitoredParameterTypeClient(
                peer.Session.Object, GranulateDryingClientPeer.DeviceId, peer.Telemetry)).ActualValue;
            peer.ExpectTranslation(GranulateDryingClientPeer.DeviceId,
                [new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }], false,
                peer.Common("ActualValue"), new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits));
            GranulateDryingClientPeer.AssertMissing(await variable.ReadEngineeringUnitsAsync().ConfigureAwait(false));
            peer.ExpectTranslation(GranulateDryingClientPeer.DeviceId,
                [new BrowsePathResult { StatusCode = StatusCodes.BadNotFound }], false,
                peer.Common("ActualValue"), new QualifiedName(Opc.Ua.BrowseNames.EURange));
            GranulateDryingClientPeer.AssertMissing(await variable.ReadRangeAsync().ConfigureAwait(false));
            peer.Verify();
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(1)]
        public void GddWritesPreserveNativeTypesAndServiceStatuses(int resultCount)
        {
            var peer = new GranulateDryingClientPeer();
            NodeId node = peer.ExpectPath(GranulateDryingClientPeer.DeviceId, peer.Gdd("DisplayLanguage"));
            peer.ExpectWrite(node, value => Assert.That(value, Is.EqualTo(Variant.From("ja-JP"))),
                Enumerable.Repeat(StatusCodes.BadUserAccessDenied, resultCount).ToArray());
            GranulateDryingClientPeer.AssertFailure(async () =>
                await peer.Client.Device(GranulateDryingClientPeer.DeviceId).DisplayLanguage.WriteAsync("ja-JP")
                    .ConfigureAwait(false),
                resultCount == 1 ? StatusCodes.BadUserAccessDenied : StatusCodes.BadUnexpectedError);
            peer.Verify();
        }

        private static async Task ReadAndWriteAsync<T>(
            GranulateDryingClientPeer peer, GranulateDryingVariable<T> variable, NodeId owner,
            QualifiedName name, Variant payload, T expectedRead, T requestedWrite, Variant expectedWire)
        {
            NodeId node = peer.ExpectPath(owner, 2, name);
            peer.ExpectRead(node, GranulateDryingClientPeer.Sample(payload));
            GranulateDryingClientPeer.AssertReading(
                await variable.ReadAsync(peer.Token).ConfigureAwait(false), node, expectedRead);
            peer.ExpectWrite(node, value => Assert.That(value, Is.EqualTo(expectedWire)), StatusCodes.GoodClamped);
            Assert.That(variable.IsWritable, Is.True);
            Assert.That(await variable.WriteAsync(requestedWrite, peer.Token).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.GoodClamped));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GddBinAndBedMeasurementsPreserveQualityTimesAndDistinctUnitsAsync(bool bad)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new GranulateDryingClientPeer(token: cancellation.Token);
            NodeId dryer = peer.ExpectChild(GranulateDryingClientPeer.DeviceId, peer.Gdd("Dryer"));
            NodeId bins = peer.ExpectChild(dryer, peer.Gdd("Bins"));
            NodeId beds = peer.ExpectChild(dryer, peer.Gdd("DesiccantBeds"));
            NodeId binId = new("bin-2", 1);
            NodeId bedId = new("bed-7", 1);
            NodeId binType = peer.Type(ObjectTypeIds.BinType);
            NodeId bedType = peer.Type(ObjectTypeIds.DesiccantBedType);
            peer.ExpectBrowse(bins, GranulateDryingClientPeer.Reference(binId, binType, "Bin_002"));
            peer.ExpectBrowse(beds, GranulateDryingClientPeer.Reference(bedId, bedType, "DesiccantBed_007"));
            peer.ExpectType(binType, binType, true);
            peer.ExpectType(bedType, bedType, true);
            GranulateDryingDeviceClient device = peer.Client.Device(GranulateDryingClientPeer.DeviceId);
            GranulateDryingBinClient bin = (await device.BinsAsync(peer.Token).ConfigureAwait(false))[0];
            GranulateDryingBedClient bed = (await device.DesiccantBedsAsync(peer.Token).ConfigureAwait(false))[0];
            Assert.That(bin.Proxy.ObjectId, Is.EqualTo(binId));
            Assert.That(bed.Proxy.ObjectId, Is.EqualTo(bedId));
            StatusCode status = bad ? StatusCodes.BadSensorFailure : StatusCodes.Good;
            NodeId binAir = peer.ExpectPath(binId, peer.Gdd("AirFlow"), peer.Common("ActualValue"));
            peer.ExpectRead(binAir, GranulateDryingClientPeer.Sample(Variant.From(bad ? 6.75 : 10.25), status));
            GranulateDryingClientPeer.AssertReading(await bin.AirFlow.ReadAsync(peer.Token).ConfigureAwait(false),
                binAir, bad ? 6.75 : 10.25, status);
            NodeId bedAir = peer.ExpectPath(bedId, peer.Gdd("AirFlow"), peer.Common("ActualValue"));
            peer.ExpectRead(bedAir, GranulateDryingClientPeer.Sample(Variant.From(bad ? 1.25 : 2.5), status));
            GranulateDryingClientPeer.AssertReading(await bed.AirFlow.ReadAsync(peer.Token).ConfigureAwait(false),
                bedAir, bad ? 1.25 : 2.5, status);
            NodeId binUnits = peer.ExpectPath(binId, peer.Gdd("AirFlow"), peer.Common("ActualValue"),
                new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits));
            peer.ExpectRead(binUnits, GranulateDryingClientPeer.Sample(Variant.FromStructure(new EUInformation
            {
                UnitId = 0x4d5148, NamespaceUri = "urn:un:cefact", DisplayName = new LocalizedText("en", "m³/h"),
                Description = new LocalizedText("en", "cubic metres per hour")
            })));
            NodeId bedUnits = peer.ExpectPath(bedId, peer.Gdd("AirFlow"), peer.Common("ActualValue"),
                new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits));
            peer.ExpectRead(bedUnits, GranulateDryingClientPeer.Sample(Variant.FromStructure(new EUInformation
            {
                UnitId = 0x473531, NamespaceUri = "urn:un:cefact", DisplayName = new LocalizedText("en", "l/s"),
                Description = new LocalizedText("en", "litres per second")
            })));
            GranulateDryingClientPeer.AssertReading(
                await bin.AirFlow.ReadEngineeringUnitsAsync(peer.Token).ConfigureAwait(false), binUnits,
                new EUInformation
                {
                    UnitId = 0x4d5148, NamespaceUri = "urn:un:cefact", DisplayName = new LocalizedText("en", "m³/h"),
                    Description = new LocalizedText("en", "cubic metres per hour")
                });
            GranulateDryingClientPeer.AssertReading(
                await bed.AirFlow.ReadEngineeringUnitsAsync(peer.Token).ConfigureAwait(false), bedUnits,
                new EUInformation
                {
                    UnitId = 0x473531, NamespaceUri = "urn:un:cefact", DisplayName = new LocalizedText("en", "l/s"),
                    Description = new LocalizedText("en", "litres per second")
                });
            await ReadMeasurementAsync(peer, bin.Temperature, binId, "Temperature", 78.25).ConfigureAwait(false);
            await ReadMeasurementAsync(peer, bin.DryAirTemperature, binId, "DryAirTemperature",
                91.5).ConfigureAwait(false);
            await ReadMeasurementAsync(peer, bin.AmbientAirTemperature, binId, "AmbientAirTemperature",
                24.75).ConfigureAwait(false);
            await ReadMeasurementAsync(peer, bed.DewPoint, bedId, "DewPoint", -32.25).ConfigureAwait(false);
            await ReadMeasurementAsync(peer, bed.TemperatureOfDesiccantBed, bedId, "TemperatureOfDesiccantBed", 117.5)
                .ConfigureAwait(false);
            await ReadMeasurementAsync(peer, bed.TemperatureDesiccantBedOut, bedId, "TemperatureDesiccantBedOut", 68.75)
                .ConfigureAwait(false);
            await ReadMeasurementAsync(peer, bed.TemperatureBackToDesiccantBed, bedId,
                "TemperatureBackToDesiccantBed", 39.125)
                .ConfigureAwait(false);
            NodeId airflowSetpoint = peer.ExpectPath(bedId, peer.Gdd("AirFlow"), peer.Common("SetValue"));
            peer.ExpectWrite(airflowSetpoint, value => Assert.That(value, Is.EqualTo(Variant.From(3.75))),
                StatusCodes.Good);
            Assert.That(await bed.AirFlowSetValue.WriteAsync(3.75, peer.Token).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            NodeId temperatureSetpoint = peer.ExpectPath(bedId, peer.Gdd("TemperatureOfDesiccantBed"),
                peer.Common("SetValue"));
            peer.ExpectWrite(temperatureSetpoint, value => Assert.That(value, Is.EqualTo(Variant.From(122.5))),
                StatusCodes.GoodClamped);
            Assert.That(await bed.TemperatureSetValue.WriteAsync(122.5, peer.Token).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.GoodClamped));
            GranulateDryingClientPeer.AssertFailure(async () =>
                await bin.Temperature.WriteAsync(83, peer.Token).ConfigureAwait(false), StatusCodes.BadNotWritable);
            peer.Verify();
        }

        [Test]
        public async Task GddBinRecipeAndOperationWritesRetainUnsignedWidthsEnumsAndDurationsAsync()
        {
            var peer = new GranulateDryingClientPeer();
            NodeId dryer = peer.ExpectChild(GranulateDryingClientPeer.DeviceId, peer.Gdd("Dryer"));
            NodeId bins = peer.ExpectChild(dryer, peer.Gdd("Bins"));
            NodeId binId = new("recipe/bin-4", 1);
            NodeId type = peer.Type(ObjectTypeIds.BinType);
            peer.ExpectBrowse(bins, GranulateDryingClientPeer.Reference(binId, type));
            peer.ExpectType(type, type, true);
            GranulateDryingBinClient bin = (await peer.Client.Device(GranulateDryingClientPeer.DeviceId)
                .BinsAsync().ConfigureAwait(false))[0];
            await ReadAndWriteAsync(peer, bin.MaterialIdentification, binId, peer.Gdd("MaterialIdentification"),
                Variant.From("PA6-GF30"), "PA6-GF30", "PET-R", Variant.From("PET-R")).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, bin.BatchNumber, binId, peer.Gdd("BatchNumber"),
                Variant.From("batch-47"), "batch-47", "batch-51", Variant.From("batch-51")).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, bin.MaterialDryingTime, binId, peer.Gdd("MaterialDryingTime"),
                Variant.From(7200000.5), 7200000.5, 8100000.25, Variant.From(8100000.25)).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, bin.TemperatureReduction, binId, peer.Gdd("TemperatureReduction"),
                Variant.From(12.5), 12.5, 17.75, Variant.From(17.75)).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, bin.DustRemovalPulses, binId, peer.Gdd("DustRemovalPulses"),
                Variant.From((ushort)257), (ushort)257, (ushort)513, Variant.From((ushort)513)).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, bin.DryingMode, binId, peer.Gdd("DryingMode"),
                Variant.From(DryingModeEnumeration.CONTINUOUS), DryingModeEnumeration.CONTINUOUS,
                DryingModeEnumeration.COMBINED, Variant.From(DryingModeEnumeration.COMBINED)).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, bin.AirFlowMode, binId, peer.Gdd("AirFlowMode"),
                Variant.From(AirFlowModeEnumeration.CONTINUOUS), AirFlowModeEnumeration.CONTINUOUS,
                AirFlowModeEnumeration.INTERVAL, Variant.From(AirFlowModeEnumeration.INTERVAL)).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, bin.IntervalTimeOn, binId, peer.Gdd("IntervalTimeOn"),
                Variant.From(2500.5), 2500.5, 3100.25, Variant.From(3100.25)).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, bin.IntervalTimeOff, binId, peer.Gdd("IntervalTimeOff"),
                Variant.From(750.75), 750.75, 1100.5, Variant.From(1100.5)).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, bin.AirSource, binId, peer.Gdd("AirSource"),
                Variant.From(AirSourceEnumeration.FRESH_AIR), AirSourceEnumeration.FRESH_AIR,
                AirSourceEnumeration.RETURN_AIR, Variant.From(AirSourceEnumeration.RETURN_AIR)).ConfigureAwait(false);
            DateTimeUtc resetTime = (DateTimeUtc)new DateTime(2024, 3, 5, 7, 11, 13, DateTimeKind.Utc);
            await ReadValueAsync(peer, bin.ResetDryingTimeResponseStatus, binId,
                peer.Gdd("ResetDryingTimeResponseStatus"),
                Variant.From(resetTime), resetTime).ConfigureAwait(false);
            GranulateDryingClientPeer.AssertFailure(async () =>
                await bin.ResetDryingTimeResponseStatus.WriteAsync(resetTime).ConfigureAwait(false),
                    StatusCodes.BadNotWritable);
            NodeId operationId = peer.ExpectChild(GranulateDryingClientPeer.DeviceId, peer.Gdd("Operation"));
            GranulateDryingOperationClient operation = await peer.Client.Device(GranulateDryingClientPeer.DeviceId)
                .OperationAsync().ConfigureAwait(false);
            await ReadAndWriteAsync(peer, operation.DeviceMappingNumber, operationId, peer.Gdd("DeviceMappingNumber"),
                Variant.From(70001u), 70001u, 90003u, Variant.From(90003u)).ConfigureAwait(false);
            await ReadAndWriteAsync(peer, operation.MainFailureBehaviour, operationId, peer.Gdd("MainFailureBehaviour"),
                Variant.From(MainFailureBehaviourEnumeration.STANDBY), MainFailureBehaviourEnumeration.STANDBY,
                MainFailureBehaviourEnumeration.CONTINUE, Variant.From(MainFailureBehaviourEnumeration.CONTINUE))
                .ConfigureAwait(false);
            peer.Verify();
        }

        [Test]
        public async Task GddReadOnlyByteAndUtcIdentificationCannotBeWrittenAsync()
        {
            var peer = new GranulateDryingClientPeer();
            NodeId identification = peer.ExpectChild(GranulateDryingClientPeer.DeviceId, peer.Di("Identification"));
            GranulateDryingIdentificationClient values = await peer.Client.Device(GranulateDryingClientPeer.DeviceId)
                .IdentificationValuesAsync().ConfigureAwait(false);
            GranulateDryingClientPeer.AssertFailure(async () =>
                await values.MonthOfConstruction.WriteAsync(7).ConfigureAwait(false), StatusCodes.BadNotWritable);
            GranulateDryingClientPeer.AssertFailure(async () =>
                await
                    values.InitialOperationDate.WriteAsync(GranulateDryingClientPeer.SourceTime).ConfigureAwait(false),
                StatusCodes.BadNotWritable);
            Assert.That(values.Proxy.ObjectId, Is.EqualTo(identification));
            peer.Verify();
        }

        private static async Task ReadMeasurementAsync(
            GranulateDryingClientPeer peer, GranulateDryingVariable<double> variable, NodeId owner,
            string parameter, double expected)
        {
            NodeId node = peer.ExpectPath(owner, peer.Gdd(parameter), peer.Common("ActualValue"));
            peer.ExpectRead(node, GranulateDryingClientPeer.Sample(Variant.From(expected)));
            GranulateDryingClientPeer.AssertReading(
                await variable.ReadAsync(peer.Token).ConfigureAwait(false), node, expected);
        }
    }
}
