/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

namespace Opc.Ua.IA.Server
{
    /// <summary>
    /// Detaches provider values from caller-owned buffers before publication.
    /// </summary>
    internal static class IaValueCopy
    {
        /// <summary>
        /// Returns a value whose array storage is owned by the address space.
        /// </summary>
        /// <remarks>
        /// <see cref="Variant.Copy"/> clones structured payloads but shares
        /// primitive array memory, so a provider that reuses its buffer would
        /// otherwise change the published value without a notification.
        /// </remarks>
        public static Variant Own(in Variant value)
        {
            if (value.IsNull || !value.TypeInfo.IsArray || value.IsEmptyArray)
            {
                return value.Copy();
            }
            switch (value.TypeInfo.BuiltInType)
            {
                case BuiltInType.Boolean when value.TryGetValue(out ArrayOf<bool> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.SByte when value.TryGetValue(out ArrayOf<sbyte> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.Byte when value.TryGetValue(out ArrayOf<byte> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.Int16 when value.TryGetValue(out ArrayOf<short> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.UInt16 when value.TryGetValue(out ArrayOf<ushort> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.Int32 when value.TryGetValue(out ArrayOf<int> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.UInt32 when value.TryGetValue(out ArrayOf<uint> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.Int64 when value.TryGetValue(out ArrayOf<long> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.UInt64 when value.TryGetValue(out ArrayOf<ulong> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.Float when value.TryGetValue(out ArrayOf<float> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.Double when value.TryGetValue(out ArrayOf<double> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.String when value.TryGetValue(out ArrayOf<string> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.DateTime when value.TryGetValue(out ArrayOf<DateTimeUtc> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.Guid when value.TryGetValue(out ArrayOf<Uuid> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.StatusCode when value.TryGetValue(out ArrayOf<StatusCode> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.NodeId when value.TryGetValue(out ArrayOf<NodeId> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.QualifiedName when value.TryGetValue(out ArrayOf<QualifiedName> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                case BuiltInType.LocalizedText when value.TryGetValue(out ArrayOf<LocalizedText> v):
                    return Variant.From(ArrayOf.Create(v.Span));
                default:
                    return value.Copy();
            }
        }
    }
}
