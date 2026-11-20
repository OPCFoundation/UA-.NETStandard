// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Text;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Facts derived once from one merged NodeSet for the duration of a single
        /// document-set validation, so validating every document of the set does not
        /// re-derive them from the same nodes.
        /// </summary>
        /// <remarks>
        /// The instance is created and dropped inside one validation call and is never
        /// shared, so nothing it holds can outlive a mutation of the NodeSet. The
        /// archived fact index depends on the alias declarations of the document being
        /// validated, so it is reused only for a byte-identical alias sequence. A request
        /// for any other NodeSet is derived without caching.
        /// </remarks>
        internal sealed class ArchiveDerivations
        {
            /// <summary>
            /// Creates the derivations of one merged NodeSet.
            /// </summary>
            /// <param name="context">The merged NodeSet every cached fact is derived from.</param>
            public ArchiveDerivations(UANodeSet context)
            {
                m_context = context ?? throw new ArgumentNullException(nameof(context));
            }

            /// <summary>
            /// Gets the ReferenceType index of the NodeSet.
            /// </summary>
            /// <param name="nodeSet">The NodeSet.</param>
            /// <returns>The index.</returns>
            public WotReferenceTypeNames GetReferenceTypes(UANodeSet nodeSet)
            {
                if (!ReferenceEquals(nodeSet, m_context))
                {
                    return WotReferenceTypeNames.Build(nodeSet);
                }
                return m_referenceTypes ??= WotReferenceTypeNames.Build(nodeSet);
            }

            /// <summary>
            /// Gets the archived fact index of the NodeSet under the alias declarations
            /// the document is validated with.
            /// </summary>
            /// <param name="nodeSet">The NodeSet.</param>
            /// <param name="declaredAliases">The alias declarations of the validated document.</param>
            /// <param name="aliases">The alias resolver built from exactly those declarations.</param>
            /// <returns>The index, which callers must treat as read-only.</returns>
            public Dictionary<string, UANode?> GetFactIndex(
                UANodeSet nodeSet,
                NodeIdAlias[]? declaredAliases,
                INodeSetAliasResolver aliases)
            {
                if (!ReferenceEquals(nodeSet, m_context))
                {
                    return BuildArchivedFactIndex(nodeSet, aliases);
                }
                string key = Fingerprint(declaredAliases);
                if (!m_factIndexes.TryGetValue(key, out Dictionary<string, UANode?>? index))
                {
                    index = BuildArchivedFactIndex(nodeSet, aliases);
                    m_factIndexes.Add(key, index);
                }
                return index;
            }

            /// <summary>
            /// Gets the NodeIds and BrowseNames the NodeSet declares.
            /// </summary>
            /// <param name="nodeSet">The NodeSet.</param>
            /// <returns>The identity sets, which callers must treat as read-only.</returns>
            public (HashSet<string> NodeIds, HashSet<string> BrowseNames) GetIdentities(UANodeSet nodeSet)
            {
                if (!ReferenceEquals(nodeSet, m_context))
                {
                    return CollectIdentities(nodeSet);
                }
                return m_identities ??= CollectIdentities(nodeSet);
            }

            internal static (HashSet<string> NodeIds, HashSet<string> BrowseNames) CollectIdentities(
                UANodeSet nodeSet)
            {
                var nodeIds = new HashSet<string>(StringComparer.Ordinal);
                var browseNames = new HashSet<string>(StringComparer.Ordinal);
                if (nodeSet.Items is not null)
                {
                    foreach (UANode node in nodeSet.Items)
                    {
                        if (!string.IsNullOrEmpty(node.NodeId))
                        {
                            nodeIds.Add(node.NodeId);
                        }
                        if (!string.IsNullOrEmpty(node.BrowseName))
                        {
                            browseNames.Add(node.BrowseName);
                        }
                    }
                }
                return (nodeIds, browseNames);
            }

            private static string Fingerprint(NodeIdAlias[]? aliases)
            {
                var key = new StringBuilder();
                foreach (NodeIdAlias alias in aliases ?? [])
                {
                    if (alias is null)
                    {
                        key.Append("!;");
                        continue;
                    }
                    Append(key, alias.Alias);
                    Append(key, alias.Value);
                }
                return key.ToString();
            }

            private static void Append(StringBuilder key, string? value)
            {
                if (value is null)
                {
                    key.Append("-;");
                    return;
                }
                key.Append(value.Length).Append(':').Append(value).Append(';');
            }

            private readonly UANodeSet m_context;
            private readonly Dictionary<string, Dictionary<string, UANode?>> m_factIndexes =
                new(StringComparer.Ordinal);
            private WotReferenceTypeNames? m_referenceTypes;
            private (HashSet<string> NodeIds, HashSet<string> BrowseNames)? m_identities;
        }
    }
}
