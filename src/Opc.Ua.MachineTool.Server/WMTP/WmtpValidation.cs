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
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Validates all values before provider calls or observable mutation.
    /// </summary>
    internal static class WmtpValidation
    {
        /// <summary>
        /// Validates and snapshots mutable OPC UA metadata before staging a sensor.
        /// </summary>
        internal static WmtpMeasurementDefinition Definition(WmtpMeasurementDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            Require(!string.IsNullOrWhiteSpace(definition.Name) && definition.Name.Length <= 256,
                "A bounded, non-empty sensor name is required.");
            Require(definition.Definition != null && definition.Definition.Length <= 4096,
                "The sensor definition exceeds its bounded length.");
            Require((uint)definition.Kind <= 7 && (uint)definition.SampleKind <= 6,
                "The measurement and sample codes must be released OPC 40505 values.");
            Require(IsNumeric(definition.DataType), "A scalar numeric sensor type is required.");
            Require((definition.Features & ~WmtpMeasurementFeatures.All) == 0, "Unknown measurement features.");
            Units(definition.EngineeringUnits);
            Range(definition.EURange);
            Range(definition.InstrumentRange);
            Require(definition.InstrumentRange.Low <= definition.EURange.Low &&
                definition.InstrumentRange.High >= definition.EURange.High,
                "The nominal range exceeds the instrument.");
            Nonnegative(definition.ValuePrecision);
            Nonnegative(definition.RelativeUncertainty);
            Nonnegative(definition.AbsoluteUncertainty);
            Nonnegative(definition.InternalUpdateInterval);
            Require(definition.StaleAfter > TimeSpan.Zero, "A positive maximum acquisition age is required.");
            if (definition.High.HasValue)
            {
                Require(IsFinite(definition.High.Value), "The high threshold must be finite.");
            }
            if (definition.HighHigh.HasValue)
            {
                Require(IsFinite(definition.HighHigh.Value), "The high-high threshold must be finite.");
            }
            Require(!definition.High.HasValue || !definition.HighHigh.HasValue ||
                definition.High.Value <= definition.HighHigh.Value, "High must not exceed high-high.");
            _ = Delta(definition.Delta, definition.DataType, definition.TriggerMilliseconds);
            return definition with
            {
                EngineeringUnits = (EUInformation)definition.EngineeringUnits.Clone(),
                EURange = (Range)definition.EURange.Clone(),
                InstrumentRange = (Range)definition.InstrumentRange.Clone()
            };
        }

        /// <summary>
        /// Enforces representability in the UInt32 TriggerSettings variable, without rounding Duration inputs.
        /// </summary>
        internal static uint Trigger(double duration, double delta)
        {
            Require(IsFinite(duration) && duration >= 0 && duration <= uint.MaxValue &&
                Math.Truncate(duration) == duration, "TriggerDuration must be whole UInt32 milliseconds.");
            Require(duration != 0 || delta != 0, "TriggerSettings and DeltaCondition must not both be zero.");
            return checked((uint)duration);
        }

        /// <summary>
        /// Preserves the Number input type and rejects lossy conversion to the declared Double DeltaCondition.
        /// </summary>
        internal static double Delta(Variant delta, BuiltInType type, uint trigger)
        {
            double number = Numeric(delta, type);
            Nonnegative(number);
            if (delta.TryGetValue(out ulong unsigned))
            {
                Require(number < 18446744073709551616d && (ulong)number == unsigned,
                    "The UInt64 delta cannot be represented exactly by DeltaCondition.");
            }
            if (delta.TryGetValue(out long signed))
            {
                Require(number < 9223372036854775808d && (long)number == signed,
                    "The Int64 delta cannot be represented exactly by DeltaCondition.");
            }
            Require(trigger != 0 || number != 0, "TriggerSettings and DeltaCondition must not both be zero.");
            return number;
        }

        /// <summary>
        /// Validates a measurement without changing its raw representation or quality.
        /// </summary>
        internal static void Sample(WmtpMeasurementSample sample, BuiltInType type)
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }
            Require(!sample.Value.IsNull, "An initialized acquisition is required.");
            if (!sample.Value.WrappedValue.IsNull)
            {
                _ = Numeric(sample.Value.WrappedValue, type);
                Require(!sample.Value.SourceTimestamp.IsNull, "A measurement requires its acquisition time.");
            }
            else
            {
                Require(StatusCode.IsBad(sample.Value.StatusCode), "A missing acquisition must have bad quality.");
            }
            Nonnegative(sample.MeasurementPeriod);
        }

        /// <summary>
        /// Validates the selected device observations, without fabricating optional good-zero values.
        /// </summary>
        internal static void Device(WmtpDeviceSnapshot snapshot, WmtpFeatures features)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            Require(!snapshot.Observation.IsNull, "Device metadata requires explicit quality.");
            if ((features & WmtpFeatures.Calibration) != 0)
            {
                Require(snapshot.CalibrationMode.HasValue, "Calibration state is required.");
            }
            if ((features & WmtpFeatures.Battery) != 0)
            {
                BuiltInType batteryType = snapshot.Battery.WrappedValue.IsNull ?
                    BuiltInType.Double : snapshot.Battery.WrappedValue.TypeInfo.BuiltInType;
                Sample(new WmtpMeasurementSample { Value = snapshot.Battery }, batteryType);
                if (!snapshot.Battery.WrappedValue.IsNull)
                {
                    double battery = Numeric(snapshot.Battery.WrappedValue, batteryType);
                    Require(battery is >= 0 and <= 100, "Battery percent is outside its range.");
                }
            }
            if ((features & WmtpFeatures.License) != 0)
            {
                Require(snapshot.LicenseInformation != null, "License information is required.");
                Lifetime(snapshot.LicenseValidity);
            }
            if ((features & (WmtpFeatures.ServiceInformation | WmtpFeatures.AdvancedServiceInformation)) != 0)
            {
                Require(snapshot.LifeCycleStatus is <= 2, "Lifecycle requires the released green/yellow/red code.");
                Require(!snapshot.NextServiceDate.IsNull && !snapshot.LastService.IsNull &&
                    snapshot.NextServiceDate >= snapshot.LastService, "Service dates are missing or reversed.");
            }
            if ((features & WmtpFeatures.AdvancedServiceInformation) != 0)
            {
                Lifetime(snapshot.RemainingLifetime);
                Lifetime(snapshot.RemainingCycles);
                Lifetime(snapshot.NextService);
                Require(snapshot.OperationHours.HasValue && snapshot.OperationSinceService.HasValue,
                    "Advanced service information requires operation counters.");
                Nonnegative(snapshot.OperationHours!.Value);
                Nonnegative(snapshot.OperationSinceService!.Value);
            }
        }

        /// <summary>
        /// Validates required record fields and permits the specified ancillary defaults.
        /// </summary>
        internal static void Record(WMTPOutputDataType record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            Units(record.EngineeringUnits);
            Require(IsFinite(record.ActualValue), "A record must contain an actual finite measurement.");
            Require(record.TypeOfMeasurement <= 7 && record.TypeOfSample <= 6, "Invalid record identity codes.");
            Range(record.EURange);
            Range(record.InstrumentRange);
            Nonnegative(record.ValuePrecision);
            Nonnegative(record.RelativeUncertainty);
            Nonnegative(record.AbsoluteUncertainty);
            Nonnegative(record.MeasurementPeriod);
            Nonnegative(record.InternalUpdateInterval);
            Require((record.Definition?.Length ?? 0) <= 4096 && (record.SignalTag?.Length ?? 0) <= 256,
                "Record strings exceed their bounded sizes.");
        }

        /// <summary>
        /// Validates a complete query before count, copy or deletion.
        /// </summary>
        internal static void Query(WmtpRecordQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }
            Require(query.Selection is >= WmtpRecordSelection.All and <= WmtpRecordSelection.Last,
                "Unknown record selection.");
            Require(query.Selection != WmtpRecordSelection.Index || query.FromIndex <= query.ToIndex,
                "The index range is reversed.");
            Require(query.Selection != WmtpRecordSelection.Time ||
                (!query.FromTime.IsNull && !query.ToTime.IsNull && query.FromTime <= query.ToTime),
                "The acquisition-time range is missing or reversed.");
        }

        /// <summary>
        /// Checks a scalar's exact wire type before making a calculation-only numeric projection.
        /// </summary>
        internal static double Numeric(Variant value, BuiltInType type)
        {
            if (value.IsNull || value.TypeInfo.ValueRank != ValueRanks.Scalar ||
                value.TypeInfo.BuiltInType != type || !IsNumeric(type))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "The exact scalar numeric type is required.");
            }
            Require(value.ConvertToDouble().TryGetValue(out double number) && IsFinite(number),
                "The numeric value must be finite.");
            return number;
        }

        /// <summary>
        /// Rejects non-finite and negative durations, counts, precision and uncertainty.
        /// </summary>
        internal static void Nonnegative(double value)
        {
            Require(IsFinite(value) && value >= 0, "A finite nonnegative value is required.");
        }

        /// <summary>
        /// Turns invalid equipment or client inputs into explicit OPC UA errors.
        /// </summary>
        internal static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange, message);
            }
        }

        /// <summary>
        /// Refuses unspecified engineering-unit metadata.
        /// </summary>
        private static void Units(EUInformation units)
        {
            Require(units != null && !string.IsNullOrWhiteSpace(units.NamespaceUri) &&
                units.NamespaceUri.Length <= 2048 && !units.DisplayName.IsNull && units.UnitId >= 0 &&
                (units.DisplayName.Text?.Length ?? 0) <= 256 && (units.Description.Text?.Length ?? 0) <= 4096,
                "An explicit bounded engineering unit is required.");
        }

        /// <summary>
        /// Checks a standard range, including the allowed zero/zero ancillary default.
        /// </summary>
        private static void Range(Range range)
        {
            Require(range != null && IsFinite(range.Low) && IsFinite(range.High) && range.Low <= range.High,
                "A finite ordered range is required.");
        }

        /// <summary>
        /// Validates a device-reported countdown without coercing its wire type.
        /// </summary>
        private static void Lifetime(WmtpLifetime? lifetime)
        {
            if (lifetime == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "A selected lifetime is missing.");
            }
            BuiltInType type = lifetime.Remaining.TypeInfo.BuiltInType;
            Nonnegative(Numeric(lifetime.Remaining, type));
            Nonnegative(Numeric(lifetime.Start, type));
            Nonnegative(Numeric(lifetime.Limit, type));
            Units(lifetime.EngineeringUnits);
        }

        /// <summary>
        /// Restricts Number to the ten standard built-in numeric primitives.
        /// </summary>
        private static bool IsNumeric(BuiltInType type)
        {
            return type is >= BuiltInType.SByte and <= BuiltInType.Double;
        }

        /// <summary>
        /// Provides a portable finite check on every supported target.
        /// </summary>
        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
