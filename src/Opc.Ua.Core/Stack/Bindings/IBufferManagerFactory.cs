// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Creates buffer managers for transport and codec components.
    /// </summary>
    public interface IBufferManagerFactory
    {
        /// <summary>
        /// Creates a buffer manager instance.
        /// </summary>
        /// <param name="name">The logical name used for diagnostics.</param>
        /// <param name="maxBufferSize">The maximum payload size supported by the manager.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        /// <returns>The created buffer manager.</returns>
        IBufferManager Create(string name, int maxBufferSize, ITelemetryContext telemetry);
    }
}
