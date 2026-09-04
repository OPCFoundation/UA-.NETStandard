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

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Indexes the instance declarations a set of Thing Models states, and
    /// answers <see cref="IWotTypeDeclarationResolver"/> over them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both document-backed parts of the WoT Binding Section 5.1.5 local
    /// context - a fixed set of sibling documents and the documents held in a
    /// registry snapshot - derive declarations the same way, from the
    /// affordances of a Thing Model and from the <c>tm:extends</c> links that
    /// say what it extends. Sharing one index is what keeps the two from
    /// drifting, and keeps the bounded, cycle-checked supertype walk written
    /// once.
    /// </para>
    /// <para>
    /// An instance of this type is mutated only while it is being built. It is
    /// safe to share for reading once building has finished.
    /// </para>
    /// </remarks>
    public sealed class WotDocumentDeclarationIndex
    {
        /// <summary>
        /// Indexes one document. A document that is not a Thing Model declares
        /// nothing and is ignored.
        /// </summary>
        /// <param name="document">The document to index.</param>
        /// <param name="aliases">
        /// Additional names a <c>tm:extends</c> href may use to name this
        /// document - a registry resource identifier, for instance. The
        /// document's own <c>id</c> and the identity of the type it projects
        /// are always indexed.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public void Add(WotDocument document, IEnumerable<string>? aliases = null)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            if (!WotNodeSetConverter.TryDescribeProjectedType(
                    document, out _, out _, out string typeNodeId) ||
                typeNodeId.Length == 0 ||
                !WotNodeSetConverter.TryDescribeTypeDeclarations(
                    document,
                    out ArrayOf<WotTypeDeclaration> declarations,
                    out ArrayOf<string> supertypes))
            {
                return;
            }

            // Two documents claiming one identity is a conflict the conversion
            // reports through the ordinary name resolution; the first one
            // indexed keeps the entry so the declaration view never depends on
            // enumeration order.
            if (!m_types.ContainsKey(typeNodeId))
            {
                // Section 6.8: a type document that states
                // uav:includeInherited: true has already listed the
                // declarations it inherits, so walking its supertypes would
                // ask for a second copy of what it already says. One that
                // states false, or says nothing, lists only its own.
                m_types[typeNodeId] = new Entry(
                    declarations,
                    WotNodeSetConverter.ReadIncludeInherited(document) == true
                        ? ArrayOf<string>.Empty
                        : supertypes);
            }
            AddAlias(typeNodeId, typeNodeId);
            AddAlias(document.Id, typeNodeId);
            if (aliases is not null)
            {
                foreach (string alias in aliases)
                {
                    AddAlias(alias, typeNodeId);
                }
            }
        }

        /// <summary>
        /// Reports the declarations of a type this index holds.
        /// </summary>
        /// <param name="typeNodeId">
        /// The type's identity, as a portable ExpandedNodeId string.
        /// </param>
        /// <param name="scope">Which declarations are wanted.</param>
        /// <returns>
        /// The declarations, or <c>null</c> when this index does not hold the
        /// type.
        /// </returns>
        public WotTypeDeclarationSet? Resolve(string typeNodeId, WotDeclarationScope scope)
        {
            if (string.IsNullOrEmpty(typeNodeId) ||
                !m_types.TryGetValue(typeNodeId, out Entry entry))
            {
                return null;
            }
            if (scope == WotDeclarationScope.Direct)
            {
                return new WotTypeDeclarationSet
                {
                    TypeNodeId = typeNodeId,
                    Declarations = entry.Declarations
                };
            }
            return BuildEffective(typeNodeId, entry);
        }

        /// <summary>
        /// Walks the supertype chain, letting a subtype's declaration hide a
        /// supertype's declaration of the same qualified name and kind.
        /// </summary>
        /// <remarks>
        /// The walk is bounded by
        /// <see cref="WotTypeDeclarations.MaxSupertypeDepth"/> and refuses to
        /// visit a type twice, so a hierarchy that loops - which a document set
        /// can state, because nothing stops two Thing Models extending each
        /// other - stops with an incomplete answer rather than running forever.
        /// A supertype the index does not hold also makes the answer
        /// incomplete: a member that matches nothing here may still match a
        /// declaration of the type that could not be read.
        /// </remarks>
        private WotTypeDeclarationSet BuildEffective(string typeNodeId, Entry entry)
        {
            var byName = new Dictionary<string, WotTypeDeclaration>(StringComparer.Ordinal);
            var supertypes = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { typeNodeId };
            string? detail = null;

            Merge(byName, entry.Declarations, inherited: false);

            var pending = new Queue<ArrayOf<string>>();
            pending.Enqueue(entry.Supertypes);
            while (pending.Count > 0)
            {
                ArrayOf<string> hrefs = pending.Dequeue();
                foreach (string href in hrefs)
                {
                    if (!m_aliases.TryGetValue(TrimFragment(href), out string? nextNodeId))
                    {
                        detail ??=
                            $"The supertype '{href}' is not held by this part of the " +
                            "local context, so its declarations are unknown.";
                        continue;
                    }
                    if (!visited.Add(nextNodeId))
                    {
                        detail ??=
                            $"The supertype chain revisits '{nextNodeId}', so it is a " +
                            "cycle rather than a hierarchy.";
                        continue;
                    }
                    if (supertypes.Count >= WotTypeDeclarations.MaxSupertypeDepth)
                    {
                        detail ??=
                            "The supertype chain exceeded the maximum of " +
                            $"{WotTypeDeclarations.MaxSupertypeDepth} types.";
                        return Build(typeNodeId, byName, supertypes, detail);
                    }
                    supertypes.Add(nextNodeId);
                    Entry next = m_types[nextNodeId];
                    Merge(byName, next.Declarations, inherited: true);
                    pending.Enqueue(next.Supertypes);
                }
            }
            return Build(typeNodeId, byName, supertypes, detail);
        }

        private static WotTypeDeclarationSet Build(
            string typeNodeId,
            Dictionary<string, WotTypeDeclaration> byName,
            List<string> supertypes,
            string? detail)
        {
            var ordered = new List<WotTypeDeclaration>(byName.Values);
            ordered.Sort(WotTypeDeclarations.Compare);
            return new WotTypeDeclarationSet
            {
                TypeNodeId = typeNodeId,
                Declarations = ordered.ToArrayOf(),
                Supertypes = supertypes.ToArrayOf(),
                IsComplete = detail is null,
                Detail = detail
            };
        }

        private static void Merge(
            Dictionary<string, WotTypeDeclaration> byName,
            ArrayOf<WotTypeDeclaration> declarations,
            bool inherited)
        {
            foreach (WotTypeDeclaration declaration in declarations)
            {
                string key = declaration.NamespaceUri + "\u0000" + declaration.BrowseName +
                    "\u0000" + ((int)declaration.Kind).ToString(
                        System.Globalization.CultureInfo.InvariantCulture);

                // The nearest declaration wins: a subtype that redeclares a
                // name states the version an instance has to populate, and the
                // supertype's is the one it replaced.
                if (byName.ContainsKey(key))
                {
                    continue;
                }
                byName[key] = inherited
                    ? declaration with { IsInherited = true }
                    : declaration;
            }
        }

        private void AddAlias(string? alias, string typeNodeId)
        {
            if (string.IsNullOrEmpty(alias))
            {
                return;
            }
            string key = TrimFragment(alias!);
            if (key.Length != 0 && !m_aliases.ContainsKey(key))
            {
                m_aliases[key] = typeNodeId;
            }
        }

        private static string TrimFragment(string href)
        {
            int hash = href.IndexOf('#', StringComparison.Ordinal);
            return hash < 0 ? href : href.Substring(0, hash);
        }

        private readonly record struct Entry(
            ArrayOf<WotTypeDeclaration> Declarations,
            ArrayOf<string> Supertypes);

        private readonly Dictionary<string, Entry> m_types = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> m_aliases = new(StringComparer.Ordinal);
    }
}
