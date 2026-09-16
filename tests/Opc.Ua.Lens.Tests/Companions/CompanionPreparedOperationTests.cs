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
using Opc.Ua.Client;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class CompanionPreparedOperationTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  batch=7\r\nrate=2  ")]
    public async Task PreparedExecutionUsesTheExactSelectedRequestOnlyOnceAsync(string? input)
    {
        var context = new CompanionPreparedOperationTestContext(
            endpoint: "opc.tcp://remote.example:4840/Server?profile=inspection");
        await using (context.ConfigureAwait(false))
        {
            context.Targets =
            [
                context.Target with { NodeId = new NodeId(4321u, 2), DisplayName = "Another device" },
                context.Target
            ];
            context.Inspection = context.Inspection with
            {
                Operations =
                [
                    new CompanionOperation("refresh", "Refresh", CompanionOperationSafety.ReadOnly),
                    context.Operation
                ]
            };
            await context.InitializeAsync().ConfigureAwait(false);
            context.Inspection = context.Inspection with { Operations = [context.Operation with { }] };

            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", input).ConfigureAwait(false);

            Assert.That(draft.Target.NodeId, Is.EqualTo(new NodeId(1234u, 2)));
            Assert.That(draft.Target.ProviderId, Is.EqualTo("sample"));
            Assert.That(draft.Target.DisplayName, Is.EqualTo("Sample device"));
            Assert.That(draft.Operation.Id, Is.EqualTo("apply"));
            Assert.That(draft.Operation.DisplayName, Is.EqualTo("Apply recipe"));
            Assert.That(draft.Operation.Safety, Is.EqualTo(CompanionOperationSafety.ReadOnly));
            Assert.That(draft.Operation.InputHint, Is.EqualTo("Recipe input"));
            Assert.That(draft.Input, Is.EqualTo(input));
            Assert.That(draft.EndpointUrl, Is.EqualTo("opc.tcp://remote.example:4840/Server"));
            Assert.That(draft.ExpiresAt, Is.EqualTo(context.UtcNow.AddMinutes(5)));
            Assert.That(draft.Summary, Does.Contain("Sample device").And.Contain("ReadOnly"));
            Assert.That(draft.Summary, Does.Not.Contain("profile=inspection"));
            context.VerifyInspectionCount(2);
            context.VerifyExecutionCount(0);

            CompanionOperationResult result = await context.Workspace.ExecuteAsync(draft, false)
                .ConfigureAwait(false);

            Assert.That(result.Summary, Is.EqualTo("Operation complete."));
            Assert.That(result.Values, Has.Count.EqualTo(1));
            Assert.That(result.Values[0].Name, Is.EqualTo("Processed"));
            Assert.That(result.Values[0].Value.TryGetValue(out uint processed), Is.True);
            Assert.That(processed, Is.EqualTo(17u));
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
            context.VerifyInspectionCount(3);
            context.VerifyExecution(input);
            context.VerifyExecutionCount(1);
        }
    }

    [Test]
    public async Task PreparationSummaryHidesRawInputAndEndpointQueryWithoutChangingTheRequestAsync()
    {
        const string input = "raw-input-marker";
        var context = new CompanionPreparedOperationTestContext(
            endpoint: "opc.tcp://localhost:4840/Sample?profile=hidden-query-marker");
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);

            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", input).ConfigureAwait(false);

            Assert.That(draft.EndpointUrl, Is.EqualTo("opc.tcp://localhost:4840/Sample"));
            Assert.That(draft.Summary, Does.Contain("Apply recipe").And.Contain("Sample device"));
            Assert.That(draft.Summary, Does.Contain("Endpoint: opc.tcp://localhost:4840/Sample"));
            Assert.That(draft.Summary, Does.Contain("Effect: ReadOnly"));
            Assert.That(draft.Summary, Does.Not.Contain(input));
            Assert.That(draft.Summary, Does.Not.Contain("hidden-query-marker"));
            Assert.That(draft.Summary, Does.Not.Contain("?"));
            Assert.That(draft.Input, Is.EqualTo(input));
            context.VerifyInspectionCount(2);
            context.VerifyExecutionCount(0);

            CompanionOperationResult result = await context.Workspace.ExecuteAsync(draft, false)
                .ConfigureAwait(false);

            Assert.That(result.Summary, Is.EqualTo("Operation complete."));
            context.VerifyInspectionCount(3);
            context.VerifyExecution(input);
            context.VerifyExecutionCount(1);
        }
    }

    [Test]
    public async Task PreparationRequiresAnOperationFromTheCurrentTargetInspectionAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
            await context.Workspace.DiscoverAsync("sample").ConfigureAwait(false);

            await Assert.ThatAsync(
                () => context.Workspace.PrepareAsync(context.Target, "apply", null),
                Throws.InvalidOperationException.With.Message.Contains("Inspect")).ConfigureAwait(false);

            await context.Workspace.InspectAsync(context.Target).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.PrepareAsync(context.Target, "unoffered", null),
                Throws.InvalidOperationException.With.Message.Contains("Inspect")).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.PrepareAsync(
                    context.Target with { NodeId = new NodeId(9999u, 2) }, "apply", null),
                Throws.InvalidOperationException.With.Message.Contains("Inspect")).ConfigureAwait(false);

            context.VerifyInspectionCount(1);
            context.VerifyExecutionCount(0);
        }
    }

    [TestCase("removed", true)]
    [TestCase("removed", false)]
    [TestCase("id", true)]
    [TestCase("id", false)]
    [TestCase("display-name", true)]
    [TestCase("display-name", false)]
    [TestCase("safety", true)]
    [TestCase("safety", false)]
    [TestCase("input-hint", true)]
    [TestCase("input-hint", false)]
    public async Task FreshInspectionRejectsRemovedOrChangedOperationsAsync(string change, bool duringPreparation)
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft? draft = duringPreparation
                ? null
                : await context.Workspace.PrepareAsync(context.Target, "apply", "recipe=7").ConfigureAwait(false);
            CompanionOperation changed = change switch
            {
                "id" => context.Operation with { Id = "replacement" },
                "display-name" => context.Operation with { DisplayName = "Apply another recipe" },
                "safety" => context.Operation with { Safety = CompanionOperationSafety.SampleMutation },
                "input-hint" => context.Operation with { InputHint = "A different input contract" },
                _ => context.Operation
            };
            context.Inspection = context.Inspection with
            {
                Operations = change == "removed" ? [] : [changed]
            };

            await Assert.ThatAsync(async () =>
            {
                if (draft is null)
                {
                    await context.Workspace.PrepareAsync(context.Target, "apply", "recipe=7").ConfigureAwait(false);
                }
                else
                {
                    await context.Workspace.ExecuteAsync(draft, true).ConfigureAwait(false);
                }
            }, Throws.InvalidOperationException.With.Message.Contains("changed")).ConfigureAwait(false);

            context.VerifyInspectionCount(duringPreparation ? 2 : 3);
            context.VerifyExecutionCount(0);
        }
    }

    [TestCase(true, false)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    [TestCase(false, true)]
    public async Task DuplicateFreshOperationIdsAreRejectedEvenWhenTheOriginalOfferRemainsAsync(
        bool duringPreparation,
        bool changeDuplicateMetadata)
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft? draft = duringPreparation
                ? null
                : await context.Workspace.PrepareAsync(context.Target, "apply", "recipe=7").ConfigureAwait(false);
            CompanionOperation duplicate = changeDuplicateMetadata
                ? context.Operation with
                {
                    DisplayName = "Ambiguous operation",
                    Safety = CompanionOperationSafety.SampleMutation
                }
                : context.Operation;
            context.Inspection = context.Inspection with { Operations = [context.Operation, duplicate] };

            await Assert.ThatAsync(async () =>
            {
                if (draft is null)
                {
                    await context.Workspace.PrepareAsync(context.Target, "apply", "recipe=7").ConfigureAwait(false);
                }
                else
                {
                    await context.Workspace.ExecuteAsync(draft, true).ConfigureAwait(false);
                }
            }, Throws.InvalidOperationException.With.Message.Contains("invalid operation")).ConfigureAwait(false);

            if (draft is not null)
            {
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteAsync(draft, true),
                    Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
            }
            context.VerifyInspectionCount(duringPreparation ? 2 : 3);
            context.VerifyExecutionCount(0);
        }
    }

    [Test]
    public async Task ForeignWorkspaceDraftCannotReplaceAnOwnedPreparationAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft owned = await context.Workspace.PrepareAsync(
                context.Target, "apply", "owned").ConfigureAwait(false);
            var other = new CompanionWorkspace(
                [context.Provider.Object], context.Telemetry, context.Clock.Object);
            await using (other.ConfigureAwait(false))
            {
                await other.BindAsync(context.Session.Object).ConfigureAwait(false);
                await other.DiscoverAsync("sample").ConfigureAwait(false);
                await other.InspectAsync(context.Target).ConfigureAwait(false);
                CompanionOperationDraft foreign = await other.PrepareAsync(
                    context.Target, "apply", "foreign").ConfigureAwait(false);

                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteAsync(foreign, false),
                    Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
                context.VerifyInspectionCount(4);
                context.VerifyExecutionCount(0);

                CompanionOperationResult ownedResult = await context.Workspace.ExecuteAsync(owned, false)
                    .ConfigureAwait(false);
                CompanionOperationResult foreignResult = await other.ExecuteAsync(foreign, false)
                    .ConfigureAwait(false);

                Assert.That(ownedResult.Summary, Is.EqualTo("Operation complete."));
                Assert.That(foreignResult.Summary, Is.EqualTo("Operation complete."));
                context.VerifyExecution("owned");
                context.VerifyExecution("foreign");
                context.VerifyInspectionCount(6);
                context.VerifyExecutionCount(2);
            }
        }
    }

    [Test]
    public async Task ANewDraftWithIdenticalValuesCannotBypassReferenceOwnershipAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft owned = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
            var copy = new CompanionOperationDraft(
                owned.Target, owned.Operation, owned.Input, context.Session.Object, owned.ExpiresAt);

            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(copy, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
            context.VerifyInspectionCount(2);
            context.VerifyExecutionCount(0);

            await context.Workspace.ExecuteAsync(owned, false).ConfigureAwait(false);

            context.VerifyExecution("recipe=7");
            context.VerifyExecutionCount(1);
        }
    }

    [Test]
    public async Task ReplacingPreparationKeepsTheOldInputButOnlyRunsTheNewDraftAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft old = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
            CompanionOperationDraft replacement = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=9").ConfigureAwait(false);

            Assert.That(old.Input, Is.EqualTo("recipe=7"));
            Assert.That(replacement.Input, Is.EqualTo("recipe=9"));
            Assert.That(replacement, Is.Not.SameAs(old));
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(old, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
            context.VerifyInspectionCount(3);
            context.VerifyExecutionCount(0);

            await context.Workspace.ExecuteAsync(replacement, false).ConfigureAwait(false);

            context.VerifyExecution("recipe=9");
            context.VerifyExecutionCount(1);
        }
    }

    [Test]
    public async Task FailedReplacementDoesNotRestoreThePreviousPreparationAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft old = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
            context.Inspection = context.Inspection with { Operations = [] };

            await Assert.ThatAsync(
                () => context.Workspace.PrepareAsync(context.Target, "apply", "recipe=9"),
                Throws.InvalidOperationException.With.Message.Contains("changed")).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(old, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);

            context.VerifyInspectionCount(3);
            context.VerifyExecutionCount(0);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedPreparationOrInspectionCannotRestoreThePreviousDraftAsync(bool prepare)
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft old = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
            context.Provider.Setup(item => item.InspectAsync(
                    It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromException<CompanionInspection>(
                    new ServiceResultException(StatusCodes.BadUserAccessDenied)));

            await Assert.ThatAsync(async () =>
            {
                if (prepare)
                {
                    await context.Workspace.PrepareAsync(context.Target, "apply", "recipe=9").ConfigureAwait(false);
                }
                else
                {
                    await context.Workspace.InspectAsync(context.Target).ConfigureAwait(false);
                }
            }, Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(old, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);

            context.VerifyInspectionCount(3);
            context.VerifyExecutionCount(0);
        }
    }

    [TestCase(-1, true)]
    [TestCase(0, false)]
    [TestCase(1, false)]
    public async Task PreparationExpiresAtExactlyFiveMinutesAsync(int ticksFromExpiry, bool accepted)
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            DateTimeOffset preparedAt = context.UtcNow;
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", null).ConfigureAwait(false);
            context.UtcNow = preparedAt.AddMinutes(5).AddTicks(ticksFromExpiry);

            Assert.That(draft.ExpiresAt, Is.EqualTo(preparedAt.AddMinutes(5)));
            if (accepted)
            {
                CompanionOperationResult result = await context.Workspace.ExecuteAsync(draft, false)
                    .ConfigureAwait(false);
                Assert.That(result.Summary, Is.EqualTo("Operation complete."));
                context.VerifyExecution(null);
            }
            else
            {
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteAsync(draft, false),
                    Throws.InvalidOperationException.With.Message.Contains("expired")).ConfigureAwait(false);
            }
            context.VerifyInspectionCount(accepted ? 3 : 2);
            context.VerifyExecutionCount(accepted ? 1 : 0);
        }
    }

    [TestCase("session-id", false)]
    [TestCase("session-id", true)]
    [TestCase("identity", false)]
    [TestCase("identity", true)]
    [TestCase("namespace", false)]
    [TestCase("namespace", true)]
    [TestCase("namespace-order", false)]
    [TestCase("namespace-added", false)]
    [TestCase("endpoint", false)]
    [TestCase("endpoint", true)]
    [TestCase("endpoint-case", false)]
    [TestCase("endpoint-query", false)]
    [TestCase("security-mode", false)]
    [TestCase("security-mode", true)]
    [TestCase("security-policy", false)]
    [TestCase("security-policy", true)]
    [TestCase("disconnected", false)]
    [TestCase("disconnected", true)]
    [TestCase("expired", true)]
    public async Task SessionDriftBeforeOrDuringExecutionInspectionRejectsAndConsumesDraftAsync(
        string change,
        bool duringInspection)
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
            if (duringInspection)
            {
                context.Provider.Setup(item => item.InspectAsync(
                        It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                    .Callback(() => ChangeSession(context, change))
                    .Returns(() => ValueTask.FromResult(context.Inspection));
            }
            else
            {
                ChangeSession(context, change);
            }

            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, false),
                Throws.InvalidOperationException.With.Message.Contains("changed")).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);

            context.VerifyInspectionCount(duringInspection ? 3 : 2);
            context.VerifyExecutionCount(0);
        }
    }

    [TestCase("session-id")]
    [TestCase("identity")]
    [TestCase("namespace")]
    [TestCase("endpoint")]
    [TestCase("security-mode")]
    [TestCase("security-policy")]
    [TestCase("disconnected")]
    [TestCase("expired")]
    public async Task SessionDriftDuringPreparationCannotPublishADraftAsync(string change)
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            context.Provider.Setup(item => item.InspectAsync(
                    It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                .Callback(() => ChangeSession(context, change))
                .Returns(() => ValueTask.FromResult(context.Inspection));

            await Assert.ThatAsync(
                () => context.Workspace.PrepareAsync(context.Target, "apply", "recipe=7"),
                Throws.InvalidOperationException.With.Message.Contains("changed")).ConfigureAwait(false);

            context.VerifyInspectionCount(2);
            context.VerifyExecutionCount(0);
        }
    }

    [TestCase("cancel")]
    [TestCase("bind-same-session")]
    [TestCase("bind-new-session")]
    [TestCase("disconnect")]
    [TestCase("inspect")]
    [TestCase("discover")]
    public async Task WorkspaceStateChangesInvalidatePreparationAsync(string change)
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
            switch (change)
            {
                case "cancel":
                    await context.Workspace.CancelAsync().ConfigureAwait(false);
                    break;
                case "bind-same-session":
                    await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
                    break;
                case "bind-new-session":
                    await context.Workspace.BindAsync(new Mock<ISession>(MockBehavior.Strict).Object)
                        .ConfigureAwait(false);
                    break;
                case "disconnect":
                    await context.Workspace.BindAsync(null).ConfigureAwait(false);
                    break;
                case "inspect":
                    await context.Workspace.InspectAsync(context.Target).ConfigureAwait(false);
                    break;
                case "discover":
                    await context.Workspace.DiscoverAsync("sample").ConfigureAwait(false);
                    break;
            }

            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);

            context.VerifyInspectionCount(change == "inspect" ? 3 : 2);
            context.VerifyExecutionCount(0);
            context.Session.Verify(item => item.Dispose(), Times.Never);
        }
    }

    [Test]
    public async Task AlreadyCanceledExecutionConsumesPreparationBeforeProviderWorkAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, false, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);

            context.VerifyInspectionCount(2);
            context.VerifyExecutionCount(0);
        }
    }

    [Test]
    public async Task FailedDiscoveryRevokesAnExistingPreparationBeforeProviderWorkAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", "original").ConfigureAwait(false);
            context.Provider.Setup(item => item.DiscoverAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Discovery unavailable."));

            await Assert.ThatAsync(() => context.Workspace.DiscoverAsync("sample"),
                Throws.InvalidOperationException.With.Message.Contains("Discovery unavailable")).ConfigureAwait(false);
            await Assert.ThatAsync(() => context.Workspace.ExecuteAsync(draft, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
            context.VerifyExecutionCount(0);
        }
    }

    [Test]
    public async Task InvalidReplacementInputRevokesThePreviousPreparationAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", "original").ConfigureAwait(false);
            Assert.That(() => context.Workspace.PrepareAsync(context.Target, "apply", new string('x', 65537)),
                Throws.ArgumentException);
            await Assert.ThatAsync(() => context.Workspace.ExecuteAsync(draft, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
            context.VerifyExecutionCount(0);
        }
    }

    [Test]
    public async Task OverlappingPreparationCannotPublishTheSupersededRequestAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Provider.Setup(item => item.InspectAsync(
                It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                .Returns(async (CompanionContext _, CompanionTarget _, CancellationToken token) =>
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    return context.Inspection;
                });
            Task<CompanionOperationDraft> first = context.Workspace.PrepareAsync(context.Target, "apply", "old");
            Task<CompanionOperationDraft> replacement;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                replacement = context.Workspace.PrepareAsync(context.Target, "apply", "new");
            }
            finally
            {
                release.TrySetResult();
            }
            await Assert.ThatAsync(() => first,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            CompanionOperationDraft draft = await replacement.ConfigureAwait(false);
            Assert.That(draft.Input, Is.EqualTo("new"));
            await context.Workspace.ExecuteAsync(draft, false).ConfigureAwait(false);
            context.VerifyExecution("new");
            context.VerifyExecutionCount(1);
        }
    }

    [Test]
    public async Task AlreadyCanceledPreparationInvalidatesThePreviousDraftWithoutProviderWorkAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft old = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                () => context.Workspace.PrepareAsync(context.Target, "apply", "recipe=9", cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(old, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);

            context.VerifyInspectionCount(2);
            context.VerifyExecutionCount(0);
        }
    }

    [Test]
    public async Task CanceledFreshInspectionCannotDispatchALateProviderResultAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            context.Provider.Setup(item => item.InspectAsync(
                    It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                .Returns(async (CompanionContext _, CompanionTarget _, CancellationToken _) =>
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    return context.Inspection;
                });

            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, false, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);

            context.VerifyInspectionCount(3);
            context.VerifyExecutionCount(0);
        }
    }

    [Test]
    public async Task ConcurrentReplayIsRejectedBeforeFreshInspectionCompletesAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
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
            Task<CompanionOperationResult> execution = context.Workspace.ExecuteAsync(draft, false);
            Task rejection;
            try
            {
                await Task.WhenAny(started.Task, execution).ConfigureAwait(false);
                Assert.That(started.Task.IsCompletedSuccessfully, Is.True);
                rejection = Assert.ThatAsync(
                    () => context.Workspace.ExecuteAsync(draft, false),
                    Throws.InvalidOperationException.With.Message.Contains("Prepare"));
                context.VerifyExecutionCount(0);
            }
            finally
            {
                release.TrySetResult();
            }

            await rejection.ConfigureAwait(false);
            CompanionOperationResult result = await execution.ConfigureAwait(false);

            Assert.That(result.Summary, Is.EqualTo("Operation complete."));
            context.VerifyInspectionCount(3);
            context.VerifyExecution("recipe=7");
            context.VerifyExecutionCount(1);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelOrRebindDuringPreparationPreventsAStaleCommitAsync(bool rebind)
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft old = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
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
            Task<CompanionOperationDraft> preparation = context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=9");
            await Task.WhenAny(started.Task, preparation).ConfigureAwait(false);
            Assert.That(started.Task.IsCompletedSuccessfully, Is.True);

            if (rebind)
            {
                await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
            }
            else
            {
                await context.Workspace.CancelAsync().ConfigureAwait(false);
            }

            await Assert.ThatAsync(
                () => preparation,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(old, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
            context.VerifyInspectionCount(3);
            context.VerifyExecutionCount(0);
            context.Session.Verify(item => item.Dispose(), Times.Never);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelOrRebindDuringProviderExecutionPreventsReplayAsync(bool rebind)
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<CompanionOperationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.Provider.Setup(item => item.ExecuteAsync(
                    It.IsAny<CompanionContext>(), context.Target, "apply",
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(async (CompanionContext _, CompanionTarget _, string _, string? _, CancellationToken token) =>
                {
                    started.TrySetResult();
                    return await release.Task.WaitAsync(token).ConfigureAwait(false);
                });
            Task<CompanionOperationResult> execution = context.Workspace.ExecuteAsync(draft, false);
            await Task.WhenAny(started.Task, execution).ConfigureAwait(false);
            Assert.That(started.Task.IsCompletedSuccessfully, Is.True);

            if (rebind)
            {
                await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
            }
            else
            {
                await context.Workspace.CancelAsync().ConfigureAwait(false);
            }

            await Assert.ThatAsync(
                () => execution,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
            context.VerifyInspectionCount(3);
            context.VerifyExecution("recipe=7");
            context.VerifyExecutionCount(1);
            context.Session.Verify(item => item.Dispose(), Times.Never);
        }
    }

    [Test]
    public async Task ProviderFailureConsumesDraftWithoutPoisoningNewPreparationAsync()
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft failed = await context.Workspace.PrepareAsync(
                context.Target, "apply", "failed request").ConfigureAwait(false);
            context.Provider.SetupSequence(item => item.ExecuteAsync(
                    It.IsAny<CompanionContext>(), context.Target, "apply",
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromException<CompanionOperationResult>(
                    new ServiceResultException(StatusCodes.BadUserAccessDenied)))
                .Returns(() => ValueTask.FromResult(context.Result));

            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(failed, false),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(failed, false),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
            context.VerifyInspectionCount(3);
            context.VerifyExecutionCount(1);

            CompanionOperationDraft retry = await context.Workspace.PrepareAsync(
                context.Target, "apply", "new request").ConfigureAwait(false);
            CompanionOperationResult result = await context.Workspace.ExecuteAsync(retry, false)
                .ConfigureAwait(false);

            Assert.That(result.Summary, Is.EqualTo("Operation complete."));
            context.VerifyExecution("failed request");
            context.VerifyExecution("new request");
            context.VerifyInspectionCount(5);
            context.VerifyExecutionCount(2);
        }
    }

    [TestCase("opc.tcp://localhost:4840/Sample", false)]
    [TestCase("opc.tcp://remote.example:4840/Server", true)]
    [TestCase("opc.tcp://remote.example:4840/Server", false)]
    public async Task PreparedSampleMutationRequiresLoopbackAndExplicitConfirmationAsync(
        string endpoint,
        bool confirmed)
    {
        var context = new CompanionPreparedOperationTestContext(
            CompanionOperationSafety.SampleMutation, endpoint);
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);

            context.VerifyExecutionCount(0);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, confirmed),
                Throws.InvalidOperationException.With.Message.Contains("Sample operations")).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, true),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);

            context.VerifyInspectionCount(3);
            context.VerifyExecutionCount(0);
        }
    }

    [TestCase("opc.tcp://localhost:4840/Sample")]
    [TestCase("opc.tcp://127.0.0.1:4840/Sample")]
    [TestCase("opc.tcp://[::1]:4840/Sample")]
    public async Task ConfirmedPreparedLoopbackSampleExecutesOnceAsync(string endpoint)
    {
        var context = new CompanionPreparedOperationTestContext(
            CompanionOperationSafety.SampleMutation, endpoint);
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", "recipe=7").ConfigureAwait(false);

            context.VerifyExecutionCount(0);
            CompanionOperationResult result = await context.Workspace.ExecuteAsync(draft, true)
                .ConfigureAwait(false);

            Assert.That(result.Summary, Is.EqualTo("Operation complete."));
            context.VerifyInspectionCount(3);
            context.VerifyExecution("recipe=7");
            context.VerifyExecutionCount(1);
        }
    }

    [TestCase("relative-endpoint")]
    [TestCase("opc.tcp://user@localhost:4840/Sample")]
    [TestCase("opc.tcp://localhost:4840/Sample#other")]
    public async Task PreparationRejectsRelativeEndpointsUserInfoAndFragmentsAsync(string endpoint)
    {
        var context = new CompanionPreparedOperationTestContext(endpoint: endpoint);
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                () => context.Workspace.PrepareAsync(context.Target, "apply", null),
                Throws.InvalidOperationException.With.Message.Contains("absolute endpoint")).ConfigureAwait(false);

            context.VerifyInspectionCount(1);
            context.VerifyExecutionCount(0);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    public async Task LocalFilePreparationRequiresAnExplicitDestinationAsync(string? input)
    {
        var context = new CompanionPreparedOperationTestContext(CompanionOperationSafety.LocalFile);
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                () => context.Workspace.PrepareAsync(context.Target, "apply", input),
                Throws.ArgumentException.With.Message.Contains("destination")).ConfigureAwait(false);

            context.VerifyInspectionCount(1);
            context.VerifyExecutionCount(0);
        }
    }

    [Test]
    public async Task LocalFileExecutionUsesTheExactDestinationWithoutSampleConfirmationAsync()
    {
        const string destination = @"D:\exports\inspection 42.json";
        var context = new CompanionPreparedOperationTestContext(
            CompanionOperationSafety.LocalFile, "opc.tcp://remote.example:4840/Server");
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                context.Target, "apply", destination).ConfigureAwait(false);

            context.VerifyExecutionCount(0);
            CompanionOperationResult result = await context.Workspace.ExecuteAsync(draft, false)
                .ConfigureAwait(false);

            Assert.That(result.Summary, Is.EqualTo("Operation complete."));
            Assert.That(draft.Input, Is.EqualTo(destination));
            context.VerifyInspectionCount(3);
            context.VerifyExecution(destination);
            context.VerifyExecutionCount(1);
        }
    }

    [TestCase(65535, true)]
    [TestCase(65536, true)]
    [TestCase(65537, false)]
    [TestCase(65538, false)]
    public async Task PreparationEnforcesTheExactInputLengthLimitAsync(int length, bool accepted)
    {
        var context = new CompanionPreparedOperationTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            string input = new('x', length);

            if (accepted)
            {
                CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                    context.Target, "apply", input).ConfigureAwait(false);
                Assert.That(draft.Input, Has.Length.EqualTo(length));
                await context.Workspace.ExecuteAsync(draft, false).ConfigureAwait(false);
                context.VerifyExecution(input);
            }
            else
            {
                await Assert.ThatAsync(
                    () => context.Workspace.PrepareAsync(context.Target, "apply", input),
                    Throws.ArgumentException.With.Message.Contains("65536")).ConfigureAwait(false);
            }

            context.VerifyInspectionCount(accepted ? 3 : 1);
            context.VerifyExecutionCount(accepted ? 1 : 0);
        }
    }

    private static void ChangeSession(CompanionPreparedOperationTestContext context, string change)
    {
        switch (change)
        {
            case "session-id":
                context.SessionId = new NodeId(202u);
                break;
            case "identity":
                context.Identity = new Mock<IUserIdentity>(MockBehavior.Strict).Object;
                break;
            case "namespace":
                context.NamespaceUris.Update([Namespaces.OpcUa, "urn:ualens:server", "urn:ualens:replacement"]);
                break;
            case "namespace-order":
                context.NamespaceUris.Update([Namespaces.OpcUa, "urn:ualens:test", "urn:ualens:server"]);
                break;
            case "namespace-added":
                context.NamespaceUris.Append("urn:ualens:additional");
                break;
            case "endpoint":
                context.Endpoint.EndpointUrl = "opc.tcp://localhost:4840/AnotherServer";
                break;
            case "endpoint-case":
                context.Endpoint.EndpointUrl = "opc.tcp://localhost:4840/sample";
                break;
            case "endpoint-query":
                context.Endpoint.EndpointUrl += "?profile=changed";
                break;
            case "security-mode":
                context.Endpoint.SecurityMode = MessageSecurityMode.None;
                break;
            case "security-policy":
                context.Endpoint.SecurityPolicyUri = SecurityPolicies.None;
                break;
            case "disconnected":
                context.Connected = false;
                break;
            case "expired":
                context.UtcNow = context.UtcNow.AddMinutes(5);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown session change.");
        }
    }
}

internal sealed class CompanionPreparedOperationTestContext : IAsyncDisposable
{
    public CompanionPreparedOperationTestContext(
        CompanionOperationSafety safety = CompanionOperationSafety.ReadOnly,
        string endpoint = "opc.tcp://localhost:4840/Sample",
        ITelemetryContext? telemetry = null,
        ArrayOf<ICompanionProvider> additionalProviders = default)
    {
        Telemetry = telemetry ?? DefaultTelemetry.Create(static _ => { });
        Endpoint = new EndpointDescription
        {
            EndpointUrl = endpoint,
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
        };
        Session.SetupGet(item => item.Connected).Returns(() => Connected);
        Session.SetupGet(item => item.SessionId).Returns(() => SessionId);
        Session.SetupGet(item => item.Identity).Returns(() => Identity);
        Session.SetupGet(item => item.NamespaceUris).Returns(NamespaceUris);
        Session.SetupGet(item => item.Endpoint).Returns(Endpoint);
        Clock.Setup(item => item.GetUtcNow()).Returns(() => UtcNow);
        Operation = new CompanionOperation("apply", "Apply recipe", safety, "Recipe input");
        Targets = [Target];
        Inspection = new CompanionInspection(
            [new CompanionValue("Temperature", Variant.From(42.5))], [Operation], "Inspection complete.");
        Provider.SetupGet(item => item.Descriptor)
            .Returns(new CompanionDescriptor("sample", "Sample", "urn:ualens:test", "Test model"));
        Provider.Setup(item => item.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(Targets));
        Provider.Setup(item => item.InspectAsync(
                It.IsAny<CompanionContext>(), Target, It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(Inspection));
        Provider.Setup(item => item.ExecuteAsync(
                It.IsAny<CompanionContext>(), Target, "apply",
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(Result));
        Workspace = new CompanionWorkspace([Provider.Object, .. additionalProviders], Telemetry, Clock.Object);
    }

    public ITelemetryContext Telemetry { get; }

    public Mock<ISession> Session { get; } = new(MockBehavior.Strict);

    public Mock<ICompanionProvider> Provider { get; } = new(MockBehavior.Strict);

    public Mock<TimeProvider> Clock { get; } = new(MockBehavior.Strict);

    public CompanionWorkspace Workspace { get; }

    public CompanionTarget Target { get; } = new("sample", new NodeId(1234u, 2), "Sample device", "Device");

    public CompanionOperation Operation { get; }

    public ArrayOf<CompanionTarget> Targets { get; set; }

    public CompanionInspection Inspection { get; set; }

    public CompanionOperationResult Result { get; } =
        new("Operation complete.", [new CompanionValue("Processed", Variant.From(17u))]);

    public NamespaceTable NamespaceUris { get; } =
        new([Namespaces.OpcUa, "urn:ualens:server", "urn:ualens:test"]);

    public EndpointDescription Endpoint { get; }

    public bool Connected { get; set; } = true;

    public NodeId SessionId { get; set; } = new(101u);

    public IUserIdentity Identity { get; set; } = new Mock<IUserIdentity>(MockBehavior.Strict).Object;

    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await Workspace.BindAsync(Session.Object).ConfigureAwait(false);
        await Workspace.DiscoverAsync("sample").ConfigureAwait(false);
        await Workspace.InspectAsync(Target).ConfigureAwait(false);
    }

    public void VerifyInspectionCount(int count)
    {
        Provider.Verify(item => item.InspectAsync(
                It.Is<CompanionContext>(value => ReferenceEquals(value.Session, Session.Object)),
                Target, It.IsAny<CancellationToken>()),
            Times.Exactly(count));
    }

    public void VerifyExecution(string? input)
    {
        Provider.Verify(item => item.ExecuteAsync(
                It.Is<CompanionContext>(value => ReferenceEquals(value.Session, Session.Object)),
                Target, "apply", input, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public void VerifyExecutionCount(int count)
    {
        Provider.Verify(item => item.ExecuteAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(count));
    }

    public ValueTask DisposeAsync()
    {
        return Workspace.DisposeAsync();
    }
}
