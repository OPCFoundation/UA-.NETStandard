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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Envelope-less OPC UA JSON encode / decode helper for the HTTPS REST
    /// binding defined by OPC UA Part 6 §G.3 "OpenAPI Mapping" (v1.05.07).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shared <see cref="JsonEncoder.EncodeMessage{T}(T,ExpandedNodeId)"/>
    /// / <see cref="JsonDecoder.DecodeMessage{T}()"/> pair always wraps the
    /// payload in the <c>{UaTypeId, UaBody}</c> envelope used by the
    /// HTTPS-JSON (Part 6 §7.4.5) and WSS <c>opcua+uajson</c> (§7.5.2)
    /// sub-profiles. The REST binding routes the service identity through
    /// the URL path instead, so the body is the bare
    /// <c>&lt;Service&gt;Request</c> / <c>&lt;Service&gt;Response</c>
    /// object — no envelope at the HTTPS layer.
    /// </para>
    /// <para>
    /// This codec exposes the same primitives without the envelope:
    /// <list type="bullet">
    /// <item><see cref="DecodeBodyAsync{T}(Stream, IServiceMessageContext, JsonDecoderOptions?, CancellationToken)"/>
    /// constructs <c>T</c> via its parameterless constructor
    /// and reads its fields directly from the JSON root object using
    /// <see cref="JsonDecoder"/>.</item>
    /// <item><see cref="EncodeBody{T}(T, IServiceMessageContext, JsonEncoderOptions?)"/>
    /// / <see cref="EncodeBodyAsync{T}(T, Stream, IServiceMessageContext, JsonEncoderOptions?, CancellationToken)"/>
    /// open the root JSON object (via the <see cref="JsonEncoder"/>
    /// constructor), invoke <see cref="IEncodeable.Encode(IEncoder)"/>
    /// directly, then close the object. The resulting payload is a single
    /// top-level <c>{...}</c> with the response's fields and no envelope.</item>
    /// </list>
    /// </para>
    /// <para>
    /// The codec is encoder-options-agnostic: callers pick Compact
    /// (mandatory default, Part 6 §5.4.9) or Verbose via the encoder /
    /// decoder options argument. See
    /// <see cref="WebApiMediaType.ToEncoderOptions(WebApiEncoding)"/>
    /// for the standard mapping.
    /// </para>
    /// <para>
    /// The codec does not authenticate, dispatch, or transform requests —
    /// it owns only the wire-format step. Caller responsibilities:
    /// content-type negotiation (<see cref="WebApiMediaType"/>), routing
    /// the URL path to the right concrete CLR type, and
    /// flowing the decoded request through the existing UA server
    /// dispatcher (<c>ITransportListenerCallback.ProcessRequestAsync</c>).
    /// </para>
    /// </remarks>
    public static class WebApiBodyCodec
    {
        /// <summary>
        /// Reads the supplied <paramref name="body"/> stream into a buffer
        /// (bounded by <see cref="IServiceMessageContext.MaxMessageSize"/>),
        /// then decodes a single envelope-less OPC UA JSON object as
        /// <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">
        /// The concrete CLR request / response type whose fields appear at
        /// the root of the JSON document.
        /// </typeparam>
        /// <param name="body">
        /// The HTTP request / response body. Read in full up to end-of-stream.
        /// </param>
        /// <param name="context">
        /// Encoding context (namespace / server tables, quotas, telemetry).
        /// </param>
        /// <param name="options">
        /// Optional decoder options. Defaults to a fresh
        /// <see cref="JsonDecoderOptions"/> instance.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="body"/> or <paramref name="context"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadDecodingError"/> if the body
        /// is malformed JSON or cannot be decoded as <typeparamref name="T"/>;
        /// with <see cref="StatusCodes.BadEncodingLimitsExceeded"/> if the
        /// payload exceeds <see cref="IServiceMessageContext.MaxMessageSize"/>.
        /// </exception>
        public static async ValueTask<T> DecodeBodyAsync<T>(
            Stream body,
            IServiceMessageContext context,
            JsonDecoderOptions? options = null,
            CancellationToken ct = default) where T : IEncodeable, new()
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            byte[] payload;
            using (var buffer = new MemoryStream())
            {
                await body.CopyToAsync(buffer, 81920, ct).ConfigureAwait(false);
                payload = buffer.ToArray();
            }

            return DecodeBody<T>(payload, context, options);
        }

        /// <summary>
        /// Decodes a single envelope-less OPC UA JSON object as
        /// <typeparamref name="T"/> from an in-memory buffer.
        /// </summary>
        /// <typeparam name="T">
        /// The concrete CLR request / response type whose fields appear at
        /// the root of the JSON document.
        /// </typeparam>
        /// <param name="payload">The UTF-8 encoded JSON body.</param>
        /// <param name="context">
        /// Encoding context (namespace / server tables, quotas, telemetry).
        /// </param>
        /// <param name="options">
        /// Optional decoder options. Defaults to a fresh
        /// <see cref="JsonDecoderOptions"/> instance.
        /// </param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadDecodingError"/> on malformed
        /// JSON or decoding failure;
        /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/> when the
        /// payload exceeds <see cref="IServiceMessageContext.MaxMessageSize"/>.
        /// </exception>
        public static T DecodeBody<T>(
            ReadOnlySequence<byte> payload,
            IServiceMessageContext context,
            JsonDecoderOptions? options = null) where T : IEncodeable, new()
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.MaxMessageSize > 0 && context.MaxMessageSize < payload.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxMessageSize {0} < {1}",
                    context.MaxMessageSize,
                    payload.Length);
            }

            try
            {
                using var decoder = new JsonDecoder(payload, context, options);
                var value = new T();
                value.Decode(decoder);
                return value;
            }
            catch (ServiceResultException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    ex,
                    "Failed to decode OPC UA REST body as {0}.",
                    typeof(T).Name);
            }
        }

        /// <summary>
        /// Decodes a single envelope-less OPC UA JSON object as
        /// <typeparamref name="T"/> from an in-memory byte array.
        /// </summary>
        /// <typeparam name="T">
        /// The concrete CLR request / response type whose fields appear at
        /// the root of the JSON document.
        /// </typeparam>
        /// <param name="payload">The UTF-8 encoded JSON body.</param>
        /// <param name="context">
        /// Encoding context (namespace / server tables, quotas, telemetry).
        /// </param>
        /// <param name="options">
        /// Optional decoder options. Defaults to a fresh
        /// <see cref="JsonDecoderOptions"/> instance.
        /// </param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="payload"/> or <paramref name="context"/> is
        /// <c>null</c>.
        /// </exception>
        public static T DecodeBody<T>(
            byte[] payload,
            IServiceMessageContext context,
            JsonDecoderOptions? options = null) where T : IEncodeable, new()
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return DecodeBody<T>(new ReadOnlySequence<byte>(payload), context, options);
        }

        /// <summary>
        /// Encodes <paramref name="value"/> as a single envelope-less OPC UA
        /// JSON object and returns the bytes as a freshly-allocated array.
        /// </summary>
        /// <typeparam name="T">
        /// The concrete CLR response / request type whose fields are written
        /// at the root of the JSON document.
        /// </typeparam>
        /// <param name="value">The value to encode.</param>
        /// <param name="context">
        /// Encoding context (namespace / server tables, quotas, telemetry).
        /// </param>
        /// <param name="options">
        /// Optional encoder options. Defaults to
        /// <see cref="JsonEncoderOptions.Verbose"/> (matching the underlying
        /// <see cref="JsonEncoder"/> default); REST callers should pass
        /// <see cref="JsonEncoderOptions.Compact"/> for spec-default output.
        /// </param>
        /// <returns>The encoded UTF-8 JSON bytes.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/> or <paramref name="context"/> is
        /// <c>null</c>.
        /// </exception>
        public static byte[] EncodeBody<T>(
            T value,
            IServiceMessageContext context,
            JsonEncoderOptions? options = null) where T : IEncodeable
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using var memory = new MemoryStream();
            using (var encoder = new JsonEncoder(memory, context, options))
            {
                value.Encode(encoder);
                encoder.Close();
            }
            return memory.ToArray();
        }

        /// <summary>
        /// Encodes <paramref name="value"/> as a single envelope-less OPC UA
        /// JSON object directly into the supplied <paramref name="destination"/>
        /// stream.
        /// </summary>
        /// <typeparam name="T">
        /// The concrete CLR response / request type whose fields are written
        /// at the root of the JSON document.
        /// </typeparam>
        /// <param name="value">The value to encode.</param>
        /// <param name="destination">The destination stream.</param>
        /// <param name="context">
        /// Encoding context (namespace / server tables, quotas, telemetry).
        /// </param>
        /// <param name="options">
        /// Optional encoder options. Defaults to
        /// <see cref="JsonEncoderOptions.Verbose"/> (matching the underlying
        /// <see cref="JsonEncoder"/> default); REST callers should pass
        /// <see cref="JsonEncoderOptions.Compact"/> for spec-default output.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/>, <paramref name="destination"/>, or
        /// <paramref name="context"/> is <c>null</c>.
        /// </exception>
        public static async ValueTask EncodeBodyAsync<T>(
            T value,
            Stream destination,
            IServiceMessageContext context,
            JsonEncoderOptions? options = null,
            CancellationToken ct = default) where T : IEncodeable
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            byte[] payload = EncodeBody(value, context, options);
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            await destination.WriteAsync(payload.AsMemory(0, payload.Length), ct)
                .ConfigureAwait(false);
#else
            await destination.WriteAsync(payload, 0, payload.Length, ct)
                .ConfigureAwait(false);
#endif
        }

        /// <summary>
        /// Encodes <paramref name="value"/> as a single envelope-less OPC UA
        /// JSON object directly into the supplied
        /// <paramref name="destination"/> buffer writer.
        /// </summary>
        /// <typeparam name="T">
        /// The concrete CLR response / request type whose fields are written
        /// at the root of the JSON document.
        /// </typeparam>
        /// <param name="value">The value to encode.</param>
        /// <param name="destination">The destination buffer writer.</param>
        /// <param name="context">
        /// Encoding context (namespace / server tables, quotas, telemetry).
        /// </param>
        /// <param name="options">
        /// Optional encoder options. Defaults to
        /// <see cref="JsonEncoderOptions.Verbose"/> (matching the underlying
        /// <see cref="JsonEncoder"/> default); REST callers should pass
        /// <see cref="JsonEncoderOptions.Compact"/> for spec-default output.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/>, <paramref name="destination"/>, or
        /// <paramref name="context"/> is <c>null</c>.
        /// </exception>
        public static void EncodeBody<T>(
            T value,
            IBufferWriter<byte> destination,
            IServiceMessageContext context,
            JsonEncoderOptions? options = null) where T : IEncodeable
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using var encoder = new JsonEncoder(destination, context, options);
            value.Encode(encoder);
            encoder.Close();
        }
    }
}
