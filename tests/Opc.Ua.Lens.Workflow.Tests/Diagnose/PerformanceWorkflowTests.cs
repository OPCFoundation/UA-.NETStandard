// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Performance;
using UaLens.Tests.Desktop;
using UaLens.Tests.Observe;
using UaLens.ViewModels;

namespace UaLens.Tests.Diagnose;

[TestFixture]
[NonParallelizable]
public sealed partial class PerformanceWorkflowTests
{

    [TestCase(0, 59, "59 s")]
    [TestCase(0, 60, "1 min")]
    [TestCase(1, 59, "59 min")]
    [TestCase(1, 60, "1 h")]
    [TestCase(2, 2, "2 h")]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task SettingsCommandChangesActualBoundWorkloadAndCloseRetainsIntentWithoutStartingARun(
        int unit, int duration, string display)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = HistorianWorkflowTests.Host(context);
            await using var plugin = new PerformancePlugin(host);
            Task command = Task.CompletedTask;
            PerformanceSettingsDialog dialog = await DesktopInteraction.OpenedAsync<PerformanceSettingsDialog>(
                () => command = plugin.OpenSettingsDialogCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                Assert.That(dialog.DataContext, Is.SameAs(plugin));
                Slider rate = DesktopInteraction.Control<Slider>(dialog, "RateSlider");
                rate.Value = 450;
                var durationInput = dialog.GetLogicalDescendants().OfType<NumericUpDown>().Single();
                durationInput.Value = duration;
                ComboBox units = dialog.GetLogicalDescendants().OfType<ComboBox>()
                    .Single(combo => ReferenceEquals(combo.ItemsSource, plugin.DurationUnitOptions));
                units.SelectedItem = (DurationUnit)unit;
                ComboBox generator = dialog.GetLogicalDescendants().OfType<ComboBox>()
                    .Single(combo => ReferenceEquals(combo.ItemsSource, plugin.GeneratorOptions));
                generator.SelectedItem = ValueGenerator.Fixed;
                CheckBox burst = dialog.GetLogicalDescendants().OfType<CheckBox>()
                    .Single(box => Equals(box.Content, "Unbounded burst"));
                burst.IsChecked = true;
                Assert.That(rate.IsEnabled, Is.False);
                Assert.That(plugin.RateEditable, Is.False);
                burst.IsChecked = false;
                Assert.That(rate.IsEnabled, Is.True);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CloseButton"));
                await command.ConfigureAwait(true);

                Assert.That(plugin.TargetRate, Is.EqualTo(450));
                Assert.That(plugin.TargetRateText, Is.EqualTo("450"));
                Assert.That(plugin.Generator, Is.EqualTo(ValueGenerator.Fixed));
                Assert.That(plugin.DurationSeconds, Is.EqualTo(duration));
                Assert.That(plugin.DurationUnit, Is.EqualTo((DurationUnit)unit));
                Assert.That(plugin.EffectiveDurationText, Is.EqualTo(display));
                Assert.That(plugin.UnboundedBurst, Is.False);
                Assert.That(plugin.IsRunning, Is.False);
                Assert.That(plugin.RunHistory, Is.Empty);
                Assert.That(plugin.StopCommand.CanExecute(null), Is.False);
                Assert.That(context.ConfigurationsCreated, Is.Zero);
            }
            finally
            {
                dialog.Close();
                await command.ConfigureAwait(true);
            }
        });
    }
}
