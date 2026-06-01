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
        private double m_speedSetPoint = 50.0;
        private double m_setPointValue = 100.0;
        private long m_numberOfStarts;
        private long m_operatingTimeTicks;

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

            ushort pumpsNs = (ushort)Server.NamespaceUris.GetIndex(PumpsNamespaceUri);

            WithIdentification(builder, pumpsNs);
            WithMeasurements(builder);
            WithActuation(builder);
            WithSignals(builder);
            WithSupervision(builder, pumpsNs);
            WithMaintenance(builder);

            // Single manager-owned simulation tick advances all live
            // measurements at 250 ms intervals; lets time-based fault
            // injection work cleanly.
            builder.Simulation(TimeSpan.FromMilliseconds(250))
                .OnTick((ctx, elapsed) => AdvanceSimulation());
        }

        // ── Identification properties via WithProperty ──────────────
        private void WithIdentification(INodeManagerBuilder builder, ushort ns)
        {
            try
            {
                INodeBuilder id = builder.Node("Pump #1/Identification");
                id.WithProperty("Manufacturer", "SimPump Corp")
                  .WithProperty("Model", "PumpX-2000")
                  .WithProperty("SerialNumber", "SN-001")
                  .WithProperty("HardwareRevision", "1.0")
                  .WithProperty("SoftwareRevision", "2.5.3")
                  .WithProperty("DeviceRevision", "3")
                  .WithProperty("DeviceClass", "Pump")
                  .WithProperty("ProductInstanceUri",
                      "urn:simdevice:SimPump:PumpX-2000:SN-001");
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                Server.Telemetry.CreateLogger<PumpNodeManager>()
                    .LogDebug("Identification path not found, skipping ID wiring.");
            }
        }

        // ── Measurements with engineering units ─────────────────────
        private void WithMeasurements(INodeManagerBuilder builder)
        {
            TryAddMeasurement(builder,
                "Pump #1/Operational/Measurements/DifferentialPressure",
                () => m_currentPressure,
                EngineeringUnits.Pascal, min: 0, max: 1_000_000);

            TryAddMeasurement(builder,
                "Pump #1/Operational/Measurements/FluidTemperature",
                () => m_currentTemperature,
                EngineeringUnits.Kelvin, min: 233.15, max: 473.15);

            TryAddMeasurement(builder,
                "Pump #1/Operational/Measurements/BearingTemperature",
                () => m_currentBearingTemp,
                EngineeringUnits.Kelvin, min: 233.15, max: 473.15);

            TryAddMeasurement(builder,
                "Pump #1/Operational/Measurements/PumpPowerInput",
                () => m_currentPower,
                EngineeringUnits.Watt, min: 0, max: 50_000);

            TryAddMeasurement(builder,
                "Pump #1/Operational/Measurements/MassFlow",
                () => m_currentFlow,
                EngineeringUnits.KilogramsPerSecond, min: 0, max: 1.0);

            TryAddMeasurement(builder,
                "Pump #1/Operational/Measurements/PumpEfficiency",
                () => m_currentEfficiency,
                EngineeringUnits.Percent, min: 0, max: 100);

            TryAddMeasurement(builder,
                "Pump #1/Operational/Measurements/Level",
                () => m_currentLevel,
                EngineeringUnits.Metre, min: 0, max: 10);
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

        // ── Actuation ───────────────────────────────────────────────
        private void WithActuation(INodeManagerBuilder builder)
        {
            TryAddVariableReadWrite<double>(builder,
                "Pump #1/Operational/Actuation/SetPointValue",
                () => m_setPointValue,
                v => m_setPointValue = v);

            TryAddVariableReadWrite<double>(builder,
                "Pump #1/Operational/Actuation/SpeedSetPoint",
                () => m_speedSetPoint,
                v => m_speedSetPoint = v);
        }

        // ── Signals ─────────────────────────────────────────────────
        private void WithSignals(INodeManagerBuilder builder)
        {
            TryAddVariable<bool>(builder,
                "Pump #1/Operational/Signals/PumpOperation",
                () => true);

            TryAddVariable<bool>(builder,
                "Pump #1/Operational/Signals/RatedSpeed",
                () => m_speedSetPoint > 45.0);
        }

        // ── Supervision flags wired to NAMUR alarms ─────────────────
        private void WithSupervision(INodeManagerBuilder builder, ushort pumpsNs)
        {
            // Attach a temperature-high alarm on the Events container and
            // wire the Cavitation supervision flag to drive it.
            try
            {
                INodeBuilder events = builder.Node("Pump #1/Events");

                // Limit alarm with thresholds in Kelvin.
                IAlarmBuilder<NonExclusiveLimitAlarmState> tempAlarm = events
                    .CreateLimitAlarm(new QualifiedName("OverTempAlarm", pumpsNs))
                    .WithLimits(highHigh: 373.15, high: 363.15, low: 283.15, lowLow: 273.15)
                    .OnAcknowledge((ctx, c, eventId, comment) => ServiceResult.Good);

                // bool supervision → alarm Active state.
                TryAddSupervisionEdge(builder,
                    "Pump #1/Events/Supervision/ProcessFluid/Cavitation",
                    tempAlarm);
            }
            catch (ServiceResultException)
            {
                // Events container not found — skip alarm wiring (older NodeSet).
            }

            // Per-read simulated supervision flags (kept lightweight).
            TryAddVariable<bool>(builder,
                "Pump #1/Events/Supervision/ProcessFluid/Cavitation",
                () => m_cavitation);

            TryAddVariable<bool>(builder,
                "Pump #1/Events/Supervision/PumpOperation/MotorOverheat",
                () => m_motorOverheat);
        }

        // ── Maintenance ─────────────────────────────────────────────
        private void WithMaintenance(INodeManagerBuilder builder)
        {
            TryAddVariable<double>(builder,
                "Pump #1/Maintenance/GeneralMaintenance/OperatingTime",
                () => SimulateOperatingTime());

            TryAddVariable<uint>(builder,
                "Pump #1/Operational/Measurements/NumberOfStarts",
                () => (uint)Interlocked.Read(ref m_numberOfStarts));
        }

        // ── Helper: try wiring a variable, log if path not found ────
        private void TryAddVariable<T>(
            INodeManagerBuilder builder,
            string browsePath,
            Func<T> getter)
        {
            try
            {
                builder.Variable<T>(browsePath).OnRead(getter);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                Server.Telemetry.CreateLogger<PumpNodeManager>()
                    .LogDebug("Browse path not found (skipping): {Path}", browsePath);
            }
        }

        private void TryAddVariableReadWrite<T>(
            INodeManagerBuilder builder,
            string browsePath,
            Func<T> getter,
            Action<T> setter)
        {
            try
            {
                builder.Variable<T>(browsePath)
                    .OnRead(getter)
                    .OnWrite(setter);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                Server.Telemetry.CreateLogger<PumpNodeManager>()
                    .LogDebug("Browse path not found (skipping): {Path}", browsePath);
            }
        }

        // Typed measurement wiring with engineering units + EURange.
        private void TryAddMeasurement(
            INodeManagerBuilder builder,
            string browsePath,
            Func<double> getter,
            EUInformation units,
            double min,
            double max)
        {
            try
            {
                IVariableBuilder<double> v = builder.Variable<double>(browsePath);
                v.OnRead(getter);
                try
                {
                    v.WithEngineeringUnits(units).WithEURange(min, max);
                }
                catch (ServiceResultException ex) when (
                    ex.StatusCode == StatusCodes.BadTypeMismatch)
                {
                    // Not an AnalogItemState — read-only wiring is still valid,
                    // engineering units just don't apply.
                }
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                Server.Telemetry.CreateLogger<PumpNodeManager>()
                    .LogDebug("Browse path not found (skipping): {Path}", browsePath);
            }
        }

        // Wire a bool supervision variable to activate/deactivate an alarm
        // on each rising / falling edge.
        private void TryAddSupervisionEdge<TAlarm>(
            INodeManagerBuilder builder,
            string boolPath,
            IAlarmBuilder<TAlarm> alarm)
            where TAlarm : ConditionState
        {
            try
            {
                builder.Variable<bool>(boolPath).ActivatesAlarm(alarm);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                Server.Telemetry.CreateLogger<PumpNodeManager>()
                    .LogDebug("Supervision flag not found (skipping): {Path}", boolPath);
            }
        }

        // ── Simulation tick — advances all live measurements ────────
        private void AdvanceSimulation()
        {
            long t = Interlocked.Increment(ref m_simulationTicks);
            double speedFactor = m_speedSetPoint / 50.0;

            m_currentPressure = 200000.0 + (50000.0 * speedFactor * Math.Sin(t * 0.03));
            m_currentTemperature = 313.15 + (5.0 * Math.Sin(t * 0.01));
            m_currentBearingTemp = 333.15 + (8.0 * Math.Cos(t * 0.008));
            m_currentPower = 5000.0 * speedFactor + (500.0 * Math.Sin(t * 0.02));
            m_currentFlow = 0.05 * speedFactor + (0.005 * Math.Cos(t * 0.04));
            m_currentEfficiency = 75.0 + (10.0 * Math.Sin(t * 0.015));
            m_currentLevel = 2.5 + (0.5 * Math.Sin(t * 0.02));

            // Fault injection — supervision flags transition true/false.
            m_cavitation = (t % 120) > 100;
            m_motorOverheat = (t % 200) > 190;

            // Periodic restart simulation — every 3600 ticks (~15 min at 250ms).
            if (t % 3600 == 0)
            {
                Interlocked.Increment(ref m_numberOfStarts);
            }
            Interlocked.Increment(ref m_operatingTimeTicks);
        }

        private double SimulateOperatingTime()
            => Interlocked.Read(ref m_operatingTimeTicks) / 3600.0;
    }
}
