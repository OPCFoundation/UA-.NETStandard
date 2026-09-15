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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using UaLens.Connection;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ConnectionSetupDialogWorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task ForwardUseSetupReturnsExactIntentWithoutNetworkOrListener(bool pinned)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            var initial = new ConnectionSetupSelection("opc.tcp://server.example.test:4840/Factory");
            var dialog = new ConnectionSetupDialog(context.Connection, initial, pinned);
            Task<ConnectionSetupSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner);
            try
            {
                TextBox endpoint = DesktopInteraction.Control<TextBox>(dialog, "SetupEndpointUrl");
                if (pinned)
                {
                    Assert.That(endpoint.IsEffectivelyEnabled, Is.False);
                    endpoint.Text = "opc.tcp://changed.example.test:4841/Other";
                }
                Assert.That(DesktopInteraction.Control<Button>(dialog, "UseConnectionSetupButton").IsEnabled, Is.True);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "TransportSetupReadiness").Text,
                    Does.Contain("Transport: Ready").And.Contain("External prerequisites: Pending"));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "UseConnectionSetupButton"));
                Assert.That(await prompt.ConfigureAwait(true), Is.EqualTo(initial));
                Assert.That(context.Discoveries, Is.Empty);
                Assert.That(context.ConfigurationsCreated, Is.Zero);
                Assert.That(context.Reverse.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
                Assert.That(context.Connection.CurrentSession, Is.Null);
                context.RuntimeFactory.Verify(f => f.Create(It.IsAny<ReverseConnectionProfile>()), Times.Never);
                VerifyNoConnect(context);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase(1, 1)]
    [TestCase(300, 60)]
    [TestCase(21, 17)]
    public Task ReverseBoundaryValuesReturnExactIntentWithoutStartingListener(int wait, int hold)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            ReverseConnectionProfile reverse = Reverse() with { WaitTimeoutSeconds = wait, HoldTimeSeconds = hold };
            var expected = new ConnectionSetupSelection(reverse.EndpointUrl, reverse);
            var dialog = new ConnectionSetupDialog(context.Connection, expected);
            Task<ConnectionSetupSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<Button>(dialog, "StartReverseListenerButton").IsEnabled,
                    Is.True);
                Assert.That(DesktopInteraction.Control<Button>(dialog, "WaitForReverseServerButton").IsEnabled,
                    Is.False);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "UseConnectionSetupButton"));
                Assert.That(await prompt.ConfigureAwait(true), Is.EqualTo(expected));
                Assert.That(context.Reverse.Snapshot.Profile, Is.Null);
                Assert.That(context.ConfigurationsCreated, Is.Zero);
                context.RuntimeFactory.Verify(f => f.Create(It.IsAny<ReverseConnectionProfile>()), Times.Never);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase("UnknownApplication")]
    [TestCase("UnknownScheme")]
    [TestCase("Listener")]
    [TestCase("ServerUri")]
    [TestCase("MissingTls")]
    [TestCase("WaitBelow")]
    [TestCase("WaitAbove")]
    [TestCase("HoldBelow")]
    [TestCase("HoldAbove")]
    public Task InvalidSavedSetupCannotBeAccepted(string invalid)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            ReverseConnectionProfile reverse = Reverse();
            ConnectionSetupSelection initial = invalid switch
            {
                "UnknownApplication" => new(reverse.EndpointUrl, ApplicationIdentityId: "removed"),
                "UnknownScheme" => new("opc.unsupported://server.example.test:4840/Factory"),
                "Listener" => new(reverse.EndpointUrl, reverse with { ListenerUrl = "not an absolute URL" }),
                "ServerUri" => new(reverse.EndpointUrl, reverse with { ServerUri = "not an application URI" }),
                "MissingTls" => new("wss://server.example.test:4840/Factory", reverse with
                {
                    EndpointUrl = "wss://server.example.test:4840/Factory",
                    ListenerUrl = "wss://localhost:4841/listener"
                }),
                "WaitBelow" => new(reverse.EndpointUrl, reverse with { WaitTimeoutSeconds = 0 }),
                "WaitAbove" => new(reverse.EndpointUrl, reverse with { WaitTimeoutSeconds = 301 }),
                "HoldBelow" => new(reverse.EndpointUrl, reverse with { HoldTimeSeconds = 0 }),
                _ => new(reverse.EndpointUrl, reverse with { HoldTimeSeconds = 61 })
            };
            var dialog = new ConnectionSetupDialog(context.Connection, initial, pinned: true);
            Task<ConnectionSetupSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<Button>(dialog, "UseConnectionSetupButton").IsEnabled, Is.False);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "TransportSetupReadiness").Text,
                    Does.Contain("Blocked").Or.Contain("blocked"));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await prompt.ConfigureAwait(true), Is.Null);
                Assert.That(context.Discoveries, Is.Empty);
                context.RuntimeFactory.Verify(f => f.Create(It.IsAny<ReverseConnectionProfile>()), Times.Never);
                VerifyNoConnect(context);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase("ReverseWaitSeconds")]
    [TestCase("ReverseHoldSeconds")]
    public Task FractionalTimeoutCannotBeAcceptedUntilCorrected(string field)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            ReverseConnectionProfile reverse = Reverse();
            var dialog = new ConnectionSetupDialog(
                context.Connection, new ConnectionSetupSelection(reverse.EndpointUrl, reverse));
            Task<ConnectionSetupSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner);
            try
            {
                NumericUpDown value = DesktopInteraction.Control<NumericUpDown>(dialog, field);
                value.Value = 1.5m;
                Assert.That(DesktopInteraction.Control<Button>(dialog, "UseConnectionSetupButton").IsEnabled, Is.False);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "TransportSetupReadiness").Text,
                    Does.Contain("whole number"));
                value.Value = 9;
                Assert.That(DesktopInteraction.Control<Button>(dialog, "UseConnectionSetupButton").IsEnabled, Is.True);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "UseConnectionSetupButton"));
                ConnectionSetupSelection? result = await prompt.ConfigureAwait(true);
                Assert.That(result!.ReverseConnection!.WaitTimeoutSeconds,
                    Is.EqualTo(field == "ReverseWaitSeconds" ? 9 : 20));
                Assert.That(result.ReverseConnection.HoldTimeSeconds,
                    Is.EqualTo(field == "ReverseHoldSeconds" ? 9 : 15));
                Assert.That(context.Reverse.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task ReverseStartWaitCancelAndStopReflectOwnedRuntimeState()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            ReverseConnectionProfile reverse = Reverse();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiting = new TaskCompletionSource<ITransportWaitingConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken waitToken = default;
            context.Runtime.Setup(r => r.StartAsync(It.IsAny<CancellationToken>())).Returns(start.Task);
            context.Runtime.Setup(r => r.WaitAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    waitToken = token;
                    return waiting.Task.WaitAsync(token);
                });
            context.DiscoverAsync = async (setup, token) =>
            {
                await context.Reverse.WaitAsync(setup.ReverseConnection!, token).ConfigureAwait(false);
                return [];
            };
            var dialog = new ConnectionSetupDialog(
                context.Connection, new ConnectionSetupSelection(reverse.EndpointUrl, reverse));
            Task<ConnectionSetupSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner);
            try
            {
                Button wait = DesktopInteraction.Control<Button>(dialog, "WaitForReverseServerButton");
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "StartReverseListenerButton"));
                Assert.That(context.Reverse.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Starting));
                Assert.That(DesktopInteraction.Control<Button>(dialog, "UseConnectionSetupButton").IsEnabled, Is.False);
                await DesktopInteraction.ChangedAsync(wait, () => wait.IsEnabled, () =>
                {
                    start.SetResult();
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(context.Reverse.Snapshot.Profile, Is.EqualTo(reverse));
                Assert.That(context.Reverse.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Listening));
                DesktopInteraction.Click(wait);
                Assert.That(context.Reverse.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Waiting));
                Assert.That(DesktopInteraction.Control<Button>(dialog, "CancelReverseWaitButton").IsEnabled, Is.True);
                await DesktopInteraction.ChangedAsync(wait, () => wait.IsEnabled, () =>
                {
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelReverseWaitButton"));
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(waitToken.IsCancellationRequested, Is.True);
                Assert.That(context.Reverse.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Listening));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ReverseConnectionStatus").Text,
                    Does.StartWith("Discovery canceled."));
                Button startButton = DesktopInteraction.Control<Button>(dialog, "StartReverseListenerButton");
                await DesktopInteraction.ChangedAsync(startButton, () => startButton.IsEnabled, () =>
                {
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "StopReverseListenerButton"));
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(context.Reverse.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
                Assert.That(dialog.DiscoverySetup, Is.Null);
                Assert.That(dialog.DiscoveredEndpoints.IsNull, Is.True);
                context.Runtime.Verify(r => r.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
                context.Runtime.Verify(r => r.WaitAsync(It.IsAny<CancellationToken>()), Times.Once);
                context.Runtime.Verify(r => r.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
                VerifyNoConnect(context);
            }
            finally
            {
                start.TrySetResult();
                waiting.TrySetCanceled();
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task DiscoveryPublishesOnlyMatchingSetupRevision(bool changeSetup)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            var initial = new ConnectionSetupSelection("opc.tcp://server.example.test:4840/Factory");
            var endpoint = new EndpointDescription(initial.EndpointUrl)
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            var completion = new TaskCompletionSource<ArrayOf<EndpointDescription>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.DiscoverAsync = (_, _) => completion.Task;
            var dialog = new ConnectionSetupDialog(context.Connection, initial);
            Task<ConnectionSetupSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner);
            try
            {
                Button wait = DesktopInteraction.Control<Button>(dialog, "WaitForReverseServerButton");
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                DesktopInteraction.Click(wait);
                Assert.That(context.Discoveries, Is.EqualTo(new[] { initial }));
                Assert.That(wait.IsEnabled, Is.False);
                if (changeSetup)
                {
                    DesktopInteraction.Control<TextBox>(dialog, "SetupEndpointUrl").Text =
                        "opc.tcp://other.example.test:4840/Other";
                }
                await DesktopInteraction.ChangedAsync(wait, () => wait.IsEnabled, async () =>
                {
                    await Dispatcher.UIThread.InvokeAsync(() => completion.SetResult([endpoint]),
                        DispatcherPriority.Background);
                }).ConfigureAwait(true);
                if (changeSetup)
                {
                    Assert.That(dialog.DiscoveredEndpoints.IsNull, Is.True);
                    Assert.That(dialog.DiscoverySetup, Is.Null);
                    Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ReverseConnectionStatus").Text,
                        Does.Contain("setup changed during discovery"));
                }
                else
                {
                    Assert.That(dialog.DiscoverySetup, Is.EqualTo(initial),
                        DesktopInteraction.Control<TextBlock>(dialog, "ReverseConnectionStatus").Text);
                    Assert.That(dialog.DiscoveredEndpoints.Count, Is.EqualTo(1));
                    Assert.That(dialog.DiscoveredEndpoints[0].SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(dialog.DiscoveredEndpoints[0].SecurityPolicyUri,
                        Is.EqualTo(SecurityPolicies.Basic256Sha256));
                }
                Assert.That(context.Connection.CurrentSession, Is.Null);
                VerifyNoConnect(context);
            }
            finally
            {
                completion.TrySetResult([]);
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase(false, false, 1)]
    [TestCase(false, true, 0)]
    [TestCase(true, false, 0)]
    [TestCase(true, true, 0)]
    public Task ClosingStopsOnlyNewlyOwnedMatchingListener(bool preexisting, bool accept, int stops)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            ReverseConnectionProfile reverse = Reverse();
            if (preexisting)
            {
                await context.Reverse.StartAsync(reverse).ConfigureAwait(true);
            }
            var initial = new ConnectionSetupSelection(reverse.EndpointUrl, reverse);
            var dialog = new ConnectionSetupDialog(context.Connection, initial);
            Task<ConnectionSetupSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner);
            try
            {
                if (!preexisting)
                {
                    Button wait = DesktopInteraction.Control<Button>(dialog, "WaitForReverseServerButton");
                    await DesktopInteraction.ChangedAsync(wait, () => wait.IsEnabled, () =>
                    {
                        DesktopInteraction.Click(
                            DesktopInteraction.Control<Button>(dialog, "StartReverseListenerButton"));
                        return Task.CompletedTask;
                    }).ConfigureAwait(true);
                }
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(
                    dialog, accept ? "UseConnectionSetupButton" : "CancelButton"));
                Assert.That(await prompt.ConfigureAwait(true), accept ? Is.EqualTo(initial) : Is.Null);
                context.Runtime.Verify(r => r.StopAsync(It.IsAny<CancellationToken>()), Times.Exactly(stops));
                Assert.That(context.Reverse.Snapshot.Phase,
                    Is.EqualTo(stops == 0 ? ReverseConnectionPhase.Listening : ReverseConnectionPhase.Stopped));
                Assert.That(context.Connection.CurrentSession, Is.Null);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task DiscoveryFailureRestoresCommandsWithoutAlternateConnection()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            context.DiscoverAsync = (_, _) =>
                Task.FromException<ArrayOf<EndpointDescription>>(new IOException("controlled discovery failure"));
            var dialog = new ConnectionSetupDialog(context.Connection,
                new ConnectionSetupSelection("opc.tcp://server.example.test:4840/Factory"));
            Task<ConnectionSetupSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner);
            try
            {
                Button wait = DesktopInteraction.Control<Button>(dialog, "WaitForReverseServerButton");
                DesktopInteraction.Click(wait);
                Assert.That(wait.IsEnabled, Is.True);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ReverseConnectionStatus").Text,
                    Is.EqualTo("No matching discovery result: controlled discovery failure"));
                Assert.That(dialog.DiscoveredEndpoints.IsNull, Is.True);
                Assert.That(context.Discoveries, Has.Count.EqualTo(1));
                VerifyNoConnect(context);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task ConfiguredApplicationAndTlsSelectionReturnsReferencesWithoutAcquiringKeys()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var certificates = new Mock<ICertificateProvider>(MockBehavior.Strict);
            var passwords = new Mock<ICertificatePasswordProvider>(MockBehavior.Strict);
            var source = new ConfiguredCertificateSource("application-source", "Application source",
                new CertificateIdentifier(), certificates.Object,
                [new ConfiguredCertificatePasswordSource("application-access", "Configured access", passwords.Object)],
                CryptoPurpose.ApplicationInstanceKey);
            var reference = new CertificateIdentityReference
            {
                SourceId = source.Id,
                PasswordSourceId = "application-access",
                SubjectName = "CN=Controlled application"
            };
            var application = new ConfiguredApplicationIdentity(
                "application", "Selected application", source, reference,
                (_, _, _) => throw new InvalidOperationException("Setup must not acquire an application key."));
            var tls = new ConfiguredReverseTlsSource("listener-tls", "Configured listener TLS",
                _ => throw new InvalidOperationException("Setup must not acquire TLS material."));
            await using var context = new DesktopConnectionContext(
                new ConnectionConfigurationCatalog([application], [tls]));
            ReverseConnectionProfile reverse = Reverse() with
            {
                EndpointUrl = "wss://server.example.test:4840/Factory",
                ListenerUrl = "wss://localhost:4841/listener"
            };
            var dialog = new ConnectionSetupDialog(context.Connection,
                new ConnectionSetupSelection(reverse.EndpointUrl, reverse));
            Task<ConnectionSetupSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<Button>(dialog, "UseConnectionSetupButton").IsEnabled, Is.False);
                DesktopInteraction.Control<ComboBox>(dialog, "ListenerTlsConfiguration").SelectedIndex = 1;
                DesktopInteraction.Control<ComboBox>(dialog, "ApplicationIdentityConfiguration").SelectedIndex = 1;
                Assert.That(DesktopInteraction.Control<Button>(dialog, "UseConnectionSetupButton").IsEnabled, Is.True);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "UseConnectionSetupButton"));
                ConnectionSetupSelection? result = await prompt.ConfigureAwait(true);
                Assert.That(result!.ApplicationIdentityId, Is.EqualTo("application"));
                Assert.That(result.ReverseConnection!.TlsConfigurationId, Is.EqualTo("listener-tls"));
                Assert.That(result.ReverseConnection.ServerUri, Is.EqualTo("urn:unit:test:reverse"));
                Assert.That(reverse.TlsConfigurationId, Is.Null);
                Assert.That(context.ConfigurationsCreated, Is.Zero);
                Assert.That(context.Reverse.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
                certificates.VerifyNoOtherCalls();
                passwords.VerifyNoOtherCalls();
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task ListenerStartFailureOrCancellationReleasesRuntimeAndRestoresCommands(bool cancel)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken startToken = default;
            context.Runtime.Setup(r => r.StartAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    startToken = token;
                    return pending.Task.WaitAsync(token);
                });
            ReverseConnectionProfile reverse = Reverse();
            var dialog = new ConnectionSetupDialog(context.Connection,
                new ConnectionSetupSelection(reverse.EndpointUrl, reverse));
            Task<ConnectionSetupSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner);
            try
            {
                Button start = DesktopInteraction.Control<Button>(dialog, "StartReverseListenerButton");
                DesktopInteraction.Click(start);
                Assert.That(start.IsEnabled, Is.False);
                await DesktopInteraction.ChangedAsync(start, () => start.IsEnabled, () =>
                {
                    if (cancel)
                    {
                        DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelReverseWaitButton"));
                    }
                    else
                    {
                        pending.SetException(new IOException("controlled startup failure"));
                    }
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(startToken.IsCancellationRequested, Is.EqualTo(cancel));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ReverseConnectionStatus").Text,
                    Is.EqualTo(cancel ? "Listener start canceled." :
                        "Listener did not start: controlled startup failure"));
                Assert.That(context.Reverse.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
                Assert.That(DesktopInteraction.Control<Button>(dialog, "WaitForReverseServerButton").IsEnabled,
                    Is.False);
                context.Runtime.Verify(r => r.DisposeAsync(), Times.Once);
                context.Runtime.Verify(r => r.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
                Assert.That(context.Discoveries, Is.Empty);
            }
            finally
            {
                pending.TrySetResult();
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task ClosingDoesNotStopAReplacementListenerOwnedElsewhere()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            ReverseConnectionProfile original = Reverse();
            ReverseConnectionProfile replacement = original with { ServerUri = "urn:unit:test:replacement" };
            var otherRuntime = new Mock<IReverseConnectionRuntime>(MockBehavior.Strict);
            otherRuntime.Setup(r => r.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            otherRuntime.Setup(r => r.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            otherRuntime.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
            context.RuntimeFactory.Setup(f => f.Create(replacement)).Returns(otherRuntime.Object);
            var dialog = new ConnectionSetupDialog(context.Connection,
                new ConnectionSetupSelection(original.EndpointUrl, original));
            Task<ConnectionSetupSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner);
            try
            {
                Button wait = DesktopInteraction.Control<Button>(dialog, "WaitForReverseServerButton");
                await DesktopInteraction.ChangedAsync(wait, () => wait.IsEnabled, () =>
                {
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "StartReverseListenerButton"));
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                await context.Reverse.StopAsync().ConfigureAwait(true);
                await context.Reverse.StartAsync(replacement).ConfigureAwait(true);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await prompt.ConfigureAwait(true), Is.Null);
                Assert.That(context.Reverse.Snapshot.Profile, Is.EqualTo(replacement));
                Assert.That(context.Reverse.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Listening));
                context.Runtime.Verify(r => r.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
                otherRuntime.Verify(r => r.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
                otherRuntime.Verify(r => r.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    private static ReverseConnectionProfile Reverse()
    {
        return new ReverseConnectionProfile
        {
            ListenerUrl = "opc.tcp://localhost:4841/listener",
            ServerUri = "urn:unit:test:reverse",
            EndpointUrl = "opc.tcp://server.example.test:4840/Factory"
        };
    }

    private static void VerifyNoConnect(DesktopConnectionContext context)
    {
        context.Backend.Verify(b => b.ConnectAsync(It.IsAny<ApplicationConfiguration>(),
            It.IsAny<EndpointDescription>(), It.IsAny<ConnectionProfile>(), It.IsAny<IClientIdentityProvider>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
