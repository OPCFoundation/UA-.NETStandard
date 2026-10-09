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
            if (m_root is null)
            {
                return;
            }
            var groups = new Dictionary<string, SchemaGroupState>(StringComparer.Ordinal);
            var resources = new Dictionary<string, SchemaFileState>(StringComparer.Ordinal);
            foreach (SchemaRegistryStore.SchemaEntry entry in entries)
            {
                string[] parts = entry.Reference.Entity.Xid!.Substring(1).Split('/');
                string groupId = parts[1];
                string resourceId = parts[3];
                string resourceXid = "/schemagroups/" + groupId + "/schemas/" + resourceId;
                if (!groups.TryGetValue(groupId, out SchemaGroupState? group))
                {
                    group = new SchemaGroupState(m_root);
                    group.Create(SystemContext, Instance("/schemagroups/" + groupId),
                        new QualifiedName(groupId, NamespaceIndexes[0]), new LocalizedText(groupId),
                        assignNodeIds: false);
                    group.ReferenceTypeId = Ua.ReferenceTypeIds.Organizes;
                    XRegistryProjectionEngine.SetValue(group.GroupId, groupId);
                    string namespaceUri = entry.Registration?.NamespaceUri ?? m_options.NamespaceUris[groupId];
                    XRegistryProjectionEngine.SetValue(group.Name, namespaceUri);
                    XRegistryProjectionEngine.SetValue(group.NamespaceUri, namespaceUri);
                    groups.Add(groupId, group);
                }
                if (!resources.TryGetValue(resourceXid, out SchemaFileState? logical))
                {
                    logical = CreateFile(group, resourceXid, resourceId, entry, exact: false);
                    logical.AddVersions(SystemContext);
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
            foreach (SchemaGroupState existing in m_schemaGroups)
            {
                m_root.RemoveChild(existing);
                await DeleteNodeAsync(SystemContext, existing.NodeId, cancellationToken).ConfigureAwait(false);
            }
            m_schemaGroups.Clear();
            foreach (SchemaGroupState group in groups.Values)
            {
                SystemContext.AssignInstanceChildNodeIds(group);
                m_root.AddChild(group);
                await AddPredefinedNodeAsync(SystemContext, group, cancellationToken).ConfigureAwait(false);
                m_schemaGroups.Add(group);
            }
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
        }

        private NodeId Instance(string xid) => new("SchemaRegistry" + xid, NamespaceIndexes[0]);

        private readonly List<SchemaGroupState> m_schemaGroups = [];
    }
}
