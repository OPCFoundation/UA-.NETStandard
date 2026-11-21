// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The alias policy the WoT Binding conversion applies: the standard
    /// base-namespace names a converted NodeSet2 document writes, and the
    /// identifiers they stand for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A conversion from a WoT document states a ReferenceType or a DataType
    /// of the base namespace by the name OPC 10000-5 gives it -
    /// <c>HasComponent</c>, <c>Double</c> - because that is what the WoT
    /// Binding's mappings are written in and what a restored document has to
    /// read as. Two things then need to know what those names stand for: the
    /// pass that declares them in the document's <c>&lt;Aliases&gt;</c> table
    /// so a Server can load it, and the comparison that asks whether a
    /// restored document says what the source said. Stating the policy once,
    /// here, is what keeps the two answering the same question.
    /// </para>
    /// <para>
    /// The names themselves are not repeated: they are the ones NodeSet2
    /// already knows, so this only says that the WoT Binding writes them. A
    /// name outside that set - a vendor alias a source document declared for
    /// itself - is not resolved here, and stays the document's own to declare.
    /// </para>
    /// </remarks>
    public sealed class WotNodeSetAliases : INodeSetAliasResolver
    {
        private WotNodeSetAliases()
        {
        }

        /// <summary>
        /// Gets the policy, which holds no state and is safe to share.
        /// </summary>
        public static WotNodeSetAliases Instance { get; } = new();

        /// <inheritdoc/>
        public bool TryResolve(string alias, out string nodeId)
        {
            return NodeSetStandardAliases.TryResolve(alias, out nodeId);
        }
    }
}
