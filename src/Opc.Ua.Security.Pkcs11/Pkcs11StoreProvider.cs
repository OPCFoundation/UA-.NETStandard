/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Security.Pkcs11
{
    /// <summary>
    /// Creates <see cref="Pkcs11CertificateStore"/> instances for store paths
    /// that use the RFC 7512 <c>pkcs11:</c> scheme.
    /// </summary>
    /// <remarks>
    /// Register with <c>CertificateManagerOptions.AddStoreProvider</c>, or
    /// through <c>IOpcUaBuilder.AddPkcs11CertificateStore</c>.
    /// Once registered, an application configuration can point any certificate
    /// store at a token by changing only its store path.
    /// </remarks>
    public sealed class Pkcs11StoreProvider : ICertificateStoreProvider
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="options">
        /// Token options applied to every store this provider creates, or
        /// <c>null</c> to take them from each store path.
        /// </param>
        /// <remarks>
        /// Supplying options is how a PIN is kept out of the configuration file:
        /// the store path names the token, and the PIN arrives from a secret
        /// store through <see cref="Pkcs11TokenOptions.PinProvider"/>.
        /// </remarks>
        public Pkcs11StoreProvider(Pkcs11TokenOptions? options = null)
        {
            m_options = options;
        }

        /// <inheritdoc/>
        public string StoreTypeName => Pkcs11CertificateStore.StoreTypeName;

        /// <inheritdoc/>
        public bool SupportsStorePath(string storePath)
        {
            return Pkcs11TokenOptions.IsPkcs11Uri(storePath);
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="telemetry"/> is <c>null</c>.
        /// </exception>
        public ICertificateStore CreateStore(ITelemetryContext telemetry)
        {
            return new Pkcs11CertificateStore(telemetry, m_options);
        }

        private readonly Pkcs11TokenOptions? m_options;
    }
}
