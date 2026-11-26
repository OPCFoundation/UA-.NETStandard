// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Server startup task that activates the <see cref="ServerLoadDirector"/> once the server is started: it builds
    /// the context-dependent collaborators (peer view, direction policy, endpoint directory and publisher) with the
    /// populated server message context and the local ServerUri, then calls <see cref="ServerLoadDirector.Configure"/>.
    /// </summary>
    public sealed class LoadDirectionStartupTask : IServerStartupTask
    {
        /// <summary>
        /// Creates the task.
        /// </summary>
        /// <param name="store">The shared store the direction signals and endpoints are gossiped through.</param>
        /// <param name="protector">Protects record integrity.</param>
        /// <param name="options">The load-direction options.</param>
        /// <param name="director">The director to activate.</param>
        /// <param name="timeProvider">The time source for staleness checks.</param>
        public LoadDirectionStartupTask(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            LoadDirectionOptions options,
            ServerLoadDirector director,
            TimeProvider timeProvider)
            : this(store, protector, options, director, timeProvider,
                (options ?? throw new ArgumentNullException(nameof(options))).PeerApplicationUris)
        {
        }

        /// <summary>
        /// Creates the task with the resolved static member list.
        /// </summary>
        internal LoadDirectionStartupTask(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            LoadDirectionOptions options,
            ServerLoadDirector director,
            TimeProvider timeProvider,
            ArrayOf<string> peerApplicationUris,
            bool useHostedPublishing = false)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_protector = LoadDirectionRecordValidation.RequireProtection(m_store, protector);
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_director = director ?? throw new ArgumentNullException(nameof(director));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_peerApplicationUris = peerApplicationUris;
            m_useHostedPublishing = useHostedPublishing;
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            string[] serverUris = server.DefaultSystemContext.ServerUris.ToArray();
            string? localServerUri = serverUris.Length > 0 ? serverUris[0] : null;
            if (string.IsNullOrEmpty(localServerUri))
            {
                server.DefaultSystemContext.Telemetry
                    .CreateLogger<LoadDirectionStartupTask>()
                    .LoadDirectionDisabledLocalServerUriUnavailable();
                return default;
            }

            IServiceMessageContext context = server.MessageContext;
            var view = new SharedPeerDirectionView(
                m_store, context, m_protector, m_options, m_timeProvider, m_peerApplicationUris);
            var policy = new BandedServerDirectionPolicy(view, m_options, LoadDirectionRandom.NextIndex);
            var directory = new SharedPeerEndpointDirectory(
                m_store, context, m_protector, m_options, m_timeProvider, m_peerApplicationUris);
            if (m_useHostedPublishing && server is IServerEndpointRegistryProvider)
            {
                m_director.ConfigureForHostedPublishing(policy, directory, localServerUri!);
            }
            else
            {
                var endpointPublisher = new SharedPeerEndpointPublisher(
                    m_store, context, m_protector, m_options, localServerUri!, m_timeProvider);
                m_director.Configure(policy, directory, endpointPublisher, localServerUri!);
            }
            return default;
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly LoadDirectionOptions m_options;
        private readonly ServerLoadDirector m_director;
        private readonly TimeProvider m_timeProvider;
        private readonly ArrayOf<string> m_peerApplicationUris;
        private readonly bool m_useHostedPublishing;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="LoadDirectionStartupTask"/>.
    /// </summary>
    internal static partial class LoadDirectionStartupTaskLog
    {
        [LoggerMessage(EventId = RedundancyServerEventIds.LoadDirectionStartupTask + 0, Level = LogLevel.Warning,
            Message = "Load direction disabled: the local ServerUri is unavailable.")]
        public static partial void LoadDirectionDisabledLocalServerUriUnavailable(this ILogger logger);
    }

}
