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

namespace Opc.Ua.XRegistry.Http
{
    /// <summary>
    /// Immutable HTTP transport limits and deployment qualification.
    /// </summary>
    public sealed record XRegistryHttpOptions
    {
        /// <summary>
        /// Gets whether an explicit localhost or loopback-IP registry root may use HTTP instead of HTTPS.
        /// The default is <see langword="false"/>; remote plaintext HTTP is never permitted.
        /// </summary>
        public bool AllowLoopbackHttp { get; init; }

        /// <summary>
        /// Attests that the selected backend has been qualified against the pinned
        /// xRegistry 1.0-rc4 HTTP binding, including atomic failure and write touch.
        /// A version string alone is not qualification. Does not enable replay.
        /// The default is <see langword="false"/>.
        /// </summary>
        public bool IsQualifiedBinding { get; init; }

        /// <summary>
        /// Gets the maximum size, in bytes, of each encoded or decoded HTTP body.
        /// Incoming bodies are bounded before and after each decompression step.
        /// Must be positive; the default is 33,554,432 bytes (32 MiB).
        /// </summary>
        /// <remarks>
        /// Hosted error responses may use a small independent budget to preserve rejection details.
        /// </remarks>
        public int MaximumBodyBytes { get; init; } = 33_554_432;

        /// <summary>
        /// Gets the maximum nesting depth used when parsing or encoding transported JSON.
        /// Must be between 1 and 1,024 inclusive; the default is 64.
        /// </summary>
        public int MaximumJsonDepth { get; init; } = 64;

        /// <summary>
        /// Gets the maximum aggregate UTF-8 size of headers checked by the transport.
        /// Each name/value pair includes four bytes of overhead in addition to its name and value.
        /// Must be positive; the default is 65,536 bytes (64 KiB).
        /// </summary>
        public int MaximumHeaderBytes { get; init; } = 65_536;

        /// <summary>
        /// Gets the limit on header name/value pairs checked by the transport for one request or response.
        /// Repeated values count separately. Must be positive; the default is 128.
        /// </summary>
        public int MaximumHeaders { get; init; } = 128;

        /// <summary>
        /// Gets the maximum number of query parameters, including repeated names and valueless flags.
        /// Must be positive; the default is 128.
        /// </summary>
        public int MaximumParameters { get; init; } = 128;

        /// <summary>
        /// Gets the maximum number of characters in registry URIs and request targets, including the query.
        /// Must be positive; the default is 16,384.
        /// </summary>
        public int MaximumUriLength { get; init; } = 16_384;

        /// <summary>
        /// Gets the timeout for a client operation or the hosted inspection and execution phase, including body reads.
        /// The host applies a separate timeout of the same duration to response writing. The default is 30 seconds.
        /// </summary>
        /// <remarks>
        /// Must be positive and no greater than <see cref="int.MaxValue"/> milliseconds.
        /// Caller cancellation and a shorter HttpClient timeout still apply.
        /// </remarks>
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets the optional telemetry context used to create the hosted route's request and backend-failure logger.
        /// The default is <see langword="null"/>.
        /// </summary>
        public ITelemetryContext? Telemetry { get; init; }

        internal void Validate()
        {
            if (MaximumBodyBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBodyBytes));
            }
            if (MaximumJsonDepth is < 1 or > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumJsonDepth));
            }
            if (MaximumHeaderBytes <= 0 || MaximumHeaders <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumHeaderBytes));
            }
            if (MaximumParameters <= 0 || MaximumUriLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumParameters));
            }
            if (RequestTimeout <= TimeSpan.Zero || RequestTimeout.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
            }
        }
    }
}
