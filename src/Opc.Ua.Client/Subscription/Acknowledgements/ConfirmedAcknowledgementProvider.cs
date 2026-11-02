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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Acknowledges a notification only after the application confirmed it. The subscription
    /// engines track an acknowledgement before its notification is delivered, and its terminal
    /// outcome synchronously ends the tracking. Only tracked acknowledgements can be confirmed, so a late or
    /// repeated confirmation has no effect and the state stays bounded by the acknowledgements
    /// the session still holds.
    /// </summary>
    internal sealed class ConfirmedAcknowledgementProvider :
        ISubscriptionAcknowledgementProvider,
        IAcknowledgementTracker
    {
        /// <summary>
        /// Gets the number of tracked acknowledgements that have not ended yet.
        /// </summary>
        public int TrackedAcknowledgements => m_tracked.Count;

        /// <summary>
        /// Gets the number of tracked acknowledgements the application confirmed.
        /// </summary>
        public int PendingConfirmations
        {
            get
            {
                int confirmed = 0;
                foreach (KeyValuePair<AcknowledgementHandle, bool> entry in m_tracked)
                {
                    if (entry.Value)
                    {
                        confirmed++;
                    }
                }
                return confirmed;
            }
        }

        /// <inheritdoc/>
        public void Track(AcknowledgementHandle handle)
        {
            if (!handle.IsNull)
            {
                m_tracked.TryAdd(handle, false);
            }
        }

        /// <inheritdoc/>
        public void Complete(AcknowledgementHandle handle)
        {
            m_tracked.TryRemove(handle, out _);
        }

        /// <inheritdoc/>
        public bool Release(AcknowledgementHandle handle)
        {
            return m_tracked.TryRemove(handle, out bool confirmed) && confirmed;
        }

        /// <summary>
        /// Records that the application processed a notification. Has no effect unless the
        /// acknowledgement is tracked and has not ended.
        /// </summary>
        /// <param name="handle">The notification's acknowledgement.</param>
        public void Confirm(AcknowledgementHandle handle)
        {
            if (!handle.IsNull)
            {
                // Atomic with the removal on completion: an ended acknowledgement is not revived.
                m_tracked.TryUpdate(handle, true, false);
            }
        }

        /// <inheritdoc/>
        public ValueTask<AcknowledgementDecision> DecideAsync(
            ArrayOf<AcknowledgementHandle> acknowledgements,
            CancellationToken ct = default)
        {
            List<int>? deferred = null;
            for (int i = 0; i < acknowledgements.Count; i++)
            {
                if (!m_tracked.TryGetValue(acknowledgements[i], out bool confirmed) || !confirmed)
                {
                    (deferred ??= []).Add(i);
                }
            }
            return new ValueTask<AcknowledgementDecision>(deferred == null
                ? AcknowledgementDecision.SendAll
                : new AcknowledgementDecision(deferred.ToArray()));
        }

        private readonly ConcurrentDictionary<AcknowledgementHandle, bool> m_tracked = new();
    }
}
