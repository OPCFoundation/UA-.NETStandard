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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gds.Server.AliasNames;
using Opc.Ua.Server;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// The GDS AliasName Server facet (OPC 10000-17 Annex C).
    /// </summary>
    /// <remarks>
    /// <para>
    /// When enabled (<see cref="GlobalDiscoveryServerConfiguration.EnableAliasNameAggregation"/>
    /// or <see cref="AliasNameAggregationEnabled"/>), every Server registered
    /// with the capability <c>ALIAS</c> (OPC 10000-12 Annex D) is read by an
    /// <see cref="IAliasNameSourceReader"/> before RegisterApplication
    /// returns (Annex C.2; at most <see cref="AliasNameRegistrationWait"/>,
    /// a slower read completes in the background), and its AliasNames are
    /// merged by the
    /// <see cref="AliasNameAggregator"/>. UnregisterApplication removes
    /// them again (Annex C.3).
    /// </para>
    /// <para>
    /// The merged list is exposed as nodes of the application-record
    /// namespace: an <c>AliasNameType</c> instance per alias, with
    /// <c>AliasFor</c> references to the ExpandedNodeIds of its targets on
    /// the registered Servers, organized by the well-known
    /// <c>Aliases</c>/<c>TagVariables</c>/<c>Topics</c> objects or by
    /// <c>AliasNameCategoryType</c> instances for the Servers' own
    /// categories. <c>FindAlias</c> on the well-known categories includes
    /// the merged list through the server's alias-name store registry.
    /// </para>
    /// </remarks>
    public partial class ApplicationsNodeManager
    {
        /// <summary>
        /// Whether the GDS aggregates the AliasNames of registered Servers.
        /// Initialized from
        /// <see cref="GlobalDiscoveryServerConfiguration.EnableAliasNameAggregation"/>;
        /// takes effect when the address space is created.
        /// </summary>
        public bool AliasNameAggregationEnabled { get; set; }

        /// <summary>
        /// Reads the AliasNames of registered Servers. Defaults to a
        /// <see cref="SessionAliasNameSourceReader"/> on the server's
        /// configuration.
        /// </summary>
        public IAliasNameSourceReader? AliasNameSourceReader { get; set; }

        /// <summary>
        /// The master AliasNames list, or <c>null</c> while the facet is
        /// disabled.
        /// </summary>
        public AliasNameAggregator? AliasNameAggregator => m_aliasAggregator;

        /// <summary>
        /// Maximum time to read the AliasNames of one Server. A Server that
        /// cannot be read in time stays registered without AliasNames until
        /// it is read again (UpdateApplication or the periodic refresh).
        /// </summary>
        public TimeSpan AliasNameSourceTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Maximum time RegisterApplication waits for the AliasNames of the
        /// registering Server before it returns; a read that takes longer
        /// completes in the background. Keeps RegisterApplication within
        /// the call timeouts of its clients when a Server is slow or
        /// unreachable.
        /// </summary>
        public TimeSpan AliasNameRegistrationWait { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Returns <c>true</c> if <paramref name="application"/> is a Server
        /// that announces the AliasName capability.
        /// </summary>
        public static bool IsAliasNameSource(ApplicationRecordDataType? application)
        {
            if (application == null ||
                application.ApplicationType == ApplicationType.Client ||
                application.DiscoveryUrls.Count == 0)
            {
                return false;
            }
            foreach (string capability in application.ServerCapabilities)
            {
                if (string.Equals(capability, AliasNameCapability, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Reads the AliasNames of <paramref name="applicationId"/> again and
        /// updates the merged list, or removes them when the application is
        /// gone or no longer an AliasName Server.
        /// </summary>
        /// <returns><c>false</c> if the source could not be read.</returns>
        public async ValueTask<bool> RefreshAliasNameSourceAsync(
            NodeId applicationId,
            CancellationToken cancellationToken = default)
        {
            AliasNameAggregator? aggregator = m_aliasAggregator;
            if (aggregator == null)
            {
                return false;
            }

            ApplicationRecordDataType? application = m_database.GetApplication(applicationId);
            if (!IsAliasNameSource(application))
            {
                await UpdateAliasNameNodesAsync(a => a.RemoveSource(applicationId), cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

            IAliasNameSourceReader reader = AliasNameSourceReader ??= new SessionAliasNameSourceReader(
                m_configuration, Server.Telemetry);
            AliasNameSourceSnapshot snapshot;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_aliasShutdown.Token))
            {
                timeout.CancelAfter(AliasNameSourceTimeout);
                try
                {
                    snapshot = await reader.ReadAsync(application!, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    m_logger.AliasNameSourceNotRead(application!.ApplicationUri, ex.Message);
                    return false;
                }
            }

            // The record may have been unregistered while it was read; the
            // check runs under the same lock as the unregister hook.
            await UpdateAliasNameNodesAsync(
                a =>
                {
                    if (m_database.GetApplication(applicationId) == null)
                    {
                        return false;
                    }
                    a.SetSource(applicationId, snapshot);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            m_logger.AliasNameSourceRead(
                application!.ApplicationUri, snapshot.Categories.Count, snapshot.Aliases.Count);
            return true;
        }

        /// <summary>
        /// Runs <see cref="RefreshAliasNameSourceAsync"/> in the background.
        /// The returned task never faults.
        /// </summary>
        private Task RefreshAliasNameSourceInBackground(NodeId applicationId)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await RefreshAliasNameSourceAsync(applicationId, m_aliasShutdown.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.AliasNameRefreshFailed(ex.Message);
                }
            });
        }

        /// <summary>
        /// Creates the aggregator and registers it with the server's
        /// alias-name store registry. Called while the address space is
        /// configured.
        /// </summary>
        private void InitializeAliasNameAggregation()
        {
            if (!AliasNameAggregationEnabled || m_aliasAggregator != null)
            {
                return;
            }

            m_aliasAggregator = new AliasNameAggregator(
                ApplicationsNamespaceIndex,
                Server.NamespaceUris,
                Server.ServerUris);
            m_aliasRegistry = (Server as IAliasNameStoreRegistryProvider)?.AliasNameStoreRegistry;
            m_aliasRegistry?.RegisterContributor(m_aliasAggregator);

            // Records restored from a persistent database are read once the
            // server runs; nodes cannot be added while the address space is
            // still being built.
            int refreshInterval = m_globalDiscoveryServerConfiguration.AliasNameRefreshInterval;
            _ = Task.Run(() => RunAliasNameRefreshAsync(
                refreshInterval > 0 ? TimeSpan.FromSeconds(refreshInterval) : Timeout.InfiniteTimeSpan,
                m_aliasShutdown.Token));
        }

        private async Task RunAliasNameRefreshAsync(TimeSpan interval, CancellationToken ct)
        {
            try
            {
                while (!Server.IsRunning)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
                }
                while (!ct.IsCancellationRequested)
                {
                    foreach (NodeId applicationId in FindAliasNameSourceIds())
                    {
                        await RefreshAliasNameSourceAsync(applicationId, ct).ConfigureAwait(false);
                    }
                    if (interval == Timeout.InfiniteTimeSpan)
                    {
                        return;
                    }
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                m_logger.AliasNameRefreshFailed(ex.Message);
            }
        }

        /// <summary>
        /// The registered applications that announce the AliasName
        /// capability, plus those aggregated so far (to drop stale ones).
        /// </summary>
        private List<NodeId> FindAliasNameSourceIds()
        {
            var ids = new List<NodeId>();
            uint nextRecordId = 0;
            do
            {
                ApplicationDescription[]? records = m_database.QueryApplications(
                    nextRecordId,
                    1000,
                    null!,
                    null!,
                    0,
                    null!,
                    [AliasNameCapability],
                    out DateTimeUtc _,
                    out nextRecordId);
                if (records == null)
                {
                    break;
                }
                foreach (ApplicationDescription record in records)
                {
                    ApplicationRecordDataType[]? matches = m_database.FindApplications(record.ApplicationUri!);
                    if (matches == null)
                    {
                        continue;
                    }
                    foreach (ApplicationRecordDataType match in matches)
                    {
                        if (IsAliasNameSource(match) && !ids.Contains(match.ApplicationId))
                        {
                            ids.Add(match.ApplicationId);
                        }
                    }
                }
            }
            while (nextRecordId != 0);

            if (m_aliasAggregator != null)
            {
                foreach (NodeId source in m_aliasAggregator.Sources)
                {
                    if (!ids.Contains(source))
                    {
                        ids.Add(source);
                    }
                }
            }
            return ids;
        }

        /// <summary>
        /// RegisterApplication hook (Annex C.2).
        /// </summary>
        private async ValueTask OnAliasNameSourceRegisteredAsync(
            NodeId applicationId,
            ApplicationRecordDataType application,
            CancellationToken cancellationToken)
        {
            if (m_aliasAggregator != null && IsAliasNameSource(application))
            {
                Task read = RefreshAliasNameSourceInBackground(applicationId);
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task wait = Task.Delay(AliasNameRegistrationWait, waitCancellation.Token);
                if (await Task.WhenAny(read, wait).ConfigureAwait(false) == read)
                {
                    waitCancellation.Cancel();
                }
            }
        }

        /// <summary>
        /// UnregisterApplication hook (Annex C.3).
        /// </summary>
        private async ValueTask OnAliasNameSourceUnregisteredAsync(
            NodeId applicationId,
            CancellationToken cancellationToken)
        {
            if (m_aliasAggregator != null)
            {
                await UpdateAliasNameNodesAsync(a => a.RemoveSource(applicationId), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Applies <paramref name="change"/> to the merged list and, if it
        /// changed anything, brings the aggregated alias and category nodes
        /// in line with it. Changes and node updates are serialized.
        /// </summary>
        private async ValueTask UpdateAliasNameNodesAsync(
            Func<AliasNameAggregator, bool> change,
            CancellationToken cancellationToken)
        {
            AliasNameAggregator? aggregator = m_aliasAggregator;
            if (aggregator == null)
            {
                return;
            }

            await m_aliasSync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!change(aggregator))
                {
                    return;
                }

                AliasNameAggregateView view = aggregator.GetView();
                var categories = view.Categories.ToDictionary(c => c.NodeId);
                var aliases = view.Aliases.ToDictionary(a => a.NodeId);

                // Remove what is gone or has moved: aliases first, then
                // categories, children before their parents.
                foreach (KeyValuePair<NodeId, AliasNameNodeEntry> entry in m_aliasNodes.ToList())
                {
                    if (!entry.Value.IsCategory &&
                        (!aliases.TryGetValue(entry.Key, out AliasNameAggregateAlias? alias) ||
                            alias.CategoryId != entry.Value.ParentId))
                    {
                        await RemoveAliasNameNodeAsync(entry.Key, cancellationToken).ConfigureAwait(false);
                    }
                }
                foreach (KeyValuePair<NodeId, AliasNameNodeEntry> entry in m_aliasNodes.Reverse().ToList())
                {
                    if (entry.Value.IsCategory &&
                        (!categories.TryGetValue(entry.Key, out AliasNameAggregateCategory? category) ||
                            category.ParentId != entry.Value.ParentId))
                    {
                        await RemoveAliasNameNodeAsync(entry.Key, cancellationToken).ConfigureAwait(false);
                    }
                }

                foreach (AliasNameAggregateCategory category in view.Categories)
                {
                    if (!m_aliasNodes.ContainsKey(category.NodeId))
                    {
                        await AddAliasNameNodeAsync(
                            CreateCategoryNode(aggregator, category),
                            category.ParentId,
                            isCategory: true,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                foreach (AliasNameAggregateAlias alias in view.Aliases)
                {
                    if (!m_aliasNodes.TryGetValue(alias.NodeId, out AliasNameNodeEntry? entry))
                    {
                        entry = await AddAliasNameNodeAsync(
                            CreateAliasNode(aggregator, alias),
                            alias.CategoryId,
                            isCategory: false,
                            cancellationToken).ConfigureAwait(false);
                    }
                    UpdateAliasTargets(entry.Node, alias.Targets);
                }
            }
            finally
            {
                m_aliasSync.Release();
            }
        }

        private AliasNameCategoryState CreateCategoryNode(
            AliasNameAggregator aggregator,
            AliasNameAggregateCategory category)
        {
            AliasNameCategoryState node = SystemContext.CreateInstanceOfAliasNameCategoryType();
            node.BrowseName = new QualifiedName(category.Name, aggregator.NamespaceIndex);
            node.DisplayName = new LocalizedText(category.Name);
            node.SymbolicName = category.Name;
            node.TypeDefinitionId = Ua.ObjectTypeIds.AliasNameCategoryType;
            node.ReferenceTypeId = ReferenceTypeIds.Organizes;

            // Mint the ids of the FindAlias method and its arguments first,
            // then pin the category to the aggregator's id: assigning it
            // before the children would derive their ids from it.
            node.AssignNodeIds(SystemContext, []);
            node.NodeId = category.NodeId;

            NodeId categoryId = category.NodeId;
            ITypeTable typeTree = Server.TypeTree;
            node.FindAlias?.OnCallAsync = async (context, method, objectId, pattern, referenceTypeFilter, ct) =>
            {
                ServiceResult? invalid = ValidateFindAliasArguments(typeTree, pattern, referenceTypeFilter);
                if (invalid != null)
                {
                    return new FindAliasMethodStateResult { ServiceResult = invalid, AliasNodeList = [] };
                }
                try
                {
                    IReadOnlyList<AliasNameDataType> found = await aggregator
                        .FindAliasAsync(categoryId, pattern, referenceTypeFilter, typeTree, ct)
                        .ConfigureAwait(false);
                    return new FindAliasMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        AliasNodeList = found.ToArrayOf()
                    };
                }
                catch (ServiceResultException ex)
                {
                    return new FindAliasMethodStateResult { ServiceResult = ex.Result, AliasNodeList = [] };
                }
            };
            return node;
        }

        private AliasNameState CreateAliasNode(
            AliasNameAggregator aggregator,
            AliasNameAggregateAlias alias)
        {
            AliasNameState node = SystemContext.CreateInstanceOfAliasNameType();
            node.NodeId = alias.NodeId;
            node.BrowseName = new QualifiedName(alias.Name, aggregator.NamespaceIndex);
            node.DisplayName = new LocalizedText(string.Empty, alias.Name);
            node.SymbolicName = alias.Name;
            node.TypeDefinitionId = Ua.ObjectTypeIds.AliasNameType;
            node.ReferenceTypeId = ReferenceTypeIds.Organizes;
            return node;
        }

        /// <summary>
        /// Registers an aggregated node and the Organizes pair to its
        /// parent, which is one of the well-known categories in another
        /// node manager or an aggregated category of this one.
        /// </summary>
        private async ValueTask<AliasNameNodeEntry> AddAliasNameNodeAsync(
            BaseObjectState node,
            NodeId parentId,
            bool isCategory,
            CancellationToken cancellationToken)
        {
            node.AddReference(ReferenceTypeIds.Organizes, true, parentId);
            await AddPredefinedNodeAsync(SystemContext, node, cancellationToken).ConfigureAwait(false);

            if (m_aliasNodes.TryGetValue(parentId, out AliasNameNodeEntry? parent))
            {
                if (!parent.Node.ReferenceExists(ReferenceTypeIds.Organizes, false, node.NodeId))
                {
                    parent.Node.AddReference(ReferenceTypeIds.Organizes, false, node.NodeId);
                }
            }
            else
            {
                await Server.NodeManager.AddReferencesAsync(
                    parentId,
                    [new NodeStateReference(ReferenceTypeIds.Organizes, false, node.NodeId)],
                    cancellationToken).ConfigureAwait(false);
            }

            var entry = new AliasNameNodeEntry(node, parentId, isCategory);
            m_aliasNodes.Add(node.NodeId, entry);
            return entry;
        }

        private async ValueTask RemoveAliasNameNodeAsync(NodeId nodeId, CancellationToken cancellationToken)
        {
            m_aliasNodes.Remove(nodeId);
            // Removes the parent's Organizes reference too, whichever node
            // manager owns the parent.
            await DeleteNodeAsync(SystemContext, nodeId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Replaces the AliasFor references of an alias node with the
        /// merged targets; the ServerArray indexes in them change when a
        /// Server is removed.
        /// </summary>
        private void UpdateAliasTargets(BaseObjectState node, IReadOnlyList<AliasNameAggregateTarget> targets)
        {
            var references = new List<IReference>();
            node.GetReferences(SystemContext, references);
            bool changed = false;
            foreach (IReference reference in references)
            {
                if (reference.IsInverse ||
                    reference.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition ||
                    reference.ReferenceTypeId == ReferenceTypeIds.Organizes)
                {
                    continue;
                }
                if (!targets.Any(t => t.ReferenceTypeId == reference.ReferenceTypeId && t.Target == reference.TargetId))
                {
                    node.RemoveReference(reference.ReferenceTypeId, false, reference.TargetId);
                    changed = true;
                }
            }
            foreach (AliasNameAggregateTarget target in targets)
            {
                if (!node.ReferenceExists(target.ReferenceTypeId, false, target.Target))
                {
                    node.AddReference(target.ReferenceTypeId, false, target.Target);
                    changed = true;
                }
            }
            if (changed)
            {
                node.ClearChangeMasks(SystemContext, false);
            }
        }

        private static ServiceResult? ValidateFindAliasArguments(
            ITypeTable typeTree,
            string pattern,
            NodeId referenceTypeFilter)
        {
            if (!AliasNameWildcardMatcher.IsValidPattern(pattern))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "AliasNameSearchPattern is not a valid search string.");
            }
            if (!referenceTypeFilter.IsNull &&
                referenceTypeFilter != ReferenceTypeIds.References &&
                referenceTypeFilter != ReferenceTypeIds.AliasFor &&
                !typeTree.IsTypeOf(referenceTypeFilter, ReferenceTypeIds.AliasFor))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "ReferenceTypeFilter must be AliasFor or one of its subtypes.");
            }
            return null;
        }

        /// <summary>
        /// Stops the refresh and detaches the aggregator from the registry.
        /// </summary>
        private void DisposeAliasNameAggregation()
        {
            m_aliasShutdown.Cancel();
            if (m_aliasAggregator != null)
            {
                m_aliasRegistry?.UnregisterContributor(m_aliasAggregator);
            }
            m_aliasRegistry = null;
            m_aliasShutdown.Dispose();
            m_aliasSync.Dispose();
        }

        private sealed record AliasNameNodeEntry(BaseObjectState Node, NodeId ParentId, bool IsCategory);

        /// <summary>
        /// The ServerCapability identifier of an AliasName Server
        /// (OPC 10000-12 Annex D).
        /// </summary>
        private const string AliasNameCapability = "ALIAS";

        private AliasNameAggregator? m_aliasAggregator;
        private IAliasNameStoreRegistry? m_aliasRegistry;
        private readonly CancellationTokenSource m_aliasShutdown = new();
        private readonly SemaphoreSlim m_aliasSync = new(1, 1);

        /// <summary>
        /// The aggregated nodes in creation order (parents before children);
        /// only touched under <see cref="m_aliasSync"/>.
        /// </summary>
        private readonly OrderedAliasNodes m_aliasNodes = new();

        /// <summary>
        /// A dictionary that enumerates in insertion order.
        /// </summary>
        private sealed class OrderedAliasNodes : IEnumerable<KeyValuePair<NodeId, AliasNameNodeEntry>>
        {
            public bool ContainsKey(NodeId nodeId) => m_entries.ContainsKey(nodeId);

            public bool TryGetValue(NodeId nodeId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AliasNameNodeEntry? entry)
            {
                return m_entries.TryGetValue(nodeId, out entry);
            }

            public void Add(NodeId nodeId, AliasNameNodeEntry entry)
            {
                m_entries.Add(nodeId, entry);
                m_order.Add(nodeId);
            }

            public void Remove(NodeId nodeId)
            {
                if (m_entries.Remove(nodeId))
                {
                    m_order.Remove(nodeId);
                }
            }

            public IEnumerator<KeyValuePair<NodeId, AliasNameNodeEntry>> GetEnumerator()
            {
                foreach (NodeId nodeId in m_order)
                {
                    yield return new KeyValuePair<NodeId, AliasNameNodeEntry>(nodeId, m_entries[nodeId]);
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

            private readonly Dictionary<NodeId, AliasNameNodeEntry> m_entries = [];
            private readonly List<NodeId> m_order = [];
        }
    }

    /// <summary>
    /// Source-generated log messages for the GDS AliasName Server facet.
    /// </summary>
    internal static partial class ApplicationsNodeManagerAliasNamesLog
    {
        [LoggerMessage(EventId = GdsServerCommonEventIds.AliasNameAggregation + 5, Level = LogLevel.Information,
            Message = "AliasName source {ApplicationUri}: aggregated {Categories} categories and {Aliases} alias targets.")]
        public static partial void AliasNameSourceRead(
            this ILogger logger, string? applicationUri, int categories, int aliases);

        [LoggerMessage(EventId = GdsServerCommonEventIds.AliasNameAggregation + 6, Level = LogLevel.Warning,
            Message = "AliasName source {ApplicationUri} could not be read: {Error}")]
        public static partial void AliasNameSourceNotRead(this ILogger logger, string? applicationUri, string error);

        [LoggerMessage(EventId = GdsServerCommonEventIds.AliasNameAggregation + 7, Level = LogLevel.Error,
            Message = "AliasName refresh stopped: {Error}")]
        public static partial void AliasNameRefreshFailed(this ILogger logger, string error);
    }
}
