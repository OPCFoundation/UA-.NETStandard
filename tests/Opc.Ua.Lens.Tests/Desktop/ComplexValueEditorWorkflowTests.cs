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
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Models;
using UaLens.StructuredValues;
using UaLens.Tests.StructuredValues;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ComplexValueEditorWorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task OptionalControlsCommitOnlyIncludedFields(bool include)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var type = context.Register("OptionalDesktop", StructureType.StructureWithOptionalFields,
            [
                StructuredValueTestContext.Field("Required", DataTypeIds.Int32),
                StructuredValueTestContext.Field("Number", DataTypeIds.Int32, optional: true)
            ]);
            IEncodeable original = type.Type.CreateInstance();
            ((IStructure)original)["Required"] = Variant.From(3);
            ((IStructure)original)["Number"] = Variant.From(7);
            var editor = new ComplexValueEditor();
            try
            {
                await editor.InitializeAsync(type.DataTypeId, type.Definition, context.Service,
                    Variant.FromStructure(original)).ConfigureAwait(true);
                Grid[] rows = StructuredEditorActions.Body(editor).Children.OfType<Grid>().ToArray();
                rows[0].Children.OfType<TextBox>().Single().Text = "41";
                rows[1].Children.OfType<CheckBox>().Single().IsChecked = include;
                rows[1].Children.OfType<TextBox>().Single().Text = include ? "73" : "not an integer";
                Assert.That(rows[0].Children.OfType<TextBox>().Single().IsEnabled, Is.True);
                Assert.That(rows[1].Children.OfType<TextBox>().Single().IsEnabled, Is.EqualTo(include));
                Variant committed = StructuredEditorActions.Commit(editor);
                Assert.That(committed.TryGetValue<Opc.Ua.Encoders.StructureWithOptionalFields>(
                    out Opc.Ua.Encoders.StructureWithOptionalFields? result, context.MessageContext), Is.True);
                Assert.That(result!.EncodingMask, Is.EqualTo(include ? 1u : 0u));
                Assert.That(result["Required"], Is.EqualTo(Variant.From(41)));
                Assert.That(result["Number"], Is.EqualTo(include ? Variant.From(73) : Variant.Null));
                Assert.That(((IStructure)original)["Required"], Is.EqualTo(Variant.From(3)));
                Assert.That(((IStructure)original)["Number"], Is.EqualTo(Variant.From(7)));
                rows[0].Children.OfType<TextBox>().Single().Text = "2147483648";
                Assert.That(editor.TryCommit(out Variant invalid, out string? error), Is.False);
                Assert.That(invalid.IsNull, Is.True);
                Assert.That(error, Does.Contain("Required"));
            }
            finally
            {
                await editor.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public Task UnionControlsCommitOnlySelectedField(int selection)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var type = context.Register("UnionDesktop", StructureType.Union,
            [
                StructuredValueTestContext.Field("Number", DataTypeIds.Int32),
                StructuredValueTestContext.Field("Text", DataTypeIds.String)
            ]);
            IEncodeable original = type.Type.CreateInstance();
            ((IStructure)original)["Text"] = Variant.From("original");
            var editor = new ComplexValueEditor();
            try
            {
                await editor.InitializeAsync(type.DataTypeId, type.Definition, context.Service,
                    Variant.FromStructure(original)).ConfigureAwait(true);
                StackPanel body = StructuredEditorActions.Body(editor);
                body.Children.OfType<ComboBox>().Single().SelectedIndex = selection;
                Grid[] rows = body.Children.OfType<Grid>().ToArray();
                rows[0].Children.OfType<TextBox>().Single().Text = "19";
                rows[1].Children.OfType<TextBox>().Single().Text = "replacement";
                Assert.That(rows[0].Children.OfType<TextBox>().Single().IsEnabled, Is.EqualTo(selection == 1));
                Assert.That(rows[1].Children.OfType<TextBox>().Single().IsEnabled, Is.EqualTo(selection == 2));
                Variant committed = StructuredEditorActions.Commit(editor);
                Assert.That(committed.TryGetValue<Opc.Ua.Encoders.Union>(
                    out Opc.Ua.Encoders.Union? result, context.MessageContext), Is.True);
                Assert.That(result!.SwitchField, Is.EqualTo((uint)selection));
                if (selection != 0)
                {
                    Assert.That(result[selection == 1 ? "Number" : "Text"],
                        Is.EqualTo(selection == 1 ? Variant.From(19) : Variant.From("replacement")));
                }
                Assert.That(((Opc.Ua.Encoders.Union)original).SwitchField, Is.EqualTo(2));
                Assert.That(((IStructure)original)["Text"], Is.EqualTo(Variant.From("original")));
            }
            finally
            {
                await editor.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase(7)]
    [TestCase(42)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public Task EnumEditorPreservesUnlistedValuesAndAcceptsNamedSelection(int initial)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var editor = new ComplexValueEditor();
            var definition = Enumeration();
            try
            {
                await editor.InitializeAsync(DataTypeIds.Int32, definition, context.Service,
                    Variant.From(initial)).ConfigureAwait(true);
                ComboBox selector = StructuredEditorActions.Body(editor).Children.OfType<ComboBox>().Single();
                Assert.That(StructuredEditorActions.Commit(editor).TryGetValue(out int retained), Is.True);
                Assert.That(retained, Is.EqualTo(initial));
                Assert.That(selector.SelectedItem!.ToString(), Does.Contain($"({initial})"));
                selector.SelectedIndex = 1;
                Assert.That(StructuredEditorActions.Commit(editor).TryGetValue(out int changed), Is.True);
                Assert.That(changed, Is.EqualTo(7));
                selector.SelectedIndex = -1;
                Assert.That(editor.TryCommit(out Variant invalid, out string? error), Is.False);
                Assert.That(invalid.IsNull, Is.True);
                Assert.That(error, Is.EqualTo("Select an enumeration value."));
            }
            finally
            {
                await editor.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task EnumEditorRejectsInvalidDefinitionWithoutFalseCommit()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var editor = new ComplexValueEditor();
            try
            {
                var definition = new EnumDefinition
                {
                    Fields = [new EnumField { Name = "Outside", Value = (long)int.MaxValue + 1 }]
                };
                await Assert.ThatAsync(() => editor.InitializeAsync(
                    DataTypeIds.Int32, definition, context.Service, Variant.From(0)),
                    Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(true);
                Assert.That(editor.TryCommit(out Variant invalid, out string? error), Is.False);
                Assert.That(invalid.IsNull, Is.True);
                Assert.That(error, Does.Contain("Int32"));
                Assert.That(DesktopInteraction.Control<TextBlock>(editor, "HintLabel").Text, Is.EqualTo(error));
            }
            finally
            {
                await editor.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase(BuiltInType.Byte, 0x80UL)]
    [TestCase(BuiltInType.UInt16, 0x8000UL)]
    [TestCase(BuiltInType.UInt32, 0x80000000UL)]
    [TestCase(BuiltInType.UInt64, 0x8000000000000000UL)]
    public Task OptionSetCommitPreservesUnknownBitsAndUnsignedWidth(BuiltInType type, ulong high)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var editor = new ComplexValueEditor();
            Variant original = Unsigned(type, high | 1);
            var definition = new EnumDefinition
            {
                IsOptionSet = true,
                Fields = [new EnumField { Name = "Enabled", Value = 0 }, new EnumField { Name = "Alarm", Value = 1 }]
            };
            try
            {
                await editor.InitializeAsync(new NodeId((uint)type), definition, context.Service, original)
                    .ConfigureAwait(true);
                StackPanel[] rows = StructuredEditorActions.Body(editor).Children.OfType<StackPanel>().ToArray();
                rows[0].Children.OfType<CheckBox>().Single().IsChecked = false;
                rows[1].Children.OfType<CheckBox>().Single().IsChecked = true;
                Variant result = StructuredEditorActions.Commit(editor);
                Assert.That(context.RoundTrip(result), Is.EqualTo(Unsigned(type, high | 2)));
                Assert.That(result.TypeInfo.BuiltInType, Is.EqualTo(type));
                Assert.That(original, Is.EqualTo(Unsigned(type, high | 1)));
                Assert.That(context.Reads, Is.Zero);
            }
            finally
            {
                await editor.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task OptionSetCommitKeepsValueAndValidityIndependent()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            context.RegisterOptionSetBaseCodec();
            var original = new OptionSet
            {
                Value = ByteString.From([0x81, 0x80]),
                ValidBits = ByteString.From([0x01, 0x80])
            };
            var definition = new EnumDefinition
            {
                IsOptionSet = true,
                Fields = [new EnumField { Name = "Ready", Value = 0 }, new EnumField { Name = "Remote", Value = 8 }]
            };
            var editor = new ComplexValueEditor();
            try
            {
                await editor.InitializeAsync(DataTypeIds.OptionSet, definition, context.Service,
                    Variant.FromStructure(original)).ConfigureAwait(true);
                StackPanel[] rows = StructuredEditorActions.Body(editor).Children.OfType<StackPanel>().ToArray();
                CheckBox[] first = rows[0].Children.OfType<CheckBox>().ToArray();
                CheckBox[] second = rows[1].Children.OfType<CheckBox>().ToArray();
                first[0].IsChecked = false;
                first[1].IsChecked = true;
                second[0].IsChecked = true;
                second[1].IsChecked = false;
                Variant committed = context.RoundTrip(StructuredEditorActions.Commit(editor));
                Assert.That(committed.TryGetValue<OptionSet>(out OptionSet? result, context.MessageContext), Is.True);
                Assert.That(result!.Value, Is.EqualTo(ByteString.From([0x80, 0x81])));
                Assert.That(result.ValidBits, Is.EqualTo(ByteString.From([0x01, 0x80])));
                Assert.That(original.Value, Is.EqualTo(ByteString.From([0x81, 0x80])));
                Assert.That(original.ValidBits, Is.EqualTo(ByteString.From([0x01, 0x80])));
            }
            finally
            {
                await editor.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase("Null")]
    [TestCase("Empty")]
    [TestCase("Singleton")]
    public Task ArrayEditorDistinguishesNullEmptyAndSingleton(string kind)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var editor = new ComplexValueEditor();
            Variant initial = kind switch
            {
                "Null" => Variant.Null,
                "Empty" => Variant.From(ArrayOf<int>.Empty),
                _ => Variant.From((ArrayOf<int>)[37])
            };
            try
            {
                await editor.InitializeValueAsync(DataTypeIds.Int32, null, context.Service,
                    initial, ValueRanks.OneDimension, [8u]).ConfigureAwait(true);
                Variant committed = StructuredEditorActions.Commit(editor);
                Assert.That(committed.TryGetValue(out ArrayOf<int> values), Is.True);
                Assert.That(values.IsNull, Is.EqualTo(kind == "Null"));
                Assert.That(values.Count, Is.EqualTo(kind == "Singleton" ? 1 : 0));
                if (kind == "Singleton")
                {
                    Assert.That(values[0], Is.EqualTo(37));
                }
                Assert.That(StructuredEditorActions.Body(editor).Children.OfType<TextBox>(), Is.Empty);
                Assert.That(committed.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            }
            finally
            {
                await editor.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task ArrayResizeRequiresExplicitConsentAndAppliedValidDimensions()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            Variant original = Variant.From(((ArrayOf<int>)[1, 2, 3, 4, 5, 6]).ToMatrix([2, 3]));
            var editor = new ComplexValueEditor();
            try
            {
                await editor.InitializeArrayAsync(DataTypeIds.Int32, null, context.Service,
                    original, 2, [4u, 4u]).ConfigureAwait(true);
                StructuredEditorActions.ApplyShape(editor, "3, 2", false);
                StructuredEditorActions.AssertMatrix(StructuredEditorActions.Commit(editor),
                    [3, 2], [1, 2, 3, 4, 5, 6]);
                TextBox dimensions = StructuredEditorActions.Body(editor).Children.OfType<TextBox>().Single();
                dimensions.Text = "2, 2";
                Assert.That(editor.TryCommit(out Variant pending, out string? error), Is.False);
                Assert.That(pending.IsNull, Is.True);
                Assert.That(error, Does.Contain("pending matrix dimensions"));
                StructuredEditorActions.ApplyShape(editor, "2, 2", false);
                Assert.That(editor.TryCommit(out _, out error), Is.False);
                Assert.That(error, Does.Contain("Explicitly allow resizing"));
                StructuredEditorActions.ApplyShape(editor, "2, 2", true);
                StructuredEditorActions.AssertMatrix(StructuredEditorActions.Commit(editor), [2, 2], [1, 2, 3, 4]);
                StructuredEditorActions.ApplyShape(editor, "2, 3", true);
                StructuredEditorActions.AssertMatrix(StructuredEditorActions.Commit(editor),
                    [2, 3], [1, 2, 3, 4, 0, 0]);
                StructuredEditorActions.ApplyShape(editor, "5, 1", true);
                Assert.That(editor.TryCommit(out _, out error), Is.False);
                Assert.That(error, Does.Contain("declared maximum"));
                StructuredEditorActions.ApplyShape(editor, "-1, 2", true);
                Assert.That(editor.TryCommit(out _, out error), Is.False);
                Assert.That(error, Does.Contain("non-negative Int32"));
                StructuredEditorActions.AssertMatrix(original, [2, 3], [1, 2, 3, 4, 5, 6]);
            }
            finally
            {
                await editor.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase(-4)]
    [TestCase(-1)]
    [TestCase(33)]
    public Task ArrayEditorRejectsUnsupportedRank(int rank)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var editor = new ComplexValueEditor();
            try
            {
                await Assert.ThatAsync(() => editor.InitializeArrayAsync(
                    DataTypeIds.Int32, null, context.Service, Variant.Null, rank, default),
                    Throws.TypeOf<ServiceResultException>()).ConfigureAwait(true);
                Assert.That(editor.TryCommit(out Variant result, out string? error), Is.False);
                Assert.That(result.IsNull, Is.True);
                Assert.That(error, Does.Contain("ranks 1 through 32"));
            }
            finally
            {
                await editor.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task NewBindingAndInvalidationRejectStaleLoadCompletion()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            EnumDefinition definition = Enumeration();
            StructuredValueDraft oldDraft = await context.Service.OpenAsync(
                DataTypeIds.Int32, definition, Variant.From(0)).ConfigureAwait(true);
            StructuredValueDraft currentDraft = await context.Service.OpenAsync(
                DataTypeIds.Int32, definition, Variant.From(7)).ConfigureAwait(true);
            var delayed = new TaskCompletionSource<StructuredValueDraft>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new Mock<IStructuredValueService>(MockBehavior.Strict);
            service.Setup(s => s.OpenAsync(DataTypeIds.Int32, definition, Variant.From(0),
                It.IsAny<CancellationToken>())).Returns(delayed.Task);
            service.Setup(s => s.OpenAsync(DataTypeIds.Int32, definition, Variant.From(7),
                It.IsAny<CancellationToken>())).ReturnsAsync(currentDraft);
            var editor = new ComplexValueEditor();
            Task oldLoad = editor.InitializeAsync(DataTypeIds.Int32, definition, service.Object, Variant.From(0));
            try
            {
                Assert.That(editor.TryCommit(out _, out string? loading), Is.False);
                Assert.That(loading, Does.Contain("Loading"));
                TextBlock hint = DesktopInteraction.Control<TextBlock>(editor, "HintLabel");
                await DesktopInteraction.ChangedAsync(hint,
                    () => hint.Text == "Select a named enumeration value.", () =>
                    {
                        editor.Value = Variant.From(7);
                        return Task.CompletedTask;
                    }).ConfigureAwait(true);
                delayed.SetResult(oldDraft);
                await Assert.ThatAsync(() => oldLoad, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(true);
                Assert.That(StructuredEditorActions.Commit(editor).TryGetValue(out int result), Is.True);
                Assert.That(result, Is.EqualTo(7));
                Assert.That(DesktopInteraction.Control<TextBlock>(editor, "HintLabel").Text,
                    Is.EqualTo("Select a named enumeration value."));
                context.Service.Refresh();
                Assert.That(editor.TryCommit(out Variant stale, out string? error), Is.False);
                Assert.That(stale.IsNull, Is.True);
                Assert.That(error, Does.Contain("metadata changed"));
                service.VerifyAll();
            }
            finally
            {
                delayed.TrySetResult(oldDraft);
                await editor.StopAsync().ConfigureAwait(true);
                try
                {
                    await oldLoad.ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                }
            }
        });
    }

    [Test]
    public Task StopAndDetachDrainPendingWorkWithoutStaleCommit()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            EnumDefinition definition = Enumeration();
            StructuredValueDraft draft = await context.Service.OpenAsync(
                DataTypeIds.Int32, definition, Variant.From(7)).ConfigureAwait(true);
            var delayed = new TaskCompletionSource<StructuredValueDraft>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken loadToken = default;
            var service = new Mock<IStructuredValueService>(MockBehavior.Strict);
            service.Setup(s => s.OpenAsync(DataTypeIds.Int32, definition, Variant.From(7),
                It.IsAny<CancellationToken>()))
                .Callback((NodeId _, DataTypeDefinition _, Variant _, CancellationToken token) => loadToken = token)
                .Returns(delayed.Task);
            var editor = new ComplexValueEditor();
            DesktopInteraction.Owner.Content = editor;
            Task loading = editor.InitializeAsync(DataTypeIds.Int32, definition, service.Object, Variant.From(7));
            try
            {
                DesktopInteraction.Owner.Content = null;
                Task stopped = editor.StopAsync();
                Assert.That(loadToken.IsCancellationRequested, Is.True);
                Assert.That(stopped.IsCompleted, Is.False);
                delayed.SetResult(draft);
                await Assert.ThatAsync(() => loading, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(true);
                await stopped.ConfigureAwait(true);
                Assert.That(editor.TryCommit(out Variant result, out string? error), Is.False);
                Assert.That(result.IsNull, Is.True);
                Assert.That(error, Is.EqualTo("Loading was canceled."));
                Assert.That(StructuredEditorActions.Body(editor).Children, Is.Empty);
            }
            finally
            {
                delayed.TrySetResult(draft);
                DesktopInteraction.Owner.Content = null;
                await editor.StopAsync().ConfigureAwait(true);
                try
                {
                    await loading.ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                }
            }
        });
    }

    [Test]
    public Task ModelInspectorLoadCommitClearUsesIndependentTypedDraft()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var view = new ModelInspectorView();
            var inspection = new ModelInspection(0, context.SessionId,
                context.MessageContext.NamespaceUris.ToArray(), "i=1234", new NodeId(1234u), "Cells",
                NodeClass.Variable, DataTypeIds.Int32, new QualifiedName("Int32"), 2,
                new DataValue(Variant.Null), true, false, null) { ArrayDimensions = [2u, 3u] };
            DesktopInteraction.Owner.Content = view;
            try
            {
                await view.LoadAsync(inspection, context.Service, CancellationToken.None).ConfigureAwait(true);
                ComplexValueEditor editor = DesktopInteraction.Control<ComplexValueEditor>(view, "ValueEditor");
                Assert.That(editor.IsVisible, Is.True);
                StructuredEditorActions.ApplyShape(editor, "2, 3", true);
                Assert.That(view.TryCommit(out Variant candidate, out string? error), Is.True, error);
                StructuredEditorActions.AssertMatrix(candidate, [2, 3], [0, 0, 0, 0, 0, 0]);
                Assert.That(inspection.Value.WrappedValue.IsNull, Is.True);
                context.Service.Refresh();
                Assert.That(view.TryCommit(out _, out error), Is.False);
                Assert.That(error, Does.Contain("metadata changed"));
                await view.StopAsync().ConfigureAwait(true);
                view.Clear();
                Assert.That(editor.IsVisible, Is.False);
                Assert.That(view.TryCommit(out Variant cleared, out error), Is.False);
                Assert.That(cleared.IsNull, Is.True);
                Assert.That(error, Is.EqualTo("Read a Variable before committing."));
                Assert.That(context.Reads, Is.Zero);
            }
            finally
            {
                await view.StopAsync().ConfigureAwait(true);
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public Task NestedAcceptChangesDraftAndCancelPreservesInput(bool array, bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var childType = context.Register("NestedChild", StructureType.Structure,
                [StructuredValueTestContext.Field("Counter", DataTypeIds.Int32)]);
            IEncodeable childSource = childType.Type.CreateInstance();
            ((IStructure)childSource)["Counter"] = Variant.From(7);
            var field = array
                ? new StructureField
                {
                    Name = "Payload", DataType = DataTypeIds.Int32, ValueRank = 2, ArrayDimensions = [2u, 3u]
                }
                : StructuredValueTestContext.Field("Payload", childType.DataTypeId);
            var parentType = context.Register("NestedParent", StructureType.Structure,
                [field, StructuredValueTestContext.Field("Neighbor", DataTypeIds.Int32)]);
            IEncodeable original = parentType.Type.CreateInstance();
            Variant originalPayload = array
                ? Variant.From(((ArrayOf<int>)[1, 2, 3, 4, 5, 6]).ToMatrix([2, 3]))
                : Variant.FromStructure(childSource);
            ((IStructure)original)["Payload"] = originalPayload;
            ((IStructure)original)["Neighbor"] = Variant.From(61);
            context.Reader = (ids, _) => ValueTask.FromResult(ids[0].NodeId == childType.DataTypeId
                ? StructuredValueTestContext.Reply(Variant.FromStructure(childType.Definition))
                : StructuredValueTestContext.Reply(Variant.Null, StatusCodes.BadAttributeIdInvalid));
            var editor = new ComplexValueEditor();
            DesktopInteraction.Owner.Content = editor;
            try
            {
                await editor.InitializeAsync(parentType.DataTypeId, parentType.Definition, context.Service,
                    Variant.FromStructure(original)).ConfigureAwait(true);
                Button edit = StructuredEditorActions.Body(editor).Children.OfType<Grid>().First()
                    .Children.OfType<Button>().Single();
                Window nested = await DesktopInteraction.OpenedAsync<Window>(() => DesktopInteraction.Click(edit))
                    .ConfigureAwait(true);
                ComplexValueEditor inner = nested.GetLogicalDescendants().OfType<ComplexValueEditor>().Single();
                Button ok = nested is ComplexValueElementDialog
                    ? DesktopInteraction.Control<Button>(nested, "OkButton")
                    : nested.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "OK"));
                await DesktopInteraction.ChangedAsync(ok, () => ok.IsEnabled, () => Task.CompletedTask)
                    .ConfigureAwait(true);
                Assert.That(editor.TryCommit(out Variant pending, out string? error), Is.False);
                Assert.That(pending.IsNull, Is.True);
                Assert.That(error, Is.EqualTo("Finish or cancel the nested edit before committing."));
                if (array)
                {
                    StructuredEditorActions.ApplyShape(inner, "1, 3", true);
                }
                else
                {
                    StructuredEditorActions.Body(inner).Children.OfType<Grid>().Single()
                        .Children.OfType<TextBox>().Single().Text = "83";
                }
                Button finish = accept ? ok : nested.GetLogicalDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, "Cancel"));
                DesktopInteraction.Click(finish);
                // The close queues the parent's normal-priority dialog continuation.
                // This one lower-priority operation follows that continuation, without polling.
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                Variant committed = StructuredEditorActions.Commit(editor);
                Assert.That(committed.TryGetValue<Opc.Ua.Encoders.Structure>(
                    out Opc.Ua.Encoders.Structure? result, context.MessageContext), Is.True);
                Assert.That(result!["Neighbor"], Is.EqualTo(Variant.From(61)));
                if (array)
                {
                    StructuredEditorActions.AssertMatrix(result["Payload"], accept ? [1, 3] : [2, 3],
                        accept ? [1, 2, 3] : [1, 2, 3, 4, 5, 6]);
                    StructuredEditorActions.AssertMatrix(((IStructure)original)["Payload"],
                        [2, 3], [1, 2, 3, 4, 5, 6]);
                }
                else
                {
                    Assert.That(result["Payload"].TryGetValue<Opc.Ua.Encoders.Structure>(
                        out Opc.Ua.Encoders.Structure? changedChild, context.MessageContext), Is.True);
                    Assert.That(changedChild!["Counter"], Is.EqualTo(Variant.From(accept ? 83 : 7)));
                    Assert.That(((IStructure)childSource)["Counter"], Is.EqualTo(Variant.From(7)));
                }
                Assert.That(((IStructure)original)["Neighbor"], Is.EqualTo(Variant.From(61)));
            }
            finally
            {
                foreach (Window child in DesktopInteraction.Owner.OwnedWindows.ToArray())
                {
                    child.Close();
                }
                await editor.StopAsync().ConfigureAwait(true);
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [Test]
    public Task ArrayNullAndEmptyCommandsRequireDiscardConsentAndKeepTheirWireDistinction()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            Variant original = Variant.From((ArrayOf<int>)[11, 22, 33]);
            var editor = new ComplexValueEditor();
            try
            {
                await editor.InitializeArrayAsync(DataTypeIds.Int32, null, context.Service,
                    original, ValueRanks.OneDimension, [8u]).ConfigureAwait(true);
                StackPanel body = StructuredEditorActions.Body(editor);
                StackPanel actions = body.Children.OfType<StackPanel>().Single();
                Button setNull = actions.Children.OfType<Button>().Single(button => Equals(button.Content, "Set null"));
                Button setEmpty = actions.Children.OfType<Button>()
                    .Single(button => Equals(button.Content, "Set empty"));
                DesktopInteraction.Click(setNull);
                Assert.That(editor.TryCommit(out Variant rejected, out string? error), Is.False);
                Assert.That(rejected.IsNull, Is.True);
                Assert.That(error, Does.Contain("Explicitly allow discarding"));
                body.Children.OfType<CheckBox>().Single().IsChecked = true;
                DesktopInteraction.Click(setNull);
                Assert.That(StructuredEditorActions.Commit(editor).TryGetValue(out ArrayOf<int> nullArray), Is.True);
                Assert.That(nullArray.IsNull, Is.True);
                body.Children.OfType<CheckBox>().Single().IsChecked = false;
                DesktopInteraction.Click(setEmpty);
                Assert.That(StructuredEditorActions.Commit(editor).TryGetValue(out ArrayOf<int> emptyArray), Is.True);
                Assert.That(emptyArray.IsNull, Is.False);
                Assert.That(emptyArray.Count, Is.Zero);
                Assert.That(body.Children.OfType<TextBlock>().First().Text, Does.Contain("0 elements"));
                Assert.That(original, Is.EqualTo(Variant.From((ArrayOf<int>)[11, 22, 33])));
            }
            finally
            {
                await editor.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task ModelInspectorPrimitiveEditAcceptsOrCancelsAnIndependentCandidate(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var view = new ModelInspectorView();
            var inspection = new ModelInspection(0, context.SessionId,
                context.MessageContext.NamespaceUris.ToArray(), "i=1234", new NodeId(1234u), "Scalar",
                NodeClass.Variable, DataTypeIds.Int32, new QualifiedName("Int32"), ValueRanks.Scalar,
                new DataValue(Variant.From(31)), true, false, null);
            DesktopInteraction.Owner.Content = view;
            try
            {
                await view.LoadAsync(inspection, context.Service, CancellationToken.None).ConfigureAwait(true);
                Task editing = Task.CompletedTask;
                PrimitiveValuePromptDialog dialog = await DesktopInteraction.OpenedAsync<PrimitiveValuePromptDialog>(
                    () => editing = view.EditValueAsync(CancellationToken.None)).ConfigureAwait(true);
                DesktopInteraction.Control<TextBox>(dialog, "ValueText").Text = "97";
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(
                    dialog, accept ? "OkButton" : "CancelButton"));
                if (accept)
                {
                    await editing.ConfigureAwait(true);
                }
                else
                {
                    await Assert.ThatAsync(() => editing, Throws.TypeOf<OperationCanceledException>())
                        .ConfigureAwait(true);
                }
                Assert.That(view.TryCommit(out Variant result, out string? error), Is.True, error);
                Assert.That(result, Is.EqualTo(Variant.From(accept ? 97 : 31)));
                Assert.That(inspection.Value.WrappedValue, Is.EqualTo(Variant.From(31)));
                Assert.That(DesktopInteraction.Control<ComplexValueEditor>(view, "ValueEditor").IsVisible, Is.False);
                view.Clear();
                await Assert.ThatAsync(() => view.EditValueAsync(CancellationToken.None),
                    Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Read a Variable before editing."))
                    .ConfigureAwait(true);
                Assert.That(view.TryCommit(out _, out _), Is.False);
            }
            finally
            {
                await view.StopAsync().ConfigureAwait(true);
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    private static EnumDefinition Enumeration()
    {
        return new EnumDefinition
        {
            Fields = [new EnumField { Name = "Idle", Value = 0 }, new EnumField { Name = "Running", Value = 7 }]
        };
    }

    private static Variant Unsigned(BuiltInType type, ulong value)
    {
        return type switch
        {
            BuiltInType.Byte => Variant.From((byte)value),
            BuiltInType.UInt16 => Variant.From((ushort)value),
            BuiltInType.UInt32 => Variant.From((uint)value),
            BuiltInType.UInt64 => Variant.From(value),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }
}
