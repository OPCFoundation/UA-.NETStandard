// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Serialization
{
    /// <summary>
    /// Reads output-format aliases and writes their canonical wire names.
    /// </summary>
    public sealed class FormatKindJsonConverter : JsonConverter<FormatKind>
    {
        /// <inheritdoc/>
        public override FormatKind Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                reader.GetString().TryParse(out FormatKind kind))
            {
                return kind;
            }

            if (reader.TokenType == JsonTokenType.Number &&
                reader.TryGetInt32(out int numericValue))
            {
                var numericKind = (FormatKind)numericValue;
                if (Enum.IsDefined(numericKind))
                {
                    return numericKind;
                }
            }

            throw new JsonException(
                $"Unsupported output format. Use {FormatKindExtensions.SupportedNames}.");
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            FormatKind value,
            JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (!Enum.IsDefined(value))
            {
                throw new JsonException($"Unsupported output format value '{value}'.");
            }

            writer.WriteStringValue(value.ToWireName());
        }
    }
}
