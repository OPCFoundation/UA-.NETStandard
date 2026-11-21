// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Buffers;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Provides the fastest buffer manager variant without debug cookies.
    /// </summary>
    public sealed class FastBufferManager : ArrayPoolBufferManagerBase
    {
        /// <summary>
        /// Initializes the manager.
        /// </summary>
        /// <param name="name">The diagnostic name.</param>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        public FastBufferManager(string name, int maxBufferSize, ITelemetryContext telemetry)
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
        internal FastBufferManager(
            string name,
            int maxBufferSize,
            ITelemetryContext telemetry,
            ArrayPool<byte> arrayPool)
            : base(name, maxBufferSize, telemetry, arrayPool, kMetadataByteCount)
        {
        }

        private const int kMetadataByteCount = 0;
    }
}
