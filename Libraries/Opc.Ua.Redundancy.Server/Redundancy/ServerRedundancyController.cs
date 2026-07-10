/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
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
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
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
            IServerInternal server,
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
            IServerInternal server,
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
            var replacer = server.DiagnosticsNodeManager as IPredefinedNodeSubtypeReplacer;

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

            ApplyValues(context, redundancy, options);
            redundancy.ClearChangeMasks(context, false);
        }

        /// <summary>
        /// Publishes the mode-specific redundancy values onto the live node.
        /// </summary>
        private static void ApplyValues(
            ISystemContext context,
            ServerRedundancyState redundancy,
            ServerRedundancyOptions options)
        {
            if (options.Mode == RedundancySupport.None)
            {
                return;
            }

            ArrayOf<string> peerUris = options.GetPeerApplicationUris();

            if (redundancy.RedundantServerArray != null)
            {
                var servers = new List<RedundantServerDataType>(peerUris.Count);
                foreach (string peerUri in peerUris)
                {
                    servers.Add(new RedundantServerDataType
                    {
                        ServerId = peerUri,
                        ServiceLevel = options.PeerServiceLevel,
                        ServerState = ServerState.Running
                    });
                }

                redundancy.RedundantServerArray.Value =
                    new ArrayOf<RedundantServerDataType>(servers.ToArray());
                redundancy.RedundantServerArray.ClearChangeMasks(context, false);
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
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private IServerInternal? m_server;
    }
}
