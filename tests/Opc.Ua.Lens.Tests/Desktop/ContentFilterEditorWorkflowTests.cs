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
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ContentFilterEditorWorkflowTests
{
    [TestCase("Literal")]
    [TestCase("Array")]
    [TestCase("Element")]
    [TestCase("Attribute")]
    [TestCase("SimpleAttribute")]
    public Task OperandKindsRoundTripExactValues(string kind)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            FilterOperand original = kind switch
            {
                "Literal" => new LiteralOperand(Variant.From(-7)),
                "Array" => new LiteralOperand(Variant.From((ArrayOf<int>)[1, 3, 5])),
                "Element" => new ElementOperand(3),
                "Attribute" => new AttributeOperand
                {
                    NodeId = new NodeId("before", 2), AttributeId = Attributes.Value
                },
                _ => new SimpleAttributeOperand
                {
                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                    BrowsePath = [new QualifiedName("Machine", 2), new QualifiedName("Temperature", 2)],
                    AttributeId = Attributes.Value
                }
            };
            var dialog = new FilterOperandEditDialog(original, null);
            Task<FilterOperand?> shown = dialog.ShowDialog<FilterOperand?>(DesktopInteraction.Owner);
            try
            {
                if (kind is "Literal" or "Array")
                {
                    Assert.That(DesktopInteraction.Control<TextBox>(dialog, "LiteralValue").Text,
                        Is.EqualTo(kind == "Literal" ? "-7" : "[ 1 3 5 ]"));
                    DesktopInteraction.Control<TextBox>(dialog, "LiteralValue").Text =
                        kind == "Literal" ? "2147483647" : "8, -4, 12";
                }
                else if (kind == "Element")
                {
                    DesktopInteraction.Control<NumericUpDown>(dialog, "ElementIndex").Value = 9;
                }
                else if (kind == "Attribute")
                {
                    DesktopInteraction.Control<TextBox>(dialog, "AttrNodeId").Text = "ns=2;s=Motor";
                    DesktopInteraction.Control<TextBox>(dialog, "AttrAlias").Text = "Source";
                    DesktopInteraction.Control<TextBox>(dialog, "AttrPath").Text = "/2:Folder.2:Reading";
                    DesktopInteraction.Control<ComboBox>(dialog, "AttrAttributeId").SelectedIndex = 6;
                    DesktopInteraction.Control<TextBox>(dialog, "AttrIndexRange").Text = "1:3";
                }
                else
                {
                    Assert.That(DesktopInteraction.Control<TextBox>(dialog, "SimplePath").Text,
                        Is.EqualTo("2:Machine\n2:Temperature"));
                    DesktopInteraction.Control<TextBox>(dialog, "SimpleTypeId").Text = "ns=2;i=1001";
                    DesktopInteraction.Control<TextBox>(dialog, "SimplePath").Text = " 2:Device \n\n 2:Actual ";
                    DesktopInteraction.Control<ComboBox>(dialog, "SimpleAttributeId").SelectedIndex = 4;
                    DesktopInteraction.Control<TextBox>(dialog, "SimpleIndexRange").Text = "0:2";
                }
                Assert.That(DesktopInteraction.Control<Button>(dialog, "AttrPickNode").IsEnabled, Is.False);
                Assert.That(dialog.Result, Is.Null);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                FilterOperand? result = await shown.ConfigureAwait(true);
                Assert.That(dialog.Result, Is.SameAs(result));
                Assert.That(result, Is.Not.SameAs(original));
                Type expectedType = kind switch
                {
                    "Literal" or "Array" => typeof(LiteralOperand),
                    "Element" => typeof(ElementOperand),
                    "Attribute" => typeof(AttributeOperand),
                    _ => typeof(SimpleAttributeOperand)
                };
                Assert.That(result, Is.TypeOf(expectedType));
                switch (result)
                {
                    case LiteralOperand literal when kind == "Literal":
                        Assert.That(literal.Value, Is.EqualTo(Variant.From(int.MaxValue)));
                        Assert.That(((LiteralOperand)original).Value, Is.EqualTo(Variant.From(-7)));
                        break;
                    case LiteralOperand literal:
                        Assert.That(literal.Value.TryGetValue(out ArrayOf<int> array), Is.True);
                        Assert.That(array.ToArray(), Is.EqualTo(new[] { 8, -4, 12 }));
                        Assert.That(((LiteralOperand)original).Value,
                            Is.EqualTo(Variant.From((ArrayOf<int>)[1, 3, 5])));
                        break;
                    case ElementOperand element:
                        Assert.That(element.Index, Is.EqualTo(9));
                        Assert.That(((ElementOperand)original).Index, Is.EqualTo(3));
                        break;
                    case AttributeOperand attribute:
                        Assert.That(attribute.NodeId, Is.EqualTo(new NodeId("Motor", 2)));
                        Assert.That(attribute.Alias, Is.EqualTo("Source"));
                        Assert.That(attribute.AttributeId, Is.EqualTo(Attributes.DataType));
                        Assert.That(attribute.IndexRange, Is.EqualTo("1:3"));
                        Assert.That(attribute.BrowsePath.Elements.Count, Is.EqualTo(2));
                        Assert.That(attribute.BrowsePath.Elements[0].ReferenceTypeId,
                            Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
                        Assert.That(attribute.BrowsePath.Elements[1].ReferenceTypeId,
                            Is.EqualTo(ReferenceTypeIds.Aggregates));
                        Assert.That(attribute.BrowsePath.Elements[1].TargetName,
                            Is.EqualTo(new QualifiedName("Reading", 2)));
                        Assert.That(((AttributeOperand)original).NodeId, Is.EqualTo(new NodeId("before", 2)));
                        break;
                    case SimpleAttributeOperand simple:
                        Assert.That(simple.TypeDefinitionId, Is.EqualTo(new NodeId(1001u, 2)));
                        Assert.That(simple.BrowsePath.ToArray(),
                            Is.EqualTo(new[] { new QualifiedName("Device", 2), new QualifiedName("Actual", 2) }));
                        Assert.That(simple.AttributeId, Is.EqualTo(Attributes.DisplayName));
                        Assert.That(simple.IndexRange, Is.EqualTo("0:2"));
                        Assert.That(((SimpleAttributeOperand)original).TypeDefinitionId,
                            Is.EqualTo(ObjectTypeIds.BaseEventType));
                        break;
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase("Literal", "Could not parse")]
    [TestCase("NodeId", "Invalid NodeId")]
    [TestCase("Element", "Element index")]
    public Task InvalidOperandInputCannotAcceptAndCancelPreservesOriginal(string kind, string error)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var original = new LiteralOperand(Variant.From(37));
            var dialog = new FilterOperandEditDialog(original, null);
            Task<FilterOperand?> shown = dialog.ShowDialog<FilterOperand?>(DesktopInteraction.Owner);
            try
            {
                if (kind == "Literal")
                {
                    DesktopInteraction.Control<TextBox>(dialog, "LiteralValue").Text = "not an Int32";
                }
                else if (kind == "Element")
                {
                    DesktopInteraction.Control<ComboBox>(dialog, "KindCombo").SelectedItem = FilterOperandKind.Element;
                    DesktopInteraction.Control<NumericUpDown>(dialog, "ElementIndex").Value = null;
                }
                else
                {
                    DesktopInteraction.Control<ComboBox>(dialog, "KindCombo").SelectedItem =
                        FilterOperandKind.Attribute;
                    DesktopInteraction.Control<TextBox>(dialog, "AttrNodeId").Text = "ns=two;i=bad";
                }
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(shown.IsCompleted, Is.False);
                Assert.That(dialog.Result, Is.Null);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel").Text,
                    kind == "Literal" ? Does.StartWith("Error:") : Does.Contain(error));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                Assert.That(original.Value, Is.EqualTo(Variant.From(37)));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Test]
    public Task EmptySimpleOperandUsesWireDefaultsAndSlashPathPreservesNamespaces()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new FilterOperandEditDialog(null, null);
            Task<FilterOperand?> shown = dialog.ShowDialog<FilterOperand?>(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<Grid>(dialog, "SimplePanel").IsVisible, Is.True);
                Assert.That(DesktopInteraction.Control<Grid>(dialog, "LiteralPanel").IsVisible, Is.False);
                DesktopInteraction.Control<TextBox>(dialog, "SimpleTypeId").Text = string.Empty;
                DesktopInteraction.Control<TextBox>(dialog, "SimplePath").Text = "/2:Device/2:Reading";
                DesktopInteraction.Control<ComboBox>(dialog, "SimpleAttributeId").SelectedIndex = -1;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                var result = (SimpleAttributeOperand)(await shown.ConfigureAwait(true))!;
                Assert.That(result.TypeDefinitionId.IsNull, Is.True);
                Assert.That(result.AttributeId, Is.EqualTo(Attributes.Value));
                Assert.That(result.BrowsePath.ToArray(),
                    Is.EqualTo(new[] { new QualifiedName("Device", 2), new QualifiedName("Reading", 2) }));
                Assert.That(result.IndexRange, Is.Empty);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Test]
    public Task FilterAddEditRemoveAndReorderPreserveOperandMeaning()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            ContentFilter original = Filter(
                (FilterOperator.Equals, new LiteralOperand(Variant.From(11))),
                (FilterOperator.GreaterThan, new LiteralOperand(Variant.From(20))));
            var editor = new ContentFilterEditor();
            DesktopInteraction.Owner.Content = editor;
            try
            {
                editor.Initialize(original, null);
                ListBox elements = DesktopInteraction.Control<ListBox>(editor, "ElementsList");
                ListBox operands = DesktopInteraction.Control<ListBox>(editor, "OperandsList");
                DesktopInteraction.Control<ComboBox>(editor, "OperatorCombo").SelectedItem = FilterOperator.LessThan;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(editor, "MoveUpBtn"));
                Assert.That(elements.SelectedIndex, Is.Zero);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(editor, "MoveDownBtn"));
                Assert.That(elements.SelectedIndex, Is.EqualTo(1));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(editor, "AddElementBtn"));
                Assert.That(elements.SelectedIndex, Is.EqualTo(2));
                FilterOperandEditDialog add = await DesktopInteraction.OpenedAsync<FilterOperandEditDialog>(
                    () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(editor, "AddOperandBtn")))
                    .ConfigureAwait(true);
                DesktopInteraction.Control<ComboBox>(add, "KindCombo").SelectedItem = FilterOperandKind.Element;
                DesktopInteraction.Control<NumericUpDown>(add, "ElementIndex").Value = 0;
                await DesktopInteraction.ChangedAsync(operands, () => operands.Items.Count == 1, () =>
                {
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(add, "OkButton"));
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(operands.Items[0], Is.EqualTo("[0] Element[0]"));
                operands.SelectedIndex = 0;
                FilterOperandEditDialog edit = await DesktopInteraction.OpenedAsync<FilterOperandEditDialog>(
                    () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(editor, "EditOperandBtn")))
                    .ConfigureAwait(true);
                DesktopInteraction.Control<ComboBox>(edit, "KindCombo").SelectedItem = FilterOperandKind.Literal;
                DesktopInteraction.Control<ComboBox>(edit, "LiteralDataType").SelectedItem = "Int32";
                DesktopInteraction.Control<TextBox>(edit, "LiteralValue").Text = "99";
                await DesktopInteraction.ChangedAsync(operands,
                    () => operands.Items.Count == 1 && Equals(operands.Items[0], "[0] Literal(99)"), () =>
                    {
                        DesktopInteraction.Click(DesktopInteraction.Control<Button>(edit, "OkButton"));
                        return Task.CompletedTask;
                    }).ConfigureAwait(true);
                ContentFilter result = editor.BuildResult();
                Assert.That(result.Elements.ToList().Select(element => element.FilterOperator),
                    Is.EqualTo(new[] { FilterOperator.GreaterThan, FilterOperator.LessThan, FilterOperator.Equals }));
                var literal = WorkflowAssertions.GetEncodeable<LiteralOperand>(result.Elements[2].FilterOperands[0]);
                Assert.That(literal.Value, Is.EqualTo(Variant.From(99)));
                Assert.That(((ContentFilterElementVm)elements.Items[2]!).Summary, Is.EqualTo("Equals(Literal(99))"));
                operands.SelectedIndex = 0;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(editor, "RemoveOperandBtn"));
                Assert.That(editor.BuildResult().Elements[2].FilterOperands.IsEmpty, Is.True);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(editor, "RemoveElementBtn"));
                Assert.That(editor.BuildResult().Elements.Count, Is.EqualTo(2));
                Assert.That(original.Elements[0].FilterOperator, Is.EqualTo(FilterOperator.Equals));
                var originalLiteral = WorkflowAssertions.GetEncodeable<LiteralOperand>(
                    original.Elements[0].FilterOperands[0]);
                Assert.That(originalLiteral.Value, Is.EqualTo(Variant.From(11)));
            }
            finally
            {
                foreach (Window child in DesktopInteraction.Owner.OwnedWindows.ToArray())
                {
                    child.Close();
                }
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [Test]
    public Task ReinitializeReplacesDraftAndEmptySelectionDisablesMutation()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            var editor = new ContentFilterEditor();
            editor.Initialize(Filter((FilterOperator.Equals, new LiteralOperand(Variant.From(11)))), null);
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(editor, "AddElementBtn"));
            editor.Initialize(Filter((FilterOperator.IsNull, new LiteralOperand(Variant.From("fresh")))), null);
            ContentFilter result = editor.BuildResult();
            Assert.That(result.Elements.Count, Is.EqualTo(1));
            Assert.That(result.Elements[0].FilterOperator, Is.EqualTo(FilterOperator.IsNull));
            var literal = WorkflowAssertions.GetEncodeable<LiteralOperand>(result.Elements[0].FilterOperands[0]);
            Assert.That(literal.Value, Is.EqualTo(Variant.From("fresh")));
            Assert.That(DesktopInteraction.Control<ListBox>(editor, "ElementsList").SelectedIndex, Is.Zero);
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(editor, "AddOperandBtn"));
            Assert.That(DesktopInteraction.Control<TextBlock>(editor, "EditorStatus").Text,
                Is.EqualTo("Cannot open operand editor outside a window context."));
            Assert.That(editor.BuildResult().Elements[0].FilterOperands.Count, Is.EqualTo(1));
            editor.Initialize(null, null);
            Assert.That(editor.BuildResult().Elements.IsEmpty, Is.True);
            Assert.That(DesktopInteraction.Control<Button>(editor, "RemoveElementBtn").IsEnabled, Is.False);
            Assert.That(DesktopInteraction.Control<Button>(editor, "AddOperandBtn").IsEnabled, Is.False);
            Assert.That(DesktopInteraction.Control<ListBox>(editor, "OperandsList").Items, Is.Empty);
            return Task.CompletedTask;
        });
    }

    private static ContentFilter Filter(params (FilterOperator Operator, FilterOperand Operand)[] elements)
    {
        return new ContentFilter
        {
            Elements = elements.Select(element => new ContentFilterElement
            {
                FilterOperator = element.Operator,
                FilterOperands = [new ExtensionObject(element.Operand)]
            }).ToArray()
        };
    }
}
