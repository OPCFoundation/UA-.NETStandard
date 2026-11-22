// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Builds the set of simple names of the standard <c>Opc.Ua.*State</c>
    /// classes (node-state and method-state) available in a compilation,
    /// including referenced assemblies such as <c>Opc.Ua.Core</c>. The model
    /// generator consults this set (together with the classes declared in the
    /// current pass) to decide whether a reference to a standard typed
    /// <c>MethodState</c> can be emitted, or must degrade to the base
    /// <c>MethodState</c> because a curated Core omits the typed class.
    /// </summary>
    internal static class OpcUaStateTypeIndex
    {
        /// <summary>
        /// Enumerate the immediate type members of the <c>Opc.Ua</c> namespace
        /// whose name ends with <c>State</c>.
        /// </summary>
        public static ImmutableHashSet<string> Build(Compilation compilation)
        {
            ImmutableHashSet<string>.Builder builder =
                ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            if (compilation == null)
            {
                return builder.ToImmutable();
            }
            INamespaceSymbol opcUa = ResolveNamespace(compilation.GlobalNamespace, "Opc", "Ua");
            if (opcUa != null)
            {
                foreach (INamedTypeSymbol type in opcUa.GetTypeMembers())
                {
                    if (type.Name.EndsWith("State", StringComparison.Ordinal))
                    {
                        builder.Add(type.Name);
                    }
                }
            }
            return builder.ToImmutable();
        }

        private static INamespaceSymbol ResolveNamespace(
            INamespaceSymbol root, params string[] path)
        {
            INamespaceSymbol current = root;
            foreach (string segment in path)
            {
                if (current == null)
                {
                    return null;
                }
                INamespaceSymbol next = null;
                foreach (INamespaceSymbol child in current.GetNamespaceMembers())
                {
                    if (string.Equals(child.Name, segment, StringComparison.Ordinal))
                    {
                        next = child;
                        break;
                    }
                }
                current = next;
            }
            return current;
        }
    }
}
