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
    /// Acknowledges a notification only after the application confirmed it. Confirmations
    /// are kept until the acknowledgement ends, so the bookkeeping stays bounded by the
    /// acknowledgements the session still tracks.
    /// </summary>
    /// <param name="next">An application observer that also receives the outcomes.</param>
    internal sealed class ConfirmedAcknowledgementProvider(ISubscriptionAcknowledgementObserver? next) :
        ISubscriptionAcknowledgementProvider,
        ISubscriptionAcknowledgementObserver
    {
        /// <summary>
        /// Gets the number of confirmations that wait for their acknowledgement to end.
        /// </summary>
        public int PendingConfirmations => m_confirmed.Count;

        /// <summary>
        /// Records that the application processed a notification.
        /// </summary>
        /// <param name="handle">The notification's acknowledgement.</param>
        public void Confirm(AcknowledgementHandle handle)
        {
            if (!handle.IsNull)
            {
                m_confirmed.TryAdd(handle, 0);
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
                if (!m_confirmed.ContainsKey(acknowledgements[i]))
                {
                    (deferred ??= []).Add(i);
                }
            }
            return new ValueTask<AcknowledgementDecision>(deferred == null
                ? AcknowledgementDecision.SendAll
                : new AcknowledgementDecision(deferred.ToArray()));
        }

        /// <inheritdoc/>
        public ValueTask OnCompletedAsync(
            ArrayOf<AcknowledgementOutcome> outcomes,
            CancellationToken ct = default)
        {
            HashSet<AcknowledgementScope>? retired = null;
            for (int i = 0; i < outcomes.Count; i++)
            {
                AcknowledgementOutcome outcome = outcomes[i];
                m_confirmed.TryRemove(outcome.Handle, out _);
                if (outcome.Disposition is AcknowledgementDisposition.OwnerRetired or
                    AcknowledgementDisposition.Abandoned &&
                    outcome.Handle.Scope != null)
                {
                    (retired ??= []).Add(outcome.Handle.Scope);
                }
            }
            if (retired != null)
            {
                // Confirmations of a retired subscription lifetime can no longer be used.
                foreach (AcknowledgementHandle confirmed in m_confirmed.Keys)
                {
                    if (confirmed.Scope != null && retired.Contains(confirmed.Scope))
                    {
                        m_confirmed.TryRemove(confirmed, out _);
                    }
                }
            }
            return next?.OnCompletedAsync(outcomes, ct) ?? default;
        }

        private readonly ConcurrentDictionary<AcknowledgementHandle, byte> m_confirmed = new();
    }
}
