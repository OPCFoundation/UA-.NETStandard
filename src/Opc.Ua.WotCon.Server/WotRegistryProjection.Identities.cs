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
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server
{
    internal sealed partial class WotRegistryProjection
    {
        private void WireTypedRegistryMethods(BaseObjectState registry)
        {
            if (registry is not WoTRegistryState typed)
            {
                return;
            }
            if (typed.CreateDocumentGroup is { } create)
            {
                create.OnCallMethod2Async = (c, m, o, i, output, ct) =>
                    OnProvisionGroupAsync(registry.NodeId, false, c, o, i, output, ct);
                create.Executable = create.UserExecutable = m_registry is IWotTypedRegistryService;
            }
            if (typed.GetOrCreateDocumentGroup is { } getOrCreate)
            {
                getOrCreate.OnCallMethod2Async = (c, m, o, i, output, ct) =>
                    OnProvisionGroupAsync(registry.NodeId, true, c, o, i, output, ct);
                getOrCreate.Executable = getOrCreate.UserExecutable = m_registry is IWotTypedRegistryService;
            }
        }

        private void ConfigureTypedGroupNode(GroupState node, WotResourceGroup group)
        {
            bool supported = m_registry is IWotTypedRegistryService && group.CatalogUri is not null;
            if (node is ThingModelGroupState tm)
            {
                XRegistryProjectionEngine.SetValue(tm.CatalogUri, group.CatalogUri ?? string.Empty);
                tm.AddCreateThingModelResource(m_manager.SystemContext)
                    .AddGetOrCreateThingModelResource(m_manager.SystemContext);
                BindResourceMethod(tm.CreateThingModelResource!, false);
                BindResourceMethod(tm.GetOrCreateThingModelResource!, true);
            }
            else if (node is ThingDescriptionGroupState td)
            {
                XRegistryProjectionEngine.SetValue(td.CatalogUri, group.CatalogUri ?? string.Empty);
                td.AddCreateThingDescriptionResource(m_manager.SystemContext)
                    .AddGetOrCreateThingDescriptionResource(m_manager.SystemContext);
                BindResourceMethod(td.CreateThingDescriptionResource!, false);
                BindResourceMethod(td.GetOrCreateThingDescriptionResource!, true);
            }

            void BindResourceMethod(MethodState method, bool getOrCreate)
            {
                method.Executable = method.UserExecutable = supported;
                method.OnCallMethod2Async = (c, m, o, i, output, ct) => OnProvisionResourceAsync(
                    group.GroupId, group.Kind, node.NodeId, getOrCreate, c, o, i, output, ct);
            }
        }

        private async ValueTask<ServiceResult> OnProvisionGroupAsync(
            NodeId receiver,
            bool getOrCreate,
            ISystemContext context,
            NodeId objectId,
            ArrayOf<Variant> input,
            List<Variant> output,
            CancellationToken cancellationToken)
        {
            ServiceResult access = m_manager.CheckManagementAccess(
                context, getOrCreate ? "GetOrCreateDocumentGroup" : "CreateDocumentGroup");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            if (objectId != receiver)
            {
                return StatusCodes.BadMethodInvalid;
            }
            if (input.Count != 2 ||
                !input[0].TryGetValue(out int kind) ||
                !WotDocumentKinds.IsDocument((WoTDocumentKindEnum)kind) ||
                !input[1].TryGetValue(out string catalogUri) ||
                !WotRegistryIdentity.IsAbsoluteUri(catalogUri))
            {
                return StatusCodes.BadInvalidArgument;
            }
            if (m_registry is not IWotTypedRegistryService typed)
            {
                return StatusCodes.BadNotSupported;
            }
            try
            {
                WotDocumentGroupResult result = getOrCreate
                    ? await typed.GetOrCreateDocumentGroupAsync(
                        (WoTDocumentKindEnum)kind, catalogUri, cancellationToken).ConfigureAwait(false)
                    : await typed.CreateDocumentGroupAsync(
                        (WoTDocumentKindEnum)kind, catalogUri, cancellationToken).ConfigureAwait(false);
                await m_engine.ReconcileProjectionAsync(CancellationToken.None).ConfigureAwait(false);
                output.Clear();
                output.Add(new Variant(GroupNodeId(result.Group.GroupId)));
                output.Add(new Variant(result.Group.GroupId));
                if (getOrCreate)
                {
                    output.Add(new Variant(result.Created));
                }
                return ServiceResult.Good;
            }
            catch (ServiceResultException ex)
            {
                return ex.Result;
            }
        }

        private async ValueTask<ServiceResult> OnProvisionResourceAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            NodeId receiver,
            bool getOrCreate,
            ISystemContext context,
            NodeId objectId,
            ArrayOf<Variant> input,
            List<Variant> output,
            CancellationToken cancellationToken)
        {
            ServiceResult access = m_manager.CheckManagementAccess(
                context, getOrCreate ? "GetOrCreateDocumentResource" : "CreateDocumentResource");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            if (objectId != receiver)
            {
                return StatusCodes.BadMethodInvalid;
            }
            WotResourceGroup? group = m_registry.Current.FindGroup(groupId);
            if (group is null ||
                group.Kind != kind ||
                !WotRegistryIdentity.IsAbsoluteUri(group.CatalogUri) ||
                input.Count != 3 ||
                !input[0].TryGetValue(out string sourceId) ||
                !WotRegistryIdentity.IsAbsoluteUri(sourceId) ||
                !input[1].TryGetValue(out string versionId) ||
                !input[2].TryGetValue(out bool requestOpen))
            {
                return StatusCodes.BadInvalidArgument;
            }
            if (m_registry is not IWotTypedRegistryService typed ||
                (requestOpen && m_registry is not WotRegistryService))
            {
                return StatusCodes.BadNotSupported;
            }
            var preparation = new ResourcePreparation(this, context, cancellationToken);
            try
            {
                WotDocumentResourceResult result;
                if (m_registry is WotRegistryService stock)
                {
                    result = await stock.ProvisionDocumentResourceAsync(
                        groupId, kind, sourceId, versionId, getOrCreate,
                        requestOpen ? preparation.PrepareAsync : null, preparation.CancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    result = getOrCreate
                        ? await typed.GetOrCreateDocumentResourceAsync(
                            groupId, kind, sourceId, versionId, cancellationToken).ConfigureAwait(false)
                        : await typed.CreateDocumentResourceAsync(
                            groupId, kind, sourceId, versionId, cancellationToken).ConfigureAwait(false);
                }
                await m_engine.ReconcileProjectionAsync(CancellationToken.None).ConfigureAwait(false);
                output.Clear();
                output.Add(new Variant(m_engine.EventSourceFor(result.Resource.Xid).NodeId));
                output.Add(new Variant(ResourceNodeId(
                    result.Resource.GroupId, result.Resource.ResourceId, result.Version.VersionId)));
                output.Add(new Variant(result.Resource.ResourceId));
                output.Add(new Variant(result.Version.VersionId));
                output.Add(new Variant(preparation.FileHandle));
                if (getOrCreate)
                {
                    output.Add(new Variant(result.CreatedResource));
                    output.Add(new Variant(result.CreatedVersion));
                }
                preparation.Complete();
                return ServiceResult.Good;
            }
            catch (OperationCanceledException) when (preparation.SessionClosed)
            {
                return StatusCodes.BadSessionClosed;
            }
            catch (ServiceResultException ex)
            {
                return ex.Result;
            }
            finally
            {
                await preparation.DisposeAsync().ConfigureAwait(false);
            }
        }

        private sealed class ResourcePreparation : IAsyncDisposable
        {
            public ResourcePreparation(
                WotRegistryProjection projection,
                ISystemContext context,
                CancellationToken cancellationToken)
            {
                m_projection = projection;
                m_context = context;
                m_transactionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, CancellationToken.None);
            }

            public uint FileHandle => m_reservation?.FileHandle ?? 0;
            public CancellationToken CancellationToken => m_transactionCancellation.Token;
            public bool SessionClosed => m_reservation?.SessionClosedToken.IsCancellationRequested == true;

            public async ValueTask PrepareAsync(
                WotResource resource,
                WotResourceVersion version,
                IWotRegistryVersionLease lease,
                CancellationToken cancellationToken)
            {
                await m_projection.m_engine.ReconcileProjectionAsync(cancellationToken).ConfigureAwait(false);
                m_reservation = await m_projection.m_engine.ReserveResourceWriteAsync(
                    new ResourceAdapter(resource, version, lease), m_context, cancellationToken).ConfigureAwait(false);
                m_sessionClosedRegistration = m_reservation.SessionClosedToken
                    .Register(m_transactionCancellation.Cancel);
            }

            public void Complete()
            {
                m_reservation?.Complete();
            }

            public async ValueTask DisposeAsync()
            {
                m_sessionClosedRegistration.Dispose();
                m_transactionCancellation.Dispose();
                if (m_reservation is not null)
                {
                    await m_reservation.DisposeAsync().ConfigureAwait(false);
                }
            }

            private readonly WotRegistryProjection m_projection;
            private readonly ISystemContext m_context;
            private readonly CancellationTokenSource m_transactionCancellation;
            private CancellationTokenRegistration m_sessionClosedRegistration;
            private XRegistryResourceFileReservation? m_reservation;
        }
    }
}
