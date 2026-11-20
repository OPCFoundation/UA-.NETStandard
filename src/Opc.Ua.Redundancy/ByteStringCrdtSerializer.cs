// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Text.Json;
using Crdt;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// CRDT value serializer for <see cref="ByteString"/> payloads (serialized
    /// node states and encoded values). A leading var-uint marker carries
    /// <c>0</c> for a null <see cref="ByteString"/> and <c>length + 1</c>
    /// otherwise, so null and empty are distinguished.
    /// </summary>
    internal sealed class ByteStringCrdtSerializer : ICrdtValueSerializer<ByteString>
    {
        /// <summary>
        /// The shared serializer instance.
        /// </summary>
        public static ByteStringCrdtSerializer Instance { get; } = new();

        /// <inheritdoc/>
        public void Write(ref CrdtWriter writer, ByteString value)
        {
            if (value.IsNull)
            {
                writer.WriteVarUInt32(0);
                return;
            }

            byte[] bytes = value.ToArray();
            writer.WriteVarUInt32((uint)bytes.Length + 1);
            writer.WriteRaw(bytes);
        }

        /// <inheritdoc/>
        public ByteString Read(ref CrdtReader reader)
        {
            uint marker = reader.ReadVarUInt32();
            if (marker == 0)
            {
                return default;
            }

            ReadOnlySpan<byte> raw = reader.ReadRaw((int)(marker - 1));
            return new ByteString(raw.ToArray());
        }

        /// <inheritdoc/>
        public void WriteJson(Utf8JsonWriter writer, ByteString value)
        {
            if (value.IsNull)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteBase64StringValue(value.ToArray());
        }

        /// <inheritdoc/>
        public ByteString ReadJson(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return default;
            }

            return new ByteString(reader.GetBytesFromBase64());
        }
    }
}
