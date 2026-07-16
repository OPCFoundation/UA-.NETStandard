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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Roles;

namespace Opc.Ua.Client.Tests.Roles
{
    [TestFixture]
    [Category("Roles")]
    public sealed class RoleManagementClientCoverageTests
    {
        private Mock<ISession> m_session = null!;
        private RoleManagementClient m_client = null!;

        [SetUp]
        public void SetUp()
        {
            m_session = new Mock<ISession>(MockBehavior.Strict);
            m_session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            m_client = new RoleManagementClient(m_session.Object);
        }

        [Test]
        public async Task ListRolesAsyncFiltersReferencesAndReadsRolePropertiesAsync()
        {
            NodeId roleId = new(6001);
            NodeId identitiesId = new(7001);
            NodeId applicationsId = new(7002);
            NodeId applicationsExcludeId = new(7003);
            NodeId endpointsId = new(7004);
            NodeId endpointsExcludeId = new(7005);
            NodeId customConfigurationId = new(7006);
            ArrayOf<string> applications = s_applications.ToArrayOf();

            m_session.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    0,
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseResponse>(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            References =
                            [
                                new ReferenceDescription
                                {
                                    NodeId = roleId,
                                    BrowseName = new QualifiedName("Operator"),
                                    TypeDefinition = ObjectTypeIds.RoleType
                                },
                                new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId(999u, "urn:not-in-table"),
                                    BrowseName = new QualifiedName("Remote"),
                                    TypeDefinition = ObjectTypeIds.RoleType
                                },
                                new ReferenceDescription
                                {
                                    NodeId = new NodeId(6002),
                                    BrowseName = new QualifiedName("Folder"),
                                    TypeDefinition = ObjectTypeIds.FolderType
                                }
                            ]
                        }
                    ],
                    DiagnosticInfos = []
                }));
            m_session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<BrowsePath>>(paths => paths.Count == 6),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            ResultFor(identitiesId),
                            ResultFor(applicationsId),
                            ResultFor(applicationsExcludeId),
                            ResultFor(endpointsId),
                            ResultFor(endpointsExcludeId),
                            ResultFor(customConfigurationId)
                        ],
                        DiagnosticInfos = []
                    }));
            m_session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    0,
                    TimestampsToReturn.Neither,
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new DataValue(Variant.From(new QualifiedName("Operator"))),
                        new DataValue(Variant.FromStructure(new[]
                        {
                            new IdentityMappingRuleType
                            {
                                CriteriaType = IdentityCriteriaType.UserName,
                                Criteria = "alice"
                            }
                        }.ToArrayOf())),
                        new DataValue(Variant.From(applications)),
                        new DataValue(Variant.From(false)),
                        new DataValue(Variant.FromStructure(new[]
                        {
                            new EndpointType { EndpointUrl = "opc.tcp://localhost:4840" }
                        }.ToArrayOf())),
                        new DataValue(Variant.From(false)),
                        new DataValue(Variant.From(true))
                    ],
                    DiagnosticInfos = []
                }));

            var roles = await m_client.ListRolesAsync().ConfigureAwait(false);

            Assert.That(roles, Has.Count.EqualTo(1));
            RoleInfo role = roles[0];
            Assert.That(role.RoleId, Is.EqualTo(roleId));
            Assert.That(role.BrowseName.Name, Is.EqualTo("Operator"));
            Assert.That(role.Identities, Has.Count.EqualTo(1));
            Assert.That(role.Applications, Has.Count.EqualTo(1));
            Assert.That(role.Applications[0], Is.EqualTo("urn:app"));
            Assert.That(role.ApplicationsExclude, Is.False);
            Assert.That(role.Endpoints, Has.Count.EqualTo(1));
            Assert.That(role.EndpointsExclude, Is.False);
            Assert.That(role.CustomConfiguration, Is.True);
        }

        [Test]
        public void AddRoleAsyncThrowsWhenServerDoesNotReturnNodeId()
        {
            m_session.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = []
                        }
                    ],
                    DiagnosticInfos = []
                }));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.AddRoleAsync("Role").ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ResolveChildFailuresSurfaceAsServiceResultExceptions()
        {
            m_session.SetupSequence(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new BrowsePathResult { StatusCode = StatusCodes.BadNodeIdUnknown }],
                        DiagnosticInfos = []
                    }))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new BrowsePathResult { StatusCode = StatusCodes.Good, Targets = [] }],
                        DiagnosticInfos = []
                    }));

            ServiceResultException badStatus = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.AddApplicationAsync(new NodeId(1), "urn:app").ConfigureAwait(false));
            ServiceResultException notFound = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.AddApplicationAsync(new NodeId(1), "urn:app").ConfigureAwait(false));

            Assert.That(badStatus.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(notFound.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void WritePropertyAsyncThrowsWhenWriteResultIsBad()
        {
            m_session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [ResultFor(new NodeId(5001))],
                        DiagnosticInfos = []
                    }));
            m_session.Setup(s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WriteResponse>(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [StatusCodes.BadUserAccessDenied],
                    DiagnosticInfos = []
                }));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.SetApplicationsExcludeAsync(new NodeId(1), true).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        private static BrowsePathResult ResultFor(NodeId nodeId)
        {
            return new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = [new BrowsePathTarget { TargetId = nodeId, RemainingPathIndex = uint.MaxValue }]
            };
        }

        private static readonly string[] s_applications = ["urn:app"];
    }
}
