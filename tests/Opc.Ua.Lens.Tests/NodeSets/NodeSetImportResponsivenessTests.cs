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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Moq;
using NUnit.Framework;
using UaLens.NodeSets.Loading;
using UaLens.Tests.Desktop;
using UaLens.Views;

namespace UaLens.Tests.NodeSets
{
    [TestFixture]
    [Explicit("Requires a native desktop and UALENS_NODESET_REPRO_FILE pointing to a local model.")]
    [Category("LensNodeSetsDesktop")]
    [NonParallelizable]
    public sealed class NodeSetImportResponsivenessTests
    {
        [Test]
        public async Task SuppliedFileImportKeepsDispatcherAlive()
        {
            string? path = Environment.GetEnvironmentVariable("UALENS_NODESET_REPRO_FILE");
            if (string.IsNullOrWhiteSpace(path))
            {
                Assert.Ignore("Set UALENS_NODESET_REPRO_FILE to the model to reproduce.");
            }
            Assert.That(File.Exists(path), Is.True);
            var opening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task desktop = DesktopApplication.RunAsync(async () =>
            {
                var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
                var file = new Mock<IStorageFile>(MockBehavior.Strict);
                file.SetupGet(value => value.Path).Returns(new Uri(Path.GetFullPath(path!)));
                file.SetupGet(value => value.Name).Returns(Path.GetFileName(path));
                storage.Setup(value => value.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()))
                    .ReturnsAsync([file.Object]);
                var repository = new Mock<INodeSetRepository>(MockBehavior.Strict);
                await using DesktopWindowScope scope = await DesktopWindowScope
                    .OpenAsync(storage.Object, repository.Object).ConfigureAwait(true);
                opening.TrySetResult();
                scope.Window.FindControl<MenuItem>("MenuOpenNodeSets")!
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var elapsed = Stopwatch.StartNew();
                while (scope.ViewModel.IsLoadingNodeSets && elapsed.Elapsed < TimeSpan.FromSeconds(20))
                {
                    if (scope.Window.OwnedWindows.OfType<NodeSetDependencyDialog>().FirstOrDefault() is { } dialog)
                    {
                        await DesktopWindowScope.FrameAsync(dialog).ConfigureAwait(true);
                        TestContext.Progress.WriteLine("A visible dependency prompt was reached.");
                        DesktopWindowScope.Click(dialog.GetVisualDescendants().OfType<Button>()
                            .Single(button => button.Name == "CancelDependencyButton"));
                    }
                    await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                }
                Assert.That(scope.ViewModel.IsLoadingNodeSets, Is.False, "Import did not finish or allow cancellation.");
                Assert.That(scope.Window.IsEnabled, Is.True, "The main window remained disabled after import.");
                Assert.That(scope.Window.OwnedWindows, Is.Empty);
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                TestContext.Progress.WriteLine($"Import state: {scope.ViewModel.ConnectionStatus}");
            });
            await opening.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            while (!desktop.IsCompleted)
            {
                Task heartbeat = Dispatcher.UIThread.InvokeAsync(
                    () => { }, DispatcherPriority.Background).GetTask();
                Task observed = await Task.WhenAny(heartbeat, desktop).WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);
                await observed.ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);
            }
            await desktop.ConfigureAwait(false);
        }
    }
}
