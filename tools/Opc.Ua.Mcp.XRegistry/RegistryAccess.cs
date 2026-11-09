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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.XRegistry;
using RegistryTypes = Opc.Ua.XRegistry.ObjectTypeIds;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// The collection below a registry entity to enumerate.
    /// </summary>
    public enum RegistryCollection
    {
        /// <summary>
        /// Groups organized by a registry.
        /// </summary>
        Groups,

        /// <summary>
        /// Logical resources organized by a group.
        /// </summary>
        Resources,

        /// <summary>
        /// Concrete versions of one logical resource.
        /// </summary>
        Versions,

        /// <summary>
        /// Labels of the selected entity.
        /// </summary>
        Labels
    }

    /// <summary>
    /// Distinguishes roles that share the same generated ResourceType.
    /// </summary>
    internal enum RegistryRole
    {
        /// <summary>
        /// The registry root.
        /// </summary>
        Registry,

        /// <summary>
        /// A group of logical resources.
        /// </summary>
        Group,

        /// <summary>
        /// A logical resource with a distinct Versions folder.
        /// </summary>
        Resource,

        /// <summary>
        /// A concrete version organized by a typed Versions folder.
        /// </summary>
        Version,

        /// <summary>
        /// An older flat resource whose deletion scope is ambiguous.
        /// </summary>
        LegacyResource
    }

    /// <summary>
    /// Model-qualified access and role checks for generic registry tools.
    /// </summary>
    internal static class RegistryAccess
    {
        /// <summary>
        /// Resolves the current session and requires the base registry model.
        /// </summary>
        internal static ISession Session(OpcUaSessionManager manager, string? name)
        {
            ISession session = manager.GetSessionOrThrow(name);
            _ = McpModelAccess.ModelNode(session, RegistryTypes.RegistryType);
            IEncodeableFactory factory = session.MessageContext.Factory;
            if (!factory.ContainsEncodeableType(XRegistry.DataTypeIds.RegistryCapabilitiesDataType))
            {
                factory.Builder.AddOpcUaXRegistry().Commit();
            }
            return session;
        }

        /// <summary>
        /// Keeps mutations on encrypted sessions and requires an explicit opt-in to unconditional operations.
        /// </summary>
        internal static void Mutation(ISession session, uint? expectedEpoch = null, bool force = false)
        {
            McpModelAccess.RequireSecureMutation(session, expectedEpoch, force);
        }

        /// <summary>
        /// Resolves an inherited xRegistry child using the declaration namespace.
        /// </summary>
        internal static ValueTask<NodeId> ChildAsync(ISession session, NodeId parent, string name, CancellationToken ct)
        {
            return McpModelAccess.ChildAsync(session, parent,
                McpModelAccess.Name(session, XRegistryWellKnown.XRegistryNamespaceUri, name), ct);
        }

        /// <summary>
        /// Determines the real entity role from its type and parent hierarchy, not its name or caller assertion.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<RegistryRole> RoleAsync(ISession session, NodeId node, CancellationToken ct)
        {
            NodeId actual = await McpModelAccess.TypeDefinitionAsync(session, node, ct).ConfigureAwait(false);
            if (await IsTypeAsync(session, actual, RegistryTypes.RegistryType, ct).ConfigureAwait(false))
            {
                return RegistryRole.Registry;
            }
            if (await IsTypeAsync(session, actual, RegistryTypes.GroupType, ct).ConfigureAwait(false))
            {
                return RegistryRole.Group;
            }
            if (!await IsTypeAsync(session, actual, RegistryTypes.ResourceType, ct).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The node is not a registry entity.");
            }
            NodeId parent = default;
            await foreach (ReferenceDescription reference in CollectionReferencesAsync(
                session, node, BrowseDirection.Inverse, ct)
                .ConfigureAwait(false))
            {
                if (!parent.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                        "The resource has ambiguous organizing parents.");
                }
                parent = McpModelAccess.ModelNode(session, reference.NodeId);
            }
            if (parent.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                    "The resource's logical or version role cannot be established.");
            }
            NodeId parentType = await McpModelAccess.TypeDefinitionAsync(session, parent, ct).ConfigureAwait(false);
            if (await IsTypeAsync(session, parentType, RegistryTypes.ResourceVersionsType, ct).ConfigureAwait(false))
            {
                NodeId owner = default;
                await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                    parent, ReferenceTypeIds.HasComponent, BrowseDirection.Inverse,
                    nodeClassMask: (uint)NodeClass.Object, ct: ct).ConfigureAwait(false))
                {
                    if (!owner.IsNull)
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                            "The Versions folder has ambiguous owners.");
                    }
                    owner = McpModelAccess.ModelNode(session, reference.NodeId);
                }
                if (owner.IsNull || owner == node)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                        "The Versions folder has no distinct logical resource owner.");
                }
                await McpModelAccess.RequireTypeAsync(session, owner, RegistryTypes.ResourceType, ct)
                    .ConfigureAwait(false);
                NodeId ownedVersions = await ChildAsync(session, owner, "Versions", ct).ConfigureAwait(false);
                if (ownedVersions != parent)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                        "The Versions folder is not the owner's declared version collection.");
                }
                return RegistryRole.Version;
            }
            if (!await IsTypeAsync(session, parentType, RegistryTypes.GroupType, ct).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                    "The resource is not organized by a group or a typed Versions folder.");
            }
            NodeId versions = await ChildAsync(session, node, "Versions", ct).ConfigureAwait(false);
            if (versions.IsNull)
            {
                return RegistryRole.LegacyResource;
            }
            await McpModelAccess.RequireTypeAsync(session, versions, RegistryTypes.ResourceVersionsType, ct)
                .ConfigureAwait(false);
            return RegistryRole.Resource;
        }

        /// <summary>
        /// Rejects ambiguous legacy deletions rather than removing a resource when one version was requested.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask RequireRoleAsync(
            ISession session, NodeId node, RegistryRole expected, CancellationToken ct)
        {
            RegistryRole actual = await RoleAsync(session, node, ct).ConfigureAwait(false);
            if (actual != expected)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    $"Expected {expected}, but the node is {actual}; select an unambiguous entity.");
            }
        }

        /// <summary>
        /// Reads stable structural metadata separately from the selected version view.
        /// </summary>
        internal static async ValueTask<JsonObject> ReadAsync(ISession session, NodeId node, CancellationToken ct)
        {
            RegistryRole role = await RoleAsync(session, node, ct).ConfigureAwait(false);
            ArrayOf<string> names = role switch
            {
                RegistryRole.Registry =>
                    ["RegistryId", "SpecVersion", "Xid", "Name", "Description", "Epoch",
                        "CreatedAt", "ModifiedAt", "CapabilitiesInfo"],
                RegistryRole.Group =>
                    ["GroupId", "Xid", "Name", "Description", "Epoch", "CreatedAt", "ModifiedAt"],
                _ =>
                    ["ResourceId", "VersionId", "Xid", "Name", "Description", "Epoch", "MetaEpoch",
                        "CreatedAt", "ModifiedAt", "MetaCreatedAt", "MetaModifiedAt", "Format", "ContentType",
                        "Documentation", "ExternalReference", "ResourceUrl"]
            };
            JsonObject result = await McpModelAccess.PropertiesAsync(session, node,
                names.ConvertAll(name => McpModelAccess.Name(
                    session, XRegistryWellKnown.XRegistryNamespaceUri, name)), ct).ConfigureAwait(false);
            result["role"] = role.ToString();
            result["typeDefinitionId"] = (await McpModelAccess.TypeDefinitionAsync(session, node, ct)
                .ConfigureAwait(false)).ToString();
            NodeId labels = await ChildAsync(session, node,
                role == RegistryRole.Resource ? "MetaLabels" : "Labels", ct).ConfigureAwait(false);
            result["labelsNodeId"] = labels.IsNull ? null : labels.ToString();
            return result;
        }

        /// <summary>
        /// Selects the correct parent and streams one registry collection, including typed label values.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        internal static async IAsyncEnumerable<JsonObject> ListAsync(
            ISession session, NodeId node, RegistryCollection collection, [EnumeratorCancellation] CancellationToken ct)
        {
            NodeId parent = node;
            ExpandedNodeId expectedType;
            switch (collection)
            {
                case RegistryCollection.Groups:
                    await RequireRoleAsync(session, node, RegistryRole.Registry, ct).ConfigureAwait(false);
                    expectedType = RegistryTypes.GroupType;
                    break;
                case RegistryCollection.Resources:
                    await RequireRoleAsync(session, node, RegistryRole.Group, ct).ConfigureAwait(false);
                    expectedType = RegistryTypes.ResourceType;
                    break;
                case RegistryCollection.Versions:
                    await RequireRoleAsync(session, node, RegistryRole.Resource, ct).ConfigureAwait(false);
                    parent = await ChildAsync(session, node, "Versions", ct).ConfigureAwait(false);
                    expectedType = RegistryTypes.ResourceType;
                    break;
                case RegistryCollection.Labels:
                    RegistryRole role = await RoleAsync(session, node, ct).ConfigureAwait(false);
                    parent = await ChildAsync(session, node,
                        role == RegistryRole.Resource ? "MetaLabels" : "Labels", ct).ConfigureAwait(false);
                    expectedType = default;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(collection));
            }
            if (parent.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The requested collection is absent.");
            }
            int scanned = 0;
            IAsyncEnumerable<ReferenceDescription> references = collection == RegistryCollection.Labels
                ? session.EnumerateReferencesAsync(parent, ReferenceTypeIds.HasProperty,
                    nodeClassMask: (uint)NodeClass.Variable, ct: ct)
                : CollectionReferencesAsync(session, parent, BrowseDirection.Forward, ct);
            await foreach (ReferenceDescription reference in references.ConfigureAwait(false))
            {
                if (++scanned > 10_000)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The registry collection exceeds the 10000-reference scan budget.");
                }
                if (!expectedType.IsNull)
                {
                    NodeId actual = McpModelAccess.ModelNode(session, reference.TypeDefinition);
                    if (!await IsTypeAsync(session, actual, expectedType, ct).ConfigureAwait(false))
                    {
                        continue;
                    }
                }
                JsonObject entry = McpModelAccess.Reference(session, reference);
                if (collection == RegistryCollection.Labels)
                {
                    NodeId child = McpModelAccess.ModelNode(session, reference.NodeId);
                    DataValue value = await McpModelAccess.ReadDataValueAsync(session, child, ct).ConfigureAwait(false);
                    entry["dataValue"] = McpCompanionJson.DataValue(value, session.MessageContext);
                    entry["error"] = StatusCode.IsBad(value.StatusCode);
                }
                yield return entry;
            }
        }

        /// <summary>
        /// Accepts collection components and organizing links, not the parallel notifier chain.
        /// </summary>
        private static async IAsyncEnumerable<ReferenceDescription> CollectionReferencesAsync(
            ISession session, NodeId node, BrowseDirection direction, [EnumeratorCancellation] CancellationToken ct)
        {
            var visited = new HashSet<ExpandedNodeId>();
            int scanned = 0;
            await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                node, ReferenceTypeIds.HierarchicalReferences, direction,
                nodeClassMask: (uint)NodeClass.Object, ct: ct).ConfigureAwait(false))
            {
                if (++scanned > 10_000)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The registry hierarchy exceeds the 10000-reference scan budget.");
                }
                NodeId type = reference.ReferenceTypeId;
                if (type != ReferenceTypeIds.Organizes && type != ReferenceTypeIds.HasComponent &&
                    !await session.NodeCache.IsTypeOfAsync(type, ReferenceTypeIds.Organizes, ct)
                        .ConfigureAwait(false) &&
                    !await session.NodeCache.IsTypeOfAsync(type, ReferenceTypeIds.HasComponent, ct)
                        .ConfigureAwait(false))
                {
                    continue;
                }
                if (visited.Add(reference.NodeId))
                {
                    yield return reference;
                }
            }
        }

        /// <summary>
        /// Matches direct and derived types using the session's type cache.
        /// </summary>
        private static async ValueTask<bool> IsTypeAsync(
            ISession session, NodeId actual, ExpandedNodeId expected, CancellationToken ct)
        {
            NodeId target = McpModelAccess.ModelNode(session, expected);
            return actual == target || await session.NodeCache.IsTypeOfAsync(actual, target, ct).ConfigureAwait(false);
        }
    }
}
