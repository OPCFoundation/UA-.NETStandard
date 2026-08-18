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
using Opc.Ua.Aas.V3;
using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.Aas.Server.Registry
{
    /// <summary>
    /// Adapts <see cref="IAasRegistryService"/> to the shared xRegistry projection engine.
    /// </summary>
    public sealed class AasRegistryProjection : IDisposable
    {
        /// <summary>
        /// Initializes a projection adapter.
        /// </summary>
        public AasRegistryProjection(
            ISystemContext systemContext,
            NamespaceTable namespaceUris,
            Func<NodeState, CancellationToken, ValueTask> addNodeAsync,
            Func<NodeId, CancellationToken, ValueTask> deleteNodeAsync,
            IAasRegistryService registry,
            Func<ISystemContext, string, ServiceResult>? checkManagementAccess = null)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_namespaceUris = namespaceUris;
            m_modelNs = (ushort)namespaceUris.GetIndex(Opc.Ua.Aas.V3.Namespaces.AasV3);
            var context = new XRegistryProjectionContext(
                systemContext ?? throw new ArgumentNullException(nameof(systemContext)),
                namespaceUris,
                m_modelNs,
                addNodeAsync ?? throw new ArgumentNullException(nameof(addNodeAsync)),
                deleteNodeAsync ?? throw new ArgumentNullException(nameof(deleteNodeAsync)),
                checkManagementAccess ?? DenyManagementAccess);
            m_engine = new XRegistryProjectionEngine(context, new Strategy(this), "AASRegistry");
        }

        /// <summary>
        /// Attaches the projection to the well-known registry Object.
        /// </summary>
        public ValueTask AttachAsync(BaseObjectState registryNode, CancellationToken ct)
        {
            return m_engine.AttachAsync(registryNode, ct);
        }

        /// <summary>
        /// Reconciles the AddressSpace projection with the current immutable snapshot.
        /// </summary>
        public ValueTask ReconcileAsync(CancellationToken ct)
        {
            return m_engine.ReconcileAsync(ct);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_engine.Dispose();
        }

        private static ServiceResult DenyManagementAccess(ISystemContext context, string operation)
        {
            return ServiceResult.Create(StatusCodes.BadNotSupported, "The AAS registry is not updateable.");
        }

        private GroupState CreateGroupNode(BaseObjectState registryNode, AasRegistryGroup group)
        {
            GroupState node = group.Kind switch
            {
                AasRegistryEntityKind.SubmodelTemplate => new AASSubmodelTemplateGroupState(registryNode),
                AasRegistryEntityKind.ConceptDictionary => new AASConceptDictionaryGroupState(registryNode),
                AasRegistryEntityKind.PackageStore => new AASPackageStoreGroupState(registryNode),
                _ => new AASShellGroupState(registryNode)
            };
            node.TypeDefinitionId = ExpandedNodeId.ToNodeId(GroupTypeId(group.Kind), m_namespaceUris!);
            return node;
        }

        private ResourceState CreateResourceNode(GroupState groupNode, AasRegistryResource resource)
        {
            ResourceState node = resource.Kind switch
            {
                AasRegistryEntityKind.ConceptDescription => new AASConceptDescriptionFileState(groupNode),
                AasRegistryEntityKind.Package => new AASPackageFileState(groupNode),
                AasRegistryEntityKind.Environment => new AASEnvironmentFileState(groupNode),
                _ => new AASSubmodelFileState(groupNode)
            };
            node.TypeDefinitionId = ExpandedNodeId.ToNodeId(ResourceTypeId(resource.Kind), m_namespaceUris!);
            return node;
        }

        private void ConfigureGroupNode(GroupState node, AasRegistryGroup group)
        {
            switch (node)
            {
                case AASShellGroupState shell:
                    XRegistryProjectionEngine.SetValue(shell.AasIdentifier, group.SourceIdentity);
                    XRegistryProjectionEngine.SetValue(shell.DisclosureTier, group.DisclosureTier);
                    XRegistryProjectionEngine.SetValue(shell.Authorization, group.Authorization);
                    break;
                case AASSubmodelTemplateGroupState template:
                    XRegistryProjectionEngine.SetValue(template.TemplateNamespace, group.SourceIdentity);
                    break;
                case AASConceptDictionaryGroupState dictionary:
                    XRegistryProjectionEngine.SetValue(dictionary.DictionaryIdentifier, group.SourceIdentity);
                    break;
                case AASPackageStoreGroupState store:
                    XRegistryProjectionEngine.SetValue(store.StoreIdentifier, group.SourceIdentity);
                    break;
            }
        }

        private void ConfigureResourceNode(ResourceState node, AasRegistryResource resource)
        {
            AasRegistryResourceVersion? version = resource.DefaultVersion;
            XRegistryProjectionEngine.SetValue(node.Format, version?.Format ?? string.Empty);
            XRegistryProjectionEngine.SetValue(node.ContentType, version?.ContentType ?? string.Empty);
            if (node is AASSubmodelFileState submodel)
            {
                XRegistryProjectionEngine.SetValue(submodel.SubmodelIdentifier, resource.SourceIdentity);
                XRegistryProjectionEngine.SetValue(submodel.SemanticId, resource.SemanticId);
                XRegistryProjectionEngine.SetValue(submodel.Template, resource.Template);
                XRegistryProjectionEngine.SetValue(submodel.Digest, version?.DigestHex ?? string.Empty);
                XRegistryProjectionEngine.SetValue(submodel.DigestAlg, version is null ? string.Empty : "Sha256");
                XRegistryProjectionEngine.SetValue(submodel.IsDefault, version is not null);
                XRegistryProjectionEngine.SetValue(submodel.DisclosureTier, resource.DisclosureTier);
                XRegistryProjectionEngine.SetValue(submodel.Authorization, resource.Authorization);
            }
            else if (node is AASConceptDescriptionFileState concept)
            {
                XRegistryProjectionEngine.SetValue(concept.ConceptIdentifier, resource.SourceIdentity);
            }
            else if (node is AASPackageFileState package)
            {
                XRegistryProjectionEngine.SetValue(package.PackageIdentifier, resource.SourceIdentity);
                XRegistryProjectionEngine.SetValue(package.Digest, version?.DigestHex ?? string.Empty);
                XRegistryProjectionEngine.SetValue(package.DigestAlg, version is null ? string.Empty : "Sha256");
            }
            else if (node is AASEnvironmentFileState environment)
            {
                XRegistryProjectionEngine.SetValue(environment.EnvironmentIdentifier, resource.SourceIdentity);
            }
        }

        private ExpandedNodeId GroupTypeId(AasRegistryEntityKind kind)
        {
            return kind switch
            {
                AasRegistryEntityKind.SubmodelTemplate => Opc.Ua.Aas.V3.ObjectTypeIds.AASSubmodelTemplateGroupType,
                AasRegistryEntityKind.ConceptDictionary => Opc.Ua.Aas.V3.ObjectTypeIds.AASConceptDictionaryGroupType,
                AasRegistryEntityKind.PackageStore => Opc.Ua.Aas.V3.ObjectTypeIds.AASPackageStoreGroupType,
                _ => Opc.Ua.Aas.V3.ObjectTypeIds.AASShellGroupType
            };
        }

        private ExpandedNodeId ResourceTypeId(AasRegistryEntityKind kind)
        {
            return kind switch
            {
                AasRegistryEntityKind.ConceptDescription => Opc.Ua.Aas.V3.ObjectTypeIds.AASConceptDescriptionFileType,
                AasRegistryEntityKind.Package => Opc.Ua.Aas.V3.ObjectTypeIds.AASPackageFileType,
                AasRegistryEntityKind.Environment => Opc.Ua.Aas.V3.ObjectTypeIds.AASEnvironmentFileType,
                _ => Opc.Ua.Aas.V3.ObjectTypeIds.AASSubmodelFileType
            };
        }

        private sealed class Strategy : IXRegistryProjectionStrategy
        {
            public Strategy(AasRegistryProjection projection)
            {
                m_projection = projection;
            }

            public IXRegistryProjectionSnapshot Current => m_projection.m_registry.Current;

            public GroupState CreateGroupNode(BaseObjectState registryNode, IXRegistryProjectionGroup group)
            {
                return m_projection.CreateGroupNode(registryNode, ((AasProjectionGroupAdapter)group).Group);
            }

            public ResourceState CreateResourceNode(GroupState groupNode, IXRegistryProjectionResource resource)
            {
                return m_projection.CreateResourceNode(groupNode, ((AasProjectionResourceAdapter)resource).Resource);
            }

            public void ConfigureGroupNode(GroupState node, IXRegistryProjectionGroup group)
            {
                m_projection.ConfigureGroupNode(node, ((AasProjectionGroupAdapter)group).Group);
            }

            public void ConfigureResourceNode(ResourceState node, IXRegistryProjectionResource resource)
            {
                m_projection.ConfigureResourceNode(node, ((AasProjectionResourceAdapter)resource).Resource);
            }

            public IXRegistryProjectedResourceFile? CreateResourceFile(
                ResourceState node,
                IXRegistryProjectionResource resource)
            {
                return null;
            }

            public ValueTask<IXRegistryProjectionGroup?> CreateGroupAsync(string groupId, CancellationToken ct)
            {
                return new ValueTask<IXRegistryProjectionGroup?>((IXRegistryProjectionGroup?)null);
            }

            public ValueTask<(IXRegistryProjectionGroup Group, bool Created)> GetOrCreateGroupAsync(
                string groupId,
                CancellationToken ct)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            public ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
                string groupId,
                string resourceId,
                CancellationToken ct)
            {
                return new ValueTask<IXRegistryProjectionResource?>((IXRegistryProjectionResource?)null);
            }

            public ValueTask<(IXRegistryProjectionResource Resource, bool Created)> GetOrCreateResourceAsync(
                string groupId,
                string resourceId,
                CancellationToken ct)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            public ValueTask<ServiceResult> DeleteGroupAsync(string groupId, long? epoch, CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> DeleteResourceAsync(
                string groupId,
                string resourceId,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> AddRegistryLabelAsync(
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> RemoveRegistryLabelAsync(string key, long? epoch, CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> AddGroupLabelAsync(
                string groupId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> RemoveGroupLabelAsync(
                string groupId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> AddResourceLabelAsync(
                string groupId,
                string resourceId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> RemoveResourceLabelAsync(
                string groupId,
                string resourceId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            private static ValueTask<ServiceResult> NotSupported()
            {
                return new ValueTask<ServiceResult>(
                    ServiceResult.Create(StatusCodes.BadNotSupported, "The AAS registry is not updateable."));
            }

            private readonly AasRegistryProjection m_projection;
        }

        private readonly IAasRegistryService m_registry;
        private readonly ushort m_modelNs;
        private readonly XRegistryProjectionEngine m_engine;
        private readonly NamespaceTable? m_namespaceUris;
    }
}
