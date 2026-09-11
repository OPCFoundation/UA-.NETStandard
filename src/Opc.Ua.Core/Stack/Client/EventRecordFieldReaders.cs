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

namespace Opc.Ua
{
    /// <summary>
    /// Positional read helpers shared by source-generated event-record
    /// decoders. Mirrors the inlined helpers that lived on
    /// <c>AlarmEventDecoder</c> before decoder generation landed —
    /// the conversion semantics (especially <c>DateTimeUtc</c> →
    /// <c>DateTime</c>) match the historic hand-rolled behavior.
    /// </summary>
    /// <remarks>
    /// Generated decoders read fields by absolute position into their
    /// own <c>StandardFields</c> layout. Every helper returns a
    /// safe default when the position is out-of-range or the variant
    /// holds a type that cannot be projected to the requested type —
    /// the same contract the legacy decoder offered.
    /// </remarks>
    public static class EventRecordFieldReaders
    {
        /// <summary>
        /// Reads a ByteString event field or returns its null value.
        /// </summary>
        public static ByteString GetByteString(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out ByteString v)
                        ? v : default;
        }

        /// <summary>
        /// Reads a string event field or returns <c>null</c>.
        /// </summary>
        public static string? GetString(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out string v)
                        ? v : null;
        }

        /// <summary>
        /// Reads a DateTime event field or returns its default value.
        /// </summary>
        public static DateTime GetDateTime(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out DateTimeUtc v)
                        ? (DateTime)v : default;
        }

        /// <summary>
        /// Reads a LocalizedText event field or returns its null value.
        /// </summary>
        public static LocalizedText GetLocalizedText(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out LocalizedText v)
                        ? v : LocalizedText.Null;
        }

        /// <summary>
        /// Reads a UInt16 event field or returns zero.
        /// </summary>
        public static ushort GetUInt16(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out ushort v)
                        ? v : (ushort)0;
        }

        /// <summary>
        /// Reads an optional UInt32 event field.
        /// </summary>
        public static uint? GetNullableUInt32(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out uint value) ? value : null;
        }

        /// <summary>
        /// Reads an optional String array event field.
        /// </summary>
        public static string[]? GetStringArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<string> values)
                ? values.ToArray()
                : null;
        }

        /// <summary>
        /// Reads a Boolean event field or returns <c>false</c>.
        /// </summary>
        public static bool GetBool(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out bool v) && v;
        }

        /// <summary>
        /// Reads a StatusCode event field or returns its default value.
        /// </summary>
        public static StatusCode GetStatusCode(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out StatusCode v)
                        ? v : default;
        }

        /// <summary>
        /// Reads an optional Boolean event field.
        /// </summary>
        public static bool? GetNullableBool(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out bool v) ? v : null;
        }

        /// <summary>
        /// Reads an optional Double event field.
        /// </summary>
        public static double? GetNullableDouble(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out double v) ? v : null;
        }

        /// <summary>
        /// Reads an optional DateTime event field.
        /// </summary>
        public static DateTime? GetNullableDateTime(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out DateTimeUtc v) ? (DateTime)v : null;
        }

        /// <summary>
        /// Reads an optional LocalizedText array event field.
        /// </summary>
        public static LocalizedText[]? GetLocalizedTextArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<LocalizedText> arr)
                ? arr.ToArray() : null;
        }

        /// <summary>
        /// Reads a NodeId event field or returns its null value.
        /// </summary>
        public static NodeId GetNodeId(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out NodeId v)
                        ? v : NodeId.Null;
        }

        /// <summary>
        /// Reads an optional NodeId array event field.
        /// </summary>
        public static NodeId[]? GetNodeIdArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            if (fields[index].TryGetValue(out ArrayOf<NodeId> values))
            {
                return values.ToArray();
            }
            return null;
        }

        /// <summary>
        /// Decodes an optional structured event field.
        /// </summary>
        /// <typeparam name="T">The generated structured data type.</typeparam>
        public static T? GetEncodeable<T>(IReadOnlyList<Variant> fields, int index)
            where T : class, IEncodeable
        {
            if (index >= fields.Count)
            {
                return null;
            }
            if (fields[index].TryGetValue(out ExtensionObject extension) &&
                extension.TryGetValue(out T? value))
            {
                return value;
            }
            return null;
        }

        /// <summary>
        /// Reads an optional SByte event field.
        /// </summary>
        public static sbyte? GetNullableSByte(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out sbyte v) ? v : null;
        }

        /// <summary>
        /// Reads an optional Byte event field.
        /// </summary>
        public static byte? GetNullableByte(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out byte v) ? v : null;
        }

        /// <summary>
        /// Reads an optional Int16 event field.
        /// </summary>
        public static short? GetNullableInt16(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out short v) ? v : null;
        }

        /// <summary>
        /// Reads an optional UInt16 event field.
        /// </summary>
        public static ushort? GetNullableUInt16(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ushort v) ? v : null;
        }

        /// <summary>
        /// Reads an optional Int32 event field.
        /// </summary>
        public static int? GetNullableInt32(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out int v) ? v : null;
        }

        /// <summary>
        /// Reads an optional Int64 event field.
        /// </summary>
        public static long? GetNullableInt64(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out long v) ? v : null;
        }

        /// <summary>
        /// Reads an optional UInt64 event field.
        /// </summary>
        public static ulong? GetNullableUInt64(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ulong v) ? v : null;
        }

        /// <summary>
        /// Reads an optional Float event field.
        /// </summary>
        public static float? GetNullableFloat(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out float v) ? v : null;
        }

        /// <summary>
        /// Reads an optional Guid event field.
        /// </summary>
        public static Guid? GetNullableGuid(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out Uuid v) ? (Guid)v : null;
        }

        /// <summary>
        /// Reads an XmlElement event field or returns <c>null</c>.
        /// </summary>
        public static System.Xml.XmlElement? GetXmlElement(
            IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out XmlElement v)
                ? (System.Xml.XmlElement?)v : null;
        }

        /// <summary>
        /// Reads an ExpandedNodeId event field or returns its null value.
        /// </summary>
        public static ExpandedNodeId GetExpandedNodeId(
            IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out ExpandedNodeId v)
                        ? v : ExpandedNodeId.Null;
        }

        /// <summary>
        /// Reads a QualifiedName event field or returns its null value.
        /// </summary>
        public static QualifiedName GetQualifiedName(
            IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out QualifiedName v)
                        ? v : QualifiedName.Null;
        }

        /// <summary>
        /// Reads an event field as the raw variant it arrived as. Used for
        /// fields whose data type has no more specific projection.
        /// </summary>
        public static Variant GetVariant(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count ? fields[index] : Variant.Null;
        }

        /// <summary>
        /// Reads an optional Boolean array event field.
        /// </summary>
        public static bool[]? GetBoolArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<bool> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional SByte array event field.
        /// </summary>
        public static sbyte[]? GetSByteArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<sbyte> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional Byte array event field.
        /// </summary>
        public static byte[]? GetByteArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<byte> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional Int16 array event field.
        /// </summary>
        public static short[]? GetInt16Array(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<short> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional UInt16 array event field.
        /// </summary>
        public static ushort[]? GetUInt16Array(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<ushort> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional Int32 array event field.
        /// </summary>
        public static int[]? GetInt32Array(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<int> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional UInt32 array event field.
        /// </summary>
        public static uint[]? GetUInt32Array(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<uint> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional Int64 array event field.
        /// </summary>
        public static long[]? GetInt64Array(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<long> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional UInt64 array event field.
        /// </summary>
        public static ulong[]? GetUInt64Array(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<ulong> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional Float array event field.
        /// </summary>
        public static float[]? GetFloatArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<float> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional Double array event field.
        /// </summary>
        public static double[]? GetDoubleArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<double> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional DateTime array event field.
        /// </summary>
        public static DateTime[]? GetDateTimeArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            if (!fields[index].TryGetValue(out ArrayOf<DateTimeUtc> values))
            {
                return null;
            }
            var result = new DateTime[values.Count];
            for (int ii = 0; ii < values.Count; ii++)
            {
                result[ii] = (DateTime)values[ii];
            }
            return result;
        }

        /// <summary>
        /// Reads an optional Guid array event field.
        /// </summary>
        public static Guid[]? GetGuidArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            if (!fields[index].TryGetValue(out ArrayOf<Uuid> values))
            {
                return null;
            }
            var result = new Guid[values.Count];
            for (int ii = 0; ii < values.Count; ii++)
            {
                result[ii] = (Guid)values[ii];
            }
            return result;
        }

        /// <summary>
        /// Reads an optional ByteString array event field.
        /// </summary>
        public static ByteString[]? GetByteStringArray(
            IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<ByteString> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional XmlElement array event field.
        /// </summary>
        public static System.Xml.XmlElement?[]? GetXmlElementArray(
            IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            if (!fields[index].TryGetValue(out ArrayOf<XmlElement> values))
            {
                return null;
            }
            var result = new System.Xml.XmlElement?[values.Count];
            for (int ii = 0; ii < values.Count; ii++)
            {
                result[ii] = (System.Xml.XmlElement?)values[ii];
            }
            return result;
        }

        /// <summary>
        /// Reads an optional ExpandedNodeId array event field.
        /// </summary>
        public static ExpandedNodeId[]? GetExpandedNodeIdArray(
            IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<ExpandedNodeId> v)
                ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional QualifiedName array event field.
        /// </summary>
        public static QualifiedName[]? GetQualifiedNameArray(
            IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<QualifiedName> v)
                ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional StatusCode array event field.
        /// </summary>
        public static StatusCode[]? GetStatusCodeArray(
            IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<StatusCode> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an optional Variant array event field. Used for fields whose
        /// element data type has no more specific projection.
        /// </summary>
        public static Variant[]? GetVariantArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            // NOTE: this only matches a field that arrived as a literal
            // Variant[]. A field the generator has no specific projection for
            // usually arrives as an array of a concrete built-in type
            // (ExtensionObject[] for a structure array, String[] for a
            // UriString array) and still reads back as null here. Wrapping
            // those element-wise needs a non-reflective Variant conversion the
            // stack does not expose yet - Variant(object) is deprecated and
            // AOT-hostile.
            return fields[index].TryGetValue(out ArrayOf<Variant> v) ? v.ToArray() : null;
        }

        /// <summary>
        /// Reads an enumeration event field. An OPC UA enumeration is
        /// transferred as its underlying Int32.
        /// </summary>
        /// <typeparam name="T">The generated enumeration type.</typeparam>
        public static T GetEnum<T>(IReadOnlyList<Variant> fields, int index)
            where T : struct, Enum
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return default;
            }
            return fields[index].TryGetValue(out int value)
                ? (T)Enum.ToObject(typeof(T), value)
                : default;
        }

        /// <summary>
        /// Reads an optional enumeration array event field.
        /// </summary>
        /// <typeparam name="T">The generated enumeration type.</typeparam>
        public static T[]? GetEnumArray<T>(IReadOnlyList<Variant> fields, int index)
            where T : struct, Enum
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            if (!fields[index].TryGetValue(out ArrayOf<int> values))
            {
                return null;
            }
            var result = new T[values.Count];
            for (int ii = 0; ii < result.Length; ii++)
            {
                result[ii] = (T)Enum.ToObject(typeof(T), values[ii]);
            }
            return result;
        }

        /// <summary>
        /// Decodes an optional array of structured event fields.
        /// </summary>
        /// <typeparam name="T">The generated structured data type.</typeparam>
        public static T[]? GetEncodeableArray<T>(IReadOnlyList<Variant> fields, int index)
            where T : class, IEncodeable
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            if (!fields[index].TryGetValue(out ArrayOf<ExtensionObject> extensions))
            {
                return null;
            }
            var values = new T[extensions.Count];
            for (int i = 0; i < extensions.Count; i++)
            {
                if (!extensions[i].TryGetValue(out T? value))
                {
                    return null;
                }
                values[i] = value;
            }
            return values;
        }
    }
}
