/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
