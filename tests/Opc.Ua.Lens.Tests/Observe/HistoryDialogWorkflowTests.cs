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
using Opc.Ua;
using UaLens.Plugins.Historian;
using UaLens.Tests.Desktop;
using UaLens.Views;

namespace UaLens.Tests.Observe;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class HistoryDialogWorkflowTests
{
    private static readonly DateTime s_time = new(2026, 3, 4, 5, 6, 7, 123, DateTimeKind.Utc);

    [TestCase(false, 0, "12.75", 12.75)]
    [TestCase(false, 1, "-27", -27)]
    [TestCase(false, 2, "4.5e2", 450)]
    [TestCase(true, 0, "0", 0)]
    [TestCase(true, 1, "-0.125", -0.125)]
    [TestCase(true, 2, "1.7976931348623157E+308", double.MaxValue)]
    public Task HistoryEditReturnsExactActionTypedValueStatusAndTimestamps(
        bool insert, int actionIndex, string text, double expected)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var options = new EditHistoryRowDialogOptions(
                insert ? EditHistoryRowMode.Insert : EditHistoryRowMode.Edit,
                new NodeId("Temperature", 2), s_time, -12.5, StatusCodes.Uncertain);
            var dialog = new EditHistoryRowDialog(options);
            Task<EditHistoryRowResult?> shown = dialog.ShowDialog<EditHistoryRowResult?>(DesktopInteraction.Owner);
            try
            {
                Assert.That(dialog.Title, Is.EqualTo(insert ? "Insert history row" : "Edit history row"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "NodeIdLabel").Text,
                    Is.EqualTo("ns=2;s=Temperature"));
                Assert.That(DesktopInteraction.Control<TextBox>(dialog, "ValueText").Text, Is.EqualTo("-12.5"));
                ComboBox actions = DesktopInteraction.Control<ComboBox>(dialog, "ActionCombo");
                Assert.That(
                    actions.Items.Cast<string>(),
                    Is.EqualTo(s_historyEditReturnsExactActionTypedValueStatusAndTimestampsExpected));
                Assert.That(actions.SelectedIndex, Is.EqualTo(insert ? 0 : 1));
                UtcDateTimePicker picker = DesktopInteraction.Control<UtcDateTimePicker>(dialog, "TimestampPicker");
                Assert.That(picker.IsVisible, Is.EqualTo(insert));
                picker.Value = s_time.AddMinutes(3);
                DesktopInteraction.Control<TextBox>(dialog, "ValueText").Text = text;
                actions.SelectedIndex = actionIndex;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));

                EditHistoryRowResult? result = await shown.ConfigureAwait(true);
                Assert.That(result, Is.SameAs(dialog.Result));
                Assert.That(result!.NodeId, Is.EqualTo(new NodeId("Temperature", 2)));
                Assert.That(result.Timestamp, Is.EqualTo(insert ? s_time.AddMinutes(3) : s_time));
                Assert.That(result.Timestamp.Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(result.Action, Is.EqualTo(new[]
                {
                    PerformUpdateType.Insert, PerformUpdateType.Replace, PerformUpdateType.Update
                }[actionIndex]));
                Assert.That(result.Value.TryGetValue(out double actual), Is.True);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(result.Status, Is.EqualTo(StatusCodes.Uncertain));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task RestrictedActionListAndMissingSelectionUseDeclaredDefault(bool deselect)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new EditHistoryRowDialog(new EditHistoryRowDialogOptions(
                EditHistoryRowMode.Insert, new NodeId(51u, 2), s_time, 9, StatusCodes.Good,
                [PerformUpdateType.Update, PerformUpdateType.Insert], PerformUpdateType.Insert,
                "Insert a calibrated point"));
            Task<EditHistoryRowResult?> shown = dialog.ShowDialog<EditHistoryRowResult?>(DesktopInteraction.Owner);
            try
            {
                ComboBox actions = DesktopInteraction.Control<ComboBox>(dialog, "ActionCombo");
                Assert.That(
                    actions.Items.Cast<string>(),
                    Is.EqualTo(s_restrictedActionListAndMissingSelectionUseDeclaredDefaultExpected));
                Assert.That(actions.SelectedIndex, Is.EqualTo(1));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "HintLabel").Text,
                    Is.EqualTo("Insert a calibrated point"));
                actions.SelectedIndex = deselect ? -1 : 0;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                EditHistoryRowResult? result = await shown.ConfigureAwait(true);
                Assert.That(result!.Action, Is.EqualTo(deselect ? PerformUpdateType.Insert : PerformUpdateType.Update));
                Assert.That(result.Timestamp, Is.EqualTo(s_time));
                Assert.That(result.Value, Is.EqualTo(new Variant(9d)));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase("")]
    [TestCase("not a double")]
    [TestCase("12,5")]
    public Task InvalidHistoryValueCannotAcceptAndCancelPreservesOriginalRow(string text)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var original = new HistoryRow(s_time, s_time.AddSeconds(2), new Variant(37.5), StatusCodes.BadNoData);
            var dialog = new EditHistoryRowDialog(new NodeId("Pressure", 2), original);
            Task<EditHistoryRowResult?> shown = dialog.ShowDialog<EditHistoryRowResult?>(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "TimestampLabel").Text,
                    Is.EqualTo("2026-03-04T05:06:07.123Z"));
                DesktopInteraction.Control<TextBox>(dialog, "ValueText").Text = text;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(shown.IsCompleted, Is.False);
                Assert.That(dialog.Result, Is.Null);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultLabel").Text,
                    Is.EqualTo("Value must parse as a double."));
                DesktopInteraction.Control<TextBox>(dialog, "ValueText").Text = "84";
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                Assert.That(original.Value, Is.EqualTo(new Variant(37.5)));
                Assert.That(original.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(original.ServerTimestamp, Is.EqualTo(s_time.AddSeconds(2)));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase("unavailable", "0")]
    [TestCase("15.25", "15.25")]
    public Task ExistingRowEditorUsesNumericProjectionWithoutChangingOriginalValue(string text, string initial)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var row = new HistoryRow(s_time, s_time, new Variant(text), StatusCodes.Good);
            var dialog = new EditHistoryRowDialog(new NodeId("Sensor", 2), row);
            Task<EditHistoryRowResult?> shown = dialog.ShowDialog<EditHistoryRowResult?>(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBox>(dialog, "ValueText").Text, Is.EqualTo(initial));
                Assert.That(DesktopInteraction.Control<ComboBox>(dialog, "ActionCombo").SelectedItem,
                    Is.EqualTo("Replace"));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                Assert.That(row.Value, Is.EqualTo(new Variant(text)));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public Task AnnotationEditClonesInputAndCommitsOnlyAcceptedFields(bool withExisting, bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var existing = new Annotation
            {
                Message = "original", UserName = "previous author", AnnotationTime = s_time
            };
            var dialog = new AnnotationEditDialog(s_time.ToLocalTime(), withExisting ? existing : null);
            Task<Annotation?> shown = dialog.ShowDialog<Annotation?>(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "TimestampLabel").Text,
                    Is.EqualTo("2026-03-04T05:06:07.123Z"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "AnnotationTimeLabel").Text,
                    Is.EqualTo("(stamped at Save)"));
                Assert.That(DesktopInteraction.Control<TextBox>(dialog, "MessageText").Text,
                    Is.EqualTo(withExisting ? "original" : string.Empty));
                DesktopInteraction.Control<TextBox>(dialog, "MessageText").Text = "校正済み\n42 °C";
                DateTime before = DateTime.UtcNow;
                DesktopInteraction.Click(
                    DesktopInteraction.Control<Button>(dialog, accept ? "OkButton" : "CancelButton"));
                Annotation? result = await shown.ConfigureAwait(true);
                DateTime after = DateTime.UtcNow;
                if (accept)
                {
                    Assert.That(result, Is.Not.SameAs(existing));
                    Assert.That(result!.Message, Is.EqualTo("校正済み\n42 °C"));
                    Assert.That(result.UserName,
                        Is.EqualTo(DesktopInteraction.Control<TextBlock>(dialog, "UserNameLabel").Text));
                    Assert.That((DateTime)result.AnnotationTime, Is.InRange(before, after));
                    Assert.That(dialog.Result, Is.SameAs(result));
                }
                else
                {
                    Assert.That(result, Is.Null);
                    Assert.That(dialog.Result, Is.Null);
                }
                Assert.That(existing.Message, Is.EqualTo("original"));
                Assert.That(existing.UserName, Is.EqualTo("previous author"));
                Assert.That((DateTime)existing.AnnotationTime, Is.EqualTo(s_time));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Test]
    public Task EmptyAnnotationCannotSaveButCorrectedMessageCan()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new AnnotationEditDialog(s_time, null);
            Task<Annotation?> shown = dialog.ShowDialog<Annotation?>(DesktopInteraction.Owner);
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "MessageText").Text = string.Empty;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(shown.IsCompleted, Is.False);
                Assert.That(dialog.Result, Is.Null);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultLabel").Text,
                    Is.EqualTo("Message must not be empty."));
                DesktopInteraction.Control<TextBox>(dialog, "MessageText").Text = "Sensor calibrated";
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That((await shown.ConfigureAwait(true))!.Message, Is.EqualTo("Sensor calibrated"));
                Assert.That(dialog.Result!.UserName, Is.Not.Empty);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static readonly string[] s_historyEditReturnsExactActionTypedValueStatusAndTimestampsExpected =
    [
        "Insert",
        "Replace",
        "Update",
    ];
    private static readonly string[] s_restrictedActionListAndMissingSelectionUseDeclaredDefaultExpected =
    [
        "Update",
        "Insert",
    ];
}
