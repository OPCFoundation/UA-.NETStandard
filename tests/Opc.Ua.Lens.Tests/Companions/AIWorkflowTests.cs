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
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;
using Ai = Opc.Ua.AI;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class AIWorkflowTests
    {
        [Test]
        public async Task LocalRequestUsesExactImmutableWireArgumentsAndTypedResponseAsync()
        {
            var fixture = new AIWorkflowFixture();
            ByteString approved = fixture.Payload.Copy();
            var input = (AICompanionTaskInput)await fixture.PrepareAsync("invoke-request").ConfigureAwait(false);
            Assert.That(input.Review, Does.Contain("http://localhost:8080/v1"));
            Assert.That(input.Review, Does.Contain("egress permitted: False"));
            Assert.That(input.Review, Does.Contain("OPC UA destination: opc.tcp://localhost:4840/AI"));
            Assert.That(input.Review, Does.Not.Contain("private-prompt-marker"));
            Assert.That(input.ModelId, Is.EqualTo(fixture.Model.NodeId));
            Assert.That(input.Payload, Is.EqualTo(approved));
            fixture.Payload = ByteString.From("{\"messages\":[]}"u8);
            fixture.VerifyNoMutation();

            CompanionOperationResult result = await fixture.ExecuteAsync("invoke-request", input)
                .ConfigureAwait(false);

            CallMethodRequest call = fixture.Call("Invoke");
            Assert.That(call.ObjectId, Is.EqualTo(fixture.Deployment.NodeId));
            Assert.That(call.MethodId, Is.EqualTo(fixture.Server.Resolve(Ai.MethodIds.DeploymentType_Invoke)));
            Assert.That(call.InputArguments, Has.Count.EqualTo(5));
            Assert.That(call.InputArguments[0].TryGetValue(out ByteString payload), Is.True);
            Assert.That(payload, Is.EqualTo(approved));
            Assert.That(call.InputArguments[1].TryGetValue(out string? uri), Is.True);
            Assert.That(uri, Is.Empty);
            Assert.That(call.InputArguments[2].TryGetValue(out string? contentType), Is.True);
            Assert.That(contentType, Is.EqualTo("application/json"));
            Assert.That(call.InputArguments[3].TryGetValue(
                out ArrayOf<Opc.Ua.KeyValuePair> parameters, fixture.Server.MessageContext), Is.True);
            Assert.That(parameters.IsEmpty, Is.True);
            Assert.That(call.InputArguments[4].TryGetValue(out double timeout), Is.True);
            Assert.That(timeout, Is.EqualTo(1250d));
            Assert.That(Field(result.Values, "Response").TryGetValue(out ByteString response), Is.True);
            Assert.That(response, Is.EqualTo(fixture.Response));
            Assert.That(Field(result.Values, "Model used").TryGetValue(out NodeId model), Is.True);
            Assert.That(model, Is.EqualTo(fixture.Model.NodeId));
            Assert.That(Field(result.Values, "Usage").TryGetValue<Ai.UsageDataType>(
                out Ai.UsageDataType? usage, fixture.Server.MessageContext), Is.True);
            Assert.That(usage, Is.Not.Null);
            Assert.That(usage!.InputUnits, Is.EqualTo(11ul));
            Assert.That(usage.OutputUnits, Is.EqualTo(7ul));
            Assert.That(usage.TotalUnits, Is.EqualTo(18ul));
            Assert.That(Field(result.Values, "Complete response").TryGetValue(out bool complete), Is.True);
            Assert.That(complete, Is.True);
            Assert.That(fixture.Mutations, Is.EqualTo(sInvoke));
            fixture.VerifyBorrowedSession();
        }

        [TestCase(Ai.FinishReasonEnum.Stop, true)]
        [TestCase(Ai.FinishReasonEnum.Length, false)]
        [TestCase(Ai.FinishReasonEnum.ToolCall, false)]
        [TestCase(Ai.FinishReasonEnum.Filtered, false)]
        [TestCase(Ai.FinishReasonEnum.Cancelled, false)]
        [TestCase(Ai.FinishReasonEnum.Error, false)]
        public async Task FinishReasonsRemainDistinctAndNeverTriggerToolsOrFollowupRequestsAsync(
            Ai.FinishReasonEnum finishReason, bool complete)
        {
            var fixture = new AIWorkflowFixture { FinishReason = finishReason };
            CompanionTaskInput input = await fixture.PrepareAsync("invoke-request").ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("invoke-request", input)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Finish reason").TryGetValue(out string? actualReason), Is.True);
            Assert.That(actualReason, Is.EqualTo(finishReason.ToString()));
            Assert.That(Field(result.Values, "Complete response").TryGetValue(out bool actualComplete), Is.True);
            Assert.That(actualComplete, Is.EqualTo(complete));
            Assert.That(fixture.Mutations, Is.EqualTo(sInvoke));
        }

        [Test]
        public async Task MissingInlineModelProvenanceDoesNotBecomeACompleteResponseAsync()
        {
            var fixture = new AIWorkflowFixture { ReturnedModel = NodeId.Null };
            CompanionTaskInput input = await fixture.PrepareAsync("invoke-request").ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("invoke-request", input)
                .ConfigureAwait(false);

            Assert.That(result.Summary, Does.Contain("not established"));
            Assert.That(Field(result.Values, "Complete response").TryGetValue(out bool complete), Is.True);
            Assert.That(complete, Is.False);
            Assert.That(fixture.Mutations, Is.EqualTo(sInvoke));
        }

        [Test]
        public async Task AnUnapprovedReturnedModelFailsWithoutRetryOrFallbackAsync()
        {
            var fixture = new AIWorkflowFixture();
            fixture.ReturnedModel = fixture.Candidate.NodeId;
            CompanionTaskInput input = await fixture.PrepareAsync("invoke-request").ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync("invoke-request", input),
                Throws.TypeOf<ServiceResultException>().With.Message.Contains("approved model")).ConfigureAwait(false);

            Assert.That(fixture.Mutations, Is.EqualTo(sInvoke));
        }

        [Test]
        public async Task DiscoveryAndInspectionKeepSelectionAndSeparateDeploymentSafetyAsync()
        {
            var fixture = new AIWorkflowFixture();
            ArrayOf<CompanionTarget> targets = await fixture.Provider.DiscoverAsync(
                fixture.Context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(targets.Contains(target => target.NodeId == fixture.Model.NodeId), Is.True);
            Assert.That(targets.Contains(target => target.NodeId == fixture.Dataset.NodeId), Is.True);
            Assert.That(targets.Contains(target => target.NodeId == fixture.Deployment.NodeId), Is.True);
            Assert.That(targets.Contains(target => target.NodeId == fixture.Job.NodeId), Is.True);
            Assert.That(targets.Contains(target => target.NodeId == fixture.Learning.NodeId), Is.True);
            Assert.That(targets.Contains(target => target.NodeId == fixture.Transfer.NodeId), Is.True);
            Assert.That(targets.Contains(target => target.NodeId == fixture.Evaluation.NodeId), Is.True);

            CompanionInspection inspection = await fixture.Provider.InspectAsync(
                fixture.Context, fixture.Deployment, CancellationToken.None).ConfigureAwait(false);
            Assert.That(inspection.Operations.Contains(operation =>
                operation.Id == "invoke-sample" && operation.Safety == CompanionOperationSafety.SampleMutation),
                Is.True);
            Assert.That(inspection.Operations.Contains(operation =>
                operation.Id == "capabilities" && operation.Safety == CompanionOperationSafety.ReadOnly), Is.True);
            foreach (string id in new[] { "invoke-request", "submit-inference-job", "submit-transfer-request" })
            {
                CompanionOperation operation = inspection.Operations.ToList().Single(value => value.Id == id);
                Assert.That(operation.Safety, Is.EqualTo(CompanionOperationSafety.DeploymentMutation));
                Assert.That(operation.HasTypedInput, Is.True);
                CompanionInputDefinition parameters = operation.Inputs.ToList()
                    .Single(value => value.Name == "parameters");
                Assert.That(parameters.DataType, Is.EqualTo(BuiltInType.ExtensionObject));
                Assert.That(parameters.DataTypeId, Is.EqualTo(new ExpandedNodeId(DataTypes.KeyValuePair)));
                Assert.That(parameters.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(parameters.ArrayDimensions, Has.Count.EqualTo(1));
                Assert.That(parameters.ArrayDimensions[0], Is.Zero);
            }
            fixture.VerifyNoMutation();
        }

        [Test]
        public async Task LegacyInvokeRemainsFixedSyntheticAndRejectsUserInputAsync()
        {
            var fixture = new AIWorkflowFixture();
            await Assert.ThatAsync(
                () => fixture.Provider.ExecuteAsync(fixture.Context, fixture.Deployment, "invoke-sample",
                    "private-prompt-marker", CancellationToken.None).AsTask(),
                Throws.ArgumentException).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => fixture.Provider.ExecuteAsync(fixture.Context, fixture.Deployment, "invoke-request",
                    null, CancellationToken.None).AsTask(),
                Throws.ArgumentException).ConfigureAwait(false);
            fixture.VerifyNoMutation();

            await fixture.Provider.ExecuteAsync(
                fixture.Context, fixture.Deployment, "invoke-sample", null, CancellationToken.None)
                .ConfigureAwait(false);

            CallMethodRequest request = fixture.Call("Invoke");
            Assert.That(request.InputArguments[0].TryGetValue(out ByteString payload), Is.True);
            Assert.That(payload, Is.EqualTo(ByteString.From(
                "{\"messages\":[{\"role\":\"user\",\"content\":\"Reply with UaLens sample.\"}]}"u8)));
            Assert.That(request.InputArguments[4].TryGetValue(out double timeout), Is.True);
            Assert.That(timeout, Is.EqualTo(10000d));
            fixture.VerifyBorrowedSession();
        }

        [TestCase("https://service.invalid/v1", true, Ai.InferenceLocationEnum.Cloud)]
        [TestCase("http://localhost:8080/v1", true, Ai.InferenceLocationEnum.OnServer)]
        [TestCase("https://edge.invalid/v1", false, Ai.InferenceLocationEnum.EdgeOffServer)]
        public async Task DefaultPolicyRejectsExternalEgressBeforeEvenAProbeAsync(
            string endpoint, bool egress, Ai.InferenceLocationEnum location)
        {
            var fixture = new AIWorkflowFixture();
            fixture.SetDestination(endpoint, egress, location);

            await Assert.ThatAsync(() => fixture.PrepareAsync("invoke-request"),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task ConfiguredEgressPolicyReceivesExactRequestAndMustAuthorizeAgainAsync()
        {
            var policy = new Mock<IAITaskEgressPolicy>(MockBehavior.Strict);
            var fixture = new AIWorkflowFixture(policy.Object);
            fixture.SetDestination("https://approved.invalid/v1", true, Ai.InferenceLocationEnum.Cloud);
            int authorizations = 0;
            AICompanionTaskInput? approved = null;
            policy.Setup(value => value.AuthorizeAsync(
                    fixture.Context, It.IsAny<AICompanionTaskInput>(), It.IsAny<CancellationToken>()))
                .Returns((CompanionContext _, AICompanionTaskInput request, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    authorizations++;
                    Assert.That(request.Target, Is.EqualTo(fixture.Deployment));
                    Assert.That(request.OperationId, Is.EqualTo("invoke-request"));
                    Assert.That(request.Payload, Is.EqualTo(fixture.Payload));
                    Assert.That(request.Deployment!.EndpointUri, Is.EqualTo("https://approved.invalid/v1"));
                    if (approved is null)
                    {
                        approved = request;
                    }
                    else
                    {
                        Assert.That(request, Is.SameAs(approved));
                    }
                    return ValueTask.CompletedTask;
                });
            CompanionTaskInput input = await fixture.PrepareAsync("invoke-request").ConfigureAwait(false);

            await fixture.ExecuteAsync("invoke-request", input).ConfigureAwait(false);

            Assert.That(authorizations, Is.EqualTo(2));
            Assert.That(fixture.Mutations, Is.EqualTo(sInvoke));
            policy.VerifyAll();
        }

        [Test]
        public async Task RevokedExactEgressAuthorizationPreventsDispatchAsync()
        {
            var policy = new Mock<IAITaskEgressPolicy>(MockBehavior.Strict);
            var fixture = new AIWorkflowFixture(policy.Object);
            fixture.SetDestination("https://approved.invalid/v1", true, Ai.InferenceLocationEnum.Cloud);
            policy.SetupSequence(value => value.AuthorizeAsync(
                    fixture.Context, It.IsAny<AICompanionTaskInput>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask)
                .Throws(new UnauthorizedAccessException("The exact AI grant was revoked."));
            CompanionTaskInput input = await fixture.PrepareAsync("invoke-request").ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync("invoke-request", input),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [TestCase("http://localhost:8080/v1?secret=hidden")]
        [TestCase("http://user:password@localhost:8080/v1")]
        [TestCase("http://localhost:8080/v1#fragment")]
        [TestCase("mqtt://localhost:1883/inference")]
        [TestCase("file:///C:/models/model.bin")]
        public async Task UnsafeOrBrokerDestinationsCannotBeOverriddenByAnEgressPolicyAsync(string endpoint)
        {
            var policy = new Mock<IAITaskEgressPolicy>(MockBehavior.Strict);
            var fixture = new AIWorkflowFixture(policy.Object);
            fixture.SetDestination(endpoint, false, Ai.InferenceLocationEnum.OnServer);

            await Assert.ThatAsync(() => fixture.PrepareAsync("invoke-request"),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

            policy.Verify(value => value.AuthorizeAsync(
                It.IsAny<CompanionContext>(), It.IsAny<AICompanionTaskInput>(), It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [TestCase("name")]
        [TestCase("count")]
        [TestCase("type")]
        [TestCase("rank")]
        [TestCase("parameters")]
        [TestCase("json")]
        [TestCase("array-json")]
        [TestCase("content-type")]
        [TestCase("empty")]
        [TestCase("oversize")]
        [TestCase("timeout-zero")]
        [TestCase("timeout-nan")]
        [TestCase("timeout-over")]
        public async Task InvalidSchemaPayloadAndCapacityAreRejectedWithoutMutationAsync(string fault)
        {
            var fixture = new AIWorkflowFixture();
            var inputs = fixture.RequestInputs("invoke-request").ToList();
            switch (fault)
            {
                case "name":
                    inputs[0] = inputs[0] with { Name = "differentModel" };
                    break;
                case "count":
                    inputs.RemoveAt(0);
                    break;
                case "type":
                    inputs[0] = inputs[0] with { Value = Variant.From("not a NodeId") };
                    break;
                case "rank":
                    inputs[2] = inputs[2] with
                    {
                        Value = Variant.From(ArrayOf.Wrapped([fixture.Payload]))
                    };
                    break;
                case "parameters":
                    inputs[4] = inputs[4] with
                    {
                        Value = Variant.FromStructure<Opc.Ua.KeyValuePair>(
                        [
                            new() { Key = new QualifiedName("temperature"), Value = Variant.From(0.25d) }
                        ])
                    };
                    break;
                case "json":
                    inputs[2] = inputs[2] with { Value = Variant.From(ByteString.From("{"u8)) };
                    break;
                case "array-json":
                    inputs[2] = inputs[2] with { Value = Variant.From(ByteString.From("[]"u8)) };
                    break;
                case "content-type":
                    inputs[3] = inputs[3] with { Value = Variant.From("text/plain") };
                    break;
                case "empty":
                    inputs[2] = inputs[2] with { Value = Variant.From(ByteString.Empty) };
                    break;
                case "oversize":
                    inputs[2] = inputs[2] with
                    {
                        Value = Variant.From(ByteString.From(new byte[AICompanionProvider.MaximumInlineBytes + 1]))
                    };
                    break;
                case "timeout-zero":
                    inputs[5] = inputs[5] with { Value = Variant.From(0d) };
                    break;
                case "timeout-nan":
                    inputs[5] = inputs[5] with { Value = Variant.From(double.NaN) };
                    break;
                case "timeout-over":
                    inputs[5] = inputs[5] with { Value = Variant.From(30001d) };
                    break;
                default:
                    throw new AssertionException("Unspecified invalid-input partition.");
            }

            await Assert.ThatAsync(
                () => fixture.Provider.PrepareInputAsync(
                    fixture.Context, fixture.Deployment, "invoke-request", inputs.ToArrayOf(), CancellationToken.None)
                    .AsTask(),
                fault == "parameters"
                    ? Throws.TypeOf<ServiceResultException>()
                    : fault == "json"
                        ? Throws.InstanceOf<System.Text.Json.JsonException>() : Throws.ArgumentException)
                .ConfigureAwait(false);

            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [TestCase(1d)]
        [TestCase(30000d)]
        public async Task InvokeAcceptsBothTimeoutBoundariesWithoutChangingThemAsync(double timeout)
        {
            var fixture = new AIWorkflowFixture();
            var values = fixture.RequestInputs("invoke-request").ToList();
            values[5] = values[5] with { Value = Variant.From(timeout) };
            CompanionTaskInput input = await fixture.Provider.PrepareInputAsync(
                fixture.Context, fixture.Deployment, "invoke-request", values.ToArrayOf(), CancellationToken.None)
                .ConfigureAwait(false);

            await fixture.ExecuteAsync("invoke-request", input).ConfigureAwait(false);

            Assert.That(fixture.Call("Invoke").InputArguments[4].TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(timeout));
        }

        [TestCase(0)]
        [TestCase(1)]
        public async Task InlineServerCapIsEnforcedAtTheExactRequestSizeAsync(int excess)
        {
            var fixture = new AIWorkflowFixture();
            fixture.Set(fixture.Deployment, Ai.BrowseNames.MaxInlinePayloadSize,
                Variant.From((uint)(fixture.Payload.Length - excess)));
            if (excess == 0)
            {
                CompanionTaskInput input = await fixture.PrepareAsync("invoke-request").ConfigureAwait(false);
                await fixture.ExecuteAsync("invoke-request", input).ConfigureAwait(false);
                Assert.That(fixture.Mutations, Is.EqualTo(sInvoke));
            }
            else
            {
                await Assert.ThatAsync(() => fixture.PrepareAsync("invoke-request"),
                    Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
                Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
            }
        }

        [TestCase("invoke-request")]
        [TestCase("submit-inference-job")]
        public async Task ZeroPublishedInlineLimitForbidsInlineRequests(string operation)
        {
            var fixture = new AIWorkflowFixture();
            fixture.Set(fixture.Deployment, Ai.BrowseNames.MaxInlinePayloadSize, Variant.From(0u));

            await Assert.ThatAsync(() => fixture.PrepareAsync(operation),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);

            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task ZeroInlineLimitStillAllowsAnExplicitTransferPreparation()
        {
            var fixture = new AIWorkflowFixture();
            fixture.Set(fixture.Deployment, Ai.BrowseNames.MaxInlinePayloadSize, Variant.From(0u));

            CompanionTaskInput input = await fixture.PrepareAsync("submit-transfer-request").ConfigureAwait(false);

            Assert.That(input, Is.TypeOf<AICompanionTaskInput>());
            Assert.That(((AICompanionTaskInput)input).OperationId, Is.EqualTo("submit-transfer-request"));
            fixture.VerifyNoMutation();
        }

        [TestCase("missing")]
        [TestCase("status-code")]
        [TestCase("wide-integer")]
        public async Task InlineLimitRequiresTheAdvertisedUInt32Type(string fault)
        {
            var fixture = new AIWorkflowFixture();
            if (fault == "missing")
            {
                fixture.Hide(fixture.Deployment, Ai.BrowseNames.MaxInlinePayloadSize);
            }
            else
            {
                fixture.Set(fixture.Deployment, Ai.BrowseNames.MaxInlinePayloadSize,
                    fault == "status-code" ? Variant.From(StatusCodes.BadNotFound) : Variant.From(16384UL));
            }

            await Assert.ThatAsync(() => fixture.PrepareAsync("invoke-request"),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
            fixture.VerifyNoMutation();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OnServerDeploymentCanOmitItsSeparateBackendUri(bool absent)
        {
            var fixture = new AIWorkflowFixture();
            fixture.SetDestination(string.Empty, false, Ai.InferenceLocationEnum.OnServer);
            if (absent)
            {
                fixture.Hide(fixture.Deployment, Ai.BrowseNames.EndpointUri);
            }

            CompanionTaskInput input = await fixture.PrepareAsync("invoke-request").ConfigureAwait(false);

            Assert.That(input.Review, Does.Contain("the selected OPC UA server (OnServer)"));
            fixture.VerifyNoMutation();
            await fixture.ExecuteAsync("invoke-request", input).ConfigureAwait(false);
            Assert.That(fixture.Mutations, Is.EqualTo(sInvoke));
        }

        [TestCase(Ai.InferenceLocationEnum.EdgeOffServer)]
        [TestCase(Ai.InferenceLocationEnum.Cloud)]
        [TestCase(Ai.InferenceLocationEnum.InSimulator)]
        public async Task MissingRemoteOrSimulatorDestinationDoesNotInheritTheOnServerException(
            Ai.InferenceLocationEnum location)
        {
            var fixture = new AIWorkflowFixture();
            fixture.SetDestination(string.Empty, true, location);
            fixture.Hide(fixture.Deployment, Ai.BrowseNames.EndpointUri);

            await Assert.ThatAsync(() => fixture.PrepareAsync("invoke-request"),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OnServerWithoutUriStillRequiresPolicyForRemoteServerOrEgress(bool egress)
        {
            var fixture = new AIWorkflowFixture();
            fixture.SetDestination(string.Empty, egress, Ai.InferenceLocationEnum.OnServer);
            if (!egress)
            {
                fixture.Endpoint.EndpointUrl = "opc.tcp://explicit-policy.invalid:4840/AI";
            }

            await Assert.ThatAsync(() => fixture.PrepareAsync("invoke-request"),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [TestCase("state")]
        [TestCase("model")]
        [TestCase("egress")]
        [TestCase("destination")]
        [TestCase("limit")]
        [TestCase("fallback")]
        [TestCase("permissions")]
        [TestCase("capability")]
        [TestCase("identity")]
        [TestCase("disconnect")]
        public async Task FreshStateCapabilityPermissionAndSessionChecksRejectStaleRequestsAsync(string change)
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync("invoke-request").ConfigureAwait(false);
            switch (change)
            {
                case "state":
                    fixture.Set(fixture.Deployment, Ai.BrowseNames.State,
                        Variant.From((int)Ai.DeploymentStateEnum.Faulted));
                    break;
                case "model":
                    fixture.ModelReference(fixture.Candidate.NodeId);
                    break;
                case "egress":
                    fixture.Set(fixture.Deployment, Ai.BrowseNames.EgressPermitted, Variant.From(true));
                    break;
                case "destination":
                    fixture.Set(fixture.Deployment, Ai.BrowseNames.EndpointUri,
                        Variant.From("http://localhost:9090/v1"));
                    break;
                case "limit":
                    fixture.Set(fixture.Deployment, Ai.BrowseNames.MaxInlinePayloadSize, Variant.From(1u));
                    break;
                case "fallback":
                    fixture.FallbackReference(fixture.Server.Id("other-deployment"));
                    break;
                case "permissions":
                    fixture.Deny(fixture.Deployment, Ai.BrowseNames.Invoke);
                    break;
                case "capability":
                    fixture.Capabilities = [new Ai.CapabilityDataType { Name = "reachable", Supported = false }];
                    break;
                case "identity":
                    fixture.SessionId = new NodeId(202u);
                    break;
                case "disconnect":
                    fixture.Connected = false;
                    break;
                default:
                    throw new AssertionException("Unspecified freshness partition.");
            }

            await Assert.ThatAsync(() => fixture.ExecuteAsync("invoke-request", input),
                change is "permissions" or "capability"
                    ? Throws.TypeOf<ServiceResultException>() : Throws.InvalidOperationException)
                .ConfigureAwait(false);

            fixture.VerifyNoMutation();
            fixture.VerifyBorrowedSession();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BadAndUncertainEgressReadsNeverBecomeNoEgressAuthorizationAsync(bool uncertain)
        {
            var fixture = new AIWorkflowFixture();
            NodeId egress = fixture.Property(fixture.Deployment, Ai.BrowseNames.EgressPermitted);
            fixture.Server.SetValue(egress, Variant.From(false), status:
                uncertain ? StatusCodes.Uncertain : StatusCodes.BadUserAccessDenied);

            await Assert.ThatAsync(() => fixture.PrepareAsync("invoke-request"),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DuplicateOrOversizedCapabilitiesNeverAuthorizeARequestAsync(bool oversized)
        {
            var fixture = new AIWorkflowFixture(maxFields: 12)
            {
                Capabilities = oversized
                    ? Enumerable.Range(0, 13)
                        .Select(index => new Ai.CapabilityDataType { Name = "cap-" + index, Supported = true })
                        .ToArrayOf()
                    :
                    [
                        new() { Name = "chat", Supported = true },
                        new() { Name = "chat", Supported = false }
                    ]
            };

            await Assert.ThatAsync(() => fixture.PrepareAsync("invoke-request"),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            fixture.VerifyNoMutation();
            Assert.That(fixture.Server.Calls, Has.Count.EqualTo(1));
        }

        [TestCase("missing")]
        [TestCase("executable")]
        [TestCase("user")]
        [TestCase("malformed")]
        public async Task RequestRequiresTheActualMethodAndBothBooleanPermissionsAsync(string fault)
        {
            var fixture = new AIWorkflowFixture();
            if (fault == "missing")
            {
                fixture.Hide(fixture.Deployment, Ai.BrowseNames.Invoke);
            }
            else
            {
                NodeId method = fixture.Permission(fixture.Deployment, Ai.BrowseNames.Invoke);
                fixture.Server.SetValue(method, fault == "malformed" ? Variant.From(1u) : Variant.From(false),
                    fault == "executable" ? Attributes.Executable : Attributes.UserExecutable);
            }

            await Assert.ThatAsync(() => fixture.PrepareAsync("invoke-request"),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [Test]
        public async Task CapabilitiesChangingDeploymentStateAreRecheckedBeforeDispatchAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync("invoke-request").ConfigureAwait(false);
            fixture.AfterCapabilities = () => fixture.Set(
                fixture.Deployment, Ai.BrowseNames.State, Variant.From((int)Ai.DeploymentStateEnum.Faulted));

            await Assert.ThatAsync(() => fixture.ExecuteAsync("invoke-request", input),
                Throws.InvalidOperationException.With.Message.Contains("changed")).ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [TestCase("invoke-request", "Invoke")]
        [TestCase("submit-inference-job", "InvokeAsync")]
        [TestCase("submit-transfer-request", "BeginTransfer")]
        public async Task LostMethodRepliesAreUnknownAndAreNeverRetriedAsync(string operation, string method)
        {
            var fixture = new AIWorkflowFixture
            {
                FailingMethod = method,
                FailureStatus = StatusCodes.BadCommunicationError
            };
            CompanionTaskInput input = await fixture.PrepareAsync(operation).ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync(operation, input),
                Throws.InvalidOperationException.With.Message.Contains("unknown after dispatch"))
                .ConfigureAwait(false);

            Assert.That(fixture.Mutations, Is.EqualTo<string[]>([method]));
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task ServerCapacityFailureIsNotRetriedOrReportedAsAcceptedAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync("submit-inference-job").ConfigureAwait(false);
            fixture.FailingMethod = "InvokeAsync";
            fixture.FailureStatus = StatusCodes.BadTooManyOperations;

            await Assert.ThatAsync(() => fixture.ExecuteAsync("submit-inference-job", input),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.Code))
                    .EqualTo((uint)StatusCodes.BadTooManyOperations)).ConfigureAwait(false);

            Assert.That(fixture.Mutations, Is.EqualTo(sInvokeAsync));
        }

        [Test]
        public async Task AsyncSubmissionReturnsExactJobWithoutReadingOrInferringItsResultAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync("submit-inference-job").ConfigureAwait(false);
            fixture.Server.ReadHandler = (read, _) =>
            {
                if (read.NodeId == fixture.Property(fixture.Job, Ai.BrowseNames.ResponsePayload))
                {
                    throw new AssertionException("Submission must not read a job response.");
                }
                return fixture.Server.Read(read);
            };

            CompanionOperationResult result = await fixture.ExecuteAsync("submit-inference-job", input)
                .ConfigureAwait(false);

            CallMethodRequest request = fixture.Call("InvokeAsync");
            Assert.That(request.ObjectId, Is.EqualTo(fixture.Deployment.NodeId));
            Assert.That(request.InputArguments, Has.Count.EqualTo(4));
            Assert.That(request.InputArguments[0].TryGetValue(out ByteString bytes), Is.True);
            Assert.That(bytes, Is.EqualTo(fixture.Payload));
            Assert.That(request.InputArguments[1].TryGetValue(out string? uri), Is.True);
            Assert.That(uri, Is.Empty);
            Assert.That(Field(result.Values, "Resource").TryGetValue(out NodeId job), Is.True);
            Assert.That(job, Is.EqualTo(fixture.Job.NodeId));
            Assert.That(Field(result.Values, "Accepted").TryGetValue(out bool accepted), Is.True);
            Assert.That(accepted, Is.True);
            Assert.That(Field(result.Values, "Observed terminal").TryGetValue(out bool terminal), Is.True);
            Assert.That(terminal, Is.False);
            Assert.That(fixture.Mutations, Is.EqualTo(sInvokeAsync));
        }

        [Test]
        public async Task AsyncNullJobIdentifierIsAnUnknownOutcomeNotAReasonToResubmitAsync()
        {
            var fixture = new AIWorkflowFixture { ReturnedJob = NodeId.Null };
            CompanionTaskInput input = await fixture.PrepareAsync("submit-inference-job").ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync("submit-inference-job", input),
                Throws.InvalidOperationException.With.Message.Contains("unknown")).ConfigureAwait(false);

            Assert.That(fixture.Mutations, Is.EqualTo(sInvokeAsync));
        }

        [Test]
        public async Task ObservationUsesReadyRunningAndHaltedStatesRatherThanPayloadPresenceAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync(
                "observe-job", fixture.Job, [new("reads", Variant.From(8u))]).ConfigureAwait(false);
            fixture.ObserveStates(
            [
                ObjectIds.ProgramStateMachineType_Ready,
                ObjectIds.ProgramStateMachineType_Running,
                ObjectIds.ProgramStateMachineType_Halted
            ]);

            CompanionOperationResult result = await fixture.ExecuteAsync("observe-job", input, fixture.Job)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Observations").TryGetValue(out uint count), Is.True);
            Assert.That(count, Is.EqualTo(3u));
            Assert.That(Field(result.Values, "Observed terminal").TryGetValue(out bool terminal), Is.True);
            Assert.That(terminal, Is.True);
            Assert.That(Field(result.Values, "Response").TryGetValue(out ByteString response), Is.True);
            Assert.That(response, Is.EqualTo(fixture.Response));
            Assert.That(Field(result.Values, "Program state ID").TryGetValue(out NodeId state), Is.True);
            Assert.That(state, Is.EqualTo(ObjectIds.ProgramStateMachineType_Halted));
            fixture.VerifyNoMutation();
        }

        [TestCase(1u)]
        [TestCase(8u)]
        public async Task BoundedObservationStopsWithoutCancellingANonTerminalJobAsync(uint reads)
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync(
                "observe-job", fixture.Job, [new("reads", Variant.From(reads))]).ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("observe-job", input, fixture.Job)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Observations").TryGetValue(out uint observed), Is.True);
            Assert.That(observed, Is.EqualTo(reads));
            Assert.That(Field(result.Values, "Observed terminal").TryGetValue(out bool terminal), Is.True);
            Assert.That(terminal, Is.False);
            Assert.That(result.Values.Contains(value => value.Name == "Response"), Is.False);
            fixture.VerifyNoMutation();
        }

        [TestCase(0u)]
        [TestCase(9u)]
        public async Task InvalidObservationBoundsFailBeforeReadingResultsAsync(uint reads)
        {
            var fixture = new AIWorkflowFixture();

            await Assert.ThatAsync(() => fixture.PrepareAsync(
                "observe-job", fixture.Job, [new("reads", Variant.From(reads))]),
                Throws.ArgumentException).ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [TestCase("failure")]
        [TestCase("empty-success")]
        [TestCase("unknown")]
        public async Task TerminalLifecycleDoesNotFabricateAnInferenceOutcomeAsync(string outcome)
        {
            var fixture = new AIWorkflowFixture();
            fixture.SetProgramState(fixture.Job, ObjectIds.ProgramStateMachineType_Halted);
            if (outcome == "failure")
            {
                fixture.Set(fixture.Job, Ai.BrowseNames.LastError, Variant.From(new LocalizedText("backend-secret")));
            }
            else
            {
                fixture.Set(fixture.Job, Ai.BrowseNames.ResponsePayload, Variant.From(ByteString.Empty));
                if (outcome == "unknown")
                {
                    fixture.Set(fixture.Job, Ai.BrowseNames.ModelUsed, Variant.From(NodeId.Null));
                }
            }
            CompanionTaskInput input = await fixture.PrepareAsync(
                "observe-job", fixture.Job, [new("reads", Variant.From(1u))]).ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("observe-job", input, fixture.Job)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Observed terminal").TryGetValue(out bool terminal), Is.True);
            Assert.That(terminal, Is.True);
            Assert.That(result.Summary, Does.Contain(outcome switch
            {
                "failure" => "failure",
                "unknown" => "unknown",
                _ => "reported Stop"
            }));
            Assert.That(result.Summary, Does.Not.Contain("backend-secret"));
            if (outcome == "empty-success")
            {
                Assert.That(Field(result.Values, "Response").TryGetValue(out ByteString payload), Is.True);
                Assert.That(payload.IsEmpty, Is.True);
            }
            fixture.VerifyNoMutation();
        }

        [Test]
        public async Task HaltUsesTheStandardTypedMethodAndDoesNotClaimCancellationCompletionAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync("halt-job", fixture.Job, []).ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("halt-job", input, fixture.Job)
                .ConfigureAwait(false);

            CallMethodRequest call = fixture.Call("Halt");
            Assert.That(call.ObjectId, Is.EqualTo(fixture.Job.NodeId));
            Assert.That(call.MethodId, Is.EqualTo(MethodIds.ProgramStateMachineType_Halt));
            Assert.That(call.InputArguments.IsEmpty, Is.True);
            Assert.That(Field(result.Values, "Observed terminal").TryGetValue(out bool terminal), Is.True);
            Assert.That(terminal, Is.False);
            Assert.That(result.Summary, Does.Contain("not yet observed"));
            Assert.That(fixture.Mutations, Is.EqualTo(sHalt));
        }

        [TestCase("completed")]
        [TestCase("replaced")]
        [TestCase("disconnected")]
        [TestCase("permission")]
        public async Task HaltRejectsCompletedReplacedDisconnectedOrUnauthorizedJobsAsync(string fault)
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync("halt-job", fixture.Job, []).ConfigureAwait(false);
            switch (fault)
            {
                case "completed":
                    fixture.SetProgramState(fixture.Job, ObjectIds.ProgramStateMachineType_Halted);
                    break;
                case "replaced":
                    fixture.Set(fixture.Job, Ai.BrowseNames.JobId, Variant.From("replacement-job"));
                    break;
                case "disconnected":
                    fixture.Connected = false;
                    break;
                case "permission":
                    fixture.Deny(fixture.Job, BrowseNames.Halt, Namespaces.OpcUa);
                    break;
                default:
                    throw new AssertionException("Unspecified halt partition.");
            }

            await Assert.ThatAsync(() => fixture.ExecuteAsync("halt-job", input, fixture.Job),
                fault is "completed" or "permission"
                    ? Throws.TypeOf<ServiceResultException>() : Throws.InvalidOperationException)
                .ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [Test]
        public async Task InFlightCancellationIsUnknownAndNeverRetriesOrCancelsAnUnownedJobAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync("submit-inference-job").ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<CallResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            NodeId method = fixture.Server.Resolve(Ai.MethodIds.DeploymentType_InvokeAsync);
            fixture.Server.Session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.Is<ArrayOf<CallMethodRequest>>(requests =>
                        requests.Count == 1 && requests[0].MethodId == method),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<CallMethodRequest> _, CancellationToken token) =>
                {
                    dispatched.SetResult();
                    return new ValueTask<CallResponse>(release.Task.WaitAsync(token));
                });
            Task<CompanionOperationResult> execution = fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Deployment, "submit-inference-job", input, null, cancellation.Token).AsTask();
            await dispatched.Task.ConfigureAwait(false);

            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(() => execution,
                Throws.InvalidOperationException.With.Message.Contains("unknown after dispatch"))
                .ConfigureAwait(false);
            fixture.Server.Session.Verify(value => value.CallAsync(It.IsAny<RequestHeader?>(),
                It.Is<ArrayOf<CallMethodRequest>>(requests => requests.Count == 1 && requests[0].MethodId == method),
                It.IsAny<CancellationToken>()), Times.Once);
            fixture.VerifyNoMutation();
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task CancellationBeforeDispatchDoesNotAllocateOrStartAnythingAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync("submit-inference-job").ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Deployment, "submit-inference-job", input, null, cancellation.Token).AsTask(),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [Test]
        public async Task TransferRequiredIsReturnedWithoutOpeningOrExecutingTheTransferAsync()
        {
            var fixture = new AIWorkflowFixture { TransferRequired = true };
            CompanionTaskInput input = await fixture.PrepareAsync("invoke-request").ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("invoke-request", input)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Transfer required").TryGetValue(out bool required), Is.True);
            Assert.That(required, Is.True);
            Assert.That(Field(result.Values, "Transfer").TryGetValue(out NodeId transfer), Is.True);
            Assert.That(transfer, Is.EqualTo(fixture.Transfer.NodeId));
            Assert.That(fixture.Mutations, Is.EqualTo(sInvoke));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(4096)]
        [TestCase(4097)]
        [TestCase(1048576)]
        public async Task ExplicitTransferReadVerifiesExactBytesAndClosesOnlyItsOwnHandleAsync(int size)
        {
            var fixture = new AIWorkflowFixture();
            fixture.SetResponse(ByteString.From(Enumerable.Repeat((byte)0xA5, size).ToArray()));
            ulong cap = (ulong)Math.Max(1, size);
            CompanionTaskInput input = await fixture.PrepareAsync(
                "read-response", fixture.Transfer, fixture.TransferInputs(cap)).ConfigureAwait(false);
            fixture.VerifyNoMutation();

            CompanionOperationResult result = await fixture.ExecuteAsync("read-response", input, fixture.Transfer)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Response").TryGetValue(out ByteString payload), Is.True);
            Assert.That(payload, Is.EqualTo(fixture.Response));
            Assert.That(Field(result.Values, "SHA-256 verified").TryGetValue(out bool verified), Is.True);
            Assert.That(verified, Is.True);
            Assert.That(Field(result.Values, "Response bytes").TryGetValue(out int bytes), Is.True);
            Assert.That(bytes, Is.EqualTo(size));
            Assert.That(fixture.Mutations[0], Is.EqualTo("Open"));
            Assert.That(fixture.Mutations[^1], Is.EqualTo("Close"));
            Assert.That(fixture.Mutations.ToList().Count(name => name == "Open"), Is.EqualTo(1));
            Assert.That(fixture.Mutations.ToList().Count(name => name == "Close"), Is.EqualTo(1));
            Assert.That(fixture.Mutations.ToList().All(name => name is "Open" or "Read" or "Close"), Is.True);
            fixture.VerifyBorrowedSession();
        }

        [TestCase(0ul, 32)]
        [TestCase(1048577ul, 32)]
        [TestCase(1ul, 0)]
        [TestCase(1ul, 31)]
        [TestCase(1ul, 33)]
        public async Task TransferRequiresAFiniteCapAndAnExactSha256BeforeOpeningAsync(ulong cap, int digestSize)
        {
            var fixture = new AIWorkflowFixture();

            await Assert.ThatAsync(() => fixture.PrepareAsync("read-response", fixture.Transfer,
                [
                    new("maximumBytes", Variant.From(cap)),
                    new("sha256", Variant.From(ByteString.From(new byte[digestSize])))
                ]), Throws.ArgumentException).ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [Test]
        public async Task AdvertisedOversizeResponseFailsBeforeOpeningAsync()
        {
            var fixture = new AIWorkflowFixture();
            fixture.SetResponse(ByteString.From([1, 2]));

            await Assert.ThatAsync(() => fixture.PrepareAsync(
                "read-response", fixture.Transfer, fixture.TransferInputs(1)),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [Test]
        public async Task DishonestFileSizeCannotBypassStreamingByteCapAndHandleStillClosesAsync()
        {
            var fixture = new AIWorkflowFixture();
            fixture.SetResponse(ByteString.From([1, 2]));
            fixture.Set(fixture.ResponseFile, BrowseNames.Size, Variant.From(1ul));
            CompanionTaskInput input = await fixture.PrepareAsync(
                "read-response", fixture.Transfer, fixture.TransferInputs(1)).ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync("read-response", input, fixture.Transfer),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            Assert.That(fixture.Mutations, Is.EqualTo(sReadOnce));
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task DigestMismatchReturnsNoResponseAndNeverAbortsABorrowedTransferAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync("read-response", fixture.Transfer,
                [
                    new("maximumBytes", Variant.From(1024ul)),
                    new("sha256", Variant.From(ByteString.From(new byte[32])))
                ]).ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync("read-response", input, fixture.Transfer),
                Throws.TypeOf<ServiceResultException>().With.Message.Contains("SHA-256")).ConfigureAwait(false);

            Assert.That(fixture.Mutations, Is.EqualTo(sReadTwice));
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task ResponseCloseFailureCannotReturnVerifiedBytesAsSuccessAsync()
        {
            var fixture = new AIWorkflowFixture { FailingMethod = "Close" };
            CompanionTaskInput input = await fixture.PrepareAsync(
                "read-response", fixture.Transfer, fixture.TransferInputs(1024)).ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync("read-response", input, fixture.Transfer),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            Assert.That(fixture.Mutations, Is.EqualTo(sReadTwice));
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task ShortFileReadsContinueToEofInsteadOfTruncatingTheResponseAsync()
        {
            var fixture = new AIWorkflowFixture { ResponseChunkLimit = 1 };
            CompanionTaskInput input = await fixture.PrepareAsync(
                "read-response", fixture.Transfer, fixture.TransferInputs(1024)).ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("read-response", input, fixture.Transfer)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Response").TryGetValue(out ByteString payload), Is.True);
            Assert.That(payload, Is.EqualTo(fixture.Response));
            Assert.That(fixture.Mutations.ToList().Count(method => method == "Read"),
                Is.EqualTo(fixture.Response.Length + 1));
            Assert.That(fixture.Mutations[^1], Is.EqualTo("Close"));
        }

        [Test]
        public async Task TinyChunksCannotTurnABoundedTransferIntoUnboundedPollingAsync()
        {
            var fixture = new AIWorkflowFixture { ResponseChunkLimit = 1 };
            fixture.SetResponse(ByteString.From(new byte[512]));
            CompanionTaskInput input = await fixture.PrepareAsync(
                "read-response", fixture.Transfer, fixture.TransferInputs(512)).ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync("read-response", input, fixture.Transfer),
                Throws.TypeOf<ServiceResultException>().With.Message.Contains("budget")).ConfigureAwait(false);

            Assert.That(fixture.Mutations.ToList().Count(method => method == "Read"), Is.EqualTo(258));
            Assert.That(fixture.Mutations[^1], Is.EqualTo("Close"));
            Assert.That(fixture.Mutations.Contains(method => method == "Abort"), Is.False);
        }

        [Test]
        public async Task CancellingAResponseReadStillClosesOnlyItsOwnedHandleAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync(
                "read-response", fixture.Transfer, fixture.TransferInputs(1024)).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reply = new TaskCompletionSource<CallResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Server.Session.Setup(value => value.CallAsync(It.IsAny<RequestHeader?>(),
                    It.Is<ArrayOf<CallMethodRequest>>(requests => requests.Count == 1 &&
                        requests[0].ObjectId == fixture.ResponseFile.NodeId &&
                        requests[0].MethodId == MethodIds.FileType_Read),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<CallMethodRequest> _, CancellationToken token) =>
                {
                    reading.SetResult();
                    return new ValueTask<CallResponse>(reply.Task.WaitAsync(token));
                });
            Task<CompanionOperationResult> execution = fixture.Provider.ExecutePreparedAsync(
                fixture.Context, fixture.Transfer, "read-response", input, null, cancellation.Token).AsTask();
            await reading.Task.ConfigureAwait(false);

            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(() => execution, Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            Assert.That(fixture.Mutations, Is.EqualTo(sOpenClose));
            fixture.VerifyBorrowedSession();
        }

        [TestCase(Ai.TransferStateEnum.Building)]
        [TestCase(Ai.TransferStateEnum.Ready)]
        [TestCase(Ai.TransferStateEnum.Executing)]
        [TestCase(Ai.TransferStateEnum.Failed)]
        [TestCase(Ai.TransferStateEnum.Expired)]
        public async Task NonCompletedTransferCannotBeDownloadedAsync(Ai.TransferStateEnum state)
        {
            var fixture = new AIWorkflowFixture();
            fixture.Set(fixture.Transfer, Ai.BrowseNames.State, Variant.From((int)state));

            await Assert.ThatAsync(() => fixture.PrepareAsync(
                "read-response", fixture.Transfer, fixture.TransferInputs(1024)),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [TestCase("identity")]
        [TestCase("state")]
        [TestCase("model")]
        [TestCase("size")]
        [TestCase("file")]
        [TestCase("expired")]
        [TestCase("security")]
        public async Task TransferFreshnessAndSecurityAreRecheckedBeforeOpeningAsync(string change)
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync(
                "read-response", fixture.Transfer, fixture.TransferInputs(1024)).ConfigureAwait(false);
            switch (change)
            {
                case "identity":
                    fixture.Set(fixture.Transfer, Ai.BrowseNames.TransferId, Variant.From("replacement-transfer"));
                    break;
                case "state":
                    fixture.Set(fixture.Transfer, Ai.BrowseNames.State,
                        Variant.From((int)Ai.TransferStateEnum.Executing));
                    break;
                case "model":
                    fixture.Set(fixture.Transfer, Ai.BrowseNames.ModelUsed, Variant.From(fixture.Candidate.NodeId));
                    break;
                case "size":
                    fixture.Set(fixture.ResponseFile, BrowseNames.Size, Variant.From(0ul));
                    break;
                case "file":
                    fixture.ReplaceResponseFile();
                    break;
                case "expired":
                    fixture.Set(fixture.Transfer, Ai.BrowseNames.ExpiresAt,
                        Variant.From(new DateTimeUtc(2000, 1, 1)));
                    break;
                case "security":
                    fixture.Endpoint.SecurityMode = MessageSecurityMode.None;
                    break;
                default:
                    throw new AssertionException("Unspecified transfer freshness partition.");
            }

            await Assert.ThatAsync(() => fixture.ExecuteAsync("read-response", input, fixture.Transfer),
                Throws.InvalidOperationException)
                .ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [Test]
        public async Task LargeRequestUsesTypedTransferAndNeverDownloadsAnImplicitResponseAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync("submit-transfer-request").ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("submit-transfer-request", input)
                .ConfigureAwait(false);

            Assert.That(fixture.Call("BeginTransfer").InputArguments[0].TryGetValue(out string? contentType), Is.True);
            Assert.That(contentType, Is.EqualTo("application/json"));
            Assert.That(fixture.Call("BeginTransfer").InputArguments[1].TryGetValue(out ulong size), Is.True);
            Assert.That(size, Is.EqualTo((ulong)fixture.Payload.Length));
            Assert.That(fixture.Uploaded.ToArray(), Is.EqualTo(fixture.Payload.Span.ToArray()));
            Assert.That(fixture.Mutations, Is.EqualTo(sExecuteTransfer));
            Assert.That(Field(result.Values, "Resource").TryGetValue(out NodeId transfer), Is.True);
            Assert.That(transfer, Is.EqualTo(fixture.Transfer.NodeId));
            Assert.That(Field(result.Values, "Observed terminal").TryGetValue(out bool terminal), Is.True);
            Assert.That(terminal, Is.False);
            fixture.VerifyBorrowedSession();
        }

        [TestCase(4096)]
        [TestCase(4097)]
        public async Task TransferUploadPreservesTheExactChunkBoundaryAndTail(int payloadBytes)
        {
            var fixture = new AIWorkflowFixture();
            const string prefix = "{\"messages\":[{\"role\":\"user\",\"content\":\"";
            const string suffix = "\"}]}";
            fixture.Payload = ByteString.From(System.Text.Encoding.UTF8.GetBytes(
                prefix + new string('x', payloadBytes - prefix.Length - suffix.Length) + suffix));
            Assert.That(fixture.Payload.Length, Is.EqualTo(payloadBytes));
            CompanionTaskInput input = await fixture.PrepareAsync("submit-transfer-request").ConfigureAwait(false);

            await fixture.ExecuteAsync("submit-transfer-request", input).ConfigureAwait(false);

            Assert.That(fixture.Uploaded.ToArray(), Is.EqualTo(fixture.Payload.ToArray()));
            var lengths = new List<int>();
            foreach (CallMethodRequest call in fixture.Server.Calls)
            {
                if (call.ObjectId == fixture.RequestFile.NodeId && call.InputArguments.Count == 2)
                {
                    Assert.That(call.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                    lengths.Add(bytes.Length);
                }
            }
            int[] expectedLengths = payloadBytes == 4096 ? [4096] : [4096, 1];
            string[] expectedCalls = payloadBytes == 4096
                ? ["BeginTransfer", "Open", "Write", "Close", "Execute"]
                : ["BeginTransfer", "Open", "Write", "Write", "Close", "Execute"];
            Assert.That(lengths, Is.EqualTo(expectedLengths));
            Assert.That(fixture.Mutations.ToArray(), Is.EqualTo(expectedCalls));
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task FailedUploadClosesItsHandleAndAbortsOnlyTheTransferItCreatedAsync()
        {
            var fixture = new AIWorkflowFixture { FailingMethod = "Write" };
            CompanionTaskInput input = await fixture.PrepareAsync("submit-transfer-request").ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync("submit-transfer-request", input),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            Assert.That(fixture.Mutations,
                Is.EqualTo(sAbortOwnedTransfer));
            Assert.That(fixture.Call("Abort").ObjectId, Is.EqualTo(fixture.Transfer.NodeId));
            Assert.That(fixture.Uploaded, Is.Empty);
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task FailedOwnedCleanupIsSurfacedAlongsideTheOriginalFailureAsync()
        {
            var fixture = new AIWorkflowFixture { FailingMethod = "Write", FailAbort = true };
            CompanionTaskInput input = await fixture.PrepareAsync("submit-transfer-request").ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync("submit-transfer-request", input),
                Throws.TypeOf<AggregateException>().With.Message.Contains("cleanup")).ConfigureAwait(false);

            Assert.That(fixture.Mutations,
                Is.EqualTo(sAbortOwnedTransfer));
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task RefusedTransferDoesNotUploadExecuteOrClaimAcceptanceAsync()
        {
            var fixture = new AIWorkflowFixture { BeginAccepted = false };
            CompanionTaskInput input = await fixture.PrepareAsync("submit-transfer-request").ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("submit-transfer-request", input)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Accepted").TryGetValue(out bool accepted), Is.True);
            Assert.That(accepted, Is.False);
            Assert.That(fixture.Mutations, Is.EqualTo(sBeginTransfer));
        }

        [Test]
        public async Task LostTransferExecuteReplyDoesNotAbortOrResubmitPotentiallyRunningInferenceAsync()
        {
            var fixture = new AIWorkflowFixture
            {
                FailingMethod = "Execute",
                FailureStatus = StatusCodes.BadCommunicationError
            };
            CompanionTaskInput input = await fixture.PrepareAsync("submit-transfer-request").ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync("submit-transfer-request", input),
                Throws.InvalidOperationException.With.Message.Contains("unknown after dispatch"))
                .ConfigureAwait(false);

            Assert.That(fixture.Mutations,
                Is.EqualTo(sExecuteTransfer));
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task ExplicitAbortUsesOnlyTheSelectedTransferAndReportsUnobservedCancellationAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync(
                "abort-transfer", fixture.Transfer, []).ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("abort-transfer", input, fixture.Transfer)
                .ConfigureAwait(false);

            Assert.That(fixture.Call("Abort").ObjectId, Is.EqualTo(fixture.Transfer.NodeId));
            Assert.That(fixture.Mutations, Is.EqualTo(sAbort));
            Assert.That(Field(result.Values, "Observed terminal").TryGetValue(out bool observed), Is.True);
            Assert.That(observed, Is.False);
        }

        [TestCase("start-collection", Ai.LearningJobStateEnum.Idle, "StartCollection")]
        [TestCase("stop-collection", Ai.LearningJobStateEnum.Collecting, "StopCollection")]
        [TestCase("trigger-training", Ai.LearningJobStateEnum.Labelling, "TriggerTraining")]
        [TestCase("promote-model", Ai.LearningJobStateEnum.Ready, "PromoteModel")]
        public async Task LearningUsesExistingSelectedModelDatasetAndExactTypedMethodAsync(
            string operation, Ai.LearningJobStateEnum phase, string method)
        {
            var policy = new Mock<IAITaskEgressPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<AICompanionTaskInput>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            var fixture = new AIWorkflowFixture(policy.Object);
            fixture.Set(fixture.Learning, Ai.BrowseNames.State, Variant.From((int)phase));
            CompanionTaskInput input = await fixture.PrepareAsync(
                operation, fixture.Learning, fixture.LearningInputs(operation)).ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync(operation, input, fixture.Learning)
                .ConfigureAwait(false);

            CallMethodRequest call = fixture.Call(method);
            Assert.That(call.ObjectId, Is.EqualTo(fixture.Learning.NodeId));
            Assert.That(call.InputArguments.Count, Is.EqualTo(operation == "promote-model" ? 1 : 0));
            if (operation == "promote-model")
            {
                Assert.That(call.InputArguments[0].TryGetValue(out NodeId deployment), Is.True);
                Assert.That(deployment, Is.EqualTo(fixture.Deployment.NodeId));
                Assert.That(deployment.IsNull, Is.False);
            }
            Assert.That(Field(result.Values, "Observed terminal").TryGetValue(out bool terminal), Is.True);
            Assert.That(terminal, Is.False);
            Assert.That(result.Summary, Does.Contain("not a claim"));
            Assert.That(fixture.Mutations, Is.EqualTo<string[]>([method]));
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task SampleAccountingDoesNotAdvertiseTrainingWithoutAnExecutableServerMethodAsync()
        {
            var fixture = new AIWorkflowFixture();
            fixture.Set(fixture.Learning, Ai.BrowseNames.State, Variant.From((int)Ai.LearningJobStateEnum.Labelling));
            fixture.Hide(fixture.Learning, Ai.BrowseNames.TriggerTraining);

            CompanionInspection inspection = await fixture.Provider.InspectAsync(
                fixture.Context, fixture.Learning, CancellationToken.None).ConfigureAwait(false);

            Assert.That(inspection.Operations.Contains(operation => operation.Id == "trigger-training"), Is.False);
            Assert.That(inspection.Summary, Does.Contain("only accounts for samples"));
            fixture.VerifyNoMutation();
        }

        [Test]
        public async Task LearningWithoutPublishedEgressContractRequiresExactHostAuthorizationAsync()
        {
            var fixture = new AIWorkflowFixture();
            fixture.Set(fixture.Learning, Ai.BrowseNames.State, Variant.From((int)Ai.LearningJobStateEnum.Labelling));

            await Assert.ThatAsync(() => fixture.PrepareAsync(
                "trigger-training", fixture.Learning, fixture.LearningInputs("trigger-training")),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [TestCase("phase")]
        [TestCase("dataset")]
        [TestCase("candidate")]
        [TestCase("permission")]
        public async Task LearningRechecksPhaseSelectionsAndPermissionBeforeMutationAsync(string change)
        {
            var policy = new Mock<IAITaskEgressPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<AICompanionTaskInput>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            var fixture = new AIWorkflowFixture(policy.Object);
            fixture.Set(fixture.Learning, Ai.BrowseNames.State, Variant.From((int)Ai.LearningJobStateEnum.Labelling));
            CompanionTaskInput input = await fixture.PrepareAsync(
                "trigger-training", fixture.Learning, fixture.LearningInputs("trigger-training"))
                .ConfigureAwait(false);
            switch (change)
            {
                case "phase":
                    fixture.Set(fixture.Learning, Ai.BrowseNames.State,
                        Variant.From((int)Ai.LearningJobStateEnum.Training));
                    break;
                case "dataset":
                    fixture.Set(fixture.Learning, Ai.BrowseNames.Dataset, Variant.From(NodeId.Null));
                    break;
                case "candidate":
                    fixture.Set(fixture.Learning, Ai.BrowseNames.CandidateModel, Variant.From(fixture.Model.NodeId));
                    break;
                case "permission":
                    fixture.Deny(fixture.Learning, Ai.BrowseNames.TriggerTraining);
                    break;
                default:
                    throw new AssertionException("Unspecified learning freshness partition.");
            }

            await Assert.ThatAsync(() => fixture.ExecuteAsync("trigger-training", input, fixture.Learning),
                change == "permission" ? Throws.TypeOf<ServiceResultException>() : Throws.InvalidOperationException)
                .ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [Test]
        public async Task TrainingRefusalRemainsDistinctFromMethodSuccessAndTrainingCompletionAsync()
        {
            var policy = new Mock<IAITaskEgressPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<AICompanionTaskInput>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            var fixture = new AIWorkflowFixture(policy.Object) { TrainingAccepted = false };
            fixture.Set(fixture.Learning, Ai.BrowseNames.State, Variant.From((int)Ai.LearningJobStateEnum.Labelling));
            CompanionTaskInput input = await fixture.PrepareAsync(
                "trigger-training", fixture.Learning, fixture.LearningInputs("trigger-training"))
                .ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("trigger-training", input, fixture.Learning)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Method returned").TryGetValue(out bool returned), Is.True);
            Assert.That(returned, Is.True);
            Assert.That(Field(result.Values, "Accepted").TryGetValue(out bool accepted), Is.True);
            Assert.That(accepted, Is.False);
            Assert.That(Field(result.Values, "Observed terminal").TryGetValue(out bool terminal), Is.True);
            Assert.That(terminal, Is.False);
            Assert.That(fixture.Mutations, Is.EqualTo(sTriggerTraining));
        }

        [TestCase("model")]
        [TestCase("dataset")]
        [TestCase("all-deployments")]
        public async Task LearningCannotSilentlyReassignSelectionsOrPromoteToAllDeploymentsAsync(string fault)
        {
            var fixture = new AIWorkflowFixture();
            fixture.Set(fixture.Learning, Ai.BrowseNames.State, Variant.From((int)Ai.LearningJobStateEnum.Ready));
            var values = fixture.LearningInputs("promote-model").ToList();
            int index = fault == "model" ? 0 : fault == "dataset" ? 1 : 2;
            values[index] = values[index] with
            {
                Value = Variant.From(fault == "all-deployments" ? NodeId.Null : fixture.Candidate.NodeId)
            };

            await Assert.ThatAsync(() => fixture.PrepareAsync("promote-model", fixture.Learning, values.ToArrayOf()),
                fault == "all-deployments"
                    ? Throws.TypeOf<ServiceResultException>() : Throws.InvalidOperationException)
                .ConfigureAwait(false);

            fixture.VerifyNoMutation();
        }

        [Test]
        public async Task EvaluationReturnsExactExistingMetricsWithoutStartingOrFetchingAnythingAsync()
        {
            var fixture = new AIWorkflowFixture();
            CompanionTaskInput input = await fixture.PrepareAsync("read-evaluation", fixture.Evaluation, [])
                .ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync("read-evaluation", input, fixture.Evaluation)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Metrics").TryGetValue(
                out ArrayOf<Ai.EvaluationMetricDataType> metrics, fixture.Server.MessageContext), Is.True);
            Assert.That(metrics, Has.Count.EqualTo(1));
            Assert.That(metrics[0].Name, Is.EqualTo("accuracy"));
            Assert.That(metrics[0].Value, Is.EqualTo(0.95));
            Assert.That(metrics[0].Threshold, Is.EqualTo(0.9));
            Assert.That(metrics[0].Comparison, Is.EqualTo(">="));
            Assert.That(metrics[0].Passed, Is.True);
            Assert.That(result.Summary, Does.Contain("no start-evaluation method"));
            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task ContradictoryEvaluationMetricsAreNotReportedAsPassingAsync()
        {
            var fixture = new AIWorkflowFixture();
            fixture.Set(fixture.Evaluation, Ai.BrowseNames.Metrics,
                Variant.FromStructure<Ai.EvaluationMetricDataType>(
                [
                    new()
                    {
                        Name = "accuracy", Value = 0.5, Threshold = 0.9, Comparison = ">=", Passed = false
                    }
                ]));

            await Assert.ThatAsync(() => fixture.PrepareAsync("read-evaluation", fixture.Evaluation, []),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task MissingEvaluationAndInferenceCancelContractsRemainUnsupportedAsync()
        {
            var fixture = new AIWorkflowFixture();

            await Assert.ThatAsync(() => fixture.PrepareAsync("start-evaluation", fixture.Evaluation, []),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            await Assert.ThatAsync(() => fixture.PrepareAsync("cancel", fixture.Job, []),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

            Assert.That(fixture.Server.Calls.IsEmpty, Is.True);
        }

        [Test]
        public void ResponseBufferIsFiniteAndDisposalNeverLeaksFurtherAccess()
        {
            using var buffer = new AIResponseTaskBuffer(2);
            buffer.Write([1, 2]);
            Assert.That(buffer.Length, Is.EqualTo(2));
            Assert.That(() => buffer.Write([3]), Throws.TypeOf<ServiceResultException>());
            Assert.That(buffer.Length, Is.EqualTo(2));
            Assert.That(buffer.VerifyAndCopy(ByteString.From(SHA256.HashData([1, 2]))),
                Is.EqualTo(ByteString.From([1, 2])));
            buffer.Dispose();
            Assert.That(buffer.CanWrite, Is.False);
            Assert.That(() => buffer.Write([1]), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => buffer.VerifyAndCopy(ByteString.Empty), Throws.TypeOf<ObjectDisposedException>());
        }

        private static readonly string[] sInvoke = ["Invoke"];
        private static readonly string[] sInvokeAsync = ["InvokeAsync"];
        private static readonly string[] sHalt = ["Halt"];
        private static readonly string[] sReadOnce = ["Open", "Read", "Close"];
        private static readonly string[] sReadTwice = ["Open", "Read", "Read", "Close"];
        private static readonly string[] sOpenClose = ["Open", "Close"];
        private static readonly string[] sExecuteTransfer = ["BeginTransfer", "Open", "Write", "Close", "Execute"];
        private static readonly string[] sAbortOwnedTransfer = ["BeginTransfer", "Open", "Write", "Close", "Abort"];
        private static readonly string[] sBeginTransfer = ["BeginTransfer"];
        private static readonly string[] sAbort = ["Abort"];
        private static readonly string[] sTriggerTraining = ["TriggerTraining"];
    }

    /// <summary>
    /// Typed clients dispatch to finite in-memory service responses. No endpoint is opened.
    /// </summary>
    internal sealed class AIWorkflowFixture
    {
        public AIWorkflowFixture(IAITaskEgressPolicy? policy = null, int maxFields = 256)
        {
            Server.NamespaceUris.GetIndexOrAppend(Ai.Namespaces.AI);
            Server.Session.SetupGet(value => value.Connected).Returns(() => Connected);
            Server.Session.SetupGet(value => value.SessionId).Returns(() => SessionId);
            Server.Session.SetupGet(value => value.Endpoint).Returns(Endpoint);
            Server.Session.SetupGet(value => value.Factory).Returns(Server.MessageContext.Factory);
            Server.Session.SetupGet(value => value.TypeTree).Returns(new TypeTable(Server.NamespaceUris));
            Server.Session.SetupGet(value => value.OperationLimits).Returns(new OperationLimits());
            Server.Session.SetupGet(value => value.ServerCapabilities).Returns(new ServerCapabilities());
            Server.Session.SetupGet(value => value.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            cache.Setup(value => value.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId child, NodeId parent, CancellationToken _) => ValueTask.FromResult(child == parent));
            Server.Session.SetupGet(value => value.NodeCache).Returns(cache.Object);
            Server.Session.Setup(value => value.BrowseAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> requests,
                    CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = requests.ConvertAll(Server.Browse)
                    });
                });
            Context = Server.Context(maxFields: maxFields);
            var clock = new Mock<TimeProvider>(MockBehavior.Strict);
            clock.Setup(value => value.GetUtcNow()).Returns(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero));
            Provider = new AICompanionProvider(policy, clock.Object);
            Model = Target("model", Ai.ObjectTypeIds.ModelType, "Model");
            Candidate = Target("candidate", Ai.ObjectTypeIds.ModelType, "Model");
            Dataset = Target("dataset", Ai.ObjectTypeIds.DatasetType, "Dataset");
            Deployment = Target("deployment", Ai.ObjectTypeIds.DeploymentType, "Deployment");
            Job = Target("inference", Ai.ObjectTypeIds.InferenceJobType, "Inference job");
            Learning = Target("learning", Ai.ObjectTypeIds.LearningJobType, "Learning job");
            Transfer = Target("transfer", Ai.ObjectTypeIds.InferenceTransferType, "Transfer");
            Evaluation = Target("evaluation", Ai.ObjectTypeIds.EvaluationRunType, "Evaluation");
            RequestFile = Target("request-file", ObjectTypeIds.FileType, "File");
            ResponseFile = Target("response-file", ObjectTypeIds.FileType, "File");
            ReturnedJob = Job.NodeId;
            ReturnedModel = Model.NodeId;
            Set(Deployment, Ai.BrowseNames.DeploymentId, Variant.From("deployment-1"));
            SetDestination("http://localhost:8080/v1", false, Ai.InferenceLocationEnum.OnServer);
            Set(Deployment, Ai.BrowseNames.State, Variant.From((int)Ai.DeploymentStateEnum.Ready));
            Set(Deployment, Ai.BrowseNames.DataJurisdiction, Variant.From("local-fixture"));
            Set(Deployment, Ai.BrowseNames.MaxInlinePayloadSize, Variant.From(16384u));
            ModelReference(Model.NodeId);
            Set(Model, Ai.BrowseNames.ModelId, Variant.From("model-1"));
            Set(Model, Ai.BrowseNames.Name, Variant.From("Fixture model"));
            Set(Candidate, Ai.BrowseNames.ModelId, Variant.From("model-2"));
            Set(Dataset, Ai.BrowseNames.DatasetId, Variant.From("dataset-1"));
            Set(Dataset, Ai.BrowseNames.Name, Variant.From("Fixture dataset"));
            Set(Dataset, Ai.BrowseNames.SourceKind, Variant.From((int)Ai.DatasetSourceEnum.Synthetic));
            Set(Dataset, Ai.BrowseNames.ContentType, Variant.From("application/json"));
            Set(Dataset, Ai.BrowseNames.SizeBytes, Variant.From(0ul));
            Set(Dataset, Ai.BrowseNames.SampleCount, Variant.From(0u));
            Set(Job, Ai.BrowseNames.JobId, Variant.From("job-1"));
            Set(Job, Ai.BrowseNames.Deployment, Variant.From(Deployment.NodeId));
            Set(Job, Ai.BrowseNames.ResponsePayload, Variant.From(Response));
            Set(Job, Ai.BrowseNames.ResponseContentType, Variant.From("application/json"));
            Set(Job, Ai.BrowseNames.ModelUsed, Variant.From(Model.NodeId));
            Set(Job, Ai.BrowseNames.FinishReason, Variant.From((int)Ai.FinishReasonEnum.Stop));
            Set(Job, Ai.BrowseNames.LastError, Variant.From(LocalizedText.Null));
            SetProgramState(Job, ObjectIds.ProgramStateMachineType_Running);
            Set(Learning, Ai.BrowseNames.JobId, Variant.From("learning-1"));
            Set(Learning, Ai.BrowseNames.State, Variant.From((int)Ai.LearningJobStateEnum.Collecting));
            Set(Learning, Ai.BrowseNames.BaseModel, Variant.From(Model.NodeId));
            Set(Learning, Ai.BrowseNames.Dataset, Variant.From(Dataset.NodeId));
            Set(Learning, Ai.BrowseNames.CandidateModel, Variant.From(Candidate.NodeId));
            Set(Learning, Ai.BrowseNames.TargetDeployment, Variant.From(Deployment.NodeId));
            SetProgramState(Learning, ObjectIds.ProgramStateMachineType_Running);
            Set(Transfer, Ai.BrowseNames.TransferId, Variant.From("transfer-1"));
            Set(Transfer, Ai.BrowseNames.State, Variant.From((int)Ai.TransferStateEnum.Completed));
            Set(Transfer, Ai.BrowseNames.ResponseContentType, Variant.From("application/json"));
            Set(Transfer, Ai.BrowseNames.ModelUsed, Variant.From(Model.NodeId));
            Set(Transfer, Ai.BrowseNames.ExpiresAt, Variant.From(new DateTimeUtc(2099, 1, 1)));
            Server.AddChild(Transfer.NodeId, RequestFile.NodeId, Ai.Namespaces.AI, Ai.BrowseNames.Request);
            Server.AddChild(Transfer.NodeId, ResponseFile.NodeId, Ai.Namespaces.AI, Ai.BrowseNames.Response);
            SetResponse(Response);
            Set(Evaluation, Ai.BrowseNames.RunId, Variant.From("evaluation-1"));
            Set(Evaluation, Ai.BrowseNames.EvaluatedModel, Variant.From(Model.NodeId));
            Set(Evaluation, Ai.BrowseNames.Passed, Variant.From(true));
            Set(Evaluation, Ai.BrowseNames.Metrics, Variant.FromStructure<Ai.EvaluationMetricDataType>(
            [
                new()
                {
                    Name = "accuracy", Value = 0.95, Threshold = 0.9, Comparison = ">=", Passed = true
                }
            ]));
            AddMethod(Deployment, "GetCapabilities", Ai.MethodIds.DeploymentType_GetCapabilities);
            AddMethod(Deployment, "Invoke", Ai.MethodIds.DeploymentType_Invoke);
            AddMethod(Deployment, "InvokeAsync", Ai.MethodIds.DeploymentType_InvokeAsync);
            AddMethod(Deployment, "BeginTransfer", Ai.MethodIds.DeploymentType_BeginTransfer);
            AddMethod(Transfer, "Execute", Ai.MethodIds.InferenceTransferType_Execute);
            AddMethod(Transfer, "Abort", Ai.MethodIds.InferenceTransferType_Abort);
            AddMethod(Job, "Halt", MethodIds.ProgramStateMachineType_Halt, Namespaces.OpcUa);
            AddMethod(Learning, "Halt", MethodIds.ProgramStateMachineType_Halt, Namespaces.OpcUa);
            AddMethod(Learning, "StartCollection", Ai.MethodIds.LearningJobType_StartCollection);
            AddMethod(Learning, "StopCollection", Ai.MethodIds.LearningJobType_StopCollection);
            AddMethod(Learning, "TriggerTraining", Ai.MethodIds.LearningJobType_TriggerTraining);
            AddMethod(Learning, "PromoteModel", Ai.MethodIds.LearningJobType_PromoteModel);
            AddFileMethods(RequestFile);
            AddFileMethods(ResponseFile);
            AddDiscovery();
            Server.CallHandler = HandleCall;
        }

        public IndustrialCompanionTestSession Server { get; } = new();

        public CompanionContext Context { get; }

        public AICompanionProvider Provider { get; }

        public CompanionTarget Model { get; }

        public CompanionTarget Candidate { get; }

        public CompanionTarget Dataset { get; }

        public CompanionTarget Deployment { get; }

        public CompanionTarget Job { get; }

        public CompanionTarget Learning { get; }

        public CompanionTarget Transfer { get; }

        public CompanionTarget RequestFile { get; }

        public CompanionTarget ResponseFile { get; }

        public CompanionTarget Evaluation { get; }

        public EndpointDescription Endpoint { get; } = new()
        {
            EndpointUrl = "opc.tcp://localhost:4840/AI",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
        };

        public bool Connected { get; set; } = true;

        public NodeId SessionId { get; set; } = new(101u);

        public ByteString Payload { get; set; } = ByteString.From(
            "{\"messages\":[{\"role\":\"user\",\"content\":\"private-prompt-marker\"}]}"u8);

        public ByteString Response { get; private set; } = ByteString.From("{\"answer\":\"fixture\"}"u8);

        public ArrayOf<Ai.CapabilityDataType> Capabilities { get; set; } =
        [
            new() { Name = "reachable", Supported = true },
            new() { Name = "inline-payload", Supported = true },
            new() { Name = "async-inference", Supported = true },
            new() { Name = "chunked-transfer", Supported = true },
            new() { Name = "chat", Supported = true }
        ];

        public NodeId ReturnedJob { get; set; }

        public NodeId ReturnedModel { get; set; }

        public Ai.FinishReasonEnum FinishReason { get; init; } = Ai.FinishReasonEnum.Stop;

        public bool TransferRequired { get; init; }

        public bool BeginAccepted { get; init; } = true;

        public bool TrainingAccepted { get; init; } = true;

        public int ResponseChunkLimit { get; init; } = 4096;

        public Action? AfterCapabilities { get; set; }

        public string? FailingMethod { get; set; }

        public StatusCode FailureStatus { get; set; } = StatusCodes.BadInvalidState;

        public bool FailAbort { get; init; }

        public List<byte> Uploaded { get; } = [];

        public ArrayOf<string> Mutations => Server.Calls
            .Filter(call => m_methodNames[call.MethodId] != "GetCapabilities")
            .ConvertAll(call => m_methodNames[call.MethodId]);

        public Task<CompanionTaskInput> PrepareAsync(
            string operation, CompanionTarget? target = null, ArrayOf<CompanionValue> inputs = default)
        {
            return Provider.PrepareInputAsync(Context, target ?? Deployment, operation,
                inputs.IsNull ? RequestInputs(operation) : inputs, CancellationToken.None).AsTask();
        }

        public Task<CompanionOperationResult> ExecuteAsync(
            string operation, CompanionTaskInput input, CompanionTarget? target = null)
        {
            return Provider.ExecutePreparedAsync(
                Context, target ?? Deployment, operation, input, null, CancellationToken.None).AsTask();
        }

        public ArrayOf<CompanionValue> RequestInputs(string operation)
        {
            ArrayOf<CompanionValue> common =
            [
                new("model", Variant.From(Model.NodeId)),
                new("capability", Variant.From("chat")),
                new("payload", Variant.From(Payload)),
                new("contentType", Variant.From("application/json")),
                new("parameters", Variant.FromStructure(ArrayOf<Opc.Ua.KeyValuePair>.Empty))
            ];
            return operation == "invoke-request" ? [.. common, new("timeout", Variant.From(1250d))] : common;
        }

        public ArrayOf<CompanionValue> LearningInputs(string operation)
        {
            ArrayOf<CompanionValue> common =
            [
                new("model", Variant.From(Model.NodeId)),
                new("dataset", Variant.From(Dataset.NodeId))
            ];
            return operation == "promote-model"
                ? [.. common, new("deployment", Variant.From(Deployment.NodeId))] : common;
        }

        public ArrayOf<CompanionValue> TransferInputs(ulong maximum)
        {
            return
            [
                new("maximumBytes", Variant.From(maximum)),
                new("sha256", Variant.From(ByteString.From(SHA256.HashData(Response.Span))))
            ];
        }

        public CallMethodRequest Call(string name)
        {
            return Server.Calls.ToList().Single(call => m_methodNames[call.MethodId] == name);
        }

        public void SetDestination(string endpoint, bool egress, Ai.InferenceLocationEnum location)
        {
            Set(Deployment, Ai.BrowseNames.EndpointUri, Variant.From(endpoint));
            Set(Deployment, Ai.BrowseNames.EgressPermitted, Variant.From(egress));
            Set(Deployment, Ai.BrowseNames.InferenceLocation, Variant.From((int)location));
        }

        public void ModelReference(NodeId model)
        {
            SetReference(Ai.ReferenceTypeIds.UsesModel, model);
        }

        public void FallbackReference(NodeId deployment)
        {
            SetReference(Ai.ReferenceTypeIds.FallsBackTo, deployment);
        }

        public NodeId Property(CompanionTarget target, string name)
        {
            return m_properties[(target.NodeId, name)];
        }

        public void Set(CompanionTarget target, string name, Variant value)
        {
            string namespaceUri = target.TypeName == "File" ? Namespaces.OpcUa : Ai.Namespaces.AI;
            if (m_properties.TryGetValue((target.NodeId, name), out NodeId node))
            {
                Server.SetValue(node, value);
            }
            else
            {
                m_properties.Add((target.NodeId, name), Server.AddProperty(target.NodeId, namespaceUri, name, value));
            }
        }

        public void SetResponse(ByteString bytes)
        {
            Response = bytes;
            Set(ResponseFile, BrowseNames.Size, Variant.From((ulong)bytes.Length));
            m_responsePosition = 0;
        }

        public void Deny(CompanionTarget target, string name, string? namespaceUri = null)
        {
            NodeId method = m_permissions[(target.NodeId, namespaceUri ?? Ai.Namespaces.AI, name)];
            Server.SetValue(method, Variant.From(false), Attributes.UserExecutable);
        }

        public NodeId Permission(CompanionTarget target, string name)
        {
            return m_permissions[(target.NodeId, Ai.Namespaces.AI, name)];
        }

        public void Hide(CompanionTarget target, string name, string? namespaceUri = null)
        {
            var qualified = new QualifiedName(name,
                (ushort)Server.NamespaceUris.GetIndex(namespaceUri ?? Ai.Namespaces.AI));
            Server.TranslateHandler = (path, _) =>
                path.StartingNode == target.NodeId &&
                path.RelativePath.Elements.Count == 1 &&
                path.RelativePath.Elements[0].TargetName == qualified
                    ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch } : Server.Translate(path);
        }

        public void SetProgramState(CompanionTarget target, NodeId state)
        {
            string name = state == ObjectIds.ProgramStateMachineType_Halted ? "Halted" :
                state == ObjectIds.ProgramStateMachineType_Ready ? "Ready" : "Running";
            if (!m_states.TryGetValue(target.NodeId, out AIWorkflowStateNodes? nodes))
            {
                NodeId current = Server.AddProperty(
                    target.NodeId, Namespaces.OpcUa, BrowseNames.CurrentState, Variant.From(new LocalizedText(name)));
                NodeId id = Server.AddProperty(current, Namespaces.OpcUa, BrowseNames.Id, Variant.From(state));
                nodes = new AIWorkflowStateNodes(current, id);
                m_states.Add(target.NodeId, nodes);
            }
            Server.SetValue(nodes.Current, Variant.From(new LocalizedText(name)));
            Server.SetValue(nodes.Id, Variant.From(state));
        }

        public void ObserveStates(ArrayOf<NodeId> states)
        {
            int reads = 0;
            AIWorkflowStateNodes nodes = m_states[Job.NodeId];
            Server.ReadHandler = (read, _) =>
            {
                if (read.NodeId == nodes.Current)
                {
                    SetProgramState(Job, states[Math.Min(reads / 2, states.Count - 1)]);
                    reads++;
                }
                return Server.Read(read);
            };
        }

        public void ReplaceResponseFile()
        {
            CompanionTarget replacement = Target("replacement-file", ObjectTypeIds.FileType, "File");
            Set(replacement, BrowseNames.Size, Variant.From((ulong)Response.Length));
            AddFileMethods(replacement);
            Server.AddChild(Transfer.NodeId, replacement.NodeId, Ai.Namespaces.AI, Ai.BrowseNames.Response);
        }

        public void VerifyNoMutation()
        {
            Assert.That(Mutations.IsEmpty, Is.True);
            Server.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        public void VerifyBorrowedSession()
        {
            Server.Session.Verify(value => value.CloseAsync(
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            Server.Session.Verify(value => value.Dispose(), Times.Never);
        }

        private CompanionTarget Target(string id, ExpandedNodeId type, string kind)
        {
            NodeId node = Server.Id(id);
            Server.AddObject(node, type, id);
            return new CompanionTarget("ai", node, id, kind);
        }

        private void SetReference(ExpandedNodeId reference, NodeId target)
        {
            Server.SetReferences(Deployment.NodeId, BrowseDirection.Forward, Server.Resolve(reference),
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(target),
                    NodeClass = NodeClass.Object,
                    IsForward = true,
                    ReferenceTypeId = Server.Resolve(reference)
                }
            ]);
        }

        private void AddMethod(
            CompanionTarget target, string name, ExpandedNodeId declaration, string? namespaceUri = null)
        {
            string uri = namespaceUri ?? Ai.Namespaces.AI;
            NodeId method = Server.AddProperty(target.NodeId, uri, name, Variant.Null);
            Server.SetValue(method, Variant.From(true), Attributes.Executable);
            Server.SetValue(method, Variant.From(true), Attributes.UserExecutable);
            m_permissions.Add((target.NodeId, uri, name), method);
            m_methodNames[method] = name;
            m_methodNames[Server.Resolve(declaration)] = name;
        }

        private void AddFileMethods(CompanionTarget file)
        {
            AddMethod(file, BrowseNames.Open, MethodIds.FileType_Open, Namespaces.OpcUa);
            AddMethod(file, BrowseNames.Read, MethodIds.FileType_Read, Namespaces.OpcUa);
            AddMethod(file, BrowseNames.Write, MethodIds.FileType_Write, Namespaces.OpcUa);
            AddMethod(file, BrowseNames.Close, MethodIds.FileType_Close, Namespaces.OpcUa);
        }

        private void AddDiscovery()
        {
            NodeId root = new AIClient(Server.Session.Object, Server.Telemetry).AIRootId;
            AddFolder(root, Ai.BrowseNames.Models, [Model, Candidate]);
            AddFolder(root, Ai.BrowseNames.Datasets, [Dataset]);
            AddFolder(root, Ai.BrowseNames.Deployments, [Deployment]);
            AddFolder(root, Ai.BrowseNames.LearningJobs, [Learning]);
            AddFolder(root, Ai.BrowseNames.Jobs, [Job, Transfer]);
            AddFolder(root, Ai.BrowseNames.Evaluations, [Evaluation]);
        }

        private void AddFolder(NodeId root, string name, ArrayOf<CompanionTarget> targets)
        {
            NodeId folder = Server.Id(name + "-folder");
            Server.AddObject(folder, ObjectTypeIds.FolderType);
            Server.AddChild(root, folder, Ai.Namespaces.AI, name);
            foreach (CompanionTarget target in targets)
            {
                Server.AddChild(folder, target.NodeId);
            }
        }

        private CallMethodResult HandleCall(CallMethodRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!m_methodNames.TryGetValue(request.MethodId, out string? method))
            {
                throw new AssertionException("Unexpected AI method: " + request.MethodId);
            }
            if (method == FailingMethod || (method == "Abort" && FailAbort))
            {
                return new CallMethodResult { StatusCode = FailureStatus };
            }
            switch (method)
            {
                case "GetCapabilities":
                    Assert.That(request.ObjectId, Is.EqualTo(Deployment.NodeId));
                    AfterCapabilities?.Invoke();
                    return Good([Variant.FromStructure(Capabilities)]);
                case "Invoke":
                    Assert.That(request.ObjectId, Is.EqualTo(Deployment.NodeId));
                    return Good(
                    [
                        Variant.From(TransferRequired ? ByteString.Empty : Response),
                        Variant.From("application/json"),
                        Variant.From(TransferRequired ? NodeId.Null : ReturnedModel),
                        Variant.FromStructure(new Ai.UsageDataType
                        {
                            InputUnits = 11, OutputUnits = 7, TotalUnits = 18, UnitKind = "tokens"
                        }),
                        Variant.From((int)(TransferRequired ? Ai.FinishReasonEnum.Length : FinishReason)),
                        Variant.FromStructure(ArrayOf<Ai.SafetyAssessmentDataType>.Empty),
                        Variant.From(0d),
                        Variant.From(TransferRequired),
                        Variant.From(TransferRequired ? Transfer.NodeId : NodeId.Null)
                    ]);
                case "InvokeAsync":
                    Assert.That(request.ObjectId, Is.EqualTo(Deployment.NodeId));
                    return Good([Variant.From(ReturnedJob)]);
                case "BeginTransfer":
                    Assert.That(request.ObjectId, Is.EqualTo(Deployment.NodeId));
                    Set(Transfer, Ai.BrowseNames.State, Variant.From((int)Ai.TransferStateEnum.Building));
                    return Good(
                        [Variant.From(BeginAccepted ? Transfer.NodeId : NodeId.Null), Variant.From(BeginAccepted)]);
                case "Open":
                    Assert.That(request.InputArguments[0].TryGetValue(out byte mode), Is.True);
                    Assert.That(request.ObjectId == RequestFile.NodeId || request.ObjectId == ResponseFile.NodeId,
                        Is.True);
                    Assert.That(mode, Is.EqualTo(request.ObjectId == RequestFile.NodeId ? 6 : 1));
                    return Good([Variant.From(request.ObjectId == RequestFile.NodeId ? 37u : 42u)]);
                case "Write":
                    Assert.That(request.ObjectId, Is.EqualTo(RequestFile.NodeId));
                    Assert.That(request.InputArguments[0].TryGetValue(out uint writeHandle), Is.True);
                    Assert.That(writeHandle, Is.EqualTo(37u));
                    Assert.That(request.InputArguments[1].TryGetValue(out ByteString data), Is.True);
                    Assert.That(data.Length, Is.InRange(1, 4096));
                    Uploaded.AddRange(data.Span.ToArray());
                    return Good();
                case "Read":
                    Assert.That(request.ObjectId, Is.EqualTo(ResponseFile.NodeId));
                    Assert.That(request.InputArguments[0].TryGetValue(out uint readHandle), Is.True);
                    Assert.That(readHandle, Is.EqualTo(42u));
                    Assert.That(request.InputArguments[1].TryGetValue(out int maximum), Is.True);
                    Assert.That(maximum, Is.EqualTo(4096));
                    int count = Math.Min(Math.Min(maximum, ResponseChunkLimit), Response.Length - m_responsePosition);
                    var chunk = ByteString.From(Response.Span.Slice(m_responsePosition, count));
                    m_responsePosition += count;
                    return Good([Variant.From(chunk)]);
                case "Close":
                    Assert.That(token.CanBeCanceled, Is.True, "Owned handle cleanup must have its own deadline.");
                    Assert.That(request.ObjectId == RequestFile.NodeId || request.ObjectId == ResponseFile.NodeId,
                        Is.True);
                    Assert.That(request.InputArguments[0].TryGetValue(out uint closeHandle), Is.True);
                    Assert.That(closeHandle, Is.EqualTo(request.ObjectId == RequestFile.NodeId ? 37u : 42u));
                    return Good();
                case "Execute":
                    Assert.That(request.ObjectId, Is.EqualTo(Transfer.NodeId));
                    return Good([Variant.From(true)]);
                case "Abort":
                    Assert.That(request.ObjectId, Is.EqualTo(Transfer.NodeId));
                    Assert.That(request.InputArguments.IsEmpty, Is.True);
                    return Good();
                case "Halt":
                    Assert.That(request.ObjectId == Job.NodeId || request.ObjectId == Learning.NodeId, Is.True);
                    Assert.That(request.InputArguments.IsEmpty, Is.True);
                    return Good();
                case "StartCollection":
                case "StopCollection":
                    Assert.That(request.ObjectId, Is.EqualTo(Learning.NodeId));
                    return Good();
                case "TriggerTraining":
                    Assert.That(request.ObjectId, Is.EqualTo(Learning.NodeId));
                    return Good([Variant.From(TrainingAccepted)]);
                case "PromoteModel":
                    Assert.That(request.ObjectId, Is.EqualTo(Learning.NodeId));
                    return Good([Variant.From(Candidate.NodeId)]);
                default:
                    throw new AssertionException("Unspecified typed AI method: " + method);
            }
        }

        private readonly Dictionary<(NodeId Parent, string Name), NodeId> m_properties = [];
        private readonly Dictionary<(NodeId Parent, string Namespace, string Name), NodeId> m_permissions = [];
        private readonly Dictionary<NodeId, string> m_methodNames = [];
        private readonly Dictionary<NodeId, AIWorkflowStateNodes> m_states = [];
        private int m_responsePosition;
    }

    internal sealed record AIWorkflowStateNodes(NodeId Current, NodeId Id);
}
