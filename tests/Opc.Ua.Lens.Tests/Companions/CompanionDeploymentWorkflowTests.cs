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
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Identity;
using UaLens.Connection;
using UaLens.Plugins.Companions;
using UaLens.Tests.Desktop;
using UaLens.Tests.Observe;
using UaLens.ViewModels;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class CompanionDeploymentWorkflowTests
    {
        [Test]
        public async Task AuthorizedDeploymentPreparesWithoutMutationAndDispatchesExactlyOnceAsync()
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);

                CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                    context.Target, "apply", CompanionDeploymentTestData.Input).ConfigureAwait(false);

                Assert.That(draft.DeploymentGrant?.RuleId, Is.EqualTo("plant-rule"));
                Assert.That(draft.DeploymentGrant?.Revision, Is.EqualTo("revision-7"));
                Assert.That(draft.ExpiresAt, Is.EqualTo(context.UtcNow.AddMinutes(2)));
                Assert.That(draft.Target, Is.SameAs(context.Target));
                Assert.That(draft.Operation, Is.SameAs(context.Operation));
                Assert.That(draft.Summary, Does.Contain("plant-rule").And.Contain("revision-7"));
                Assert.That(draft.Summary, Does.Not.Contain("recipe=7").And.Not.Contain("query-marker"));
                context.VerifyInspectionCount(2);
                context.VerifyExecutionCount(0);
                VerifyAuthorizationCount(policy, 1);

                CompanionOperationResult result = await context.Workspace.ExecuteAsync(
                    draft, false, confirmDeployment: true).ConfigureAwait(false);

                AssertResult(result);
                context.VerifyExecution(CompanionDeploymentTestData.Input);
                context.VerifyExecutionCount(1);
                context.VerifyInspectionCount(4);
                VerifyAuthorizationCount(policy, 2);
                await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                context.VerifyExecutionCount(1);
            }
        }

        [Test]
        public async Task DefaultWorkspaceDeniesPreparationAndDirectExecutionAsync()
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                await context.InitializeAsync().ConfigureAwait(false);

                await Assert.ThatAsync(
                    () => context.Workspace.PrepareAsync(context.Target, "apply", CompanionDeploymentTestData.Input),
                    Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("No configured"))
                    .ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteAsync(
                        context.Target, "apply", CompanionDeploymentTestData.Input, true),
                    Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("freshly prepared"))
                    .ConfigureAwait(false);

                context.VerifyExecutionCount(0);
                context.VerifyInspectionCount(2);
            }
        }

        [Test]
        public async Task DirectExecutionCannotBypassAnAllowingDeploymentPolicyAsync()
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                    context.Target, "apply", CompanionDeploymentTestData.Input).ConfigureAwait(false);

                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteAsync(
                        context.Target, "apply", CompanionDeploymentTestData.Input, true),
                    Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("freshly prepared"))
                    .ConfigureAwait(false);

                await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                context.VerifyExecutionCount(0);
                context.VerifyInspectionCount(2);
                VerifyAuthorizationCount(policy, 1);
            }
        }

        [TestCase("read-only", false, false)]
        [TestCase("read-only", false, true)]
        [TestCase("local-file", false, false)]
        [TestCase("local-file", false, true)]
        [TestCase("sample", true, false)]
        [TestCase("sample", true, true)]
        [TestCase("sample", false, false)]
        [TestCase("sample", false, true)]
        public async Task LegacySafetyModesRetainTheirIndependentGatesAsync(
            string kind, bool confirmSample, bool confirmDeployment)
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionOperationSafety safety = kind switch
            {
                "read-only" => CompanionOperationSafety.ReadOnly,
                "local-file" => CompanionOperationSafety.LocalFile,
                "sample" => CompanionOperationSafety.SampleMutation,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                policy.Object, safety: safety,
                endpoint: kind == "sample" ? "opc.tcp://localhost:4840/Sample" : CompanionDeploymentTestData.Endpoint);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);
                string input = kind == "local-file" ? @"D:\exports\deployment report.json" : "recipe=7";
                CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                    context.Target, "apply", input).ConfigureAwait(false);
                Assert.That(draft.DeploymentGrant, Is.Null);
                Assert.That(draft.ExpiresAt, Is.EqualTo(context.UtcNow.AddMinutes(5)));
                context.VerifyExecutionCount(0);

                if (kind == "sample" && !confirmSample)
                {
                    await Assert.ThatAsync(
                        () => context.Workspace.ExecuteAsync(
                            draft, confirmSample, confirmDeployment: confirmDeployment),
                        Throws.InvalidOperationException.With.Message.Contains("explicit confirmation"))
                        .ConfigureAwait(false);
                    context.VerifyExecutionCount(0);
                }
                else
                {
                    CompanionOperationResult result = await context.Workspace.ExecuteAsync(
                        draft, confirmSample, confirmDeployment: confirmDeployment).ConfigureAwait(false);
                    AssertResult(result);
                    context.VerifyExecution(input);
                    context.VerifyExecutionCount(1);
                }
                await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                VerifyAuthorizationCount(policy, 0);
                context.VerifyInspectionCount(3);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public async Task LocalFileStillRequiresADestinationDespiteDeploymentConfirmationAsync(string? destination)
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                policy.Object, safety: CompanionOperationSafety.LocalFile);
            await using (context.ConfigureAwait(false))
            {
                await context.InitializeAsync().ConfigureAwait(false);

                await Assert.ThatAsync(
                    () => context.Workspace.PrepareAsync(context.Target, "apply", destination),
                    Throws.ArgumentException.With.Message.Contains("destination")).ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteAsync(context.Target, "apply", destination, true),
                    Throws.ArgumentException.With.Message.Contains("destination")).ConfigureAwait(false);

                VerifyAuthorizationCount(policy, 0);
                context.VerifyExecutionCount(0);
                context.VerifyInspectionCount(1);
            }
        }

        [Test]
        public async Task DeploymentRuleDoesNotUnlockRemoteSampleMutationAsync()
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                policy.Object, safety: CompanionOperationSafety.SampleMutation);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                    context.Target, "apply", CompanionDeploymentTestData.Input).ConfigureAwait(false);

                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteAsync(draft, true, confirmDeployment: true),
                    Throws.InvalidOperationException.With.Message.Contains("loopback")).ConfigureAwait(false);

                Assert.That(draft.DeploymentGrant, Is.Null);
                await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                VerifyAuthorizationCount(policy, 0);
                context.VerifyExecutionCount(0);
            }
        }

        [TestCase("missing-confirmation")]
        [TestCase("already-canceled")]
        [TestCase("denied")]
        public async Task EveryFailedRunConsumesPreparationBeforeReplayAsync(string failure)
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                    context.Target, "apply", CompanionDeploymentTestData.Input).ConfigureAwait(false);
                using var cancellation = new CancellationTokenSource();
                if (failure == "already-canceled")
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                }
                if (failure == "denied")
                {
                    policy.Setup(value => value.AuthorizeAsync(
                            It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(),
                            It.IsAny<CancellationToken>()))
                        .Returns(() => ValueTask.FromException<CompanionDeploymentGrant>(
                            new UnauthorizedAccessException("Rule revoked.")));
                }

                if (failure == "already-canceled")
                {
                    await Assert.ThatAsync(
                        () => context.Workspace.ExecuteAsync(
                            draft, true, confirmDeployment: true, cancellationToken: cancellation.Token),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(
                        () => context.Workspace.ExecuteAsync(
                            draft, true, confirmDeployment: failure != "missing-confirmation",
                            cancellationToken: cancellation.Token),
                        Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                }

                await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                VerifyAuthorizationCount(policy, failure == "denied" ? 2 : 1);
                context.VerifyExecutionCount(0);
                context.VerifyInspectionCount(failure == "already-canceled" ? 2 : 3);
            }
        }

        [Test]
        public async Task RunConsumesTheDraftBeforeAsynchronousAuthorizationCompletesAsync()
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                    context.Target, "apply", CompanionDeploymentTestData.Input).ConfigureAwait(false);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                policy.Setup(value => value.AuthorizeAsync(
                        It.IsAny<CompanionContext>(), draft, It.IsAny<CancellationToken>()))
                    .Returns(async (CompanionContext _, CompanionOperationDraft _, CancellationToken token) =>
                    {
                        entered.TrySetResult();
                        await release.Task.WaitAsync(s_timeout, token).ConfigureAwait(false);
                        return draft.DeploymentGrant!;
                    });

                Task<CompanionOperationResult> running = context.Workspace.ExecuteAsync(
                    draft, false, confirmDeployment: true);
                try
                {
                    await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                    await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                    Assert.That(running.IsCompleted, Is.False);
                    context.VerifyExecutionCount(0);
                    VerifyAuthorizationCount(policy, 2);
                }
                finally
                {
                    release.TrySetResult();
                }

                AssertResult(await running.WaitAsync(s_timeout).ConfigureAwait(false));
                await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                context.VerifyExecutionCount(1);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnconfirmedRunIsConsumedWhileItsFreshInspectionIsStillPendingAsync(bool useTaskOverload)
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                    context.Target, "apply", CompanionDeploymentTestData.Input).ConfigureAwait(false);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.Provider.Setup(value => value.InspectAsync(
                        It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                    .Returns(async (CompanionContext _, CompanionTarget _, CancellationToken token) =>
                    {
                        entered.TrySetResult();
                        await release.Task.WaitAsync(s_timeout, token).ConfigureAwait(false);
                        return context.Inspection;
                    });
                Task<CompanionOperationResult> running = useTaskOverload
                    ? context.Workspace.ExecuteTaskAsync(draft, true, progress: null)
                    : context.Workspace.ExecuteAsync(draft, true);
                try
                {
                    await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                    await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                    Assert.That(running.IsCompleted, Is.False);
                    context.VerifyExecutionCount(0);
                    VerifyAuthorizationCount(policy, 1);
                }
                finally
                {
                    release.TrySetResult();
                }

                await Assert.ThatAsync(
                    () => running.WaitAsync(s_timeout),
                    Throws.TypeOf<UnauthorizedAccessException>().With.Message
                        .Contains("Confirm")).ConfigureAwait(false);
                await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                context.VerifyExecutionCount(0);
                VerifyAuthorizationCount(policy, 1);
            }
        }

        [TestCase("rule")]
        [TestCase("rule-case")]
        [TestCase("revision")]
        [TestCase("revision-case")]
        [TestCase("null")]
        [TestCase("expired")]
        [TestCase("deadline")]
        [TestCase("overlong")]
        [TestCase("empty-rule")]
        [TestCase("invalid-revision")]
        public async Task ChangedOrMalformedRunGrantsDenyWithoutDispatchAsync(string fault)
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                    context.Target, "apply", CompanionDeploymentTestData.Input).ConfigureAwait(false);
                ReturnGrant(policy, FaultyGrant(context, draft.ExpiresAt, fault));

                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteAsync(draft, false, confirmDeployment: true),
                    fault is "empty-rule" or "invalid-revision"
                        ? Throws.ArgumentException
                        : Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

                await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                context.VerifyInspectionCount(3);
                context.VerifyExecutionCount(0);
                VerifyAuthorizationCount(policy, 2);
            }
        }

        [TestCase("null")]
        [TestCase("expired")]
        [TestCase("deadline")]
        [TestCase("overlong")]
        [TestCase("empty-rule")]
        [TestCase("invalid-revision")]
        public async Task MalformedPreparationGrantsNeverCreateARunnableDraftAsync(string fault)
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft previous = await context.Workspace.PrepareAsync(
                    context.Target, "apply", CompanionDeploymentTestData.Input).ConfigureAwait(false);
                ReturnGrant(policy, FaultyGrant(context, context.UtcNow.AddMinutes(5), fault));

                await Assert.ThatAsync(
                    () => context.Workspace.PrepareAsync(context.Target, "apply", CompanionDeploymentTestData.Input),
                    fault is "empty-rule" or "invalid-revision"
                        ? Throws.ArgumentException
                        : Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

                await AssertConsumedAsync(context, previous).ConfigureAwait(false);
                context.VerifyExecutionCount(0);
                context.VerifyInspectionCount(3);
                VerifyAuthorizationCount(policy, 2);
            }
        }

        [TestCase("none")]
        [TestCase("sign")]
        [TestCase("invalid")]
        [TestCase("none-policy")]
        [TestCase("empty-policy")]
        [TestCase("null-policy")]
        public async Task InsecureChannelRejectsEvenACustomGrantBeforePolicyEvaluationAsync(string channel)
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                context.Endpoint.SecurityMode = channel switch
                {
                    "none" => MessageSecurityMode.None,
                    "sign" => MessageSecurityMode.Sign,
                    "invalid" => MessageSecurityMode.Invalid,
                    _ => MessageSecurityMode.SignAndEncrypt
                };
                context.Endpoint.SecurityPolicyUri = channel switch
                {
                    "none-policy" => SecurityPolicies.None,
                    "empty-policy" => string.Empty,
                    "null-policy" => null!,
                    _ => SecurityPolicies.Basic256Sha256
                };
                ReturnGrant(policy, new CompanionDeploymentGrant(
                    "plant-rule", "revision-7", context.UtcNow.AddMinutes(1)));
                await context.InitializeAsync().ConfigureAwait(false);

                await Assert.ThatAsync(
                    () => context.Workspace.PrepareAsync(context.Target, "apply", CompanionDeploymentTestData.Input),
                    Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("signed and encrypted"))
                    .ConfigureAwait(false);

                VerifyAuthorizationCount(policy, 0);
                context.VerifyExecutionCount(0);
                context.VerifyInspectionCount(2);
            }
        }

        [TestCase("identity", true)]
        [TestCase("identity", false)]
        [TestCase("application", true)]
        [TestCase("application", false)]
        [TestCase("session", true)]
        [TestCase("session", false)]
        [TestCase("namespace", true)]
        [TestCase("namespace", false)]
        [TestCase("namespace-order", true)]
        [TestCase("namespace-order", false)]
        [TestCase("endpoint", true)]
        [TestCase("endpoint", false)]
        [TestCase("mode", true)]
        [TestCase("mode", false)]
        [TestCase("policy", true)]
        [TestCase("policy", false)]
        [TestCase("disconnected", true)]
        [TestCase("disconnected", false)]
        [TestCase("expired", true)]
        [TestCase("expired", false)]
        [TestCase("rebind", true)]
        [TestCase("rebind", false)]
        [TestCase("stop", true)]
        [TestCase("stop", false)]
        [TestCase("caller-canceled", true)]
        [TestCase("caller-canceled", false)]
        public async Task DelayedAuthorizationCannotApproveChangedSessionOrCanceledWorkAsync(
            string change, bool duringPreparation)
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft? draft = duringPreparation ? null :
                    await context.Workspace.PrepareAsync(
                        context.Target, "apply", CompanionDeploymentTestData.Input).ConfigureAwait(false);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                policy.Setup(value => value.AuthorizeAsync(
                        It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It
                            .IsAny<CancellationToken>()))
                    .Returns(async (CompanionContext _, CompanionOperationDraft request, CancellationToken token) =>
                    {
                        using CancellationTokenRegistration registration = token.Register(
                            () => canceled.TrySetResult());
                        entered.TrySetResult();
                        await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                        return new CompanionDeploymentGrant("plant-rule", "revision-7", request.ExpiresAt);
                    });
                using var cancellation = new CancellationTokenSource();
                Task pending = duringPreparation
                    ? context.Workspace.PrepareAsync(
                        context.Target, "apply", CompanionDeploymentTestData.Input, cancellation.Token)
                    : context.Workspace.ExecuteAsync(
                        draft!, false, confirmDeployment: true, cancellationToken: cancellation.Token);
                Task reset = Task.CompletedTask;
                try
                {
                    await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                    switch (change)
                    {
                        case "expired":
                            context.UtcNow = context.UtcNow.AddMinutes(5);
                            break;
                        case "rebind":
                            reset = context.Workspace.BindAsync(context.Session.Object);
                            await canceled.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                            break;
                        case "stop":
                            reset = context.Workspace.CancelAsync();
                            await canceled.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                            break;
                        case "caller-canceled":
                            await cancellation.CancelAsync().ConfigureAwait(false);
                            await canceled.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                            break;
                        default:
                            CompanionDeploymentTestData.ChangeSession(context, change);
                            break;
                    }
                    context.VerifyExecutionCount(0);
                }
                finally
                {
                    release.TrySetResult();
                }

                await Assert.ThatAsync(
                    () => pending.WaitAsync(s_timeout),
                    change is "rebind" or "stop" or "caller-canceled"
                        ? Throws.InstanceOf<OperationCanceledException>()
                        : Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                await reset.WaitAsync(s_timeout).ConfigureAwait(false);
                if (draft is not null)
                {
                    await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                }
                context.VerifyExecutionCount(0);
                context.VerifyInspectionCount(duringPreparation ? 2 : 3);
                VerifyAuthorizationCount(policy, duringPreparation ? 1 : 2);
            }
        }

        [Test]
        public async Task SupersededPreparationCannotPublishAuthorizationAsync()
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                await context.InitializeAsync().ConfigureAwait(false);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                int authorizations = 0;
                policy.Setup(value => value.AuthorizeAsync(
                        It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It
                            .IsAny<CancellationToken>()))
                    .Returns(async (CompanionContext _, CompanionOperationDraft request, CancellationToken token) =>
                    {
                        if (Interlocked.Increment(ref authorizations) == 1)
                        {
                            entered.TrySetResult();
                            await release.Task.WaitAsync(s_timeout, token).ConfigureAwait(false);
                        }
                        return new CompanionDeploymentGrant("plant-rule", "revision-7", request.ExpiresAt);
                    });
                Task<CompanionOperationDraft> first = context.Workspace
                    .PrepareAsync(context.Target, "apply", "recipe=7");
                Task<CompanionOperationDraft>? second = null;
                try
                {
                    await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                    second = context.Workspace.PrepareAsync(context.Target, "apply", "recipe=9");
                    context.VerifyExecutionCount(0);
                }
                finally
                {
                    release.TrySetResult();
                }

                await Assert.ThatAsync(() => first.WaitAsync(s_timeout),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                CompanionOperationDraft replacement = await (second ??
                    throw new AssertionException("The replacement preparation was not queued."))
                    .WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(replacement.Input, Is.EqualTo("recipe=9"));
                Assert.That(replacement.DeploymentGrant?.RuleId, Is.EqualTo("plant-rule"));
                AssertResult(await context.Workspace.ExecuteAsync(
                    replacement, false, confirmDeployment: true).ConfigureAwait(false));
                context.VerifyExecution("recipe=9");
                context.VerifyExecutionCount(1);
                context.VerifyInspectionCount(5);
                VerifyAuthorizationCount(policy, 3);
            }
        }

        [TestCase("removed", 0)]
        [TestCase("metadata", 0)]
        [TestCase("identity", 0)]
        [TestCase("canceled", 0)]
        [TestCase("draft-expiry", -1)]
        [TestCase("draft-expiry", 0)]
        [TestCase("draft-expiry", 1)]
        [TestCase("grant-expiry", -1)]
        [TestCase("grant-expiry", 0)]
        [TestCase("grant-expiry", 1)]
        public async Task FinalInspectionAfterAuthorizationRejectsRemovedOperationsAndExpiryAsync(
            string change, int ticksFromDeadline)
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                    context.Target, "apply", CompanionDeploymentTestData.Input).ConfigureAwait(false);
                DateTimeOffset runDeadline = change == "draft-expiry"
                    ? draft.ExpiresAt
                    : context.UtcNow.AddSeconds(30);
                ReturnGrant(policy, new CompanionDeploymentGrant("plant-rule", "revision-7", runDeadline));
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                int inspections = 0;
                context.Provider.Setup(value => value.InspectAsync(
                        It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                    .Returns(async (CompanionContext _, CompanionTarget _, CancellationToken _) =>
                    {
                        if (Interlocked.Increment(ref inspections) == 2)
                        {
                            entered.TrySetResult();
                            await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                        }
                        return context.Inspection;
                    });
                using var cancellation = new CancellationTokenSource();
                Task<CompanionOperationResult> running = context.Workspace.ExecuteAsync(
                    draft, false, confirmDeployment: true, cancellationToken: cancellation.Token);
                try
                {
                    await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                    VerifyAuthorizationCount(policy, 2);
                    context.VerifyExecutionCount(0);
                    switch (change)
                    {
                        case "removed":
                            context.Inspection = context.Inspection with { Operations = [] };
                            break;
                        case "metadata":
                            context.Inspection = context.Inspection with
                            {
                                Operations = [context.Operation with { DisplayName = "Changed operation" }]
                            };
                            break;
                        case "identity":
                            CompanionDeploymentTestData.ChangeSession(context, "identity");
                            break;
                        case "canceled":
                            await cancellation.CancelAsync().ConfigureAwait(false);
                            break;
                        default:
                            context.UtcNow = runDeadline.AddTicks(ticksFromDeadline);
                            break;
                    }
                }
                finally
                {
                    release.TrySetResult();
                }

                bool allowed = (change is "draft-expiry" or "grant-expiry") && ticksFromDeadline < 0;
                if (allowed)
                {
                    AssertResult(await running.WaitAsync(s_timeout).ConfigureAwait(false));
                    context.VerifyExecution(CompanionDeploymentTestData.Input);
                }
                else if (change == "canceled")
                {
                    await Assert.ThatAsync(() => running.WaitAsync(s_timeout),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                }
                else if (change == "grant-expiry")
                {
                    await Assert.ThatAsync(() => running.WaitAsync(s_timeout),
                        Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(() => running.WaitAsync(s_timeout),
                        Throws.InvalidOperationException.With.Message.Contains("changed")).ConfigureAwait(false);
                }
                await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                context.VerifyExecutionCount(allowed ? 1 : 0);
                context.VerifyInspectionCount(4);
                Assert.That(inspections, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task WorkspaceDisposalDrainsAuthorizationWithoutDisposingBorrowedSessionAsync()
        {
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(policy.Object);
            await using (context.ConfigureAwait(false))
            {
                ConfigurePolicy(policy, context);
                await context.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft draft = await context.Workspace.PrepareAsync(
                    context.Target, "apply", CompanionDeploymentTestData.Input).ConfigureAwait(false);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                policy.Setup(value => value.AuthorizeAsync(
                        It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It
                            .IsAny<CancellationToken>()))
                    .Returns(async (CompanionContext _, CompanionOperationDraft _, CancellationToken token) =>
                    {
                        using CancellationTokenRegistration registration = token.Register(
                            () => canceled.TrySetResult());
                        entered.TrySetResult();
                        await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                        return draft.DeploymentGrant!;
                    });
                Task<CompanionOperationResult> running = context.Workspace.ExecuteAsync(
                    draft, false, confirmDeployment: true);
                Task disposal = Task.CompletedTask;
                try
                {
                    await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                    disposal = context.Workspace.DisposeAsync().AsTask();
                    await canceled.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                    Assert.That(disposal.IsCompleted, Is.False);
                }
                finally
                {
                    release.TrySetResult();
                }

                await Assert.ThatAsync(() => running.WaitAsync(s_timeout),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                await disposal.WaitAsync(s_timeout).ConfigureAwait(false);
                await context.Workspace.DisposeAsync().ConfigureAwait(false);
                Assert.That(() => context.Workspace.DiscoverAsync("sample"), Throws.TypeOf<ObjectDisposedException>());
                context.VerifyExecutionCount(0);
                context.Session.Verify(value => value.Dispose(), Times.Never);
            }
        }

        [Test]
        public async Task TypedDeploymentSnapshotsAuthorizedInputsAndDispatchesOnlyPreparedDomainInputAsync()
        {
            var context = new CompanionTypedTaskTestContext(
                safety: CompanionOperationSafety.DeploymentMutation, endpoint: CompanionDeploymentTestData.Endpoint);
            await using (context.ConfigureAwait(false))
            {
                context.Endpoint.Server = new ApplicationDescription
                {
                    ApplicationUri = CompanionDeploymentTestData.ApplicationUri
                };
                IUserIdentity originalIdentity = context.Identity;
                var rule = new CompanionDeploymentRule(
                    "typed-rule", "revision-7", CompanionDeploymentTestData.Endpoint,
                    CompanionDeploymentTestData.ApplicationUri, SecurityPolicies.Basic256Sha256,
                    "typed-provider", new ExpandedNodeId(1234u, "urn:typed"), "typed", context.UtcNow.AddMinutes(1),
                    identity => ReferenceEquals(identity, originalIdentity), HasExpectedTypedInputs);
                var configured = new ConfiguredCompanionDeploymentPolicy([rule], context.Clock.Object);
                var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                policy.Setup(value => value.AuthorizeAsync(
                        It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It
                            .IsAny<CancellationToken>()))
                    .Returns((CompanionContext borrowed, CompanionOperationDraft request, CancellationToken token) =>
                        configured.AuthorizeAsync(borrowed, request, token));
                var workspace = new CompanionWorkspace(
                    [context.Provider.Object], context.Telemetry, context.Clock.Object, policy.Object);
                await using (workspace.ConfigureAwait(false))
                {
                    await workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
                    await workspace.DiscoverAsync("typed-provider").ConfigureAwait(false);
                    await workspace.InspectAsync(context.Target).ConfigureAwait(false);
                    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    context.Provider.Setup(value => value.InspectAsync(
                            It.IsAny<CompanionContext>(), context.Target, It.IsAny<CancellationToken>()))
                        .Returns(async (CompanionContext _, CompanionTarget _, CancellationToken token) =>
                        {
                            entered.TrySetResult();
                            await release.Task.WaitAsync(s_timeout, token).ConfigureAwait(false);
                            return context.Inspection;
                        });
                    CompanionValue[] source = context.Inputs.ToArray()!;
                    Task<CompanionOperationDraft> preparing = workspace.PrepareTaskAsync(
                        context.Target, "typed", ArrayOf.Wrapped(source));
                    try
                    {
                        await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                        source[0] = new CompanionValue("label", Variant.From("changed-before-authorization"));
                        source[2] = new CompanionValue("count", Variant.From(999u));
                        context.VerifyCalls(0, 0);
                    }
                    finally
                    {
                        release.TrySetResult();
                    }
                    CompanionOperationDraft draft = await preparing.WaitAsync(s_timeout).ConfigureAwait(false);
                    ArrayOf<CompanionValue> returned = draft.Inputs;
                    Assert.That(MemoryMarshal.TryGetArray(
                        returned.Memory, out ArraySegment<CompanionValue> segment), Is.True);
                    CompanionValue[] storage = segment.Array ??
                        throw new AssertionException("Expected array-backed returned task fields.");
                    storage[segment.Offset] = new CompanionValue("label", Variant.From("changed-after-authorization"));
                    storage[segment.Offset + 2] = new CompanionValue("count", Variant.From(555u));
                    Assert.That(HasExpectedTypedInputs(draft), Is.True);
                    Assert.That(context.CapturedInputs[2].Value.TryGetValue(out uint capturedCount), Is.True);
                    Assert.That(capturedCount, Is.EqualTo(19u));
                    Assert.That(draft.TaskInput, Is.SameAs(context.PreparedInput));
                    Assert.That(draft.DeploymentGrant?.RuleId, Is.EqualTo("typed-rule"));
                    Assert.That(draft.Summary, Does.Not.Contain("unreviewed-label").And.Not.Contain("query-marker"));
                    Assert.That(draft.ExpiresAt, Is.EqualTo(context.UtcNow.AddMinutes(1)));
                    context.VerifyCalls(1, 0);
                    var progress = new Mock<IProgress<CompanionTaskProgress>>(MockBehavior.Strict);
                    progress.Setup(value => value.Report(
                        It.Is<CompanionTaskProgress>(update => update.Phase == "Applying reviewed input" &&
                            update.Percent == 37)));
                    context.Provider.Setup(value => value.ExecutePreparedAsync(
                            It.IsAny<CompanionContext>(), context.Target, "typed", context.PreparedInput,
                            progress.Object, It.IsAny<CancellationToken>()))
                        .Returns((CompanionContext borrowed, CompanionTarget target, string operation,
                            CompanionTaskInput input, IProgress<CompanionTaskProgress>? reporter,
                            CancellationToken token) =>
                        {
                            Assert.That(borrowed.Session, Is.SameAs(context.Session.Object));
                            Assert.That(target, Is.SameAs(context.Target));
                            Assert.That(operation, Is.EqualTo("typed"));
                            Assert.That(input, Is.SameAs(draft.TaskInput));
                            Assert.That(token.CanBeCanceled, Is.True);
                            reporter?.Report(new CompanionTaskProgress("Applying reviewed input", 37));
                            return ValueTask.FromResult(context.Result);
                        });

                    CompanionOperationResult result = await workspace.ExecuteTaskAsync(
                        draft, false, confirmDeployment: true, progress: progress.Object).ConfigureAwait(false);

                    Assert.That(result.Summary, Is.EqualTo("Typed operation completed."));
                    Assert.That(result.Values[0].Name, Is.EqualTo("Accepted count"));
                    Assert.That(result.Values[0].Value.TryGetValue(out uint accepted), Is.True);
                    Assert.That(accepted, Is.EqualTo(19u));
                    progress.VerifyAll();
                    VerifyAuthorizationCount(policy, 2);
                    context.VerifyCalls(1, 1);
                    context.VerifyInspections(4);
                    await Assert.ThatAsync(
                        () => workspace.ExecuteTaskAsync(
                            draft, false, confirmDeployment: true, progress: progress.Object),
                        Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
                }
                context.Session.Verify(value => value.Dispose(), Times.Never);
            }
        }

        [Test]
        public async Task PluginRequiresFreshDeploymentConfirmationAndClearsItBeforeDispatchAsync()
        {
            var host = new ObserveTestHost();
            await using (host.ConfigureAwait(false))
            {
                var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                    policy.Object, host.Telemetry);
                ConfigurePolicy(policy, context);
                var document = new CompanionPlugin(host.Host, context.Workspace);
                await using (document.ConfigureAwait(false))
                {
                    await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                    document.ConfirmLocalSample = true;
                    document.ConfirmDeployment = true;
                    Assert.That(document.IsDeploymentOperation, Is.True);
                    Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);

                    await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                    Assert.That(document.PreparedOperation?.DeploymentGrant?.RuleId, Is.EqualTo("plant-rule"));
                    Assert.That(document.PreparedOperation?.Input, Is.EqualTo("recipe=7"));
                    Assert.That(document.PreparationSummary, Does.Contain("plant-rule").And.Contain("revision-7"));
                    Assert.That(document.ConfirmLocalSample, Is.False);
                    Assert.That(document.ConfirmDeployment, Is.False);
                    Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                    context.VerifyExecutionCount(0);
                    document.ConfirmLocalSample = true;
                    Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                    bool clearedBeforeDispatch = false;
                    context.Provider.Setup(value => value.ExecuteAsync(
                            It.IsAny<CompanionContext>(), context.Target, "apply",
                            CompanionDeploymentTestData.Input, It.IsAny<CancellationToken>()))
                        .Callback(() => clearedBeforeDispatch = document.PreparedOperation is null &&
                            !document.ConfirmDeployment &&
                            !document.ConfirmLocalSample)
                        .Returns(() => ValueTask.FromResult(context.Result));
                    document.ConfirmDeployment = true;
                    Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);

                    await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                    Assert.That(clearedBeforeDispatch, Is.True);
                    Assert.That(document.PreparedOperation, Is.Null);
                    Assert.That(document.ConfirmDeployment, Is.False);
                    Assert.That(document.ConfirmLocalSample, Is.False);
                    Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                    Assert.That(document.Status, Is.EqualTo("Operation complete."));
                    Assert.That(document.Values, Has.Count.EqualTo(1));
                    Assert.That(document.Values[0].Name, Is.EqualTo("Processed"));
                    Assert.That(document.Values[0].Value.TryGetValue(out uint processed), Is.True);
                    Assert.That(processed, Is.EqualTo(17u));
                    Assert.That(document.IsBusy, Is.False);
                    context.VerifyExecution(CompanionDeploymentTestData.Input);
                    context.VerifyExecutionCount(1);
                    context.VerifyInspectionCount(4);
                    VerifyAuthorizationCount(policy, 2);

                    document.ConfirmDeployment = true;
                    await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                    Assert.That(document.Status, Does.Contain("Prepare the selected operation"));
                    Assert.That(document.ConfirmDeployment, Is.False);
                    context.VerifyExecutionCount(1);
                }
                context.Session.Verify(value => value.Dispose(), Times.Never);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task PluginPolicyDenialNeverLeavesARunnableOrConfirmedDraftAsync(bool denyDuringPreparation)
        {
            var host = new ObserveTestHost();
            await using (host.ConfigureAwait(false))
            {
                var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                    policy.Object, host.Telemetry);
                ConfigurePolicy(policy, context);
                var document = new CompanionPlugin(host.Host, context.Workspace);
                await using (document.ConfigureAwait(false))
                {
                    await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                    if (!denyDuringPreparation)
                    {
                        await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                        Assert.That(document.PreparedOperation?.DeploymentGrant?.RuleId, Is.EqualTo("plant-rule"));
                    }
                    policy.Setup(value => value.AuthorizeAsync(
                            It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(),
                            It.IsAny<CancellationToken>()))
                        .Returns(() => ValueTask.FromException<CompanionDeploymentGrant>(
                            new UnauthorizedAccessException("Rule revoked.")));
                    document.ConfirmDeployment = true;
                    document.ConfirmLocalSample = true;

                    if (denyDuringPreparation)
                    {
                        await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                    }
                    else
                    {
                        await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                    }

                    Assert.That(document.Status,
                        Is.EqualTo(denyDuringPreparation ? "Prepare task: Rule revoked." : "Run task: Rule revoked."));
                    Assert.That(document.PreparedOperation, Is.Null);
                    Assert.That(document.ConfirmDeployment, Is.False);
                    Assert.That(document.ConfirmLocalSample, Is.False);
                    Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                    Assert.That(document.IsBusy, Is.False);
                    Assert.That(document.Values, Has.Count.EqualTo(1));
                    Assert.That(document.Values[0].Name, Is.EqualTo("Temperature"));
                    Assert.That(document.Values[0].Value.TryGetValue(out double temperature), Is.True);
                    Assert.That(temperature, Is.EqualTo(42.5));
                    context.VerifyExecutionCount(0);
                    context.VerifyInspectionCount(denyDuringPreparation ? 2 : 3);
                    VerifyAuthorizationCount(policy, denyDuringPreparation ? 1 : 2);
                }
            }
        }

        [TestCase("input")]
        [TestCase("operation")]
        [TestCase("target")]
        [TestCase("provider")]
        [TestCase("discover")]
        [TestCase("inspect")]
        [TestCase("stop")]
        [TestCase("restore")]
        [TestCase("connection")]
        public async Task PluginChangesDiscardDeploymentPreparationAndBothConfirmationsAsync(string change)
        {
            var host = new ObserveTestHost();
            await using (host.ConfigureAwait(false))
            {
                var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                    policy.Object, host.Telemetry);
                ConfigurePolicy(policy, context);
                AddAlternateSelections(context);
                var document = new CompanionPlugin(host.Host, context.Workspace);
                await using (document.ConfigureAwait(false))
                {
                    await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                    await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                    CompanionOperationDraft draft = document.PreparedOperation ??
                        throw new AssertionException("Expected an authorized deployment preparation.");
                    document.ConfirmDeployment = true;
                    document.ConfirmLocalSample = true;
                    Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);

                    switch (change)
                    {
                        case "discover":
                            await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(false);
                            break;
                        case "inspect":
                            await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(false);
                            break;
                        case "stop":
                            await document.CancelCommand.ExecuteAsync(null).ConfigureAwait(false);
                            break;
                        case "restore":
                            await document.RestoreStateAsync(DocumentState()).ConfigureAwait(false);
                            break;
                        case "connection":
                            await document.OnConnectionStateChangedAsync(default).ConfigureAwait(false);
                            break;
                        default:
                            ChangeDocumentSelection(document, change);
                            break;
                    }

                    Assert.That(document.PreparedOperation, Is.Null);
                    Assert.That(document.ConfirmDeployment, Is.False);
                    Assert.That(document.ConfirmLocalSample, Is.False);
                    Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                    Assert.That(document.PreparationSummary, Does.StartWith("Prepare the selected task"));
                    Assert.That(document.IsBusy, Is.False);
                    await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                    context.VerifyExecutionCount(0);
                    VerifyAuthorizationCount(policy, 1);
                    if (change == "stop")
                    {
                        Assert.That(document.Status, Does.StartWith("Stopped."));
                        Assert.That(document.Targets, Is.Empty);
                    }
                    else if (change == "connection")
                    {
                        Assert.That(document.IsOffline, Is.True);
                        Assert.That(document.Targets, Is.Empty);
                    }
                    else if (change == "restore")
                    {
                        Assert.That(document.Status, Does.Contain("no task or workload"));
                        Assert.That(document.OperationInput, Is.Empty);
                    }
                }
            }
        }

        [TestCase("input")]
        [TestCase("operation")]
        [TestCase("target")]
        [TestCase("provider")]
        public async Task PluginCannotRunAStaleIntentAfterDelayedPreparationAsync(string change)
        {
            var host = new ObserveTestHost();
            await using (host.ConfigureAwait(false))
            {
                var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                    policy.Object, host.Telemetry);
                AddAlternateSelections(context);
                var document = new CompanionPlugin(host.Host, context.Workspace);
                await using (document.ConfigureAwait(false))
                {
                    await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    policy.Setup(value => value.AuthorizeAsync(
                            It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(),
                            It.IsAny<CancellationToken>()))
                        .Returns(async (CompanionContext _, CompanionOperationDraft request, CancellationToken token) =>
                        {
                            entered.TrySetResult();
                            await release.Task.WaitAsync(s_timeout, token).ConfigureAwait(false);
                            return new CompanionDeploymentGrant("plant-rule", "revision-7", request.ExpiresAt);
                        });
                    Task preparing = document.PrepareTaskCommand.ExecuteAsync(null);
                    try
                    {
                        await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                        document.ConfirmDeployment = true;
                        document.ConfirmLocalSample = true;
                        ChangeDocumentSelection(document, change);
                        Assert.That(document.ConfirmDeployment, Is.False);
                        Assert.That(document.ConfirmLocalSample, Is.False);
                        await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                        Assert.That(document.Status, Does.Contain("Prepare the selected operation"));
                        context.VerifyExecutionCount(0);
                    }
                    finally
                    {
                        release.TrySetResult();
                    }

                    await preparing.WaitAsync(s_timeout).ConfigureAwait(false);
                    Assert.That(document.PreparedOperation, Is.Null);
                    Assert.That(document.ConfirmDeployment, Is.False);
                    Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                    Assert.That(document.IsBusy, Is.False);
                    Assert.That(document.Status, Does.Contain("canceled"));
                    document.ConfirmDeployment = true;
                    await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                    Assert.That(document.ConfirmDeployment, Is.False);
                    Assert.That(document.Status, Does.Contain("Prepare the selected operation"));
                    context.VerifyExecutionCount(0);
                    VerifyAuthorizationCount(policy, 1);
                }
            }
        }

        [Test]
        public async Task PluginRestoreIgnoresInjectedAuthorizationAndDoesNotActivateProvidersAsync()
        {
            var host = new ObserveTestHost();
            await using (host.ConfigureAwait(false))
            {
                var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                    policy.Object, host.Telemetry);
                ConfigurePolicy(policy, context);
                var document = new CompanionPlugin(host.Host, context.Workspace);
                await using (document.ConfigureAwait(false))
                {
                    await InitializeDocumentAsync(document, context).ConfigureAwait(false);
                    await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                    CompanionOperationDraft draft = document.PreparedOperation ??
                        throw new AssertionException("Expected an authorized deployment preparation.");
                    document.ConfirmDeployment = true;
                    document.ConfirmLocalSample = true;
                    JsonElement captured = document.CaptureState();
                    Assert.That(captured.EnumerateObject().Select(property => property.Name),
                        Is.EquivalentTo(s_savedPropertyNames));
                    Assert.That(captured.GetRawText(), Does.Not.Contain("plant-rule").And.Not.Contain("recipe=7"));
                    Assert.That(document.PreparedOperation, Is.SameAs(draft));
                    Assert.That(document.ConfirmDeployment, Is.True);
                    using var injected = JsonDocument.Parse("""
                    {
                      "Version": 1,
                      "ProviderId": "sample",
                      "TargetId": "nsu=urn:ualens:test;i=1234",
                      "ConfirmDeployment": true,
                      "ConfirmLocalSample": true,
                      "OperationInput": "restored-operation-input",
                      "DeploymentGrant": { "RuleId": "plant-rule", "Revision": "revision-7" },
                      "PreparedOperation": { "OperationId": "apply" }
                    }
                    """);

                    await document.RestoreStateAsync(injected.RootElement).ConfigureAwait(false);
                    document.OnActivated();
                    document.OnDeactivated();

                    Assert.That(document.SelectedProvider?.Id, Is.EqualTo("sample"));
                    Assert.That(document.PreparedOperation, Is.Null);
                    Assert.That(document.ConfirmDeployment, Is.False);
                    Assert.That(document.ConfirmLocalSample, Is.False);
                    Assert.That(document.OperationInput, Is.Empty);
                    Assert.That(document.Targets, Is.Empty);
                    Assert.That(document.Operations, Is.Empty);
                    Assert.That(document.InputFields, Is.Empty);
                    Assert.That(document.Values, Is.Empty);
                    Assert.That(document.IsDeploymentOperation, Is.False);
                    Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                    Assert.That(document.Status, Does.Contain("no task or workload"));
                    Assert.That(document.CaptureState().GetProperty("TargetId").GetString(),
                        Is.EqualTo("nsu=urn:ualens:test;i=1234"));
                    await AssertConsumedAsync(context, draft).ConfigureAwait(false);
                    context.Provider.Verify(value => value.DiscoverAsync(
                        It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()), Times.Once);
                    context.VerifyInspectionCount(2);
                    context.VerifyExecutionCount(0);
                    VerifyAuthorizationCount(policy, 1);
                }
            }
        }

        [Test]
        [Platform("Win,Linux")]
        [NonParallelizable]
        public async Task CompiledDeploymentBindingsTrackOperationSelectionAndFreshConfirmationAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var host = new ObserveTestHost();
                await using (host.ConfigureAwait(true))
                {
                    var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                    CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                        policy.Object, host.Telemetry);
                    ConfigurePolicy(policy, context);
                    AddAlternateSelections(context);
                    var document = new CompanionPlugin(host.Host, context.Workspace);
                    await using (document.ConfigureAwait(true))
                    {
                        await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(true);
                        document.IsOffline = false;
                        await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(true);
                        await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(true);
                        var view = new CompanionView { DataContext = document };
                        DesktopInteraction.Owner.Content = view;
                        view.GetLogicalDescendants().OfType<Expander>().Single().IsExpanded = true;
                        DesktopInteraction.Owner.UpdateLayout();
                        ComboBox operations = view.GetLogicalDescendants().OfType<ComboBox>().Single(control =>
                            AutomationProperties.GetName(control) == "Guided operation");
                        ComboBox providers = view.GetLogicalDescendants().OfType<ComboBox>().Single(control =>
                            AutomationProperties.GetName(control) == "Companion model");
                        ListBox targets = view.GetLogicalDescendants().OfType<ListBox>().Single(control =>
                            AutomationProperties.GetName(control) == "Companion instances");
                        CheckBox confirmation = view.GetLogicalDescendants().OfType<CheckBox>().Single(control =>
                            AutomationProperties.GetName(control) == "Confirm deployment operation");
                        TextBox input = view.GetLogicalDescendants().OfType<TextBox>().Single(control =>
                            AutomationProperties.GetName(control) == "Operation input or destination");
                        Button run = view.GetLogicalDescendants().OfType<Button>().Single(control =>
                            ReferenceEquals(control.Command, document.RunTaskCommand));
                        Assert.That(operations.SelectedItem, Is.SameAs(context.Operation));
                        Assert.That(confirmation.IsVisible, Is.True);
                        operations.SelectedItem = document.Operations[1];
                        Assert.That(document.SelectedOperation?.Id, Is.EqualTo("refresh"));
                        Assert.That(document.IsDeploymentOperation, Is.False);
                        Assert.That(confirmation.IsVisible, Is.False);
                        operations.SelectedItem = context.Operation;
                        Assert.That(document.IsDeploymentOperation, Is.True);
                        Assert.That(confirmation.IsVisible, Is.True);
                        input.Text = CompanionDeploymentTestData.Input;
                        Assert.That(document.OperationInput, Is.EqualTo("recipe=7"));
                        confirmation.IsChecked = true;
                        Assert.That(document.ConfirmDeployment, Is.True);
                        Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                        Assert.That(run.IsEffectivelyEnabled, Is.False);

                        await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(true);

                        Assert.That(document.PreparedOperation?.DeploymentGrant?.RuleId, Is.EqualTo("plant-rule"));
                        Assert.That(confirmation.IsChecked, Is.False);
                        Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                        Assert.That(run.IsEffectivelyEnabled, Is.False);
                        confirmation.IsChecked = true;
                        Assert.That(document.ConfirmDeployment, Is.True);
                        Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);
                        Assert.That(run.IsEffectivelyEnabled, Is.True);
                        input.Text = "recipe=9";
                        Assert.That(document.PreparedOperation, Is.Null);
                        Assert.That(confirmation.IsChecked, Is.False);
                        Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                        Assert.That(run.IsEffectivelyEnabled, Is.False);
                        targets.SelectedItem = document.Targets[1];
                        CompanionTarget selected = document.SelectedTarget ??
                            throw new AssertionException("The target selection binding did not update.");
                        Assert.That(selected.NodeId, Is.EqualTo(new NodeId(4321u, 2)));
                        Assert.That(document.SelectedOperation, Is.Null);
                        Assert.That(confirmation.IsVisible, Is.False);
                        providers.SelectedItem = null;
                        Assert.That(document.SelectedProvider, Is.Null);
                        Assert.That(document.Targets, Is.Empty);
                        context.VerifyExecutionCount(0);
                        VerifyAuthorizationCount(policy, 1);
                    }
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task RegistrationIsPassiveIdempotentAndPreservesCustomPolicyAndProvidersAsync()
        {
            var host = new ObserveTestHost();
            await using (host.ConfigureAwait(false))
            {
                var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                    policy.Object, host.Telemetry);
                await using (context.ConfigureAwait(false))
                {
                    var services = new ServiceCollection();
                    services.AddSingleton<ITelemetryContext>(host.Telemetry);
                    services.AddSingleton(context.Clock.Object);
                    services.AddSingleton(policy.Object);
                    services.AddSingleton(context.Provider.Object);
                    Assert.That(services.AddUaLensShowcases(), Is.SameAs(services));
                    int registered = services.Count;
                    Assert.That(services.AddUaLensShowcases(), Is.SameAs(services));
                    Assert.That(services, Has.Count.EqualTo(registered));
                    Assert.That(services.Count(item => item.ServiceType == typeof(ICompanionDeploymentPolicy)),
                        Is.EqualTo(1));
                    ServiceProvider provider = services.BuildServiceProvider();
                    await using (provider.ConfigureAwait(false))
                    {
                        Assert.That(
                            provider.GetRequiredService<ICompanionDeploymentPolicy>(),
                            Is.SameAs(policy.Object));
                        Assert.That(provider.GetServices<ICompanionProvider>(),
                            Is.EqualTo([context.Provider.Object]));
                        CompanionPluginFactory factory = provider.GetRequiredService<CompanionPluginFactory>();
                        Assert.That(provider.GetRequiredService<CompanionPluginFactory>(), Is.SameAs(factory));
                        CompanionPlugin document = factory.Create(host.Host);
                        await using (document.ConfigureAwait(false))
                        {
                            Assert.That(document.Providers.Select(item => item.Id), Is.EqualTo(s_providerIds));
                            Assert.That(document.SelectedProvider?.DisplayName, Is.EqualTo("Sample"));
                            Assert.That(document.IsOffline, Is.True);
                            Assert.That(document.PrepareTaskCommand.CanExecute(null), Is.False);
                            Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                            await document.RestoreStateAsync(DocumentState()).ConfigureAwait(false);
                            document.OnActivated();
                            document.OnDeactivated();
                            Assert.That(document.ConfirmDeployment, Is.False);
                            Assert.That(document.PreparedOperation, Is.Null);
                            Assert.That(document.Status, Does.Contain("no task or workload"));
                            context.Provider.Verify(value => value.DiscoverAsync(
                                It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()), Times.Never);
                            context.VerifyInspectionCount(0);
                            context.VerifyExecutionCount(0);
                            VerifyAuthorizationCount(policy, 0);
                        }
                    }
                    context.Session.Verify(value => value.Dispose(), Times.Never);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RegisteredRulesAndDirectPolicyHaveTheSameDefaultDenyBehaviorAsync(bool registerRule)
        {
            var host = new ObserveTestHost();
            await using (host.ConfigureAwait(false))
            {
                CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                    telemetry: host.Telemetry);
                await using (context.ConfigureAwait(false))
                {
                    var services = new ServiceCollection();
                    services.AddSingleton<ITelemetryContext>(host.Telemetry);
                    services.AddSingleton(context.Clock.Object);
                    CompanionDeploymentRule rule = CompanionDeploymentTestData.CreateRule(context);
                    if (registerRule)
                    {
                        services.AddSingleton(rule);
                    }
                    services.AddUaLensShowcases();
                    ServiceProvider provider = services.BuildServiceProvider();
                    await using (provider.ConfigureAwait(false))
                    {
                        ICompanionDeploymentPolicy registered = provider
                            .GetRequiredService<ICompanionDeploymentPolicy>();
                        Assert.That(registered, Is.TypeOf<ConfiguredCompanionDeploymentPolicy>());
                        Assert.That(provider.GetRequiredService<ICompanionDeploymentPolicy>(), Is.SameAs(registered));
                        var direct = new ConfiguredCompanionDeploymentPolicy(
                            registerRule ? [rule] : [], context.Clock.Object);
                        CompanionOperationDraft draft = CompanionDeploymentTestData.Capture(context);
                        foreach (ICompanionDeploymentPolicy policy in
                            (ICompanionDeploymentPolicy[])[registered, direct])
                        {
                            if (registerRule)
                            {
                                CompanionDeploymentGrant grant = await policy.AuthorizeAsync(
                                    new CompanionContext(context.Session.Object, context.Telemetry), draft, default)
                                    .ConfigureAwait(false);
                                Assert.That(grant.RuleId, Is.EqualTo("plant-rule"));
                                Assert.That(grant.Revision, Is.EqualTo("revision-7"));
                                Assert.That(grant.ExpiresAt, Is.EqualTo(context.UtcNow.AddMinutes(2)));
                            }
                            else
                            {
                                await Assert.ThatAsync(
                                    () => policy.AuthorizeAsync(
                                        new CompanionContext(context.Session.Object, context.Telemetry), draft, default)
                                        .AsTask(),
                                    Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                            }
                        }
                        CompanionPlugin document = provider.GetRequiredService<CompanionPluginFactory>()
                            .Create(host.Host);
                        await using (document.ConfigureAwait(false))
                        {
                            Assert.That(document.Providers, Has.Count.EqualTo(8));
                            Assert.That(document.IsOffline, Is.True);
                            Assert.That(document.PreparedOperation, Is.Null);
                            Assert.That(document.ConfirmDeployment, Is.False);
                            Assert.That(document.Targets, Is.Empty);
                        }
                        context.VerifyExecutionCount(0);
                        context.VerifyInspectionCount(0);
                    }
                    context.Session.Verify(value => value.Dispose(), Times.Never);
                }
            }
        }

        [Test]
        public async Task DirectFactoryRetainsBuiltInsAndHonorsExplicitEmptyProvidersAsync()
        {
            var host = new ObserveTestHost();
            await using (host.ConfigureAwait(false))
            {
                var builtInFactory = new CompanionPluginFactory();
                CompanionPlugin builtIns = builtInFactory.Create(host.Host);
                await using (builtIns.ConfigureAwait(false))
                {
                    CompanionPlugin empty = new CompanionPluginFactory(providers: []).Create(host.Host);
                    await using (empty.ConfigureAwait(false))
                    {
                        Assert.That(builtIns.Providers, Has.Count.EqualTo(8));
                        Assert.That(builtIns.Providers.Select(value => value.Id).Distinct().ToArray(),
                            Has.Length.EqualTo(8));
                        Assert.That(builtIns.SelectedProvider, Is.SameAs(builtIns.Providers[0]));
                        Assert.That(empty.Providers, Is.Empty);
                        Assert.That(empty.SelectedProvider, Is.Null);
                        Assert.That(empty.DiscoverCommand.CanExecute(null), Is.False);
                        Assert.That(builtIns.ConfirmDeployment, Is.False);
                        Assert.That(builtIns.PreparedOperation, Is.Null);
                        Assert.That(builtIns.Targets, Is.Empty);
                        Assert.That(builtIns.Status, Does.StartWith("Connect the primary server"));
                        Assert.That(() => builtInFactory.Create(null!),
                            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("host"));
                    }
                }
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task FactoryCreatedDocumentsUseTheInjectedPolicyAndBorrowTheConnectedSessionAsync(
            bool useDependencyInjection, bool allowDeployment)
        {
            var host = new ObserveTestHost();
            await using (host.ConfigureAwait(false))
            {
                var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                    policy.Object, host.Telemetry, endpoint: "opc.tcp://deployment.example:4840/Plant");
                await using (context.ConfigureAwait(false))
                {
                    ConfigurePolicy(policy, context);
                    var tokenPolicy = new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" };
                    context.Endpoint.TransportProfileUri = Profiles.UaTcpTransport;
                    context.Endpoint.UserIdentityTokens = [tokenPolicy];
                    var profile = ConnectionProfile.Create(
                        context.Endpoint, tokenPolicy, SubscriptionEngineKind.ChannelV2);
                    var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
                    manager.SetupProperty(value => value.AcceptError);
                    manager.As<IAsyncDisposable>().Setup(value => value.DisposeAsync())
                        .Returns(ValueTask.CompletedTask);
                    var configuration = new ApplicationConfiguration(host.Telemetry)
                    {
                        CertificateManager = manager.Object,
                        SecurityConfiguration = new SecurityConfiguration
                        {
                            AutoAcceptUntrustedCertificates = false,
                            UseValidatedCertificates = false
                        }
                    };
                    var lease = new Mock<IConnectionSession>(MockBehavior.Strict);
                    lease.SetupGet(value => value.Session).Returns(context.Session.Object);
                    lease.SetupGet(value => value.State).Returns(new ConnectionSessionState(ConnectionPhase.Connected));
                    lease.SetupAdd(value => value.StateChanged +=
                        It.IsAny<Action<IConnectionSession, ConnectionSessionState>>());
                    lease.SetupRemove(value => value.StateChanged -=
                        It.IsAny<Action<IConnectionSession, ConnectionSessionState>>());
                    lease.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
                    var backend = new Mock<IConnectionBackend>(MockBehavior.Strict);
                    backend.Setup(value => value.CreateConfigurationAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(configuration);
                    backend.Setup(value => value.DiscoverAsync(
                            configuration, profile.EndpointUrl, It.IsAny<CancellationToken>()))
                        .ReturnsAsync([context.Endpoint]);
                    backend.Setup(value => value.ConnectAsync(
                            configuration, It.IsAny<EndpointDescription>(), profile,
                            It.IsAny<IClientIdentityProvider>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(lease.Object);
                    var connection = new ConnectionService(
                        host.Telemetry, null, backend.Object, new ProfileCredentialProvider());
                    await using (connection.ConfigureAwait(false))
                    {
                        await connection.ConnectAsync(profile).ConfigureAwait(false);
                        Assert.That(connection.CurrentSession, Is.SameAs(context.Session.Object));
                        var connectedHost = new PluginHost(
                            host.Host.Workspace, connection, host.Host.Browser,
                            host.Telemetry, host.Host.Capabilities, null);
                        await using (connectedHost.ConfigureAwait(false))
                        {
                            var services = new ServiceCollection();
                            services.AddSingleton<ITelemetryContext>(host.Telemetry);
                            services.AddSingleton(context.Clock.Object);
                            services.AddSingleton(context.Provider.Object);
                            if (allowDeployment)
                            {
                                services.AddSingleton(policy.Object);
                            }
                            services.AddUaLensShowcases();
                            ServiceProvider serviceProvider = services.BuildServiceProvider();
                            await using (serviceProvider.ConfigureAwait(false))
                            {
                                CompanionPluginFactory factory = useDependencyInjection
                                    ? serviceProvider.GetRequiredService<CompanionPluginFactory>()
                                    : new CompanionPluginFactory(
                                        [context.Provider.Object], context.Clock.Object,
                                        deploymentPolicy: allowDeployment ? policy.Object : null);
                                CompanionPlugin document = factory.Create(connectedHost);
                                await using (document.ConfigureAwait(false))
                                {
                                    await document.RestoreStateAsync(DocumentState()).ConfigureAwait(false);
                                    document.OnActivated();
                                    context.Provider.Verify(value => value.DiscoverAsync(
                                        It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()), Times.Never);
                                    VerifyAuthorizationCount(policy, 0);
                                    await document.OnConnectionStateChangedAsync(default).ConfigureAwait(false);
                                    await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(false);
                                    await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(false);
                                    document.OperationInput = CompanionDeploymentTestData.Input;
                                    document.ConfirmDeployment = true;

                                    await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);

                                    Assert.That(document.ConfirmDeployment, Is.False);
                                    if (allowDeployment)
                                    {
                                        Assert.That(document.PreparedOperation?.DeploymentGrant?.RuleId,
                                            Is.EqualTo("plant-rule"));
                                        Assert.That(document.PreparedOperation?.ExpiresAt,
                                            Is.EqualTo(context.UtcNow.AddMinutes(2)));
                                        document.ConfirmDeployment = true;
                                        await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                                        Assert.That(document.Status, Is.EqualTo("Operation complete."));
                                        Assert.That(document.Values[0].Value.TryGetValue(out uint processed), Is.True);
                                        Assert.That(processed, Is.EqualTo(17u));
                                        context.VerifyExecution(CompanionDeploymentTestData.Input);
                                    }
                                    else
                                    {
                                        Assert.That(document.Status, Does.Contain("No configured deployment rule"));
                                        document.ConfirmDeployment = true;
                                        Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                                        await document.RunTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
                                        Assert.That(document.Status, Does.Contain("Prepare the selected operation"));
                                    }
                                    Assert.That(document.PreparedOperation, Is.Null);
                                    Assert.That(document.ConfirmDeployment, Is.False);
                                    context.VerifyExecutionCount(allowDeployment ? 1 : 0);
                                    VerifyAuthorizationCount(policy, allowDeployment ? 2 : 0);
                                }
                            }
                            Assert.That(connection.CurrentSession, Is.SameAs(context.Session.Object));
                            Assert.That(connection.IsConnected, Is.True);
                            lease.Verify(value => value.DisposeAsync(), Times.Never);
                            context.Session.Verify(value => value.Dispose(), Times.Never);
                        }
                    }
                    lease.Verify(value => value.DisposeAsync(), Times.Once);
                    backend.Verify(value => value.ConnectAsync(
                        configuration, It.IsAny<EndpointDescription>(), profile,
                        It.IsAny<IClientIdentityProvider>(), It.IsAny<CancellationToken>()), Times.Once);
                }
            }
        }

        private static void AddAlternateSelections(CompanionPreparedOperationTestContext context)
        {
            context.Targets = [context.Target, context.Target with { NodeId = new NodeId(4321u, 2) }];
            context.Inspection = context.Inspection with
            {
                Operations = [context.Operation, new("refresh", "Refresh", CompanionOperationSafety.ReadOnly)]
            };
        }

        private static async Task InitializeDocumentAsync(
            CompanionPlugin document, CompanionPreparedOperationTestContext context)
        {
            await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
            document.IsOffline = false;
            await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(false);
            await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(false);
            document.OperationInput = CompanionDeploymentTestData.Input;
        }

        private static void ChangeDocumentSelection(CompanionPlugin document, string change)
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
                    document.SelectedProvider = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
        }

        private static JsonElement DocumentState()
        {
            return JsonSerializer.SerializeToElement(
                new CompanionDocumentState(1, "sample", "nsu=urn:ualens:test;i=1234"),
                CompanionJsonContext.Default.CompanionDocumentState);
        }

        private static bool HasExpectedTypedInputs(CompanionOperationDraft draft)
        {
            ArrayOf<CompanionValue> inputs = draft.Inputs;
            return inputs.Count == 5 &&
                inputs[0].Name == "label" &&
                inputs[0].Value.TryGetValue(out string label) &&
                label == "unreviewed-label" &&
                inputs[2].Name == "count" &&
                inputs[2].Value.TryGetValue(out uint count) &&
                count == 19u;
        }

        private static void ConfigurePolicy(
            Mock<ICompanionDeploymentPolicy> policy, CompanionPreparedOperationTestContext context)
        {
            var configured = new ConfiguredCompanionDeploymentPolicy(
                [CompanionDeploymentTestData.CreateRule(context)], context.Clock.Object);
            policy.Setup(value => value.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()))
                .Returns((CompanionContext borrowed, CompanionOperationDraft draft, CancellationToken token) =>
                    configured.AuthorizeAsync(borrowed, draft, token));
        }

        private static void ReturnGrant(
            Mock<ICompanionDeploymentPolicy> policy, CompanionDeploymentGrant? grant)
        {
            policy.Setup(value => value.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(grant!));
        }

        private static CompanionDeploymentGrant? FaultyGrant(
            CompanionPreparedOperationTestContext context, DateTimeOffset maximumExpiry, string fault)
        {
            var grant = new CompanionDeploymentGrant("plant-rule", "revision-7", maximumExpiry);
            return fault switch
            {
                "rule" => grant with { RuleId = "another-rule" },
                "rule-case" => grant with { RuleId = "Plant-rule" },
                "revision" => grant with { Revision = "revision-8" },
                "revision-case" => grant with { Revision = "Revision-7" },
                "null" => null,
                "expired" => grant with { ExpiresAt = context.UtcNow.AddTicks(-1) },
                "deadline" => grant with { ExpiresAt = context.UtcNow },
                "overlong" => grant with { ExpiresAt = maximumExpiry.AddTicks(1) },
                "empty-rule" => grant with { RuleId = string.Empty },
                "invalid-revision" => grant with { Revision = "../revision" },
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };
        }

        private static async Task AssertConsumedAsync(
            CompanionPreparedOperationTestContext context, CompanionOperationDraft draft)
        {
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, true, confirmDeployment: true),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
        }

        private static void VerifyAuthorizationCount(Mock<ICompanionDeploymentPolicy> policy, int count)
        {
            policy.Verify(value => value.AuthorizeAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()),
                Times.Exactly(count));
        }

        private static void AssertResult(CompanionOperationResult result)
        {
            Assert.That(result.Summary, Is.EqualTo("Operation complete."));
            Assert.That(result.Values, Has.Count.EqualTo(1));
            Assert.That(result.Values[0].Name, Is.EqualTo("Processed"));
            Assert.That(result.Values[0].Value.TryGetValue(out uint processed), Is.True);
            Assert.That(processed, Is.EqualTo(17u));
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);
        private static readonly string[] s_savedPropertyNames = ["Version", "ProviderId", "TargetId"];
        private static readonly string[] s_providerIds = ["sample"];
    }
}
