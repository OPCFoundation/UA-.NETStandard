/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.EndpointRegistry.Server;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Server;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.EndpointRegistry.PubSub
{
    /// <summary>
    /// Lifecycle-safe OPC 30455 PubSub Binding facet for an existing Endpoint Registry and
    /// PubSub server. Registry state owns metadata and epochs; the PubSub view owns source
    /// Objects and native configuration. This service never starts a transport.
    /// </summary>
    public sealed partial class EndpointRegistryPubSubBinding :
        IPubSubConfigurationViewObserver, IRegistryStateObserver, IAsyncDisposable
    {
        /// <summary>
        /// Constructs the facet directly. Call <see cref="StartAsync"/> after both node managers
        /// have created their address spaces. The service does not own either manager.
        /// </summary>
        public EndpointRegistryPubSubBinding(
            EndpointRegistryNodeManager registry,
            PubSubNodeManager pubsub,
            IServiceMessageContext messageContext,
            ITelemetryContext telemetry,
            EndpointRegistryPubSubBindingOptions? options = null)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_pubsub = pubsub ?? throw new ArgumentNullException(nameof(pubsub));
            if (pubsub.Application is not Opc.Ua.PubSub.Application.IPubSubConfigurationLifecycle)
            {
                throw new ArgumentException("The binding facet requires pre-retirement application lifecycle notifications.",
                    nameof(pubsub));
            }
            m_context = messageContext ?? throw new ArgumentNullException(nameof(messageContext));
            m_logger = (telemetry ?? throw new ArgumentNullException(nameof(telemetry)))
                .CreateLogger<EndpointRegistryPubSubBinding>();
            m_options = options ?? new EndpointRegistryPubSubBindingOptions();
            m_host = registry.Generic ?? throw new InvalidOperationException("The generic Endpoint Registry must be initialized.");
            m_localProvider = m_host.CreateProvider("endpoints", m_options.LocalIdentifierPrefix);
            m_remoteProvider = m_host.CreateProvider("endpoints", m_options.RemoteIdentifierPrefix);
            InitializeRemoteBindings();
        }

        /// <summary>Returns private copies of the current verified native binding snapshots.</summary>
        public ArrayOf<PubSubBindingSnapshotDataType> Snapshots
        {
            get
            {
                lock (m_gate)
                {
                    var result = new PubSubBindingSnapshotDataType[m_snapshots.Count];
                    for (int index = 0; index < result.Length; index++)
                    {
                        result[index] = (PubSubBindingSnapshotDataType)m_snapshots[index].Clone();
                    }
                    return result;
                }
            }
        }

        /// <summary>Attaches to real source lifetimes and the shared registry commit path.</summary>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            if (m_stopping)
            {
                throw new ObjectDisposedException(nameof(EndpointRegistryPubSubBinding));
            }
            if (m_started)
            {
                throw new InvalidOperationException("The binding is already started.");
            }
            m_started = true;
            m_host.AddObserver(this);
            m_pubsub.Application.MetaDataRegistry.MetaDataChanged += OnMetaDataChanged;
            await m_pubsub.AddViewObserverAsync(this, cancellationToken).ConfigureAwait(false);
            m_expiryTimer = m_options.TimeProvider.CreateTimer(
                static state => ((EndpointRegistryPubSubBinding)state!).QueueExpirySweep(), this,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// Revalidates schema associations and metadata against the active native generation.
        /// Schema registration remains the responsibility of the configured provider.
        /// </summary>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            PubSubConfigurationView? view;
            lock (m_gate)
            {
                view = m_view;
            }
            if (view is not null)
            {
                await SurfaceLocalAsync(view, cancellationToken).ConfigureAwait(false);
            }
            await RefreshReferencesAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask RetiringAsync(CancellationToken cancellationToken)
        {
            lock (m_gate)
            {
                m_view = null;
                m_invalidationVersion++;
                InvalidateReferences();
            }
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask ActivatedAsync(PubSubConfigurationView view, CancellationToken cancellationToken)
        {
            lock (m_gate)
            {
                m_view = view;
            }
            await SurfaceLocalAsync(view, cancellationToken).ConfigureAwait(false);
            await RefreshReferencesAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask ActivatingAsync(RegistryCommittedState state, CancellationToken cancellationToken)
        {
            lock (m_gate)
            {
                m_invalidationVersion++;
                InvalidateReferences();
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask ActivatedAsync(RegistryCommittedState state, CancellationToken cancellationToken)
        {
            return RefreshReferencesAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                m_stopping = true;
            }
            m_expiryTimer?.Dispose();
            if (m_expiryTimer is { } timer)
            {
                await timer.DisposeAsync().ConfigureAwait(false);
            }
            await m_expiryTask.ConfigureAwait(false);
            await m_metadataUpdateTask.ConfigureAwait(false);
            if (m_started)
            {
                m_pubsub.Application.MetaDataRegistry.MetaDataChanged -= OnMetaDataChanged;
                await m_pubsub.RemoveViewObserverAsync(this).ConfigureAwait(false);
                m_host.RemoveObserver(this);
                m_started = false;
            }
            m_reconcileGate.Dispose();
            m_referenceGate.Dispose();
            m_remoteGate.Dispose();
        }

        private async ValueTask SurfaceLocalAsync(PubSubConfigurationView view, CancellationToken cancellationToken)
        {
            if (!m_options.SurfaceLocalPublishers)
            {
                return;
            }
            await m_reconcileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (m_gate)
                {
                    if (m_view != view)
                    {
                        return;
                    }
                    m_updatingLocal = true;
                    m_invalidationVersion++;
                    InvalidateReferences();
                }
                var desired = new HashSet<string>(StringComparer.Ordinal);
                PubSubConfigurationDataType configuration = view.Configuration;
                foreach (PubSubAddressSpaceTarget target in view.Targets.ToArray() ?? [])
                {
                    if (target.Reader is not null || target.WriterGroup is not { } group ||
                        target.Writer is { } writer && !PubSubBindingRules.OwnQueue(writer))
                    {
                        continue;
                    }
                    PubSubConnectionDataType connection = target.Connection;
                    Facts facts = FactsOf(target, configuration);
                    string id = PubSubSurfaceBuilder.Identifier(m_options.LocalIdentifierPrefix,
                        PortableSource(target).ToString());
                    string xid = "/endpoints/" + id;
                    var messages = new List<RegistryMemberDataType>();
                    foreach (DataSetWriterDataType component in SelectedWriters(group, target.Writer))
                    {
                        if (PubSubBindingRules.Queue(group, component).Queue != PubSubBindingRules.Queue(group, target.Writer).Queue)
                        {
                            continue;
                        }
                        string messageId = "writer-" + component.DataSetWriterId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        SchemaReferenceDataType? schema = SchemaOf(connection, group, component, configuration);
                        RegistryObjectValueDataType message = PubSubSurfaceBuilder.Message(messageId, connection, group, component, schema);
                        if (PubSubBindingRules.CheckMessage(message, connection, group, component).Count == 0)
                        {
                            messages.Add(PubSubSurfaceBuilder.Member(messageId, message));
                        }
                    }
                    try
                    {
                        RegistryObjectValueDataType endpoint = PubSubSurfaceBuilder.Endpoint(id, connection, group,
                            target.Writer, messages.ToArray(), PubSubSurfaceBuilder.Fingerprint(m_context, connection,
                                facts.DataSets, facts.MetaData));
                        if (PubSubBindingRules.CheckEndpoint(endpoint, connection, group, target.Writer).Count != 0)
                        {
                            continue;
                        }
                        RegistryMutationResultDataType result = await m_localProvider.ReplaceAsync(xid,
                            m_host.Mapper.Project(endpoint, nameof(EndpointDataType)), cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                        EnsureCommitted(result);
                        desired.Add(xid);
                    }
                    catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadInvalidArgument)
                    {
                        m_logger.SourceNotSurfaced(PortableSource(target).ToString(), error);
                    }
                }
                foreach (string xid in ProviderPaths(m_options.LocalIdentifierPrefix))
                {
                    if (!desired.Contains(xid))
                    {
                        EnsureCommitted(await m_localProvider.DeleteAsync(xid, cancellationToken: cancellationToken)
                            .ConfigureAwait(false));
                    }
                }
            }
            finally
            {
                lock (m_gate)
                {
                    m_updatingLocal = false;
                }
                m_reconcileGate.Release();
            }
        }

        private async ValueTask RefreshReferencesAsync(CancellationToken cancellationToken)
        {
            await m_referenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                PubSubConfigurationView? view;
                long verification;
                lock (m_gate)
                {
                    InvalidateReferences();
                    view = m_updatingLocal ? null : m_view;
                    verification = m_invalidationVersion;
                }
                if (view is null)
                {
                    return;
                }
                RegistryObjectValueDataType document = m_host.Current.CloneDocument();
                PubSubConfigurationDataType configuration = view.Configuration;
                var endpointSnapshots = new Dictionary<BaseObjectState, List<PubSubBindingSnapshotDataType>>();
                var snapshots = new List<PubSubBindingSnapshotDataType>();
                foreach (PubSubAddressSpaceTarget target in view.Targets)
                {
                    PubSubConnectionDataType connection = target.Connection;
                    Facts facts = FactsOf(target, configuration);
                    foreach (RegistryMemberDataType entry in Groups(document, "endpoints"))
                    {
                        if (entry.Value is not RegistryObjectValueDataType endpoint ||
                            PubSubBindingRules.CheckEndpoint(endpoint, connection, target.WriterGroup, target.Writer, target.Reader).Count != 0 ||
                            !Authorized(target, endpoint))
                        {
                            continue;
                        }
                        string endpointXid = "/endpoints/" + entry.Name;
                        BaseObjectState? endpointNode = m_registry.FindCatalogObject(endpointXid);
                        RegistryEntityReferenceDataType? endpointReference = m_registry.FindCatalogReference(endpointXid, "Group");
                        if (endpointNode is null || endpointReference is null)
                        {
                            continue;
                        }
                        PubSubBindingSnapshotDataType snapshot = Snapshot(target, endpointReference, null, facts);
                        snapshots.Add(snapshot);
                        if (!endpointSnapshots.TryGetValue(endpointNode, out List<PubSubBindingSnapshotDataType>? list))
                        {
                            list = [];
                            endpointSnapshots.Add(endpointNode, list);
                        }
                        list.Add(snapshot);
                        if (facts.Issues.Count == 0)
                        {
                            AddBindingReference(view, verification, target.Node, endpointNode, target.Reader is null
                                ? ReferenceTypeIds.PublishesTo : ReferenceTypeIds.SubscribesTo);
                        }
                    }
                    if (target.Writer is null && target.Reader is null)
                    {
                        continue;
                    }
                    foreach (string collection in new[] { "endpoints", "messagegroups" })
                    {
                        foreach (RegistryMemberDataType group in Groups(document, collection))
                        {
                            if (group.Value is not RegistryObjectValueDataType container ||
                                PubSubBindingRules.Get(container, "messages") is not RegistryObjectValueDataType messages)
                            {
                                continue;
                            }
                            foreach (RegistryMemberDataType item in messages.Members)
                            {
                                if (item.Value is not RegistryObjectValueDataType message ||
                                    PubSubBindingRules.CheckMessage(message, connection, target.WriterGroup,
                                        target.Writer, target.Reader, container).Count != 0)
                                {
                                    continue;
                                }
                                string xid = "/" + collection + "/" + group.Name + "/messages/" + item.Name;
                                BaseObjectState? messageNode = m_registry.FindCatalogObject(xid);
                                RegistryEntityReferenceDataType? messageReference = m_registry.FindCatalogReference(xid, "Resource");
                                RegistryEntityReferenceDataType? endpointReference = collection == "endpoints"
                                    ? m_registry.FindCatalogReference("/endpoints/" + group.Name, "Group") : null;
                                if (messageNode is null || messageReference is null || !SchemaMatches(message, facts.Schema))
                                {
                                    continue;
                                }
                                PubSubBindingSnapshotDataType snapshot = Snapshot(target, endpointReference, messageReference, facts);
                                snapshots.Add(snapshot);
                                if (endpointReference is not null &&
                                    m_registry.FindCatalogObject(endpointReference.Xid!) is { } endpointNode)
                                {
                                    if (!endpointSnapshots.TryGetValue(endpointNode, out List<PubSubBindingSnapshotDataType>? list))
                                    {
                                        list = [];
                                        endpointSnapshots.Add(endpointNode, list);
                                    }
                                    list.Add(snapshot);
                                }
                                if (facts.Issues.Count == 0)
                                {
                                    AddBindingReference(view, verification, target.Node, messageNode, ReferenceTypeIds.HasMessageDefinition);
                                }
                            }
                        }
                    }
                }
                foreach (KeyValuePair<BaseObjectState, List<PubSubBindingSnapshotDataType>> item in endpointSnapshots)
                {
                    lock (m_gate)
                    {
                        if (m_view != view || m_invalidationVersion != verification)
                        {
                            return;
                        }
                        m_snapshotNodes.Add(item.Key);
                    }
                    await m_registry.SetPubSubBindingsAsync(item.Key, item.Value.ToArray(), cancellationToken)
                        .ConfigureAwait(false);
                    lock (m_gate)
                    {
                        if (m_view != view || m_invalidationVersion != verification)
                        {
                            if (item.Key is EndpointGroupState { PubSubBindings: { } property })
                            {
                                property.Value = [];
                            }
                            return;
                        }
                    }
                }
                lock (m_gate)
                {
                    if (m_view == view && m_invalidationVersion == verification)
                    {
                        m_snapshots = snapshots.ToArray();
                        m_snapshotNodes = [.. endpointSnapshots.Keys];
                    }
                }
            }
            finally
            {
                m_referenceGate.Release();
            }
        }

        private Facts FactsOf(PubSubAddressSpaceTarget target, PubSubConfigurationDataType configuration)
        {
            var datasets = new List<PublishedDataSetDataType>();
            var metadata = new List<DataSetMetaDataType>();
            var issues = new List<RegistryDiagnosticDataType>();
            SchemaReferenceDataType? schema = null;
            PubSubConnectionDataType connection = target.Connection;
            if (target.Reader is { } reader)
            {
                if (reader.DataSetMetaData is not { } expected || expected.ConfigurationVersion is null)
                {
                    issues.Add(PubSubBindingRules.Issue("M_METADATA", "DataSetReader/DataSetMetaData",
                        "Reader metadata and configuration versions are required."));
                }
                else
                {
                    metadata.Add(expected);
                    var key = new DataSetMetaDataKey(PublisherId.From(reader.PublisherId), reader.WriterGroupId,
                        reader.DataSetWriterId, expected.DataSetClassId, expected.ConfigurationVersion.MajorVersion);
                    MetaDataMatchResult match = m_pubsub.Application.MetaDataRegistry.TryGet(key, out DataSetMetaDataType? actual);
                    if (match != MetaDataMatchResult.NotFound && (match != MetaDataMatchResult.Match || !Utils.IsEqual(expected, actual)))
                    {
                        issues.Add(PubSubBindingRules.Issue("E_METADATA_VERSION", "DataSetReader/DataSetMetaData",
                            "Runtime and configured metadata must agree exactly."));
                    }
                    schema = Association(key, expected, issues);
                }
            }
            else if (target.WriterGroup is { } group)
            {
                foreach (DataSetWriterDataType writer in SelectedWriters(group, target.Writer))
                {
                    PublishedDataSetDataType? dataset = FindDataSet(configuration, writer.DataSetName);
                    if (dataset?.DataSetMetaData is not { } expected || expected.ConfigurationVersion is null)
                    {
                        issues.Add(PubSubBindingRules.Issue("M_METADATA", "PublishedDataSets",
                            "The selected writer's PublishedDataSet and metadata are required."));
                        continue;
                    }
                    datasets.Add(dataset);
                    metadata.Add(expected);
                    DataSetMetaDataKey key = PubSubSurfaceBuilder.Key(connection, group, writer, expected);
                    if (m_pubsub.Application.MetaDataRegistry.TryGet(key, out DataSetMetaDataType? actual) != MetaDataMatchResult.Match ||
                        !Utils.IsEqual(expected, actual))
                    {
                        issues.Add(PubSubBindingRules.Issue("E_METADATA_VERSION", "PublishedDataSets/DataSetMetaData",
                            "Runtime and configured metadata must agree, including MajorVersion and MinorVersion."));
                    }
                    SchemaReferenceDataType? association = Association(key, expected, issues);
                    if (target.Writer is not null)
                    {
                        schema = association;
                    }
                }
            }
            return new Facts(datasets.ToArray(), metadata.ToArray(), schema, issues.ToArray());
        }

        private SchemaReferenceDataType? Association(
            DataSetMetaDataKey key, DataSetMetaDataType expected, List<RegistryDiagnosticDataType> issues)
        {
            PubSubSchemaAssociation? association = m_options.Schemas?.FindSchema(key, (DataSetMetaDataType)expected.Clone());
            if (association is null)
            {
                return null;
            }
            if (!Utils.IsEqual(expected, association.MetaData))
            {
                issues.Add(PubSubBindingRules.Issue("E_SCHEMA_METADATA_VERSION", "schema", "The schema association describes different DataSet metadata."));
                return null;
            }
            return association.Schema;
        }

        private SchemaReferenceDataType? SchemaOf(
            PubSubConnectionDataType connection, WriterGroupDataType group, DataSetWriterDataType writer,
            PubSubConfigurationDataType configuration)
        {
            DataSetMetaDataType? metadata = FindDataSet(configuration, writer.DataSetName)?.DataSetMetaData;
            return metadata is null ? null : Association(PubSubSurfaceBuilder.Key(connection, group, writer, metadata),
                metadata, []);
        }

        private PubSubBindingSnapshotDataType Snapshot(
            PubSubAddressSpaceTarget target, RegistryEntityReferenceDataType? endpoint,
            RegistryEntityReferenceDataType? message, Facts facts)
        {
            return new PubSubBindingSnapshotDataType
            {
                Source = PortableSource(target),
                Endpoint = endpoint ?? new RegistryEntityReferenceDataType(),
                Message = message ?? new RegistryEntityReferenceDataType(),
                Schema = facts.Schema!,
                Configuration = target.Connection,
                PublishedDataSets = facts.DataSets,
                DataSetMetaData = facts.MetaData,
                CompleteConfiguration = facts.Issues.Count == 0,
                Issues = facts.Issues
            };
        }

        private static ExpandedNodeId PortableSource(PubSubAddressSpaceTarget target)
        {
            return target.Source;
        }

        private bool Authorized(PubSubAddressSpaceTarget target, RegistryObjectValueDataType endpoint)
        {
            return PubSubBindingRules.Get(endpoint, "authorization") is null ||
                m_options.AuthorizeBinding?.Invoke(target, endpoint) == true;
        }

        private void AddBindingReference(
            PubSubConfigurationView view, long verification, BaseObjectState source, BaseObjectState target, ExpandedNodeId reference)
        {
            lock (m_gate)
            {
                if (m_view != view || m_updatingLocal || m_invalidationVersion != verification)
                {
                    return;
                }
                NodeId type = ExpandedNodeId.ToNodeId(reference, m_context.NamespaceUris);
                foreach (BindingReference existing in m_references)
                {
                    if (existing.Source == source && existing.Target == target && existing.Type == type)
                    {
                        return;
                    }
                }
                source.AddReference(type, false, target.NodeId);
                target.AddReference(type, true, source.NodeId);
                m_references.Add(new BindingReference(source, target, type));
            }
        }

        private void InvalidateReferences()
        {
            foreach (BindingReference reference in m_references)
            {
                reference.Source.RemoveReference(reference.Type, false, reference.Target.NodeId);
                reference.Target.RemoveReference(reference.Type, true, reference.Source.NodeId);
            }
            m_references.Clear();
            m_snapshots = [];
            foreach (BaseObjectState node in m_snapshotNodes)
            {
                if (node is EndpointGroupState { PubSubBindings: { } property })
                {
                    property.Value = [];
                }
            }
            m_snapshotNodes.Clear();
        }

        private void OnMetaDataChanged(object? sender, DataSetMetaDataChangedEventArgs args)
        {
            lock (m_gate)
            {
                // Runtime metadata can change independently. Never leave a previously verified
                // association visible until an asynchronous refresh has revalidated it.
                m_invalidationVersion++;
                InvalidateReferences();
                if (!m_stopping)
                {
                    m_metadataUpdatePending = true;
                    if (!m_metadataWorkerActive)
                    {
                        m_metadataWorkerActive = true;
                        m_metadataUpdateTask = RefreshMetadataLoopAsync();
                    }
                }
            }
        }

        private async Task RefreshMetadataLoopAsync()
        {
            await Task.Yield();
            while (true)
            {
                lock (m_gate)
                {
                    if (m_stopping || !m_metadataUpdatePending)
                    {
                        m_metadataWorkerActive = false;
                        return;
                    }
                    m_metadataUpdatePending = false;
                }
                try
                {
                    await RefreshReferencesAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    m_logger.BackgroundRefreshFailed(error);
                }
            }
        }

        private void QueueExpirySweep()
        {
            lock (m_gate)
            {
                if (!m_stopping && m_expiryTask.IsCompleted && m_registry.Generic == m_host)
                {
                    m_expiryTask = SweepExpiryAsync();
                }
            }
        }

        private async Task SweepExpiryAsync()
        {
            await Task.Yield();
            try
            {
                await ExpireRemoteAsync(m_options.TimeProvider.GetUtcNow()).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                m_logger.BackgroundRefreshFailed(error);
            }
        }

        private static bool SchemaMatches(RegistryObjectValueDataType message, SchemaReferenceDataType? schema)
        {
            string? uri = PubSubBindingRules.Text(message, "dataschemauri");
            string? xid = PubSubBindingRules.Text(message, "dataschemaxid");
            string? format = PubSubBindingRules.Text(message, "dataschemaformat");
            if (uri is null && xid is null && PubSubBindingRules.Get(message, "dataschema") is null)
            {
                return true;
            }
            return schema is not null && format == schema.Format &&
                (uri is not null ? uri == schema.SelectedObjectUri || uri == schema.EntityUri :
                    xid is not null && xid == schema.Entity?.Xid);
        }

        private static PublishedDataSetDataType? FindDataSet(PubSubConfigurationDataType configuration, string? name)
        {
            PublishedDataSetDataType? found = null;
            foreach (PublishedDataSetDataType dataset in configuration.PublishedDataSets)
            {
                if (dataset.Name == name)
                {
                    if (found is not null)
                    {
                        return null;
                    }
                    found = dataset;
                }
            }
            return found;
        }

        private static ArrayOf<DataSetWriterDataType> SelectedWriters(WriterGroupDataType group, DataSetWriterDataType? writer)
        {
            if (writer is not null)
            {
                return [writer];
            }
            var writers = new List<DataSetWriterDataType>();
            foreach (DataSetWriterDataType component in group.DataSetWriters)
            {
                if (!PubSubBindingRules.OwnQueue(component))
                {
                    writers.Add(component);
                }
            }
            return writers.ToArray();
        }

        private static ArrayOf<RegistryMemberDataType> Groups(RegistryObjectValueDataType document, string collection)
        {
            return (PubSubBindingRules.Get(document, collection) as RegistryObjectValueDataType)?.Members ?? [];
        }

        private string[] ProviderPaths(string prefix)
        {
            var paths = new List<string>();
            foreach (RegistryMemberDataType entry in Groups(m_host.Current.CloneDocument(), "endpoints"))
            {
                if (entry.Name!.StartsWith(prefix, StringComparison.Ordinal))
                {
                    paths.Add("/endpoints/" + entry.Name);
                }
            }
            return paths.ToArray();
        }

        private static void EnsureCommitted(RegistryMutationResultDataType result)
        {
            if (!StatusCode.IsGood(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode,
                    result.Issues.Count == 0 ? "Provider commit failed." : result.Issues[0].Detail ?? "Provider commit failed.");
            }
        }

        private sealed record Facts(
            ArrayOf<PublishedDataSetDataType> DataSets,
            ArrayOf<DataSetMetaDataType> MetaData,
            SchemaReferenceDataType? Schema,
            ArrayOf<RegistryDiagnosticDataType> Issues);

        private sealed record BindingReference(BaseObjectState Source, BaseObjectState Target, NodeId Type);

        private readonly EndpointRegistryNodeManager m_registry;
        private readonly PubSubNodeManager m_pubsub;
        private readonly IServiceMessageContext m_context;
        private readonly ILogger m_logger;
        private readonly EndpointRegistryPubSubBindingOptions m_options;
        private readonly RegistryNativeHost m_host;
        private readonly RegistryNativeProvider m_localProvider;
        private readonly RegistryNativeProvider m_remoteProvider;
        private readonly Lock m_gate = new();
        private readonly SemaphoreSlim m_reconcileGate = new(1, 1);
        private readonly SemaphoreSlim m_referenceGate = new(1, 1);
        private readonly SemaphoreSlim m_remoteGate = new(1, 1);
        private readonly List<BindingReference> m_references = [];
        private List<BaseObjectState> m_snapshotNodes = [];
        private ArrayOf<PubSubBindingSnapshotDataType> m_snapshots;
        private PubSubConfigurationView? m_view;
        private bool m_started;
        private bool m_updatingLocal;
        private long m_invalidationVersion;
        private bool m_stopping;
        private bool m_metadataUpdatePending;
        private bool m_metadataWorkerActive;
        private Task m_metadataUpdateTask = Task.CompletedTask;
        private Task m_expiryTask = Task.CompletedTask;
        private ITimer? m_expiryTimer;
    }

}
