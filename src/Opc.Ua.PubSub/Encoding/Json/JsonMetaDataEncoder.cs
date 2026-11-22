// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Text.Json;

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Serialises a <see cref="DataSetMetaDataType"/> into a JSON
    /// property using the Stack <see cref="Ua.JsonEncoder"/> so the
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
        /// <exception cref="ArgumentNullException"></exception>
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
            using (Ua.JsonEncoder encoder = new(buffer, context, options))
            {
                encoder.WriteEncodeable("MetaData", metaData);
            }
            using var document = JsonDocument.Parse(buffer.WrittenMemory);
            JsonElement root = document.RootElement;
            writer.WritePropertyName(propertyName);
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("MetaData", out JsonElement valueElement))
            {
                writer.WriteNullValue();
                return;
            }
            if (valueElement.ValueKind is JsonValueKind.Null or
                JsonValueKind.Undefined)
            {
                writer.WriteNullValue();
                return;
            }
            writer.WriteRawValue(valueElement.GetRawText(), skipInputValidation: true);
        }
    }
}
