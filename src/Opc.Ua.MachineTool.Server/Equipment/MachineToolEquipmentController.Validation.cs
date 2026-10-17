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
using System.Numerics;
using static Opc.Ua.MachineTool.Server.MachineToolInventoryPublisher;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolEquipmentController
    {
        /// <summary>
        /// Validates positive finite inventory bounds and dependent feature selections before allocation.
        /// </summary>
        private void ValidateOptions()
        {
            if ((m_options.Features & ~MachineToolFeatures.All) != 0 ||
                m_options.MaximumTools is < 1 or > 65536 ||
                m_options.MaximumLifeEntriesPerTool is < 1 or > 256 ||
                m_options.MaximumElements is < 1 or > 65536 ||
                m_options.MaximumTextLength is < 1 or > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(m_options), "Invalid equipment bounds or features.");
            }
            if ((Has(MachineToolFeatures.DynamicTools) || Has(MachineToolFeatures.ToolLife)) &&
                !Has(MachineToolFeatures.Tools))
            {
                throw Invalid("Dynamic tools and tool life require the Tools feature.");
            }
            if (Has(MachineToolFeatures.DynamicTools) && m_options.PublishFinalObservationsAsync == null)
            {
                throw Invalid("Dynamic tools require the final-observation publication barrier.");
            }
        }

        /// <summary>
        /// Validates the complete bounded observation before any factory runs.
        /// </summary>
        private void ValidateObservation(MachineToolSnapshot snapshot)
        {
            if (!Has(MachineToolFeatures.Tools))
            {
                if (snapshot.Equipment?.Tools.Count > 0)
                {
                    throw Invalid("Tool observations require the explicitly selected Tools feature.");
                }
                return;
            }
            MachineToolEquipmentObservation equipment = snapshot.Equipment ??
                throw Invalid("The enabled equipment inventory requires an explicit provider observation.");
            if (Observation != null && !Has(MachineToolFeatures.DynamicTools) &&
                !SameOrder(Observation.Equipment!.Tools, equipment.Tools))
            {
                throw Invalid("A static tool inventory cannot add, remove or reorder tool slots.");
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            var controlIds = new HashSet<(ToolManagement, uint, uint?)>();
            int elements = 0;
            foreach (MachineToolTool tool in equipment.Tools)
            {
                ValidateTool(tool, false, keys, identifiers, controlIds, ref elements);
            }
        }

        /// <summary>
        /// Checks each physical tool exactly once, including the constituent tools of a multitool.
        /// </summary>
        private void ValidateTool(
            MachineToolTool tool, bool constituent, HashSet<string> keys, HashSet<string> identifiers,
            HashSet<(ToolManagement, uint, uint?)> controlIds, ref int elements)
        {
            if (tool == null)
            {
                throw Invalid("A tool observation cannot be null.");
            }
            Text(tool.Key, true);
            Text(tool.Name, false);
            Text(tool.Identifier, false);
            if (!keys.Add(tool.Key) || keys.Count > m_options.MaximumTools ||
                ++elements > m_options.MaximumElements)
            {
                throw Invalid("The tool inventory contains duplicate keys or exceeds its configured bounds.");
            }
            if (tool.Identifier != null && !identifiers.Add(tool.Identifier))
            {
                throw Invalid("A globally identified tool cannot occur twice in one machine inventory.");
            }
            ValidateSample(tool.Sample);
            if (tool.Location != null)
            {
                Text(tool.Location.Name, true);
            }
            if ((int)tool.Kind is < 0 or > 2 || (constituent && tool.Kind != MachineToolToolKind.Tool))
            {
                throw Invalid("A multitool can only contain ToolType instances, not nested multitools.");
            }
            if (tool.Kind != MachineToolToolKind.Companion && !tool.CompanionTypeId.IsNull)
            {
                throw Invalid("Only a Companion tool supplies a custom base-tool type.");
            }
            if (tool.Kind == MachineToolToolKind.Companion)
            {
                NodeId type = ExpandedNodeId.ToNodeId(tool.CompanionTypeId, m_context.Context.NamespaceUris);
                if (m_options.CreateCompanionToolAsync == null || type.IsNull ||
                    !m_context.Context.TypeTable.IsTypeOf(type,
                        ExpandedNodeId.ToNodeId(ObjectTypeIds.BaseToolType, m_context.Context.NamespaceUris)) ||
                    m_context.Context.TypeTable.IsTypeOf(type,
                        ExpandedNodeId.ToNodeId(ObjectTypeIds.ToolType, m_context.Context.NamespaceUris)) ||
                    m_context.Context.TypeTable.IsTypeOf(type,
                        ExpandedNodeId.ToNodeId(ObjectTypeIds.MultiToolType, m_context.Context.NamespaceUris)))
                {
                    throw Invalid("A Companion entry requires an explicit direct BaseTool subtype and typed factory.");
                }
            }
            if (tool.Kind == MachineToolToolKind.Tool)
            {
                MachineToolToolControl control = tool.Control ??
                    throw Invalid("A single tool requires controller data.");
                if ((int)control.Interpretation is < 0 or > 2 || (int)control.ReasonForLocking is < 0 or > 5 ||
                    (control.Interpretation == ToolManagement.GroupBased && !control.ControlIdentifier2.HasValue) ||
                    (control.Interpretation == ToolManagement.NumberBased && control.ControlIdentifier2.HasValue))
                {
                    throw Invalid("Invalid tool management, lock reason or control identifier combination.");
                }
                if (control.Interpretation != ToolManagement.Other &&
                    !controlIds.Add((control.Interpretation, control.ControlIdentifier1, control.ControlIdentifier2)))
                {
                    throw Invalid("The controller identifiers must uniquely identify each reported tool.");
                }
                if (!control.LastUsage.IsNull)
                {
                    ValidateDataValue(control.LastUsage);
                    if (!control.LastUsage.WrappedValue.IsNull &&
                        !control.LastUsage.WrappedValue.TryGetValue(out DateTimeUtc _))
                    {
                        throw Invalid("LastUsage must be a UtcTime sample.");
                    }
                    if (control.LastUsage.WrappedValue.TryGetValue(out DateTimeUtc last) && last == default &&
                        !StatusCode.IsBad(control.LastUsage.StatusCode))
                    {
                        throw Invalid("A usable LastUsage cannot be an unspecified time.");
                    }
                }
                if (tool.Tools.Count != 0 ||
                    (Has(MachineToolFeatures.ToolLife) && tool.Life.Count == 0) ||
                    (!Has(MachineToolFeatures.ToolLife) && tool.Life.Count != 0))
                {
                    throw Invalid("Single tools require the selected life inventory and cannot contain tools.");
                }
                if (tool.Life.Count > m_options.MaximumLifeEntriesPerTool)
                {
                    throw Invalid("The tool-life inventory exceeds its per-tool limit.");
                }
                var names = new HashSet<QualifiedName>();
                foreach (MachineToolToolLife life in tool.Life)
                {
                    if (life == null || ++elements > m_options.MaximumElements)
                    {
                        throw Invalid("The combined equipment inventory exceeds its configured bound.");
                    }
                    Text(life.Name, true);
                    Text(life.NamespaceUri, false);
                    if (!names.Add(LifeName(life)))
                    {
                        throw Invalid("Tool-life browse names must be unique.");
                    }
                    ValidateLife(life);
                }
            }
            else
            {
                if (tool.Control != null || tool.Life.Count != 0 ||
                    (tool.Kind != MachineToolToolKind.MultiTool && tool.Tools.Count != 0))
                {
                    throw Invalid("Only a ToolType can carry controller and tool-life observations.");
                }
                foreach (MachineToolTool child in tool.Tools)
                {
                    ValidateTool(child, true, keys, identifiers, controlIds, ref elements);
                }
            }
        }

        /// <summary>
        /// Checks measured units, finite Number representations, integral counts and directional threshold order.
        /// </summary>
        private void ValidateLife(MachineToolToolLife life)
        {
            if ((int)life.Indication is < 0 or > 7)
            {
                throw Invalid("Unknown tool-life indication.");
            }
            EUInformation units = life.EngineeringUnits ??
                throw Invalid("Tool life requires explicit engineering units.");
            Text(units.NamespaceUri, true);
            Text(units.DisplayName.Text, false);
            Text(units.Description.Text, false);
            bool standardUnits = units.NamespaceUri == "http://www.opcfoundation.org/UA/units/un/cefact";
            bool distance = units.UnitId is 0x4D4D54 or 0x4D5452 or 0x4B4D54; // MMT, MTR, KMT.
            bool validUnit = life.Indication switch
            {
                ToolLifeIndication.Time => standardUnits && units.UnitId == 0x485552, // HUR.
                ToolLifeIndication.NumberOfParts or ToolLifeIndication.NumberOfUsages =>
                    standardUnits && units.UnitId == 0x433632, // C62, one.
                ToolLifeIndication.Feed_Distance or ToolLifeIndication.Cutting_Distance => standardUnits && distance,
                ToolLifeIndication.Length or ToolLifeIndication.Diameter =>
                    standardUnits && (distance || units.UnitId == 0x3448), // 4H, micrometre.
                _ => units.UnitId != -1 || !units.DisplayName.IsNull
            };
            if (!validUnit)
            {
                throw Invalid("The engineering unit does not match OPC 40501-1 ToolLifeIndication.");
            }
            ValidateSample(life.Sample);
            ValidateNumber(life.Value, life.Indication);
            if (!life.StartValue.IsNull)
            {
                ValidateNumber(life.StartValue, life.Indication);
            }
            if (!life.WarningValue.IsNull)
            {
                ValidateNumber(life.WarningValue, life.Indication);
            }
            if (!life.LimitValue.IsNull)
            {
                ValidateNumber(life.LimitValue, life.Indication);
            }
            Ordered(life.StartValue, life.WarningValue, life.IsCountingUp);
            Ordered(life.WarningValue, life.LimitValue, life.IsCountingUp);
            Ordered(life.StartValue, life.LimitValue, life.IsCountingUp);
        }

        /// <summary>
        /// Requires a source time for usable observations and never treats an absent DataValue as Good zero.
        /// </summary>
        internal static void ValidateSample(MachineToolSampleInfo sample)
        {
            if (!StatusCode.IsBad(sample.StatusCode) && sample.SourceTimestamp == default)
            {
                throw Invalid("A usable equipment sample requires its source timestamp.");
            }
        }

        /// <summary>
        /// Validates metadata independently of the scalar's concrete type.
        /// </summary>
        internal static void ValidateDataValue(in DataValue value)
        {
            if (value.IsNull || (value.WrappedValue.IsNull && !StatusCode.IsBad(value.StatusCode)))
            {
                throw Invalid("An available sample requires a value; absence must be explicit.");
            }
            ValidateSample(new MachineToolSampleInfo(value.SourceTimestamp, value.StatusCode));
        }

        /// <summary>
        /// Validates the numeric sample without coercing its wire representation.
        /// </summary>
        private static void ValidateNumber(in DataValue value, ToolLifeIndication indication)
        {
            ValidateDataValue(value);
            if (value.WrappedValue.IsNull)
            {
                return;
            }
            BinaryNumber number = Number(value.WrappedValue);
            if (indication is ToolLifeIndication.NumberOfParts or ToolLifeIndication.NumberOfUsages &&
                number.Exponent < 0 && number.Mantissa % (BigInteger.One << -number.Exponent) != BigInteger.Zero)
            {
                throw Invalid("A part or usage count cannot be fractional.");
            }
        }

        /// <summary>
        /// Compares thresholds exactly, including adjacent UInt64 values and mixed integral/floating types.
        /// </summary>
        private static void Ordered(in DataValue first, in DataValue second, bool countingUp)
        {
            if (first.IsNull || second.IsNull || first.WrappedValue.IsNull || second.WrappedValue.IsNull)
            {
                return;
            }
            BinaryNumber left = Number(first.WrappedValue);
            BinaryNumber right = Number(second.WrappedValue);
            int exponent = Math.Min(left.Exponent, right.Exponent);
            int order = (left.Mantissa << (left.Exponent - exponent))
                .CompareTo(right.Mantissa << (right.Exponent - exponent));
            if ((countingUp && order > 0) || (!countingUp && order < 0))
            {
                throw Invalid("Tool-life thresholds do not follow the reported counting direction.");
            }
        }

        /// <summary>
        /// Represents all built-in finite Number scalars exactly as a binary rational.
        /// </summary>
        private static BinaryNumber Number(Variant value)
        {
            if (value.TryGetValue(out sbyte int8))
            {
                return new BinaryNumber(int8, 0);
            }
            if (value.TryGetValue(out byte uint8))
            {
                return new BinaryNumber(uint8, 0);
            }
            if (value.TryGetValue(out short int16))
            {
                return new BinaryNumber(int16, 0);
            }
            if (value.TryGetValue(out ushort uint16))
            {
                return new BinaryNumber(uint16, 0);
            }
            if (value.TryGetValue(out int int32))
            {
                return new BinaryNumber(int32, 0);
            }
            if (value.TryGetValue(out uint uint32))
            {
                return new BinaryNumber(uint32, 0);
            }
            if (value.TryGetValue(out long int64))
            {
                return new BinaryNumber(int64, 0);
            }
            if (value.TryGetValue(out ulong uint64))
            {
                return new BinaryNumber(uint64, 0);
            }
            double floating;
            if (value.TryGetValue(out float single))
            {
                floating = single;
            }
            else if (!value.TryGetValue(out floating))
            {
                throw Invalid("Tool life and its thresholds must be scalar built-in Number values.");
            }
            if (double.IsInfinity(floating) || double.IsNaN(floating))
            {
                throw Invalid("Tool-life values must be finite.");
            }
            long bits = BitConverter.DoubleToInt64Bits(floating);
            int exponent = (int)((bits >> 52) & 0x7FF);
            long significand = bits & 0xFFFFFFFFFFFFF;
            if (exponent != 0)
            {
                significand |= 1L << 52;
            }
            return new BinaryNumber(bits < 0 ? -significand : significand,
                exponent == 0 ? -1074 : exponent - 1075);
        }

        /// <summary>
        /// Keeps an existing tool's selected variable schema stable while allowing its observations to change.
        /// </summary>
        private static void ValidateShape(MachineToolTool previous, MachineToolTool next)
        {
            if (previous.Kind != next.Kind || previous.CompanionTypeId != next.CompanionTypeId ||
                (previous.Identifier == null) != (next.Identifier == null) ||
                (previous.Name == null) != (next.Name == null) ||
                (previous.Location == null) != (next.Location == null) ||
                previous.Life.Count != next.Life.Count)
            {
                throw Invalid("An existing tool slot cannot change its selected schema.");
            }
            if (previous.Control != null && next.Control != null &&
                (previous.Control.ControlIdentifier2.HasValue != next.Control.ControlIdentifier2.HasValue ||
                    previous.Control.PlannedForOperating.HasValue != next.Control.PlannedForOperating.HasValue ||
                    previous.Control.LastUsage.IsNull != next.Control.LastUsage.IsNull))
            {
                throw Invalid("An existing tool slot cannot change its optional controller variables.");
            }
            for (int i = 0; i < next.Life.Count; i++)
            {
                MachineToolToolLife before = previous.Life[i];
                MachineToolToolLife after = next.Life[i];
                if (before.Name != after.Name || before.NamespaceUri != after.NamespaceUri ||
                    before.StartValue.IsNull != after.StartValue.IsNull ||
                    before.WarningValue.IsNull != after.WarningValue.IsNull ||
                    before.LimitValue.IsNull != after.LimitValue.IsNull)
                {
                    throw Invalid("An existing tool-life entry cannot change its selected variable schema.");
                }
            }
        }

        /// <summary>
        /// Compares the bounded physical order, including the members of multitools.
        /// </summary>
        private static bool SameOrder(ArrayOf<MachineToolTool> first, ArrayOf<MachineToolTool> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }
            for (int i = 0; i < first.Count; i++)
            {
                if (first[i] == null || second[i] == null || first[i].Key != second[i].Key ||
                    !SameOrder(first[i].Tools, second[i].Tools))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks provider text without deriving missing names or global identifiers.
        /// </summary>
        private void Text(string? value, bool required)
        {
            if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > m_options.MaximumTextLength)
            {
                throw Invalid("Equipment text is missing or exceeds the configured bound.");
            }
        }

        /// <summary>
        /// Exact, bounded binary representation used only to validate numerical ordering.
        /// </summary>
        private readonly record struct BinaryNumber(BigInteger Mantissa, int Exponent);
    }
}
