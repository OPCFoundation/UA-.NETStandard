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
            EventPropagationFrame frame;
            if (previous == null || !previous.IsActive || !ReferenceEquals(previous.Event, e))
            {
                frame = new EventPropagationFrame(e);
                s_eventPropagation.Value = frame;
                scope = new EventPropagationScope(frame, previous);
            }
            else
            {
                frame = previous;
                scope = EmptyEventPropagationScope.Instance;
            }
            return frame.TryVisit(this);
        }

        /// <summary>
        /// Owns only the temporary visited set of one publication, never a process-wide event history.
        /// </summary>
        private sealed class EventPropagationFrame
        {
            /// <summary>
            /// Creates the visited set for one event publication.
            /// </summary>
            public EventPropagationFrame(IFilterTarget e)
            {
                Event = e;
            }

            /// <summary>
            /// Gets the event whose nested propagation shares this scope.
            /// </summary>
            public IFilterTarget Event { get; }

            /// <summary>
            /// Gets whether its top-level publisher is still active.
            /// </summary>
            public bool IsActive => Volatile.Read(ref m_active);

            /// <summary>
            /// Marks one node before invoking any of its callbacks.
            /// </summary>
            public bool TryVisit(NodeState node)
            {
                lock (m_gate)
                {
                    return m_nodes.Add(node);
                }
            }

            /// <summary>
            /// Releases node references when the publication ends.
            /// </summary>
            public void Close()
            {
                lock (m_gate)
                {
                    Volatile.Write(ref m_active, false);
                    m_nodes.Clear();
                }
            }

            private readonly Lock m_gate = new();
            private readonly HashSet<NodeState> m_nodes = new(new NodeIdentityComparer());
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
