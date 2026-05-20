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
using System.Diagnostics.CodeAnalysis;
using System.Xml;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Internal helpers for converting between WoT provider scalar /
    /// array values and OPC UA <see cref="Variant"/>.
    /// </summary>
    /// <remarks>
    /// The fast path covers every type listed in OPC 10100-1 §6.3.8
    /// Table 14 (the WoT property type mapping) and their array
    /// counterparts. Anything else falls back to the reflection-based
    /// <see cref="Variant"/> constructor, which is flagged for the
    /// AOT/trimming analyzers — providers that lean on this path are
    /// responsible for keeping their data shapes AOT-safe.
    /// </remarks>
    internal static class WotVariantHelper
    {
        [SuppressMessage("Trimming", "IL2026", Justification = "Reflection fallback documented; AOT-safe path handled above.")]
        [SuppressMessage("AOT", "IL3050", Justification = "Reflection fallback documented; AOT-safe path handled above.")]
        public static Variant ToVariant(object? value)
        {
            // Scalars first — boxed primitives carry their exact runtime
            // type so pattern matching on them is unambiguous.
            switch (value)
            {
                case null:
                    return Variant.Null;
                case Variant v:
                    return v;
                case bool b:
                    return new Variant(b);
                case sbyte sb:
                    return new Variant(sb);
                case byte by:
                    return new Variant(by);
                case short s16:
                    return new Variant(s16);
                case ushort u16:
                    return new Variant(u16);
                case int s32:
                    return new Variant(s32);
                case uint u32:
                    return new Variant(u32);
                case long s64:
                    return new Variant(s64);
                case ulong u64:
                    return new Variant(u64);
                case float f:
                    return new Variant(f);
                case double d:
                    return new Variant(d);
                case string str:
                    return new Variant(str);
                case DateTime dt:
                    return new Variant(dt);
                case Guid g:
                    return new Variant(new Uuid(g));
                case Uuid uuid:
                    return new Variant(uuid);
                case ByteString bs:
                    return new Variant(bs);
                case XmlElement xml:
                    return new Variant(xml);
                case NodeId nodeId:
                    return new Variant(nodeId);
                case ExpandedNodeId expandedNodeId:
                    return new Variant(expandedNodeId);
                case QualifiedName qn:
                    return new Variant(qn);
                case LocalizedText lt:
                    return new Variant(lt);
                case StatusCode sc:
                    return new Variant(sc);
            }

            // Arrays: the CLR treats signed / unsigned same-size primitive
            // arrays as runtime-compatible (sbyte[] is-a byte[], short[]
            // is-a ushort[], etc. per ECMA-335 II.8.7), so a C# type
            // pattern like `case byte[]` would also match sbyte[]. Dispatch
            // on the exact array type instead to keep the mapping accurate.
            Type t = value.GetType();
            if (t == typeof(byte[]))
            { return new Variant(ByteString.From((byte[])value)); }
            if (t == typeof(sbyte[]))
            { return new Variant(new ArrayOf<sbyte>((sbyte[])value)); }
            if (t == typeof(bool[]))
            { return new Variant(new ArrayOf<bool>((bool[])value)); }
            if (t == typeof(short[]))
            { return new Variant(new ArrayOf<short>((short[])value)); }
            if (t == typeof(ushort[]))
            { return new Variant(new ArrayOf<ushort>((ushort[])value)); }
            if (t == typeof(int[]))
            { return new Variant(new ArrayOf<int>((int[])value)); }
            if (t == typeof(uint[]))
            { return new Variant(new ArrayOf<uint>((uint[])value)); }
            if (t == typeof(long[]))
            { return new Variant(new ArrayOf<long>((long[])value)); }
            if (t == typeof(ulong[]))
            { return new Variant(new ArrayOf<ulong>((ulong[])value)); }
            if (t == typeof(float[]))
            { return new Variant(new ArrayOf<float>((float[])value)); }
            if (t == typeof(double[]))
            { return new Variant(new ArrayOf<double>((double[])value)); }
            if (t == typeof(string[]))
            { return new Variant(new ArrayOf<string>((string[])value)); }
            if (t == typeof(DateTime[]))
            {
                DateTime[] dta = (DateTime[])value;
                DateTimeUtc[] dtua = new DateTimeUtc[dta.Length];
                for (int i = 0; i < dta.Length; i++)
                { dtua[i] = new DateTimeUtc(dta[i]); }
                return new Variant(new ArrayOf<DateTimeUtc>(dtua));
            }

#pragma warning disable CS0618 // intentional reflection fallback
            return new Variant(value);
#pragma warning restore CS0618
        }
    }
}
