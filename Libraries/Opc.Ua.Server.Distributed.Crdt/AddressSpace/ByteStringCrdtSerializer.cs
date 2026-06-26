/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Crdt;

namespace Opc.Ua.Server.Distributed.Crdt
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
