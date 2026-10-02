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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Models;
using UaLens.Tests.Observe;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.StructuredValues;

[TestFixture]
[Explicit("Requires an interactive desktop. All service calls are mocked; only test-owned windows are opened.")]
[Category("EditorDialogProbe")]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class StructuredEditorDialogTests
{
    [OneTimeSetUp]
    public void InitializeDesktop()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
            Application.Current!.Styles.Add(new FluentTheme());
        }
    }

    [Test]
    public async Task ModelsUsesNamedBitsAndDimensionAwareCreationWithoutAnyServerMutationAsync()
    {
        using var context = new StructuredValueTestContext();
        var view = new ModelInspectorView();
        var owner = CreateOwner();
        owner.Content = view;
        owner.Show();
        try
        {
            var flags = new EnumDefinition
            {
                IsOptionSet = true,
                Fields = [new EnumField { Name = "Enabled", Value = 0 }]
            };
            var inspection = new ModelInspection(
                0, context.SessionId, context.MessageContext.NamespaceUris.ToArray(), "i=1234",
                new NodeId(1234u), "Flags", NodeClass.Variable, DataTypeIds.UInt32,
                new QualifiedName("UInt32"), ValueRanks.Scalar, new DataValue(Variant.From(0x80000000u)),
                true, false, flags);
            Task load = view.LoadAsync(inspection, context.Service, CancellationToken.None);
            PumpUntil(() => load.IsCompleted);
            await load.ConfigureAwait(true);
            ComplexValueEditor editor = view.FindControl<ComplexValueEditor>("ValueEditor")!;
            Assert.That(editor.IsVisible, Is.True);
            CheckBox bit = editor.FindControl<StackPanel>("BodyPanel")!.Children
                .OfType<StackPanel>().Single().Children.OfType<CheckBox>().Single();
            bit.IsChecked = true;
            Assert.That(view.TryCommit(out Variant changed, out string? error), Is.True, error);
            Assert.That(changed.TryGetValue(out uint actual), Is.True);
            Assert.That(actual, Is.EqualTo(0x80000001u));
            Assert.That(inspection.Value.WrappedValue.TryGetValue(out uint original), Is.True);
            Assert.That(original, Is.EqualTo(0x80000000u));

            inspection = inspection with
            {
                DataType = DataTypeIds.Int32,
                ValueRank = 2,
                Value = new DataValue(Variant.Null),
                Definition = null,
                ArrayDimensions = [2u, 3u]
            };
            load = view.LoadAsync(inspection, context.Service, CancellationToken.None);
            PumpUntil(() => load.IsCompleted);
            await load.ConfigureAwait(true);
            ApplyShape(editor, "2, 3");
            Assert.That(view.TryCommit(out Variant created, out error), Is.True, error);
            AssertMatrix(created, [2, 3], [0, 0, 0, 0, 0, 0]);
            ApplyShape(editor, "3, 2");
            Assert.That(view.TryCommit(out Variant invalid, out error), Is.False);
            Assert.That(invalid.IsNull, Is.True);
            Assert.That(error, Does.Contain("declared maximum"));
            Assert.That(inspection.Value.WrappedValue.IsNull, Is.True);
            Assert.That(context.Reads, Is.Zero);
            context.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            Task stop = view.StopAsync();
            PumpUntil(() => stop.IsCompleted);
            await stop.ConfigureAwait(true);
            owner.Close();
        }
    }

    [Test]
    public async Task WriteDialogReshapesTheTypedMatrixAndWritesOnlyTheExplicitCommitAsync()
    {
        using var context = new StructuredValueTestContext();
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            Variant original = Variant.From(((ArrayOf<int>)[1, 2, 3, 4, 5, 6]).ToMatrix([2, 3]));
            context.Reader = (ids, _) => ValueTask.FromResult(ids[0].AttributeId switch
            {
                Attributes.Value => Reply(original, Variant.From(DataTypeIds.Int32), Variant.From(2),
                    Variant.From((ArrayOf<uint>)[2u, 3u])),
                Attributes.DataTypeDefinition => StructuredValueTestContext.Reply(
                    Variant.Null, StatusCodes.BadAttributeIdInvalid),
                _ => throw new InvalidOperationException("Unexpected editor read.")
            });
            ArrayOf<WriteValue> sent = default;
            context.Session.Setup(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Callback((RequestHeader? _, ArrayOf<WriteValue> values, CancellationToken _) => sent = values)
                .ReturnsAsync(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(), Results = [StatusCodes.Good]
                });
            var node = new NodeViewModel(
                host.Host.Browser, NodeId.Null, new NodeId(1234u), "Matrix", NodeClass.Variable);
            var owner = CreateOwner();
            owner.Show();
            var dialog = new WriteValueDialog(node, context.Session.Object, context.Service);
            await using (dialog.ConfigureAwait(false))
            {
                try
                {
                    Task shown = dialog.ShowDialog(owner);
                    ComplexValueEditor editor = dialog.FindControl<ComplexValueEditor>("ComplexEditor")!;
                    TextBlock currentLabel = dialog.FindControl<TextBlock>("CurrentLabel")!;
                    PumpUntil(() => editor.IsVisible ||
                        currentLabel.Text!.Contains("failed", StringComparison.Ordinal));
                    Assert.That(editor.IsVisible, Is.True, dialog.FindControl<TextBlock>("CurrentLabel")!.Text);
                    Assert.That(sent.IsNull, Is.True);
                    ApplyShape(editor, "1, 3");
                    Assert.That(sent.IsNull, Is.True);
                    dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => shown.IsCompleted);
                    await shown.ConfigureAwait(true);

                    Assert.That(sent.Count, Is.EqualTo(1));
                    Assert.That(sent[0].NodeId, Is.EqualTo(node.NodeId));
                    AssertMatrix(sent[0].Value.WrappedValue, [1, 3], [1, 2, 3]);
                    AssertMatrix(original, [2, 3], [1, 2, 3, 4, 5, 6]);
                    context.Session.Verify(value => value.WriteAsync(
                        It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                        Times.Once);
                }
                finally
                {
                    dialog.Close();
                    Task stop = dialog.StopAsync();
                    PumpUntil(() => stop.IsCompleted);
                    await stop.ConfigureAwait(true);
                    owner.Close();
                }
            }
        }
    }

    [Test]
    public async Task MethodDialogCreatesAMatrixArgumentAndCallsOnlyAfterExplicitAcceptanceAsync()
    {
        using var context = new StructuredValueTestContext();
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var argument = new Argument
            {
                Name = "Cells", DataType = DataTypeIds.Int32, ValueRank = 2, ArrayDimensions = [2u, 3u]
            };
            context.Session.Setup(value => value.BrowseAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            References =
                            [
                                new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId(300u),
                                    BrowseName = new QualifiedName(BrowseNames.InputArguments)
                                }
                            ]
                        }
                    ]
                });
            context.Reader = (ids, _) => ValueTask.FromResult(ids[0].AttributeId switch
            {
                Attributes.Value => Reply(Variant.FromStructure((ArrayOf<Argument>)[argument])),
                Attributes.DataTypeDefinition => StructuredValueTestContext.Reply(
                    Variant.Null, StatusCodes.BadAttributeIdInvalid),
                _ => throw new InvalidOperationException("Unexpected argument metadata read.")
            });
            ArrayOf<CallMethodRequest> sent = default;
            context.Session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Callback((RequestHeader? _, ArrayOf<CallMethodRequest> calls, CancellationToken _) => sent = calls)
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                });
            var node = new NodeViewModel(
                host.Host.Browser, new NodeId(200u), new NodeId(1234u), "Method", NodeClass.Method);
            var owner = CreateOwner();
            owner.Show();
            var dialog = new MethodCallDialog(node, context.Session.Object, context.Service);
            await using (dialog.ConfigureAwait(false))
            {
                try
                {
                    Task shown = dialog.ShowDialog(owner);
                    PumpUntil(() => dialog.Inputs.Count == 1 && dialog.Inputs[0].IsComplex);
                    MethodArgRow row = dialog.Inputs[0];
                    Assert.That(sent.IsNull, Is.True);
                    var edit = (IAsyncRelayCommand)row.EditComplexCommand!;
                    Task editing = edit.ExecuteAsync(null);
                    PumpUntil(() => dialog.OwnedWindows.Count == 1);
                    Window nested = dialog.OwnedWindows[0];
                    ComplexValueEditor editor = nested.FindControl<ComplexValueEditor>("Editor")!;
                    PumpUntil(() => nested.FindControl<Button>("OkButton")!.IsEnabled);
                    ApplyShape(editor, "2, 3");
                    nested.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => editing.IsCompleted);
                    await editing.ConfigureAwait(true);
                    AssertMatrix(row.CachedVariant, [2, 3], [0, 0, 0, 0, 0, 0]);
                    Assert.That(sent.IsNull, Is.True);

                    dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => !sent.IsNull);
                    Assert.That(sent.Count, Is.EqualTo(1));
                    Assert.That(sent[0].MethodId, Is.EqualTo(node.NodeId));
                    Assert.That(sent[0].ObjectId, Is.EqualTo(new NodeId(200u)));
                    Assert.That(sent[0].InputArguments.Count, Is.EqualTo(1));
                    AssertMatrix(sent[0].InputArguments[0], [2, 3], [0, 0, 0, 0, 0, 0]);
                    dialog.Close();
                    PumpUntil(() => shown.IsCompleted);
                    await shown.ConfigureAwait(true);
                    context.Session.Verify(value => value.CallAsync(
                        It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                        It.IsAny<CancellationToken>()), Times.Once);
                }
                finally
                {
                    dialog.Close();
                    Task stop = dialog.StopAsync();
                    PumpUntil(() => stop.IsCompleted);
                    await stop.ConfigureAwait(true);
                    owner.Close();
                }
            }
        }
    }

    private static void ApplyShape(ComplexValueEditor editor, string dimensions)
    {
        StackPanel body = editor.FindControl<StackPanel>("BodyPanel")!;
        body.Children.OfType<CheckBox>().Single().IsChecked = true;
        body.Children.OfType<TextBox>().Single().Text = dimensions;
        body.Children.OfType<Button>().Single(button => button.Content is "Apply shape / create matrix")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void AssertMatrix(Variant value, ArrayOf<int> dimensions, ArrayOf<int> expected)
    {
        Assert.That(value.TryGetValue(out MatrixOf<int> matrix), Is.True);
        Assert.That(matrix.Dimensions, Is.EqualTo(dimensions.ToArray()));
        Assert.That(matrix.ToArrayOf(), Is.EqualTo(expected));
    }

    private static ReadResponse Reply(params Variant[] values)
    {
        return new ReadResponse
        {
            ResponseHeader = new ResponseHeader(),
            Results = values.Select(value => new DataValue(value)).ToArray()
        };
    }

    private static Window CreateOwner()
    {
        return new Window
        {
            Title = "UaLens - Editor regression",
            Width = 800,
            Height = 620,
            ShowInTaskbar = false
        };
    }

    private static void PumpUntil(Func<bool> complete)
    {
        if (complete())
        {
            return;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
            (_, _) =>
            {
                if (complete())
                {
                    timeout.Cancel();
                }
            });
        timer.Start();
        try
        {
            Dispatcher.UIThread.MainLoop(timeout.Token);
        }
        finally
        {
            timer.Stop();
        }
        Assert.That(complete(), Is.True, "The owned editor window did not complete its expected operation.");
    }
}
