/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Device Integration owner for the OPC 30010 AutoID Devices model.
    /// </summary>
    /// <remarks>
    /// The owner for a server whose only Device Integration based companion is AutoID: it loads DI
    /// and, through <see cref="AutoIdModelProvider"/>, AutoID, owns the
    /// <see cref="AutoIdRuntime"/> that creates the readers below the <c>DeviceSet</c>, and disposes
    /// it on shutdown. To combine AutoID with other companions, compose it into their owner
    /// instead with <c>AddAutoIdModel</c> and a typed configuration callback.
    /// </remarks>
    public sealed class AutoIdNodeManager : DiNodeManager
    {
        /// <summary>
        /// Creates a manager with default options.
        /// </summary>
        public AutoIdNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : this(server, configuration, postSetupRunner: null, options: null)
        {
        }

        /// <summary>
        /// Creates a manager with explicit options.
        /// </summary>
        public AutoIdNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            AutoIdServerOptions options)
            : this(server, configuration, null, new OptionsWrapper<AutoIdServerOptions>(
                options ?? throw new ArgumentNullException(nameof(options))))
        {
        }

        /// <summary>
        /// Creates a manager participating in the DI post-setup pipeline.
        /// </summary>
        public AutoIdNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<AutoIdServerOptions>? options = null)
            : this(server, configuration, postSetupRunner, options, default)
        {
        }

        /// <summary>
        /// Creates an AutoID owner with additional DI companion models.
        /// </summary>
        public AutoIdNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<AutoIdServerOptions>? options,
            ArrayOf<IDiModelProvider> providers)
            : this(server, configuration, postSetupRunner, options, providers, null)
        {
        }

        /// <summary>
        /// Creates an AutoID owner with an injectable image FileType binder.
        /// </summary>
        public AutoIdNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<AutoIdServerOptions>? options,
            ArrayOf<IDiModelProvider> providers,
            Opc.Ua.Server.FileSystem.IFileTypeBinder? fileBinder)
            : base(server, configuration, postSetupRunner, providers, ManagerNamespaceUris(options?.Value))
        {
            Runtime = new AutoIdRuntime(this, options?.Value ?? new AutoIdServerOptions(), fileBinder);
        }

        /// <summary>
        /// Gets the runtime that creates and owns the readers.
        /// </summary>
        public AutoIdRuntime Runtime { get; }

        /// <inheritdoc cref="AutoIdRuntime.Options"/>
        public AutoIdServerOptions Options => Runtime.Options;

        /// <inheritdoc cref="AutoIdRuntime.Readers"/>
        public ArrayOf<AutoIdReader> Readers => Runtime.Readers;

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            // The stand-alone owner is a Device Integration owner that loads DI and then the same
            // AutoID model provider a composed owner uses.
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            new AutoIdModelProvider(Runtime.Options).AddPredefinedNodes(nodes, context);
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
        internal static string[] ManagerNamespaceUris(AutoIdServerOptions? options)
        {
            options ??= new AutoIdServerOptions();
            var uris = new List<string> { AutoIdModel.NamespaceUri };
            foreach (string uri in options.AdditionalNamespaceUris)
            {
                uris.Add(uri);
            }
            return [.. uris];
        }
    }
}
