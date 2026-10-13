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
using Opc.Ua.Client.Roles;

namespace Opc.Ua.Client.Tests.Roles
{
    /// <summary>
    /// Verifies the opt-in allocation bounds on role mapping snapshots.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RoleManagementClientLimitTests
    {
        /// <summary>
        /// Each mapping collection accepts zero/unbounded, exact and empty boundaries.
        /// </summary>
        [TestCase(0, 0, 3)]
        [TestCase(0, 3, 3)]
        [TestCase(0, 1, 0)]
        [TestCase(1, 0, 3)]
        [TestCase(1, 3, 3)]
        [TestCase(1, 1, 0)]
        [TestCase(2, 0, 3)]
        [TestCase(2, 3, 3)]
        [TestCase(2, 1, 0)]
        public async Task MappingLimitAcceptsZeroExactAndEmpty(int kind, int limit, int count)
        {
            Mock<ISession> session = CreateSession(kind, count);
            var client = new RoleManagementClient(session.Object, limit);
            RoleInfo role = await client.ReadRoleAsync(new NodeId(12u)).ConfigureAwait(false);
            Assert.That(kind switch
            {
                0 => role.Identities.Count,
                1 => role.Applications.Count,
                _ => role.Endpoints.Count
            }, Is.EqualTo(count));
            if (count > 0)
            {
                Assert.That(kind switch
                {
                    0 => role.Identities[0].Criteria,
                    1 => role.Applications[0],
                    _ => role.Endpoints[0].EndpointUrl
                }, Is.EqualTo("entry-0"));
            }
        }

        /// <summary>
        /// The first oversized mapping is rejected before a role snapshot can be returned.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void MappingLimitRejectsFirstExcessEntry(int kind)
        {
            Mock<ISession> session = CreateSession(kind, 3);
            var client = new RoleManagementClient(session.Object, 2);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadRoleAsync(new NodeId(12u)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// A bad mapping value keeps the legacy empty mapping instead of decoding its payload.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task MappingLimitPreservesBadValueHandling(int kind)
        {
            Mock<ISession> session = CreateSession(kind, 3, StatusCodes.BadUserAccessDenied);
            var client = new RoleManagementClient(session.Object, 1);
            RoleInfo role = await client.ReadRoleAsync(new NodeId(12u)).ConfigureAwait(false);
            Assert.That(role.Identities, Is.Empty);
            Assert.That(role.Applications, Is.Empty);
            Assert.That(role.Endpoints, Is.Empty);
        }

        /// <summary>
        /// Negative caps fail before using the session.
        /// </summary>
        [Test]
        public void NegativeMappingLimitIsRejected()
        {
            var session = new Mock<ISession>();
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoleManagementClient(session.Object, -1));
            session.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Models one resolved mapping property and the role's BrowseName.
        /// </summary>
        private static Mock<ISession> CreateSession(int kind, int count, StatusCode status = default)
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(new NamespaceTable());
            int property = kind == 0 ? 0 : kind == 1 ? 1 : 3;
            BrowsePathResult[] paths = [.. Enumerable.Range(0, 6)
                .Select(i => new BrowsePathResult
                {
                    StatusCode = i == property ? StatusCodes.Good : StatusCodes.BadNoMatch,
                    Targets = i == property
                        ?
                        [
                            new BrowsePathTarget
                            {
                                TargetId = new ExpandedNodeId(20u),
                                RemainingPathIndex = uint.MaxValue
                            }
                        ]
                        : []
                })];
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse { Results = paths });
            Variant mapping = kind switch
            {
                0 => Variant.FromStructure(Enumerable.Range(0, count)
                    .Select(i => new IdentityMappingRuleType { Criteria = $"entry-{i}" }).ToArray().ToArrayOf()),
                1 => Variant.From(Enumerable.Range(0, count).Select(i => $"entry-{i}").ToArray().ToArrayOf()),
                _ => Variant.FromStructure(Enumerable.Range(0, count)
                    .Select(i => new EndpointType { EndpointUrl = $"entry-{i}" }).ToArray().ToArrayOf())
            };
            session.Setup(value => value.ReadAsync(
                It.IsAny<RequestHeader>(), 0, TimestampsToReturn.Neither,
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(Variant.From(new QualifiedName("Role"))),
                        new DataValue(mapping, status)
                    ]
                });
            return session;
        }
    }
}
