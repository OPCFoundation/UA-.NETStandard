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
using System.Buffers;
using System.Text.Json;

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Serialises a <see cref="DataSetMetaDataType"/> into a JSON
    /// property using the Stack <see cref="Opc.Ua.JsonEncoder"/> so the
    /// structural type definition, configuration version, namespaces
    /// and structure definitions follow the canonical Part 6 mapping.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.5">
    /// Part 14 §7.2.5.5</see>. Encoding mode selection mirrors
    /// <see cref="JsonVariantEncoder.ToEncoderOptions"/>.
    /// </remarks>
    internal static class JsonMetaDataEncoder
    {
        /// <summary>
        /// Writes the supplied <see cref="DataSetMetaDataType"/> as a
        /// JSON property on the destination writer.
        /// </summary>
        /// <param name="writer">Destination writer.</param>
        /// <param name="propertyName">Property name to emit.</param>
        /// <param name="metaData">Metadata payload.</param>
        /// <param name="mode">Encoding mode.</param>
        /// <param name="context">Stack message context.</param>
        public static void WriteMetaData(
            Utf8JsonWriter writer,
            string propertyName,
            DataSetMetaDataType metaData,
            JsonEncodingMode mode,
            IServiceMessageContext context)
        {
            if (writer is null)
            {
                throw new ArgumentNullException(nameof(writer));
            }
            if (propertyName is null)
            {
                throw new ArgumentNullException(nameof(propertyName));
            }
            if (metaData is null)
            {
                throw new ArgumentNullException(nameof(metaData));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            JsonEncoderOptions options = JsonVariantEncoder.ToEncoderOptions(mode);
            using JsonBufferWriter buffer = new(1024);
            using (Opc.Ua.JsonEncoder encoder = new(buffer, context, options))
            {
                encoder.WriteEncodeable<DataSetMetaDataType>("MetaData", metaData);
            }
            using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
            JsonElement root = document.RootElement;
            writer.WritePropertyName(propertyName);
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("MetaData", out JsonElement valueElement))
            {
                writer.WriteNullValue();
                return;
            }
            if (valueElement.ValueKind == JsonValueKind.Null
                || valueElement.ValueKind == JsonValueKind.Undefined)
            {
                writer.WriteNullValue();
                return;
            }
            writer.WriteRawValue(valueElement.GetRawText(), skipInputValidation: true);
        }
    }
}
