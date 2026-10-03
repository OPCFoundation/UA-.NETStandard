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
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Reads HTTP response bodies while enforcing the transport's configured message-size limit.
    /// </summary>
    internal static class HttpResponseBodyReader
    {
        /// <summary>
        /// Checks both the declared and actual response size and reports oversized bodies as BadResponseTooLarge.
        /// </summary>
        public static async ValueTask<byte[]> ReadAsync(
            HttpContent content,
            int maxMessageSize,
            CancellationToken cancellationToken)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (maxMessageSize > 0 && content.Headers.ContentLength > maxMessageSize)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadResponseTooLarge,
                    "Response body exceeds the configured MaxMessageSize ({0} bytes).",
                    maxMessageSize);
            }

            // OPC 10000-6 §7.4.5: a JSON body may be gzip compressed (RFC
            // 1952) and then carries Content-Encoding: gzip. The limit below
            // applies to the inflated body, so a small compressed response
            // cannot expand past MaxMessageSize.
            bool gzip = IsGzip(content);
            // The body stream belongs to the content, which the caller disposes
            // with the response; only the inflating wrapper is released here.
#if NET5_0_OR_GREATER
            Stream body = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
            Stream body = await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
            if (gzip)
            {
                using var inflated = new GZipStream(body, CompressionMode.Decompress, leaveOpen: true);
                return await ReadBoundedAsync(inflated, maxMessageSize, cancellationToken).ConfigureAwait(false);
            }
            return await ReadBoundedAsync(body, maxMessageSize, cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask<byte[]> ReadBoundedAsync(
            Stream stream,
            int maxMessageSize,
            CancellationToken cancellationToken)
        {
            try
            {
                // A Content-Length hint cannot truncate verification of the actual body.
                return await WebApiBodyCodec.ReadAllBoundedAsync(
                    stream, maxMessageSize, -1, cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadRequestTooLarge)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadResponseTooLarge,
                    exception,
                    "Response body exceeds the configured MaxMessageSize ({0} bytes).",
                    maxMessageSize);
            }
            catch (InvalidDataException exception)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    exception,
                    "The gzip compressed response body is corrupt.");
            }
        }

        /// <summary>
        /// Whether the body is gzip compressed. Any other content coding
        /// than gzip (or identity) is not negotiated by the stack and is
        /// rejected rather than decoded as if it were plain.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The body uses an unsupported content coding.
        /// </exception>
        private static bool IsGzip(HttpContent content)
        {
            bool gzip = false;
            foreach (string coding in content.Headers.ContentEncoding)
            {
                if (string.Equals(coding, "gzip", StringComparison.OrdinalIgnoreCase) && !gzip)
                {
                    gzip = true;
                    continue;
                }
                if (!string.Equals(coding, "identity", StringComparison.OrdinalIgnoreCase))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unsupported Content-Encoding '{0}' of the response body.",
                        coding);
                }
            }
            return gzip;
        }
    }
}
