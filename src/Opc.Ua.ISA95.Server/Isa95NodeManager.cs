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
            SystemContext.NodeIdFactory = this;
        }

        public FolderState? Root { get; private set; }

        public ushort InstanceNamespaceIndex =>
            (ushort)Server.NamespaceUris.GetIndex(m_options.InstanceNamespaceUri);

        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (!node.NodeId.IsNull)
            {
                return node.NodeId;
            }
            if (node is BaseInstanceState instance && instance.Parent != null)
            {
                string parent = instance.Parent.NodeId.IdentifierAsString;
                string name = instance.BrowseName.Name ?? instance.SymbolicName ?? "Node";
                return new NodeId($"{parent}_{name}", InstanceNamespaceIndex);
            }
            return new NodeId(
                $"ISA95_{Interlocked.Increment(ref m_nextNodeId)}",
                InstanceNamespaceIndex);
        }

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
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            await CreateV2StatusEventTypeAsync(cancellationToken).ConfigureAwait(false);
            Root = CreateRoot(externalReferences);
            CreateJobControlV1Endpoints(Root);
            CreateJobControlV2Endpoints(Root);
            await AddPredefinedNodeAsync(Root, cancellationToken).ConfigureAwait(false);
            await ConfigureCommonModelAsync(Root, cancellationToken).ConfigureAwait(false);
            ConfigureCatalogChanges();
            await RefreshJobOrderListsAsync(cancellationToken).ConfigureAwait(false);
            ConfigureStatusEvents();
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

        private FolderState CreateRoot(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            var root = new FolderState(null)
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
            root.AddReference(
                Ua.ReferenceTypeIds.Organizes,
                isInverse: true,
                Ua.ObjectIds.ObjectsFolder);
            if (!externalReferences.TryGetValue(
                Ua.ObjectIds.ObjectsFolder,
                out IList<IReference>? references))
            {
                references = [];
                externalReferences[Ua.ObjectIds.ObjectsFolder] =
                    references;
            }
            references.Add(
                new NodeStateReference(
                    Ua.ReferenceTypeIds.Organizes,
                    isInverse: false,
                    root.NodeId));
            return root;
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
                AddV2ReceiverMethods(m_v2OrderReceiver);
                AddChild(root, m_v2OrderReceiver);
                WireV2OrderReceiver(m_v2OrderReceiver, m_providers.JobOrderReceiverV2);
                InitializeV2OrderVariables(m_v2OrderReceiver);
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
                WireV2ResponseProvider(
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
                WireV2ResponseReceiver(
                    m_v2ResponseReceiver,
                    m_providers.JobResponseReceiverV2);
            }
        }

        private void AddV2ReceiverMethods(V2.ISA95JobOrderReceiverObjectState receiver)
        {
            receiver.Store ??= GetOrAddChild(
                receiver,
                ModelBrowseName(V2.BrowseNames.Store, V2.Namespaces.ISA95JobControlV2),
                V2Extensions.CreateInstanceOfStoreMethodType(
                    SystemContext,
                    receiver,
                    ModelBrowseName(V2.BrowseNames.Store, V2.Namespaces.ISA95JobControlV2)));
            receiver.StoreAndStart ??= GetOrAddChild(
                receiver,
                ModelBrowseName(
                    V2.BrowseNames.StoreAndStart,
                    V2.Namespaces.ISA95JobControlV2),
                V2Extensions.CreateInstanceOfStoreAndStartMethodType(
                    SystemContext,
                    receiver,
                    ModelBrowseName(
                        V2.BrowseNames.StoreAndStart,
                        V2.Namespaces.ISA95JobControlV2)));
            receiver.Start ??= GetOrAddChild(
                receiver,
                ModelBrowseName(V2.BrowseNames.Start, V2.Namespaces.ISA95JobControlV2),
                V2Extensions.CreateInstanceOfStartMethodType(
                    SystemContext,
                    receiver,
                    ModelBrowseName(V2.BrowseNames.Start, V2.Namespaces.ISA95JobControlV2)));
            receiver.Update ??= GetOrAddChild(
                receiver,
                ModelBrowseName(V2.BrowseNames.Update, V2.Namespaces.ISA95JobControlV2),
                V2Extensions.CreateInstanceOfUpdateMethodType(
                    SystemContext,
                    receiver,
                    ModelBrowseName(V2.BrowseNames.Update, V2.Namespaces.ISA95JobControlV2)));
            receiver.Stop ??= GetOrAddChild(
                receiver,
                ModelBrowseName(V2.BrowseNames.Stop, V2.Namespaces.ISA95JobControlV2),
                V2Extensions.CreateInstanceOfStopMethodType(
                    SystemContext,
                    receiver,
                    ModelBrowseName(V2.BrowseNames.Stop, V2.Namespaces.ISA95JobControlV2)));
            receiver.Cancel ??= GetOrAddChild(
                receiver,
                ModelBrowseName(V2.BrowseNames.Cancel, V2.Namespaces.ISA95JobControlV2),
                V2Extensions.CreateInstanceOfCancelMethodType(
                    SystemContext,
                    receiver,
                    ModelBrowseName(V2.BrowseNames.Cancel, V2.Namespaces.ISA95JobControlV2)));
            receiver.Clear ??= GetOrAddChild(
                receiver,
                ModelBrowseName(V2.BrowseNames.Clear, V2.Namespaces.ISA95JobControlV2),
                V2Extensions.CreateInstanceOfClearMethodType(
                    SystemContext,
                    receiver,
                    ModelBrowseName(V2.BrowseNames.Clear, V2.Namespaces.ISA95JobControlV2)));
            receiver.Pause ??= GetOrAddChild(
                receiver,
                ModelBrowseName(V2.BrowseNames.Pause, V2.Namespaces.ISA95JobControlV2),
                V2Extensions.CreateInstanceOfPauseMethodType(
                    SystemContext,
                    receiver,
                    ModelBrowseName(V2.BrowseNames.Pause, V2.Namespaces.ISA95JobControlV2)));
            receiver.Resume ??= GetOrAddChild(
                receiver,
                ModelBrowseName(V2.BrowseNames.Resume, V2.Namespaces.ISA95JobControlV2),
                V2Extensions.CreateInstanceOfResumeMethodType(
                    SystemContext,
                    receiver,
                    ModelBrowseName(V2.BrowseNames.Resume, V2.Namespaces.ISA95JobControlV2)));
            receiver.Abort ??= GetOrAddChild(
                receiver,
                ModelBrowseName(V2.BrowseNames.Abort, V2.Namespaces.ISA95JobControlV2),
                V2Extensions.CreateInstanceOfAbortMethodType(
                    SystemContext,
                    receiver,
                    ModelBrowseName(V2.BrowseNames.Abort, V2.Namespaces.ISA95JobControlV2)));
            receiver.RevokeStart ??= GetOrAddChild(
                receiver,
                ModelBrowseName(
                    V2.BrowseNames.RevokeStart,
                    V2.Namespaces.ISA95JobControlV2),
                V2Extensions.CreateInstanceOfRevokeStartMethodType(
                    SystemContext,
                    receiver,
                    ModelBrowseName(
                        V2.BrowseNames.RevokeStart,
                        V2.Namespaces.ISA95JobControlV2)));
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

        private void WireV2OrderReceiver(
            V2.ISA95JobOrderReceiverObjectState endpoint,
            IIsa95JobOrderReceiverV2 provider)
        {
            endpoint.Store!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobOrderReceiverObjectType_Store);
            endpoint.StoreAndStart!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobOrderReceiverObjectType_StoreAndStart);
            endpoint.Update!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobOrderReceiverObjectType_Update);
            endpoint.Start!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobOrderReceiverObjectType_Start);
            endpoint.Stop!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobOrderReceiverObjectType_Stop);
            endpoint.Cancel!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobOrderReceiverObjectType_Cancel);
            endpoint.Clear!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobOrderReceiverObjectType_Clear);
            endpoint.Pause!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobOrderReceiverObjectType_Pause);
            endpoint.Resume!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobOrderReceiverObjectType_Resume);
            endpoint.Abort!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobOrderReceiverObjectType_Abort);
            endpoint.RevokeStart!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobOrderReceiverObjectType_RevokeStart);
            endpoint.Store!.OnCallAsync = Store;
            endpoint.StoreAndStart!.OnCallAsync = StoreAndStart;
            endpoint.Update!.OnCallAsync = Update;
            endpoint.Start!.OnCallAsync = Start;
            endpoint.Stop!.OnCallAsync = Stop;
            endpoint.Cancel!.OnCallAsync = Cancel;
            endpoint.Clear!.OnCallAsync = Clear;
            endpoint.Pause!.OnCallAsync = Pause;
            endpoint.Resume!.OnCallAsync = Resume;
            endpoint.Abort!.OnCallAsync = Abort;
            endpoint.RevokeStart!.OnCallAsync = RevokeStart;

            async ValueTask<V2.StoreMethodStateResult> Store(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                V2.ISA95JobOrderDataType order,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result = await provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Store,
                    order,
                    comment,
                    ct).ConfigureAwait(false);
                await RefreshJobOrderListsAsync(ct).ConfigureAwait(false);
                return new V2.StoreMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            }

            async ValueTask<V2.StoreAndStartMethodStateResult> StoreAndStart(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                V2.ISA95JobOrderDataType order,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result = await provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.StoreAndStart,
                    order,
                    comment,
                    ct).ConfigureAwait(false);
                await RefreshJobOrderListsAsync(ct).ConfigureAwait(false);
                return new V2.StoreAndStartMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            }

            async ValueTask<V2.UpdateMethodStateResult> Update(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                V2.ISA95JobOrderDataType order,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result = await provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.Update,
                    order,
                    comment,
                    ct).ConfigureAwait(false);
                await RefreshJobOrderListsAsync(ct).ConfigureAwait(false);
                return new V2.UpdateMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            }

            async ValueTask<V2.StartMethodStateResult> Start(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                string id,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result =
                    await InvokeById(Isa95JobOrderOperationV2.Start, id, comment, ct)
                        .ConfigureAwait(false);
                return new V2.StartMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            }

            async ValueTask<V2.StopMethodStateResult> Stop(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                string id,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result =
                    await InvokeById(Isa95JobOrderOperationV2.Stop, id, comment, ct)
                        .ConfigureAwait(false);
                return new V2.StopMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            }

            async ValueTask<V2.CancelMethodStateResult> Cancel(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                string id,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result =
                    await InvokeById(Isa95JobOrderOperationV2.Cancel, id, comment, ct)
                        .ConfigureAwait(false);
                return new V2.CancelMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            }

            async ValueTask<V2.ClearMethodStateResult> Clear(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                string id,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result =
                    await InvokeById(Isa95JobOrderOperationV2.Clear, id, comment, ct)
                        .ConfigureAwait(false);
                return new V2.ClearMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            }

            async ValueTask<V2.PauseMethodStateResult> Pause(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                string id,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result =
                    await InvokeById(Isa95JobOrderOperationV2.Pause, id, comment, ct)
                        .ConfigureAwait(false);
                return new V2.PauseMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            }

            async ValueTask<V2.ResumeMethodStateResult> Resume(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                string id,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result =
                    await InvokeById(Isa95JobOrderOperationV2.Resume, id, comment, ct)
                        .ConfigureAwait(false);
                return new V2.ResumeMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            }

            async ValueTask<V2.AbortMethodStateResult> Abort(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                string id,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result =
                    await InvokeById(Isa95JobOrderOperationV2.Abort, id, comment, ct)
                        .ConfigureAwait(false);
                return new V2.AbortMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            }

            async ValueTask<V2.RevokeStartMethodStateResult> RevokeStart(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                string id,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result =
                    await InvokeById(Isa95JobOrderOperationV2.RevokeStart, id, comment, ct)
                        .ConfigureAwait(false);
                return new V2.RevokeStartMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            }

            async ValueTask<Isa95JobOrderReceiptV2> InvokeById(
                Isa95JobOrderOperationV2 operation,
                string id,
                ArrayOf<LocalizedText> comment,
                CancellationToken ct)
            {
                Isa95JobOrderReceiptV2 result = await provider.ReceiveJobOrderAsync(
                    operation,
                    new V2.ISA95JobOrderDataType { JobOrderID = id },
                    comment,
                    ct).ConfigureAwait(false);
                await RefreshJobOrderListsAsync(ct).ConfigureAwait(false);
                return result;
            }
        }

        private void WireV2ResponseProvider(
            V2.ISA95JobResponseProviderObjectState endpoint,
            IIsa95JobResponseProviderV2 provider)
        {
            endpoint.RequestJobResponseByJobOrderID!.MethodDeclarationId =
                ModelNodeId(
                    V2.MethodIds
                        .ISA95JobResponseProviderObjectType_RequestJobResponseByJobOrderID);
            endpoint.RequestJobResponseByJobOrderState!.MethodDeclarationId =
                ModelNodeId(
                    V2.MethodIds
                        .ISA95JobResponseProviderObjectType_RequestJobResponseByJobOrderState);
            endpoint.RequestJobResponseByJobOrderID!.OnCallAsync = async (
                _,
                _,
                _,
                jobOrderId,
                ct) =>
            {
                Isa95JobResponseByIdResultV2 result =
                    await provider.RequestJobResponseByJobOrderIdAsync(
                        jobOrderId,
                        ct).ConfigureAwait(false);
                return new V2.RequestJobResponseByJobOrderIDMethodStateResult
                {
                    ServiceResult = result.Result,
                    JobResponse = NormalizeV2Response(
                        result.Response ?? new V2.ISA95JobResponseDataType()),
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.RequestJobResponseByJobOrderState!.OnCallAsync = async (
                _,
                _,
                _,
                state,
                ct) =>
            {
                Isa95JobResponsesByStateResultV2 result =
                    await provider.RequestJobResponsesByStateAsync(state, ct)
                        .ConfigureAwait(false);
                return new V2.RequestJobResponseByJobOrderStateMethodStateResult
                {
                    ServiceResult = result.Result,
                    JobResponses = NormalizeV2Responses(result.Responses),
                    ReturnStatus = result.ReturnStatus
                };
            };
        }

        private void WireV2ResponseReceiver(
            V2.ISA95JobResponseReceiverObjectState endpoint,
            IIsa95JobResponseReceiverV2 provider)
        {
            endpoint.ReceiveJobResponse!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobResponseReceiverObjectType_ReceiveJobResponse);
            endpoint.ReceiveJobResponse!.OnCallAsync = async (
                _,
                _,
                _,
                response,
                ct) =>
            {
                Isa95JobResponseReceiptV2 result =
                    await provider.ReceiveJobResponseAsync(response, ct)
                        .ConfigureAwait(false);
                return new V2.ReceiveJobResponseMethodStateResult
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

        private void InitializeV2OrderVariables(
            V2.ISA95JobOrderReceiverObjectState endpoint)
        {
            endpoint.JobOrderList!.Value = [];
            endpoint.JobOrderList.OnSimpleReadValue = ReadV2JobOrderList;
            endpoint.CurrentState!.StatusCode = StatusCodes.BadNotReadable;
            endpoint.WorkMaster!.Value = [];
            endpoint.MaterialClassID!.Value = [];
            endpoint.MaterialDefinitionID!.Value = [];
            endpoint.EquipmentID!.Value = [];
            endpoint.PhysicalAssetID!.Value = [];
            endpoint.PersonnelID!.Value = [];
            endpoint.MaxDownloadableJobOrders!.Value =
                m_providers.JobOrderCatalog?.MaxDownloadableJobOrders ?? 0;
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
                v2Orders = NormalizeV2JobOrders(
                    await catalog.GetJobOrdersV2Async(ct).ConfigureAwait(false));
            }

            lock (m_jobOrderRefreshLock)
            {
                if (generation <= m_jobOrderAppliedGeneration)
                {
                    return;
                }
                if (updateV1)
                {
                    m_v1JobOrders = v1Orders;
                }
                if (updateV2)
                {
                    m_v2JobOrders = v2Orders;
                }
                m_jobOrderAppliedGeneration = generation;
            }
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

        private ArrayOf<V2.ISA95JobOrderAndStateDataType> NormalizeV2JobOrders(
            ArrayOf<V2.ISA95JobOrderAndStateDataType> orders)
        {
            if (orders.IsNull || orders.Count == 0)
            {
                return orders;
            }
            var normalized = new V2.ISA95JobOrderAndStateDataType[orders.Count];
            for (int ii = 0; ii < orders.Count; ii++)
            {
                V2.ISA95JobOrderAndStateDataType order = orders[ii];
                var normalizedOrder =
                    (V2.ISA95JobOrderAndStateDataType)order.Clone();
                normalizedOrder.State = NormalizeV2State(order.State);
                normalized[ii] = normalizedOrder;
            }
            return normalized.ToArrayOf();
        }

        private ArrayOf<V2.ISA95JobResponseDataType> NormalizeV2Responses(
            ArrayOf<V2.ISA95JobResponseDataType> responses)
        {
            if (responses.IsNull || responses.Count == 0)
            {
                return responses;
            }
            var normalized = new V2.ISA95JobResponseDataType[responses.Count];
            for (int ii = 0; ii < responses.Count; ii++)
            {
                normalized[ii] = NormalizeV2Response(responses[ii]);
            }
            return normalized.ToArrayOf();
        }

        private V2.ISA95JobResponseDataType NormalizeV2Response(
            V2.ISA95JobResponseDataType response)
        {
            var normalized = (V2.ISA95JobResponseDataType)response.Clone();
            normalized.JobState = NormalizeV2State(response.JobState);
            return normalized;
        }

        private ArrayOf<V2.ISA95StateDataType> NormalizeV2State(
            ArrayOf<V2.ISA95StateDataType> state)
        {
            if (state.IsNull || state.Count == 0)
            {
                return state;
            }
            var normalized = new V2.ISA95StateDataType[state.Count];
            for (int ii = 0; ii < state.Count; ii++)
            {
                V2.ISA95StateDataType entry = state[ii];
                var normalizedEntry = (V2.ISA95StateDataType)entry.Clone();
                normalizedEntry.BrowsePath = NormalizeV2BrowsePath(
                    entry.BrowsePath);
                normalized[ii] = normalizedEntry;
            }
            return normalized.ToArrayOf();
        }

        private RelativePath NormalizeV2BrowsePath(RelativePath browsePath)
        {
            if (browsePath == null || browsePath.Elements.Count == 0)
            {
                return browsePath ?? new RelativePath();
            }
            var normalizedPath = (RelativePath)browsePath.Clone();
            var normalized = new RelativePathElement[browsePath.Elements.Count];
            ushort namespaceIndex = (ushort)Server.NamespaceUris.GetIndex(
                V2.Namespaces.ISA95JobControlV2);
            for (int ii = 0; ii < browsePath.Elements.Count; ii++)
            {
                RelativePathElement element = browsePath.Elements[ii];
                var normalizedElement = (RelativePathElement)element.Clone();
                QualifiedName targetName = element.TargetName;
                if (targetName.NamespaceIndex == 0 &&
                    IsV2SubstateMachine(targetName.Name))
                {
                    targetName = new QualifiedName(
                        targetName.Name,
                        namespaceIndex);
                }
                normalizedElement.TargetName = targetName;
                normalized[ii] = normalizedElement;
            }
            normalizedPath.Elements = normalized.ToArrayOf();
            return normalizedPath;
        }

        private static bool IsV2SubstateMachine(string? browseName)
        {
            return string.Equals(
                    browseName,
                    V2.BrowseNames.NotAllowedToStartSubstates,
                    StringComparison.Ordinal) ||
                string.Equals(
                    browseName,
                    V2.BrowseNames.AllowedToStartSubstates,
                    StringComparison.Ordinal) ||
                string.Equals(
                    browseName,
                    V2.BrowseNames.EndedSubstates,
                    StringComparison.Ordinal) ||
                string.Equals(
                    browseName,
                    V2.BrowseNames.InterruptedSubstates,
                    StringComparison.Ordinal);
        }

        private void ConfigureStatusEvents()
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
            builder.Seal();
        }

        private void ConfigureCatalogChanges()
        {
            if (m_providers.JobOrderCatalog == null ||
                m_providers.JobOrderCatalogChangeSource == null)
            {
                return;
            }
            m_catalogChangesTask = ProcessCatalogChangesAsync(
                m_providers.JobOrderCatalogChangeSource,
                m_catalogChangesCts.Token);
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
                ev.JobResponse!.Value = NormalizeV2Response(status.JobResponse);
                ev.JobState!.Value = NormalizeV2State(status.State);
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
        private long m_nextNodeId;
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
