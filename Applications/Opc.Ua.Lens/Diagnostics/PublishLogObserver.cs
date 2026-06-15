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
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;

namespace UaLens.Diagnostics;

/// <summary>
/// Classification of a single publish-message entry surfaced in the
/// diagnostics pane.  Mirrors the broad <c>NotificationData</c> categories
/// the OPC UA stack delivers per publish callback.
/// </summary>
internal enum PublishLogKind
{
    /// <summary>Notification carried a <c>DataChangeNotification</c>.</summary>
    Data,
    /// <summary>Notification carried an <c>EventNotificationList</c>.</summary>
    Event,
    /// <summary>Notification was a keep-alive (no <c>NotificationData</c>).</summary>
    KeepAlive,
    /// <summary>
    /// Single publish message carried both data and event notifications.
    /// Detected by merging consecutive entries that share
    /// <see cref="PublishLogEntry.SubscriptionId"/> +
    /// <see cref="PublishLogEntry.SequenceNumber"/>.
    /// </summary>
    Mixed
}

/// <summary>
/// One row in the <see cref="PublishLogObserver.Entries"/> table — a
/// faithful record of a single publish-message arrival on a subscription.
/// </summary>
internal sealed record PublishLogEntry(
    DateTime ReceivedAtLocal,
    uint SubscriptionId,
    uint SequenceNumber,
    DateTime PublishTimeUtc,
    int NotifCount,
    PublishLogKind Kind)
{
    /// <summary>Local wall-clock time the publish callback was entered.</summary>
    public string TimeText
        => ReceivedAtLocal.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>Server-side publish time, formatted as UTC.</summary>
    public string PublishTimeText
        => PublishTimeUtc.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public string SubscriptionText
        => SubscriptionId.ToString(CultureInfo.InvariantCulture);

    public string SequenceText
        => SequenceNumber.ToString(CultureInfo.InvariantCulture);

    public string NotifCountText
        => NotifCount.ToString(CultureInfo.InvariantCulture);

    public string KindText => Kind.ToString();
}

/// <summary>
/// Singleton sink for low-level publish-message metadata, surfaced as a
/// "Publishes" sub-tab in <c>DiagnosticsView</c>.  Each registered adapter
/// (<c>ChannelV2EngineAdapter</c> / <c>ClassicEngineAdapter</c>) appends
/// one entry per publish callback; the observer dedupes consecutive
/// callbacks that share <c>(SubscriptionId, SequenceNumber)</c> into a
/// single row with <see cref="PublishLogKind.Mixed"/> when both data and
/// event payloads ride the same message.
/// </summary>
/// <remarks>
/// Collection mutations are marshalled onto the Avalonia UI thread via
/// <see cref="Dispatcher.UIThread"/> so bound list controls receive
/// in-order <c>CollectionChanged</c> notifications.  The collection is
/// capped at <see cref="MaxEntries"/> rows; once full, oldest entries
/// are evicted FIFO before each append.
/// </remarks>
internal sealed class PublishLogObserver
{
    /// <summary>Maximum number of rows retained.  Older rows are dropped FIFO.</summary>
    public const int MaxEntries = 500;

    /// <summary>Live, observable collection bound by the diagnostics view.</summary>
    public ObservableCollection<PublishLogEntry> Entries { get; } = new();

    /// <summary>
    /// Records one publish callback.  Safe to invoke from any thread —
    /// the collection mutation is queued onto the UI dispatcher.
    /// </summary>
    public void Record(
        uint subscriptionId,
        uint sequenceNumber,
        DateTime publishTimeUtc,
        int notifCount,
        PublishLogKind kind)
    {
        DateTime nowLocal = DateTime.UtcNow.ToLocalTime();
        var entry = new PublishLogEntry(
            nowLocal,
            subscriptionId,
            sequenceNumber,
            publishTimeUtc,
            notifCount,
            kind);
        Dispatcher.UIThread.Post(() => AddOrMerge(entry));
    }

    /// <summary>
    /// Clears every recorded entry.  Hooked by the diagnostics view's
    /// optional "Clear" button (kept here for callers that want to drop
    /// stale rows on reconnect).
    /// </summary>
    public void Clear()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Entries.Clear();
        }
        else
        {
            Dispatcher.UIThread.Post(Entries.Clear);
        }
    }

    private void AddOrMerge(PublishLogEntry entry)
    {
        if (Entries.Count > 0)
        {
            int lastIndex = Entries.Count - 1;
            PublishLogEntry last = Entries[lastIndex];
            if (last.SubscriptionId == entry.SubscriptionId
                && last.SequenceNumber == entry.SequenceNumber
                && last.Kind != PublishLogKind.KeepAlive
                && entry.Kind != PublishLogKind.KeepAlive)
            {
                PublishLogKind merged = MergeKind(last.Kind, entry.Kind);
                Entries[lastIndex] = last with
                {
                    NotifCount = last.NotifCount + entry.NotifCount,
                    Kind = merged
                };
                return;
            }
        }

        Entries.Add(entry);
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }
    }

    private static PublishLogKind MergeKind(PublishLogKind a, PublishLogKind b)
    {
        if (a == b)
        {
            return a;
        }
        if (a == PublishLogKind.Mixed || b == PublishLogKind.Mixed)
        {
            return PublishLogKind.Mixed;
        }
        if ((a == PublishLogKind.Data && b == PublishLogKind.Event)
            || (a == PublishLogKind.Event && b == PublishLogKind.Data))
        {
            return PublishLogKind.Mixed;
        }
        return b;
    }
}
