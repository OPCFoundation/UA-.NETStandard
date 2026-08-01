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
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pumps;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Regression tests pinning the NodeId surface of the pump instances that
    /// <see cref="PumpNodeManager"/> materialises. The manager used to walk the
    /// finished subtree and re-stamp every child through
    /// <see cref="ISystemContext.NodeIdFactory"/>; the source-generated
    /// <c>CreateInstanceOf&lt;Type&gt;</c> / <c>Add&lt;Child&gt;</c> /
    /// <c>CreateOrReplace&lt;Child&gt;</c> helpers now do that themselves. These
    /// tests assert the observable result is unchanged: the same model
    /// identifiers are materialised, every NodeId the generated helpers assign
    /// comes from the NodeIdFactory, and no two nodes share one.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [NonParallelizable]
    public sealed class PumpInstanceNodeIdRegressionTests
    {
        /// <summary>
        /// Every model identifier a configured pump exposes, as a symbolic-name
        /// path relative to the pump root. Captured from the manager before the
        /// generated helpers took over NodeId assignment, so a child that stops
        /// being materialised - or that appears out of nowhere - fails here.
        /// Also covers the OpenUSD representation, component and signal nodes
        /// the sample adds on top of the DI/Pumps model.
        /// </summary>
        private static readonly string[] s_expectedPumpSurface =
        [
            "AlarmActive",
            "BayPosition",
            "Bearing",
            "Bearing/OpenUsdRepresentation",
            "Bearing/OpenUsdRepresentation/PrimPath",
            "Bearing/OpenUsdRepresentation/Stage",
            "Events",
            "Events/OverTempAlarm",
            "Events/OverTempAlarm/",
            "Events/OverTempAlarm/",
            "Events/OverTempAlarm/",
            "Events/OverTempAlarm/",
            "Events/OverTempAlarm/AckedState",
            "Events/OverTempAlarm/AckedState/Id",
            "Events/OverTempAlarm/Acknowledge",
            "Events/OverTempAlarm/Acknowledge/InputArguments",
            "Events/OverTempAlarm/Acknowledge/OutputArguments",
            "Events/OverTempAlarm/ActiveState",
            "Events/OverTempAlarm/ActiveState/Id",
            "Events/OverTempAlarm/AddComment",
            "Events/OverTempAlarm/AddComment/InputArguments",
            "Events/OverTempAlarm/AddComment/OutputArguments",
            "Events/OverTempAlarm/BranchId",
            "Events/OverTempAlarm/ClientUserId",
            "Events/OverTempAlarm/Comment",
            "Events/OverTempAlarm/Comment/SourceTimestamp",
            "Events/OverTempAlarm/ConditionClassId",
            "Events/OverTempAlarm/ConditionClassName",
            "Events/OverTempAlarm/ConditionName",
            "Events/OverTempAlarm/Disable",
            "Events/OverTempAlarm/Disable/InputArguments",
            "Events/OverTempAlarm/Disable/OutputArguments",
            "Events/OverTempAlarm/Enable",
            "Events/OverTempAlarm/Enable/InputArguments",
            "Events/OverTempAlarm/Enable/OutputArguments",
            "Events/OverTempAlarm/EnabledState",
            "Events/OverTempAlarm/EnabledState/Id",
            "Events/OverTempAlarm/EventId",
            "Events/OverTempAlarm/EventType",
            "Events/OverTempAlarm/InputNode",
            "Events/OverTempAlarm/LastSeverity",
            "Events/OverTempAlarm/LastSeverity/SourceTimestamp",
            "Events/OverTempAlarm/Message",
            "Events/OverTempAlarm/Quality",
            "Events/OverTempAlarm/Quality/SourceTimestamp",
            "Events/OverTempAlarm/ReceiveTime",
            "Events/OverTempAlarm/Retain",
            "Events/OverTempAlarm/Severity",
            "Events/OverTempAlarm/SourceName",
            "Events/OverTempAlarm/SourceNode",
            "Events/OverTempAlarm/SuppressedOrShelved",
            "Events/OverTempAlarm/Time",
            "Events/SupervisionProcessFluid",
            "Events/SupervisionProcessFluid/Cavitation",
            "Events/SupervisionProcessFluid/Cavitation/FalseState",
            "Events/SupervisionProcessFluid/Cavitation/TrueState",
            "Events/SupervisionPumpOperation",
            "Events/SupervisionPumpOperation/MotorOverheat",
            "Events/SupervisionPumpOperation/MotorOverheat/FalseState",
            "Events/SupervisionPumpOperation/MotorOverheat/TrueState",
            "FluidSurfacePosition",
            "Identification",
            "Identification/ArticleNumber",
            "Identification/AssetId",
            "Identification/ComponentName",
            "Identification/CountryOfOrigin",
            "Identification/DayOfConstruction",
            "Identification/DeviceClass",
            "Identification/FabricationNumber",
            "Identification/HardwareRevision",
            "Identification/Location",
            "Identification/Manufacturer",
            "Identification/ManufacturerUri",
            "Identification/Model",
            "Identification/MonthOfConstruction",
            "Identification/OrderProductCode",
            "Identification/ProductCode",
            "Identification/ProductInstanceUri",
            "Identification/SerialNumber",
            "Identification/SoftwareRevision",
            "Identification/Supplier",
            "Identification/TypeOfProduct",
            "Identification/YearOfConstruction",
            "Impeller",
            "Impeller/OpenUsdRepresentation",
            "Impeller/OpenUsdRepresentation/PrimPath",
            "Impeller/OpenUsdRepresentation/Stage",
            "Maintenance",
            "Maintenance/GeneralMaintenance",
            "Maintenance/GeneralMaintenance/MaintenancePlan",
            "OpenUsdRepresentation",
            "OpenUsdRepresentation/AlarmActiveVisibility",
            "OpenUsdRepresentation/AlarmActiveVisibility/AlarmAspect",
            "OpenUsdRepresentation/AlarmActiveVisibility/BadQualityAction",
            "OpenUsdRepresentation/AlarmActiveVisibility/BindingDefinitionId",
            "OpenUsdRepresentation/AlarmActiveVisibility/Enabled",
            "OpenUsdRepresentation/AlarmActiveVisibility/RenderTargetKind",
            "OpenUsdRepresentation/AlarmActiveVisibility/Scale",
            "OpenUsdRepresentation/AlarmActiveVisibility/SignalRole",
            "OpenUsdRepresentation/AlarmActiveVisibility/SourceNodeId",
            "OpenUsdRepresentation/AlarmActiveVisibility/TargetPrimPath",
            "OpenUsdRepresentation/AlarmActiveVisibility/TargetPropertyName",
            "OpenUsdRepresentation/AlarmActiveVisibility/TargetStage",
            "OpenUsdRepresentation/AlarmActiveVisibility/TargetUsdTypeName",
            "OpenUsdRepresentation/BayLayout",
            "OpenUsdRepresentation/BayLayout/BadQualityAction",
            "OpenUsdRepresentation/BayLayout/BindingDefinitionId",
            "OpenUsdRepresentation/BayLayout/Enabled",
            "OpenUsdRepresentation/BayLayout/RenderTargetKind",
            "OpenUsdRepresentation/BayLayout/Scale",
            "OpenUsdRepresentation/BayLayout/SignalRole",
            "OpenUsdRepresentation/BayLayout/SourceNodeId",
            "OpenUsdRepresentation/BayLayout/TargetPrimPath",
            "OpenUsdRepresentation/BayLayout/TargetPropertyName",
            "OpenUsdRepresentation/BayLayout/TargetStage",
            "OpenUsdRepresentation/BayLayout/TargetUsdTypeName",
            "OpenUsdRepresentation/BearingComponent",
            "OpenUsdRepresentation/BearingComponent/BindingDefinitionId",
            "OpenUsdRepresentation/BearingComponent/Cardinality",
            "OpenUsdRepresentation/BearingComponent/ComponentRepresentation",
            "OpenUsdRepresentation/BearingComponent/CompositionArc",
            "OpenUsdRepresentation/BearingComponent/Enabled",
            "OpenUsdRepresentation/BearingComponent/TargetPrimPath",
            "OpenUsdRepresentation/BearingTempColor",
            "OpenUsdRepresentation/BearingTempColor/BadQualityAction",
            "OpenUsdRepresentation/BearingTempColor/BindingDefinitionId",
            "OpenUsdRepresentation/BearingTempColor/Enabled",
            "OpenUsdRepresentation/BearingTempColor/Offset",
            "OpenUsdRepresentation/BearingTempColor/RenderTargetKind",
            "OpenUsdRepresentation/BearingTempColor/Scale",
            "OpenUsdRepresentation/BearingTempColor/SignalRole",
            "OpenUsdRepresentation/BearingTempColor/SourceNodeId",
            "OpenUsdRepresentation/BearingTempColor/TargetPrimPath",
            "OpenUsdRepresentation/BearingTempColor/TargetPropertyName",
            "OpenUsdRepresentation/BearingTempColor/TargetStage",
            "OpenUsdRepresentation/BearingTempColor/TargetUsdTypeName",
            "OpenUsdRepresentation/BearingTempNeedle",
            "OpenUsdRepresentation/BearingTempNeedle/BadQualityAction",
            "OpenUsdRepresentation/BearingTempNeedle/BindingDefinitionId",
            "OpenUsdRepresentation/BearingTempNeedle/Enabled",
            "OpenUsdRepresentation/BearingTempNeedle/Offset",
            "OpenUsdRepresentation/BearingTempNeedle/RenderTargetKind",
            "OpenUsdRepresentation/BearingTempNeedle/Scale",
            "OpenUsdRepresentation/BearingTempNeedle/SignalRole",
            "OpenUsdRepresentation/BearingTempNeedle/SourceNodeId",
            "OpenUsdRepresentation/BearingTempNeedle/TargetPrimPath",
            "OpenUsdRepresentation/BearingTempNeedle/TargetPropertyName",
            "OpenUsdRepresentation/BearingTempNeedle/TargetStage",
            "OpenUsdRepresentation/BearingTempNeedle/TargetUsdTypeName",
            "OpenUsdRepresentation/CavitationHalo",
            "OpenUsdRepresentation/CavitationHalo/AlarmAspect",
            "OpenUsdRepresentation/CavitationHalo/BadQualityAction",
            "OpenUsdRepresentation/CavitationHalo/BindingDefinitionId",
            "OpenUsdRepresentation/CavitationHalo/Enabled",
            "OpenUsdRepresentation/CavitationHalo/RenderTargetKind",
            "OpenUsdRepresentation/CavitationHalo/Scale",
            "OpenUsdRepresentation/CavitationHalo/SignalRole",
            "OpenUsdRepresentation/CavitationHalo/SourceNodeId",
            "OpenUsdRepresentation/CavitationHalo/TargetPrimPath",
            "OpenUsdRepresentation/CavitationHalo/TargetPropertyName",
            "OpenUsdRepresentation/CavitationHalo/TargetStage",
            "OpenUsdRepresentation/CavitationHalo/TargetUsdTypeName",
            "OpenUsdRepresentation/DiffPressureEmissive",
            "OpenUsdRepresentation/DiffPressureEmissive/BadQualityAction",
            "OpenUsdRepresentation/DiffPressureEmissive/BindingDefinitionId",
            "OpenUsdRepresentation/DiffPressureEmissive/Enabled",
            "OpenUsdRepresentation/DiffPressureEmissive/RenderTargetKind",
            "OpenUsdRepresentation/DiffPressureEmissive/Scale",
            "OpenUsdRepresentation/DiffPressureEmissive/SignalRole",
            "OpenUsdRepresentation/DiffPressureEmissive/SourceNodeId",
            "OpenUsdRepresentation/DiffPressureEmissive/TargetPrimPath",
            "OpenUsdRepresentation/DiffPressureEmissive/TargetPropertyName",
            "OpenUsdRepresentation/DiffPressureEmissive/TargetStage",
            "OpenUsdRepresentation/DiffPressureEmissive/TargetUsdTypeName",
            "OpenUsdRepresentation/DischargePressureNeedle",
            "OpenUsdRepresentation/DischargePressureNeedle/BadQualityAction",
            "OpenUsdRepresentation/DischargePressureNeedle/BindingDefinitionId",
            "OpenUsdRepresentation/DischargePressureNeedle/Enabled",
            "OpenUsdRepresentation/DischargePressureNeedle/RenderTargetKind",
            "OpenUsdRepresentation/DischargePressureNeedle/Scale",
            "OpenUsdRepresentation/DischargePressureNeedle/SignalRole",
            "OpenUsdRepresentation/DischargePressureNeedle/SourceNodeId",
            "OpenUsdRepresentation/DischargePressureNeedle/TargetPrimPath",
            "OpenUsdRepresentation/DischargePressureNeedle/TargetPropertyName",
            "OpenUsdRepresentation/DischargePressureNeedle/TargetStage",
            "OpenUsdRepresentation/DischargePressureNeedle/TargetUsdTypeName",
            "OpenUsdRepresentation/EfficiencyReadout",
            "OpenUsdRepresentation/EfficiencyReadout/BadQualityAction",
            "OpenUsdRepresentation/EfficiencyReadout/BindingDefinitionId",
            "OpenUsdRepresentation/EfficiencyReadout/Enabled",
            "OpenUsdRepresentation/EfficiencyReadout/RenderTargetKind",
            "OpenUsdRepresentation/EfficiencyReadout/Scale",
            "OpenUsdRepresentation/EfficiencyReadout/SignalRole",
            "OpenUsdRepresentation/EfficiencyReadout/SourceNodeId",
            "OpenUsdRepresentation/EfficiencyReadout/TargetPrimPath",
            "OpenUsdRepresentation/EfficiencyReadout/TargetPropertyName",
            "OpenUsdRepresentation/EfficiencyReadout/TargetStage",
            "OpenUsdRepresentation/EfficiencyReadout/TargetUsdTypeName",
            "OpenUsdRepresentation/FluidTempColor",
            "OpenUsdRepresentation/FluidTempColor/BadQualityAction",
            "OpenUsdRepresentation/FluidTempColor/BindingDefinitionId",
            "OpenUsdRepresentation/FluidTempColor/Enabled",
            "OpenUsdRepresentation/FluidTempColor/Offset",
            "OpenUsdRepresentation/FluidTempColor/RenderTargetKind",
            "OpenUsdRepresentation/FluidTempColor/Scale",
            "OpenUsdRepresentation/FluidTempColor/SignalRole",
            "OpenUsdRepresentation/FluidTempColor/SourceNodeId",
            "OpenUsdRepresentation/FluidTempColor/TargetPrimPath",
            "OpenUsdRepresentation/FluidTempColor/TargetPropertyName",
            "OpenUsdRepresentation/FluidTempColor/TargetStage",
            "OpenUsdRepresentation/FluidTempColor/TargetUsdTypeName",
            "OpenUsdRepresentation/ImpellerComponent",
            "OpenUsdRepresentation/ImpellerComponent/BindingDefinitionId",
            "OpenUsdRepresentation/ImpellerComponent/Cardinality",
            "OpenUsdRepresentation/ImpellerComponent/ComponentRepresentation",
            "OpenUsdRepresentation/ImpellerComponent/CompositionArc",
            "OpenUsdRepresentation/ImpellerComponent/Enabled",
            "OpenUsdRepresentation/ImpellerComponent/TargetPrimPath",
            "OpenUsdRepresentation/MassFlowReadout",
            "OpenUsdRepresentation/MassFlowReadout/BadQualityAction",
            "OpenUsdRepresentation/MassFlowReadout/BindingDefinitionId",
            "OpenUsdRepresentation/MassFlowReadout/Enabled",
            "OpenUsdRepresentation/MassFlowReadout/RenderTargetKind",
            "OpenUsdRepresentation/MassFlowReadout/Scale",
            "OpenUsdRepresentation/MassFlowReadout/SignalRole",
            "OpenUsdRepresentation/MassFlowReadout/SourceNodeId",
            "OpenUsdRepresentation/MassFlowReadout/SourceSemanticId",
            "OpenUsdRepresentation/MassFlowReadout/TargetPrimPath",
            "OpenUsdRepresentation/MassFlowReadout/TargetPropertyName",
            "OpenUsdRepresentation/MassFlowReadout/TargetStage",
            "OpenUsdRepresentation/MassFlowReadout/TargetUsdTypeName",
            "OpenUsdRepresentation/MotorFanSpin",
            "OpenUsdRepresentation/MotorFanSpin/BadQualityAction",
            "OpenUsdRepresentation/MotorFanSpin/BindingDefinitionId",
            "OpenUsdRepresentation/MotorFanSpin/Enabled",
            "OpenUsdRepresentation/MotorFanSpin/RenderTargetKind",
            "OpenUsdRepresentation/MotorFanSpin/Scale",
            "OpenUsdRepresentation/MotorFanSpin/SignalRole",
            "OpenUsdRepresentation/MotorFanSpin/SourceNodeId",
            "OpenUsdRepresentation/MotorFanSpin/TargetPrimPath",
            "OpenUsdRepresentation/MotorFanSpin/TargetPropertyName",
            "OpenUsdRepresentation/MotorFanSpin/TargetStage",
            "OpenUsdRepresentation/MotorFanSpin/TargetUsdTypeName",
            "OpenUsdRepresentation/NumberOfStartsReadout",
            "OpenUsdRepresentation/NumberOfStartsReadout/BadQualityAction",
            "OpenUsdRepresentation/NumberOfStartsReadout/BindingDefinitionId",
            "OpenUsdRepresentation/NumberOfStartsReadout/Enabled",
            "OpenUsdRepresentation/NumberOfStartsReadout/RenderTargetKind",
            "OpenUsdRepresentation/NumberOfStartsReadout/Scale",
            "OpenUsdRepresentation/NumberOfStartsReadout/SignalRole",
            "OpenUsdRepresentation/NumberOfStartsReadout/SourceNodeId",
            "OpenUsdRepresentation/NumberOfStartsReadout/TargetPrimPath",
            "OpenUsdRepresentation/NumberOfStartsReadout/TargetPropertyName",
            "OpenUsdRepresentation/NumberOfStartsReadout/TargetStage",
            "OpenUsdRepresentation/NumberOfStartsReadout/TargetUsdTypeName",
            "OpenUsdRepresentation/OverheatHalo",
            "OpenUsdRepresentation/OverheatHalo/AlarmAspect",
            "OpenUsdRepresentation/OverheatHalo/BadQualityAction",
            "OpenUsdRepresentation/OverheatHalo/BindingDefinitionId",
            "OpenUsdRepresentation/OverheatHalo/Enabled",
            "OpenUsdRepresentation/OverheatHalo/RenderTargetKind",
            "OpenUsdRepresentation/OverheatHalo/Scale",
            "OpenUsdRepresentation/OverheatHalo/SignalRole",
            "OpenUsdRepresentation/OverheatHalo/SourceNodeId",
            "OpenUsdRepresentation/OverheatHalo/TargetPrimPath",
            "OpenUsdRepresentation/OverheatHalo/TargetPropertyName",
            "OpenUsdRepresentation/OverheatHalo/TargetStage",
            "OpenUsdRepresentation/OverheatHalo/TargetUsdTypeName",
            "OpenUsdRepresentation/PrimPath",
            "OpenUsdRepresentation/ShaftSpin",
            "OpenUsdRepresentation/ShaftSpin/BadQualityAction",
            "OpenUsdRepresentation/ShaftSpin/BindingDefinitionId",
            "OpenUsdRepresentation/ShaftSpin/Enabled",
            "OpenUsdRepresentation/ShaftSpin/RenderTargetKind",
            "OpenUsdRepresentation/ShaftSpin/Scale",
            "OpenUsdRepresentation/ShaftSpin/SignalRole",
            "OpenUsdRepresentation/ShaftSpin/SourceNodeId",
            "OpenUsdRepresentation/ShaftSpin/SourceSemanticId",
            "OpenUsdRepresentation/ShaftSpin/TargetPrimPath",
            "OpenUsdRepresentation/ShaftSpin/TargetPropertyName",
            "OpenUsdRepresentation/ShaftSpin/TargetStage",
            "OpenUsdRepresentation/ShaftSpin/TargetUsdTypeName",
            "OpenUsdRepresentation/SpeedSetpointCommand",
            "OpenUsdRepresentation/SpeedSetpointCommand/BadQualityAction",
            "OpenUsdRepresentation/SpeedSetpointCommand/BindingDefinitionId",
            "OpenUsdRepresentation/SpeedSetpointCommand/CommandTargetNodeId",
            "OpenUsdRepresentation/SpeedSetpointCommand/CommandTriggerPropertyName",
            "OpenUsdRepresentation/SpeedSetpointCommand/Enabled",
            "OpenUsdRepresentation/SpeedSetpointCommand/Scale",
            "OpenUsdRepresentation/SpeedSetpointCommand/SignalRole",
            "OpenUsdRepresentation/SpeedSetpointCommand/TargetPrimPath",
            "OpenUsdRepresentation/SpeedSetpointCommand/TargetPropertyName",
            "OpenUsdRepresentation/SpeedSetpointCommand/TargetStage",
            "OpenUsdRepresentation/SpeedSetpointCommand/TargetUsdTypeName",
            "OpenUsdRepresentation/Stage",
            "OpenUsdRepresentation/SuctionLevelRise",
            "OpenUsdRepresentation/SuctionLevelRise/BadQualityAction",
            "OpenUsdRepresentation/SuctionLevelRise/BindingDefinitionId",
            "OpenUsdRepresentation/SuctionLevelRise/Enabled",
            "OpenUsdRepresentation/SuctionLevelRise/RenderTargetKind",
            "OpenUsdRepresentation/SuctionLevelRise/Scale",
            "OpenUsdRepresentation/SuctionLevelRise/SignalRole",
            "OpenUsdRepresentation/SuctionLevelRise/SourceNodeId",
            "OpenUsdRepresentation/SuctionLevelRise/TargetPrimPath",
            "OpenUsdRepresentation/SuctionLevelRise/TargetPropertyName",
            "OpenUsdRepresentation/SuctionLevelRise/TargetStage",
            "OpenUsdRepresentation/SuctionLevelRise/TargetUsdTypeName",
            "Operational",
            "Operational/Measurements",
            "Operational/Measurements/BearingTemperature",
            "Operational/Measurements/BearingTemperature/EURange",
            "Operational/Measurements/BearingTemperature/EngineeringUnits",
            "Operational/Measurements/DifferentialPressure",
            "Operational/Measurements/DifferentialPressure/EURange",
            "Operational/Measurements/DifferentialPressure/EngineeringUnits",
            "Operational/Measurements/FluidTemperature",
            "Operational/Measurements/FluidTemperature/EURange",
            "Operational/Measurements/FluidTemperature/EngineeringUnits",
            "Operational/Measurements/Level",
            "Operational/Measurements/Level/EURange",
            "Operational/Measurements/Level/EngineeringUnits",
            "Operational/Measurements/MassFlow",
            "Operational/Measurements/MassFlow/EURange",
            "Operational/Measurements/MassFlow/EngineeringUnits",
            "Operational/Measurements/NumberOfStarts",
            "Operational/Measurements/PumpEfficiency",
            "Operational/Measurements/PumpEfficiency/EURange",
            "Operational/Measurements/PumpEfficiency/EngineeringUnits",
            "Operational/Measurements/PumpPowerInput",
            "Operational/Measurements/PumpPowerInput/EURange",
            "Operational/Measurements/PumpPowerInput/EngineeringUnits",
            "ShaftAngle",
            "SpeedSetpoint"
        ];

        /// <summary>
        /// The alarm subtree is built by the fluent alarm builder rather than by
        /// the generated instance helpers, so its descendants keep the standard
        /// (namespace 0) declaration NodeIds. Excluded from the NodeId
        /// assertions and pinned separately by
        /// <see cref="AlarmSubtreeKeepsStandardDeclarationNodeIds"/>.
        /// </summary>
        private const string AlarmSubtreePrefix = "Events/OverTempAlarm/";

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
            m_manager = new PumpNodeManager(server.CurrentInstance, m_fixture.Config);
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await m_manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);

            m_configuredPump = m_manager.FindPredefinedNode<PumpState>(
                new NodeId("5001_Pump_1", m_manager.InstanceNamespaceIndex));
            m_secondPump = m_manager.FindPredefinedNode<PumpState>(
                new NodeId("5001_Pump_2", m_manager.InstanceNamespaceIndex));
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            m_manager?.Dispose();
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Every model identifier the pump exposed before the generated helpers
        /// took over NodeId assignment must still be materialised.
        /// </summary>
        [Test]
        public void ConfiguredPumpExposesTheFullModelSurface()
        {
            List<PumpNode> nodes = CollectSubtree(m_configuredPump!);

            Assert.That(
                nodes.Select(node => node.Path).OrderBy(path => path, StringComparer.Ordinal),
                Is.EqualTo(s_expectedPumpSurface.OrderBy(path => path, StringComparer.Ordinal)));
        }

        /// <summary>
        /// Every configured pump, including pumps created after startup, must
        /// expose the same generated and fluent-wired simulation surface as the
        /// initially configured pump.
        /// </summary>
        [Test]
        public void EveryPumpInstanceExposesTheSameSimulationSurface()
        {
            IEnumerable<string> configured = CollectSubtree(m_configuredPump!)
                .Select(node => node.Path);
            IEnumerable<string> second = CollectSubtree(m_secondPump!)
                .Select(node => node.Path);

            Assert.That(second, Is.EquivalentTo(configured));
            Assert.That(second, Contains.Item("Identification/SerialNumber"));
            Assert.That(second, Contains.Item("Operational/Measurements/PumpEfficiency"));
            Assert.That(second, Contains.Item("Events/OverTempAlarm"));
            Assert.That(
                second,
                Contains.Item("Events/SupervisionPumpOperation/MotorOverheat/TrueState"));
        }

        /// <summary>
        /// Every node the generated helpers materialise must carry a NodeId
        /// minted by <see cref="PumpNodeManager.New"/> - that is
        /// <c>{parentIdentifier}_{symbolicName}</c> in the server instance
        /// namespace - rather than the type-level declaration NodeId the model ships.
        /// </summary>
        [Test]
        public void MaterialisedPumpNodeIdsComeFromTheNodeIdFactory()
        {
            var offenders = new List<string>();
            foreach (PumpState pump in new[] { m_configuredPump!, m_secondPump! })
            {
                Assert.That(pump.NodeId.IdentifierAsString, Is.EqualTo(
                    "5001_" + pump.BrowseName.Name),
                    "The pump root must be minted from the DeviceSet parent.");

                foreach (PumpNode node in CollectSubtree(pump).Where(IsGeneratedHelperNode))
                {
                    NodeId expected = new(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}_{1}",
                            node.Parent.NodeId.IdentifierAsString,
                            node.State.SymbolicName),
                        m_manager!.InstanceNamespaceIndex);
                    if (node.State.NodeId != expected)
                    {
                        offenders.Add($"{pump.BrowseName.Name}/{node.Path}: " +
                            $"{node.State.NodeId} (expected {expected})");
                    }
                }
            }

            Assert.That(offenders, Is.Empty);
        }

        /// <summary>
        /// Two instances of the same type must never collide on a NodeId.
        /// </summary>
        [Test]
        public void MaterialisedPumpNodeIdsAreUniqueAcrossInstances()
        {
            List<NodeId> nodeIds =
            [
                m_configuredPump!.NodeId,
                m_secondPump!.NodeId,
                .. CollectSubtree(m_configuredPump!)
                    .Where(IsGeneratedHelperNode)
                    .Select(node => node.State.NodeId),
                .. CollectSubtree(m_secondPump!)
                    .Where(IsGeneratedHelperNode)
                    .Select(node => node.State.NodeId)
            ];

            Assert.That(nodeIds, Has.None.Matches<NodeId>(nodeId => nodeId.IsNull));
            Assert.That(nodeIds.Distinct().Count(), Is.EqualTo(nodeIds.Count));
        }

        [Test]
        public void EveryPumpExposesTheEventNotifierHierarchy()
        {
            foreach (PumpState pump in new[] { m_configuredPump!, m_secondPump! })
            {
                SupervisionState events = pump.Events!;
                Assert.Multiple(() =>
                {
                    Assert.That(
                        pump.EventNotifier & EventNotifiers.SubscribeToEvents,
                        Is.Not.Zero);
                    Assert.That(
                        events.EventNotifier & EventNotifiers.SubscribeToEvents,
                        Is.Not.Zero);
                });

                var pumpReferences = new List<IReference>();
                pump.GetReferences(m_manager!.SystemContext, pumpReferences);
                Assert.That(
                    pumpReferences,
                    Has.Exactly(1).Matches<IReference>(reference =>
                        reference.ReferenceTypeId == Opc.Ua.Types.ReferenceTypeIds.HasNotifier &&
                        !reference.IsInverse &&
                        reference.TargetId == events.NodeId));

                BaseInstanceState alarm = CollectSubtree(pump)
                    .Single(node => node.Path == "Events/OverTempAlarm")
                    .State;
                var eventReferences = new List<IReference>();
                events.GetReferences(m_manager.SystemContext, eventReferences);
                Assert.Multiple(() =>
                {
                    Assert.That(
                        eventReferences,
                        Has.Exactly(1).Matches<IReference>(reference =>
                            reference.ReferenceTypeId == Opc.Ua.Types.ReferenceTypeIds.HasNotifier &&
                            reference.IsInverse &&
                            reference.TargetId == pump.NodeId));
                    Assert.That(
                        eventReferences,
                        Has.Exactly(1).Matches<IReference>(reference =>
                            reference.ReferenceTypeId == Opc.Ua.Types.ReferenceTypeIds.HasEventSource &&
                            !reference.IsInverse &&
                            reference.TargetId == alarm.NodeId));
                });
            }
        }

        /// <summary>
        /// Documents a pre-existing gap this change does not alter: the alarm
        /// the fluent builder attaches keeps the standard declaration NodeIds
        /// for its condition children, because it is materialised outside the
        /// generated instance helpers.
        /// </summary>
        [Test]
        public void AlarmSubtreeKeepsStandardDeclarationNodeIds()
        {
            List<PumpNode> alarmNodes = [.. CollectSubtree(m_configuredPump!)
                .Where(node => node.Path.StartsWith(AlarmSubtreePrefix, StringComparison.Ordinal))];

            Assert.That(alarmNodes, Is.Not.Empty);
            Assert.That(
                alarmNodes.Where(node => !node.State.NodeId.IsNull),
                Has.All.Matches<PumpNode>(node => node.State.NodeId.NamespaceIndex == 0));
        }

        /// <summary>
        /// The generated factory must hand back a subtree that already carries
        /// per-instance NodeIds. The other tests observe the address space
        /// after registration, where
        /// <c>AsyncCustomNodeManager.AddPredefinedNodeAsync</c> applies a
        /// defensive rebase; this one asserts the helper on its own so a
        /// regression there is not masked by that safety net.
        /// </summary>
        [Test]
        public void GeneratedFactoryAssignsInstanceNodeIdsBeforeRegistration()
        {
            NodeState deviceSet = m_manager!.FindPredefinedNode<BaseObjectState>(
                NodeId.Create(
                    global::Opc.Ua.Di.Objects.DeviceSet,
                    global::Opc.Ua.Di.Namespaces.OpcUaDi,
                    m_manager.Server.NamespaceUris));
            // Deliberately not registered with the manager - this asserts the
            // state the source-generated factory produces on its own.
            PumpState pump = m_manager.SystemContext.CreateInstanceOfPumpType(
                deviceSet,
                new QualifiedName("Pump_3", m_manager.InstanceNamespaceIndex));

            Assert.That(
                pump.NodeId,
                Is.EqualTo(new NodeId("5001_Pump_3", m_manager.InstanceNamespaceIndex)));
            Assert.That(pump.Identification, Is.Not.Null);
            Assert.That(
                ((NodeState)pump.Identification!).NodeId,
                Is.EqualTo(new NodeId(
                    "5001_Pump_3_Identification",
                    m_manager.InstanceNamespaceIndex)));

            List<PumpNode> nodes = CollectSubtree(pump);
            Assert.That(nodes, Is.Not.Empty);
            Assert.That(
                nodes,
                Has.All.Matches<PumpNode>(node => node.State.NodeId == new NodeId(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}_{1}",
                        node.Parent.NodeId.IdentifierAsString,
                        node.State.SymbolicName),
                    m_manager.InstanceNamespaceIndex)));
        }

        private static bool IsGeneratedHelperNode(PumpNode node)
        {
            return !node.Path.StartsWith(AlarmSubtreePrefix, StringComparison.Ordinal);
        }

        private List<PumpNode> CollectSubtree(NodeState root)
        {
            var nodes = new List<PumpNode>();
            Collect(root, string.Empty, nodes);
            return nodes;
        }

        private void Collect(NodeState node, string prefix, List<PumpNode> nodes)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_manager!.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                string path = prefix.Length == 0
                    ? child.SymbolicName ?? string.Empty
                    : prefix + "/" + child.SymbolicName;
                nodes.Add(new PumpNode(path, child, node));
                Collect(child, path, nodes);
            }
        }

        private sealed record PumpNode(string Path, BaseInstanceState State, NodeState Parent);

        private ServerFixture<StandardServer>? m_fixture;
        private PumpNodeManager? m_manager;
        private PumpState? m_configuredPump;
        private PumpState? m_secondPump;
    }
}
