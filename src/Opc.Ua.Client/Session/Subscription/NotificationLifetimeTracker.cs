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
 *
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
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Tracks the notification lifetimes of the acknowledgement objects an engine created until
    /// each reaches a terminal disposition. Shared by the classic and the V2 engine.
    /// </summary>
    /// <remarks>
    /// Several acknowledgement objects can confirm the same message (a re-ack after a transfer or
    /// for an outdated message). They share one lifetime entry, which is reported once: on the
    /// first confirmation, otherwise when its last object ends. A bounded history of reported
    /// lifetimes suppresses late duplicates. Not thread-safe: callers serialize access.
    /// </remarks>
    internal sealed class NotificationLifetimeTracker
    {
        /// <summary>
        /// Gets the number of tracked acknowledgement objects.
        /// </summary>
        public int Count => m_tracked.Count;

        /// <summary>
        /// Gets the tracked acknowledgement objects.
        /// </summary>
        public IEnumerable<KeyValuePair<SubscriptionAcknowledgement, TrackedAcknowledgement>> Objects => m_tracked;

        /// <summary>
        /// Tracks an acknowledgement object under its lifetime. A lifetime that was already
        /// reported is not tracked again.
        /// </summary>
        public void Track(SubscriptionAcknowledgement acknowledgement, NotificationLifetime lifetime)
        {
            if (lifetime.IsNull || m_reported.Contains(lifetime))
            {
                return;
            }
            if (!m_entries.TryGetValue(lifetime, out LifetimeEntry? entry))
            {
                entry = new LifetimeEntry(lifetime);
                m_entries.Add(lifetime, entry);
            }
            if (!m_tracked.ContainsKey(acknowledgement))
            {
                entry.Objects++;
                m_tracked.Add(acknowledgement, new TrackedAcknowledgement(entry));
            }
        }

        /// <summary>
        /// Gets the tracking state of an acknowledgement object.
        /// </summary>
        public bool TryGet(
            SubscriptionAcknowledgement acknowledgement,
            [NotNullWhen(true)] out TrackedAcknowledgement? tracked)
        {
            if (m_tracked.Count == 0)
            {
                tracked = null;
                return false;
            }
            return m_tracked.TryGetValue(acknowledgement, out tracked);
        }

        /// <summary>
        /// Gets the lifetime of a tracked acknowledgement object.
        /// </summary>
        public bool TryGetLifetime(SubscriptionAcknowledgement acknowledgement, out NotificationLifetime lifetime)
        {
            if (TryGet(acknowledgement, out TrackedAcknowledgement? tracked))
            {
                lifetime = tracked.Entry.Lifetime;
                return true;
            }
            lifetime = default;
            return false;
        }

        /// <summary>
        /// Ends the tracking of an acknowledgement object and reports its lifetime once: on the
        /// first confirmation, or when its last object ends.
        /// </summary>
        /// <returns>Whether the object was tracked and in flight.</returns>
        public bool Terminate(
            SubscriptionAcknowledgement acknowledgement,
            NotificationDisposition disposition,
            ref List<NotificationTermination>? terminations)
        {
            if (!TryGet(acknowledgement, out TrackedAcknowledgement? tracked))
            {
                return false;
            }
            m_tracked.Remove(acknowledgement);
            LifetimeEntry entry = tracked.Entry;
            entry.Objects--;
            if (!entry.Reported && (disposition == NotificationDisposition.Acknowledged || entry.Objects == 0))
            {
                entry.Reported = true;
                (terminations ??= []).Add(new NotificationTermination(entry.Lifetime, disposition));
                RememberReported(entry.Lifetime);
            }
            if (entry.Objects == 0)
            {
                m_entries.Remove(entry.Lifetime);
            }
            return tracked.InFlight;
        }

        /// <summary>
        /// Ends every tracked lifetime as owner retired, reporting each unreported one once.
        /// </summary>
        public void RetireAll(ref List<NotificationTermination>? terminations)
        {
            foreach (LifetimeEntry entry in m_entries.Values)
            {
                if (!entry.Reported)
                {
                    (terminations ??= []).Add(
                        new NotificationTermination(entry.Lifetime, NotificationDisposition.OwnerRetired));
                    RememberReported(entry.Lifetime);
                }
            }
            m_tracked.Clear();
            m_entries.Clear();
        }

        /// <summary>
        /// Remembers a reported lifetime for a bounded time.
        /// </summary>
        private void RememberReported(NotificationLifetime lifetime)
        {
            if (!m_reported.Add(lifetime))
            {
                return;
            }
            m_reportedOrder.Enqueue(lifetime);
            if (m_reportedOrder.Count > kReportedLifetimeHistory)
            {
                m_reported.Remove(m_reportedOrder.Dequeue());
            }
        }

        /// <summary>
        /// The number of reported lifetimes remembered to suppress late duplicates.
        /// </summary>
        private const int kReportedLifetimeHistory = 1024;

        private readonly Dictionary<SubscriptionAcknowledgement, TrackedAcknowledgement> m_tracked =
            new(RefEqualityComparer.Default);
        private readonly Dictionary<NotificationLifetime, LifetimeEntry> m_entries = [];
        private readonly HashSet<NotificationLifetime> m_reported = [];
        private readonly Queue<NotificationLifetime> m_reportedOrder = new();
    }

    /// <summary>
    /// The send state of one tracked acknowledgement object.
    /// </summary>
    internal sealed class TrackedAcknowledgement(LifetimeEntry entry)
    {
        /// <summary>
        /// Gets the lifetime entry of the acknowledged message.
        /// </summary>
        public LifetimeEntry Entry { get; } = entry;

        /// <summary>
        /// Gets or sets whether a Publish request carries the acknowledgement.
        /// </summary>
        public bool InFlight { get; set; }

        /// <summary>
        /// Gets or sets the last sweep that found the acknowledgement pending.
        /// </summary>
        public int Mark { get; set; }
    }

    /// <summary>
    /// One tracked notification lifetime and the acknowledgement objects that confirm it.
    /// </summary>
    internal sealed class LifetimeEntry(NotificationLifetime lifetime)
    {
        /// <summary>
        /// Gets the lifetime.
        /// </summary>
        public NotificationLifetime Lifetime { get; } = lifetime;

        /// <summary>
        /// Gets or sets the number of tracked acknowledgement objects.
        /// </summary>
        public int Objects { get; set; }

        /// <summary>
        /// Gets or sets whether the end of the lifetime was reported.
        /// </summary>
        public bool Reported { get; set; }
    }
}
