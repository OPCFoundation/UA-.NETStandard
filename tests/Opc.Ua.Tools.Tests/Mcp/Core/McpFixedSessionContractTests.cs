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

#if NET10_0
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    /// <summary>
    /// Verifies explicit fixed-host contracts without claiming SDK generation atomicity for arbitrary ISession mocks.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class McpFixedSessionContractTests
    {
        /// <summary>
        /// An unknown custom session is rejected before preflight or mutation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnknownCustomSessionFailsClosedBeforeClassification(bool contractForAnotherSession)
        {
            var model = new McpAuthorizationModel();
            model.AddNode(s_target);
            IMcpFixedSessionContract? contract = contractForAnotherSession
                ? new FixedContract(new Mock<ISession>().Object) : null;
            await using ServiceProvider provider = CreateProvider(contract);
            OpcUaSessionManager manager = await RegisterAsync(provider, model).ConfigureAwait(false);
            string json = await ConvenienceTools.WriteValueAsync(manager, s_target.ToString(), "17", "Int32")
                .ConfigureAwait(false);
            AssertError(json, "BadNotSupported");
            Assert.That(model.Browses, Is.Empty);
            model.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// A fixed contract confirms only its exact registration; normal results and registered name are preserved.
        /// </summary>
        [Test]
        public async Task ExplicitFixedContractPreservesNormalBehaviorAndRegisteredAuthority()
        {
            var model = new McpAuthorizationModel();
            model.AddNode(s_target);
            var contract = new FixedContract(model.Session.Object);
            ConfigureWrite(model);
            await using ServiceProvider provider = CreateProvider(contract);
            OpcUaSessionManager manager = await RegisterAsync(provider, model).ConfigureAwait(false);
            string json = await ConvenienceTools.WriteValueAsync(
                manager, s_target.ToString(), "17", "Int32", "prod-a").ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(), Is.EqualTo("GoodClamped"));
            Assert.That(contract.CheckedRegistrations, Has.Count.EqualTo(2));
            Assert.That(contract.CheckedRegistrations[0], Is.SameAs(manager.GetSessionInfoOrThrow()));
            Assert.That(contract.CheckedRegistrations[1].Name, Is.EqualTo("Prod-A"));
            model.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Once);
            model.Session.Verify(value => value.Dispose(), Times.Never);
        }

        /// <summary>
        /// An observed contract or metadata-epoch change invalidates the preflight before the custom service is called.
        /// </summary>
        [TestCase("Namespace")]
        [TestCase("SessionId")]
        [TestCase("Endpoint")]
        [TestCase("ServerUris")]
        [TestCase("NodeCache")]
        [TestCase("ModelEpoch")]
        public async Task ChangedFixedContractMetadataPreventsMutation(string changed)
        {
            var model = new McpAuthorizationModel();
            model.AddNode(s_target);
            var contract = new FixedContract(model.Session.Object);
            int classified = 0;
            model.Session.Setup(value => value.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> requests,
                    CancellationToken _) =>
                {
                    classified++;
                    if (requests[0].ReferenceTypeId == ReferenceTypeIds.HierarchicalReferences)
                    {
                        ChangeMetadata();
                    }
                    return ValueTask.FromResult(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new BrowseResult { StatusCode = StatusCodes.Good, References = [] }]
                    });
                });
            await using ServiceProvider provider = CreateProvider(contract);
            OpcUaSessionManager manager = await RegisterAsync(provider, model).ConfigureAwait(false);
            string json = await ConvenienceTools.WriteValueAsync(manager, s_target.ToString(), "17", "Int32")
                .ConfigureAwait(false);
            AssertError(json, "BadSessionIdInvalid");
            Assert.That(classified, Is.EqualTo(3));
            model.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);

            void ChangeMetadata()
            {
                switch (changed)
                {
                    case "Namespace":
                        model.NamespaceUris.Update([Namespaces.OpcUa, McpAuthorizationModel.GdsNamespaceUri]);
                        break;
                    case "SessionId":
                        model.Session.SetupGet(value => value.SessionId).Returns(new NodeId(201u, 1));
                        break;
                    case "Endpoint":
                        model.Endpoint.EndpointUrl = "opc.tcp://replacement.invalid:4840";
                        break;
                    case "ServerUris":
                        model.MessageContext.ServerUris.Append("urn:replacement");
                        break;
                    case "NodeCache":
                        model.Session.SetupGet(value => value.NodeCache).Returns(new Mock<INodeCache>().Object);
                        break;
                    default:
                        contract.ModelEpoch++;
                        break;
                }
            }
        }

        /// <summary>
        /// The fixed-session declaration grants no protected operation authority.
        /// </summary>
        [Test]
        public async Task FixedContractDoesNotAuthorizeProtectedModels()
        {
            var model = new McpAuthorizationModel();
            var contract = new FixedContract(model.Session.Object);
            await using ServiceProvider provider = CreateProvider(contract);
            OpcUaSessionManager manager = await RegisterAsync(provider, model).ConfigureAwait(false);
            string json = await ConvenienceTools.WriteValueAsync(
                manager, ObjectTypeIds.RoleType.ToString(), "17", "Int32").ConfigureAwait(false);
            AssertError(json, "BadUserAccessDenied");
            model.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Known committed custom-session results are not postchecked after a late cancellation or contract change.
        /// </summary>
        [Test]
        public async Task FixedContractPreservesAcknowledgedResultAfterLateInvalidation()
        {
            var model = new McpAuthorizationModel();
            model.AddNode(s_target);
            var contract = new FixedContract(model.Session.Object);
            using var cancellation = new CancellationTokenSource();
            model.Session.Setup(value => value.WriteAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    contract.ModelEpoch++;
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    return new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [StatusCodes.GoodClamped]
                    };
                });
            await using ServiceProvider provider = CreateProvider(contract);
            OpcUaSessionManager manager = await RegisterAsync(provider, model).ConfigureAwait(false);
            string json = await ConvenienceTools.WriteValueAsync(
                manager, s_target.ToString(), "17", "Int32", ct: cancellation.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(), Is.EqualTo("GoodClamped"));
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(contract.CheckedRegistrations, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Registers exactly the immutable mock contract requested by the test, leaving the default fail-closed.
        /// </summary>
        private static ServiceProvider CreateProvider(IMcpFixedSessionContract? contract = null)
        {
            var services = new ServiceCollection();
            if (contract != null)
            {
                services.AddSingleton(contract);
            }
            services.AddOpcUaMcpCore(new OpcUaMcpOptions());
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Borrows the known scripted session without granting operation capabilities.
        /// </summary>
        private static async Task<OpcUaSessionManager> RegisterAsync(
            ServiceProvider provider, McpAuthorizationModel model)
        {
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            _ = await manager.RegisterExistingSessionAsync("Prod-A", model.Session.Object, "UserName")
                .ConfigureAwait(false);
            return manager;
        }

        /// <summary>
        /// Makes the custom mutation willing so only the real generic tool can prevent it.
        /// </summary>
        private static void ConfigureWrite(McpAuthorizationModel model)
        {
            model.Session.Setup(value => value.WriteAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [StatusCodes.GoodClamped]
                }));
        }

        /// <summary>
        /// Pins the exact denied result rather than accepting any failure.
        /// </summary>
        private static void AssertError(string json, string status)
        {
            using var document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(), Is.EqualTo(status));
        }

        /// <summary>
        /// Explicitly declares one test-owned mock fixed until its host-owned model epoch changes.
        /// </summary>
        private sealed class FixedContract(ISession session) : IMcpFixedSessionContract
        {
            /// <summary>
            /// Gets or sets the model epoch whose change withdraws the host's fixed metadata guarantee.
            /// </summary>
            internal long ModelEpoch { get; set; }

            /// <summary>
            /// Gets the actual host registrations inspected by the contract.
            /// </summary>
            internal List<OpcUaSessionManager.SessionInfo> CheckedRegistrations { get; } = [];

            /// <inheritdoc/>
            public bool IsFixed(OpcUaSessionManager.SessionInfo registration)
            {
                CheckedRegistrations.Add(registration);
                return ReferenceEquals(registration.Session, session) && ModelEpoch == 0;
            }
        }

        private static readonly NodeId s_target = new(100u, 1);
    }
}
#endif
