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
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.SchemaRegistry.Server
{
    public sealed partial class SchemaRegistryNodeManager
    {
        private async ValueTask ProjectSchemasAsync(
            ArrayOf<SchemaRegistryStore.SchemaEntry> entries, CancellationToken cancellationToken)
        {
            await m_projectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ProjectSchemasCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_projectionGate.Release();
            }
        }

        private async ValueTask ProjectSchemasCoreAsync(CancellationToken cancellationToken)
        {
            if (m_root is null)
            {
                return;
            }
            var groups = new Dictionary<string, SchemaGroupState>(StringComparer.Ordinal);
            var resources = new Dictionary<string, SchemaFileState>(StringComparer.Ordinal);
            SchemaRegistryStore.Generation catalog = m_store.CaptureCatalog();
            foreach (KeyValuePair<string, string> admitted in catalog.Groups)
            {
                groups.Add(admitted.Key, CreateSchemaGroup(admitted.Key, admitted.Value, catalog));
            }
            foreach (SchemaRegistryStore.SchemaEntry entry in catalog.Entries.Values)
            {
                string[] parts = entry.Reference.Entity.Xid!.Substring(1).Split('/');
                string groupId = parts[1];
                string resourceId = parts[3];
                string resourceXid = "/schemagroups/" + groupId + "/schemas/" + resourceId;
                if (!groups.TryGetValue(groupId, out SchemaGroupState? group))
                {
                    string namespaceUri = entry.Registration?.NamespaceUri ?? m_options.NamespaceUris[groupId];
                    group = CreateSchemaGroup(groupId, namespaceUri, catalog);
                    groups.Add(groupId, group);
                }
                if (!resources.TryGetValue(resourceXid, out SchemaFileState? logical))
                {
                    SchemaRegistryStore.SchemaEntry delegated = catalog.Defaults.TryGetValue(resourceXid,
                        out SchemaRegistryStore.DefaultSelection? selected)
                        ? catalog.Entries[selected.VersionXid] : entry;
                    logical = CreateFile(group, resourceXid, resourceId, delegated, exact: false);
                    logical.AddVersions(SystemContext);
                    logical.AddMetaEpoch(SystemContext);
                    XRegistryProjectionEngine.SetValue(logical.MetaEpoch, catalog.EntityEpochs[resourceXid]);
                    group.AddChild(logical);
                    resources.Add(resourceXid, logical);
                }
                var version = CreateFile(logical.Versions!, entry.Reference.Entity.Xid!, parts[5], entry, exact: true);
                logical.Versions!.AddChild(version);
                if (m_store.IsDefault(entry.Reference.Entity.Xid!))
                {
                    SetFileValues(logical, entry);
                }
            }
            foreach (KeyValuePair<string, SchemaRegistrationDataType> item in catalog.Drafts)
            {
                string[] parts = item.Key.Split('/');
                SchemaRegistrationDataType descriptor = item.Value;
                if (!groups.TryGetValue(parts[2], out SchemaGroupState? group))
                {
                    group = CreateSchemaGroup(parts[2], descriptor.NamespaceUri!, catalog);
                    groups.Add(parts[2], group);
                }
                string resourceXid = "/schemagroups/" + parts[2] + "/schemas/" + parts[4];
                if (!resources.TryGetValue(resourceXid, out SchemaFileState? logical))
                {
                    logical = CreateDraftFile(group, resourceXid, parts[4], descriptor, catalog);
                    logical.AddVersions(SystemContext);
                    group.AddChild(logical);
                    resources.Add(resourceXid, logical);
                }
                logical.Versions!.AddChild(CreateDraftFile(logical.Versions, item.Key, parts[6], descriptor, catalog));
            }
            foreach (SchemaGroupState existing in m_schemaGroups)
            {
                m_root.RemoveChild(existing);
                await DeleteNodeAsync(SystemContext, existing.NodeId, cancellationToken).ConfigureAwait(false);
            }
            m_schemaGroups.Clear();
            foreach (SchemaGroupState group in groups.Values)
            {
                AssignSchemaNodeIds(group);
                m_root.AddChild(group);
                await AddPredefinedNodeAsync(SystemContext, group, cancellationToken).ConfigureAwait(false);
                m_schemaGroups.Add(group);
            }
        }

        private SchemaGroupState CreateSchemaGroup(string id, string uri, SchemaRegistryStore.Generation catalog)
        {
            var group = new SchemaGroupState(m_root);
            group.Create(SystemContext, Instance("/schemagroups/" + id),
                new QualifiedName(id, NamespaceIndexes[0]), new LocalizedText(id), assignNodeIds: false);
            group.ReferenceTypeId = Ua.ReferenceTypeIds.Organizes;
            group.AddXid(SystemContext).AddEpoch(SystemContext);
            XRegistryProjectionEngine.SetValue(group.GroupId, id);
            XRegistryProjectionEngine.SetValue(group.Name, uri);
            XRegistryProjectionEngine.SetValue(group.NamespaceUri, uri);
            XRegistryProjectionEngine.SetValue(group.Xid, "/schemagroups/" + id);
            XRegistryProjectionEngine.SetValue(group.Epoch, catalog.EntityEpochs["/schemagroups/" + id]);
            BindGroupCreation(group, id);
            XRegistryProjectionEngine.LinkMethodArguments(group, SystemContext);
            return group;
        }

        private SchemaFileState CreateDraftFile(NodeState parent, string xid, string name,
            SchemaRegistrationDataType descriptor, SchemaRegistryStore.Generation catalog)
        {
            var file = new SchemaFileState(parent);
            file.Create(SystemContext, Instance(xid), new QualifiedName(name, NamespaceIndexes[0]),
                new LocalizedText(name), assignNodeIds: false);
            file.ReferenceTypeId = Ua.ReferenceTypeIds.Organizes;
            file.AddXid(SystemContext).AddEpoch(SystemContext).AddFormat(SystemContext).AddVersionId(SystemContext);
            XRegistryProjectionEngine.SetValue(file.ResourceId, xid.Split('/')[4]);
            XRegistryProjectionEngine.SetValue(file.SchemaName, descriptor.SchemaName!);
            XRegistryProjectionEngine.SetValue(file.Name, descriptor.SchemaName! + " (" + descriptor.Format + ")");
            XRegistryProjectionEngine.SetValue(file.Xid, xid);
            XRegistryProjectionEngine.SetValue(file.Epoch, catalog.EntityEpochs[xid]);
            XRegistryProjectionEngine.SetValue(file.Format, descriptor.Format!);
            XRegistryProjectionEngine.SetValue(file.Size, 0ul);
            XRegistryProjectionEngine.SetValue(file.Writable, true);
            XRegistryProjectionEngine.SetValue(file.UserWritable, true);
            file.Open!.OnCallAsync = async (caller, _, _, mode, ct) =>
            {
                if ((mode & (byte)OpenFileMode.Write) == 0 || xid.Split('/').Length != 7)
                {
                    return new OpenMethodStateResult { ServiceResult = StatusCodes.BadNotReadable };
                }
                BeginSchemaUploadMethodStateResult opened = await BeginUploadAsync(caller, descriptor, ct, file, mode)
                    .ConfigureAwait(false);
                return new OpenMethodStateResult { ServiceResult = opened.ServiceResult, FileHandle = opened.FileHandle };
            };
            XRegistryProjectionEngine.LinkMethodArguments(file, SystemContext);
            return file;
        }

        private SchemaFileState CreateFile(
            NodeState parent,
            string xid,
            string browseName,
            SchemaRegistryStore.SchemaEntry entry,
            bool exact)
        {
            var file = new SchemaFileState(parent);
            file.Create(SystemContext, Instance(xid), new QualifiedName(browseName, NamespaceIndexes[0]),
                new LocalizedText(browseName), assignNodeIds: false);
            file.ReferenceTypeId = Ua.ReferenceTypeIds.Organizes;
            file.AddXid(SystemContext).AddEpoch(SystemContext).AddFormat(SystemContext).AddContentType(SystemContext)
                .AddVersionId(SystemContext);
            file.AddIsDefault(SystemContext).AddNativeContent(SystemContext);
            file.AddAncestor(SystemContext);
            if (entry.Metadata.Compatibility is not null)
            {
                file.AddCompatibility(SystemContext);
            }
            if (entry.Metadata.DataTypeEncoding is not null)
            {
                file.AddDataTypeEncoding(SystemContext);
            }
            if (entry.Metadata.ModelVersion is not null)
            {
                file.AddModelVersion(SystemContext);
            }
            if (entry.Metadata.ConfigurationVersion is not null)
            {
                file.AddConfigurationVersion(SystemContext);
            }
            XRegistryProjectionEngine.SetValue(file.ResourceId, entry.Reference.Entity.Xid!.Split('/')[4]);
            string[] path = xid.Substring(1).Split('/');
            string resourceXid = "/" + string.Join("/", path, 0, 4);
            string schemaName = entry.Registration?.SchemaName ?? m_options.SchemaNames[resourceXid];
            XRegistryProjectionEngine.SetValue(file.Name, schemaName + " (" + entry.Format + ")");
            XRegistryProjectionEngine.SetValue(file.SchemaName, schemaName);
            XRegistryProjectionEngine.SetValue(file.Xid, xid);
            XRegistryProjectionEngine.SetValue(file.Format, entry.Format);
            XRegistryProjectionEngine.SetValue(file.ContentType, entry.ContentType);
            XRegistryProjectionEngine.SetValue(file.SchemaIdAlg, entry.Reference.SchemaIdAlg!);
            XRegistryProjectionEngine.SetValue(file.SchemaId, exact ? entry.Reference.SchemaId : ByteString.Empty);
            if (exact)
            {
                SetFileValues(file, entry);
                XRegistryProjectionEngine.SetValue(file.VersionId, browseName);
                XRegistryProjectionEngine.SetValue(file.IsDefault, m_store.IsDefault(xid));
            }
            ServiceResult CheckFileAccess(ISystemContext caller, RegistryAccessKind kind)
            {
                ServiceResult allowed = m_authorize(caller, kind);
                if (ServiceResult.IsBad(allowed))
                {
                    return allowed;
                }
                foreach (SchemaRegistryStore.SchemaEntry candidate in m_store.Entries)
                {
                    if (candidate.Reference.Entity.Xid == xid || !exact &&
                        candidate.Reference.Entity.Xid!.StartsWith(xid + "/versions/", StringComparison.Ordinal) &&
                        m_store.IsDefault(candidate.Reference.Entity.Xid))
                    {
                        return IsVisible(caller, candidate.Reference)
                            ? ServiceResult.Good : StatusCodes.BadUserAccessDenied;
                    }
                }
                return StatusCodes.BadNotFound;
            }
            m_files.Bind(file, () =>
            {
                foreach (SchemaRegistryStore.SchemaEntry candidate in m_store.Entries)
                {
                    if (candidate.Reference.Entity.Xid == xid || !exact &&
                        candidate.Reference.Entity.Xid!.StartsWith(xid + "/versions/", StringComparison.Ordinal) &&
                        m_store.IsDefault(candidate.Reference.Entity.Xid))
                    {
                        return candidate.Document;
                    }
                }
                throw new ServiceResultException(StatusCodes.BadNotFound, "No configured schema Version was selected.");
            }, CheckFileAccess);
            if (exact)
            {
                var readOpen = file.Open!.OnCall;
                XRegistryProjectionEngine.SetValue(file.Writable, true);
                XRegistryProjectionEngine.SetValue(file.UserWritable, true);
                file.Open.OnCallAsync = async (caller, method, owner, mode, ct) =>
                {
                    if ((mode & (byte)OpenFileMode.Write) == 0)
                    {
                        uint handle = 0;
                        ServiceResult result = readOpen!(caller, method, owner, mode, ref handle);
                        return new OpenMethodStateResult { ServiceResult = result, FileHandle = handle };
                    }
                    var descriptor = entry.Registration is { } registered
                        ? (SchemaRegistrationDataType)registered.Clone() : new SchemaRegistrationDataType
                        {
                            NamespaceUri = m_options.NamespaceUris[path[1]],
                            SchemaName = schemaName,
                            Format = entry.Format,
                            VersionId = path[5],
                            EntityUri = entry.Reference.EntityUri
                        };
                    descriptor.ExpectedEpoch = entry.Epoch;
                    descriptor.MakeDefault = false;
                    BeginSchemaUploadMethodStateResult opened = await BeginUploadAsync(caller, descriptor, ct, file, mode)
                        .ConfigureAwait(false);
                    return new OpenMethodStateResult { ServiceResult = opened.ServiceResult, FileHandle = opened.FileHandle };
                };
            }
            GuardFileValues(file, CheckFileAccess);
            XRegistryProjectionEngine.LinkMethodArguments(file, SystemContext);
            return file;
        }

        private void GuardFileValues(NodeState node, Func<ISystemContext, RegistryAccessKind, ServiceResult> authorize)
        {
            if (node is BaseVariableState variable)
            {
                variable.OnSimpleReadValue = (ISystemContext caller, NodeState _, ref Variant value) =>
                {
                    ServiceResult access = authorize(caller, RegistryAccessKind.Read);
                    value = ServiceResult.IsGood(access) ? variable.WrappedValue : Variant.Null;
                    return access;
                };
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                GuardFileValues(child, authorize);
            }
        }

        private void SetFileValues(SchemaFileState file, SchemaRegistryStore.SchemaEntry entry)
        {
            XRegistryProjectionEngine.SetValue(file.SchemaId, entry.Reference.SchemaId);
            XRegistryProjectionEngine.SetValue(file.SchemaIdAlg, entry.Reference.SchemaIdAlg!);
            XRegistryProjectionEngine.SetValue(file.Format, entry.Format);
            XRegistryProjectionEngine.SetValue(file.ContentType, entry.ContentType);
            XRegistryProjectionEngine.SetValue(file.Epoch, entry.Epoch);
            XRegistryProjectionEngine.SetValue(file.Size, (ulong)entry.Document.Length);
            XRegistryProjectionEngine.SetValue(file.NativeContent, m_store.Read(entry.Reference).Document);
            XRegistryProjectionEngine.SetValue(file.VersionId, entry.Reference.Entity.Xid!.Split('/')[6]);
            XRegistryProjectionEngine.SetValue(file.IsDefault, m_store.IsDefault(entry.Reference.Entity.Xid!));
            XRegistryProjectionEngine.SetValue(file.Ancestor, entry.Metadata.Ancestor);
            if (entry.Metadata.Compatibility is not null)
            {
                XRegistryProjectionEngine.SetValue(file.Compatibility, entry.Metadata.Compatibility);
            }
            if (entry.Metadata.DataTypeEncoding is not null)
            {
                XRegistryProjectionEngine.SetValue(file.DataTypeEncoding, entry.Metadata.DataTypeEncoding);
            }
            if (entry.Metadata.ModelVersion is not null)
            {
                XRegistryProjectionEngine.SetValue(file.ModelVersion, entry.Metadata.ModelVersion);
            }
            if (entry.Metadata.ConfigurationVersion is not null)
            {
                XRegistryProjectionEngine.SetValue(file.ConfigurationVersion, entry.Metadata.ConfigurationVersion);
            }
        }

        private NodeId Instance(string xid) => new("SchemaRegistry" + xid, NamespaceIndexes[0]);

        private readonly List<SchemaGroupState> m_schemaGroups = [];
        private readonly SemaphoreSlim m_projectionGate = new(1, 1);
    }
}
