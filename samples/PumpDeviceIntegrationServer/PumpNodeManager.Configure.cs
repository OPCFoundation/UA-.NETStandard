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

            PumpState pump = builder.Node<PumpState>("Pump #1").Node;
            WithIdentification(builder);
            RegisterPumpSimulation(builder, pump);

            builder.Simulation(TimeSpan.FromMilliseconds(250))
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
            // Seal starts the shared simulation registry; only seal successfully wired builders.
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

        /// <summary>
        /// Configures the identification values demonstrated by the
        /// hand-wired first pump.
        /// </summary>
        /// <param name="builder">The active fluent builder.</param>
        private static void WithIdentification(INodeManagerBuilder builder)
        {
            builder.Node("Pump #1/Identification")
                .WithProperty("Manufacturer", "SimPump Corp")
                .WithProperty("SerialNumber", "SN-001")
                .WithProperty(
                    "ProductInstanceUri",
                    "urn:simdevice:SimPump:PumpX-2000:SN-001");
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
                .Bind(out IValueUpdater<uint> numberOfStarts);

            ushort pumpsNs = (ushort)Server.NamespaceUris.GetIndex(
                Opc.Ua.Pumps.Namespaces.Pumps);
            INodeBuilder<PumpState> pumpBuilder =
                builder.Node<PumpState>(pump.NodeId);
            INodeBuilder<SupervisionState> events =
                pumpBuilder.Components().Events();

            IAlarmBuilder<NonExclusiveLimitAlarmState> overTempAlarm = events
                .CreateLimitAlarm(
                    new QualifiedName("OverTempAlarm", pumpsNs))
                .WithLimits(
                    highHigh: 373.15,
                    high: 363.15,
                    low: 283.15,
                    lowLow: 273.15)
                .OnAcknowledge((ctx, c, eventId, comment) => ServiceResult.Good);

            events.Components().SupervisionProcessFluid()
                .Components().Cavitation()
                .Bind(out IValueUpdater<bool> cavitation);
            IVariableBuilder<bool> motorOverheatBuilder = events
                .Components().SupervisionPumpOperation()
                .Components().MotorOverheat()
                .Bind(out IValueUpdater<bool> motorOverheat);
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
                .WithEURange(min, max);
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
                Publish(tick, publishAll: true);
            }

            public void Advance(long tick)
            {
                Publish(tick, publishAll: false);
            }

            private void Publish(long tick, bool publishAll)
            {
                long localTick = tick + m_phaseOffset;
                m_pressure.SetValue(
                    200_000.0 + (50_000.0 * Math.Sin(localTick * 0.03)));
                m_fluidTemperature.SetValue(
                    313.15 + (5.0 * Math.Sin(localTick * 0.01)));
                m_bearingTemperature.SetValue(
                    333.15 + (8.0 * Math.Cos(localTick * 0.008)));
                m_power.SetValue(
                    5_000.0 + (500.0 * Math.Sin(localTick * 0.02)));
                m_flow.SetValue(
                    0.05 + (0.005 * Math.Cos(localTick * 0.04)));
                m_efficiency.SetValue(
                    75.0 + (10.0 * Math.Sin(localTick * 0.015)));
                m_level.SetValue(
                    2.5 + (0.5 * Math.Sin(localTick * 0.02)));

                uint numberOfStarts = checked((uint)(localTick / 3_600));
                if (publishAll || numberOfStarts != m_currentNumberOfStarts)
                {
                    m_currentNumberOfStarts = numberOfStarts;
                    m_numberOfStarts.SetValue(numberOfStarts);
                }

                long cavitationCycle = localTick % 40;
                bool cavitation = cavitationCycle >= 32 && cavitationCycle < 36;
                if (publishAll || cavitation != m_currentCavitation)
                {
                    m_currentCavitation = cavitation;
                    m_cavitation.SetValue(cavitation);
                }

                long overheatCycle = localTick % 64;
                bool motorOverheat = overheatCycle >= 56 && overheatCycle < 60;
                if (publishAll || motorOverheat != m_currentMotorOverheat)
                {
                    m_currentMotorOverheat = motorOverheat;
                    m_motorOverheat.SetValue(motorOverheat);
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
        }

        // Registration is a compound operation protected by the lock; the concurrent dictionary
        // allows the 250 ms tick to enumerate fully initialized states without taking that lock.
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
