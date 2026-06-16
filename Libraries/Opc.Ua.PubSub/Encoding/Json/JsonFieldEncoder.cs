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
        public static void EncodeFields(
            Utf8JsonWriter writer,
            IReadOnlyList<DataSetField> fields,
            DataSetMetaDataType? metaData,
            JsonEncodingMode mode,
            IServiceMessageContext context)
        {
            if (writer is null)
            {
                throw new ArgumentNullException(nameof(writer));
            }
            if (fields is null)
            {
                throw new ArgumentNullException(nameof(fields));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            writer.WritePropertyName("Payload");
            writer.WriteStartObject();
            for (int i = 0; i < fields.Count; i++)
            {
                DataSetField field = fields[i];
                string name = ResolveFieldName(field, metaData, i);
                WriteOneField(writer, name, field, mode, context);
            }
            writer.WriteEndObject();
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
        private static void WriteOneField(
            Utf8JsonWriter writer,
            string propertyName,
            DataSetField field,
            JsonEncodingMode mode,
            IServiceMessageContext context)
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
                    DataValue dv = new(
                        field.Value,
                        field.StatusCode,
                        field.SourceTimestamp,
                        DateTimeUtc.MinValue);
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
    }
}
