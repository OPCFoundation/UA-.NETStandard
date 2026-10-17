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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Tests.Observe;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class CompanionPreflightTests
{
    [Test]
    public async Task PrepareReviewsWithoutExecutionAndRunConsumesExplicitSampleConfirmationAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            CompanionPreparedOperationTestContext context = CreateContext(host);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                document.ConfirmLocalSample = true;

                Assert.That(document.PrepareTaskCommand.CanExecute(null), Is.True);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                Assert.That(document.PreparedOperation?.Input, Is.EqualTo("recipe=7"));
                Assert.That(document.PreparedOperation?.Operation.Id, Is.EqualTo("apply"));
                Assert.That(document.PreparedOperation?.Operation.Safety,
                    Is.EqualTo(CompanionOperationSafety.SampleMutation));
                Assert.That(document.PreparationSummary,
                    Does.Contain("Sample device").And.Contain("SampleMutation").And.Contain("localhost"));
                Assert.That(document.PreparationSummary, Does.Not.Contain("recipe=7"));
                Assert.That(document.Status, Does.Contain("Prepared for one execution"));
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                context.VerifyInspectionCount(2);
                context.VerifyExecutionCount(0);

                bool preparationClearedBeforeDispatch = false;
                bool confirmationClearedBeforeDispatch = false;
                context.Provider.Setup(item => item.ExecuteAsync(
                        It.IsAny<CompanionContext>(), context.Target, "apply",
                        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .Callback(() =>
                    {
                        preparationClearedBeforeDispatch = document.PreparedOperation is null;
                        confirmationClearedBeforeDispatch = !document.ConfirmLocalSample;
                    })
                    .Returns(() => ValueTask.FromResult(context.Result));
                document.ConfirmLocalSample = true;
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);

                await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                Assert.That(preparationClearedBeforeDispatch, Is.True);
                Assert.That(confirmationClearedBeforeDispatch, Is.True);
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.Status, Is.EqualTo("Operation complete."));
                Assert.That(document.Values, Has.Count.EqualTo(1));
                Assert.That(document.Values[0].Name, Is.EqualTo("Processed"));
                Assert.That(document.Values[0].Value.TryGetValue(out uint processed), Is.True);
                Assert.That(processed, Is.EqualTo(17u));
                Assert.That(document.IsBusy, Is.False);
                context.VerifyInspectionCount(3);
                context.VerifyExecution("recipe=7");
                context.VerifyExecutionCount(1);
            }
        }
    }

    [TestCase("read-only")]
    [TestCase("local-file")]
    public async Task PreparedNonSampleOperationsRunWithoutSampleConfirmationAsync(string kind)
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            CompanionOperationSafety safety = kind == "local-file"
                ? CompanionOperationSafety.LocalFile
                : CompanionOperationSafety.ReadOnly;
            CompanionPreparedOperationTestContext context = CreateContext(host, safety);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                document.OperationInput = kind == "local-file" ? @"D:\exports\recipe report.json" : string.Empty;
                string expectedInput = document.OperationInput;
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);
                context.VerifyExecutionCount(0);
                await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                Assert.That(document.Status, Is.EqualTo("Operation complete."));
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                context.VerifyInspectionCount(3);
                context.VerifyExecution(expectedInput);
                context.VerifyExecutionCount(1);
            }
        }
    }

    [Test]
    public async Task RunWithoutPreparationIsDisabledAndCannotDispatchAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            CompanionPreparedOperationTestContext context = CreateContext(host);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                document.ConfirmLocalSample = true;

                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                Assert.That(document.Status, Does.Contain("Prepare the selected operation"));
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.IsBusy, Is.False);
                Assert.That(document.Values[0].Name, Is.EqualTo("Temperature"));
                Assert.That(document.Values[0].Value.TryGetValue(out double temperature), Is.True);
                Assert.That(temperature, Is.EqualTo(42.5));
                context.VerifyInspectionCount(1);
                context.VerifyExecutionCount(0);
            }
        }
    }

    [TestCase("input")]
    [TestCase("operation")]
    [TestCase("target")]
    [TestCase("provider")]
    public async Task SelectionChangesClearPreparationAndConfirmationAsync(string change)
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            CompanionPreparedOperationTestContext context = CreateContext(host);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                document.ConfirmLocalSample = true;
                Assert.That(document.PreparedOperation?.Input, Is.EqualTo("recipe=7"));
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);

                ChangeSelection(document, change);

                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.PreparationSummary, Does.StartWith("Prepare the selected task"));
                context.VerifyInspectionCount(2);
                context.VerifyExecutionCount(0);
            }
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task DiscoverAndInspectClearPreparationBeforeProviderWorkEvenOnFailureAsync(
        bool inspect,
        bool fail)
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            CompanionPreparedOperationTestContext context = CreateContext(host);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                document.ConfirmLocalSample = true;
                Assert.That(document.PreparedOperation?.Input, Is.EqualTo("recipe=7"));
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);
                bool preparationClearedBeforeProvider = false;
                bool confirmationClearedBeforeProvider = false;
                Action observeInvalidation = () =>
                {
                    preparationClearedBeforeProvider = document.PreparedOperation is null;
                    confirmationClearedBeforeProvider = !document.ConfirmLocalSample;
                };
                if (inspect)
                {
                    context.Provider.Setup(item => item.InspectAsync(
                            It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                        .Callback(observeInvalidation)
                        .Returns(() => fail
                            ? ValueTask.FromException<CompanionInspection>(
                                new ServiceResultException(StatusCodes.BadUserAccessDenied))
                            : ValueTask.FromResult(context.Inspection));
                    await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(false);
                }
                else
                {
                    context.Provider.Setup(item => item.DiscoverAsync(
                            It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
                        .Callback(observeInvalidation)
                        .Returns(() => fail
                            ? ValueTask.FromException<ArrayOf<CompanionTarget>>(
                                new ServiceResultException(StatusCodes.BadUserAccessDenied))
                            : ValueTask.FromResult(context.Targets));
                    await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(false);
                }

                Assert.That(preparationClearedBeforeProvider, Is.True);
                Assert.That(confirmationClearedBeforeProvider, Is.True);
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.IsBusy, Is.False);
                if (fail)
                {
                    Assert.That(document.Status, Does.StartWith(inspect ? "Inspect:" : "Discover:")
                        .And.Contain("BadUserAccessDenied"));
                }
                else
                {
                    Assert.That(document.Status,
                        Is.EqualTo(inspect ? "Inspection complete." : "Found 2 typed instances."));
                }
                context.Provider.Verify(
                    item => item.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()),
                    Times.Exactly(inspect ? 1 : 2));
                context.VerifyInspectionCount(inspect ? 3 : 2);
                context.VerifyExecutionCount(0);
            }
        }
    }

    [TestCase("input")]
    [TestCase("operation")]
    [TestCase("target")]
    [TestCase("provider")]
    public async Task SelectionChangesDuringPreparationCannotPublishAStaleDraftAsync(string change)
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            CompanionPreparedOperationTestContext context = CreateContext(host);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.Provider.Setup(item => item.InspectAsync(
                        It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                    .Returns(async (CompanionContext _, CompanionTarget _, CancellationToken token) =>
                    {
                        started.TrySetResult();
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                        return context.Inspection;
                    });
                Task preparation = document.PrepareTaskCommand.ExecuteAsync(null);
                try
                {
                    await Task.WhenAny(started.Task, preparation).ConfigureAwait(false);
                    Assert.That(started.Task.IsCompletedSuccessfully, Is.True);
                    document.ConfirmLocalSample = true;

                    ChangeSelection(document, change);

                    Assert.That(document.ConfirmLocalSample, Is.False);
                }
                finally
                {
                    release.TrySetResult();
                }

                await preparation.ConfigureAwait(false);

                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.IsBusy, Is.False);
                Assert.That(document.PreparationSummary, Does.StartWith("Prepare the selected task"));
                context.VerifyInspectionCount(2);
                context.VerifyExecutionCount(0);
            }
        }
    }

    [Test]
    public async Task EditingInputDuringRunCannotRedirectTheRequestOrPublishItsOldResultAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            CompanionPreparedOperationTestContext context = CreateContext(host, CompanionOperationSafety.ReadOnly);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.Provider.Setup(item => item.InspectAsync(
                        It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                    .Returns(async (CompanionContext _, CompanionTarget _, CancellationToken token) =>
                    {
                        started.TrySetResult();
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                        return context.Inspection;
                    });
                Task execution = document.RunTaskCommand.ExecuteAsync(null);
                try
                {
                    await Task.WhenAny(started.Task, execution).ConfigureAwait(false);
                    Assert.That(started.Task.IsCompletedSuccessfully, Is.True);
                    document.OperationInput = "recipe=9";
                }
                finally
                {
                    release.TrySetResult();
                }

                await execution.ConfigureAwait(false);

                Assert.That(document.OperationInput, Is.EqualTo("recipe=9"));
                Assert.That(document.Values, Has.Count.EqualTo(1));
                Assert.That(document.Values[0].Name, Is.EqualTo("Temperature"));
                Assert.That(document.Values[0].Value.TryGetValue(out double temperature), Is.True);
                Assert.That(temperature, Is.EqualTo(42.5));
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.IsBusy, Is.False);
                context.VerifyInspectionCount(3);
                context.VerifyExecution("recipe=7");
                context.VerifyExecutionCount(1);
            }
        }
    }

    [Test]
    public async Task FailedPreparationClearsExistingConfirmationAndReportsTheRefusalAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            CompanionPreparedOperationTestContext context = CreateContext(host);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                document.ConfirmLocalSample = true;
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);
                context.Inspection = context.Inspection with { Operations = [] };

                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                Assert.That(document.Status, Does.StartWith("Prepare task:").And.Contain("changed"));
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.IsBusy, Is.False);
                context.VerifyInspectionCount(3);
                context.VerifyExecutionCount(0);
            }
        }
    }

    [Test]
    public async Task ProviderFailureRequiresNewPreparationAndNewConfirmationAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            CompanionPreparedOperationTestContext context = CreateContext(host);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                document.ConfirmLocalSample = true;
                context.Provider.SetupSequence(item => item.ExecuteAsync(
                        It.IsAny<CompanionContext>(), context.Target, "apply",
                        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .Returns(() => ValueTask.FromException<CompanionOperationResult>(
                        new ServiceResultException(StatusCodes.BadUserAccessDenied)))
                    .Returns(() => ValueTask.FromResult(context.Result));

                await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                Assert.That(document.Status, Does.StartWith("Run task:").And.Contain("BadUserAccessDenied"));
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.IsBusy, Is.False);
                context.VerifyExecutionCount(1);
                await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                Assert.That(document.Status, Does.Contain("Prepare the selected operation"));
                context.VerifyInspectionCount(3);
                context.VerifyExecutionCount(1);

                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                document.ConfirmLocalSample = true;
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);
                await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                Assert.That(document.Status, Is.EqualTo("Operation complete."));
                Assert.That(document.Values, Has.Count.EqualTo(1));
                Assert.That(document.Values[0].Value.TryGetValue(out uint processed), Is.True);
                Assert.That(processed, Is.EqualTo(17u));
                context.VerifyInspectionCount(5);
                context.Provider.Verify(item => item.ExecuteAsync(
                        It.Is<CompanionContext>(value => ReferenceEquals(value.Session, context.Session.Object)),
                        context.Target, "apply", "recipe=7", It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
                context.VerifyExecutionCount(2);
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelAndConnectionChangesDiscardPreparedStateAsync(bool connectionChanged)
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            CompanionPreparedOperationTestContext context = CreateContext(host);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                document.ConfirmLocalSample = true;
                CompanionOperationDraft draft = document.PreparedOperation ??
                    throw new InvalidOperationException("Expected a prepared task before cancellation.");

                if (connectionChanged)
                {
                    await document.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await document.CancelCommand.ExecuteAsync(null).ConfigureAwait(false);
                }

                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.SelectedTarget, Is.Null);
                Assert.That(document.SelectedOperation, Is.Null);
                Assert.That(document.Targets, Is.Empty);
                Assert.That(document.Values, Is.Empty);
                Assert.That(document.Operations, Is.Empty);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.Status, Does.StartWith(connectionChanged ? "Connect the primary" : "Stopped."));
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteAsync(draft, true),
                    Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
                context.VerifyInspectionCount(2);
                context.VerifyExecutionCount(0);
            }
        }
    }

    [Test]
    public async Task CancelingARunningCommandReportsCancellationAndCannotReplayAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            CompanionPreparedOperationTestContext context = CreateContext(host);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                document.ConfirmLocalSample = true;
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<CompanionOperationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                context.Provider.Setup(item => item.ExecuteAsync(
                        It.IsAny<CompanionContext>(), context.Target, "apply",
                        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .Returns(async (
                        CompanionContext _, CompanionTarget _, string _, string? _, CancellationToken token) =>
                    {
                        started.TrySetResult();
                        return await release.Task.WaitAsync(token).ConfigureAwait(false);
                    });
                Task execution = document.RunTaskCommand.ExecuteAsync(null);
                await Task.WhenAny(started.Task, execution).ConfigureAwait(false);
                Assert.That(started.Task.IsCompletedSuccessfully, Is.True);

                document.RunTaskCommand.Cancel();
                await execution.ConfigureAwait(false);

                Assert.That(document.Status, Is.EqualTo("Run task canceled."));
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.IsBusy, Is.False);
                Assert.That(document.Values[0].Name, Is.EqualTo("Temperature"));
                await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                Assert.That(document.Status, Does.Contain("Prepare the selected operation"));
                context.VerifyInspectionCount(3);
                context.VerifyExecution("recipe=7");
                context.VerifyExecutionCount(1);
            }
        }
    }

    private static CompanionPreparedOperationTestContext CreateContext(
        ObserveTestHost host,
        CompanionOperationSafety safety = CompanionOperationSafety.SampleMutation)
    {
        var otherProvider = new Mock<ICompanionProvider>(MockBehavior.Strict);
        otherProvider.SetupGet(item => item.Descriptor)
            .Returns(new CompanionDescriptor("other", "Other model", "urn:ualens:other", "Test model"));
        var context = new CompanionPreparedOperationTestContext(
            safety, telemetry: host.Telemetry, additionalProviders: [otherProvider.Object]);
        context.Targets =
        [
            context.Target,
            context.Target with { NodeId = new NodeId(5678u, 2), DisplayName = "Other device" }
        ];
        context.Inspection = context.Inspection with
        {
            Operations =
            [
                context.Operation,
                new CompanionOperation("refresh", "Refresh", CompanionOperationSafety.ReadOnly)
            ]
        };
        return context;
    }

    private static async Task InitializeDocumentAsync(
        CompanionPlugin document,
        CompanionPreparedOperationTestContext context)
    {
        await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
        document.IsOffline = false;
        await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(false);
        await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(false);
        document.OperationInput = "recipe=7";
    }

    private static void ChangeSelection(CompanionPlugin document, string change)
    {
        switch (change)
        {
            case "input":
                document.OperationInput = "recipe=9";
                break;
            case "operation":
                document.SelectedOperation = document.Operations[1];
                break;
            case "target":
                document.SelectedTarget = document.Targets[1];
                break;
            case "provider":
                document.SelectedProvider = document.Providers[1];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown selection change.");
        }
    }
}
