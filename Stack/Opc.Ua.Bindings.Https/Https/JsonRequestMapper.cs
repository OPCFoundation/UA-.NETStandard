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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Helper that encodes <see cref="IServiceRequest"/> / <see cref="IServiceResponse"/>
    /// payloads in the OPC UA JSON wire format used by the HTTPS-JSON
    /// (<c>application/opcua+uajson</c>, OPC UA Part 6 §7.4.5) and WSS
    /// <c>opcua+uajson</c> (Part 6 §7.5.2) transport profiles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The JSON message envelope is the standard <c>JsonEncoder.EncodeMessage</c>
    /// shape: a top-level object containing <c>TypeId</c> and <c>Body</c>
    /// properties identifying the request/response. The compact
    /// (reversible) encoding flavour is mandatory per Part 6 §5.4.9 — the
    /// mapper enforces it via <see cref="JsonEncoderOptions.Compact"/>.
    /// </para>
    /// <para>
    /// Neither sub-protocol carries a UA Secure Conversation layer (per
    /// Part 6 §7.4.5 and §7.5.2.2), so the mapper is plain encode/decode
    /// against the wire stream; transport security is provided exclusively
    /// by the surrounding TLS connection. Callers are responsible for
    /// rejecting any non-<see cref="MessageSecurityMode.None"/> endpoint
    /// configuration before invoking the mapper.
    /// </para>
    /// </remarks>
    internal static class JsonRequestMapper
    {
        /// <summary>
        /// Decodes a single OPC UA service request from a JSON message body.
        /// </summary>
        /// <param name="body">
        /// The request body. May be a network stream; the entire payload up
        /// to end-of-stream is consumed.
        /// </param>
        /// <param name="context">
        /// The encoding context (namespace / server tables, quotas, telemetry).
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The decoded service request.</returns>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadDecodingError"/> if the body
        /// is malformed or not a recognized OPC UA JSON service request.
        /// </exception>
        public static async ValueTask<IServiceRequest> DecodeRequestAsync(
            Stream body,
            IServiceMessageContext context,
            CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            byte[] payload = await ReadAllBoundedAsync(body, context.MaxMessageSize, ct)
                .ConfigureAwait(false);

            try
            {
                return JsonDecoder.DecodeMessage<IServiceRequest>(payload, context);
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
                    "Failed to decode OPC UA JSON service request body.");
            }
        }

        /// <summary>
        /// Reads the entire <paramref name="body"/> into a byte array, bounding
        /// the buffered size by <paramref name="maxLength"/> (the UA
        /// <c>MaxMessageSize</c> quota) so an oversized or chunked / no-Content-Length
        /// body cannot exhaust memory before the decoder enforces the quota.
        /// A non-positive <paramref name="maxLength"/> disables the cap.
        /// </summary>
        /// <param name="body">The request body stream.</param>
        /// <param name="maxLength">Maximum number of bytes to buffer.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadRequestTooLarge"/> when the body
        /// exceeds <paramref name="maxLength"/>.
        /// </exception>
        internal static async ValueTask<byte[]> ReadAllBoundedAsync(
            Stream body,
            int maxLength,
            CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

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
        /// Encodes a single OPC UA service response into JSON and writes the
        /// bytes to <paramref name="destination"/>.
        /// </summary>
        /// <param name="response">The service response to encode.</param>
        /// <param name="context">
        /// The encoding context (namespace / server tables, quotas).
        /// </param>
        /// <param name="destination">
        /// Destination stream the encoded JSON bytes are written to.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        public static async ValueTask EncodeResponseAsync(
            IServiceResponse response,
            IServiceMessageContext context,
            Stream destination,
            CancellationToken ct)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            byte[] encoded = EncodeResponse(response, context);
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            await destination.WriteAsync(
                encoded.AsMemory(0, encoded.Length),
                ct).ConfigureAwait(false);
#else
            await destination.WriteAsync(encoded, 0, encoded.Length, ct)
                .ConfigureAwait(false);
#endif
        }

        /// <summary>
        /// Encodes a single OPC UA service response into JSON and returns the
        /// bytes as a freshly-allocated array.
        /// </summary>
        /// <param name="response">The service response to encode.</param>
        /// <param name="context">
        /// The encoding context (namespace / server tables, quotas).
        /// </param>
        public static byte[] EncodeResponse(
            IServiceResponse response,
            IServiceMessageContext context)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using var memory = new MemoryStream();
            using (var encoder = new JsonEncoder(memory, context, JsonEncoderOptions.Compact))
            {
                encoder.EncodeMessage(response, response.TypeId);
                encoder.Close();
            }
            return memory.ToArray();
        }
    }
}
