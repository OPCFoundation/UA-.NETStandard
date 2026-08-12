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
