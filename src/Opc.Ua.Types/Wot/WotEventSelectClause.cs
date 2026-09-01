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
    /// One OPC UA event field select clause authored by
    /// <c>uav:eventSelectClauses</c> (WoT Binding Section 6.1): the EventType
    /// that declares the field, and the browse path from that type to it.
    /// </summary>
    /// <remarks>
    /// The clause is the readable form of an OPC 10000-4
    /// <c>SimpleAttributeOperand</c>. It is deliberately transport-neutral:
    /// <see cref="TypeDefinitionId"/> stays the portable ExpandedNodeId the
    /// document wrote, because a namespace index only means something to the
    /// session that read the namespace table, and a consumer resolves both the
    /// type and the path elements against its own table when it creates the
    /// MonitoredItem.
    /// </remarks>
    public sealed class WotEventSelectClause : IEquatable<WotEventSelectClause>
    {
        /// <summary>
        /// Initializes a new select clause.
        /// </summary>
        /// <param name="typeDefinitionId">
        /// The portable ExpandedNodeId of the EventType that declares the field.
        /// </param>
        /// <param name="browsePath">
        /// The relative browse path from that type to the field. The empty path
        /// is the OPC 10000-9 <c>ConditionId</c> idiom and selects the NodeId
        /// Attribute of the Node the notification is about.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when a required value is <c>null</c>.
        /// </exception>
        public WotEventSelectClause(string typeDefinitionId, string browsePath)
        {
            TypeDefinitionId = typeDefinitionId ??
                throw new ArgumentNullException(nameof(typeDefinitionId));
            BrowsePath = browsePath ?? throw new ArgumentNullException(nameof(browsePath));
            MemberPath = BuildMemberPath(BrowsePath);
        }

        /// <summary>
        /// Gets the portable ExpandedNodeId of the EventType that declares the
        /// selected field (WoT Binding Sections 5.1.1 and 6.1).
        /// </summary>
        public string TypeDefinitionId { get; }

        /// <summary>
        /// Gets the browse path from <see cref="TypeDefinitionId"/> to the
        /// selected field, relative because the type definition is the clause's
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
            if (IsConditionIdSelection)
            {
                return string.Empty;
            }
            string[] elements = BrowsePath.Split('/');
            var normalized = new StringBuilder();
            for (int ii = 0; ii < elements.Length; ii++)
            {
                if (ii > 0)
                {
                    normalized.Append('/');
                }
                normalized.Append(
                    WotEventSelectClauses.NormalizeElement(elements[ii], resolvePrefix));
            }
            return normalized.ToString();
        }

        /// <inheritdoc/>
        public bool Equals(WotEventSelectClause? other)
        {
            return other is not null &&
                string.Equals(TypeDefinitionId, other.TypeDefinitionId, StringComparison.Ordinal) &&
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
                StringComparer.Ordinal.GetHashCode(TypeDefinitionId),
                StringComparer.Ordinal.GetHashCode(BrowsePath));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return TypeDefinitionId + "#" + BrowsePath;
        }

        private static ArrayOf<string> BuildMemberPath(string browsePath)
        {
            if (browsePath.Length == 0)
            {
                return new[] { WotEventSelectClauses.ConditionIdFieldName };
            }
            string[] elements = browsePath.Split('/');
            var members = new List<string>(elements.Length + 1);
            for (int ii = 0; ii < elements.Length; ii++)
            {
                members.Add(WotEventSelectClauses.MemberName(elements[ii]));
            }
            if (WotEventSelectClauses.IsStateVariableFieldName(members[members.Count - 1]))
            {
                members.Add(WotEventSelectClauses.StateNameMember);
            }
            return members.ToArray();
        }
    }

    /// <summary>
    /// The <c>uav:eventSelectClauses</c> term of WoT Binding Section 6.1: its
    /// spelling, its documented default and the one implementation of its
    /// structural rules.
    /// </summary>
    /// <remarks>
    /// The term states the complete select-clause list an event MonitoredItem
    /// is created with. Where it is absent a consumer selects the eight
    /// mandatory <c>BaseEventType</c> fields of <see cref="Default"/>; where it
    /// is present that default is replaced rather than extended, so one list is
    /// read rather than a list plus a rule about what silently precedes it.
    /// </remarks>
    public static class WotEventSelectClauses
    {
        /// <summary>
        /// The term itself (WoT Binding Section 6.1).
        /// </summary>
        public const string Term = "uav:eventSelectClauses";

        /// <summary>
        /// The clause member naming the EventType that declares the field.
        /// </summary>
        public const string TypeDefinitionIdTerm = "uav:typeDefinitionId";

        /// <summary>
        /// The clause member carrying the browse path to the field. The term is
        /// the same <c>uav:browsePath</c> Section 5.1.4 defines, anchored here
        /// by the clause's type definition.
        /// </summary>
        public const string BrowsePathTerm = "uav:browsePath";

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
        /// <returns>The materialized member path of each clause.</returns>
        public static ArrayOf<ArrayOf<string>> GetMaterializedMemberPaths(
            ArrayOf<WotEventSelectClause> clauses)
        {
            if (clauses.IsNull || clauses.Count == 0)
            {
                return ArrayOf<ArrayOf<string>>.Empty;
            }
            var basePaths = new List<string[]>(clauses.Count);
            var reachedThrough = new HashSet<string>(StringComparer.Ordinal);
            for (int ii = 0; ii < clauses.Count; ii++)
            {
                WotEventSelectClause clause = clauses[ii];
                string[] names = clause.IsConditionIdSelection
                    ? [ConditionIdFieldName]
                    : SplitMemberNames(clause.BrowsePath);
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
        /// Splits a browse path into the <c>data</c> member names its elements
        /// materialize, dropping the namespace qualification that says where a
        /// field is declared (WoT Binding Section 6.1).
        /// </summary>
        private static string[] SplitMemberNames(string browsePath)
        {
            string[] elements = browsePath.Split('/');
            var names = new string[elements.Length];
            for (int ii = 0; ii < elements.Length; ii++)
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
        /// The documented default select-clause list, which applies when an
        /// event affordance carries no <c>uav:eventSelectClauses</c>
        /// (WoT Binding Section 6.1). It is the one statement of that default:
        /// a planner, a channel and the validation rules all read it from here.
        /// </summary>
        public static ArrayOf<WotEventSelectClause> Default { get; } = BuildDefault();

        /// <summary>
        /// Parses and validates a <c>uav:eventSelectClauses</c> array against
        /// the structural rules of WoT Binding Sections 6.1 and 7.
        /// </summary>
        /// <remarks>
        /// The rules are stated once here because both the document converter
        /// and the OPC UA binding planner enforce them, and a second
        /// implementation would eventually disagree with the first. A
        /// <c>WhereClause</c> or <c>ContentFilter</c> is rejected with its own
        /// message: it is a query language this Binding deliberately does not
        /// express, and an author who wrote one deserves to be told why rather
        /// than being told a member is unexpected.
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
                        $"{TypeDefinitionIdTerm} and {BrowsePathTerm}.";
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
        /// <returns><c>true</c> when no two clauses collide.</returns>
        public static bool TryFindMaterializedCollision(
            ArrayOf<WotEventSelectClause> clauses,
            Func<string, string?>? resolvePrefix,
            out string error,
            out int errorIndex)
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
                WotEventSelectClause entry = clauses[ii];
                WotEventSelectClause first = clauses[firstIndex];
                errorIndex = ii;
                if (string.Equals(
                        first.TypeDefinitionId, entry.TypeDefinitionId, StringComparison.Ordinal) &&
                    string.Equals(
                        first.GetNormalizedBrowsePath(resolvePrefix),
                        entry.GetNormalizedBrowsePath(resolvePrefix),
                        StringComparison.Ordinal))
                {
                    error = $"The clause ({entry.TypeDefinitionId}, '{entry.BrowsePath}') " +
                        "appears twice; the same clause shall not be selected twice.";
                    return false;
                }
                error = $"The clause ({entry.TypeDefinitionId}, '{entry.BrowsePath}') " +
                    $"materializes the data member '{FormatMemberPath(memberPath)}', which " +
                    $"({first.TypeDefinitionId}, '{first.BrowsePath}') already materializes. " +
                    "The materialized member path of a clause shall be unique within the " +
                    "array, because that member and not the browse path it came from decides " +
                    "the output, so two clauses that reach it would compete for it and " +
                    "nothing in the document would say which of them filled it " +
                    "(Section 6.1).";
                return false;
            }
            return true;
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
            string? typeDefinitionId = null;
            string? browsePath = null;
            foreach (JsonProperty member in clause.EnumerateObject())
            {
                if (string.Equals(member.Name, TypeDefinitionIdTerm, StringComparison.Ordinal))
                {
                    if (member.Value.ValueKind != JsonValueKind.String)
                    {
                        error = $"The {TypeDefinitionIdTerm} of a select clause shall be an " +
                            "ExpandedNodeId string.";
                        return false;
                    }
                    typeDefinitionId = member.Value.GetString();
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
                        $"{TypeDefinitionIdTerm} and {BrowsePathTerm} and no other member.";
                return false;
            }

            if (string.IsNullOrEmpty(typeDefinitionId))
            {
                error = $"A select clause shall carry {TypeDefinitionIdTerm}, the " +
                    "ExpandedNodeId of the EventType that declares the selected field.";
                return false;
            }
            if (browsePath is null)
            {
                error = $"A select clause shall carry {BrowsePathTerm}; the empty path is " +
                    "written explicitly for the ConditionId selection.";
                return false;
            }
            if (browsePath.Length > 0 && browsePath[0] == '/')
            {
                error = $"The select-clause browse path '{browsePath}' is absolute; a " +
                    "clause path is relative to the clause's " +
                    $"{TypeDefinitionIdTerm}, which anchors it (Section 6.1).";
                return false;
            }
            if (browsePath.Length > 0 &&
                browsePath.Contains("//", StringComparison.Ordinal))
            {
                error = $"The select-clause browse path '{browsePath}' carries an empty " +
                    "element; only the whole path may be empty, which is the ConditionId " +
                    "selection (Section 6.1).";
                return false;
            }
            if (browsePath.Length > 0 && browsePath[browsePath.Length - 1] == '/')
            {
                error = $"The select-clause browse path '{browsePath}' ends with a separator, " +
                    "so its last element is empty (Section 6.1).";
                return false;
            }

            parsed = new WotEventSelectClause(typeDefinitionId!, browsePath);
            error = string.Empty;
            return true;
        }

        private static ArrayOf<WotEventSelectClause> BuildDefault()
        {
            var clauses = new WotEventSelectClause[DefaultFieldNames.Count];
            for (int ii = 0; ii < DefaultFieldNames.Count; ii++)
            {
                clauses[ii] = new WotEventSelectClause(BaseEventTypeId, DefaultFieldNames[ii]);
            }
            return clauses;
        }
    }
}
