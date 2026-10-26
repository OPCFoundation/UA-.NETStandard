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
            return $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                      "tm": "https://www.w3.org/2019/wot/tm#"
                    }
                  ],
                  "@type": ["Thing", "{{rootType}}"],
                  {{rootIdentity}}
                  "base": "{{Endpoint}}",
                  "actions": {
                    "run": {
                      "forms": [{
                        "href": "{{actionHref}}",
                        {{actionFormMembers}}
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
