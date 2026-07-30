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
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly List<PumpSimulationState> m_simulatedPumps = [];

        partial void Configure(INodeManagerBuilder builder)
        {
            Server.Telemetry.CreateLogger<PumpNodeManager>()
                .ConfiguringPumpNodeManagerFluentWiring();

            var historian = builder.UseHistorian();
            historian.UseInMemory();
            historian.RegisterAsDefault();

            for (int pumpNumber = 1; pumpNumber <= m_pumpStates.Count; pumpNumber++)
            {
                string pumpBrowseName = GetPumpBrowseName(pumpNumber);
                var state = new PumpSimulationState(
                    pumpNumber,
                    (pumpNumber - 1) * (Math.PI / 5.0));
                m_simulatedPumps.Add(state);

                WithIdentification(builder, pumpBrowseName, pumpNumber);
                WithMaintenance(builder, pumpBrowseName);
                WithMeasurements(builder, pumpBrowseName, state);
                WithSupervision(builder, pumpBrowseName, state);
            }

            builder.Simulation(SimulationInterval)
                .OnTick(AdvanceSimulationAsync);
        }

        private static void WithIdentification(
            INodeManagerBuilder builder,
            string pumpBrowseName,
            int pumpNumber)
        {
            string serialNumber = "SN-" + pumpNumber.ToString("D3", CultureInfo.InvariantCulture);
            builder.Node(pumpBrowseName + "/Identification")
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

        private void WithMaintenance(INodeManagerBuilder builder, string pumpBrowseName)
        {
            NodeId functionalGroupType = NodeId.Create(
                Opc.Ua.Di.ObjectTypes.FunctionalGroupType,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                Server.NamespaceUris);

            builder.Node(pumpBrowseName + "/Maintenance")
                .AddObject(
                    new QualifiedName("GeneralMaintenance", (ushort)Server.NamespaceUris.GetIndex(
                        Opc.Ua.Pumps.Namespaces.Pumps)),
                    functionalGroupType)
                .WithProperty("MaintenancePlan", Variant.From("Inspect seals and bearings every 30 days."));
        }

        private void WithMeasurements(
            INodeManagerBuilder builder,
            string pumpBrowseName,
            PumpSimulationState state)
        {
            string measurementsPath = pumpBrowseName + "/Operational/Measurements/";
            AddMeasurement(builder,
                measurementsPath + "DifferentialPressure",
                state,
                snapshot => snapshot.Pressure,
                EngineeringUnits.Pascal,
                min: 0,
                max: 1_000_000);

            AddMeasurement(builder,
                measurementsPath + "FluidTemperature",
                state,
                snapshot => snapshot.Temperature,
                EngineeringUnits.Kelvin,
                min: 233.15,
                max: 473.15);

            AddMeasurement(builder,
                measurementsPath + "BearingTemperature",
                state,
                snapshot => snapshot.BearingTemperature,
                EngineeringUnits.Kelvin,
                min: 233.15,
                max: 473.15);

            AddMeasurement(builder,
                measurementsPath + "PumpPowerInput",
                state,
                snapshot => snapshot.Power,
                EngineeringUnits.Watt,
                min: 0,
                max: 50_000);

            AddMeasurement(builder,
                measurementsPath + "MassFlow",
                state,
                snapshot => snapshot.Flow,
                EngineeringUnits.KilogramsPerSecond,
                min: 0,
                max: 1.0);

            AddMeasurement(builder,
                measurementsPath + "PumpEfficiency",
                state,
                snapshot => snapshot.Efficiency,
                EngineeringUnits.Percent,
                min: 0,
                max: 100);

            AddMeasurement(builder,
                measurementsPath + "Level",
                state,
                snapshot => snapshot.Level,
                EngineeringUnits.Metre,
                min: 0,
                max: 10);

            IVariableBuilder<uint> starts = builder.Variable<uint>(
                measurementsPath + "NumberOfStarts");
            starts.OnRead((
                ISystemContext context,
                NodeState node,
                NumericRange indexRange,
                QualifiedName dataEncoding,
                ref Variant value,
                ref StatusCode statusCode,
                ref DateTimeUtc timestamp) =>
            {
                if (!state.TryRead(out PumpSimulationSnapshot snapshot))
                {
                    statusCode = StatusCodes.BadWaitingForInitialData;
                    timestamp = DateTime.UtcNow;
                    value = Variant.Null;
                    return ServiceResult.Good;
                }

                value = Variant.From((uint)snapshot.NumberOfStarts);
                statusCode = StatusCodes.Good;
                timestamp = snapshot.Timestamp;
                return ServiceResult.Good;
            });
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

        private void WithSupervision(
            INodeManagerBuilder builder,
            string pumpBrowseName,
            PumpSimulationState state)
        {
            ushort pumpsNs = (ushort)Server.NamespaceUris.GetIndex(
                Opc.Ua.Pumps.Namespaces.Pumps);

            INodeBuilder<PumpState> pump = builder.Node<PumpState>(pumpBrowseName);

            IAlarmBuilder<NonExclusiveLimitAlarmState> tempAlarm = pump
                .Components().Events()
                .CreateLimitAlarm(new QualifiedName("OverTempAlarm", pumpsNs))
                .WithLimits(highHigh: 373.15, high: 363.15, low: 283.15, lowLow: 273.15)
                .OnAcknowledge((ctx, c, eventId, comment) => ServiceResult.Good);

            IVariableBuilder<bool> cavitation = pump.Components().Events()
                .Components().SupervisionProcessFluid()
                .Components().Cavitation();
            WireBoolean(
                cavitation,
                state,
                snapshot => snapshot.Cavitation,
                new LocalizedText("Cavitation"),
                new LocalizedText("No cavitation"));
            cavitation.ActivatesAlarm(tempAlarm)
                .Historize(historyAccessLevel: AccessLevels.HistoryRead);

            IVariableBuilder<bool> motorOverheat = pump.Components().Events()
                .Components().SupervisionPumpOperation()
                .Components().MotorOverheat();
            WireBoolean(
                motorOverheat,
                state,
                snapshot => snapshot.MotorOverheat,
                new LocalizedText("Motor overheat"),
                new LocalizedText("No motor overheat"));
            motorOverheat.Historize(historyAccessLevel: AccessLevels.HistoryRead);
        }

        private static void AddMeasurement(
            INodeManagerBuilder builder,
            string browsePath,
            PumpSimulationState state,
            Func<PumpSimulationSnapshot, double> getter,
            EUInformation units,
            double min,
            double max)
        {
            IVariableBuilder<double> variable = builder.Variable<double>(browsePath)
                .WithEngineeringUnits(units)
                .WithEURange(min, max)
                .Historize(historyAccessLevel: AccessLevels.HistoryRead);
            BaseVariableState variableState = variable.Node;
            state.RegisterAnalogVariable(variableState, getter);
            variable.OnRead((
                ISystemContext context,
                NodeState node,
                NumericRange indexRange,
                QualifiedName dataEncoding,
                ref Variant value,
                ref StatusCode statusCode,
                ref DateTimeUtc timestamp) =>
            {
                if (!state.TryRead(out PumpSimulationSnapshot snapshot))
                {
                    statusCode = StatusCodes.BadWaitingForInitialData;
                    timestamp = DateTime.UtcNow;
                    value = Variant.Null;
                    return ServiceResult.Good;
                }

                value = Variant.From(getter(snapshot));
                statusCode = StatusCodes.Good;
                timestamp = snapshot.Timestamp;
                return ServiceResult.Good;
            });
        }

        private static void WireBoolean(
            IVariableBuilder<bool> variable,
            PumpSimulationState state,
            Func<PumpSimulationSnapshot, bool> getter,
            LocalizedText trueState,
            LocalizedText falseState)
        {
            BaseVariableState variableState = variable.Node;
            state.RegisterBooleanVariable(variableState, getter);
            variable.OnRead((
                ISystemContext context,
                NodeState node,
                NumericRange indexRange,
                QualifiedName dataEncoding,
                ref Variant value,
                ref StatusCode statusCode,
                ref DateTimeUtc timestamp) =>
            {
                if (!state.TryRead(out PumpSimulationSnapshot snapshot))
                {
                    statusCode = StatusCodes.BadWaitingForInitialData;
                    timestamp = DateTime.UtcNow;
                    value = Variant.Null;
                    return ServiceResult.Good;
                }

                value = Variant.From(getter(snapshot));
                statusCode = StatusCodes.Good;
                timestamp = snapshot.Timestamp;
                return ServiceResult.Good;
            });

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

        private async ValueTask AdvanceSimulationAsync(
            ISystemContext context,
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            foreach (PumpSimulationState state in m_simulatedPumps)
            {
                PumpSimulationSnapshot snapshot = state.Advance();
                await state.PublishAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
            }
        }

        private readonly record struct PumpSimulationSnapshot(
            double Pressure,
            double Temperature,
            double BearingTemperature,
            double Power,
            double Flow,
            double Efficiency,
            double Level,
            bool Cavitation,
            bool MotorOverheat,
            long NumberOfStarts,
            DateTimeUtc Timestamp);

        private sealed class PumpSimulationState
        {
            private readonly System.Threading.Lock m_lock = new();
            private readonly List<AnalogBinding> m_analogBindings = [];
            private readonly List<BooleanBinding> m_booleanBindings = [];
            private readonly int m_pumpNumber;
            private readonly double m_phaseOffset;
            private PumpSimulationSnapshot m_snapshot;
            private long m_ticks;
            private bool m_hasValue;

            public PumpSimulationState(int pumpNumber, double phaseOffset)
            {
                m_pumpNumber = pumpNumber;
                m_phaseOffset = phaseOffset;
            }

            public void RegisterAnalogVariable(
                BaseVariableState variable,
                Func<PumpSimulationSnapshot, double> getter)
            {
                m_analogBindings.Add(new AnalogBinding(variable, getter));
            }

            public void RegisterBooleanVariable(
                BaseVariableState variable,
                Func<PumpSimulationSnapshot, bool> getter)
            {
                m_booleanBindings.Add(new BooleanBinding(variable, getter));
            }

            public bool TryRead(out PumpSimulationSnapshot snapshot)
            {
                lock (m_lock)
                {
                    snapshot = m_snapshot;
                    return m_hasValue;
                }
            }

            public PumpSimulationSnapshot Advance()
            {
                long t = Interlocked.Increment(ref m_ticks);
                double shifted = t + (m_phaseOffset * 33.333333333333336);
                long numberOfStarts = 1 + ((t + (m_pumpNumber * 257L)) / 3600L);
                var snapshot = new PumpSimulationSnapshot(
                    200000.0 + (50000.0 * Math.Sin((shifted * 0.03) + m_phaseOffset)),
                    313.15 + (5.0 * Math.Sin((shifted * 0.01) + m_phaseOffset)),
                    333.15 + (8.0 * Math.Cos((shifted * 0.008) + m_phaseOffset)),
                    5000.0 + (500.0 * Math.Sin((shifted * 0.02) + m_phaseOffset)),
                    0.05 + (0.005 * Math.Cos((shifted * 0.04) + m_phaseOffset)),
                    75.0 + (10.0 * Math.Sin((shifted * 0.015) + m_phaseOffset)),
                    2.5 + (0.5 * Math.Sin((shifted * 0.02) + m_phaseOffset)),
                    ((t + (m_pumpNumber * 13L)) % 120L) > 100L,
                    ((t + (m_pumpNumber * 17L)) % 200L) > 190L,
                    numberOfStarts,
                    DateTime.UtcNow);

                lock (m_lock)
                {
                    m_snapshot = snapshot;
                    m_hasValue = true;
                }

                return snapshot;
            }

            public async ValueTask PublishAsync(
                ISystemContext context,
                PumpSimulationSnapshot snapshot,
                CancellationToken cancellationToken)
            {
                foreach (AnalogBinding binding in m_analogBindings)
                {
                    PublishValue(binding.Variable, Variant.From(binding.Getter(snapshot)), snapshot.Timestamp);
                    await binding.Variable.ClearChangeMasksAsync(context, false, cancellationToken)
                        .ConfigureAwait(false);
                }

                foreach (BooleanBinding binding in m_booleanBindings)
                {
                    PublishValue(binding.Variable, Variant.From(binding.Getter(snapshot)), snapshot.Timestamp);
                    await binding.Variable.ClearChangeMasksAsync(context, false, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            private static void PublishValue(
                BaseVariableState variable,
                Variant value,
                DateTimeUtc timestamp)
            {
                variable.WrappedValue = value;
                variable.StatusCode = StatusCodes.Good;
                variable.Timestamp = timestamp;
            }

            private readonly record struct AnalogBinding(
                BaseVariableState Variable,
                Func<PumpSimulationSnapshot, double> Getter);

            private readonly record struct BooleanBinding(
                BaseVariableState Variable,
                Func<PumpSimulationSnapshot, bool> Getter);
        }
    }

    internal static partial class PumpNodeManagerLog
    {
        [LoggerMessage(EventId = PumpDeviceIntegrationServerEventIds.PumpNodeManager + 0,
            Level = LogLevel.Information,
            Message = "Configuring PumpNodeManager fluent wiring...")]
        public static partial void ConfiguringPumpNodeManagerFluentWiring(this ILogger logger);
    }
}
