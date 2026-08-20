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

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// The clause 6.3.1 assignment of each of the thirty
    /// <see cref="AASDataTypeDefXsdDataType"/> values to exactly one OPC UA
    /// DataType, and back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The assignment is a bijection on purpose. A value materializes as one
    /// <c>Value</c> Variable whose DataType is the one assigned to its declared
    /// xsd type, and a serializer reads the declared type back off that
    /// DataType — which only works when no DataType is assigned to two xsd
    /// types. Where a built-in denotes the xsd type on its own it is used;
    /// where two xsd types would otherwise share one built-in, this namespace
    /// defines a subtype for one of them, as OPC UA does in deriving
    /// <c>DecimalString</c>, <c>DurationString</c>, <c>DateString</c> and
    /// <c>TimeString</c> from <c>String</c>.
    /// </para>
    /// <para>
    /// Three assignments are deliberate departures from the obvious choice.
    /// <c>xs:duration</c> takes <c>DurationString</c> rather than
    /// <c>Duration</c>, because <c>Duration</c> is a <c>Double</c> count of
    /// milliseconds while <c>xs:duration</c> has year and month components that
    /// are not a fixed number of them — <c>P1M</c> is not thirty days.
    /// <c>xs:date</c> and <c>xs:time</c> take <c>DateString</c> and
    /// <c>TimeString</c> rather than <c>DateTime</c>, because a
    /// <c>DateTime</c> is an instant while a date is a day and a time is a
    /// time-of-day, and assigning either <c>DateTime</c> would require
    /// inventing the missing component.
    /// </para>
    /// <para>
    /// The DataTypes are expressed as <see cref="ExpandedNodeId"/> because ten
    /// of them live in this companion namespace, whose index is the Server's to
    /// choose. Use the <see cref="NamespaceTable"/> overload of
    /// <c>TryGetValueType</c> to read a declared type back off a Session-local
    /// NodeId.
    /// </para>
    /// </remarks>
    public static class AasXsdTypeMap
    {
        /// <summary>
        /// Returns the OPC UA DataType assigned to one xsd type.
        /// </summary>
        /// <param name="valueType">The declared xsd type.</param>
        /// <returns>The namespace-qualified DataType identifier.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="valueType"/> is not one of the thirty defined values.
        /// The enumerations are closed: a value outside them cannot round-trip,
        /// so it is rejected rather than dropped silently (clause 6.3.3).
        /// </exception>
        public static ExpandedNodeId ToDataTypeId(AASDataTypeDefXsdDataType valueType)
        {
            if (!s_toDataType.TryGetValue(valueType, out ExpandedNodeId dataTypeId))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(valueType),
                    valueType,
                    "The value is not one of the thirty DataTypeDefXsd values.");
            }

            return dataTypeId;
        }

        /// <summary>
        /// Returns the OPC UA DataType assigned to one xsd type, without
        /// throwing.
        /// </summary>
        /// <param name="valueType">The declared xsd type.</param>
        /// <param name="dataTypeId">The assigned DataType when the return value is <c>true</c>.</param>
        /// <returns><c>true</c> when the xsd type is one of the thirty defined values.</returns>
        public static bool TryGetDataTypeId(
            AASDataTypeDefXsdDataType valueType,
            out ExpandedNodeId dataTypeId)
        {
            if (s_toDataType.TryGetValue(valueType, out ExpandedNodeId found))
            {
                dataTypeId = found;
                return true;
            }

            dataTypeId = ExpandedNodeId.Null;
            return false;
        }

        /// <summary>
        /// Reads the declared xsd type back off a value node's DataType.
        /// </summary>
        /// <remarks>
        /// This is the direction a serializer runs in, and the reason the
        /// assignment must be injective.
        /// </remarks>
        /// <param name="dataTypeId">The value node's DataType.</param>
        /// <param name="valueType">The declared xsd type when the return value is <c>true</c>.</param>
        /// <returns><c>true</c> when the DataType is one this clause assigns.</returns>
        public static bool TryGetValueType(
            ExpandedNodeId dataTypeId,
            out AASDataTypeDefXsdDataType valueType)
        {
            if (!dataTypeId.IsNull &&
                s_fromDataType.TryGetValue(dataTypeId, out AASDataTypeDefXsdDataType found))
            {
                valueType = found;
                return true;
            }

            valueType = default;
            return false;
        }

        /// <summary>
        /// Reads the declared xsd type back off a Session-local value node
        /// DataType, resolving its namespace through the Server's table.
        /// </summary>
        /// <param name="dataTypeId">The value node's DataType, carrying a namespace index.</param>
        /// <param name="namespaceUris">The Server's namespace table.</param>
        /// <param name="valueType">The declared xsd type when the return value is <c>true</c>.</param>
        /// <returns><c>true</c> when the DataType is one this clause assigns.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="namespaceUris"/> is <c>null</c>.</exception>
        public static bool TryGetValueType(
            NodeId dataTypeId,
            NamespaceTable namespaceUris,
            out AASDataTypeDefXsdDataType valueType)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            valueType = default;

            if (dataTypeId.IsNull)
            {
                return false;
            }

            string? namespaceUri = namespaceUris.GetString(dataTypeId.NamespaceIndex);
            return namespaceUri is not null &&
                TryGetValueType(
                    new ExpandedNodeId(dataTypeId, namespaceUri),
                    out valueType);
        }

        /// <summary>
        /// Gets every xsd type this clause assigns a DataType to, in
        /// enumeration order.
        /// </summary>
        public static IReadOnlyList<AASDataTypeDefXsdDataType> ValueTypes => s_valueTypes;

        /// <summary>
        /// Reports whether a DataType is one of the ten subtypes this namespace
        /// defines because two xsd types would otherwise share one built-in.
        /// </summary>
        /// <param name="dataTypeId">The DataType to test.</param>
        /// <returns><c>true</c> when the DataType is defined by this companion namespace.</returns>
        public static bool IsAasDefinedSubtype(ExpandedNodeId dataTypeId)
        {
            return !dataTypeId.IsNull &&
                string.Equals(dataTypeId.NamespaceUri, Namespaces.AasV3, StringComparison.Ordinal) &&
                s_fromDataType.ContainsKey(dataTypeId);
        }

        private static ExpandedNodeId Core(uint identifier)
        {
            return new ExpandedNodeId(identifier, 0, Opc.Ua.Namespaces.OpcUa, 0);
        }

        private static readonly Dictionary<AASDataTypeDefXsdDataType, ExpandedNodeId> s_toDataType =
            new()
            {
                // Where a built-in DataType denotes the xsd type on its own, it
                // is used.
                [AASDataTypeDefXsdDataType.Boolean] = Core(Opc.Ua.DataTypes.Boolean),

                // xsd 'byte' is signed and xsd 'unsignedByte' is not, which is
                // the reverse of what the OPC UA names suggest.
                [AASDataTypeDefXsdDataType.Byte] = Core(Opc.Ua.DataTypes.SByte),
                [AASDataTypeDefXsdDataType.UnsignedByte] = Core(Opc.Ua.DataTypes.Byte),

                [AASDataTypeDefXsdDataType.Short] = Core(Opc.Ua.DataTypes.Int16),
                [AASDataTypeDefXsdDataType.UnsignedShort] = Core(Opc.Ua.DataTypes.UInt16),
                [AASDataTypeDefXsdDataType.Int] = Core(Opc.Ua.DataTypes.Int32),
                [AASDataTypeDefXsdDataType.UnsignedInt] = Core(Opc.Ua.DataTypes.UInt32),
                [AASDataTypeDefXsdDataType.Long] = Core(Opc.Ua.DataTypes.Int64),
                [AASDataTypeDefXsdDataType.UnsignedLong] = Core(Opc.Ua.DataTypes.UInt64),
                [AASDataTypeDefXsdDataType.Float] = Core(Opc.Ua.DataTypes.Float),
                [AASDataTypeDefXsdDataType.Double] = Core(Opc.Ua.DataTypes.Double),

                // Decimal is arbitrary precision, and its Scale preserves the
                // authored number of decimal places.
                [AASDataTypeDefXsdDataType.Decimal] = Core(Opc.Ua.DataTypes.Decimal),

                // Integer and UInteger are the abstract unions of OPC UA's
                // concrete integer types, so their range is that of Int64 and
                // UInt64 whereas xs:integer is unbounded. A value outside the
                // representable range is rejected rather than truncated.
                [AASDataTypeDefXsdDataType.Integer] = Core(Opc.Ua.DataTypes.Integer),
                [AASDataTypeDefXsdDataType.NonNegativeInteger] = Core(Opc.Ua.DataTypes.UInteger),

                // The three remaining integer restrictions each need their own
                // DataType, and the AAS subtypes mirror the xsd restriction
                // hierarchy: negativeInteger restricts nonPositiveInteger.
                [AASDataTypeDefXsdDataType.PositiveInteger] = DataTypeIds.AASPositiveInteger,
                [AASDataTypeDefXsdDataType.NonPositiveInteger] = DataTypeIds.AASNonPositiveInteger,
                [AASDataTypeDefXsdDataType.NegativeInteger] = DataTypeIds.AASNegativeInteger,

                [AASDataTypeDefXsdDataType.String] = Core(Opc.Ua.DataTypes.String),

                // anyURI would otherwise collide with string.
                [AASDataTypeDefXsdDataType.AnyUri] = DataTypeIds.AASAnyUri,

                [AASDataTypeDefXsdDataType.DateTime] = Core(Opc.Ua.DataTypes.DateTime),

                // A date is a day, not an instant; a time-of-day has no day.
                [AASDataTypeDefXsdDataType.Date] = Core(Opc.Ua.DataTypes.DateString),
                [AASDataTypeDefXsdDataType.Time] = Core(Opc.Ua.DataTypes.TimeString),

                // The ISO 8601 duration form, not Duration (i=290), which is a
                // count of milliseconds.
                [AASDataTypeDefXsdDataType.Duration] = Core(Opc.Ua.DataTypes.DurationString),

                // OPC UA has no DataType denoting a Gregorian period, so each
                // of the five partial-date types takes its own String subtype.
                [AASDataTypeDefXsdDataType.GYear] = DataTypeIds.AASGYear,
                [AASDataTypeDefXsdDataType.GYearMonth] = DataTypeIds.AASGYearMonth,
                [AASDataTypeDefXsdDataType.GMonth] = DataTypeIds.AASGMonth,
                [AASDataTypeDefXsdDataType.GMonthDay] = DataTypeIds.AASGMonthDay,
                [AASDataTypeDefXsdDataType.GDay] = DataTypeIds.AASGDay,

                [AASDataTypeDefXsdDataType.Base64Binary] = Core(Opc.Ua.DataTypes.ByteString),

                // The octets are identical to a base64Binary value's; only the
                // written form differs, so hexBinary needs its own DataType to
                // keep that difference recoverable.
                [AASDataTypeDefXsdDataType.HexBinary] = DataTypeIds.AASHexBinary
            };

        private static readonly Dictionary<ExpandedNodeId, AASDataTypeDefXsdDataType>
            s_fromDataType = BuildInverse();

        private static readonly AASDataTypeDefXsdDataType[] s_valueTypes = BuildValueTypes();

        private static Dictionary<ExpandedNodeId, AASDataTypeDefXsdDataType> BuildInverse()
        {
            var inverse = new Dictionary<ExpandedNodeId, AASDataTypeDefXsdDataType>();
            foreach (KeyValuePair<AASDataTypeDefXsdDataType, ExpandedNodeId> entry in s_toDataType)
            {
                // A duplicate here would mean a serializer could not recover
                // the declared type, which clause 6.3.1 forbids outright. It is
                // a defect in this table rather than a runtime condition, so it
                // fails loudly at type initialization.
                inverse.Add(entry.Value, entry.Key);
            }

            return inverse;
        }

        private static AASDataTypeDefXsdDataType[] BuildValueTypes()
        {
            var values = new AASDataTypeDefXsdDataType[s_toDataType.Count];
            s_toDataType.Keys.CopyTo(values, 0);
            Array.Sort(values);
            return values;
        }
    }
}
