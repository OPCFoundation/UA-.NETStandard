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
using System.Globalization;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The names a NodeSet gives its ReferenceTypes, so the readable direction
    /// can write a WoT Binding Section 6.2 typed link for <em>any</em>
    /// ReferenceType rather than only the handful the library hard-codes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A link states a relation as a compact model name in <c>rel</c> and the
    /// definitive ExpandedNodeId in <c>uav:refId</c>. Producing the first for a
    /// companion model's own ReferenceType needs the ReferenceType's BrowseName
    /// and - to state the inverse direction at all - its InverseName. Three
    /// sources supply them, in this order of specificity:
    /// </para>
    /// <list type="number">
    /// <item>
    /// the <c>UAReferenceType</c> declarations of the NodeSet itself, which is
    /// where a companion model defines its own relations and the only place an
    /// InverseName and the Symmetric flag are stated;
    /// </item>
    /// <item>
    /// the NodeSet's <c>&lt;Aliases&gt;</c> table, which names the
    /// ReferenceTypes a NodeSet <em>uses</em> without defining them, and whose
    /// alias is by construction the ReferenceType's BrowseName;
    /// </item>
    /// <item>
    /// the standard base-namespace table, which covers namespace 0.
    /// </item>
    /// </list>
    /// <para>
    /// A ReferenceType none of the three names cannot be written as a compact
    /// model name at all. It is reported rather than emitted under some other
    /// relation, because a link whose <c>rel</c> named the wrong ReferenceType
    /// would read as a fact the source never stated.
    /// </para>
    /// </remarks>
    internal sealed class WotReferenceTypeNames
    {
        /// <summary>
        /// One ReferenceType and the two names it answers to.
        /// </summary>
        /// <param name="NodeId">The portable ExpandedNodeId.</param>
        /// <param name="Prefix">
        /// The <c>@context</c> prefix that binds the ReferenceType's namespace:
        /// <c>ua</c> for namespace 0 and <c>ns&lt;index&gt;</c> otherwise,
        /// matching the bindings the document's <c>@context</c> is written
        /// with.
        /// </param>
        /// <param name="BrowseName">The BrowseName, which reads forward.</param>
        /// <param name="InverseName">
        /// The InverseName, which reads backwards, or an empty string when the
        /// source states none.
        /// </param>
        /// <param name="IsSymmetric">
        /// Whether the ReferenceType is symmetric, in which case both
        /// directions are the same relation and the BrowseName names both.
        /// </param>
        public readonly record struct Entry(
            string NodeId,
            string Prefix,
            string BrowseName,
            string InverseName,
            bool IsSymmetric);

        /// <summary>
        /// Indexes the ReferenceTypes a NodeSet declares, aliases or inherits
        /// from the base namespace.
        /// </summary>
        /// <param name="nodeSet">The NodeSet being converted.</param>
        /// <returns>The index.</returns>
        public static WotReferenceTypeNames Build(UANodeSet nodeSet)
        {
            var names = new WotReferenceTypeNames();
            string[]? namespaceUris = nodeSet.NamespaceUris;

            foreach (UANode node in nodeSet.Items ?? [])
            {
                if (node is not UAReferenceType declared || declared.NodeId is null)
                {
                    continue;
                }
                string? browseName = WotNodeSetConverter.LocalName(declared.BrowseName);
                if (string.IsNullOrEmpty(browseName))
                {
                    continue;
                }
                string? portable = WotNodeSetConverter.ToPortableNodeId(
                    declared.NodeId, namespaceUris);
                if (string.IsNullOrEmpty(portable))
                {
                    continue;
                }
                var entry = new Entry(
                    portable!,
                    PrefixOf(declared.NodeId, namespaceUris),
                    browseName!,
                    FirstText(declared.InverseName),
                    declared.Symmetric);
                names.m_entries[declared.NodeId] = entry;
                names.m_entries[entry.NodeId] = entry;

                // A NodeSet states a reference by alias far more often than by
                // identifier, and an alias is spelled exactly like the
                // BrowseName, so the declaration answers to both.
                if (!names.m_entries.ContainsKey(browseName!))
                {
                    names.m_entries[browseName!] = entry;
                }
            }

            foreach (NodeIdAlias alias in nodeSet.Aliases ?? [])
            {
                if (alias?.Alias is null ||
                    alias.Value is null ||
                    names.m_entries.ContainsKey(alias.Alias))
                {
                    continue;
                }
                if (names.m_entries.TryGetValue(alias.Value, out Entry declared))
                {
                    names.m_entries[alias.Alias] = declared;
                    continue;
                }
                if (!TryStandard(alias.Value, out Entry standard) &&
                    !TryStandard(alias.Alias, out standard))
                {
                    // An alias of a ReferenceType neither declared here nor
                    // standard: the alias itself is the BrowseName, and no
                    // InverseName is knowable, so only the forward direction
                    // can be written.
                    string? portable = WotNodeSetConverter.ToPortableNodeId(
                        alias.Value, namespaceUris);
                    if (string.IsNullOrEmpty(portable))
                    {
                        continue;
                    }
                    standard = new Entry(
                        portable!,
                        PrefixOf(alias.Value, namespaceUris),
                        alias.Alias,
                        string.Empty,
                        false);
                }
                names.m_entries[alias.Alias] = standard;
            }

            return names;
        }

        /// <summary>
        /// Gets the compact model name and definitive identifier a typed link
        /// states for one reference of a NodeSet.
        /// </summary>
        /// <param name="referenceType">
        /// The reference's ReferenceType, as the NodeSet spells it - an alias
        /// or a NodeId.
        /// </param>
        /// <param name="isForward">The direction the reference runs.</param>
        /// <param name="modelName">The compact model name for <c>rel</c>.</param>
        /// <param name="refId">
        /// The portable ExpandedNodeId for <c>uav:refId</c>.
        /// </param>
        /// <returns>
        /// <c>false</c> when the ReferenceType has no name here, or when the
        /// reference runs inverse and the ReferenceType states no InverseName -
        /// in both cases the relation cannot be written without inventing one.
        /// </returns>
        public bool TryGetRelation(
            string? referenceType,
            bool isForward,
            out string modelName,
            out string refId)
        {
            modelName = string.Empty;
            refId = string.Empty;
            if (referenceType is null)
            {
                return false;
            }
            if (!m_entries.TryGetValue(referenceType, out Entry entry) &&
                !TryStandard(referenceType, out entry))
            {
                return false;
            }

            // OPC 10000-3: a symmetric ReferenceType has one name for both
            // directions, so its inverse reads under the BrowseName too.
            string name = isForward || entry.IsSymmetric
                ? entry.BrowseName
                : entry.InverseName;
            if (name.Length == 0)
            {
                return false;
            }
            modelName = entry.Prefix + ":" + name;
            refId = entry.NodeId;
            return true;
        }

        /// <summary>
        /// Gets the portable identifier of a ReferenceType a NodeSet names.
        /// </summary>
        /// <param name="referenceType">The alias or NodeId.</param>
        /// <param name="refId">The portable ExpandedNodeId.</param>
        /// <returns><c>true</c> when the ReferenceType is known here.</returns>
        public bool TryGetIdentifier(string? referenceType, out string refId)
        {
            if (referenceType is not null &&
                (m_entries.TryGetValue(referenceType, out Entry entry) ||
                    TryStandard(referenceType, out entry)))
            {
                refId = entry.NodeId;
                return true;
            }
            refId = string.Empty;
            return false;
        }

        private static bool TryStandard(string referenceType, out Entry entry)
        {
            if (WotVocabulary.TryGetReferenceTypeBrowseName(
                    referenceType, out string browseName) &&
                WotVocabulary.TryGetReferenceTypeNodeId(browseName, out string nodeId))
            {
                WotVocabulary.TryGetReferenceTypeInverseName(
                    nodeId, out string inverseName);
                entry = new Entry(nodeId, "ua", browseName, inverseName, false);
                return true;
            }
            entry = default;
            return false;
        }

        /// <summary>
        /// Reads the first text of a NodeSet LocalizedText array, which is the
        /// value in the document's own locale.
        /// </summary>
        private static string FirstText(Export.LocalizedText[]? texts)
        {
            foreach (Export.LocalizedText text in texts ?? [])
            {
                if (!string.IsNullOrEmpty(text?.Value))
                {
                    return text!.Value!;
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Maps a NodeSet-local NodeId onto the <c>@context</c> prefix that
        /// binds its namespace, which is how the document is written.
        /// </summary>
        private static string PrefixOf(string nodeId, string[]? namespaceUris)
        {
            if (!nodeId.StartsWith("ns=", StringComparison.Ordinal))
            {
                return "ua";
            }
            int separator = nodeId.IndexOf(';', StringComparison.Ordinal);
            if (separator < 0 ||
                !int.TryParse(
#if NETSTANDARD2_0 || NET472 || NET48
                    nodeId.Substring(3, separator - 3),
#else
                    nodeId.AsSpan(3, separator - 3),
#endif
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int index) ||
                index < 1 ||
                namespaceUris is null ||
                index > namespaceUris.Length)
            {
                return "ua";
            }
            return "ns" + index.ToString(CultureInfo.InvariantCulture);
        }

        private readonly Dictionary<string, Entry> m_entries = new(StringComparer.Ordinal);
    }
}
