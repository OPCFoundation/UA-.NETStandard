// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Pcap.Bindings;
using UaLens.Diagnostics.Capture;
using UaLens.Tests.Desktop;
using UaLens.Views;

namespace UaLens.Tests.Diagnose
{
    [TestFixture]
    [Platform("Win,Linux")]
    [Category("LensDesktopMainline")]
    [NonParallelizable]
    public sealed class DiagnosticCaptureWindowTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeCaptureWindowRequiresConsentAndDrainsOnCloseAsync(bool record)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "UaLens-capture-view-" + Guid.NewGuid());
                Directory.CreateDirectory(root);
                var registry = new ChannelCaptureRegistry();
                var captures = new DiagnosticCaptureWorkspace(DefaultTelemetry.Create(static _ => { }), registry,
                    newFolder: () => Path.Combine(root, Guid.NewGuid().ToString("N")));
                try
                {
                    await using (captures.ConfigureAwait(true))
                    {
                        var window = new DiagnosticCaptureWindow(captures);
                        await using (window.ConfigureAwait(true))
                        {
                            Task showing = Task.CompletedTask;
                            await DesktopInteraction.OpenedAsync<DiagnosticCaptureWindow>(
                                () => showing = window.ShowDialog(DesktopInteraction.Owner)).ConfigureAwait(true);
                            Button start = DesktopInteraction.Control<Button>(window, "StartCaptureButton");
                            Assert.That(start.IsEnabled, Is.False);
                            Assert.That(registry.CurrentObserver, Is.Null);
                            Assert.That(Directory.GetFileSystemEntries(root), Is.Empty);
                            if (record)
                            {
                                DesktopInteraction.Control<NumericUpDown>(window, "FramesInput").Value = 1;
                                DesktopInteraction.Control<NumericUpDown>(window, "BytesInput").Value = 8;
                                DesktopInteraction.Control<CheckBox>(window, "PacketConsent").IsChecked = true;
                                await DesktopInteraction.ChangedAsync(
                                    start, () => start.IsEnabled, () => Task.CompletedTask).ConfigureAwait(true);
                                DesktopInteraction.Click(start);
                                await window.Operation.ConfigureAwait(true);
                                Assert.That(window.OperationError, Is.Empty);
                                Assert.That(registry.CurrentObserver, Is.Not.Null);
                                registry.CurrentObserver!.OnFrameSent(1, s_chunk);
                            }
                            window.Close();
                            await showing.ConfigureAwait(true);
                        }
                        Assert.That(registry.CurrentObserver, Is.Null);
                        DiagnosticCaptureSnapshot snapshot = captures.Snapshot();
                        Assert.That(snapshot.Phase, Is.EqualTo(record
                            ? DiagnosticCapturePhase.Completed : DiagnosticCapturePhase.Empty), snapshot.Status);
                        Assert.That(snapshot.Frames, Is.EqualTo(record ? 1 : 0));
                    }
                    Assert.That(Directory.GetFileSystemEntries(root), Is.Empty);
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            }).ConfigureAwait(false);
        }

        private static readonly byte[] s_chunk = [(byte)'H', (byte)'E', (byte)'L', (byte)'F', 8, 0, 0, 0];
    }
}
