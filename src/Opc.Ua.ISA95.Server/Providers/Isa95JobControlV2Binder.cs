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
using System.Threading;
using System.Threading.Tasks;
using V2 = Opc.Ua.ISA95.JobControl.V2;
using V2Extensions = Opc.Ua.ISA95.JobControl.V2.OpcUaISA95JobControlV2Extensions;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Materialises and binds the OPC 10031-4 Job Control V2 endpoint objects
    /// against <see cref="IIsa95JobOrderReceiverV2"/>,
    /// <see cref="IIsa95JobResponseProviderV2"/> and
    /// <see cref="IIsa95JobResponseReceiverV2"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The eleven job verbs (<c>Store</c>, <c>StoreAndStart</c>, <c>Start</c>,
    /// <c>Stop</c>, <c>Abort</c>, <c>Pause</c>, <c>Resume</c>, <c>Clear</c>,
    /// <c>Cancel</c>, <c>Update</c>, <c>RevokeStart</c>) are declared by the
    /// Job Control V2 model, not by the companion specification that composes
    /// it. <see cref="Isa95NodeManager"/> binds them for a stand-alone ISA-95
    /// server and OPC 40001-3 <c>JobManagementType</c> binds exactly the same
    /// endpoints below a machine, so the wiring lives here rather than being
    /// duplicated per composing model.
    /// </para>
    /// <para>
    /// The binder owns no nodes and starts no work of its own: it writes the
    /// method handlers and the initial variable values, and calls back into
    /// <c>onJobOrdersChanged</c> after every operation that can change the
    /// catalog so the host can refresh whatever job-order snapshot it
    /// publishes.
    /// </para>
    /// </remarks>
    public sealed class Isa95JobControlV2Binder
    {
        /// <summary>
        /// Creates a binder for one address space.
        /// </summary>
        /// <param name="context">
        /// The system context of the node manager that owns the endpoints.
        /// </param>
        /// <param name="namespaceUris">
        /// The namespace table used to resolve Job Control V2 method
        /// declarations and substate browse names.
        /// </param>
        /// <param name="onJobOrdersChanged">
        /// Invoked after every operation that can change the job-order catalog.
        /// May be <see langword="null"/> when the host publishes no job-order
        /// list.
        /// </param>
        public Isa95JobControlV2Binder(
            ISystemContext context,
            NamespaceTable namespaceUris,
            Func<CancellationToken, ValueTask>? onJobOrdersChanged = null)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_namespaceUris = namespaceUris ??
                throw new ArgumentNullException(nameof(namespaceUris));
            m_onJobOrdersChanged = onJobOrdersChanged;
        }

        /// <summary>
        /// Gets the namespace index of the Job Control V2 model.
        /// </summary>
        public ushort JobControlV2NamespaceIndex =>
            (ushort)m_namespaceUris.GetIndex(V2.Namespaces.ISA95JobControlV2);

        /// <summary>
        /// Materialises the optional job verbs the Job Control V2 receiver
        /// declares. Existing children are kept, so the method is idempotent
        /// and safe on a receiver whose generated factory already created some
        /// of them.
        /// </summary>
        /// <param name="receiver">The receiver endpoint to complete.</param>
        public void AddReceiverMethods(V2.ISA95JobOrderReceiverObjectState receiver)
        {
            if (receiver == null)
            {
                throw new ArgumentNullException(nameof(receiver));
            }

            receiver.Store ??= GetOrAddChild(
                receiver,
                V2.BrowseNames.Store,
                V2Extensions.CreateInstanceOfStoreMethodType(
                    m_context, receiver, ModelBrowseName(V2.BrowseNames.Store)));
            receiver.StoreAndStart ??= GetOrAddChild(
                receiver,
                V2.BrowseNames.StoreAndStart,
                V2Extensions.CreateInstanceOfStoreAndStartMethodType(
                    m_context, receiver, ModelBrowseName(V2.BrowseNames.StoreAndStart)));
            receiver.Start ??= GetOrAddChild(
                receiver,
                V2.BrowseNames.Start,
                V2Extensions.CreateInstanceOfStartMethodType(
                    m_context, receiver, ModelBrowseName(V2.BrowseNames.Start)));
            receiver.Update ??= GetOrAddChild(
                receiver,
                V2.BrowseNames.Update,
                V2Extensions.CreateInstanceOfUpdateMethodType(
                    m_context, receiver, ModelBrowseName(V2.BrowseNames.Update)));
            receiver.Stop ??= GetOrAddChild(
                receiver,
                V2.BrowseNames.Stop,
                V2Extensions.CreateInstanceOfStopMethodType(
                    m_context, receiver, ModelBrowseName(V2.BrowseNames.Stop)));
            receiver.Cancel ??= GetOrAddChild(
                receiver,
                V2.BrowseNames.Cancel,
                V2Extensions.CreateInstanceOfCancelMethodType(
                    m_context, receiver, ModelBrowseName(V2.BrowseNames.Cancel)));
            receiver.Clear ??= GetOrAddChild(
                receiver,
                V2.BrowseNames.Clear,
                V2Extensions.CreateInstanceOfClearMethodType(
                    m_context, receiver, ModelBrowseName(V2.BrowseNames.Clear)));
            receiver.Pause ??= GetOrAddChild(
                receiver,
                V2.BrowseNames.Pause,
                V2Extensions.CreateInstanceOfPauseMethodType(
                    m_context, receiver, ModelBrowseName(V2.BrowseNames.Pause)));
            receiver.Resume ??= GetOrAddChild(
                receiver,
                V2.BrowseNames.Resume,
                V2Extensions.CreateInstanceOfResumeMethodType(
                    m_context, receiver, ModelBrowseName(V2.BrowseNames.Resume)));
            receiver.Abort ??= GetOrAddChild(
                receiver,
                V2.BrowseNames.Abort,
                V2Extensions.CreateInstanceOfAbortMethodType(
                    m_context, receiver, ModelBrowseName(V2.BrowseNames.Abort)));
            receiver.RevokeStart ??= GetOrAddChild(
                receiver,
                V2.BrowseNames.RevokeStart,
                V2Extensions.CreateInstanceOfRevokeStartMethodType(
                    m_context, receiver, ModelBrowseName(V2.BrowseNames.RevokeStart)));
        }

        /// <summary>
        /// Binds the eleven job verbs of a Job Control V2 receiver to
        /// <paramref name="provider"/>.
        /// </summary>
        /// <param name="endpoint">The receiver endpoint.</param>
        /// <param name="provider">The provider that applies the operations.</param>
        public void BindOrderReceiver(
            V2.ISA95JobOrderReceiverObjectState endpoint,
            IIsa95JobOrderReceiverV2 provider)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

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

            endpoint.Store!.OnCallAsync = async (_, _, _, order, comment, ct) =>
            {
                Isa95JobOrderReceiptV2 result = await InvokeAsync(
                    provider, Isa95JobOrderOperationV2.Store, order, comment, ct)
                    .ConfigureAwait(false);
                return new V2.StoreMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.StoreAndStart!.OnCallAsync = async (_, _, _, order, comment, ct) =>
            {
                Isa95JobOrderReceiptV2 result = await InvokeAsync(
                    provider, Isa95JobOrderOperationV2.StoreAndStart, order, comment, ct)
                    .ConfigureAwait(false);
                return new V2.StoreAndStartMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.Update!.OnCallAsync = async (_, _, _, order, comment, ct) =>
            {
                Isa95JobOrderReceiptV2 result = await InvokeAsync(
                    provider, Isa95JobOrderOperationV2.Update, order, comment, ct)
                    .ConfigureAwait(false);
                return new V2.UpdateMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.Start!.OnCallAsync = async (_, _, _, id, comment, ct) =>
            {
                Isa95JobOrderReceiptV2 result = await InvokeByIdAsync(
                    provider, Isa95JobOrderOperationV2.Start, id, comment, ct)
                    .ConfigureAwait(false);
                return new V2.StartMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.Stop!.OnCallAsync = async (_, _, _, id, comment, ct) =>
            {
                Isa95JobOrderReceiptV2 result = await InvokeByIdAsync(
                    provider, Isa95JobOrderOperationV2.Stop, id, comment, ct)
                    .ConfigureAwait(false);
                return new V2.StopMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.Cancel!.OnCallAsync = async (_, _, _, id, comment, ct) =>
            {
                Isa95JobOrderReceiptV2 result = await InvokeByIdAsync(
                    provider, Isa95JobOrderOperationV2.Cancel, id, comment, ct)
                    .ConfigureAwait(false);
                return new V2.CancelMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.Clear!.OnCallAsync = async (_, _, _, id, comment, ct) =>
            {
                Isa95JobOrderReceiptV2 result = await InvokeByIdAsync(
                    provider, Isa95JobOrderOperationV2.Clear, id, comment, ct)
                    .ConfigureAwait(false);
                return new V2.ClearMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.Pause!.OnCallAsync = async (_, _, _, id, comment, ct) =>
            {
                Isa95JobOrderReceiptV2 result = await InvokeByIdAsync(
                    provider, Isa95JobOrderOperationV2.Pause, id, comment, ct)
                    .ConfigureAwait(false);
                return new V2.PauseMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.Resume!.OnCallAsync = async (_, _, _, id, comment, ct) =>
            {
                Isa95JobOrderReceiptV2 result = await InvokeByIdAsync(
                    provider, Isa95JobOrderOperationV2.Resume, id, comment, ct)
                    .ConfigureAwait(false);
                return new V2.ResumeMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.Abort!.OnCallAsync = async (_, _, _, id, comment, ct) =>
            {
                Isa95JobOrderReceiptV2 result = await InvokeByIdAsync(
                    provider, Isa95JobOrderOperationV2.Abort, id, comment, ct)
                    .ConfigureAwait(false);
                return new V2.AbortMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.RevokeStart!.OnCallAsync = async (_, _, _, id, comment, ct) =>
            {
                Isa95JobOrderReceiptV2 result = await InvokeByIdAsync(
                    provider, Isa95JobOrderOperationV2.RevokeStart, id, comment, ct)
                    .ConfigureAwait(false);
                return new V2.RevokeStartMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
        }

        /// <summary>
        /// Binds the two request methods of a Job Control V2 response provider
        /// to <paramref name="provider"/>.
        /// </summary>
        public void BindResponseProvider(
            V2.ISA95JobResponseProviderObjectState endpoint,
            IIsa95JobResponseProviderV2 provider)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            endpoint.RequestJobResponseByJobOrderID!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobResponseProviderObjectType_RequestJobResponseByJobOrderID);
            endpoint.RequestJobResponseByJobOrderState!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobResponseProviderObjectType_RequestJobResponseByJobOrderState);

            endpoint.RequestJobResponseByJobOrderID!.OnCallAsync = async (
                _, _, _, jobOrderId, ct) =>
            {
                Isa95JobResponseByIdResultV2 result = await provider
                    .RequestJobResponseByJobOrderIdAsync(jobOrderId, ct)
                    .ConfigureAwait(false);
                return new V2.RequestJobResponseByJobOrderIDMethodStateResult
                {
                    ServiceResult = result.Result,
                    JobResponse = NormalizeResponse(
                        result.Response ?? new V2.ISA95JobResponseDataType()),
                    ReturnStatus = result.ReturnStatus
                };
            };
            endpoint.RequestJobResponseByJobOrderState!.OnCallAsync = async (
                _, _, _, state, ct) =>
            {
                Isa95JobResponsesByStateResultV2 result = await provider
                    .RequestJobResponsesByStateAsync(state, ct)
                    .ConfigureAwait(false);
                return new V2.RequestJobResponseByJobOrderStateMethodStateResult
                {
                    ServiceResult = result.Result,
                    JobResponses = NormalizeResponses(result.Responses),
                    ReturnStatus = result.ReturnStatus
                };
            };
        }

        /// <summary>
        /// Binds the receive method of a Job Control V2 response receiver to
        /// <paramref name="provider"/>.
        /// </summary>
        public void BindResponseReceiver(
            V2.ISA95JobResponseReceiverObjectState endpoint,
            IIsa95JobResponseReceiverV2 provider)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            endpoint.ReceiveJobResponse!.MethodDeclarationId = ModelNodeId(
                V2.MethodIds.ISA95JobResponseReceiverObjectType_ReceiveJobResponse);
            endpoint.ReceiveJobResponse!.OnCallAsync = async (_, _, _, response, ct) =>
            {
                Isa95JobResponseReceiptV2 result = await provider
                    .ReceiveJobResponseAsync(response, ct)
                    .ConfigureAwait(false);
                return new V2.ReceiveJobResponseMethodStateResult
                {
                    ServiceResult = result.Result,
                    ReturnStatus = result.ReturnStatus
                };
            };
        }

        /// <summary>
        /// Assigns the initial values of the receiver's list variables. The
        /// <c>JobOrderList</c> read handler stays with the host, which owns the
        /// catalog snapshot.
        /// </summary>
        /// <param name="endpoint">The receiver endpoint.</param>
        /// <param name="maxDownloadableJobOrders">
        /// The value published on <c>MaxDownloadableJobOrders</c>.
        /// </param>
        public void InitializeOrderVariables(
            V2.ISA95JobOrderReceiverObjectState endpoint,
            ushort maxDownloadableJobOrders)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            endpoint.JobOrderList!.Value = [];
            endpoint.CurrentState!.StatusCode = StatusCodes.BadNotReadable;
            endpoint.WorkMaster!.Value = [];
            endpoint.MaterialClassID!.Value = [];
            endpoint.MaterialDefinitionID!.Value = [];
            endpoint.EquipmentID!.Value = [];
            endpoint.PhysicalAssetID!.Value = [];
            endpoint.PersonnelID!.Value = [];
            endpoint.MaxDownloadableJobOrders!.Value = maxDownloadableJobOrders;
        }

        /// <summary>
        /// Qualifies the substate browse paths inside a job response with the
        /// Job Control V2 namespace index of this address space.
        /// </summary>
        public V2.ISA95JobResponseDataType NormalizeResponse(
            V2.ISA95JobResponseDataType response)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }
            var normalized = (V2.ISA95JobResponseDataType)response.Clone();
            normalized.JobState = NormalizeState(response.JobState);
            return normalized;
        }

        /// <summary>
        /// <see cref="NormalizeResponse"/> applied to a list.
        /// </summary>
        public ArrayOf<V2.ISA95JobResponseDataType> NormalizeResponses(
            ArrayOf<V2.ISA95JobResponseDataType> responses)
        {
            if (responses.IsNull || responses.Count == 0)
            {
                return responses;
            }
            var normalized = new V2.ISA95JobResponseDataType[responses.Count];
            for (int ii = 0; ii < responses.Count; ii++)
            {
                normalized[ii] = NormalizeResponse(responses[ii]);
            }
            return normalized.ToArrayOf();
        }

        /// <summary>
        /// Qualifies the substate browse paths of a state list with the Job
        /// Control V2 namespace index of this address space.
        /// </summary>
        public ArrayOf<V2.ISA95StateDataType> NormalizeState(
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
                normalizedEntry.BrowsePath = NormalizeBrowsePath(entry.BrowsePath);
                normalized[ii] = normalizedEntry;
            }
            return normalized.ToArrayOf();
        }

        /// <summary>
        /// <see cref="NormalizeState"/> applied to every entry of a job-order
        /// list.
        /// </summary>
        public ArrayOf<V2.ISA95JobOrderAndStateDataType> NormalizeJobOrders(
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
                var normalizedOrder = (V2.ISA95JobOrderAndStateDataType)order.Clone();
                normalizedOrder.State = NormalizeState(order.State);
                normalized[ii] = normalizedOrder;
            }
            return normalized.ToArrayOf();
        }

        private async ValueTask<Isa95JobOrderReceiptV2> InvokeAsync(
            IIsa95JobOrderReceiverV2 provider,
            Isa95JobOrderOperationV2 operation,
            V2.ISA95JobOrderDataType order,
            ArrayOf<LocalizedText> comment,
            CancellationToken ct)
        {
            Isa95JobOrderReceiptV2 result = await provider
                .ReceiveJobOrderAsync(operation, order, comment, ct)
                .ConfigureAwait(false);
            await NotifyJobOrdersChangedAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<Isa95JobOrderReceiptV2> InvokeByIdAsync(
            IIsa95JobOrderReceiverV2 provider,
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
            await NotifyJobOrdersChangedAsync(ct).ConfigureAwait(false);
            return result;
        }

        private ValueTask NotifyJobOrdersChangedAsync(CancellationToken ct)
        {
            return m_onJobOrdersChanged == null ? default : m_onJobOrdersChanged(ct);
        }

        private RelativePath NormalizeBrowsePath(RelativePath browsePath)
        {
            if (browsePath == null || browsePath.Elements.Count == 0)
            {
                return browsePath ?? new RelativePath();
            }
            var normalizedPath = (RelativePath)browsePath.Clone();
            var normalized = new RelativePathElement[browsePath.Elements.Count];
            ushort namespaceIndex = JobControlV2NamespaceIndex;
            for (int ii = 0; ii < browsePath.Elements.Count; ii++)
            {
                RelativePathElement element = browsePath.Elements[ii];
                var normalizedElement = (RelativePathElement)element.Clone();
                QualifiedName targetName = element.TargetName;
                if (targetName.NamespaceIndex == 0 && IsSubstateMachine(targetName.Name))
                {
                    targetName = new QualifiedName(targetName.Name, namespaceIndex);
                }
                normalizedElement.TargetName = targetName;
                normalized[ii] = normalizedElement;
            }
            normalizedPath.Elements = normalized.ToArrayOf();
            return normalizedPath;
        }

        private static bool IsSubstateMachine(string? browseName)
        {
            return string.Equals(
                    browseName, V2.BrowseNames.NotAllowedToStartSubstates, StringComparison.Ordinal) ||
                string.Equals(
                    browseName, V2.BrowseNames.AllowedToStartSubstates, StringComparison.Ordinal) ||
                string.Equals(
                    browseName, V2.BrowseNames.EndedSubstates, StringComparison.Ordinal) ||
                string.Equals(
                    browseName, V2.BrowseNames.InterruptedSubstates, StringComparison.Ordinal);
        }

        private QualifiedName ModelBrowseName(string name)
        {
            return new QualifiedName(name, JobControlV2NamespaceIndex);
        }

        private NodeId ModelNodeId(ExpandedNodeId nodeId)
        {
            var resolved = ExpandedNodeId.ToNodeId(nodeId, m_namespaceUris);
            if (resolved.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Job Control V2 method declaration namespace '{0}' is not registered.",
                    V2.Namespaces.ISA95JobControlV2);
            }
            return resolved;
        }

        /// <summary>
        /// Returns the existing child of that browse name, or the supplied
        /// replacement.
        /// </summary>
        /// <remarks>
        /// The replacement is deliberately not pushed through
        /// <see cref="NodeState.AddChild"/>: the caller assigns it to the
        /// generated typed property, and a generated <c>GetChildren</c> lists
        /// the typed properties first and then the untyped child list. A node
        /// that sits in both is enumerated twice, which surfaces much later as
        /// a duplicate-NodeId failure in whatever walks the subtree.
        /// </remarks>
        /// <typeparam name="T">The generated child state type.</typeparam>
        private T GetOrAddChild<T>(NodeState parent, string browseName, T replacement)
            where T : BaseInstanceState
        {
            QualifiedName qualifiedName = ModelBrowseName(browseName);
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is T typed &&
                    (typed.BrowseName == qualifiedName ||
                        string.Equals(typed.SymbolicName, browseName, StringComparison.Ordinal)))
                {
                    return typed;
                }
            }
            if (replacement.ReferenceTypeId.IsNull)
            {
                replacement.ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent;
            }
            replacement.ModellingRuleId = NodeId.Null;
            return replacement;
        }

        private readonly ISystemContext m_context;
        private readonly NamespaceTable m_namespaceUris;
        private readonly Func<CancellationToken, ValueTask>? m_onJobOrdersChanged;
    }
}
