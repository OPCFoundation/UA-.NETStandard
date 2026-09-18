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
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    internal sealed partial class WotProjectedEventBinding
    {
        public async ValueTask InitializeDescriptorAsync(
            INodeManagerBuilder builder, string name, CancellationToken cancellationToken)
        {
            if (builder.NodeManager is not AsyncCustomNodeManager manager)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "Event metadata requires a registering node manager.");
            }
            ushort namespaceIndex = m_context.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            var folderName = new QualifiedName("EventBindings", namespaceIndex);
            FolderState? folder = Notifier.FindChild(m_context, folderName) as FolderState;
            if (folder is null)
            {
                folder = new FolderState(Notifier);
                folder.Create(m_context,
                    new NodeId("EventBindings-" + Guid.NewGuid().ToString("N"), Notifier.NodeId.NamespaceIndex),
                    folderName, new LocalizedText("Event bindings"), assignNodeIds: false);
                folder.ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent;
                folder.RolePermissions = Notifier.RolePermissions;
                folder.UserRolePermissions = Notifier.UserRolePermissions;
                folder.AccessRestrictions = Notifier.AccessRestrictions;
                Notifier.AddChild(folder);
                await manager.AddPredefinedNodeAsync(folder, cancellationToken).ConfigureAwait(false);
            }
            var descriptor = new WoTEventBindingState(folder);
            descriptor.Create(m_context,
                new NodeId("EventBinding-" + Guid.NewGuid().ToString("N"), Notifier.NodeId.NamespaceIndex),
                new QualifiedName(name, namespaceIndex), new LocalizedText(name), assignNodeIds: false);
            descriptor.BindingId!.Value = JsonPointer;
            descriptor.IdentityMode!.Value = IdentityMode;
            descriptor.Generation!.Value = 0;
            descriptor.Generation.StatusCode = StatusCodes.BadWaitingForInitialData;
            descriptor.SourceDocumentId!.Value = ResourceXid;
            descriptor.Availability!.Value = StatusCodes.BadWaitingForInitialData;
            descriptor.AddSourceServerUri(m_context);
            descriptor.SourceServerUri!.StatusCode = StatusCodes.BadNoData;
            descriptor.RolePermissions = Notifier.RolePermissions;
            descriptor.UserRolePermissions = Notifier.UserRolePermissions;
            descriptor.AccessRestrictions = Notifier.AccessRestrictions;
            descriptor.ReferenceTypeId = Ua.ReferenceTypeIds.Organizes;
            MethodState method = descriptor.GetEventProvenance!;
            method.Executable = true;
            method.UserExecutable = true;
            method.RolePermissions = Notifier.RolePermissions;
            method.UserRolePermissions = Notifier.UserRolePermissions;
            method.AccessRestrictions = Notifier.AccessRestrictions;
            m_context.AssignInstanceChildNodeIds(descriptor);
            folder.AddChild(descriptor);
            await manager.AddPredefinedNodeAsync(descriptor, cancellationToken).ConfigureAwait(false);
            builder.Node(method.NodeId).OnCallWithResult((_, _, _, arguments, token) =>
                new ValueTask<MethodInvocationResult>(GetProvenance(arguments, token)));
            m_descriptor = descriptor;
            ArrayOf<WotProjectedEventBinding> retained = m_routeRegistry.GetDescriptorBindings(Notifier.NodeId);
            if (!retained.IsEmpty)
            {
                var retainedName = new QualifiedName("Retained", namespaceIndex);
                FolderState? retainedFolder = folder.FindChild(m_context, retainedName) as FolderState;
                if (retainedFolder is null)
                {
                    retainedFolder = new FolderState(folder);
                    retainedFolder.Create(m_context,
                        new NodeId("RetainedEventBindings-" + Guid.NewGuid().ToString("N"),
                            Notifier.NodeId.NamespaceIndex),
                        retainedName, new LocalizedText("Retained event bindings"), assignNodeIds: false);
                    retainedFolder.ReferenceTypeId = Ua.ReferenceTypeIds.Organizes;
                    folder.AddChild(retainedFolder);
                    await manager.AddPredefinedNodeAsync(retainedFolder, cancellationToken).ConfigureAwait(false);
                }
                for (int index = 0; index < retained.Count; index++)
                {
                    await retained[index].RegisterDescriptorAliasAsync(
                        builder, manager, retainedFolder, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public void SetPublishedGeneration(long generation)
        {
            uint actual = checked((uint)generation);
            lock (m_gate)
            {
                m_publishedGeneration = actual;
                m_isPublished = true;
                if (m_descriptor is { } descriptor)
                {
                    descriptor.Generation!.Value = actual;
                    descriptor.Generation.StatusCode = StatusCodes.Good;
                }
            }
        }

        public void ReportFailure(StatusCode status)
        {
            lock (m_gate)
            {
                m_failureStatus = status;
                if (m_descriptor is { } descriptor)
                {
                    descriptor.Availability!.Value = status;
                }
                foreach (WeakReference<WoTEventBindingState> reference in m_descriptorAliases)
                {
                    if (reference.TryGetTarget(out WoTEventBindingState? alias))
                    {
                        alias.Availability!.Value = status;
                    }
                }
            }
        }

        private async ValueTask RegisterDescriptorAliasAsync(
            INodeManagerBuilder builder,
            AsyncCustomNodeManager manager,
            FolderState folder,
            CancellationToken cancellationToken)
        {
            WoTEventBindingState alias;
            lock (m_gate)
            {
                if (m_disposed || m_descriptor is not { } source ||
                    manager.FindPredefinedNode<NodeState>(source.NodeId) is not null)
                {
                    return;
                }
                alias = new WoTEventBindingState(folder);
                alias.Create(builder.Context, source.NodeId,
                    new QualifiedName(source.BrowseName.Name + "@" + m_publishedGeneration,
                        source.BrowseName.NamespaceIndex),
                    source.DisplayName, assignNodeIds: false);
                alias.ReferenceTypeId = Ua.ReferenceTypeIds.Organizes;
                CopyAccess(alias, source);
                CopyProperty(alias.BindingId!, source.BindingId!);
                CopyProperty(alias.IdentityMode!, source.IdentityMode!);
                CopyProperty(alias.Generation!, source.Generation!);
                CopyProperty(alias.SourceDocumentId!, source.SourceDocumentId!);
                CopyProperty(alias.Availability!, source.Availability!);
                alias.AddSourceServerUri(builder.Context);
                CopyProperty(alias.SourceServerUri!, source.SourceServerUri!);
                MethodState targetMethod = alias.GetEventProvenance!;
                MethodState sourceMethod = source.GetEventProvenance!;
                targetMethod.NodeId = sourceMethod.NodeId;
                targetMethod.MethodDeclarationId = sourceMethod.MethodDeclarationId;
                targetMethod.Executable = sourceMethod.Executable;
                targetMethod.UserExecutable = sourceMethod.UserExecutable;
                CopyAccess(targetMethod, sourceMethod);
                CopyProperty(targetMethod.InputArguments!, sourceMethod.InputArguments!);
                CopyProperty(targetMethod.OutputArguments!, sourceMethod.OutputArguments!);
                m_descriptorAliases.RemoveAll(static reference => !reference.TryGetTarget(out _));
                m_descriptorAliases.Add(new WeakReference<WoTEventBindingState>(alias));
            }
            folder.AddChild(alias);
            await manager.AddPredefinedNodeAsync(alias, cancellationToken).ConfigureAwait(false);
            builder.Node(alias.GetEventProvenance!.NodeId).OnCallWithResult((_, _, _, arguments, token) =>
                new ValueTask<MethodInvocationResult>(GetProvenance(arguments, token)));
        }

        private static void CopyProperty<T>(PropertyState<T> target, PropertyState<T> source)
        {
            target.NodeId = source.NodeId;
            target.Value = source.Value;
            target.StatusCode = source.StatusCode;
            CopyAccess(target, source);
        }

        private static void CopyAccess(NodeState target, NodeState source)
        {
            target.RolePermissions = source.RolePermissions;
            target.UserRolePermissions = source.UserRolePermissions;
            target.AccessRestrictions = source.AccessRestrictions;
        }

        private void MarkDescriptorRetired()
        {
            m_failureStatus = StatusCodes.BadShutdown;
            if (m_descriptor is { } descriptor)
            {
                descriptor.Availability!.Value = StatusCodes.BadShutdown;
            }
            foreach (WeakReference<WoTEventBindingState> reference in m_descriptorAliases)
            {
                if (reference.TryGetTarget(out WoTEventBindingState? alias))
                {
                    alias.Availability!.Value = StatusCodes.BadShutdown;
                }
            }
            m_descriptorAliases.Clear();
        }

        private MethodInvocationResult GetProvenance(
            ArrayOf<Variant> arguments, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new MethodInvocationResult(StatusCodes.BadRequestCancelledByClient);
            }
            if (arguments.Count != 1)
            {
                return new MethodInvocationResult(arguments.Count == 0
                    ? StatusCodes.BadArgumentsMissing : StatusCodes.BadTooManyArguments);
            }
            if (!arguments[0].TryGetValue(out ByteString eventId))
            {
                return new MethodInvocationResult(StatusCodes.BadTypeMismatch);
            }
            lock (m_gate)
            {
                if (m_disposed || !m_origins.TryGetValue(eventId, out Origin? origin))
                {
                    return new MethodInvocationResult(StatusCodes.BadNoData);
                }
                if (!m_isPublished)
                {
                    return new MethodInvocationResult(StatusCodes.BadWaitingForInitialData);
                }
                WoTEventOriginDataType value = CreateOrigin(origin);
                return new MethodInvocationResult(
                    ServiceResult.Good, [new Variant(new ExtensionObject(value))]);
            }
        }

        private WoTEventOriginDataType CreateOrigin(Origin origin)
        {
            var result = new WoTEventOriginDataType
            {
                IdentityMode = IdentityMode,
                BindingId = JsonPointer,
                Generation = m_publishedGeneration,
                SourceDocumentId = ResourceXid,
                ReceiveTime = origin.ReceiveTime
            };
            if (origin.Captured is not { } captured)
            {
                return result;
            }
            if (captured.Source.IsAuthenticated)
            {
                result.SourceServerUri = captured.Source.ServerUri;
                result.EncodingMask |= 1U;
            }
            if (captured.HasEventType)
            {
                result.SourceEventType = captured.EventType;
                result.EncodingMask |= 2U;
            }
            if (captured.HasSourceNode)
            {
                result.SourceNode = captured.SourceNode;
                result.EncodingMask |= 4U;
            }
            if (captured.HasEventId)
            {
                result.SourceEventId = captured.EventId;
                result.EncodingMask |= 8U;
            }
            if (captured.HasConditionId)
            {
                result.SourceConditionId = captured.ConditionId;
                result.EncodingMask |= 16U;
            }
            if (captured.HasBranchId)
            {
                result.SourceBranchId = captured.BranchId;
                result.EncodingMask |= 32U;
            }
            if (captured.HasTime)
            {
                result.SourceTime = captured.Time;
                result.EncodingMask |= 64U;
            }
            if (captured.HasReceiveTime)
            {
                result.SourceReceiveTime = captured.ReceiveTime;
                result.EncodingMask |= 128U;
            }
            return result;
        }

        private void UpdateDescriptor(WotCapturedEvent? captured)
        {
            if (m_descriptor is not { } descriptor)
            {
                return;
            }
            descriptor.Availability!.Value = StatusCodes.Good;
            if (captured?.Source.IsAuthenticated == true)
            {
                descriptor.SourceServerUri!.Value = captured.Source.ServerUri;
                descriptor.SourceServerUri.StatusCode = StatusCodes.Good;
            }
        }

        private sealed record Origin(
            WotCapturedEvent? Captured, DateTimeUtc ReceiveTime, ArrayOf<DataValue> Fields);

        private readonly Dictionary<ByteString, Origin> m_origins = [];
        private readonly List<WeakReference<WoTEventBindingState>> m_descriptorAliases = [];
        private WoTEventBindingState? m_descriptor;
        private uint m_publishedGeneration;
        private bool m_isPublished;
        private StatusCode m_failureStatus = StatusCodes.Good;
    }
}
