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
            switch (value)
            {
                case null: return Variant.Null;
                case Variant v: return v;
                case bool b: return new Variant(b);
                case sbyte sb: return new Variant(sb);
                case byte by: return new Variant(by);
                case short s16: return new Variant(s16);
                case ushort u16: return new Variant(u16);
                case int s32: return new Variant(s32);
                case uint u32: return new Variant(u32);
                case long s64: return new Variant(s64);
                case ulong u64: return new Variant(u64);
                case float f: return new Variant(f);
                case double d: return new Variant(d);
                case string str: return new Variant(str);
                case DateTime dt: return new Variant(dt);
                case Guid g: return new Variant(new Uuid(g));
                case Uuid uuid: return new Variant(uuid);
                case byte[] bytes: return new Variant(ByteString.From(bytes));
                case ByteString bs: return new Variant(bs);
                case XmlElement xml: return new Variant(xml);
                case NodeId nodeId: return new Variant(nodeId);
                case ExpandedNodeId expandedNodeId: return new Variant(expandedNodeId);
                case QualifiedName qn: return new Variant(qn);
                case LocalizedText lt: return new Variant(lt);
                case StatusCode sc: return new Variant(sc);
                case bool[] ba: return new Variant(new ArrayOf<bool>(ba));
                case sbyte[] sba: return new Variant(new ArrayOf<sbyte>(sba));
                case short[] s16a: return new Variant(new ArrayOf<short>(s16a));
                case ushort[] u16a: return new Variant(new ArrayOf<ushort>(u16a));
                case int[] s32a: return new Variant(new ArrayOf<int>(s32a));
                case uint[] u32a: return new Variant(new ArrayOf<uint>(u32a));
                case long[] s64a: return new Variant(new ArrayOf<long>(s64a));
                case ulong[] u64a: return new Variant(new ArrayOf<ulong>(u64a));
                case float[] fa: return new Variant(new ArrayOf<float>(fa));
                case double[] da: return new Variant(new ArrayOf<double>(da));
                case string[] sa: return new Variant(new ArrayOf<string>(sa));
                case DateTime[] dta:
                    DateTimeUtc[] dtua = new DateTimeUtc[dta.Length];
                    for (int i = 0; i < dta.Length; i++) { dtua[i] = new DateTimeUtc(dta[i]); }
                    return new Variant(new ArrayOf<DateTimeUtc>(dtua));
                default:
#pragma warning disable CS0618 // intentional reflection fallback
                    return new Variant(value);
#pragma warning restore CS0618
            }
        }
    }
}
