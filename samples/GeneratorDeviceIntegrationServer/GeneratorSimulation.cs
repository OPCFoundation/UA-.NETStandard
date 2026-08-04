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
using Opc.Ua.Server.Fluent;

namespace Generators
{
    /// <summary>
    /// Small numeric helpers that are not available on every target framework.
    /// </summary>
    /// <remarks>
    /// <c>Math.Clamp</c> arrived after .NET Standard 2.0, and this sample builds for
    /// net48 as well, so the sample carries its own rather than gating the call
    /// sites behind conditional compilation.
    /// </remarks>
    internal static class Numeric
    {
        /// <summary>
        /// Clamps a value to an inclusive range.
        /// </summary>
        /// <param name="value">The value to clamp.</param>
        /// <param name="min">Lower bound.</param>
        /// <param name="max">Upper bound.</param>
        /// <returns>The value, bounded by <paramref name="min"/> and <paramref name="max"/>.</returns>
        public static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }
            return value > max ? max : value;
        }
    }

    /// <summary>
    /// The twelve operating states of a generating set.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>GeneratorStateMachineType</c>. Kept as a plain enum so the
    /// simulation can reason about the state without touching address-space nodes
    /// on the tick path.
    /// </remarks>
    internal enum GeneratorRunState
    {
        /// <summary>Shut down and not available.</summary>
        Off,

        /// <summary>Available and waiting for a start command.</summary>
        Ready,

        /// <summary>Cranking.</summary>
        Starting,

        /// <summary>Running unloaded while coolant comes up to temperature.</summary>
        Warmup,

        /// <summary>At rated speed, breaker open.</summary>
        Running,

        /// <summary>Breaker closed and carrying load.</summary>
        Loaded,

        /// <summary>Matching voltage, frequency and phase to a live bus.</summary>
        Synchronizing,

        /// <summary>Sharing load with other sets on a common bus.</summary>
        Paralleled,

        /// <summary>Running unloaded to cool down before shutdown.</summary>
        Cooldown,

        /// <summary>Coming to rest.</summary>
        Stopping,

        /// <summary>Shut down by a protection trip.</summary>
        Fault,

        /// <summary>Shut down by the emergency stop.</summary>
        EmergencyStopped,
    }

    /// <summary>
    /// Value updaters for the engine measurements.
    /// </summary>
    /// <param name="Speed">Engine speed, in revolutions per minute.</param>
    /// <param name="OilPressure">Oil pressure, in pascal.</param>
    /// <param name="CoolantTemperature">Coolant temperature, in kelvin.</param>
    /// <param name="ExhaustTemperature">Exhaust gas temperature, in kelvin.</param>
    /// <param name="FuelRate">Fuel rate, in cubic metres per second.</param>
    /// <param name="EngineHours">Accumulated running hours.</param>
    /// <param name="PercentLoad">Engine load, in percent.</param>
    /// <param name="Starts">Number of start attempts.</param>
    internal sealed record EngineUpdaters(
        IValueUpdater<double> Speed,
        IValueUpdater<double> OilPressure,
        IValueUpdater<double> CoolantTemperature,
        IValueUpdater<double> ExhaustTemperature,
        IValueUpdater<double> FuelRate,
        IValueUpdater<double> EngineHours,
        IValueUpdater<double> PercentLoad,
        IValueUpdater<uint> Starts);

    /// <summary>
    /// Value updaters for one alternator phase.
    /// </summary>
    /// <param name="Voltage">Line-to-neutral voltage, in volts.</param>
    /// <param name="Current">Phase current, in amperes.</param>
    /// <param name="RealPower">Phase real power, in watts.</param>
    /// <param name="PowerFactor">Phase power factor.</param>
    internal sealed record PhaseUpdaters(
        IValueUpdater<double> Voltage,
        IValueUpdater<double> Current,
        IValueUpdater<double> RealPower,
        IValueUpdater<double> PowerFactor);

    /// <summary>
    /// Value updaters for the alternator aggregates.
    /// </summary>
    /// <param name="Frequency">Output frequency, in hertz.</param>
    /// <param name="RealPower">Total real power, in watts.</param>
    /// <param name="ApparentPower">Total apparent power, in volt-amperes.</param>
    /// <param name="Voltage">Average line-to-line voltage, in volts.</param>
    /// <param name="Current">Average line current, in amperes.</param>
    /// <param name="LoadPercent">Load, in percent of prime rating.</param>
    /// <param name="Energy">Accumulated real energy, in watt-hours.</param>
    /// <param name="PowerFactor">Average power factor.</param>
    /// <param name="Phases">Per-phase updaters for L1, L2 and L3.</param>
    internal sealed record AlternatorUpdaters(
        IValueUpdater<double> Frequency,
        IValueUpdater<double> RealPower,
        IValueUpdater<double> ApparentPower,
        IValueUpdater<double> Voltage,
        IValueUpdater<double> Current,
        IValueUpdater<double> LoadPercent,
        IValueUpdater<double> Energy,
        IValueUpdater<double> PowerFactor,
        PhaseUpdaters[] Phases);

    /// <summary>
    /// Value updaters for the balance-of-plant subsystems.
    /// </summary>
    /// <param name="FuelLevel">Tank level, in percent.</param>
    /// <param name="FuelConsumed">Total fuel consumed, in litres.</param>
    /// <param name="AmbientTemperature">Ambient air temperature, in kelvin.</param>
    /// <param name="OilTemperature">Oil temperature, in kelvin.</param>
    /// <param name="BatteryVoltage">Starting battery voltage, in volts.</param>
    /// <param name="BreakerClosed">Whether the generator breaker is closed.</param>
    /// <param name="AvailableToLoad">Whether the set is ready to accept load.</param>
    internal sealed record PlantUpdaters(
        IValueUpdater<double> FuelLevel,
        IValueUpdater<double> FuelConsumed,
        IValueUpdater<double> AmbientTemperature,
        IValueUpdater<double> OilTemperature,
        IValueUpdater<double> BatteryVoltage,
        IValueUpdater<bool> BreakerClosed,
        IValueUpdater<bool> AvailableToLoad);

    /// <summary>
    /// A fault a generating set can develop, past the point where a protection
    /// supervising it must annunciate.
    /// </summary>
    /// <remarks>
    /// The healthy physics in <see cref="GeneratorDatasheet"/> are functions of load
    /// fraction alone, and by construction they stay inside the trip points - a set
    /// running to its datasheet cannot protect-trip, which is the whole point of the
    /// datasheet. A fault is therefore modelled as a deviation applied to the
    /// measured value <em>after</em> the healthy value is derived, which is what a
    /// real fault is: the machine no longer follows its curve.
    /// </remarks>
    internal enum GeneratorFault
    {
        /// <summary>Running to the datasheet.</summary>
        None,

        /// <summary>Radiator or fan failure: jacket water climbs past its limit.</summary>
        CoolingFailure,

        /// <summary>Pump or leak: oil pressure collapses while the engine turns.</summary>
        OilPressureLoss,

        /// <summary>The set is asked for more than its rating.</summary>
        Overload,

        /// <summary>Governor failure: the engine runs away.</summary>
        GovernorFailure,
    }

    /// <summary>
    /// One generating set's simulated behaviour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Load fraction is the only independent variable. Every published value
    /// follows from it through the curves in <see cref="GeneratorDatasheet"/>, so
    /// <c>P = sqrt(3) * V * I * PF</c> and
    /// <c>eta = P / (Vdot * rho * LHV)</c> hold at every tick by construction
    /// rather than by coincidence.
    /// </para>
    /// <para>
    /// Each set carries its own state, and its duty point and start-up schedule
    /// are offset by a per-set profile index, so no two sets in a plant report the
    /// same numbers or sit in the same operating state.
    /// </para>
    /// </remarks>
    internal sealed class GeneratorSimulation
    {
        private const double CrankSeconds = 4.0;
        private const double WarmupSeconds = 12.0;
        private const double CooldownSeconds = 10.0;
        private const double StoppingSeconds = 5.0;

        private readonly int m_profile;
        private readonly double m_tickSeconds;
        private readonly EngineUpdaters m_engine;
        private readonly AlternatorUpdaters m_alternator;
        private readonly PlantUpdaters m_plant;

        private GeneratorRunState m_state = GeneratorRunState.Off;
        private double m_stateSeconds;
        private double m_engineHours;
        private double m_energyWattHours;
        private double m_fuelConsumedLitres;
        private double m_fuelLevelPercent = GeneratorDatasheet.Simulation.InitialFuelPercent;
        private double m_coolantCelsius = GeneratorDatasheet.Simulation.AmbientCelsius;
        private uint m_starts;
        private GeneratorFault m_fault;

        /// <summary>
        /// Initialises the simulation for one set.
        /// </summary>
        /// <param name="profile">Zero-based per-set profile index.</param>
        /// <param name="interval">Simulation tick interval.</param>
        /// <param name="engine">Engine updaters.</param>
        /// <param name="alternator">Alternator updaters.</param>
        /// <param name="plant">Balance-of-plant updaters.</param>
        public GeneratorSimulation(
            int profile,
            TimeSpan interval,
            EngineUpdaters engine,
            AlternatorUpdaters alternator,
            PlantUpdaters plant)
        {
            m_profile = profile;
            m_tickSeconds = interval.TotalSeconds;
            m_engine = engine;
            m_alternator = alternator;
            m_plant = plant;

            // Stagger the sets so a plant shows several states at once rather than
            // every machine marching in lockstep.
            m_stateSeconds = -profile * 6.0;
        }

        /// <summary>
        /// Gets the fault the set is currently running with.
        /// </summary>
        public GeneratorFault Fault => m_fault;

        /// <summary>
        /// Gives the set a fault, or clears one.
        /// </summary>
        /// <param name="fault">The fault to develop, or
        /// <see cref="GeneratorFault.None"/> to run to the datasheet again.</param>
        /// <remarks>
        /// Kept separate from the healthy curves so the datasheet identities still
        /// hold for a healthy set: a fault deviates the measured value from the
        /// curve rather than changing the curve.
        /// </remarks>
        public void InjectFault(GeneratorFault fault)
        {
            m_fault = fault;
        }

        /// <summary>
        /// Gets the current operating state.
        /// </summary>
        public GeneratorRunState State => m_state;

        /// <summary>
        /// Gets the current load fraction, where 1.0 is prime power.
        /// </summary>
        public double LoadFraction { get; private set; }

        /// <summary>
        /// Gets the current engine speed, in revolutions per minute.
        /// </summary>
        public double SpeedRpm { get; private set; }

        /// <summary>
        /// Gets the current coolant temperature, in degrees Celsius.
        /// </summary>
        public double CoolantCelsius => m_coolantCelsius;

        /// <summary>
        /// Gets the current exhaust temperature, in degrees Celsius.
        /// </summary>
        public double ExhaustCelsius { get; private set; } =
            GeneratorDatasheet.Simulation.AmbientCelsius;

        /// <summary>
        /// Gets the current fuel level, in percent of tank capacity.
        /// </summary>
        public double FuelLevelPercent => m_fuelLevelPercent;

        /// <summary>
        /// Gets the current oil pressure, in bar.
        /// </summary>
        public double OilPressureBar { get; private set; }

        /// <summary>
        /// Gets a value indicating whether a protection has tripped.
        /// </summary>
        public bool ProtectionTripped =>
            m_state is GeneratorRunState.Fault or GeneratorRunState.EmergencyStopped;

        /// <summary>
        /// Gets a value indicating whether the set is producing power.
        /// </summary>
        public bool IsLoaded =>
            m_state is GeneratorRunState.Loaded or GeneratorRunState.Paralleled;

        /// <summary>
        /// Gets a value indicating whether the coolant is above its trip point.
        /// </summary>
        public bool CoolantOverTemperature =>
            IsSpinning && m_coolantCelsius > GeneratorDatasheet.TripPoints.HighCoolantCelsius;

        /// <summary>
        /// Gets a value indicating whether oil pressure is below its trip point.
        /// </summary>
        /// <remarks>
        /// Only supervised once the engine is turning at speed. A real set bypasses
        /// the low-oil-pressure trip while cranking, because oil pressure has not
        /// built yet - supervising it during start-up trips every set the moment it
        /// tries to start.
        /// </remarks>
        public bool LowOilPressure =>
            IsSpinning && OilPressureBar < GeneratorDatasheet.TripPoints.LowOilPressureBar;

        /// <summary>
        /// Gets a value indicating whether the engine is turning at rated speed.
        /// </summary>
        public bool IsSpinning =>
            m_state is GeneratorRunState.Warmup
                or GeneratorRunState.Running
                or GeneratorRunState.Loaded
                or GeneratorRunState.Synchronizing
                or GeneratorRunState.Paralleled
                or GeneratorRunState.Cooldown;

        /// <summary>
        /// Gets or sets the callback invoked when the operating state changes.
        /// </summary>
        /// <remarks>
        /// The enum below is the single decider of what state a set is in; the
        /// address-space state machine follows it through this callback. Having one
        /// place decide is what keeps the two from disagreeing - a state machine
        /// driven independently of the physics eventually reports a machine as
        /// running while the simulation says it is stopped.
        /// </remarks>
        public Action<GeneratorRunState, GeneratorRunState>? StateChanged { get; set; }

        /// <summary>
        /// Requests a state change from outside the automatic cycle.
        /// </summary>
        /// <param name="state">The state to enter.</param>
        /// <returns>
        /// <see langword="true"/> when the request was accepted.
        /// </returns>
        /// <remarks>
        /// Used by the generator-set methods. Legality is checked here rather than
        /// in each handler so a caller cannot force a machine from Off straight to
        /// Loaded.
        /// </remarks>
        public bool RequestState(GeneratorRunState state)
        {
            if (!IsLegalTransition(m_state, state))
            {
                return false;
            }
            Enter(state);
            return true;
        }

        /// <summary>
        /// Returns whether a transition between two states is declared by the model.
        /// </summary>
        /// <param name="from">Current state.</param>
        /// <param name="to">Requested state.</param>
        /// <returns><see langword="true"/> when the transition exists.</returns>
        public static bool IsLegalTransition(GeneratorRunState from, GeneratorRunState to)
        {
            return (from, to) switch
            {
                (GeneratorRunState.Off, GeneratorRunState.Ready) => true,
                (GeneratorRunState.Ready, GeneratorRunState.Starting) => true,
                (GeneratorRunState.Ready, GeneratorRunState.Off) => true,
                (GeneratorRunState.Starting, GeneratorRunState.Warmup) => true,
                (GeneratorRunState.Starting, GeneratorRunState.Fault) => true,
                (GeneratorRunState.Warmup, GeneratorRunState.Running) => true,
                (GeneratorRunState.Running, GeneratorRunState.Loaded) => true,
                (GeneratorRunState.Running, GeneratorRunState.Synchronizing) => true,
                (GeneratorRunState.Running, GeneratorRunState.Cooldown) => true,
                (GeneratorRunState.Running, GeneratorRunState.Fault) => true,
                (GeneratorRunState.Running, GeneratorRunState.EmergencyStopped) => true,
                (GeneratorRunState.Synchronizing, GeneratorRunState.Paralleled) => true,
                (GeneratorRunState.Synchronizing, GeneratorRunState.Running) => true,
                (GeneratorRunState.Paralleled, GeneratorRunState.Loaded) => true,
                (GeneratorRunState.Paralleled, GeneratorRunState.Fault) => true,
                (GeneratorRunState.Paralleled, GeneratorRunState.EmergencyStopped) => true,
                (GeneratorRunState.Loaded, GeneratorRunState.Cooldown) => true,
                (GeneratorRunState.Loaded, GeneratorRunState.Fault) => true,
                (GeneratorRunState.Loaded, GeneratorRunState.EmergencyStopped) => true,
                (GeneratorRunState.Cooldown, GeneratorRunState.Stopping) => true,
                (GeneratorRunState.Stopping, GeneratorRunState.Off) => true,
                (GeneratorRunState.Fault, GeneratorRunState.Off) => true,
                (GeneratorRunState.EmergencyStopped, GeneratorRunState.Off) => true,
                _ => false,
            };
        }

        /// <summary>
        /// Trips the set on a protection, if it is running.
        /// </summary>
        /// <returns><see langword="true"/> when the set was tripped.</returns>
        public bool Trip()
        {
            return RequestState(GeneratorRunState.Fault);
        }

        /// <summary>
        /// Advances the set by one tick and publishes every derived value.
        /// </summary>
        /// <param name="tick">Monotonic tick counter shared by all sets.</param>
        public void Advance(long tick)
        {
            m_stateSeconds += m_tickSeconds;
            AdvanceState();
            double load = ComputeLoadFraction(tick);
            LoadFraction = load;
            Publish(load);
        }

        /// <summary>
        /// Runs the operating state machine.
        /// </summary>
        private void AdvanceState()
        {
            switch (m_state)
            {
                case GeneratorRunState.Off when m_stateSeconds > 3.0:
                    Enter(GeneratorRunState.Ready);
                    break;
                case GeneratorRunState.Ready when m_stateSeconds > 3.0:
                    Enter(GeneratorRunState.Starting);
                    break;
                case GeneratorRunState.Starting when m_stateSeconds > CrankSeconds:
                    Enter(GeneratorRunState.Warmup);
                    break;
                case GeneratorRunState.Warmup when m_stateSeconds > WarmupSeconds:
                    Enter(GeneratorRunState.Running);
                    break;
                // The first set energises a dead bus and simply closes onto it;
                // every other set has to match voltage, frequency and phase to a bus
                // that is already live before its breaker can close. Without this the
                // Synchronizing and Paralleled states, and the five transitions
                // through them, would be declared by the model and never observed.
                case GeneratorRunState.Running when m_stateSeconds > 6.0:
                    Enter(m_profile == 0
                        ? GeneratorRunState.Loaded
                        : GeneratorRunState.Synchronizing);
                    break;
                case GeneratorRunState.Synchronizing when m_stateSeconds > 8.0:
                    Enter(GeneratorRunState.Paralleled);
                    break;
                case GeneratorRunState.Paralleled when m_stateSeconds > 25.0:
                    Enter(GeneratorRunState.Loaded);
                    break;
                case GeneratorRunState.Loaded when m_stateSeconds > 180.0:
                    Enter(GeneratorRunState.Cooldown);
                    break;
                case GeneratorRunState.Cooldown when m_stateSeconds > CooldownSeconds:
                    Enter(GeneratorRunState.Stopping);
                    break;
                case GeneratorRunState.Stopping when m_stateSeconds > StoppingSeconds:
                    Enter(GeneratorRunState.Off);
                    break;
                case GeneratorRunState.Fault when m_stateSeconds > 20.0:
                    Enter(GeneratorRunState.Off);
                    break;
                case GeneratorRunState.EmergencyStopped when m_stateSeconds > 30.0:
                    Enter(GeneratorRunState.Off);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Transitions to a new state and resets the dwell timer.
        /// </summary>
        /// <param name="state">The state to enter.</param>
        private void Enter(GeneratorRunState state)
        {
            GeneratorRunState previous = m_state;
            m_state = state;
            m_stateSeconds = 0.0;
            if (previous != state)
            {
                // Counted here rather than in the automatic cycle, so a start
                // commanded through the Start or StartTest method is counted too. A
                // start counter that only sees half the starts is worse than none.
                if (state == GeneratorRunState.Starting)
                {
                    m_starts++;
                }
                StateChanged?.Invoke(previous, state);
            }
        }

        /// <summary>
        /// Returns the load fraction for the current state and tick.
        /// </summary>
        /// <param name="tick">Monotonic tick counter.</param>
        /// <returns>Load as a fraction of prime power.</returns>
        private double ComputeLoadFraction(long tick)
        {
            if (!IsLoaded)
            {
                return 0.0;
            }
            double phase = (tick * m_tickSeconds / 45.0) + (m_profile * 0.9);
            double swing = Math.Sin(phase) * GeneratorDatasheet.Simulation.LoadSwingFraction;
            double load = GeneratorDatasheet.Simulation.NominalLoadFraction + swing;
            if (m_fault == GeneratorFault.Overload)
            {
                load = GeneratorDatasheet.TripPoints.OverloadFraction + 0.06;
            }

            // The ceiling is above the overload trip point rather than equal to it:
            // clamping to exactly the trip point makes a strictly-greater-than
            // comparison unsatisfiable, so the protection could never annunciate.
            return Numeric.Clamp(load, 0.05, GeneratorDatasheet.TripPoints.OverloadFraction + 0.10);
        }

        /// <summary>
        /// Derives and publishes every measured value from the load fraction.
        /// </summary>
        /// <param name="load">Load as a fraction of prime power.</param>
        private void Publish(double load)
        {
            bool spinning = m_state
                is GeneratorRunState.Warmup
                or GeneratorRunState.Running
                or GeneratorRunState.Loaded
                or GeneratorRunState.Synchronizing
                or GeneratorRunState.Paralleled
                or GeneratorRunState.Cooldown;

            SpeedRpm = m_state switch
            {
                GeneratorRunState.Starting =>
                    GeneratorDatasheet.Engine.RatedSpeedRpm * 0.18,
                GeneratorRunState.Stopping =>
                    GeneratorDatasheet.Engine.RatedSpeedRpm * 0.35,
                _ when spinning => GeneratorDatasheet.Engine.RatedSpeedRpm,
                _ => 0.0,
            };

            // Isochronous governor: rated speed with a small load-dependent droop
            // so the published frequency moves rather than sitting on a constant.
            double droop = spinning ? -load * 1.2 : 0.0;
            double speed = SpeedRpm + droop;
            double frequency = spinning
                ? speed * GeneratorDatasheet.Electrical.Poles / 120.0
                : 0.0;

            double realPower = load * GeneratorDatasheet.Ratings.PrimePowerWatts;
            double powerFactor = GeneratorDatasheet.Electrical.RatedPowerFactor;
            double apparentPower = realPower / powerFactor;
            double lineVoltage = spinning ? GeneratorDatasheet.Electrical.RatedLineVoltage : 0.0;
            double current = lineVoltage > 0.0
                ? apparentPower / (Math.Sqrt(3.0) * lineVoltage)
                : 0.0;

            // Thermal states chase their targets rather than jumping, so the twin
            // shows a machine warming up and cooling down.
            double coolantTarget = spinning
                ? GeneratorDatasheet.Curves.CoolantCelsius(load)
                : GeneratorDatasheet.Simulation.AmbientCelsius;
            m_coolantCelsius += (coolantTarget - m_coolantCelsius) * 0.02;

            double exhaustTarget = spinning
                ? GeneratorDatasheet.Curves.ExhaustCelsius(load)
                : GeneratorDatasheet.Simulation.AmbientCelsius;
            ExhaustCelsius += (exhaustTarget - ExhaustCelsius) * 0.05;

            OilPressureBar = spinning
                ? GeneratorDatasheet.Engine.RatedOilPressureBar * (0.85 + (0.15 * load))
                : 0.0;

            // A fault deviates the measurement from the healthy curve, which is what
            // takes it past a trip point. Applied here rather than inside the curves
            // so the datasheet identities keep holding for a healthy set.
            switch (m_fault)
            {
                case GeneratorFault.CoolingFailure when spinning:
                    m_coolantCelsius +=
                        (GeneratorDatasheet.TripPoints.HighCoolantCelsius + 8.0 - m_coolantCelsius)
                            * 0.25;
                    break;
                case GeneratorFault.OilPressureLoss when spinning:
                    OilPressureBar = GeneratorDatasheet.TripPoints.LowOilPressureBar * 0.5;
                    break;
                case GeneratorFault.GovernorFailure when spinning:
                    SpeedRpm = GeneratorDatasheet.TripPoints.OverspeedRpm * 1.04;
                    speed = SpeedRpm;
                    frequency = speed * GeneratorDatasheet.Electrical.Poles / 120.0;
                    break;
                default:
                    break;
            }

            double fuelLitresPerHour = spinning
                ? GeneratorDatasheet.Curves.FuelLitresPerHour(load)
                : 0.0;

            if (spinning)
            {
                double hours = m_tickSeconds / GeneratorDatasheet.Convert.SecondsPerHour;
                m_engineHours += hours;
                m_energyWattHours += realPower * hours;
                double burned = fuelLitresPerHour * hours;
                m_fuelConsumedLitres += burned;
                m_fuelLevelPercent = Math.Max(
                    0.0,
                    m_fuelLevelPercent - (100.0 * burned / GeneratorDatasheet.Fuel.TankCapacityLitres));
            }

            m_engine.Speed.SetValue(speed);
            m_engine.OilPressure.SetValue(GeneratorDatasheet.Convert.ToPascal(OilPressureBar));
            m_engine.CoolantTemperature.SetValue(GeneratorDatasheet.Convert.ToKelvin(m_coolantCelsius));
            m_engine.ExhaustTemperature.SetValue(GeneratorDatasheet.Convert.ToKelvin(ExhaustCelsius));
            m_engine.FuelRate.SetValue(
                GeneratorDatasheet.Convert.ToCubicMetresPerSecond(fuelLitresPerHour));
            m_engine.EngineHours.SetValue(m_engineHours);
            m_engine.PercentLoad.SetValue(load * 100.0);
            m_engine.Starts.SetValue(m_starts);

            m_alternator.Frequency.SetValue(frequency);
            m_alternator.RealPower.SetValue(realPower);
            m_alternator.ApparentPower.SetValue(apparentPower);
            m_alternator.Voltage.SetValue(lineVoltage);
            m_alternator.Current.SetValue(current);
            m_alternator.LoadPercent.SetValue(load * 100.0);
            m_alternator.Energy.SetValue(m_energyWattHours);
            m_alternator.PowerFactor.SetValue(realPower > 0.0 ? powerFactor : 0.0);

            // A real machine is never perfectly balanced; a small fixed imbalance
            // per phase keeps L1/L2/L3 genuinely distinct without breaking the
            // aggregate identity, because the three offsets sum to zero.
            double[] imbalance = [1.004, 0.998, 0.998];
            for (int i = 0; i < m_alternator.Phases.Length; i++)
            {
                PhaseUpdaters phase = m_alternator.Phases[i];
                phase.Voltage.SetValue(
                    spinning ? GeneratorDatasheet.Electrical.RatedPhaseVoltage * imbalance[i] : 0.0);
                phase.Current.SetValue(current * imbalance[i]);
                phase.RealPower.SetValue(realPower / 3.0 * imbalance[i]);
                phase.PowerFactor.SetValue(realPower > 0.0 ? powerFactor : 0.0);
            }

            m_plant.FuelLevel.SetValue(m_fuelLevelPercent);
            m_plant.FuelConsumed.SetValue(m_fuelConsumedLitres);
            m_plant.AmbientTemperature.SetValue(
                GeneratorDatasheet.Convert.ToKelvin(GeneratorDatasheet.Simulation.AmbientCelsius));
            m_plant.OilTemperature.SetValue(
                GeneratorDatasheet.Convert.ToKelvin(Math.Max(
                    GeneratorDatasheet.Simulation.AmbientCelsius,
                    m_coolantCelsius + 8.0)));
            m_plant.BatteryVoltage.SetValue(
                m_state == GeneratorRunState.Starting
                    ? GeneratorDatasheet.Simulation.BatteryVolts * 0.82
                    : GeneratorDatasheet.Simulation.BatteryVolts + (spinning ? 3.6 : 0.0));
            m_plant.BreakerClosed.SetValue(IsLoaded);
            m_plant.AvailableToLoad.SetValue(
                m_state is GeneratorRunState.Running or GeneratorRunState.Loaded);
        }
    }
}
