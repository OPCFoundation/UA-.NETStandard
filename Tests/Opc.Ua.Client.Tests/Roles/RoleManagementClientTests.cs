/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Roles;

namespace Opc.Ua.Client.Tests.Roles
{
    /// <summary>
    /// Unit tests for <see cref="RoleManagementClient"/> using a mocked
    /// <see cref="ISession"/>. Verifies the correct OPC UA service request
    /// shape (object/method NodeIds, input-argument marshalling) for every
    /// public method, plus the result decoding for <c>AddRole</c>.
    /// </summary>
    [TestFixture]
    [Category("Roles")]
    [Parallelizable]
    public class RoleManagementClientTests
    {
        private Mock<ISession> m_sessionMock = null!;
        private RoleManagementClient m_client = null!;

        [SetUp]
        public void SetUp()
        {
            m_sessionMock = new Mock<ISession>();
            m_sessionMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            m_client = new RoleManagementClient(m_sessionMock.Object);
        }

        // ----------------------------------------------------------------
        // Construction
        // ----------------------------------------------------------------

        [Test]
        public void Constructor_NullSession_Throws()
        {
            Assert.That(() => new RoleManagementClient(null!),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void Session_ReturnsConstructorArgument()
        {
            Assert.That(m_client.Session, Is.SameAs(m_sessionMock.Object));
        }

        // ----------------------------------------------------------------
        // AddRoleAsync
        // ----------------------------------------------------------------

        [Test]
        public async Task AddRoleAsync_SendsCallToRoleSetWithRoleNameAndNamespaceUri()
        {
            NodeId expectedNew = new(42u, 2);
            ArrayOf<CallMethodRequest> capturedRequests = default;
            m_sessionMock.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) => capturedRequests = requests)
                .Returns(new ValueTask<CallResponse>(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = ArrayOf.Wrapped(
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = ArrayOf.Wrapped([Variant.From(expectedNew)])
                        }
                    ]),
                    DiagnosticInfos = []
                }));

            NodeId result = await m_client.AddRoleAsync("OpsLead", "urn:example:roles").ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(expectedNew));
            Assert.That(capturedRequests.Count, Is.EqualTo(1));
            CallMethodRequest req = capturedRequests[0];
            Assert.That(req.ObjectId, Is.EqualTo(ObjectIds.Server_ServerCapabilities_RoleSet));
            Assert.That(req.MethodId, Is.EqualTo(MethodIds.Server_ServerCapabilities_RoleSet_AddRole));
            Assert.That(req.InputArguments.Count, Is.EqualTo(2));
            Assert.That(req.InputArguments[0].TryGetValue(out string roleName), Is.True);
            Assert.That(roleName, Is.EqualTo("OpsLead"));
            Assert.That(req.InputArguments[1].TryGetValue(out string namespaceUri), Is.True);
            Assert.That(namespaceUri, Is.EqualTo("urn:example:roles"));
        }

        [Test]
        public void AddRoleAsync_EmptyName_Throws()
        {
            Assert.That(
                async () => await m_client.AddRoleAsync(string.Empty).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentException>());
        }

        [Test]
        public async Task AddRoleAsync_NullNamespaceUri_SendsEmptyString()
        {
            ArrayOf<CallMethodRequest> capturedRequests = default;
            SetupCallResponse(captured => capturedRequests = captured,
                outputs: [Variant.From(new NodeId(1u, 0))]);

            await m_client.AddRoleAsync("MyRole").ConfigureAwait(false);

            Assert.That(capturedRequests[0].InputArguments[1].TryGetValue(out string ns), Is.True);
            Assert.That(ns, Is.EqualTo(string.Empty));
        }

        // ----------------------------------------------------------------
        // RemoveRoleAsync
        // ----------------------------------------------------------------

        [Test]
        public async Task RemoveRoleAsync_SendsCallToRoleSetWithRoleNodeId()
        {
            NodeId roleId = new(7u, 0);
            ArrayOf<CallMethodRequest> capturedRequests = default;
            SetupCallResponse(captured => capturedRequests = captured);

            await m_client.RemoveRoleAsync(roleId).ConfigureAwait(false);

            CallMethodRequest req = capturedRequests[0];
            Assert.That(req.ObjectId, Is.EqualTo(ObjectIds.Server_ServerCapabilities_RoleSet));
            Assert.That(req.MethodId, Is.EqualTo(MethodIds.Server_ServerCapabilities_RoleSet_RemoveRole));
            Assert.That(req.InputArguments.Count, Is.EqualTo(1));
            Assert.That(req.InputArguments[0].TryGetValue(out NodeId arg), Is.True);
            Assert.That(arg, Is.EqualTo(roleId));
        }

        [Test]
        public void RemoveRoleAsync_NullRoleId_Throws()
        {
            Assert.That(
                async () => await m_client.RemoveRoleAsync(NodeId.Null).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        // ----------------------------------------------------------------
        // AddIdentityAsync
        // ----------------------------------------------------------------

        [Test]
        public async Task AddIdentityAsync_TranslatesBrowsePathThenSendsCallWithRuleStructure()
        {
            NodeId roleId = ObjectIds.WellKnownRole_Observer;
            NodeId methodId = new(99u, 0);
            var rule = new IdentityMappingRuleType
            {
                CriteriaType = IdentityCriteriaType.UserName,
                Criteria = "alice"
            };

            // First call: TranslateBrowsePathsToNodeIdsAsync resolves AddIdentity.
            ArrayOf<BrowsePath> capturedPaths = default;
            m_sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) => capturedPaths = paths)
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(
                        [
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets = ArrayOf.Wrapped(
                                [
                                    new BrowsePathTarget { TargetId = methodId }
                                ])
                            }
                        ]),
                        DiagnosticInfos = []
                    }));

            // Second call: CallAsync sends the AddIdentity method invocation.
            ArrayOf<CallMethodRequest> capturedRequests = default;
            SetupCallResponse(captured => capturedRequests = captured);

            await m_client.AddIdentityAsync(roleId, rule).ConfigureAwait(false);

            // Translate browse path is to roleId / "AddIdentity".
            Assert.That(capturedPaths.Count, Is.EqualTo(1));
            Assert.That(capturedPaths[0].StartingNode, Is.EqualTo(roleId));
            Assert.That(capturedPaths[0].RelativePath.Elements.Count, Is.EqualTo(1));
            Assert.That(capturedPaths[0].RelativePath.Elements[0].TargetName.Name,
                Is.EqualTo(BrowseNames.AddIdentity));

            // Call uses the resolved methodId and the rule structure.
            Assert.That(capturedRequests[0].ObjectId, Is.EqualTo(roleId));
            Assert.That(capturedRequests[0].MethodId, Is.EqualTo(methodId));
            Assert.That(capturedRequests[0].InputArguments.Count, Is.EqualTo(1));
#pragma warning disable CS8600 // TryGetStructure uses [MaybeNullWhen(false)] on the out arg.
            bool decoded = capturedRequests[0].InputArguments[0].TryGetStructure(
                out IdentityMappingRuleType actualRule);
#pragma warning restore CS8600
            Assert.That(decoded, Is.True);
            Assert.That(actualRule!.CriteriaType, Is.EqualTo(IdentityCriteriaType.UserName));
            Assert.That(actualRule.Criteria, Is.EqualTo("alice"));
        }

        [Test]
        public void AddIdentityAsync_NullRule_Throws()
        {
            Assert.That(
                async () => await m_client.AddIdentityAsync(ObjectIds.WellKnownRole_Observer, null!).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        // ----------------------------------------------------------------
        // AddApplicationAsync / RemoveApplicationAsync
        // ----------------------------------------------------------------

        [Test]
        public async Task AddApplicationAsync_SendsApplicationUriString()
        {
            NodeId methodId = new(100u, 0);
            SetupTranslateResponse(methodId);
            ArrayOf<CallMethodRequest> capturedRequests = default;
            SetupCallResponse(captured => capturedRequests = captured);

            await m_client.AddApplicationAsync(
                ObjectIds.WellKnownRole_Observer, "urn:example:app").ConfigureAwait(false);

            Assert.That(capturedRequests[0].InputArguments[0].TryGetValue(out string uri), Is.True);
            Assert.That(uri, Is.EqualTo("urn:example:app"));
        }

        // ----------------------------------------------------------------
        // AddEndpointAsync
        // ----------------------------------------------------------------

        [Test]
        public async Task AddEndpointAsync_SendsEndpointStructure()
        {
            NodeId methodId = new(101u, 0);
            SetupTranslateResponse(methodId);
            ArrayOf<CallMethodRequest> capturedRequests = default;
            SetupCallResponse(captured => capturedRequests = captured);

            var endpoint = new EndpointType
            {
                EndpointUrl = "opc.tcp://srv:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256"
            };
            await m_client.AddEndpointAsync(ObjectIds.WellKnownRole_Observer, endpoint).ConfigureAwait(false);

#pragma warning disable CS8600 // TryGetStructure uses [MaybeNullWhen(false)] on the out arg.
            bool decoded = capturedRequests[0].InputArguments[0].TryGetStructure(
                out EndpointType actual);
#pragma warning restore CS8600
            Assert.That(decoded, Is.True);
            Assert.That(actual!.EndpointUrl, Is.EqualTo(endpoint.EndpointUrl));
            Assert.That(actual.SecurityMode, Is.EqualTo(endpoint.SecurityMode));
            Assert.That(actual.SecurityPolicyUri, Is.EqualTo(endpoint.SecurityPolicyUri));
        }

        // ----------------------------------------------------------------
        // SetApplicationsExcludeAsync / SetEndpointsExcludeAsync / SetCustomConfigurationAsync
        // ----------------------------------------------------------------

        [Test]
        public async Task SetApplicationsExcludeAsync_WritesBoolToResolvedProperty()
        {
            NodeId propertyId = new(200u, 0);
            SetupTranslateResponse(propertyId);

            ArrayOf<WriteValue> capturedWrites = default;
            m_sessionMock.Setup(s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<WriteValue>, CancellationToken>(
                    (_, writes, _) => capturedWrites = writes)
                .Returns(new ValueTask<WriteResponse>(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = ArrayOf.Wrapped([StatusCodes.Good]),
                    DiagnosticInfos = []
                }));

            await m_client.SetApplicationsExcludeAsync(
                ObjectIds.WellKnownRole_Observer, value: true).ConfigureAwait(false);

            Assert.That(capturedWrites.Count, Is.EqualTo(1));
            Assert.That(capturedWrites[0].NodeId, Is.EqualTo(propertyId));
            Assert.That(capturedWrites[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(capturedWrites[0].Value.WrappedValue.TryGetValue(out bool b), Is.True);
            Assert.That(b, Is.True);
        }

        [Test]
        public async Task SetEndpointsExcludeAsync_ResolvesEndpointsExcludeProperty()
        {
            NodeId propertyId = new(201u, 0);
            ArrayOf<BrowsePath> capturedPaths = default;
            m_sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) => capturedPaths = paths)
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(
                        [
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets = ArrayOf.Wrapped(
                                [
                                    new BrowsePathTarget { TargetId = propertyId }
                                ])
                            }
                        ]),
                        DiagnosticInfos = []
                    }));
            m_sessionMock.Setup(s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WriteResponse>(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = ArrayOf.Wrapped([StatusCodes.Good]),
                    DiagnosticInfos = []
                }));

            await m_client.SetEndpointsExcludeAsync(ObjectIds.WellKnownRole_Operator, false).ConfigureAwait(false);

            Assert.That(capturedPaths[0].RelativePath.Elements[0].TargetName.Name,
                Is.EqualTo(BrowseNames.EndpointsExclude));
        }

        // ----------------------------------------------------------------
        // Gap 13: Remove* / SetCustomConfiguration unit coverage
        // ----------------------------------------------------------------

        [Test]
        public async Task RemoveIdentityAsync_TranslatesBrowsePathThenSendsCallWithRuleStructure()
        {
            NodeId methodId = new(110u, 0);
            ArrayOf<BrowsePath> capturedPaths = default;
            m_sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) => capturedPaths = paths)
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(
                        [
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets = ArrayOf.Wrapped(
                                [
                                    new BrowsePathTarget { TargetId = methodId }
                                ])
                            }
                        ]),
                        DiagnosticInfos = []
                    }));

            ArrayOf<CallMethodRequest> capturedRequests = default;
            SetupCallResponse(captured => capturedRequests = captured);

            var rule = new IdentityMappingRuleType
            {
                CriteriaType = IdentityCriteriaType.UserName,
                Criteria = "alice"
            };
            await m_client.RemoveIdentityAsync(ObjectIds.WellKnownRole_Observer, rule).ConfigureAwait(false);

            Assert.That(capturedPaths[0].RelativePath.Elements[0].TargetName.Name,
                Is.EqualTo(BrowseNames.RemoveIdentity));
            Assert.That(capturedRequests[0].MethodId, Is.EqualTo(methodId));
#pragma warning disable CS8600
            bool decoded = capturedRequests[0].InputArguments[0].TryGetStructure(
                out IdentityMappingRuleType actual);
#pragma warning restore CS8600
            Assert.That(decoded, Is.True);
            Assert.That(actual!.CriteriaType, Is.EqualTo(IdentityCriteriaType.UserName));
            Assert.That(actual.Criteria, Is.EqualTo("alice"));
        }

        [Test]
        public void RemoveIdentityAsync_NullRule_Throws()
        {
            Assert.That(
                async () => await m_client.RemoveIdentityAsync(ObjectIds.WellKnownRole_Observer, null!)
                    .ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public async Task RemoveApplicationAsync_TranslatesBrowsePathThenSendsCallWithUri()
        {
            NodeId methodId = new(111u, 0);
            SetupTranslateResponse(methodId);
            ArrayOf<CallMethodRequest> capturedRequests = default;
            SetupCallResponse(captured => capturedRequests = captured);

            await m_client.RemoveApplicationAsync(
                ObjectIds.WellKnownRole_Observer, "urn:example:app").ConfigureAwait(false);

            Assert.That(capturedRequests[0].MethodId, Is.EqualTo(methodId));
            Assert.That(capturedRequests[0].InputArguments[0].TryGetValue(out string uri), Is.True);
            Assert.That(uri, Is.EqualTo("urn:example:app"));
        }

        [Test]
        public void RemoveApplicationAsync_NullUri_Throws()
        {
            Assert.That(
                async () => await m_client.RemoveApplicationAsync(ObjectIds.WellKnownRole_Observer, null!)
                    .ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentException>());
        }

        [Test]
        public void RemoveApplicationAsync_EmptyUri_Throws()
        {
            Assert.That(
                async () => await m_client.RemoveApplicationAsync(ObjectIds.WellKnownRole_Observer, string.Empty)
                    .ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentException>());
        }

        [Test]
        public async Task RemoveEndpointAsync_SendsEndpointStructure()
        {
            NodeId methodId = new(112u, 0);
            SetupTranslateResponse(methodId);
            ArrayOf<CallMethodRequest> capturedRequests = default;
            SetupCallResponse(captured => capturedRequests = captured);

            var endpoint = new EndpointType
            {
                EndpointUrl = "opc.tcp://srv:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt
            };
            await m_client.RemoveEndpointAsync(ObjectIds.WellKnownRole_Observer, endpoint).ConfigureAwait(false);

            Assert.That(capturedRequests[0].MethodId, Is.EqualTo(methodId));
#pragma warning disable CS8600
            bool decoded = capturedRequests[0].InputArguments[0].TryGetStructure(
                out EndpointType actual);
#pragma warning restore CS8600
            Assert.That(decoded, Is.True);
            Assert.That(actual!.EndpointUrl, Is.EqualTo(endpoint.EndpointUrl));
        }

        [Test]
        public void RemoveEndpointAsync_NullEndpoint_Throws()
        {
            Assert.That(
                async () => await m_client.RemoveEndpointAsync(ObjectIds.WellKnownRole_Observer, null!)
                    .ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public async Task SetCustomConfigurationAsync_ResolvesCustomConfigurationProperty()
        {
            NodeId propertyId = new(202u, 0);
            ArrayOf<BrowsePath> capturedPaths = default;
            m_sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) => capturedPaths = paths)
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(
                        [
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets = ArrayOf.Wrapped(
                                [
                                    new BrowsePathTarget { TargetId = propertyId }
                                ])
                            }
                        ]),
                        DiagnosticInfos = []
                    }));
            ArrayOf<WriteValue> capturedWrites = default;
            m_sessionMock.Setup(s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<WriteValue>, CancellationToken>(
                    (_, writes, _) => capturedWrites = writes)
                .Returns(new ValueTask<WriteResponse>(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = ArrayOf.Wrapped([StatusCodes.Good]),
                    DiagnosticInfos = []
                }));

            await m_client.SetCustomConfigurationAsync(
                ObjectIds.WellKnownRole_Operator, value: true).ConfigureAwait(false);

            Assert.That(capturedPaths[0].RelativePath.Elements[0].TargetName.Name,
                Is.EqualTo(BrowseNames.CustomConfiguration));
            Assert.That(capturedWrites[0].NodeId, Is.EqualTo(propertyId));
            Assert.That(capturedWrites[0].Value.WrappedValue.TryGetValue(out bool b), Is.True);
            Assert.That(b, Is.True);
        }

        [Test]
        public void AddApplicationAsync_NullUri_Throws()
        {
            Assert.That(
                async () => await m_client.AddApplicationAsync(ObjectIds.WellKnownRole_Observer, null!)
                    .ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentException>());
        }

        [Test]
        public void AddEndpointAsync_NullEndpoint_Throws()
        {
            Assert.That(
                async () => await m_client.AddEndpointAsync(ObjectIds.WellKnownRole_Observer, null!)
                    .ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private void SetupTranslateResponse(NodeId resolvedNodeId)
        {
            m_sessionMock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(
                        [
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets = ArrayOf.Wrapped(
                                [
                                    new BrowsePathTarget { TargetId = resolvedNodeId }
                                ])
                            }
                        ]),
                        DiagnosticInfos = []
                    }));
        }

        private void SetupCallResponse(
            System.Action<ArrayOf<CallMethodRequest>> captureRequests,
            Variant[]? outputs = null)
        {
            m_sessionMock.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) => captureRequests(requests))
                .Returns(new ValueTask<CallResponse>(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = ArrayOf.Wrapped(
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = outputs == null
                                ? []
                                : ArrayOf.Wrapped(outputs)
                        }
                    ]),
                    DiagnosticInfos = []
                }));
        }
    }
}
