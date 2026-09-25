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

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class VisionWorkflowTaskTests
    {
        [Test]
        public void TaskSnapshotsOriginalAndReturnedStructuresAndMetadata()
        {
            var fixture = new VisionWorkflowTestSupport();
            VisionDetectionDataType detection = VisionWorkflowTestSupport.Detection();
            ArrayOf<CompanionValue> inputs = VisionWorkflowTestSupport.Replace(
                fixture.Inputs("submit-detections"), "detections",
                Variant.FromStructure([detection]));
            byte[] metadata = [17, 31, 47];
            VisionWorkflowTask task = fixture.CreateTask("submit-detections", inputs, new ByteString(metadata));
            detection.ClassLabel = "changed-caller";
            metadata[0] = 99;
            Assert.That(task.Inputs[1].Value.TryGetValue(
                out ArrayOf<VisionDetectionDataType> snapshot, fixture.Context.Session.MessageContext), Is.True);
            Assert.That(snapshot[0].ClassLabel, Is.EqualTo("component"));
            snapshot[0].ClassLabel = "changed-reader";
            Assert.That(task.Inputs[1].Value.TryGetValue(
                out ArrayOf<VisionDetectionDataType> second, fixture.Context.Session.MessageContext), Is.True);
            Assert.That(second[0].ClassLabel, Is.EqualTo("component"));
            Assert.That(task.Binding.Metadata[0], Is.EqualTo(17));
            ByteString firstMetadata = task.Binding.Metadata;
            ByteString secondMetadata = task.Binding.Metadata;
            Assert.That(firstMetadata.Memory, Is.Not.EqualTo(secondMetadata.Memory));
            Assert.That(task.Review, Does.Not.Contain("changed-caller").And.Not.Contain("changed-reader"));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public void ReviewExpiresExactlyAtFiveMinutesButOwnedCleanupDoesNot(int ticks)
        {
            var fixture = new VisionWorkflowTestSupport();
            VisionWorkflowTask task = fixture.CreateTask("get-clip");
            fixture.UtcNow += TimeSpan.FromMinutes(5) + TimeSpan.FromTicks(ticks);

            if (ticks < 0)
            {
                Assert.That(() => task.RequireCurrent(fixture.Context, fixture.Clock.Object), Throws.Nothing);
            }
            else
            {
                Assert.That(() => task.RequireCurrent(fixture.Context, fixture.Clock.Object),
                    Throws.InvalidOperationException);
            }
            Assert.That(() => task.RequireOwnedSession(fixture.Context), Throws.Nothing);
        }

        [TestCase("session")]
        [TestCase("session-id")]
        [TestCase("identity")]
        [TestCase("endpoint")]
        [TestCase("application")]
        [TestCase("security-mode")]
        [TestCase("security-policy")]
        [TestCase("certificate")]
        [TestCase("namespace")]
        [TestCase("disconnected")]
        public void ChangedSessionEvidenceInvalidatesExecutionAndCleanup(string change)
        {
            var fixture = new VisionWorkflowTestSupport();
            VisionWorkflowTask task = fixture.CreateTask("get-clip");
            CompanionContext context = fixture.Context;
            switch (change)
            {
                case "session":
                    context = new VisionWorkflowTestSupport().Context;
                    break;
                case "session-id":
                    fixture.Session.SetupGet(value => value.SessionId).Returns(new NodeId("different-session", 1));
                    break;
                case "identity":
                    fixture.Session.SetupGet(value => value.Identity).Returns(new Mock<IUserIdentity>().Object);
                    break;
                case "endpoint":
                    fixture.Endpoint.EndpointUrl += "/different";
                    break;
                case "application":
                    fixture.Endpoint.Server.ApplicationUri = "urn:other:application";
                    break;
                case "security-mode":
                    fixture.Endpoint.SecurityMode = MessageSecurityMode.Sign;
                    break;
                case "security-policy":
                    fixture.Endpoint.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
                    break;
                case "certificate":
                    fixture.Endpoint.ServerCertificate = ByteString.From(2, 4, 6, 8);
                    break;
                case "namespace":
                    fixture.Fixture.NamespaceUris.GetIndexOrAppend("urn:changed:model");
                    break;
                case "disconnected":
                    fixture.Session.SetupGet(value => value.Connected).Returns(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }

            Assert.That(() => task.RequireCurrent(context, fixture.Clock.Object), Throws.InvalidOperationException);
            Assert.That(() => task.RequireOwnedSession(context), Throws.InvalidOperationException);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public async Task ConcurrentDispatchConsumesExactlyOneRequest()
        {
            var fixture = new VisionWorkflowTestSupport();
            VisionWorkflowTask task = fixture.CreateTask("get-clip");
            int accepted = 0;
            int rejected = 0;
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                try
                {
                    task.BeginExecution();
                    Interlocked.Increment(ref accepted);
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref rejected);
                }
            }))).ConfigureAwait(false);

            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(rejected, Is.EqualTo(7));
            Assert.That(Guid.TryParseExact(task.RequestId, "N", out _), Is.True);
        }

        [TestCase("get-clip", 5)]
        [TestCase("probe-stream", 3)]
        [TestCase("configure-stream", 6)]
        [TestCase("select-endpoints", 2)]
        [TestCase("run-inference", 1)]
        [TestCase("run-continuous-window", 1)]
        [TestCase("submit-detections", 5)]
        [TestCase("submit-inspection", 3)]
        [TestCase("submit-correction", 7)]
        [TestCase("submit-image-reference", 3)]
        public void DefinitionsRequireTypedDeploymentPreparation(string id, int inputs)
        {
            var fixture = new VisionWorkflowTestSupport();
            CompanionOperation operation = fixture.Definition(id).Operation;
            Assert.That(operation.Id, Is.EqualTo(id));
            Assert.That(operation.Safety, Is.EqualTo(CompanionOperationSafety.DeploymentMutation));
            Assert.That(operation.Inputs.Count, Is.EqualTo(inputs));
            Assert.That(operation.HasTypedInput, Is.True);
            foreach (CompanionInputDefinition field in operation.Inputs)
            {
                Assert.That(() => CompanionInputContract.ValidateDefinition(field), Throws.Nothing);
                if (field.RequiresEditor)
                {
                    Assert.That(field.DataTypeId.NamespaceUri, Is.EqualTo(Opc.Ua.Vision.Namespaces.Vision));
                }
            }
            Assert.That(VisionTaskDefinition.ForTarget("CoordinateFrame").IsEmpty, Is.True);
            Assert.That(VisionTaskDefinition.ForTarget("VisionResult").IsEmpty, Is.True);
        }

        [Test]
        public async Task BinaryStructureInputsAreRegisteredAndDecodedBeforePreparation()
        {
            var fixture = new VisionWorkflowTestSupport(registerTypes: false);
            VisionDetectionDataType detection = VisionWorkflowTestSupport.Detection();
            ByteString bytes;
            using (var encoder = new BinaryEncoder(fixture.Context.Session.MessageContext))
            {
                detection.Encode(encoder);
                bytes = ByteString.From(encoder.CloseAndReturnBuffer());
            }
            var encoded = new ExtensionObject(detection.BinaryEncodingId, bytes);
            ArrayOf<CompanionValue> inputs = VisionWorkflowTestSupport.Replace(
                fixture.Inputs("submit-detections"), "detections", Variant.From([encoded]));
            var provider = new VisionCompanionProvider(fixture.AllowExecution().Object, fixture.Clock.Object);

            CompanionTaskInput prepared = await provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "submit-detections", inputs, CancellationToken.None).ConfigureAwait(false);

            Assert.That(prepared, Is.TypeOf<VisionWorkflowTask>());
            Assert.That(fixture.Context.Session.Factory.TryGetEncodeableType(detection.BinaryEncodingId, out _),
                Is.True);
            Assert.That(((VisionWorkflowTask)prepared).Inputs[1].Value.TryGetValue(
                out ArrayOf<VisionDetectionDataType> decoded, fixture.Context.Session.MessageContext), Is.True);
            Assert.That(decoded.Count, Is.EqualTo(1));
            Assert.That(decoded[0].DetectionId, Is.EqualTo("detection-7"));
            Assert.That(decoded[0].BoundingBox2D.CenterX, Is.EqualTo(30.5));
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public void OptionalFrameOmissionIsExplicitAndRevokesThePreviousPreparation()
        {
            var fixture = new VisionWorkflowTestSupport();
            CompanionInputDefinition frame = fixture.Definition("submit-detections").Operation.Inputs[2];
            int changes = 0;
            var editor = new CompanionInputEditor(frame, () => changes++);
            Assert.That(editor.CanOmit, Is.True);
            Assert.That(() => editor.Capture(), Throws.InvalidOperationException);
            editor.Omitted = true;

            CompanionValue omitted = editor.Capture();

            Assert.That(omitted.Name, Is.EqualTo("image"));
            Assert.That(omitted.Value.TryGetValue(out ExtensionObject body), Is.True);
            Assert.That(body.IsNull, Is.True);
            Assert.That(editor.TypedSummary, Is.EqualTo("Optional structure omitted."));
            Assert.That(changes, Is.EqualTo(1));
            editor.AcceptValue(Variant.FromStructure(VisionWorkflowTestSupport.Image()),
                fixture.Context.Session.MessageContext, static () => { });
            Assert.That(editor.Omitted, Is.False);
            Assert.That(editor.Capture().Value.TryGetValue<VisionImageReferenceDataType>(
                out VisionImageReferenceDataType? image, fixture.Context.Session.MessageContext), Is.True);
            Assert.That(image!.Width, Is.EqualTo(640u));
            Assert.That(changes, Is.GreaterThan(1));
            var required = new CompanionInputEditor(frame with { Required = true }, static () => { })
            {
                Omitted = true
            };
            Assert.That(required.CanOmit, Is.False);
            Assert.That(() => required.Capture(), Throws.InvalidOperationException);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RealVisionProviderNeedsWorkspacePolicyAndIndependentConfirmation(bool granted, bool confirmed)
        {
            var fixture = new VisionWorkflowTestSupport();
            var root = NodeId.Create(
                Opc.Ua.Vision.Objects.Vision, Opc.Ua.Vision.Namespaces.Vision, fixture.Fixture.NamespaceUris);
            var pipelines = new NodeId("pipelines", fixture.Fixture.NamespaceIndex);
            fixture.AddChild(root, "Pipelines", pipelines);
            fixture.AddChild(pipelines, "Pipeline", fixture.Pipeline);
            fixture.AddResult();
            var execution = new Mock<IVisionExecutionPolicy>(MockBehavior.Strict);
            execution.Setup(value => value.AuthorizeAsync(
                It.IsAny<CompanionContext>(), It.IsAny<VisionWorkflowTask>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            var provider = new VisionCompanionProvider(execution.Object, fixture.Clock.Object);
            var deployment = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            deployment.Setup(value => value.AuthorizeAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()))
                .Returns((CompanionContext context, CompanionOperationDraft draft, CancellationToken _) =>
                {
                    Assert.That(context.Session, Is.SameAs(fixture.Session.Object));
                    Assert.That(draft.Target.NodeId, Is.EqualTo(fixture.Pipeline));
                    Assert.That(draft.Operation.Id, Is.EqualTo("run-inference"));
                    return ValueTask.FromResult(new CompanionDeploymentGrant("fixture", "v1", draft.ExpiresAt));
                });
            var workspace = new CompanionWorkspace([provider], fixture.Context.Telemetry, fixture.Clock.Object,
                granted ? deployment.Object : null);
            await using (workspace.ConfigureAwait(false))
            {
                await workspace.BindAsync(fixture.Session.Object).ConfigureAwait(false);
                ArrayOf<CompanionTarget> targets = await workspace.DiscoverAsync("vision").ConfigureAwait(false);
                CompanionTarget target = targets.ToList().Single(value => value.TypeName == "InferencePipeline");
                await workspace.InspectAsync(target).ConfigureAwait(false);
                if (!granted)
                {
                    await Assert.ThatAsync(() => workspace.PrepareTaskAsync(
                        target, "run-inference", fixture.Inputs("run-inference")),
                        Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("No configured"))
                        .ConfigureAwait(false);
                    fixture.Fixture.VerifyNoMutationOrSessionOwnership();
                    return;
                }
                CompanionOperationDraft draft = await workspace.PrepareTaskAsync(
                    target, "run-inference", fixture.Inputs("run-inference")).ConfigureAwait(false);
                Assert.That(draft.DeploymentGrant?.RuleId, Is.EqualTo("fixture"));
                fixture.OnCall = (_, _) => ValueTask.FromResult(new CallMethodResult
                {
                    OutputArguments = [Variant.From("result-7")]
                });
                if (confirmed)
                {
                    CompanionOperationResult result = await workspace.ExecuteAsync(
                        draft, confirmLocalSample: false, confirmDeployment: true).ConfigureAwait(false);
                    Assert.That(result.Summary, Does.Contain("exact returned ResultId"));
                    Assert.That(fixture.Calls.Count, Is.EqualTo(1));
                }
                else
                {
                    await Assert.ThatAsync(() => workspace.ExecuteAsync(
                        draft, confirmLocalSample: true, confirmDeployment: false),
                        Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("Confirm"))
                        .ConfigureAwait(false);
                    Assert.That(fixture.Calls.Count, Is.Zero);
                }
            }
            fixture.VerifyBorrowedSession();
        }
    }
}
