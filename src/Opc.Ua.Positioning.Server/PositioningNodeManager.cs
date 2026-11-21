// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gpos;
using Opc.Ua.Positioning.Server.Hosting;
using Opc.Ua.Rsl;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Positioning.Server
{
    /// <summary>
    /// Standalone node manager for the RSL and GPOS models.
    /// </summary>
    public sealed class PositioningNodeManager : FluentNodeManagerBase, INodeIdFactory
    {
        private readonly IPositioningPostSetupRunner? m_runner;

        /// <summary>
        /// Creates a standalone Positioning node manager.
        /// </summary>
        public PositioningNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(server, configuration, runner: null)
        {
        }

        /// <summary>
        /// Creates a Positioning node manager with hosting configurator support.
        /// </summary>
        public PositioningNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IPositioningPostSetupRunner? runner)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<PositioningNodeManager>(),
                  Rsl.Namespaces.RSL,
                  Gpos.Namespaces.GPOS)
        {
            m_runner = runner;
        }

        /// <summary>
        /// Creates an address-space builder bound to this manager.
        /// </summary>
        public PositioningAddressSpaceBuilder CreatePositioningBuilder()
        {
            return new PositioningAddressSpaceBuilder(this);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaRsl(context);
            nodes.AddOpcUaGpos(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await LoadPredefinedNodesAsync(
                SystemContext,
                externalReferences,
                cancellationToken).ConfigureAwait(false);
            await PositioningNamespaceMetadata.EnsureAsync(
                this,
                cancellationToken).ConfigureAwait(false);

            if (m_runner != null)
            {
                await m_runner.RunAsync(this, cancellationToken)
                    .ConfigureAwait(false);
            }

            m_logger.NodeManagerReady();
        }
    }

    internal static partial class PositioningNodeManagerLog
    {
        [LoggerMessage(
            EventId = PositioningServerEventIds.NodeManagerReady,
            Level = LogLevel.Information,
            Message = "Positioning node manager loaded RSL and GPOS.")]
        public static partial void NodeManagerReady(this ILogger logger);
    }
}
