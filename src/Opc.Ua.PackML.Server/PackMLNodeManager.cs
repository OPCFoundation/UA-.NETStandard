/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

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
            return new ValueTask<NodeStateCollection>(new NodeStateCollection().AddOpcUaPackML(context));
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            await PackMLLifetime.CompleteShutdownAsync(
                Runtime.DisposeAsync, () => base.DisposeAsyncCore()).ConfigureAwait(false);
        }

        /// <summary>
        /// Connects units created at startup or later to the server event notifier.
        /// </summary>
        internal ValueTask RegisterUnitNotifierAsync(
            PackMLBaseObjectState node,
            CancellationToken cancellationToken)
        {
            return AddRootNotifierAsync(node, cancellationToken);
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
