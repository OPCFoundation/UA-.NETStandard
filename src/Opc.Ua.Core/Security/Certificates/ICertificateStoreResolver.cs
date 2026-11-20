/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Optional capability for opening certificate stores through instance-scoped providers.
    /// </summary>
    public interface ICertificateStoreResolver
    {
        /// <summary>
        /// Opens a configured store without substituting another store when opening fails.
        /// </summary>
        /// <param name="storePath">The configured store path.</param>
        /// <param name="storeType">The explicit store type, or null to resolve it from the path.</param>
        /// <param name="noPrivateKeys">Whether private-key access is disabled.</param>
        /// <returns>An opened store owned and disposed by the caller.</returns>
        ICertificateStore OpenCertificateStore(
            string storePath,
            string? storeType = null,
            bool noPrivateKeys = true);
    }
}
