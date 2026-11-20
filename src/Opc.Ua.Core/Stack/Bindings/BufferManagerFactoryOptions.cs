/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Selects the concrete buffer manager implementation.
    /// </summary>
    public enum BufferManagerImplementationKind
    {
        /// <summary>
        /// Uses the build-specific default implementation.
        /// </summary>
        Auto,

        /// <summary>
        /// Uses the fast implementation without buffer cookies.
        /// </summary>
        Fast,

        /// <summary>
        /// Uses the cookie-based implementation with one trailing cookie byte.
        /// </summary>
        Cookie,

        /// <summary>
        /// Uses the tracing implementation with cookie and allocation diagnostics.
        /// </summary>
        MemoryTracing
    }

    /// <summary>
    /// Configures <see cref="DefaultBufferManagerFactory"/> behavior.
    /// </summary>
    public sealed class BufferManagerFactoryOptions
    {
        /// <summary>
        /// Gets or sets the preferred implementation kind.
        /// </summary>
        public BufferManagerImplementationKind ImplementationKind { get; set; } =
            BufferManagerImplementationKind.Auto;

        /// <summary>
        /// Gets or sets the shared process-wide outstanding byte budget.
        /// </summary>
        /// <remarks>
        /// A value greater than zero enables <see cref="Bindings.LimitingBufferManager"/> wrapping.
        /// </remarks>
        public long? MaxOutstandingBytesPerProcess { get; set; }
    }
}
