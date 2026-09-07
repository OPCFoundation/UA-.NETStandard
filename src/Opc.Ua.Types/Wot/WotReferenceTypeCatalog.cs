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

namespace Opc.Ua.Wot
{
    /// <summary>
    /// What a local context answered when asked for the ReferenceType a
    /// compact model name denotes.
    /// </summary>
    internal enum WotReferenceTypeOutcome
    {
        /// <summary>
        /// The local context holds no ReferenceType of that name. The
        /// definitive <c>uav:refId</c>, or the standard base-namespace table,
        /// may still resolve it.
        /// </summary>
        Unresolved,

        /// <summary>
        /// Exactly one ReferenceType matched.
        /// </summary>
        Resolved,

        /// <summary>
        /// More than one ReferenceType matched the same name — one by its
        /// BrowseName and another by its InverseName, say. WoT Binding Section
        /// 6.2 requires <c>uav:refId</c> to settle it.
        /// </summary>
        Ambiguous,

        /// <summary>
        /// The name resolved in this namespace, but to a Node that is not a
        /// ReferenceType. The model defines the name; it just does not define
        /// it as something a relation may use.
        /// </summary>
        NotAReferenceType
    }

    /// <summary>
    /// What the local context answered for one compact model name.
    /// </summary>
    /// <param name="Outcome">The kind of answer.</param>
    /// <param name="Matches">
    /// Every ReferenceType the name matched. Empty unless
    /// <paramref name="Outcome"/> is <see cref="WotReferenceTypeOutcome.Resolved"/>
    /// or <see cref="WotReferenceTypeOutcome.Ambiguous"/>.
    /// </param>
    internal readonly record struct WotReferenceTypeAnswer(
        WotReferenceTypeOutcome Outcome,
        ArrayOf<WotResolvedReferenceType> Matches)
    {
        /// <summary>
        /// Gets the single match of a <see cref="WotReferenceTypeOutcome.Resolved"/>
        /// answer.
        /// </summary>
        public WotResolvedReferenceType Single => Matches[0];

        /// <summary>
        /// Builds the answer for a set of matches, which is what says whether
        /// the name resolved uniquely, ambiguously, or not at all.
        /// </summary>
        public static WotReferenceTypeAnswer FromMatches(
            ArrayOf<WotResolvedReferenceType> matches)
        {
            return matches.Count switch
            {
                0 => new WotReferenceTypeAnswer(
                    WotReferenceTypeOutcome.Unresolved,
                    ArrayOf<WotResolvedReferenceType>.Empty),
                1 => new WotReferenceTypeAnswer(WotReferenceTypeOutcome.Resolved, matches),
                _ => new WotReferenceTypeAnswer(WotReferenceTypeOutcome.Ambiguous, matches)
            };
        }

        /// <summary>
        /// The answer for a name a model defines as something other than a
        /// ReferenceType.
        /// </summary>
        public static WotReferenceTypeAnswer NotAReferenceType { get; } =
            new(
                WotReferenceTypeOutcome.NotAReferenceType,
                ArrayOf<WotResolvedReferenceType>.Empty);

        /// <summary>
        /// The answer for a name the local context does not hold.
        /// </summary>
        public static WotReferenceTypeAnswer Unresolved { get; } =
            new(
                WotReferenceTypeOutcome.Unresolved,
                ArrayOf<WotResolvedReferenceType>.Empty);
    }

    /// <summary>
    /// Holds the ReferenceTypes a conversion resolved against the WoT Binding
    /// Section 5.1.5 local context, keyed by the compact model name a link
    /// relation used, together with the NodeClass the context reports for each
    /// definitive <c>uav:refId</c> the document carries.
    /// </summary>
    /// <remarks>
    /// Resolving a name against the local context is asynchronous, but the
    /// synthesis that consumes the result is not. The names are therefore
    /// resolved once, up front, exactly as the Thing and parent references
    /// already are, and the synthesis reads the answers from here. An entry is
    /// kept even when the local context did not hold the name, so a second
    /// lookup for the same name never re-enters the resolver.
    /// </remarks>
    internal sealed class WotReferenceTypeCatalog
    {
        /// <summary>
        /// Records what the local context answered for a compact model name.
        /// </summary>
        /// <param name="modelName">The compact model name used as the key.</param>
        /// <param name="answer">The answer, resolved or not.</param>
        public void Add(string modelName, WotReferenceTypeAnswer answer)
        {
            m_entries[modelName] = answer;
        }

        /// <summary>
        /// Gets what the local context answered for a compact model name.
        /// </summary>
        /// <param name="modelName">The compact model name.</param>
        /// <param name="answer">The answer.</param>
        /// <returns><c>true</c> when the name was looked up.</returns>
        public bool TryGet(string modelName, out WotReferenceTypeAnswer answer)
        {
            return m_entries.TryGetValue(modelName, out answer);
        }

        /// <summary>
        /// Gets whether the catalog already holds an answer, resolved or not.
        /// </summary>
        /// <param name="modelName">The compact model name.</param>
        /// <returns><c>true</c> when the name was already looked up.</returns>
        public bool Contains(string modelName)
        {
            return m_entries.ContainsKey(modelName);
        }

        /// <summary>
        /// Records the NodeClass the local context reports for a definitive
        /// <c>uav:refId</c>.
        /// </summary>
        /// <remarks>
        /// An identifier the local context does not hold is recorded as
        /// <c>null</c>: a document may name a ReferenceType of a model the
        /// converter was not given, and Section 6.2 keeps that legal. Only an
        /// identifier the context <em>does</em> hold, as something other than a
        /// ReferenceType, is a document error.
        /// </remarks>
        /// <param name="expandedNodeId">The portable ExpandedNodeId.</param>
        /// <param name="nodeClass">The NodeClass, or <c>null</c>.</param>
        public void AddIdentity(string expandedNodeId, WotExpectedNodeClass? nodeClass)
        {
            m_identities[expandedNodeId] = nodeClass;
        }

        /// <summary>
        /// Gets whether a definitive identifier was already looked up.
        /// </summary>
        /// <param name="expandedNodeId">The portable ExpandedNodeId.</param>
        /// <returns><c>true</c> when it was.</returns>
        public bool ContainsIdentity(string expandedNodeId)
        {
            return m_identities.ContainsKey(expandedNodeId);
        }

        /// <summary>
        /// Gets whether the local context holds the identifier as a Node that
        /// is not a ReferenceType.
        /// </summary>
        /// <param name="expandedNodeId">The portable ExpandedNodeId.</param>
        /// <returns>
        /// <c>true</c> when the identifier names a Node of a NodeClass a
        /// relation may not use.
        /// </returns>
        public bool NamesNonReferenceType(string expandedNodeId)
        {
            return m_identities.TryGetValue(
                expandedNodeId, out WotExpectedNodeClass? nodeClass) &&
                nodeClass is not null &&
                nodeClass != WotExpectedNodeClass.ReferenceType;
        }

        private readonly Dictionary<string, WotReferenceTypeAnswer> m_entries =
            new(StringComparer.Ordinal);

        private readonly Dictionary<string, WotExpectedNodeClass?> m_identities =
            new(StringComparer.Ordinal);
    }
}
