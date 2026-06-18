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

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Deserialises the payload of a UADP DataSetMessage.
    /// </summary>
    /// <remarks>
    /// Implements the inverse of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.4">
    /// Part 14 §7.2.4.5.4</see>.
    /// </remarks>
    internal static class UadpFieldDecoder
    {
        /// <summary>
        /// Decodes a DataSet payload into a list of
        /// <see cref="DataSetField"/>. Returns an empty list for
        /// KeepAlive messages.
        /// </summary>
        /// <param name="reader">Active reader positioned right after the
        /// DataSetMessage header.</param>
        /// <param name="encoding">Field encoding mode from
        /// DataSetFlags1.</param>
        /// <param name="messageType">DataSet message type from
        /// DataSetFlags2.</param>
        /// <param name="metaData">DataSet metadata; required for RawData
        /// encoding and used to bind field names for the other
        /// encodings.</param>
        /// <param name="context">Stack service message context.</param>
        /// <returns>The decoded fields, or <c>null</c> if the payload was
        /// malformed (truncated, missing required metadata, etc.).</returns>
        public static ArrayOf<DataSetField>? DecodeFields(
            ref UadpBinaryReader reader,
            PubSubFieldEncoding encoding,
            PubSubDataSetMessageType messageType,
            DataSetMetaDataType? metaData,
            IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (messageType == PubSubDataSetMessageType.KeepAlive)
            {
                return [];
            }

            if (messageType == PubSubDataSetMessageType.DeltaFrame)
            {
                return DecodeDeltaFrame(ref reader, encoding, metaData, context);
            }

            return DecodeKeyOrEventFrame(ref reader, encoding, metaData, context);
        }

        private static ArrayOf<DataSetField>? DecodeKeyOrEventFrame(
            ref UadpBinaryReader reader,
            PubSubFieldEncoding encoding,
            DataSetMetaDataType? metaData,
            IServiceMessageContext context)
        {
            int fieldCount;
            if (encoding == PubSubFieldEncoding.RawData)
            {
                if (metaData is null || metaData.Fields.Count == 0)
                {
                    return null;
                }
                fieldCount = metaData.Fields.Count;
            }
            else
            {
                if (!reader.TryReadUInt16Le(out ushort declaredCount))
                {
                    return null;
                }
                fieldCount = declaredCount;
            }

            if (fieldCount < 0)
            {
                return null;
            }

            var fields = new List<DataSetField>(fieldCount);
            for (int i = 0; i < fieldCount; i++)
            {
                DataSetField? field = ReadOneField(
                    ref reader, encoding, metaData, i, context);
                if (field is null)
                {
                    return null;
                }
                fields.Add(field);
            }
            return fields;
        }

        private static ArrayOf<DataSetField>? DecodeDeltaFrame(
            ref UadpBinaryReader reader,
            PubSubFieldEncoding encoding,
            DataSetMetaDataType? metaData,
            IServiceMessageContext context)
        {
            if (!reader.TryReadUInt16Le(out ushort fieldCount))
            {
                return null;
            }

            var fields = new List<DataSetField>(fieldCount);
            for (int i = 0; i < fieldCount; i++)
            {
                if (!reader.TryReadUInt16Le(out ushort fieldIndex))
                {
                    return null;
                }

                DataSetField? field = ReadOneField(
                    ref reader, encoding, metaData, fieldIndex, context);
                if (field is null)
                {
                    return null;
                }
                fields.Add(field);
            }
            return fields;
        }

        private static DataSetField? ReadOneField(
            ref UadpBinaryReader reader,
            PubSubFieldEncoding encoding,
            DataSetMetaDataType? metaData,
            int metadataIndex,
            IServiceMessageContext context)
        {
            string name = string.Empty;
            FieldMetaData? fmd = null;
            if (metaData is not null && metadataIndex >= 0 &&
                metadataIndex < metaData.Fields.Count)
            {
                fmd = metaData.Fields[metadataIndex];
                if (fmd is not null && fmd.Name is not null)
                {
                    name = fmd.Name;
                }
            }

            switch (encoding)
            {
                case PubSubFieldEncoding.Variant:
                    {
                        Variant value;
                        try
                        {
                            value = reader.ReadVariant(context);
                        }
                        catch
                        {
                            return null;
                        }
                        return new DataSetField
                        {
                            Name = name,
                            Value = value,
                            Encoding = PubSubFieldEncoding.Variant
                        };
                    }
                case PubSubFieldEncoding.DataValue:
                    DataValue dv;
                    try
                    {
                        dv = reader.ReadDataValue(context);
                    }
                    catch
                    {
                        return null;
                    }
                    return new DataSetField
                    {
                        Name = name,
                        Value = dv.WrappedValue,
                        StatusCode = dv.StatusCode,
                        SourceTimestamp = dv.SourceTimestamp,
                        SourcePicoSeconds = dv.SourcePicoseconds,
                        ServerTimestamp = dv.ServerTimestamp,
                        ServerPicoSeconds = dv.ServerPicoseconds,
                        Encoding = PubSubFieldEncoding.DataValue
                    };
                case PubSubFieldEncoding.RawData:
                    {
                        if (fmd is null)
                        {
                            return null;
                        }
                        Variant value;
                        try
                        {
                            value = reader.ReadRawScalar(
                                fmd.BuiltInType.ToBuiltInType(),
                                fmd.ValueRank,
                                fmd.MaxStringLength,
                                fmd.ArrayDimensions,
                                context);
                        }
                        catch
                        {
                            return null;
                        }
                        return new DataSetField
                        {
                            Name = name,
                            Value = value,
                            Encoding = PubSubFieldEncoding.RawData
                        };
                    }
                default:
                    return null;
            }
        }
    }
}
