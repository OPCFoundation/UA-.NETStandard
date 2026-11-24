// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Dependency
{
    /// <summary>
    /// Selects referenced payloads required by local designs before decoding and materialization.
    /// </summary>
    internal sealed class ModelDependencyResolver
    {
        /// <summary>
        /// Creates a resolver over the selected referenced model declarations.
        /// </summary>
        internal ModelDependencyResolver(IReadOnlyDictionary<string, ModelDependencyReference> references)
        {
            m_references = references;
        }

        /// <summary>
        /// Resolves the namespace closure, leaving explicitly supplied designs and built-in types authoritative.
        /// </summary>
        internal IEnumerable<ModelDependencyV1> Resolve(
            IReadOnlyList<Namespace> namespaces,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_references.Count == 0)
            {
                yield break;
            }
            var visited = new HashSet<string>(StringComparer.Ordinal)
            {
                ModelDependencyV1.OpcUaNamespaceUri
            };
            foreach (Namespace ns in namespaces)
            {
                if (ns.FilePath != null)
                {
                    visited.Add(ns.Value);
                }
            }
            var pending = new Queue<string>();
            foreach (Namespace ns in namespaces)
            {
                Enqueue(ns.Value);
            }
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string uri = pending.Dequeue();
                if (!m_references.TryGetValue(uri, out ModelDependencyReference reference))
                {
                    continue;
                }
                ModelDependencyV1? dependency = reference.GetDependencyForGeneration(cancellationToken);
                if (dependency == null ||
                    !string.Equals(uri, dependency.ModelUri, StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (DependencyNode node in dependency.Nodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Enqueue(node.SymbolicNamespace);
                    Enqueue(node.BaseTypeNamespace);
                    Enqueue(node.DataTypeNamespace);
                    EnqueueSecurity(node.Security);
                    foreach (DependencyDataField field in node.Fields)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Enqueue(field.DataTypeNamespace);
                    }
                    var children = new Stack<DependencyChild>(node.Children);
                    while (children.Count > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        DependencyChild child = children.Pop();
                        Enqueue(child.BrowseNameNamespace);
                        Enqueue(child.TypeDefinitionNamespace);
                        Enqueue(child.DataTypeNamespace);
                        Enqueue(child.ReferenceTypeNamespace);
                        Enqueue(child.MethodStateNamespace);
                        Enqueue(child.MethodDeclarationNamespace);
                        EnqueueSecurity(child.Security);
                        EnqueueArguments(child.InputArguments);
                        EnqueueArguments(child.OutputArguments);
                        if (child.DefaultValueNamespaceUris != null)
                        {
                            foreach (string valueUri in child.DefaultValueNamespaceUris)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                Enqueue(valueUri);
                            }
                        }
                        else if (!string.IsNullOrEmpty(child.DefaultValueXml))
                        {
                            // Legacy values carry namespace indexes without their producer's table.
                            // Keep the old dependency set when their resolution cannot be narrowed safely.
                            foreach (string referenceUri in m_references.Keys)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                Enqueue(referenceUri);
                            }
                        }
                        foreach (DependencyChild nested in child.Children)
                        {
                            children.Push(nested);
                        }
                    }
                }
                yield return dependency;
            }

            void Enqueue(string? uri)
            {
                if (!string.IsNullOrEmpty(uri) && visited.Add(uri!))
                {
                    pending.Enqueue(uri!);
                }
            }

            void EnqueueArguments(IReadOnlyList<DependencyMethodArg> arguments)
            {
                foreach (DependencyMethodArg argument in arguments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Enqueue(argument.DataTypeNamespace);
                }
            }

            void EnqueueSecurity(DependencySecurity? security)
            {
                EnqueuePermissions(security?.RolePermissions);
                EnqueuePermissions(security?.DefaultRolePermissions);
            }

            void EnqueuePermissions(RolePermissionSet? permissions)
            {
                if (permissions?.RolePermission == null)
                {
                    return;
                }
                foreach (RolePermission permission in permissions.RolePermission)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Enqueue(permission.Role?.Namespace);
                    if (ExpandedNodeId.TryParse(permission.RoleId, out ExpandedNodeId roleId))
                    {
                        Enqueue(roleId.NamespaceUri);
                    }
                }
            }
        }

        private readonly IReadOnlyDictionary<string, ModelDependencyReference> m_references;
    }
}
