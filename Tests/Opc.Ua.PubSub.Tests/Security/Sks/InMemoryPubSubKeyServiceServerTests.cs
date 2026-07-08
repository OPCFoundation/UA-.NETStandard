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
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Tests.Security.Sks
{
    /// <summary>
    /// Tests for <see cref="InMemoryPubSubKeyServiceServer"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("8.3.1")]
    [TestSpec("8.3.2")]
    public class InMemoryPubSubKeyServiceServerTests
    {
        private const string CallerId = "client/cn=test";

        private static SksSecurityGroup BuildGroup(
            string id = "group-1",
            string policyUri = PubSubSecurityPolicyUri.PubSubAes128Ctr,
            int maxFuture = 4,
            int maxPast = 2,
            string[]? authorizedCallerIdentities = null)
        {
            return new SksSecurityGroup(
                id,
                policyUri,
                TimeSpan.FromMinutes(5),
                maxFuture,
                maxPast,
                Array.Empty<PubSubSecurityKey>(),
                authorizedCallerIdentities ?? [CallerId]);
        }

        [Test]
        public async Task AddSecurityGroup_ThenGetSecurityGroup_RoundTrips()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup()).ConfigureAwait(false);
            SksSecurityGroup? roundTrip = await server.GetSecurityGroupAsync("group-1").ConfigureAwait(false);
            Assert.That(roundTrip, Is.Not.Null);
            Assert.That(roundTrip!.SecurityGroupId, Is.EqualTo("group-1"));
            Assert.That(roundTrip.SecurityPolicyUri, Is.EqualTo(PubSubSecurityPolicyUri.PubSubAes128Ctr));
            Assert.That(roundTrip.Keys.IsEmpty, Is.False);
            Assert.That(((string[]?)server.SecurityGroupIds) ?? [], Has.Member("group-1"));
        }

        [Test]
        [TestSpec("8.3.2", Summary = "GetSecurityKeys honors granted RolePermissions")]
        public async Task GetSecurityKeysAllowsCallerWithGrantedRolePermission()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(new SksSecurityGroup(
                "role-group",
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(5),
                1,
                1,
                Array.Empty<PubSubSecurityKey>(),
                rolePermissions:
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                        Permissions = (uint)PermissionType.Call
                    }
                ])).ConfigureAwait(false);

            SksKeyResponse response = await server.GetSecurityKeysAsync(
                "security-admin",
                new SksKeyRequest("role-group", 0, 1),
                [ObjectIds.WellKnownRole_SecurityAdmin]).ConfigureAwait(false);

            Assert.That(response.Keys, Has.Count.EqualTo(1));
        }

        [Test]
        [TestSpec("8.3.2", Summary = "GetSecurityKeys allows anonymous only when RolePermissions grant it")]
        public async Task GetSecurityKeysAllowsAnonymousWhenAnonymousRoleGrantsCall()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(new SksSecurityGroup(
                "anonymous-group",
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(5),
                1,
                1,
                Array.Empty<PubSubSecurityKey>(),
                rolePermissions:
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Anonymous,
                        Permissions = (uint)PermissionType.Call
                    }
                ])).ConfigureAwait(false);

            SksKeyResponse response = await server.GetSecurityKeysAsync(
                string.Empty,
                new SksKeyRequest("anonymous-group", 0, 1)).ConfigureAwait(false);

            Assert.That(response.Keys, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task GetSecurityGroup_ReturnsNullForUnknownGroup()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            SksSecurityGroup? group = await server.GetSecurityGroupAsync("missing").ConfigureAwait(false);
            Assert.That(group, Is.Null);
        }

        [Test]
        public async Task GetSecurityKeysAsync_ReturnsRequestedKeyCount()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(maxFuture: 6)).ConfigureAwait(false);
            SksKeyResponse response = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 3U)).ConfigureAwait(false);
            Assert.That(((byte[][]?)response.Keys) ?? [], Has.Length.EqualTo(3));
            Assert.That(response.SecurityPolicyUri, Is.EqualTo(PubSubSecurityPolicyUri.PubSubAes128Ctr));
            Assert.That(response.KeyLifetime, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(response.FirstTokenId, Is.GreaterThan(0U));
        }

        [Test]
        [TestSpec("8.3.2", Part = 14)]
        public async Task AuthorizedCallerForGroupReceivesKeys()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(authorizedCallerIdentities: [CallerId])).ConfigureAwait(false);
            SksKeyResponse response = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 2U)).ConfigureAwait(false);
            Assert.That(((byte[][]?)response.Keys) ?? [], Has.Length.EqualTo(2));
            Assert.That(response.SecurityPolicyUri, Is.EqualTo(PubSubSecurityPolicyUri.PubSubAes128Ctr));
        }

        [Test]
        [TestSpec("8.3.2", Part = 14)]
        public async Task AuthenticatedUnauthorizedCallerForAnotherGroupIsDenied()
        {
            const string otherCallerId = "client/cn=other";
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(id: "group-1", authorizedCallerIdentities: [CallerId])).ConfigureAwait(false);
            await server.AddSecurityGroupAsync(BuildGroup(id: "group-2", authorizedCallerIdentities: [otherCallerId])).ConfigureAwait(false);
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.GetSecurityKeysAsync(
                    CallerId,
                    new SksKeyRequest("group-2", 0U, 1U)).ConfigureAwait(false))!;
            Assert.That(ex.Status.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("8.3.2", Part = 14)]
        public async Task SecurityGroupWithNoAuthorizedMembersDeniesAllRequests()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(authorizedCallerIdentities: [])).ConfigureAwait(false);
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.GetSecurityKeysAsync(
                    CallerId,
                    new SksKeyRequest("group-1", 0U, 1U)).ConfigureAwait(false))!;
            Assert.That(ex.Status.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task GetSecurityKeysAsync_RejectsEmptyCallerIdentity()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup()).ConfigureAwait(false);
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.GetSecurityKeysAsync(
                    string.Empty,
                    new SksKeyRequest("group-1", 0U, 1U)).ConfigureAwait(false))!;
            Assert.That(ex.Status.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task GetSecurityKeysAsync_RejectsUnknownGroup()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.GetSecurityKeysAsync(
                    CallerId,
                    new SksKeyRequest("missing", 0U, 1U)).ConfigureAwait(false))!;
            Assert.That(ex.Status.Code, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task AddSecurityGroupAsync_RejectsDuplicate()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup()).ConfigureAwait(false);
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.AddSecurityGroupAsync(BuildGroup()).ConfigureAwait(false))!;
            Assert.That(ex.Status.Code, Is.EqualTo(StatusCodes.BadAlreadyExists));
        }

        [Test]
        public void AddSecurityGroupAsync_RejectsUnsupportedPolicy()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.AddSecurityGroupAsync(
                    BuildGroup(policyUri: "http://example.org/UnsupportedPolicy")).ConfigureAwait(false))!;
            Assert.That(ex.Status.Code, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        [Test]
        public async Task RemoveSecurityGroupAsync_ThenGet_ReturnsNull()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup()).ConfigureAwait(false);
            await server.RemoveSecurityGroupAsync("group-1").ConfigureAwait(false);
            Assert.That(await server.GetSecurityGroupAsync("group-1").ConfigureAwait(false), Is.Null);
            Assert.That(((string[]?)server.SecurityGroupIds) ?? [], Does.Not.Contain("group-1"));
        }

        [Test]
        public void RemoveSecurityGroupAsync_RejectsUnknownGroup()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.RemoveSecurityGroupAsync("missing").ConfigureAwait(false))!;
            Assert.That(ex.Status.Code, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task GetSecurityKeysAsync_GeneratesAdditionalKeysWhenRequested()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(maxFuture: 8)).ConfigureAwait(false);
            SksKeyResponse first = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 2U)).ConfigureAwait(false);
            SksKeyResponse second = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 6U)).ConfigureAwait(false);
            Assert.That(((byte[][]?)second.Keys) ?? [], Has.Length.EqualTo(6));
            Assert.That(second.FirstTokenId, Is.EqualTo(first.FirstTokenId));
        }

        [Test]
        public async Task GetSecurityKeysAsync_HonorsExplicitStartingTokenId()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(maxFuture: 8)).ConfigureAwait(false);
            SksKeyResponse all = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 5U)).ConfigureAwait(false);
            uint pickStart = all.FirstTokenId + 2u;
            SksKeyResponse subset = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", pickStart, 2U)).ConfigureAwait(false);
            Assert.That(subset.FirstTokenId, Is.EqualTo(pickStart));
            Assert.That(((byte[][]?)subset.Keys) ?? [], Has.Length.EqualTo(2));
        }

        [Test]
        [TestSpec("8.3.2", Part = 14, Summary = "RolePermissions grant GetSecurityKeys Call access")]
        public async Task RolePermissionsGrantAuthenticatedCallerAccess()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(new SksSecurityGroup(
                "role-group",
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(5),
                2,
                1,
                Array.Empty<PubSubSecurityKey>(),
                rolePermissions: [new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)PermissionType.Call
                }])).ConfigureAwait(false);

            SksKeyResponse response = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("role-group", 0U, 1U),
                [ObjectIds.WellKnownRole_AuthenticatedUser]).ConfigureAwait(false);

            Assert.That(response.Keys, Has.Count.EqualTo(1));
        }

        [Test]
        [TestSpec("8.4.2", Part = 14, Summary = "InvalidateKeys revokes current and future keys")]
        public async Task InvalidateKeysAdvancesBeyondInvalidatedFutureKeys()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(maxFuture: 3)).ConfigureAwait(false);
            SksKeyResponse before = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 4U)).ConfigureAwait(false);

            await server.InvalidateKeysAsync("group-1").ConfigureAwait(false);
            SksKeyResponse after = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 1U)).ConfigureAwait(false);

            Assert.That(after.FirstTokenId, Is.GreaterThan(before.FirstTokenId + 3U));
        }

        [Test]
        [TestSpec("8.4.3", Part = 14, Summary = "ForceKeyRotation promotes the next key")]
        public async Task ForceKeyRotationPromotesNextFutureKey()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(maxFuture: 3)).ConfigureAwait(false);
            SksKeyResponse before = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 2U)).ConfigureAwait(false);

            await server.ForceKeyRotationAsync("group-1").ConfigureAwait(false);
            SksKeyResponse after = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 1U)).ConfigureAwait(false);

            Assert.That(after.FirstTokenId, Is.EqualTo(before.FirstTokenId + 1U));
        }

        [Test]
        [TestSpec("8.4.1", Part = 14, Summary = "MaxPastKeyCount bounds retained past keys")]
        public async Task ForceKeyRotationPrunesPastKeysToMaxPastKeyCount()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(maxFuture: 4, maxPast: 1)).ConfigureAwait(false);

            await server.ForceKeyRotationAsync("group-1").ConfigureAwait(false);
            await server.ForceKeyRotationAsync("group-1").ConfigureAwait(false);
            SksSecurityGroup? group = await server.GetSecurityGroupAsync("group-1").ConfigureAwait(false);

            Assert.That(group, Is.Not.Null);
            Assert.That(group!.Keys.Count, Is.LessThanOrEqualTo(group.MaxFutureKeyCount + group.MaxPastKeyCount + 1));
        }

        [Test]
        public void Constructor_AcceptsNullDependencies()
        {
            Assert.That(() => new InMemoryPubSubKeyServiceServer(), Throws.Nothing);
        }

        [Test]
        public void GetSecurityKeysAsync_RejectsEmptySecurityGroupId()
        {
            var server = new InMemoryPubSubKeyServiceServer();
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.GetSecurityKeysAsync(
                    CallerId,
                    new SksKeyRequest(string.Empty, 0U, 1U)).ConfigureAwait(false))!;
            Assert.That(ex.Status.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void RemoveSecurityGroupAsync_RejectsEmptyGroupId()
        {
            var server = new InMemoryPubSubKeyServiceServer();
            Assert.That(
                async () => await server.RemoveSecurityGroupAsync(string.Empty).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void GetSecurityGroupAsync_RejectsEmptyGroupId()
        {
            var server = new InMemoryPubSubKeyServiceServer();
            Assert.That(
                async () => await server.GetSecurityGroupAsync(string.Empty).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void AddSecurityGroupAsync_RejectsNullGroup()
        {
            var server = new InMemoryPubSubKeyServiceServer();
            Assert.That(
                async () => await server.AddSecurityGroupAsync(null!).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
