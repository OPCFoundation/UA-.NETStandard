/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// A certificate store provider that creates
    /// <see cref="DirectoryCertificateStore"/> instances backed by the
    /// file system.
    /// </summary>
    public sealed class DirectoryStoreProvider : ICertificateStoreProvider
    {
        /// <inheritdoc/>
        public string StoreTypeName => CertificateStoreType.Directory;

        /// <inheritdoc/>
        public bool SupportsStorePath(string storePath)
        {
            return !string.IsNullOrEmpty(storePath) &&
                !storePath.StartsWith(
                    "X509Store:",
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public ICertificateStore CreateStore(ITelemetryContext telemetry)
        {
            return new DirectoryCertificateStore(telemetry);
        }
    }
}
