// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Serialization
{
    /// <summary>
    /// Reads capture-source aliases and writes their canonical wire names.
    /// </summary>
    public sealed class CaptureSourceKindJsonConverter : JsonConverter<CaptureSourceKind>
    {
        /// <inheritdoc/>
        public override CaptureSourceKind Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                reader.GetString().TryParse(out CaptureSourceKind kind))
            {
                return kind;
            }

            if (reader.TokenType == JsonTokenType.Number &&
                reader.TryGetInt32(out int numericValue))
            {
                var numericKind = (CaptureSourceKind)numericValue;
                if (Enum.IsDefined(numericKind))
                {
                    return numericKind;
                }
            }

            throw new JsonException(
                $"Unsupported capture source. Use {CaptureSourceKindExtensions.SupportedNames}.");
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            CaptureSourceKind value,
            JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (!Enum.IsDefined(value))
            {
                throw new JsonException($"Unsupported capture source value '{value}'.");
            }

            writer.WriteStringValue(value.ToWireName());
        }
    }
}
