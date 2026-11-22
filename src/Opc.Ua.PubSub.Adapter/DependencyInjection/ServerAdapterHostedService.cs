// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Adapter.DependencyInjection
{
    /// <summary>
    /// Generic-host adapter that drives the
    /// <see cref="ServerAdapterRuntime"/> through the host lifetime. It
    /// depends on <see cref="IPubSubApplication"/> so resolving it forces the
    /// deferred adapter composition steps (which populate the runtime) to run
    /// before <see cref="StartAsync"/> starts the subscription coordinators. On
    /// stop the runtime is disposed, closing every external-server session.
    /// </summary>
    internal sealed class ServerAdapterHostedService : IHostedService
    {
        /// <summary>
        /// Initializes a new <see cref="ServerAdapterHostedService"/>.
        /// </summary>
        /// <param name="application">
        /// The PubSub application whose resolution forces the adapter
        /// composition steps to run.
        /// </param>
        /// <param name="runtime">
        /// The runtime owning the adapter sessions and coordinators.
        /// </param>
        /// <param name="reloadCoordinator">
        /// The coordinator that listens for hot-reload changes.
        /// </param>
        public ServerAdapterHostedService(
            IPubSubApplication application,
            ServerAdapterRuntime runtime,
            ServerAdapterReloadCoordinator reloadCoordinator)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            m_application = application;
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_reloadCoordinator = reloadCoordinator
                ?? throw new ArgumentNullException(nameof(reloadCoordinator));
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await m_runtime.StartAsync(cancellationToken).ConfigureAwait(false);
            await m_reloadCoordinator.StartAsync(m_application, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await m_reloadCoordinator.DisposeAsync().ConfigureAwait(false);
            await m_runtime.DisposeAsync().ConfigureAwait(false);
        }

        private readonly IPubSubApplication m_application;
        private readonly ServerAdapterRuntime m_runtime;
        private readonly ServerAdapterReloadCoordinator m_reloadCoordinator;
    }
}
