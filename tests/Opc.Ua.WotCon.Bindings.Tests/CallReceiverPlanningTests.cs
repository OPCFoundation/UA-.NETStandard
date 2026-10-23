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
 *
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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Exercises source Object receiver capture through the public document planning path.
    /// </summary>
    [TestFixture]
    public sealed class CallReceiverPlanningTests
    {
        private const string Endpoint = "opc.tcp://source.example:4840";
        private const string RootObjectId = "nsu=urn:source;s=Owner";
        private const string ExplicitObjectId = "nsu=urn:source;s=ExplicitOwner";

        /// <summary>
        /// Verifies that an omitted receiver is captured from the original root Object.
        /// </summary>
        [Test]
        public void PrepareCapturesOmittedReceiverFromSourceObject()
        {
            WotBindingPlan plan = Prepare(SourceThing());

            Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError), Is.False,
                string.Join("; ", plan.Diagnostics.Select(diagnostic => diagnostic.Message)));
            WotCompiledForm invoke = plan.CompiledForms.Single();
            Assert.Multiple(() =>
            {
                Assert.That(invoke.Operation, Is.EqualTo(WoTBindingCapabilityEnum.InvokeAction));
                Assert.That(invoke.Addressing.Target, Is.EqualTo("nsu=urn:source;s=Run"));
                Assert.That(invoke.Addressing.Metadata["callObjectId"], Is.EqualTo(RootObjectId));
                Assert.That(invoke.Endpoint.Scheme, Is.EqualTo("opc.tcp"));
                Assert.That(invoke.Endpoint.Host, Is.EqualTo("source.example"));
            });
        }

        /// <summary>
        /// Verifies that explicit receiver selection overrides the source root identity.
        /// </summary>
        [Test]
        public void PreparePreservesExplicitReceiverInsteadOfRootIdentity()
        {
            WotBindingPlan plan = Prepare(SourceThing(
                actionFormMembers: "\"uav:callObjectId\":\"" + ExplicitObjectId + "\","));

            Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError), Is.False,
                string.Join("; ", plan.Diagnostics.Select(diagnostic => diagnostic.Message)));
            WotCompiledForm invoke = plan.CompiledForms.Single();
            Assert.That(invoke.Addressing.Metadata["callObjectId"], Is.EqualTo(ExplicitObjectId));
            Assert.That(invoke.Addressing.Target, Is.EqualTo("nsu=urn:source;s=Run"));
        }

        /// <summary>
        /// Verifies that importing an explicit receiver does not change its admission policy.
        /// </summary>
        [Test]
        public void PrepareLeavesImportedExplicitReceiverUnmarked()
        {
            string source = SourceThing(
                actionFormMembers: "\"uav:callObjectId\":\"" + ExplicitObjectId + "\",")
                .Replace("\"run\": {",
                    "\"run\": {\"uav:resolvedFrom\":\"urn:original-source#/actions/run\",",
                    System.StringComparison.Ordinal);

            WotBindingPlan plan = Prepare(source);

            Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError), Is.False,
                string.Join("; ", plan.Diagnostics.Select(diagnostic => diagnostic.Message)));
            WotCompiledForm invoke = plan.CompiledForms.Single();
            Assert.That(invoke.Addressing.Metadata.ContainsKey("sourceCallReceiver"), Is.False);
            Assert.That(invoke.Addressing.Metadata["callObjectId"], Is.EqualTo(ExplicitObjectId));
            Assert.That(invoke.Addressing.Target, Is.EqualTo("nsu=urn:source;s=Run"));
        }

        /// <summary>
        /// Verifies that trusted parsed projection facts are not recreated from persisted JSON.
        /// </summary>
        [Test]
        public async Task PrepareResolvedDocumentRetainsReceiverButReparsedBytesRejectOmission()
        {
            using WotDocument view = await ResolveSourceProjectionAsync(SourceThing()).ConfigureAwait(false);
            var registry = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()]);

            WotBindingPlan trusted = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "trusted", WoTDocumentKindEnum.ThingDescription, view));
            WotBindingPlan reparsed = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "persisted", WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(view.RootElement.GetRawText())));

            Assert.That(trusted.Diagnostics.Any(diagnostic => diagnostic.IsError), Is.False,
                string.Join("; ", trusted.Diagnostics.Select(diagnostic => diagnostic.Message)));
            WotCompiledForm invoke = trusted.CompiledForms.Single();
            Assert.Multiple(() =>
            {
                Assert.That(invoke.Addressing.Metadata["callObjectId"], Is.EqualTo(RootObjectId));
                Assert.That(invoke.Addressing.Metadata["sourceCallReceiver"], Is.EqualTo("object"));
                Assert.That(invoke.Addressing.Target, Is.EqualTo("nsu=urn:source;s=Run"));
                Assert.That(invoke.Endpoint.Host, Is.EqualTo("source.example"));
                Assert.That(view.RootElement.GetProperty("actions").GetProperty("run")
                    .GetProperty("forms")[0].TryGetProperty("uav:callObjectId", out _), Is.False);
                Assert.That(reparsed.CompiledForms, Is.Empty);
                Assert.That(reparsed.Diagnostics.Any(diagnostic =>
                    diagnostic.IsError &&
                    diagnostic.Message.Contains("receiver", System.StringComparison.OrdinalIgnoreCase)), Is.True,
                    string.Join("; ", reparsed.Diagnostics.Select(diagnostic => diagnostic.Message)));
            });
        }

        /// <summary>
        /// Verifies that an imported explicit ConditionRefresh keeps its standard receiver and native input.
        /// </summary>
        [Test]
        public async Task PrepareImportedExplicitConditionRefreshRemainsUsable()
        {
            const string receiver = "i=2782";
            const uint subscriptionId = 42;
            string source = SourceThing(
                actionFormMembers: "\"uav:callObjectId\":\"" + receiver + "\",")
                .Replace("nsu=urn:source;s=Run", "i=3875", System.StringComparison.Ordinal)
                .Replace("\"run\": {", """
                    "run": {
                      "input": {
                        "type": "object",
                        "uav:argumentLayout": "named",
                        "uav:fieldOrder": ["SubscriptionId"],
                        "required": ["SubscriptionId"],
                        "properties": {
                          "SubscriptionId": { "type": "integer", "uav:dataTypeId": "i=7" }
                        }
                      },
                    """, System.StringComparison.Ordinal);
            using WotDocument view = await ResolveSourceProjectionAsync(source).ConfigureAwait(false);
            var registry = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()]);

            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "explicit-type", WoTDocumentKindEnum.ThingDescription, view));

            Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError), Is.False,
                string.Join("; ", plan.Diagnostics.Select(diagnostic => diagnostic.Message)));
            WotCompiledForm invoke = plan.CompiledForms.Single();
            Assert.That(invoke.Addressing.Metadata["callObjectId"], Is.EqualTo(receiver));
            Assert.That(invoke.Addressing.Metadata.ContainsKey("sourceCallReceiver"), Is.False);
            Assert.That(invoke.Addressing.Target, Is.EqualTo("i=3875"));
            WotBindingPlan persisted = Prepare(view.RootElement.GetRawText());
            Assert.That(persisted.CompiledForms.Single().Addressing.Metadata["callObjectId"],
                Is.EqualTo(receiver));

            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, calls, _) =>
                {
                    Assert.That(calls, Has.Count.EqualTo(1));
                    Assert.That(calls[0].ObjectId, Is.EqualTo(global::Opc.Ua.ObjectTypeIds.ConditionType));
                    Assert.That(calls[0].MethodId,
                        Is.EqualTo(global::Opc.Ua.MethodIds.ConditionType_ConditionRefresh));
                    Assert.That(calls[0].InputArguments, Has.Count.EqualTo(1));
                    Assert.That(calls[0].InputArguments[0].TypeInfo.BuiltInType,
                        Is.EqualTo(BuiltInType.UInt32));
                    Assert.That(calls[0].InputArguments[0].TryGetValue(out uint actualSubscriptionId), Is.True);
                    Assert.That(actualSubscriptionId, Is.EqualTo(subscriptionId));
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, invoke, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(subscriptionId)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(value => value.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Verifies that an explicitly present invalid receiver fails instead of falling back to root identity.
        /// </summary>
        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("\"\"")]
        [TestCase("\"not-a-node-id\"")]
        [TestCase("\"ns=1;s=ExplicitOwner\"")]
        [TestCase("\"i=0\"")]
        public void PrepareRejectsInvalidExplicitReceiverDespiteValidRootIdentity(string explicitReceiver)
        {
            WotBindingPlan plan = Prepare(SourceThing(
                actionFormMembers: "\"uav:callObjectId\":" + explicitReceiver + ","));

            Assert.Multiple(() =>
            {
                Assert.That(plan.CompiledForms, Is.Empty);
                Assert.That(plan.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotBindingDiagnosticCode.InvalidFieldValue &&
                    diagnostic.Message.Contains("uav:callObjectId", System.StringComparison.Ordinal)), Is.True,
                    string.Join("; ", plan.Diagnostics.Select(diagnostic => diagnostic.Message)));
            });
        }

        /// <summary>
        /// Verifies that source identity is not inferred from absent or non-Object root identity.
        /// </summary>
        [TestCase("", "uav:object")]
        [TestCase("\"uav:id\":\"nsu=urn:source;s=Owner\",", "uav:variable")]
        [TestCase("\"uav:id\":\"nsu=urn:source;s=Owner\",", "tm:ThingModel")]
        public void PrepareRejectsMissingOrNonObjectRootIdentity(string rootIdentity, string rootType)
        {
            WotBindingPlan plan = Prepare(SourceThing(rootIdentity, rootType));

            Assert.Multiple(() =>
            {
                Assert.That(plan.CompiledForms, Is.Empty);
                Assert.That(plan.UnsupportedForms, Has.Length.EqualTo(1));
                Assert.That(plan.Diagnostics.Any(diagnostic =>
                    diagnostic.IsError &&
                    diagnostic.Message.Contains("receiver", System.StringComparison.OrdinalIgnoreCase)), Is.True,
                    string.Join("; ", plan.Diagnostics.Select(diagnostic => diagnostic.Message)));
            });
        }

        /// <summary>
        /// Verifies that two source OPC endpoints make omitted receiver selection ambiguous.
        /// </summary>
        [Test]
        public void PrepareRejectsActionEndpointDifferentFromSourceObjectEndpoint()
        {
            WotBindingPlan plan = Prepare(SourceThing(
                actionHref: "opc.tcp://other.example:4840"));

            Assert.Multiple(() =>
            {
                Assert.That(plan.CompiledForms, Is.Empty);
                Assert.That(plan.UnsupportedForms, Has.Length.EqualTo(1));
                Assert.That(plan.Diagnostics.Any(diagnostic =>
                    diagnostic.IsError &&
                    diagnostic.Message.Contains("endpoint", System.StringComparison.OrdinalIgnoreCase)), Is.True,
                    string.Join("; ", plan.Diagnostics.Select(diagnostic => diagnostic.Message)));
            });
        }

        /// <summary>
        /// Verifies that published action-level componentOf arrays retain nested Object and ObjectType
        /// candidates without making the first array element an implicit receiver.
        /// </summary>
        [Test]
        public void PublishedComponentOfArrayRetainsCandidatesWithoutChoosingArrayOrder()
        {
            const string objectOwner = "nsu=urn:source;s=NestedObject";
            const string typeOwner = "nsu=urn:source;i=3000";
            WotBindingPlan firstOrder = PreparePublishedAction(
                "\"uav:componentOf\":[\"" + objectOwner + "\",\"" + typeOwner + "\"]");
            WotBindingPlan reversedOrder = PreparePublishedAction(
                "\"uav:componentOf\":[\"" + typeOwner + "\",\"" + objectOwner + "\"]");

            WotCompiledForm first = firstOrder.CompiledForms.Single();
            WotCompiledForm reversed = reversedOrder.CompiledForms.Single();
            Assert.Multiple(() =>
            {
                Assert.That(first.Addressing.Metadata["componentOfCandidates"],
                    Is.EqualTo("[\"" + objectOwner + "\",\"" + typeOwner + "\"]"));
                Assert.That(reversed.Addressing.Metadata["componentOfCandidates"],
                    Is.EqualTo("[\"" + typeOwner + "\",\"" + objectOwner + "\"]"));
                Assert.That(first.Addressing.Metadata.ContainsKey("callObjectId"), Is.False);
                Assert.That(reversed.Addressing.Metadata.ContainsKey("callObjectId"), Is.False);
                Assert.That(first.Addressing.Metadata["bindingProcessingMode"], Is.EqualTo("published-one-zero"));
                Assert.That(first.Addressing.Target, Is.EqualTo("nsu=urn:source;s=Run"));
            });
        }

        /// <summary>
        /// Verifies that an explicit receiver is admitted only when it is one of the published candidates.
        /// </summary>
        [Test]
        public void PublishedExplicitReceiverMustMatchComponentOfCandidate()
        {
            const string candidates = "\"uav:componentOf\":[\"nsu=urn:source;s=NestedObject\"," +
                "\"nsu=urn:source;i=3000\"]";
            WotBindingPlan accepted = PreparePublishedAction(
                candidates, formMembers: "\"uav:callObjectId\":\"nsu=urn:source;i=3000\",");
            WotBindingPlan rejected = PreparePublishedAction(
                candidates, formMembers: "\"uav:callObjectId\":\"nsu=urn:source;s=Other\",");

            Assert.That(accepted.CompiledForms.Single().Addressing.Metadata["callObjectId"],
                Is.EqualTo("nsu=urn:source;i=3000"));
            Assert.That(rejected.CompiledForms, Is.Empty);
            Assert.That(rejected.Diagnostics.Any(diagnostic =>
                diagnostic.IsError && diagnostic.Code == WotBindingDiagnosticCode.ConflictingFields), Is.True);
        }

        /// <summary>
        /// Verifies that missing, scalar, empty, and duplicate componentOf declarations fail closed.
        /// </summary>
        /// <param name="componentDeclaration">The action-level componentOf JSON fragment.</param>
        [TestCase(null)]
        [TestCase("\"uav:componentOf\":\"nsu=urn:source;s=NestedObject\"")]
        [TestCase("\"uav:componentOf\":[]")]
        [TestCase("\"uav:componentOf\":[\"nsu=urn:source;s=NestedObject\",\"nsu=urn:source;s=NestedObject\"]")]
        public void PublishedInvalidComponentOfDeclarationsAreDenied(string? componentDeclaration)
        {
            WotBindingPlan plan = PreparePublishedAction(componentDeclaration);

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.UnsupportedForms, Has.Length.EqualTo(1));
            Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError), Is.True);
        }

        /// <summary>
        /// Verifies that an old form-scoped scalar receiver cannot restore a missing published owner array.
        /// </summary>
        [Test]
        public void PublishedFormReceiverDoesNotReplaceMissingActionComponentOfArray()
        {
            WotBindingPlan plan = PreparePublishedAction(
                componentDeclaration: null,
                formMembers: "\"uav:callObjectId\":\"nsu=urn:source;s=NestedObject\",");

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.Diagnostics.Any(diagnostic =>
                diagnostic.IsError &&
                diagnostic.Message.Contains("array-valued action-level uav:componentOf",
                    System.StringComparison.Ordinal)), Is.True);
        }

        /// <summary>
        /// Verifies that ordinary action names do not create a false Condition occurrence.
        /// </summary>
        [Test]
        public void PublishedNonConditionMethodHasNoConditionInvocation()
        {
            WotBindingPlan plan = PreparePublishedAction(
                "\"uav:componentOf\":[\"nsu=urn:source;s=NestedObject\"]",
                actionName: "ConditionMaintenance");

            Assert.That(plan.CompiledForms.Single().ConditionInvocation, Is.Null);
        }

        /// <summary>
        /// Verifies that a uniquely owning candidate is the actual Method Call receiver.
        /// </summary>
        /// <param name="reverse">Whether to place the non-owning candidate first.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublishedComponentOfOwnerIsUsedAsTheActualCallReceiver(bool reverse)
        {
            const string owner = "nsu=urn:source;s=NestedObject";
            const string nonOwner = "nsu=urn:source;i=3000";
            string[] candidates = reverse ? [nonOwner, owner] : [owner, nonOwner];
            WotCompiledForm form = PreparePublishedAction(
                "\"uav:componentOf\":[\"" + candidates[0] + "\",\"" + candidates[1] + "\"]")
                .CompiledForms.Single();
            Mock<ISession> session = CreatePublishedReceiverSession([owner]);
            NodeId? calledObject = null;
            NodeId? calledMethod = null;
            session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, calls, _) =>
                {
                    Assert.That(calls, Has.Count.EqualTo(1));
                    calledObject = calls[0].ObjectId;
                    calledMethod = calls[0].MethodId;
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable ownerChannel = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(calledObject, Is.EqualTo(new NodeId("NestedObject", 1)));
            Assert.That(calledMethod, Is.EqualTo(new NodeId("Run", 1)));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Verifies that a compiled explicit receiver is the Object used for the actual Method Call.
        /// </summary>
        [Test]
        public async Task PublishedExplicitReceiverIsUsedAsTheActualCallReceiver()
        {
            const string otherOwner = "nsu=urn:source;s=OtherObject";
            const string explicitOwner = "nsu=urn:source;s=NestedObject";
            WotCompiledForm form = PreparePublishedAction(
                "\"uav:componentOf\":[\"" + otherOwner + "\",\"" + explicitOwner + "\"]",
                formMembers: "\"uav:callObjectId\":\"" + explicitOwner + "\",")
                .CompiledForms.Single();
            Mock<ISession> session = CreatePublishedReceiverSession(
                [otherOwner, explicitOwner], multipleObjectOwners: true);
            NodeId? calledObject = null;
            NodeId? calledMethod = null;
            session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, calls, _) =>
                {
                    Assert.That(calls, Has.Count.EqualTo(1));
                    calledObject = calls[0].ObjectId;
                    calledMethod = calls[0].MethodId;
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable ownerChannel = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(calledObject, Is.EqualTo(new NodeId("NestedObject", 1)));
            Assert.That(calledMethod, Is.EqualTo(new NodeId("Run", 1)));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Verifies that an Object's declared ObjectType can own the Method while the Call receiver stays the Object.
        /// </summary>
        [Test]
        public async Task PublishedObjectTypeMethodOwnershipUsesTheContainingObjectReceiver()
        {
            const string owner = "nsu=urn:source;s=NestedObject";
            WotCompiledForm form = PreparePublishedAction(
                "\"uav:componentOf\":[\"" + owner + "\"]").CompiledForms.Single();
            Mock<ISession> session = CreatePublishedReceiverSession([owner], methodOwnedByObjectType: true);
            NodeId? calledObject = null;
            NodeId? calledMethod = null;
            session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, calls, _) =>
                {
                    calledObject = calls[0].ObjectId;
                    calledMethod = calls[0].MethodId;
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable ownerChannel = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(calledObject, Is.EqualTo(new NodeId("NestedObject", 1)));
            Assert.That(calledMethod, Is.EqualTo(new NodeId("Run", 1)));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Verifies that two actual owners are denied regardless of candidate array order.
        /// </summary>
        /// <param name="reverse">Whether to reverse the candidate array.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublishedAmbiguousOwnersAreDeniedWithoutChoosingTheFirstCandidate(bool reverse)
        {
            string[] owners = reverse
                ? ["nsu=urn:source;i=3000", "nsu=urn:source;s=NestedObject"]
                : ["nsu=urn:source;s=NestedObject", "nsu=urn:source;i=3000"];
            string declaration = "\"uav:componentOf\":[\"" + owners[0] + "\",\"" + owners[1] + "\"]";
            WotCompiledForm form = PreparePublishedAction(declaration).CompiledForms.Single();
            Mock<ISession> session = CreatePublishedReceiverSession(owners, multipleObjectOwners: true);
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable ownerChannel = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadMethodInvalid));
            Assert.That(result.Outputs, Is.Empty);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Verifies that the published owner declaration cannot authorize a Call on another endpoint.
        /// </summary>
        [Test]
        public async Task PublishedOwnerOnDifferentEndpointIsDeniedBeforeCall()
        {
            const string owner = "nsu=urn:source;s=NestedObject";
            WotCompiledForm form = PreparePublishedAction(
                "\"uav:componentOf\":[\"" + owner + "\"]").CompiledForms.Single();
            Mock<ISession> session = CreatePublishedReceiverSession(
                [owner], configuredEndpoint: "opc.tcp://different.example:4840");
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable ownerChannel = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(result.Outputs, Is.Empty);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Verifies that missing native Method signature metadata rejects a published action before Call.
        /// </summary>
        [Test]
        public async Task PublishedMissingNativeArgumentMetadataPreventsSpeculativeCall()
        {
            const string owner = "nsu=urn:source;s=NestedObject";
            WotCompiledForm form = PreparePublishedAction(
                "\"uav:componentOf\":[\"" + owner + "\"]",
                schemas: """
                    "input":{
                      "type":"object",
                      "properties":{
                        "Second":{"type":"integer","uav:dataTypeId":"i=6"},
                        "First":{"type":"integer","uav:dataTypeId":"i=6"}
                      }
                    },
                    """).CompiledForms.Single();
            Mock<ISession> session = CreatePublishedReceiverSession([owner]);
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable ownerChannel = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(result.Outputs, Is.Empty);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
        }

        private static WotBindingPlan PreparePublishedAction(
            string? componentDeclaration,
            string formMembers = "",
            string actionName = "run",
            string schemas = "")
        {
            string actionMembers = componentDeclaration is null ? string.Empty : componentDeclaration + ",";
            string document = $$$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA/WoT-Binding/"}],
                  "securityDefinitions":{"none":{"scheme":"nosec"}},
                  "security":["none"],
                  "actions":{"{{{actionName}}}":{
                    {{{actionMembers}}}
                    {{{schemas}}}
                    "forms":[{
                      "href":"{{{Endpoint}}}",
                      {{{formMembers}}}
                      "uav:id":"nsu=urn:source;s=Run",
                      "op":"invokeaction"
                    }]
                  }}
                }
                """;
            var selection = new WotBindingSelectionContext(
                [], [], WotBindingProcessingMode.PublishedOneZero);
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "published-component-of",
                WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(document),
                selection: selection);
            var registry = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()]);
            return registry.Prepare(request);
        }

        private static Mock<ISession> CreatePublishedReceiverSession(
            string[] owners,
            string configuredEndpoint = "opc.tcp://source.example:4840",
            bool methodOwnedByObjectType = false,
            bool multipleObjectOwners = false)
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            context.Factory.Builder.AddOpcUa().Commit();
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            session.SetupGet(value => value.ServerUris).Returns(new StringTable());
            session.SetupGet(value => value.Factory).Returns(context.Factory);
            session.SetupGet(value => value.MessageContext).Returns(context);
            session.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            session.SetupGet(value => value.SessionId).Returns(new NodeId("published-session", 0));
            session.SetupGet(value => value.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                null,
                new EndpointDescription
                {
                    EndpointUrl = configuredEndpoint,
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                },
                null));
            session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, requests, _) =>
                    {
                        Assert.That(requests, Has.Count.EqualTo(2));
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results =
                            [
                                new DataValue(new Variant((int)NodeClass.Object)),
                                new DataValue(new Variant((int)NodeClass.Method))
                            ]
                        });
                    });
            session.Setup(value => value.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, requests, _) =>
                    {
                        BrowseDescription browse = requests[0];
                        ArrayOf<ReferenceDescription> references = [];
                        bool isObjectOwner = browse.NodeId == new NodeId("NestedObject", 1) ||
                            multipleObjectOwners && browse.NodeId == new NodeId(3000, 1);
                        bool isTypeOwner = browse.NodeId == new NodeId("OwnerType", 1);
                        string ownerId = isObjectOwner || isTypeOwner
                            ? "nsu=urn:source;s=NestedObject"
                            : "nsu=urn:source;i=3000";
                        if (browse.ReferenceTypeId == Types.ReferenceTypeIds.HasTypeDefinition &&
                            browse.NodeId == new NodeId("NestedObject", 1))
                        {
                            references = [new ReferenceDescription
                            {
                                BrowseName = new QualifiedName("OwnerType"),
                                NodeId = new ExpandedNodeId("OwnerType", 1)
                            }];
                        }
                        else if (browse.ReferenceTypeId == Types.ReferenceTypeIds.HasComponent &&
                            browse.BrowseDirection == BrowseDirection.Forward &&
                            owners.Contains(ownerId, StringComparer.Ordinal) &&
                            ((!methodOwnedByObjectType && isObjectOwner) ||
                                (methodOwnedByObjectType && isTypeOwner)))
                        {
                            references = [new ReferenceDescription
                            {
                                BrowseName = new QualifiedName("Run"),
                                NodeId = new ExpandedNodeId("Run", 1)
                            }];
                        }
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new BrowseResult { References = references }]
                        });
                    });
            return session;
        }

        private static async Task<WotDocument> ResolveSourceProjectionAsync(string source)
        {
            var sourceResolver = new Mock<IWotThingResolver>();
            sourceResolver.Setup(value => value.ResolveThingAsync(
                    "urn:receiver-source", It.IsAny<WotResolutionContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotResolverResult>(
                    WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(source))));
            var resolver = new WotProjectionResolver(sourceResolver.Object);
            using var projection = WotDocument.Parse(Encoding.UTF8.GetBytes("""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
                  ],
                  "@type": ["Thing", "uav:projection"],
                  "uav:id": "nsu=urn:projection;s=View",
                  "title": "Receiver projection",
                  "uav:scenario": "urn:scenario:Receiver",
                  "uav:projects": [{
                    "uav:sourceName": "source",
                    "href": "urn:receiver-source",
                    "type": "application/td+json",
                    "uav:routing": "source",
                    "uav:select": [{ "uav:affordanceKind": "action" }]
                  }]
                }
                """));
            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(projection).ConfigureAwait(false);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            return result.Value!;
        }

        private static WotBindingPlan Prepare(string document)
        {
            var registry = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()]);
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "call-receiver-tests",
                WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(document));
            return registry.Prepare(request);
        }

        private static string SourceThing(
            string rootIdentity = "\"uav:id\":\"" + RootObjectId + "\",",
            string rootType = "uav:object",
            string actionHref = Endpoint,
            string actionFormMembers = "")
        {
            return $$$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                      "tm": "https://www.w3.org/2019/wot/tm#"
                    }
                  ],
                  "@type": ["Thing", "{{{rootType}}}"],
                  {{{rootIdentity}}}
                  "base": "{{{Endpoint}}}",
                  "actions": {
                    "run": {
                      "forms": [{
                        "href": "{{{actionHref}}}",
                        {{{actionFormMembers}}}
                        "uav:id": "nsu=urn:source;s=Run",
                        "op": "invokeaction"
                      }]
                    }
                  }
                }
                """;
        }
    }
}
