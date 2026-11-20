// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using UaLens.ViewModels;

namespace UaLens.Plugins.Companions.Machinery
{
    /// <summary>
    /// Transient composition of the existing prepared-task UI, never registered or saved as a document.
    /// </summary>
    internal sealed class MachineryTaskDialog : Window, IAsyncDisposable
    {
        /// <summary>
        /// Displays selected-item context and reuses the existing typed-input, Prepare, Review and Run controls.
        /// </summary>
        public MachineryTaskDialog(CompanionPlugin task, string selectedContext, string? jobId = null)
        {
            TaskModel = task ?? throw new ArgumentNullException(nameof(task));
            m_jobId = jobId is { Length: > 0 and <= 256 } ? jobId : null;
            Title = "Selected Machinery task";
            Width = 860;
            Height = 600;
            MinWidth = 600;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var close = new Button { Content = "Close task", HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (_, _) => Close();
            var heading = new TextBlock
            {
                Text = selectedContext + "\nNo permission or preparation is inherited from Machinery inspection.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 8)
            };
            var footer = new StackPanel { Margin = new Thickness(12, 8), Children = { close } };
            DockPanel.SetDock(heading, Dock.Top);
            DockPanel.SetDock(footer, Dock.Bottom);
            Content = new DockPanel
            {
                Children =
                {
                    heading,
                    footer,
                    new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = ((IPlugin)TaskModel).View
                    }
                }
            };
            TaskModel.PropertyChanged += OnTaskChanged;
            KeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    Close();
                }
            };
        }

        /// <summary>
        /// Gets the reused, transient task model.
        /// </summary>
        public CompanionPlugin TaskModel { get; }

        /// <summary>
        /// Closes on parent cancellation; the owning action then drains its task model.
        /// </summary>
        public async Task ShowOwnedAsync(Window owner, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(owner);
            cancellationToken.ThrowIfCancellationRequested();
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
                Dispatcher.UIThread.Post(Close));
            try
            {
                await ShowDialog(owner).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                await DisposeAsync().ConfigureAwait(true);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return new(m_disposal ??= DisposeCoreAsync());
        }

        private Task DisposeCoreAsync()
        {
            TaskModel.PropertyChanged -= OnTaskChanged;
            Close();
            return Task.CompletedTask;
        }

        private void OnTaskChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(CompanionPlugin.SelectedOperation) || m_jobId is null)
            {
                return;
            }
            foreach (CompanionInputEditor field in TaskModel.InputFields)
            {
                if (field.Definition.Name == "jobOrderId" && field.IsText)
                {
                    field.Text = m_jobId;
                }
            }
        }

        private readonly string? m_jobId;
        private Task? m_disposal;
    }
}
