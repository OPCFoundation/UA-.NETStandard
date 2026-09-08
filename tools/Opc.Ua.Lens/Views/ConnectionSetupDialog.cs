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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Opc.Ua;
using UaLens.Connection;
using Orientation = Avalonia.Layout.Orientation;

namespace UaLens.Views;

/// <summary>
/// Configures real registered transports and an app-owned reverse listener.
/// Start/Wait/Cancel/Stop are explicit operations; restoring this dialog's safe
/// intent never starts a socket. Newly started listeners are stopped on cancel.
/// </summary>
internal sealed class ConnectionSetupDialog : Window
{
    public ConnectionSetupDialog(
        ConnectionService connection,
        ConnectionSetupSelection initial,
        bool pinned = false)
    {
        m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
        m_backend = connection.ConfiguredBackend ??
            throw new NotSupportedException("This backend does not expose configured transport setup.");
        m_initial = initial;
        m_pinned = pinned;
        Title = pinned ? "Saved connection setup (pinned)" : "Transport and reverse-connect setup";
        Width = 760;
        Height = 750;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var content = new StackPanel { Margin = new Thickness(18), Spacing = 9 };
        m_endpoint = new TextBox { Name = "SetupEndpointUrl", Text = initial.EndpointUrl, MaxLength = 2048 };
        m_reverse = new CheckBox
        {
            Name = "ReverseConnectToggle", Content = "Use reverse connect (server calls this client)"
        };
        m_reverse.IsChecked = initial.ReverseConnection is not null;
        m_listener = new TextBox
        {
            Name = "ReverseListenerUrl",
            Text = initial.ReverseConnection?.ListenerUrl ?? "opc.tcp://localhost:4841",
            MaxLength = 2048
        };
        m_serverUri = new TextBox
        {
            Name = "ExpectedServerUri",
            Text = initial.ReverseConnection?.ServerUri ?? string.Empty,
            MaxLength = 2048
        };
        m_timeout = new NumericUpDown
        {
            Name = "ReverseWaitSeconds", Minimum = 1, Maximum = 300,
            Value = initial.ReverseConnection?.WaitTimeoutSeconds ?? 20,
            Increment = 1
        };
        m_tls = new ComboBox { Name = "ListenerTlsConfiguration", HorizontalAlignment = HorizontalAlignment.Stretch };
        m_application = new ComboBox
        {
            Name = "ApplicationIdentityConfiguration", HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var tlsItems = new List<string> { "None (TCP only)" };
        foreach (ConfiguredReverseTlsSource source in m_backend.Configurations.ReverseTlsSources)
        {
            tlsItems.Add(source.DisplayName);
        }
        m_tls.ItemsSource = tlsItems;
        m_tls.SelectedIndex = 0;
        for (int i = 0; i < m_backend.Configurations.ReverseTlsSources.Count; i++)
        {
            if (m_backend.Configurations.ReverseTlsSources[i].Id == initial.ReverseConnection?.TlsConfigurationId)
            {
                m_tls.SelectedIndex = i + 1;
            }
        }
        var applicationItems = new List<string> { "Default UaLens application certificate (not the user identity)" };
        foreach (ConfiguredApplicationIdentity identity in m_backend.Configurations.ApplicationIdentities)
        {
            applicationItems.Add(identity.ToString());
        }
        m_application.ItemsSource = applicationItems;
        m_application.SelectedIndex = 0;
        for (int i = 0; i < m_backend.Configurations.ApplicationIdentities.Count; i++)
        {
            if (m_backend.Configurations.ApplicationIdentities[i].Id == initial.ApplicationIdentityId)
            {
                m_application.SelectedIndex = i + 1;
            }
        }
        AddField(content, "Server endpoint / discovery URL", m_endpoint);
        AddField(content, "ApplicationInstanceKey — configured secure-channel identity", m_application);
        var capabilities = new StackPanel { Spacing = 6 };
        foreach (ConnectionTransportCapability capability in m_backend.Transports.Capabilities)
        {
            capabilities.Children.Add(new TextBlock
            {
                Text = capability.ToString(), TextWrapping = TextWrapping.Wrap, FontSize = 12
            });
        }
        content.Children.Add(new Expander
        {
            Header = "Registered forward transports and platform prerequisites",
            Content = capabilities,
            IsExpanded = false
        });
        content.Children.Add(m_reverse);
        var reverseFields = new StackPanel { Spacing = 7 };
        AddField(reverseFields,
            "Local listener URL (binding/firewall and server reverse target are external setup)", m_listener);
        AddField(reverseFields, "Expected ServerUri (exact application URI, not a hostname)", m_serverUri);
        AddField(reverseFields, "Wait timeout (seconds)", m_timeout);
        AddField(reverseFields, "Configured listener TLS certificate + validator (required for WSS)", m_tls);
        content.Children.Add(reverseFields);
        content.Children.Add(new TextBlock
        {
            Text = "Only the exact ServerUri AND complete endpoint URL are accepted. " +
                "UA certificate trust remains explicit. WSS TLS is additional; no auto-accept, " +
                "HSM provisioning, token authority or firewall installation is performed.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });
        m_status = new TextBlock { Name = "ReverseConnectionStatus", TextWrapping = TextWrapping.Wrap };
        content.Children.Add(m_status);
        m_start = new Button { Name = "StartReverseListenerButton", Content = "Start listener" };
        m_wait = new Button { Name = "WaitForReverseServerButton", Content = "Wait / discover" };
        m_cancelWait = new Button { Name = "CancelReverseWaitButton", Content = "Cancel wait" };
        m_stop = new Button { Name = "StopReverseListenerButton", Content = "Stop listener" };
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { m_start, m_wait, m_cancelWait, m_stop }
        });
        var use = new Button { Name = "UseConnectionSetupButton", Content = "Use setup", IsDefault = true };
        var cancel = new Button { Name = "CancelButton", Content = "Cancel", IsCancel = true };
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { use, cancel }
        });
        Content = new ScrollViewer { Content = content };
        if (pinned)
        {
            m_endpoint.IsEnabled = false;
            m_application.IsEnabled = false;
            m_reverse.IsEnabled = false;
            reverseFields.IsEnabled = false;
        }
        m_start.Click += async (_, _) =>
        {
            m_operation = StartListenerAsync();
            await m_operation.ConfigureAwait(true);
        };
        m_wait.Click += async (_, _) =>
        {
            m_operation = WaitForServerAsync();
            await m_operation.ConfigureAwait(true);
        };
        m_cancelWait.Click += async (_, _) => await CancelWaitAsync().ConfigureAwait(true);
        m_stop.Click += async (_, _) =>
        {
            await CancelWaitAsync().ConfigureAwait(true);
            await m_operation.ConfigureAwait(true);
            m_operation = StopListenerAsync();
            await m_operation.ConfigureAwait(true);
        };
        use.Click += (_, _) =>
        {
            if (!m_operation.IsCompleted)
            {
                m_status.Text = "Wait for the current operation, or cancel it first.";
                return;
            }
            try
            {
                ConnectionSetupSelection selected = ReadSetup();
                if (selected.ReverseConnection is { } reverse)
                {
                    using ReverseConnectionLease lease = m_backend.ReverseConnections.Acquire(reverse);
                }
                Close(selected);
            }
            catch (Exception error) when (IsSetupFailure(error))
            {
                m_status.Text = error.Message;
            }
        };
        cancel.Click += (_, _) => Close(null);
        RefreshState();
    }

    public ArrayOf<EndpointDescription> DiscoveredEndpoints { get; private set; }

    public ConnectionSetupSelection? DiscoverySetup { get; private set; }

    public async Task<ConnectionSetupSelection?> PromptAsync(Window owner, CancellationToken ct = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        m_lifetime = lifetime;
        CancellationTokenRegistration registration = ct.Register(() =>
            Dispatcher.UIThread.Post(() =>
            {
                if (IsVisible)
                {
                    Close(null);
                }
            }));
        await using (registration.ConfigureAwait(true))
        {
            ConnectionSetupSelection? result = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                result = await ShowDialog<ConnectionSetupSelection?>(owner).ConfigureAwait(true);
                ct.ThrowIfCancellationRequested();
                return result;
            }
            finally
            {
                await lifetime.CancelAsync().ConfigureAwait(true);
                await CancelWaitAsync().ConfigureAwait(true);
                await m_operation.ConfigureAwait(true);
                if (m_startedHere && (result?.ReverseConnection is null || ct.IsCancellationRequested))
                {
                    await m_backend.ReverseConnections.StopAsync(CancellationToken.None).ConfigureAwait(true);
                }
                m_lifetime = null;
            }
        }
    }

    private ConnectionSetupSelection ReadSetup()
    {
        if (m_pinned)
        {
            m_backend.Transports.RequireForward(m_initial.EndpointUrl);
            m_initial.ReverseConnection?.Validate(m_initial.EndpointUrl);
            if (m_initial.ApplicationIdentityId is { } application)
            {
                m_backend.Configurations.ResolveApplication(application);
            }
            return m_initial;
        }
        string url = m_endpoint.Text?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? endpoint) ||
            string.IsNullOrEmpty(endpoint.Host) || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException(
                "Enter an absolute endpoint URL without credentials, query secrets or fragments.");
        }
        m_backend.Transports.RequireForward(url);
        string? applicationId = m_application.SelectedIndex > 0
            ? m_backend.Configurations.ApplicationIdentities[m_application.SelectedIndex - 1].Id
            : null;
        ReverseConnectionProfile? reverse = null;
        if (m_reverse.IsChecked == true)
        {
            reverse = new ReverseConnectionProfile
            {
                ListenerUrl = m_listener.Text?.Trim() ?? string.Empty,
                EndpointUrl = url,
                ServerUri = m_serverUri.Text?.Trim() ?? string.Empty,
                WaitTimeoutSeconds = (int)(m_timeout.Value ?? 20),
                TlsConfigurationId = m_tls.SelectedIndex > 0
                    ? m_backend.Configurations.ReverseTlsSources[m_tls.SelectedIndex - 1].Id
                    : null
            };
            m_backend.Transports.RequireReverse(reverse);
        }
        return new ConnectionSetupSelection(url, reverse, applicationId);
    }

    private async Task StartListenerAsync()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime!.Token);
        m_startCancellation = cancellation;
        try
        {
            ReverseConnectionProfile profile = ReadSetup().ReverseConnection ??
                throw new InvalidOperationException("Enable reverse connect and configure the expected server first.");
            m_startedHere |= m_backend.ReverseConnections.Snapshot.Phase == ReverseConnectionPhase.Stopped;
            SetBusy("Starting the explicitly configured listener…");
            await m_backend.ReverseConnections.StartAsync(profile, cancellation.Token).ConfigureAwait(true);
            RefreshState();
        }
        catch (OperationCanceledException)
        {
            RefreshState("Listener start canceled.");
        }
        catch (Exception error) when (IsSetupFailure(error))
        {
            RefreshState($"Listener did not start: {error.Message}");
        }
        finally
        {
            m_startCancellation = null;
        }
    }

    private async Task WaitForServerAsync()
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime!.Token);
        m_waitCancellation = wait;
        try
        {
            ConnectionSetupSelection setup = ReadSetup();
            if (setup.ReverseConnection is null)
            {
                throw new InvalidOperationException("Enable and explicitly start reverse connect before waiting.");
            }
            SetBusy("Waiting for the exact endpoint and ServerUri… Cancel wait releases this wait only.");
            DiscoveredEndpoints = await m_connection.DiscoverEndpointsAsync(setup, wait.Token).ConfigureAwait(true);
            DiscoverySetup = setup;
            RefreshState($"Matched server; {DiscoveredEndpoints.Count} endpoint/security descriptions discovered. " +
                "Use setup to select one.");
        }
        catch (OperationCanceledException)
        {
            RefreshState("Wait canceled. The explicitly started listener remains available until Stop or Cancel.");
        }
        catch (Exception error) when (IsSetupFailure(error))
        {
            RefreshState($"No matching discovery result: {error.Message}");
        }
        finally
        {
            m_waitCancellation = null;
        }
    }

    private async Task CancelWaitAsync()
    {
        if (m_startCancellation is { } starting)
        {
            await starting.CancelAsync().ConfigureAwait(true);
        }
        if (m_waitCancellation is { } cancellation)
        {
            await cancellation.CancelAsync().ConfigureAwait(true);
        }
        await m_backend.ReverseConnections.CancelWaitAsync().ConfigureAwait(true);
    }

    private async Task StopListenerAsync()
    {
        try
        {
            SetBusy("Stopping the owned reverse listener (disconnects the primary reverse session, if any)…");
            await m_connection.StopReverseListenerAsync(m_lifetime!.Token).ConfigureAwait(true);
            m_startedHere = false;
            DiscoveredEndpoints = default;
            DiscoverySetup = null;
            RefreshState();
        }
        catch (OperationCanceledException)
        {
            RefreshState("Listener stop canceled.");
        }
        catch (Exception error) when (IsSetupFailure(error))
        {
            RefreshState($"Listener stop failed: {error.Message}");
        }
    }

    private void SetBusy(string message)
    {
        m_start.IsEnabled = false;
        m_wait.IsEnabled = false;
        m_cancelWait.IsEnabled = true;
        m_cancelWait.Content = "Cancel operation";
        m_status.Text = message;
    }

    private void RefreshState(string? message = null)
    {
        ReverseConnectionSnapshot state = m_backend.ReverseConnections.Snapshot;
        m_start.IsEnabled = state.Phase == ReverseConnectionPhase.Stopped;
        m_wait.IsEnabled = state.Phase == ReverseConnectionPhase.Listening;
        m_cancelWait.IsEnabled = state.Phase == ReverseConnectionPhase.Waiting;
        m_cancelWait.Content = "Cancel wait";
        m_stop.IsEnabled = state.Phase is not (ReverseConnectionPhase.Stopped or ReverseConnectionPhase.Stopping);
        m_status.Text = message ?? $"Listener: {state.Phase}. " +
            (state.Profile is null
                ? "No listener was started."
                : $"{state.Profile.ListenerUrl} → {state.Profile.ServerUri}");
    }

    private static bool IsSetupFailure(Exception error)
    {
        return error is InvalidOperationException or NotSupportedException or ArgumentException
            or ServiceResultException or TimeoutException or System.IO.IOException
            or System.Net.Sockets.SocketException or UnauthorizedAccessException or AggregateException;
    }

    private static void AddField(StackPanel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(control);
    }

    private readonly ConnectionService m_connection;
    private readonly IConfiguredConnectionBackend m_backend;
    private readonly ConnectionSetupSelection m_initial;
    private readonly bool m_pinned;
    private readonly TextBox m_endpoint;
    private readonly CheckBox m_reverse;
    private readonly TextBox m_listener;
    private readonly TextBox m_serverUri;
    private readonly NumericUpDown m_timeout;
    private readonly ComboBox m_tls;
    private readonly ComboBox m_application;
    private readonly TextBlock m_status;
    private readonly Button m_start;
    private readonly Button m_wait;
    private readonly Button m_cancelWait;
    private readonly Button m_stop;
    private CancellationTokenSource? m_lifetime;
    private CancellationTokenSource? m_waitCancellation;
    private CancellationTokenSource? m_startCancellation;
    private Task m_operation = Task.CompletedTask;
    private bool m_startedHere;
}
