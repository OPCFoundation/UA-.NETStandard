/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Export
{
    /// <summary>
    /// Resolves a name a NodeSet2 document writes where a NodeId is expected
    /// to the identifier it stands for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NodeSet2 document may write a name such as <c>HasComponent</c> in
    /// place of an identifier, and its own <c>&lt;Aliases&gt;</c> table says
    /// what that name stands for. What a name means is therefore a matter of
    /// policy rather than of the document alone: the importer accepts only
    /// what the document declares, while a producer that writes readable names
    /// has to know the standard ones, and a profile such as the WoT Binding
    /// states which names it may write without the document repeating them.
    /// </para>
    /// <para>
    /// This states that policy as one thing that can be handed to whatever
    /// needs it - a comparison, an alias completion pass - so that each of
    /// them applies the caller's policy rather than a policy of its own. An
    /// implementation is expected to be free of side effects and to give the
    /// same answer for the same name every time, because both callers depend
    /// on a deterministic result.
    /// </para>
    /// </remarks>
    public interface INodeSetAliasResolver
    {
        /// <summary>
        /// Resolves a name to the identifier it stands for.
        /// </summary>
        /// <param name="alias">The name as the document writes it.</param>
        /// <param name="nodeId">
        /// The identifier the name stands for, or an empty string when the
        /// name is not one this resolver knows.
        /// </param>
        /// <returns>
        /// <c>true</c> when the name resolved, <c>false</c> when it is not an
        /// alias as far as this resolver is concerned and has to be left
        /// exactly as the document wrote it.
        /// </returns>
        bool TryResolve(string alias, out string nodeId);
    }
}
