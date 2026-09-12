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
using Opc.Ua.Client;
using UaLens.Plugins.EventView;
using UaLens.Tests.Desktop;
using UaLens.Views;

namespace UaLens.Tests.Observe;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class EventFilterDialogWorkflowTests
{
    [TestCase(-1, 0)]
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(999.75, 999)]
    [TestCase(1000, 1000)]
    [TestCase(1001, 1000)]
    public Task FilterAcceptPreservesFieldOrderAndClampsSeverity(double input, int expected)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var current = new EventFilterConfig(50, ["Severity", "Message"], new NodeId(4001u, 2));
            var dialog = new EventFilterDialog(current);
            Task<EventFilterConfig?> shown = dialog.ShowDialog<EventFilterConfig?>(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<Button>(dialog, "PickTypeButton").IsEnabled, Is.False);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "EventTypeLabel").Text,
                    Is.EqualTo("ns=2;i=4001"));
                CheckBox[] fields = Fields(dialog);
                Assert.That(fields.Where(field => field.IsChecked == true).Select(field => field.Content),
                    Is.EqualTo(s_filterAcceptPreservesFieldOrderAndClampsSeverityExpected));
                fields.Single(field => Equals(field.Content, "SourceName")).IsChecked = true;
                fields.Single(field => Equals(field.Content, "Message")).IsChecked = false;
                DesktopInteraction.Control<Slider>(dialog, "SeveritySlider").Value = input;
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "SeverityValue").Text,
                    Is.EqualTo(expected.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                EventFilterConfig? result = await shown.ConfigureAwait(true);
                Assert.That(result!.SeverityThreshold, Is.EqualTo(expected));
                Assert.That(result.Fields, Is.EqualTo(s_filterAcceptPreservesFieldOrderAndClampsSeverityExpected2));
                Assert.That(result.EventTypeNodeId, Is.EqualTo(new NodeId(4001u, 2)));
                Assert.That(result.WhereClause, Is.Null);
                Assert.That(current.Fields, Is.EqualTo(s_filterAcceptPreservesFieldOrderAndClampsSeverityExpected3));
                Assert.That(current.SeverityThreshold, Is.EqualTo(50));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase("empty")]
    [TestCase("single")]
    [TestCase("multiple")]
    public Task PickingTypeDiscoversInheritedCustomFieldsInOrderAndKeepsOnlyExplicitSelections(string selection)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            NodeId custom = new(4001u, 2);
            var requests = new List<BrowseDescription>();
            session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ViewDescription? view, uint maximum,
                    ArrayOf<BrowseDescription> descriptions, CancellationToken token) =>
                {
                    Assert.That(header, Is.Null);
                    Assert.That(view, Is.Null);
                    Assert.That(maximum, Is.Zero);
                    Assert.That(token, Is.EqualTo(CancellationToken.None));
                    BrowseDescription request = descriptions[0];
                    requests.Add(request);
                    ReferenceDescription[] references;
                    if (request.BrowseDirection == BrowseDirection.Inverse)
                    {
                        Assert.That(request.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasSubtype));
                        Assert.That(request.IncludeSubtypes, Is.False);
                        Assert.That(request.NodeClassMask, Is.EqualTo((uint)NodeClass.ObjectType));
                        references = request.NodeId == custom
                            ? [Reference(ObjectTypeIds.BaseEventType, "BaseEventType", NodeClass.ObjectType)]
                            : [];
                    }
                    else if (request.ReferenceTypeId == ReferenceTypeIds.HasSubtype)
                    {
                        references = [Reference(custom, "BoilerEvent", NodeClass.ObjectType)];
                    }
                    else
                    {
                        Assert.That(request.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasChild));
                        Assert.That(request.IncludeSubtypes, Is.True);
                        Assert.That(request.NodeClassMask, Is.EqualTo((uint)NodeClass.Variable));
                        references = request.NodeId == custom
                            ? [Reference(new NodeId(71u), "Severity", NodeClass.Variable),
                                Reference(new NodeId(72u), "Pressure", NodeClass.Variable),
                                Reference(new NodeId(73u), "pressure", NodeClass.Variable), new ReferenceDescription()]
                            : [Reference(new NodeId(61u), "Time", NodeClass.Variable),
                                Reference(new NodeId(62u), "Severity", NodeClass.Variable)];
                    }
                    return new ValueTask<BrowseResponse>(new BrowseResponse
                    {
                        Results = [new BrowseResult { References = references }]
                    });
                });
            var original = new EventFilterConfig(350, ["Time", "Severity"]);
            var dialog = new EventFilterDialog(original, session.Object);
            Task<EventFilterConfig?> shown = dialog.ShowDialog<EventFilterConfig?>(DesktopInteraction.Owner);
            try
            {
                BrowsePickerDialog picker = await DesktopInteraction.OpenedAsync<BrowsePickerDialog>(
                    () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "PickTypeButton")))
                    .ConfigureAwait(true);
                try
                {
                    TreeView tree = DesktopInteraction.Control<TreeView>(picker, "Tree");
                    var root = (BrowsePickerNode)tree.Items[0]!;
                    Assert.That(root.NodeId, Is.EqualTo(ObjectTypeIds.BaseEventType));
                    tree.SelectedItem = root.Children.Single();
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(picker, "OkButton"));
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { },
                        Avalonia.Threading.DispatcherPriority.Background);
                }
                finally
                {
                    picker.Close();
                }
                CheckBox[] fields = Fields(dialog);
                Assert.That(fields.Select(field => field.Content),
                    Is.EqualTo(s_pickingTypeDiscoversInheritedCustomFieldsInOrderAndKeepsOnlyEExpected));
                Assert.That(fields.Select(field => field.IsChecked),
                    Is.EqualTo(new bool?[] { true, true, false, false }));
                foreach (CheckBox field in fields)
                {
                    field.IsChecked = selection == "multiple"
                        ? Equals(field.Content, "Severity") || Equals(field.Content, "Pressure")
                        : selection == "single" && Equals(field.Content, "pressure");
                }
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                EventFilterConfig? result = await shown.ConfigureAwait(true);
                Assert.That(result!.EventTypeNodeId, Is.EqualTo(custom));
                Assert.That(result.Fields, Is.EqualTo(selection switch
                {
                    "multiple" => s_pickingTypeDiscoversInheritedCustomFieldsInOrderAndKeepsOnlyEExpected2,
                    "single" => s_pickingTypeDiscoversInheritedCustomFieldsInOrderAndKeepsOnlyEExpected3,
                    _ => Array.Empty<string>()
                }));
                Assert.That(result.SeverityThreshold, Is.EqualTo(350));
                Assert.That(requests.Select(request => request.NodeId),
                    Is.EqualTo(new[] { ObjectTypeIds.BaseEventType, custom, ObjectTypeIds.BaseEventType,
                        ObjectTypeIds.BaseEventType, custom }));
                Assert.That(
                    original.Fields,
                    Is.EqualTo(s_pickingTypeDiscoversInheritedCustomFieldsInOrderAndKeepsOnlyEExpected4));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task WhereClauseExplicitClearDiffersFromCancel(bool apply)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            ContentFilter initial = Filter(new LiteralOperand(new Variant(3)), new LiteralOperand(new Variant(7)));
            var dialog = new WhereClauseDialog(initial, null);
            Task<ContentFilter?> shown = dialog.ShowDialog<ContentFilter?>(DesktopInteraction.Owner);
            try
            {
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "ClearButton"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ValidationStatus").Text,
                    Is.EqualTo("Cleared."));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "ValidateButton"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ValidationStatus").Text,
                    Is.EqualTo("Filter is empty — the server will accept all events."));
                DesktopInteraction.Click(
                    DesktopInteraction.Control<Button>(dialog, apply ? "OkButton" : "CancelButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                Assert.That(dialog.Result, Is.Null);
                Assert.That(dialog.Applied, Is.EqualTo(apply));
                Assert.That(initial.Elements.Count, Is.EqualTo(1));
                var literal = WorkflowAssertions.GetEncodeable<LiteralOperand>(initial.Elements[0].FilterOperands[0]);
                Assert.That(literal.Value, Is.EqualTo(new Variant(3)));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase("valid")]
    [TestCase("element")]
    [TestCase("arity")]
    [TestCase("noSession")]
    [TestCase("typeTreeFailure")]
    public Task ValidationShowsElementAndOperandErrorsAndApplyPreservesWorkInProgress(string kind)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            ContentFilter initial = Filter(
                kind == "element" ? new ElementOperand(99) : new LiteralOperand(new Variant(3)),
                new LiteralOperand(new Variant(7)));
            if (kind == "arity")
            {
                initial.Elements[0].FilterOperator = FilterOperator.Between;
            }
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            session.SetupGet(s => s.TypeTree).Returns(new Mock<ITypeTable>().Object);
            if (kind == "typeTreeFailure")
            {
                session.SetupGet(s => s.TypeTree).Throws(new InvalidOperationException("type tree unavailable"));
            }
            var dialog = new WhereClauseDialog(initial, kind == "noSession" ? null : session.Object);
            Task<ContentFilter?> shown = dialog.ShowDialog<ContentFilter?>(DesktopInteraction.Owner);
            try
            {
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "ValidateButton"));
                string? status = DesktopInteraction.Control<TextBlock>(dialog, "ValidationStatus").Text;
                switch (kind)
                {
                    case "valid":
                        Assert.That(status, Is.EqualTo("✓ Validates against the session's TypeTree."));
                        break;
                    case "element":
                        Assert.That(status, Does.StartWith("✗ ").And.Contain("BadContentFilterInvalid")
                            .And.Contain("[0]").And.Contain("operand[0]").And.Contain("BadFilterOperandInvalid")
                            .And.Contain("element that does not exist"));
                        break;
                    case "arity":
                        Assert.That(status, Does.Contain("BadEventFilterInvalid").And.Contain("[0]")
                            .And.Contain("correct number of operands"));
                        Assert.That(status, Does.Not.Contain("operand["));
                        break;
                    case "noSession":
                        Assert.That(status, Does.StartWith("No active session").And.Contain("Connect to validate"));
                        break;
                    default:
                        Assert.That(status, Is.EqualTo("Validate failed: type tree unavailable"));
                        break;
                }
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                ContentFilter? result = await shown.ConfigureAwait(true);
                Assert.That(dialog.Applied, Is.True);
                Assert.That(result, Is.Not.SameAs(initial));
                Assert.That(result!.Elements.Count, Is.EqualTo(1));
                Assert.That(result.Elements[0].FilterOperator,
                    Is.EqualTo(kind == "arity" ? FilterOperator.Between : FilterOperator.Equals));
                Assert.That(result.Elements[0].FilterOperands.Count, Is.EqualTo(2));
                if (kind == "element")
                {
                    var element = WorkflowAssertions.GetEncodeable<ElementOperand>(
                        result.Elements[0].FilterOperands[0]);
                    Assert.That(element.Index, Is.EqualTo(99));
                }
                var literal = WorkflowAssertions.GetEncodeable<LiteralOperand>(result.Elements[0].FilterOperands[1]);
                Assert.That(literal.Value, Is.EqualTo(new Variant(7)));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ValidationStatus").Text, Is.EqualTo(status));
                Assert.That(session.Invocations.All(call =>
                    call.Method.Name.StartsWith("get_", StringComparison.Ordinal)), Is.True);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task FilterNestedClauseClearAppliesOnlyWhenNestedEditorAccepts(bool apply)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            ContentFilter clause = Filter(new LiteralOperand(new Variant(1)), new LiteralOperand(new Variant(2)));
            var current = new EventFilterConfig(600, ["Message"], WhereClause: clause);
            var dialog = new EventFilterDialog(current);
            Task<EventFilterConfig?> shown = dialog.ShowDialog<EventFilterConfig?>(DesktopInteraction.Owner);
            try
            {
                WhereClauseDialog nested = await DesktopInteraction.OpenedAsync<WhereClauseDialog>(
                    () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "EditWhereClauseButton")))
                    .ConfigureAwait(true);
                try
                {
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(nested, "ClearButton"));
                    DesktopInteraction.Click(
                        DesktopInteraction.Control<Button>(nested, apply ? "OkButton" : "CancelButton"));
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { },
                        Avalonia.Threading.DispatcherPriority.Background);
                }
                finally
                {
                    nested.Close();
                }
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "WhereClauseSummary").Text,
                    apply ? Is.EqualTo("(none — all events pass)") : Does.Contain("Equals"));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                EventFilterConfig? result = await shown.ConfigureAwait(true);
                Assert.That(result!.WhereClause, apply ? Is.Null : Is.SameAs(clause));
                Assert.That(
                    result.Fields,
                    Is.EqualTo(s_filterNestedClauseClearAppliesOnlyWhenNestedEditorAcceptsExpected));
                Assert.That(result.SeverityThreshold, Is.EqualTo(600));
                Assert.That(clause.Elements.Count, Is.EqualTo(1));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Test]
    public Task FilterCancelLeavesOriginalSelectionAndClauseUnchanged()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            ContentFilter clause = Filter(new LiteralOperand(new Variant(1)), new LiteralOperand(new Variant(2)));
            var current = new EventFilterConfig(400, ["Time", "Message"], ObjectTypeIds.BaseEventType, clause);
            var dialog = new EventFilterDialog(current);
            Task<EventFilterConfig?> shown = dialog.ShowDialog<EventFilterConfig?>(DesktopInteraction.Owner);
            try
            {
                foreach (CheckBox field in Fields(dialog))
                {
                    field.IsChecked = false;
                }
                DesktopInteraction.Control<Slider>(dialog, "SeveritySlider").Value = 900;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                Assert.That(
                    current.Fields,
                    Is.EqualTo(s_filterCancelLeavesOriginalSelectionAndClauseUnchangedExpected));
                Assert.That(current.SeverityThreshold, Is.EqualTo(400));
                Assert.That(current.WhereClause, Is.SameAs(clause));
                Assert.That(clause.Elements[0].FilterOperands.Count, Is.EqualTo(2));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase("cycle")]
    [TestCase("cap")]
    [TestCase("empty")]
    [TestCase("bad")]
    [TestCase("fault")]
    public Task TypeDiscoveryStopsAtCycleOrHopCapAndFallsBackOnlyWhenNoFieldsAreAvailable(string graph)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.NamespaceUris).Returns(new NamespaceTable());
            var requests = new List<BrowseDescription>();
            NodeId selectedType = new(1000u, 2);
            session.Setup(value => value.BrowseAsync(
                null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), CancellationToken.None))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> descriptions,
                    CancellationToken _) =>
                {
                    BrowseDescription request = descriptions[0];
                    requests.Add(request);
                    ReferenceDescription[] references;
                    if (request.BrowseDirection == BrowseDirection.Forward &&
                        request.ReferenceTypeId == ReferenceTypeIds.HasSubtype)
                    {
                        references = [Reference(selectedType, "BoilerEvent", NodeClass.ObjectType)];
                    }
                    else if (request.BrowseDirection == BrowseDirection.Inverse)
                    {
                        Assert.That(request.NodeId.TryGetValue(out uint id), Is.True);
                        references = graph switch
                        {
                            "cycle" => [Reference(new NodeId(id == 1000 ? 1001u : 1000u, 2),
                                "Cycle", NodeClass.ObjectType)],
                            "cap" when id < 1035 => [Reference(new NodeId(id + 1, 2), "Parent", NodeClass.ObjectType)],
                            _ => []
                        };
                    }
                    else
                    {
                        Assert.That(request.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasChild));
                        Assert.That(request.NodeClassMask, Is.EqualTo((uint)NodeClass.Variable));
                        if (graph == "fault")
                        {
                            return ValueTask.FromException<BrowseResponse>(
                                new ServiceResultException(StatusCodes.BadCommunicationError));
                        }
                        if (graph == "bad")
                        {
                            return new ValueTask<BrowseResponse>(new BrowseResponse
                            {
                                Results = [new BrowseResult { StatusCode = StatusCodes.BadUserAccessDenied }]
                            });
                        }
                        Assert.That(request.NodeId.TryGetValue(out uint id), Is.True);
                        references = graph switch
                        {
                            "cap" => [Reference(new NodeId(id + 2000, 2), $"Field{id}", NodeClass.Variable)],
                            "cycle" when id == 1001 =>
                            [
                                Reference(new NodeId(3000u, 2), "ParentOnly", NodeClass.Variable),
                                Reference(new NodeId(3001u, 2), "Message", NodeClass.Variable)
                            ],
                            "cycle" =>
                            [
                                Reference(new NodeId(4000u, 2), "Message", NodeClass.Variable),
                                Reference(new NodeId(4001u, 2), "ChildOnly", NodeClass.Variable)
                            ],
                            _ => []
                        };
                    }
                    return new ValueTask<BrowseResponse>(new BrowseResponse
                    {
                        Results = [new BrowseResult { References = references }]
                    });
                });
            var original = new EventFilterConfig(615, ["Message"]);
            var dialog = new EventFilterDialog(original, session.Object);
            Task<EventFilterConfig?> shown = dialog.ShowDialog<EventFilterConfig?>(DesktopInteraction.Owner);
            try
            {
                BrowsePickerDialog picker = await DesktopInteraction.OpenedAsync<BrowsePickerDialog>(
                    () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "PickTypeButton")))
                    .ConfigureAwait(true);
                try
                {
                    TreeView tree = DesktopInteraction.Control<TreeView>(picker, "Tree");
                    var root = (BrowsePickerNode)tree.Items[0]!;
                    tree.SelectedItem = root.Children.Single();
                    int expectedCount = graph == "cap" ? 32 : graph == "cycle" ? 3 : 9;
                    StackPanel panel = DesktopInteraction.Control<StackPanel>(dialog, "FieldsPanel");
                    await DesktopInteraction.CollectionChangedAsync(panel.Children,
                        () => panel.Children.Count == expectedCount &&
                            DesktopInteraction.Control<TextBlock>(dialog, "EventTypeLabel").Text == "ns=2;i=1000",
                        () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(picker, "OkButton")))
                        .ConfigureAwait(true);
                }
                finally
                {
                    picker.Close();
                }
                CheckBox[] fields = Fields(dialog);
                string[] expectedFields;
                if (graph == "cap")
                {
                    Assert.That(fields.Select(field => field.Content),
                        Is.EqualTo(Enumerable.Range(1000, 32).Reverse().Select(id => $"Field{id}")));
                    Assert.That(requests.Count(request => request.BrowseDirection == BrowseDirection.Inverse),
                        Is.EqualTo(32));
                    Assert.That(requests.Count(request => request.ReferenceTypeId == ReferenceTypeIds.HasChild),
                        Is.EqualTo(32));
                    fields[0].IsChecked = true;
                    fields[1].IsChecked = true;
                    fields[^1].IsChecked = true;
                    expectedFields = ["Field1031", "Field1030", "Field1000"];
                }
                else if (graph == "cycle")
                {
                    Assert.That(fields.Select(field => field.Content),
                        Is.EqualTo(s_typeDiscoveryStopsAtCycleOrHopCapAndFallsBackOnlyWhenNoFieldsExpected));
                    Assert.That(requests.Count(request => request.BrowseDirection == BrowseDirection.Inverse),
                        Is.EqualTo(2));
                    Assert.That(
                        fields.Select(field => field.IsChecked),
                        Is.EqualTo(new bool?[] { false, true, false }));
                    fields[2].IsChecked = true;
                    expectedFields = ["Message", "ChildOnly"];
                }
                else
                {
                    Assert.That(
                        fields.Select(field => field.Content),
                        Is.EqualTo(s_typeDiscoveryStopsAtCycleOrHopCapAndFallsBackOnlyWhenNoFieldsExpected2));
                    Assert.That(requests, Has.Count.EqualTo(3));
                    expectedFields = ["Message"];
                }
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                EventFilterConfig? result = await shown.ConfigureAwait(true);
                Assert.That(result!.Fields, Is.EqualTo(expectedFields));
                Assert.That(result.EventTypeNodeId, Is.EqualTo(selectedType));
                Assert.That(result.SeverityThreshold, Is.EqualTo(615));
                Assert.That(
                    original.Fields,
                    Is.EqualTo(s_filterNestedClauseClearAppliesOnlyWhenNestedEditorAcceptsExpected));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static ContentFilter Filter(FilterOperand first, FilterOperand second)
    {
        return new ContentFilter
        {
            Elements = [new ContentFilterElement
            {
                FilterOperator = FilterOperator.Equals,
                FilterOperands = [new ExtensionObject(first), new ExtensionObject(second)]
            }]
        };
    }

    private static CheckBox[] Fields(EventFilterDialog dialog)
    {
        return DesktopInteraction.Control<StackPanel>(dialog, "FieldsPanel").Children.OfType<CheckBox>().ToArray();
    }

    private static ReferenceDescription Reference(NodeId id, string name, NodeClass nodeClass)
    {
        return new ReferenceDescription
        {
            NodeId = id, BrowseName = new QualifiedName(name),
            DisplayName = new LocalizedText(name), NodeClass = nodeClass
        };
    }

    private static readonly string[] s_filterAcceptPreservesFieldOrderAndClampsSeverityExpected =
    [
        "Message",
        "Severity",
    ];
    private static readonly string[] s_filterAcceptPreservesFieldOrderAndClampsSeverityExpected2 =
    [
        "SourceName",
        "Severity",
    ];
    private static readonly string[] s_filterAcceptPreservesFieldOrderAndClampsSeverityExpected3 =
    [
        "Severity",
        "Message",
    ];
    private static readonly string[] s_pickingTypeDiscoversInheritedCustomFieldsInOrderAndKeepsOnlyEExpected =
    [
        "Time",
        "Severity",
        "Pressure",
        "pressure",
    ];
    private static readonly string[] s_pickingTypeDiscoversInheritedCustomFieldsInOrderAndKeepsOnlyEExpected2 =
    [
        "Severity",
        "Pressure",
    ];
    private static readonly string[] s_pickingTypeDiscoversInheritedCustomFieldsInOrderAndKeepsOnlyEExpected3 =
    [
        "pressure",
    ];
    private static readonly string[] s_pickingTypeDiscoversInheritedCustomFieldsInOrderAndKeepsOnlyEExpected4 =
    [
        "Time",
        "Severity",
    ];
    private static readonly string[] s_filterNestedClauseClearAppliesOnlyWhenNestedEditorAcceptsExpected =
    [
        "Message",
    ];
    private static readonly string[] s_filterCancelLeavesOriginalSelectionAndClauseUnchangedExpected =
    [
        "Time",
        "Message",
    ];
    private static readonly string[] s_typeDiscoveryStopsAtCycleOrHopCapAndFallsBackOnlyWhenNoFieldsExpected =
    [
        "ParentOnly",
        "Message",
        "ChildOnly",
    ];
    private static readonly string[] s_typeDiscoveryStopsAtCycleOrHopCapAndFallsBackOnlyWhenNoFieldsExpected2 =
    [
        "EventId",
        "EventType",
        "SourceName",
        "SourceNode",
        "Time",
        "ReceiveTime",
        "LocalTime",
        "Message",
        "Severity",
    ];
}
