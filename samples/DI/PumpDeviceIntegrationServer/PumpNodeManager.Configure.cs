/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
// IDE0005 false positives below: PumpState needs Opc.Ua.Pumps; INodeManagerBuilder /
// NodeManagerBuilder need Opc.Ua.Server.Fluent (verified: removal causes CS0246).
#pragma warning disable IDE0005
using Opc.Ua.Pumps;
using Opc.Ua.Server.Fluent;
#pragma warning restore IDE0005
using Opc.Ua.Server.Historian;

namespace Pumps
{
    /// <summary>
    /// Sibling partial that wires per-node callbacks for the
    /// <see cref="PumpNodeManager"/> using the fluent builder.
    /// Demonstrates the OPC 40223 Pumps companion specification
    /// with a full simulation loop.
    /// </summary>
    /// <remarks>
    /// The node manager is hand-written and uses the fluent
    /// <see cref="INodeManagerBuilder"/> API to wire per-node
    /// callbacks. See <see cref="Configure"/> for the entry point.
    /// </remarks>
    public partial class PumpNodeManager
    {
        partial void Configure(INodeManagerBuilder builder)
        {
            Server.Telemetry.CreateLogger<PumpNodeManager>()
                .ConfiguringPumpNodeManagerFluentWiring();

            builder.UseHistorian()
                .UseInMemoryProvider()
                .RegisterAsDefault();

            foreach (PumpState pump in m_pumpStates)
            {
                RegisterPumpSimulation(builder, pump);
            }

            builder.Simulation(SimulationInterval)
                .OnTick((ctx, elapsed) => AdvanceSimulation());
        }

        /// <summary>
        /// Wires a pump created after the initial fluent configuration into
        /// the already-running manager simulation.
        /// </summary>
        /// <param name="pump">The registered pump instance.</param>
        private void RegisterPumpSimulation(PumpState pump)
        {
            ushort pumpsNs = (ushort)Server.NamespaceUris.GetIndex(
                Opc.Ua.Pumps.Namespaces.Pumps);
            NodeManagerBuilder builder = CreateFluentBuilder(pumpsNs);
            RegisterPumpSimulation(builder, pump);
            builder.Seal();
        }

        /// <summary>
        /// Configures the variable updaters, alarms, and phase profile for one
        /// pump instance.
        /// </summary>
        /// <param name="builder">The active fluent builder.</param>
        /// <param name="pump">The pump to configure.</param>
        /// <exception cref="ServiceResultException"></exception>
        private void RegisterPumpSimulation(
            INodeManagerBuilder builder,
            PumpState pump)
        {
            lock (m_simulationRegistrationLock)
            {
                if (m_pumpSimulations.ContainsKey(pump.NodeId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Pump '{0}' (id '{1}') already has a simulation registered.",
                        pump.BrowseName,
                        pump.NodeId);
                }

                int profileIndex = m_nextSimulationProfile++;
                WithIdentification(builder, pump, profileIndex + 1);
                WithMaintenance(builder, pump);
                PumpSimulationState simulation = CreatePumpSimulation(
                    builder,
                    pump,
                    profileIndex);
                simulation.Initialize(Volatile.Read(ref m_simulationTicks));
                if (!m_pumpSimulations.TryAdd(pump.NodeId, simulation))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Pump '{0}' (id '{1}') already has a simulation registered.",
                        pump.BrowseName,
                        pump.NodeId);
                }

                // The OpenUSD twin of this pump renders this simulation.
                if (m_twins.TryGetValue(pump.NodeId, out PumpTwin? twin))
                {
                    twin.Simulation = simulation;
                }
            }
        }

        /// <summary>
        /// Configures the nameplate of one simulated unit with the
        /// identification data published in <c>DATASHEET.md</c>. The
        /// properties themselves are materialised by
        /// <see cref="MaterialiseNameplate"/>; this method
        /// only assigns their values through the fluent builder. Fields
        /// that identify the individual unit rather than the product are
        /// derived from <paramref name="pumpNumber"/>.
        /// </summary>
        /// <param name="builder">The active fluent builder.</param>
        /// <param name="pump">The pump whose nameplate is configured.</param>
        /// <param name="pumpNumber">One-based number of the simulated unit.</param>
        private static void WithIdentification(
            INodeManagerBuilder builder,
            PumpState pump,
            int pumpNumber)
        {
            string serialNumber = "SN-" + pumpNumber.ToString("D3", CultureInfo.InvariantCulture);
            string assetId = "PMP-" +
                (1000 + pumpNumber).ToString(CultureInfo.InvariantCulture);
            string componentName = "Feed Pump " + (char)('A' + ((pumpNumber - 1) % 26));
            string location = "Plant 1 / Utility Skid / Bay " +
                (pumpNumber + 2).ToString(CultureInfo.InvariantCulture);
            string fabricationNumber = "F-2025-" +
                pumpNumber.ToString("D4", CultureInfo.InvariantCulture);

            builder.Node(pump.Identification!.NodeId)
                .WithProperty(
                    "Manufacturer",
                    new LocalizedText(PumpDatasheet.Nameplate.Manufacturer))
                .WithProperty(
                    "ManufacturerUri",
                    PumpDatasheet.Nameplate.ManufacturerUri)
                .WithProperty(
                    "Model",
                    new LocalizedText(PumpDatasheet.Nameplate.Model))
                .WithProperty("ProductCode", PumpDatasheet.Nameplate.ProductCode)
                .WithProperty("DeviceClass", PumpDatasheet.Nameplate.DeviceClass)
                .WithProperty(
                    "HardwareRevision",
                    PumpDatasheet.Nameplate.HardwareRevision)
                .WithProperty(
                    "SoftwareRevision",
                    PumpDatasheet.Nameplate.SoftwareRevision)
                .WithProperty("SerialNumber", serialNumber)
                .WithProperty(
                    "ProductInstanceUri",
                    PumpDatasheet.Nameplate.ProductInstanceUriPrefix + serialNumber)
                .WithProperty("AssetId", assetId)
                .WithProperty("ComponentName", new LocalizedText(componentName))
                .WithProperty("Location", location)
                .WithProperty(
                    "YearOfConstruction",
                    PumpDatasheet.Nameplate.YearOfConstruction)
                .WithProperty(
                    "MonthOfConstruction",
                    PumpDatasheet.Nameplate.MonthOfConstruction)
                .WithProperty(
                    "DayOfConstruction",
                    PumpDatasheet.Nameplate.DayOfConstruction)
                .WithProperty(
                    "ArticleNumber",
                    PumpDatasheet.Nameplate.ArticleNumber)
                .WithProperty(
                    "OrderProductCode",
                    PumpDatasheet.Nameplate.OrderProductCode)
                .WithProperty(
                    "TypeOfProduct",
                    PumpDatasheet.Nameplate.TypeOfProduct)
                .WithProperty("Supplier", PumpDatasheet.Nameplate.Supplier)
                .WithProperty(
                    "CountryOfOrigin",
                    PumpDatasheet.Nameplate.CountryOfOrigin)
                .WithProperty("FabricationNumber", fabricationNumber);
        }

        private void WithMaintenance(INodeManagerBuilder builder, PumpState pump)
        {
            // IDE0007 vs IDE0008 disagree on this factory-call shape; keep the
            // explicit type (matches the pre-existing style at this call site).
#pragma warning disable IDE0007
            NodeId functionalGroupType = NodeId.Create(
                Opc.Ua.Di.ObjectTypes.FunctionalGroupType,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                Server.NamespaceUris);
#pragma warning restore IDE0007
            var generalMaintenance = new QualifiedName(
                "GeneralMaintenance",
                (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.Pumps.Namespaces.Pumps));

            builder.Node(pump.Maintenance!.NodeId)
                .AddObject(generalMaintenance, functionalGroupType)
                .WithProperty(
                    "MaintenancePlan",
                    Variant.From("Inspect seals and bearings every 30 days."));
        }

        private PumpSimulationState CreatePumpSimulation(
            INodeManagerBuilder builder,
            PumpState pump,
            int profileIndex)
        {
            MeasurementsState measurements = pump.Operational!.Measurements!;

            AddMeasurement(
                builder,
                measurements.DifferentialPressure!.NodeId,
                EngineeringUnits.Pascal,
                min: PumpDatasheet.Ranges.DifferentialPressureMin,
                max: PumpDatasheet.Ranges.DifferentialPressureMax,
                out IValueUpdater<double> pressure);
            AddMeasurement(
                builder,
                measurements.FluidTemperature!.NodeId,
                EngineeringUnits.Kelvin,
                min: PumpDatasheet.Ranges.FluidTemperatureMin,
                max: PumpDatasheet.Ranges.FluidTemperatureMax,
                out IValueUpdater<double> fluidTemperature);
            AddMeasurement(
                builder,
                measurements.BearingTemperature!.NodeId,
                EngineeringUnits.Kelvin,
                min: PumpDatasheet.Ranges.BearingTemperatureMin,
                max: PumpDatasheet.Ranges.BearingTemperatureMax,
                out IValueUpdater<double> bearingTemperature);
            AddMeasurement(
                builder,
                measurements.PumpPowerInput!.NodeId,
                EngineeringUnits.Watt,
                min: PumpDatasheet.Ranges.PumpPowerInputMin,
                max: PumpDatasheet.Ranges.PumpPowerInputMax,
                out IValueUpdater<double> power);
            AddMeasurement(
                builder,
                measurements.MassFlow!.NodeId,
                EngineeringUnits.KilogramsPerSecond,
                min: PumpDatasheet.Ranges.MassFlowMin,
                max: PumpDatasheet.Ranges.MassFlowMax,
                out IValueUpdater<double> flow);
            AddMeasurement(
                builder,
                measurements.PumpEfficiency!.NodeId,
                EngineeringUnits.Percent,
                min: PumpDatasheet.Ranges.PumpEfficiencyMin,
                max: PumpDatasheet.Ranges.PumpEfficiencyMax,
                out IValueUpdater<double> efficiency);
            AddMeasurement(
                builder,
                measurements.Level!.NodeId,
                EngineeringUnits.Metre,
                min: PumpDatasheet.Ranges.LevelMin,
                max: PumpDatasheet.Ranges.LevelMax,
                out IValueUpdater<double> level);

            builder.Variable<uint>(measurements.NumberOfStarts!.NodeId)
                .Bind(out IValueUpdater<uint> numberOfStarts)
                .Historize(historyAccessLevel: AccessLevels.HistoryRead);

            ushort pumpsNs = (ushort)Server.NamespaceUris.GetIndex(
                Opc.Ua.Pumps.Namespaces.Pumps);
            INodeBuilder<PumpState> pumpBuilder = builder.Node<PumpState>(pump.NodeId);
            INodeBuilder<SupervisionState> events = pumpBuilder.Components().Events();

            // The alarm reports the bearing-temperature chain, so its
            // source node is the measurement and its limits are the
            // datasheet trip points.
            IAlarmBuilder<NonExclusiveLimitAlarmState> overTempAlarm = events
                .CreateLimitAlarm(new QualifiedName("OverTempAlarm", pumpsNs))
                .WithLimits(
                    highHigh: PumpDatasheet.TripPoints.BearingTemperatureHighHigh,
                    high: PumpDatasheet.TripPoints.BearingTemperatureHigh,
                    low: PumpDatasheet.TripPoints.BearingTemperatureLow,
                    lowLow: PumpDatasheet.TripPoints.BearingTemperatureLowLow)
                .MonitorVariable(measurements.BearingTemperature)
                .OnAcknowledge((ctx, c, eventId, comment) => ServiceResult.Good);

            IVariableBuilder<bool> cavitationBuilder = events.Components()
                .SupervisionProcessFluid()
                .Components()
                .Cavitation();
            WireBoolean(
                cavitationBuilder,
                new LocalizedText("Cavitation"),
                new LocalizedText("No cavitation"),
                out IValueUpdater<bool> cavitation);

            IVariableBuilder<bool> motorOverheatBuilder = events.Components()
                .SupervisionPumpOperation()
                .Components()
                .MotorOverheat();
            WireBoolean(
                motorOverheatBuilder,
                new LocalizedText("Motor overheat"),
                new LocalizedText("No motor overheat"),
                out IValueUpdater<bool> motorOverheat);
            motorOverheatBuilder.ActivatesAlarm(overTempAlarm);

            return new PumpSimulationState(
                profileIndex,
                SimulationInterval,
                pressure,
                fluidTemperature,
                bearingTemperature,
                power,
                flow,
                efficiency,
                level,
                numberOfStarts,
                cavitation,
                motorOverheat);
        }

        private static void AddMeasurement(
            INodeManagerBuilder builder,
            NodeId nodeId,
            EUInformation units,
            double min,
            double max,
            out IValueUpdater<double> updater)
        {
            builder.Variable<double>(nodeId)
                .Bind(out updater)
                .WithEngineeringUnits(units)
                .WithEURange(min, max)
                .Historize(
                    historyAccessLevel: AccessLevels.HistoryRead,
                    autoCapture: true,
                    captureOptions: s_historianCaptureOptions);
        }

        private static void WireBoolean(
            IVariableBuilder<bool> variable,
            LocalizedText trueState,
            LocalizedText falseState,
            out IValueUpdater<bool> updater)
        {
            variable.Bind(out updater)
                .Historize(historyAccessLevel: AccessLevels.HistoryRead);

            BaseVariableState variableState = variable.Node;
            NodeState? trueStateNode = variableState.FindChild(
                variable.Builder.Context,
                new QualifiedName("TrueState"));
            if (trueStateNode is BaseVariableState trueStateVariable)
            {
                trueStateVariable.WrappedValue = Variant.From(trueState);
            }

            NodeState? falseStateNode = variableState.FindChild(
                variable.Builder.Context,
                new QualifiedName("FalseState"));
            if (falseStateNode is BaseVariableState falseStateVariable)
            {
                falseStateVariable.WrappedValue = Variant.From(falseState);
            }
        }

        private static readonly HistorianCaptureOptions s_historianCaptureOptions = new()
        {
            MaxQueuedSamples = 8192,
            BatchTarget = 128,
            BatchWindow = TimeSpan.FromMilliseconds(50),
            FullMode = CaptureFullMode.DropOldest
        };

        private void AdvanceSimulation()
        {
            long tick = Interlocked.Increment(ref m_simulationTicks);
            foreach (PumpSimulationState simulation in m_pumpSimulations.Values)
            {
                simulation.Advance(tick);
            }
            PublishOpenUsdSignals();
        }

        /// <summary>
        /// Adds a Variable to the set the simulation tick publishes.
        /// </summary>
        /// <param name="variable">The variable to publish every tick.</param>
        /// <param name="getter">Yields the latest value.</param>
        /// <remarks>
        /// The OpenUSD signals are created directly rather than through the
        /// fluent builder, so they have no <see cref="IValueUpdater{TValue}"/>
        /// of their own. A variable wired with only an <c>OnRead</c> handler
        /// serves polled reads but never raises a data change, which leaves a
        /// twin driven from a subscription rendering the start-up value
        /// forever; publishing here is what makes monitored items observe them.
        /// </remarks>
        private void TrackSignal(BaseVariableState variable, Func<double> getter)
        {
            lock (m_simulationRegistrationLock)
            {
                m_liveSignals = [.. m_liveSignals, (variable, getter)];
            }
        }

        private void PublishOpenUsdSignals()
        {
            DateTime now = DateTime.UtcNow;
            foreach ((BaseVariableState variable, Func<double> getter) in
                Volatile.Read(ref m_liveSignals))
            {
                variable.Value = getter();
                variable.Timestamp = now;
                variable.ClearChangeMasks(SystemContext, includeChildren: false);
            }

            foreach (PumpTwin twin in m_twins.Values)
            {
                BaseDataVariableState? alarm = twin.AlarmActive;
                if (alarm != null)
                {
                    alarm.Value = twin.AlarmActiveState;
                    alarm.Timestamp = now;
                    alarm.ClearChangeMasks(SystemContext, includeChildren: false);
                }

                BaseDataVariableState? surface = twin.FluidSurface;
                if (surface != null)
                {
                    surface.Value = new ExtensionObject(
                        FluidSurfaceAt(twin.Simulation?.LevelMetres ??
                            PumpDatasheet.Simulation.LevelNominal));
                    surface.Timestamp = now;
                    surface.ClearChangeMasks(SystemContext, includeChildren: false);
                }
            }
        }

        private static class EngineeringUnits
        {
            public static readonly EUInformation Pascal =
                new("Pa", "Pascal", "http://www.opcfoundation.org/UA/units/un/cefact");

            public static readonly EUInformation Kelvin =
                new("K", "Kelvin", "http://www.opcfoundation.org/UA/units/un/cefact");

            public static readonly EUInformation Watt =
                new("W", "Watt", "http://www.opcfoundation.org/UA/units/un/cefact");

            public static readonly EUInformation KilogramsPerSecond =
                new("kg/s", "Kilograms per Second", "http://www.opcfoundation.org/UA/units/un/cefact");

            public static readonly EUInformation Percent =
                new("%", "Percent", "http://www.opcfoundation.org/UA/units/un/cefact");

            public static readonly EUInformation Metre =
                new("m", "Metre", "http://www.opcfoundation.org/UA/units/un/cefact");
        }

        private sealed class PumpSimulationState
        {
            public PumpSimulationState(
                int profileIndex,
                TimeSpan tickInterval,
                IValueUpdater<double> pressure,
                IValueUpdater<double> fluidTemperature,
                IValueUpdater<double> bearingTemperature,
                IValueUpdater<double> power,
                IValueUpdater<double> flow,
                IValueUpdater<double> efficiency,
                IValueUpdater<double> level,
                IValueUpdater<uint> numberOfStarts,
                IValueUpdater<bool> cavitation,
                IValueUpdater<bool> motorOverheat)
            {
                m_phaseOffset =
                    profileIndex * PumpDatasheet.Simulation.PhaseOffsetTicks;
                m_tickInterval = tickInterval;
                m_pressure = pressure;
                m_fluidTemperature = fluidTemperature;
                m_bearingTemperature = bearingTemperature;
                m_power = power;
                m_flow = flow;
                m_efficiency = efficiency;
                m_level = level;
                m_numberOfStarts = numberOfStarts;
                m_cavitation = cavitation;
                m_motorOverheat = motorOverheat;
            }

            public void Initialize(long tick)
            {
                Publish(tick, publishAll: true, StatusCodes.BadWaitingForInitialData);
            }

            public void Advance(long tick)
            {
                Publish(tick, publishAll: !m_hasPublishedGoodValue, StatusCodes.Good);
                m_hasPublishedGoodValue = true;
            }

            /// <summary>
            /// Publishes one simulation step. Volumetric flow is the only
            /// independent variable; head, differential pressure, mass
            /// flow, efficiency and shaft power all follow from the
            /// datasheet characteristic curves, so the published values
            /// stay mutually consistent.
            /// </summary>
            private void Publish(long tick, bool publishAll, StatusCode statusCode)
            {
                long localTick = tick + m_phaseOffset;
                DateTime sourceTimestamp = DateTime.UtcNow;

                double flow = PumpDatasheet.Hydraulics.RatedFlow *
                    (1.0 +
                        (PumpDatasheet.Simulation.FlowModulation *
                            Math.Sin(localTick * PumpDatasheet.Simulation.FlowRate)));
                double head = Head(flow);
                double efficiency = Efficiency(flow);
                double massFlow = PumpDatasheet.Hydraulics.FluidDensity *
                    flow /
                    3600.0;
                double differentialPressure =
                    PumpDatasheet.Hydraulics.FluidDensity *
                    PumpDatasheet.Hydraulics.GravitationalAcceleration *
                    head;
                double shaftPower = differentialPressure * (flow / 3600.0) /
                    (efficiency / 100.0);
                double bearingTemperature =
                    PumpDatasheet.Simulation.BearingTemperatureBase +
                    (PumpDatasheet.Simulation.BearingTemperatureLoadRise *
                        shaftPower /
                        PumpDatasheet.Hydraulics.RatedShaftPower) +
                    CoolingFaultExcursion(localTick);
                double level = PumpDatasheet.Simulation.LevelNominal +
                    (PumpDatasheet.Simulation.LevelAmplitude *
                        Math.Sin(localTick * PumpDatasheet.Simulation.LevelRate));
                Volatile.Write(ref m_levelMetres, level);

                m_pressure.SetValue(differentialPressure, statusCode, sourceTimestamp);
                m_flow.SetValue(massFlow, statusCode, sourceTimestamp);
                m_efficiency.SetValue(efficiency, statusCode, sourceTimestamp);
                m_power.SetValue(shaftPower, statusCode, sourceTimestamp);
                m_bearingTemperature.SetValue(
                    bearingTemperature,
                    statusCode,
                    sourceTimestamp);
                m_level.SetValue(level, statusCode, sourceTimestamp);
                m_fluidTemperature.SetValue(
                    PumpDatasheet.Simulation.FluidTemperatureNominal +
                    (PumpDatasheet.Simulation.FluidTemperatureAmplitude *
                        Math.Sin(
                            localTick *
                            PumpDatasheet.Simulation.FluidTemperatureRate)),
                    statusCode,
                    sourceTimestamp);

                // Integrate the shaft position from the running speed so the
                // twin shows the pump actually turning. Speed follows flow, so
                // the impeller visibly slows and picks up with the duty point.
                // MassFlow is a rate: binding it straight to a rotation op
                // would pin the shaft at a fraction of a degree instead.
                // Unbounded on purpose - wrapping at 360 would make a client
                // that interpolates between samples spin the shaft backwards
                // across the wrap.
                double rpm = PumpDatasheet.Hydraulics.RatedSpeed *
                    (massFlow / PumpDatasheet.Hydraulics.RatedMassFlow);
                double seconds = m_tickInterval.TotalSeconds;
                Volatile.Write(
                    ref m_shaftAngle,
                    Volatile.Read(ref m_shaftAngle) + (rpm * 6.0 * seconds));

                uint numberOfStarts = checked((uint)(localTick /
                    PumpDatasheet.Simulation.StartIntervalTicks));
                if (publishAll || numberOfStarts != m_currentNumberOfStarts)
                {
                    m_currentNumberOfStarts = numberOfStarts;
                    m_numberOfStarts.SetValue(numberOfStarts, statusCode, sourceTimestamp);
                }

                // Cavitation is reported once the suction head falls below
                // the NPSH requirement, with hysteresis so the supervision
                // state does not chatter at the threshold.
                bool cavitation = m_currentCavitation
                    ? level < PumpDatasheet.TripPoints.CavitationClearLevel
                    : level < PumpDatasheet.TripPoints.CavitationSetLevel;
                if (publishAll || cavitation != m_currentCavitation)
                {
                    m_currentCavitation = cavitation;
                    m_cavitation.SetValue(cavitation, statusCode, sourceTimestamp);
                }

                bool motorOverheat = m_currentMotorOverheat
                    ? bearingTemperature >=
                        PumpDatasheet.TripPoints.MotorOverheatClear
                    : bearingTemperature >=
                        PumpDatasheet.TripPoints.MotorOverheatSet;
                if (publishAll || motorOverheat != m_currentMotorOverheat)
                {
                    m_currentMotorOverheat = motorOverheat;
                    m_motorOverheat.SetValue(motorOverheat, statusCode, sourceTimestamp);
                }
            }

            /// <summary>
            /// Head curve of the datasheet, in m for a flow in m&#179;/h.
            /// </summary>
            private static double Head(double flow)
            {
                return PumpDatasheet.Hydraulics.ShutoffHead -
                    (PumpDatasheet.Hydraulics.HeadCurveCoefficient * flow * flow);
            }

            /// <summary>
            /// Efficiency curve of the datasheet, in percent for a flow in
            /// m&#179;/h.
            /// </summary>
            private static double Efficiency(double flow)
            {
                double deviation =
                    (flow - PumpDatasheet.Hydraulics.RatedFlow) /
                    PumpDatasheet.Hydraulics.RatedFlow;
                return PumpDatasheet.Hydraulics.RatedEfficiency *
                    (1.0 -
                        (PumpDatasheet.Hydraulics.EfficiencyCurveFactor *
                            deviation *
                            deviation));
            }

            /// <summary>
            /// Bearing temperature rise in K caused by the periodic
            /// bearing-cooling interruption documented in the datasheet.
            /// The ramp crosses both the high and the high-high trip point
            /// so a complete alarm cycle is observable.
            /// </summary>
            private static double CoolingFaultExcursion(long tick)
            {
                long cycle = tick % PumpDatasheet.Simulation.CoolingFaultPeriodTicks;
                if (cycle < PumpDatasheet.Simulation.CoolingFaultOnsetTick)
                {
                    return 0.0;
                }
                const long rampTicks = PumpDatasheet.Simulation.CoolingFaultPeriodTicks -
                    PumpDatasheet.Simulation.CoolingFaultOnsetTick;
                return PumpDatasheet.Simulation.CoolingFaultRise *
                    (cycle - PumpDatasheet.Simulation.CoolingFaultOnsetTick) /
                    rampTicks;
            }

            /// <summary>
            /// Shaft angular position in degrees, integrated from the running
            /// speed. Drives the OpenUSD rotation binding.
            /// </summary>
            public double ShaftAngleDegrees => Volatile.Read(ref m_shaftAngle);

            /// <summary>
            /// The supervision state the OpenUSD status-light binding follows.
            /// </summary>
            public bool AlarmActive => m_currentCavitation || m_currentMotorOverheat;

            /// <summary>
            /// Suction vessel level in metres, as last published. Drives the
            /// OpenUSD liquid-surface binding.
            /// </summary>
            public double LevelMetres => Volatile.Read(ref m_levelMetres);

            private readonly long m_phaseOffset;
            private readonly TimeSpan m_tickInterval;
            private readonly IValueUpdater<double> m_pressure;
            private readonly IValueUpdater<double> m_fluidTemperature;
            private readonly IValueUpdater<double> m_bearingTemperature;
            private readonly IValueUpdater<double> m_power;
            private readonly IValueUpdater<double> m_flow;
            private readonly IValueUpdater<double> m_efficiency;
            private readonly IValueUpdater<double> m_level;
            private readonly IValueUpdater<uint> m_numberOfStarts;
            private readonly IValueUpdater<bool> m_cavitation;
            private readonly IValueUpdater<bool> m_motorOverheat;
            private uint m_currentNumberOfStarts;
            private bool m_currentCavitation;
            private bool m_currentMotorOverheat;
            private double m_shaftAngle;
            private double m_levelMetres;
            private bool m_hasPublishedGoodValue;
        }

        private readonly ConcurrentDictionary<NodeId, PumpSimulationState> m_pumpSimulations = new();
        private readonly Lock m_simulationRegistrationLock = new();
        private long m_simulationTicks;
        private int m_nextSimulationProfile;

        /// <summary>
        /// Measurement Variables created outside the fluent builder, paired
        /// with the getter that yields their latest simulated value.
        /// </summary>
        /// <remarks>
        /// Held as an immutable snapshot that <see cref="TrackSignal"/> replaces
        /// under <see cref="m_simulationRegistrationLock"/>. A pump can be added
        /// through <c>CreatePumpAsync</c> while the simulation timer is running,
        /// and mutating a list while the tick enumerates it would throw on the
        /// timer thread and stop the simulation for every pump. Publishing from a
        /// snapshot keeps the tick allocation-free and lock-free.
        /// </remarks>
        private (BaseVariableState Variable, Func<double> Getter)[] m_liveSignals = [];
    }

    internal static partial class PumpNodeManagerLog
    {
        [LoggerMessage(EventId = PumpDeviceIntegrationServerEventIds.PumpNodeManager + 0,
            Level = LogLevel.Information,
            Message = "Configuring PumpNodeManager fluent wiring...")]
        public static partial void ConfiguringPumpNodeManagerFluentWiring(this ILogger logger);
    }
}
