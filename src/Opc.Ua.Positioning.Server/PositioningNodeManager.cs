/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
            SystemContext.NodeIdFactory = this;
        }

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance && instance.Parent != null)
            {
                return new NodeId(
                    $"{instance.Parent.NodeId.IdentifierAsString}_{instance.SymbolicName}",
                    instance.Parent.NodeId.NamespaceIndex);
            }

            return node.NodeId;
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
            await base.CreateAddressSpaceAsync(
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
