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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Tests.Observe;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class CompanionInputEditorTests
{
    [Test]
    public void ConstructorRequiresTheFieldDefinitionAndChangeCallback()
    {
        Assert.That(
            () => new CompanionInputEditor(null!, static () => { }),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("definition"));
        Assert.That(
            () => new CompanionInputEditor(Definition(BuiltInType.String), null!),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("changed"));
    }

    [TestCase(false, "true")]
    [TestCase(true, "false")]
    [TestCase(false, "")]
    [TestCase(true, "not a Boolean")]
    public void BooleanCaptureUsesTheCheckboxRatherThanParsingText(bool selected, string text)
    {
        int changes = 0;
        var editor = new CompanionInputEditor(Definition(BuiltInType.Boolean), () => changes++)
        {
            Text = text,
            Checked = selected
        };
        int before = changes;

        CompanionValue captured = editor.Capture();

        Assert.That(editor.IsBoolean, Is.True);
        Assert.That(captured.Name, Is.EqualTo("field"));
        Assert.That(captured.Value.TypeInfo.IsScalar, Is.True);
        Assert.That(captured.Value.TryGetValue(out bool value), Is.True);
        Assert.That(value, Is.EqualTo(selected));
        Assert.That(editor.Text, Is.EqualTo(text));
        Assert.That(changes, Is.EqualTo(before), "Capturing a value must not edit or re-arm its form.");
    }

    [TestCase("0", 0u)]
    [TestCase(" 1 ", 1u)]
    [TestCase("4294967295", uint.MaxValue)]
    public void UnsignedIntegerCapturePreservesTheFullUInt32Range(string text, uint expected)
    {
        var editor = new CompanionInputEditor(Definition(BuiltInType.UInt32), static () => { }) { Text = text };

        CompanionValue captured = editor.Capture();

        Assert.That(editor.IsBoolean, Is.False);
        Assert.That(captured.Name, Is.EqualTo("field"));
        Assert.That(captured.Value.TryGetValue(out uint value), Is.True);
        Assert.That(value, Is.EqualTo(expected));
        Assert.That(editor.Text, Is.EqualTo(text));
    }

    [TestCase("-2147483648", int.MinValue)]
    [TestCase("-1", -1)]
    [TestCase("0", 0)]
    [TestCase("+1", 1)]
    [TestCase("2147483647", int.MaxValue)]
    public void SignedIntegerCapturePreservesSignAndInt32Boundaries(string text, int expected)
    {
        var editor = new CompanionInputEditor(Definition(BuiltInType.Int32), static () => { }) { Text = text };

        CompanionValue captured = editor.Capture();

        Assert.That(captured.Name, Is.EqualTo("field"));
        Assert.That(captured.Value.TryGetValue(out int value), Is.True);
        Assert.That(value, Is.EqualTo(expected));
        Assert.That(editor.Text, Is.EqualTo(text));
    }

    [TestCase("0", 0d)]
    [TestCase("-2.5", -2.5)]
    [TestCase("1.25e2", 125d)]
    [TestCase("1.7976931348623157E+308", double.MaxValue)]
    [TestCase("4.9406564584124654E-324", double.Epsilon)]
    [SetCulture("de-DE")]
    public void DoubleCaptureUsesInvariantScalarParsingEvenInADecimalCommaCulture(string text, double expected)
    {
        var editor = new CompanionInputEditor(Definition(BuiltInType.Double), static () => { }) { Text = text };

        CompanionValue captured = editor.Capture();

        Assert.That(captured.Name, Is.EqualTo("field"));
        Assert.That(captured.Value.TryGetValue(out double value), Is.True);
        Assert.That(value, Is.EqualTo(expected));
        Assert.That(editor.Text, Is.EqualTo(text));
    }

    [TestCase("UInt32", "-1")]
    [TestCase("UInt32", "4294967296")]
    [TestCase("UInt32", "1.5")]
    [TestCase("UInt32", "true")]
    [TestCase("Int32", "-2147483649")]
    [TestCase("Int32", "2147483648")]
    [TestCase("Int32", "1.5")]
    [TestCase("Double", "not-a-number")]
    [TestCase("Double", "1.2.3")]
    public void InvalidNumericValuesFailWithTheFieldNameAndLeaveTheEditorUnchanged(string typeName, string text)
    {
        BuiltInType type = Enum.Parse<BuiltInType>(typeName);
        int changes = 0;
        var editor = new CompanionInputEditor(Definition(type), () => changes++) { Text = text };

        Assert.That(
            () => editor.Capture(),
            Throws.ArgumentException.With.Message.Contains("Test field"));

        Assert.That(editor.Text, Is.EqualTo(text));
        Assert.That(changes, Is.EqualTo(1));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\t\r\n")]
    public void RequiredTextRejectsEmptyOrWhitespaceInsteadOfCreatingAnImplicitValue(string text)
    {
        var editor = new CompanionInputEditor(Definition(BuiltInType.String), static () => { }) { Text = text };

        Assert.That(
            () => editor.Capture(),
            Throws.ArgumentException.With.Message.Contains("Test field"));

        Assert.That(editor.Text, Is.EqualTo(text));
        Assert.That(editor.Checked, Is.False);
    }

    [TestCase("")]
    [TestCase(" \t\r\n")]
    [TestCase("  patch-1\r\npatch-2  ")]
    public void OptionalStringCapturePreservesWhitespaceAndEmptyValuesExactly(string text)
    {
        var editor = new CompanionInputEditor(
            Definition(BuiltInType.String) with { Required = false, IsMultiline = true }, static () => { })
        {
            Text = text
        };

        CompanionValue captured = editor.Capture();

        Assert.That(captured.Value.TryGetValue(out string value), Is.True);
        Assert.That(value, Is.EqualTo(text));
        Assert.That(captured.Value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.String));
        Assert.That(captured.Name, Is.EqualTo("field"));
        Assert.That(editor.Definition.IsMultiline, Is.True);
    }

    [TestCase(65535, true)]
    [TestCase(65536, true)]
    [TestCase(65537, false)]
    public void ScalarTextEnforcesTheExactEditorLengthLimit(int length, bool accepted)
    {
        string text = new('x', length);
        var editor = new CompanionInputEditor(Definition(BuiltInType.String), static () => { }) { Text = text };

        if (accepted)
        {
            CompanionValue captured = editor.Capture();
            Assert.That(captured.Value.TryGetValue(out string value), Is.True);
            Assert.That(value, Is.EqualTo(text));
        }
        else
        {
            Assert.That(() => editor.Capture(), Throws.ArgumentException);
        }
        Assert.That(editor.Text, Has.Length.EqualTo(length));
        Assert.That(editor.Checked, Is.False);
    }

    [Test]
    public void GeneratedPropertiesNotifyOnlyOnActualEditsAndCaptureDoesNotNotify()
    {
        int changes = 0;
        var editor = new CompanionInputEditor(Definition(BuiltInType.Boolean), () => changes++);

        editor.Text = "first";
        editor.Text = "first";
        editor.Checked = false;
        editor.Checked = true;
        editor.Checked = true;
        editor.Text = string.Empty;
        CompanionValue captured = editor.Capture();

        Assert.That(changes, Is.EqualTo(3));
        Assert.That(captured.Value.TryGetValue(out bool value), Is.True);
        Assert.That(value, Is.True);
        Assert.That(editor.Text, Is.Empty);
    }

    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    [TestCase(true, true, true)]
    public void FilePickerCommandIsAvailableOnlyForAFileFieldWithAPicker(
        bool fileSource, bool hasPicker, bool expected)
    {
        int picks = 0;
        Func<Task<string?>>? picker = hasPicker ? () =>
        {
            picks++;
            return Task.FromResult<string?>(@"D:\not-opened.pkg");
        } : null;
        var editor = new CompanionInputEditor(
            Definition(BuiltInType.String) with { IsFileSource = fileSource }, static () => { }, picker);

        Assert.That(editor.PickFileCommand.CanExecute(null), Is.EqualTo(expected));
        Assert.That(picks, Is.Zero);
        Assert.That(editor.Text, Is.Empty);
    }

    [TestCase(null, false)]
    [TestCase(@"D:\selected\package with spaces.pkg", true)]
    public async Task FilePickerUpdatesOnlyASelectedPathAndNotifiesOneChangeAsync(string? selected, bool changed)
    {
        int picks = 0;
        int changes = 0;
        var editor = new CompanionInputEditor(
            Definition(BuiltInType.String) with { IsFileSource = true }, () => changes++, () =>
            {
                picks++;
                return Task.FromResult(selected);
            })
        {
            Text = @"D:\previous.pkg"
        };

        await editor.PickFileCommand.ExecuteAsync(null).ConfigureAwait(false);

        Assert.That(picks, Is.EqualTo(1));
        Assert.That(changes, Is.EqualTo(changed ? 2 : 1));
        Assert.That(editor.Text, Is.EqualTo(selected ?? @"D:\previous.pkg"));
        Assert.That(editor.Capture().Value.TryGetValue(out string captured), Is.True);
        Assert.That(captured, Is.EqualTo(selected ?? @"D:\previous.pkg"));
    }

    [Test]
    public async Task MissingPickerFailsExplicitlyEvenIfItsDisabledCommandIsInvokedDirectlyAsync()
    {
        var editor = new CompanionInputEditor(
            Definition(BuiltInType.String) with { IsFileSource = true }, static () => { })
        {
            Text = "unchanged"
        };

        await Assert.ThatAsync(
            () => editor.PickFileCommand.ExecuteAsync(null),
            Throws.InvalidOperationException).ConfigureAwait(false);

        Assert.That(editor.Text, Is.EqualTo("unchanged"));
        Assert.That(editor.PickFileCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task PickerFailureIsPropagatedWithoutReplacingTheExistingPathAsync()
    {
        int changes = 0;
        var editor = new CompanionInputEditor(
            Definition(BuiltInType.String) with { IsFileSource = true }, () => changes++,
            static () => Task.FromException<string?>(new IOException("File selection failed.")))
        {
            Text = "previous selection"
        };

        await Assert.ThatAsync(
            () => editor.PickFileCommand.ExecuteAsync(null),
            Throws.TypeOf<IOException>().With.Message.EqualTo("File selection failed.")).ConfigureAwait(false);

        Assert.That(editor.Text, Is.EqualTo("previous selection"));
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public async Task PluginPreparesTheGeneratedTypedFormAndRunsOnlyItsReviewedRequestAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var context = new CompanionTypedTaskTestContext(host.Telemetry, CompanionOperationSafety.SampleMutation);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                document.OperationInput = "raw input must not be dispatched";
                document.ConfirmLocalSample = true;

                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                CompanionOperationDraft draft = document.PreparedOperation ??
                    throw new AssertionException("The typed form was not prepared.");
                Assert.That(draft.TaskInput, Is.SameAs(context.PreparedInput));
                Assert.That(draft.Input, Is.Null);
                Assert.That(document.InputFields.Select(field => field.Definition.Name),
                    Is.EqualTo(s_inputNames));
                Assert.That(context.CapturedInputs[2].Value.TryGetValue(out uint count), Is.True);
                Assert.That(count, Is.EqualTo(19u));
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.PreparationSummary, Does.Contain("Reviewed typed settings."));
                context.VerifyCalls(1, 0);
                bool clearedBeforeDispatch = false;
                context.Provider.Setup(value => value.ExecutePreparedAsync(
                        It.IsAny<CompanionContext>(), context.Target, "typed", context.PreparedInput,
                        It.IsAny<IProgress<CompanionTaskProgress>?>(), It.IsAny<CancellationToken>()))
                    .Callback(() => clearedBeforeDispatch =
                        document.PreparedOperation is null && !document.ConfirmLocalSample)
                    .Returns(() => ValueTask.FromResult(context.Result));
                document.ConfirmLocalSample = true;

                await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                Assert.That(clearedBeforeDispatch, Is.True);
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.Status, Is.EqualTo("Typed operation completed."));
                Assert.That(document.Values.Single().Value.TryGetValue(out uint accepted), Is.True);
                Assert.That(accepted, Is.EqualTo(19u));
                context.VerifyCalls(1, 1);
            }
        }
    }

    [TestCase("text")]
    [TestCase("checkbox")]
    [TestCase("numeric")]
    [TestCase("operation-reset")]
    [TestCase("target-reset")]
    public async Task TypedFieldEditsAndFormResetsRevokePreparationAndSampleConfirmationAsync(string change)
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var context = new CompanionTypedTaskTestContext(host.Telemetry, CompanionOperationSafety.SampleMutation);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                CompanionOperationDraft draft = document.PreparedOperation ??
                    throw new AssertionException("The typed form was not prepared.");
                document.ConfirmLocalSample = true;
                switch (change)
                {
                    case "text":
                        document.InputFields[0].Text = "changed label";
                        break;
                    case "checkbox":
                        document.InputFields[1].Checked = false;
                        break;
                    case "numeric":
                        document.InputFields[2].Text = "20";
                        break;
                    case "operation-reset":
                        document.SelectedOperation = null;
                        break;
                    case "target-reset":
                        document.SelectedTarget = null;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change));
                }

                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.PreparationSummary, Does.StartWith("Prepare the selected task"));
                if (change.EndsWith("-reset", StringComparison.Ordinal))
                {
                    Assert.That(document.InputFields, Is.Empty);
                }
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteTaskAsync(draft, true, null),
                    Throws.InvalidOperationException).ConfigureAwait(false);
                context.VerifyCalls(1, 0);
            }
        }
    }

    [Test]
    public async Task InvalidScalarTextFailsPreparationWithoutProviderWorkOrRetainingConfirmationAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var context = new CompanionTypedTaskTestContext(host.Telemetry, CompanionOperationSafety.SampleMutation);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                document.InputFields[2].Text = "-1";
                document.ConfirmLocalSample = true;

                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                Assert.That(document.Status, Does.Contain("Prepare task:").And.Contain("Count"));
                Assert.That(document.InputFields[2].Text, Is.EqualTo("-1"));
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.IsBusy, Is.False);
                Assert.That(document.Values.Single().Value.TryGetValue(out uint original), Is.True);
                Assert.That(original, Is.EqualTo(7u));
                context.VerifyCalls(0, 0);
                context.VerifyInspections(1);
            }
        }
    }

    [Test]
    public async Task TypedValuesReviewAndConfirmationAreNotSerializedAndCannotBeRestoredAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var context = new CompanionTypedTaskTestContext(host.Telemetry, CompanionOperationSafety.SampleMutation);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                CompanionOperationDraft draft = document.PreparedOperation ??
                    throw new AssertionException("The typed form was not prepared.");
                document.ConfirmLocalSample = true;

                JsonElement captured = document.CaptureState();

                Assert.That(captured.EnumerateObject().Select(property => property.Name),
                    Is.EquivalentTo(s_savedProperties));
                Assert.That(captured.GetProperty("Version").GetInt32(), Is.EqualTo(1));
                Assert.That(captured.GetProperty("ProviderId").GetString(), Is.EqualTo("typed-provider"));
                Assert.That(captured.GetRawText(),
                    Does.Not.Contain("unreviewed-label").And.Not.Contain("Reviewed typed settings"));
                Assert.That(document.PreparedOperation, Is.SameAs(draft));
                Assert.That(document.ConfirmLocalSample, Is.True);
                using JsonDocument injected = JsonDocument.Parse("""
                    {
                      "Version": 1,
                      "ProviderId": "typed-provider",
                      "TargetId": "nsu=urn:typed;i=1234",
                      "InputFields": [{ "Name": "count", "Text": "999" }],
                      "TaskInput": { "Review": "forged review" },
                      "PreparedOperation": { "Operation": "typed" },
                      "ConfirmLocalSample": true
                    }
                    """);

                await document.RestoreStateAsync(injected.RootElement).ConfigureAwait(false);

                Assert.That(document.InputFields, Is.Empty);
                Assert.That(document.SelectedOperation, Is.Null);
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.OperationInput, Is.Empty);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.Status, Does.Contain("no task or workload"));
                Assert.That(document.CaptureState().GetProperty("TargetId").GetString(),
                    Is.EqualTo("nsu=urn:typed;i=1234"));
                context.VerifyCalls(1, 0);
                context.VerifyInspections(2);
                context.Provider.Verify(value => value.DiscoverAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()), Times.Once);
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteTaskAsync(draft, true, null),
                    Throws.InvalidOperationException).ConfigureAwait(false);

                await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(false);
                await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(false);

                Assert.That(document.InputFields, Has.Count.EqualTo(5));
                Assert.That(document.InputFields.All(field => field.Text.Length == 0 && !field.Checked), Is.True);
                Assert.That(document.PreparedOperation, Is.Null);
                context.VerifyCalls(1, 0);
            }
        }
    }

    [TestCase("cancel")]
    [TestCase("disconnect")]
    public async Task DocumentLifecycleResetClearsTheTypedFormAndItsPreparationAsync(string reset)
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var context = new CompanionTypedTaskTestContext(host.Telemetry);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                document.ConfirmLocalSample = true;

                if (reset == "cancel")
                {
                    await document.CancelCommand.ExecuteAsync(null).ConfigureAwait(false);
                }
                else
                {
                    await document.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);
                }

                Assert.That(document.InputFields, Is.Empty);
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.Targets, Is.Empty);
                Assert.That(document.Operations, Is.Empty);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                context.VerifyCalls(1, 0);
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EditingATypedFieldDuringPreparationCannotPublishTheOldReviewAsync(bool checkbox)
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var context = new CompanionTypedTaskTestContext(host.Telemetry);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<CompanionTaskInput>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                context.PrepareHandler = (_, _) =>
                {
                    entered.TrySetResult();
                    return new ValueTask<CompanionTaskInput>(release.Task);
                };
                Task preparing = document.PrepareTaskCommand.ExecuteAsync(null);
                try
                {
                    await Task.WhenAny(entered.Task, preparing).ConfigureAwait(false);
                    Assert.That(entered.Task.IsCompletedSuccessfully, Is.True);
                    if (checkbox)
                    {
                        document.InputFields[1].Checked = false;
                    }
                    else
                    {
                        document.InputFields[0].Text = "replacement label";
                    }
                }
                finally
                {
                    release.TrySetResult(context.PreparedInput);
                }
                await preparing.ConfigureAwait(false);

                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.Status, Does.Contain("canceled"));
                Assert.That(context.CapturedInputs[0].Value.TryGetValue(out string original), Is.True);
                Assert.That(original, Is.EqualTo("unreviewed-label"));
                Assert.That(context.CapturedInputs[1].Value.TryGetValue(out bool wasChecked), Is.True);
                Assert.That(wasChecked, Is.True);
                context.VerifyCalls(1, 0);
            }
        }
    }

    [Test]
    public async Task EditingTheFormDuringRunCannotRedirectThePreparedRequestOrPublishItsOldResultAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var context = new CompanionTypedTaskTestContext(host.Telemetry);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<CompanionOperationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                context.Provider.Setup(value => value.ExecutePreparedAsync(
                        It.IsAny<CompanionContext>(), context.Target, "typed", context.PreparedInput,
                        It.IsAny<IProgress<CompanionTaskProgress>?>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        entered.TrySetResult();
                        return new ValueTask<CompanionOperationResult>(release.Task);
                    });
                Task running = document.RunTaskCommand.ExecuteAsync(null);
                try
                {
                    await Task.WhenAny(entered.Task, running).ConfigureAwait(false);
                    Assert.That(entered.Task.IsCompletedSuccessfully, Is.True);
                    document.InputFields[2].Text = "999";
                }
                finally
                {
                    release.TrySetResult(context.Result);
                }
                await running.ConfigureAwait(false);

                Assert.That(document.InputFields[2].Text, Is.EqualTo("999"));
                Assert.That(context.CapturedInputs[2].Value.TryGetValue(out uint requested), Is.True);
                Assert.That(requested, Is.EqualTo(19u));
                Assert.That(document.Values.Single().Value.TryGetValue(out uint displayed), Is.True);
                Assert.That(displayed, Is.EqualTo(7u));
                Assert.That(document.Status, Is.Not.EqualTo("Typed operation completed."));
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.IsBusy, Is.False);
                context.VerifyCalls(1, 1);
            }
        }
    }

    [Test]
    [Explicit("Requires an interactive desktop; no network or certificate stores are accessed.")]
    [Category("CompanionDialogProbe")]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public async Task CompiledViewTemplateBindsRealTypedEditorsAndHidesTheRawInputAsync()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
            Application.Current!.Styles.Add(new FluentTheme());
        }
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var context = new CompanionTypedTaskTestContext(host.Telemetry);
            var document = new CompanionPlugin(host.Host, context.Workspace)
            {
                SelectedOperation = new CompanionOperation("form", "Form", CompanionOperationSafety.ReadOnly)
                {
                    Inputs =
                    [
                        new("enabled", "Enabled", BuiltInType.Boolean, "Enable"),
                        new("patches", "Patches", BuiltInType.String, "One per line", false, IsMultiline: true),
                        new("path", "Package file", BuiltInType.String, "Local path", IsFileSource: true)
                    ]
                }
            };
            try
            {
                var view = new CompanionView { DataContext = document };
                ItemsControl fields = view.GetLogicalDescendants().OfType<ItemsControl>()
                    .Single(control => ReferenceEquals(control.ItemsSource, document.InputFields));
                TextBox raw = view.GetLogicalDescendants().OfType<TextBox>().Single(control =>
                    AutomationProperties.GetName(control) == "Operation input or destination");
                Assert.That(raw.IsVisible, Is.False);
                Assert.That(fields.Items, Has.Count.EqualTo(3));

                Control boolean = BuildField(fields, document.InputFields[0]);
                CheckBox toggle = boolean.GetLogicalDescendants().OfType<CheckBox>().Single();
                Assert.That(toggle.IsVisible, Is.True);
                Assert.That(boolean.GetLogicalDescendants().OfType<TextBox>().Single().IsVisible, Is.False);
                toggle.IsChecked = true;
                Assert.That(document.InputFields[0].Capture().Value.TryGetValue(out bool enabled), Is.True);
                Assert.That(enabled, Is.True);

                Control multiline = BuildField(fields, document.InputFields[1]);
                TextBox patches = multiline.GetLogicalDescendants().OfType<TextBox>().Single();
                Assert.That(patches.IsVisible, Is.True);
                Assert.That(patches.AcceptsReturn, Is.True);
                Assert.That(patches.MaxLength, Is.EqualTo(65536));
                patches.Text = "patch-1\npatch-2";
                Assert.That(document.InputFields[1].Capture().Value.TryGetValue(out string captured), Is.True);
                Assert.That(captured, Is.EqualTo("patch-1\npatch-2"));

                Control file = BuildField(fields, document.InputFields[2]);
                Button picker = file.GetLogicalDescendants().OfType<Button>().Single(control =>
                    ReferenceEquals(control.Command, document.InputFields[2].PickFileCommand));
                Assert.That(picker.IsVisible, Is.True);
                Assert.That(picker.Command, Is.SameAs(document.InputFields[2].PickFileCommand));
                Assert.That(multiline.GetLogicalDescendants().OfType<Button>().Single(control =>
                    ReferenceEquals(control.Command, document.InputFields[1].PickFileCommand)).IsVisible, Is.False);
                context.VerifyCalls(0, 0);
            }
            finally
            {
                Task disposal = document.DisposeAsync().AsTask();
                PumpUntil(disposal);
                await disposal.ConfigureAwait(true);
            }
        }
    }

    private static void PumpUntil(Task task)
    {
        if (task.IsCompleted)
        {
            return;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        {
            if (task.IsCompleted)
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
        Assert.That(task.IsCompleted, Is.True, "The owned companion document did not finish cleanup.");
    }

    private static CompanionInputDefinition Definition(BuiltInType type)
    {
        return new CompanionInputDefinition("field", "Test field", type, "Input hint");
    }

    private static async Task InitializeDocumentAsync(
        CompanionPlugin document, CompanionTypedTaskTestContext context)
    {
        await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
        document.IsOffline = false;
        await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(false);
        await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(false);
        document.InputFields[0].Text = "unreviewed-label";
        document.InputFields[1].Checked = true;
        document.InputFields[2].Text = "19";
        document.InputFields[3].Text = "-3";
        document.InputFields[4].Text = "2.5";
    }

    private static Control BuildField(ItemsControl fields, CompanionInputEditor editor)
    {
        Control control = fields.ItemTemplate?.Build(editor) ??
            throw new AssertionException("The compiled typed-input template was not available.");
        control.DataContext = editor;
        return control;
    }

    private static readonly string[] s_inputNames = ["label", "enabled", "count", "delta", "gain"];
    private static readonly string[] s_savedProperties = ["Version", "ProviderId", "TargetId"];
}
