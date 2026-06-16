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
    internal static class EventRecordFieldReaders
    {
        public static ByteString GetByteString(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out ByteString v)
                        ? v : default;
        }

        public static string? GetString(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out string v)
                        ? v : null;
        }

        public static DateTime GetDateTime(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out DateTimeUtc v)
                        ? (DateTime)v : default;
        }

        public static LocalizedText GetLocalizedText(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out LocalizedText v)
                        ? v : LocalizedText.Null;
        }

        public static ushort GetUInt16(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out ushort v)
                        ? v : (ushort)0;
        }

        public static bool GetBool(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out bool v) && v;
        }

        public static StatusCode GetStatusCode(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out StatusCode v)
                        ? v : default;
        }

        public static bool? GetNullableBool(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out bool v) ? v : null;
        }

        public static double? GetNullableDouble(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out double v) ? v : null;
        }

        public static DateTime? GetNullableDateTime(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out DateTimeUtc v) ? (DateTime)v : null;
        }

        public static LocalizedText[]? GetLocalizedTextArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<LocalizedText> arr)
                ? arr.ToArray() : null;
        }

        public static NodeId GetNodeId(IReadOnlyList<Variant> fields, int index)
        {
            return index < fields.Count && fields[index].TryGetValue(out NodeId v)
                        ? v : NodeId.Null;
        }
    }
}
