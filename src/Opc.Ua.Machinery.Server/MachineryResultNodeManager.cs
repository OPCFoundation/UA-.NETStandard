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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.NodeManager;
using ConformanceUnitNames = Opc.Ua.Machinery.Server.ConformanceUnits;
using ResultBrowseNames = Opc.Ua.Machinery.Result.BrowseNames;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Configures the stand-alone OPC 40001-101 result server.
    /// </summary>
    public sealed class MachineryResultServerOptions
    {
        /// <summary>
        /// Default application-owned namespace for result instances.
        /// </summary>
        public const string DefaultInstanceNamespaceUri =
            "urn:opcua-netstandard:machinery:results";

        /// <summary>
        /// Gets or sets the application-owned namespace URI used for the result
        /// instances.
        /// </summary>
        public string InstanceNamespaceUri { get; set; } = DefaultInstanceNamespaceUri;

        /// <summary>
        /// Gets or sets the browse name of the root folder organized by the
        /// <c>Objects</c> folder.
        /// </summary>
        public string RootBrowseName { get; set; } = "ResultManagement";

        /// <summary>
        /// Gets or sets whether the <c>ResultTransfer</c> download path is
        /// exposed.
        /// </summary>
        public bool EnableFileTransfer { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of concurrently open result-transfer
        /// handles.
        /// </summary>
        public int MaxConcurrentResultTransfers { get; set; } = 8;

        /// <summary>
        /// Gets or sets how long an open result-transfer handle survives
        /// without activity.
        /// </summary>
        public TimeSpan ResultTransferTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Gets or sets the maximum number of result identifiers this
        /// manager keeps pinned at once. See
        /// <see cref="MachineryServerOptions.MaxPinnedResultHandles"/>.
        /// </summary>
        public int MaxPinnedResultHandles { get; set; } = 64;

        /// <summary>
        /// Gets or sets how long a pinned result handle survives without
        /// activity. See
        /// <see cref="MachineryServerOptions.PinnedResultHandleTimeout"/>.
        /// </summary>
        public TimeSpan PinnedResultHandleTimeout { get; set; } = TimeSpan.FromMinutes(5);

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(InstanceNamespaceUri) ||
                !Uri.IsWellFormedUriString(InstanceNamespaceUri, UriKind.Absolute))
            {
                throw new ArgumentException(
                    "MachineryResultServerOptions.InstanceNamespaceUri must be an " +
                    "absolute URI or URN.",
                    nameof(InstanceNamespaceUri));
            }
            if (string.IsNullOrWhiteSpace(RootBrowseName))
            {
                throw new ArgumentException(
                    "MachineryResultServerOptions.RootBrowseName must not be empty.",
                    nameof(RootBrowseName));
            }
        }

        internal MachineryServerOptions ToTransferOptions()
        {
            return new MachineryServerOptions
            {
                Parts = MachineryParts.Result,
                InstanceNamespaceUri = InstanceNamespaceUri,
                MaxConcurrentResultTransfers = MaxConcurrentResultTransfers,
                ResultTransferTimeout = ResultTransferTimeout,
                MaxPinnedResultHandles = MaxPinnedResultHandles,
                PinnedResultHandleTimeout = PinnedResultHandleTimeout
            };
        }
    }

    /// <summary>
    /// Stand-alone node manager for OPC 40001-101 Result Transfer.
    /// </summary>
    /// <remarks>
    /// OPC 40001-101 needs UA core only — neither Device Integration nor the
    /// OPC 40001-1 machine model — so a server whose whole job is to hand out
    /// measurement results does not have to carry either. That is what this
    /// manager is for; a machine that also publishes results uses
    /// <c>WithResultManagement</c> on its machine builder instead, and both
    /// share the same binder underneath.
    /// </remarks>
    public sealed class MachineryResultNodeManager :
        FluentNodeManagerBase,
        INodeIdFactory,
        IConformanceContributor
    {
        /// <summary>
        /// Creates a stand-alone result node manager.
        /// </summary>
        public MachineryResultNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            MachineryResultServerOptions options,
            IMachineryResultStore store)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<MachineryResultNodeManager>(),
                ValidateOptions(options).InstanceNamespaceUri,
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult)
        {
            m_options = options;
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_resultLogger = server.Telemetry.CreateLogger<MachineryResultNodeManager>();
            RegisterEncodeables(server.Factory);
        }

        /// <summary>
        /// Gets the application-owned instance namespace index.
        /// </summary>
        public ushort InstanceNamespaceIndex =>
            (ushort)Server.NamespaceUris.GetIndex(m_options.InstanceNamespaceUri);

        /// <summary>
        /// Gets the published result-management object, or
        /// <see langword="null"/> before the address space is built.
        /// </summary>
        public ResultManagementState? ResultManagement { get; private set; }

        /// <summary>
        /// Gets the publisher used to push new results, or
        /// <see langword="null"/> before the address space is built.
        /// </summary>
        public IMachineryResultPublisher? Publisher { get; private set; }

        /// <inheritdoc/>
        /// <remarks>
        /// Answered the same way <see cref="MachineryNodeManager"/> answers it:
        /// <c>Machinery-Result Types</c> is a type-exposure unit that loading
        /// the model satisfies, and the rest are reported only once the
        /// structure behind them is there. The four method units stand or fall
        /// together because the binder publishes all five methods of
        /// <c>ResultManagementType</c> at once.
        /// </remarks>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                var units = new List<QualifiedName>
                {
                    new(ConformanceUnitNames.ResultTypes)
                };
                if (ResultManagement != null)
                {
                    units.Add(new QualifiedName(ConformanceUnitNames.ResultGetLatestResult));
                    units.Add(new QualifiedName(ConformanceUnitNames.ResultGetResultById));
                    units.Add(
                        new QualifiedName(ConformanceUnitNames.ResultGetResultsFiltered));
                    units.Add(
                        new QualifiedName(ConformanceUnitNames.ResultAcknowledgeResults));
                }
                if (m_transfer != null)
                {
                    units.Add(new QualifiedName(ConformanceUnitNames.ResultFiles));
                }
                if (m_raisedResultEvent)
                {
                    units.Add(new QualifiedName(ConformanceUnitNames.ResultEvents));
                }
                return units.ToArrayOf();
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The simple facet needs <c>GetLatestResult</c> and the result types;
        /// the full one additionally needs the result-ready events, so it can
        /// only be claimed once one has actually been reported. Advertising it
        /// before then would promise a client an event stream that has never
        /// produced an event.
        /// </remarks>
        public ArrayOf<string> ServerProfiles
        {
            get
            {
                var profiles = new List<string>();
                if (ResultManagement != null)
                {
                    profiles.Add(
                        Opc.Ua.Machinery.Server.ServerProfiles.ResultSimpleTransfer);
                    if (m_raisedResultEvent)
                    {
                        profiles.Add(
                            Opc.Ua.Machinery.Server.ServerProfiles.ResultTransfer);
                    }
                }
                return profiles.ToArrayOf();
            }
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            nodes.AddMachineryResultTypeSystem(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            return node.NodeId.IsNull
                ? new NodeId(Utils.IncrementIdentifier(ref m_lastUsedNodeId), InstanceNamespaceIndex)
                : node.NodeId;
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            NodeManagerBuilder builder = CreateFluentBuilder(InstanceNamespaceIndex);
            var browseName = new QualifiedName(
                m_options.RootBrowseName,
                InstanceNamespaceIndex);
            ResultManagementState management =
                SystemContext.CreateInstanceOfResultManagementType(parent: null!, browseName);
            management.NodeId = new NodeId(m_options.RootBrowseName, InstanceNamespaceIndex);
            management.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.Organizes;
            management.EventNotifier = EventNotifiers.SubscribeToEvents;
            management.AddResults(SystemContext);
            if (m_options.EnableFileTransfer)
            {
                management.AddResultTransfer(SystemContext);
            }

            var binder = new MachineryResultManagementBinder(
                management,
                SystemContext,
                m_options.ToTransferOptions());
            binder.BindMethods(m_store);
            await binder.BindEventTypeAsync(
                this,
                InstanceNamespaceIndex,
                cancellationToken).ConfigureAwait(false);

            builder.AddRoot(management);
            await RegisterAuthoredNodesAsync(builder, cancellationToken).ConfigureAwait(false);
            await CompleteConfigureAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);
            await builder.SealAsync(cancellationToken).ConfigureAwait(false);

            if (m_options.EnableFileTransfer)
            {
                m_transfer = binder.BindTransfer(
                    this,
                    m_store,
                    m_options.ToTransferOptions(),
                    m_resultLogger);
            }

            ResultManagement = management;
            Publisher = new StandaloneResultPublisher(this, binder, m_store);
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            MachineryResultTransferManager? transfer = m_transfer;
            m_transfer = null;
            if (transfer != null)
            {
                await transfer.DisposeAsync().ConfigureAwait(false);
            }
            await base.DisposeAsyncCore().ConfigureAwait(false);
        }

        private static MachineryResultServerOptions ValidateOptions(
            MachineryResultServerOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            options.Validate();
            return options;
        }

        private static void RegisterEncodeables(IEncodeableFactory factory)
        {
            if (factory.ContainsEncodeableType(
                Opc.Ua.Machinery.Result.DataTypeIds.ResultDataType))
            {
                return;
            }
            factory.Builder.AddOpcUaMachineryResult().Commit();
        }

        private readonly MachineryResultServerOptions m_options;
        private readonly IMachineryResultStore m_store;
        private readonly ILogger m_resultLogger;
        private MachineryResultTransferManager? m_transfer;
        private uint m_lastUsedNodeId;
        private bool m_raisedResultEvent;

        private sealed class StandaloneResultPublisher : IMachineryResultPublisher
        {
            public StandaloneResultPublisher(
                MachineryResultNodeManager owner,
                MachineryResultManagementBinder binder,
                IMachineryResultStore store)
            {
                m_owner = owner;
                m_binder = binder;
                m_store = store;
            }

            public async ValueTask PublishAsync(
                MachineryResult result,
                CancellationToken cancellationToken = default)
            {
                if (result == null)
                {
                    throw new ArgumentNullException(nameof(result));
                }
                await MachineryResultIngestion
                    .StoreAsync(m_store, result, cancellationToken)
                    .ConfigureAwait(false);
                m_binder.RaiseResultReady(result);
                m_owner.m_raisedResultEvent = true;
            }

            private readonly MachineryResultNodeManager m_owner;
            private readonly MachineryResultManagementBinder m_binder;
            private readonly IMachineryResultStore m_store;
        }
    }
}
