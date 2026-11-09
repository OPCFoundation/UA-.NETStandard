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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using UaLens.NodeSets.Loading;

namespace UaLens.Views
{
    internal enum NodeSetDependencyChoice
    {
        Cancel,
        Browse,
        Download
    }

    /// <summary>
    /// Requests permission before contacting the official repository.
    /// </summary>
    internal sealed class NodeSetDependencyDialog : Window
    {
        public NodeSetDependencyDialog(NodeSetRequirement requirement)
        {
            ArgumentNullException.ThrowIfNull(requirement);
            Title = "Missing NodeSet2 dependency";
            Width = 660;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var browse = new Button
            {
                Name = "BrowseDependencyButton",
                Content = "Browse local file",
                IsDefault = true
            };
            var download = new Button { Name = "DownloadDependencyButton", Content = "Search and download" };
            var cancel = new Button { Name = "CancelDependencyButton", Content = "Cancel import", IsCancel = true };
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Required model: {requirement.ModelUri}\n" +
                            $"Version label: {requirement.Version ?? "(unspecified)"}\n" +
                            $"Model version: {requirement.ModelVersion ?? "(unspecified)"}\n" +
                            $"Publication date: {requirement.PublicationDate:yyyy-MM-dd}",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "Choose a local NodeSet2 XML file, or allow Lens to search and download from " +
                            "https://github.com/OPCFoundation/UA-Nodeset. No model contents are uploaded. " +
                            "If the model is not in that repository, Lens will ask for its local file.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new WrapPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { browse, download, cancel }
                    }
                }
            };
            browse.Click += (_, _) => Close(NodeSetDependencyChoice.Browse);
            download.Click += (_, _) => Close(NodeSetDependencyChoice.Download);
            cancel.Click += (_, _) => Close(NodeSetDependencyChoice.Cancel);
        }

        public async Task<NodeSetDependencyChoice> PromptAsync(Window owner, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using ConfiguredAsyncDisposable registration = cancellationToken.Register(() =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsVisible)
                    {
                        Close(NodeSetDependencyChoice.Cancel);
                    }
                })).ConfigureAwait(true);
            NodeSetDependencyChoice choice = await ShowDialog<NodeSetDependencyChoice>(owner).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            return choice;
        }
    }
}
