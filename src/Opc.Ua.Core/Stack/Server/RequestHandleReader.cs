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
using System.IO;
using System.Text.Json;

namespace Opc.Ua
{
    /// <summary>
    /// Reads the RequestHandle of a request message that could not be decoded,
    /// so the ServiceFault returned for it can echo the handle: OPC 10000-4
    /// §7.33 says the requestHandle in the ResponseHeader of a ServiceFault
    /// should be set to what was provided in the RequestHeader even if the
    /// request was not valid.
    /// </summary>
    /// <remarks>
    /// Every service request starts with its RequestHeader, whose first fields
    /// are authenticationToken (NodeId), timestamp (UtcTime) and requestHandle
    /// (IntegerId) (OPC 10000-4 §7.32). Only those leading fields are read, so a
    /// request that fails anywhere, for example on a string above MaxStringLength,
    /// still yields its handle. The methods are best effort
    /// and return 0 when the handle cannot be read.
    /// </remarks>
    internal static class RequestHandleReader
    {
        /// <summary>
        /// Reads the RequestHandle from a binary encoded request message: the
        /// encoding NodeId followed by the request body (OPC 10000-6 §5.2.9).
        /// </summary>
        /// <remarks>
        /// The NodeIds before the handle are skipped by their encoded length
        /// (OPC 10000-6 §5.2.2.9) rather than decoded, so a request that was
        /// rejected because its AuthenticationToken exceeds MaxStringLength or
        /// MaxByteStringLength still yields the handle.
        /// </remarks>
        /// <param name="message">The message body.</param>
        /// <returns>The RequestHandle, or 0 if it cannot be read.</returns>
        public static uint FromBinary(Stream? message)
        {
            if (message == null)
            {
                return 0;
            }

            try
            {
                return SkipNodeId(message) && // encoding id of the request
                    SkipNodeId(message) && // RequestHeader.AuthenticationToken
                    Skip(message, 8) && // RequestHeader.Timestamp
                    TryReadUInt32(message, out uint requestHandle)
                    ? requestHandle
                    : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <inheritdoc cref="FromBinary(Stream)"/>
        public static uint FromBinary(byte[]? message)
        {
            return message == null ? 0 : FromBinary(new ArraySegment<byte>(message));
        }

        /// <inheritdoc cref="FromBinary(Stream)"/>
        public static uint FromBinary(ArraySegment<byte> message)
        {
            if (message.Array == null)
            {
                return 0;
            }

            using var stream = new MemoryStream(message.Array, message.Offset, message.Count, writable: false);
            return FromBinary(stream);
        }

        /// <summary>
        /// Reads the RequestHandle from a JSON encoded request message, the
        /// <c>UaBody.RequestHeader.RequestHandle</c> member (OPC 10000-6 §5.4.1).
        /// Reading stops at the first malformed token.
        /// </summary>
        /// <param name="message">The UTF-8 encoded message.</param>
        /// <returns>The RequestHandle, or 0 if it cannot be read.</returns>
        public static uint FromJson(ReadOnlySpan<byte> message)
        {
            try
            {
                var reader = new Utf8JsonReader(message, new JsonReaderOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                // The object at each of the first three nesting levels:
                // kMessage, kBody, kRequestHeader or kOther.
                Span<int> scopes = stackalloc int[3];
                int depth = -1;
                int nextScope = kOther;

                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                        case JsonTokenType.StartArray:
                            depth++;
                            if (depth < scopes.Length)
                            {
                                scopes[depth] = reader.TokenType != JsonTokenType.StartObject
                                    ? kOther
                                    : depth == 0 ? kMessage : nextScope;
                            }
                            nextScope = kOther;
                            break;
                        case JsonTokenType.EndObject:
                        case JsonTokenType.EndArray:
                            depth--;
                            nextScope = kOther;
                            break;
                        case JsonTokenType.PropertyName:
                            nextScope = kOther;
                            if (depth < 0 || depth >= scopes.Length)
                            {
                                break;
                            }
                            if (scopes[depth] == kMessage && reader.ValueTextEquals("UaBody"))
                            {
                                nextScope = kBody;
                            }
                            else if (scopes[depth] == kBody && reader.ValueTextEquals("RequestHeader"))
                            {
                                nextScope = kRequestHeader;
                            }
                            else if (scopes[depth] == kRequestHeader &&
                                reader.ValueTextEquals("RequestHandle"))
                            {
                                return reader.Read() &&
                                    reader.TokenType == JsonTokenType.Number &&
                                    reader.TryGetUInt32(out uint handle)
                                    ? handle
                                    : 0;
                            }
                            break;
                        default:
                            nextScope = kOther;
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // malformed JSON before the handle
            }

            return 0;
        }

        /// <summary>
        /// Skips a NodeId (OPC 10000-6 §5.2.2.9). The ExpandedNodeId flags are
        /// not valid for a NodeId and end the read.
        /// </summary>
        private static bool SkipNodeId(Stream stream)
        {
            int encoding = stream.ReadByte();
            switch (encoding)
            {
                case 0x00: // two byte: identifier
                    return Skip(stream, 1);
                case 0x01: // four byte: namespace (byte), identifier (UInt16)
                    return Skip(stream, 3);
                case 0x02: // numeric: namespace (UInt16), identifier (UInt32)
                    return Skip(stream, 6);
                case 0x04: // guid: namespace (UInt16), identifier (Guid)
                    return Skip(stream, 18);
                case 0x03: // string: namespace (UInt16), identifier (String)
                case 0x05: // opaque: namespace (UInt16), identifier (ByteString)
                    if (!Skip(stream, 2) || !TryReadUInt32(stream, out uint length))
                    {
                        return false;
                    }
                    // -1 encodes a null string
                    return length == uint.MaxValue ||
                        (length <= int.MaxValue && Skip(stream, (int)length));
                default:
                    return false;
            }
        }

        private static bool Skip(Stream stream, int count)
        {
            if (stream.CanSeek)
            {
                if (stream.Length - stream.Position < count)
                {
                    return false;
                }
                stream.Seek(count, SeekOrigin.Current);
                return true;
            }

            byte[] scratch = new byte[Math.Min(count, 4096)];
            while (count > 0)
            {
                int read = stream.Read(scratch, 0, Math.Min(count, scratch.Length));
                if (read <= 0)
                {
                    return false;
                }
                count -= read;
            }
            return true;
        }

        private static bool TryReadUInt32(Stream stream, out uint value)
        {
            value = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                int next = stream.ReadByte();
                if (next < 0)
                {
                    return false;
                }
                value |= (uint)next << shift;
            }
            return true;
        }

        private const int kOther = 0;
        private const int kMessage = 1;
        private const int kBody = 2;
        private const int kRequestHeader = 3;
    }
}
