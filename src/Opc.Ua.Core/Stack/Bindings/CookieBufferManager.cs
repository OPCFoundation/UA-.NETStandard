/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
