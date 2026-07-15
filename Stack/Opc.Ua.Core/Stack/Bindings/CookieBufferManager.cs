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
 *
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

using System.Buffers;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Provides buffer cookie validation without allocation tracing.
    /// </summary>
    public sealed class CookieBufferManager : ArrayPoolBufferManagerBase
    {
        /// <summary>
        /// Initializes the manager.
        /// </summary>
        /// <param name="name">The diagnostic name.</param>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        public CookieBufferManager(string name, int maxBufferSize, ITelemetryContext telemetry)
            : base(name, maxBufferSize, telemetry, kMetadataByteCount)
        {
        }

        /// <summary>
        /// Initializes the manager with a caller-supplied pool.
        /// </summary>
        /// <param name="name">The diagnostic name.</param>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        /// <param name="arrayPool">The array pool to use.</param>
        internal CookieBufferManager(
            string name,
            int maxBufferSize,
            ITelemetryContext telemetry,
            ArrayPool<byte> arrayPool)
            : base(name, maxBufferSize, telemetry, arrayPool, kMetadataByteCount)
        {
        }

        /// <inheritdoc/>
        public override void Lock(byte[] buffer)
        {
            BufferCookie.Lock(buffer);
        }

        /// <inheritdoc/>
        public override void Unlock(byte[] buffer)
        {
            BufferCookie.Unlock(buffer);
        }

        /// <inheritdoc/>
        protected override void OnBufferTaken(byte[] buffer, string owner)
        {
            BufferCookie.Initialize(buffer);
        }

        /// <inheritdoc/>
        protected override void OnBufferReturning(byte[] buffer, string owner)
        {
            BufferCookie.ValidateAndDestroy(buffer);
        }

        private const int kMetadataByteCount = 1;
    }
}
