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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NUnit.Framework;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[Explicit("Requires a dedicated real-desktop test process.")]
[Category("LensDesktopWorkflow")]
[NonParallelizable]
public sealed class NodeAttributesViewWorkflowTests
{
    [Test]
    [Platform("Win")]
    public async Task WindowsClipboardWriterStoresTextWithoutBlocking()
    {
        string expected = $"UaLens attribute clipboard {Guid.NewGuid():N}";

        await ClipboardTextWriter.SetTextAsync(null, expected)
            .WaitAsync(DesktopApplication.Timeout)
            .ConfigureAwait(false);

        string? actual = await Task.Run(ReadWindowsClipboardText).ConfigureAwait(false);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public Task ShellLeavesKeyValueCopyGestureToTheAttributesInspector()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
            await using (scope.ConfigureAwait(true))
            {
                scope.ViewModel.AttributesPanelMode = SidePanelMode.AttrsOnly;
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                var row = new AttributeRow("DisplayName", "Pump A");
                scope.ViewModel.Attributes.Rows.Add(row);
                ListBox list = DesktopInteraction.Control<ListBox>(scope.Window, "AttributesList");
                NodeAttributesView view = scope.Window.GetVisualDescendants()
                    .OfType<NodeAttributesView>()
                    .Single();
                var copied = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                view.ClipboardWriter = text =>
                {
                    copied.TrySetResult(text);
                    return Task.CompletedTask;
                };
                list.SelectedItem = row;

                KeyEventArgs input = DesktopWindowScope.Key(
                    list,
                    Key.C,
                    KeyModifiers.Control | KeyModifiers.Shift);

                Assert.That(
                    await copied.Task.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true),
                    Is.EqualTo("DisplayName: Pump A"));
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(input.Handled, Is.True);
                Assert.That(scope.ViewModel.Tabs, Is.Empty);
            }
        });
    }

    [Test]
    public Task CopyShortcutsUseTheActiveRowAndExpectedFormats()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var context = new DesktopConnectionContext();
            try
            {
                using var model = new NodeAttributesViewModel(
                    context.Telemetry, context.Connection, InlineWorkspaceDispatcher.Instance);
                var first = new AttributeRow("NodeId", "ns=2;s=Pump");
                var second = new AttributeRow("DisplayName", "Pump A");
                model.Rows.Add(first);
                model.Rows.Add(second);

                string? copied = null;
                var view = new NodeAttributesView
                {
                    DataContext = model,
                    ClipboardWriter = text =>
                    {
                        copied = text;
                        return Task.CompletedTask;
                    }
                };
                DesktopInteraction.Owner.Content = view;
                try
                {
                    ListBox list = DesktopInteraction.Control<ListBox>(view, "AttributesList");
                    list.SelectedItem = second;

                    KeyEventArgs valueCopy = Press(list, KeyModifiers.Control);
                    Assert.That(valueCopy.Handled, Is.True);
                    Assert.That(copied, Is.EqualTo("Pump A"));

                    KeyEventArgs keyValueCopy = Press(list, KeyModifiers.Meta | KeyModifiers.Shift);
                    Assert.That(keyValueCopy.Handled, Is.True);
                    Assert.That(copied, Is.EqualTo("DisplayName: Pump A"));

                    copied = null;
                    list.SelectedItem = null;
                    KeyEventArgs noSelection = Press(list, KeyModifiers.Control);
                    Assert.That(noSelection.Handled, Is.False);
                    Assert.That(copied, Is.Null);
                }
                finally
                {
                    DesktopInteraction.Owner.Content = null;
                }
            }
            finally
            {
                await context.DisposeAsync().ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task ContextMenuCopiesTheRightClickedStatusRow()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var context = new DesktopConnectionContext();
            try
            {
                using var model = new NodeAttributesViewModel(
                    context.Telemetry, context.Connection, InlineWorkspaceDispatcher.Instance);
                var value = new AttributeRow("Value", "42");
                var status = new AttributeRow("(read failed)", "connection closed");
                model.Rows.Add(value);
                model.Rows.Add(status);

                ContextMenu? menu = null;
                var view = new NodeAttributesView
                {
                    DataContext = model
                };
                DesktopInteraction.Owner.Content = view;
                try
                {
                    ListBox list = DesktopInteraction.Control<ListBox>(view, "AttributesList");
                    list.SelectedItem = value;
                    DesktopInteraction.Owner.UpdateLayout();
                    Grid statusRow = list.GetVisualDescendants()
                        .OfType<Grid>()
                        .Single(grid => ReferenceEquals(grid.DataContext, status));
                    Point position = statusRow.TranslatePoint(new Point(1, 1), DesktopInteraction.Owner)
                        ?? throw new AssertionException("The attribute row is not attached to the owned desktop.");
                    using var pointer = new Pointer(31, PointerType.Mouse, isPrimary: true);
                    var pressed = new PointerPressedEventArgs(
                        statusRow,
                        pointer,
                        DesktopInteraction.Owner,
                        position,
                        0,
                        new PointerPointProperties(
                            RawInputModifiers.RightMouseButton,
                            PointerUpdateKind.RightButtonPressed),
                        KeyModifiers.None,
                        1);

                    statusRow.RaiseEvent(pressed);

                    Assert.That(list.SelectedItem, Is.SameAs(status));
                    menu = list.ContextMenu!;
                    list.RaiseEvent(new ContextRequestedEventArgs());
                    Assert.That(menu.IsOpen, Is.True);
                    MenuItem[] actions = menu.Items.OfType<MenuItem>().ToArray();
                    Assert.That(actions.Select(action => action.Header), Is.EqualTo(
                        new object[] { "Copy _value", "Copy _key and value" }));

                    var valueCopied = new TaskCompletionSource<string>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    view.ClipboardWriter = text =>
                    {
                        Assert.That(menu.IsOpen, Is.False);
                        valueCopied.TrySetResult(text);
                        return Task.CompletedTask;
                    };
                    actions[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert.That(
                        await valueCopied.Task.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true),
                        Is.EqualTo("connection closed"));

                    list.RaiseEvent(new ContextRequestedEventArgs());
                    Assert.That(menu.IsOpen, Is.True);
                    var keyValueCopied = new TaskCompletionSource<string>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    view.ClipboardWriter = text =>
                    {
                        Assert.That(menu.IsOpen, Is.False);
                        keyValueCopied.TrySetResult(text);
                        return Task.CompletedTask;
                    };
                    actions[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert.That(
                        await keyValueCopied.Task.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true),
                        Is.EqualTo("(read failed): connection closed"));
                }
                finally
                {
                    DesktopInteraction.Owner.Content = null;
                }
            }
            finally
            {
                await context.DisposeAsync().ConfigureAwait(true);
            }
        });
    }

    private static KeyEventArgs Press(ListBox list, KeyModifiers modifiers)
    {
        var input = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = list,
            Key = Key.C,
            KeyModifiers = modifiers
        };
        list.RaiseEvent(input);
        return input;
    }

    private static string? ReadWindowsClipboardText()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                Thread.Sleep(50);
                continue;
            }

            try
            {
                IntPtr memory = GetClipboardData(13);
                if (memory == IntPtr.Zero)
                {
                    return null;
                }

                IntPtr text = GlobalLock(memory);
                if (text == IntPtr.Zero)
                {
                    return null;
                }
                try
                {
                    return Marshal.PtrToStringUni(text);
                }
                finally
                {
                    _ = GlobalUnlock(memory);
                }
            }
            finally
            {
                _ = CloseClipboard();
            }
        }
        return null;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr newOwner);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);
}
