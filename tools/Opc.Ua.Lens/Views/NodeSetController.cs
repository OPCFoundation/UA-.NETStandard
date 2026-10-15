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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.NodeSets.Loading;
using UaLens.ViewModels;

namespace UaLens.Views
{
    /// <summary>
    /// Bridges file pickers and dependency consent to the transactional model loader.
    /// </summary>
    internal sealed class NodeSetController : INodeSetDependencyResolver, IAsyncDisposable
    {
        public NodeSetController(
            MainWindow window,
            MainViewModel viewModel,
            INodeSetRepository? repository = null,
            IStorageProvider? storageProvider = null)
        {
            m_window = window ?? throw new ArgumentNullException(nameof(window));
            m_viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            m_storageProvider = storageProvider;
            m_ownedRepository = repository is null ? new UaNodeSetRepository() : null;
            m_repository = repository ?? m_ownedRepository!;
            m_log = viewModel.Telemetry.CreateLogger("NodeSets");
        }

        public void Attach()
        {
            m_window.RequiredControl<MenuItem>("MenuOpenNodeSets").Click +=
                async (_, _) => await OpenAsync().ConfigureAwait(true);
            m_window.RequiredControl<Button>("WelcomeOpenNodeSetsBtn").Click +=
                async (_, _) => await OpenAsync().ConfigureAwait(true);
            m_window.RequiredControl<MenuItem>("MenuCloseNodeSets").Click +=
                async (_, _) => await m_viewModel.CloseNodeSetsAsync().ConfigureAwait(true);
            m_window.RequiredControl<Button>("CloseNodeSetsButton").Click +=
                async (_, _) => await m_viewModel.CloseNodeSetsAsync().ConfigureAwait(true);
            m_window.RequiredControl<Button>("CancelNodeSetLoadButton").Click +=
                async (_, _) =>
                {
                    if (m_import is { } import)
                    {
                        await import.CancelAsync().ConfigureAwait(true);
                    }
                };
        }

        public Task<NodeSetDocument?> ResolveAsync(NodeSetRequirement requirement, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(requirement);
            return Dispatcher.UIThread.InvokeAsync(
                () => ResolveOnUiAsync(requirement, cancellationToken),
                DispatcherPriority.Normal, cancellationToken).GetTask().Unwrap();
        }

        public async ValueTask DisposeAsync()
        {
            if (m_import is { } import)
            {
                await import.CancelAsync().ConfigureAwait(true);
            }
            await m_openWork.ConfigureAwait(true);
            m_import?.Dispose();
            m_ownedRepository?.Dispose();
        }

        private Task OpenAsync()
        {
            if (m_import is not null || m_window.IsClosingRequested)
            {
                return m_openWork;
            }
            m_import = new CancellationTokenSource();
            m_openWork = OpenCoreAsync(m_import);
            return m_openWork;
        }

        private async Task OpenCoreAsync(CancellationTokenSource import)
        {
            try
            {
                IReadOnlyList<IStorageFile> files = await PickFilesAsync(
                    "Open NodeSet2 XML files (live connection closes after successful import)",
                    true, import.Token).ConfigureAwait(true);
                if (files.Count == 0)
                {
                    return;
                }
                ClearError();
                await m_viewModel.OpenNodeSetsAsync(
                    [.. files.Select(LocalPath)], this, import.Token).ConfigureAwait(true);
                ClearError();
            }
            catch (OperationCanceledException)
            {
                m_viewModel.ConnectionStatus = "NodeSet2 import cancelled.";
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                or InvalidOperationException or ArgumentException or FormatException
                or HttpRequestException or System.Xml.XmlException
                or System.Text.Json.JsonException or ServiceResultException or NotSupportedException)
            {
                ShowError($"NodeSet2 import failed: {error.Message}");
                MainWindowLog.WorkspaceOperationFailed(m_log, "open-nodesets", error);
            }
            finally
            {
                import.Dispose();
                m_import = null;
            }
        }

        private async Task<NodeSetDocument?> ResolveOnUiAsync(
            NodeSetRequirement requirement, CancellationToken cancellationToken)
        {
            var dialog = new NodeSetDependencyDialog(requirement);
            NodeSetDependencyChoice choice = await dialog.PromptAsync(m_window, cancellationToken).ConfigureAwait(true);
            if (choice == NodeSetDependencyChoice.Cancel)
            {
                return null;
            }
            string title = $"Select NodeSet2 for {requirement.ModelUri}";
            if (choice == NodeSetDependencyChoice.Download)
            {
                try
                {
                    m_viewModel.ConnectionStatus = $"Searching UA-Nodeset for {requirement.ModelUri}...";
                    NodeSetDocument? document = await m_repository.FindAsync(requirement, cancellationToken)
                        .ConfigureAwait(true);
                    if (document is not null)
                    {
                        return document;
                    }
                    title = $"Not in UA-Nodeset. Select local NodeSet2 for {requirement.ModelUri}";
                    m_viewModel.ConnectionStatus = title;
                }
                catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException
                    or System.Text.Json.JsonException or InvalidOperationException)
                {
                    ShowError($"UA-Nodeset lookup failed: {error.Message}. Choose a local dependency or cancel.");
                    MainWindowLog.WorkspaceOperationFailed(m_log, "download-nodeset", error);
                    title = $"Download failed. Select local NodeSet2 for {requirement.ModelUri}";
                }
            }
            IReadOnlyList<IStorageFile> files = await PickFilesAsync(title, false, cancellationToken)
                .ConfigureAwait(true);
            return files.Count == 0
                ? null
                : await NodeSetDocument.ReadAsync(LocalPath(files[0]), cancellationToken).ConfigureAwait(true);
        }

        private async Task<IReadOnlyList<IStorageFile>> PickFilesAsync(
            string title, bool multiple, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<IStorageFile> files = await (m_storageProvider ?? m_window.StorageProvider)
                .OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = multiple,
                    FileTypeFilter = [new FilePickerFileType("OPC UA NodeSet2 XML") { Patterns = ["*.xml"] }]
                }).WaitAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            return files;
        }

        private static string LocalPath(IStorageFile file)
        {
            return file.TryGetLocalPath()
                ?? throw new NotSupportedException(
                    "Select a local NodeSet2 file so sibling dependencies can be resolved.");
        }

        private void ShowError(string message)
        {
            m_viewModel.ConnectionStatus = message;
            m_window.RequiredControl<TextBlock>("OperationErrorText").Text = message;
            m_window.RequiredControl<Border>("OperationErrorBanner").IsVisible = true;
        }

        private void ClearError()
        {
            m_window.RequiredControl<TextBlock>("OperationErrorText").Text = null;
            m_window.RequiredControl<Border>("OperationErrorBanner").IsVisible = false;
        }

        private readonly MainWindow m_window;
        private readonly MainViewModel m_viewModel;
        private readonly INodeSetRepository m_repository;
        private readonly UaNodeSetRepository? m_ownedRepository;
        private readonly IStorageProvider? m_storageProvider;
        private readonly ILogger m_log;
        private CancellationTokenSource? m_import;
        private Task m_openWork = Task.CompletedTask;
    }
}
