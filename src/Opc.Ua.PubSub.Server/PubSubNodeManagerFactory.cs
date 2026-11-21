// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.Server;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// <see cref="IAsyncNodeManagerFactory"/> that produces
    /// <see cref="PubSubNodeManager"/> instances bound to a shared
    /// <see cref="IPubSubApplication"/> and optional
    /// <see cref="IPubSubKeyServiceServer"/>.
    /// </summary>
    /// <remarks>
    /// Mirrors the WoT Connectivity factory pattern. The factory
    /// itself does not own any namespaces beyond the PubSub server
    /// vendor URI; the standard PubSub nodes are loaded by the
    /// hosting server's diagnostics node manager.
    /// </remarks>
    public sealed class PubSubNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly IPubSubApplication m_application;
        private readonly IPubSubKeyServiceServer? m_keyService;
        private readonly PubSubServerOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly IEnumerable<PubSubActionMethodRegistration> m_actionMethodRegistrations;
        private readonly IEnumerable<PushSecurityKeyProvider> m_pushKeyProviders;
        private readonly IPubSubIdAllocator m_idAllocator;

        /// <summary>
        /// Creates a new factory with explicit dependencies.
        /// </summary>
        /// <param name="application">Runtime application.</param>
        /// <param name="keyService">Optional SKS server.</param>
        /// <param name="options">Server options.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="actionMethodRegistrations">Optional PublishedActionMethod bindings.</param>
        /// <param name="pushKeyProviders">Optional SetSecurityKeys push providers.</param>
        /// <param name="idAllocator">Shared PubSub id allocator.</param>
        public PubSubNodeManagerFactory(
            IPubSubApplication application,
            IPubSubKeyServiceServer? keyService,
            PubSubServerOptions options,
            ITelemetryContext telemetry,
            IEnumerable<PubSubActionMethodRegistration>? actionMethodRegistrations = null,
            IEnumerable<PushSecurityKeyProvider>? pushKeyProviders = null,
            IPubSubIdAllocator? idAllocator = null)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_application = application;
            m_keyService = keyService;
            m_options = options;
            m_telemetry = telemetry;
            m_actionMethodRegistrations =
                actionMethodRegistrations ?? [];
            m_pushKeyProviders = pushKeyProviders ?? [];
            m_idAllocator = idAllocator ?? new InMemoryPubSubIdAllocator();
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[] { PubSubNodeManager.NamespaceUri };

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The node manager is owned by the MasterNodeManager once registered.
#pragma warning disable CA2000 // Dispose objects before losing scope
            return new ValueTask<IAsyncNodeManager>(new PubSubNodeManager(
                server,
                configuration,
                m_application,
                m_keyService,
                m_options,
                m_telemetry,
                m_actionMethodRegistrations,
                m_pushKeyProviders,
                m_idAllocator));
#pragma warning restore CA2000 // Dispose objects before losing scope
        }
    }
}
