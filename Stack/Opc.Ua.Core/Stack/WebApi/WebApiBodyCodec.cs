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
    /// <item><see cref="DecodeBodyAsync{T}(Stream, IServiceMessageContext, JsonDecoderOptions?, long, CancellationToken)"/>
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
        /// <param name="contentLengthHint">
        /// Optional total length of <paramref name="body"/> in bytes (e.g. the
        /// HTTP <c>Content-Length</c> header). When non-negative the buffer is
        /// pre-allocated to the exact size and the rent-and-grow loop is
        /// avoided. Pass <c>-1</c> when the size is unknown (chunked /
        /// streamed). Set to a value above
        /// <see cref="IServiceMessageContext.MaxMessageSize"/> to short-circuit
        /// oversized requests before any read is issued.
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
            long contentLengthHint = -1,
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

            // Bound the buffered size by MaxMessageSize so an oversized
            // or chunked / no-Content-Length body cannot exhaust memory
            // before the in-buffer decode call enforces the quota.
            // Throws BadRequestTooLarge the moment the quota is exceeded,
            // before allocating the full payload.
            byte[] payload = await ReadAllBoundedAsync(
                body, context.MaxMessageSize, contentLengthHint, ct)
                .ConfigureAwait(false);

            return DecodeBody<T>(payload, context, options);
        }

        // Mirrors JsonRequestMapper.ReadAllBoundedAsync: caps the buffered
        // body length at MaxMessageSize. A non-positive maxLength disables
        // the cap. When contentLengthHint >= 0 the read path skips the
        // ArrayPool rent-and-grow loop and reads directly into an exact-
        // sized buffer.
        internal static async ValueTask<byte[]> ReadAllBoundedAsync(
            Stream body,
            int maxLength,
            long contentLengthHint,
            CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            // Reject oversized requests before any I/O is issued.
            if (maxLength > 0 && contentLengthHint > maxLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadRequestTooLarge,
                    "Request body exceeds the configured MaxMessageSize ({0} bytes).",
                    maxLength);
            }

            // Fast path: Content-Length known and within budget — allocate
            // exact buffer and read directly. Avoids ArrayPool rental,
            // MemoryStream growth, and the final ToArray() copy.
            if (contentLengthHint >= 0)
            {
                if (contentLengthHint == 0)
                {
                    return Array.Empty<byte>();
                }
                var exact = new byte[contentLengthHint];
                int total = 0;
                while (total < exact.Length)
                {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    int n = await body
                        .ReadAsync(exact.AsMemory(total, exact.Length - total), ct)
                        .ConfigureAwait(false);
#else
                    int n = await body
                        .ReadAsync(exact, total, exact.Length - total, ct)
                        .ConfigureAwait(false);
#endif
                    if (n == 0)
                    {
                        break;
                    }
                    total += n;
                }
                if (total == exact.Length)
                {
                    return exact;
                }
                // Truncated body — return the actually-read prefix so callers
                // surface a decoding error rather than a length mismatch.
                var truncated = new byte[total];
                Buffer.BlockCopy(exact, 0, truncated, 0, total);
                return truncated;
            }

            // Unknown length: rent a working buffer and grow into a
            // MemoryStream, capping at MaxMessageSize as soon as the cap is
            // exceeded.
            using var buffer = new MemoryStream();
            byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                int read;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                while ((read = await body
                    .ReadAsync(rented.AsMemory(0, rented.Length), ct).ConfigureAwait(false)) > 0)
#else
                while ((read = await body
                    .ReadAsync(rented, 0, rented.Length, ct).ConfigureAwait(false)) > 0)
#endif
                {
                    if (maxLength > 0 && buffer.Length + read > maxLength)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadRequestTooLarge,
                            "Request body exceeds the configured MaxMessageSize ({0} bytes).",
                            maxLength);
                    }
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    await buffer.WriteAsync(rented.AsMemory(0, read), ct).ConfigureAwait(false);
#else
                    await buffer.WriteAsync(rented, 0, read, ct).ConfigureAwait(false);
#endif
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
            return buffer.ToArray();
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
        /// Non-generic counterpart of
        /// <see cref="DecodeBodyAsync{T}(Stream, IServiceMessageContext, JsonDecoderOptions?, long, CancellationToken)"/>
        /// that constructs the result via the parameterless constructor of
        /// <paramref name="bodyType"/>. Used by transport channels that
        /// dispatch on a runtime <see cref="Type"/> (e.g.
        /// <c>WebApiServiceRoute.ResponseType</c>) and cannot supply the
        /// type argument at compile time.
        /// </summary>
        /// <param name="bodyType">
        /// The concrete <see cref="IEncodeable"/> CLR type whose fields appear
        /// at the root of the JSON document. Must declare a public
        /// parameterless constructor.
        /// </param>
        /// <param name="body">The HTTP request / response body.</param>
        /// <param name="context">
        /// Encoding context (namespace / server tables, quotas, telemetry).
        /// </param>
        /// <param name="options">
        /// Optional decoder options. Defaults to a fresh
        /// <see cref="JsonDecoderOptions"/> instance.
        /// </param>
        /// <param name="contentLengthHint">
        /// Optional total length of <paramref name="body"/> in bytes (HTTP
        /// <c>Content-Length</c> header when present). When non-negative the
        /// buffer is pre-allocated to the exact size and the rent-and-grow
        /// loop is avoided. Pass <c>-1</c> when the size is unknown.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The decoded value as <see cref="IEncodeable"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="bodyType"/>, <paramref name="body"/>, or
        /// <paramref name="context"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="bodyType"/> does not implement
        /// <see cref="IEncodeable"/> or has no public parameterless
        /// constructor.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadDecodingError"/> on malformed
        /// JSON or decoding failure;
        /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/> when the
        /// payload exceeds <see cref="IServiceMessageContext.MaxMessageSize"/>.
        /// </exception>
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
            "Constructs an instance of bodyType via Activator.CreateInstance which is not " +
            "NativeAOT-safe when the type is not statically rooted. Callers that need AOT " +
            "should use the generic DecodeBodyAsync<T> overload instead.")]
        public static async ValueTask<IEncodeable> DecodeBodyAsync(
            Type bodyType,
            Stream body,
            IServiceMessageContext context,
            JsonDecoderOptions? options = null,
            long contentLengthHint = -1,
            CancellationToken ct = default)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // Bounded read enforces MaxMessageSize before allocation.
            byte[] payload = await ReadAllBoundedAsync(
                body, context.MaxMessageSize, contentLengthHint, ct)
                .ConfigureAwait(false);

            return DecodeBody(bodyType, payload, context, options);
        }

        /// <summary>
        /// Non-generic counterpart of
        /// <see cref="DecodeBody{T}(byte[], IServiceMessageContext, JsonDecoderOptions?)"/>
        /// that constructs the result via the parameterless constructor of
        /// <paramref name="bodyType"/>.
        /// </summary>
        /// <param name="bodyType">
        /// The concrete <see cref="IEncodeable"/> CLR type whose fields appear
        /// at the root of the JSON document. Must declare a public
        /// parameterless constructor.
        /// </param>
        /// <param name="payload">The UTF-8 encoded JSON body.</param>
        /// <param name="context">
        /// Encoding context (namespace / server tables, quotas, telemetry).
        /// </param>
        /// <param name="options">
        /// Optional decoder options.
        /// </param>
        /// <returns>The decoded value as <see cref="IEncodeable"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="bodyType"/>, <paramref name="payload"/>, or
        /// <paramref name="context"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="bodyType"/> does not implement
        /// <see cref="IEncodeable"/> or has no public parameterless
        /// constructor.
        /// </exception>
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
            "Constructs an instance of bodyType via Activator.CreateInstance which is not " +
            "NativeAOT-safe when the type is not statically rooted. Callers that need AOT " +
            "should use the generic DecodeBody<T> overload instead.")]
        public static IEncodeable DecodeBody(
            Type bodyType,
            byte[] payload,
            IServiceMessageContext context,
            JsonDecoderOptions? options = null)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return DecodeBody(bodyType, new ReadOnlySequence<byte>(payload), context, options);
        }

        /// <summary>
        /// Non-generic counterpart of
        /// <see cref="DecodeBody{T}(ReadOnlySequence{byte}, IServiceMessageContext, JsonDecoderOptions?)"/>
        /// that constructs the result via the parameterless constructor of
        /// <paramref name="bodyType"/>.
        /// </summary>
        /// <param name="bodyType">
        /// The concrete <see cref="IEncodeable"/> CLR type whose fields appear
        /// at the root of the JSON document. Must declare a public
        /// parameterless constructor.
        /// </param>
        /// <param name="payload">The UTF-8 encoded JSON body.</param>
        /// <param name="context">
        /// Encoding context (namespace / server tables, quotas, telemetry).
        /// </param>
        /// <param name="options">Optional decoder options.</param>
        /// <returns>The decoded value as <see cref="IEncodeable"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="bodyType"/> or <paramref name="context"/> is
        /// <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="bodyType"/> does not implement
        /// <see cref="IEncodeable"/> or has no public parameterless
        /// constructor.
        /// </exception>
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
            "Constructs an instance of bodyType via Activator.CreateInstance which is not " +
            "NativeAOT-safe when the type is not statically rooted. Callers that need AOT " +
            "should use the generic DecodeBody<T> overload instead.")]
        public static IEncodeable DecodeBody(
            Type bodyType,
            ReadOnlySequence<byte> payload,
            IServiceMessageContext context,
            JsonDecoderOptions? options = null)
        {
            if (bodyType == null)
            {
                throw new ArgumentNullException(nameof(bodyType));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (!typeof(IEncodeable).IsAssignableFrom(bodyType))
            {
                throw new ArgumentException(
                    "Body type must implement IEncodeable.",
                    nameof(bodyType));
            }

            if (context.MaxMessageSize > 0 && context.MaxMessageSize < payload.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxMessageSize {0} < {1}",
                    context.MaxMessageSize,
                    payload.Length);
            }

            IEncodeable value;
            try
            {
                value = (IEncodeable)Activator.CreateInstance(bodyType)!;
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    "Body type must declare a public parameterless constructor.",
                    nameof(bodyType),
                    ex);
            }

            try
            {
                using var decoder = new JsonDecoder(payload, context, options);
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
                    bodyType.Name);
            }
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
