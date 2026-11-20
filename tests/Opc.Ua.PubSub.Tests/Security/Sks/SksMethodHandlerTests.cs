// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Security.Sks
{
    /// <summary>
    /// Tests for <see cref="SksMethodHandler"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("8.3.2")]
    public class SksMethodHandlerTests
    {
        private static SystemContext BuildContext(string? userId)
        {
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                UserId = userId
            };
        }

        private static SksMethodHandler CreateHandler(InMemoryPubSubKeyServiceServer server)
        {
            return new SksMethodHandler(server, NUnitTelemetryContext.Create());
        }

        private static async Task<InMemoryPubSubKeyServiceServer> CreateServerWithGroupAsync(
            string id = "group-1",
            string[]? authorizedCallerIdentities = null)
        {
            var server = new InMemoryPubSubKeyServiceServer();
            await server.AddSecurityGroupAsync(
                new SksSecurityGroup(
                    id,
                    PubSubSecurityPolicyUri.PubSubAes128Ctr,
                    TimeSpan.FromMinutes(5),
                    4,
                    2,
                    Array.Empty<PubSubSecurityKey>(),
                    authorizedCallerIdentities ?? ["user1"])).ConfigureAwait(false);
            return server;
        }

        [Test]
        public async Task HandleGetSecurityKeys_ReturnsGoodAndPopulatesOutputs()
        {
            InMemoryPubSubKeyServiceServer server = await CreateServerWithGroupAsync().ConfigureAwait(false);
            SksMethodHandler handler = CreateHandler(server);
            SystemContext ctx = BuildContext("user1");
            var inputs = new List<Variant>
            {
                Variant.From("group-1"),
                Variant.From(0U),
                Variant.From(2U)
            };
            var outputs = new List<Variant>();

            ServiceResult result = handler.HandleGetSecurityKeys(
                ctx,
                ObjectIds.PublishSubscribe,
                inputs,
                outputs);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(outputs, Has.Count.EqualTo(5));
            Assert.That(outputs[0].TryGetValue(out string? policyUri), Is.True);
            Assert.That(policyUri, Is.EqualTo(PubSubSecurityPolicyUri.PubSubAes128Ctr));
            Assert.That(outputs[1].TryGetValue(out uint firstTokenId), Is.True);
            Assert.That(firstTokenId, Is.GreaterThan(0U));
            Assert.That(outputs[2].TryGetValue(out ArrayOf<ByteString> keys), Is.True);
            Assert.That(keys, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task HandleGetSecurityKeys_ReturnsBadInvalidArgumentForFewArgs()
        {
            InMemoryPubSubKeyServiceServer server = await CreateServerWithGroupAsync().ConfigureAwait(false);
            SksMethodHandler handler = CreateHandler(server);
            SystemContext ctx = BuildContext("user1");
            var outputs = new List<Variant>();
            ServiceResult result = handler.HandleGetSecurityKeys(
                ctx,
                ObjectIds.PublishSubscribe,
                [Variant.From("group-1")],
                outputs);
            Assert.That(
                result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(outputs, Is.Empty);
        }

        [Test]
        public async Task HandleGetSecurityKeys_ReturnsBadInvalidArgumentForWrongTypes()
        {
            InMemoryPubSubKeyServiceServer server = await CreateServerWithGroupAsync().ConfigureAwait(false);
            SksMethodHandler handler = CreateHandler(server);
            SystemContext ctx = BuildContext("user1");
            var inputs = new List<Variant>
            {
                Variant.From("group-1"),
                Variant.From("not-a-uint"),
                Variant.From(2U)
            };
            ServiceResult result = handler.HandleGetSecurityKeys(
                ctx,
                ObjectIds.PublishSubscribe,
                inputs,
                []);
            Assert.That(
                result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task HandleGetSecurityKeys_ReturnsBadInvalidArgumentForEmptyGroupId()
        {
            InMemoryPubSubKeyServiceServer server = await CreateServerWithGroupAsync().ConfigureAwait(false);
            SksMethodHandler handler = CreateHandler(server);
            SystemContext ctx = BuildContext("user1");
            var inputs = new List<Variant>
            {
                Variant.From(string.Empty),
                Variant.From(0U),
                Variant.From(1U)
            };
            ServiceResult result = handler.HandleGetSecurityKeys(
                ctx,
                ObjectIds.PublishSubscribe,
                inputs,
                []);
            Assert.That(
                result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task HandleGetSecurityKeys_SurfacesUnknownGroupAsBadNotFound()
        {
            var server = new InMemoryPubSubKeyServiceServer();
            SksMethodHandler handler = CreateHandler(server);
            SystemContext ctx = BuildContext("user1");
            var inputs = new List<Variant>
            {
                Variant.From("missing"),
                Variant.From(0U),
                Variant.From(1U)
            };
            ServiceResult result = handler.HandleGetSecurityKeys(
                ctx,
                ObjectIds.PublishSubscribe,
                inputs,
                []);
            Assert.That(
                result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        [TestSpec("8.3.2", Part = 14)]
        public async Task HandleGetSecurityKeysForwardsCallerIdentityToAuthorization()
        {
            InMemoryPubSubKeyServiceServer server = await CreateServerWithGroupAsync(
                authorizedCallerIdentities: ["authorized-user"]).ConfigureAwait(false);
            SksMethodHandler handler = CreateHandler(server);
            SystemContext ctx = BuildContext("unauthorized-user");
            var inputs = new List<Variant>
            {
                Variant.From("group-1"),
                Variant.From(0U),
                Variant.From(1U)
            };
            ServiceResult result = handler.HandleGetSecurityKeys(
                ctx,
                ObjectIds.PublishSubscribe,
                inputs,
                []);
            Assert.That(
                result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task HandleGetSecurityKeys_RejectsAnonymousCallerWithBadUserAccessDenied()
        {
            InMemoryPubSubKeyServiceServer server = await CreateServerWithGroupAsync().ConfigureAwait(false);
            SksMethodHandler handler = CreateHandler(server);
            SystemContext ctx = BuildContext(userId: null);
            var inputs = new List<Variant>
            {
                Variant.From("group-1"),
                Variant.From(0U),
                Variant.From(1U)
            };
            ServiceResult result = handler.HandleGetSecurityKeys(
                ctx,
                ObjectIds.PublishSubscribe,
                inputs,
                []);
            Assert.That(
                result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void Constructor_RejectsNullKeyService()
        {
            Assert.That(
                () => new SksMethodHandler(null!, NUnitTelemetryContext.Create()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Constructor_RejectsNullTelemetry()
        {
            Assert.That(
                () => new SksMethodHandler(new InMemoryPubSubKeyServiceServer(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
