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
    /// request that fails later, for example on a string argument above
    /// MaxStringLength, still yields its handle. The methods are best effort
    /// and return 0 when the handle cannot be read.
    /// </remarks>
    internal static class RequestHandleReader
    {
        /// <summary>
        /// Reads the RequestHandle from a binary encoded request message: the
        /// encoding NodeId followed by the request body (OPC 10000-6 §5.2.9).
        /// </summary>
        /// <param name="message">The message body.</param>
        /// <param name="context">The decoding context.</param>
        /// <returns>The RequestHandle, or 0 if it cannot be read.</returns>
        public static uint FromBinary(Stream? message, IServiceMessageContext context)
        {
            if (message == null)
            {
                return 0;
            }

            try
            {
                using var decoder = new BinaryDecoder(message, context);
                return ReadBinaryRequestHandle(decoder);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <inheritdoc cref="FromBinary(Stream, IServiceMessageContext)"/>
        public static uint FromBinary(byte[]? message, IServiceMessageContext context)
        {
            if (message == null)
            {
                return 0;
            }

            try
            {
                using var decoder = new BinaryDecoder(message, context);
                return ReadBinaryRequestHandle(decoder);
            }
            catch (Exception)
            {
                return 0;
            }
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

        private static uint ReadBinaryRequestHandle(BinaryDecoder decoder)
        {
            _ = decoder.ReadNodeId(null); // encoding id of the request
            _ = decoder.ReadNodeId(null); // RequestHeader.AuthenticationToken
            _ = decoder.ReadDateTime(null); // RequestHeader.Timestamp
            return decoder.ReadUInt32(null); // RequestHeader.RequestHandle
        }

        private const int kOther = 0;
        private const int kMessage = 1;
        private const int kBody = 2;
        private const int kRequestHeader = 3;
    }
}
