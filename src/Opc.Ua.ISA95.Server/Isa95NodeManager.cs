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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.ISA95.Server.Builders;
using Opc.Ua.ISA95.Server.Hosting;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;
using V2Extensions = Opc.Ua.ISA95.JobControl.V2.OpcUaISA95JobControlV2Extensions;

namespace Opc.Ua.ISA95.Server
{
    /// <summary>
    /// Hosts the OPC-10030 and OPC-10031-4 V1/V2 models.
    /// </summary>
    public sealed class Isa95NodeManager : FluentNodeManagerBase, INodeIdFactory
    {
        public Isa95NodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            Isa95ServerOptions options,
            Isa95ServerProviders providers,
            IReadOnlyList<IIsa95ModelConfigurator>? configurators = null)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<Isa95NodeManager>(),
                options.InstanceNamespaceUri,
                Namespaces.ISA95,
                V1.Namespaces.ISA95JobControlV1,
                V2.Namespaces.ISA95JobControlV2)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_providers = providers ?? throw new ArgumentNullException(nameof(providers));
            m_configurators = configurators ?? [];
            m_options.Validate();
            RegisterEncodeables(server.Factory);
        }

        public FolderState? Root { get; private set; }

        public ushort InstanceNamespaceIndex =>
            (ushort)Server.NamespaceUris.GetIndex(m_options.InstanceNamespaceUri);

        /// <summary>
        /// The shared Job Control V2 endpoint binder. The same binder wires the
        /// OPC 40001-3 <c>JobManagementType</c> endpoints, so the eleven job
        /// verbs behave identically whether a client reaches them through the
        /// stand-alone ISA-95 root or through a machine.
        /// </summary>
        /// <remarks>
        /// Created once on the startup thread by
        /// <see cref="CreateAddressSpaceAsync"/>, before anything that runs
        /// concurrently can reach it.
        /// </remarks>
        private Isa95JobControlV2Binder V2Binder =>
            m_v2Binder ?? throw ServiceResultException.Create(
                StatusCodes.BadInvalidState,
                "The ISA-95 address space has not been created yet.");

        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaISA95(context);
            V1.OpcUaISA95JobControlV1Extensions.AddOpcUaISA95JobControlV1(nodes, context);
            V2Extensions.AddOpcUaISA95JobControlV2(nodes, context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await LoadPredefinedNodesAsync(SystemContext, externalReferences, cancellationToken)
                .ConfigureAwait(false);

            m_v2Binder = new Isa95JobControlV2Binder(
                SystemContext,
                Server.NamespaceUris,
                RefreshJobOrderListsAsync);

            await CreateV2StatusEventTypeAsync(cancellationToken).ConfigureAwait(false);
            NodeManagerBuilder builder = CreateFluentBuilder(InstanceNamespaceIndex);
            Root = CreateRoot();
            CreateJobControlV1Endpoints(Root);
            CreateJobControlV2Endpoints(Root);

            // Staged once the endpoints are attached, so the whole subtree is
            // registered together and the reverse-reference pass publishes the
            // root's Organizes edge to the Objects folder.
            builder.Add(Root);
            await RegisterAuthoredNodesAsync(builder, cancellationToken).ConfigureAwait(false);
            await CompleteConfigureAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);
            await builder.SealAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureCommonModelAsync(Root, cancellationToken).ConfigureAwait(false);
            await ConfigureCatalogChangesAsync(cancellationToken).ConfigureAwait(false);
            await RefreshJobOrderListsAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureStatusEventsAsync(cancellationToken).ConfigureAwait(false);
        }

        public override async ValueTask DeleteAddressSpaceAsync(
            CancellationToken cancellationToken = default)
        {
            CancelCatalogChanges();
            await m_catalogChangesTask.ConfigureAwait(false);
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelCatalogChanges();
                if (Interlocked.Exchange(ref m_catalogChangesDisposed, 1) == 0)
                {
                    m_catalogChangesCts.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Builds the instance root with an explicit string NodeId, which stays
        /// the address clients browse. Staging it supplies the inverse
        /// Organizes reference to the Objects folder and publishes the matching
        /// forward edge through the reverse-reference pass.
        /// </summary>
        private FolderState CreateRoot()
        {
            return new FolderState(null)
            {
                SymbolicName = m_options.RootBrowseName,
                NodeId = new NodeId(m_options.RootBrowseName, InstanceNamespaceIndex),
                BrowseName = new QualifiedName(
                    m_options.RootBrowseName,
                    InstanceNamespaceIndex),
                DisplayName = new LocalizedText(m_options.RootBrowseName),
                TypeDefinitionId = Ua.ObjectTypeIds.FolderType,
                ReferenceTypeId = Ua.ReferenceTypeIds.Organizes
            };
        }

        private void CreateJobControlV1Endpoints(FolderState root)
        {
            if (!m_options.EnableJobControlV1)
            {
                return;
            }
            if (m_providers.JobOrderReceiverV1 != null)
            {
                m_v1OrderReceiver =
                    V1.OpcUaISA95JobControlV1Extensions
                        .CreateInstanceOfISA95JobOrderReceiverObjectType(
                            SystemContext,
                            root,
                            InstanceBrowseName(
                                $"{m_options.JobControlV1BrowseName}_JobOrderReceiver"));
                AddChild(root, m_v1OrderReceiver);
                WireV1OrderReceiver(m_v1OrderReceiver, m_providers.JobOrderReceiverV1);
                InitializeV1OrderVariables(m_v1OrderReceiver);
            }
            if (m_options.ExposeJobResponseProvider &&
                m_providers.JobResponseProviderV1 != null)
            {
                m_v1ResponseProvider =
                    V1.OpcUaISA95JobControlV1Extensions
                        .CreateInstanceOfISA95JobResponseProviderObjectType(
                            SystemContext,
                            root,
                            InstanceBrowseName(
                                $"{m_options.JobControlV1BrowseName}_JobResponseProvider"));
                AddChild(root, m_v1ResponseProvider);
                WireV1ResponseProvider(
                    m_v1ResponseProvider,
                    m_providers.JobResponseProviderV1);
            }
            if (m_options.ExposeJobResponseReceiver &&
                m_providers.JobResponseReceiverV1 != null)
            {
                m_v1ResponseReceiver =
                    V1.OpcUaISA95JobControlV1Extensions
                        .CreateInstanceOfISA95JobResponseReceiverObjectType(
                            SystemContext,
                            root,
                            InstanceBrowseName(
                                $"{m_options.JobControlV1BrowseName}_JobResponseReceiver"));
                AddChild(root, m_v1ResponseReceiver);
                WireV1ResponseReceiver(
                    m_v1ResponseReceiver,
                    m_providers.JobResponseReceiverV1);
            }
        }

        private void CreateJobControlV2Endpoints(FolderState root)
        {
            if (!m_options.EnableJobControlV2)
            {
                return;
            }
            if (m_providers.JobOrderReceiverV2 != null)
            {
                QualifiedName browseName = InstanceBrowseName(
                    $"{m_options.JobControlV2BrowseName}_JobOrderReceiver");
                m_v2OrderReceiver = m_options.EnableJobControlSubStates
                    ? V2Extensions
                        .CreateInstanceOfISA95JobOrderReceiverSubStatesType(
                            SystemContext,
                            root,
                            browseName)
                    : V2Extensions
                        .CreateInstanceOfISA95JobOrderReceiverObjectType(
                            SystemContext,
                            root,
                            browseName);
                V2Binder.AddReceiverMethods(m_v2OrderReceiver);
                AddChild(root, m_v2OrderReceiver);
                V2Binder.BindOrderReceiver(
                    m_v2OrderReceiver,
                    m_providers.JobOrderReceiverV2);
                V2Binder.InitializeOrderVariables(
                    m_v2OrderReceiver,
                    m_providers.JobOrderCatalog?.MaxDownloadableJobOrders ?? 0);
                m_v2OrderReceiver.JobOrderList!.OnSimpleReadValue = ReadV2JobOrderList;
            }
            if (m_options.ExposeJobResponseProvider &&
                m_providers.JobResponseProviderV2 != null)
            {
                m_v2ResponseProvider =
                    V2Extensions
                        .CreateInstanceOfISA95JobResponseProviderObjectType(
                            SystemContext,
                            root,
                            InstanceBrowseName(
                                $"{m_options.JobControlV2BrowseName}_JobResponseProvider"));
                m_v2ResponseProvider.EventNotifier = EventNotifiers.SubscribeToEvents;
                AddChild(root, m_v2ResponseProvider);
                V2Binder.BindResponseProvider(
                    m_v2ResponseProvider,
                    m_providers.JobResponseProviderV2);
            }
            if (m_options.ExposeJobResponseReceiver &&
                m_providers.JobResponseReceiverV2 != null)
            {
                m_v2ResponseReceiver =
                    V2Extensions
                        .CreateInstanceOfISA95JobResponseReceiverObjectType(
                            SystemContext,
                            root,
                            InstanceBrowseName(
                                $"{m_options.JobControlV2BrowseName}_JobResponseReceiver"));
                AddChild(root, m_v2ResponseReceiver);
                V2Binder.BindResponseReceiver(
                    m_v2ResponseReceiver,
                    m_providers.JobResponseReceiverV2);
            }
        }

        private T GetOrAddChild<T>(
            NodeState parent,
            QualifiedName browseName,
            T fallback)
            where T : BaseInstanceState
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is T typed &&
                    (typed.BrowseName == browseName ||
                        string.Equals(
                            typed.SymbolicName,
                            browseName.Name,
                            StringComparison.Ordinal)))
                {
                    return typed;
                }
            }
            return AddChild(parent, fallback);
        }

        private static T AddChild<T>(NodeState parent, T child)
            where T : BaseInstanceState
        {
            if (child.ReferenceTypeId.IsNull)
            {
                child.ReferenceTypeId = parent is FolderState
                    ? Ua.ReferenceTypeIds.Organizes
                    : Ua.ReferenceTypeIds.HasComponent;
            }
            parent.AddChild(child);
            return child;
        }

        private void WireV1OrderReceiver(
            V1.ISA95JobOrderReceiverObjectState endpoint,
            IIsa95JobOrderReceiverV1 provider)
        {
            endpoint.ReceiveJobOrder!.MethodDeclarationId = ModelNodeId(
                V1.MethodIds.ISA95JobOrderReceiverObjectType_ReceiveJobOrder);
            endpoint.ReceiveJobOrder!.OnCallAsync = async (
                _,
                _,
                _,
                command,
                order,
                ct) =>
            {
                Isa95JobOrderReceiptV1 result =
                    await provider.ReceiveJobOrderAsync(command, order, ct)
                        .ConfigureAwait(false);
                await RefreshJobOrderListsAsync(ct).ConfigureAwait(false);
                return new V1.ReceiveJobOrderMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
        }

        private void WireV1ResponseProvider(
            V1.ISA95JobResponseProviderObjectState endpoint,
            IIsa95JobResponseProviderV1 provider)
        {
            endpoint.RequestJobResponse!.MethodDeclarationId = ModelNodeId(
                V1.MethodIds.ISA95JobResponseProviderObjectType_RequestJobResponse);
            endpoint.RequestJobResponse!.OnCallAsync = async (
                _,
                _,
                _,
                jobOrderId,
                state,
                ct) =>
            {
                Isa95JobResponseQueryV1 result =
                    await provider.RequestJobResponseAsync(jobOrderId, state, ct)
                        .ConfigureAwait(false);
                return new V1.RequestJobResponseMethodStateResult
                {
                    ServiceResult = result.Result,
                    JobResponse = result.Responses,
                    ReturnStatus = result.ReturnStatus
                };
            };
        }

        private void WireV1ResponseReceiver(
            V1.ISA95JobResponseReceiverObjectState endpoint,
            IIsa95JobResponseReceiverV1 provider)
        {
            endpoint.ReceiveJobResponse!.MethodDeclarationId = ModelNodeId(
                V1.MethodIds.ISA95JobResponseReceiverObjectType_ReceiveJobResponse);
            endpoint.ReceiveJobResponse!.OnCallAsync = async (
                _,
                _,
                _,
                response,
                ct) =>
            {
                Isa95JobResponseReceiptV1 result =
                    await provider.ReceiveJobResponseAsync(response, ct)
                        .ConfigureAwait(false);
                return new V1.ReceiveJobResponseMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
        }

        private void InitializeV1OrderVariables(
            V1.ISA95JobOrderReceiverObjectState endpoint)
        {
            endpoint.JobOrderList!.Value = [];
            endpoint.JobOrderList.OnSimpleReadValue = ReadV1JobOrderList;
            endpoint.WorkMaster!.Value = [];
            endpoint.MaterialClassID!.Value = [];
            endpoint.MaterialDefinitionID!.Value = [];
            endpoint.EquipmentID!.Value = [];
            endpoint.PhysicalAssetID!.Value = [];
            endpoint.PersonnelID!.Value = [];
        }

        private async ValueTask RefreshJobOrderListsAsync(CancellationToken ct)
        {
            IIsa95JobOrderCatalog? catalog = m_providers.JobOrderCatalog;
            if (catalog == null)
            {
                return;
            }

            long generation = Interlocked.Increment(
                ref m_jobOrderRefreshGeneration);
            bool updateV1 = m_v1OrderReceiver?.JobOrderList != null;
            bool updateV2 = m_v2OrderReceiver?.JobOrderList != null;
            ArrayOf<V1.ISA95JobOrderDataType> v1Orders = default;
            ArrayOf<V2.ISA95JobOrderAndStateDataType> v2Orders = default;
            if (updateV1)
            {
                v1Orders = await catalog.GetJobOrdersV1Async(ct)
                    .ConfigureAwait(false);
            }
            if (updateV2)
            {
                v2Orders = V2Binder.NormalizeJobOrders(
                    await catalog.GetJobOrdersV2Async(ct).ConfigureAwait(false));
            }

            BaseVariableState? v1List = updateV1 ? m_v1OrderReceiver!.JobOrderList : null;
            BaseVariableState? v2List = updateV2 ? m_v2OrderReceiver!.JobOrderList : null;
            lock (m_jobOrderRefreshLock)
            {
                if (generation <= m_jobOrderAppliedGeneration)
                {
                    return;
                }
                // The lists serve their value from these snapshots, so nothing
                // else sets the Value change mask. The new source timestamp
                // sets it only when it differs from the last one, which two
                // refreshes within one clock tick do not, so mark it explicitly.
                DateTimeUtc now = DateTimeUtc.Now;
                if (updateV1)
                {
                    m_v1JobOrders = v1Orders;
                    v1List!.Timestamp = now;
                    v1List.UpdateChangeMasks(NodeStateChangeMasks.Value);
                }
                if (updateV2)
                {
                    m_v2JobOrders = v2Orders;
                    v2List!.Timestamp = now;
                    v2List.UpdateChangeMasks(NodeStateChangeMasks.Value);
                }
                m_jobOrderAppliedGeneration = generation;
            }

            // Outside the lock: the change handlers of monitored items read the
            // lists back through the read callbacks, which take the same lock.
            v1List?.ClearChangeMasks(SystemContext, includeChildren: false);
            v2List?.ClearChangeMasks(SystemContext, includeChildren: false);
        }

        private ServiceResult ReadV1JobOrderList(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            ArrayOf<V1.ISA95JobOrderDataType> snapshot;
            lock (m_jobOrderRefreshLock)
            {
                snapshot = m_v1JobOrders;
            }
            value = Variant.FromStructure(snapshot);
            return ServiceResult.Good;
        }

        private ServiceResult ReadV2JobOrderList(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            ArrayOf<V2.ISA95JobOrderAndStateDataType> snapshot;
            lock (m_jobOrderRefreshLock)
            {
                snapshot = m_v2JobOrders;
            }
            value = Variant.FromStructure(snapshot);
            return ServiceResult.Good;
        }

        private async ValueTask ConfigureStatusEventsAsync(
            CancellationToken cancellationToken)
        {
            if (m_v2ResponseProvider == null || m_providers.JobStatusSourceV2 == null)
            {
                return;
            }
            NodeManagerBuilder builder = CreateFluentBuilder(InstanceNamespaceIndex);
            builder
                .Node<V2.ISA95JobResponseProviderObjectState>(
                    m_v2ResponseProvider.NodeId)
                .Publish(
                    CreateStatusEventsAsync,
                    new EventPublishOptions { AlwaysOn = true });

            // Sealing activates this pass's behaviors and completes the root-notifier
            // registration the Publish above staged, so it has to be awaited.
            await builder.SealAsync(cancellationToken).ConfigureAwait(false);
        }

        private ValueTask ConfigureCatalogChangesAsync(
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (m_providers.JobOrderCatalog == null ||
                m_providers.JobOrderCatalogChangeSource == null)
            {
                return default;
            }
            m_catalogChangesTask = ProcessCatalogChangesAsync(
                m_providers.JobOrderCatalogChangeSource,
                m_catalogChangesCts.Token);
            return default;
        }

        private void CancelCatalogChanges()
        {
            try
            {
                m_catalogChangesCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Shutdown already disposed the cancellation source.
            }
        }

        private async Task ProcessCatalogChangesAsync(
            IIsa95JobOrderCatalogChangeSource source,
            CancellationToken ct)
        {
            try
            {
                await foreach (Isa95JobOrderCatalogChange _ in
                    source.SubscribeCatalogChangesAsync(ct).ConfigureAwait(false))
                {
                    await RefreshJobOrderListsAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Normal node manager shutdown.
            }
            catch (Exception ex)
            {
                m_logger.CatalogChangeStreamFailed(ex);
            }
        }

        private async ValueTask CreateV2StatusEventTypeAsync(CancellationToken ct)
        {
            if (!m_options.EnableJobControlV2 ||
                m_providers.JobStatusSourceV2 == null ||
                m_providers.JobResponseProviderV2 == null)
            {
                return;
            }

            m_v2StatusEventTypeId = new NodeId(
                "ISA95JobOrderStatusEventInstanceType",
                InstanceNamespaceIndex);
            var eventType = new BaseObjectTypeState
            {
                NodeId = m_v2StatusEventTypeId,
                BrowseName = InstanceBrowseName(
                    "ISA95JobOrderStatusEventInstanceType"),
                DisplayName = new LocalizedText(
                    "ISA95 Job Order Status Event Instance Type"),
                SuperTypeId = ModelNodeId(
                    V2.ObjectTypeIds.ISA95JobOrderStatusEventType),
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            };
            await AddPredefinedNodeAsync(eventType, ct).ConfigureAwait(false);
        }

        private async ValueTask ConfigureCommonModelAsync(
            FolderState root,
            CancellationToken ct)
        {
            if (m_configurators.Count == 0)
            {
                return;
            }
            var builder = Isa95ModelBuilder.Create(
                SystemContext,
                root,
                InstanceNamespaceIndex,
                AddPredefinedNodeAsync);
            foreach (IIsa95ModelConfigurator configurator in m_configurators)
            {
                await configurator.ConfigureAsync(builder, ct).ConfigureAwait(false);
            }
        }

        private async IAsyncEnumerable<V2.ISA95JobOrderStatusEventState>
            CreateStatusEventsAsync(
                V2.ISA95JobResponseProviderObjectState notifier,
                ISystemContext context,
                [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (Isa95JobStatusNotificationV2 status in
                m_providers.JobStatusSourceV2!.SubscribeAsync(ct)
                    .ConfigureAwait(false))
            {
                await RefreshJobOrderListsAsync(ct).ConfigureAwait(false);
                V2.ISA95JobOrderStatusEventState ev =
                    V2Extensions.CreateInstanceOfISA95JobOrderStatusEventType(
                    context,
                    notifier,
                    ModelBrowseName(
                        V2.BrowseNames.ISA95JobOrderStatusEventType,
                        V2.Namespaces.ISA95JobControlV2));
                BindStatusEventFields(context, ev);
                ev.TypeDefinitionId = m_v2StatusEventTypeId;
                ev.EventType ??= FindRequiredChild<PropertyState<NodeId>>(
                    ev,
                    new QualifiedName(Ua.BrowseNames.EventType));
                ev.EventType.Value = m_v2StatusEventTypeId;
                ev.JobOrder!.Value = status.JobOrder;
                ev.JobResponse!.Value = V2Binder.NormalizeResponse(status.JobResponse);
                ev.JobState!.Value = V2Binder.NormalizeState(status.State);
                ev.Time = PropertyState<DateTimeUtc>.With<VariantBuilder>(
                    ev,
                    status.Timestamp);
                yield return ev;
            }
        }

        private void BindStatusEventFields(
            ISystemContext context,
            V2.ISA95JobOrderStatusEventState statusEvent)
        {
            statusEvent.CreateOrReplaceJobOrder(
                context,
                FindRequiredChild<
                    PropertyState<V2.ISA95JobOrderDataType>>(
                    statusEvent,
                    ModelBrowseName(
                        V2.BrowseNames.JobOrder,
                        V2.Namespaces.ISA95JobControlV2)));
            statusEvent.CreateOrReplaceJobResponse(
                context,
                FindRequiredChild<
                    PropertyState<V2.ISA95JobResponseDataType>>(
                    statusEvent,
                    ModelBrowseName(
                        V2.BrowseNames.JobResponse,
                        V2.Namespaces.ISA95JobControlV2)));
            statusEvent.CreateOrReplaceJobState(
                context,
                FindRequiredChild<
                    PropertyState<ArrayOf<V2.ISA95StateDataType>>>(
                    statusEvent,
                    ModelBrowseName(
                        V2.BrowseNames.JobState,
                        V2.Namespaces.ISA95JobControlV2)));
        }

        private T FindRequiredChild<T>(
            NodeState parent,
            QualifiedName browseName)
            where T : BaseInstanceState
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is T typed &&
                    (typed.BrowseName == browseName ||
                        string.Equals(
                            typed.SymbolicName,
                            browseName.Name,
                            StringComparison.Ordinal)))
                {
                    return typed;
                }
            }
            throw new InvalidOperationException(
                $"The generated {browseName.Name} child is missing.");
        }

        private QualifiedName InstanceBrowseName(string name)
        {
            return new QualifiedName(name, InstanceNamespaceIndex);
        }

        private QualifiedName ModelBrowseName(string name, string namespaceUri)
        {
            return new QualifiedName(
                name,
                (ushort)Server.NamespaceUris.GetIndex(namespaceUri));
        }

        private NodeId ModelNodeId(ExpandedNodeId nodeId)
        {
            var resolved = ExpandedNodeId.ToNodeId(
                nodeId,
                SystemContext.NamespaceUris);
            if (resolved.IsNull)
            {
                throw new InvalidOperationException(
                    "The method declaration namespace is not registered.");
            }
            return resolved;
        }

        private static void RegisterEncodeables(IEncodeableFactory factory)
        {
            IEncodeableFactoryBuilder builder = factory.Builder;
            bool commit = false;
            if (!factory.ContainsEncodeableType(
                DataTypeIds.ISA95TestResultDataType))
            {
                builder = builder.AddOpcUaISA95();
                commit = true;
            }
            if (!factory.ContainsEncodeableType(
                V1.DataTypeIds.ISA95JobOrderDataType))
            {
                builder = V1.OpcUaISA95JobControlV1Extensions
                    .AddOpcUaISA95JobControlV1(builder);
                commit = true;
            }
            if (!factory.ContainsEncodeableType(
                V2.DataTypeIds.ISA95JobOrderDataType))
            {
                builder = V2Extensions
                    .AddOpcUaISA95JobControlV2(builder);
                commit = true;
            }
            if (commit)
            {
                builder.Commit();
            }
        }

        private Isa95JobControlV2Binder? m_v2Binder;
        private readonly Isa95ServerOptions m_options;
        private readonly Isa95ServerProviders m_providers;
        private readonly IReadOnlyList<IIsa95ModelConfigurator> m_configurators;
        private V1.ISA95JobOrderReceiverObjectState? m_v1OrderReceiver;
        private V1.ISA95JobResponseProviderObjectState? m_v1ResponseProvider;
        private V1.ISA95JobResponseReceiverObjectState? m_v1ResponseReceiver;
        private V2.ISA95JobOrderReceiverObjectState? m_v2OrderReceiver;
        private V2.ISA95JobResponseProviderObjectState? m_v2ResponseProvider;
        private V2.ISA95JobResponseReceiverObjectState? m_v2ResponseReceiver;
        private NodeId m_v2StatusEventTypeId;
        private readonly CancellationTokenSource m_catalogChangesCts = new();
        private readonly Lock m_jobOrderRefreshLock = new();

        private ArrayOf<V1.ISA95JobOrderDataType> m_v1JobOrders =
            [];

        private ArrayOf<V2.ISA95JobOrderAndStateDataType> m_v2JobOrders =
            [];

        private Task m_catalogChangesTask = Task.CompletedTask;
        private int m_catalogChangesDisposed;
        private long m_jobOrderAppliedGeneration;
        private long m_jobOrderRefreshGeneration;
    }

    internal static partial class Isa95NodeManagerLog
    {
        [LoggerMessage(
            EventId = 9501,
            Level = LogLevel.Error,
            Message = "The ISA-95 job order catalog change stream failed.")]
        public static partial void CatalogChangeStreamFailed(
            this ILogger logger,
            Exception exception);
    }
}
