// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.Security.Cryptography;
using System.Threading.Tasks;
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
            var options = new LoadDirectionOptions { EndpointKeyPrefix = "deployment/endpoints/" };
            var publisher = new SharedPeerEndpointPublisher(
                store, context, protector, options, "urn:server:a");
            await publisher.PublishAsync(
                [Endpoint("opc.tcp://a:4840", MessageSecurityMode.None, "urn:server:a")]).ConfigureAwait(false);
            (bool found, ByteString record) = await store.TryGetAsync(
                options.EndpointKeyPrefix + "urn:server:a").ConfigureAwait(false);
            Assert.That(found, Is.True);
            await store.SetAsync(options.EndpointKeyPrefix + "urn:server:b", record).ConfigureAwait(false);
            var directory = new SharedPeerEndpointDirectory(store, context, protector, options);

            Assert.That(await directory.GetEndpointsAsync("urn:server:b").ConfigureAwait(false), Is.Empty);
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
            var options = new LoadDirectionOptions();

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
        public async Task UnknownPeerReturnsEmptyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions();

            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options);
            EndpointDescription[] resolved = (await directory.GetEndpointsAsync("urn:server:missing").ConfigureAwait(false)).ToArray()!;

            Assert.That(resolved, Is.Empty);
        }

        [Test]
        public async Task EmptyServerUriReturnsEmptyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions();

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
            var options = new LoadDirectionOptions();
            await store.SetAsync("endpoint/urn:server:a", ByteString.From(new byte[] { 7, 7, 7 })).ConfigureAwait(false);

            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options);
            EndpointDescription[] resolved = (await directory.GetEndpointsAsync("urn:server:a").ConfigureAwait(false)).ToArray()!;

            Assert.That(resolved, Is.Empty, "an undecodable endpoint record must be dropped (fail-closed)");
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
