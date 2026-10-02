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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[NonParallelizable]
public sealed class ViewNodeStateDialogWorkflowTests
{
    [TestCase("Empty")]
    [TestCase("Value")]
    [TestCase("Failure")]
    public async Task NodeStateItemLoadsOnceAndSurfacesLoaderFailure(string outcome)
    {
        int calls = 0;
        CancellationToken suppliedToken = new(canceled: true);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new NodeStateItem("References", async (node, token) =>
        {
            calls++;
            suppliedToken = token;
            await release.Task.ConfigureAwait(true);
            if (outcome == "Failure")
            {
                throw new InvalidOperationException("controlled loader failure");
            }
            node.Children.Clear();
            if (outcome == "Value")
            {
                node.Children.Add(new NodeStateItem("HasComponent = i=42"));
            }
        });
        Assert.That(item.Children.Single().Header, Is.EqualTo("(expand to load…)"));
        item.IsExpanded = true;
        item.IsExpanded = false;
        item.IsExpanded = true;
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(suppliedToken.CanBeCanceled, Is.False);
        await DesktopInteraction.CollectionChangedAsync(item.Children,
            () => outcome == "Empty" ? item.Children.Count == 0 :
                item.Children.Count == 1 && item.Children[0].Header != "(expand to load…)",
            release.SetResult).ConfigureAwait(false);
        string[] expected = outcome switch
        {
            "Empty" => [],
            "Value" => ["HasComponent = i=42"],
            _ => ["(load failed: controlled loader failure)"]
        };
        Assert.That(item.Children.Select(child => child.Header), Is.EqualTo(expected));
        item.IsExpanded = false;
        item.IsExpanded = true;
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    public Task NullOrDisconnectedNodeDisplaysSentinelWithoutRead(bool nullNode)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            var dialog = new ViewNodeStateDialog(context.Browser, context.Connection,
                nullNode ? null : new NodeId(1234u, 2));
            Task shown = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "HeaderBlock").Text,
                    Is.EqualTo(nullNode ? "NodeId: (none)" : "NodeId: ns=2;i=1234"));
                Assert.That(DesktopInteraction.Control<TreeView>(dialog, "StateTree").Items
                    .Cast<NodeStateItem>().Select(item => item.Header),
                    Is.EqualTo(s_nullOrDisconnectedNodeDisplaysSentinelWithoutReadExpected));
                Assert.That(context.ConfigurationsCreated, Is.Zero);
                Assert.That(context.Discoveries, Is.Empty);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CloseButton"));
                await shown.ConfigureAwait(true);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static readonly string[] s_nullOrDisconnectedNodeDisplaysSentinelWithoutReadExpected =
    [
        "(disconnected or null node)",
    ];
}
