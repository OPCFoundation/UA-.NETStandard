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
using System.Text;
using System.Text.Json;
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
    /// Contains tests for OPC UA WoT path target.
    /// </summary>
    [TestFixture]
    public sealed partial class OpcUaWotPathTargetTests
    {
        /// <summary>
        /// Verifies that path only forms compile with the complete endpoint.
        /// </summary>
        [TestCase("properties", "readproperty")]
        [TestCase("properties", "writeproperty")]
        [TestCase("properties", "observeproperty")]
        [TestCase("actions", "invokeaction")]
        [TestCase("events", "subscribeevent")]
        public void PathOnlyFormsCompileWithTheCompleteEndpoint(string collection, string operation)
        {
            WotProtocolBinderRegistry registry = Registry(new Mock<ISession>());
            WotBindingPlan plan = Plan(registry, collection, operation, "/Objects/t:Device/t:Value");

            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            Assert.That(plan.CompiledForms[0].Endpoint.BaseUri, Is.EqualTo("opc.tcp://localhost:4840/UA/Factory"));
            Assert.That(plan.CompiledForms[0].IsExecutable, Is.True);
        }

        /// <summary>
        /// Verifies that read uses portable names and the declared anchor before the business operation.
        /// </summary>
        /// <param name="absolute">The absolute flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadUsesPortableNamesAndTheDeclaredAnchorBeforeTheBusinessOperation(bool absolute)
        {
            ServiceMessageContext context = Context();
            Mock<ISession> session = Session(context);
            NodeId target = new("ResolvedValue", 2);
            int translated = 0;
            int valueReads = 0;
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<BrowsePath> requests, CancellationToken _) =>
                {
                    translated++;
                    Assert.That(requests.Count, Is.EqualTo(1));
                    Assert.That(requests[0].StartingNode,
                        Is.EqualTo(absolute ? Ua.ObjectIds.RootFolder : new NodeId("Source", 2)));
                    ArrayOf<RelativePathElement> elements = requests[0].RelativePath.Elements;
                    Assert.That(elements.Count, Is.EqualTo(absolute ? 3 : 2));
                    Assert.That(elements[^1].TargetName, Is.EqualTo(new QualifiedName("Value", 2)));
                    Assert.That(elements[^2].TargetName, Is.EqualTo(new QualifiedName("Device/Primary", 2)));
                    Assert.That(elements[^1].ReferenceTypeId,
                        Is.EqualTo(Types.ReferenceTypeIds.HierarchicalReferences));
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(Translation(target));
                });
            session.Setup(value => value.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> requests,
                    CancellationToken _) =>
                {
                    Assert.That(translated, Is.EqualTo(1));
                    Assert.That(requests.Count, Is.EqualTo(1));
                    Assert.That(requests[0].NodeId, Is.EqualTo(target));
                    if (requests[0].AttributeId == Attributes.NodeClass)
                    {
                        return new ValueTask<ReadResponse>(Read(new Variant((int)NodeClass.Variable)));
                    }
                    Assert.That(requests[0].AttributeId, Is.EqualTo(Attributes.Value));
                    valueReads++;
                    return new ValueTask<ReadResponse>(Read(new Variant(42)));
                });
            WotProtocolBinderRegistry registry = Registry(session);
            string path = (absolute ? "/Objects/" : string.Empty) + "t:Device&/Primary/t:Value";
            WotBindingPlan plan = Plan(registry, "properties", "readproperty", path);
            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(result.Value.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(translated, Is.EqualTo(1));
            Assert.That(valueReads, Is.EqualTo(1));
            Assert.That(context.NamespaceUris.Count, Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies that invalid path targets never send the business read.
        /// </summary>
        [TestCase("missing", WotBindingProcessingMode.Current)]
        [TestCase("ambiguous", WotBindingProcessingMode.Current)]
        [TestCase("partial", WotBindingProcessingMode.Current)]
        [TestCase("remote", WotBindingProcessingMode.Current)]
        [TestCase("wrong-class", WotBindingProcessingMode.Current)]
        [TestCase("id-disagrees", WotBindingProcessingMode.Current)]
        [TestCase("missing", WotBindingProcessingMode.PublishedOneZero)]
        [TestCase("ambiguous", WotBindingProcessingMode.PublishedOneZero)]
        [TestCase("partial", WotBindingProcessingMode.PublishedOneZero)]
        [TestCase("remote", WotBindingProcessingMode.PublishedOneZero)]
        [TestCase("wrong-class", WotBindingProcessingMode.PublishedOneZero)]
        [TestCase("id-disagrees", WotBindingProcessingMode.PublishedOneZero)]
        public async Task InvalidPathTargetsNeverSendTheBusinessRead(
            string failure, WotBindingProcessingMode processingMode)
        {
            ServiceMessageContext context = Context();
            Mock<ISession> session = Session(context);
            NodeId target = new("ResolvedValue", 2);
            TranslateBrowsePathsToNodeIdsResponse response = Translation(target);
            if (failure == "missing")
            {
                response.Results = [new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }];
            }
            else if (failure == "ambiguous")
            {
                response.Results[0].Targets =
                [
                    response.Results[0].Targets[0],
                    new BrowsePathTarget
                    {
                        TargetId = new ExpandedNodeId("Other", 2),
                        RemainingPathIndex = uint.MaxValue
                    }
                ];
            }
            else if (failure == "partial")
            {
                response.Results[0].Targets[0].RemainingPathIndex = 1;
            }
            else if (failure == "remote")
            {
                response.Results[0].Targets[0].TargetId = new ExpandedNodeId(target, null, 1);
            }
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
            int businessReads = 0;
            session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> requests,
                    CancellationToken _) =>
                {
                    if (requests[0].AttributeId == Attributes.Value)
                    {
                        businessReads++;
                    }
                    return new ValueTask<ReadResponse>(Read(new Variant((int)NodeClass.Method)));
                });
            WotProtocolBinderRegistry registry = Registry(session);
            WotBindingPlan plan = Plan(
                registry, "properties", "readproperty", "/Objects/t:Value",
                failure == "id-disagrees" ? "\"uav:id\":\"nsu=urn:path:source;s=Other\"," : string.Empty,
                processingMode: processingMode);
            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(result.Status), Is.True);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(businessReads, Is.Zero);
            Assert.That(context.NamespaceUris.Count, Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies that a numeric namespace index in the published browse
        /// path is resolved through the document's namespace authority rather
        /// than reused as an index into the reordered server NamespaceArray.
        /// </summary>
        [Test]
        public async Task PublishedNumericBrowsePrefixUsesDocumentNamespaceAuthority()
        {
            ServiceMessageContext context = Context();
            Mock<ISession> session = Session(context);
            NodeId target = new("ResolvedValue", 2);
            int translated = 0;
            int valueReads = 0;
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<BrowsePath> requests, CancellationToken _) =>
                {
                    translated++;
                    ArrayOf<RelativePathElement> elements = requests[0].RelativePath.Elements;
                    Assert.That(requests[0].StartingNode, Is.EqualTo(Ua.ObjectIds.RootFolder));
                    Assert.That(elements, Has.Count.EqualTo(3));
                    Assert.That(elements[0].TargetName, Is.EqualTo(new QualifiedName("Objects", 0)));
                    Assert.That(elements[1].TargetName, Is.EqualTo(new QualifiedName("Device", 2)));
                    Assert.That(elements[2].TargetName, Is.EqualTo(new QualifiedName("Value", 2)));
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(Translation(target));
                });
            session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> requests,
                    CancellationToken _) =>
                {
                    if (requests[0].AttributeId == Attributes.NodeClass)
                    {
                        return new ValueTask<ReadResponse>(Read(new Variant((int)NodeClass.Variable)));
                    }
                    Assert.That(requests[0].NodeId, Is.EqualTo(target));
                    valueReads++;
                    return new ValueTask<ReadResponse>(Read(new Variant(731)));
                });
            var registry = Registry(session);
            const string document = """
                {
                  "@context": {
                    "1": "urn:path:source",
                    "uav": "http://opcfoundation.org/UA/WoT-Binding/"
                  },
                  "@type": ["Thing", "uav:object"],
                  "uav:id": "nsu=urn:path:source;s=Source",
                  "security": "none",
                  "securityDefinitions": { "none": { "scheme": "nosec" } },
                  "properties": {
                    "value": {
                      "type": "integer",
                      "forms": [{
                        "href": "opc.tcp://localhost:4840/UA/Factory",
                        "uav:browsePath": "/Objects/1:Device/1:Value",
                        "op": "readproperty"
                      }]
                    }
                  }
                }
                """;
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "/things/published-path",
                WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(document),
                selection: new WotBindingSelectionContext(
                    [], [], WotBindingProcessingMode.PublishedOneZero));
            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            Assert.That(request.CreateContext(
                WotPayloadCodecRegistry.Default, WotBindingBounds.Default).ProcessingMode,
                Is.EqualTo(WotBindingProcessingMode.PublishedOneZero));
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(result.Value.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(731));
            Assert.That(translated, Is.EqualTo(1));
            Assert.That(valueReads, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a numeric browse prefix is not enabled by URI or vocabulary heuristics.
        /// </summary>
        [Test]
        public void NumericBrowsePrefixRequiresPublishedProcessingSelection()
        {
            const string document = """
                {
                  "@context": {
                    "1": "urn:path:source",
                    "uav": "http://opcfoundation.org/UA/WoT-Binding/"
                  },
                  "properties": {
                    "value": {
                      "type": "integer",
                      "forms": [{
                        "href": "opc.tcp://localhost:4840/UA/Factory",
                        "uav:browsePath": "/Objects/1:Device/1:Value",
                        "op": "readproperty"
                      }]
                    }
                  }
                }
                """;
            WotBindingPlan plan = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()]).Prepare(
                WotBindingPlanRequest.FromDocument(
                    "/things/current-path",
                    WoTDocumentKindEnum.ThingDescription,
                    Encoding.UTF8.GetBytes(document)));

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(
                plan.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotBindingDiagnosticCode.InvalidFieldValue),
                Is.True);
        }

        /// <summary>
        /// Verifies a published numeric NodeId namespace resolves through the
        /// original document context rather than the connected Session index.
        /// </summary>
        [TestCase("identity", "root")]
        [TestCase("query", "root")]
        [TestCase("identity", "form")]
        [TestCase("query", "form")]
        public void PublishedNumericNodeIdUsesDocumentNamespaceAuthority(
            string targetLocation,
            string contextLocation)
        {
            string formTarget = targetLocation == "identity"
                ? "\"uav:id\":\"ns=1;s=Temperature\","
                : "\"href\":\"opc.tcp://localhost:4840/UA/Factory?id=ns=1;s=Temperature\",";
            string href = targetLocation == "identity"
                ? "\"href\":\"opc.tcp://localhost:4840/UA/Factory\","
                : string.Empty;
            string rootContext = contextLocation == "root"
                ? "\"@context\": { \"1\": \"urn:path:source\" },"
                : string.Empty;
            string formContext = contextLocation == "form"
                ? "\"@context\": { \"1\": \"urn:path:source\" },"
                : string.Empty;
            string document = $$"""
                {
                  {{rootContext}}
                  "properties": {
                    "value": {
                      "type": "integer",
                      "forms": [{
                        {{formContext}}
                        {{formTarget}}
                        {{href}}
                        "op": "readproperty"
                      }]
                    }
                  }
                }
                """;
            var request = WotBindingPlanRequest.FromDocument(
                "/things/published-numeric-id",
                WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(document),
                selection: new WotBindingSelectionContext(
                    [], [], WotBindingProcessingMode.PublishedOneZero));
            using WotDocument parsed = WotDocument.Parse(Encoding.UTF8.GetBytes(document));
            JsonElement sourceForm = parsed.RootElement
                .GetProperty("properties")
                .GetProperty("value")
                .GetProperty("forms")[0];

            Assert.That(
                parsed.TryGetContextPrefix("1", out string namespaceUri, sourceForm),
                Is.True);
            Assert.That(namespaceUri, Is.EqualTo("urn:path:source"));
            WotBindingPlan plan = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()]).Prepare(request);

            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            Assert.That(
                plan.CompiledForms[0].Addressing.Target,
                Is.EqualTo("nsu=urn:path:source;s=Temperature"));
        }

        /// <summary>
        /// Verifies a published numeric NodeId fails closed without context
        /// authority, even when the index could exist in the Session.
        /// </summary>
        [Test]
        public void PublishedNumericNodeIdWithoutDocumentAuthorityFailsPlanning()
        {
            const string document = """
                {
                  "properties": {
                    "value": {
                      "type": "integer",
                      "forms": [{
                        "href": "opc.tcp://localhost:4840/UA/Factory?id=ns=1;s=Temperature",
                        "op": "readproperty"
                      }]
                    }
                  }
                }
                """;
            var request = WotBindingPlanRequest.FromDocument(
                "/things/unmapped-published-numeric-id",
                WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(document),
                selection: new WotBindingSelectionContext(
                    [], [], WotBindingProcessingMode.PublishedOneZero));
            WotBindingPlan plan = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()]).Prepare(request);

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.UnsupportedForms, Has.Length.EqualTo(1));
            Assert.That(plan.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        /// <summary>
        /// Verifies that an unresolved numeric browse namespace cannot be
        /// replaced by a coincidentally valid server NamespaceArray index.
        /// </summary>
        [Test]
        public void PublishedNumericBrowsePrefixWithoutDocumentAuthorityFailsPlanning()
        {
            var registry = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()]);
            const string document = """
                {
                  "@context": { "uav": "http://opcfoundation.org/UA/WoT-Binding/" },
                  "@type": ["Thing", "uav:object"],
                  "uav:id": "nsu=urn:path:source;s=Source",
                  "properties": {
                    "value": {
                      "type": "integer",
                      "forms": [{
                        "href": "opc.tcp://localhost:4840/UA/Factory",
                        "uav:browsePath": "/Objects/1:Value",
                        "op": "readproperty"
                      }]
                    }
                  }
                }
                """;
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "/things/unmapped-published-path",
                WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(document),
                selection: new WotBindingSelectionContext(
                    [], [], WotBindingProcessingMode.PublishedOneZero)));

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.UnsupportedForms, Has.Length.EqualTo(1));
            Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError), Is.True);
        }

        private static WotBindingPlan Plan(
            WotProtocolBinderRegistry registry,
            string collection,
            string operation,
            string path,
            string formTerms = "",
            string href = "opc.tcp://localhost:4840/UA/Factory",
            WotBindingProcessingMode processingMode = WotBindingProcessingMode.Current)
        {
            string json = $$$"""
                {
                  "@context": {"t":"urn:path:source","uav":"http://opcfoundation.org/UA/WoT-Binding/"},
                  "@type":["Thing","uav:object"],
                  "title":"Path target",
                  "uav:id":"nsu=urn:path:source;s=Source",
                  "security":"none",
                  "securityDefinitions":{"none":{"scheme":"nosec"}},
                  "{{{collection}}}":{
                    "Value":{
                      "type":"integer",
                      "forms":[{
                        {{{formTerms}}}
                        "href":"{{{href}}}",
                        "uav:browsePath":"{{{path}}}",
                        "uav:callObjectId":"nsu=urn:path:source;s=Source",
                        "op":"{{{operation}}}"
                      }]
                    }
                  }
                }
                """;
            if (collection != "actions")
            {
                json = json.Replace("\"uav:callObjectId\":\"nsu=urn:path:source;s=Source\",", string.Empty,
                    StringComparison.Ordinal);
            }
            WotBindingSelectionContext? selection = processingMode == WotBindingProcessingMode.Current
                ? null
                : new WotBindingSelectionContext([], [], processingMode);
            return registry.Prepare(WotBindingPlanRequest.FromDocument(
                "/things/path", WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(json), selection: selection));
        }

        private static WotProtocolBinderRegistry Registry(Mock<ISession> session, WotBindingBounds? bounds = null)
        {
            return new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    SessionFactory = (_, _) => new ValueTask<ISession>(session.Object),
                    DisposeSession = false
                })],
                bounds: bounds,
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
        }

        private static ServiceMessageContext Context()
        {
            var context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            context.NamespaceUris.Append("urn:path:unrelated");
            context.NamespaceUris.Append("urn:path:source");
            return context;
        }

        private static Mock<ISession> Session(ServiceMessageContext context)
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(value => value.ServerUris).Returns(context.ServerUris);
            session.SetupGet(value => value.Factory).Returns(context.Factory);
            return session;
        }

        private static TranslateBrowsePathsToNodeIdsResponse Translation(NodeId target)
        {
            return new TranslateBrowsePathsToNodeIdsResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results =
                [
                    new BrowsePathResult
                    {
                        StatusCode = StatusCodes.Good,
                        Targets = [new BrowsePathTarget { TargetId = target, RemainingPathIndex = uint.MaxValue }]
                    }
                ]
            };
        }

        private static ReadResponse Read(Variant value)
        {
            return new ReadResponse { ResponseHeader = new ResponseHeader(), Results = [new DataValue(value)] };
        }
    }
}
