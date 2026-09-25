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
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.VisionWorkflowTestSupport;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class VisionWorkflowAccessTests
    {
        [TestCase("name")]
        [TestCase("count")]
        [TestCase("type")]
        [TestCase("rank")]
        public async Task InvalidShapeIsRejectedBeforeAddressSpaceReads(string fault)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs = fixture.Inputs("get-clip");
            inputs = fault switch
            {
                "name" => inputs.ConvertAll(input => input with { Name = "wrong" }),
                "count" => [],
                "type" => Replace(inputs, "timestamp", Variant.From("not-a-timestamp")),
                "rank" => Replace(inputs, "timestamp", Variant.From(
                    [new DateTimeUtc(fixture.UtcNow)])),
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };
            var provider = new VisionCompanionProvider(timeProvider: fixture.Clock.Object);
            await Assert.ThatAsync(() => provider.PrepareInputAsync(
                fixture.Context, fixture.SensorTarget, "get-clip", inputs, CancellationToken.None).AsTask(),
                Throws.ArgumentException).ConfigureAwait(false);

            fixture.Session.Verify(value => value.ReadAsync(It.IsAny<RequestHeader>(), It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(), It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(VisionRealityKindEnum.Physical)]
        [TestCase(VisionRealityKindEnum.Hybrid)]
        public async Task NonSimulatedSensorsCannotAuthorizeMedia(VisionRealityKindEnum reality)
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.Set(fixture.Sensor, "RealityKind", Variant.From((int)reality));
            await Assert.ThatAsync(() => PrepareAsync(fixture, "get-clip"),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase("disconnected")]
        [TestCase("null-session-id")]
        [TestCase("sign-only")]
        [TestCase("none")]
        [TestCase("none-policy")]
        [TestCase("empty-policy")]
        public async Task PreparationRejectsDisconnectedOrUnsecuredVisionSessions(string fault)
        {
            var fixture = new VisionWorkflowTestSupport();
            switch (fault)
            {
                case "disconnected":
                    fixture.Session.SetupGet(session => session.Connected).Returns(false);
                    break;
                case "null-session-id":
                    fixture.Session.SetupGet(session => session.SessionId).Returns(NodeId.Null);
                    break;
                case "sign-only":
                    fixture.Endpoint.SecurityMode = MessageSecurityMode.Sign;
                    break;
                case "none":
                    fixture.Endpoint.SecurityMode = MessageSecurityMode.None;
                    break;
                case "none-policy":
                    fixture.Endpoint.SecurityPolicyUri = SecurityPolicies.None;
                    break;
                case "empty-policy":
                    fixture.Endpoint.SecurityPolicyUri = string.Empty;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            if (fault is "disconnected" or "null-session-id")
            {
                await Assert.ThatAsync(() => PrepareAsync(fixture, "get-clip"), Throws.InvalidOperationException)
                    .ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => PrepareAsync(fixture, "get-clip"),
                    Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            }
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase("SensorId", false)]
        [TestCase("SensorId", true)]
        [TestCase("RealityKind", false)]
        [TestCase("RealityKind", true)]
        [TestCase("FrameId", false)]
        [TestCase("FrameId", true)]
        [TestCase("EndpointUri", false)]
        [TestCase("EndpointUri", true)]
        [TestCase("SecureTransport", false)]
        [TestCase("SecureTransport", true)]
        public async Task RequiredMetadataRejectsNonGoodQuality(string name, bool uncertain)
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId owner = name is "SensorId" or "RealityKind" or "FrameId" ? fixture.Sensor : fixture.Clip;
            NodeId property = fixture.Fixture.Children[(owner, name)];
            StatusCode status = uncertain ? StatusCodes.Uncertain : StatusCodes.BadUserAccessDenied;
            fixture.Fixture.Values[(property, Attributes.Value)] =
                fixture.Fixture.Values[(property, Attributes.Value)].WithStatus(status);

            await Assert.ThatAsync(() => PrepareAsync(fixture, "get-clip"),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                .ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ObjectClassQualityCannotAuthorizeATarget(bool uncertain)
        {
            var fixture = new VisionWorkflowTestSupport();
            StatusCode status = uncertain ? StatusCodes.Uncertain : StatusCodes.BadUserAccessDenied;
            fixture.Fixture.Values[(fixture.Sensor, Attributes.NodeClass)] =
                new DataValue(Variant.From((int)NodeClass.Object)).WithStatus(status);

            await Assert.ThatAsync(() => PrepareAsync(fixture, "get-clip"),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                .ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UncertainServiceOrBrowseResultCannotEstablishTargetIdentity(bool browse)
        {
            var fixture = new VisionWorkflowTestSupport();
            if (browse)
            {
                fixture.BrowseStatus = StatusCodes.Uncertain;
            }
            else
            {
                fixture.Session.Setup(session => session.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                    .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> reads,
                        CancellationToken _) => ValueTask.FromResult(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader
                            {
                                ServiceResult = reads[0].AttributeId == Attributes.NodeClass
                                    ? StatusCodes.Uncertain : StatusCodes.Good
                            },
                            Results = reads.ConvertAll(read => fixture.Fixture.Values[(read.NodeId, read.AttributeId)])
                        }));
            }

            await Assert.ThatAsync(() => PrepareAsync(fixture, "get-clip"),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.Uncertain))
                .ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase("get-clip")]
        [TestCase("submit-detections")]
        public async Task AMethodNameOnAnotherComponentTypeDoesNotAuthorizeIt(string operation)
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.SetType(operation == "get-clip" ? fixture.Media : fixture.Feedback,
                Opc.Ua.ObjectTypeIds.BaseObjectType);
            await Assert.ThatAsync(() => PrepareAsync(fixture, operation),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadTypeMismatch))
                .ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(Attributes.Executable, false)]
        [TestCase(Attributes.Executable, true)]
        [TestCase(Attributes.UserExecutable, false)]
        [TestCase(Attributes.UserExecutable, true)]
        public async Task MethodPermissionQualityCannotBecomeAuthorization(uint attribute, bool uncertain)
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId method = fixture.Fixture.Children[(fixture.Media, "GetClip")];
            StatusCode status = uncertain ? StatusCodes.Uncertain : StatusCodes.BadUserAccessDenied;
            fixture.Fixture.Values[(method, attribute)] = new DataValue(Variant.From(true)).WithStatus(status);
            await Assert.ThatAsync(() => PrepareAsync(fixture, "get-clip"),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                .ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase("sensor-id")]
        [TestCase("frame")]
        [TestCase("endpoint-id")]
        [TestCase("endpoint-uri")]
        [TestCase("authentication")]
        [TestCase("state")]
        [TestCase("component")]
        public async Task ChangedBindingInvalidatesPreparationBeforeDispatch(string change)
        {
            var fixture = new VisionWorkflowTestSupport();
            var provider = new VisionCompanionProvider(timeProvider: fixture.Clock.Object);
            CompanionTaskInput task = await provider.PrepareInputAsync(fixture.Context, fixture.SensorTarget,
                "get-clip", fixture.Inputs("get-clip"), CancellationToken.None).ConfigureAwait(false);
            switch (change)
            {
                case "sensor-id":
                    fixture.Set(fixture.Sensor, "SensorId", Variant.From("replacement"));
                    break;
                case "frame":
                    fixture.Set(fixture.Sensor, "FrameId", Variant.From("new-frame"));
                    break;
                case "endpoint-id":
                    fixture.Set(fixture.Clip, "EndpointId", Variant.From("new-endpoint"));
                    break;
                case "endpoint-uri":
                    fixture.Set(fixture.Clip, "EndpointUri", Variant.From("https://localhost/changed"));
                    break;
                case "authentication":
                    fixture.Set(fixture.Clip, "Authentication", Variant.From(1));
                    break;
                case "state":
                    fixture.Set(fixture.Clip, "State", Variant.From((int)VisionEndpointStateEnum.Active));
                    break;
                case "component":
                    fixture.SetType(fixture.Media, Opc.Ua.ObjectTypeIds.BaseObjectType);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
            await Assert.ThatAsync(() => provider.ExecutePreparedAsync(fixture.Context, fixture.SensorTarget,
                "get-clip", task, null, CancellationToken.None).AsTask(),
                Throws.TypeOf<InvalidOperationException>().Or.TypeOf<ServiceResultException>())
                .ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public async Task OperationOfferingRequiresBothActionAndOwnedCleanup()
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionOperation> sensor = await VisionWorkflows.OperationsAsync(
                fixture.Context, fixture.SensorTarget, [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(sensor.Count, Is.EqualTo(4));
            Assert.That(sensor.ToList().All(value => value.Safety == CompanionOperationSafety.DeploymentMutation),
                Is.True);
            fixture.Fixture.Children.Remove((fixture.Media, "ReleaseStreamEndpoint"));
            sensor = await VisionWorkflows.OperationsAsync(
                fixture.Context, fixture.SensorTarget, [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(sensor.ToList().Select(value => value.Id), Does.Not.Contain("probe-stream"));
            fixture.Fixture.Children.Remove((fixture.Pipeline, "Stop"));
            ArrayOf<CompanionOperation> pipeline = await VisionWorkflows.OperationsAsync(
                fixture.Context, fixture.PipelineTarget, [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(pipeline.ToList().Select(value => value.Id), Does.Not.Contain("run-continuous-window"));
            Assert.That(pipeline.ToList().Select(value => value.Id), Does.Contain("run-inference"));
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        private static Task<CompanionTaskInput> PrepareAsync(VisionWorkflowTestSupport fixture, string operation)
        {
            var provider = new VisionCompanionProvider(fixture.AllowExecution().Object, fixture.Clock.Object);
            return provider.PrepareInputAsync(fixture.Context, fixture.Target(operation),
                operation, fixture.Inputs(operation), CancellationToken.None).AsTask();
        }
    }
}
