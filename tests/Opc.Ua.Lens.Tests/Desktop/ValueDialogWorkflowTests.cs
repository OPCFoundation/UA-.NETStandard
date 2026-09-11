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
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Tests.StructuredValues;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ValueDialogWorkflowTests
{
    [TestCase("Primitive")]
    [TestCase("Structure")]
    [TestCase("Matrix")]
    public Task WriteAcceptCommitsTypedDraftBeforeSendingRequest(string kind)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            await using var connection = new DesktopConnectionContext();
            NodeId dataType = DataTypeIds.Int32;
            DataTypeDefinition? definition = null;
            IEncodeable? structure = null;
            Variant initial = Variant.From(-3);
            int rank = ValueRanks.Scalar;
            ArrayOf<uint> dimensions = [];
            if (kind == "Matrix")
            {
                initial = Variant.From(((ArrayOf<int>)[1, 2, 3, 4, 5, 6]).ToMatrix([2, 3]));
                rank = 2;
                dimensions = [2u, 3u];
            }
            if (kind == "Structure")
            {
                var type = context.Register("WriteDesktop", StructureType.Structure,
                [
                    StructuredValueTestContext.Field("Number", DataTypeIds.Int32),
                    StructuredValueTestContext.Field("Caption", DataTypeIds.String)
                ]);
                dataType = type.DataTypeId;
                definition = type.Definition;
                structure = type.Type.CreateInstance();
                ((IStructure)structure)["Number"] = Variant.From(11);
                ((IStructure)structure)["Caption"] = Variant.From("original");
                initial = Variant.FromStructure(structure);
            }
            context.Reader = (ids, _) => ValueTask.FromResult(ids[0].AttributeId == Attributes.DataTypeDefinition
                ? definition is null
                    ? StructuredValueTestContext.Reply(Variant.Null, StatusCodes.BadAttributeIdInvalid)
                    : Reply(Variant.FromStructure(definition))
                : Reply(initial, Variant.From(dataType), Variant.From(rank), Variant.From(dimensions)));
            ArrayOf<WriteValue> sent = default;
            context.Session.Setup(s => s.WriteAsync(It.IsAny<RequestHeader?>(),
                It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Callback((RequestHeader? _, ArrayOf<WriteValue> values, CancellationToken _) => sent = values)
                .ReturnsAsync(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(), Results = [StatusCodes.Good]
                });
            NodeViewModel node = Node(connection, NodeClass.Variable);
            var dialog = new WriteValueDialog(node, context.Session.Object, context.Service);
            Task shown = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                ComplexValueEditor editor = DesktopInteraction.Control<ComplexValueEditor>(dialog, "ComplexEditor");
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "DataTypeLabel").Text,
                    Is.EqualTo($"{dataType}    rank={rank}"));
                Assert.That(sent.IsNull, Is.True);
                if (kind == "Primitive")
                {
                    Assert.That(editor.IsVisible, Is.False);
                    DesktopInteraction.Control<TextBox>(dialog, "ValueText").Text = "42";
                }
                else if (kind == "Matrix")
                {
                    Assert.That(editor.IsVisible, Is.True);
                    StructuredEditorActions.ApplyShape(editor, "1, 3", true);
                }
                else
                {
                    Assert.That(editor.IsVisible, Is.True);
                    Grid[] rows = StructuredEditorActions.Body(editor).Children.OfType<Grid>().ToArray();
                    rows[0].Children.OfType<TextBox>().Single().Text = "42";
                    rows[1].Children.OfType<TextBox>().Single().Text = "accepted";
                }
                DateTime source = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
                DateTime server = source.AddSeconds(2);
                DesktopInteraction.Control<CheckBox>(dialog, "StatusOverride").IsChecked = true;
                DesktopInteraction.Control<TextBox>(dialog, "StatusText").Text = "BadOutOfService";
                DesktopInteraction.Control<CheckBox>(dialog, "SourceOverride").IsChecked = true;
                DesktopInteraction.Control<UtcDateTimePicker>(dialog, "SourceTimePicker").Value = source;
                DesktopInteraction.Control<CheckBox>(dialog, "ServerOverride").IsChecked = true;
                DesktopInteraction.Control<UtcDateTimePicker>(dialog, "ServerTimePicker").Value = server;
                Assert.That(sent.IsNull, Is.True);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                await shown.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                await dialog.StopAsync().ConfigureAwait(true);

                Assert.That(sent.Count, Is.EqualTo(1));
                Assert.That(sent[0].NodeId, Is.EqualTo(new NodeId(1234u, 2)));
                Assert.That(sent[0].AttributeId, Is.EqualTo(Attributes.Value));
                Assert.That(sent[0].Value.StatusCode, Is.EqualTo(StatusCodes.BadOutOfService));
                Assert.That(sent[0].Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)source));
                Assert.That(sent[0].Value.ServerTimestamp, Is.EqualTo((DateTimeUtc)server));
                if (kind == "Primitive")
                {
                    Assert.That(sent[0].Value.WrappedValue, Is.EqualTo(Variant.From(42)));
                    Assert.That(initial, Is.EqualTo(Variant.From(-3)));
                }
                else if (kind == "Matrix")
                {
                    StructuredEditorActions.AssertMatrix(sent[0].Value.WrappedValue, [1, 3], [1, 2, 3]);
                    StructuredEditorActions.AssertMatrix(initial, [2, 3], [1, 2, 3, 4, 5, 6]);
                }
                else
                {
                    Assert.That(sent[0].Value.WrappedValue.TryGetValue<Opc.Ua.Encoders.Structure>(
                        out Opc.Ua.Encoders.Structure? result, context.MessageContext), Is.True);
                    Assert.That(result!["Number"], Is.EqualTo(Variant.From(42)));
                    Assert.That(result["Caption"], Is.EqualTo(Variant.From("accepted")));
                    Assert.That(((IStructure)structure!)["Number"], Is.EqualTo(Variant.From(11)));
                    Assert.That(((IStructure)structure)["Caption"], Is.EqualTo(Variant.From("original")));
                }
                context.Session.Verify(s => s.WriteAsync(It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                dialog.Close();
                await dialog.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task RepeatedAcceptCannotSubmitAnotherPendingWrite()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            await using var connection = new DesktopConnectionContext();
            ConfigureScalarRead(context);
            var reply = new TaskCompletionSource<WriteResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Session.Setup(s => s.WriteAsync(It.IsAny<RequestHeader?>(),
                It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<WriteResponse>(reply.Task));
            var dialog = new WriteValueDialog(
                Node(connection, NodeClass.Variable), context.Session.Object, context.Service);
            Task shown = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "ValueText").Text = "81";
                Button ok = DesktopInteraction.Control<Button>(dialog, "OkButton");
                DesktopInteraction.Click(ok);
                DesktopInteraction.Click(ok);
                context.Session.Verify(s => s.WriteAsync(It.IsAny<RequestHeader?>(),
                    It.Is<ArrayOf<WriteValue>>(values =>
                        values[0].Value.WrappedValue == Variant.From(81) &&
                        values[0].Value.StatusCode == StatusCodes.Good &&
                        values[0].Value.SourceTimestamp.IsNull &&
                        values[0].Value.ServerTimestamp.IsNull),
                    It.IsAny<CancellationToken>()), Times.Once);
                Assert.That(shown.IsCompleted, Is.False);
                reply.SetResult(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(), Results = [StatusCodes.Good]
                });
                await shown.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultLabel").Text,
                    Does.StartWith("Write OK:"));
            }
            finally
            {
                reply.TrySetResult(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(), Results = [StatusCodes.BadNotWritable]
                });
                dialog.Close();
                await dialog.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase("Short", "all requested attributes")]
    [TestCase("BadAttribute", "BadUserAccessDenied")]
    [TestCase("Malformed", "Invalid DataType")]
    public Task MetadataFailureDoesNotWriteOrReportSuccess(string failure, string message)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            await using var connection = new DesktopConnectionContext();
            context.Reader = (_, _) =>
            {
                ReadResponse response = failure == "Short"
                    ? Reply(Variant.From(4))
                    : Reply(Variant.From(4),
                        failure == "Malformed" ? Variant.From("Int32") : Variant.From(DataTypeIds.Int32),
                        Variant.From(ValueRanks.Scalar), Variant.From((ArrayOf<uint>)[]));
                if (failure == "BadAttribute")
                {
                    response.Results = [new DataValue(Variant.From(4), StatusCodes.BadUserAccessDenied),
                        response.Results[1], response.Results[2], response.Results[3]];
                }
                return ValueTask.FromResult(response);
            };
            var dialog = new WriteValueDialog(
                Node(connection, NodeClass.Variable), context.Session.Object, context.Service);
            Task shown = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "CurrentLabel").Text, Does.Contain(message));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultLabel").Text,
                    Does.StartWith("Cannot write"));
                Assert.That(shown.IsCompleted, Is.False);
                context.Session.Verify(s => s.WriteAsync(It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                dialog.Close();
                await dialog.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase("BadStatus", "Write failed:")]
    [TestCase("Short", "BadDecodingError")]
    [TestCase("Service", "BadUserAccessDenied")]
    [TestCase("Exception", "Write exception: refused")]
    public Task ProtocolFailureStaysVisibleAndDoesNotCloseWriteDialog(string failure, string message)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            await using var connection = new DesktopConnectionContext();
            ConfigureScalarRead(context);
            context.Session.Setup(s => s.WriteAsync(It.IsAny<RequestHeader?>(),
                It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(() => failure == "Exception"
                    ? ValueTask.FromException<WriteResponse>(new InvalidOperationException("refused"))
                    : ValueTask.FromResult(new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            ServiceResult = failure == "Service" ? StatusCodes.BadUserAccessDenied : StatusCodes.Good
                        },
                        Results = failure == "Short" ? [] : [StatusCodes.BadNotWritable]
                    }));
            var dialog = new WriteValueDialog(
                Node(connection, NodeClass.Variable), context.Session.Object, context.Service);
            Task shown = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "ValueText").Text = "not an integer";
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultLabel").Text,
                    Does.StartWith("Parse error:"));
                context.Session.Verify(s => s.WriteAsync(It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Never);
                DesktopInteraction.Control<TextBox>(dialog, "ValueText").Text = "14";
                DesktopInteraction.Control<CheckBox>(dialog, "StatusOverride").IsChecked = true;
                DesktopInteraction.Control<TextBox>(dialog, "StatusText").Text = "UnknownStatus";
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultLabel").Text,
                    Does.Contain("not a recognised"));
                DesktopInteraction.Control<TextBox>(dialog, "StatusText").Text = "0x80000000";
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultLabel").Text, Does.Contain(message));
                Assert.That(shown.IsCompleted, Is.False);
                context.Session.Verify(s => s.WriteAsync(It.IsAny<RequestHeader?>(),
                    It.Is<ArrayOf<WriteValue>>(values => values[0].Value.StatusCode == StatusCodes.Bad),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                dialog.Close();
                await dialog.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task CancelAndCloseDrainLoadsBeforeReleasingStructuredService()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            await using var connection = new DesktopConnectionContext();
            var reply = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken loadToken = default;
            context.Reader = (_, token) =>
            {
                loadToken = token;
                return new ValueTask<ReadResponse>(reply.Task);
            };
            var dialog = new WriteValueDialog(
                Node(connection, NodeClass.Variable), context.Session.Object, context.Service);
            Task shown = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                await shown.ConfigureAwait(true);
                Task stop = dialog.StopAsync();
                Assert.That(loadToken.IsCancellationRequested, Is.True);
                Assert.That(stop.IsCompleted, Is.False);
                reply.SetResult(Reply(Variant.From(4), Variant.From(DataTypeIds.Int32),
                    Variant.From(ValueRanks.Scalar), Variant.From((ArrayOf<uint>)[])));
                await stop.ConfigureAwait(true);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "CurrentLabel").Text,
                    Does.StartWith("(read failed:"));
                context.Session.Verify(s => s.WriteAsync(It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Never);
                Assert.That(context.Reads, Is.EqualTo(1));
            }
            finally
            {
                reply.TrySetResult(Reply(Variant.From(4), Variant.From(DataTypeIds.Int32),
                    Variant.From(ValueRanks.Scalar), Variant.From((ArrayOf<uint>)[])));
                dialog.Close();
                await dialog.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task CallAcceptCommitsTypedArgumentsAndDisplaysOutputs()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            await using var connection = new DesktopConnectionContext();
            ConfigureArguments(context,
            [
                new Argument { Name = "Gain", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar },
                new Argument { Name = "Cells", DataType = DataTypeIds.Int32, ValueRank = 2, ArrayDimensions = [2u, 3u] }
            ]);
            ArrayOf<CallMethodRequest> sent = default;
            context.Session.Setup(s => s.CallAsync(It.IsAny<RequestHeader?>(),
                It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Callback((RequestHeader? _, ArrayOf<CallMethodRequest> calls, CancellationToken _) => sent = calls)
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        InputArgumentResults = [StatusCodes.Good, StatusCodes.Good],
                        OutputArguments = [Variant.From(19), Variant.From("accepted")]
                    }]
                });
            var dialog = new MethodCallDialog(
                Node(connection, NodeClass.Method), context.Session.Object, context.Service);
            Task shown = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                Assert.That(dialog.Inputs.Select(row => row.Argument.Name), Is.EqualTo(s_callAcceptCommitsTypedArgumentsAndDisplaysOutputsExpected));
                dialog.Inputs[0].ValueText = "41";
                var command = (IAsyncRelayCommand)dialog.Inputs[1].EditComplexCommand!;
                Task editing = Task.CompletedTask;
                ComplexValueElementDialog nested = await DesktopInteraction.OpenedAsync<ComplexValueElementDialog>(
                    () => editing = command.ExecuteAsync(null)).ConfigureAwait(true);
                Button nestedOk = DesktopInteraction.Control<Button>(nested, "OkButton");
                await DesktopInteraction.ChangedAsync(nestedOk, () => nestedOk.IsEnabled, () => Task.CompletedTask)
                    .ConfigureAwait(true);
                StructuredEditorActions.ApplyShape(
                    DesktopInteraction.Control<ComplexValueEditor>(nested, "Editor"), "2, 3", true);
                Assert.That(sent.IsNull, Is.True);
                DesktopInteraction.Click(nestedOk);
                await editing.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                StructuredEditorActions.AssertMatrix(dialog.Inputs[1].CachedVariant, [2, 3], [0, 0, 0, 0, 0, 0]);
                Assert.That(sent.IsNull, Is.True);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));

                Assert.That(sent.Count, Is.EqualTo(1));
                Assert.That(sent[0].ObjectId, Is.EqualTo(new NodeId(200u, 2)));
                Assert.That(sent[0].MethodId, Is.EqualTo(new NodeId(1234u, 2)));
                Assert.That(sent[0].InputArguments.Count, Is.EqualTo(2));
                Assert.That(sent[0].InputArguments[0], Is.EqualTo(Variant.From(41)));
                StructuredEditorActions.AssertMatrix(sent[0].InputArguments[1], [2, 3], [0, 0, 0, 0, 0, 0]);
                Assert.That(dialog.Outputs.ToArray(), Is.EqualTo(new[]
                {
                    new MethodOutputRow(0, "Int32", "19"), new MethodOutputRow(1, "String", "accepted")
                }));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultStatus").Text,
                    Does.Contain("InputArgumentResults:"));
                Assert.That(shown.IsCompleted, Is.False);
                context.Session.Verify(s => s.CallAsync(It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                dialog.Close();
                await dialog.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task RepeatedAcceptCannotSubmitAnotherPendingCall(bool protocolFault)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            await using var connection = new DesktopConnectionContext();
            ConfigureArguments(context, [new Argument
            {
                Name = "Count", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar
            }]);
            var reply = new TaskCompletionSource<CallResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Session.Setup(s => s.CallAsync(It.IsAny<RequestHeader?>(),
                It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<CallResponse>(reply.Task));
            var dialog = new MethodCallDialog(
                Node(connection, NodeClass.Method), context.Session.Object, context.Service);
            _ = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                Button ok = DesktopInteraction.Control<Button>(dialog, "OkButton");
                dialog.Inputs[0].ValueText = "bad";
                DesktopInteraction.Click(ok);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultStatus").Text,
                    Does.Contain("Argument 'Count' parse error"));
                dialog.Inputs[0].ValueText = "8";
                DesktopInteraction.Click(ok);
                DesktopInteraction.Click(ok);
                Assert.That(ok.IsEnabled, Is.False);
                context.Session.Verify(s => s.CallAsync(It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()), Times.Once);
                await DesktopInteraction.ChangedAsync(ok, () => ok.IsEnabled, () =>
                {
                    if (protocolFault)
                    {
                        reply.SetException(new ServiceResultException(StatusCodes.BadTimeout, "controlled failure"));
                    }
                    else
                    {
                        reply.SetResult(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new CallMethodResult
                            {
                                StatusCode = StatusCodes.BadInvalidArgument,
                                InputArgumentResults = [StatusCodes.BadOutOfRange]
                            }]
                        });
                    }
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(dialog.Outputs, Is.Empty);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultStatus").Text,
                    Does.Contain(protocolFault ? "Call exception:" : "BadOutOfRange"));
                Assert.That(dialog.IsVisible, Is.True);
            }
            finally
            {
                reply.TrySetResult(new CallResponse { ResponseHeader = new ResponseHeader(), Results = [] });
                dialog.Close();
                await dialog.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task ElementDialogAcceptAndCancelSeparateCommittedResult(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            var definition = new EnumDefinition
            {
                Fields = [new EnumField { Name = "Idle", Value = 0 }, new EnumField { Name = "Ready", Value = 7 }]
            };
            var dialog = new ComplexValueElementDialog(
                DataTypeIds.Int32, definition, context.Service, Variant.From(0));
            Task shown = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                Button ok = DesktopInteraction.Control<Button>(dialog, "OkButton");
                await DesktopInteraction.ChangedAsync(ok, () => ok.IsEnabled, () => Task.CompletedTask)
                    .ConfigureAwait(true);
                ComplexValueEditor editor = DesktopInteraction.Control<ComplexValueEditor>(dialog, "Editor");
                StructuredEditorActions.Body(editor).Children.OfType<ComboBox>().Single().SelectedIndex = 1;
                Assert.That(dialog.WasCommitted, Is.False);
                Assert.That(dialog.Result.IsNull, Is.True);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(
                    dialog, accept ? "OkButton" : "CancelButton"));
                await shown.ConfigureAwait(true);
                Assert.That(dialog.WasCommitted, Is.EqualTo(accept));
                Assert.That(dialog.Result.IsNull, Is.EqualTo(!accept));
                if (accept)
                {
                    Assert.That(dialog.Result.TryGetValue(out int selected), Is.True);
                    Assert.That(selected, Is.EqualTo(7));
                }
            }
            finally
            {
                dialog.Close();
                await dialog.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task ArrayDialogEditsAndReordersTypedElementsWithoutMutatingInput(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            ArrayOf<Variant> original = [Variant.From(11), Variant.From(22), Variant.From(33)];
            var dialog = new EditArrayDialog("Selected array", DataTypeIds.Int32, null,
                original, context.Service, BuiltInType.Int32);
            Task shown = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                ListBox list = DesktopInteraction.Control<ListBox>(dialog, "ItemsList");
                list.SelectedIndex = 1;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "MoveUpButton"));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "MoveUpButton"));
                Assert.That(list.SelectedIndex, Is.Zero);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "MoveDownButton"));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "AddButton"));
                Assert.That(list.SelectedIndex, Is.EqualTo(3));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "RemoveButton"));
                Assert.That(list.SelectedIndex, Is.EqualTo(2));
                PrimitiveValuePromptDialog child = await DesktopInteraction.OpenedAsync<PrimitiveValuePromptDialog>(
                    () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "EditButton")))
                    .ConfigureAwait(true);
                DesktopInteraction.Control<TextBox>(child, "ValueText").Text = "2147483648";
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(child, "OkButton"));
                Assert.That(child.WasCommitted, Is.False);
                Assert.That(DesktopInteraction.Control<TextBlock>(child, "StatusLabel").Text,
                    Does.StartWith("Parse error:"));
                DesktopInteraction.Control<TextBox>(child, "ValueText").Text = "91";
                await DesktopInteraction.CollectionChangedAsync((INotifyCollectionChanged)list.ItemsSource!,
                    () => list.Items.Cast<ArrayRow>().Last().Value == Variant.From(91),
                    () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(child, "OkButton")))
                    .ConfigureAwait(true);
                Assert.That(list.Items.Cast<ArrayRow>().Select(row => row.Value),
                    Is.EqualTo(new[] { Variant.From(11), Variant.From(22), Variant.From(91) }));
                Assert.That(original.ToArray(),
                    Is.EqualTo(new[] { Variant.From(11), Variant.From(22), Variant.From(33) }));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(
                    dialog, accept ? "OkButton" : "CancelButton"));
                await shown.ConfigureAwait(true);
                Assert.That(dialog.WasCommitted, Is.EqualTo(accept));
                Assert.That(dialog.Result.IsNull, Is.EqualTo(!accept));
                if (accept)
                {
                    Assert.That(dialog.Result.ToArray(),
                        Is.EqualTo(new[] { Variant.From(11), Variant.From(22), Variant.From(91) }));
                }
            }
            finally
            {
                dialog.Close();
                await dialog.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task CallWithoutArgumentsResolvesParentAndDisplaysTypedArrayOutput()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            await using var connection = new DesktopConnectionContext();
            var browses = new System.Collections.Generic.List<BrowseDescription>();
            context.Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> ids,
                    CancellationToken _) =>
                {
                    browses.Add(ids[0]);
                    return ValueTask.FromResult(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            References = ids[0].BrowseDirection == BrowseDirection.Inverse
                                ? [new ReferenceDescription { NodeId = new ExpandedNodeId(45u) }] : []
                        }]
                    });
                });
            ArrayOf<CallMethodRequest> sent = default;
            context.Session.Setup(s => s.CallAsync(It.IsAny<RequestHeader?>(),
                It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Callback((RequestHeader? _, ArrayOf<CallMethodRequest> values, CancellationToken _) => sent = values)
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From((ArrayOf<int>)[2, 4])]
                    }]
                });
            var method = new NodeViewModel(connection.Browser, NodeId.Null, new NodeId(1234u, 2),
                "No inputs", NodeClass.Method);
            var dialog = new MethodCallDialog(method, context.Session.Object, context.Service);
            _ = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                Assert.That(dialog.Inputs, Is.Empty);
                Assert.That(sent.IsNull, Is.True);
                Assert.That(browses.Select(browse => browse.ReferenceTypeId),
                    Is.EqualTo(new[] { ReferenceTypeIds.HasComponent, ReferenceTypeIds.HasProperty }));
                Assert.That(browses.Select(browse => browse.BrowseDirection),
                    Is.EqualTo(new[] { BrowseDirection.Inverse, BrowseDirection.Forward }));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(sent.Count, Is.EqualTo(1));
                Assert.That(sent[0].ObjectId, Is.EqualTo(new NodeId(45u)));
                Assert.That(sent[0].MethodId, Is.EqualTo(method.NodeId));
                Assert.That(sent[0].InputArguments.IsEmpty, Is.True);
                Assert.That(dialog.Outputs.Single(), Is.EqualTo(new MethodOutputRow(0, "Int32[]", "[2, 4]")));
                Assert.That(context.Reads, Is.Zero);
            }
            finally
            {
                dialog.Close();
                await dialog.StopAsync().ConfigureAwait(true);
            }
        });
    }

    [TestCase("ShortBrowse")]
    [TestCase("DeniedBrowse")]
    [TestCase("MalformedArguments")]
    public Task MethodMetadataFailureDoesNotInvokeTheServer(string failure)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var context = new StructuredValueTestContext();
            await using var connection = new DesktopConnectionContext();
            context.Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = failure == "ShortBrowse" ? [] : [new BrowseResult
                    {
                        StatusCode = failure == "DeniedBrowse" ? StatusCodes.BadUserAccessDenied : StatusCodes.Good,
                        References = [new ReferenceDescription
                        {
                            NodeId = new ExpandedNodeId(300u),
                            BrowseName = new QualifiedName(BrowseNames.InputArguments)
                        }]
                    }]
                });
            context.Reader = (_, _) => ValueTask.FromResult(Reply(Variant.From("not Argument[]")));
            var dialog = new MethodCallDialog(
                Node(connection, NodeClass.Method), context.Session.Object, context.Service);
            _ = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultStatus").Text,
                    Does.StartWith("Failed to load arguments:"));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ResultStatus").Text,
                    Does.StartWith("Cannot call"));
                Assert.That(dialog.Outputs, Is.Empty);
                Assert.That(context.Reads, Is.EqualTo(failure == "MalformedArguments" ? 1 : 0));
                context.Session.Verify(s => s.CallAsync(It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                dialog.Close();
                await dialog.StopAsync().ConfigureAwait(true);
            }
        });
    }

    private static NodeViewModel Node(DesktopConnectionContext connection, NodeClass nodeClass)
    {
        return new NodeViewModel(connection.Browser, new NodeId(200u, 2), new NodeId(1234u, 2),
            nodeClass == NodeClass.Method ? "Transform" : "Reading", nodeClass);
    }

    private static void ConfigureScalarRead(StructuredValueTestContext context)
    {
        context.Reader = (ids, _) => ValueTask.FromResult(ids[0].AttributeId == Attributes.DataTypeDefinition
            ? StructuredValueTestContext.Reply(Variant.Null, StatusCodes.BadAttributeIdInvalid)
            : Reply(Variant.From(4), Variant.From(DataTypeIds.Int32),
                Variant.From(ValueRanks.Scalar), Variant.From((ArrayOf<uint>)[])));
    }

    private static void ConfigureArguments(StructuredValueTestContext context, ArrayOf<Argument> arguments)
    {
        context.Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
            It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowseResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [new BrowseResult
                {
                    StatusCode = StatusCodes.Good,
                    References = [new ReferenceDescription
                    {
                        NodeId = new ExpandedNodeId(300u),
                        BrowseName = new QualifiedName(BrowseNames.InputArguments)
                    }]
                }]
            });
        context.Reader = (ids, _) => ValueTask.FromResult(ids[0].AttributeId == Attributes.DataTypeDefinition
            ? StructuredValueTestContext.Reply(Variant.Null, StatusCodes.BadAttributeIdInvalid)
            : Reply(Variant.FromStructure(arguments)));
    }

    private static ReadResponse Reply(params Variant[] values)
    {
        return new ReadResponse
        {
            ResponseHeader = new ResponseHeader(),
            Results = values.Select(value => new DataValue(value)).ToArray()
        };
    }

    private static readonly string[] s_callAcceptCommitsTypedArgumentsAndDisplaysOutputsExpected =
    [
        "Gain",
        "Cells",
    ];
}
