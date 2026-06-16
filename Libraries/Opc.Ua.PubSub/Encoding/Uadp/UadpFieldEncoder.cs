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
    /// Serialises a UADP DataSetMessage payload (the field block that
    /// follows the DataSetMessage header).
    /// </summary>
    /// <remarks>
    /// Implements the field encoding rules from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.4">
    /// Part 14 §7.2.4.5.4</see> (Table 162 / Table 165) including the
    /// three field-encoding modes (Variant / RawData / DataValue) and
    /// the differing layouts for KeyFrame, DeltaFrame, Event and
    /// KeepAlive messages.
    /// </remarks>
    internal static class UadpFieldEncoder
    {
        /// <summary>
        /// Encodes the payload block for a single DataSetMessage.
        /// </summary>
        /// <param name="writer">Active UADP writer positioned right after the
        /// DataSetMessage header.</param>
        /// <param name="fields">Source fields in metadata order.</param>
        /// <param name="encoding">Selected field encoding mode.</param>
        /// <param name="messageType">DataSet message type (KeyFrame /
        /// DeltaFrame / Event / KeepAlive).</param>
        /// <param name="metaData">DataSet metadata used for RawData
        /// scalar/array layout; may be <c>null</c> for Variant / DataValue
        /// encodings.</param>
        /// <param name="context">Stack service message context.</param>
        /// <param name="fieldContentMask">Per-field content mask honoured
        /// when <paramref name="encoding"/> is
        /// <see cref="PubSubFieldEncoding.DataValue"/>. Defaults to
        /// <see cref="DataSetFieldContentMask.None"/> for backward
        /// compatibility (all members emitted).</param>
        public static void EncodeFields(
            ref UadpBinaryWriter writer,
            IReadOnlyList<DataSetField> fields,
            PubSubFieldEncoding encoding,
            PubSubDataSetMessageType messageType,
            DataSetMetaDataType? metaData,
            IServiceMessageContext context,
            DataSetFieldContentMask fieldContentMask = DataSetFieldContentMask.None)
        {
            if (fields is null)
            {
                throw new ArgumentNullException(nameof(fields));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (messageType == PubSubDataSetMessageType.KeepAlive)
            {
                return;
            }

            if (messageType == PubSubDataSetMessageType.DeltaFrame)
            {
                EncodeDeltaFrame(
                    ref writer, fields, encoding, metaData, context, fieldContentMask);
                return;
            }

            EncodeKeyOrEventFrame(
                ref writer, fields, encoding, metaData, context, fieldContentMask);
        }

        private static void EncodeKeyOrEventFrame(
            ref UadpBinaryWriter writer,
            IReadOnlyList<DataSetField> fields,
            PubSubFieldEncoding encoding,
            DataSetMetaDataType? metaData,
            IServiceMessageContext context,
            DataSetFieldContentMask fieldContentMask)
        {
            switch (encoding)
            {
                case PubSubFieldEncoding.Variant:
                    writer.WriteUInt16Le((ushort)fields.Count);
                    for (int i = 0; i < fields.Count; i++)
                    {
                        writer.WriteVariant(fields[i].Value, context);
                    }
                    break;
                case PubSubFieldEncoding.DataValue:
                    writer.WriteUInt16Le((ushort)fields.Count);
                    for (int i = 0; i < fields.Count; i++)
                    {
                        DataValue dv = BuildDataValue(fields[i], fieldContentMask);
                        writer.WriteDataValue(dv, context);
                    }
                    break;
                case PubSubFieldEncoding.RawData:
                    if (metaData is null || metaData.Fields.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "RawData encoding requires DataSetMetaData with field declarations.");
                    }
                    EncodeRawFields(ref writer, fields, metaData, context);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported PubSubFieldEncoding {encoding}.");
            }
        }

        private static void EncodeDeltaFrame(
            ref UadpBinaryWriter writer,
            IReadOnlyList<DataSetField> fields,
            PubSubFieldEncoding encoding,
            DataSetMetaDataType? metaData,
            IServiceMessageContext context,
            DataSetFieldContentMask fieldContentMask)
        {
            writer.WriteUInt16Le((ushort)fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                DataSetField field = fields[i];
                writer.WriteUInt16Le(field.DeltaFrameFieldIndex(i));

                switch (encoding)
                {
                    case PubSubFieldEncoding.Variant:
                        writer.WriteVariant(field.Value, context);
                        break;
                    case PubSubFieldEncoding.DataValue:
                        DataValue dv = BuildDataValue(field, fieldContentMask);
                        writer.WriteDataValue(dv, context);
                        break;
                    case PubSubFieldEncoding.RawData:
                        if (metaData is null ||
                            i >= metaData.Fields.Count)
                        {
                            throw new InvalidOperationException(
                                "RawData delta frame requires aligned DataSetMetaData fields.");
                        }
                        FieldMetaData fmd = metaData.Fields[i];
                        writer.WriteRawScalar(
                            field.Value,
                            fmd.BuiltInType.ToBuiltInType(),
                            fmd.ValueRank,
                            fmd.MaxStringLength,
                            fmd.ArrayDimensions,
                            context);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported PubSubFieldEncoding {encoding}.");
                }
            }
        }

        /// <summary>
        /// Builds the <see cref="DataValue"/> emitted for one field. When
        /// <paramref name="mask"/> is
        /// <see cref="DataSetFieldContentMask.None"/> every populated
        /// envelope member from the field is preserved (backward-compatible
        /// behaviour). Otherwise only the members whose mask bit is set
        /// flow into the resulting <see cref="DataValue"/>; the rest are
        /// reset to defaults so the underlying
        /// <c>BinaryEncoder.WriteDataValue</c> omits them via its
        /// encoding-mask byte.
        /// </summary>
        /// <param name="field">Source field.</param>
        /// <param name="mask">Per-field content mask from the writer.</param>
        /// <returns>The <see cref="DataValue"/> to serialise.</returns>
        private static DataValue BuildDataValue(
            DataSetField field, DataSetFieldContentMask mask)
        {
            if (mask == DataSetFieldContentMask.None)
            {
                return new DataValue(
                    field.Value,
                    field.StatusCode,
                    field.SourceTimestamp,
                    field.ServerTimestamp,
                    field.SourcePicoSeconds,
                    field.ServerPicoSeconds);
            }
            StatusCode statusCode = (mask & DataSetFieldContentMask.StatusCode) != 0
                ? field.StatusCode
                : StatusCodes.Good;
            DateTimeUtc sourceTimestamp = (mask & DataSetFieldContentMask.SourceTimestamp) != 0
                ? field.SourceTimestamp
                : DateTimeUtc.MinValue;
            DateTimeUtc serverTimestamp = (mask & DataSetFieldContentMask.ServerTimestamp) != 0
                ? field.ServerTimestamp
                : DateTimeUtc.MinValue;
            ushort sourcePico = (mask & DataSetFieldContentMask.SourcePicoSeconds) != 0
                ? field.SourcePicoSeconds
                : (ushort)0;
            ushort serverPico = (mask & DataSetFieldContentMask.ServerPicoSeconds) != 0
                ? field.ServerPicoSeconds
                : (ushort)0;
            return new DataValue(
                field.Value,
                statusCode,
                sourceTimestamp,
                serverTimestamp,
                sourcePico,
                serverPico);
        }

        private static void EncodeRawFields(
            ref UadpBinaryWriter writer,
            IReadOnlyList<DataSetField> fields,
            DataSetMetaDataType metaData,
            IServiceMessageContext context)
        {
            int count = Math.Min(fields.Count, metaData.Fields.Count);
            for (int i = 0; i < count; i++)
            {
                FieldMetaData fmd = metaData.Fields[i];
                writer.WriteRawScalar(
                    fields[i].Value,
                    fmd.BuiltInType.ToBuiltInType(),
                    fmd.ValueRank,
                    fmd.MaxStringLength,
                    fmd.ArrayDimensions,
                    context);
            }
        }
    }

    /// <summary>
    /// Extension helpers shared by the UADP field encoder and decoder.
    /// </summary>
    internal static class UadpFieldEncoderExtensions
    {
        /// <summary>
        /// Converts a metadata <c>BuiltInType</c> byte to
        /// <see cref="Opc.Ua.BuiltInType"/>.
        /// </summary>
        /// <param name="value">Metadata byte value.</param>
        public static BuiltInType ToBuiltInType(this byte value)
        {
            return (BuiltInType)value;
        }

        /// <summary>
        /// Returns the explicit delta frame field index for a field — at
        /// the wire level this is the metadata position; the data model
        /// preserves order, so we use the loop index.
        /// </summary>
        /// <param name="field">Source field.</param>
        /// <param name="index">Iterator index used as the wire index.</param>
        public static ushort DeltaFrameFieldIndex(this DataSetField field, int index)
        {
            _ = field;
            if (index < 0 || index > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return (ushort)index;
        }
    }
}
