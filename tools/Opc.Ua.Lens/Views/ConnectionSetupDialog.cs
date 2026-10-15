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
        bool pinned = false,
        ConnectionProfile? selectedProfile = null)
    {
        m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
        m_backend = connection.ConfiguredBackend ??
            throw new NotSupportedException("This backend does not expose configured transport setup.");
        m_initial = initial ?? throw new ArgumentNullException(nameof(initial));
        m_pinned = pinned;
        m_selectedProfile = selectedProfile;
        Title = pinned ? "Saved connection setup (pinned)" : "Transport and reverse-connect setup";
        Width = 760;
        Height = 820;
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
        m_hold = new NumericUpDown
        {
            Name = "ReverseHoldSeconds", Minimum = 1, Maximum = 60,
            Value = initial.ReverseConnection?.HoldTimeSeconds ?? 15,
            Increment = 1
        };
        m_tls = new ComboBox { Name = "ListenerTlsConfiguration", HorizontalAlignment = HorizontalAlignment.Stretch };
        m_application = new ComboBox
        {
            Name = "ApplicationIdentityConfiguration", HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var tlsItems = new List<ConfigurationChoice> { new(null, "None (TCP only)") };
        foreach (ConfiguredReverseTlsSource source in m_backend.Configurations.ReverseTlsSources)
        {
            tlsItems.Add(new ConfigurationChoice(source.Id, source.DisplayName));
        }
        SelectReference(m_tls, tlsItems, initial.ReverseConnection?.TlsConfigurationId);
        var applicationItems = new List<ConfigurationChoice>
        {
            new(null, "Default UaLens application certificate (not the user identity)")
        };
        foreach (ConfiguredApplicationIdentity identity in m_backend.Configurations.ApplicationIdentities)
        {
            applicationItems.Add(new ConfigurationChoice(identity.Id, identity.ToString()));
        }
        SelectReference(m_application, applicationItems, initial.ApplicationIdentityId);
        m_fields = new StackPanel { Spacing = 9 };
        AddField(m_fields, "Server endpoint / discovery URL (scheme selects the registered binding)", m_endpoint);
        AddField(m_fields, "ApplicationInstanceKey — configured secure-channel identity", m_application);
        content.Children.Add(m_fields);
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
        m_fields.Children.Add(m_reverse);
        m_reverseFields = new StackPanel { Spacing = 7 };
        AddField(m_reverseFields,
            "Local listener URL (binding/firewall and server reverse target are external setup)", m_listener);
        AddField(m_reverseFields, "Expected ServerUri (exact application URI, not a hostname)", m_serverUri);
        AddField(m_reverseFields, "Wait timeout (seconds)", m_timeout);
        AddField(m_reverseFields, "Unclaimed connection hold time (seconds)", m_hold);
        AddField(m_reverseFields, "Configured listener TLS certificate + validator (required for WSS)", m_tls);
        m_fields.Children.Add(m_reverseFields);
        content.Children.Add(new TextBlock
        {
            Text = "Only the exact ServerUri AND complete endpoint URL are accepted. " +
                "UA certificate trust remains explicit. WSS TLS is additional; no auto-accept, " +
                "HSM provisioning, token authority or firewall installation is performed.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });
        m_readiness = new TextBlock
        {
            Name = "TransportSetupReadiness", TextWrapping = TextWrapping.Wrap, FontSize = 12
        };
        AddField(content, "Selected setup readiness (registration is not network or trust qualification)", m_readiness);
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
        m_use = new Button { Name = "UseConnectionSetupButton", Content = "Use setup", IsDefault = true };
        var cancel = new Button { Name = "CancelButton", Content = "Cancel", IsCancel = true };
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { m_use, cancel }
        });
        Content = new ScrollViewer { Content = content };
        m_endpoint.TextChanged += (_, _) => OnSetupChanged();
        m_listener.TextChanged += (_, _) => OnSetupChanged();
        m_serverUri.TextChanged += (_, _) => OnSetupChanged();
        m_application.SelectionChanged += (_, _) => OnSetupChanged();
        m_tls.SelectionChanged += (_, _) => OnSetupChanged();
        m_timeout.ValueChanged += (_, _) => OnSetupChanged();
        m_hold.ValueChanged += (_, _) => OnSetupChanged();
        m_reverse.PropertyChanged += (_, args) =>
        {
            if (args.Property == CheckBox.IsCheckedProperty)
            {
                OnSetupChanged();
            }
        };
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
        m_cancelWait.Click += async (_, _) =>
        {
            try
            {
                await CancelWaitAsync().ConfigureAwait(true);
            }
            catch (Exception error) when (IsSetupFailure(error))
            {
                m_status.Text = $"Operation cancellation failed: {error.Message}";
            }
        };
        m_stop.Click += async (_, _) =>
        {
            m_operation = StopListenerAsync(m_operation);
            await m_operation.ConfigureAwait(true);
        };
        m_use.Click += (_, _) =>
        {
            if (!m_operation.IsCompleted)
            {
                m_status.Text = "Wait for the current operation, or cancel it first.";
                return;
            }
            try
            {
                ConnectionSetupSelection selected = ReadSetup();
                RequireReadySetup(selected);
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
                if (m_startedProfile is not null &&
                    m_backend.ReverseConnections.Snapshot.Profile == m_startedProfile &&
                    (result?.ReverseConnection != m_startedProfile || ct.IsCancellationRequested))
                {
                    await m_backend.ReverseConnections.StopAsync(CancellationToken.None).ConfigureAwait(true);
                }
                m_lifetime = null;
            }
        }
    }

    private ConnectionSetupSelection ReadSetup()
    {
        ConnectionSetupSelection setup = ReadIntent();
        m_backend.Transports.RequireSetup(setup, m_backend.Configurations, m_selectedProfile);
        return setup;
    }

    private ConnectionSetupSelection ReadIntent()
    {
        if (m_pinned)
        {
            return m_initial;
        }
        string url = m_endpoint.Text?.Trim() ?? string.Empty;
        string? applicationId = ReadReference(m_application);
        ReverseConnectionProfile? reverse = null;
        if (m_reverse.IsChecked == true)
        {
            reverse = new ReverseConnectionProfile
            {
                ListenerUrl = m_listener.Text?.Trim() ?? string.Empty,
                EndpointUrl = url,
                ServerUri = m_serverUri.Text?.Trim() ?? string.Empty,
                WaitTimeoutSeconds = ReadSeconds(m_timeout, 300, "Wait timeout"),
                HoldTimeSeconds = ReadSeconds(m_hold, 60, "Connection hold time"),
                TlsConfigurationId = ReadReference(m_tls)
            };
        }
        return new ConnectionSetupSelection(url, reverse, applicationId);
    }

    private async Task StartListenerAsync()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime!.Token);
        m_startCancellation = cancellation;
        string? message = null;
        SetBusy("Starting the explicitly configured listener...");
        try
        {
            ReverseConnectionProfile profile = ReadSetup().ReverseConnection ??
                throw new InvalidOperationException("Enable reverse connect and configure the expected server first.");
            bool stopped = m_backend.ReverseConnections.Snapshot.Phase == ReverseConnectionPhase.Stopped;
            await m_backend.ReverseConnections.StartAsync(profile, cancellation.Token).ConfigureAwait(true);
            if (stopped)
            {
                m_startedProfile = profile;
            }
        }
        catch (OperationCanceledException)
        {
            message = "Listener start canceled.";
        }
        catch (Exception error) when (IsSetupFailure(error))
        {
            message = $"Listener did not start: {error.Message}";
        }
        finally
        {
            m_startCancellation = null;
            CompleteOperation(message);
        }
    }

    private async Task WaitForServerAsync()
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime!.Token);
        m_waitCancellation = wait;
        string? message = null;
        SetBusy("Discovering the selected transport... Cancel operation releases this discovery only.");
        int revision = m_setupRevision;
        DiscoveredEndpoints = default;
        DiscoverySetup = null;
        m_discoveryCompleted = false;
        try
        {
            ConnectionSetupSelection setup = ReadSetup();
            ArrayOf<EndpointDescription> endpoints =
                await m_connection.DiscoverEndpointsAsync(setup, wait.Token).ConfigureAwait(true);
            wait.Token.ThrowIfCancellationRequested();
            if (revision != m_setupRevision || ReadIntent() != setup)
            {
                throw new InvalidOperationException(
                    "The setup changed during discovery. Discover the current selection explicitly.");
            }
            DiscoveredEndpoints = endpoints;
            DiscoverySetup = setup;
            m_discoveryCompleted = true;
            message = $"{DiscoveredEndpoints.Count} endpoint/security descriptions discovered. " +
                "Use setup, then Connect to select security and user identity. Discovery does not establish trust.";
        }
        catch (OperationCanceledException)
        {
            message = "Discovery canceled. An explicitly started listener remains available until Stop or Cancel.";
        }
        catch (Exception error) when (IsSetupFailure(error))
        {
            message = $"No matching discovery result: {error.Message}";
        }
        finally
        {
            m_waitCancellation = null;
            CompleteOperation(message);
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
    }

    private async Task StopListenerAsync(Task previous)
    {
        string? message = null;
        SetBusy("Stopping the owned reverse listener (disconnects the primary reverse session, if any)...");
        m_stop.IsEnabled = false;
        try
        {
            await CancelWaitAsync().ConfigureAwait(true);
            await previous.ConfigureAwait(true);
            await m_connection.StopReverseListenerAsync(m_lifetime!.Token).ConfigureAwait(true);
            m_startedProfile = null;
            DiscoveredEndpoints = default;
            DiscoverySetup = null;
            m_discoveryCompleted = false;
        }
        catch (OperationCanceledException)
        {
            message = "Listener stop canceled.";
        }
        catch (Exception error) when (IsSetupFailure(error))
        {
            message = $"Listener stop failed: {error.Message}";
        }
        finally
        {
            CompleteOperation(message);
        }
    }

    private void SetBusy(string message)
    {
        m_operations++;
        m_operationMessage = message;
        RefreshState();
    }

    private void CompleteOperation(string? message)
    {
        m_operations--;
        RefreshState(message);
    }

    private void OnSetupChanged()
    {
        m_setupRevision++;
        DiscoveredEndpoints = default;
        DiscoverySetup = null;
        m_discoveryCompleted = false;
        RefreshState();
    }

    private void RequireReadySetup(ConnectionSetupSelection setup)
    {
        ConnectionTransportReadiness readiness = Assess(setup);
        foreach (ConnectionTransportCheck check in readiness.Checks)
        {
            if (check.State == ConnectionTransportCheckState.Blocked)
            {
                throw new InvalidOperationException(check.Detail);
            }
        }
    }

    private ConnectionTransportReadiness Assess(ConnectionSetupSelection setup)
    {
        return ConnectionTransportReadiness.Assess(m_backend.Transports, m_backend.Configurations, setup,
            m_backend.ReverseConnections.Snapshot, m_selectedProfile, DiscoveredEndpoints, m_discoveryCompleted);
    }

    private void RefreshState(string? message = null)
    {
        ReverseConnectionSnapshot state = m_backend.ReverseConnections.Snapshot;
        bool busy = m_operations != 0;
        m_fields.IsEnabled = !m_pinned && !busy;
        m_reverseFields.IsVisible = m_reverse.IsChecked == true;
        m_start.IsEnabled = false;
        m_wait.IsEnabled = false;
        m_use.IsEnabled = false;
        try
        {
            ConnectionTransportReadiness readiness = Assess(ReadIntent());
            m_readiness.Text = string.Join(Environment.NewLine, readiness.Checks.ToList());
            m_start.IsEnabled = !busy && readiness.CanStartListener;
            m_wait.IsEnabled = !busy && readiness.CanDiscover;
            m_use.IsEnabled = !busy && readiness.CanUseSetup;
        }
        catch (Exception error) when (IsSetupFailure(error))
        {
            m_readiness.Text = $"Setup blocked: {error.Message}";
        }
        m_wait.Content = m_reverse.IsChecked == true ? "Wait / discover" : "Discover endpoints";
        m_cancelWait.IsEnabled = m_startCancellation is not null || m_waitCancellation is not null;
        m_cancelWait.Content = "Cancel operation";
        m_stop.IsEnabled = state.Phase is not (ReverseConnectionPhase.Stopped or ReverseConnectionPhase.Stopping)
            && m_operations < 2 && (!busy || m_startCancellation is not null || m_waitCancellation is not null);
        m_status.Text = busy ? m_operationMessage : message ?? $"Listener: {state.Phase}. " +
            (state.Profile is null
                ? "Use setup saves intent only; it does not start a listener."
                : $"{state.Profile.ListenerUrl} -> {state.Profile.ServerUri}");
    }

    private static int ReadSeconds(NumericUpDown control, int maximum, string name)
    {
        if (control.Value is not { } value || value < 1 || value > maximum || decimal.Truncate(value) != value)
        {
            throw new ArgumentException($"{name} must be a whole number from 1 to {maximum} seconds.");
        }
        return (int)value;
    }

    private static string? ReadReference(ComboBox picker)
    {
        return picker.SelectedItem is ConfigurationChoice selected
            ? selected.Id
            : throw new InvalidOperationException("Select an explicit configured source or the displayed default.");
    }

    private static void SelectReference(ComboBox picker, List<ConfigurationChoice> choices, string? id)
    {
        int index = choices.FindIndex(choice => string.Equals(choice.Id, id, StringComparison.Ordinal));
        if (index < 0)
        {
            index = choices.Count;
            choices.Add(new ConfigurationChoice(id, $"Unavailable saved configuration: {id}"));
        }
        picker.ItemsSource = choices;
        picker.SelectedIndex = index;
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

    private sealed record ConfigurationChoice(string? Id, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private readonly ConnectionService m_connection;
    private readonly IConfiguredConnectionBackend m_backend;
    private readonly ConnectionSetupSelection m_initial;
    private readonly bool m_pinned;
    private readonly ConnectionProfile? m_selectedProfile;
    private readonly StackPanel m_fields;
    private readonly StackPanel m_reverseFields;
    private readonly TextBox m_endpoint;
    private readonly CheckBox m_reverse;
    private readonly TextBox m_listener;
    private readonly TextBox m_serverUri;
    private readonly NumericUpDown m_timeout;
    private readonly NumericUpDown m_hold;
    private readonly ComboBox m_tls;
    private readonly ComboBox m_application;
    private readonly TextBlock m_status;
    private readonly TextBlock m_readiness;
    private readonly Button m_start;
    private readonly Button m_wait;
    private readonly Button m_cancelWait;
    private readonly Button m_stop;
    private readonly Button m_use;
    private CancellationTokenSource? m_lifetime;
    private CancellationTokenSource? m_waitCancellation;
    private CancellationTokenSource? m_startCancellation;
    private Task m_operation = Task.CompletedTask;
    private ReverseConnectionProfile? m_startedProfile;
    private string? m_operationMessage;
    private int m_operations;
    private int m_setupRevision;
    private bool m_discoveryCompleted;
}
