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
using System.Xml.Linq;

namespace Opc.Ua.Export
{
    /// <summary>
    /// The aliases one NodeSet2 document declares for itself, read as an
    /// <see cref="INodeSetAliasResolver"/> that falls back to a policy when
    /// the document says nothing about a name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A document's own <c>&lt;Aliases&gt;</c> table is the first and final
    /// word on any name it declares: a name it binds to <c>i=12</c> means
    /// <c>i=12</c> in that document however the rest of the world reads it.
    /// A name it does not declare is left to the fallback, which is where a
    /// caller's policy - the standard names of a profile, say - gets its say,
    /// and where no fallback leaves the name exactly as written.
    /// </para>
    /// <para>
    /// This is the one place a declared table is turned into a lookup, so the
    /// order of the two sources is stated once rather than at each of the
    /// places that has to resolve a name.
    /// </para>
    /// </remarks>
    internal sealed class NodeSetDeclaredAliases : INodeSetAliasResolver
    {
        private NodeSetDeclaredAliases(
            Dictionary<string, string> declared,
            INodeSetAliasResolver? fallback)
        {
            m_declared = declared;
            m_fallback = fallback;
        }

        /// <summary>
        /// Reads the aliases a node set declares.
        /// </summary>
        /// <param name="nodeSet">The node set, or <c>null</c>.</param>
        /// <param name="fallback">
        /// The policy consulted for a name the node set does not declare, or
        /// <c>null</c> to resolve nothing else.
        /// </param>
        /// <returns>The resolver over that node set's table.</returns>
        public static NodeSetDeclaredAliases FromNodeSet(
            UANodeSet? nodeSet,
            INodeSetAliasResolver? fallback = null)
        {
            return FromDeclarations(nodeSet?.Aliases, fallback);
        }

        /// <summary>
        /// Reads a declared alias table.
        /// </summary>
        /// <remarks>
        /// A name declared more than once keeps the first declaration, which
        /// is how the importer reads a repeated name as well.
        /// </remarks>
        /// <param name="aliases">The declarations, or <c>null</c>.</param>
        /// <param name="fallback">
        /// The policy consulted for a name the declarations do not cover, or
        /// <c>null</c> to resolve nothing else.
        /// </param>
        /// <returns>The resolver over those declarations.</returns>
        public static NodeSetDeclaredAliases FromDeclarations(
            NodeIdAlias[]? aliases,
            INodeSetAliasResolver? fallback = null)
        {
            var declared = new Dictionary<string, string>(
                aliases?.Length ?? 0,
                StringComparer.Ordinal);
            foreach (NodeIdAlias alias in aliases ?? [])
            {
                Declare(declared, alias?.Alias, alias?.Value);
            }
            return new NodeSetDeclaredAliases(declared, fallback);
        }

        /// <summary>
        /// Reads the <c>&lt;Aliases&gt;</c> table of a serialized NodeSet2
        /// document.
        /// </summary>
        /// <param name="root">The document element.</param>
        /// <param name="fallback">
        /// The policy consulted for a name the document does not declare, or
        /// <c>null</c> to resolve nothing else.
        /// </param>
        /// <returns>The resolver over that document's table.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="root"/> is <c>null</c>.
        /// </exception>
        public static NodeSetDeclaredAliases FromDocument(
            XElement root,
            INodeSetAliasResolver? fallback = null)
        {
            if (root is null)
            {
                throw new ArgumentNullException(nameof(root));
            }
            var declared = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XElement table in root.Elements())
            {
                if (!string.Equals(table.Name.LocalName, AliasesElement, StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (XElement alias in table.Elements())
                {
                    Declare(declared, alias.Attribute(AliasAttribute)?.Value, alias.Value);
                }
            }
            return new NodeSetDeclaredAliases(declared, fallback);
        }

        /// <inheritdoc/>
        public bool TryResolve(string alias, out string nodeId)
        {
            if (alias is not null)
            {
                if (m_declared.TryGetValue(alias, out nodeId!))
                {
                    return true;
                }
                if (m_fallback is not null && m_fallback.TryResolve(alias, out nodeId!))
                {
                    return true;
                }
            }
            nodeId = string.Empty;
            return false;
        }

        private static void Declare(
            Dictionary<string, string> declared,
            string? alias,
            string? value)
        {
            // A declaration that binds a name to nothing states nothing, so it
            // is not a declaration this resolves through: the name stays the
            // document's own to declare properly.
            if (alias is { Length: > 0 } &&
                value is { Length: > 0 } &&
                !declared.ContainsKey(alias))
            {
                declared.Add(alias, value);
            }
        }

        private const string AliasesElement = "Aliases";
        private const string AliasAttribute = "Alias";

        private readonly Dictionary<string, string> m_declared;
        private readonly INodeSetAliasResolver? m_fallback;
    }
}
