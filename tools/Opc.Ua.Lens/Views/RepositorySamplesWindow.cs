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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UaLens.Samples;

namespace UaLens.Views
{
    /// <summary>
    /// Modeless setup for allowlisted, locally built samples. It borrows the sample
    /// owner, never grants peer trust, and drains the run before this window closes.
    /// </summary>
    internal sealed class RepositorySamplesWindow : Window, IAsyncDisposable
    {
        public RepositorySamplesWindow(
            IRepositorySampleService samples,
            ILogger log,
            Action<Uri> useEndpoint,
            Func<CancellationToken, Task<string?>>? chooseSource = null)
        {
            m_samples = samples ?? throw new ArgumentNullException(nameof(samples));
            m_log = log ?? throw new ArgumentNullException(nameof(log));
            m_useEndpoint = useEndpoint ?? throw new ArgumentNullException(nameof(useEndpoint));
            m_chooseSource = chooseSource ?? PickSourceAsync;
            Title = "Repository samples";
            Width = 760;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            m_sample = new ComboBox
            {
                Name = "SampleBox",
                ItemsSource = RepositorySampleCatalog.Entries.ToArray(),
                ItemTemplate = new FuncDataTemplate<RepositorySampleDescriptor>((entry, _) =>
                    new TextBlock { Text = entry?.Title }),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            m_root = new TextBox { Name = "SourceRootBox", IsReadOnly = true };
            m_browse = new Button { Name = "BrowseSourceButton", Content = "Choose trusted checkout..." };
            m_configuration = new ComboBox
            {
                Name = "BuildConfigurationBox",
                ItemsSource = Enum.GetValues<RepositorySampleBuildConfiguration>(),
                SelectedItem = RepositorySampleBuildConfiguration.Release
            };
            m_framework = new ComboBox
            {
                Name = "FrameworkBox",
                ItemsSource = Enum.GetValues<RepositorySampleFramework>(),
                SelectedItem = RepositorySampleFramework.Net10
            };
            m_layout = new ComboBox
            {
                Name = "BuildLayoutBox",
                ItemsSource = Enum.GetValues<RepositorySampleBuildLayout>(),
                SelectedItem = RepositorySampleBuildLayout.FrameworkDirectory
            };
            m_seconds = new NumericUpDown
            {
                Name = "RunSecondsBox",
                Minimum = 1,
                Maximum = 300,
                Value = 60,
                Increment = 1
            };
            m_trustSource = new CheckBox
            {
                Name = "TrustSourceBox",
                Content = "I trust this checkout and its existing managed build."
            };
            m_configure = new Button { Name = "ConfigureSampleButton", Content = "Check setup" };
            m_confirmRun = new CheckBox
            {
                Name = "ConfirmSampleRunBox",
                Content = "I authorize this bounded local sample process and its private run resources."
            };
            m_start = new Button { Name = "StartSampleButton", Content = "Start sample" };
            m_stop = new Button { Name = "StopSampleButton", Content = "Stop / retry cleanup" };
            m_use = new Button { Name = "UseSampleEndpointButton", Content = "Use advertised endpoint" };
            m_status = new TextBlock { Name = "SampleStatus", TextWrapping = TextWrapping.Wrap };
            m_output = new TextBox
            {
                Name = "SampleOutput",
                IsReadOnly = true,
                AcceptsReturn = true,
                Height = 180
            };
            var close = new Button { Name = "CloseSampleButton", Content = "Close and stop sample" };
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(18),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Use only a trusted local repository and a built sample. UaLens does not build, " +
                                "download, install, or trust a server automatically.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        m_sample, m_root, m_browse,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal, Spacing = 8,
                            Children = { m_configuration, m_framework, m_layout }
                        },
                        m_trustSource, m_configure,
                        new TextBlock { Text = "Sample runtime (seconds)" },
                        m_seconds, m_confirmRun,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal, Spacing = 8,
                            Children = { m_start, m_stop, m_use }
                        },
                        new TextBlock
                        {
                            Text = "Stop first waits for the sample's configured timed shutdown, then uses a bounded " +
                                "owned-process termination fallback. Readiness does not authorize secure connection.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        m_status, m_output, close
                    }
                }
            };
            m_sample.SelectionChanged += (_, _) => InvalidateSetup();
            m_configuration.SelectionChanged += (_, _) => InvalidateSetup();
            m_framework.SelectionChanged += (_, _) => InvalidateSetup();
            m_layout.SelectionChanged += (_, _) => InvalidateSetup();
            m_trustSource.IsCheckedChanged += (_, _) =>
            {
                m_confirmRun.IsChecked = false;
                Refresh();
            };
            m_confirmRun.IsCheckedChanged += (_, _) => Refresh();
            m_seconds.ValueChanged += (_, _) => m_confirmRun.IsChecked = false;
            m_browse.Click += async (_, _) => await BeginCommandAsync(ChooseSourceAsync).ConfigureAwait(true);
            m_configure.Click += async (_, _) => await BeginCommandAsync(ConfigureAsync).ConfigureAwait(true);
            m_start.Click += async (_, _) => await BeginCommandAsync(StartAsync).ConfigureAwait(true);
            m_stop.Click += async (_, _) =>
            {
                if (!m_stopTask.IsCompleted)
                {
                    return;
                }
                m_stopTask = StopCommandAsync();
                await m_stopTask.ConfigureAwait(true);
            };
            m_use.Click += (_, _) =>
            {
                RepositorySampleSnapshot snapshot = m_samples.Snapshot;
                if (snapshot.Phase == RepositorySamplePhase.AdvertisedReady && snapshot.Evidence is { } evidence)
                {
                    m_useEndpoint(evidence.Endpoint);
                }
            };
            close.Click += (_, _) => Close();
            m_timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Refresh());
            Opened += (_, _) => m_timer.Start();
            Closing += async (_, args) =>
            {
                if (m_closeReady)
                {
                    return;
                }
                args.Cancel = true;
                try
                {
                    await DisposeAsync().ConfigureAwait(true);
                }
                catch (Exception error) when (IsExpectedFailure(error))
                {
                    ShowFailure(error);
                }
            };
            Refresh();
        }

        public ValueTask DisposeAsync()
        {
            if (m_disposal is null || m_disposal.IsFaulted)
            {
                m_disposal = CloseCoreAsync();
            }
            return new ValueTask(m_disposal);
        }

        private async Task BeginCommandAsync(Func<CancellationToken, Task> command)
        {
            if (m_busy || m_closeRequested)
            {
                return;
            }
            m_pending = RunCommandAsync(command);
            await m_pending.ConfigureAwait(true);
        }

        private async Task RunCommandAsync(Func<CancellationToken, Task> command)
        {
            m_busy = true;
            m_error = null;
            Refresh();
            try
            {
                await command(m_lifetime.Token).ConfigureAwait(true);
            }
            catch (Exception error) when (IsExpectedFailure(error))
            {
                ShowFailure(error);
            }
            finally
            {
                m_busy = false;
                Refresh();
            }
        }

        private async Task ChooseSourceAsync(CancellationToken cancellationToken)
        {
            string? root = await m_chooseSource(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (root is not null)
            {
                m_root.Text = root;
                InvalidateSetup();
            }
        }

        private async Task ConfigureAsync(CancellationToken cancellationToken)
        {
            m_configured = false;
            m_confirmRun.IsChecked = false;
            if (m_trustSource.IsChecked != true ||
                m_sample.SelectedItem is not RepositorySampleDescriptor sample ||
                m_configuration.SelectedItem is not RepositorySampleBuildConfiguration configuration ||
                m_framework.SelectedItem is not RepositorySampleFramework framework ||
                m_layout.SelectedItem is not RepositorySampleBuildLayout layout)
            {
                throw new InvalidOperationException("Choose and explicitly trust the local source and build first.");
            }
            RepositorySampleSnapshot snapshot = await m_samples.ConfigureAsync(
                sample.Id, new RepositorySampleSource(m_root.Text ?? string.Empty, configuration, framework, layout),
                cancellationToken).ConfigureAwait(true);
            m_configured = snapshot.Phase == RepositorySamplePhase.Configured;
        }

        private async Task StartAsync(CancellationToken cancellationToken)
        {
            bool confirmed = m_confirmRun.IsChecked == true;
            m_confirmRun.IsChecked = false;
            if (!m_configured || m_trustSource.IsChecked != true || !confirmed)
            {
                throw new InvalidOperationException("Check the trusted setup and authorize this run before starting.");
            }
            await m_samples.StartAsync(
                new RepositorySampleRunOptions { RunSeconds = checked((int)(m_seconds.Value ?? 0)) },
                cancellationToken).ConfigureAwait(true);
        }

        private async Task StopCommandAsync()
        {
            m_confirmRun.IsChecked = false;
            m_error = null;
            try
            {
                await m_samples.StopAsync().ConfigureAwait(true);
            }
            catch (Exception error) when (IsExpectedFailure(error))
            {
                ShowFailure(error);
            }
            finally
            {
                Refresh();
            }
        }

        private async Task CloseCoreAsync()
        {
            m_closeRequested = true;
            m_confirmRun.IsChecked = false;
            Refresh();
            await m_lifetime.CancelAsync().ConfigureAwait(true);
            RepositorySampleSnapshot stopped = await m_samples.StopAsync().ConfigureAwait(true);
            await Task.WhenAll(m_pending, m_stopTask).ConfigureAwait(true);
            if (stopped.OwnsResources || m_samples.Snapshot.OwnsResources)
            {
                throw new RepositorySampleException(RepositorySampleFailure.Cleanup,
                    "The sample still owns resources. Retry Stop before closing this window.");
            }
            m_timer.Stop();
            m_lifetime.Dispose();
            await Task.Yield();
            m_closeReady = true;
            if (IsVisible)
            {
                Close();
            }
        }

        private async Task<string?> PickSourceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Select a trusted local repository", AllowMultiple = false })
                .ConfigureAwait(true);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return folders.Count == 0 ? null : folders[0].TryGetLocalPath() ??
                    throw new InvalidOperationException("Choose an absolute local directory.");
            }
            finally
            {
                foreach (IStorageFolder folder in folders)
                {
                    folder.Dispose();
                }
            }
        }

        private void InvalidateSetup()
        {
            m_configured = false;
            m_trustSource.IsChecked = false;
            m_confirmRun.IsChecked = false;
            Refresh();
        }

        private void Refresh()
        {
            RepositorySampleSnapshot snapshot = m_samples.Snapshot;
            if (snapshot.Phase == RepositorySamplePhase.RequiresConfiguration)
            {
                m_configured = false;
            }
            Task<RepositorySampleSnapshot> completion = m_samples.Completion;
            if (!ReferenceEquals(completion, m_observedCompletion) && completion.Exception is { } failure)
            {
                m_observedCompletion = completion;
                MainWindowLog.WorkspaceOperationFailed(m_log, "repository-sample-completion", failure);
                m_error = $"Sample completion failed: {failure.GetBaseException().GetType().Name}.";
            }
            bool idle = !m_busy && !snapshot.OwnsResources && !m_closeRequested;
            m_sample.IsEnabled = idle;
            m_browse.IsEnabled = idle;
            m_configuration.IsEnabled = idle;
            m_framework.IsEnabled = idle;
            m_layout.IsEnabled = idle;
            m_trustSource.IsEnabled = idle;
            m_configure.IsEnabled = idle && m_trustSource.IsChecked == true;
            m_seconds.IsEnabled = idle;
            m_confirmRun.IsEnabled = idle && m_configured;
            m_start.IsEnabled = idle &&
                m_configured &&
                m_trustSource.IsChecked == true &&
                m_confirmRun.IsChecked == true;
            m_stop.IsEnabled = snapshot.OwnsResources && m_stopTask.IsCompleted && !m_closeReady;
            m_use.IsEnabled = snapshot.Phase == RepositorySamplePhase.AdvertisedReady &&
                snapshot.Evidence is not null &&
                !m_closeRequested;
            m_status.Text = m_error ??
                ($"{snapshot.Phase}: {snapshot.Message}\n" +
                    $"Owned PID: {snapshot.ProcessId}; exit: {snapshot.ExitCode}; " +
                    $"forced termination requested: {snapshot.ForcedTermination}");
            m_output.Text = string.Join(Environment.NewLine,
                snapshot.Output.ToArray()?.Select(line => line.Text) ?? []);
        }

        private void ShowFailure(Exception error)
        {
            MainWindowLog.WorkspaceOperationFailed(m_log, "repository-sample", error);
            m_error = error is OperationCanceledException ? "Sample operation canceled." : error.Message;
            Refresh();
        }

        private static bool IsExpectedFailure(Exception error)
        {
            return error is RepositorySampleException or IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException or OperationCanceledException or
                OverflowException or System.ComponentModel.Win32Exception or Opc.Ua.ServiceResultException;
        }

        private readonly IRepositorySampleService m_samples;
        private readonly ILogger m_log;
        private readonly Action<Uri> m_useEndpoint;
        private readonly Func<CancellationToken, Task<string?>> m_chooseSource;
        private readonly ComboBox m_sample;
        private readonly TextBox m_root;
        private readonly Button m_browse;
        private readonly ComboBox m_configuration;
        private readonly ComboBox m_framework;
        private readonly ComboBox m_layout;
        private readonly NumericUpDown m_seconds;
        private readonly CheckBox m_trustSource;
        private readonly CheckBox m_confirmRun;
        private readonly Button m_configure;
        private readonly Button m_start;
        private readonly Button m_stop;
        private readonly Button m_use;
        private readonly TextBlock m_status;
        private readonly TextBox m_output;
        private readonly DispatcherTimer m_timer;
        private readonly CancellationTokenSource m_lifetime = new();
        private Task m_pending = Task.CompletedTask;
        private Task m_stopTask = Task.CompletedTask;
        private Task? m_disposal;
        private Task<RepositorySampleSnapshot>? m_observedCompletion;
        private bool m_configured;
        private bool m_busy;
        private bool m_closeRequested;
        private bool m_closeReady;
        private string? m_error;
    }
}
