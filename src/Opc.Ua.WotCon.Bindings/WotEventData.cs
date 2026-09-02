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
using System.Collections.Immutable;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// One node of the event <c>data</c> object a notification carries, in the
    /// shape the WoT Binding describes it (Sections 6.1 and 13.3): a member is
    /// either a value or an object of further members, and a member name never
    /// contains the browse-path separator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the normative shape. A select clause materializes into exactly
    /// one member of <c>data</c> by a rule that is a function of its normalized
    /// browse path alone: the empty path into <c>ConditionId</c>, a one-element
    /// path into the member of that element's name, and a longer path into the
    /// member named by its last element nested inside an object member for each
    /// preceding element. <c>EnabledState/Id</c> is therefore
    /// <c>data.EnabledState.Id</c> and never a member literally called
    /// <c>EnabledState/Id</c>.
    /// </para>
    /// <para>
    /// <see cref="WotNotification.EventFields"/> is the other representation of
    /// the same notification: a flat index keyed by the joined browse path the
    /// document authored. That index is this runtime's transport-side artifact
    /// - a <c>MonitoredItem</c> returns field values positionally and a runtime
    /// naturally keys them by the clause that asked for them - and it is kept
    /// so a consumer written against it keeps working. Where the two are
    /// compared, the <c>data</c> object is the one the Binding describes.
    /// </para>
    /// </remarks>
    public sealed class WotEventData
    {
        /// <summary>
        /// The empty <c>data</c> object, which is what a property observe
        /// notification carries.
        /// </summary>
        public static WotEventData Empty { get; } = new WotEventData();

        /// <summary>
        /// Initializes an empty object node.
        /// </summary>
        public WotEventData()
        {
            Members = ImmutableDictionary<string, WotEventData>.Empty;
        }

        /// <summary>
        /// Initializes a value node.
        /// </summary>
        /// <param name="value">The field value the notification carried.</param>
        public WotEventData(DataValue value)
        {
            Value = value;
            HasValue = true;
            Members = ImmutableDictionary<string, WotEventData>.Empty;
        }

        /// <summary>
        /// Initializes an object node.
        /// </summary>
        /// <param name="members">The nested members, by member name.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="members"/> is <c>null</c>.
        /// </exception>
        public WotEventData(IReadOnlyDictionary<string, WotEventData> members)
        {
            Members = members ?? throw new ArgumentNullException(nameof(members));
        }

        /// <summary>
        /// Gets whether this node carries a field value, as opposed to nested
        /// members.
        /// </summary>
        public bool HasValue { get; }

        /// <summary>
        /// Gets the field value this node carries, together with its status and
        /// the event's source and server timestamps. The value is null-valued
        /// where <see cref="HasValue"/> is <c>false</c>.
        /// </summary>
        public DataValue Value { get; } = new DataValue();

        /// <summary>
        /// Gets the nested members of this node, by member name. Empty for a
        /// value node.
        /// </summary>
        public IReadOnlyDictionary<string, WotEventData> Members { get; }

        /// <summary>
        /// Gets the member of the given name, or <c>null</c> where this node
        /// has no such member.
        /// </summary>
        /// <param name="name">The member name.</param>
        public WotEventData? this[string name]
        {
            get
            {
                if (name is null)
                {
                    throw new ArgumentNullException(nameof(name));
                }
                return Members.TryGetValue(name, out WotEventData? member) ? member : null;
            }
        }

        /// <summary>
        /// Resolves the value a member path names, which is what a select
        /// clause's <see cref="WotEventSelectClause.MemberPath"/> addresses.
        /// </summary>
        /// <param name="memberPath">The member names, outermost first.</param>
        /// <param name="value">The resolved value.</param>
        /// <returns>
        /// <c>true</c> when the path reaches a member that carries a value.
        /// </returns>
        public bool TryGetValue(ArrayOf<string> memberPath, out DataValue value)
        {
            if (memberPath.IsNull)
            {
                value = new DataValue();
                return false;
            }
            WotEventData current = this;
            for (int ii = 0; ii < memberPath.Count; ii++)
            {
                if (!current.Members.TryGetValue(memberPath[ii], out WotEventData? next))
                {
                    value = new DataValue();
                    return false;
                }
                current = next;
            }
            value = current.Value;
            return current.HasValue;
        }
    }

    /// <summary>
    /// Builds the nested event <c>data</c> object of WoT Binding Sections 6.1
    /// and 13.3 from the select clauses a subscription was created with and the
    /// field values one notification carried.
    /// </summary>
    /// <remarks>
    /// The builder is where the state-Variable rule of Section 6.1 is resolved
    /// for a companion state this Binding does not name: a clause whose field
    /// another clause of the same list reaches through, or which reaches
    /// through a field a clause already selected as a value, makes that field
    /// an object whose <c>Name</c> member carries the state's own localized
    /// display text. Whichever order the two clauses appear in, the same object
    /// results.
    /// </remarks>
    internal sealed class WotEventDataBuilder
    {
        /// <summary>
        /// Adds one field value at the member path a clause materializes into,
        /// reporting whether the value could be placed.
        /// </summary>
        /// <param name="memberPath">The member names, outermost first.</param>
        /// <param name="value">The field value.</param>
        /// <returns>
        /// <c>true</c> when the value was placed, and <c>false</c> when an
        /// earlier clause already filled the member, which is the collision a
        /// planner reports.
        /// </returns>
        public bool Add(ArrayOf<string> memberPath, DataValue value)
        {
            if (memberPath.IsNull || memberPath.Count == 0)
            {
                return false;
            }
            Node current = m_root;
            for (int ii = 0; ii < memberPath.Count - 1; ii++)
            {
                current = current.Descend(memberPath[ii]);
            }
            return current.Place(memberPath[memberPath.Count - 1], value);
        }

        /// <summary>
        /// Builds the immutable <c>data</c> object.
        /// </summary>
        public WotEventData Build()
        {
            return m_root.Build();
        }

        private sealed class Node
        {
            public Node Descend(string name)
            {
                if (!m_members.TryGetValue(name, out Node? child))
                {
                    child = new Node();
                    m_members.Add(name, child);
                    return child;
                }
                if (child.m_hasValue)
                {
                    // Section 6.1: the field is a state Variable after all, so
                    // its own value is that object's Name member and the deeper
                    // clause fills a sibling.
                    child.m_members.Add(
                        WotEventSelectClauses.StateNameMember,
                        new Node { m_hasValue = true, m_value = child.m_value });
                    child.m_hasValue = false;
                    child.m_value = new DataValue();
                }
                return child;
            }

            public bool Place(string name, DataValue value)
            {
                if (!m_members.TryGetValue(name, out Node? child))
                {
                    m_members.Add(name, new Node { m_hasValue = true, m_value = value });
                    return true;
                }
                if (child.m_hasValue)
                {
                    return false;
                }
                if (child.m_members.ContainsKey(WotEventSelectClauses.StateNameMember))
                {
                    return false;
                }
                // The member is already an object, so the value is the state's
                // own display text and belongs in its Name member.
                child.m_members.Add(
                    WotEventSelectClauses.StateNameMember,
                    new Node { m_hasValue = true, m_value = value });
                return true;
            }

            public WotEventData Build()
            {
                if (m_hasValue)
                {
                    return new WotEventData(m_value);
                }
                if (m_members.Count == 0)
                {
                    return WotEventData.Empty;
                }
                ImmutableDictionary<string, WotEventData>.Builder builder =
                    ImmutableDictionary.CreateBuilder<string, WotEventData>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, Node> member in m_members)
                {
                    builder.Add(member.Key, member.Value.Build());
                }
                return new WotEventData(builder.ToImmutable());
            }

            private readonly Dictionary<string, Node> m_members =
                new(StringComparer.Ordinal);
            private bool m_hasValue;
            private DataValue m_value = new();
        }

        private readonly Node m_root = new();
    }
}
