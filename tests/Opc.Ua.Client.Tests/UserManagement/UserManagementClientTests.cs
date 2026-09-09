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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.UserManagement;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.UserManagement
{
    /// <summary>
    /// Verifies the public user-list snapshot contract.
    /// </summary>
    [TestFixture]
    public sealed class UserManagementClientTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ListUsersReturnsArrayOfSnapshotsAsync(bool throughInterface)
        {
            ArrayOf<UserManagementDataType> raw =
            [
                new UserManagementDataType
                {
                    UserName = "operator",
                    UserConfiguration = (uint)(UserConfigurationMask.NoDelete | UserConfigurationMask.Disabled),
                    Description = "Operator account"
                },
                new UserManagementDataType
                {
                    UserName = string.Empty,
                    UserConfiguration = (uint)(
                        UserConfigurationMask.NoChangeByUser | UserConfigurationMask.MustChangePassword),
                    Description = string.Empty
                }
            ];
            Mock<ISession> session = CreateSession(new DataValue(Variant.FromStructure(raw)));
            var client = new UserManagementClient(session.Object);

            ArrayOf<UserManagementUser> users = throughInterface
                ? await ((IUserManagementClient)client).ListUsersAsync().ConfigureAwait(false)
                : await client.ListUsersAsync().ConfigureAwait(false);

            Assert.That(users.IsNull, Is.False);
            Assert.That(users.Count, Is.EqualTo(2));
            Assert.That(users[0].UserName, Is.EqualTo("operator"));
            Assert.That(users[0].Description, Is.EqualTo("Operator account"));
            Assert.That(users[0].UserConfiguration,
                Is.EqualTo(UserConfigurationMask.NoDelete | UserConfigurationMask.Disabled));
            Assert.That(users[0].NoDelete, Is.True);
            Assert.That(users[0].IsDisabled, Is.True);
            Assert.That(users[0].IsActive, Is.False);
            Assert.That(users[1].UserName, Is.Empty);
            Assert.That(users[1].Description, Is.Null);
            Assert.That(users[1].NoChangeByUser, Is.True);
            Assert.That(users[1].MustChangePassword, Is.True);
            Assert.That(users[1].IsActive, Is.True);
            session.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(),
                0,
                TimestampsToReturn.Neither,
                It.Is<ArrayOf<ReadValueId>>(ids =>
                    ids.Count == 1 && ids[0].NodeId == s_propertyId && ids[0].AttributeId == Attributes.Value),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ListUsersReturnsEmptyArrayAsync(bool missingValue)
        {
            Variant value = missingValue
                ? default
                : Variant.FromStructure(ArrayOf<UserManagementDataType>.Empty);
            Mock<ISession> session = CreateSession(new DataValue(value));
            var client = new UserManagementClient(session.Object);

            ArrayOf<UserManagementUser> users = await client.ListUsersAsync().ConfigureAwait(false);

            Assert.That(users.IsNull, Is.False);
            Assert.That(users.Count, Is.Zero);
        }

        [Test]
        public async Task ListUsersPreservesBadReadStatusAsync()
        {
            Mock<ISession> session = CreateSession(DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied));
            var client = new UserManagementClient(session.Object);

            await Assert.ThatAsync(
                async () =>
                {
                    await client.ListUsersAsync().ConfigureAwait(false);
                },
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
        }

        [Test]
        public async Task ReadPasswordLengthReturnsServerRangeAsync()
        {
            Mock<ISession> session = CreateSession(new DataValue(Variant.FromStructure(new Range(128, 8))));
            var client = new UserManagementClient(session.Object);

            Range range = await client.ReadPasswordLengthAsync().ConfigureAwait(false);

            Assert.That(range, Is.Not.Null);
            Assert.That(range.High, Is.EqualTo(128));
            Assert.That(range.Low, Is.EqualTo(8));
            session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(),
                It.Is<ArrayOf<BrowsePath>>(paths =>
                    paths.Count == 1 &&
                    paths[0].StartingNode == new NodeId(Objects.UserManagement) &&
                    paths[0].RelativePath.Elements.Count == 1 &&
                    paths[0].RelativePath.Elements[0].TargetName == new QualifiedName(BrowseNames.PasswordLength)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadPasswordLengthReturnsDefaultForMissingStructureAsync(bool unrelatedValue)
        {
            Variant value = unrelatedValue ? Variant.From(42) : default;
            Mock<ISession> session = CreateSession(new DataValue(value));
            var client = new UserManagementClient(session.Object);

            Range range = await client.ReadPasswordLengthAsync().ConfigureAwait(false);

            Assert.That(range, Is.Not.Null);
            Assert.That(range.High, Is.Zero);
            Assert.That(range.Low, Is.Zero);
        }

        [Test]
        public async Task ReadPasswordLengthPreservesBadReadStatusAsync()
        {
            Mock<ISession> session = CreateSession(DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied));
            var client = new UserManagementClient(session.Object);

            await Assert.ThatAsync(
                async () =>
                {
                    await client.ReadPasswordLengthAsync().ConfigureAwait(false);
                },
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
        }

        private static Mock<ISession> CreateSession(in DataValue value)
        {
            var session = new Mock<ISession>(MockBehavior.Loose);
            session.SetupGet(s => s.MessageContext).Returns(
                ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results =
                    [
                        new BrowsePathResult
                        {
                            StatusCode = StatusCodes.Good,
                            Targets =
                            [
                                new BrowsePathTarget
                                {
                                    TargetId = new ExpandedNodeId(s_propertyId),
                                    RemainingPathIndex = uint.MaxValue
                                }
                            ]
                        }
                    ]
                });
            var response = new ReadResponse { Results = [value] };
            session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
            return session;
        }

        private static readonly NodeId s_propertyId = new("UserManagement.Property", 1);
    }
}
