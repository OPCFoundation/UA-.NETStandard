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
using System.Collections.Generic;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Server
{
    internal sealed partial class GmsSystemBinding
    {
        /// <summary>
        /// Rejects missing enabled providers and observations for unselected optional capabilities.
        /// </summary>
        private void ValidateInput(GmsSystemSnapshot snapshot)
        {
            if (snapshot.MachineTool == null || snapshot.MachineTool.Equipment != null)
            {
                throw Invalid("GMS requires inherited observations and its own explicitly typed sensor inventory.");
            }
            RequireSelection(GmsFeatures.Sensors, !snapshot.Sensors.IsNull);
            RequireSelection(GmsFeatures.Identification, snapshot.Identification != null);
            RequireSelection(GmsFeatures.Loading, snapshot.Loading != null);
            RequireSelection(GmsFeatures.ToolMonitoring, snapshot.ToolMonitoring != null);
            RequireSelection(GmsFeatures.Accessories, !snapshot.Accessories.IsNull);
            RequireSelection(GmsFeatures.AdditionalSensors, !snapshot.AdditionalSensors.IsNull);
            RequireSelection(GmsFeatures.Calibration, snapshot.Calibration != null);
            ValidateSystems(snapshot);
            ValidateEquipment(snapshot);
            ValidateProduction(snapshot);
            ValidateWarnings(snapshot);
        }

        /// <summary>
        /// Distinguishes an explicitly empty provider surface from a missing provider surface.
        /// </summary>
        private void RequireSelection(GmsFeatures feature, bool supplied)
        {
            if (Has(feature) != supplied)
            {
                throw Invalid($"The {feature} observation must be supplied exactly when its capability is selected.");
            }
        }

        /// <summary>
        /// Projects native sensor descriptions into the existing shared inventory contract.
        /// </summary>
        private ArrayOf<MachineToolTool> ConvertSensors(
            ArrayOf<GmsSensor> sensors, Dictionary<string, GmsSensor> index, bool constituent)
        {
            var result = new MachineToolTool[sensors.Count];
            for (int i = 0; i < sensors.Count; i++)
            {
                GmsSensor sensor = sensors[i] ?? throw Invalid("A sensor observation cannot be null.");
                MachineToolTool tool = sensor.Tool ?? throw Invalid("A sensor needs its inherited tool observation.");
                Text(tool.Key, true);
                if (index.Count >= m_options.MaximumSensors || !index.TryAdd(tool.Key, sensor) ||
                    !tool.Tools.IsEmpty || !tool.CompanionTypeId.IsNull ||
                    tool.Kind is not (MachineToolToolKind.Tool or MachineToolToolKind.MultiTool) ||
                    (constituent && tool.Kind != MachineToolToolKind.Tool))
                {
                    throw Invalid(
                        "Sensor keys must be unique and bounded, with at most one MultiSensor nesting level.");
                }
                ValidateSensor(sensor);
                if (tool.Kind == MachineToolToolKind.MultiTool)
                {
                    result[i] = tool with { Tools = ConvertSensors(sensor.Sensors, index, true) };
                    continue;
                }
                if (!sensor.Sensors.IsEmpty || sensor.Qualified == null)
                {
                    throw Invalid("A single sensor must have its qualification countdown and cannot contain tools.");
                }
                var life = new List<MachineToolToolLife>(tool.Life.Count + 1);
                foreach (MachineToolToolLife value in tool.Life)
                {
                    if (value == null ||
                        (value.Name == BrowseNames.Qualified && value.NamespaceUri == GmsModel.NamespaceUri))
                    {
                        throw Invalid("Qualified is supplied by the native qualification observation exactly once.");
                    }
                    life.Add(value);
                }
                GmsQualifiedLife qualified = sensor.Qualified;
                Raw(qualified.RemainingHours, BuiltInType.Double, required: true);
                Raw(qualified.StartHours, BuiltInType.Double);
                Raw(qualified.WarningHours, BuiltInType.Double);
                Raw(qualified.LimitHours, BuiltInType.Double);
                if (sensor.IsQualifiedStatus.WrappedValue.TryGetValue(out ToolIsQualifiedStatus status) &&
                    status == ToolIsQualifiedStatus.Qualified &&
                    !StatusCode.IsBad(sensor.IsQualifiedStatus.StatusCode) &&
                    qualified.RemainingHours.WrappedValue.TryGetValue(out double hours) &&
                    !StatusCode.IsBad(qualified.RemainingHours.StatusCode) && hours <= 0)
                {
                    throw Invalid(
                        "A usable Qualified observation cannot report an already expired qualification time.");
                }
                life.Add(new MachineToolToolLife
                {
                    Name = BrowseNames.Qualified,
                    NamespaceUri = GmsModel.NamespaceUri,
                    Value = qualified.RemainingHours,
                    Indication = ToolLifeIndication.Time,
                    IsCountingUp = false,
                    EngineeringUnits = new EUInformation
                    {
                        NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                        UnitId = 0x485552,
                        DisplayName = new LocalizedText("h"),
                        Description = new LocalizedText("hour")
                    },
                    StartValue = qualified.StartHours,
                    WarningValue = qualified.WarningHours,
                    LimitValue = qualified.LimitHours,
                    Sample = new MachineToolSampleInfo(
                        qualified.RemainingHours.SourceTimestamp, qualified.RemainingHours.StatusCode)
                });
                result[i] = tool with { Life = life.ToArrayOf() };
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Checks native sensor data types and ranges before factory or existing-node mutation.
        /// </summary>
        private void ValidateSensor(GmsSensor sensor)
        {
            Enumeration(sensor.Alignment, 2);
            Strings(sensor.Axes);
            if (sensor.Tool.Kind == MachineToolToolKind.MultiTool)
            {
                if (sensor.Qualified != null || !sensor.Capabilities.IsNull || !sensor.IsQualifiedStatus.IsNull ||
                    !sensor.AbsoluteProbe.IsNull || !sensor.EngineeringUnit.IsNull ||
                    !sensor.MeasuringRange.IsNull || !sensor.WorkingRange.IsNull || !sensor.Resolution.IsNull ||
                    !sensor.Class.WrappedValue.IsNull)
                {
                    throw Invalid("A multisensor exposes only its own Alignment and Axes, not single-sensor values.");
                }
                return;
            }
            Unsigned(sensor.Class, 11, true);
            Unsigned(sensor.Capabilities, 5, array: true);
            Enumeration(sensor.IsQualifiedStatus, 2);
            Raw(sensor.AbsoluteProbe, BuiltInType.Boolean);
            Raw(sensor.EngineeringUnit, BuiltInType.ExtensionObject);
            if (!sensor.EngineeringUnit.WrappedValue.IsNull &&
                !sensor.EngineeringUnit.WrappedValue.TryGetStructure<EUInformation>(out EUInformation? _))
            {
                throw Invalid("EngineeringUnit must be an actual EUInformation structure.");
            }
            Nonnegative(sensor.MeasuringRange);
            Nonnegative(sensor.WorkingRange);
            Nonnegative(sensor.Resolution, positive: true);
            if (sensor.MeasuringRange.WrappedValue.TryGetValue(out double measuring) &&
                sensor.WorkingRange.WrappedValue.TryGetValue(out double working) && measuring > working)
            {
                throw Invalid("The measuring range cannot exceed the reported working range.");
            }
        }

        /// <summary>
        /// Validates optional or mandatory raw scalar values without coercing their built-in types.
        /// </summary>
        private static void Raw(
            in DataValue sample, BuiltInType type, bool required = false, int rank = ValueRanks.Scalar)
        {
            if (sample.IsNull)
            {
                if (required)
                {
                    throw Invalid(
                        "A mandatory observation cannot be absent; use an explicit bad status when unavailable.");
                }
                return;
            }
            if (!StatusCode.IsBad(sample.StatusCode) &&
                (sample.SourceTimestamp == default || sample.WrappedValue.IsNull))
            {
                throw Invalid("Usable observations require an actual value and acquisition timestamp.");
            }
            if (sample.WrappedValue.IsNull)
            {
                return;
            }
            TypeInfo info = sample.WrappedValue.TypeInfo;
            if (info.BuiltInType != type || info.ValueRank != rank ||
                (sample.WrappedValue.TryGetValue(out double number) &&
                    (double.IsNaN(number) || double.IsInfinity(number))) ||
                (sample.WrappedValue.TryGetValue(out float real) && (float.IsNaN(real) || float.IsInfinity(real))))
            {
                throw Invalid("The observation has an incorrect scalar/array type or a nonfinite numeric value.");
            }
        }

        /// <summary>
        /// Validates an enumeration's exact Int32 wire representation and released range.
        /// </summary>
        private static void Enumeration(in DataValue sample, int maximum)
        {
            Raw(sample, sample.WrappedValue.TypeInfo.BuiltInType == BuiltInType.Enumeration
                ? BuiltInType.Enumeration : BuiltInType.Int32);
            if (sample.WrappedValue.TryGetValue(out int value) && (value < 0 || value > maximum))
            {
                throw Invalid("The enumeration value is outside its released range.");
            }
        }

        /// <summary>
        /// Accepts all and only scalar or homogeneous array UInteger built-in representations.
        /// </summary>
        private void Unsigned(in DataValue sample, ulong maximum, bool required = false, bool array = false)
        {
            BuiltInType type = sample.WrappedValue.TypeInfo.BuiltInType;
            Raw(sample, type, required, array ? ValueRanks.OneDimension : ValueRanks.Scalar);
            if (sample.WrappedValue.IsNull)
            {
                return;
            }
            if (type is not (BuiltInType.Byte or BuiltInType.UInt16 or BuiltInType.UInt32 or BuiltInType.UInt64))
            {
                throw Invalid("UInteger observations require an unsigned integral built-in representation.");
            }
            if (!array)
            {
                ulong number = sample.WrappedValue.TryGetValue(out byte b) ? b :
                    sample.WrappedValue.TryGetValue(out ushort s) ? s :
                    sample.WrappedValue.TryGetValue(out uint u) ? u :
                    sample.WrappedValue.TryGetValue(out ulong l) ? l : ulong.MaxValue;
                if (number > maximum)
                {
                    throw Invalid("The classification value exceeds the released EnumStrings range.");
                }
                return;
            }
            if (sample.WrappedValue.TryGetValue(out ArrayOf<byte> bytes))
            {
                UnsignedArray(bytes, static value => value, maximum);
            }
            else if (sample.WrappedValue.TryGetValue(out ArrayOf<ushort> shorts))
            {
                UnsignedArray(shorts, static value => value, maximum);
            }
            else if (sample.WrappedValue.TryGetValue(out ArrayOf<uint> integers))
            {
                UnsignedArray(integers, static value => value, maximum);
            }
            else if (sample.WrappedValue.TryGetValue(out ArrayOf<ulong> longs))
            {
                UnsignedArray(longs, static value => value, maximum);
            }
        }

        /// <summary>
        /// Checks native unsigned-array elements without boxing or changing their stored representation.
        /// </summary>
        /// <typeparam name="T">The actual unsigned integral built-in type.</typeparam>
        private void UnsignedArray<T>(ArrayOf<T> values, Func<T, ulong> convert, ulong maximum)
        {
            if (values.Count > m_options.MaximumArrayLength)
            {
                throw Invalid("The native array exceeds its configured bound.");
            }
            foreach (T value in values)
            {
                if (convert(value) > maximum)
                {
                    throw Invalid("A capability value exceeds the released EnumStrings range.");
                }
            }
        }

        /// <summary>
        /// Checks a Double magnitude in its native unit, without converting durations or dimensions.
        /// </summary>
        private static void Nonnegative(in DataValue sample, bool positive = false, bool required = false)
        {
            Raw(sample, BuiltInType.Double, required);
            if (sample.WrappedValue.TryGetValue(out double value) && (positive ? value <= 0 : value < 0))
            {
                throw Invalid("The observation requires a finite nonnegative magnitude or positive interval.");
            }
        }

        /// <summary>
        /// Checks a bounded String-array field, preserving absent and explicitly empty arrays.
        /// </summary>
        private void Strings(in DataValue sample)
        {
            Raw(sample, BuiltInType.String, rank: ValueRanks.OneDimension);
            if (sample.WrappedValue.TryGetValue(out ArrayOf<string> values))
            {
                if (values.Count > m_options.MaximumArrayLength)
                {
                    throw Invalid("The native String array exceeds its configured bound.");
                }
                foreach (string text in values)
                {
                    Text(text, true);
                }
            }
        }

        /// <summary>
        /// Rejects empty required names and overlong provider text.
        /// </summary>
        private void Text(string? text, bool required = false)
        {
            if ((required && string.IsNullOrWhiteSpace(text)) || text?.Length > m_options.MaximumTextLength)
            {
                throw Invalid("Required text must be nonempty and within its configured bound.");
            }
        }
    }
}
