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
using Opc.Ua.Generators;
using Opc.Ua.Server.Fluent;

namespace Generators
{
    /// <summary>
    /// The datasheet-driven simulation, the operating state machine and the
    /// protection alarms.
    /// </summary>
    public partial class GeneratorNodeManager
    {
        private readonly ConcurrentDictionary<NodeId, GeneratorSimulation> m_simulations = new();
        private readonly Lock m_registrationLock = new();

        /// <summary>
        /// Serialises the simulation tick against client method calls.
        /// </summary>
        /// <remarks>
        /// The tick runs on a thread-pool thread and method calls arrive on request
        /// threads, and both drive state transitions and write the same address-space
        /// nodes. One gate for the whole plant rather than one per set: a tick is
        /// microseconds of arithmetic, so the contention is irrelevant, and a single
        /// gate cannot be taken in the wrong order.
        /// </remarks>
        private readonly Lock m_simulationGate = new();
        private long m_ticks;
        private int m_nextProfile;
        private GeneratorSimulation? m_faultSubject;

        /// <summary>
        /// Seconds between the start of one fault cycle and the next.
        /// </summary>
        private const double FaultCycleSeconds = 150.0;

        /// <summary>
        /// Seconds a fault is left in place, long enough to trip and annunciate.
        /// </summary>
        private const double FaultDwellSeconds = 20.0;

        /// <summary>
        /// The faults the schedule rotates through, one per cycle, so a long run
        /// exercises every protection rather than the same one repeatedly.
        /// </summary>
        private static readonly GeneratorFault[] s_faultRotation =
        [
            GeneratorFault.CoolingFailure,
            GeneratorFault.OilPressureLoss,
            GeneratorFault.Overload,
            GeneratorFault.GovernorFailure,
        ];

        /// <summary>
        /// Wires the simulation for every materialised set and starts the tick.
        /// </summary>
        /// <param name="builder">The active fluent builder.</param>
        partial void Configure(INodeManagerBuilder builder)
        {
            foreach (GeneratorSetState set in m_generatorSets)
            {
                RegisterGeneratorSimulation(builder, set);
            }

            builder.Simulation(SimulationInterval)
                .OnTick((ctx, elapsed) => AdvanceSimulation());
        }

        /// <summary>
        /// Wires a set created after the initial fluent configuration into the
        /// already-running simulation.
        /// </summary>
        /// <param name="set">The registered generator set.</param>
        private void RegisterGeneratorSimulation(GeneratorSetState set)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(
                Opc.Ua.Generators.Namespaces.Generators);
            NodeManagerBuilder builder = CreateFluentBuilder(ns);
            RegisterGeneratorSimulation(builder, set);
            builder.Seal();
        }

        /// <summary>
        /// Binds every measured variable of one set and records its simulation state.
        /// </summary>
        /// <param name="builder">The active fluent builder.</param>
        /// <param name="set">The generator set to wire.</param>
        private void RegisterGeneratorSimulation(INodeManagerBuilder builder, GeneratorSetState set)
        {
            int profile;
            lock (m_registrationLock)
            {
                profile = m_nextProfile++;
            }

            EngineState engine = set.Engine!;
            AlternatorState alternator = set.Alternator!;

            // Engine.
            Analog(builder, engine.Speed!.NodeId, EngineeringUnits.RevolutionsPerMinute,
                0, GeneratorDatasheet.Ranges.SpeedMaxRpm, out IValueUpdater<double> speed);
            Analog(builder, engine.OilPressure!.NodeId, EngineeringUnits.Pascal,
                0, GeneratorDatasheet.Convert.ToPascal(GeneratorDatasheet.Ranges.OilPressureMaxBar),
                out IValueUpdater<double> oilPressure);
            Analog(builder, engine.CoolantTemperature!.NodeId, EngineeringUnits.Kelvin,
                GeneratorDatasheet.Convert.ToKelvin(0),
                GeneratorDatasheet.Convert.ToKelvin(GeneratorDatasheet.Ranges.CoolantMaxCelsius),
                out IValueUpdater<double> coolantTemperature);
            Analog(builder, engine.ExhaustGasTemperature!.NodeId, EngineeringUnits.Kelvin,
                GeneratorDatasheet.Convert.ToKelvin(0),
                GeneratorDatasheet.Convert.ToKelvin(GeneratorDatasheet.Ranges.ExhaustMaxCelsius),
                out IValueUpdater<double> exhaustTemperature);
            Analog(builder, engine.FuelRate!.NodeId, EngineeringUnits.CubicMetrePerSecond,
                0,
                GeneratorDatasheet.Convert.ToCubicMetresPerSecond(
                    GeneratorDatasheet.Ranges.FuelRateMaxLitresPerHour),
                out IValueUpdater<double> fuelRate);
            Analog(builder, engine.EngineHours!.NodeId, EngineeringUnits.Hour,
                0, 100_000, out IValueUpdater<double> engineHours);
            Analog(builder, engine.PercentLoad!.NodeId, EngineeringUnits.Percent,
                0, GeneratorDatasheet.Ranges.LoadPercentMax, out IValueUpdater<double> percentLoad);

            builder.Variable<uint>(engine.NumberOfStarts!.NodeId)
                .Bind(out IValueUpdater<uint> starts);

            // Alternator aggregates.
            Analog(builder, alternator.Frequency!.NodeId, EngineeringUnits.Hertz,
                GeneratorDatasheet.Ranges.FrequencyMinHertz,
                GeneratorDatasheet.Ranges.FrequencyMaxHertz, out IValueUpdater<double> frequency);
            Analog(builder, alternator.TotalRealPower!.NodeId, EngineeringUnits.Watt,
                0, GeneratorDatasheet.Ranges.RealPowerMaxWatts, out IValueUpdater<double> realPower);
            Analog(builder, alternator.TotalApparentPower!.NodeId, EngineeringUnits.VoltAmpere,
                0, GeneratorDatasheet.Ranges.RealPowerMaxWatts / GeneratorDatasheet.Electrical.RatedPowerFactor,
                out IValueUpdater<double> apparentPower);
            Analog(builder, alternator.AverageLineToLineVoltage!.NodeId, EngineeringUnits.Volt,
                0, GeneratorDatasheet.Ranges.VoltageMaxVolts, out IValueUpdater<double> voltage);
            Analog(builder, alternator.AverageCurrent!.NodeId, EngineeringUnits.Ampere,
                0, GeneratorDatasheet.Ranges.CurrentMaxAmperes, out IValueUpdater<double> current);
            Analog(builder, alternator.LoadPercent!.NodeId, EngineeringUnits.Percent,
                0, GeneratorDatasheet.Ranges.LoadPercentMax, out IValueUpdater<double> loadPercent);
            Analog(builder, alternator.TotalRealEnergy!.NodeId, EngineeringUnits.WattHour,
                0, 1e9, out IValueUpdater<double> energy);

            builder.Variable<double>(alternator.AveragePowerFactor!.NodeId)
                .Bind(out IValueUpdater<double> powerFactor);

            // Per-phase values.
            var phases = new PhaseUpdaters[3];
            AlternatorPhaseState[] phaseStates = [alternator.L1!, alternator.L2!, alternator.L3!];
            for (int i = 0; i < phaseStates.Length; i++)
            {
                AlternatorPhaseState phase = phaseStates[i];
                Analog(builder, phase.LineToNeutralVoltage!.NodeId, EngineeringUnits.Volt,
                    0, GeneratorDatasheet.Ranges.VoltageMaxVolts, out IValueUpdater<double> pv);
                Analog(builder, phase.Current!.NodeId, EngineeringUnits.Ampere,
                    0, GeneratorDatasheet.Ranges.CurrentMaxAmperes, out IValueUpdater<double> pi);
                Analog(builder, phase.RealPower!.NodeId, EngineeringUnits.Watt,
                    0, GeneratorDatasheet.Ranges.RealPowerMaxWatts, out IValueUpdater<double> pp);
                builder.Variable<double>(phase.PowerFactor!.NodeId)
                    .Bind(out IValueUpdater<double> ppf);
                phases[i] = new PhaseUpdaters(pv, pi, pp, ppf);
            }

            // Balance of plant.
            Analog(builder, set.FuelSystem!.FuelLevel!.NodeId, EngineeringUnits.Percent,
                0, 100, out IValueUpdater<double> fuelLevel);
            Analog(builder, set.FuelSystem!.TotalFuelConsumed!.NodeId, EngineeringUnits.Litre,
                0, 1e6, out IValueUpdater<double> fuelConsumed);
            Analog(builder, set.CoolingSystem!.AmbientTemperature!.NodeId, EngineeringUnits.Kelvin,
                GeneratorDatasheet.Convert.ToKelvin(-40),
                GeneratorDatasheet.Convert.ToKelvin(60), out IValueUpdater<double> ambient);
            Analog(builder, set.LubricationSystem!.OilTemperature!.NodeId, EngineeringUnits.Kelvin,
                GeneratorDatasheet.Convert.ToKelvin(0),
                GeneratorDatasheet.Convert.ToKelvin(150), out IValueUpdater<double> oilTemperature);
            Analog(builder, set.StartingSystem!.BatteryVoltage!.NodeId, EngineeringUnits.Volt,
                0, GeneratorDatasheet.Ranges.BatteryVoltageMaxVolts,
                out IValueUpdater<double> batteryVoltage);

            builder.Variable<bool>(set.GeneratorBreakerClosed!.NodeId)
                .Bind(out IValueUpdater<bool> breakerClosed);
            builder.Variable<bool>(set.AvailableToLoad!.NodeId)
                .Bind(out IValueUpdater<bool> availableToLoad);

            var simulation = new GeneratorSimulation(
                profile,
                SimulationInterval,
                new EngineUpdaters(
                    speed, oilPressure, coolantTemperature, exhaustTemperature,
                    fuelRate, engineHours, percentLoad, starts),
                new AlternatorUpdaters(
                    frequency, realPower, apparentPower, voltage, current,
                    loadPercent, energy, powerFactor, phases),
                new PlantUpdaters(
                    fuelLevel, fuelConsumed, ambient, oilTemperature,
                    batteryVoltage, breakerClosed, availableToLoad));

            m_simulations[set.NodeId] = simulation;

            // The first set registered is the one the fault schedule drives. It is
            // the machine nearest the hero camera, so a trip is visible in the
            // viewport rather than happening to a set that is off the far end of
            // the row - an alarm nobody can see demonstrates nothing.
            m_faultSubject ??= simulation;

            AttachSimulationToTwin(set, simulation);
            AttachProtectionAlarms(set, simulation);
            AttachStateMachine(set, simulation);
        }

        /// <summary>
        /// Lets one set develop a fault on a slow rotation, so the protection
        /// path is exercised in a running plant.
        /// </summary>
        /// <param name="tick">Monotonic tick counter.</param>
        /// <remarks>
        /// <para>
        /// A set running to its datasheet cannot protect-trip: the healthy curves
        /// are bounded well inside every trip point, which is what a datasheet
        /// means. Without a deliberate excursion the four alarms, the shutdown
        /// class, <c>ResetFaults</c> and the whole Fault branch of the state machine
        /// would be code that never runs - and a sample that documents alarms which
        /// never fire is worse than one that has none.
        /// </para>
        /// <para>
        /// Confined to a single set so the rest of the plant stays a clean
        /// reference, and disabled entirely with <c>--faults false</c>.
        /// </para>
        /// </remarks>
        private void DriveFaultSchedule(long tick)
        {
            if (!InjectFaults || m_simulations.IsEmpty)
            {
                return;
            }

            double seconds = tick * SimulationInterval.TotalSeconds;
            int slot = (int)(seconds / FaultCycleSeconds);
            double intoSlot = seconds - (slot * FaultCycleSeconds);

            // Healthy for most of each cycle, then one fault long enough to trip,
            // annunciate and shut the set down before the next cycle clears it.
            GeneratorFault fault = intoSlot < FaultCycleSeconds - FaultDwellSeconds
                ? GeneratorFault.None
                : s_faultRotation[slot % s_faultRotation.Length];

            GeneratorSimulation? subject = m_faultSubject;
            if (subject != null && subject.Fault != fault)
            {
                subject.InjectFault(fault);
            }
        }

        /// <summary>
        /// Advances every set by one tick.
        /// </summary>
        /// <remarks>
        /// Runs on a thread-pool thread while client requests are served on their
        /// own threads, so it is serialised against the method handlers by
        /// <see cref="m_simulationGate"/>. Without that, a tick and a concurrent
        /// EmergencyStop can both transition the same set and race on the paired
        /// CurrentState / CurrentState.Id write, leaving a client with a state name
        /// that does not match the state node beside it.
        /// </remarks>
        private void AdvanceSimulation()
        {
            long tick = Interlocked.Increment(ref m_ticks);
            double seconds = SimulationInterval.TotalSeconds;
            lock (m_simulationGate)
            {
                foreach (GeneratorSimulation simulation in m_simulations.Values)
                {
                    simulation.Advance(tick);
                }
                DriveFaultSchedule(tick);
                foreach (GeneratorTwin twin in m_twins.Values)
                {
                    twin.AdvanceFan(seconds);
                }
                EvaluateProtections();
            }
            PublishOpenUsdSignals();
        }

        /// <summary>
        /// Binds an analog variable with its engineering units and range.
        /// </summary>
        /// <param name="builder">The active fluent builder.</param>
        /// <param name="nodeId">The variable to bind.</param>
        /// <param name="units">Engineering units published as a property.</param>
        /// <param name="min">Lower bound of the engineering range.</param>
        /// <param name="max">Upper bound of the engineering range.</param>
        /// <param name="updater">Receives the updater the simulation writes through.</param>
        private static void Analog(
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

        /// <summary>
        /// The UNECE/CEFACT units this sample publishes.
        /// </summary>
        /// <remarks>
        /// The companion specification types every measured value as
        /// <c>AnalogUnitType</c> carrying a machine-readable unit, so a client can
        /// discover the unit from the model alone rather than guessing from a name.
        /// </remarks>
        private static class EngineeringUnits
        {
            private const string Cefact = "http://www.opcfoundation.org/UA/units/un/cefact";

            public static readonly EUInformation RevolutionsPerMinute =
                new("r/min", "Revolutions per Minute", Cefact);

            public static readonly EUInformation Pascal = new("Pa", "Pascal", Cefact);

            public static readonly EUInformation Kelvin = new("K", "Kelvin", Cefact);

            public static readonly EUInformation CubicMetrePerSecond =
                new("m3/s", "Cubic Metre per Second", Cefact);

            public static readonly EUInformation Hour = new("h", "Hour", Cefact);

            public static readonly EUInformation Percent = new("%", "Percent", Cefact);

            public static readonly EUInformation Hertz = new("Hz", "Hertz", Cefact);

            public static readonly EUInformation Watt = new("W", "Watt", Cefact);

            public static readonly EUInformation VoltAmpere = new("VA", "Volt Ampere", Cefact);

            public static readonly EUInformation Volt = new("V", "Volt", Cefact);

            public static readonly EUInformation Ampere = new("A", "Ampere", Cefact);

            public static readonly EUInformation WattHour = new("W h", "Watt Hour", Cefact);

            public static readonly EUInformation Litre = new("l", "Litre", Cefact);
        }
    }

    /// <summary>
    /// Source-generated log messages for the generator simulation.
    /// </summary>
    internal static partial class GeneratorSimulationLog
    {
        [LoggerMessage(
            EventId = GeneratorServerEventIds.GeneratorSimulation + 0,
            Level = LogLevel.Debug,
            Message = "Generator set {NodeId} entered state {State}.")]
        public static partial void GeneratorStateChanged(
            this ILogger logger, NodeId nodeId, GeneratorRunState state);
    }
}
