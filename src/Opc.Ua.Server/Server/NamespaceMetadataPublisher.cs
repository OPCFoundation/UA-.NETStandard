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
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Publishes a <c>NamespaceMetadataType</c> Object under
    /// <c>Server/Namespaces</c> for every namespace the server exposes in its
    /// <c>NamespaceArray</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OPC 10000-5 requires the <c>Namespaces</c> Object to describe the
    /// namespaces a Server provides, and every companion specification repeats
    /// the requirement in its own "Namespace Metadata" clause. Clients rely on
    /// <c>NamespaceVersion</c> and <c>NamespacePublicationDate</c> to decide
    /// whether their cached model matches the one the Server exposes, so a
    /// namespace without metadata cannot be version-checked at all.
    /// </para>
    /// <para>
    /// Models sourced from a ModelDesign carry their metadata Object in the
    /// model itself, but models source-generated from a NodeSet2 file do not,
    /// and neither does the Server's own application namespace. This publisher
    /// closes that gap uniformly: it walks the <c>NamespaceArray</c>, asks the
    /// <see cref="IConfigurationNodeManager"/> for (or creates) the metadata
    /// Object of each namespace, and fills in the version and publication date
    /// from the <see cref="ModelDependencyAttribute"/> that the source
    /// generator stamps onto every assembly emitting or consuming a model.
    /// </para>
    /// <para>
    /// Publishing is idempotent: namespaces that already carry a metadata
    /// Object keep it, and values that the model already populated are never
    /// overwritten.
    /// </para>
    /// </remarks>
    public sealed class NamespaceMetadataPublisher
    {
        /// <summary>
        /// Initialises the publisher.
        /// </summary>
        /// <param name="server">The server whose namespaces are described.</param>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is <c>null</c>.</exception>
        public NamespaceMetadataPublisher(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_logger = server.Telemetry.CreateLogger<NamespaceMetadataPublisher>();
        }

        /// <summary>
        /// Ensures every namespace in the server's <c>NamespaceArray</c> is
        /// described by a <c>NamespaceMetadataType</c> Object under
        /// <c>Server/Namespaces</c>.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        public async ValueTask PublishAsync(CancellationToken cancellationToken = default)
        {
            IConfigurationNodeManager? configuration = m_server.ConfigurationNodeManager;
            if (configuration == null)
            {
                return;
            }

            Dictionary<string, ModelDependencyAttribute> models = CollectModelMetadata();
            string[] namespaceUris = m_server.NamespaceUris.ToArray();
            var published = new List<NamespaceMetadataState>(namespaceUris.Length);

            for (int ii = 0; ii < namespaceUris.Length; ii++)
            {
                string namespaceUri = namespaceUris[ii];
                if (string.IsNullOrEmpty(namespaceUri))
                {
                    continue;
                }

                NamespaceMetadataState? metadata = await configuration
                    .CreateNamespaceMetadataStateAsync(namespaceUri, cancellationToken)
                    .ConfigureAwait(false);
                if (metadata == null)
                {
                    m_logger.NamespaceMetadataNotPublished(namespaceUri);
                    continue;
                }

                if (models.TryGetValue(namespaceUri, out ModelDependencyAttribute? model))
                {
                    ApplyModelMetadata(metadata, model);
                }

                published.Add(metadata);
                m_logger.NamespaceMetadataPublished((ushort)ii, namespaceUri);
            }

            await LinkUnreachableMetadataAsync(published, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Attaching the metadata Object as a child of the
        /// <c>Namespaces</c> Object only makes it browsable when the node
        /// manager that created it is also the one serving that Object. When
        /// another node manager owns it — which is the normal arrangement,
        /// because <c>Namespaces</c> lives in namespace 0 — the link has to be
        /// registered as a cross-manager reference instead. This runs after
        /// every metadata Object exists so nodes that are already reachable
        /// are never linked twice.
        /// </summary>
        private async ValueTask LinkUnreachableMetadataAsync(
            List<NamespaceMetadataState> published,
            CancellationToken cancellationToken)
        {
            if (published.Count == 0)
            {
                return;
            }

            HashSet<NodeId> reachable = await BrowseNamespacesAsync(cancellationToken)
                .ConfigureAwait(false);

            var missing = new List<IReference>();
            foreach (NamespaceMetadataState metadata in published)
            {
                if (metadata.NodeId.IsNull || reachable.Contains(metadata.NodeId))
                {
                    continue;
                }

                missing.Add(new ReferenceNode
                {
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    IsInverse = false,
                    TargetId = metadata.NodeId
                });
            }

            if (missing.Count == 0)
            {
                return;
            }

            await m_server.NodeManager.AddReferencesAsync(
                ObjectIds.Server_Namespaces,
                missing,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the NodeIds currently browsable from the <c>Namespaces</c>
        /// Object through the master node manager.
        /// </summary>
        private async ValueTask<HashSet<NodeId>> BrowseNamespacesAsync(
            CancellationToken cancellationToken)
        {
            ArrayOf<BrowseDescription> nodesToBrowse =
            [
                new BrowseDescription
                {
                    NodeId = ObjectIds.Server_Namespaces,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.None
                }
            ];

            using var context = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.Browse,
                RequestLifetime.None);

            (ArrayOf<BrowseResult> results, _) = await m_server.NodeManager.BrowseAsync(
                context,
                new ViewDescription(),
                0,
                nodesToBrowse,
                cancellationToken).ConfigureAwait(false);

            var reachable = new HashSet<NodeId>();
            if (results.Count == 0 || StatusCode.IsBad(results[0].StatusCode))
            {
                return reachable;
            }

            for (int ii = 0; ii < results[0].References.Count; ii++)
            {
                reachable.Add(ExpandedNodeId.ToNodeId(
                    results[0].References[ii].NodeId,
                    m_server.NamespaceUris));
            }
            return reachable;
        }

        /// <summary>
        /// Copies the version and publication date declared by the model onto
        /// the metadata Object, leaving values the model itself already
        /// published untouched.
        /// </summary>
        private static void ApplyModelMetadata(
            NamespaceMetadataState metadata,
            ModelDependencyAttribute model)
        {
            if (metadata.NamespaceVersion != null &&
                string.IsNullOrEmpty(metadata.NamespaceVersion.Value) &&
                !string.IsNullOrEmpty(model.Version))
            {
                metadata.NamespaceVersion.Value = model.Version;
            }

            if (metadata.NamespacePublicationDate != null &&
                metadata.NamespacePublicationDate.Value.IsNull &&
                TryParsePublicationDate(model.PublicationDate, out DateTime publicationDate))
            {
                metadata.NamespacePublicationDate.Value = DateTimeUtc.From(publicationDate);
            }
        }

        /// <summary>
        /// Parses the ISO-8601 publication date carried by the model attribute.
        /// </summary>
        private static bool TryParsePublicationDate(string? value, out DateTime publicationDate)
        {
            publicationDate = DateTime.MinValue;
            return !string.IsNullOrEmpty(value) &&
                DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out publicationDate);
        }

        /// <summary>
        /// Builds a model-URI keyed view of the model metadata declared by the
        /// assemblies that contribute node managers to this server. Entries
        /// that declare a publication date win over ones that do not, so a
        /// fully-described model is preferred over a transitive stub.
        /// </summary>
        private Dictionary<string, ModelDependencyAttribute> CollectModelMetadata()
        {
            var models = new Dictionary<string, ModelDependencyAttribute>(StringComparer.Ordinal);
            var visited = new HashSet<Assembly>();

            foreach (IAsyncNodeManager nodeManager in m_server.NodeManager.AsyncNodeManagers)
            {
                // A sync-native manager (CustomNodeManager2) exposes its
                // user-authored type through SyncNodeManager, while its async
                // facade is a framework wrapper. An async-native manager
                // (AsyncCustomNodeManager) is the reverse: it is the
                // user-authored type, and its SyncNodeManager is the framework
                // wrapper. Scanning both assemblies finds the assembly that
                // carries the ModelDependency attribute regardless of shape.
                ScanAssembly(nodeManager.GetType().Assembly, visited, models);
                INodeManager? sync = nodeManager.SyncNodeManager;
                if (sync != null)
                {
                    ScanAssembly(sync.GetType().Assembly, visited, models);
                }
            }

            return models;
        }

        /// <summary>
        /// Merges the <see cref="ModelDependencyAttribute"/> declarations of a
        /// single assembly into the accumulated model map, preferring entries
        /// that carry a publication date over ones that do not.
        /// </summary>
        private static void ScanAssembly(
            Assembly assembly,
            HashSet<Assembly> visited,
            Dictionary<string, ModelDependencyAttribute> models)
        {
            if (!visited.Add(assembly))
            {
                return;
            }

            foreach (ModelDependencyAttribute model in assembly
                .GetCustomAttributes<ModelDependencyAttribute>())
            {
                if (string.IsNullOrEmpty(model.ModelUri))
                {
                    continue;
                }

                if (!models.TryGetValue(model.ModelUri, out ModelDependencyAttribute? existing) ||
                    (string.IsNullOrEmpty(existing.PublicationDate) &&
                        !string.IsNullOrEmpty(model.PublicationDate)))
                {
                    models[model.ModelUri] = model;
                }
            }
        }

        private readonly IServerInternal m_server;
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="NamespaceMetadataPublisher"/>.
    /// </summary>
    internal static partial class NamespaceMetadataPublisherLog
    {
        [LoggerMessage(
            EventId = ServerEventIds.NamespaceMetadataPublisher + 0,
            Level = LogLevel.Debug,
            Message = "Published NamespaceMetadata for namespace {NamespaceIndex} ('{NamespaceUri}').")]
        public static partial void NamespaceMetadataPublished(
            this ILogger logger,
            ushort namespaceIndex,
            string namespaceUri);

        [LoggerMessage(
            EventId = ServerEventIds.NamespaceMetadataPublisher + 1,
            Level = LogLevel.Warning,
            Message = "Could not publish NamespaceMetadata for namespace '{NamespaceUri}'.")]
        public static partial void NamespaceMetadataNotPublished(
            this ILogger logger,
            string namespaceUri);
    }
}
