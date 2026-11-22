// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
