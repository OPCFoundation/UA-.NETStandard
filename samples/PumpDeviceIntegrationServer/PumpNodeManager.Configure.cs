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
using Opc.Ua.Pumps;
using Opc.Ua.Server.Fluent;

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
            }
        }

        private static void WithIdentification(
            INodeManagerBuilder builder,
            PumpState pump,
            int pumpNumber)
        {
            string serialNumber = "SN-" + pumpNumber.ToString("D3", CultureInfo.InvariantCulture);
            builder.Node(pump.Identification!.NodeId)
                .WithProperty("Manufacturer", Variant.From(new LocalizedText("SimPump Corp")))
                .WithProperty("Model", Variant.From(new LocalizedText("PumpX-2000")))
                .WithProperty("SerialNumber", serialNumber)
                .WithProperty(
                    "ProductInstanceUri",
                    "urn:simdevice:SimPump:PumpX-2000:" + serialNumber)
                .WithProperty("DeviceClass", "Pump")
                .WithProperty("HardwareRevision", "1.0")
                .WithProperty("SoftwareRevision", "2.5.3");
        }

        private void WithMaintenance(INodeManagerBuilder builder, PumpState pump)
        {
            NodeId functionalGroupType = NodeId.Create(
                Opc.Ua.Di.ObjectTypes.FunctionalGroupType,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                Server.NamespaceUris);
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
                min: 0,
                max: 1_000_000,
                out IValueUpdater<double> pressure);
            AddMeasurement(
                builder,
                measurements.FluidTemperature!.NodeId,
                EngineeringUnits.Kelvin,
                min: 233.15,
                max: 473.15,
                out IValueUpdater<double> fluidTemperature);
            AddMeasurement(
                builder,
                measurements.BearingTemperature!.NodeId,
                EngineeringUnits.Kelvin,
                min: 233.15,
                max: 473.15,
                out IValueUpdater<double> bearingTemperature);
            AddMeasurement(
                builder,
                measurements.PumpPowerInput!.NodeId,
                EngineeringUnits.Watt,
                min: 0,
                max: 50_000,
                out IValueUpdater<double> power);
            AddMeasurement(
                builder,
                measurements.MassFlow!.NodeId,
                EngineeringUnits.KilogramsPerSecond,
                min: 0,
                max: 1.0,
                out IValueUpdater<double> flow);
            AddMeasurement(
                builder,
                measurements.PumpEfficiency!.NodeId,
                EngineeringUnits.Percent,
                min: 0,
                max: 100,
                out IValueUpdater<double> efficiency);
            AddMeasurement(
                builder,
                measurements.Level!.NodeId,
                EngineeringUnits.Metre,
                min: 0,
                max: 10,
                out IValueUpdater<double> level);

            builder.Variable<uint>(measurements.NumberOfStarts!.NodeId)
                .Bind(out IValueUpdater<uint> numberOfStarts)
                .Historize(historyAccessLevel: AccessLevels.HistoryRead);

            ushort pumpsNs = (ushort)Server.NamespaceUris.GetIndex(
                Opc.Ua.Pumps.Namespaces.Pumps);
            INodeBuilder<PumpState> pumpBuilder = builder.Node<PumpState>(pump.NodeId);
            INodeBuilder<SupervisionState> events = pumpBuilder.Components().Events();

            IAlarmBuilder<NonExclusiveLimitAlarmState> overTempAlarm = events
                .CreateLimitAlarm(new QualifiedName("OverTempAlarm", pumpsNs))
                .WithLimits(
                    highHigh: 373.15,
                    high: 363.15,
                    low: 283.15,
                    lowLow: 273.15)
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
            overTempAlarm.MonitorVariable(motorOverheatBuilder.Node);
            motorOverheatBuilder.ActivatesAlarm(overTempAlarm);

            return new PumpSimulationState(
                profileIndex,
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
                .Historize(historyAccessLevel: AccessLevels.HistoryRead);
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

        private void AdvanceSimulation()
        {
            long tick = Interlocked.Increment(ref m_simulationTicks);
            foreach (PumpSimulationState simulation in m_pumpSimulations.Values)
            {
                simulation.Advance(tick);
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
                m_phaseOffset = profileIndex * 17L;
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

            private void Publish(long tick, bool publishAll, StatusCode statusCode)
            {
                long localTick = tick + m_phaseOffset;
                DateTime sourceTimestamp = DateTime.UtcNow;
                m_pressure.SetValue(
                    200_000.0 + (50_000.0 * Math.Sin(localTick * 0.03)),
                    statusCode,
                    sourceTimestamp);
                m_fluidTemperature.SetValue(
                    313.15 + (5.0 * Math.Sin(localTick * 0.01)),
                    statusCode,
                    sourceTimestamp);
                m_bearingTemperature.SetValue(
                    333.15 + (8.0 * Math.Cos(localTick * 0.008)),
                    statusCode,
                    sourceTimestamp);
                m_power.SetValue(
                    5_000.0 + (500.0 * Math.Sin(localTick * 0.02)),
                    statusCode,
                    sourceTimestamp);
                m_flow.SetValue(
                    0.05 + (0.005 * Math.Cos(localTick * 0.04)),
                    statusCode,
                    sourceTimestamp);
                m_efficiency.SetValue(
                    75.0 + (10.0 * Math.Sin(localTick * 0.015)),
                    statusCode,
                    sourceTimestamp);
                m_level.SetValue(
                    2.5 + (0.5 * Math.Sin(localTick * 0.02)),
                    statusCode,
                    sourceTimestamp);

                uint numberOfStarts = checked((uint)(localTick / 3_600));
                if (publishAll || numberOfStarts != m_currentNumberOfStarts)
                {
                    m_currentNumberOfStarts = numberOfStarts;
                    m_numberOfStarts.SetValue(numberOfStarts, statusCode, sourceTimestamp);
                }

                long cavitationCycle = localTick % 40;
                bool cavitation = cavitationCycle >= 32 && cavitationCycle < 36;
                if (publishAll || cavitation != m_currentCavitation)
                {
                    m_currentCavitation = cavitation;
                    m_cavitation.SetValue(cavitation, statusCode, sourceTimestamp);
                }

                long overheatCycle = localTick % 64;
                bool motorOverheat = overheatCycle >= 56 && overheatCycle < 60;
                if (publishAll || motorOverheat != m_currentMotorOverheat)
                {
                    m_currentMotorOverheat = motorOverheat;
                    m_motorOverheat.SetValue(motorOverheat, statusCode, sourceTimestamp);
                }
            }

            private readonly long m_phaseOffset;
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
            private bool m_hasPublishedGoodValue;
        }

        private readonly ConcurrentDictionary<NodeId, PumpSimulationState> m_pumpSimulations = new();
        private readonly Lock m_simulationRegistrationLock = new();
        private long m_simulationTicks;
        private int m_nextSimulationProfile;
    }

    internal static partial class PumpNodeManagerLog
    {
        [LoggerMessage(EventId = PumpDeviceIntegrationServerEventIds.PumpNodeManager + 0,
            Level = LogLevel.Information,
            Message = "Configuring PumpNodeManager fluent wiring...")]
        public static partial void ConfiguringPumpNodeManagerFluentWiring(this ILogger logger);
    }
}
