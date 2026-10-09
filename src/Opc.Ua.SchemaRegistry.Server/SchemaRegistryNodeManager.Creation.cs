/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.SchemaRegistry.Server
{
    public sealed partial class SchemaRegistryNodeManager
    {
        private void BindCreation(ISystemContext context, SchemaRegistryState root)
        {
            root.AddCreateGroup(context).AddGetOrCreateGroup(context);
            root.CreateGroup!.OnCallAsync = async (caller, _, _, id, ct) =>
            {
                await CreateGroupAsync(caller, id, false, ct).ConfigureAwait(false);
                return new CreateGroupMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    GroupNodeId = Instance("/schemagroups/" + id)
                };
            };
            root.GetOrCreateGroup!.OnCallAsync = async (caller, _, _, id, ct) =>
            {
                bool created = await CreateGroupAsync(caller, id, true, ct).ConfigureAwait(false);
                return new GetOrCreateGroupMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    GroupNodeId = Instance("/schemagroups/" + id),
                    Created = created
                };
            };
        }

        private async ValueTask<bool> CreateGroupAsync(ISystemContext caller, string id, bool getOrCreate,
            CancellationToken cancellationToken)
        {
            CheckCreation(caller);
            SchemaRegistryStore.Generation current = m_store.CaptureCatalog();
            bool exists = current.EntityEpochs.ContainsKey("/schemagroups/" + id);
            if (exists)
            {
                if (!getOrCreate)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                }
                return false;
            }
            if (!m_options.NamespaceUris.TryGetValue(id, out string? uri))
            {
                throw new ServiceResultException(StatusCodes.BadNotFound,
                    "CreateGroup needs an independently configured NamespaceUri; use RegisterSchema for new source identities.");
            }
            await m_store.CreateGroupAsync(id, uri, cancellationToken).ConfigureAwait(false);
            return true;
        }

        private void BindGroupCreation(SchemaGroupState group, string id)
        {
            group.AddCreateResource(SystemContext).AddGetOrCreateResource(SystemContext);
            group.CreateResource!.OnCallAsync = async (caller, _, _, resource, version, open, ct) =>
            {
                (SchemaRegistrationDataType descriptor, NodeId node, uint handle, _) =
                    await CreateResourceAsync(caller, id, resource, version, open, false, ct).ConfigureAwait(false);
                return new CreateResourceMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    ResourceNodeId = node,
                    AssignedVersionId = descriptor.VersionId!,
                    FileHandle = handle
                };
            };
            group.GetOrCreateResource!.OnCallAsync = async (caller, _, _, resource, version, open, ct) =>
            {
                (SchemaRegistrationDataType descriptor, NodeId node, uint handle, bool created) =
                    await CreateResourceAsync(caller, id, resource, version, open, true, ct).ConfigureAwait(false);
                return new GetOrCreateResourceMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    ResourceNodeId = node,
                    AssignedVersionId = descriptor.VersionId!,
                    FileHandle = handle,
                    Created = created
                };
            };
        }

        private async ValueTask<(SchemaRegistrationDataType Descriptor, NodeId Node, uint Handle, bool Created)>
            CreateResourceAsync(ISystemContext caller, string group, string resource, string version, bool open,
                bool getOrCreate, CancellationToken cancellationToken)
        {
            CheckCreation(caller);
            string resourceXid = "/schemagroups/" + group + "/schemas/" + resource;
            SchemaRegistryStore.Generation current = m_store.CaptureCatalog();
            if (getOrCreate && string.IsNullOrEmpty(version) && current.Defaults.TryGetValue(resourceXid,
                out SchemaRegistryStore.DefaultSelection? selected))
            {
                version = selected.VersionXid.Split('/')[6];
            }
            string? name = m_options.SchemaNames.TryGetValue(resourceXid, out string? configured) ? configured : null;
            string? format = null;
            string? namespaceUri = current.Groups.TryGetValue(group, out string? uri) ? uri :
                m_options.NamespaceUris.TryGetValue(group, out uri) ? uri : null;
            foreach (SchemaRegistryStore.SchemaEntry existing in current.Entries.Values)
            {
                if (existing.Reference.Entity.Xid!.StartsWith(resourceXid + "/versions/", StringComparison.Ordinal))
                {
                    name = existing.Registration?.SchemaName ?? name;
                    namespaceUri = existing.Registration?.NamespaceUri ?? namespaceUri;
                    format = existing.Format;
                }
            }
            if (name is null || namespaceUri is null)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound,
                    "CreateResource requires an independently configured schema subject or a registered Resource.");
            }
            foreach (ISchemaFormatProvider provider in m_formats)
            {
                if (XRegistryIdentifier.FromSourceIdentity(name + "/" + SchemaRegistryStore.FormatToken(provider.Format)) == resource)
                {
                    format = provider.Format;
                }
            }
            if (format is null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The ResourceId has no matching source subject/format.");
            }
            if (string.IsNullOrEmpty(version))
            {
                version = checked(current.Revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            string xid = resourceXid + "/versions/" + version;
            var descriptor = new SchemaRegistrationDataType
            {
                NamespaceUri = namespaceUri,
                SchemaName = name,
                Format = format,
                VersionId = version,
                EntityUri = EntityUri(xid),
                ResourceUri = EntityUri(resourceXid),
                MakeDefault = !current.Defaults.ContainsKey(resourceXid)
            };
            bool exists = current.Entries.ContainsKey(xid) || current.Drafts.ContainsKey(xid);
            descriptor = await m_store.CreateDraftAsync(descriptor, getOrCreate, cancellationToken).ConfigureAwait(false);
            NodeId node = Instance(m_store.RegistrationReference(descriptor).Entity.Xid!);
            uint handle = 0;
            if (open)
            {
                SchemaFileState file = FindSchemaFile(node) ??
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The committed Version projection is unavailable.");
                BeginSchemaUploadMethodStateResult opened = await BeginUploadAsync(caller, descriptor, cancellationToken, file)
                    .ConfigureAwait(false);
                if (ServiceResult.IsBad(opened.ServiceResult))
                {
                    throw new ServiceResultException(opened.ServiceResult);
                }
                handle = opened.FileHandle;
            }
            return (descriptor, node, handle, !exists);
        }

        private string EntityUri(string xid) => m_options.EntityUri?.Invoke(xid) ??
            "urn:opcua:schema:" + Uri.EscapeDataString(xid);

        private void CheckCreation(ISystemContext caller)
        {
            ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Write);
            if (ServiceResult.IsBad(allowed))
            {
                throw new ServiceResultException(allowed);
            }
        }

        private SchemaFileState? FindSchemaFile(NodeId id)
        {
            foreach (SchemaGroupState group in m_schemaGroups)
            {
                SchemaFileState? found = Find(group);
                if (found is not null)
                {
                    return found;
                }
            }
            return null;

            SchemaFileState? Find(NodeState node)
            {
                if (node is SchemaFileState file && file.NodeId == id)
                {
                    return file;
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(SystemContext, children);
                foreach (BaseInstanceState child in children)
                {
                    SchemaFileState? result = Find(child);
                    if (result is not null)
                    {
                        return result;
                    }
                }
                return null;
            }
        }

        private void AssignSchemaNodeIds(NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child.NodeId.IsNull)
                {
                    child.NodeId = new NodeId(NodeId.ToExpandedNodeId(node.NodeId, Server.NamespaceUris).ToString() +
                        "/" + Uri.EscapeDataString(Server.NamespaceUris.GetString(child.BrowseName.NamespaceIndex) ??
                            throw new InvalidOperationException("A child namespace is not registered.")) +
                        ":" + Uri.EscapeDataString(child.BrowseName.Name ??
                            throw new InvalidOperationException("A child BrowseName is empty.")), NamespaceIndexes[0]);
                }
                AssignSchemaNodeIds(child);
            }
        }
    }
}
