// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
public sealed partial class ViewNodeStateDialogWorkflowTests
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
}
