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
 *
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

namespace Opc.Ua.Export
{
    /// <summary>
    /// Completes a NodeSet2 document's <c>&lt;Aliases&gt;</c> table with the
    /// names the document uses but does not declare, as far as a caller's
    /// alias policy knows them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NodeSet2 document may write a standard name such as
    /// <c>HasComponent</c> or <c>Double</c> wherever a NodeId is expected, but
    /// only if it declares that name in its own <c>&lt;Aliases&gt;</c> table.
    /// The importer resolves an attribute through that table and reports
    /// <c>BadNodeIdInvalid</c> for a name it does not find, so a document that
    /// uses a name it never declares cannot be loaded at all.
    /// </para>
    /// <para>
    /// A producer that writes readable names - a converter synthesizing a
    /// NodeSet, or one restoring a document whose spelling has to stay exactly
    /// as it was - therefore has to declare them. Rewriting the names to
    /// identifiers instead would break a byte-exact restore, so the names are
    /// kept and the missing declarations are added.
    /// </para>
    /// <para>
    /// Which names those are is the producer's policy rather than this pass's,
    /// so it is stated by the <see cref="INodeSetAliasResolver"/> the caller
    /// hands in - the same abstraction a comparison reads a document through,
    /// so that completing a document and comparing it apply one policy. A name
    /// the resolver does not know is left exactly as it was, so an undeclared
    /// vendor alias in a source document still fails the import with the
    /// message that names it, rather than being quietly discarded. Completing
    /// a table is a producer's decision about a document it writes; it never
    /// states what an existing document already meant.
    /// </para>
    /// </remarks>
    internal static class NodeSetAliasCompleter
    {
        /// <summary>
        /// Declares every name a node set uses but does not yet declare and
        /// the resolver knows, and returns the same instance.
        /// </summary>
        /// <remarks>
        /// The pass is idempotent and adds nothing to a node set that already
        /// declares what it uses, which is what keeps a byte-exact restore
        /// byte-exact. New declarations are appended after the ones the
        /// document brought, in ascending ordinal order of the alias, so the
        /// result depends only on the content and the policy and never on
        /// enumeration order.
        /// </remarks>
        /// <param name="nodeSet">The node set to complete, or <c>null</c>.</param>
        /// <param name="resolver">The policy that says what a name stands for.</param>
        /// <returns><paramref name="nodeSet"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="resolver"/> is <c>null</c>.
        /// </exception>
        public static UANodeSet? Complete(UANodeSet? nodeSet, INodeSetAliasResolver resolver)
        {
            if (resolver is null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }
            if (nodeSet?.Items is not { Length: > 0 } items)
            {
                return nodeSet;
            }

            var declared = new HashSet<string>(StringComparer.Ordinal);
            if (nodeSet.Aliases is { Length: > 0 } aliases)
            {
                foreach (NodeIdAlias alias in aliases)
                {
                    if (alias?.Alias is { Length: > 0 } name)
                    {
                        declared.Add(name);
                    }
                }
            }

            var missing = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (UANode node in items)
            {
                CollectFromNode(node, declared, missing, resolver);
            }

            if (missing.Count == 0)
            {
                return nodeSet;
            }

            int existing = nodeSet.Aliases?.Length ?? 0;
            var completed = new NodeIdAlias[existing + missing.Count];
            if (existing > 0)
            {
                Array.Copy(nodeSet.Aliases!, completed, existing);
            }
            int index = existing;
            foreach (KeyValuePair<string, string> entry in missing)
            {
                completed[index++] = new NodeIdAlias
                {
                    Alias = entry.Key,
                    Value = entry.Value
                };
            }
            nodeSet.Aliases = completed;
            return nodeSet;
        }

        private static void CollectFromNode(
            UANode? node,
            HashSet<string> declared,
            SortedDictionary<string, string> missing,
            INodeSetAliasResolver resolver)
        {
            if (node is null)
            {
                return;
            }

            if (node.References is { Length: > 0 } references)
            {
                foreach (Reference reference in references)
                {
                    if (reference is null)
                    {
                        continue;
                    }
                    Collect(reference.ReferenceType, declared, missing, resolver);
                    Collect(reference.Value, declared, missing, resolver);
                }
            }

            if (node.RolePermissions is { Length: > 0 } permissions)
            {
                foreach (RolePermission permission in permissions)
                {
                    Collect(permission?.Value, declared, missing, resolver);
                }
            }

            if (node is UAInstance instance)
            {
                Collect(instance.ParentNodeId, declared, missing, resolver);
            }

            switch (node)
            {
                case UAVariable variable:
                    Collect(variable.DataType, declared, missing, resolver);
                    break;
                case UAMethod method:
                    Collect(method.MethodDeclarationId, declared, missing, resolver);
                    break;
                case UAVariableType variableType:
                    Collect(variableType.DataType, declared, missing, resolver);
                    break;
                case UADataType dataType:
                    CollectFromDefinition(dataType.Definition, declared, missing, resolver);
                    break;
            }
        }

        private static void CollectFromDefinition(
            DataTypeDefinition? definition,
            HashSet<string> declared,
            SortedDictionary<string, string> missing,
            INodeSetAliasResolver resolver)
        {
            if (definition is null)
            {
                return;
            }
            Collect(definition.BaseType, declared, missing, resolver);
            if (definition.Field is not { Length: > 0 } fields)
            {
                return;
            }
            foreach (DataTypeField field in fields)
            {
                Collect(field?.DataType, declared, missing, resolver);
            }
        }

        /// <summary>
        /// Records one name that has to be declared, when it is a name at all
        /// and the caller's policy knows what it stands for.
        /// </summary>
        private static void Collect(
            string? value,
            HashSet<string> declared,
            SortedDictionary<string, string> missing,
            INodeSetAliasResolver resolver)
        {
            if (string.IsNullOrEmpty(value) ||
                declared.Contains(value!) ||
                missing.ContainsKey(value!) ||
                IsIdentifier(value!) ||
                !resolver.TryResolve(value!, out string nodeId))
            {
                return;
            }
            missing.Add(value!, nodeId);
        }

        /// <summary>
        /// Gets whether a value is already an identifier rather than a name.
        /// </summary>
        /// <remarks>
        /// The check is by shape rather than by <c>NodeId.Parse</c>: parsing
        /// throws for every name, and a NodeSet of any size carries thousands
        /// of these values. Every identifier form NodeSet2 admits begins with
        /// a two-character type prefix or a namespace prefix, and no
        /// BrowseName can, so the two are told apart without allocating.
        /// </remarks>
        private static bool IsIdentifier(string value)
        {
            return StartsWith(value, "i=") ||
                StartsWith(value, "s=") ||
                StartsWith(value, "g=") ||
                StartsWith(value, "b=") ||
                StartsWith(value, "ns=") ||
                StartsWith(value, "nsu=") ||
                StartsWith(value, "svr=");
        }

        private static bool StartsWith(string value, string prefix)
        {
            return value.StartsWith(prefix, StringComparison.Ordinal);
        }
    }
}
