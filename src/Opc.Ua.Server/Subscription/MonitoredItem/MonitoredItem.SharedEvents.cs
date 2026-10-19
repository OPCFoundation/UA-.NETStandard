/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Evaluation of a reported event that is shared by the event monitored items with an
    /// equal filter.
    /// </summary>
    /// <remarks>
    /// The where clause result and the select clause values of an event depend only on the
    /// event, the filter and the preferred locales of the session; the server tables in the
    /// filter context are the same for every item. Items with an equal filter therefore get
    /// the same result, which the event consumer of a notifier computes once per event and
    /// hands to every item of the group. What stays per item: the duplicate check, the
    /// permission check, filtered retain, the queue (overflow, size) and the
    /// <see cref="EventFieldList"/> with the item's client handle.
    /// </remarks>
    public partial class MonitoredItem
    {
        /// <summary>
        /// Returns the key of the group of items that can share the evaluation of an event
        /// with this item, or <c>null</c> when the item has no event filter to group by.
        /// </summary>
        internal SharedEventFilterKey? GetSharedEventFilterKey()
        {
            lock (m_lock)
            {
                if (m_eventQueueHandler == null || FilterToUse is not EventFilter filter)
                {
                    return null;
                }

                string[]? preferredLocales = m_subscription?.Session?.PreferredLocales;
                SharedEventFilterKey? key = m_sharedEventFilterKey;
                if (key == null || !key.Matches(filter, preferredLocales))
                {
                    key = new SharedEventFilterKey(filter, preferredLocales);
                    m_sharedEventFilterKey = key;
                }
                return key;
            }
        }

        /// <summary>
        /// Adds an event to the queue through <see cref="QueueEvent(IFilterTarget)"/>, which
        /// takes the where clause result and the field values from the evaluation shared by
        /// the items of the group <paramref name="key"/>.
        /// </summary>
        /// <remarks>
        /// The evaluation is offered to this class's implementation of
        /// <see cref="QueueEvent(IFilterTarget, bool)"/> for the duration of the call, so a
        /// derived item that overrides how events are queued still sees every event, and
        /// uses the evaluation only when it hands the same event on to this class. An item
        /// whose filter or session locales changed since the event was grouped evaluates
        /// the event itself.
        /// </remarks>
        internal void QueueSharedEvent(
            IFilterTarget instance,
            SharedEventFilterKey key,
            SharedEventEvaluation evaluation)
        {
            SharedEventScope previous = t_sharedEvent;
            t_sharedEvent = new SharedEventScope(this, instance, key, evaluation);
            try
            {
                QueueEvent(instance);
            }
            finally
            {
                t_sharedEvent = previous;
            }
        }

        /// <summary>
        /// Takes the shared evaluation offered by <see cref="QueueSharedEvent"/> for
        /// <paramref name="instance"/> queued to this item, if any.
        /// </summary>
        private SharedEventEvaluation? TakeSharedEvaluation(
            IFilterTarget instance,
            out SharedEventFilterKey? key)
        {
            SharedEventScope scope = t_sharedEvent;
            if (!ReferenceEquals(scope.Item, this) || !ReferenceEquals(scope.Instance, instance))
            {
                key = null;
                return null;
            }

            t_sharedEvent = default;
            key = scope.Key;
            return scope.Evaluation;
        }

        /// <summary>
        /// A shared evaluation offered to one item for one event.
        /// </summary>
        private readonly struct SharedEventScope
        {
            public SharedEventScope(
                MonitoredItem item,
                IFilterTarget instance,
                SharedEventFilterKey key,
                SharedEventEvaluation evaluation)
            {
                Item = item;
                Instance = instance;
                Key = key;
                Evaluation = evaluation;
            }

            public MonitoredItem? Item { get; }

            public IFilterTarget? Instance { get; }

            public SharedEventFilterKey? Key { get; }

            public SharedEventEvaluation? Evaluation { get; }
        }

        [ThreadStatic]
        private static SharedEventScope t_sharedEvent;
        private SharedEventFilterKey? m_sharedEventFilterKey;

        /// <summary>
        /// The filter and preferred locales an event monitored item evaluates events with,
        /// compared by value.
        /// </summary>
        /// <remarks>
        /// Every item has its own filter instance, so equal filters are told apart by value.
        /// A key remembers the key it was found equal to, so after the first event the
        /// keys of a group compare by reference.
        /// </remarks>
        internal sealed class SharedEventFilterKey : IEquatable<SharedEventFilterKey>
        {
            /// <summary>
            /// Creates the key of <paramref name="filter"/> evaluated for a session with
            /// <paramref name="preferredLocales"/>.
            /// </summary>
            public SharedEventFilterKey(EventFilter filter, string[]? preferredLocales)
            {
                Filter = filter;
                PreferredLocales = preferredLocales;

                var hash = new HashCode();
                hash.Add(filter);
                if (preferredLocales != null)
                {
                    foreach (string locale in preferredLocales)
                    {
                        hash.Add(locale, StringComparer.Ordinal);
                    }
                }
                m_hashCode = hash.ToHashCode();
            }

            /// <summary>
            /// The filter the key was created for.
            /// </summary>
            public EventFilter Filter { get; }

            /// <summary>
            /// The preferred locales the key was created for.
            /// </summary>
            public string[]? PreferredLocales { get; }

            /// <summary>
            /// Whether the key was created for these instances of the filter and locales.
            /// </summary>
            public bool Matches(EventFilter filter, string[]? preferredLocales)
            {
                return ReferenceEquals(Filter, filter) &&
                    ReferenceEquals(PreferredLocales, preferredLocales);
            }

            /// <inheritdoc/>
            public bool Equals(SharedEventFilterKey? other)
            {
                if (ReferenceEquals(this, other))
                {
                    return true;
                }

                if (other is null || m_hashCode != other.m_hashCode)
                {
                    return false;
                }

                SharedEventFilterKey representative = Representative;
                if (ReferenceEquals(representative, other.Representative))
                {
                    return true;
                }

                if (!LocalesEqual(PreferredLocales, other.PreferredLocales) ||
                    !Filter.IsEqual(other.Filter))
                {
                    return false;
                }

                // a dictionary compares the key it holds with the key it is asked for, so
                // the asked key adopts the representative of the held one: the held key of
                // a group stays stable and every other key of the group compares by
                // reference from the next event on.
                Volatile.Write(ref other.m_equivalent, representative);
                return true;
            }

            /// <summary>
            /// The key this key compares by reference with, after it was found equal to it.
            /// </summary>
            internal SharedEventFilterKey Representative => Volatile.Read(ref m_equivalent) ?? this;

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return Equals(obj as SharedEventFilterKey);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return m_hashCode;
            }

            private static bool LocalesEqual(string[]? left, string[]? right)
            {
                if (ReferenceEquals(left, right))
                {
                    return true;
                }

                if (left == null || right == null || left.Length != right.Length)
                {
                    return false;
                }

                for (int ii = 0; ii < left.Length; ii++)
                {
                    if (!string.Equals(left[ii], right[ii], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                return true;
            }

            private readonly int m_hashCode;
            private SharedEventFilterKey? m_equivalent;
        }

        /// <summary>
        /// The evaluation of one event for one group of items with an equal filter. The
        /// where clause and the select clauses are evaluated when the first item needs them;
        /// the items can queue the event on different threads.
        /// </summary>
        internal sealed class SharedEventEvaluation
        {
            /// <summary>
            /// Returns whether the event passes the where clause of the group.
            /// </summary>
            public bool PassesWhereClause(
                IFilterContext context,
                EventFilter filter,
                IFilterTarget instance)
            {
                int state = Volatile.Read(ref m_whereClause);
                if (state == 0)
                {
                    lock (m_lock)
                    {
                        state = m_whereClause;
                        if (state == 0)
                        {
                            state = filter.WhereClause.Evaluate(context, instance) ? 1 : -1;
                            Volatile.Write(ref m_whereClause, state);
                        }
                    }
                }
                return state > 0;
            }

            /// <summary>
            /// Returns the field values of the event for the select clauses of the group.
            /// The array is shared by the items of the group and must not be modified.
            /// </summary>
            public Variant[] GetEventFields(
                MonitoredItem item,
                FilterContext context,
                EventFilter filter,
                IFilterTarget instance)
            {
                Variant[]? fields = Volatile.Read(ref m_fields);
                if (fields == null)
                {
                    lock (m_lock)
                    {
                        fields = m_fields;
                        if (fields == null)
                        {
                            fields = item.EvaluateEventFields(context, filter, instance);
                            Volatile.Write(ref m_fields, fields);
                        }
                    }
                }
                return fields;
            }

            private readonly Lock m_lock = new();
            private int m_whereClause;
            private Variant[]? m_fields;
        }
    }
}
