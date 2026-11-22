// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Opc.Ua
{
    public abstract partial class NodeState
    {
        /// <summary>
        /// Visits this node once within one nested event publication, keeping parent and inverse-notifier paths together.
        /// Independent publications, including reuse of the same event instance, receive independent scopes.
        /// </summary>
        protected bool TryBeginEventReport(IFilterTarget e, out IDisposable scope)
        {
            EventPropagationFrame? previous = s_eventPropagation.Value;
            if (previous is not null && previous.TryJoin(e, this, out bool firstVisit))
            {
                scope = EmptyEventPropagationScope.Instance;
                return firstVisit;
            }

            var frame = new EventPropagationFrame(e, this);
            s_eventPropagation.Value = frame;
            scope = new EventPropagationScope(frame, previous);
            return true;
        }

        /// <summary>
        /// Owns only the temporary visited set of one publication, never a process-wide event history.
        /// </summary>
        private sealed class EventPropagationFrame
        {
            /// <summary>
            /// Creates the visited set with the first source of one event publication.
            /// </summary>
            public EventPropagationFrame(IFilterTarget e, NodeState source)
            {
                m_event = e;
                m_nodes.Add(source);
            }

            /// <summary>
            /// Joins only the same active occurrence and marks a source atomically with publication closure.
            /// </summary>
            public bool TryJoin(IFilterTarget e, NodeState node, out bool firstVisit)
            {
                lock (m_gate)
                {
                    firstVisit = false;
                    if (!m_active || !ReferenceEquals(m_event, e))
                    {
                        return false;
                    }
                    firstVisit = m_nodes.Add(node);
                    return true;
                }
            }

            /// <summary>
            /// Releases node references when the publication ends.
            /// </summary>
            public void Close()
            {
                lock (m_gate)
                {
                    m_active = false;
                    m_nodes.Clear();
                    m_event = null;
                }
            }

            private readonly Lock m_gate = new();
            private readonly HashSet<NodeState> m_nodes = new(new NodeIdentityComparer());

            /// <summary>The occurrence retained only while this publication remains active.</summary>
            private IFilterTarget? m_event;

            private bool m_active = true;
        }

        /// <summary>
        /// Restores a nested publication's caller scope on success, cancellation or a failed event sink.
        /// </summary>
        private sealed class EventPropagationScope : IDisposable
        {
            /// <summary>
            /// Captures the active publication and the caller's prior scope.
            /// </summary>
            public EventPropagationScope(EventPropagationFrame current, EventPropagationFrame? previous)
            {
                m_current = current;
                m_previous = previous;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_current.Close();
                s_eventPropagation.Value = m_previous;
            }

            private readonly EventPropagationFrame m_current;
            private readonly EventPropagationFrame? m_previous;
        }

        /// <summary>
        /// Shares an existing publication without ending its caller's scope.
        /// </summary>
        private sealed class EmptyEventPropagationScope : IDisposable
        {
            /// <summary>
            /// Gets the shared non-owning scope.
            /// </summary>
            public static EmptyEventPropagationScope Instance { get; } = new();

            /// <inheritdoc/>
            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Uses node object identity rather than potentially reused or unassigned NodeIds.
        /// </summary>
        private sealed class NodeIdentityComparer : IEqualityComparer<NodeState>
        {
            /// <inheritdoc/>
            public bool Equals(NodeState? x, NodeState? y)
            {
                return ReferenceEquals(x, y);
            }

            /// <inheritdoc/>
            public int GetHashCode(NodeState obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static readonly AsyncLocal<EventPropagationFrame?> s_eventPropagation = new();
    }
}
