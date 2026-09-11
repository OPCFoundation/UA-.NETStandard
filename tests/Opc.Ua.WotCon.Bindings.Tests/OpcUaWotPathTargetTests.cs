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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    public sealed partial class OpcUaWotPathTargetTests
    {
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

        [TestCase("missing")]
        [TestCase("ambiguous")]
        [TestCase("partial")]
        [TestCase("remote")]
        [TestCase("wrong-class")]
        [TestCase("id-disagrees")]
        public async Task InvalidPathTargetsNeverSendTheBusinessRead(string failure)
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
                failure == "id-disagrees" ? "\"uav:id\":\"nsu=urn:path:source;s=Other\"," : string.Empty);
            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(result.Status), Is.True);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(businessReads, Is.Zero);
            Assert.That(context.NamespaceUris.Count, Is.EqualTo(3));
        }

        private static WotBindingPlan Plan(
            WotProtocolBinderRegistry registry,
            string collection,
            string operation,
            string path,
            string formTerms = "",
            string href = "opc.tcp://localhost:4840/UA/Factory")
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
            return registry.Prepare(WotBindingPlanRequest.FromDocument(
                "/things/path", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(json)));
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
