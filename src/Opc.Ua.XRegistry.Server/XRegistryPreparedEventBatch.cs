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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// A fully constructed native notification batch. Dispose before publication
    /// to discard it; report once only after the associated registry commit is known.
    /// Reporting failure cannot undo that commit and must not be retried blindly.
    /// </summary>
    public sealed class XRegistryPreparedEventBatch : IDisposable
    {
        internal XRegistryPreparedEventBatch(ISystemContext context, ArrayOf<Entry> entries)
        {
            m_context = context;
            m_entries = entries;
            Count = entries.Count;
        }

        /// <summary>
        /// Gets the number of coalesced notifications prepared before publication.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Reports the prepared events once. All event construction and validation
        /// has completed before this method is called.
        /// </summary>
        /// <exception cref="InvalidOperationException">The batch was already reported or discarded.</exception>
        public async ValueTask ReportAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref m_consumed, 1, 0) != 0)
            {
                throw new InvalidOperationException("The notification batch was already reported or discarded.");
            }
            ArrayOf<Entry> events = m_entries;
            try
            {
                for (int index = 0; index < events.Count; index++)
                {
                    Entry entry = events[index];
                    await entry.Notifier.ReportEventAsync(m_context, entry.Event, cancellationToken).ConfigureAwait(
                        false);
                }
            }
            finally
            {
                m_entries = default;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref m_consumed, 2, 0) == 0)
            {
                m_entries = default;
            }
        }

        internal sealed record Entry(NodeState Notifier, BaseEventState Event);

        private readonly ISystemContext m_context;
        private ArrayOf<Entry> m_entries;
        private int m_consumed;
    }
}
