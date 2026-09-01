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
using System.Text;
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The path half of an OPC UA event field select clause, which the authored
    /// and the resolved forms of a clause state identically
    /// (WoT Binding Section 6.1).
    /// </summary>
    /// <remarks>
    /// A clause names an EventType and a browse path from it. The two forms
    /// differ only in how the EventType is named — an authored clause references
    /// its definition with <c>tm:ref</c>, a resolved clause carries the portable
    /// ExpandedNodeId that reference produced — so every rule stated over the
    /// path is stated once, over this interface.
    /// </remarks>
    public interface IWotEventSelectClause
    {
        /// <summary>
        /// Gets the browse path from the clause's EventType to the selected
        /// field, relative because the EventType is the clause's anchor.
        /// </summary>
        string BrowsePath { get; }

        /// <summary>
        /// Gets <see cref="BrowsePath"/> as the elements it is made of, in path
        /// order.
        /// </summary>
        ArrayOf<string> PathElements { get; }

        /// <summary>
        /// Gets the <c>data</c> member path the clause materializes into,
        /// decided from the clause alone.
        /// </summary>
        ArrayOf<string> MemberPath { get; }

        /// <summary>
        /// Gets the name the selected field carries in the event <c>data</c>
        /// object.
        /// </summary>
        string FieldName { get; }

        /// <summary>
        /// Gets whether the clause is the empty-path <c>ConditionId</c>
        /// selection.
        /// </summary>
        bool IsConditionIdSelection { get; }
    }

    /// <summary>
    /// One OPC UA event field select clause exactly as a document authors it in
    /// <c>uav:eventSelectClauses</c> (WoT Binding Section 6.1): a <c>tm:ref</c>
    /// naming the EventType definition that declares the field, and the browse
    /// path from that EventType to it.
    /// </summary>
    /// <remarks>
    /// The authored clause is what the document states and never more: the
    /// EventType is named by reference, and the portable ExpandedNodeId a
    /// consumer needs is the <c>uav:id</c> of the definition that reference
    /// resolves to. Resolution is asynchronous and bounded, so it happens once,
    /// in <see cref="WotEventSelectionResolver"/>, and what a runtime consumes
    /// is the resolved <see cref="WotResolvedEventSelectClause"/> rather than
    /// this.
    /// </remarks>
    public sealed class WotEventSelectClause :
        IWotEventSelectClause, IEquatable<WotEventSelectClause>
    {
        /// <summary>
        /// Initializes a new authored select clause.
        /// </summary>
        /// <param name="typeDefinitionReference">
        /// The <c>tm:ref</c> naming the EventType definition that declares the
        /// field: a document URI with an optional RFC 6901 JSON Pointer.
        /// </param>
        /// <param name="browsePath">
        /// The relative browse path from that EventType to the field. The empty
        /// path is the OPC 10000-9 <c>ConditionId</c> idiom and selects the
        /// NodeId Attribute of the Node the notification is about.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when a required value is <c>null</c>.
        /// </exception>
        public WotEventSelectClause(string typeDefinitionReference, string browsePath)
        {
            TypeDefinitionReference = typeDefinitionReference ??
                throw new ArgumentNullException(nameof(typeDefinitionReference));
            BrowsePath = browsePath ?? throw new ArgumentNullException(nameof(browsePath));
            PathElements = WotEventSelectClauses.SplitBrowsePath(BrowsePath);
            MemberPath = WotEventSelectClauses.BuildMemberPath(PathElements);
        }

        /// <summary>
        /// Gets the <c>tm:ref</c> naming the EventType definition that declares
        /// the selected field (WoT Binding Section 6.1).
        /// </summary>
        /// <remarks>
        /// The reference resolves through the local document context of
        /// Section 5.1.5 and is never dereferenced over the network. The
        /// definition it names supplies the clause's <c>TypeDefinitionId</c>
        /// through its <c>uav:id</c>.
        /// </remarks>
        public string TypeDefinitionReference { get; }

        /// <summary>
        /// Gets the browse path from the referenced EventType to the selected
        /// field, relative because the EventType definition is the clause's
        /// anchor (WoT Binding Sections 5.1.4 and 6.1).
        /// </summary>
        public string BrowsePath { get; }

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
        /// <para>
        /// This, and never the joined string, is what every rule of Section 6.1
        /// is stated over. An element may carry a NamespaceUri - the
        /// <c>nsu=&lt;NamespaceUri&gt;;&lt;Name&gt;</c> form of OPC 10000-6 or
        /// the <c>{&lt;NamespaceUri&gt;}&lt;Name&gt;</c> form of OPC 10000-4 -
        /// and a NamespaceUri routinely contains '/', which is also the path
        /// separator. Splitting the joined string on every '/' would therefore
        /// tear <c>nsu=http://example.org/pump/;Temperature</c> into five
        /// nonsense elements. The elements are parsed once here, where the
        /// NamespaceUri is delimited by the <c>;</c> or <c>}</c> that ends it,
        /// so a URI slash is never mistaken for a separator.
        /// </para>
        /// <para>
        /// The empty path - the <c>ConditionId</c> idiom - has no elements.
        /// </para>
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
        /// Where the selected Node is an OPC UA state Variable - whose own
        /// value is the state's localized display text and whose <c>Id</c>
        /// sub-Variable carries the Boolean - the clause naming the field
        /// supplies that object's <c>Name</c> member, so <c>EnabledState</c>
        /// materializes <c>data.EnabledState.Name</c>. The state Variables this
        /// Binding declares are named by
        /// <see cref="WotEventSelectClauses.StateVariableFieldNames"/>, which is
        /// the same set the Condition <c>data</c> schema of Section 13.3 writes
        /// as an <c>{ Id, Name }</c> object, so the document and a runtime
        /// agree about the shape without either having to guess. A companion
        /// state this Binding does not name is decided by the clause list
        /// rather than by one clause, so it is resolved by
        /// <see cref="WotEventSelectClauses.GetMaterializedMemberPaths"/> and
        /// not here.
        /// </para>
        /// <para>
        /// The value is computed once, when the clause is constructed, and the
        /// clause is immutable thereafter: the documented default list is a
        /// process-wide shared value, and a member computed lazily would be
        /// written by whichever thread reached it first.
        /// </para>
        /// </remarks>
        public ArrayOf<string> MemberPath { get; }

        /// <summary>
        /// Gets the name the selected field carries in the event <c>data</c>
        /// object: the last member of <see cref="MemberPath"/>
        /// (WoT Binding Sections 6.1 and 13.3).
        /// </summary>
        /// <remarks>
        /// This is a member <em>name</em> and never a joined browse path: a
        /// path element's namespace qualification names where the field is
        /// declared and not what the member is called.
        /// </remarks>
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
        /// portable form of WoT Binding Section 5.1.3, which is the first step
        /// of the materialization rule of Section 6.1.
        /// </summary>
        /// <remarks>
        /// Normalizing is what makes two paths that name the same elements the
        /// same path even when their prefixes differ. It is not by itself the
        /// uniqueness key: that is the <em>materialized member path</em> the
        /// clause fills, because the namespace qualification is dropped when a
        /// member is named and a state Variable appends <c>Name</c>, so two
        /// distinct normalized paths can still reach one member. Where a prefix
        /// cannot be resolved to a NamespaceUri the element is normalized as
        /// written, which is exact for the documents this library reads and
        /// never reports two distinct paths as one.
        /// </remarks>
        /// <param name="resolvePrefix">
        /// Resolves a path element's prefix to the NamespaceUri the document's
        /// <c>@context</c> binds it to, or <c>null</c> for none.
        /// </param>
        /// <returns>The normalized path.</returns>
        public string GetNormalizedBrowsePath(Func<string, string?>? resolvePrefix = null)
        {
            return WotEventSelectClauses.NormalizeBrowsePath(this, resolvePrefix);
        }

        /// <inheritdoc/>
        public bool Equals(WotEventSelectClause? other)
        {
            return other is not null &&
                string.Equals(
                    TypeDefinitionReference,
                    other.TypeDefinitionReference,
                    StringComparison.Ordinal) &&
                string.Equals(BrowsePath, other.BrowsePath, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as WotEventSelectClause);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(TypeDefinitionReference),
                StringComparer.Ordinal.GetHashCode(BrowsePath));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return "tm:ref '" + TypeDefinitionReference + "' path '" + BrowsePath + "'";
        }
    }

    /// <summary>
    /// The <c>uav:eventSelectClauses</c> term of WoT Binding Section 6.1: its
    /// spelling, the implicit <c>BaseEventType</c> default and the one
    /// implementation of its structural rules.
    /// </summary>
    /// <remarks>
    /// An event affordance states its field selection by linking its EventType
    /// definition with <c>tm:ref</c>, by refining that baseline with this term,
    /// or with both. Where it states neither, a consumer selects the eight
    /// mandatory <c>BaseEventType</c> fields of <see cref="Default"/>.
    /// </remarks>
    public static class WotEventSelectClauses
    {
        /// <summary>
        /// The term itself (WoT Binding Section 6.1).
        /// </summary>
        public const string Term = "uav:eventSelectClauses";

        /// <summary>
        /// The member naming the EventType definition that declares the field:
        /// the W3C <c>tm:ref</c> this Binding already uses for projections
        /// (WoT Binding Sections 6.1 and 12.3).
        /// </summary>
        public const string TypeDefinitionReferenceTerm = "tm:ref";

        /// <summary>
        /// The clause member carrying the browse path to the field. The term is
        /// the same <c>uav:browsePath</c> Section 5.1.4 defines, anchored here
        /// by the EventType definition the clause references.
        /// </summary>
        public const string BrowsePathTerm = "uav:browsePath";

        /// <summary>
        /// The term naming the order an EventType definition's <c>data</c>
        /// members are walked in (WoT Binding Sections 6.1 and 6.11.4). JSON
        /// object member order is not an order, so a walked object with more
        /// than one property states this one.
        /// </summary>
        public const string FieldOrderTerm = "uav:fieldOrder";

        /// <summary>
        /// The <c>@type</c> token that marks an EventType definition
        /// (WoT Binding Section 5.2).
        /// </summary>
        public const string EventTypeAnnotation = "uav:eventType";

        /// <summary>
        /// The ExpandedNodeId of <c>BaseEventType</c>, the type that declares
        /// every standard event field (OPC 10000-5).
        /// </summary>
        public const string BaseEventTypeId = "i=2041";

        /// <summary>
        /// The <c>data</c> member an empty-path clause supplies, which is the
        /// OPC 10000-9 <c>ConditionId</c> idiom (WoT Binding Section 6.1).
        /// </summary>
        public const string ConditionIdFieldName = "ConditionId";

        /// <summary>
        /// The member a state Variable's own value supplies, which is the
        /// state's localized display text (WoT Binding Sections 6.1 and 13.3).
        /// </summary>
        public const string StateNameMember = "Name";

        /// <summary>
        /// The state Variables this Binding declares: a Variable whose own
        /// value is the state's localized display text and whose <c>Id</c>
        /// sub-Variable carries the Boolean (WoT Binding Sections 6.1 and
        /// 13.3).
        /// </summary>
        /// <remarks>
        /// A clause naming one of these supplies the <c>Name</c> member of the
        /// object the field materializes into rather than a string at the
        /// field itself. The set is exactly the one the Condition <c>data</c>
        /// schema of Section 13.3 writes as an <c>{ Id, Name }</c> object, so a
        /// document and a runtime agree about the shape by construction. A
        /// companion state Variable this Binding does not name is recognized
        /// from the selection itself: where another clause of the same list
        /// selects a longer path through the field, the field is an object and
        /// its own clause supplies that object's <c>Name</c>.
        /// </remarks>
        public static ArrayOf<string> StateVariableFieldNames { get; } =
        [
            "EnabledState",
            "AckedState",
            "ConfirmedState",
            "ActiveState"
        ];

        /// <summary>
        /// Determines whether a <c>data</c> member name names a state Variable
        /// this Binding declares (WoT Binding Sections 6.1 and 13.3).
        /// </summary>
        /// <param name="name">The member name.</param>
        /// <returns><c>true</c> when the name is a declared state Variable.</returns>
        public static bool IsStateVariableFieldName(string? name)
        {
            if (name is null)
            {
                return false;
            }
            for (int ii = 0; ii < StateVariableFieldNames.Count; ii++)
            {
                if (string.Equals(StateVariableFieldNames[ii], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the <c>data</c> member path each clause of a list materializes
        /// into, in list order (WoT Binding Sections 6.1 and 13.3).
        /// </summary>
        /// <remarks>
        /// <para>
        /// A clause's own <see cref="WotEventSelectClause.MemberPath"/> decides
        /// the member from the clause alone, which is exact for the state
        /// Variables <see cref="StateVariableFieldNames"/> declares. A
        /// companion state this Binding does not name is decided by the
        /// <em>list</em>: where another clause reaches <em>through</em> a field
        /// - its member path strictly extends this one - the field is an object
        /// and this clause supplies that object's <c>Name</c> member. Stating
        /// it over the list is what makes the rule decidable from the document
        /// alone, without consulting a model.
        /// </para>
        /// <para>
        /// This is the uniqueness key of Section 6.1: two clauses that
        /// materialize the same member compete for it, whatever EventType each
        /// names as the declaring type and however differently their browse
        /// paths are spelled.
        /// </para>
        /// </remarks>
        /// <param name="clauses">The clause list, in document order.</param>
        /// <typeparam name="TClause">The clause form: authored or resolved.</typeparam>
        /// <returns>The materialized member path of each clause.</returns>
        public static ArrayOf<ArrayOf<string>> GetMaterializedMemberPaths<TClause>(
            ArrayOf<TClause> clauses)
            where TClause : IWotEventSelectClause
        {
            if (clauses.IsNull || clauses.Count == 0)
            {
                return ArrayOf<ArrayOf<string>>.Empty;
            }
            var basePaths = new List<string[]>(clauses.Count);
            var reachedThrough = new HashSet<string>(StringComparer.Ordinal);
            for (int ii = 0; ii < clauses.Count; ii++)
            {
                TClause clause = clauses[ii];
                string[] names = clause.IsConditionIdSelection
                    ? [ConditionIdFieldName]
                    : SplitMemberNames(clause.PathElements);
                basePaths.Add(names);
                for (int length = 1; length < names.Length; length++)
                {
                    reachedThrough.Add(JoinMemberPath(names, length));
                }
            }

            var paths = new ArrayOf<string>[clauses.Count];
            for (int ii = 0; ii < clauses.Count; ii++)
            {
                string[] names = basePaths[ii];
                bool isState = !clauses[ii].IsConditionIdSelection &&
                    (IsStateVariableFieldName(names[names.Length - 1]) ||
                        reachedThrough.Contains(JoinMemberPath(names, names.Length)));
                if (!isState)
                {
                    paths[ii] = names;
                    continue;
                }
                var members = new string[names.Length + 1];
                Array.Copy(names, members, names.Length);
                members[names.Length] = StateNameMember;
                paths[ii] = members;
            }
            return paths;
        }

        /// <summary>
        /// Joins a materialized member path into the dotted form the Binding
        /// writes it in - <c>EnabledState.Name</c> - for a diagnostic message.
        /// </summary>
        /// <param name="memberPath">The member names, outermost first.</param>
        /// <returns>The dotted member path.</returns>
        public static string FormatMemberPath(ArrayOf<string> memberPath)
        {
            if (memberPath.IsNull || memberPath.Count == 0)
            {
                return string.Empty;
            }
            var text = new StringBuilder();
            for (int ii = 0; ii < memberPath.Count; ii++)
            {
                if (ii > 0)
                {
                    text.Append('.');
                }
                text.Append(memberPath[ii]);
            }
            return text.ToString();
        }

        /// <summary>
        /// Gets the <c>data</c> member name one browse-path element
        /// materializes: the element's name, without the prefix or
        /// NamespaceUri qualification that says where the field is
        /// <em>declared</em> (WoT Binding Sections 5.1.3 and 6.1).
        /// </summary>
        /// <param name="element">One browse-path element.</param>
        /// <returns>The member name.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="element"/> is <c>null</c>.
        /// </exception>
        public static string MemberName(string element)
        {
            if (element is null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            SplitElement(element, out _, out string name);
            return name;
        }

        /// <summary>
        /// Normalizes one browse-path element to the portable QualifiedName
        /// form of WoT Binding Section 5.1.3: the NamespaceUri the element's
        /// prefix is bound to, and the element's name.
        /// </summary>
        /// <remarks>
        /// Normalization is what makes two paths that name the same elements
        /// the same path even when their prefixes differ. Where the prefix
        /// cannot be resolved the element is normalized to its own spelling, so
        /// two distinct elements are never reported as one.
        /// </remarks>
        /// <param name="element">One browse-path element.</param>
        /// <param name="resolvePrefix">
        /// Resolves a prefix to the NamespaceUri the document binds it to.
        /// </param>
        /// <returns>The normalized element.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="element"/> is <c>null</c>.
        /// </exception>
        public static string NormalizeElement(
            string element, Func<string, string?>? resolvePrefix = null)
        {
            if (element is null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            SplitElement(element, out string? qualifier, out string name);
            if (qualifier is null)
            {
                // A bare name is in namespace 0, which is one namespace and not
                // an unresolved one.
                return Qualify(string.Empty, name);
            }
            if (qualifier.Length > 0 && IsNamespaceUri(element))
            {
                return Qualify(qualifier, name);
            }
            string? namespaceUri = resolvePrefix?.Invoke(qualifier);
            return namespaceUri is { Length: > 0 }
                ? Qualify(namespaceUri, name)
                : element;
        }

        /// <summary>
        /// Splits one browse-path element into the qualifier that says which
        /// namespace declares it - a NamespaceUri or a context-bound prefix -
        /// and the name itself (WoT Binding Section 5.1.3).
        /// </summary>
        private static void SplitElement(string element, out string? qualifier, out string name)
        {
            if (element.StartsWith(NamespaceUriPrefix, StringComparison.Ordinal))
            {
                int separator = element.IndexOf(';', NamespaceUriPrefix.Length);
                if (separator > NamespaceUriPrefix.Length && separator + 1 < element.Length)
                {
                    qualifier = element.Substring(
                        NamespaceUriPrefix.Length, separator - NamespaceUriPrefix.Length);
                    name = element.Substring(separator + 1);
                    return;
                }
                qualifier = null;
                name = element;
                return;
            }
            if (element.Length > 0 && element[0] == '{')
            {
                int close = element.IndexOf('}', 1);
                if (close > 1 && close + 1 < element.Length)
                {
                    qualifier = element.Substring(1, close - 1);
                    name = element.Substring(close + 1);
                    return;
                }
                qualifier = null;
                name = element;
                return;
            }
            int colon = element.IndexOf(':', 0);
            if (colon > 0 && colon + 1 < element.Length)
            {
                qualifier = element.Substring(0, colon);
                name = element.Substring(colon + 1);
                return;
            }
            qualifier = null;
            name = element;
        }

        private static bool IsNamespaceUri(string element)
        {
            return element.StartsWith(NamespaceUriPrefix, StringComparison.Ordinal) ||
                (element.Length > 0 && element[0] == '{');
        }

        /// <summary>
        /// Splits a select-clause browse path into the elements it is made of
        /// (WoT Binding Sections 5.1.3 and 6.1).
        /// </summary>
        /// <remarks>
        /// <para>
        /// A path element is separated from the next by '/', but an element may
        /// carry the NamespaceUri that says which namespace declares it, in
        /// either the <c>nsu=&lt;NamespaceUri&gt;;&lt;Name&gt;</c> form of
        /// OPC 10000-6 or the <c>{&lt;NamespaceUri&gt;}&lt;Name&gt;</c> form of
        /// OPC 10000-4, and a NamespaceUri routinely contains '/'. Only the
        /// separators that follow the delimiter ending the NamespaceUri - the
        /// <c>;</c> or the <c>}</c> - separate elements, so
        /// <c>nsu=http://example.org/pump/;Temperature</c> is one element and
        /// not five. Escaping the URI does not help: OPC 10000-6 §5.3.1.11
        /// escapes only <c>;</c> and <c>%</c>, and '/' is a legal, unescaped
        /// character of every http NamespaceUri this Binding meets.
        /// </para>
        /// <para>
        /// A name never carries the separator, so the split is exact and the
        /// elements round-trip through <see cref="JoinBrowsePath"/>. An empty
        /// path yields no elements; an empty element is preserved, because it
        /// is what the parser reports as a malformed path rather than silently
        /// discards.
        /// </para>
        /// </remarks>
        /// <param name="browsePath">The joined browse path.</param>
        /// <returns>The path elements, in path order.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="browsePath"/> is <c>null</c>.
        /// </exception>
        public static ArrayOf<string> SplitBrowsePath(string browsePath)
        {
            if (browsePath is null)
            {
                throw new ArgumentNullException(nameof(browsePath));
            }
            if (browsePath.Length == 0)
            {
                return ArrayOf<string>.Empty;
            }
            var elements = new List<string>();
            int start = 0;
            while (true)
            {
                int separator = browsePath.IndexOf('/', SkipQualifier(browsePath, start));
                if (separator < 0)
                {
                    elements.Add(browsePath.Substring(start));
                    break;
                }
                elements.Add(browsePath.Substring(start, separator - start));
                start = separator + 1;
            }
            return elements.ToArray();
        }

        /// <summary>
        /// Joins path elements back into the browse path they came from, which
        /// is the inverse of <see cref="SplitBrowsePath"/>.
        /// </summary>
        /// <param name="elements">The path elements, in path order.</param>
        /// <returns>The joined browse path, empty when there are no elements.</returns>
        public static string JoinBrowsePath(ArrayOf<string> elements)
        {
            if (elements.IsNull || elements.Count == 0)
            {
                return string.Empty;
            }
            var path = new StringBuilder();
            for (int ii = 0; ii < elements.Count; ii++)
            {
                if (ii > 0)
                {
                    path.Append('/');
                }
                path.Append(elements[ii]);
            }
            return path.ToString();
        }

        /// <summary>
        /// Gets the index at which the separator search for the element
        /// starting at <paramref name="start"/> begins: past the NamespaceUri
        /// the element carries, whose own '/' characters are not separators.
        /// </summary>
        private static int SkipQualifier(string browsePath, int start)
        {
            if (start >= browsePath.Length)
            {
                return start;
            }
            if (string.CompareOrdinal(
                    browsePath, start, NamespaceUriPrefix, 0, NamespaceUriPrefix.Length) == 0)
            {
                // ';' terminates the NamespaceUri and is percent-escaped inside
                // it, so the first one is the delimiter (OPC 10000-6 §5.3.1.11).
                int delimiter = browsePath.IndexOf(';', start + NamespaceUriPrefix.Length);
                return delimiter < 0 ? start : delimiter + 1;
            }
            if (browsePath[start] == '{')
            {
                int delimiter = browsePath.IndexOf('}', start + 1);
                return delimiter < 0 ? start : delimiter + 1;
            }
            return start;
        }

        /// <summary>
        /// Splits a browse path into the <c>data</c> member names its elements
        /// materialize, dropping the namespace qualification that says where a
        /// field is declared (WoT Binding Section 6.1).
        /// </summary>
        private static string[] SplitMemberNames(ArrayOf<string> elements)
        {
            var names = new string[elements.Count];
            for (int ii = 0; ii < elements.Count; ii++)
            {
                names[ii] = MemberName(elements[ii]);
            }
            return names;
        }

        /// <summary>
        /// Joins the first <paramref name="length"/> member names into a key.
        /// A member name never carries the path separator - the path was split
        /// on it - so the joined form identifies exactly one member path.
        /// </summary>
        private static string JoinMemberPath(string[] names, int length)
        {
            return string.Join("/", names, 0, length);
        }

        /// <summary>
        /// Joins a materialized member path into the same key form.
        /// </summary>
        private static string JoinMemberPath(ArrayOf<string> memberPath)
        {
            var key = new StringBuilder();
            for (int ii = 0; ii < memberPath.Count; ii++)
            {
                if (ii > 0)
                {
                    key.Append('/');
                }
                key.Append(memberPath[ii]);
            }
            return key.ToString();
        }

        private static string Qualify(string namespaceUri, string name)
        {
            return "{" + namespaceUri + "}" + name;
        }

        private const string NamespaceUriPrefix = "nsu=";

        /// <summary>
        /// The mandatory <c>BaseEventType</c> field names selected when the
        /// term is absent, in the order WoT Binding Section 6.1 states.
        /// </summary>
        public static ArrayOf<string> DefaultFieldNames { get; } =
        [
            "EventId",
            "EventType",
            "SourceNode",
            "SourceName",
            "Time",
            "ReceiveTime",
            "Message",
            "Severity"
        ];

        /// <summary>
        /// The implicit <c>BaseEventType</c> selection of WoT Binding
        /// Section 6.1, which applies when an event affordance carries neither
        /// <c>tm:ref</c> nor <c>uav:eventSelectClauses</c>. It is the one
        /// statement of that default: a resolver, a planner, a channel and the
        /// validation rules all read it from here.
        /// </summary>
        /// <remarks>
        /// The default needs no resolution: every clause of it is declared by
        /// <c>BaseEventType</c>, whose ExpandedNodeId is fixed by OPC 10000-5,
        /// so it is already in the resolved form a runtime consumes.
        /// </remarks>
        public static ArrayOf<WotResolvedEventSelectClause> Default { get; } = BuildDefault();

        /// <summary>
        /// Parses and validates a <c>uav:eventSelectClauses</c> array against
        /// the structural rules of WoT Binding Sections 6.1 and 7.
        /// </summary>
        /// <remarks>
        /// The rules are stated once here because the document converter, the
        /// EventType-reference resolver and the OPC UA binding planner all
        /// enforce them, and a second implementation would eventually disagree
        /// with the first. Only the rules a document states on its own are
        /// checked: whether the referenced definition exists, is an EventType
        /// and declares the named field is decided by
        /// <see cref="WotEventSelectionResolver"/>, which is the one place that
        /// resolves a reference. A <c>WhereClause</c> or <c>ContentFilter</c> is
        /// rejected with its own message: it is a query language this Binding
        /// deliberately does not express, and an author who wrote one deserves
        /// to be told why rather than being told a member is unexpected.
        /// </remarks>
        /// <param name="selectClauses">The array member's value.</param>
        /// <param name="clauses">The parsed clauses, in document order.</param>
        /// <param name="error">The first rule violated, when parsing failed.</param>
        /// <param name="errorIndex">
        /// The index of the offending clause, or <c>-1</c> when the violation
        /// belongs to the list itself.
        /// </param>
        /// <returns><c>true</c> when the value is a valid clause list.</returns>
        public static bool TryParse(
            JsonElement selectClauses,
            out ArrayOf<WotEventSelectClause> clauses,
            out string error,
            out int errorIndex)
        {
            return TryParse(selectClauses, null, out clauses, out error, out errorIndex);
        }

        /// <summary>
        /// Parses and validates a <c>uav:eventSelectClauses</c> array against
        /// the structural rules of WoT Binding Sections 6.1 and 7, resolving
        /// path-element prefixes through the document that carries them.
        /// </summary>
        /// <remarks>
        /// Clause uniqueness is stated over the <em>materialized member path</em>
        /// (Section 6.1): that member, and not the browse path it was derived
        /// from, decides the output, so two clauses that reach the same member
        /// compete for it whatever EventType each names as the declaring type
        /// and however differently their paths are spelled. Namespace
        /// qualification is dropped when a member is named and a state Variable
        /// appends <c>Name</c>, so <c>Severity</c> beside a
        /// namespace-qualified <c>Severity</c>, and <c>EnabledState</c> beside
        /// <c>EnabledState/Name</c>, are each two paths and one member.
        /// Resolving prefixes still matters for the repeated-clause message,
        /// which distinguishes the same clause written twice from two clauses
        /// that merely collide.
        /// </remarks>
        /// <param name="selectClauses">The array member's value.</param>
        /// <param name="resolvePrefix">
        /// Resolves a path-element prefix to the NamespaceUri the document's
        /// <c>@context</c> binds it to, or <c>null</c> to compare elements as
        /// written.
        /// </param>
        /// <param name="clauses">The parsed clauses, in document order.</param>
        /// <param name="error">The first rule violated, when parsing failed.</param>
        /// <param name="errorIndex">
        /// The index of the offending clause, or <c>-1</c> when the violation
        /// belongs to the list itself.
        /// </param>
        /// <returns><c>true</c> when the value is a valid clause list.</returns>
        public static bool TryParse(
            JsonElement selectClauses,
            Func<string, string?>? resolvePrefix,
            out ArrayOf<WotEventSelectClause> clauses,
            out string error,
            out int errorIndex)
        {
            clauses = ArrayOf<WotEventSelectClause>.Empty;
            errorIndex = -1;
            if (selectClauses.ValueKind != JsonValueKind.Array)
            {
                error = $"The {Term} term shall be an ordered array of clause objects.";
                return false;
            }
            int count = selectClauses.GetArrayLength();
            if (count == 0)
            {
                error = $"The {Term} array shall not be empty; a document that " +
                    "carries the term states the complete list.";
                return false;
            }

            var parsed = new List<WotEventSelectClause>(count);
            int index = 0;
            foreach (JsonElement clause in selectClauses.EnumerateArray())
            {
                errorIndex = index;
                index++;
                if (clause.ValueKind != JsonValueKind.Object)
                {
                    error = "A select clause shall be an object carrying exactly " +
                        $"{TypeDefinitionReferenceTerm} and {BrowsePathTerm}.";
                    return false;
                }
                if (!TryParseClause(clause, out WotEventSelectClause? entry, out error))
                {
                    return false;
                }
                parsed.Add(entry!);
            }

            // The materialized member path depends on the list a clause sits in
            // - a field another clause reaches through is an object whose Name
            // member this clause supplies - so the list is complete before any
            // member is named (Section 6.1).
            ArrayOf<WotEventSelectClause> candidates = parsed.ToArray();
            if (!TryFindMaterializedCollision(
                candidates, resolvePrefix, out error, out errorIndex))
            {
                return false;
            }

            clauses = candidates;
            error = string.Empty;
            errorIndex = -1;
            return true;
        }

        /// <summary>
        /// Reports the first pair of clauses that materialize into one
        /// <c>data</c> member (WoT Binding Section 6.1).
        /// </summary>
        /// <remarks>
        /// Stated here once because the document converter, the parser and the
        /// OPC UA binding planner all enforce it, and the planner enforces it
        /// again on the clauses it rewrote into portable form.
        /// </remarks>
        /// <param name="clauses">The clause list, in document order.</param>
        /// <param name="resolvePrefix">
        /// Resolves a path-element prefix to the NamespaceUri the document's
        /// <c>@context</c> binds it to, or <c>null</c> to compare paths as
        /// written.
        /// </param>
        /// <param name="error">The collision, when one was found.</param>
        /// <param name="errorIndex">
        /// The index of the second clause of the colliding pair, or <c>-1</c>
        /// when there is none.
        /// </param>
        /// <typeparam name="TClause">The clause form: authored or resolved.</typeparam>
        /// <returns><c>true</c> when no two clauses collide.</returns>
        public static bool TryFindMaterializedCollision<TClause>(
            ArrayOf<TClause> clauses,
            Func<string, string?>? resolvePrefix,
            out string error,
            out int errorIndex)
            where TClause : IWotEventSelectClause
        {
            error = string.Empty;
            errorIndex = -1;
            if (clauses.IsNull || clauses.Count == 0)
            {
                return true;
            }
            ArrayOf<ArrayOf<string>> members = GetMaterializedMemberPaths(clauses);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int ii = 0; ii < clauses.Count; ii++)
            {
                ArrayOf<string> memberPath = members[ii];
                string key = JoinMemberPath(memberPath);
                if (!seen.TryGetValue(key, out int firstIndex))
                {
                    seen.Add(key, ii);
                    continue;
                }
                TClause entry = clauses[ii];
                TClause first = clauses[firstIndex];
                errorIndex = ii;
                if (string.Equals(
                        entry.ToString(), first.ToString(), StringComparison.Ordinal) &&
                    string.Equals(
                        NormalizeBrowsePath(first, resolvePrefix),
                        NormalizeBrowsePath(entry, resolvePrefix),
                        StringComparison.Ordinal))
                {
                    error = $"The clause ({entry}) appears twice; the same clause shall not " +
                        "be selected twice.";
                    return false;
                }
                error = $"The clause ({entry}) materializes the data member " +
                    $"'{FormatMemberPath(memberPath)}', which ({first}) already materializes. " +
                    "The materialized member path of a clause shall be unique within the " +
                    "selection, because that member and not the browse path it came from " +
                    "decides the output, so two clauses that reach it would compete for it " +
                    "and nothing in the document would say which of them filled it " +
                    "(Section 6.1).";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Normalizes a clause's browse path to the portable form of
        /// WoT Binding Section 5.1.3.
        /// </summary>
        /// <param name="clause">The clause.</param>
        /// <param name="resolvePrefix">
        /// Resolves a path-element prefix to the NamespaceUri the document's
        /// <c>@context</c> binds it to, or <c>null</c> to compare paths as
        /// written.
        /// </param>
        /// <returns>The normalized path.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="clause"/> is <c>null</c>.
        /// </exception>
        public static string NormalizeBrowsePath(
            IWotEventSelectClause clause, Func<string, string?>? resolvePrefix = null)
        {
            if (clause is null)
            {
                throw new ArgumentNullException(nameof(clause));
            }
            if (clause.IsConditionIdSelection)
            {
                return string.Empty;
            }
            ArrayOf<string> elements = clause.PathElements;
            var normalized = new StringBuilder();
            for (int ii = 0; ii < elements.Count; ii++)
            {
                if (ii > 0)
                {
                    normalized.Append('/');
                }
                normalized.Append(NormalizeElement(elements[ii], resolvePrefix));
            }
            return normalized.ToString();
        }

        /// <summary>
        /// Determines whether a member name spells an OPC 10000-4
        /// <c>WhereClause</c> or <c>ContentFilter</c>, which no term of this
        /// Binding carries (WoT Binding Sections 6.1 and 7).
        /// </summary>
        /// <param name="member">The member name.</param>
        /// <returns><c>true</c> when the name names a content filter.</returns>
        public static bool IsContentFilterMember(string? member)
        {
            if (string.IsNullOrEmpty(member))
            {
                return false;
            }
            string local = member!.StartsWith(WotDocument.UavPrefix, StringComparison.Ordinal)
                ? member.Substring(WotDocument.UavPrefix.Length)
                : member;
            return string.Equals(local, "whereClause", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(local, "where", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(local, "contentFilter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(local, "filter", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseClause(
            JsonElement clause,
            out WotEventSelectClause? parsed,
            out string error)
        {
            parsed = null;
            string? typeDefinitionReference = null;
            string? browsePath = null;
            foreach (JsonProperty member in clause.EnumerateObject())
            {
                if (string.Equals(
                    member.Name, TypeDefinitionReferenceTerm, StringComparison.Ordinal))
                {
                    if (member.Value.ValueKind != JsonValueKind.String)
                    {
                        error = $"The {TypeDefinitionReferenceTerm} of a select clause shall " +
                            "be a document URI with an optional RFC 6901 JSON Pointer.";
                        return false;
                    }
                    typeDefinitionReference = member.Value.GetString();
                    continue;
                }
                if (string.Equals(member.Name, BrowsePathTerm, StringComparison.Ordinal))
                {
                    if (member.Value.ValueKind != JsonValueKind.String)
                    {
                        error = $"The {BrowsePathTerm} of a select clause shall be a string.";
                        return false;
                    }
                    browsePath = member.Value.GetString();
                    continue;
                }
                error = IsContentFilterMember(member.Name)
                    ? $"A select clause carries '{member.Name}'. The EventFilter " +
                        "WhereClause - the OPC 10000-4 ContentFilter that decides which " +
                        "occurrences are delivered - is out of scope of this Binding and " +
                        "shall not be carried by any of its terms (Section 6.1)."
                    : $"A select clause carries '{member.Name}'; a clause carries exactly " +
                        $"{TypeDefinitionReferenceTerm} and {BrowsePathTerm} and no other " +
                        "member.";
                return false;
            }

            if (string.IsNullOrEmpty(typeDefinitionReference))
            {
                error = $"A select clause shall carry {TypeDefinitionReferenceTerm}, naming " +
                    "the EventType definition that declares the selected field.";
                return false;
            }
            if (!IsEventTypeReference(typeDefinitionReference!))
            {
                error = $"The select-clause {TypeDefinitionReferenceTerm} " +
                    $"'{typeDefinitionReference}' is not a document URI with an optional " +
                    "RFC 6901 JSON Pointer (Section 6.1).";
                return false;
            }
            if (browsePath is null)
            {
                error = $"A select clause shall carry {BrowsePathTerm}; the empty path is " +
                    "written explicitly for the ConditionId selection.";
                return false;
            }
            if (browsePath.Length > 0)
            {
                // The rule is stated over the elements, not over the joined
                // string: a NamespaceUri-qualified element legally carries '/'
                // inside its URI, so 'nsu=http://example.org/pump/;Temperature'
                // is one well-formed element and not a path with empty ones.
                ArrayOf<string> elements = SplitBrowsePath(browsePath);
                if (elements[0].Length == 0)
                {
                    error = $"The select-clause browse path '{browsePath}' is absolute; a " +
                        "clause path is relative to the EventType definition the clause " +
                        $"references through {TypeDefinitionReferenceTerm}, which anchors it " +
                        "(Section 6.1).";
                    return false;
                }
                if (elements[elements.Count - 1].Length == 0)
                {
                    error = $"The select-clause browse path '{browsePath}' ends with a " +
                        "separator, so its last element is empty (Section 6.1).";
                    return false;
                }
                for (int ii = 1; ii < elements.Count - 1; ii++)
                {
                    if (elements[ii].Length == 0)
                    {
                        error = $"The select-clause browse path '{browsePath}' carries an " +
                            "empty element; only the whole path may be empty, which is the " +
                            "ConditionId selection (Section 6.1).";
                        return false;
                    }
                }
            }

            parsed = new WotEventSelectClause(typeDefinitionReference!, browsePath);
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Determines whether a value spells an EventType reference: a document
        /// URI, optionally followed by a '#' and a non-empty RFC 6901 JSON
        /// Pointer (WoT Binding Section 6.1).
        /// </summary>
        /// <param name="reference">The <c>tm:ref</c> value.</param>
        /// <returns><c>true</c> when the value is a well-formed reference.</returns>
        public static bool IsEventTypeReference(string? reference)
        {
            return TrySplitEventTypeReference(reference, out _, out _);
        }

        /// <summary>
        /// Splits an EventType reference into the document URI it names and the
        /// RFC 6901 JSON Pointer into that document, which is empty where the
        /// reference names the document's root (WoT Binding Section 6.1).
        /// </summary>
        /// <param name="reference">The <c>tm:ref</c> value.</param>
        /// <param name="document">The document URI.</param>
        /// <param name="pointer">The JSON Pointer, empty for the root.</param>
        /// <returns><c>true</c> when the value is a well-formed reference.</returns>
        public static bool TrySplitEventTypeReference(
            string? reference, out string document, out string pointer)
        {
            document = string.Empty;
            pointer = string.Empty;
            if (string.IsNullOrEmpty(reference))
            {
                return false;
            }
            int hash = reference!.IndexOf('#', StringComparison.Ordinal);
            if (hash < 0)
            {
                document = reference;
                return true;
            }
            if (hash == 0 || hash + 1 >= reference.Length)
            {
                // A reference without a document names nothing this Binding can
                // resolve, and a '#' that starts no pointer is not a pointer.
                return false;
            }
            string candidate = reference.Substring(hash + 1);
            if (candidate[0] != '/')
            {
                return false;
            }
            for (int ii = 0; ii < candidate.Length; ii++)
            {
                if (candidate[ii] != '~')
                {
                    continue;
                }
                if (ii + 1 >= candidate.Length ||
                    (candidate[ii + 1] != '0' && candidate[ii + 1] != '1'))
                {
                    return false;
                }
                ii++;
            }
            document = reference.Substring(0, hash);
            pointer = candidate;
            return true;
        }

        /// <summary>
        /// Builds the <c>data</c> member path a browse path materializes into,
        /// decided from that path alone (WoT Binding Section 6.1).
        /// </summary>
        /// <param name="elements">The browse-path elements, in path order.</param>
        /// <returns>The member path, outermost first.</returns>
        internal static ArrayOf<string> BuildMemberPath(ArrayOf<string> elements)
        {
            if (elements.Count == 0)
            {
                return new[] { ConditionIdFieldName };
            }
            var members = new List<string>(elements.Count + 1);
            for (int ii = 0; ii < elements.Count; ii++)
            {
                members.Add(MemberName(elements[ii]));
            }
            if (IsStateVariableFieldName(members[members.Count - 1]))
            {
                members.Add(StateNameMember);
            }
            return members.ToArray();
        }

        private static ArrayOf<WotResolvedEventSelectClause> BuildDefault()
        {
            var clauses = new WotResolvedEventSelectClause[DefaultFieldNames.Count];
            for (int ii = 0; ii < DefaultFieldNames.Count; ii++)
            {
                clauses[ii] = new WotResolvedEventSelectClause(
                    BaseEventTypeId,
                    DefaultFieldNames[ii],
                    WotEventSelectClauseSource.ImplicitDefault);
            }
            return clauses;
        }
    }
}
