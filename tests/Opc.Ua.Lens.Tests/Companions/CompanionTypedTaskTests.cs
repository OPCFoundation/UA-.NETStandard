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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class CompanionTypedTaskTests
{
    [Test]
    public void ExplicitEmptyFormIsTypedButAnUnspecifiedFormUsesTheLegacyPath()
    {
        var legacy = new CompanionOperation("refresh", "Refresh", CompanionOperationSafety.ReadOnly);
        CompanionOperation typed = legacy with { Inputs = [] };

        Assert.That(legacy.HasTypedInput, Is.False);
        Assert.That(legacy.Inputs.IsNull, Is.True);
        Assert.That(typed.HasTypedInput, Is.True);
        Assert.That(typed.Inputs.IsNull, Is.False);
        Assert.That(typed.Inputs.Count, Is.Zero);
    }

    [Test]
    public async Task PrepareCapturesExactScalarsAndDispatchesOnlyTheReviewedDomainInputOnceAsync()
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            CompanionOperationDraft draft = await context.Workspace.PrepareTaskAsync(
                context.Target, "typed", context.Inputs, cancellation.Token).ConfigureAwait(false);

            Assert.That(draft.Input, Is.Null);
            Assert.That(draft.Target, Is.SameAs(context.Target));
            Assert.That(draft.Operation, Is.SameAs(context.Operation));
            Assert.That(draft.TaskInput, Is.SameAs(context.PreparedInput));
            Assert.That(draft.ExpiresAt, Is.EqualTo(context.UtcNow.AddMinutes(5)));
            Assert.That(draft.Summary, Does.Contain(context.PreparedInput.Review).And.Contain("Typed device"));
            Assert.That(draft.Summary, Does.Not.Contain("unreviewed-label"));
            AssertInputs(context.CapturedInputs);
            Assert.That(context.PrepareToken.CanBeCanceled, Is.True);
            context.VerifyCalls(1, 0);

            var progress = new Mock<IProgress<CompanionTaskProgress>>(MockBehavior.Strict);
            progress.Setup(value => value.Report(
                It.Is<CompanionTaskProgress>(item => item.Phase == "Applying typed request" && item.Percent == 37)));
            context.Provider.Setup(value => value.ExecutePreparedAsync(
                    It.IsAny<CompanionContext>(), context.Target, "typed", context.PreparedInput,
                    progress.Object, It.IsAny<CancellationToken>()))
                .Returns((CompanionContext borrowed, CompanionTarget target, string operation,
                    CompanionTaskInput input, IProgress<CompanionTaskProgress>? reporter, CancellationToken token) =>
                {
                    Assert.That(borrowed.Session, Is.SameAs(context.Session.Object));
                    Assert.That(target, Is.SameAs(context.Target));
                    Assert.That(operation, Is.EqualTo("typed"));
                    Assert.That(input, Is.SameAs(draft.TaskInput));
                    Assert.That(token.CanBeCanceled, Is.True);
                    reporter?.Report(new CompanionTaskProgress("Applying typed request", 37));
                    return ValueTask.FromResult(context.Result);
                });

            CompanionOperationResult result = await context.Workspace.ExecuteTaskAsync(
                draft, false, progress.Object, cancellation.Token).ConfigureAwait(false);

            Assert.That(result.Summary, Is.EqualTo("Typed operation completed."));
            Assert.That(result.Values[0].Name, Is.EqualTo("Accepted count"));
            Assert.That(result.Values[0].Value.TryGetValue(out uint count), Is.True);
            Assert.That(count, Is.EqualTo(19u));
            progress.VerifyAll();
            context.VerifyCalls(1, 1);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteTaskAsync(draft, false, progress.Object),
                Throws.InvalidOperationException).ConfigureAwait(false);
            context.VerifyCalls(1, 1);
            context.Session.Verify(value => value.Dispose(), Times.Never);
        }
    }

    [Test]
    public async Task InputArrayIsCopiedBeforeQueuedInspectionCanYieldAsync()
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<CompanionInspection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            context.Provider.Setup(value => value.InspectAsync(
                    It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    entered.TrySetResult();
                    return new ValueTask<CompanionInspection>(release.Task);
                });
            CompanionValue[] storage = [.. context.Inputs];
            Task<CompanionOperationDraft> preparing = context.Workspace.PrepareTaskAsync(
                context.Target, "typed", ArrayOf.Wrapped(storage));
            try
            {
                await Task.WhenAny(entered.Task, preparing).ConfigureAwait(false);
                Assert.That(entered.Task.IsCompletedSuccessfully, Is.True);
                storage[0] = new CompanionValue("label", Variant.From("replacement"));
                storage[2] = new CompanionValue("count", Variant.From(99u));
            }
            finally
            {
                release.TrySetResult(context.Inspection);
            }
            CompanionOperationDraft draft = await preparing.ConfigureAwait(false);

            AssertInputs(context.CapturedInputs);
            Assert.That(draft.TaskInput, Is.SameAs(context.PreparedInput));
            Assert.That(storage[0].Value.TryGetValue(out string changed), Is.True);
            Assert.That(changed, Is.EqualTo("replacement"));
            context.VerifyCalls(1, 0);
        }
    }

    [TestCase("null-field")]
    [TestCase("missing")]
    [TestCase("extra")]
    [TestCase("duplicate")]
    [TestCase("reordered")]
    [TestCase("wrong-name")]
    [TestCase("wrong-type")]
    [TestCase("array")]
    [TestCase("null-variant")]
    [TestCase("null-string")]
    [TestCase("empty-required")]
    [TestCase("blank-required")]
    public async Task MalformedTypedFieldsNeverReachDomainPreparationAsync(string fault)
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            List<CompanionValue> fields = context.Inputs.ToList();
            switch (fault)
            {
                case "null-field":
                    fields[0] = null!;
                    break;
                case "missing":
                    fields.RemoveAt(0);
                    break;
                case "extra":
                    fields.Add(new CompanionValue("extra", Variant.From("unexpected")));
                    break;
                case "duplicate":
                    fields[1] = fields[0];
                    break;
                case "reordered":
                    (fields[0], fields[1]) = (fields[1], fields[0]);
                    break;
                case "wrong-name":
                    fields[0] = fields[0] with { Name = "Label" };
                    break;
                case "wrong-type":
                    fields[2] = new CompanionValue("count", Variant.From(19));
                    break;
                case "array":
                    fields[2] = new CompanionValue("count", Variant.From((ArrayOf<uint>)[19u]));
                    break;
                case "null-variant":
                    fields[0] = new CompanionValue("label", Variant.Null);
                    break;
                case "null-string":
                    fields[0] = new CompanionValue("label", Variant.From((string)null!));
                    break;
                case "empty-required":
                    fields[0] = new CompanionValue("label", Variant.From(string.Empty));
                    break;
                case "blank-required":
                    fields[0] = new CompanionValue("label", Variant.From(" \t\r\n"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            await Assert.ThatAsync(
                () => context.Workspace.PrepareTaskAsync(context.Target, "typed", [.. fields]),
                Throws.ArgumentException).ConfigureAwait(false);

            context.VerifyCalls(0, 0);
            context.VerifyInspections(1);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(32)]
    public async Task ExactTypedFieldCountIncludingAnEmptyFormCanBePreparedAsync(int count)
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            ArrayOf<CompanionInputDefinition> definitions = [.. Enumerable.Range(0, count).Select(index =>
                new CompanionInputDefinition(
                    index.ToString(CultureInfo.InvariantCulture), "Field", BuiltInType.String, "Value"))];
            context.Inspection = context.Inspection with
            {
                Operations = [context.Operation with { Inputs = definitions }]
            };
            ArrayOf<CompanionValue> inputs = definitions.ConvertAll(field =>
                new CompanionValue(field.Name, Variant.From("provided")));
            await context.InitializeAsync().ConfigureAwait(false);

            CompanionOperationDraft draft = await context.Workspace.PrepareTaskAsync(
                context.Target, "typed", inputs).ConfigureAwait(false);
            CompanionOperationResult result = await context.Workspace.ExecuteTaskAsync(draft, false, null)
                .ConfigureAwait(false);

            Assert.That(draft.Operation.HasTypedInput, Is.True);
            Assert.That(context.CapturedInputs.Count, Is.EqualTo(count));
            Assert.That(context.CapturedInputs.ConvertAll(field => field.Name),
                Is.EqualTo(definitions.ConvertAll(field => field.Name)));
            Assert.That(result.Summary, Is.EqualTo("Typed operation completed."));
            context.VerifyCalls(1, 1);
        }
    }

    [Test]
    public async Task MoreThanThirtyTwoFieldsIsRejectedBeforeFreshInspectionAsync()
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            ArrayOf<CompanionValue> fields = [.. Enumerable.Repeat(context.Inputs[0], 33)];

            await Assert.ThatAsync(
                () => context.Workspace.PrepareTaskAsync(context.Target, "typed", fields),
                Throws.ArgumentException).ConfigureAwait(false);

            context.VerifyInspections(1);
            context.VerifyCalls(0, 0);
        }
    }

    [TestCase(65535, true)]
    [TestCase(65536, true)]
    [TestCase(65537, false)]
    public async Task AggregateStringLimitAppliesAcrossAllTypedFieldsAsync(int totalLength, bool accepted)
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            context.Inspection = context.Inspection with
            {
                Operations =
                [
                    context.Operation with
                    {
                        Inputs =
                        [
                            new("first", "First", BuiltInType.String, "Text"),
                            new("second", "Second", BuiltInType.String, "Text")
                        ]
                    }
                ]
            };
            await context.InitializeAsync().ConfigureAwait(false);
            ArrayOf<CompanionValue> fields =
            [
                new("first", Variant.From(new string('a', 32768))),
                new("second", Variant.From(new string('b', totalLength - 32768)))
            ];

            if (accepted)
            {
                CompanionOperationDraft draft = await context.Workspace.PrepareTaskAsync(
                    context.Target, "typed", fields).ConfigureAwait(false);
                Assert.That(draft.TaskInput, Is.SameAs(context.PreparedInput));
                Assert.That(context.CapturedInputs[1].Value.TryGetValue(out string second), Is.True);
                Assert.That(second, Is.EqualTo(new string('b', totalLength - 32768)));
            }
            else
            {
                await Assert.ThatAsync(
                    () => context.Workspace.PrepareTaskAsync(context.Target, "typed", fields),
                    Throws.ArgumentException).ConfigureAwait(false);
            }
            context.VerifyCalls(accepted ? 1 : 0, 0);
        }
    }

    [TestCase("duplicate")]
    [TestCase("empty-name")]
    [TestCase("empty-display")]
    [TestCase("unsupported-type")]
    [TestCase("too-many")]
    public async Task InspectionRejectsMalformedTypedFormDefinitionsAsync(string fault)
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            List<CompanionInputDefinition> fields = context.Operation.Inputs.ToList();
            switch (fault)
            {
                case "duplicate":
                    fields[1] = fields[0];
                    break;
                case "empty-name":
                    fields[0] = fields[0] with { Name = " " };
                    break;
                case "empty-display":
                    fields[0] = fields[0] with { DisplayName = string.Empty };
                    break;
                case "unsupported-type":
                    fields[0] = fields[0] with { DataType = BuiltInType.ByteString };
                    break;
                case "too-many":
                    fields = [.. Enumerable.Repeat(fields[0], 33)];
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            context.Inspection = context.Inspection with
            {
                Operations = [context.Operation with { Inputs = [.. fields] }]
            };
            await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
            await context.Workspace.DiscoverAsync("typed-provider").ConfigureAwait(false);

            await Assert.ThatAsync(
                () => context.Workspace.InspectAsync(context.Target),
                Throws.InvalidOperationException).ConfigureAwait(false);

            context.VerifyCalls(0, 0);
        }
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase(" \r\n", false)]
    [TestCase("review", true)]
    public async Task DomainPreparationMustSupplyNonemptyReviewEvidenceAsync(string? review, bool accepted)
    {
        var context = new CompanionTypedTaskTestContext
        {
            PreparedInput = new CompanionTestTaskInput(review!)
        };
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);

            if (accepted)
            {
                CompanionOperationDraft draft = await context.PrepareAsync().ConfigureAwait(false);
                Assert.That(draft.TaskInput, Is.SameAs(context.PreparedInput));
                Assert.That(draft.Summary, Does.Contain("review"));
            }
            else
            {
                await Assert.ThatAsync(context.PrepareAsync, Throws.InvalidOperationException).ConfigureAwait(false);
            }
            context.VerifyCalls(1, 0);
        }
    }

    [TestCase(8192, true)]
    [TestCase(8193, false)]
    public async Task DomainReviewEnforcesItsExactLengthLimitAsync(int length, bool accepted)
    {
        var context = new CompanionTypedTaskTestContext
        {
            PreparedInput = new CompanionTestTaskInput(new string('r', length))
        };
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            if (accepted)
            {
                CompanionOperationDraft draft = await context.PrepareAsync().ConfigureAwait(false);
                Assert.That(draft.TaskInput?.Review, Is.EqualTo(new string('r', length)));
            }
            else
            {
                await Assert.ThatAsync(context.PrepareAsync, Throws.InvalidOperationException).ConfigureAwait(false);
            }
            context.VerifyCalls(1, 0);
        }
    }

    [Test]
    public async Task NullDomainPreparationCannotPublishADraftAsync()
    {
        var context = new CompanionTypedTaskTestContext { PreparedInput = null! };
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);

            await Assert.ThatAsync(context.PrepareAsync, Throws.InvalidOperationException).ConfigureAwait(false);

            context.VerifyCalls(1, 0);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RawPreparationAndExecutionCannotBypassTypedFormsAsync(bool emptyForm)
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            if (emptyForm)
            {
                context.Inspection = context.Inspection with
                {
                    Operations = [context.Operation with { Inputs = [] }]
                };
            }
            await context.InitializeAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                () => context.Workspace.PrepareAsync(context.Target, "typed", "raw"),
                Throws.InvalidOperationException).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(context.Target, "typed", "raw", true),
                Throws.InvalidOperationException).ConfigureAwait(false);

            context.VerifyCalls(0, 0);
        }
    }

    [TestCase("opc.tcp://localhost:4840/Sample", false, false)]
    [TestCase("opc.tcp://localhost:4840/Sample", true, true)]
    [TestCase("opc.tcp://127.0.0.1:4840/Sample", true, true)]
    [TestCase("opc.tcp://[::1]:4840/Sample", true, true)]
    [TestCase("opc.tcp://remote.example:4840/Server", false, false)]
    [TestCase("opc.tcp://remote.example:4840/Server", true, false)]
    public async Task TypedSampleExecutionRequiresLoopbackAndFreshConfirmationAsync(
        string endpoint, bool confirmed, bool accepted)
    {
        var context = new CompanionTypedTaskTestContext(
            safety: CompanionOperationSafety.SampleMutation, endpoint: endpoint);
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.PrepareAsync().ConfigureAwait(false);
            context.VerifyCalls(1, 0);

            if (accepted)
            {
                CompanionOperationResult result = await context.Workspace.ExecuteTaskAsync(draft, confirmed, null)
                    .ConfigureAwait(false);
                Assert.That(result.Summary, Is.EqualTo("Typed operation completed."));
            }
            else
            {
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteTaskAsync(draft, confirmed, null),
                    Throws.InvalidOperationException).ConfigureAwait(false);
            }
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteTaskAsync(draft, true, null),
                Throws.InvalidOperationException).ConfigureAwait(false);
            context.VerifyCalls(1, accepted ? 1 : 0);
        }
    }

    [Test]
    public async Task ReadOnlyTypedTaskUsesTheRemoteSessionWithoutSampleConfirmationAsync()
    {
        var context = new CompanionTypedTaskTestContext(endpoint: "opc.tcp://remote.example:4840/Server");
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.PrepareAsync().ConfigureAwait(false);

            CompanionOperationResult result = await context.Workspace.ExecuteAsync(draft, false).ConfigureAwait(false);

            Assert.That(result.Summary, Is.EqualTo("Typed operation completed."));
            Assert.That(draft.EndpointUrl, Is.EqualTo("opc.tcp://remote.example:4840/Server"));
            context.VerifyCalls(1, 1);
        }
    }

    [Test]
    public async Task FabricatedDraftCannotUseEvenTheRealPreparedInputAsync()
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft real = await context.PrepareAsync().ConfigureAwait(false);
            var forged = new CompanionOperationDraft(
                real.Target, real.Operation, null, context.Session.Object, real.ExpiresAt, real.TaskInput);

            await Assert.ThatAsync(
                () => context.Workspace.ExecuteTaskAsync(forged, false, null),
                Throws.InvalidOperationException).ConfigureAwait(false);
            context.VerifyCalls(1, 0);
            CompanionOperationResult result = await context.Workspace.ExecuteTaskAsync(real, false, null)
                .ConfigureAwait(false);

            Assert.That(result.Summary, Is.EqualTo("Typed operation completed."));
            context.VerifyCalls(1, 1);
        }
    }

    [TestCase(-1, true)]
    [TestCase(0, false)]
    [TestCase(1, false)]
    public async Task TypedPreparationExpiresAtFiveMinutesWithoutRenewingItsLeaseAsync(
        int ticksFromExpiry, bool accepted)
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            DateTimeOffset initial = context.UtcNow;
            context.PrepareHandler = (_, _) =>
            {
                context.UtcNow += TimeSpan.FromMinutes(2);
                return ValueTask.FromResult(context.PreparedInput);
            };
            CompanionOperationDraft draft = await context.PrepareAsync().ConfigureAwait(false);
            Assert.That(draft.ExpiresAt, Is.EqualTo(initial.AddMinutes(5)));
            context.UtcNow = draft.ExpiresAt.AddTicks(ticksFromExpiry);

            if (accepted)
            {
                CompanionOperationResult result = await context.Workspace.ExecuteTaskAsync(draft, false, null)
                    .ConfigureAwait(false);
                Assert.That(result.Summary, Is.EqualTo("Typed operation completed."));
            }
            else
            {
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteTaskAsync(draft, false, null),
                    Throws.InvalidOperationException).ConfigureAwait(false);
            }
            context.VerifyCalls(1, accepted ? 1 : 0);
        }
    }

    [TestCase("session", false)]
    [TestCase("identity", false)]
    [TestCase("namespace", false)]
    [TestCase("namespace-order", false)]
    [TestCase("endpoint", false)]
    [TestCase("security", false)]
    [TestCase("disconnected", false)]
    [TestCase("session", true)]
    [TestCase("identity", true)]
    [TestCase("namespace", true)]
    [TestCase("expired", true)]
    public async Task TypedPreparationCannotSurviveSessionDriftAsync(string change, bool duringPreparation)
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            if (duringPreparation)
            {
                context.PrepareHandler = (_, _) =>
                {
                    ChangeSession(context, change);
                    return ValueTask.FromResult(context.PreparedInput);
                };
                await Assert.ThatAsync(context.PrepareAsync, Throws.InvalidOperationException).ConfigureAwait(false);
            }
            else
            {
                CompanionOperationDraft draft = await context.PrepareAsync().ConfigureAwait(false);
                ChangeSession(context, change);
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteTaskAsync(draft, false, null),
                    Throws.InvalidOperationException).ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteTaskAsync(draft, false, null),
                    Throws.InvalidOperationException).ConfigureAwait(false);
            }
            context.VerifyCalls(1, 0);
        }
    }

    [TestCase("name")]
    [TestCase("type")]
    [TestCase("required")]
    [TestCase("hint")]
    [TestCase("removed")]
    public async Task ChangedTypedFormOfferBlocksExecutionBeforeTheProviderRunsAsync(string change)
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.PrepareAsync().ConfigureAwait(false);
            CompanionInputDefinition original = context.Operation.Inputs[0];
            CompanionInputDefinition changed = change switch
            {
                "name" => original with { Name = "different" },
                "type" => original with { DataType = BuiltInType.Int32 },
                "required" => original with { Required = false },
                "hint" => original with { Hint = "Changed meaning" },
                "removed" => original,
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
            context.Inspection = context.Inspection with
            {
                Operations = change == "removed" ? [] :
                [
                    context.Operation with
                    {
                        Inputs = [changed, .. context.Operation.Inputs.ToList().Skip(1)]
                    }
                ]
            };

            await Assert.ThatAsync(
                () => context.Workspace.ExecuteTaskAsync(draft, false, null),
                Throws.InvalidOperationException).ConfigureAwait(false);

            context.VerifyCalls(1, 0);
            context.VerifyInspections(3);
        }
    }

    [Test]
    public async Task CanceledExecutionConsumesTypedPreparationBeforeDispatchAsync()
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.PrepareAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                () => context.Workspace.ExecuteTaskAsync(draft, false, null, new CancellationToken(true)),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteTaskAsync(draft, false, null),
                Throws.InvalidOperationException).ConfigureAwait(false);

            context.VerifyCalls(1, 0);
            context.VerifyInspections(2);
        }
    }

    [Test]
    public async Task CancellationAfterDomainPreparationCannotPublishItsLateInputAsync()
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            context.PrepareHandler = (_, _) =>
            {
                cancellation.Cancel();
                return ValueTask.FromResult(context.PreparedInput);
            };

            await Assert.ThatAsync(
                () => context.Workspace.PrepareTaskAsync(context.Target, "typed", context.Inputs, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(context.PrepareToken.IsCancellationRequested, Is.True);
            context.VerifyCalls(1, 0);
        }
    }

    [Test]
    public async Task SupersedingAnInFlightTypedPreparationCannotPublishOrRunTheOldInputAsync()
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<CompanionTaskInput>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var previousInput = new CompanionTestTaskInput("Superseded review");
            var replacement = new CompanionTestTaskInput("Current review");
            int preparations = 0;
            context.PrepareHandler = (_, _) =>
            {
                if (++preparations == 1)
                {
                    entered.TrySetResult();
                    return new ValueTask<CompanionTaskInput>(release.Task);
                }
                return ValueTask.FromResult<CompanionTaskInput>(replacement);
            };
            Task<CompanionOperationDraft> previous = context.PrepareAsync();
            Task<CompanionOperationDraft>? next = null;
            try
            {
                await Task.WhenAny(entered.Task, previous).ConfigureAwait(false);
                Assert.That(entered.Task.IsCompletedSuccessfully, Is.True);
                next = context.PrepareAsync();
            }
            finally
            {
                release.TrySetResult(previousInput);
            }

            await Assert.ThatAsync(() => previous, Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            CompanionOperationDraft draft = await (next ??
                throw new InvalidOperationException("The replacement preparation was not started."))
                .ConfigureAwait(false);
            Assert.That(draft.TaskInput, Is.SameAs(replacement));
            Assert.That(draft.Summary, Does.Contain("Current review").And.Not.Contain("Superseded review"));
            await context.Workspace.ExecuteTaskAsync(draft, false, null).ConfigureAwait(false);
            context.Provider.Verify(value => value.ExecutePreparedAsync(
                It.IsAny<CompanionContext>(), context.Target, "typed", replacement,
                null, It.IsAny<CancellationToken>()), Times.Once);
            context.VerifyCalls(2, 1);
        }
    }

    [TestCase("cancel")]
    [TestCase("bind")]
    [TestCase("discard")]
    public async Task ResetRevokesTypedPreparationAndItsAuthorizationAsync(string reset)
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.PrepareAsync().ConfigureAwait(false);
            switch (reset)
            {
                case "cancel":
                    await context.Workspace.CancelAsync().ConfigureAwait(false);
                    break;
                case "bind":
                    await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
                    break;
                case "discard":
                    context.Workspace.DiscardPreparation();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(reset));
            }

            await Assert.ThatAsync(
                () => context.Workspace.ExecuteTaskAsync(draft, true, null),
                Throws.InvalidOperationException).ConfigureAwait(false);

            context.VerifyCalls(1, 0);
        }
    }

    [TestCase("throw")]
    [TestCase("empty-summary")]
    [TestCase("oversized-values")]
    public async Task FailedTypedExecutionConsumesItsDraftAndCannotReturnAnInvalidSuccessAsync(string failure)
    {
        var context = new CompanionTypedTaskTestContext();
        await using (context.ConfigureAwait(false))
        {
            await context.InitializeAsync().ConfigureAwait(false);
            CompanionOperationDraft draft = await context.PrepareAsync().ConfigureAwait(false);
            context.Provider.Setup(value => value.ExecutePreparedAsync(
                    It.IsAny<CompanionContext>(), context.Target, "typed", context.PreparedInput,
                    It.IsAny<IProgress<CompanionTaskProgress>?>(), It.IsAny<CancellationToken>()))
                .Returns(() => failure switch
                {
                    "throw" => throw new InvalidOperationException("Rejected by the typed provider."),
                    "empty-summary" => ValueTask.FromResult(new CompanionOperationResult(" ", [])),
                    "oversized-values" => ValueTask.FromResult(new CompanionOperationResult(
                        "Invalid response", [.. Enumerable.Repeat(context.Result.Values[0], 257)])),
                    _ => throw new ArgumentOutOfRangeException(nameof(failure))
                });

            await Assert.ThatAsync(
                () => context.Workspace.ExecuteTaskAsync(draft, false, null),
                Throws.InvalidOperationException).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteTaskAsync(draft, false, null),
                Throws.InvalidOperationException).ConfigureAwait(false);

            context.VerifyCalls(1, 1);
        }
    }

    private static void AssertInputs(ArrayOf<CompanionValue> fields)
    {
        Assert.That(fields.ConvertAll(field => field.Name),
            Is.EqualTo(s_inputNames));
        Assert.That(fields[0].Value.TryGetValue(out string label), Is.True);
        Assert.That(label, Is.EqualTo("unreviewed-label"));
        Assert.That(fields[1].Value.TryGetValue(out bool enabled), Is.True);
        Assert.That(enabled, Is.True);
        Assert.That(fields[2].Value.TryGetValue(out uint count), Is.True);
        Assert.That(count, Is.EqualTo(19u));
        Assert.That(fields[3].Value.TryGetValue(out int delta), Is.True);
        Assert.That(delta, Is.EqualTo(-3));
        Assert.That(fields[4].Value.TryGetValue(out double gain), Is.True);
        Assert.That(gain, Is.EqualTo(2.5));
    }

    private static readonly string[] s_inputNames = ["label", "enabled", "count", "delta", "gain"];

    private static void ChangeSession(CompanionTypedTaskTestContext context, string change)
    {
        switch (change)
        {
            case "session":
                context.SessionId = new NodeId(202u);
                break;
            case "identity":
                context.Identity = new Mock<IUserIdentity>(MockBehavior.Strict).Object;
                break;
            case "namespace":
                context.NamespaceUris.Update([Namespaces.OpcUa, "urn:server", "urn:replacement"]);
                break;
            case "namespace-order":
                context.NamespaceUris.Update([Namespaces.OpcUa, "urn:typed", "urn:server"]);
                break;
            case "endpoint":
                context.Endpoint.EndpointUrl = "opc.tcp://localhost:4840/Other";
                break;
            case "security":
                context.Endpoint.SecurityMode = MessageSecurityMode.None;
                break;
            case "disconnected":
                context.Connected = false;
                break;
            case "expired":
                context.UtcNow += TimeSpan.FromMinutes(5);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }
}

internal sealed class CompanionTypedTaskTestContext : IAsyncDisposable
{
    public CompanionTypedTaskTestContext(
        ITelemetryContext? telemetry = null,
        CompanionOperationSafety safety = CompanionOperationSafety.ReadOnly,
        string endpoint = "opc.tcp://localhost:4840/Sample")
    {
        Telemetry = telemetry ?? DefaultTelemetry.Create(static _ => { });
        Endpoint = new EndpointDescription
        {
            EndpointUrl = endpoint,
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
        };
        Session.SetupGet(value => value.Connected).Returns(() => Connected);
        Session.SetupGet(value => value.SessionId).Returns(() => SessionId);
        Session.SetupGet(value => value.Identity).Returns(() => Identity);
        Session.SetupGet(value => value.NamespaceUris).Returns(NamespaceUris);
        Session.SetupGet(value => value.Endpoint).Returns(Endpoint);
        Clock.Setup(value => value.GetUtcNow()).Returns(() => UtcNow);
        Operation = new CompanionOperation("typed", "Apply typed settings", safety)
        {
            Inputs =
            [
                new("label", "Label", BuiltInType.String, "Package label"),
                new("enabled", "Enabled", BuiltInType.Boolean, "Enable the option"),
                new("count", "Count", BuiltInType.UInt32, "Unsigned count"),
                new("delta", "Delta", BuiltInType.Int32, "Signed adjustment"),
                new("gain", "Gain", BuiltInType.Double, "Invariant gain")
            ]
        };
        Inspection = new CompanionInspection(
            [new("Current count", Variant.From(7u))], [Operation], "Typed inspection.");
        Provider.SetupGet(value => value.Descriptor).Returns(
            new CompanionDescriptor("typed-provider", "Typed model", "urn:typed", "Test model"));
        Provider.Setup(value => value.DiscoverAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ArrayOf<CompanionTarget>>([Target]));
        Provider.Setup(value => value.InspectAsync(
                It.IsAny<CompanionContext>(), Target, It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(Inspection));
        Provider.Setup(value => value.PrepareInputAsync(
                It.IsAny<CompanionContext>(), Target, "typed",
                It.IsAny<ArrayOf<CompanionValue>>(), It.IsAny<CancellationToken>()))
            .Returns((CompanionContext _, CompanionTarget _, string _, ArrayOf<CompanionValue> inputs,
                CancellationToken token) =>
            {
                CapturedInputs = inputs;
                PrepareToken = token;
                return PrepareHandler?.Invoke(inputs, token) ?? ValueTask.FromResult(PreparedInput);
            });
        Provider.Setup(value => value.ExecutePreparedAsync(
                It.IsAny<CompanionContext>(), Target, "typed", It.IsAny<CompanionTaskInput>(),
                It.IsAny<IProgress<CompanionTaskProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(Result));
        Workspace = new CompanionWorkspace([Provider.Object], Telemetry, Clock.Object);
    }

    public ITelemetryContext Telemetry { get; }

    public Mock<ISession> Session { get; } = new(MockBehavior.Strict);

    public Mock<IPreparedCompanionProvider> Provider { get; } = new(MockBehavior.Strict);

    public Mock<TimeProvider> Clock { get; } = new(MockBehavior.Strict);

    public CompanionWorkspace Workspace { get; }

    public CompanionTarget Target { get; } = new("typed-provider", new NodeId(1234u, 2), "Typed device", "Device");

    public CompanionOperation Operation { get; }

    public CompanionInspection Inspection { get; set; }

    public CompanionTaskInput PreparedInput { get; set; } = new CompanionTestTaskInput("Reviewed typed settings.");

    public CompanionOperationResult Result { get; } =
        new("Typed operation completed.", [new("Accepted count", Variant.From(19u))]);

    public ArrayOf<CompanionValue> Inputs { get; } =
    [
        new("label", Variant.From("unreviewed-label")),
        new("enabled", Variant.From(true)),
        new("count", Variant.From(19u)),
        new("delta", Variant.From(-3)),
        new("gain", Variant.From(2.5))
    ];

    public ArrayOf<CompanionValue> CapturedInputs { get; private set; }

    public CancellationToken PrepareToken { get; private set; }

    public Func<ArrayOf<CompanionValue>, CancellationToken, ValueTask<CompanionTaskInput>>? PrepareHandler { get; set; }

    public NamespaceTable NamespaceUris { get; } = new([Namespaces.OpcUa, "urn:server", "urn:typed"]);

    public EndpointDescription Endpoint { get; }

    public bool Connected { get; set; } = true;

    public NodeId SessionId { get; set; } = new(101u);

    public IUserIdentity Identity { get; set; } = new Mock<IUserIdentity>(MockBehavior.Strict).Object;

    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await Workspace.BindAsync(Session.Object).ConfigureAwait(false);
        await Workspace.DiscoverAsync("typed-provider").ConfigureAwait(false);
        await Workspace.InspectAsync(Target).ConfigureAwait(false);
    }

    public Task<CompanionOperationDraft> PrepareAsync()
    {
        return Workspace.PrepareTaskAsync(Target, "typed", Inputs);
    }

    public void VerifyCalls(int preparations, int executions)
    {
        Provider.Verify(value => value.PrepareInputAsync(
            It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<string>(),
            It.IsAny<ArrayOf<CompanionValue>>(), It.IsAny<CancellationToken>()), Times.Exactly(preparations));
        Provider.Verify(value => value.ExecutePreparedAsync(
            It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<string>(),
            It.IsAny<CompanionTaskInput>(), It.IsAny<IProgress<CompanionTaskProgress>?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(executions));
        Provider.Verify(value => value.ExecuteAsync(
            It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public void VerifyInspections(int count)
    {
        Provider.Verify(value => value.InspectAsync(
            It.IsAny<CompanionContext>(), Target, It.IsAny<CancellationToken>()), Times.Exactly(count));
    }

    public ValueTask DisposeAsync()
    {
        return Workspace.DisposeAsync();
    }
}

internal sealed class CompanionTestTaskInput(string review) : CompanionTaskInput
{
    public override string Review => review;
}
