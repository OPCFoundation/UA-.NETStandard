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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Pins the inference pipeline method dispatch path. Every rejection
    /// code the dispatcher returns is a public contract:
    /// <list type="bullet">
    /// <item><description><see cref="StatusCodes.BadNodeIdUnknown"/> when
    /// the pipeline NodeId is not registered (the method was invoked on a
    /// pipeline the registry never saw).</description></item>
    /// <item><description><see cref="StatusCodes.BadNotSupported"/> when
    /// the pipeline is registered but no <see cref="IVisionInferenceProvider"/>
    /// is bound (missing configuration, not a client fault).</description></item>
    /// <item><description>The provider's own <see cref="ServiceResult"/> when
    /// the provider runs — good or bad, verbatim.</description></item>
    /// <item><description><see cref="StatusCodes.BadInternalError"/> when
    /// the provider throws a non-cancellation exception.</description></item>
    /// <item><description>Propagates
    /// <see cref="OperationCanceledException"/> unchanged so cooperative
    /// cancellation of the caller's context is honoured end-to-end.</description></item>
    /// </list>
    /// </summary>
    [TestFixture]
    public sealed class VisionMethodDispatcherPipelineHandlerTests
    {
        [Test]
        public async Task RunInferenceReturnsBadNodeIdUnknownWhenPipelineIsNotRegistered()
        {
            var registeredPipelineId = new NodeId(701, 4);
            var orphanPipelineId = new NodeId(702, 4);
            var harness = new PipelineHarness(
                pipelineNodeId: registeredPipelineId,
                inferenceProvider: null,
                attachOrphanNodeId: orphanPipelineId);

            RunInferenceMethodStateResult result = await harness.InvokeRunInference(
                new DateTimeUtc(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc))).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                "RunInference must refuse a call whose pipeline NodeId is not in the registry — " +
                "the delegate was wired for a NodeId no PipelineRegistration ever claimed.");
        }

        [Test]
        public async Task RunInferenceReturnsBadNotSupportedWhenInferenceProviderIsNull()
        {
            var harness = new PipelineHarness(
                pipelineNodeId: new NodeId(703, 4),
                inferenceProvider: null);

            RunInferenceMethodStateResult result = await harness.InvokeRunInference(
                new DateTimeUtc(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc))).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported),
                "Without an inference provider the dispatcher must refuse the call with BadNotSupported — " +
                "this is a configuration gap, not a client fault.");
        }

        [Test]
        public async Task RunInferenceForwardsProviderResultServiceCodeAndResultIdOnSuccess()
        {
            var provider = new Mock<IVisionInferenceProvider>(MockBehavior.Strict);
            provider.Setup(p => p.RunInferenceAsync(It.IsAny<VisionInferenceRunRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<VisionInferenceRunResult>(
                    new VisionInferenceRunResult(ServiceResult.Good, "run-42")));
            var harness = new PipelineHarness(
                pipelineNodeId: new NodeId(704, 4),
                inferenceProvider: provider.Object);

            RunInferenceMethodStateResult result = await harness.InvokeRunInference(
                new DateTimeUtc(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True,
                    "A Good ServiceResult from the provider must be forwarded to the caller unchanged.");
                Assert.That(result.ResultId, Is.EqualTo("run-42"),
                    "The provider-supplied ResultId is the single output the caller can use to look up " +
                    "the produced inference in the Results folder; it must be forwarded verbatim.");
            });
        }

        [Test]
        public async Task RunInferenceForwardsSensorAndDeploymentNodeIdsFromPipelineToProvider()
        {
            var sensorNodeId = new NodeId(801, 4);
            var deploymentNodeId = new NodeId(802, 4);
            var pipelineNodeId = new NodeId(705, 4);
            VisionInferenceRunRequest captured = default;
            bool wasCalled = false;
            var provider = new Mock<IVisionInferenceProvider>(MockBehavior.Strict);
            provider.Setup(p => p.RunInferenceAsync(It.IsAny<VisionInferenceRunRequest>(), It.IsAny<CancellationToken>()))
                .Returns<VisionInferenceRunRequest, CancellationToken>((req, _) =>
                {
                    captured = req;
                    wasCalled = true;
                    return new ValueTask<VisionInferenceRunResult>(
                        new VisionInferenceRunResult(ServiceResult.Good, "id"));
                });
            var harness = new PipelineHarness(
                pipelineNodeId: pipelineNodeId,
                inferenceProvider: provider.Object,
                sensorNodeId: sensorNodeId,
                deploymentNodeId: deploymentNodeId);
            var timestamp = new DateTimeUtc(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc));

            await harness.InvokeRunInference(timestamp).ConfigureAwait(false);

            Assert.That(wasCalled, Is.True,
                "The provider must have been invoked so the dispatcher-produced request is observable.");
            Assert.Multiple(() =>
            {
                Assert.That(captured.Pipeline, Is.EqualTo(pipelineNodeId),
                    "The pipeline NodeId the delegate was wired for must be forwarded to the provider.");
                Assert.That(captured.Sensor, Is.EqualTo(sensorNodeId),
                    "The pipeline's Sensor property value must be read and forwarded so the provider " +
                    "knows which sensor to render from.");
                Assert.That(captured.Deployment, Is.EqualTo(deploymentNodeId),
                    "The pipeline's Deployment property value must be forwarded so the provider " +
                    "knows which deployment to run.");
                Assert.That(captured.Timestamp, Is.EqualTo(timestamp),
                    "The caller-supplied timestamp must be forwarded to the provider unchanged.");
            });
        }

        [Test]
        public async Task RunInferenceReturnsNodeIdNullSensorAndDeploymentWhenPropertiesAreMissing()
        {
            var pipelineNodeId = new NodeId(706, 4);
            VisionInferenceRunRequest captured = default;
            bool wasCalled = false;
            var provider = new Mock<IVisionInferenceProvider>(MockBehavior.Strict);
            provider.Setup(p => p.RunInferenceAsync(It.IsAny<VisionInferenceRunRequest>(), It.IsAny<CancellationToken>()))
                .Returns<VisionInferenceRunRequest, CancellationToken>((req, _) =>
                {
                    captured = req;
                    wasCalled = true;
                    return new ValueTask<VisionInferenceRunResult>(
                        new VisionInferenceRunResult(ServiceResult.Good, "id"));
                });
            var harness = new PipelineHarness(
                pipelineNodeId: pipelineNodeId,
                inferenceProvider: provider.Object);

            await harness.InvokeRunInference(default).ConfigureAwait(false);

            Assert.That(wasCalled, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(captured.Sensor.IsNull, Is.True,
                    "When the pipeline has no Sensor property, ReadPipelineSensor must return NodeId.Null — " +
                    "propagating a random value would silently associate the run with a sensor the caller never named.");
                Assert.That(captured.Deployment.IsNull, Is.True,
                    "When the pipeline has no Deployment property, ReadPipelineDeployment must return NodeId.Null.");
            });
        }

        [Test]
        public async Task RunInferenceReturnsBadInternalErrorWhenProviderThrowsNonCancellationException()
        {
            var provider = new Mock<IVisionInferenceProvider>(MockBehavior.Strict);
            provider.Setup(p => p.RunInferenceAsync(It.IsAny<VisionInferenceRunRequest>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("provider blew up"));
            var harness = new PipelineHarness(
                pipelineNodeId: new NodeId(707, 4),
                inferenceProvider: provider.Object);

            RunInferenceMethodStateResult result = await harness.InvokeRunInference(default)
                .ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInternalError),
                "A provider exception must not tear the server down — the dispatcher must map it to BadInternalError " +
                "so the caller sees a clean failure code instead of a stack trace propagating out of the method call.");
        }

        [Test]
        public void RunInferencePropagatesOperationCanceledExceptionFromProvider()
        {
            var provider = new Mock<IVisionInferenceProvider>(MockBehavior.Strict);
            provider.Setup(p => p.RunInferenceAsync(It.IsAny<VisionInferenceRunRequest>(), It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException("cancelled from provider"));
            var harness = new PipelineHarness(
                pipelineNodeId: new NodeId(708, 4),
                inferenceProvider: provider.Object);

            Assert.That(async () => await harness.InvokeRunInference(default).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>(),
                "OperationCanceledException from the provider must be rethrown unchanged so the caller's " +
                "cooperative cancellation is honoured; the dispatcher must not swallow it into BadInternalError.");
        }

        [Test]
        public async Task StartContinuousReturnsBadNodeIdUnknownWhenPipelineIsNotRegistered()
        {
            var registeredPipelineId = new NodeId(710, 4);
            var orphanPipelineId = new NodeId(711, 4);
            var harness = new PipelineHarness(
                pipelineNodeId: registeredPipelineId,
                inferenceProvider: null,
                attachOrphanNodeId: orphanPipelineId);

            ServiceResult result = await harness.InvokeStartContinuous().ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                "StartContinuous must refuse a call whose pipeline NodeId is not in the registry.");
        }

        [Test]
        public async Task StartContinuousReturnsBadNotSupportedWhenInferenceProviderIsNull()
        {
            var harness = new PipelineHarness(
                pipelineNodeId: new NodeId(712, 4),
                inferenceProvider: null);

            ServiceResult result = await harness.InvokeStartContinuous().ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported),
                "Without an inference provider the dispatcher must refuse the call with BadNotSupported.");
        }

        [Test]
        public async Task StartContinuousForwardsProviderResultOnSuccess()
        {
            var provider = new Mock<IVisionInferenceProvider>(MockBehavior.Strict);
            NodeId capturedNodeId = default;
            bool wasCalled = false;
            provider.Setup(p => p.StartContinuousAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns<NodeId, CancellationToken>((nodeId, _) =>
                {
                    capturedNodeId = nodeId;
                    wasCalled = true;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            var pipelineNodeId = new NodeId(713, 4);
            var harness = new PipelineHarness(
                pipelineNodeId: pipelineNodeId,
                inferenceProvider: provider.Object);

            ServiceResult result = await harness.InvokeStartContinuous().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True,
                    "A Good ServiceResult from the provider must be forwarded to the caller unchanged.");
                Assert.That(wasCalled, Is.True,
                    "The provider must have been invoked so the forwarded NodeId is observable.");
                Assert.That(capturedNodeId, Is.EqualTo(pipelineNodeId),
                    "The pipeline NodeId the delegate was wired for must be forwarded to the provider so it " +
                    "knows which pipeline to start.");
            });
        }

        [Test]
        public async Task StartContinuousReturnsBadInternalErrorWhenProviderThrowsNonCancellationException()
        {
            var provider = new Mock<IVisionInferenceProvider>(MockBehavior.Strict);
            provider.Setup(p => p.StartContinuousAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("provider blew up"));
            var harness = new PipelineHarness(
                pipelineNodeId: new NodeId(714, 4),
                inferenceProvider: provider.Object);

            ServiceResult result = await harness.InvokeStartContinuous().ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInternalError),
                "A provider exception must be mapped to BadInternalError so the method call returns cleanly.");
        }

        [Test]
        public void StartContinuousPropagatesOperationCanceledExceptionFromProvider()
        {
            var provider = new Mock<IVisionInferenceProvider>(MockBehavior.Strict);
            provider.Setup(p => p.StartContinuousAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException("cancelled from provider"));
            var harness = new PipelineHarness(
                pipelineNodeId: new NodeId(715, 4),
                inferenceProvider: provider.Object);

            Assert.That(async () => await harness.InvokeStartContinuous().ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>(),
                "OperationCanceledException from the provider must be rethrown unchanged.");
        }

        [Test]
        public async Task StopReturnsBadNodeIdUnknownWhenPipelineIsNotRegistered()
        {
            var registeredPipelineId = new NodeId(720, 4);
            var orphanPipelineId = new NodeId(721, 4);
            var harness = new PipelineHarness(
                pipelineNodeId: registeredPipelineId,
                inferenceProvider: null,
                attachOrphanNodeId: orphanPipelineId);

            ServiceResult result = await harness.InvokeStop().ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                "Stop must refuse a call whose pipeline NodeId is not in the registry.");
        }

        [Test]
        public async Task StopReturnsBadNotSupportedWhenInferenceProviderIsNull()
        {
            var harness = new PipelineHarness(
                pipelineNodeId: new NodeId(722, 4),
                inferenceProvider: null);

            ServiceResult result = await harness.InvokeStop().ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported),
                "Without an inference provider the dispatcher must refuse the call with BadNotSupported.");
        }

        [Test]
        public async Task StopForwardsProviderResultOnSuccess()
        {
            var provider = new Mock<IVisionInferenceProvider>(MockBehavior.Strict);
            NodeId capturedNodeId = default;
            bool wasCalled = false;
            provider.Setup(p => p.StopAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns<NodeId, CancellationToken>((nodeId, _) =>
                {
                    capturedNodeId = nodeId;
                    wasCalled = true;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            var pipelineNodeId = new NodeId(723, 4);
            var harness = new PipelineHarness(
                pipelineNodeId: pipelineNodeId,
                inferenceProvider: provider.Object);

            ServiceResult result = await harness.InvokeStop().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True,
                    "A Good ServiceResult from the provider must be forwarded to the caller unchanged.");
                Assert.That(wasCalled, Is.True,
                    "The provider must have been invoked so the forwarded NodeId is observable.");
                Assert.That(capturedNodeId, Is.EqualTo(pipelineNodeId),
                    "The pipeline NodeId the delegate was wired for must be forwarded to the provider so it " +
                    "knows which pipeline to stop.");
            });
        }

        [Test]
        public async Task StopReturnsBadInternalErrorWhenProviderThrowsNonCancellationException()
        {
            var provider = new Mock<IVisionInferenceProvider>(MockBehavior.Strict);
            provider.Setup(p => p.StopAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("provider blew up"));
            var harness = new PipelineHarness(
                pipelineNodeId: new NodeId(724, 4),
                inferenceProvider: provider.Object);

            ServiceResult result = await harness.InvokeStop().ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInternalError),
                "A provider exception must be mapped to BadInternalError.");
        }

        [Test]
        public void StopPropagatesOperationCanceledExceptionFromProvider()
        {
            var provider = new Mock<IVisionInferenceProvider>(MockBehavior.Strict);
            provider.Setup(p => p.StopAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException("cancelled from provider"));
            var harness = new PipelineHarness(
                pipelineNodeId: new NodeId(725, 4),
                inferenceProvider: provider.Object);

            Assert.That(async () => await harness.InvokeStop().ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>(),
                "OperationCanceledException from the provider must be rethrown unchanged.");
        }

        [Test]
        public void AttachPipelineMethodsIsSafeWhenIndividualMethodsAreMissing()
        {
            // A pipeline surface built without all three method children must not cause the
            // dispatcher to throw at attach time — the InferencePipelineState type's method
            // children are all optional per its generated declaration.
            var pipeline = new InferencePipelineState(null)
            {
                RunInference = new RunInferenceMethodState(null)
                // StartContinuous and Stop deliberately left null.
            };
            var registration = new PipelineRegistration(
                "pipe", new NodeId(730, 4), pipeline, new HashSet<string>(StringComparer.Ordinal));
            var registry = new VisionRegistry();
            registry.AddPipeline(registration);
            var dispatcher = new VisionMethodDispatcher(registry, NullLogger.Instance);

            Assert.DoesNotThrow(() => dispatcher.AttachPipelineMethods(registration.NodeId, pipeline),
                "AttachPipelineMethods must tolerate a partial pipeline surface — a missing " +
                "StartContinuous or Stop method must not cause an NRE when only RunInference is wired.");
            Assert.That(pipeline.RunInference!.OnCallAsync, Is.Not.Null,
                "The RunInference handler must still be wired even when the other two methods are missing.");
        }

        private sealed class PipelineHarness
        {
            /// <summary>
            /// Builds a harness that registers a pipeline at
            /// <paramref name="pipelineNodeId"/> and attaches the delegate
            /// against the same NodeId, so calling the delegate finds the
            /// registration and the ordinary path runs. When
            /// <paramref name="attachOrphanNodeId"/> is non-Null, the
            /// delegate is instead attached against that orphan NodeId
            /// while the registration remains at
            /// <paramref name="pipelineNodeId"/>. That combination
            /// exercises the BadNodeIdUnknown branch — the closure looks up
            /// the orphan NodeId in the registry and finds nothing.
            /// </summary>
            public PipelineHarness(
                NodeId pipelineNodeId,
                IVisionInferenceProvider? inferenceProvider,
                NodeId sensorNodeId = default,
                NodeId deploymentNodeId = default,
                NodeId attachOrphanNodeId = default)
            {
                PipelineNodeId = pipelineNodeId;
                NodeId attachNodeId = attachOrphanNodeId.IsNull ? pipelineNodeId : attachOrphanNodeId;
                var pipeline = new InferencePipelineState(null)
                {
                    RunInference = new RunInferenceMethodState(null),
                    StartContinuous = new MethodState(null),
                    Stop = new MethodState(null)
                };
                if (!sensorNodeId.IsNull)
                {
                    var sensor = PropertyState<NodeId>.With<VariantBuilder>(pipeline);
                    sensor.Value = sensorNodeId;
                    pipeline.Sensor = sensor;
                }
                if (!deploymentNodeId.IsNull)
                {
                    var deployment = PropertyState<NodeId>.With<VariantBuilder>(pipeline);
                    deployment.Value = deploymentNodeId;
                    pipeline.Deployment = deployment;
                }
                var registration = new PipelineRegistration(
                    "pipe",
                    pipelineNodeId,
                    pipeline,
                    new HashSet<string>(StringComparer.Ordinal))
                {
                    InferenceProvider = inferenceProvider
                };
                m_registry = new VisionRegistry();
                m_registry.AddPipeline(registration);
                var dispatcher = new VisionMethodDispatcher(m_registry, NullLogger.Instance);
                // Attach against attachNodeId — normally the same as the registered NodeId,
                // but tests may supply an unregistered NodeId to exercise BadNodeIdUnknown.
                dispatcher.AttachPipelineMethods(attachNodeId, pipeline);
                m_runInference = pipeline.RunInference!.OnCallAsync;
                m_startContinuous = pipeline.StartContinuous!.OnCallMethod2Async;
                m_stop = pipeline.Stop!.OnCallMethod2Async;

                Assert.That(m_runInference, Is.Not.Null);
                Assert.That(m_startContinuous, Is.Not.Null);
                Assert.That(m_stop, Is.Not.Null);
            }

            public NodeId PipelineNodeId { get; }

            public async Task<RunInferenceMethodStateResult> InvokeRunInference(DateTimeUtc timestamp)
            {
                return await m_runInference!(
                    null!,
                    null!,
                    PipelineNodeId,
                    timestamp,
                    CancellationToken.None).ConfigureAwait(false);
            }

            public async Task<ServiceResult> InvokeStartContinuous()
            {
                var outputs = new List<Variant>();
                return await m_startContinuous!(
                    null!,
                    null!,
                    PipelineNodeId,
                    ArrayOf<Variant>.Empty,
                    outputs,
                    CancellationToken.None).ConfigureAwait(false);
            }

            public async Task<ServiceResult> InvokeStop()
            {
                var outputs = new List<Variant>();
                return await m_stop!(
                    null!,
                    null!,
                    PipelineNodeId,
                    ArrayOf<Variant>.Empty,
                    outputs,
                    CancellationToken.None).ConfigureAwait(false);
            }

            private readonly VisionRegistry m_registry;
            private readonly RunInferenceMethodStateMethodAsyncCallHandler? m_runInference;
            private readonly GenericMethodCalledEventHandler2Async? m_startContinuous;
            private readonly GenericMethodCalledEventHandler2Async? m_stop;
        }
    }
}
