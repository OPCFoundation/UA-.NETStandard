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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Installs the experimental model only when the runtime execution provider has been explicitly enabled.
    /// </summary>
    public partial class DiagnosticsNodeManager
    {
        /// <summary>
        /// Installs generated RSE types and creates live manager Methods with application-namespace child identities.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        internal async ValueTask InstallAsyncServiceModelAsync(
            AsyncServiceExecutionOptions options,
            IAsyncServiceExecutionProvider provider,
            string instanceNamespaceUri,
            CancellationToken ct)
        {
            ushort instanceNamespace = SystemContext.NamespaceUris.GetIndexOrAppend(instanceNamespaceUri);
            if (instanceNamespace == 0)
            {
                throw new ArgumentException("Runtime instance children require an application namespace.",
                    nameof(instanceNamespaceUri));
            }
            ushort modelNamespace = SystemContext.NamespaceUris.GetIndexOrAppend(RseNamespaces.Rse);
            var managerId = ExpandedNodeId.ToNodeId(RseObjectIds.RecoverableExecutionManager,
                SystemContext.NamespaceUris);
            var capabilitiesId = ExpandedNodeId.ToNodeId(RseObjectIds.RecoverableExecutionManager_Capabilities,
                SystemContext.NamespaceUris);
            var diagnosticsId = ExpandedNodeId.ToNodeId(RseObjectIds.RecoverableExecutionDiagnostics,
                SystemContext.NamespaceUris);
            using NodeManagerOperation operation = await EnterDiagnosticsOperationAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (string uri in new[] { instanceNamespaceUri, RseNamespaces.Rse })
                {
                    if (!NamespaceUris.Contains(uri, StringComparer.Ordinal))
                    {
                        SetNamespaces([.. NamespaceUris, uri]);
                        Server.NodeManager.RegisterNamespaceManager(uri, this);
                    }
                }
                Server.Factory.Builder.AddOpcUaRse().Commit();
                foreach (NodeState node in OpcUaRseExtensions.AddOpcUaRse([], SystemContext))
                {
                    if (node.NodeId == managerId ||
                        node.NodeId == capabilitiesId ||
                        node.NodeId == diagnosticsId ||
                        node is BaseInstanceState { Parent: not null })
                    {
                        continue;
                    }
                    if (node.NodeId == ExpandedNodeId.ToNodeId(
                        RseObjectTypeIds.AuditServiceOperationEventType, SystemContext.NamespaceUris))
                    {
                        node.RolePermissions = CreateAsyncPermissions(receiveEvents: true);
                    }
                    await AddPredefinedNodeAsync(SystemContext, node, ct).ConfigureAwait(false);
                }

                var manager = new RecoverableExecutionManagerState(null);
                manager.Create(SystemContext, managerId,
                    new QualifiedName(RseBrowseNames.RecoverableExecutionManager, modelNamespace),
                    new LocalizedText("Recoverable execution manager"), false);
                AssignAsyncInstanceChildren(manager, instanceNamespace, privileged: false);
                RecoverableExecutionCapabilitiesState capabilities = manager.Capabilities!;
                capabilities.NodeId = capabilitiesId;
                capabilities.ContractVersion!.Value = "0.4.0";
                capabilities.SupportedServices!.Value = provider.SupportedServices;
                capabilities.SupportedRecoveryModes!.Value = provider.SupportedRecoveryModes;
                capabilities.SupportedRecoveryModes.OnSimpleReadValue = (_, _, ref value) =>
                {
                    value = Variant.From(provider.SupportedRecoveryModes);
                    return ServiceResult.Good;
                };
                capabilities.MinExecutionAllowance!.Value = options.MinExecutionAllowance;
                capabilities.MaxExecutionAllowance!.Value = options.MaxExecutionAllowance;
                capabilities.DefaultExecutionAllowance!.Value = options.DefaultExecutionAllowance;
                capabilities.MinResultRetentionTime!.Value = options.MinResultRetentionTime;
                capabilities.MaxResultRetentionTime!.Value = options.MaxResultRetentionTime;
                capabilities.DefaultResultRetentionTime!.Value = options.DefaultResultRetentionTime;
                BindRecoverableMethods(manager, provider);
                provider.BindManager(manager);
                await AddAsyncRootAsync(manager, ObjectIds.Server, ct).ConfigureAwait(false);

                var diagnostics = new RecoverableExecutionDiagnosticsState(null);
                diagnostics.Create(SystemContext, diagnosticsId,
                    new QualifiedName(RseBrowseNames.RecoverableExecutionDiagnostics, modelNamespace),
                    new LocalizedText("Recoverable execution diagnostics"), false);
                diagnostics.RolePermissions = CreateAsyncPermissions();
                AssignAsyncInstanceChildren(diagnostics, instanceNamespace, privileged: true);
                BindAsyncCounter(
                    diagnostics.ActiveOperationCount!, provider, static value => value.ActiveOperationCount);
                BindAsyncCounter(
                    diagnostics.RetainedOutcomeCount!, provider, static value => value.RetainedOutcomeCount);
                BindAsyncCounter(
                    diagnostics.RetainedOutcomeBytes!, provider, static value => value.RetainedOutcomeBytes);
                BindAsyncCounter(diagnostics.TerminalRecordCount!, provider, static value => value.TerminalRecordCount);
                BindAsyncCounter(diagnostics.AdmittedCount!, provider, static value => value.AdmittedCount);
                BindAsyncCounter(diagnostics.RejectedCount!, provider, static value => value.RejectedCount);
                BindAsyncCounter(diagnostics.AcknowledgedCount!, provider, static value => value.AcknowledgedCount);
                BindAsyncCounter(diagnostics.AbandonedCount!, provider, static value => value.AbandonedCount);
                BindAsyncCounter(diagnostics.ExpiredCount!, provider, static value => value.ExpiredCount);
                BindAsyncCounter(
                    diagnostics.ExecutionDeadlineCount!, provider, static value => value.ExecutionDeadlineCount);
                BindAsyncCounter(diagnostics.ExecutionIndeterminateCount!, provider,
                    static value => value.ExecutionIndeterminateCount);
                await AddAsyncRootAsync(diagnostics, ObjectIds.Server_ServerDiagnostics, ct).ConfigureAwait(false);
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Wires the five generated scalar Method handlers through ordinary Call validation and diagnostics.
        /// </summary>
        private void BindRecoverableMethods(
            RecoverableExecutionManagerState manager, IAsyncServiceExecutionProvider provider)
        {
            SubmitMethodState submit = PromoteMethod(new SubmitMethodState(manager), manager.Submit!);
            GetResultMethodState getResult = PromoteMethod(new GetResultMethodState(manager), manager.GetResult!);
            AcknowledgeMethodState acknowledge =
                PromoteMethod(new AcknowledgeMethodState(manager), manager.Acknowledge!);
            CancelMethodState cancel = PromoteMethod(new CancelMethodState(manager), manager.Cancel!);
            AbandonMethodState abandon = PromoteMethod(new AbandonMethodState(manager), manager.Abandon!);
            manager.CreateOrReplaceSubmit(SystemContext, submit, assignInstanceNodeIds: false);
            manager.CreateOrReplaceGetResult(SystemContext, getResult, assignInstanceNodeIds: false);
            manager.CreateOrReplaceAcknowledge(SystemContext, acknowledge, assignInstanceNodeIds: false);
            manager.CreateOrReplaceCancel(SystemContext, cancel, assignInstanceNodeIds: false);
            manager.CreateOrReplaceAbandon(SystemContext, abandon, assignInstanceNodeIds: false);
            submit.OnCallAsync = async (context, _, _, request, ct) => new SubmitMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Response = await provider.SubmitAsync(RequireOperation(context), request, ct).ConfigureAwait(false)
            };
            getResult.OnCallAsync = async (context, _, _, request, ct) => new GetResultMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Response = await provider.GetAsync(RequireOperation(context), request, ct).ConfigureAwait(false)
            };
            acknowledge.OnCallAsync = async (context, _, _, request, ct) => new AcknowledgeMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Response = await provider.AcknowledgeAsync(RequireOperation(context), request, ct).ConfigureAwait(false)
            };
            cancel.OnCallAsync = async (context, _, _, request, ct) => new CancelMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Response = await provider.CancelAsync(RequireOperation(context), request, ct).ConfigureAwait(false)
            };
            abandon.OnCallAsync = async (context, _, _, request, ct) => new AbandonMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Response = await provider.AbandonAsync(RequireOperation(context), request, ct).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Promotes a generated declaration's live child to its generated typed handler, preserving its metadata.
        /// </summary>
        /// <typeparam name="T">The source-generated typed Method state.</typeparam>
        private T PromoteMethod<T>(T typed, MethodState source) where T : MethodState
        {
            typed.Create(SystemContext, source);
            return typed;
        }

        /// <summary>
        /// Requires an ordinary authenticated Call context rather than accepting an internal model invocation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static OperationContext RequireOperation(ISystemContext context)
        {
            return (context as ServerSystemContext)?.OperationContext ??
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
        }

        /// <summary>
        /// Connects an enabled root to the existing Server model without creating a second namespace-zero root.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask AddAsyncRootAsync(BaseObjectState root, NodeId parentId, CancellationToken ct)
        {
            BaseObjectState parent = FindPredefinedNode<BaseObjectState>(parentId) ??
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            parent.AddReference(ReferenceTypeIds.HasComponent, false, root.NodeId);
            root.AddReference(ReferenceTypeIds.HasComponent, true, parentId);
            await AddPredefinedNodeAsync(SystemContext, root, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Keeps live child identities separate from provisional type-member declaration identifiers.
        /// </summary>
        private void AssignAsyncInstanceChildren(NodeState parent, ushort namespaceIndex, bool privileged)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                child.NodeId = new NodeId(Guid.NewGuid(), namespaceIndex);
                if (privileged)
                {
                    child.RolePermissions = CreateAsyncPermissions();
                }
                AssignAsyncInstanceChildren(child, namespaceIndex, privileged);
            }
        }

        /// <summary>
        /// Publishes a current bounded aggregate through ordinary variable reads and permission checks.
        /// </summary>
        private static void BindAsyncCounter(
            BaseDataVariableState<uint> variable,
            IAsyncServiceExecutionProvider provider,
            Func<AsyncServiceExecutionStatistics, uint> select)
        {
            variable.OnSimpleReadValue = (_, _, ref value) =>
            {
                value = Variant.From(select(provider.GetStatistics()));
                return ServiceResult.Good;
            };
        }

        /// <summary>
        /// Publishes a saturating cumulative aggregate through the same existing diagnostics controls.
        /// </summary>
        private static void BindAsyncCounter(
            BaseDataVariableState<ulong> variable,
            IAsyncServiceExecutionProvider provider,
            Func<AsyncServiceExecutionStatistics, ulong> select)
        {
            variable.OnSimpleReadValue = (_, _, ref value) =>
            {
                value = Variant.From(select(provider.GetStatistics()));
                return ServiceResult.Good;
            };
        }

        /// <summary>
        /// Creates independently owned permissions using the existing privileged role.
        /// </summary>
        private static ArrayOf<RolePermissionType> CreateAsyncPermissions(bool receiveEvents = false)
        {
            PermissionType permissions =
                PermissionType.Browse | PermissionType.Read | PermissionType.ReadRolePermissions;
            if (receiveEvents)
            {
                permissions |= PermissionType.ReceiveEvents;
            }
            return
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)permissions
                }
            ];
        }
    }
}
