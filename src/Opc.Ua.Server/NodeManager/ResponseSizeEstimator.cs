// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        /// The encoded size of a HistoryRead response without its results: the four-byte
        /// type id, the ResponseHeader (timestamp, handle, result, empty diagnostic info,
        /// empty string table, null additional header) and the lengths of the results and
        /// diagnostic infos arrays.
        /// </summary>
        public const int HistoryReadResponseFramingSize = 4 + (8 + 4 + 4 + 1 + 4 + 3) + 4 + 4;

        /// <summary>
        /// The bytes a history continuation point (a Guid) adds to a result over the null
        /// continuation point of a result that is complete.
        /// </summary>
        public const int HistoryContinuationPointSize = 16;

        /// <summary>
        /// The encoded size of one HistoryRead result without its values, as a history page
        /// is sent: StatusCode, a 16 byte continuation point, the extension object header
        /// of the history data (four-byte type id, encoding, length) and the length of its
        /// values array. The last page of a read uses the continuation point bytes for its
        /// values.
        /// </summary>
        public const int HistoryReadResultFramingSize =
            4 + (4 + HistoryContinuationPointSize) + (4 + 1 + 4) + 4;

        /// <summary>
        /// The response byte budget of a HistoryRead: the message size limit less the
        /// framing of the response and of each of its results, so that history pages that
        /// fit the budget are sent with their continuation points. Zero (no budget) when
        /// <paramref name="maxMessageSize"/> is zero, and -1 when the framing alone
        /// exceeds the limit (<see cref="OperationContext.StartExhaustedResponseBudget"/>).
        /// </summary>
        /// <remarks>
        /// When the client asks for operation diagnostics, an empty diagnostic info is
        /// reserved per result; the content of diagnostics is not reserved.
        /// </remarks>
        public static int GetHistoryReadResponseBudget(
            int maxMessageSize,
            int resultCount,
            bool returnsOperationDiagnostics = false)
        {
            if (maxMessageSize <= 0)
            {
                return 0;
            }
            int resultFraming = HistoryReadResultFramingSize + (returnsOperationDiagnostics ? 1 : 0);
            long budget = maxMessageSize - HistoryReadResponseFramingSize -
                ((long)resultCount * resultFraming);
            // the deficit is not clamped away: the continuation point bytes that complete
            // results return to the budget would otherwise cover it.
            return budget < 1 ? -1 : (int)budget;
        }

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
        /// A lower bound of the encoded size of a data value: its encoding mask and the
        /// fields the mask selects.
        /// </summary>
        public static long Estimate(in DataValue value)
        {
            if (value.IsNull)
            {
                return 1;
            }
            long size = EstimateValue(value);
            if (value.StatusCodeValue != 0)
            {
                size += 4;
            }
            if (value.SourceTimestamp != DateTimeUtc.MinValue)
            {
                size += value.SourcePicoseconds != 0 ? 8 + 2 : 8;
            }
            if (value.ServerTimestamp != DateTimeUtc.MinValue)
            {
                size += value.ServerPicoseconds != 0 ? 8 + 2 : 8;
            }
            return size;
        }

        /// <summary>
        /// A lower bound of the encoded size of a data value that counts its encoding
        /// mask and its value only, for a data value whose status and timestamps can
        /// still be removed before it is sent (the timestamp filters of Read).
        /// </summary>
        public static long EstimateValue(in DataValue value)
        {
            if (value.IsNull)
            {
                return 1;
            }
            Variant wrappedValue = value.WrappedValue;
            return wrappedValue.IsNull ? 1 : 1 + Estimate(wrappedValue);
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
