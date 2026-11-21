// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// A certificate store provider that creates
    /// <see cref="X509CertificateStore"/> instances backed by the
    /// platform X.509 certificate store.
    /// </summary>
    public sealed class X509StoreProvider : ICertificateStoreProvider
    {
        /// <inheritdoc/>
        public string StoreTypeName => CertificateStoreType.X509Store;

        /// <inheritdoc/>
        public bool SupportsStorePath(string storePath)
        {
            return !string.IsNullOrEmpty(storePath) &&
                storePath.StartsWith(
                    "X509Store:",
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public ICertificateStore CreateStore(ITelemetryContext telemetry)
        {
            return new X509CertificateStore(telemetry);
        }
    }
}
