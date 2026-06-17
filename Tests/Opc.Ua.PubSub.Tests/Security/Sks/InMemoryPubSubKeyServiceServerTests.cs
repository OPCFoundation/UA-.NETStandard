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
            await server.AddSecurityGroupAsync(BuildGroup());
            SksSecurityGroup? roundTrip = await server.GetSecurityGroupAsync("group-1");
            Assert.That(roundTrip, Is.Not.Null);
            Assert.That(roundTrip!.SecurityGroupId, Is.EqualTo("group-1"));
            Assert.That(roundTrip.SecurityPolicyUri, Is.EqualTo(PubSubSecurityPolicyUri.PubSubAes128Ctr));
            Assert.That(roundTrip.Keys, Is.Not.Empty);
            Assert.That(server.SecurityGroupIds, Has.Member("group-1"));
        }

        [Test]
        public async Task GetSecurityGroup_ReturnsNullForUnknownGroup()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            SksSecurityGroup? group = await server.GetSecurityGroupAsync("missing");
            Assert.That(group, Is.Null);
        }

        [Test]
        public async Task GetSecurityKeysAsync_ReturnsRequestedKeyCount()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(maxFuture: 6));
            SksKeyResponse response = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 3U));
            Assert.That(response.Keys, Has.Count.EqualTo(3));
            Assert.That(response.SecurityPolicyUri, Is.EqualTo(PubSubSecurityPolicyUri.PubSubAes128Ctr));
            Assert.That(response.KeyLifetime, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(response.FirstTokenId, Is.GreaterThan(0U));
        }

        [Test]
        [TestSpec("8.3.2", Part = 14)]
        public async Task AuthorizedCallerForGroupReceivesKeys()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(authorizedCallerIdentities: [CallerId]));
            SksKeyResponse response = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 2U));
            Assert.That(response.Keys, Has.Count.EqualTo(2));
            Assert.That(response.SecurityPolicyUri, Is.EqualTo(PubSubSecurityPolicyUri.PubSubAes128Ctr));
        }

        [Test]
        [TestSpec("8.3.2", Part = 14)]
        public async Task AuthenticatedUnauthorizedCallerForAnotherGroupIsDenied()
        {
            const string otherCallerId = "client/cn=other";
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(id: "group-1", authorizedCallerIdentities: [CallerId]));
            await server.AddSecurityGroupAsync(BuildGroup(id: "group-2", authorizedCallerIdentities: [otherCallerId]));
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.GetSecurityKeysAsync(
                    CallerId,
                    new SksKeyRequest("group-2", 0U, 1U)))!;
            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("8.3.2", Part = 14)]
        public async Task SecurityGroupWithNoAuthorizedMembersDeniesAllRequests()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(authorizedCallerIdentities: []));
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.GetSecurityKeysAsync(
                    CallerId,
                    new SksKeyRequest("group-1", 0U, 1U)))!;
            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task GetSecurityKeysAsync_RejectsEmptyCallerIdentity()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup());
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.GetSecurityKeysAsync(
                    string.Empty,
                    new SksKeyRequest("group-1", 0U, 1U)))!;
            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task GetSecurityKeysAsync_RejectsUnknownGroup()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.GetSecurityKeysAsync(
                    CallerId,
                    new SksKeyRequest("missing", 0U, 1U)))!;
            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task AddSecurityGroupAsync_RejectsDuplicate()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup());
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.AddSecurityGroupAsync(BuildGroup()))!;
            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadAlreadyExists));
        }

        [Test]
        public void AddSecurityGroupAsync_RejectsUnsupportedPolicy()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.AddSecurityGroupAsync(
                    BuildGroup(policyUri: "http://example.org/UnsupportedPolicy")))!;
            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        [Test]
        public async Task RemoveSecurityGroupAsync_ThenGet_ReturnsNull()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup());
            await server.RemoveSecurityGroupAsync("group-1");
            Assert.That(await server.GetSecurityGroupAsync("group-1"), Is.Null);
            Assert.That(server.SecurityGroupIds, Does.Not.Contain("group-1"));
        }

        [Test]
        public void RemoveSecurityGroupAsync_RejectsUnknownGroup()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server.RemoveSecurityGroupAsync("missing"))!;
            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task GetSecurityKeysAsync_GeneratesAdditionalKeysWhenRequested()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(maxFuture: 8));
            SksKeyResponse first = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 2U));
            SksKeyResponse second = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 6U));
            Assert.That(second.Keys, Has.Count.EqualTo(6));
            Assert.That(second.FirstTokenId, Is.EqualTo(first.FirstTokenId));
        }

        [Test]
        public async Task GetSecurityKeysAsync_HonorsExplicitStartingTokenId()
        {
            var server = new InMemoryPubSubKeyServiceServer(new FakeTimeProvider());
            await server.AddSecurityGroupAsync(BuildGroup(maxFuture: 8));
            SksKeyResponse all = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", 0U, 5U));
            uint pickStart = all.FirstTokenId + 2u;
            SksKeyResponse subset = await server.GetSecurityKeysAsync(
                CallerId,
                new SksKeyRequest("group-1", pickStart, 2U));
            Assert.That(subset.FirstTokenId, Is.EqualTo(pickStart));
            Assert.That(subset.Keys, Has.Count.EqualTo(2));
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
                    new SksKeyRequest(string.Empty, 0U, 1U)))!;
            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void RemoveSecurityGroupAsync_RejectsEmptyGroupId()
        {
            var server = new InMemoryPubSubKeyServiceServer();
            Assert.That(
                async () => await server.RemoveSecurityGroupAsync(string.Empty),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void GetSecurityGroupAsync_RejectsEmptyGroupId()
        {
            var server = new InMemoryPubSubKeyServiceServer();
            Assert.That(
                async () => await server.GetSecurityGroupAsync(string.Empty),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void AddSecurityGroupAsync_RejectsNullGroup()
        {
            var server = new InMemoryPubSubKeyServiceServer();
            Assert.That(
                async () => await server.AddSecurityGroupAsync(null!),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
