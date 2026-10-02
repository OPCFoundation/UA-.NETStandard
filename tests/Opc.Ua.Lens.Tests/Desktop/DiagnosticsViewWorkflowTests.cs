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
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using UaLens.Diagnostics;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class DiagnosticsViewWorkflowTests
{
    [Test]
    public Task ControlledLogBindingAndDisconnectedStatusDisplayExactDetails()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            var log = new PublishLogObserver(action => action());
            var view = new DiagnosticsView();
            int hidden = 0;
            view.HideRequested += () => hidden++;
            view.BindPublishLog(log);
            log.Record(73, 11, new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), 3, PublishLogKind.Data);
            view.Bind(null);

            ListBox list = DesktopInteraction.Control<ListBox>(view, "PublishList");
            Assert.That(list.ItemsSource, Is.SameAs(log.Entries));
            var entry = (PublishLogEntry)list.Items.Single()!;
            Assert.That(entry.SubscriptionText, Is.EqualTo("73"));
            Assert.That(entry.SequenceText, Is.EqualTo("11"));
            Assert.That(entry.PublishTimeText, Is.EqualTo("05:06:07.000"));
            Assert.That(entry.NotifCountText, Is.EqualTo("3"));
            Assert.That(entry.KindText, Is.EqualTo("Data"));
            Assert.That(view.Rows.Select(row => row.Value), Is.All.EqualTo("—"));
            Assert.That(view.Rows.First().Name, Is.EqualTo("ServerStatus.StartTime"));
            Assert.That(view.Rows.Last().Name, Is.EqualTo("Limits.MaxMonitoredItemsPerCall"));
            Assert.That(view.ClientRows.Single(row => row.Name == "Session").Value,
                Is.EqualTo("Unavailable (not connected)."));
            Assert.That(view.ClientRows.Single(row => row.Name == "Publish display drops").Value, Is.EqualTo("0"));
            Assert.That(DesktopInteraction.Control<TextBlock>(view, "StatusLabel").Text, Is.EqualTo("(disconnected)"));
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(view, "HideButton"));
            Assert.That(hidden, Is.EqualTo(1));
            Assert.That(log.Entries.Single().SequenceNumber, Is.EqualTo(11));
            return Task.CompletedTask;
        });
    }

    [Test]
    public Task RebindAndDetachKeepOldPublishSourceOutOfCurrentView()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            var first = new PublishLogObserver(action => action());
            var second = new PublishLogObserver(action => action());
            var view = new DiagnosticsView();
            DesktopInteraction.Owner.Content = view;
            try
            {
                view.BindPublishLog(first);
                first.Record(7, 1, DateTime.MinValue, 1, PublishLogKind.Data);
                view.BindPublishLog(second);
                view.BindPublishLog(second);
                second.Record(8, 22, DateTime.MinValue, 2, PublishLogKind.Event);
                first.Record(7, 2, DateTime.MinValue, 9, PublishLogKind.Data);
                ListBox list = DesktopInteraction.Control<ListBox>(view, "PublishList");
                Assert.That(list.Items.Cast<PublishLogEntry>().Select(entry => entry.SequenceNumber),
                    Is.EqualTo(new uint[] { 22 }));
                Assert.That(list.Items.Cast<PublishLogEntry>().Single().Kind, Is.EqualTo(PublishLogKind.Event));
                DesktopInteraction.Owner.Content = null;
                view.Bind(null);
                Assert.That(view.ClientRows.Single(row => row.Name == "Publish display evictions").Value,
                    Is.EqualTo("0"));
                Assert.That(list.ItemsSource, Is.SameAs(second.Entries));
                Assert.That(first.Entries, Has.Count.EqualTo(2));
                Assert.That(DesktopInteraction.Control<TextBlock>(view, "StatusLabel").Text,
                    Is.EqualTo("(disconnected)"));
            }
            finally
            {
                DesktopInteraction.Owner.Content = null;
                view.Bind(null);
            }
            return Task.CompletedTask;
        });
    }
}
