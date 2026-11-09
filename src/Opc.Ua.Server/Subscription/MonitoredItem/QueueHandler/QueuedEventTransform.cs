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
        public abstract EventFieldList? Apply(EventFieldList fields);

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

            public override EventFieldList? Apply(EventFieldList fields)
            {
                EventFieldList? current = fields;
                for (int ii = 0; ii < m_steps.Count && current != null; ii++)
                {
                    current = m_steps[ii].Apply(current);
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
