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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.Themes;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Thin desktop shell. Presentation, node interaction and connection dialog
/// dispatch live in dedicated modules; this window only composes them, owns the
/// top-level lifecycle and forwards keyboard input to the shared command path.
/// </summary>
internal sealed partial class MainWindow : Window, IAsyncDisposable
{
    /// <summary>
    /// Set by <c>Program.cs</c> before the desktop lifetime starts so a freshly
    /// constructed <see cref="MainViewModel"/> can attach to an already running
    /// <see cref="Diagnostics.ResourceMonitorHost"/>.
    /// </summary>
    public static UaLens.Diagnostics.ResourceMonitorHost? PendingResourceMonitor { get; set; }

    public MainWindow(MainViewModel viewModel, AppearancePreferences? appearance = null)
        : this(viewModel, appearance, favoritesPath: null)
    {
    }

    internal MainWindow(MainViewModel viewModel, AppearancePreferences? appearance, string? favoritesPath)
    {
        m_vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        m_appearance = appearance ?? new AppearancePreferences();
        if (PendingResourceMonitor is not null)
        {
            m_vm.ResourceMonitor = PendingResourceMonitor;
        }
        DataContext = m_vm;
        InitializeComponent();

        ILogger log = m_vm.Telemetry.CreateLogger("Shell");
        m_nodes = new NodeInteractionController(this, m_vm, log);
        m_connection = new ConnectionController(this, m_vm, log);
        m_shell = new ShellPresenter(this, m_vm, m_connection, log, ChangeThemeAsync, favoritesPath);
        m_nodes.Attach();
        m_connection.Attach();
        m_shell.Attach();

        AddHandler(InputElement.KeyDownEvent, (_, e) => m_shell.OnKeyDown(e), RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            try
            {
                await ThemeManager.LoadPreferenceAsync(m_appearance).ConfigureAwait(true);
            }
            catch (Exception error) when (error is System.IO.IOException
                or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                m_vm.ConnectionStatus = $"Appearance preference could not be loaded: {error.Message}";
            }
        };
        Closing += async (_, args) =>
        {
            if (m_closeReady)
            {
                return;
            }
            args.Cancel = true;
            if (m_closing)
            {
                return;
            }
            m_closing = true;
            m_vm.ConnectionStatus = "Closing workspace and releasing connection resources…";
            try
            {
                await DisposeAsync().ConfigureAwait(true);
                m_closeReady = true;
                Close();
            }
            catch (Exception error) when (error is AggregateException or ServiceResultException
                or System.IO.IOException or OperationCanceledException)
            {
                this.RequiredControl<TextBlock>("OperationErrorText").Text = $"Shutdown failed: {error.Message}";
                this.RequiredControl<Border>("OperationErrorBanner").IsVisible = true;
                // Allow an explicit second close to force-close after a cleanup failure.
                m_closeReady = true;
                m_closing = false;
            }
        };
    }

    /// <summary>
    /// True once a close has been requested, so the trust prompt rejects and closes.
    /// </summary>
    internal bool IsClosingRequested => m_closing || m_closeReady;

    /// <summary>
    /// Awaits document and connection cleanup before the desktop lifetime can exit.
    /// </summary>
    public ValueTask DisposeAsync() => new(m_disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        await m_shell.StopAsync().ConfigureAwait(true);
        await m_vm.DisposeAsync().ConfigureAwait(true);
    }

    private async Task ChangeThemeAsync(ThemePreset preset)
    {
        try
        {
            await ThemeManager.SetThemeAsync(preset, m_appearance).ConfigureAwait(true);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            m_vm.ConnectionStatus = $"Appearance preference could not be saved: {error.Message}";
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private readonly MainViewModel m_vm;
    private readonly AppearancePreferences m_appearance;
    private readonly NodeInteractionController m_nodes;
    private readonly ConnectionController m_connection;
    private readonly ShellPresenter m_shell;
    private Task? m_disposal;
    private bool m_closing;
    private bool m_closeReady;
}

/// <summary>
/// Source-generated logging for the desktop shell modules. Shell event IDs use
/// the 400-419 range documented in <see cref="UaLensEventIds"/>.
/// </summary>
internal static partial class MainWindowLog
{
    [LoggerMessage(
        EventId = UaLensEventIds.ShellNodeSelectionFailed,
        Level = LogLevel.Debug,
        Message = "Node selection handling failed for {NodeId}.")]
    public static partial void NodeSelectionFailed(ILogger logger, NodeId nodeId, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.ShellNodeActionFailed,
        Level = LogLevel.Error,
        Message = "Node action {Action} failed.")]
    public static partial void NodeActionFailed(ILogger logger, string action, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.ShellConnectFailed,
        Level = LogLevel.Error,
        Message = "Interactive connect failed.")]
    public static partial void ConnectFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.ShellWorkspaceOperationFailed,
        Level = LogLevel.Error,
        Message = "Shell operation {Operation} failed.")]
    public static partial void WorkspaceOperationFailed(ILogger logger, string operation, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.ShellTabActionFailed,
        Level = LogLevel.Warning,
        Message = "Document strip action {Action} failed.")]
    public static partial void TabActionFailed(ILogger logger, string action, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.ShellCommandDispatchFailed,
        Level = LogLevel.Error,
        Message = "Command {Command} dispatch failed.")]
    public static partial void CommandDispatchFailed(ILogger logger, string command, Exception exception);
}
