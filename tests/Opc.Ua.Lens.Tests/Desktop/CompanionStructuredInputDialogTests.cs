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
using CommunityToolkit.Mvvm.Input;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Identity;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.Plugins.Companions;
using UaLens.Tests.Companions;
using UaLens.Tests.StructuredValues;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Desktop
{
    [TestFixture]
    [Platform("Win,Linux")]
    [NonParallelizable]
    public sealed class CompanionStructuredInputDialogTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public Task OwnedTypedDialogAcceptsOrCancelsArrayAndMatrixValues(bool matrix, bool accept)
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new CompanionDialogContext();
                await using (context.ConfigureAwait(true))
                {
                    context.Inputs.Offer([CompanionStructuredInputTestContext.Field(BuiltInType.Int32) with
                    {
                        ValueRank = matrix ? 2 : 1,
                        ArrayDimensions = matrix ? [2u, 3u] : [8u]
                    }]);
                    context.Inputs.Values.Reader = (ids, _) =>
                    {
                        CompanionStructuredInputTestContext.AssertDefinitionRead(ids, DataTypeIds.Int32);
                        return ValueTask.FromResult(
                            StructuredValueTestContext.Reply(Variant.Null, StatusCodes.BadAttributeIdInvalid));
                    };
                    await context.InitializeAsync().ConfigureAwait(true);
                    CompanionInputEditor field = context.Plugin.InputFields.Single();
                    Variant original = matrix
                        ? Variant.From(((ArrayOf<int>)[17, -4, 90, 4, 5, 6]).ToMatrix([2, 3]))
                        : Variant.From([17, -4, 90, 4, 5, 6]);
                    field.AcceptValue(original, context.Inputs.Values.MessageContext, static () => { });
                    await context.ExecuteAsync(context.Plugin.PrepareTaskCommand).ConfigureAwait(true);
                    CompanionOperationDraft previous = context.Plugin.PreparedOperation ??
                        throw new AssertionException("The initial value was not prepared.");

                    ComplexValueElementDialog dialog = await context.OpenAsync(field).ConfigureAwait(true);
                    Task editing = field.EditCommand.ExecutionTask ??
                        throw new AssertionException("The bound edit command did not start.");
                    Assert.That(dialog.Owner, Is.SameAs(DesktopInteraction.Owner));
                    Assert.That(dialog.IsVisible, Is.True);
                    Assert.That(context.Plugin.PreparedOperation, Is.Null);
                    Assert.That(context.Plugin.RunTaskCommand.CanExecute(null), Is.False);
                    Assert.That(context.Inputs.Values.Reads, Is.Zero);
                    Assert.That(field.IsText, Is.False);
                    Assert.That(context.View.GetLogicalDescendants().OfType<TextBox>()
                        .Any(control => ReferenceEquals(control.DataContext, field) && control.IsVisible), Is.False);
                    ComplexValueEditor editor = DesktopInteraction.Control<ComplexValueEditor>(dialog, "Editor");
                    Button editElements = StructuredEditorActions.Body(editor).GetLogicalDescendants().OfType<Button>()
                        .Single(button => button.Content is string text &&
                            text.StartsWith("Edit elements", StringComparison.Ordinal));
                    EditArrayDialog elements = await DesktopInteraction.OpenedAsync<EditArrayDialog>(
                        () => DesktopInteraction.Click(editElements)).ConfigureAwait(true);
                    Assert.That(elements.Owner, Is.SameAs(dialog));
                    ListBox rows = DesktopInteraction.Control<ListBox>(elements, "ItemsList");
                    Assert.That(rows.Items, Has.Count.EqualTo(6));
                    rows.SelectedIndex = 1;
                    PrimitiveValuePromptDialog prompt =
                        await DesktopInteraction.OpenedAsync<PrimitiveValuePromptDialog>(
                            () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(elements, "EditButton")))
                            .ConfigureAwait(true);
                    Assert.That(prompt.Owner, Is.SameAs(elements));
                    DesktopInteraction.Control<TextBox>(prompt, "ValueText").Text = "83";
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(prompt, "OkButton"));
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                    Assert.That(rows.Items.OfType<ArrayRow>().ElementAt(1).Value, Is.EqualTo(Variant.From(83)));
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(elements, "MoveUpButton"));
                    Assert.That(rows.SelectedIndex, Is.Zero);
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(elements, "OkButton"));
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                    Assert.That(elements.Result[0], Is.EqualTo(Variant.From(83)));
                    Assert.That(elements.Result[1], Is.EqualTo(Variant.From(17)));
                    if (matrix)
                    {
                        StructuredEditorActions.ApplyShape(editor, "1, 3", true);
                    }
                    AssertValue(StructuredEditorActions.Commit(editor), matrix,
                        matrix ? [83, 17, 90] : [83, 17, 90, 4, 5, 6], matrix ? [1, 3] : []);

                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(
                        dialog, accept ? "OkButton" : "CancelButton"));
                    await editing.ConfigureAwait(true);

                    ArrayOf<int> expected = accept
                        ? matrix ? [83, 17, 90] : [83, 17, 90, 4, 5, 6]
                        : [17, -4, 90, 4, 5, 6];
                    ArrayOf<int> dimensions = matrix ? accept ? [1, 3] : [2, 3] : [];
                    AssertValue(field.Capture().Value, matrix, expected, dimensions);
                    AssertValue(original, matrix, [17, -4, 90, 4, 5, 6], matrix ? [2, 3] : []);
                    Assert.That(dialog.WasCommitted, Is.EqualTo(accept));
                    Assert.That(dialog.IsVisible, Is.False);
                    Assert.That(context.Plugin.Status, Does.Contain(accept ? "accepted locally" : "edit canceled"));
                    Assert.That(context.Plugin.PreparedOperation, Is.Null);
                    await Assert.ThatAsync(() => context.Inputs.Tasks.Workspace.ExecuteTaskAsync(previous, false, null),
                        Throws.InvalidOperationException).ConfigureAwait(true);
                    context.Inputs.Tasks.VerifyCalls(1, 0);
                    await context.ExecuteAsync(context.Plugin.PrepareTaskCommand).ConfigureAwait(true);
                    CompanionOperationDraft prepared = context.Plugin.PreparedOperation ??
                        throw new AssertionException(context.Plugin.Status);
                    AssertValue(prepared.Inputs[0].Value, matrix, expected, dimensions);
                    await context.ExecuteAsync(context.Plugin.RunTaskCommand).ConfigureAwait(true);
                    Assert.That(context.Plugin.Status, Is.EqualTo("Typed operation completed."));
                    Assert.That(context.Plugin.Values.Single().Value, Is.EqualTo(Variant.From(19u)));
                    Assert.That(context.Plugin.PreparedOperation, Is.Null);
                    Assert.That(DesktopInteraction.Owner.OwnedWindows, Is.Empty);
                    Assert.That(context.Inputs.Values.Reads, Is.EqualTo(1));
                    context.Inputs.Tasks.VerifyCalls(2, 1);
                }
            });
        }

        [Test]
        public Task SelectionChangeCannotApplyAnOldDialog()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new CompanionDialogContext();
                await using (context.ConfigureAwait(true))
                {
                    context.Inputs.Offer([CompanionStructuredInputTestContext.Field(BuiltInType.Int32) with
                    {
                        ValueRank = 1
                    }]);
                    await context.InitializeAsync().ConfigureAwait(true);
                    CompanionInputEditor previous = context.Plugin.InputFields.Single();
                    previous.AcceptValue(Variant.From([17, -4]),
                        context.Inputs.Values.MessageContext, static () => { });
                    ComplexValueElementDialog dialog = await context.OpenAsync(previous).ConfigureAwait(true);
                    Task editing = previous.EditCommand.ExecutionTask ??
                        throw new AssertionException("The edit command did not start.");
                    ComplexValueEditor editor = DesktopInteraction.Control<ComplexValueEditor>(dialog, "Editor");
                    StructuredEditorActions.Body(editor).Children.OfType<CheckBox>().Single().IsChecked = true;
                    DesktopInteraction.Click(
                        StructuredEditorActions.Body(editor).GetLogicalDescendants().OfType<Button>()
                            .Single(button => Equals(button.Content, "Set empty")));
                    context.Plugin.SelectedOperation = null;

                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                    await editing.ConfigureAwait(true);

                    Assert.That(dialog.WasCommitted, Is.True);
                    Assert.That(dialog.Result.TryGetValue(out ArrayOf<int> discarded), Is.True);
                    Assert.That(discarded.IsNull, Is.False);
                    Assert.That(discarded.Count, Is.Zero);
                    AssertValue(previous.Capture().Value, false, [17, -4], []);
                    Assert.That(context.Plugin.InputFields, Is.Empty);
                    Assert.That(context.Plugin.PreparedOperation, Is.Null);
                    Assert.That(context.Plugin.Status, Is.EqualTo("Edit typed input canceled."));
                    Assert.That(context.Plugin.IsBusy, Is.False);
                    Assert.That(DesktopInteraction.Owner.OwnedWindows, Is.Empty);
                    context.Inputs.Tasks.VerifyCalls(0, 0);
                    Assert.That(context.Inputs.Values.Reads, Is.Zero);
                }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task AcceptedCustomDialogValueRejectsSchemaOrSessionChanges(bool changeSession)
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new CompanionDialogContext();
                await using (context.ConfigureAwait(true))
                {
                    var typeId = new NodeId(4200u, context.Inputs.Values.NamespaceIndex);
                    EnumDefinition definition = CompanionStructuredInputTestContext.Enumeration();
                    context.Inputs.Offer([context.Inputs.CustomField(BuiltInType.Enumeration, typeId)]);
                    context.Inputs.ReturnDefinition(typeId, definition);
                    await context.InitializeAsync().ConfigureAwait(true);
                    CompanionInputEditor field = context.Plugin.InputFields.Single();
                    ComplexValueElementDialog dialog = await context.OpenAsync(field).ConfigureAwait(true);
                    Task editing = field.EditCommand.ExecutionTask ??
                        throw new AssertionException("The edit command did not start.");
                    ComplexValueEditor editor = DesktopInteraction.Control<ComplexValueEditor>(dialog, "Editor");
                    StructuredEditorActions.Body(editor).Children.OfType<ComboBox>().Single().SelectedIndex = 1;
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                    await editing.ConfigureAwait(true);
                    CompanionValue accepted = field.Capture();
                    Assert.That(accepted.Value.TryGetValue(out int selected), Is.True);
                    Assert.That(selected, Is.EqualTo(7));
                    Assert.That(accepted.InputSchemaDigest, Is.EqualTo(
                        CompanionInputContract.SchemaDigest(definition, context.Inputs.Values.MessageContext)));
                    Assert.That(accepted.InputSchemaDigest.Length, Is.EqualTo(32));
                    Assert.That(context.Inputs.Values.Reads, Is.EqualTo(1));
                    if (changeSession)
                    {
                        context.Inputs.Tasks.SessionId = new NodeId(202u);
                    }
                    else
                    {
                        definition.Fields = [new EnumField { Name = "Reassigned", Value = 7 }];
                    }

                    await context.ExecuteAsync(context.Plugin.PrepareTaskCommand).ConfigureAwait(true);

                    Assert.That(context.Plugin.PreparedOperation, Is.Null);
                    Assert.That(context.Plugin.RunTaskCommand.CanExecute(null), Is.False);
                    Assert.That(context.Plugin.Status,
                        Does.Contain(changeSession ? "obsolete session" : "definition changed"));
                    if (changeSession)
                    {
                        Assert.That(() => field.Capture(),
                            Throws.InvalidOperationException.With.Message.Contains("obsolete session"));
                    }
                    else
                    {
                        Assert.That(field.Capture().InputSchemaDigest, Is.EqualTo(accepted.InputSchemaDigest));
                        Assert.That(field.Capture().Value, Is.EqualTo(accepted.Value));
                    }
                    Assert.That(context.Inputs.Values.Reads, Is.EqualTo(changeSession ? 1 : 2));
                    context.Inputs.Tasks.VerifyCalls(0, 0);
                    Assert.That(DesktopInteraction.Owner.OwnedWindows, Is.Empty);
                }
            });
        }

        [Test]
        public Task AcceptedStructuredDialogPinsItsNestedDefinitions()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new CompanionDialogContext();
                await using (context.ConfigureAwait(true))
                {
                    StructuredValueTestContext.NativeType child = context.Inputs.Values.Register(
                        "NestedDialogSettings", StructureType.Structure,
                        [StructuredValueTestContext.Field("Counter", DataTypeIds.Int32)]);
                    StructuredValueTestContext.NativeType root = context.Inputs.Values.Register(
                        "DialogSettings", StructureType.Structure,
                        [StructuredValueTestContext.Field("Nested", child.DataTypeId)]);
                    IEncodeable childValue = child.Type.CreateInstance();
                    ((IStructure)childValue)["Counter"] = Variant.From(31);
                    IEncodeable rootValue = root.Type.CreateInstance();
                    ((IStructure)rootValue)["Nested"] = Variant.FromStructure(childValue);
                    StructureDefinition nested = CoreUtils.Clone(child.Definition)!;
                    context.Inputs.Values.Reader = (ids, _) => ValueTask.FromResult(StructuredValueTestContext.Reply(
                        Variant.FromStructure(ids[0].NodeId == root.DataTypeId ? root.Definition : nested)));
                    context.Inputs.Offer([context.Inputs.CustomField(BuiltInType.ExtensionObject, root.DataTypeId)]);
                    await context.InitializeAsync().ConfigureAwait(true);
                    CompanionInputEditor field = context.Plugin.InputFields.Single();
                    field.AcceptValue(Variant.FromStructure(rootValue),
                        context.Inputs.Values.MessageContext, static () => { });
                    ComplexValueElementDialog dialog = await context.OpenAsync(field).ConfigureAwait(true);
                    Task editing = field.EditCommand.ExecutionTask ??
                        throw new AssertionException("The typed edit did not start.");
                    Assert.That(context.Inputs.Values.Reads, Is.EqualTo(2));
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                    await editing.ConfigureAwait(true);
                    CompanionValue accepted = field.Capture();
                    Assert.That(accepted.InputSchemaDigest.Length, Is.EqualTo(32));
                    Assert.That(accepted.InputSchemaDigest, Is.Not.EqualTo(
                        CompanionInputContract.SchemaDigest(root.Definition, context.Inputs.Values.MessageContext)));
                    nested.Fields[0].Description = new LocalizedText("Changed nested contract");

                    await context.ExecuteAsync(context.Plugin.PrepareTaskCommand).ConfigureAwait(true);

                    Assert.That(context.Plugin.PreparedOperation, Is.Null);
                    Assert.That(context.Plugin.RunTaskCommand.CanExecute(null), Is.False);
                    Assert.That(context.Plugin.Status, Does.Contain("definition changed"));
                    Assert.That(field.Capture().Value, Is.EqualTo(accepted.Value));
                    Assert.That(context.Inputs.Values.Reads, Is.EqualTo(4));
                    context.Inputs.Tasks.VerifyCalls(0, 0);
                    Assert.That(DesktopInteraction.Owner.OwnedWindows, Is.Empty);
                }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task PendingMetadataCannotOpenDialogAfterSelectionOrSessionChange(bool changeSession)
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new CompanionDialogContext();
                await using (context.ConfigureAwait(true))
                {
                    var typeId = new NodeId(4200u, context.Inputs.Values.NamespaceIndex);
                    context.Inputs.Offer([context.Inputs.CustomField(BuiltInType.Enumeration, typeId)]);
                    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = new TaskCompletionSource<ReadResponse>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    context.Inputs.Values.Reader = (ids, _) =>
                    {
                        CompanionStructuredInputTestContext.AssertDefinitionRead(ids, typeId);
                        entered.TrySetResult();
                        return new ValueTask<ReadResponse>(release.Task);
                    };
                    await context.InitializeAsync().ConfigureAwait(true);
                    CompanionInputEditor field = context.Plugin.InputFields.Single();
                    Task editing = field.EditCommand.ExecuteAsync(null);
                    try
                    {
                        await Task.WhenAny(entered.Task, editing).ConfigureAwait(true);
                        Assert.That(entered.Task.IsCompletedSuccessfully, Is.True);
                        Assert.That(context.Plugin.IsBusy, Is.True);
                        Assert.That(DesktopInteraction.Owner.OwnedWindows, Is.Empty);
                        if (changeSession)
                        {
                            context.Inputs.Tasks.SessionId = new NodeId(202u);
                        }
                        else
                        {
                            context.Plugin.SelectedOperation = null;
                        }
                    }
                    finally
                    {
                        release.TrySetResult(StructuredValueTestContext.Reply(
                            Variant.FromStructure(CompanionStructuredInputTestContext.Enumeration())));
                    }
                    await editing.ConfigureAwait(true);

                    Assert.That(() => field.Capture(),
                        Throws.InvalidOperationException.With.Message.Contains("accept"));
                    Assert.That(context.Plugin.PreparedOperation, Is.Null);
                    Assert.That(context.Plugin.Status,
                        Does.Contain(changeSession ? "BadInvalidState" : "canceled"));
                    Assert.That(context.Plugin.IsBusy, Is.False);
                    Assert.That(DesktopInteraction.Owner.OwnedWindows, Is.Empty);
                    context.Inputs.Tasks.VerifyCalls(0, 0);
                    Assert.That(context.Inputs.Values.Reads, Is.EqualTo(1));
                }
            });
        }

        [TestCase("Stop")]
        [TestCase("Disconnect")]
        [TestCase("Restore")]
        [TestCase("Dispose")]
        public Task LifecycleChangeClosesAndDrainsOwnedEditor(string change)
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new CompanionDialogContext();
                await using (context.ConfigureAwait(true))
                {
                    context.Inputs.Offer([CompanionStructuredInputTestContext.Field(BuiltInType.Int32) with
                    {
                        ValueRank = 2, ArrayDimensions = [2u, 3u]
                    }]);
                    await context.InitializeAsync().ConfigureAwait(true);
                    CompanionInputEditor field = context.Plugin.InputFields.Single();
                    var initial = Variant.From(((ArrayOf<int>)[17, -4, 90, 4, 5, 6]).ToMatrix([2, 3]));
                    field.AcceptValue(initial, context.Inputs.Values.MessageContext, static () => { });
                    ComplexValueElementDialog dialog = await context.OpenAsync(field).ConfigureAwait(true);
                    Task editing = field.EditCommand.ExecutionTask ??
                        throw new AssertionException("The edit command did not start.");
                    ComplexValueEditor editor = DesktopInteraction.Control<ComplexValueEditor>(dialog, "Editor");
                    StructuredEditorActions.ApplyShape(editor, "1, 3", true);
                    Assert.That(dialog.IsVisible, Is.True);

                    switch (change)
                    {
                        case "Stop":
                            await context.Plugin.CancelCommand.ExecuteAsync(null).ConfigureAwait(true);
                            break;
                        case "Disconnect":
                            await context.Desktop.Connection.DisconnectAsync().ConfigureAwait(true);
                            await context.Plugin.OnConnectionStateChangedAsync(default).ConfigureAwait(true);
                            Assert.That(context.Plugin.IsOffline, Is.True);
                            break;
                        case "Restore":
                            await context.Plugin.RestoreStateAsync(context.Plugin.CaptureState()).ConfigureAwait(true);
                            break;
                        case "Dispose":
                            await context.Plugin.DisposeAsync().ConfigureAwait(true);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(change));
                    }
                    await editing.ConfigureAwait(true);

                    Assert.That(editing.IsCompletedSuccessfully, Is.True);
                    Assert.That(dialog.IsVisible, Is.False);
                    Assert.That(dialog.WasCommitted, Is.False);
                    Assert.That(context.Plugin.IsBusy, Is.False);
                    Assert.That(context.Plugin.PreparedOperation, Is.Null);
                    Assert.That(DesktopInteraction.Owner.OwnedWindows, Is.Empty);
                    AssertValue(field.Capture().Value, true, [17, -4, 90, 4, 5, 6], [2, 3]);
                    context.Inputs.Tasks.VerifyCalls(0, 0);
                    Assert.That(context.Inputs.Values.Reads, Is.Zero);
                }
            });
        }

        private static void AssertValue(
            Variant value, bool matrix, ArrayOf<int> expected, ArrayOf<int> dimensions)
        {
            Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(matrix ? 2 : 1));
            if (matrix)
            {
                Assert.That(value.TryGetValue(out MatrixOf<int> actual), Is.True);
                Assert.That(actual.Dimensions, Is.EqualTo(dimensions.ToArray()));
                Assert.That(actual.ToArrayOf().ToArray(), Is.EqualTo(expected.ToArray()));
            }
            else
            {
                Assert.That(value.TryGetValue(out ArrayOf<int> actual), Is.True);
                Assert.That(actual.IsNull, Is.False);
                Assert.That(actual, Is.EqualTo(expected));
            }
        }

        private sealed class CompanionDialogContext : IAsyncDisposable
        {
            public CompanionDialogContext()
            {
                Inputs = new CompanionStructuredInputTestContext(Desktop.Telemetry);
                Inputs.Tasks.Endpoint.TransportProfileUri = Profiles.UaTcpTransport;
                Inputs.Tasks.Endpoint.UserIdentityTokens =
                    [new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" }];
                var lease = new Mock<IConnectionSession>(MockBehavior.Strict);
                lease.SetupGet(value => value.Session).Returns(Inputs.Tasks.Session.Object);
                lease.SetupGet(value => value.State).Returns(new ConnectionSessionState(ConnectionPhase.Connected));
                lease.SetupAdd(value => value.StateChanged +=
                    It.IsAny<Action<IConnectionSession, ConnectionSessionState>>());
                lease.SetupRemove(value => value.StateChanged -=
                    It.IsAny<Action<IConnectionSession, ConnectionSessionState>>());
                lease.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
                Desktop.DiscoverAsync = (_, _) =>
                    Task.FromResult<ArrayOf<EndpointDescription>>([Inputs.Tasks.Endpoint]);
                Desktop.Backend.Setup(value => value.ConnectAsync(
                        It.IsAny<ApplicationConfiguration>(), It.IsAny<EndpointDescription>(),
                        It.IsAny<ConnectionProfile>(), It.IsAny<IClientIdentityProvider>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(lease.Object);
                Host = new PluginHost(new Mock<IPluginWorkspace>(MockBehavior.Strict).Object,
                    Desktop.Connection, Desktop.Browser, Desktop.Telemetry,
                    new Mock<ICapabilityService>(MockBehavior.Strict).Object, null);
                Plugin = new CompanionPlugin(Host, Inputs.Tasks.Workspace);
                View = ((IPlugin)Plugin).View ?? throw new AssertionException("The companion view is missing.");
                Inputs.Tasks.VerifyCalls(0, 0);
                Inputs.Tasks.VerifyInspections(0);
                Inputs.Tasks.Provider.Verify(value => value.DiscoverAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()), Times.Never);
                Assert.That(Inputs.Values.Reads, Is.Zero);
            }

            public DesktopConnectionContext Desktop { get; } = new();

            public CompanionStructuredInputTestContext Inputs { get; }

            public PluginHost Host { get; }

            public CompanionPlugin Plugin { get; }

            public Control View { get; }

            public async Task InitializeAsync()
            {
                await Desktop.Connection.ConnectAsync(ConnectionProfile.Create(
                    Inputs.Tasks.Endpoint, Inputs.Tasks.Endpoint.UserIdentityTokens[0],
                    SubscriptionEngineKind.ChannelV2)).ConfigureAwait(true);
                Assert.That(Host.Session, Is.SameAs(Inputs.Tasks.Session.Object));
                await Plugin.OnConnectionStateChangedAsync(default).ConfigureAwait(true);
                await Plugin.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(true);
                await Plugin.InspectCommand.ExecuteAsync(null).ConfigureAwait(true);
                Assert.That(Plugin.InputFields, Has.Count.EqualTo(1), Plugin.Status);
                DesktopInteraction.Owner.Content = View;
                View.GetLogicalDescendants().OfType<Expander>().Single().IsExpanded = true;
                DesktopInteraction.Owner.UpdateLayout();
            }

            public async Task<ComplexValueElementDialog> OpenAsync(CompanionInputEditor field)
            {
                Button button = View.GetLogicalDescendants().OfType<Button>()
                    .Single(value => ReferenceEquals(value.Command, field.EditCommand));
                Assert.That(button.IsVisible, Is.True);
                Assert.That(button.IsEffectivelyEnabled, Is.True);
                ComplexValueElementDialog dialog = await DesktopInteraction.OpenedAsync<ComplexValueElementDialog>(
                    () => button.Command!.Execute(button.CommandParameter)).ConfigureAwait(true);
                Button ok = DesktopInteraction.Control<Button>(dialog, "OkButton");
                await DesktopInteraction.ChangedAsync(ok, () => ok.IsEnabled, () => Task.CompletedTask)
                    .ConfigureAwait(true);
                return dialog;
            }

            public async Task ExecuteAsync(IAsyncRelayCommand command)
            {
                Button button = View.GetLogicalDescendants().OfType<Button>()
                    .Single(value => ReferenceEquals(value.Command, command));
                Assert.That(button.IsEffectivelyEnabled, Is.True, Plugin.Status);
                button.Command!.Execute(button.CommandParameter);
                await (command.ExecutionTask ?? throw new AssertionException("The bound command did not start."))
                    .ConfigureAwait(true);
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Plugin.DisposeAsync().ConfigureAwait(true);
                }
                finally
                {
                    DesktopInteraction.Owner.Content = null;
                    await Host.DisposeAsync().ConfigureAwait(true);
                    await Desktop.DisposeAsync().ConfigureAwait(true);
                    await Inputs.DisposeAsync().ConfigureAwait(true);
                }
            }
        }
    }
}
