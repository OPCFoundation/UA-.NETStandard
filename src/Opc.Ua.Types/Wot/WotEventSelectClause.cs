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
        /// Gets the name the selected field carries in the event <c>data</c>
        /// object: the last element of the browse path, or
        /// <c>ConditionId</c> for the empty path (WoT Binding Sections 6.1 and
        /// 13.3).
        /// </summary>
        public string FieldName
        {
            get
            {
                if (IsConditionIdSelection)
                {
                    return WotEventSelectClauses.ConditionIdFieldName;
                }
                int separator = BrowsePath.LastIndexOf('/');
                return separator < 0 || separator + 1 >= BrowsePath.Length
                    ? BrowsePath
                    : BrowsePath.Substring(separator + 1);
            }
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
            var seen = new HashSet<string>(StringComparer.Ordinal);
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
                if (!seen.Add(entry!.ToString()))
                {
                    error = $"The clause ({entry.TypeDefinitionId}, '{entry.BrowsePath}') " +
                        "appears twice; the same clause shall not be selected twice.";
                    return false;
                }
                parsed.Add(entry);
            }

            clauses = parsed.ToArray();
            error = string.Empty;
            errorIndex = -1;
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
