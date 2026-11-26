// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Redundancy;

namespace Opc.Ua
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="ICertificateStoreProvider"/> that creates
    /// <see cref="SharedKeyValueCertificateStore"/> instances over a shared <see cref="ISharedKeyValueStore"/>,
    /// so trusted, issuer and rejected certificate lists are shared across a <c>RedundantServerSet</c>.
    /// </summary>
    /// <remarks>
    /// Register this provider (typically through dependency injection) so a
    /// <see cref="CertificateStoreIdentifier"/> with store type
    /// <see cref="CertificateStoreType.SharedKeyValue"/> (or a store path using the
    /// <see cref="CertificateStoreType.SharedKeyValueScheme"/> prefix) resolves to a distributed store.
    /// Distributed use requires an authenticating <see cref="IRecordProtector"/> and a
    /// <see cref="FreshnessProtectedKeyValueStore"/> whose authority epoch has been explicitly enrolled.
    /// Legacy raw records are not opened or imported by this provider.
    /// </remarks>
    public sealed class SharedKeyValueCertificateStoreProvider : ICertificateStoreProvider
    {
        /// <summary>
        /// Creates a provider bound to a shared key/value backend and record protector.
        /// </summary>
        /// <param name="store">The shared key/value backend.</param>
        /// <param name="protector">
        /// The record protector applied to every stored record. Defaults to the no-op
        /// <see cref="NullRecordProtector"/> (safe only for an in-memory, single-process store).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">
        /// A backend other than <see cref="InMemorySharedKeyValueStore"/> lacks freshness protection
        /// or an authenticating record protector.
        /// </exception>
        public SharedKeyValueCertificateStoreProvider(
            ISharedKeyValueStore store,
            IRecordProtector? protector = null)
        {
            m_protector = SharedKeyValueCertificateStore.RequireProtection(store, protector);
            m_store = store;
        }

        /// <inheritdoc/>
        public string StoreTypeName => CertificateStoreType.SharedKeyValue;

        /// <inheritdoc/>
        public bool SupportsStorePath(string storePath)
        {
            return !string.IsNullOrEmpty(storePath) &&
                storePath.StartsWith(
                    CertificateStoreType.SharedKeyValueScheme,
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public ICertificateStore CreateStore(ITelemetryContext telemetry)
        {
            return new SharedKeyValueCertificateStore(m_store, m_protector, telemetry);
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
    }
}
