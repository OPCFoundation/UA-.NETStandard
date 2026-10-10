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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    public sealed partial class OpcUaWotPathTargetTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task ExplicitPathIdentityRetainsItsServerQualification(bool inHref, bool remote)
        {
            Mock<ISession> session = Session(Context());
            SetupTarget(session, new NodeId("ResolvedValue", 2), NodeClass.Variable);
            int writes = 0;
            session.Setup(value => value.WriteAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Callback(() => writes++)
                .ReturnsAsync(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [StatusCodes.Good]
                });
            string id = (remote ? "svr=1;" : string.Empty) + "nsu=urn:path:source;s=ResolvedValue";
            string href = "opc.tcp://localhost:4840/UA/Factory";
            string terms = string.Empty;
            if (inHref)
            {
                href += "?id=" + Uri.EscapeDataString(id);
            }
            else
            {
                terms = "\"uav:id\":\"" + id + "\",";
            }
            WotProtocolBinderRegistry registry = Registry(session);
            WotBindingPlan plan = Plan(registry, "properties", "writeproperty", "/t:Value", terms, href);
            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            WotWriteResult result = await channel.WriteAsync(new DataValue(new Variant(42))).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(remote ? StatusCodes.BadNodeIdInvalid : StatusCodes.Good));
            Assert.That(writes, Is.EqualTo(remote ? 0 : 1));
        }

        [TestCase("elements", 64, true)]
        [TestCase("elements", 65, false)]
        [TestCase("text", 512, true)]
        [TestCase("text", 511, false)]
        public async Task ExecutingRegistryEnforcesItsOwnPathBounds(string bound, int boundary, bool accepted)
        {
            Mock<ISession> session = Session(Context());
            SetupTarget(session, new NodeId("ResolvedValue", 2), NodeClass.Variable);
            int writes = 0;
            session.Setup(value => value.WriteAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Callback(() => writes++)
                .ReturnsAsync(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [StatusCodes.Good]
                });
            WotProtocolBinderRegistry planner = Registry(session, new WotBindingBounds
            {
                MaxBrowsePathElements = 128,
                MaxUriLength = 4096
            });
            int elementCount = bound == "elements" ? boundary : 64;
            string path = "/" + string.Join("/", Enumerable.Repeat("t:Value", elementCount));
            WotBindingPlan plan = Plan(planner, "properties", "writeproperty", path);
            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            WotProtocolBinderRegistry executor = Registry(session, new WotBindingBounds
            {
                MaxBrowsePathElements = bound == "elements" ? 64 : 128,
                MaxUriLength = bound == "text" ? boundary : 4096
            });
            await using IWotBindingChannel channel = await executor.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            WotWriteResult result = await channel.WriteAsync(new DataValue(new Variant(42))).ConfigureAwait(false);

            Assert.That(result.Status,
                Is.EqualTo(accepted ? StatusCodes.Good : StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(writes, Is.EqualTo(accepted ? 1 : 0));
            session.Verify(value => value.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()),
                accepted ? Times.Once() : Times.Never());
        }

        [TestCase("unchanged", 1)]
        [TestCase("before", 0)]
        [TestCase("during", 0)]
        [TestCase("same-identity", 0)]
        public async Task InPlaceSessionConfigurationChangesInvalidatePathAdmission(
            string transition, int expectedWrites)
        {
            Mock<ISession> session = Session(Context());
            SetupTarget(session, new NodeId("ResolvedValue", 2), NodeClass.Variable);
            UserTokenType tokenType = UserTokenType.UserName;
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.TokenType).Returns(() => tokenType);
            session.SetupGet(value => value.Identity).Returns(identity.Object);
            session.SetupGet(value => value.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                null, new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840/UA/Factory",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy { PolicyId = "username", TokenType = UserTokenType.UserName }
                    ]
                }, null));
            int writes = 0;
            int translations = 0;
            session.Setup(value => value.WriteAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Callback(() => writes++)
                .ReturnsAsync(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [StatusCodes.Good]
                });
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<BrowsePath> _, CancellationToken _) =>
                {
                    translations++;
                    if (transition is "during" or "same-identity")
                    {
                        if (transition == "during")
                        {
                            tokenType = UserTokenType.Anonymous;
                        }
                        session.Raise(value => value.SessionConfigurationChanged += null, EventArgs.Empty);
                    }
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                        Translation(new NodeId("ResolvedValue", 2)));
                });
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    ConstrainedSessionFactory = (_, _) => new ValueTask<ISession>(session.Object),
                    DisposeSession = false
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            const string json = /*lang=json,strict*/ """
                {
                  "@context":{"t":"urn:path:source"},
                  "title":"Constrained path",
                  "securityDefinitions":{
                    "identity":{"scheme":"uav:authentication","uav:userIdentityToken":"UserName"}
                  },
                  "security":"identity",
                  "properties":{"Value":{
                    "type":"integer",
                    "forms":[{
                      "href":"opc.tcp://localhost:4840/UA/Factory",
                      "uav:browsePath":"/t:Value",
                      "op":"writeproperty"
                    }]
                  }}
                }
                """;
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "identity-transition", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(json)));
            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);
            if (transition == "before")
            {
                tokenType = UserTokenType.Anonymous;
                session.Raise(value => value.SessionConfigurationChanged += null, EventArgs.Empty);
            }

            WotWriteResult result = await channel.WriteAsync(new DataValue(new Variant(42))).ConfigureAwait(false);

            StatusCode expectedStatus = transition switch
            {
                "unchanged" => StatusCodes.Good,
                "same-identity" => StatusCodes.BadInvalidState,
                _ => StatusCodes.BadIdentityTokenRejected
            };
            Assert.That(result.Status.Code, Is.EqualTo(expectedStatus.Code), result.Error);
            Assert.That(writes, Is.EqualTo(expectedWrites));
            Assert.That(translations, Is.EqualTo(transition == "before" ? 0 : 1));
        }
    }
}
