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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ConnectedNodeInspectorTests
{
    [TestCase(NodeClass.Variable)]
    [TestCase(NodeClass.VariableType)]
    [TestCase(NodeClass.Object)]
    [TestCase(NodeClass.ObjectType)]
    [TestCase(NodeClass.View)]
    [TestCase(NodeClass.ReferenceType)]
    [TestCase(NodeClass.DataType)]
    [TestCase(NodeClass.Method)]
    public Task NodeClassSelectsTheRelevantAttributeGroupsAndPreservesValueMetadata(NodeClass nodeClass)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            context.Read = (ids, _) =>
            {
                Assert.That(ids.Count, Is.EqualTo(19));
                Assert.That(ids.ToArray()!.Select(id => id.NodeId), Is.All.EqualTo(s_node));
                return ValueTask.FromResult(new ReadResponse
                {
                    Results = ids.ToArray()!.Select(id => id.AttributeId == Attributes.Historizing
                        ? DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid)
                        : new DataValue(id.AttributeId switch
                        {
                            Attributes.NodeClass => Variant.From((int)nodeClass),
                            Attributes.DisplayName => Variant.From(new LocalizedText("Inspected node")),
                            Attributes.BrowseName => Variant.From(new QualifiedName("InspectedNode", 2)),
                            Attributes.Value => Variant.From(42),
                            Attributes.DataType => Variant.From(DataTypeIds.Int32),
                            Attributes.ValueRank => Variant.From(ValueRanks.Scalar),
                            _ => Variant.Null
                        }, StatusCodes.Good, s_time, s_time)).ToArray()
                });
            };
            var dialog = new ViewNodeStateDialog(context.Desktop.Browser, context.Desktop.Connection, s_node);
            dialog.Show(DesktopInteraction.Owner);
            try
            {
                NodeStateItem root = DesktopInteraction.Control<TreeView>(dialog, "StateTree")
                    .Items.Cast<NodeStateItem>().Single();
                Assert.That(root.Header, Does.Contain("Inspected node").And.Contain(nodeClass.ToString()));
                NodeStateItem attributes = root.Children.Single(item => item.Header == "Attributes");
                Assert.That(attributes.Children.Select(item => item.Header), Does.Contain("NodeClass = " + nodeClass));
                bool variable = nodeClass is NodeClass.Variable or NodeClass.VariableType;
                Assert.That(root.Children.Any(item => item.Header == "Value"), Is.EqualTo(variable));
                if (variable)
                {
                    NodeStateItem value = root.Children.Single(item => item.Header == "Value");
                    Assert.That(value.Children.Select(item => item.Header), Does.Contain("WrappedValue = 42"));
                    Assert.That(value.Children.Select(item => item.Header),
                        Has.Some.StartsWith("SourceTimestamp = 2026-01-02"));
                    Assert.That(attributes.Children.Select(item => item.Header),
                        Has.Some.StartsWith("Historizing  (BadAttributeIdInvalid"));
                }
                if (nodeClass is NodeClass.Object or NodeClass.View)
                {
                    Assert.That(
                        attributes.Children.Select(item => item.Header),
                        Does.Contain("EventNotifier = (null)"));
                }
                if (nodeClass == NodeClass.ReferenceType)
                {
                    Assert.That(attributes.Children.Select(item => item.Header), Does.Contain("Symmetric = (null)"));
                }
                if (nodeClass == NodeClass.DataType)
                {
                    Assert.That(attributes.Children.Select(item => item.Header),
                        Does.Contain("DataTypeDefinition = (null)"));
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase("grouped")]
    [TestCase("empty")]
    [TestCase("failed")]
    [TestCase("namesUnavailable")]
    public Task LazyReferencesGroupPagesAndKeepUnresolvedTypeIdsVisible(string mode)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            context.Read = (ids, _) => ValueTask.FromResult(new ReadResponse
            {
                Results = ids.ToArray()!.Select(id => new DataValue(
                    id.AttributeId == Attributes.NodeClass
                        ? Variant.From((int)NodeClass.Object)
                        : Variant.Null)).ToArray()
            });
            var dialog = new ViewNodeStateDialog(context.Desktop.Browser, context.Desktop.Connection, s_node);
            dialog.Show(DesktopInteraction.Owner);
            try
            {
                NodeStateItem root = DesktopInteraction.Control<TreeView>(dialog, "StateTree")
                    .Items.Cast<NodeStateItem>().Single();
                NodeStateItem references = root.Children.Single(item => item.Header == "References");
                int browses = 0;
                context.Browse = (ids, _) =>
                {
                    browses++;
                    Assert.That(ids[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.References));
                    Assert.That(ids[0].BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
                    if (mode == "failed")
                    {
                        throw new IOException("browse failed for fixture");
                    }
                    return ValueTask.FromResult(new BrowseResponse
                    {
                        Results = [new BrowseResult
                        {
                            References = mode == "empty" ? [] : [Reference("First")],
                            ContinuationPoint = mode == "empty" ? ByteString.Empty : s_point
                        }]
                    });
                };
                context.BrowseNext = (release, points, _) =>
                {
                    Assert.That(release, Is.False);
                    Assert.That(points[0], Is.EqualTo(s_point));
                    return ValueTask.FromResult(new BrowseNextResponse
                    {
                        Results = [new BrowseResult { References = [Reference("Second")] }]
                    });
                };
                context.Read = (_, _) => mode == "namesUnavailable"
                    ? ValueTask.FromException<ReadResponse>(new IOException("optional name"))
                    : ValueTask.FromResult(new ReadResponse
                    {
                        Results = [new DataValue(Variant.From(new QualifiedName("HasComponent")))]
                    });
                await DesktopInteraction.ModelChangedAsync(references,
                    () => references.Children.All(item => item.Header != "(loading…)"), () =>
                    {
                        references.IsExpanded = true;
                        return Task.CompletedTask;
                    }).ConfigureAwait(true);
                Assert.That(browses, Is.EqualTo(1));
                if (mode is "empty" or "failed")
                {
                    Assert.That(references.Children.Single().Header,
                        Does.StartWith(mode == "empty" ? "(no references)" : "(browse failed:"));
                }
                else
                {
                    NodeStateItem group = references.Children.Single();
                    Assert.That(group.Header, Is.EqualTo(mode == "grouped" ? "HasComponent  (2)" : "i=47  (2)"));
                    Assert.That(group.Children, Has.Count.EqualTo(2));
                    Assert.That(group.Children[0].Header, Does.Contain("First"));
                    Assert.That(group.Children[1].Header, Does.Contain("Second"));
                }
                references.IsExpanded = false;
                references.IsExpanded = true;
                Assert.That(browses, Is.EqualTo(1));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static ReferenceDescription Reference(string name)
    {
        return new ReferenceDescription
        {
            NodeId = new NodeId(name, 2), ReferenceTypeId = ReferenceTypeIds.HasComponent,
            DisplayName = new LocalizedText(name), NodeClass = NodeClass.Variable
        };
    }

    private static readonly NodeId s_node = new("Inspected", 2);
    private static readonly DateTime s_time = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly ByteString s_point = new(new byte[] { 1, 2 });
}
