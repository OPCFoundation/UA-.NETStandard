// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// A pluggable provider that creates <see cref="ICertificateStore"/>
    /// instances for a specific store type (e.g. directory, Windows, etc.).
    /// </summary>
    public interface ICertificateStoreProvider
    {
        /// <summary>
        /// Gets the store type name that this provider handles
        /// (e.g. <c>"Directory"</c>, <c>"X509Store"</c>).
        /// </summary>
        string StoreTypeName { get; }

        /// <summary>
        /// Returns <see langword="true"/> if this provider can open a store
        /// at the given path.
        /// </summary>
        /// <param name="storePath">The store path to check.</param>
        /// <returns>
        /// <see langword="true"/> if the path is supported; otherwise
        /// <see langword="false"/>.
        /// </returns>
        bool SupportsStorePath(string storePath);

        /// <summary>
        /// Creates a new <see cref="ICertificateStore"/> instance.
        /// </summary>
        /// <param name="telemetry">
        /// The telemetry context used for logging and diagnostics.
        /// </param>
        /// <returns>A new certificate store instance.</returns>
        ICertificateStore CreateStore(ITelemetryContext telemetry);
    }
}
