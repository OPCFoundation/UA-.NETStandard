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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// A single typed sensor, reusing ProcessValues for raw publication, units, limits and optional alarms.
    /// </summary>
    public sealed class WmtpMeasurement
    {
        /// <summary>
        /// Selects the allowed richer signal subtype while constructing a new, unpublished measurement.
        /// </summary>
        internal WmtpMeasurement(
            WmtpPeripheral peripheral,
            IMachineBuilder<WirelessMachineToolPeripheralState> builder,
            WmtpMeasurementDefinition definition,
            IWmtpMeasurementProvider provider)
        {
            m_peripheral = peripheral;
            m_definition = definition;
            m_provider = provider;
            ISystemContext context = peripheral.Context;
            State = context.CreateInstanceOfWMTPMeasurementType(
                peripheral.State.Measurements!, new QualifiedName(definition.Name, peripheral.NodeId.NamespaceIndex));
            State.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            State.ModellingRuleId = NodeId.Null;
            peripheral.State.Measurements!.AddChild(State);
            ProcessValueVariableState signal = context.CreateInstanceOfProcessValueVariableType(State,
                new QualifiedName(Opc.Ua.PADIM.BrowseNames.AnalogSignal,
                    context.NamespaceUris.GetIndexOrAppend(Opc.Ua.PADIM.Namespaces.PADIM)));
            signal.WrappedValue = Variant.Null;
            signal.StatusCode = StatusCodes.BadWaitingForInitialData;
            signal.DataType = new NodeId((uint)definition.DataType);
            signal.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            signal.ModellingRuleId = NodeId.Null;
            State.CreateOrReplaceAnalogSignal(context, signal);
            signal.OnReadValueAsync = ReadSignalAsync;
            signal.AddInstrumentRange(context, value => value.Value = (Range)definition.InstrumentRange.Clone());
            signal.AddValuePrecision(context, value => value.Value = definition.ValuePrecision);
            signal.AddDefinition(context, value => value.Value = definition.Definition);
            if ((definition.Features & WmtpMeasurementFeatures.LimitCounters) != 0)
            {
                m_counters = WmtpDeviceNodes.AddLimitCounters(context, peripheral.State, State);
            }
            builder.WithProcessValue(State, value =>
            {
                value.WithSignalTag(definition.Name)
                    .WithEngineeringUnits((EUInformation)definition.EngineeringUnits.Clone(),
                        (Range)definition.EURange.Clone())
                    .WithLimits(high: definition.High, highHigh: definition.HighHigh);
                if ((definition.Features & WmtpMeasurementFeatures.LimitAlarm) != 0)
                {
                    value.WithLimitAlarm();
                }
                value.Bind(out m_processValue);
            });
            State.TriggerSettings!.Value = definition.TriggerMilliseconds;
            State.TriggerSettings.EngineeringUnits!.Value = Milliseconds;
            State.DeltaCondition!.Value = WmtpValidation.Delta(
                definition.Delta, definition.DataType, definition.TriggerMilliseconds);
            State.DeltaCondition.EngineeringUnits!.Value = (EUInformation)definition.EngineeringUnits.Clone();
            SetDiscrete(State.TypeOfMeasurement!, (uint)definition.Kind);
            SetDiscrete(State.TypeOfSample!, (uint)definition.SampleKind);
            if ((definition.Features & WmtpMeasurementFeatures.WorkCycleInformation) != 0)
            {
                State.AddTimestamp(context).AddIndex(context).AddMeasurementPeriod(context);
            }
            if ((definition.Features & WmtpMeasurementFeatures.InternalUpdateInterval) != 0)
            {
                State.AddInternalUpdateInterval(context, node => node.Value = definition.InternalUpdateInterval);
            }
            if ((definition.Features & WmtpMeasurementFeatures.Uncertainty) != 0)
            {
                State.AddRelativeUncertainty(context, node =>
                {
                    node.Value = definition.RelativeUncertainty;
                    node.EngineeringUnits!.Value = Percent;
                }).AddAbsoluteUncertainty(context, node =>
                {
                    node.Value = definition.AbsoluteUncertainty;
                    node.EngineeringUnits!.Value = (EUInformation)definition.EngineeringUnits.Clone();
                });
            }
            Delta = definition.Delta;
            State.SetTriggerSettings!.OnCallAsync = async (ctx, _, _, duration, token) =>
            {
                await SetTriggerAsync(duration, peripheral.Request(ctx), token).ConfigureAwait(false);
                return new SetTriggerSettingsMethodStateResult { ServiceResult = ServiceResult.Good };
            };
            State.SetDeltaCondition!.OnCallAsync = async (ctx, _, _, delta, token) =>
            {
                await SetDeltaAsync(delta, peripheral.Request(ctx), token).ConfigureAwait(false);
                return new SetDeltaConditionMethodStateResult { ServiceResult = ServiceResult.Good };
            };
        }

        /// <summary>
        /// Gets the actual generated WMTP measurement, retaining its standard ProcessValue inheritance.
        /// </summary>
        public WMTPMeasurementState State { get; }

        /// <summary>
        /// Gets the sensor identifier.
        /// </summary>
        public string Name => m_definition.Name;

        /// <summary>
        /// Gets the exact last accepted delta, without coercing its provider-facing numeric type.
        /// </summary>
        public Variant Delta { get; private set; }

        /// <summary>
        /// Gets the last original acquisition; unavailable until an actual provider observation has been received.
        /// </summary>
        public WmtpMeasurementSample? Acquisition { get; private set; }

        /// <summary>
        /// Gets the server receipt time, distinct from the acquisition timestamp.
        /// </summary>
        public DateTimeUtc ReceivedAt => Volatile.Read(ref m_published)?.ReceivedAt ?? DateTimeUtc.MinValue;

        /// <summary>
        /// Gets the current raw, possibly age-degraded reading.
        /// </summary>
        public DataValue Sample
        {
            get
            {
                PublishedReading? reading = Volatile.Read(ref m_published);
                if (reading == null)
                {
                    return new DataValue(Variant.Null, StatusCodes.BadWaitingForInitialData);
                }
                DataValue sample = reading.Value;
                if (StatusCode.IsGood(sample.StatusCode) && !sample.SourceTimestamp.IsNull &&
                    DateTimeUtc.From(m_peripheral.TimeProvider.GetUtcNow()) - sample.SourceTimestamp >
                        m_definition.StaleAfter)
                {
                    sample = sample.WithStatus(StatusCodes.UncertainLastUsableValue);
                }
                return sample.WithServerTimestamp(reading.ReceivedAt);
            }
        }

        /// <summary>
        /// Gets the whole-millisecond trigger interval.
        /// </summary>
        public uint TriggerMilliseconds => State.TriggerSettings!.Value;

        /// <summary>
        /// Gets the explicit engineering unit for milliseconds.
        /// </summary>
        internal static EUInformation Milliseconds => new()
        {
            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
            UnitId = 4403766,
            DisplayName = new LocalizedText("ms"),
            Description = new LocalizedText("millisecond")
        };

        /// <summary>
        /// Gets the explicit engineering unit for percentage values.
        /// </summary>
        internal static EUInformation Percent => new()
        {
            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
            UnitId = 20529,
            DisplayName = new LocalizedText("%"),
            Description = new LocalizedText("percent")
        };

        /// <summary>
        /// Gets the validated sensor configuration used to bind actual capabilities, not mutable wire values.
        /// </summary>
        internal WmtpMeasurementDefinition Configuration => m_definition;

        /// <summary>
        /// Changes the interval only after authorized, atomic provider acceptance of both trigger conditions.
        /// Once accepted, the setting commits even if the caller cancels.
        /// </summary>
        public async ValueTask SetTriggerAsync(
            double milliseconds, WmtpRequest request, CancellationToken cancellationToken = default)
        {
            _ = await m_peripheral.RunAsync(request, WmtpOperation.ConfigureMeasurement, async token =>
            {
                uint trigger = WmtpValidation.Trigger(milliseconds, State.DeltaCondition!.Value);
                StatusCode accepted = await m_provider.ConfigureAsync(Name, trigger, Delta, token)
                    .ConfigureAwait(false);
                WmtpPeripheral.CheckAcceptance(accepted);
                State.TriggerSettings!.Value = trigger;
                await State.TriggerSettings.ClearChangeMasksAsync(m_peripheral.Context, false, CancellationToken.None)
                    .ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Changes the delta only after exact numeric validation and authorized provider acceptance.
        /// Once accepted, the setting commits even if the caller cancels.
        /// </summary>
        public async ValueTask SetDeltaAsync(
            Variant delta, WmtpRequest request, CancellationToken cancellationToken = default)
        {
            _ = await m_peripheral.RunAsync(request, WmtpOperation.ConfigureMeasurement, async token =>
            {
                double published = WmtpValidation.Delta(delta, m_definition.DataType, TriggerMilliseconds);
                StatusCode accepted = await m_provider.ConfigureAsync(Name, TriggerMilliseconds, delta, token)
                    .ConfigureAwait(false);
                WmtpPeripheral.CheckAcceptance(accepted);
                Delta = delta;
                State.DeltaCondition!.Value = published;
                await State.DeltaCondition.ClearChangeMasksAsync(m_peripheral.Context, false, CancellationToken.None)
                    .ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Acquires and validates before any peripheral reading is committed.
        /// </summary>
        internal async ValueTask<WmtpMeasurementSample> AcquireAsync(CancellationToken cancellationToken)
        {
            WmtpMeasurementSample sample = await m_provider.ReadAsync(Name, cancellationToken).ConfigureAwait(false);
            WmtpValidation.Sample(sample, m_definition.DataType);
            return sample;
        }

        /// <summary>
        /// Publishes a prevalidated acquisition and maintains duration counters without inventing samples.
        /// </summary>
        internal async ValueTask ApplyAsync(WmtpMeasurementSample sample, bool received)
        {
            TimeProvider clock = m_peripheral.TimeProvider;
            DateTimeUtc now = clock.GetUtcNow();
            long timestamp = clock.GetTimestamp();
            if (Acquisition != null && !m_counters.IsEmpty)
            {
                double elapsed = clock.GetElapsedTime(m_counterTimestamp, timestamp).TotalMilliseconds;
                double freshRemaining = Math.Max(0,
                    (Acquisition.Value.SourceTimestamp + m_definition.StaleAfter - m_counterTime).TotalMilliseconds);
                elapsed = Math.Min(elapsed, freshRemaining);
                if (m_previousStatus is 9 or 10)
                {
                    m_counters[0].Value += elapsed;
                }
                if (m_previousStatus == 10)
                {
                    m_counters[1].Value += elapsed;
                }
            }
            DataValue value = sample.Value;
            bool stale = !value.SourceTimestamp.IsNull && now - value.SourceTimestamp > m_definition.StaleAfter;
            if (stale && StatusCode.IsGood(value.StatusCode))
            {
                value = new DataValue(value.WrappedValue, StatusCodes.UncertainLastUsableValue,
                    value.SourceTimestamp, now);
            }
            Volatile.Write(ref m_published, new PublishedReading(value, received ? now : ReceivedAt));
            await m_processValue.SetValueAsync(in value, CancellationToken.None).ConfigureAwait(false);
            if (!m_counters.IsEmpty)
            {
                ushort status = 1;
                if (StatusCode.IsGood(value.StatusCode) && !value.WrappedValue.IsNull)
                {
                    double numeric = WmtpValidation.Numeric(value.WrappedValue, m_definition.DataType);
                    status = m_definition.HighHigh.HasValue && numeric > m_definition.HighHigh.Value ? (ushort)10 :
                        m_definition.High.HasValue && numeric > m_definition.High.Value ? (ushort)9 : (ushort)6;
                }
                State.Status!.Value = status;
                State.Status.StatusCode = value.StatusCode;
                State.Status.Timestamp = value.SourceTimestamp;
                m_previousStatus = status;
                foreach (PropertyState<double> counter in m_counters)
                {
                    counter.Timestamp = now;
                    counter.StatusCode = StatusCode.IsGood(value.StatusCode) ?
                        StatusCodes.Good : StatusCodes.UncertainLastUsableValue;
                }
            }
            if (State.Timestamp != null)
            {
                State.Timestamp.Value = value.SourceTimestamp;
                State.Timestamp.StatusCode = value.StatusCode;
                State.Index!.Value = sample.Index;
                State.Index.StatusCode = value.StatusCode;
                State.MeasurementPeriod!.Value = sample.MeasurementPeriod;
                State.MeasurementPeriod.StatusCode = value.StatusCode;
            }
            Acquisition = sample;
            m_counterTimestamp = timestamp;
            m_counterTime = now;
        }

        /// <summary>
        /// Applies freshness even during a long record transfer, reusing the stack's read-range/encoding validation.
        /// </summary>
        private ValueTask<AttributeReadResult> ReadSignalAsync(
            ISystemContext context, NodeState node, NumericRange range, QualifiedName encoding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DataValue sample = Sample;
            Variant value = sample.WrappedValue;
            ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                context, range, encoding, ref value);
            return new ValueTask<AttributeReadResult>(
                new AttributeReadResult(result, value, sample.StatusCode, sample.SourceTimestamp));
        }

        /// <summary>
        /// Keeps the standard discrete text aligned with its released numeric enumeration.
        /// </summary>
        private static void SetDiscrete(MultiStateValueDiscreteState<uint> node, uint value)
        {
            node.Value = value;
            foreach (EnumValueType entry in node.EnumValues!.Value)
            {
                if (entry.Value == value)
                {
                    node.ValueAsText!.Value = entry.DisplayName;
                    return;
                }
            }
            throw new ServiceResultException(StatusCodes.BadConfigurationError, "The released enumeration is missing.");
        }

        /// <summary>
        /// Atomically replaces the public reading without exposing a synchronization primitive.
        /// </summary>
        private sealed record PublishedReading(DataValue Value, DateTimeUtc ReceivedAt);

        /// <summary>The containing peripheral lifetime.</summary>
        private readonly WmtpPeripheral m_peripheral;

        /// <summary>The immutable validated sensor definition.</summary>
        private readonly WmtpMeasurementDefinition m_definition;

        /// <summary>The explicitly supplied sensor equipment provider.</summary>
        private readonly IWmtpMeasurementProvider m_provider;

        /// <summary>The shared ProcessValues runtime handle.</summary>
        private IProcessValueHandle m_processValue = null!;

        /// <summary>The two optional generated duration counters.</summary>
        private readonly ArrayOf<PropertyState<double>> m_counters;

        /// <summary>The previous monotonic counter observation.</summary>
        private long m_counterTimestamp;

        /// <summary>The previous wall-clock observation used only to cap stale intervals.</summary>
        private DateTimeUtc m_counterTime;

        /// <summary>The last known status; unknown data never accrues limit durations.</summary>
        private ushort m_previousStatus;

        /// <summary>The atomic immutable reading snapshot shared with OPC UA read callbacks.</summary>
        private PublishedReading? m_published;
    }
}
