// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Server;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Standalone Device Integration owner for the OPC 40223 Pumps model.
    /// </summary>
    /// <remarks>
    /// Loads DI, IA, Machinery and Pumps in dependency order. The shared
    /// <see cref="PumpsRuntime"/> also supports composition on an existing Machinery owner.
    /// </remarks>
    public sealed class PumpsNodeManager : DiNodeManager
    {
        /// <summary>
        /// Creates a standalone manager with default options.
        /// </summary>
        public PumpsNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : this(server, configuration, postSetupRunner: null, options: null)
        {
        }

        /// <summary>
        /// Creates a standalone manager with explicit options.
        /// </summary>
        public PumpsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            PumpsServerOptions options)
            : this(server, configuration, null, new OptionsWrapper<PumpsServerOptions>(
                options ?? throw new ArgumentNullException(nameof(options))))
        {
        }

        /// <summary>
        /// Creates a manager participating in the DI post-setup pipeline.
        /// </summary>
        public PumpsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<PumpsServerOptions>? options = null)
            : this(server, configuration, postSetupRunner, options, default)
        {
        }

        /// <summary>
        /// Creates a Pumps owner with additional DI companion models.
        /// </summary>
        public PumpsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<PumpsServerOptions>? options,
            ArrayOf<IDiModelProvider> providers)
            : base(server, configuration, postSetupRunner, providers, ManagerNamespaceUris(options?.Value))
        {
            Runtime = new PumpsRuntime(this, options?.Value ?? new PumpsServerOptions());
        }

        /// <summary>
        /// Gets the shared Pumps construction and configuration runtime.
        /// </summary>
        public PumpsRuntime Runtime { get; }

        /// <inheritdoc cref="PumpsRuntime.Options"/>
        public PumpsServerOptions Options => Runtime.Options;

        /// <inheritdoc cref="PumpsRuntime.Pumps"/>
        public ArrayOf<PumpState> Pumps => Runtime.Pumps;

        /// <inheritdoc cref="PumpsRuntime.NamespaceIndices"/>
        public PumpNamespaceIndices NamespaceIndices => Runtime.NamespaceIndices;

        /// <inheritdoc cref="PumpsRuntime.PumpRegisteredForTest"/>
        internal Func<PumpState, Task>? PumpRegisteredForTest
        {
            get => Runtime.PumpRegisteredForTest;
            set => Runtime.PumpRegisteredForTest = value;
        }

        /// <inheritdoc cref="PumpsRuntime.CreatePumpAsync(QualifiedName, CancellationToken)"/>
        public ValueTask<IPumpBuilder> CreatePumpAsync(
            QualifiedName browseName,
            CancellationToken cancellationToken = default)
        {
            return Runtime.CreatePumpAsync(browseName, cancellationToken);
        }

        /// <inheritdoc cref="PumpsRuntime.CreatePumpAsync(QualifiedName, NodeState?, CancellationToken)"/>
        public ValueTask<IPumpBuilder> CreatePumpAsync(
            QualifiedName browseName,
            NodeState? parent,
            CancellationToken cancellationToken = default)
        {
            return Runtime.CreatePumpAsync(browseName, parent, cancellationToken);
        }

        /// <inheritdoc cref="PumpsRuntime.Pump(PumpState)"/>
        public IPumpBuilder Pump(PumpState pump)
        {
            return Runtime.Pump(pump);
        }

        /// <inheritdoc cref="PumpsRuntime.Pump(NodeId)"/>
        public IPumpBuilder? Pump(NodeId nodeId)
        {
            return Runtime.Pump(nodeId);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            if (Options.LoadIndustrialAutomationModel)
            {
                nodes.AddOpcUaIA(context);
            }
            nodes.AddOpcUaMachinery(context);
            nodes.AddOpcUaPumps(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            try
            {
                await Runtime.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Gets the model and additional namespaces; DI is appended by the base manager.
        /// </summary>
        private static string[] ManagerNamespaceUris(PumpsServerOptions? options)
        {
            options ??= new PumpsServerOptions();
            var uris = new List<string>
            {
                Namespaces.Pumps,
                Machinery.Namespaces.Machinery
            };
            if (options.LoadIndustrialAutomationModel)
            {
                uris.Add(IA.Namespaces.IA);
            }
            foreach (string uri in options.AdditionalNamespaceUris)
            {
                uris.Add(uri);
            }
            return [.. uris];
        }
    }
}
