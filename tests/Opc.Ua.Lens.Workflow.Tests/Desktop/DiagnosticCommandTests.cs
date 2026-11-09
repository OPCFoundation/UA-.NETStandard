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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Moq;
using NUnit.Framework;
using UaLens.Storage;
using UaLens.Themes;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.Desktop
{
    [TestFixture]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    [NonParallelizable]
    public sealed class DiagnosticCommandTests
    {
        [TestCase("--probe-variant", "VARIANT PROBE PASS")]
        [TestCase("--probe-channel-drop", "CHANNEL DROP PROBE PASS")]
        [TestCase("--probe-scottplot", "SCOTTPLOT PROBE PASS")]
        public Task OfflineDiagnosticCommandsDispatchWithoutStartingTheApplication(string option, string expected)
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                using var output = new StringWriter(CultureInfo.InvariantCulture);
                TextWriter previous = Console.Out;
                try
                {
                    Console.SetOut(output);
                    int exitCode = await Program.Main([option]).ConfigureAwait(true);
                    Assert.That(exitCode, Is.Zero, output.ToString());
                    Assert.That(output.ToString(), Does.Contain(expected).And.Not.Contain("FAIL"));
                    Assert.That(DesktopInteraction.Owner.OwnedWindows, Is.Empty,
                        "A headless diagnostic must not accidentally enter the desktop application branch.");
                }
                finally
                {
                    Console.SetOut(previous);
                }
            });
        }

        /// <summary>
        /// Native smoke mode requires an explicit artifact destination before entering the desktop lifetime.
        /// </summary>
        [Test]
        public Task NativeViewportSmokeWithoutArtifactDirectoryIsRejectedBeforeApplicationStartup()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                using var error = new StringWriter(CultureInfo.InvariantCulture);
                TextWriter previous = Console.Error;
                try
                {
                    Console.SetError(error);
                    int exitCode = await Program.Main(["--smoke-native-viewport"]).ConfigureAwait(true);
                    Assert.That(exitCode, Is.EqualTo(2));
                    Assert.That(error.ToString(), Does.Contain("--smoke-artifact-directory"));
                    Assert.That(DesktopInteraction.Owner.OwnedWindows, Is.Empty);
                }
                finally
                {
                    Console.SetError(previous);
                }
            });
        }

        /// <summary>
        /// Smoke checks use the real shell and native renderer while retaining ownership of the caller's dispatcher.
        /// </summary>
        [TestCase(false)]
        [TestCase(true, Category = "LensOpenUsdNative")]
        public Task DesktopSmokeRendersRequestedSurfaceAndDrainsItsWorkspace(bool nativeViewport)
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                string root = Directory.CreateTempSubdirectory("UaLensSmoke").FullName;
                try
                {
                    var context = new DesktopConnectionContext();
                    await using (context.ConfigureAwait(true))
                    {
                        var model = new MainViewModel(context.Telemetry, context.Connection);
                        await using (model.ConfigureAwait(true))
                        {
                            var window = new MainWindow(
                                model, new AppearancePreferences(Path.Combine(root, "appearance.json")),
                                favoritesPath: Path.Combine(root, "favorites.json"),
                                inspectorPreferences: new InspectorPreferences(Path.Combine(root, "inspectors.json")));
                            await using (window.ConfigureAwait(true))
                            {
                                window.Show(DesktopInteraction.Owner);
                                var desktop = new Mock<IClassicDesktopStyleApplicationLifetime>(MockBehavior.Strict);
                                desktop.SetupProperty(value => value.ShutdownMode);
                                desktop.Setup(value => value.Shutdown(It.IsAny<int>())).Callback(window.Close);
                                var smoke = new DesktopSmokeTest(nativeViewport ? root : null);

                                smoke.Start(window, model, desktop.Object);
                                Task completion = smoke.Completion;
                                Assert.That(() => smoke.Start(window, model, desktop.Object),
                                    Throws.TypeOf<InvalidOperationException>().With.Message
                                        .EqualTo("The desktop smoke check has already started."));
                                Assert.That(smoke.Completion, Is.SameAs(completion));
                                await completion.WaitAsync(TimeSpan.FromSeconds(40)).ConfigureAwait(true);

                                desktop.Verify(value => value.Shutdown(0), Times.Once);
                                Assert.That(model.Workspace.IsClosing, Is.True);
                                Assert.That(model.Tabs, Is.Empty);
                                Assert.That(DesktopInteraction.Owner.IsVisible, Is.True);
                                if (nativeViewport)
                                {
                                    Assert.That(smoke.NativeViewportProof,
                                        Does.StartWith("UALENS_NATIVE_VIEWPORT_PROOF backend="));
                                    byte[] before = await File.ReadAllBytesAsync(
                                        Path.Combine(root, "native-before.png")).ConfigureAwait(true);
                                    byte[] after = await File.ReadAllBytesAsync(
                                        Path.Combine(root, "native-after.png")).ConfigureAwait(true);
                                    Assert.That(before, Is.Not.Empty.And.Not.EqualTo(after));
                                }
                                else
                                {
                                    Assert.That(smoke.NativeViewportProof, Is.Null);
                                    Assert.That(File.Exists(Path.Combine(root, "native-before.png")), Is.False);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });
        }

        /// <summary>
        /// An expired native deadline preserves cancellation and reports the inert viewport without publishing images.
        /// </summary>
        [Test]
        public Task NativeViewportExpiredDeadlineReportsContextWithoutStartingRendering()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                string root = Directory.CreateTempSubdirectory("UaLensSmokeDeadline").FullName;
                try
                {
                    var context = new DesktopConnectionContext();
                    await using (context.ConfigureAwait(true))
                    {
                        using var cancellation = new CancellationTokenSource();
                        await cancellation.CancelAsync().ConfigureAwait(true);

                        Task<string> rendering = DesktopSmokeTest.RenderNativeViewportAsync(
                            DesktopInteraction.Owner, context.Telemetry, root, cancellation.Token);
                        await Assert.ThatAsync(() => rendering, Throws.TypeOf<TimeoutException>())
                            .ConfigureAwait(true);

                        Exception? failure = rendering.Exception?.InnerException;
                        Assert.That(failure?.Message,
                            Does.StartWith("Native viewport deadline expired: backend=None, status=")
                                .And.Contain("frames=0, size=0x0")
                                .And.Contain("generations=0, allocations=0."));
                        Assert.That(failure?.InnerException, Is.InstanceOf<OperationCanceledException>()
                            .And.Property(nameof(OperationCanceledException.CancellationToken))
                                .EqualTo(cancellation.Token));
                        Assert.That(Directory.EnumerateFileSystemEntries(root), Is.Empty);
                        Assert.That(DesktopInteraction.Owner.OwnedWindows, Is.Empty);
                        Assert.That(DesktopInteraction.Owner.IsVisible, Is.True);
                    }
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });
        }

        /// <summary>
        /// A malformed compiled brush fails the smoke check while still draining the workspace and desktop lifetime.
        /// </summary>
        [Test]
        public Task DesktopSmokeInvalidThemeDrainsAndShutsDownWithFailure()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                string root = Directory.CreateTempSubdirectory("UaLensSmokeTheme").FullName;
                Application application = Application.Current!;
                bool hadResource = application.Resources.TryGetValue("TextOnAccent", out object? previous);
                try
                {
                    var context = new DesktopConnectionContext();
                    await using (context.ConfigureAwait(true))
                    {
                        var model = new MainViewModel(context.Telemetry, context.Connection);
                        await using (model.ConfigureAwait(true))
                        {
                            var window = new MainWindow(
                                model, new AppearancePreferences(Path.Combine(root, "appearance.json")),
                                favoritesPath: Path.Combine(root, "favorites.json"),
                                inspectorPreferences: new InspectorPreferences(Path.Combine(root, "inspectors.json")));
                            await using (window.ConfigureAwait(true))
                            {
                                window.Show(DesktopInteraction.Owner);
                                var desktop = new Mock<IClassicDesktopStyleApplicationLifetime>(MockBehavior.Strict);
                                desktop.SetupProperty(value => value.ShutdownMode);
                                desktop.Setup(value => value.Shutdown(It.IsAny<int>())).Callback(window.Close);
                                var smoke = new DesktopSmokeTest();
                                application.Resources["TextOnAccent"] = new LinearGradientBrush
                                {
                                    GradientStops =
                                    [
                                        new GradientStop(Colors.Black, 0),
                                        new GradientStop(Colors.White, 1)
                                    ]
                                };

                                smoke.Start(window, model, desktop.Object);
                                await Assert.ThatAsync(
                                    () => smoke.Completion.WaitAsync(TimeSpan.FromSeconds(10)),
                                    Throws.TypeOf<InvalidOperationException>().With.Message
                                        .EqualTo("The compiled theme resource 'TextOnAccent' did not load."))
                                    .ConfigureAwait(true);

                                desktop.Verify(value => value.Shutdown(1), Times.Once);
                                desktop.Verify(value => value.Shutdown(0), Times.Never);
                                Assert.That(model.Workspace.IsClosing, Is.True);
                                Assert.That(model.Tabs, Is.Empty);
                                Assert.That(smoke.NativeViewportProof, Is.Null);
                                Assert.That(window.IsVisible, Is.False);
                                Assert.That(DesktopInteraction.Owner.IsVisible, Is.True);
                            }
                        }
                    }
                }
                finally
                {
                    if (hadResource)
                    {
                        application.Resources["TextOnAccent"] = previous;
                    }
                    else
                    {
                        application.Resources.Remove("TextOnAccent");
                    }
                    Directory.Delete(root, recursive: true);
                }
            });
        }
    }
}
