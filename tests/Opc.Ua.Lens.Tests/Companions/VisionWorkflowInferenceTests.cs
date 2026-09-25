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
using Opc.Ua.Vision;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.CellProviderTestSession;
using static UaLens.Tests.Companions.VisionWorkflowTestSupport;
using Ai = Opc.Ua.AI;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class VisionWorkflowInferenceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task OnlyVerifiedLocalDeploymentAvoidsTheAdditionalExecutionPolicy(bool described)
        {
            var fixture = new VisionWorkflowTestSupport();
            if (described)
            {
                fixture.AddAiDeployment();
            }
            var provider = new VisionCompanionProvider(timeProvider: fixture.Clock.Object);
            if (!described)
            {
                await Assert.ThatAsync(() => provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                    "run-inference", fixture.Inputs("run-inference"), CancellationToken.None).AsTask(),
                    Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                fixture.Fixture.VerifyNoMutationOrSessionOwnership();
                return;
            }
            NodeId result = fixture.AddResult();
            fixture.OnCall = (request, _) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(fixture.Pipeline));
                Assert.That(request.InputArguments[0].TryGetValue(out DateTimeUtc time), Is.True);
                Assert.That(time, Is.EqualTo(new DateTimeUtc(fixture.UtcNow)));
                return ValueTask.FromResult(new CallMethodResult { OutputArguments = [Variant.From("result-7")] });
            };
            CompanionTaskInput task = await provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "run-inference", fixture.Inputs("run-inference"), CancellationToken.None).ConfigureAwait(false);
            Assert.That(task.Review, Does.Contain("the selected OPC UA server (OnServer)"));

            CompanionOperationResult outcome = await provider.ExecutePreparedAsync(fixture.Context,
                fixture.PipelineTarget, "run-inference", task, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(Field(outcome.Values, "Result node").TryGetValue(out NodeId actual), Is.True);
            Assert.That(actual, Is.EqualTo(result));
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            fixture.VerifyBorrowedSession();
        }

        [TestCase("missing")]
        [TestCase("http")]
        [TestCase("userinfo")]
        [TestCase("query")]
        [TestCase("unready")]
        [TestCase("egress-denied")]
        public async Task ExplicitPolicyCannotOverrideUnsafeKnownDeployment(string fault)
        {
            var fixture = new VisionWorkflowTestSupport();
            string? endpoint = fault switch
            {
                "missing" => null,
                "http" => "http://external.invalid/inference",
                "userinfo" => "https://user@external.invalid/inference",
                "query" => "https://external.invalid/inference?credential=not-forwarded",
                _ => "https://external.invalid/inference"
            };
            NodeId deployment = fixture.AddAiDeployment(Ai.InferenceLocationEnum.Cloud,
                endpoint, egress: fault != "egress-denied");
            if (fault == "unready")
            {
                fixture.Set(deployment, "State", Variant.From((int)Ai.DeploymentStateEnum.Degraded));
            }
            Mock<IVisionExecutionPolicy> policy = fixture.AllowExecution();
            var provider = new VisionCompanionProvider(policy.Object, fixture.Clock.Object);

            await Assert.ThatAsync(() => provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "run-inference", fixture.Inputs("run-inference"), CancellationToken.None).AsTask(),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

            policy.Verify(value => value.AuthorizeAsync(
                fixture.Context, It.IsAny<VisionWorkflowTask>(), It.IsAny<CancellationToken>()), Times.Never);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PolicyMutationOrCancellationCannotBypassTheFinalBindingCheck(bool cancel)
        {
            var fixture = new VisionWorkflowTestSupport();
            using var cancellation = new CancellationTokenSource();
            Mock<IVisionExecutionPolicy> policy = fixture.AllowExecution();
            int authorizations = 0;
            policy.Setup(value => value.AuthorizeAsync(
                fixture.Context, It.IsAny<VisionWorkflowTask>(), It.IsAny<CancellationToken>()))
                .Returns(async (CompanionContext _, VisionWorkflowTask _, CancellationToken _) =>
                {
                    if (++authorizations == 2)
                    {
                        if (cancel)
                        {
                            await cancellation.CancelAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            fixture.Set(fixture.Pipeline, "PipelineId", Variant.From("different-pipeline"));
                        }
                    }
                });
            var provider = new VisionCompanionProvider(policy.Object, fixture.Clock.Object);
            CompanionTaskInput task = await provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "run-inference", fixture.Inputs("run-inference"), cancellation.Token).ConfigureAwait(false);

            if (cancel)
            {
                await Assert.ThatAsync(() => provider.ExecutePreparedAsync(fixture.Context, fixture.PipelineTarget,
                    "run-inference", task, null, cancellation.Token).AsTask(),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => provider.ExecutePreparedAsync(fixture.Context, fixture.PipelineTarget,
                    "run-inference", task, null, cancellation.Token).AsTask(),
                    Throws.InvalidOperationException.With.Message.Contains("changed")).ConfigureAwait(false);
            }
            Assert.That(authorizations, Is.EqualTo(2));
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase("active")]
        [TestCase("continuous")]
        public async Task ExistingPipelineRunsAreNeverTakenOver(string state)
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.Set(fixture.Pipeline, state == "active" ? "State" : "Continuous",
                state == "active" ? Variant.From((int)VisionEndpointStateEnum.Active) : Variant.From(true));
            var provider = new VisionCompanionProvider(fixture.AllowExecution().Object, fixture.Clock.Object);

            await Assert.ThatAsync(() => provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "run-continuous-window", fixture.Inputs("run-continuous-window"), CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState))
                .ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(1u)]
        [TestCase(15u)]
        public async Task AcknowledgedContinuousRunStopsAtItsBoundAndObservesFalse(uint seconds)
        {
            var fixture = new VisionWorkflowTestSupport();
            ConfigureRun(fixture);
            Task<CompanionOperationResult> operation = RunWindowAsync(fixture, seconds);
            var window = TimeSpan.FromSeconds(seconds);
            await fixture.Timers.WaitForTimerAsync(window).ConfigureAwait(false);
            fixture.Advance(window - TimeSpan.FromTicks(1));
            Assert.That(operation.IsCompleted, Is.False);
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            fixture.Advance(TimeSpan.FromTicks(1));

            CompanionOperationResult result = await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(result.Summary, Does.Contain("Continuous=false was observed"));
            Assert.That(fixture.Calls.Count, Is.EqualTo(2));
            Assert.That(fixture.Calls[1].MethodId, Is.EqualTo(fixture.Fixture.Children[(fixture.Pipeline, "Stop")]));
            Assert.That(fixture.CallTokens[1], Is.Not.EqualTo(fixture.CallTokens[0]));
            fixture.VerifyBorrowedSession();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedOrUnacknowledgedStartNeverStopsAnUnownedRun(bool timeout)
        {
            var fixture = new VisionWorkflowTestSupport
            {
                OnCall = (_, _) => ValueTask.FromResult(new CallMethodResult
                {
                    StatusCode = timeout ? StatusCodes.BadTimeout : StatusCodes.BadNotSupported
                })
            };

            if (timeout)
            {
                await Assert.ThatAsync(() => RunWindowAsync(fixture, 1),
                    Throws.InvalidOperationException.With.Message.Contains("unknown")).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => RunWindowAsync(fixture, 1),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
            }
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            Assert.That(fixture.Calls[0].MethodId,
                Is.EqualTo(fixture.Fixture.Children[(fixture.Pipeline, "StartContinuous")]));
        }

        [Test]
        public async Task StopAcknowledgementCannotHideContinuousTrue()
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.OnCall = (request, _) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(fixture.Pipeline));
                if (request.MethodId == fixture.Fixture.Children[(fixture.Pipeline, "StartContinuous")])
                {
                    fixture.Set(fixture.Pipeline, "Continuous", Variant.From(true));
                }
                else
                {
                    Assert.That(request.MethodId, Is.EqualTo(fixture.Fixture.Children[(fixture.Pipeline, "Stop")]));
                }
                return ValueTask.FromResult(new CallMethodResult());
            };
            Task<CompanionOperationResult> operation = RunWindowAsync(fixture, 1);
            await fixture.Timers.WaitForTimerAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            fixture.Advance(TimeSpan.FromSeconds(1));

            await Assert.ThatAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState).And.Message.Contains("Continuous=true"))
                .ConfigureAwait(false);
            Assert.That(fixture.Calls.Count, Is.EqualTo(2));
            Assert.That(fixture.Calls[1].MethodId, Is.EqualTo(fixture.Fixture.Children[(fixture.Pipeline, "Stop")]));
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task CancellationAndStopFailureAreBothRetained()
        {
            var fixture = new VisionWorkflowTestSupport();
            ConfigureRun(fixture, stopFails: true);
            using var cancellation = new CancellationTokenSource();
            Task<CompanionOperationResult> operation = RunWindowAsync(fixture, 1, cancellation.Token);
            await fixture.Timers.WaitForTimerAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            AggregateException? failure = null;
            try
            {
                await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (AggregateException error)
            {
                failure = error;
            }

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions[0], Is.InstanceOf<OperationCanceledException>());
            Assert.That(failure.InnerExceptions[1], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)failure.InnerExceptions[1]).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(fixture.CallTokens[1].IsCancellationRequested, Is.False);
            Assert.That(fixture.Calls.Count, Is.EqualTo(2));
        }

        [TestCase("pipeline-id")]
        [TestCase("sensor-id")]
        [TestCase("reality")]
        [TestCase("deployment")]
        public async Task CleanupNeverStopsAReboundOrReclassifiedPipeline(string change)
        {
            var fixture = new VisionWorkflowTestSupport();
            ConfigureRun(fixture);
            Task<CompanionOperationResult> operation = RunWindowAsync(fixture, 1);
            await fixture.Timers.WaitForTimerAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            switch (change)
            {
                case "pipeline-id":
                    fixture.Set(fixture.Pipeline, "PipelineId", Variant.From("replacement"));
                    break;
                case "sensor-id":
                    fixture.Set(fixture.Sensor, "SensorId", Variant.From("replacement-sensor"));
                    break;
                case "reality":
                    fixture.Set(fixture.Sensor, "RealityKind", Variant.From((int)VisionRealityKindEnum.Physical));
                    break;
                case "deployment":
                    fixture.Set(fixture.Pipeline, "Deployment", Variant.From(new NodeId("replacement-deployment", 1)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
            fixture.Advance(TimeSpan.FromSeconds(1));

            await Assert.ThatAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.InvalidOperationException.With.Message.Contains("binding changed")).ConfigureAwait(false);
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task AcceptedInferenceWithUnverifiableResultIsNotRepeated()
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId result = fixture.AddResult();
            fixture.Set(result, "Pipeline", Variant.From(new NodeId("other-pipeline", 1)));
            fixture.OnCall = (_, _) => ValueTask.FromResult(new CallMethodResult
            {
                OutputArguments = [Variant.From("result-7")]
            });
            var provider = new VisionCompanionProvider(fixture.AllowExecution().Object, fixture.Clock.Object);
            CompanionTaskInput task = await provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "run-inference", fixture.Inputs("run-inference"), CancellationToken.None).ConfigureAwait(false);

            await Assert.ThatAsync(() => provider.ExecutePreparedAsync(fixture.Context, fixture.PipelineTarget,
                "run-inference", task, null, CancellationToken.None).AsTask(),
                Throws.InvalidOperationException.With.Message.Contains("result-7")
                    .And.Property("Message").Contains("do not repeat"))
                .ConfigureAwait(false);
            await Assert.ThatAsync(() => provider.ExecutePreparedAsync(fixture.Context, fixture.PipelineTarget,
                "run-inference", task, null, CancellationToken.None).AsTask(),
                Throws.InvalidOperationException.With.Message.Contains("already dispatched")).ConfigureAwait(false);
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
        }

        private static void ConfigureRun(VisionWorkflowTestSupport fixture, bool stopFails = false)
        {
            fixture.OnCall = (request, token) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(fixture.Pipeline));
                Assert.That(request.InputArguments.IsEmpty, Is.True);
                bool start = request.MethodId == fixture.Fixture.Children[(fixture.Pipeline, "StartContinuous")];
                if (start)
                {
                    fixture.Set(fixture.Pipeline, "Continuous", Variant.From(true));
                }
                else
                {
                    Assert.That(request.MethodId, Is.EqualTo(fixture.Fixture.Children[(fixture.Pipeline, "Stop")]));
                    Assert.That(token.IsCancellationRequested, Is.False);
                    fixture.Set(fixture.Pipeline, "Continuous", Variant.From(false));
                }
                return ValueTask.FromResult(new CallMethodResult
                {
                    StatusCode = !start && stopFails ? StatusCodes.BadUserAccessDenied : StatusCodes.Good
                });
            };
        }

        private static async Task<CompanionOperationResult> RunWindowAsync(
            VisionWorkflowTestSupport fixture, uint seconds, CancellationToken cancellationToken = default)
        {
            var provider = new VisionCompanionProvider(fixture.AllowExecution().Object, fixture.Clock.Object);
            ArrayOf<CompanionValue> inputs = Replace(
                fixture.Inputs("run-continuous-window"), "seconds", Variant.From(seconds));
            CompanionTaskInput task = await provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "run-continuous-window", inputs, cancellationToken).ConfigureAwait(false);
            return await provider.ExecutePreparedAsync(fixture.Context, fixture.PipelineTarget,
                "run-continuous-window", task, null, cancellationToken).ConfigureAwait(false);
        }
    }
}
