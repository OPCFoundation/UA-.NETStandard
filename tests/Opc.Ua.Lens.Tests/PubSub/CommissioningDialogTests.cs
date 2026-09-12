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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using UaLens.Connection;
using UaLens.Plugins.PubSub;
using UaLens.Tests.Observe;
using UaLens.Views;

namespace UaLens.Tests.PubSub;

[TestFixture]
[Explicit("Requires an interactive desktop. No network operation, key acquisition or listener is started.")]
[Category("CommissioningDialogProbe")]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class CommissioningDialogTests
{
    [OneTimeSetUp]
    public void InitializeDesktop()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
            Application.Current!.Styles.Add(new FluentTheme());
        }
    }

    [Test]
    public async Task PubSubViewShowsRealIdentityChoicesAndEditableScalarFieldTypesAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var plugin = new PubSubPlugin(host.Host);
            await using (plugin.ConfigureAwait(false))
            {
                var view = new PubSubView { DataContext = plugin };
                var owner = new Window
                {
                    Title = "UaLens - Commissioning regression", Width = 1000, Height = 750,
                    ShowInTaskbar = false, Content = view
                };
                owner.Show();
                try
                {
                    foreach (Expander section in view.GetLogicalDescendants().OfType<Expander>().ToArray())
                    {
                        section.IsExpanded = true;
                    }
                    owner.UpdateLayout();
                    ComboBox[] choices = view.GetLogicalDescendants().OfType<ComboBox>().ToArray();
                    ComboBox localId = choices.First(control => control.SelectedItem is PublisherIdType);
                    Assert.That(localId.Items.Cast<PublisherIdType>(), Does.Contain(PublisherIdType.UInt64));
                    Assert.That(localId.Items.Cast<PublisherIdType>(), Does.Contain(PublisherIdType.String));
                    localId.SelectedItem = PublisherIdType.String;
                    Assert.That(plugin.UsesTextLocalPublisher, Is.True);
                    ComboBox keySource = choices.Single(control => control.SelectedItem is PubSubKeySource);
                    Assert.That(keySource.Items, Has.Count.EqualTo(2));
                    keySource.SelectedItem = PubSubKeySource.ConfiguredProvider;
                    Assert.That(plugin.KeySource, Is.EqualTo(PubSubKeySource.ConfiguredProvider));

                    ListBox fields = view.GetLogicalDescendants().OfType<ListBox>().Distinct().Single(control =>
                        ReferenceEquals(control.ItemsSource, plugin.FieldDrafts));
                    Control field = fields.ItemTemplate!.Build(plugin.FieldDrafts[0])!;
                    field.DataContext = plugin.FieldDrafts[0];
                    ComboBox types = field.GetLogicalDescendants().OfType<ComboBox>().Single();
                    Assert.That(types.Items.Cast<BuiltInType>(), Does.Contain(BuiltInType.Double));
                    Assert.That(types.Items.Cast<BuiltInType>(), Does.Not.Contain(BuiltInType.ExtensionObject));
                    types.SelectedItem = BuiltInType.Double;
                    Assert.That(plugin.FieldDrafts[0].Type, Is.EqualTo(BuiltInType.Double));
                    Assert.That(plugin.IsRunning, Is.False);
                    Assert.That(host.Connection.Session, Is.Null);
                    Assert.That(PubSubStateCodec.Restore(plugin.CaptureState()).LocalPublisherIdType,
                        Is.EqualTo(PublisherIdType.UInt16));
                }
                finally
                {
                    owner.Close();
                }
            }
        }
    }

    [Test]
    public async Task TransportDialogDisplaysReadableChecksAndUseSetupDoesNotStartAListenerAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var setup = new ConnectionSetupSelection("opc.tcp://localhost:4840/Sample");
            var dialog = new ConnectionSetupDialog(host.Connection, setup);
            var owner = new Window { Width = 300, Height = 180, ShowInTaskbar = false };
            owner.Show();
            try
            {
                Task<ConnectionSetupSelection?> pending = dialog.PromptAsync(owner);
                TextBlock readiness = dialog.GetLogicalDescendants().OfType<TextBlock>().Single(control =>
                    control.Name == "TransportSetupReadiness");
                Assert.That(readiness.Text,
                    Does.Contain("Transport: Ready").And.Contain("External prerequisites: Pending"));
                Button use = dialog.GetLogicalDescendants().OfType<Button>().Single(control =>
                    control.Name == "UseConnectionSetupButton");
                Assert.That(use.IsEnabled, Is.True);
                use.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(pending);
                ConnectionSetupSelection? result = await pending.ConfigureAwait(true);
                Assert.That(result, Is.EqualTo(setup));
                Assert.That(host.Connection.ConfiguredBackend!.ReverseConnections.Snapshot.Phase,
                    Is.EqualTo(ReverseConnectionPhase.Stopped));
                Assert.That(host.Connection.Session, Is.Null);
            }
            finally
            {
                dialog.Close();
                owner.Close();
            }
        }
    }

    private static void PumpUntil(Task task)
    {
        if (task.IsCompleted)
        {
            return;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        {
            if (task.IsCompleted)
            {
                timeout.Cancel();
            }
        });
        timer.Start();
        try
        {
            Dispatcher.UIThread.MainLoop(timeout.Token);
        }
        finally
        {
            timer.Stop();
        }
        Assert.That(task.IsCompleted, Is.True, "The owned setup dialog did not complete.");
    }
}
