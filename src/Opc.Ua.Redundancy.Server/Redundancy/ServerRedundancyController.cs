// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Materializes and updates the standard <c>Server.ServerRedundancy</c> node as
    /// the correct generated subtype for the configured
    /// <see cref="RedundancySupport"/> mode, and supports changing the mode at
    /// runtime.
    /// </summary>
    public interface IServerRedundancyController
    {
        /// <summary>
        /// Gets the currently applied redundancy mode.
        /// </summary>
        RedundancySupport Mode { get; }

        /// <summary>
        /// Changes the redundancy mode at runtime, promoting
        /// <c>Server.ServerRedundancy</c> to the matching generated subtype
        /// (<c>TransparentRedundancyType</c> / <c>NonTransparentRedundancyType</c> /
        /// <c>ServerRedundancyType</c>) and emitting a ModelChange for connected
        /// clients.
        /// </summary>
        /// <param name="mode">The new redundancy mode.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask ChangeModeAsync(RedundancySupport mode, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Default <see cref="IServerRedundancyController"/> implementation.
    /// </summary>
    public sealed class ServerRedundancyController : IServerRedundancyController, IDisposable
    {
        /// <summary>
        /// Creates the controller.
        /// </summary>
        /// <param name="options">The server redundancy options.</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public ServerRedundancyController(ServerRedundancyOptions options)
            : this(options, null)
        {
        }

        /// <summary>
        /// Creates the controller with the optional shared peer-status exchange.
        /// </summary>
        internal ServerRedundancyController(ServerRedundancyOptions options, SharedRedundantPeerStatus? peerStatus)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_peerStatus = peerStatus;
        }

        /// <inheritdoc/>
        public RedundancySupport Mode => m_options.Mode;

        /// <summary>
        /// Maps a <see cref="RedundancySupport"/> mode to the OPC 10000-5 redundancy
        /// ObjectType that models it.
        /// </summary>
        /// <param name="mode">The redundancy mode.</param>
        /// <returns>
        /// <see cref="ObjectTypeIds.TransparentRedundancyType"/> for
        /// <see cref="RedundancySupport.Transparent"/>,
        /// <see cref="ObjectTypeIds.NonTransparentRedundancyType"/> for the
        /// non-transparent modes, otherwise
        /// <see cref="ObjectTypeIds.ServerRedundancyType"/>.
        /// </returns>
        public static NodeId GetTypeDefinitionId(RedundancySupport mode)
        {
            return mode switch
            {
                RedundancySupport.Transparent => ObjectTypeIds.TransparentRedundancyType,
                RedundancySupport.Cold or
                    RedundancySupport.Warm or
                    RedundancySupport.Hot or
                    RedundancySupport.HotAndMirrored => ObjectTypeIds.NonTransparentRedundancyType,
                _ => ObjectTypeIds.ServerRedundancyType
            };
        }

        /// <inheritdoc/>
        public async ValueTask ChangeModeAsync(
            RedundancySupport mode,
            CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                m_options.Mode = mode;
                if (m_server != null)
                {
                    await ApplyAsync(m_server, m_options, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Attaches the controller to the started server and performs the initial
        /// materialization of <c>Server.ServerRedundancy</c>.
        /// </summary>
        /// <param name="server">The started server.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is <c>null</c>.</exception>
        internal async ValueTask AttachAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                m_server = server;
                await ApplyAsync(server, m_options, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Gets the underlying options (for diagnostics wiring).
        /// </summary>
        internal ServerRedundancyOptions Options => m_options;

        /// <summary>
        /// Reads the current peer observations and refreshes the published redundancy array.
        /// </summary>
        internal async ValueTask RefreshPeerStatusAsync(CancellationToken cancellationToken)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_server is not { ServerObject: { ServerRedundancy.RedundantServerArray: { } array } serverObject }
                    server ||
                    Mode == RedundancySupport.None)
                {
                    return;
                }

                ArrayOf<string> ids = GetPhysicalServerIds(server, m_options);
                string? localId = GetLocalServerId(server, m_options);
                bool refreshed = false;
                try
                {
                    await UpdateServerArrayAsync(server, serverObject, array, ids, localId, cancellationToken)
                        .ConfigureAwait(false);
                    refreshed = true;
                }
                finally
                {
                    if (!refreshed)
                    {
                        PublishLocalServerArray(server, serverObject, array, ids, localId);
                    }
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        /// <summary>
        /// Materializes <c>Server.ServerRedundancy</c> as the subtype matching the
        /// configured mode and publishes the mode-specific values.
        /// </summary>
        private static async ValueTask ApplyAsync(
            IServerContext server,
            ServerRedundancyOptions options,
            CancellationToken cancellationToken)
        {
            ServerObjectState? serverObject = server.ServerObject;
            ServerRedundancyState? redundancy = serverObject?.ServerRedundancy;
            if (serverObject == null || redundancy == null)
            {
                return;
            }

            ISystemContext context = server.DefaultSystemContext;

            // Reflect the configured mode; the value is preserved across a subtype swap.
            if (redundancy.RedundancySupport != null)
            {
                redundancy.RedundancySupport.Value = options.Mode;
            }

            NodeId targetTypeDefinition = GetTypeDefinitionId(options.Mode);
            IPredefinedNodeSubtypeReplacer? replacer = server.FindNodeManagers<IPredefinedNodeSubtypeReplacer>().FirstOrDefault();

            if (replacer != null && !redundancy.TypeDefinitionId.Equals(targetTypeDefinition))
            {
                ServerRedundancyState subtype = options.Mode switch
                {
                    RedundancySupport.Transparent
                        => context.CreateInstanceOfTransparentRedundancyType(),
                    RedundancySupport.Cold or
                        RedundancySupport.Warm or
                        RedundancySupport.Hot or
                        RedundancySupport.HotAndMirrored
                        => context.CreateInstanceOfNonTransparentRedundancyType(),
                    _ => context.CreateInstanceOfServerRedundancyType()
                };

                // RedundantServerArray is Optional on the base and on
                // NonTransparentRedundancyType (Mandatory only on
                // TransparentRedundancyType). Add it so the peer set is always
                // published and its well-known NodeId is preserved by the swap.
                subtype.AddRedundantServerArray(context);

                BaseInstanceState replaced = await replacer
                    .ReplacePredefinedInstanceSubtypeAsync(
                        context,
                        redundancy,
                        subtype,
                        BuildSubtypeChildNodeIds(options.Mode),
                        node => serverObject.ServerRedundancy = (ServerRedundancyState)node,
                        cancellationToken)
                    .ConfigureAwait(false);

                redundancy = (ServerRedundancyState)replaced;
            }

            ApplyValues(server, serverObject, redundancy, options);
            redundancy.ClearChangeMasks(context, false);
        }

        /// <summary>
        /// Publishes the mode-specific redundancy values onto the live node.
        /// </summary>
        private static void ApplyValues(
            IServerContext server,
            ServerObjectState serverObject,
            ServerRedundancyState redundancy,
            ServerRedundancyOptions options)
        {
            if (options.Mode == RedundancySupport.None)
            {
                return;
            }

            ISystemContext context = server.DefaultSystemContext;
            ArrayOf<string> peerUris = options.GetPeerApplicationUris();
            if (redundancy.RedundantServerArray is { } array)
            {
                PublishLocalServerArray(
                    server, serverObject, array, GetPhysicalServerIds(server, options), GetLocalServerId(server, options));
            }

            if (redundancy is TransparentRedundancyState transparent &&
                transparent.CurrentServerId != null)
            {
                transparent.CurrentServerId.Value = options.CurrentServerId;
                transparent.CurrentServerId.ClearChangeMasks(context, false);
            }
            else if (redundancy is NonTransparentRedundancyState nonTransparent &&
                nonTransparent.ServerUriArray != null)
            {
                nonTransparent.ServerUriArray.Value = peerUris;
                nonTransparent.ServerUriArray.ClearChangeMasks(context, false);
            }
        }

        private async ValueTask UpdateServerArrayAsync(
            IServerContext server,
            ServerObjectState serverObject,
            PropertyState<ArrayOf<RedundantServerDataType>> array,
            ArrayOf<string> ids,
            string? localId,
            CancellationToken ct)
        {
            SharedRedundantPeerStatus? peerStatus = m_peerStatus;
            if (peerStatus == null)
            {
                PublishLocalServerArray(server, serverObject, array, ids, localId);
                return;
            }

            (byte localLevel, ServerState localState) = ReadLocalStatus(server, serverObject);
            if (!string.IsNullOrEmpty(localId))
            {
                await peerStatus.PublishAsync(localId, localLevel, localState, ct).ConfigureAwait(false);
            }

            var peers = new RedundantServerDataType[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                (byte level, ServerState state) = id == localId
                    ? (localLevel, localState)
                    : await peerStatus.ReadAsync(id, ct).ConfigureAwait(false);
                peers[i] = CreatePeer(id, level, state);
            }
            PublishServerArray(server, array, peers);
        }

        private static void PublishLocalServerArray(
            IServerContext server,
            ServerObjectState serverObject,
            PropertyState<ArrayOf<RedundantServerDataType>> array,
            ArrayOf<string> ids,
            string? localId)
        {
            (byte level, ServerState state) = ReadLocalStatus(server, serverObject);
            var peers = new RedundantServerDataType[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                peers[i] = id == localId
                    ? CreatePeer(id, level, state)
                    : CreatePeer(id, ServiceLevels.NoData, ServerState.Unknown);
            }
            PublishServerArray(server, array, peers);
        }

        private static void PublishServerArray(
            IServerContext server,
            PropertyState<ArrayOf<RedundantServerDataType>> array,
            RedundantServerDataType[] peers)
        {
            var values = new ArrayOf<RedundantServerDataType>(peers);
            if (!CoreUtils.IsEqual(array.Value, values))
            {
                array.Value = values;
                array.ClearChangeMasks(server.DefaultSystemContext, false);
            }
        }

        private static RedundantServerDataType CreatePeer(string id, byte level, ServerState state)
        {
            return new RedundantServerDataType
            {
                ServerId = id,
                ServiceLevel = level,
                ServerState = state
            };
        }

        private static (byte ServiceLevel, ServerState State) ReadLocalStatus(
            IServerContext server,
            ServerObjectState serverObject)
        {
            return (serverObject.ServiceLevel?.Value ?? ServiceLevels.NoData, server.CurrentState);
        }

        private static ArrayOf<string> GetPhysicalServerIds(IServerContext server, ServerRedundancyOptions options)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            ArrayOf<string> configured = options.Mode == RedundancySupport.Transparent &&
                options.TransparentPeerServerIds.Count > 0
                ? new ArrayOf<string>(options.TransparentPeerServerIds.ToArray())
                : options.GetPeerApplicationUris();
            foreach (string id in configured)
            {
                if (!string.IsNullOrWhiteSpace(id) && ids.Add(id))
                {
                    ordered.Add(id);
                }
            }
            string? localId = GetLocalServerId(server, options);
            if (!string.IsNullOrWhiteSpace(localId) && ids.Add(localId))
            {
                ordered.Add(localId);
            }
            return new ArrayOf<string>([.. ordered]);
        }

        private static string? GetLocalServerId(IServerContext server, ServerRedundancyOptions options)
        {
            return options.Mode == RedundancySupport.Transparent
                ? options.CurrentServerId
                : server.DefaultSystemContext.ServerUris.GetString(0);
        }

        /// <summary>
        /// Builds the well-known instance NodeIds for the subtype-specific children
        /// so they land on the standard <c>Server.ServerRedundancy.*</c> NodeIds
        /// after the node is promoted.
        /// </summary>
        private static Dictionary<QualifiedName, NodeId>? BuildSubtypeChildNodeIds(
            RedundancySupport mode)
        {
            return mode switch
            {
                RedundancySupport.Transparent => new Dictionary<QualifiedName, NodeId>
                {
                    [new QualifiedName(BrowseNames.CurrentServerId, 0)]
                        = VariableIds.Server_ServerRedundancy_CurrentServerId
                },
                RedundancySupport.Cold or
                    RedundancySupport.Warm or
                    RedundancySupport.Hot or
                    RedundancySupport.HotAndMirrored => new Dictionary<QualifiedName, NodeId>
                    {
                        [new QualifiedName(BrowseNames.ServerUriArray, 0)]
                            = VariableIds.Server_ServerRedundancy_ServerUriArray
                    },
                _ => null
            };
        }

        private readonly ServerRedundancyOptions m_options;
        private readonly SharedRedundantPeerStatus? m_peerStatus;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private IServerContext? m_server;
    }
}
