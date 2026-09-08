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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Subscriptions;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Owns connection dialog dispatch: the endpoint/credentials pick, fail-closed
/// certificate trust, identity/engine/reconnect settings, local certificate
/// stores and workspace files. Connection policy itself lives in the connection
/// module; this only bridges the desktop to it without downgrading security.
/// </summary>
internal sealed class ConnectionController
{
    public ConnectionController(MainWindow window, MainViewModel viewModel, ILogger log)
    {
        m_window = window ?? throw new ArgumentNullException(nameof(window));
        m_vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        m_log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void Attach()
    {
        m_vm.ConnectRequestedAsync = OnConnectFlowAsync;

        m_window.RequiredControl<MenuItem>("MenuOpenWorkspace").Click +=
            async (_, _) => await OpenWorkspaceAsync().ConfigureAwait(true);
        m_window.RequiredControl<MenuItem>("MenuSaveWorkspace").Click +=
            async (_, _) => await SaveWorkspaceAsync().ConfigureAwait(true);
        m_window.RequiredControl<MenuItem>("MenuCertificates").Click +=
            async (_, _) => await OpenCertificateStoreAsync().ConfigureAwait(true);

        var settings = m_window.RequiredControl<Button>("ConnectionSettingsButton");
        settings.Flyout = BuildSettingsFlyout();
        // Refresh the advanced menu state before the flyout renders on the same click.
        settings.Click += (_, _) => RefreshSettingsFlyout();
    }

    /// <summary>
    /// Opens a saved workspace file, importing its documents offline.
    /// </summary>
    public async Task OpenWorkspaceAsync()
    {
        try
        {
            IReadOnlyList<IStorageFile> files = await m_window.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Open UaLens workspace",
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("UaLens workspace") { Patterns = s_sessionPatterns }]
                }).ConfigureAwait(true);
            if (files.Count == 0)
            {
                return;
            }
            SessionFile? file = await SessionFile.LoadAsync(files[0].Path.LocalPath).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }
            await m_vm.LoadSessionAsync(file).ConfigureAwait(true);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException or FormatException or NotSupportedException
            or ArgumentException or ServiceResultException or AggregateException)
        {
            ShowError($"Open workspace failed: {error.Message}");
            MainWindowLog.WorkspaceOperationFailed(m_log, "open", error);
        }
    }

    private MenuFlyout BuildSettingsFlyout()
    {
        m_engineItem = new MenuItem { Header = "Use ChannelV2 engine" };
        m_engineItem.Click += (_, _) => m_vm.UseChannelV2Engine = !m_vm.UseChannelV2Engine;
        m_changeUserItem = new MenuItem { Header = "Change user…" };
        m_changeUserItem.Click += async (_, _) => await ChangeUserAsync().ConfigureAwait(true);
        m_reconnectItem = new MenuItem { Header = "Reconnect" };
        m_reconnectItem.Click += async (_, _) => await ReconnectAsync().ConfigureAwait(true);
        m_pipelineItem = new MenuItem { Header = "Publish pipeline…" };
        m_pipelineItem.Click += async (_, _) => await PublishingPipelineAsync().ConfigureAwait(true);
        m_localesItem = new MenuItem { Header = "Preferred locales…" };
        m_localesItem.Click += async (_, _) => await LocalesAsync().ConfigureAwait(true);

        var flyout = new MenuFlyout();
        flyout.Items.Add(m_engineItem);
        flyout.Items.Add(new Separator());
        flyout.Items.Add(m_changeUserItem);
        flyout.Items.Add(m_reconnectItem);
        flyout.Items.Add(new Separator());
        flyout.Items.Add(m_pipelineItem);
        flyout.Items.Add(m_localesItem);
        RefreshSettingsFlyout();
        return flyout;
    }

    private void RefreshSettingsFlyout()
    {
        if (m_engineItem is null)
        {
            return;
        }
        m_engineItem.Icon = m_vm.UseChannelV2Engine
            ? new TextBlock { Text = "✓", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
            : null;
        bool connected = m_vm.Connection.IsConnected;
        m_changeUserItem!.IsEnabled = connected;
        m_reconnectItem!.IsEnabled = connected;
        m_pipelineItem!.IsEnabled = connected;
        m_localesItem!.IsEnabled = connected;
    }

    private async Task PublishingPipelineAsync()
    {
        if (m_vm.Connection.Session is not { } session)
        {
            m_vm.ConnectionStatus = "Connect to change the publish pipeline.";
            return;
        }
        try
        {
            SessionPublishingSettings current = m_vm.PublishingPipeline ?? new SessionPublishingSettings(
                Math.Max(1, session.MinPublishRequestCount),
                Math.Max(Math.Max(1, session.MinPublishRequestCount), session.MaxPublishRequestCount));
            var dialog = new SessionPublishingSettingsDialog(current);
            SessionPublishingSettings? result =
                await dialog.ShowDialog<SessionPublishingSettings?>(m_window).ConfigureAwait(true);
            if (result is null)
            {
                return;
            }
            // Applies to the current session and is retained by the view-model so it
            // is re-applied automatically across an engine reconnect.
            m_vm.ConfigurePublishingPipeline(result);
            m_vm.ConnectionStatus =
                $"Publish pipeline set to {result.MinimumRequests} to {result.MaximumRequests} in-flight requests.";
        }
        catch (Exception error) when (error is ServiceResultException or ArgumentException
            or InvalidOperationException)
        {
            m_vm.ConnectionStatus = $"Publish pipeline change failed: {error.Message}";
            MainWindowLog.WorkspaceOperationFailed(m_log, "publish-pipeline", error);
        }
    }

    private async Task OnConnectFlowAsync(CancellationToken cancellationToken)
    {
        ClearError();
        try
        {
            if (m_vm.RestoredConnectionProfile is { } restored)
            {
                // A restored workspace pins the exact saved endpoint, security and
                // token intent. Never fall back to the free picker (Anonymous/None).
                await ConnectRestoredProfileAsync(restored, cancellationToken).ConfigureAwait(true);
                return;
            }
            EndpointCredentialsPicker.Result? pick = await EndpointCredentialsPicker.PromptAsync(
                m_window, m_vm.Telemetry, m_vm.EndpointUrl, cancellationToken).ConfigureAwait(true);
            if (pick is null)
            {
                return;
            }
            await m_vm.Connection.ConnectAsync(
                new ConnectionOptions
                {
                    EndpointUrl = pick.Endpoint.EndpointUrl ?? m_vm.EndpointUrl,
                    UseSecurity = pick.Endpoint.SecurityMode != MessageSecurityMode.None,
                    Engine = m_vm.Engine
                },
                pick.Endpoint,
                pick.Identity,
                certPrompt: PromptCertTrustAsync,
                cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is ServiceResultException or InvalidOperationException
            or TimeoutException or System.Net.Sockets.SocketException or AggregateException
            or System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            m_vm.ConnectionStatus = $"Connect failed: {error.Message}";
            ShowError($"Connect failed: {error.Message}");
            MainWindowLog.ConnectFailed(m_log, error);
        }
    }

    /// <summary>
    /// Connects using the exact endpoint, security and token policy of a restored
    /// profile. The identity is reacquired without downgrading; unsupported token
    /// types are reported rather than silently replaced.
    /// </summary>
    private async Task ConnectRestoredProfileAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        var discovery = new DiscoveryService(m_vm.Telemetry);
        ArrayOf<EndpointDescription> endpoints =
            await discovery.DiscoverAsync(profile.EndpointUrl, cancellationToken).ConfigureAwait(true);
        EndpointDescription? endpoint = null;
        for (int i = 0; i < endpoints.Count; i++)
        {
            if (profile.MatchesEndpoint(endpoints[i]))
            {
                endpoint = endpoints[i];
                break;
            }
        }
        if (endpoint is null)
        {
            m_vm.ConnectionStatus =
                "The server no longer offers the saved endpoint and security profile. Choose a connection.";
            ShowError("Restore failed: the saved endpoint/security profile is no longer offered by the server.");
            return;
        }

        UserTokenPolicy policy = profile.RequireMatch(endpoint);
        IUserIdentity? identity = await ReacquireIdentityAsync(policy).ConfigureAwait(true);
        if (identity is null)
        {
            return;
        }
        await m_vm.Connection.ConnectAsync(
            new ConnectionOptions
            {
                EndpointUrl = endpoint.EndpointUrl ?? profile.EndpointUrl,
                UseSecurity = endpoint.SecurityMode != MessageSecurityMode.None,
                Engine = profile.Engine
            },
            endpoint,
            identity,
            certPrompt: PromptCertTrustAsync,
            cancellationToken).ConfigureAwait(true);
    }

    private async Task<IUserIdentity?> ReacquireIdentityAsync(UserTokenPolicy policy)
    {
        switch (policy.TokenType)
        {
            case UserTokenType.Anonymous:
                return new UserIdentity(new AnonymousIdentityToken()) { PolicyId = policy.PolicyId! };
            case UserTokenType.UserName:
                var dialog = new CredentialsDialog();
                (string, string)? pair = await dialog.ShowDialog<(string, string)?>(m_window).ConfigureAwait(true);
                if (pair is null)
                {
                    m_vm.ConnectionStatus = "Credentials are required to restore this connection.";
                    return null;
                }
                (string user, string pass) = pair.Value;
                byte[] password = Encoding.UTF8.GetBytes(pass);
                try
                {
                    return new UserIdentity(user, password.AsSpan()) { PolicyId = policy.PolicyId! };
                }
                finally
                {
                    Array.Clear(password);
                }
            default:
                m_vm.ConnectionStatus =
                    $"The saved {policy.TokenType} identity is not supported here. Connect with a compatible client.";
                ShowError($"Restore requires a {policy.TokenType} identity, which this desktop cannot supply.");
                return null;
        }
    }

    private Task<TrustChoice> PromptCertTrustAsync(X509Certificate2 cert, ServiceResult error)
    {
        // The trust decision is requested outside certificate validation by the
        // connection coordinator. Marshal to the UI thread, default to reject and
        // close on shutdown so validation never blocks on the desktop.
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (m_window.IsClosingRequested)
            {
                return TrustChoice.Reject;
            }
            var dlg = new CertificateTrustDialog(cert, error);
            TrustChoice? choice = await dlg.ShowDialog<TrustChoice?>(m_window).ConfigureAwait(true);
            return choice ?? TrustChoice.Reject;
        });
    }

    private async Task ChangeUserAsync()
    {
        if (!m_vm.Connection.IsConnected)
        {
            m_vm.ConnectionStatus = "Not connected.";
            return;
        }
        try
        {
            var dlg = new CredentialsDialog();
            (string, string)? pair = await dlg.ShowDialog<(string, string)?>(m_window).ConfigureAwait(true);
            if (pair is null)
            {
                return;
            }
            (string user, string pass) = pair.Value;
            byte[] password = Encoding.UTF8.GetBytes(pass);
            try
            {
                var identity = new UserIdentity(user, password.AsSpan());
                await m_vm.Connection.ChangeIdentityAsync(identity, CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                Array.Clear(password);
            }
            m_vm.ConnectionStatus = $"User changed to {user}.";
        }
        catch (Exception error) when (error is ServiceResultException or InvalidOperationException or AggregateException
            or System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            m_vm.ConnectionStatus = $"Change user failed: {error.Message}";
            ShowError($"Change user failed: {error.Message}");
            MainWindowLog.WorkspaceOperationFailed(m_log, "change-user", error);
        }
    }

    private async Task ReconnectAsync()
    {
        if (m_vm.Connection.Session is not { } session)
        {
            m_vm.ConnectionStatus = "Not connected.";
            return;
        }
        try
        {
            m_vm.ConnectionStatus = "Reconnecting…";
            await session.ReconnectAsync(null, null, CancellationToken.None).ConfigureAwait(true);
            m_vm.ConnectionStatus = "Reconnected.";
        }
        catch (Exception error) when (error is ServiceResultException or InvalidOperationException)
        {
            m_vm.ConnectionStatus = $"Reconnect failed: {error.Message}";
            ShowError($"Reconnect failed: {error.Message}");
            MainWindowLog.WorkspaceOperationFailed(m_log, "reconnect", error);
        }
    }

    private async Task LocalesAsync()
    {
        var dlg = new LocalePickerDialog(m_vm.Connection);
        await dlg.ShowDialog(m_window).ConfigureAwait(true);
    }

    private async Task OpenCertificateStoreAsync()
    {
        try
        {
            ApplicationConfiguration cfg = await m_vm.Connection.GetConfigAsync().ConfigureAwait(true);
            var dlg = new CertificateStoreDialog(cfg, m_vm.Telemetry);
            await dlg.ShowDialog(m_window).ConfigureAwait(true);
        }
        catch (Exception error) when (error is ServiceResultException or InvalidOperationException
            or System.IO.IOException or UnauthorizedAccessException)
        {
            m_vm.ConnectionStatus = $"Certificates dialog failed: {error.Message}";
            MainWindowLog.WorkspaceOperationFailed(m_log, "certificates", error);
        }
    }

    private async Task SaveWorkspaceAsync()
    {
        try
        {
            IStorageFile? file = await m_window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save UaLens workspace",
                DefaultExtension = "subex",
                SuggestedFileName = "workspace.subex",
                FileTypeChoices = [new FilePickerFileType("UaLens workspace") { Patterns = s_sessionPatterns }]
            }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }
            await SessionFile.SaveAsync(m_vm.SnapshotSession(), file.Path.LocalPath).ConfigureAwait(true);
            m_vm.ConnectionStatus = $"Workspace saved to {file.Name}.";
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            ShowError($"Save workspace failed: {error.Message}");
            MainWindowLog.WorkspaceOperationFailed(m_log, "save", error);
        }
    }

    private void ShowError(string message)
    {
        m_window.RequiredControl<TextBlock>("OperationErrorText").Text = message;
        m_window.RequiredControl<Border>("OperationErrorBanner").IsVisible = true;
    }

    private void ClearError()
    {
        m_window.RequiredControl<Border>("OperationErrorBanner").IsVisible = false;
    }

    private static readonly string[] s_sessionPatterns = ["*.subex", "*.json"];

    private MenuItem? m_engineItem;
    private MenuItem? m_changeUserItem;
    private MenuItem? m_reconnectItem;
    private MenuItem? m_pipelineItem;
    private MenuItem? m_localesItem;
    private readonly MainWindow m_window;
    private readonly MainViewModel m_vm;
    private readonly ILogger m_log;
}
