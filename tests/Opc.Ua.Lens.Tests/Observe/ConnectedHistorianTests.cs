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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Historian;
using UaLens.Tests.Desktop;
using UaLens.Views;

namespace UaLens.Tests.Observe;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ConnectedHistorianTests
{
    [TestCase((int)HistorianReadMode.Raw, false)]
    [TestCase((int)HistorianReadMode.Raw, true)]
    [TestCase((int)HistorianReadMode.Processed, false)]
    [TestCase((int)HistorianReadMode.AtTime, false)]
    public Task SelectedModeDispatchesExactDetailsAndCompletesWithPublishedRows(int mode, bool modified)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            await using HistorianPlugin plugin = CreatePlugin(context);
            plugin.ReadMode = (HistorianReadMode)mode;
            plugin.IsReadModified = modified;
            plugin.ReturnBounds = true;
            plugin.NumValuesPerNode = 12;
            plugin.ProcessingIntervalMs = 250;
            plugin.SelectedAggregate = new AggregateOption
            {
                NodeId = ObjectIds.AggregateFunction_Average, DisplayName = "Average"
            };
            plugin.AddTimestampCommand.Execute(null);
            AtTimeRow time = plugin.AtTimes.First(row => !row.IsAddButton);
            time.Timestamp = s_start;
            time.IsEditing = false;
            ExtensionObject sent = default;
            int reads = 0;
            SetupRead(context, (details, ids, token) =>
            {
                reads++;
                sent = details;
                Assert.That(ids[0].NodeId, Is.EqualTo(s_node));
                Assert.That(token.CanBeCanceled, Is.True);
                return ValueTask.FromResult(HistoryProtocolTestDriver.Page(s_values, modified: modified));
            });
            await plugin.ReadCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(plugin.IsReading, Is.False);
            Assert.That(plugin.Rows.Select(row => row.Numeric), Is.EqualTo(s_numbers),
                "Read completion must include publication, not leave an unowned dispatcher callback.");
            Assert.That(plugin.Status, Does.Contain("2 rows"));
            if ((HistorianReadMode)mode == HistorianReadMode.Raw)
            {
                var details = WorkflowAssertions.GetEncodeable<ReadRawModifiedDetails>(sent);
                Assert.That(details.StartTime, Is.EqualTo((DateTimeUtc)s_start));
                Assert.That(details.EndTime, Is.EqualTo((DateTimeUtc)s_start.AddHours(1)));
                Assert.That(details.NumValuesPerNode, Is.EqualTo(12));
                Assert.That(details.ReturnBounds, Is.True);
                Assert.That(details.IsReadModified, Is.EqualTo(modified));
            }
            else if ((HistorianReadMode)mode == HistorianReadMode.Processed)
            {
                var details = WorkflowAssertions.GetEncodeable<ReadProcessedDetails>(sent);
                Assert.That(details.ProcessingInterval, Is.EqualTo(250));
                Assert.That(details.AggregateType[0], Is.EqualTo(ObjectIds.AggregateFunction_Average));
            }
            else
            {
                var details = WorkflowAssertions.GetEncodeable<ReadAtTimeDetails>(sent);
                Assert.That(details.ReqTimes.Count, Is.EqualTo(1));
                Assert.That(details.ReqTimes[0], Is.EqualTo((DateTimeUtc)s_start));
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task CancelReadPreventsLateRowsAndFinishesOwnedWork(bool serverIgnoresCancellation)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            await using HistorianPlugin plugin = CreatePlugin(context);
            var completion = new TaskCompletionSource<HistoryReadResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken received = default;
            SetupRead(context, (_, _, token) =>
            {
                received = token;
                entered.TrySetResult();
                return new ValueTask<HistoryReadResponse>(serverIgnoresCancellation
                    ? completion.Task : completion.Task.WaitAsync(token));
            });
            Task operation = plugin.ReadCommand.ExecuteAsync(null);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                Assert.That(plugin.IsReading, Is.True);
                plugin.CancelReadCommand.Execute(null);
                Assert.That(received.IsCancellationRequested, Is.True);
                completion.TrySetResult(HistoryProtocolTestDriver.Page(s_values));
                await operation.ConfigureAwait(true);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.That(plugin.Rows, Is.Empty);
                Assert.That(plugin.Status, Is.EqualTo("● Read cancelled."));
                Assert.That(plugin.IsReading, Is.False);
            }
            finally
            {
                completion.TrySetResult(HistoryProtocolTestDriver.Page([]));
                await operation.ConfigureAwait(true);
            }
        });
    }

    [TestCase((int)HistorianUpdateOp.Insert, false)]
    [TestCase((int)HistorianUpdateOp.Insert, true)]
    [TestCase((int)HistorianUpdateOp.Replace, false)]
    [TestCase((int)HistorianUpdateOp.InsertReplace, false)]
    [TestCase((int)HistorianUpdateOp.Remove, false)]
    [TestCase((int)HistorianUpdateOp.DeleteRaw, false)]
    [TestCase((int)HistorianUpdateOp.DeleteModified, false)]
    [TestCase((int)HistorianUpdateOp.DeleteAtTime, false)]
    public Task UpdatePreservesExactTargetTimesAndRefreshesOnlyAfterSuccess(int operation, bool reject)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            await using HistorianPlugin plugin = CreatePlugin(context);
            plugin.SelectedUpdateOp = (HistorianUpdateOp)operation;
            plugin.UpdateTimestamp = s_start;
            plugin.UpdateStart = s_start;
            plugin.UpdateEnd = s_start.AddMinutes(10);
            plugin.UpdateValueText = "42.5";
            plugin.UpdateAtTimes.Single(row => row.IsAddButton).RemoveCommand.Execute(null);
            AtTimeRow time = plugin.UpdateAtTimes.First(row => !row.IsAddButton);
            time.Timestamp = s_start;
            time.IsEditing = false;
            int readCount = 0;
            SetupRead(context, (_, _, _) =>
            {
                readCount++;
                return ValueTask.FromResult(HistoryProtocolTestDriver.Page(s_values));
            });
            ArrayOf<ExtensionObject> updates = default;
            context.Session.Setup(session => session.HistoryUpdateAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<ExtensionObject>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<ExtensionObject> details, CancellationToken _) =>
                {
                    updates = details;
                    return ValueTask.FromResult(HistoryProtocolTestDriver.Outcome(
                        reject ? StatusCodes.BadUserAccessDenied : StatusCodes.Good));
                });
            Task task;
            if ((HistorianUpdateOp)operation == HistorianUpdateOp.Insert)
            {
                task = plugin.ExecuteUpdateAsync();
            }
            else
            {
                task = Task.CompletedTask;
                Window confirmation = await DesktopInteraction.OpenedAsync<Window>(
                    () => task = plugin.ExecuteUpdateAsync()).ConfigureAwait(true);
                Assert.That(updates.IsNull, Is.True);
                Button confirm = confirmation.GetLogicalDescendants().OfType<Button>()
                    .Single(button => !Equals(button.Content, "Cancel"));
                DesktopInteraction.Click(confirm);
            }
            await task.ConfigureAwait(true);
            Assert.That(updates.Count, Is.EqualTo(1));
            if (updates[0].TryGetValue(out UpdateDataDetails? data))
            {
                Assert.That(data!.NodeId, Is.EqualTo(s_node));
                Assert.That(data.UpdateValues[0].WrappedValue, Is.EqualTo(Variant.From(42.5)));
                Assert.That(data.UpdateValues[0].SourceTimestamp, Is.EqualTo((DateTimeUtc)s_start));
            }
            else if (updates[0].TryGetValue(out DeleteRawModifiedDetails? range))
            {
                Assert.That(range!.NodeId, Is.EqualTo(s_node));
                Assert.That(range.StartTime, Is.EqualTo((DateTimeUtc)s_start));
                Assert.That(range.EndTime, Is.EqualTo((DateTimeUtc)(
                    (HistorianUpdateOp)operation == HistorianUpdateOp.Remove ? s_start : s_start.AddMinutes(10))));
                Assert.That(range.IsDeleteModified,
                    Is.EqualTo((HistorianUpdateOp)operation == HistorianUpdateOp.DeleteModified));
            }
            else
            {
                var atTime = WorkflowAssertions.GetEncodeable<DeleteAtTimeDetails>(updates[0]);
                Assert.That(atTime.NodeId, Is.EqualTo(s_node));
                Assert.That(atTime.ReqTimes[0], Is.EqualTo((DateTimeUtc)s_start));
            }
            Assert.That(readCount, Is.EqualTo(reject ? 0 : 1));
            Assert.That(plugin.UpdateResult, Does.Contain(reject ? "BadUserAccessDenied" : "Good"));
        });
    }

    private static HistorianPlugin CreatePlugin(ConnectedProtocolContext context)
    {
        context.Translate = (_, _) => ValueTask.FromResult(new TranslateBrowsePathsToNodeIdsResponse
        {
            Results = [new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }]
        });
        return new HistorianPlugin(context.Host)
        {
            TargetNodeId = s_node, TargetDisplayName = "Temperature",
            CustomStart = s_start, CustomEnd = s_start.AddHours(1)
        };
    }

    [TestCase("edit", false)]
    [TestCase("nearest", false)]
    [TestCase("insert", false)]
    [TestCase("after", false)]
    [TestCase("new", false)]
    [TestCase("edit", true)]
    [TestCase("insert", true)]
    public Task EditAndInsertEntryPointsCommitOnlyTheChosenTargetTimestampAndValue(string entry, bool reject)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            await using HistorianPlugin plugin = CreatePlugin(context);
            var first = new HistoryRow(s_start, s_start, Variant.From(11), StatusCodes.Good);
            var last = new HistoryRow(
                s_start.AddSeconds(10),
                s_start.AddSeconds(10),
                Variant.From(22),
                StatusCodes.Good);
            plugin.Rows.Add(first);
            plugin.Rows.Add(last);
            plugin.SelectedRow = first;
            int reads = 0;
            SetupRead(context, (_, _, _) =>
            {
                reads++;
                return ValueTask.FromResult(HistoryProtocolTestDriver.Page(s_values));
            });
            UpdateDataDetails? sent = null;
            context.Session.Setup(session => session.HistoryUpdateAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<ExtensionObject>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<ExtensionObject> updates, CancellationToken _) =>
                {
                    sent = WorkflowAssertions.GetEncodeable<UpdateDataDetails>(updates[0]);
                    return ValueTask.FromResult(HistoryProtocolTestDriver.Outcome(
                        reject ? StatusCodes.BadUserAccessDenied : StatusCodes.Good));
                });
            Task operation = Task.CompletedTask;
            EditHistoryRowDialog dialog = await DesktopInteraction.OpenedAsync<EditHistoryRowDialog>(() =>
                operation = entry switch
                {
                    "edit" => plugin.EditSelectedAsync(),
                    "nearest" => plugin.EditNearestAsync(new NearestArgs(s_start.AddSeconds(9))),
                    "after" => plugin.InsertAfterSelectedAsync(),
                    "new" => plugin.InsertNewAsync(),
                    _ => plugin.InsertAtAsync(new InsertAtArgs(s_start.AddSeconds(4), 33))
                }).ConfigureAwait(true);
            Assert.That(sent, Is.Null);
            DateTime expected = entry == "nearest" ? last.SourceTimestamp : first.SourceTimestamp;
            if (entry is not ("edit" or "nearest"))
            {
                if (entry == "after")
                {
                    Assert.That(DesktopInteraction.Control<UtcDateTimePicker>(dialog, "TimestampPicker").Value,
                        Is.EqualTo(s_start.AddSeconds(5)));
                }
                expected = s_start.AddMinutes(2);
                DesktopInteraction.Control<UtcDateTimePicker>(dialog, "TimestampPicker").Value = expected;
            }
            DesktopInteraction.Control<TextBox>(dialog, "ValueText").Text = "42.5";
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
            await operation.ConfigureAwait(true);
            Assert.That(sent, Is.Not.Null);
            Assert.That(sent!.NodeId, Is.EqualTo(s_node));
            Assert.That(sent.UpdateValues.Count, Is.EqualTo(1));
            Assert.That(sent.UpdateValues[0].SourceTimestamp, Is.EqualTo((DateTimeUtc)expected));
            Assert.That(sent.UpdateValues[0].WrappedValue, Is.EqualTo(Variant.From(42.5)));
            Assert.That(reads, Is.EqualTo(reject ? 0 : 1));
            if (reject)
            {
                Assert.That(plugin.Rows[0], Is.SameAs(first));
                Assert.That(plugin.Status, Does.Contain("denied (BadUserAccessDenied)"));
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task AnnotationEditsUseTheAnnotationPropertyAndChangeTheRowOnlyAfterSuccess(bool reject)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            await using HistorianPlugin plugin = CreatePlugin(context);
            NodeId property = new("Annotations", 2);
            context.Translate = (paths, _) =>
            {
                Assert.That(paths[0].StartingNode, Is.EqualTo(s_node));
                Assert.That(paths[0].RelativePath.Elements[0].TargetName.Name, Is.EqualTo(BrowseNames.Annotations));
                return ValueTask.FromResult(HistoryProtocolTestDriver.Property(property));
            };
            var original = new Annotation { Message = "Original", UserName = "operator", AnnotationTime = s_start };
            var row = new HistoryRow(s_start, s_start, Variant.From(11), StatusCodes.Good) { Annotation = original };
            plugin.Rows.Add(row);
            plugin.SelectedRow = row;
            UpdateDataDetails? sent = null;
            context.Session.Setup(session => session.HistoryUpdateAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<ExtensionObject>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<ExtensionObject> details, CancellationToken _) =>
                {
                    sent = WorkflowAssertions.GetEncodeable<UpdateDataDetails>(details[0]);
                    return ValueTask.FromResult(HistoryProtocolTestDriver.Outcome(
                        reject ? StatusCodes.BadUserAccessDenied : StatusCodes.Good));
                });
            Task saving = Task.CompletedTask;
            AnnotationEditDialog dialog = await DesktopInteraction.OpenedAsync<AnnotationEditDialog>(
                () => saving = plugin.EditAnnotationAsync()).ConfigureAwait(true);
            DesktopInteraction.Control<TextBox>(dialog, "MessageText").Text = "Reviewed";
            Assert.That(sent, Is.Null);
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
            await saving.ConfigureAwait(true);
            Assert.That(sent!.NodeId, Is.EqualTo(property));
            Assert.That(sent.PerformInsertReplace, Is.EqualTo(PerformUpdateType.Update));
            Assert.That(sent.UpdateValues[0].SourceTimestamp, Is.EqualTo((DateTimeUtc)s_start));
            Assert.That(sent.UpdateValues[0].WrappedValue.TryGetValue<Annotation>(
                out Annotation? annotation, context.Messages), Is.True);
            Assert.That(annotation!.Message, Is.EqualTo("Reviewed"));
            Assert.That(row.Annotation!.Message, Is.EqualTo(reject ? "Original" : "Reviewed"));
            Assert.That(plugin.Status, Does.Contain(reject ? "BadUserAccessDenied" : "Annotation saved"));
        });
    }

    private static void SetupRead(
        ConnectedProtocolContext context,
        Func<ExtensionObject, ArrayOf<HistoryReadValueId>, CancellationToken, ValueTask<HistoryReadResponse>> read)
    {
        context.Session.Setup(session => session.HistoryReadAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ExtensionObject>(), It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
            It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, ExtensionObject details, TimestampsToReturn _, bool _,
                ArrayOf<HistoryReadValueId> ids, CancellationToken token) => read(details, ids, token));
    }

    private static readonly NodeId s_node = new("Temperature", 2);
    private static readonly DateTime s_start = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly double?[] s_numbers = [11, 22];
    private static readonly DataValue[] s_values =
    [
        new(Variant.From(11), StatusCodes.Good, s_start, s_start),
        new(Variant.From(22), StatusCodes.Good, s_start.AddSeconds(1), s_start.AddSeconds(1))
    ];
}
