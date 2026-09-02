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

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Where a resolved select clause came from (WoT Binding Section 6.1).
    /// </summary>
    public enum WotEventSelectClauseSource
    {
        /// <summary>
        /// The affordance stated no selection at all, so the clause is one of
        /// the eight mandatory <c>BaseEventType</c> fields a consumer selects
        /// by default.
        /// </summary>
        ImplicitDefault,

        /// <summary>
        /// The clause was derived from a leaf of the <c>data</c> schema of the
        /// EventType definition the affordance links to with <c>tm:ref</c>.
        /// </summary>
        LinkedEventType,

        /// <summary>
        /// The clause was written by the author in
        /// <c>uav:eventSelectClauses</c> and overlays the baseline.
        /// </summary>
        Explicit
    }

    /// <summary>
    /// One OPC UA event field select clause after its EventType reference has
    /// been resolved (WoT Binding Section 6.1): the portable ExpandedNodeId of
    /// the EventType that declares the field, and the browse path from that
    /// EventType to it.
    /// </summary>
    /// <remarks>
    /// This is the form a runtime consumes: it is the readable equivalent of an
    /// OPC 10000-4 <c>SimpleAttributeOperand</c>, and it is deliberately
    /// transport-neutral. <see cref="TypeDefinitionId"/> is the portable
    /// ExpandedNodeId the linked EventType definition declared as its
    /// <c>uav:id</c>, because a namespace index only means something to the
    /// session that read the namespace table, and a consumer resolves both the
    /// type and the path elements against its own table when it creates the
    /// MonitoredItem.
    /// </remarks>
    public sealed class WotResolvedEventSelectClause :
        IWotEventSelectClause, IEquatable<WotResolvedEventSelectClause>
    {
        /// <summary>
        /// Initializes a new resolved select clause.
        /// </summary>
        /// <param name="typeDefinitionId">
        /// The portable ExpandedNodeId of the EventType that declares the field.
        /// </param>
        /// <param name="browsePath">
        /// The relative browse path from that EventType to the field. The empty
        /// path is the OPC 10000-9 <c>ConditionId</c> idiom and selects the
        /// NodeId Attribute of the Node the notification is about.
        /// </param>
        /// <param name="source">Where the clause came from.</param>
        /// <param name="typeDefinitionReference">
        /// The <c>tm:ref</c> the EventType identity was resolved from, or
        /// <c>null</c> where the clause is one of the implicit defaults and no
        /// document named it.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when a required value is <c>null</c>.
        /// </exception>
        public WotResolvedEventSelectClause(
            string typeDefinitionId,
            string browsePath,
            WotEventSelectClauseSource source = WotEventSelectClauseSource.Explicit,
            string? typeDefinitionReference = null)
        {
            TypeDefinitionId = typeDefinitionId ??
                throw new ArgumentNullException(nameof(typeDefinitionId));
            BrowsePath = browsePath ?? throw new ArgumentNullException(nameof(browsePath));
            Source = source;
            TypeDefinitionReference = typeDefinitionReference;
            PathElements = WotEventSelectClauses.SplitBrowsePath(BrowsePath);
            MemberPath = WotEventSelectClauses.BuildMemberPath(PathElements);
        }

        /// <summary>
        /// Gets the portable ExpandedNodeId of the EventType that declares the
        /// selected field (WoT Binding Sections 5.1.1 and 6.1).
        /// </summary>
        public string TypeDefinitionId { get; }

        /// <summary>
        /// Gets the browse path from <see cref="TypeDefinitionId"/> to the
        /// selected field, relative because the EventType is the clause's
        /// anchor (WoT Binding Sections 5.1.4 and 6.1).
        /// </summary>
        public string BrowsePath { get; }

        /// <summary>
        /// Gets where the clause came from.
        /// </summary>
        public WotEventSelectClauseSource Source { get; }

        /// <summary>
        /// Gets the <c>tm:ref</c> the EventType identity was resolved from, or
        /// <c>null</c> for an implicit default clause no document named.
        /// </summary>
        /// <remarks>
        /// It is carried for diagnostics: a message that names the reference an
        /// author wrote is one the author can act on, and a resolved clause has
        /// otherwise lost every trace of the document it came from.
        /// </remarks>
        public string? TypeDefinitionReference { get; }

        /// <summary>
        /// Gets whether the clause is the empty-path <c>ConditionId</c>
        /// selection, which selects the NodeId Attribute rather than a Value
        /// (WoT Binding Section 6.1).
        /// </summary>
        public bool IsConditionIdSelection => BrowsePath.Length == 0;

        /// <summary>
        /// Gets <see cref="BrowsePath"/> as the elements it is made of, in
        /// path order (WoT Binding Sections 5.1.3 and 6.1).
        /// </summary>
        /// <remarks>
        /// This, and never the joined string, is what every rule of Section 6.1
        /// is stated over. An element may carry a NamespaceUri - the
        /// <c>nsu=&lt;NamespaceUri&gt;;&lt;Name&gt;</c> form of OPC 10000-6 or
        /// the <c>{&lt;NamespaceUri&gt;}&lt;Name&gt;</c> form of OPC 10000-4 -
        /// and a NamespaceUri routinely contains '/', which is also the path
        /// separator, so the elements are parsed once rather than split from
        /// the joined string on every use.
        /// </remarks>
        public ArrayOf<string> PathElements { get; }

        /// <summary>
        /// Gets the <c>data</c> member path the clause materializes into
        /// (WoT Binding Sections 6.1 and 13.3).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The rule is a function of the browse path alone. The empty path
        /// materializes <c>ConditionId</c>; a one-element path materializes the
        /// member of that element's name; a longer path materializes the member
        /// named by its <em>last</em> element, nested inside an object member
        /// for each preceding element, so <c>EnabledState/Id</c> is
        /// <c>data.EnabledState.Id</c> and never a member literally called
        /// <c>EnabledState/Id</c>. A member name therefore never contains the
        /// path separator.
        /// </para>
        /// <para>
        /// A companion state this Binding does not name is decided by the
        /// selection rather than by one clause, so it is resolved by
        /// <see cref="WotEventSelectClauses.GetMaterializedMemberPaths{TClause}"/>
        /// and not here. The value is computed once, when the clause is
        /// constructed, and the clause is immutable thereafter.
        /// </para>
        /// </remarks>
        public ArrayOf<string> MemberPath { get; }

        /// <summary>
        /// Gets the name the selected field carries in the event <c>data</c>
        /// object: the last member of <see cref="MemberPath"/>
        /// (WoT Binding Sections 6.1 and 13.3).
        /// </summary>
        public string FieldName
        {
            get
            {
                ArrayOf<string> path = MemberPath;
                return path.Count == 0 ? string.Empty : path[path.Count - 1];
            }
        }

        /// <summary>
        /// Gets the clause's browse path with every element normalized to the
        /// portable form of WoT Binding Section 5.1.3.
        /// </summary>
        /// <param name="resolvePrefix">
        /// Resolves a path element's prefix to the NamespaceUri the document's
        /// <c>@context</c> binds it to, or <c>null</c> for none.
        /// </param>
        /// <returns>The normalized path.</returns>
        public string GetNormalizedBrowsePath(Func<string, string?>? resolvePrefix = null)
        {
            return WotEventSelectClauses.NormalizeBrowsePath(this, resolvePrefix);
        }

        /// <summary>
        /// Returns this clause with its browse path replaced, which is what a
        /// planner does when it rewrites compact model names into the portable
        /// <c>nsu=</c> form a channel can resolve without the document.
        /// </summary>
        /// <param name="browsePath">The rewritten browse path.</param>
        /// <returns>The rewritten clause.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="browsePath"/> is <c>null</c>.
        /// </exception>
        public WotResolvedEventSelectClause WithBrowsePath(string browsePath)
        {
            return new WotResolvedEventSelectClause(
                TypeDefinitionId, browsePath, Source, TypeDefinitionReference);
        }

        /// <inheritdoc/>
        public bool Equals(WotResolvedEventSelectClause? other)
        {
            return other is not null &&
                string.Equals(TypeDefinitionId, other.TypeDefinitionId, StringComparison.Ordinal) &&
                string.Equals(BrowsePath, other.BrowsePath, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as WotResolvedEventSelectClause);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(TypeDefinitionId),
                StringComparer.Ordinal.GetHashCode(BrowsePath));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return TypeDefinitionId + "#" + BrowsePath;
        }
    }
}
