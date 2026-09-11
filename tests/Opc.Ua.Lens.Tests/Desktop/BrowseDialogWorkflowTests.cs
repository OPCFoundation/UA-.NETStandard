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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Tests.StructuredValues;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class BrowseDialogWorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task PickerAcceptsOnlyAllowedClassAndPredicate(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var requests = new List<BrowseDescription>();
            context.Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> ids,
                    CancellationToken _) =>
                {
                    requests.Add(ids[0]);
                    return ValueTask.FromResult(Reply(ids[0].NodeId == ObjectIds.ObjectsFolder
                        ? [Reference(21, "Eligible", NodeClass.Variable), Reference(22, "Rejected", NodeClass.Variable),
                            Reference(23, "Denied", NodeClass.Variable), Reference(24, "Object", NodeClass.Object),
                            new ReferenceDescription { NodeId = ExpandedNodeId.Null }]
                        : []));
                });
            var inspected = new List<NodeId>();
            var options = new BrowsePickerDialog.Options(context.Session.Object, ObjectIds.ObjectsFolder,
                "Select a value", NodeClass.Variable, AcceptPredicate: (id, _) =>
                {
                    inspected.Add(id);
                    return id == new NodeId(23u)
                        ? Task.FromException<bool>(new ServiceResultException(StatusCodes.BadUserAccessDenied))
                        : Task.FromResult(id == new NodeId(21u));
                });
            var dialog = new BrowsePickerDialog(options);
            Task<NodeId?> shown = dialog.ShowDialog<NodeId?>(DesktopInteraction.Owner);
            try
            {
                TreeView tree = DesktopInteraction.Control<TreeView>(dialog, "Tree");
                var root = (BrowsePickerNode)tree.Items[0]!;
                Assert.That(root.Children.Select(child => child.IsSelectable),
                    Is.EqualTo(s_pickerAcceptsOnlyAllowedClassAndPredicateExpected));
                Assert.That(inspected, Is.EqualTo(new[] { new NodeId(21u), new NodeId(22u), new NodeId(23u) }));
                tree.SelectedItem = root.Children[1];
                Assert.That(DesktopInteraction.Control<Button>(dialog, "OkButton").IsEnabled, Is.False);
                tree.SelectedItem = root.Children[0];
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel").Text,
                    Is.EqualTo("Selected: Eligible  ·  i=21"));
                root.Children[0].IsExpanded = true;
                root.Children[0].IsExpanded = false;
                root.Children[0].IsExpanded = true;
                Assert.That(root.Children[0].Children, Is.Empty);
                Assert.That(requests.Select(request => request.NodeId),
                    Is.EqualTo(new[] { ObjectIds.ObjectsFolder, new NodeId(21u) }));
                Assert.That(requests[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
                Assert.That(requests[0].IncludeSubtypes, Is.True);
                Assert.That(requests[0].NodeClassMask, Is.Zero);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(
                    dialog, accept ? "OkButton" : "CancelButton"));
                NodeId? result = await shown.ConfigureAwait(true);
                Assert.That(result.HasValue, Is.EqualTo(accept));
                if (accept)
                {
                    Assert.That(result.GetValueOrDefault(), Is.EqualTo(new NodeId(21u)));
                    Assert.That(dialog.PickedDisplay, Is.EqualTo("Eligible"));
                    Assert.That(dialog.PickedNodeClass, Is.EqualTo(NodeClass.Variable));
                }
                else
                {
                    Assert.That(dialog.PickedDisplay, Is.Empty);
                    Assert.That(dialog.PickedNodeId.HasValue, Is.False);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Test]
    public Task FlattenedBrowseDeduplicatesCyclesAndKeepsBreadthFirstPaths()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var browsed = new List<NodeId>();
            context.Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> ids,
                    CancellationToken _) =>
                {
                    NodeId id = ids[0].NodeId;
                    browsed.Add(id);
                    ReferenceDescription[] references = id == new NodeId(1u)
                        ? [Reference(2, "Folder", NodeClass.Object), Reference(3, "Bravo", NodeClass.Variable),
                            Reference(2, "Duplicate", NodeClass.Object), new ReferenceDescription()]
                        : id == new NodeId(2u)
                            ? [Reference(4, "Delta", NodeClass.Variable), Reference(1, "Cycle", NodeClass.Object)]
                            : [];
                    return ValueTask.FromResult(Reply(references));
                });
            var dialog = new FlattenedBrowseDialog(new BrowsePickerDialog.Options(
                context.Session.Object, new NodeId(1u), "Values", NodeClass.Variable));
            TextBlock status = DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel");
            Task<NodeId?> shown = Task.FromResult<NodeId?>(null);
            try
            {
                await DesktopInteraction.ChangedAsync(status,
                    () => status.Text == "Visited 4 · matched 2 · pending 0", () =>
                    {
                        shown = dialog.ShowDialog<NodeId?>(DesktopInteraction.Owner);
                        return Task.CompletedTask;
                    }).ConfigureAwait(true);
                ListBox list = DesktopInteraction.Control<ListBox>(dialog, "ResultsList");
                FlattenedNode[] rows = list.Items.Cast<FlattenedNode>().ToArray();
                Assert.That(rows.Select(row => row.NodeId), Is.EqualTo(new[] { new NodeId(3u), new NodeId(4u) }));
                Assert.That(rows.Select(row => row.BrowsePath), Is.EqualTo(s_flattenedBrowseDeduplicatesCyclesAndKeepsBreadthFirstPathsExpected));
                Assert.That(browsed,
                    Is.EqualTo(new[] { new NodeId(1u), new NodeId(2u), new NodeId(3u), new NodeId(4u) }));
                list.SelectedIndex = 1;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.EqualTo(new NodeId(4u)));
                Assert.That(dialog.PickedItem!.BrowsePath, Is.EqualTo("/Folder/Delta"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase(15, 15, 16, 16)]
    [TestCase(16, 16, 17, 17)]
    [TestCase(17, 17, 17, 18)]
    [TestCase(18, 17, 17, 18)]
    public Task FlattenedBrowseHonorsActualDepthBoundary(int links, int matches, int reads, int visited)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var requests = new List<NodeId>();
            context.Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> ids,
                    CancellationToken _) =>
                {
                    requests.Add(ids[0].NodeId);
                    Assert.That(ids[0].NodeId.TryGetValue(out uint id), Is.True);
                    return ValueTask.FromResult(Reply(id < 100 + links
                        ? [Reference(id + 1, $"N{id + 1}", NodeClass.Variable)] : []));
                });
            var dialog = new FlattenedBrowseDialog(new BrowsePickerDialog.Options(
                context.Session.Object, new NodeId(100u), "Bounded", NodeClass.Variable));
            TextBlock status = DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel");
            try
            {
                await DesktopInteraction.ChangedAsync(status,
                    () => status.Text == $"Visited {visited} · matched {matches} · pending 0", () =>
                    {
                        _ = dialog.ShowDialog(DesktopInteraction.Owner);
                        return Task.CompletedTask;
                    }).ConfigureAwait(true);
                FlattenedNode[] rows = DesktopInteraction.Control<ListBox>(dialog, "ResultsList")
                    .Items.Cast<FlattenedNode>().ToArray();
                Assert.That(rows.Select(row => row.NodeId),
                    Is.EqualTo(Enumerable.Range(101, matches).Select(id => new NodeId((uint)id))));
                Assert.That(requests, Has.Count.EqualTo(reads));
                Assert.That(requests[^1], Is.EqualTo(new NodeId((uint)(99 + reads))));
                Assert.That(rows[^1].BrowsePath.Split('/', StringSplitOptions.RemoveEmptyEntries),
                    Has.Length.EqualTo(matches));
                Assert.That(DesktopInteraction.Control<ProgressBar>(dialog, "Progress").Value, Is.EqualTo(100));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Test]
    public Task CloseCancelsCooperatingBrowseWithoutAcceptingSelection()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var pending = new TaskCompletionSource<BrowseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken browseToken = default;
            Task<BrowseResponse>? request = null;
            context.Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> _,
                    CancellationToken token) =>
                {
                    browseToken = token;
                    request = pending.Task.WaitAsync(token);
                    return new ValueTask<BrowseResponse>(request);
                });
            var dialog = new FlattenedBrowseDialog(new BrowsePickerDialog.Options(
                context.Session.Object, ObjectIds.ObjectsFolder, "Cancelable", NodeClass.Variable));
            Task<NodeId?> shown = dialog.ShowDialog<NodeId?>(DesktopInteraction.Owner);
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
            Assert.That(await shown.ConfigureAwait(true), Is.Null);
            Assert.That(browseToken.IsCancellationRequested, Is.True);
            await Assert.ThatAsync(() => request!, Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(true);
            Assert.That(dialog.PickedNodeId.HasValue, Is.False);
            Assert.That(DesktopInteraction.Control<ListBox>(dialog, "ResultsList").Items, Is.Empty);
            pending.TrySetResult(Reply([]));
        });
    }

    [Test]
    public Task PickerLazyBrowseFailureSurfacesStatusAndDoesNotRetryOnExpansion()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var pending = new TaskCompletionSource<BrowseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> ids,
                    CancellationToken _) => ids[0].NodeId == ObjectIds.ObjectsFolder
                        ? ValueTask.FromResult(Reply([Reference(2, "Restricted", NodeClass.Object)]))
                        : new ValueTask<BrowseResponse>(pending.Task));
            var dialog = new BrowsePickerDialog(new BrowsePickerDialog.Options(
                context.Session.Object, ObjectIds.ObjectsFolder, "Lazy failure", NodeClass.Variable));
            _ = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                var root = (BrowsePickerNode)DesktopInteraction.Control<TreeView>(dialog, "Tree").Items[0]!;
                BrowsePickerNode child = root.Children.Single();
                TextBlock status = DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel");
                await DesktopInteraction.ChangedAsync(status,
                    () => status.Text?.StartsWith("Browse failed:", StringComparison.Ordinal) == true, () =>
                    {
                        child.IsExpanded = true;
                        pending.SetException(new ServiceResultException(
                            StatusCodes.BadUserAccessDenied, "controlled browse denial"));
                        return Task.CompletedTask;
                    }).ConfigureAwait(true);
                Assert.That(status.Text, Does.Contain("controlled browse denial"));
                Assert.That(child.Children, Is.Empty);
                Assert.That(child.ChildrenLoaded, Is.True);
                Assert.That(child.IsSelectable, Is.False);
                child.IsExpanded = false;
                child.IsExpanded = true;
                context.Session.Verify(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
                Assert.That(dialog.PickedNodeId.HasValue, Is.False);
            }
            finally
            {
                pending.TrySetResult(Reply([]));
                dialog.Close();
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task FlattenedBrowseContinuesPastDeniedOrFaultedBranches(bool serviceFault)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var requested = new List<NodeId>();
            context.Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> ids,
                    CancellationToken _) =>
                {
                    NodeId id = ids[0].NodeId;
                    requested.Add(id);
                    if (id == new NodeId(2u))
                    {
                        return serviceFault
                            ? ValueTask.FromException<BrowseResponse>(
                                new ServiceResultException(StatusCodes.BadUserAccessDenied))
                            : ValueTask.FromResult(new BrowseResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = [new BrowseResult { StatusCode = StatusCodes.BadUserAccessDenied }]
                            });
                    }
                    return ValueTask.FromResult(Reply(id == new NodeId(1u)
                        ? [Reference(2, "Denied", NodeClass.Object), Reference(3, "Good", NodeClass.Object)]
                        : id == new NodeId(3u) ? [Reference(4, "Value", NodeClass.Variable)] : []));
                });
            var dialog = new FlattenedBrowseDialog(new BrowsePickerDialog.Options(
                context.Session.Object, new NodeId(1u), "Partial browse", NodeClass.Variable));
            TextBlock status = DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel");
            try
            {
                await DesktopInteraction.ChangedAsync(status,
                    () => status.Text == "Visited 4 · matched 1 · pending 0", () =>
                    {
                        _ = dialog.ShowDialog(DesktopInteraction.Owner);
                        return Task.CompletedTask;
                    }).ConfigureAwait(true);
                FlattenedNode row = DesktopInteraction.Control<ListBox>(dialog, "ResultsList")
                    .Items.Cast<FlattenedNode>().Single();
                Assert.That(row.NodeId, Is.EqualTo(new NodeId(4u)));
                Assert.That(row.BrowsePath, Is.EqualTo("/Good/Value"));
                Assert.That(requested,
                    Is.EqualTo(new[] { new NodeId(1u), new NodeId(2u), new NodeId(3u), new NodeId(4u) }));
                Assert.That(DesktopInteraction.Control<ProgressBar>(dialog, "Progress").IsIndeterminate, Is.False);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static ReferenceDescription Reference(uint id, string name, NodeClass nodeClass)
    {
        return new ReferenceDescription
        {
            NodeId = new ExpandedNodeId(id),
            BrowseName = new QualifiedName(name),
            DisplayName = new LocalizedText(name),
            NodeClass = nodeClass,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            IsForward = true
        };
    }

    private static BrowseResponse Reply(ReferenceDescription[] references)
    {
        return new BrowseResponse
        {
            ResponseHeader = new ResponseHeader(),
            Results = [new BrowseResult { StatusCode = StatusCodes.Good, References = references }]
        };
    }

    private static readonly bool[] s_pickerAcceptsOnlyAllowedClassAndPredicateExpected =
    [
        true,
        false,
        false,
        false,
    ];
    private static readonly string[] s_flattenedBrowseDeduplicatesCyclesAndKeepsBreadthFirstPathsExpected =
    [
        "/Bravo",
        "/Folder/Delta",
    ];
}
