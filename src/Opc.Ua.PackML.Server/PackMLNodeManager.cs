// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.PackML.Server
{
    /// <summary>
    /// Standalone owner for the PackML model and configured application units.
    /// </summary>
    public sealed class PackMLNodeManager : AsyncCustomNodeManager, IConformanceContributor
    {
        /// <summary>
        /// Creates an owner using explicit providers and configuration.
        /// </summary>
        public PackMLNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            PackMLServerOptions options)
            : base(server, configuration, GetNamespaces(options))
        {
            m_options = options;
            Runtime = new PackMLRuntime(this);
        }

        /// <summary>
        /// Gets this owner's composable unit runtime.
        /// </summary>
        public PackMLRuntime Runtime { get; }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits => Runtime.ConformanceUnits;

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles => Runtime.ServerProfiles;

        /// <summary>
        /// Gets the application instance namespace index after startup.
        /// </summary>
        public ushort InstanceNamespaceIndex =>
            checked((ushort)SystemContext.NamespaceUris.GetIndex(m_options.NamespaceUri));

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            await m_options.BuildAsync(this, cancellationToken).ConfigureAwait(false);
            await AddReverseReferencesAsync(externalReferences, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            await PackMLLifetime.CompleteShutdownAsync(
                Runtime.DisposeAsync,
                () => base.DeleteAddressSpaceAsync(cancellationToken)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NodeStateCollection nodes = new NodeStateCollection().AddOpcUaPackML(context);
            // Method arguments and status values carry PackML structures, which
            // decode to typed values only after the server factory knows them.
            context.EncodeableFactory.Builder.AddOpcUaPackML().Commit();
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            await PackMLLifetime.CompleteShutdownAsync(
                Runtime.DisposeAsync, () => base.DisposeAsyncCore()).ConfigureAwait(false);
        }

        /// <summary>
        /// Validates namespace ownership before base initialization.
        /// </summary>
        private static string[] GetNamespaces(PackMLServerOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            options.Validate();
            return [options.NamespaceUri, Namespaces.PackML];
        }

        /// <summary>The captured namespace and startup composition options.</summary>
        private readonly PackMLServerOptions m_options;
    }
}
