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
    /// The instance declarations one conversion resolved for the type its
    /// document binds to, pre-resolved before the synchronous synthesis needs
    /// them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolving declarations is asynchronous - an AddressSpace-backed local
    /// context browses and reads to answer - and the synthesis is not. The
    /// catalog is therefore built once, in the asynchronous entry point,
    /// immediately after the type binding is settled, and handed to the
    /// synthesis as immutable data. The synthesis never resolves, never blocks
    /// and never runs a synchronous wait over an asynchronous call.
    /// </para>
    /// <para>
    /// A catalog also records <em>why</em> it holds nothing, because a rule
    /// that depends on declarations has to fail explicitly where it cannot be
    /// evaluated rather than pass because no declaration contradicted it.
    /// </para>
    /// </remarks>
    internal sealed class WotDeclarationCatalog
    {
        /// <summary>
        /// The catalog handed to a conversion whose document binds to no type.
        /// A rule that depends on declarations has nothing to apply to, which
        /// is different from being unable to evaluate one.
        /// </summary>
        public static WotDeclarationCatalog NotBound { get; } =
            new WotDeclarationCatalog(null, null, WotDeclarationScope.Effective, false, null);

        private WotDeclarationCatalog(
            string? typeNodeId,
            WotTypeDeclarationSet? set,
            WotDeclarationScope scope,
            bool capabilityOffered,
            string? detail)
        {
            TypeNodeId = typeNodeId;
            Scope = scope;
            CapabilityOffered = capabilityOffered;
            Detail = detail;
            if (set is null)
            {
                return;
            }
            m_byName = new Dictionary<string, List<WotTypeDeclaration>>(StringComparer.Ordinal);
            foreach (WotTypeDeclaration declaration in set.Declarations)
            {
                string key = Key(declaration.NamespaceUri, declaration.BrowseName);
                if (!m_byName.TryGetValue(key, out List<WotTypeDeclaration>? bucket))
                {
                    bucket = [];
                    m_byName[key] = bucket;
                }
                bucket.Add(declaration);
            }
            IsComplete = set.IsComplete;
            Detail ??= set.Detail;
        }

        /// <summary>
        /// Gets the bound type's identity, or <c>null</c> when the document
        /// binds to no type.
        /// </summary>
        public string? TypeNodeId { get; }

        /// <summary>
        /// Gets the scope the declarations were resolved with, which is what
        /// <c>uav:includeInherited</c> selected.
        /// </summary>
        public WotDeclarationScope Scope { get; }

        /// <summary>
        /// Gets whether any part of the local context offers the declaration
        /// capability at all.
        /// </summary>
        public bool CapabilityOffered { get; }

        /// <summary>
        /// Gets whether the resolved closure is the whole closure.
        /// </summary>
        public bool IsComplete { get; }

        /// <summary>
        /// Gets why the catalog is unusable or incomplete, or <c>null</c>.
        /// </summary>
        public string? Detail { get; }

        /// <summary>
        /// Gets whether declarations were resolved and can be matched against.
        /// </summary>
        public bool HasDeclarations => m_byName is not null;

        /// <summary>
        /// Builds a catalog for a document bound to <paramref name="typeNodeId"/>.
        /// </summary>
        /// <param name="typeNodeId">The bound type's identity.</param>
        /// <param name="scope">The scope <c>uav:includeInherited</c> selected.</param>
        /// <param name="set">The resolved declarations, or <c>null</c>.</param>
        /// <param name="capabilityOffered">
        /// Whether any part of the local context offers the capability.
        /// </param>
        /// <returns>The catalog.</returns>
        public static WotDeclarationCatalog Create(
            string typeNodeId,
            WotDeclarationScope scope,
            WotTypeDeclarationSet? set,
            bool capabilityOffered)
        {
            string? detail = null;
            if (!capabilityOffered)
            {
                detail =
                    "No part of the local context reports instance declarations, so a " +
                    "rule that depends on them cannot be evaluated (WoT Binding " +
                    "Section 5.1.5).";
            }
            else if (set is null)
            {
                detail =
                    $"The local context resolved the type '{typeNodeId}' but reports no " +
                    "instance declarations for it.";
            }
            return new WotDeclarationCatalog(
                typeNodeId, set, scope, capabilityOffered, detail);
        }

        /// <summary>
        /// Finds the declarations a qualified BrowseName matches.
        /// </summary>
        /// <param name="namespaceUri">The member's BrowseName namespace.</param>
        /// <param name="browseName">The member's unqualified BrowseName.</param>
        /// <returns>
        /// Every declaration of that exact name, which is empty when the type
        /// declares none.
        /// </returns>
        public IReadOnlyList<WotTypeDeclaration> Match(string namespaceUri, string browseName)
        {
            if (m_byName is null ||
                !m_byName.TryGetValue(
                    Key(namespaceUri, browseName), out List<WotTypeDeclaration>? bucket))
            {
                return [];
            }
            return bucket;
        }

        private static string Key(string namespaceUri, string browseName)
        {
            return namespaceUri + "\u0000" + browseName;
        }

        private readonly Dictionary<string, List<WotTypeDeclaration>>? m_byName;
    }
}
