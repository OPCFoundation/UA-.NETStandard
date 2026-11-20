// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// An <see cref="ISessionManagerFactory"/> that builds a
    /// <see cref="DistributedSessionManager"/> over a shared key/value store.
    /// The session store and nonce registry are created lazily in
    /// <see cref="Create"/>, when the server's populated message context is
    /// available.
    /// </summary>
    public sealed class DistributedSessionManagerFactory : ISessionManagerFactory
    {
        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="keyValueStore">The shared key/value backend.</param>
        /// <param name="protector">
        /// The record protector applied to every mirrored session entry (authenticated
        /// encryption). It may be omitted only for an <see cref="InMemorySharedKeyValueStore"/>;
        /// to knowingly store session secrets in an external store unprotected, pass
        /// <see cref="NullRecordProtector.Instance"/> explicitly.
        /// </param>
        /// <param name="options">The distributed session options.</param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="protector"/> is <c>null</c> and <paramref name="keyValueStore"/>
        /// is not an in-memory store.
        /// </exception>
        public DistributedSessionManagerFactory(
            ISharedKeyValueStore keyValueStore,
            IRecordProtector? protector = null,
            DistributedSessionOptions? options = null)
        {
            m_keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
            // Fail at configuration time, like the dependency injection registration,
            // rather than when the first session is mirrored in cleartext.
            m_protector = protector ?? SharedKeyValueSessionStore.DefaultProtectorFor(keyValueStore);
            m_options = options ?? new DistributedSessionOptions();
        }

        /// <inheritdoc/>
        public ISessionManager Create(
            IServerInternal server,
            ApplicationConfiguration configuration,
            TimeProvider timeProvider,
            Func<string, Certificate?> serverCertificateProvider)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            var sessionStore = new SharedKeyValueSessionStore(
                m_keyValueStore, server.MessageContext, m_protector);
            var nonceRegistry = new SharedSingleUseNonceRegistry(m_keyValueStore, timeProvider: timeProvider);

            return new DistributedSessionManager(
                server,
                configuration,
                sessionStore,
                nonceRegistry,
                serverCertificateProvider,
                m_options,
                timeProvider);
        }

        private readonly ISharedKeyValueStore m_keyValueStore;
        private readonly IRecordProtector m_protector;
        private readonly DistributedSessionOptions m_options;
    }
}
