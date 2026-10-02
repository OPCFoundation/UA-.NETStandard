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
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Diagnose;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class VariablePoolPickerDialogWorkflowTests
{
    [Test]
    public Task NullRootRejectedBeforeBrowse()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            Assert.That(() => new VariablePoolPickerDialog(session.Object, NodeId.Null, "Not a root"),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("root"));
            Assert.That(session.Invocations, Is.Empty);
            return Task.CompletedTask;
        });
    }

    [Test]
    public Task DiscoveryDeduplicatesCyclesAndUsesBreadthFirstNamesWhileSelectionIsIndependent()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var protocol = new PoolProtocol();
            protocol.Browse = id => new ValueTask<BrowseResponse>(Reply(id == new NodeId(1u)
                ? [Reference(2, "Folder", NodeClass.Object), Reference(3, "Temperature", NodeClass.Variable),
                    Reference(2, "Duplicate folder", NodeClass.Object), new ReferenceDescription()]
                : id == new NodeId(2u)
                    ? [Reference(1, "Cycle", NodeClass.Object),
                        new ReferenceDescription
                        {
                            NodeId = new NodeId(4u), BrowseName = new QualifiedName("Pressure"),
                            NodeClass = NodeClass.Variable
                        },
                        new ReferenceDescription { NodeId = new NodeId(5u), NodeClass = NodeClass.Variable },
                        Reference(3, "Duplicate variable", NodeClass.Variable)]
                    : []));
            var dialog = new VariablePoolPickerDialog(protocol.Session.Object, new NodeId(1u), "Boiler descendants");
            try
            {
                await DiscoverAsync(dialog, "Visited 5 · matched 3 variables · pending 0").ConfigureAwait(true);
                VariablePoolPickerItem[] rows = Items(dialog);
                Assert.That(rows.Select(row => row.NodeId),
                    Is.EqualTo(new[] { new NodeId(3u), new NodeId(4u), new NodeId(5u) }));
                Assert.That(rows.Select(row => row.BrowsePath),
                    Is.EqualTo(s_discoveryDeduplicatesCyclesAndUsesBreadthFirstNamesWhileSelecExpected));
                Assert.That(protocol.Requests.Select(request => request.NodeId),
                    Is.EqualTo(new[]
                    {
                        new NodeId(1u), new NodeId(2u), new NodeId(3u), new NodeId(4u), new NodeId(5u)
                    }));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "HeaderLabel").Text,
                    Is.EqualTo("Boiler descendants"));
                rows[1].IsSelected = true;
                rows[2].IsSelected = true;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(dialog.PickedItems, Is.EqualTo(new[]
                {
                    (new NodeId(4u), "Pressure"), (new NodeId(5u), "i=5")
                }));
                Assert.That(rows[0].IsSelected, Is.False);
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
    public Task DiscoveryHonorsDepthBoundaryWithoutBrowsingBeyondSeventeenNodes(
        int links, int matches, int requests, int visited)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var protocol = new PoolProtocol();
            protocol.Browse = id =>
            {
                Assert.That(id.TryGetValue(out uint number), Is.True);
                return new ValueTask<BrowseResponse>(Reply(number < 100 + links
                    ? [Reference(number + 1, $"N{number + 1}", NodeClass.Variable)] : []));
            };
            var dialog = new VariablePoolPickerDialog(protocol.Session.Object, new NodeId(100u), "Bounded pool");
            try
            {
                await DiscoverAsync(dialog, $"Visited {visited} · matched {matches} variables · pending 0")
                    .ConfigureAwait(true);
                VariablePoolPickerItem[] rows = Items(dialog);
                Assert.That(rows.Select(row => row.NodeId),
                    Is.EqualTo(Enumerable.Range(101, matches).Select(id => new NodeId((uint)id))));
                Assert.That(rows[^1].BrowsePath.Split('/', StringSplitOptions.RemoveEmptyEntries),
                    Has.Length.EqualTo(matches));
                Assert.That(protocol.Requests, Has.Count.EqualTo(requests));
                Assert.That(protocol.Requests[^1].NodeId, Is.EqualTo(new NodeId((uint)(99 + requests))));
                Assert.That(DesktopInteraction.Control<ProgressBar>(dialog, "Progress").Value, Is.EqualTo(100));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "SelectionLabel").Text,
                    Is.EqualTo($"0 selected · {matches} discovered"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public Task SelectAllTriStateReflectsExactPickedItems(int count)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var protocol = new PoolProtocol();
            protocol.Browse = id => new ValueTask<BrowseResponse>(Reply(id == new NodeId(1u)
                ? Enumerable.Range(0, count).Select(i => Reference((uint)(20 + i), $"Variable {i}", NodeClass.Variable))
                    .ToArray()
                : []));
            var dialog = new VariablePoolPickerDialog(protocol.Session.Object, new NodeId(1u), "Select pool");
            try
            {
                await DiscoverAsync(dialog, $"Visited {count + 1} · matched {count} variables · pending 0")
                    .ConfigureAwait(true);
                CheckBox all = DesktopInteraction.Control<CheckBox>(dialog, "SelectAllCheck");
                VariablePoolPickerItem[] rows = Items(dialog);
                Assert.That(rows.All(row => !row.IsSelected), Is.True);
                if (count > 0)
                {
                    rows[0].IsSelected = true;
                    Assert.That(all.IsChecked, count == 1 ? Is.True : Is.Null);
                    Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "SelectionLabel").Text,
                        Is.EqualTo($"1 selected · {count} discovered"));
                }
                all.IsChecked = false;
                Assert.That(rows.All(row => !row.IsSelected), Is.True);
                all.IsChecked = true;
                Assert.That(rows.All(row => row.IsSelected), Is.True);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "SelectionLabel").Text,
                    Is.EqualTo($"{count} selected · {count} discovered"));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(dialog.PickedItems,
                    Is.EqualTo(Enumerable.Range(0, count).Select(i => (new NodeId((uint)(20 + i)), $"Variable {i}"))));
                Assert.That(protocol.Requests, Has.Count.EqualTo(count + 1));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase("bad")]
    [TestCase("empty")]
    [TestCase("fault")]
    public Task UnreadableBranchDoesNotLoseVariablesAlreadyDiscoveredElsewhere(string failureKind)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var protocol = new PoolProtocol();
            protocol.Browse = id =>
            {
                if (id == new NodeId(1u))
                {
                    return new ValueTask<BrowseResponse>(Reply(
                    [
                        Reference(2, "Denied folder", NodeClass.Object),
                        Reference(3, "Temperature", NodeClass.Variable)
                    ]));
                }
                if (id == new NodeId(2u))
                {
                    return failureKind switch
                    {
                        "bad" => new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            Results = [new BrowseResult { StatusCode = StatusCodes.BadUserAccessDenied }]
                        }),
                        "empty" => new ValueTask<BrowseResponse>(new BrowseResponse()),
                        _ => ValueTask.FromException<BrowseResponse>(new ServiceResultException(StatusCodes.BadTimeout))
                    };
                }
                return new ValueTask<BrowseResponse>(Reply([]));
            };
            var dialog = new VariablePoolPickerDialog(protocol.Session.Object, new NodeId(1u), "Partial browse");
            try
            {
                await DiscoverAsync(dialog, "Visited 3 · matched 1 variables · pending 0").ConfigureAwait(true);
                VariablePoolPickerItem[] rows = Items(dialog);
                Assert.That(rows.Single().NodeId, Is.EqualTo(new NodeId(3u)));
                Assert.That(rows[0].BrowsePath, Is.EqualTo("/Temperature"));
                rows[0].IsSelected = true;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(dialog.PickedItems, Is.EqualTo(new[] { (new NodeId(3u), "Temperature") }));
                Assert.That(protocol.Requests.Select(request => request.NodeId),
                    Is.EqualTo(new[] { new NodeId(1u), new NodeId(2u), new NodeId(3u) }));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Test]
    public Task CancelDuringDiscoverySignalsOwnedTokenAndNeverAcceptsLateSelection()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reply = new TaskCompletionSource<BrowseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken token = default;
            Task<BrowseResponse>? owned = null;
            session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> _,
                    CancellationToken cancellation) =>
                {
                    token = cancellation;
                    owned = reply.Task.WaitAsync(cancellation);
                    started.SetResult();
                    return new ValueTask<BrowseResponse>(owned);
                });
            var dialog = new VariablePoolPickerDialog(session.Object, new NodeId(1u), "Cancelable pool");
            Task<IReadOnlyList<(NodeId, string)>?> shown =
                dialog.ShowDialog<IReadOnlyList<(NodeId, string)>?>(DesktopInteraction.Owner);
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                Assert.That(token.IsCancellationRequested, Is.True);
                await Assert.ThatAsync(() => owned!, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(true);
                reply.TrySetResult(Reply([Reference(7, "Late", NodeClass.Variable)]));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.That(dialog.PickedItems, Is.Null);
                Assert.That(Items(dialog), Is.Empty);
                session.Verify(s => s.BrowseAsync(null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                dialog.Close();
                reply.TrySetResult(new BrowseResponse());
            }
        });
    }

    [Test]
    public Task DiscoveryProgressReportsActualFrontierThenFinalMatchCount()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var protocol = new PoolProtocol();
            protocol.Browse = node => new ValueTask<BrowseResponse>(Reply(node == new NodeId(1u)
                ? Enumerable.Range(20, 26).Select(id => Reference((uint)id, $"V{id}", NodeClass.Variable)).ToArray()
                : []));
            var dialog = new VariablePoolPickerDialog(protocol.Session.Object, new NodeId(1u), "Wide pool");
            TextBlock status = DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel");
            var messages = new List<string?>();
            status.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBlock.TextProperty)
                {
                    messages.Add(status.Text);
                }
            };
            try
            {
                await DiscoverAsync(dialog, "Visited 27 · matched 26 variables · pending 0").ConfigureAwait(true);
                Assert.That(messages, Does.Contain("Visited 25 · matched 26 variables · pending 2"));
                Assert.That(messages[^1], Is.EqualTo("Visited 27 · matched 26 variables · pending 0"));
                Assert.That(Items(dialog).Select(item => item.NodeId),
                    Is.EqualTo(Enumerable.Range(20, 26).Select(id => new NodeId((uint)id))));
                Assert.That(protocol.Requests, Has.Count.EqualTo(27));
                Assert.That(DesktopInteraction.Control<ProgressBar>(dialog, "Progress").IsIndeterminate, Is.False);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(dialog.PickedItems, Is.Null);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static Task DiscoverAsync(VariablePoolPickerDialog dialog, string completed)
    {
        TextBlock status = DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel");
        return DesktopInteraction.ChangedAsync(status, () => status.Text == completed, () =>
        {
            _ = dialog.ShowDialog(DesktopInteraction.Owner);
            return Task.CompletedTask;
        });
    }

    private static VariablePoolPickerItem[] Items(VariablePoolPickerDialog dialog)
    {
        return DesktopInteraction.Control<ListBox>(dialog, "ResultsList")
            .Items.Cast<VariablePoolPickerItem>().ToArray();
    }

    private static BrowseResponse Reply(ReferenceDescription[] references)
    {
        return new BrowseResponse { Results = [new BrowseResult { References = references }] };
    }

    private static ReferenceDescription Reference(uint id, string name, NodeClass nodeClass)
    {
        return new ReferenceDescription
        {
            NodeId = new NodeId(id), BrowseName = new QualifiedName(name),
            DisplayName = new LocalizedText(name), NodeClass = nodeClass
        };
    }

    private sealed class PoolProtocol
    {
        public PoolProtocol()
        {
            Session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ViewDescription? view, uint limit,
                    ArrayOf<BrowseDescription> ids, CancellationToken token) =>
                {
                    Assert.That(header, Is.Null);
                    Assert.That(view, Is.Null);
                    Assert.That(limit, Is.Zero);
                    Assert.That(ids.Count, Is.EqualTo(1));
                    Assert.That(ids[0].BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
                    Assert.That(ids[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
                    Assert.That(ids[0].IncludeSubtypes, Is.True);
                    Assert.That(ids[0].NodeClassMask, Is.Zero);
                    Assert.That(ids[0].ResultMask, Is.EqualTo((uint)BrowseResultMask.All));
                    Assert.That(token.CanBeCanceled, Is.True);
                    Requests.Add(ids[0]);
                    return Browse(ids[0].NodeId);
                });
        }

        public Mock<ISession> Session { get; } = new(MockBehavior.Strict);
        public List<BrowseDescription> Requests { get; } = [];
        public Func<NodeId, ValueTask<BrowseResponse>> Browse { get; set; } =
            _ => throw new InvalidOperationException("Unexpected pool browse.");
    }

    private static readonly string[] s_discoveryDeduplicatesCyclesAndUsesBreadthFirstNamesWhileSelecExpected =
    [
        "/Temperature",
        "/Folder/Pressure",
        "/Folder/i=5",
    ];
}
