// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the GetEndpoints load-direction peer endpoint directory
    /// (<see cref="SharedPeerEndpointPublisher"/> / <see cref="SharedPeerEndpointDirectory"/>).
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class PeerEndpointDirectoryTests
    {
        [Test]
        public async Task ProtectedPeerEndpointsRejectAnotherPeerKeyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var random = RandomNumberGenerator.Create();
            byte[] key = new byte[32];
            random.GetBytes(key);
            using var protector = new AesCbcHmacRecordProtector(key);
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions
            {
                EndpointKeyPrefix = "deployment/endpoints/",
                PeerApplicationUris = ["urn:server:a", "urn:server:b"]
            };
            var publisher = new SharedPeerEndpointPublisher(
                store, context, protector, options, "urn:server:a");
            await publisher.PublishAsync(
                [Endpoint("opc.tcp://a:4840", MessageSecurityMode.None, "urn:server:a")]).ConfigureAwait(false);
            (bool found, ByteString record) = await store.TryGetAsync(
                options.EndpointKeyPrefix + "urn:server:a").ConfigureAwait(false);
            Assert.That(found, Is.True);
            await store.SetAsync(options.EndpointKeyPrefix + "urn:server:b", record).ConfigureAwait(false);
            var directory = new SharedPeerEndpointDirectory(store, context, protector, options);

            ArrayOf<EndpointDescription> other = await directory.GetEndpointsAsync("urn:server:b")
                .ConfigureAwait(false);
            Assert.That(other.Count, Is.Zero);
            ArrayOf<EndpointDescription> original = await directory.GetEndpointsAsync("urn:server:a")
                .ConfigureAwait(false);
            Assert.That(original, Has.Count.EqualTo(1));
            Assert.That(original[0].EndpointUrl, Is.EqualTo("opc.tcp://a:4840"));
        }

        [Test]
        public async Task PublishAndReadRoundTripReturnsEndpointsAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = ["urn:server:a"] };

            var publisher = new SharedPeerEndpointPublisher(
                store, context, NullRecordProtector.Instance, options, "urn:server:a");
            ArrayOf<EndpointDescription> published =
            [
                Endpoint("opc.tcp://a:4840", MessageSecurityMode.None, "urn:server:a"),
                Endpoint("opc.tcp://a:4841", MessageSecurityMode.SignAndEncrypt, "urn:server:a")
            ];
            await publisher.PublishAsync(published).ConfigureAwait(false);

            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options);
            EndpointDescription[] resolved = (await directory.GetEndpointsAsync("urn:server:a").ConfigureAwait(false)).ToArray()!;

            Assert.That(resolved, Has.Length.EqualTo(2));
            Assert.That(resolved[0].EndpointUrl, Is.EqualTo("opc.tcp://a:4840"));
            Assert.That(resolved[1].EndpointUrl, Is.EqualTo("opc.tcp://a:4841"));
            Assert.That(resolved[1].SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        }

        [Test]
        public async Task UnconfiguredPeerReturnsEmptyAsync()
        {
            const string serverUri = "urn:server:b";
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = ["urn:server:a"] };
            var publisher = new SharedPeerEndpointPublisher(
                store, context, NullRecordProtector.Instance, options, serverUri);
            await publisher.PublishAsync(
                [Endpoint("opc.tcp://b:4840", MessageSecurityMode.None, serverUri)]).ConfigureAwait(false);

            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options);
            ArrayOf<EndpointDescription> resolved = await directory.GetEndpointsAsync(serverUri).ConfigureAwait(false);

            Assert.That(resolved.Count, Is.Zero);
        }

        [Test]
        public async Task EmptyServerUriReturnsEmptyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = ["urn:server:a"] };

            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options);
            EndpointDescription[] resolved = (await directory.GetEndpointsAsync(string.Empty).ConfigureAwait(false)).ToArray()!;

            Assert.That(resolved, Is.Empty);
        }

        [Test]
        public async Task MalformedRecordReturnsEmptyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = ["urn:server:a"] };
            await store.SetAsync("endpoint/urn:server:a", ByteString.From(new byte[] { 7, 7, 7 })).ConfigureAwait(false);

            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options);
            EndpointDescription[] resolved = (await directory.GetEndpointsAsync("urn:server:a").ConfigureAwait(false)).ToArray()!;

            Assert.That(resolved, Is.Empty, "an undecodable endpoint record must be dropped (fail-closed)");
        }

        [Test]
        public async Task DirectoryRejectsMixedEndpointIdentitiesAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var random = RandomNumberGenerator.Create();
            byte[] key = new byte[32];
            random.GetBytes(key);
            using var protector = new AesCbcHmacRecordProtector(key);
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = ["urn:server:a", "urn:server:b"] };
            var publisher = new SharedPeerEndpointPublisher(store, context, protector, options, "urn:server:a");
            var directory = new SharedPeerEndpointDirectory(store, context, protector, options);
            await publisher.PublishAsync(
                [
                    Endpoint("opc.tcp://a:4840", MessageSecurityMode.None, "urn:server:a"),
                    Endpoint("opc.tcp://b:4840", MessageSecurityMode.None, "urn:server:b")
                ]).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await directory.GetEndpointsAsync("urn:server:a")
                .ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.Zero);
        }

        [TestCase(-1L, false)]
        [TestCase(0L, true)]
        [TestCase(TimeSpan.TicksPerSecond, true)]
        [TestCase(TimeSpan.TicksPerSecond + 1, false)]
        public async Task DirectoryEnforcesEndpointFreshnessAsync(long ageTicks, bool expected)
        {
            const string serverUri = "urn:server:a";
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions
            {
                PeerApplicationUris = [serverUri],
                StalenessWindow = TimeSpan.FromSeconds(1)
            };
            var clock = new FakeTimeProvider();
            var publishedAt = new FakeTimeProvider(clock.GetUtcNow().AddTicks(-ageTicks));
            var publisher = new SharedPeerEndpointPublisher(
                store, context, NullRecordProtector.Instance, options, serverUri, publishedAt);
            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options, clock);
            await publisher.PublishAsync(
                [Endpoint("opc.tcp://a:4840", MessageSecurityMode.None, serverUri)]).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await directory.GetEndpointsAsync(serverUri).ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.EqualTo(expected ? 1 : 0));
        }

        private static EndpointDescription Endpoint(string url, MessageSecurityMode mode, string serverUri)
        {
            return new EndpointDescription
            {
                EndpointUrl = url,
                SecurityMode = mode,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription
                {
                    ApplicationUri = serverUri,
                    ApplicationType = ApplicationType.Server
                }
            };
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }
    }
}
