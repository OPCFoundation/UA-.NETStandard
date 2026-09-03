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
