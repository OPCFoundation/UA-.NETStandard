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
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;

namespace UaLens.Tests.ViewModels;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ConnectedBrowserTests
{
    [TestCase((int)BrowseViewKind.Objects, Objects.RootFolder, 0)]
    [TestCase((int)BrowseViewKind.Views, Objects.ViewsFolder, 0)]
    [TestCase((int)BrowseViewKind.ObjectTypes, Objects.ObjectTypesFolder, (int)NodeClass.ObjectType)]
    [TestCase((int)BrowseViewKind.VariableTypes, Objects.VariableTypesFolder, (int)NodeClass.VariableType)]
    [TestCase((int)BrowseViewKind.DataTypes, Objects.DataTypesFolder, (int)NodeClass.DataType)]
    [TestCase((int)BrowseViewKind.ReferenceTypes, Objects.ReferenceTypesFolder, (int)NodeClass.ReferenceType)]
    public Task EachViewUsesItsRootAndReferenceFamilies(int kind, uint rootId, int nodeClass)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            var requests = new List<ArrayOf<BrowseDescription>>();
            context.Browse = (ids, _) =>
            {
                requests.Add(ids);
                return ValueTask.FromResult(new BrowseResponse { Results = [new BrowseResult()] });
            };
            await context.ConnectAsync().ConfigureAwait(true);
            requests.Clear();
            BrowserViewModel browser = context.Desktop.Browser;
            if (kind == (int)BrowseViewKind.Objects)
            {
                browser.Reload();
            }
            else
            {
                await browser.SetViewKindAsync((BrowseViewKind)kind, CancellationToken.None).ConfigureAwait(true);
            }
            NodeViewModel root = browser.Roots.Single();
            Assert.That(root.NodeId, Is.EqualTo(new NodeId(rootId)));
            Assert.That(root.IsExpanded, Is.True);
            Assert.That(root.Children, Is.Empty);
            ArrayOf<BrowseDescription> sent = requests.Single();
            Assert.That(sent.Count, Is.EqualTo(nodeClass == 0 ? 3 : 1));
            if (nodeClass == 0)
            {
                Assert.That(sent[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Aggregates));
                Assert.That(sent[0].IncludeSubtypes, Is.True);
                Assert.That(sent[1].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
                Assert.That(sent[2].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasSubtype));
            }
            else
            {
                Assert.That(sent[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasSubtype));
                Assert.That(sent[0].NodeClassMask, Is.EqualTo((uint)nodeClass));
                Assert.That(sent[0].IncludeSubtypes, Is.False);
            }
            await browser.SetViewKindAsync((BrowseViewKind)kind, CancellationToken.None).ConfigureAwait(true);
            Assert.That(requests, Has.Count.EqualTo(1), "An unchanged view preserves the expanded tree.");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task LazyBrowseMergesPagesDeduplicatesAndIgnoresUnresolvableTargets(bool failure)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            BrowserViewModel browser = context.Desktop.Browser;
            var parent = new NodeViewModel(browser, NodeId.Null, new NodeId(5000u), "Parent", NodeClass.Object);
            var completion = new TaskCompletionSource<BrowseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Browse = (_, _) => new ValueTask<BrowseResponse>(completion.Task);
            int pages = 0;
            context.BrowseNext = (release, points, _) =>
            {
                pages++;
                Assert.That(release, Is.False);
                Assert.That(points[0], Is.EqualTo(s_continuation));
                return ValueTask.FromResult(new BrowseNextResponse
                {
                    Results = [new BrowseResult { References = [Child("Repeated", 1), Child("Last", 3)] }]
                });
            };
            bool onDesktop = true;
            parent.Children.CollectionChanged += (_, _) => onDesktop &= Dispatcher.UIThread.CheckAccess();
            Task loading = browser.LoadChildrenAsync(parent);
            if (failure)
            {
                completion.SetException(new IOException("browse unavailable"));
            }
            else
            {
                completion.SetResult(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            References =
                            [
                                Child("First", 1), Child("Second", 2),
                                new ReferenceDescription { NodeId = ExpandedNodeId.Null },
                                new ReferenceDescription { NodeId = new ExpandedNodeId(25u, "urn:unknown") }
                            ],
                            ContinuationPoint = s_continuation
                        },
                        new BrowseResult { StatusCode = StatusCodes.BadUserAccessDenied }
                    ]
                });
            }
            await loading.ConfigureAwait(true);
            Assert.That(onDesktop, Is.True);
            Assert.That(parent.HasItems, Is.EqualTo(!failure));
            Assert.That(parent.ChildrenLoaded, Is.True);
            if (failure)
            {
                Assert.That(parent.Children, Is.Empty);
                Assert.That(pages, Is.Zero);
            }
            else
            {
                Assert.That(parent.Children.Select(node => node.NodeId), Is.EqualTo(s_childIds));
                Assert.That(parent.Children.Select(node => node.Text), Is.EqualTo(s_childNames));
                Assert.That(parent.Children.Select(node => node.ParentNodeId), Is.All.EqualTo(parent.NodeId));
                Assert.That(pages, Is.EqualTo(1));
            }
            await browser.LoadChildrenAsync(parent).ConfigureAwait(true);
            Assert.That(pages, Is.EqualTo(failure ? 0 : 1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task ChildVariableQueriesPreserveFallbackNamesAndFailureSemantics(bool failure)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            context.Browse = (ids, _) =>
            {
                Assert.That(ids[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(ids[0].NodeClassMask, Is.EqualTo((uint)NodeClass.Variable));
                if (failure)
                {
                    throw new IOException("denied");
                }
                ReferenceDescription unnamed = Child("unused", 2);
                unnamed.DisplayName = LocalizedText.Null;
                unnamed.BrowseName = QualifiedName.Null;
                return ValueTask.FromResult(new BrowseResponse
                {
                    Results = [new BrowseResult { References = [Child("Named", 1), Child("Duplicate", 1), unnamed] }]
                });
            };
            IReadOnlyList<(NodeId NodeId, string DisplayName)> values =
                await context.Desktop.Browser.GetChildVariablesAsync(new NodeId(5000u)).ConfigureAwait(true);
            Assert.That(values, Has.Count.EqualTo(failure ? 0 : 2));
            if (!failure)
            {
                Assert.That(values[0], Is.EqualTo((new NodeId(1u, 2), "Named")));
                Assert.That(values[1], Is.EqualTo((new NodeId(2u, 2), "ns=2;i=2")));
            }
            Assert.That(await context.Desktop.Browser.GetChildVariablesAsync(NodeId.Null).ConfigureAwait(true), Is.Empty);
        });
    }

    [TestCase("good", (byte)13)]
    [TestCase("bad", null)]
    [TestCase("empty", null)]
    [TestCase("wrongType", null)]
    [TestCase("error", null)]
    public Task EventNotifierReadsDistinguishActualFlagsFromUnavailableMetadata(string result, byte? expected)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            context.Read = (ids, _) =>
            {
                Assert.That(ids.Count, Is.EqualTo(1));
                Assert.That(ids[0].AttributeId, Is.EqualTo(Attributes.EventNotifier));
                if (result == "error")
                {
                    throw new IOException("unavailable");
                }
                return ValueTask.FromResult(new ReadResponse
                {
                    Results = result switch
                    {
                        "empty" => [],
                        "bad" => [DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid)],
                        "wrongType" => [new DataValue(Variant.From("flags"))],
                        _ => [new DataValue(Variant.From((byte)13))]
                    }
                });
            };
            Assert.That(await context.Desktop.Browser.GetEventNotifierAsync(new NodeId(5000u)).ConfigureAwait(true),
                Is.EqualTo(expected));
        });
    }

    private static ReferenceDescription Child(string name, uint id)
    {
        return new ReferenceDescription
        {
            NodeId = new NodeId(id, 2), DisplayName = new LocalizedText(name), BrowseName = new QualifiedName(name),
            NodeClass = NodeClass.Variable, IsForward = true
        };
    }

    private static readonly ByteString s_continuation = new(new byte[] { 1, 2, 3 });
    private static readonly NodeId[] s_childIds = [new(1u, 2), new(2u, 2), new(3u, 2)];
    private static readonly string[] s_childNames = ["First", "Second", "Last"];
}
