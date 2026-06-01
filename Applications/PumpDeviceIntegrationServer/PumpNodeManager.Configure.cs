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
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
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
        // ── Simulation state ────────────────────────────────────────
        private long m_simulationTicks;
        private long m_numberOfStarts;

        // Reference to the hand-rolled Pump #1 instance so the
        // simulation tick can mutate its DI properties in response to
        // supervision flags. Set by CreatePumpInstanceAsync.
        private global::Opc.Ua.Pumps.PumpState? m_pump1;

        // Optional DI DeviceHealth variable supplied by a declarative
        // DeviceState device (e.g. Pump #2). Set via
        // RegisterSupervisedDeviceHealth and toggled by AdvanceSimulation
        // to reflect cavitation / motor-overheat states using the NAMUR
        // NE 107 enumeration.
        private BaseDataVariableState<global::Opc.Ua.Di.DeviceHealthEnumeration>?
            m_supervisedDeviceHealth;

        // ── Latest simulated values, updated by the simulation tick. ──
        private double m_currentPressure;
        private double m_currentTemperature = 313.15;
        private double m_currentBearingTemp = 333.15;
        private double m_currentPower;
        private double m_currentFlow;
        private double m_currentEfficiency = 75.0;
        private double m_currentLevel = 2.5;
        private bool m_cavitation;
        private bool m_motorOverheat;

        partial void Configure(INodeManagerBuilder builder)
        {
            Server.Telemetry.CreateLogger<PumpNodeManager>()
                .LogInformation("Configuring PumpNodeManager fluent wiring...");

            WithIdentification(builder);
            WithMeasurements(builder);
            WithSupervision(builder);

            // Single manager-owned simulation tick advances all live
            // measurements at 250 ms intervals; lets time-based fault
            // injection work cleanly.
            builder.Simulation(TimeSpan.FromMilliseconds(250))
                .OnTick((ctx, elapsed) => AdvanceSimulation());
        }

        // ── Identification properties via WithProperty ──────────────
        // PumpType.Identification is a mandatory child of PumpType so
        // it is materialised by the source-generated factory used in
        // CreatePumpInstanceAsync. BrowsePathResolver's cross-namespace
        // name-only fallback (FB-3 phase 1) resolves the unqualified
        // 'Identification' segment to the DI-namespace child without
        // requiring an explicit ns= prefix in the path.
        private void WithIdentification(INodeManagerBuilder builder)
        {
            builder.Node("Pump #1/Identification")
                .WithProperty("Manufacturer", "SimPump Corp")
                .WithProperty("SerialNumber", "SN-001")
                .WithProperty("ProductInstanceUri",
                    "urn:simdevice:SimPump:PumpX-2000:SN-001");
        }

        // ── Measurements with engineering units ─────────────────────
        // All seven analog measurements live under
        // PumpType.Operational.Measurements and are materialised by
        // CreatePumpInstanceAsync via the generator-emitted AddXxx
        // helpers. The cross-namespace name-only resolver fallback
        // means the unqualified browse path resolves through the
        // Pumps -> Machinery (Operational) -> Pumps (Measurements +
        // analog states) namespace transitions transparently.
        private void WithMeasurements(INodeManagerBuilder builder)
        {
            AddMeasurement(builder,
                "Pump #1/Operational/Measurements/DifferentialPressure",
                () => m_currentPressure,
                EngineeringUnits.Pascal, min: 0, max: 1_000_000);

            AddMeasurement(builder,
                "Pump #1/Operational/Measurements/FluidTemperature",
                () => m_currentTemperature,
                EngineeringUnits.Kelvin, min: 233.15, max: 473.15);

            AddMeasurement(builder,
                "Pump #1/Operational/Measurements/BearingTemperature",
                () => m_currentBearingTemp,
                EngineeringUnits.Kelvin, min: 233.15, max: 473.15);

            AddMeasurement(builder,
                "Pump #1/Operational/Measurements/PumpPowerInput",
                () => m_currentPower,
                EngineeringUnits.Watt, min: 0, max: 50_000);

            AddMeasurement(builder,
                "Pump #1/Operational/Measurements/MassFlow",
                () => m_currentFlow,
                EngineeringUnits.KilogramsPerSecond, min: 0, max: 1.0);

            AddMeasurement(builder,
                "Pump #1/Operational/Measurements/PumpEfficiency",
                () => m_currentEfficiency,
                EngineeringUnits.Percent, min: 0, max: 100);

            AddMeasurement(builder,
                "Pump #1/Operational/Measurements/Level",
                () => m_currentLevel,
                EngineeringUnits.Metre, min: 0, max: 10);

            // Discrete count exposed alongside the analog measurements.
            builder.Variable<uint>(
                "Pump #1/Operational/Measurements/NumberOfStarts")
                .OnRead(() => (uint)Interlocked.Read(ref m_numberOfStarts));
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

        // ── Supervision flags wired to NAMUR alarms ─────────────────
        // Cavitation / MotorOverheat are TwoStateDiscreteState nodes
        // under PumpType.Events.SupervisionProcessFluid and .Supervision-
        // PumpOperation respectively. TwoStateDiscreteState inherits
        // BaseDataVariableState<bool> so the fluent Variable<bool>(path)
        // lookup picks them up. The alarm attached to the Events
        // container flips Active in lockstep with the cavitation flag.
        private void WithSupervision(INodeManagerBuilder builder)
        {
            ushort pumpsNs = (ushort)Server.NamespaceUris.GetIndex(
                global::Opc.Ua.Pumps.Namespaces.Pumps);

            IAlarmBuilder<NonExclusiveLimitAlarmState> tempAlarm = builder
                .Node("Pump #1/Events")
                .CreateLimitAlarm(new QualifiedName("OverTempAlarm", pumpsNs))
                .WithLimits(highHigh: 373.15, high: 363.15, low: 283.15, lowLow: 273.15)
                .OnAcknowledge((ctx, c, eventId, comment) => ServiceResult.Good);

            builder.Variable<bool>(
                "Pump #1/Events/SupervisionProcessFluid/Cavitation")
                .OnRead(() => m_cavitation)
                .ActivatesAlarm(tempAlarm);

            builder.Variable<bool>(
                "Pump #1/Events/SupervisionPumpOperation/MotorOverheat")
                .OnRead(() => m_motorOverheat);
        }

        // Direct measurement wiring — failures now propagate as
        // BadNodeIdUnknown ServiceResultException so wiring errors
        // surface at configuration time rather than getting silently
        // logged. The legacy TryAdd* helpers and their per-method
        // try/catch blocks were necessary while the optional pump
        // subtree was unmaterialised; CreatePumpInstanceAsync now
        // materialises every wired leaf so the wiring is unconditional.
        private static void AddMeasurement(
            INodeManagerBuilder builder,
            string browsePath,
            Func<double> getter,
            EUInformation units,
            double min,
            double max)
        {
            builder.Variable<double>(browsePath)
                .OnRead(getter)
                .WithEngineeringUnits(units)
                .WithEURange(min, max);
        }

        // ── Simulation tick — advances all live measurements ────────
        private void AdvanceSimulation()
        {
            long t = Interlocked.Increment(ref m_simulationTicks);

            m_currentPressure = 200000.0 + (50000.0 * Math.Sin(t * 0.03));
            m_currentTemperature = 313.15 + (5.0 * Math.Sin(t * 0.01));
            m_currentBearingTemp = 333.15 + (8.0 * Math.Cos(t * 0.008));
            m_currentPower = 5000.0 + (500.0 * Math.Sin(t * 0.02));
            m_currentFlow = 0.05 + (0.005 * Math.Cos(t * 0.04));
            m_currentEfficiency = 75.0 + (10.0 * Math.Sin(t * 0.015));
            m_currentLevel = 2.5 + (0.5 * Math.Sin(t * 0.02));

            // Fault injection — supervision flags transition true/false.
            m_cavitation = (t % 120) > 100;
            m_motorOverheat = (t % 200) > 190;

            // Map the simulated supervision flags onto the DI DeviceHealth
            // NAMUR NE 107 enumeration. Motor overheat is the more severe
            // condition so it wins when both are active. The variable is
            // ClearChangeMasks-ed so subscriptions see each transition.
            UpdateDeviceHealth();

            // Periodic restart simulation — every 3600 ticks (~15 min at 250ms).
            if (t % 3600 == 0)
            {
                Interlocked.Increment(ref m_numberOfStarts);
            }
        }

        /// <summary>
        /// Maps the simulated supervision flags onto the DI
        /// <see cref="global::Opc.Ua.Di.DeviceHealthEnumeration"/>
        /// using the NAMUR NE 107 severity order: a motor overheat
        /// always wins over a cavitation event (FAILURE &gt;
        /// MAINTENANCE_REQUIRED); when neither flag is set the device
        /// reports NORMAL. Exposed as a pure function so tests can
        /// exercise the mapping without instantiating the manager.
        /// </summary>
        public static global::Opc.Ua.Di.DeviceHealthEnumeration
            MapSupervisionToDeviceHealth(bool cavitation, bool motorOverheat)
        {
            if (motorOverheat)
            {
                return global::Opc.Ua.Di.DeviceHealthEnumeration.FAILURE;
            }
            if (cavitation)
            {
                return global::Opc.Ua.Di.DeviceHealthEnumeration.MAINTENANCE_REQUIRED;
            }
            return global::Opc.Ua.Di.DeviceHealthEnumeration.NORMAL;
        }

        private void UpdateDeviceHealth()
        {
            BaseDataVariableState<global::Opc.Ua.Di.DeviceHealthEnumeration>?
                health = m_supervisedDeviceHealth;
            if (health == null) { return; }

            global::Opc.Ua.Di.DeviceHealthEnumeration desired =
                MapSupervisionToDeviceHealth(m_cavitation, m_motorOverheat);

            if (health.Value != desired)
            {
                health.Value = desired;
                health.Timestamp = DateTime.UtcNow;
                health.ClearChangeMasks(SystemContext, includeChildren: false);
            }
        }
    }
}
