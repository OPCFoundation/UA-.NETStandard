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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.Subscriptions;

[TestFixture]
[Category("LensDesktop")]
[NonParallelizable]
public sealed class MonitorStyleDesktopTests
{
    [Test]
    public Task KeyboardAndPointerSelectEveryNamedLaneStyleWithoutChangingConfigurationOrRecords()
    {
        return DesktopApplication.RunAsync(async () =>
        {
            var model = new SubscriptionViewModel("Monitor", null, NullLogger.Instance);
            await using var lifetime = model.ConfigureAwait(true);
            await model.AddItemAsync(new MonitoredItemConfig
            {
                NodeId = new NodeId("A", 0),
                DisplayName = "First complete item name"
            }).ConfigureAwait(true);
            await model.AddItemAsync(new MonitoredItemConfig
            {
                NodeId = new NodeId("B", 0),
                DisplayName = "Second complete item name"
            }).ConfigureAwait(true);
            model.Recorder.Record(new NotificationEvent(NotificationKind.DataChange, model.Items[0].Id,
                1, 1, DateTime.UtcNow, 42, model.Items[0].DisplayName, model.Items[0].NodeId.ToString()));
            MonitoredItemConfig[] originalItems = [.. model.Items];
            SubscriptionConfig originalSubscription = model.Subscription;
            ArrayOf<NotificationEvent> originalRecords = model.Recorder.Snapshot();
            var view = (SubscriptionDocumentView)((IPlugin)model).View!;
            var window = new Window { Content = view, Width = 960, Height = 800 };
            try
            {
                window.Show();
                view.FindControl<ComboBox>("DocumentViewMode")!.SelectedIndex = 4;
                await DesktopWindowScope.FrameAsync(window).ConfigureAwait(true);
                ComboBox items = view.FindControl<ComboBox>("LaneItemSelector")!;
                ComboBox styles = view.FindControl<ComboBox>("LaneStyleSelector")!;
                AnimationCanvas canvas = view.FindControl<AnimationCanvas>("DocumentAnimation")!;
                Assert.That(items.Focus(), Is.True);
                DesktopWindowScope.Key(items, Key.Down);
                Assert.That(items.SelectedItem, Is.EqualTo(originalItems[1]));
                Assert.That(ControlAutomationPeer.CreatePeerForElement(items).GetName(),
                    Does.Contain(originalItems[1].DisplayName));
                Assert.That(styles.Focus(), Is.True);
                foreach (LineStyle style in Enum.GetValues<LineStyle>())
                {
                    if (style != LineStyle.Interpolated)
                    {
                        DesktopWindowScope.Key(styles, Key.Down);
                    }
                    Assert.That(styles.SelectedIndex, Is.EqualTo((int)style));
                    Assert.That(canvas.GetLineStyle(originalItems[1].Id), Is.EqualTo(style));
                    Assert.That(ControlAutomationPeer.CreatePeerForElement(styles).GetName(),
                        Does.Contain(originalItems[1].DisplayName).And.Contain(style.ToString()));
                    Assert.That(
                        ControlAutomationPeer.CreatePeerForElement((ComboBoxItem)styles.SelectedItem!).GetName(),
                        Is.EqualTo(style.ToString()));
                }

                await DesktopWindowScope.FrameAsync(window).ConfigureAwait(true);
                // The offline canvas has a 52px header, two item lanes and one keep-alive lane.
                Point local = new(canvas.Bounds.Width - 9, 52 + ((canvas.Bounds.Height - 56) / 6));
                Point position = canvas.TranslatePoint(local, window)!.Value;
                var pointer = new Mock<IPointer>();
                pointer.SetupGet(value => value.Type).Returns(PointerType.Mouse);
                var pressed = new PointerPressedEventArgs(canvas, pointer.Object, window, position, 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                    KeyModifiers.None);
                canvas.RaiseEvent(pressed);
                Assert.That(pressed.Handled, Is.True,
                    "The rendered first-lane label must handle its pointer shortcut.");
                Assert.That(items.SelectedItem, Is.EqualTo(originalItems[0]));
                Assert.That(styles.SelectedIndex, Is.EqualTo((int)LineStyle.Wave));
                Assert.That(ControlAutomationPeer.CreatePeerForElement(styles).GetName(),
                    Does.Contain(originalItems[0].DisplayName).And.Contain("Wave"));
                DesktopWindowScope.Key(styles, Key.Down);
                Assert.That(canvas.GetLineStyle(originalItems[0].Id), Is.EqualTo(LineStyle.Zigzag));

                Assert.That(model.Subscription, Is.EqualTo(originalSubscription));
                Assert.That(model.Items, Is.EqualTo(originalItems));
                Assert.That(model.Recorder.Snapshot().ToList(), Is.EqualTo(originalRecords.ToList()));
                Assert.That(model.Recorder.TotalWritten, Is.EqualTo(1));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Test]
    public Task MonitorParametersExposeNamesAndUnitsIndependentOfValues()
    {
        return DesktopApplication.RunAsync(async () =>
        {
            var model = new SubscriptionViewModel("Monitor", null, NullLogger.Instance);
            await using var lifetime = model.ConfigureAwait(true);
            var view = (SubscriptionDocumentView)((IPlugin)model).View!;
            NumericUpDown interval = view.FindControl<NumericUpDown>("PublishingIntervalInput")!;
            ComboBox mode = view.FindControl<ComboBox>("DocumentViewMode")!;
            TextBox node = view.FindControl<TextBox>("MonitoredNodeIdInput")!;
            interval.Value = 123;
            node.Text = "ns=2;s=value";
            Assert.That(ControlAutomationPeer.CreatePeerForElement(interval).GetName(),
                Is.EqualTo("Publishing interval (milliseconds)"));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(mode).GetName(), Is.EqualTo("Monitor view"));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(node).GetName(), Is.EqualTo("Monitored node id"));
            interval.Value = 500;
            node.Text = "i=2258";
            Assert.That(ControlAutomationPeer.CreatePeerForElement(interval).GetName(),
                Is.EqualTo("Publishing interval (milliseconds)"));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(node).GetName(), Is.EqualTo("Monitored node id"));
        });
    }
}
