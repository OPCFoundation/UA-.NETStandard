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
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class MonitoredItemSettingsDialogTests
{
    [TestCase(MonitoringMode.Disabled, -1, 0u, DataChangeTrigger.StatusValue, DeadbandType.None, 0)]
    [TestCase(MonitoringMode.Sampling, 0, uint.MaxValue, DataChangeTrigger.Status, DeadbandType.Absolute, 1.25)]
    [TestCase(
        MonitoringMode.Reporting, 3600000, 17u, DataChangeTrigger.StatusValueTimestamp, DeadbandType.Percent, 100)]
    public Task AcceptReturnsExactIndependentSamplingQueueModeAndFilter(
        MonitoringMode mode, double sampling, uint queue,
        DataChangeTrigger trigger, DeadbandType deadband, double value)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var current = new MonitoredItemSettings
            {
                SamplingInterval = TimeSpan.FromMilliseconds(125),
                QueueSize = 8,
                MonitoringMode = MonitoringMode.Reporting,
                DataChangeFilter = new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.StatusValue,
                    DeadbandType = (uint)DeadbandType.Absolute,
                    DeadbandValue = 2
                }
            };
            var dialog = new MonitoredItemSettingsDialog(current, "Only the selected item.");
            Task<MonitoredItemSettings?> shown = dialog.ShowDialog<MonitoredItemSettings?>(DesktopInteraction.Owner);
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "SamplingMs").Text =
                    sampling.ToString(CultureInfo.CurrentCulture);
                DesktopInteraction.Control<TextBox>(dialog, "QueueSizeBox").Text =
                    queue.ToString(CultureInfo.InvariantCulture);
                DesktopInteraction.Control<ComboBox>(dialog, "MonitoringModeCombo").SelectedIndex = (int)mode;
                DesktopInteraction.Control<ComboBox>(dialog, "TriggerCombo").SelectedIndex = (int)trigger;
                DesktopInteraction.Control<ComboBox>(dialog, "DeadbandTypeCombo").SelectedIndex = (int)deadband;
                DesktopInteraction.Control<TextBox>(dialog, "DeadbandValueBox").Text =
                    value.ToString(CultureInfo.CurrentCulture);
                DesktopInteraction.Control<CheckBox>(dialog, "DiscardOldestBox").IsChecked = false;
                Assert.That(DesktopInteraction.Control<TextBox>(dialog, "DeadbandValueBox").IsEnabled,
                    Is.EqualTo(deadband != DeadbandType.None));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                MonitoredItemSettings? result = await shown.ConfigureAwait(true);
                Assert.That(result, Is.SameAs(dialog.Result).And.Not.SameAs(current));
                Assert.That(result!.SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(sampling)));
                Assert.That(result.QueueSize, Is.EqualTo(queue));
                Assert.That(result.MonitoringMode, Is.EqualTo(mode));
                Assert.That(result.DiscardOldest, Is.False);
                if (deadband == DeadbandType.None)
                {
                    Assert.That(result.DataChangeFilter, Is.Null);
                }
                else
                {
                    Assert.That(result.DataChangeFilter!.Trigger, Is.EqualTo(trigger));
                    Assert.That(result.DataChangeFilter.DeadbandType, Is.EqualTo((uint)deadband));
                    Assert.That(result.DataChangeFilter.DeadbandValue, Is.EqualTo(value));
                }
                Assert.That(current.QueueSize, Is.EqualTo(8));
                Assert.That(current.DataChangeFilter!.DeadbandValue, Is.EqualTo(2));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ScopeHint").Text,
                    Is.EqualTo("Only the selected item."));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase("SamplingMs", "-2", "Sampling")]
    [TestCase("SamplingMs", "3600001", "Sampling")]
    [TestCase("SamplingMs", "NaN", "Sampling")]
    [TestCase("SamplingMs", "Infinity", "Sampling")]
    [TestCase("QueueSizeBox", "1.5", "Queue size")]
    [TestCase("QueueSizeBox", "4294967296", "Queue size")]
    [TestCase("QueueSizeBox", "-1", "Queue size")]
    [TestCase("DeadbandValueBox", "-1", "Deadband")]
    [TestCase("DeadbandValueBox", "NaN", "Deadband")]
    [TestCase("DeadbandValueBox", "101", "percent deadband")]
    public Task NumericAndDeadbandBoundariesRejectInvalidInput(string field, string invalid, string message)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var original = new MonitoredItemSettings { QueueSize = 5 };
            var dialog = new MonitoredItemSettingsDialog(original);
            Task<MonitoredItemSettings?> shown = dialog.ShowDialog<MonitoredItemSettings?>(DesktopInteraction.Owner);
            try
            {
                DesktopInteraction.Control<ComboBox>(dialog, "DeadbandTypeCombo").SelectedIndex =
                    (int)DeadbandType.Percent;
                DesktopInteraction.Control<TextBox>(dialog, field).Text = invalid;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(shown.IsCompleted, Is.False);
                Assert.That(dialog.Result, Is.Null);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ValidationError").Text,
                    Does.Contain(message));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                Assert.That(original.QueueSize, Is.EqualTo(5));
                Assert.That(original.DataChangeFilter, Is.Null);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Test]
    public Task CancelDoesNotApplyEditedSettings()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var original = new MonitoredItemSettings
            {
                SamplingInterval = TimeSpan.FromMilliseconds(100), QueueSize = 12, DiscardOldest = true
            };
            var dialog = new MonitoredItemSettingsDialog(original);
            Task<MonitoredItemSettings?> shown = dialog.ShowDialog<MonitoredItemSettings?>(DesktopInteraction.Owner);
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "SamplingMs").Text = "900";
                DesktopInteraction.Control<TextBox>(dialog, "QueueSizeBox").Text = "30";
                DesktopInteraction.Control<ComboBox>(dialog, "MonitoringModeCombo").SelectedIndex = 0;
                DesktopInteraction.Control<ComboBox>(dialog, "TriggerCombo").SelectedIndex = 0;
                DesktopInteraction.Control<ComboBox>(dialog, "DeadbandTypeCombo").SelectedIndex = 1;
                DesktopInteraction.Control<TextBox>(dialog, "DeadbandValueBox").Text = "5";
                DesktopInteraction.Control<CheckBox>(dialog, "DiscardOldestBox").IsChecked = false;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                Assert.That(dialog.Result, Is.Null);
                Assert.That(original.SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(100)));
                Assert.That(original.QueueSize, Is.EqualTo(12));
                Assert.That(original.DiscardOldest, Is.True);
                Assert.That(original.MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
                Assert.That(original.DataChangeFilter, Is.Null);
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
