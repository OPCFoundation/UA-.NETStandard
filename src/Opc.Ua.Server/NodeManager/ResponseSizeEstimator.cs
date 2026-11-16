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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Computes cheap lower bounds of the binary encoded size of service results, which
    /// the response byte budget of a service call is charged with
    /// (<see cref="OperationContext.TryChargeResponse(long)"/>).
    /// </summary>
    /// <remarks>
    /// Every estimate is at most the encoded size, so a budget equal to the message size
    /// limit only refuses responses that the encoder would refuse as well. A string counts
    /// one byte per UTF-16 code unit, which UTF-8 never encodes in fewer bytes.
    /// </remarks>
    internal static class ResponseSizeEstimator
    {
        /// <summary>
        /// The smallest encoded ReferenceDescription: two-byte ReferenceTypeId, IsForward,
        /// two-byte NodeId, BrowseName (namespace and null name), DisplayName mask,
        /// NodeClass and two-byte TypeDefinition.
        /// </summary>
        public const int MinReferenceDescriptionSize = 2 + 1 + 2 + 6 + 1 + 4 + 2;

        /// <summary>
        /// The smallest encoded BrowseResult: StatusCode, null continuation point and an
        /// empty references array.
        /// </summary>
        public const int MinBrowseResultSize = 4 + 4 + 4;

        /// <summary>
        /// The smallest encoded CallMethodResult: StatusCode and three array lengths.
        /// </summary>
        public const int MinCallMethodResultSize = 4 + 4 + 4 + 4;

        /// <summary>
        /// A lower bound of the encoded size of a reference description.
        /// </summary>
        public static long Estimate(ReferenceDescription? reference)
        {
            if (reference == null)
            {
                return MinReferenceDescriptionSize;
            }
            long size = MinReferenceDescriptionSize;
            string? name = reference.BrowseName.Name;
            if (name != null)
            {
                size += name.Length;
            }
            string? text = reference.DisplayName.Text;
            if (text != null)
            {
                size += text.Length;
            }
            return size;
        }

        /// <summary>
        /// A lower bound of the encoded size of a browse result with the references.
        /// </summary>
        public static long Estimate(ArrayOf<ReferenceDescription> references)
        {
            long size = MinBrowseResultSize;
            for (int ii = 0; ii < references.Count; ii++)
            {
                size += Estimate(references[ii]);
            }
            return size;
        }

        /// <summary>
        /// A lower bound of the encoded size of a method call result with the outputs.
        /// </summary>
        public static long Estimate(ArrayOf<Variant> outputs)
        {
            long size = MinCallMethodResultSize;
            for (int ii = 0; ii < outputs.Count; ii++)
            {
                size += Estimate(outputs[ii]);
            }
            return size;
        }

        /// <summary>
        /// A lower bound of the encoded size of a variant. Values whose size is not
        /// cheap to compute count with their encoding mask and array length only.
        /// </summary>
        public static long Estimate(in Variant value)
        {
            TypeInfo typeInfo = value.TypeInfo;
            if (typeInfo.IsUnknown)
            {
                return 1;
            }
            if (typeInfo.ValueRank < 0)
            {
                return 1 + EstimateScalar(value, typeInfo.BuiltInType);
            }
            // encoding mask and array length; matrices count as their mask only.
            return typeInfo.ValueRank == ValueRanks.OneDimension
                ? 1 + 4 + EstimateArray(value, typeInfo.BuiltInType)
                : 1;
        }

        private static long EstimateScalar(in Variant value, BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                case BuiltInType.SByte:
                case BuiltInType.Byte:
                    return 1;
                case BuiltInType.Int16:
                case BuiltInType.UInt16:
                    return 2;
                case BuiltInType.Int32:
                case BuiltInType.UInt32:
                case BuiltInType.Float:
                case BuiltInType.StatusCode:
                case BuiltInType.Enumeration:
                    return 4;
                case BuiltInType.Int64:
                case BuiltInType.UInt64:
                case BuiltInType.Double:
                case BuiltInType.DateTime:
                    return 8;
                case BuiltInType.Guid:
                    return 16;
                case BuiltInType.String:
                    return 4 + (value.TryGetValue(out string text) && text != null ? text.Length : 0);
                case BuiltInType.ByteString:
                    return 4 + (value.TryGetValue(out ByteString bytes) ? bytes.Length : 0);
                default:
                    return 0;
            }
        }

        private static long EstimateArray(in Variant value, BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    return value.TryGetValue(out ArrayOf<bool> booleans) ? booleans.Count : 0;
                case BuiltInType.SByte:
                    return value.TryGetValue(out ArrayOf<sbyte> sbytes) ? sbytes.Count : 0;
                case BuiltInType.Byte:
                    return value.TryGetValue(out ArrayOf<byte> bytes) ? bytes.Count : 0;
                case BuiltInType.Int16:
                    return value.TryGetValue(out ArrayOf<short> shorts) ? 2L * shorts.Count : 0;
                case BuiltInType.UInt16:
                    return value.TryGetValue(out ArrayOf<ushort> ushorts) ? 2L * ushorts.Count : 0;
                case BuiltInType.Int32:
                    return value.TryGetValue(out ArrayOf<int> ints) ? 4L * ints.Count : 0;
                case BuiltInType.UInt32:
                    return value.TryGetValue(out ArrayOf<uint> uints) ? 4L * uints.Count : 0;
                case BuiltInType.Int64:
                    return value.TryGetValue(out ArrayOf<long> longs) ? 8L * longs.Count : 0;
                case BuiltInType.UInt64:
                    return value.TryGetValue(out ArrayOf<ulong> ulongs) ? 8L * ulongs.Count : 0;
                case BuiltInType.Float:
                    return value.TryGetValue(out ArrayOf<float> floats) ? 4L * floats.Count : 0;
                case BuiltInType.Double:
                    return value.TryGetValue(out ArrayOf<double> doubles) ? 8L * doubles.Count : 0;
                case BuiltInType.DateTime:
                    return value.TryGetValue(out ArrayOf<DateTimeUtc> dates) ? 8L * dates.Count : 0;
                case BuiltInType.Guid:
                    return value.TryGetValue(out ArrayOf<Uuid> guids) ? 16L * guids.Count : 0;
                case BuiltInType.StatusCode:
                    return value.TryGetValue(out ArrayOf<StatusCode> codes) ? 4L * codes.Count : 0;
                case BuiltInType.String:
                    if (value.TryGetValue(out ArrayOf<string> strings))
                    {
                        long size = 4L * strings.Count;
                        for (int ii = 0; ii < strings.Count; ii++)
                        {
                            size += strings[ii]?.Length ?? 0;
                        }
                        return size;
                    }
                    return 0;
                case BuiltInType.ByteString:
                    if (value.TryGetValue(out ArrayOf<ByteString> byteStrings))
                    {
                        long size = 4L * byteStrings.Count;
                        for (int ii = 0; ii < byteStrings.Count; ii++)
                        {
                            size += byteStrings[ii].Length;
                        }
                        return size;
                    }
                    return 0;
                case BuiltInType.Variant:
                    return value.TryGetValue(out ArrayOf<Variant> variants) ? variants.Count : 0;
                case BuiltInType.NodeId:
                    return value.TryGetValue(out ArrayOf<NodeId> nodeIds) ? 2L * nodeIds.Count : 0;
                default:
                    return 0;
            }
        }
    }
}
