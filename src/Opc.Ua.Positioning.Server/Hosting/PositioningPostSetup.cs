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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Positioning.Server.Hosting
{
    /// <summary>
    /// Context supplied to Positioning post-setup configurators.
    /// </summary>
    public sealed class PositioningServerContext
    {
        internal PositioningServerContext(
            AsyncCustomNodeManager manager,
            ArrayOf<IGlobalPositionProvider> globalProviders,
            ArrayOf<IRelativeSpatialLocationProvider> relativeProviders,
            CancellationToken cancellationToken)
        {
            Manager = manager;
            AddressSpace = new PositioningAddressSpaceBuilder(manager);
            GlobalPositionProviders = globalProviders;
            RelativeSpatialLocationProviders = relativeProviders;
            CancellationToken = cancellationToken;
        }

        /// <summary>
        /// Owning node manager.
        /// </summary>
        public AsyncCustomNodeManager Manager { get; }

        /// <summary>
        /// Address-space builder bound to <see cref="Manager"/>.
        /// </summary>
        public PositioningAddressSpaceBuilder AddressSpace { get; }

        /// <summary>
        /// Registered global positioning providers.
        /// </summary>
        public ArrayOf<IGlobalPositionProvider> GlobalPositionProviders { get; }

        /// <summary>
        /// Registered relative spatial location providers.
        /// </summary>
        public ArrayOf<IRelativeSpatialLocationProvider> RelativeSpatialLocationProviders { get; }

        /// <summary>
        /// Startup cancellation token.
        /// </summary>
        public CancellationToken CancellationToken { get; }
    }

    /// <summary>
    /// Dispatches registered Positioning configurators to compatible node managers.
    /// </summary>
    public interface IPositioningPostSetupRunner
    {
        /// <summary>
        /// Runs configurators registered for <paramref name="manager"/>.
        /// </summary>
        ValueTask RunAsync(
            AsyncCustomNodeManager manager,
            CancellationToken cancellationToken);
    }

    internal interface IPositioningPostSetupConfigurator
    {
        Type TargetManagerType { get; }

        ValueTask RunAsync(PositioningServerContext context);
    }

    internal sealed class PositioningPostSetupRunner : IPositioningPostSetupRunner
    {
        private readonly ArrayOf<IPositioningPostSetupConfigurator> m_configurators;
        private readonly ArrayOf<IGlobalPositionProvider> m_globalProviders;
        private readonly ArrayOf<IRelativeSpatialLocationProvider> m_relativeProviders;

        public PositioningPostSetupRunner(
            IEnumerable<IPositioningPostSetupConfigurator> configurators,
            IEnumerable<IGlobalPositionProvider> globalProviders,
            IEnumerable<IRelativeSpatialLocationProvider> relativeProviders)
        {
            m_configurators = configurators.ToArray().ToArrayOf();
            m_globalProviders = globalProviders.ToArray().ToArrayOf();
            m_relativeProviders = relativeProviders.ToArray().ToArrayOf();
        }

        public async ValueTask RunAsync(
            AsyncCustomNodeManager manager,
            CancellationToken cancellationToken)
        {
            manager.ThrowIfNull(nameof(manager));
            await PositioningNamespaceMetadata.EnsureAsync(
                manager,
                cancellationToken).ConfigureAwait(false);
            var context = new PositioningServerContext(
                manager,
                m_globalProviders,
                m_relativeProviders,
                cancellationToken);

            for (int i = 0; i < m_configurators.Count; i++)
            {
                IPositioningPostSetupConfigurator configurator = m_configurators[i];
                if (configurator.TargetManagerType.IsAssignableFrom(manager.GetType()))
                {
                    await configurator.RunAsync(context).ConfigureAwait(false);
                }
            }
        }
    }

    internal static class PositioningNamespaceMetadata
    {
        private static readonly ConditionalWeakTable<
            IServerInternal,
            HashSet<NodeId>> s_linkedMetadata = new();
        private static readonly Lock s_linkedMetadataLock = new();

        public static async ValueTask EnsureAsync(
            AsyncCustomNodeManager manager,
            CancellationToken cancellationToken)
        {
            IConfigurationNodeManager? configurationNodeManager =
                manager.Server.NodeManager.ConfigurationNodeManager;
            if (configurationNodeManager == null)
            {
                return;
            }

            await EnsureAsync(
                manager,
                configurationNodeManager,
                Opc.Ua.Rsl.Namespaces.RSL,
                "1.00.1",
                new DateTime(2023, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                cancellationToken).ConfigureAwait(false);
            await EnsureAsync(
                manager,
                configurationNodeManager,
                Opc.Ua.Gpos.Namespaces.GPOS,
                "1.0.0",
                new DateTime(2025, 9, 25, 0, 0, 0, DateTimeKind.Utc),
                cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask EnsureAsync(
            AsyncCustomNodeManager manager,
            IConfigurationNodeManager configurationNodeManager,
            string namespaceUri,
            string namespaceVersion,
            DateTime publicationDate,
            CancellationToken cancellationToken)
        {
            NamespaceMetadataState metadata =
                await configurationNodeManager.CreateNamespaceMetadataStateAsync(
                    namespaceUri,
                    cancellationToken).ConfigureAwait(false);
            ushort namespaceIndex = (ushort)manager.Server.NamespaceUris.GetIndex(
                namespaceUri);
            metadata.BrowseName = new QualifiedName(
                namespaceUri,
                namespaceIndex);
            metadata.DisplayName = LocalizedText.From(namespaceUri);
            metadata.NamespaceUri!.Value = namespaceUri;
            metadata.NamespaceVersion!.Value = namespaceVersion;
            metadata.NamespacePublicationDate!.Value =
                new DateTimeUtc(publicationDate);
            metadata.IsNamespaceSubset!.Value = false;
            if (MarkMetadataLinked(manager.Server, metadata.NodeId))
            {
                if (!metadata.ReferenceExists(
                    ReferenceTypeIds.HasComponent,
                    isInverse: true,
                    ObjectIds.Server_Namespaces))
                {
                    metadata.AddReference(
                        ReferenceTypeIds.HasComponent,
                        isInverse: true,
                        ObjectIds.Server_Namespaces);
                }
                await manager.Server.NodeManager.AddReferencesAsync(
                    ObjectIds.Server_Namespaces,
                    new List<IReference>
                    {
                        new NodeStateReference(
                            ReferenceTypeIds.HasComponent,
                            isInverse: false,
                            metadata.NodeId)
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            await metadata.ClearChangeMasksAsync(
                manager.SystemContext,
                includeChildren: true,
                cancellationToken).ConfigureAwait(false);
        }

        private static bool MarkMetadataLinked(
            IServerInternal server,
            NodeId metadataId)
        {
            lock (s_linkedMetadataLock)
            {
                return s_linkedMetadata.GetOrCreateValue(server).Add(metadataId);
            }
        }
    }
}
