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
using System.Text.Json;

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Writes the <c>Payload</c> object of a JSON DataSetMessage by
    /// iterating over a <see cref="DataSetField"/> sequence and
    /// dispatching each entry to the most appropriate Variant /
    /// DataValue / raw-value encoder for the selected
    /// <see cref="JsonEncodingMode"/>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.4">
    /// Part 14 §7.2.5.4</see>. The field's <see cref="PubSubFieldEncoding"/>
    /// overrides the network-wide mode (a field declared
    /// <see cref="PubSubFieldEncoding.RawData"/> always emits a bare
    /// value; a field declared <see cref="PubSubFieldEncoding.DataValue"/>
    /// always emits the <c>Value/Status/SourceTimestamp/...</c> object).
    /// </remarks>
    public static class JsonFieldEncoder
    {
        /// <summary>
        /// Writes the supplied DataSetFields as a JSON object under the
        /// property name <c>Payload</c>.
        /// </summary>
        /// <param name="writer">Destination writer (positioned inside
        /// the parent DataSetMessage object).</param>
        /// <param name="fields">Ordered field list.</param>
        /// <param name="metaData">Optional metadata used to derive field
        /// names when a <see cref="DataSetField"/> omits its name.</param>
        /// <param name="mode">Encoding mode for the network message.</param>
        /// <param name="context">Stack message context.</param>
        /// <param name="fieldContentMask">Per-field content mask honoured
        /// when a field is emitted via the <c>DataValue</c> envelope.
        /// Defaults to <see cref="DataSetFieldContentMask.None"/> for
        /// backward compatibility (every member emitted).</param>
        /// <param name="writePayloadWrapper">When <see langword="true"/>,
        /// writes the fields under a <c>Payload</c> property; otherwise
        /// writes fields directly into the current object.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void EncodeFields(
            Utf8JsonWriter writer,
            ArrayOf<DataSetField> fields,
            DataSetMetaDataType? metaData,
            JsonEncodingMode mode,
            IServiceMessageContext context,
            DataSetFieldContentMask fieldContentMask = DataSetFieldContentMask.None,
            bool writePayloadWrapper = true)
        {
            if (writer is null)
            {
                throw new ArgumentNullException(nameof(writer));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (writePayloadWrapper)
            {
                writer.WritePropertyName("Payload");
                writer.WriteStartObject();
            }
            for (int i = 0; i < fields.Count; i++)
            {
                DataSetField field = fields[i];
                string name = ResolveFieldName(field, metaData, i);
                WriteOneField(writer, name, field, mode, context, fieldContentMask);
            }
            if (writePayloadWrapper)
            {
                writer.WriteEndObject();
            }
        }

        /// <summary>
        /// Determines the JSON property name to use for the field. The
        /// explicit name on the field wins; otherwise the metadata
        /// declaration at the same index is consulted; otherwise an
        /// auto-generated <c>Field{index}</c> name is used so the
        /// payload stays well-formed.
        /// </summary>
        /// <param name="field">Field instance.</param>
        /// <param name="metaData">Optional metadata.</param>
        /// <param name="index">Field index within the DataSetMessage.</param>
        /// <returns>Property name to emit.</returns>
        private static string ResolveFieldName(
            DataSetField field,
            DataSetMetaDataType? metaData,
            int index)
        {
            if (!string.IsNullOrEmpty(field.Name))
            {
                return field.Name;
            }
            if (metaData is not null
                && metaData.Fields.Count > index
                && metaData.Fields[index].Name is { Length: > 0 } resolvedName)
            {
                return resolvedName;
            }
            return FormattableString.Invariant($"Field{index}");
        }

        /// <summary>
        /// Writes a single field. The <see cref="DataSetField.Encoding"/>
        /// selects the wire shape; the network-wide
        /// <paramref name="mode"/> controls Variant envelope use.
        /// </summary>
        /// <param name="writer">Destination writer.</param>
        /// <param name="propertyName">JSON property name.</param>
        /// <param name="field">Source field.</param>
        /// <param name="mode">Encoding mode.</param>
        /// <param name="context">Stack message context.</param>
        /// <param name="fieldContentMask">Per-field content mask honoured
        /// when the field is emitted as a <c>DataValue</c> envelope.</param>
        private static void WriteOneField(
            Utf8JsonWriter writer,
            string propertyName,
            DataSetField field,
            JsonEncodingMode mode,
            IServiceMessageContext context,
            DataSetFieldContentMask fieldContentMask)
        {
            switch (field.Encoding)
            {
                case PubSubFieldEncoding.RawData:
                    JsonVariantEncoder.WriteVariantProperty(
                        writer,
                        propertyName,
                        field.Value,
                        JsonEncodingMode.RawData,
                        context);
                    break;
                case PubSubFieldEncoding.DataValue:
                    DataValue dv = BuildDataValue(field, fieldContentMask);
                    JsonVariantEncoder.WriteDataValueProperty(
                        writer,
                        propertyName,
                        dv,
                        mode,
                        context);
                    break;
                case PubSubFieldEncoding.Variant:
                default:
                    JsonVariantEncoder.WriteVariantProperty(
                        writer,
                        propertyName,
                        field.Value,
                        mode,
                        context);
                    break;
            }
        }

        /// <summary>
        /// Builds the <see cref="DataValue"/> envelope serialised for one
        /// field. When <paramref name="mask"/> is
        /// <see cref="DataSetFieldContentMask.None"/> every populated
        /// envelope member from the field is preserved (backward-compatible
        /// behaviour). Otherwise only the members whose mask bit is set
        /// flow into the result; the rest are reset to defaults so the
        /// underlying JSON writer omits them via standard
        /// <c>DataValue</c> reversible encoding rules.
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
                ? field.StatusCode : default;
            DateTimeUtc sourceTimestamp = (mask & DataSetFieldContentMask.SourceTimestamp) != 0
                ? field.SourceTimestamp : default;
            ushort sourcePico = (mask & DataSetFieldContentMask.SourcePicoSeconds) != 0
                ? field.SourcePicoSeconds : (ushort)0;
            DateTimeUtc serverTimestamp = (mask & DataSetFieldContentMask.ServerTimestamp) != 0
                ? field.ServerTimestamp : default;
            ushort serverPico = (mask & DataSetFieldContentMask.ServerPicoSeconds) != 0
                ? field.ServerPicoSeconds : (ushort)0;
            return new DataValue(
                field.Value,
                statusCode,
                sourceTimestamp,
                serverTimestamp,
                sourcePico,
                serverPico);
        }
    }
}
