// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Brings an event that was queued before a modification of its monitored item in line
    /// with that modification when the event is published.
    /// </summary>
    /// <remarks>
    /// Transforms of successive modifications are combined with <see cref="Then"/>. The
    /// combination applies them one after the other in a loop, so the number of
    /// modifications never adds stack depth. A transform that knows it supersedes the
    /// previous ones overrides <see cref="Then"/> and returns a single transform.
    /// </remarks>
    internal abstract class QueuedEventTransform
    {
        /// <summary>
        /// Produces the replacement for a queued event.
        /// </summary>
        /// <returns>The replacement, or <c>null</c> when the event cannot be expressed
        /// any more and is dropped.</returns>
        public abstract QueuedEvent? Apply(QueuedEvent fields);

        /// <summary>
        /// The number of transforms applied in sequence by <see cref="Apply"/>.
        /// </summary>
        public virtual int StepCount => 1;

        /// <summary>
        /// Returns the transform that applies this transform and then <paramref name="next"/>.
        /// </summary>
        /// <remarks>
        /// The result may be this instance updated in place: a transform is owned by one
        /// segment of queued events, and every combination replaces it.
        /// </remarks>
        public virtual QueuedEventTransform Then(QueuedEventTransform next)
        {
            var sequence = new Sequence();
            sequence.Append(this);
            sequence.Append(next);
            return sequence;
        }

        /// <summary>
        /// Transforms applied one after the other.
        /// </summary>
        private sealed class Sequence : QueuedEventTransform
        {
            public override int StepCount => m_steps.Count;

            public void Append(QueuedEventTransform transform)
            {
                if (transform is Sequence sequence)
                {
                    m_steps.AddRange(sequence.m_steps);
                }
                else
                {
                    m_steps.Add(transform);
                }
            }

            public override QueuedEvent? Apply(QueuedEvent fields)
            {
                QueuedEvent? current = fields;
                for (int ii = 0; ii < m_steps.Count && current != null; ii++)
                {
                    current = m_steps[ii].Apply(current.Value);
                }
                return current;
            }

            public override QueuedEventTransform Then(QueuedEventTransform next)
            {
                // a step that supersedes the last one replaces it.
                int last = m_steps.Count - 1;
                QueuedEventTransform combined = m_steps[last].Then(next);
                if (combined is not Sequence)
                {
                    m_steps[last] = combined;
                }
                else
                {
                    Append(next);
                }
                return this;
            }

            private readonly List<QueuedEventTransform> m_steps = [];
        }
    }
}
