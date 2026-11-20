/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Supports implementation for custom certificate store type.
    /// </summary>
    public interface ICertificateStoreType
    {
        /// <summary>
        /// Determines if the store path is supported by the store type.
        /// </summary>
        /// <param name="storePath">The store path to examine.</param>
        /// <returns><see langword="true"/> if the store type supports the given store path, otherwise <see langword="false"/>.</returns>
        bool SupportsStorePath(string storePath);

        /// <summary>
        /// Creates a new certificate store.
        /// </summary>
        /// <param name="telemetry">Current telemtry context</param>
        /// <returns>A reference to the new certificate store object</returns>
        ICertificateStore CreateStore(ITelemetryContext telemetry);
    }
}
