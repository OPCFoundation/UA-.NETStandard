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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// The bounds on the members of a redundant Server set the client takes,
    /// on their concurrent discovery, and on the caches of discovered
    /// endpoints.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ServerRedundancy")]
    public sealed class ServerRedundancyBoundsTests
    {
        [Test]
        public void OptionsRejectNonPositiveBounds()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ServerRedundancyOptions { MaxRedundantServers = 0 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ServerRedundancyOptions { MaxConcurrentPeerDiscoveries = 0 }.Validate());
            Assert.DoesNotThrow(() => new ServerRedundancyOptions().Validate());
        }

        [Test]
        public async Task MembersAreDeduplicatedAndCappedAsync()
        {
            var uris = new List<string>();
            for (int ii = 0; ii < 10_000; ii++)
            {
                // Every URI twice.
                uris.Add("urn:peer" + (ii / 2));
            }
            RedundantServerDataType[] servers =
            [
                new RedundantServerDataType { ServerId = "urn:first", ServiceLevel = 200, ServerState = ServerState.Running },
                new RedundantServerDataType { ServerId = "urn:first", ServiceLevel = 100, ServerState = ServerState.Running },
                new RedundantServerDataType { ServerId = "urn:second", ServiceLevel = 150, ServerState = ServerState.Running }
            ];
            Mock<ISession> session = CreateSession((int)RedundancySupport.Hot, servers, [.. uris]);
            var handler = new DefaultServerRedundancyHandler(
                new Mock<IRedundantServerEndpointResolver>(MockBehavior.Strict).Object,
                new FakeTimeProvider());

            ServerRedundancyInfo info = await ((IServerRedundancyEndpointCache)handler)
                .ReadRedundancyInfoAsync(session.Object, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(info.RedundantServers.Count, Is.EqualTo(ServerRedundancyOptions.DefaultMaxRedundantServers));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int ii = 0; ii < info.RedundantServers.Count; ii++)
            {
                Assert.That(seen.Add(info.RedundantServers[ii].ServerUri), Is.True,
                    info.RedundantServers[ii].ServerUri + " is listed once");
            }
            Assert.That(info.RedundantServers[0].ServerUri, Is.EqualTo("urn:first"));
            Assert.That(info.RedundantServers[0].ServiceLevel, Is.EqualTo(200));
            Assert.That(info.RedundantServers[1].ServerUri, Is.EqualTo("urn:second"));
            Assert.That(info.RedundantServers[2].ServerUri, Is.EqualTo("urn:peer0"));
        }

        [Test]
        public async Task ConfiguredMemberLimitIsUsedAsync()
        {
            Mock<ISession> session = CreateSession(
                (int)RedundancySupport.Warm, null, ["urn:a", "urn:b", "urn:c", "urn:d"]);
            var handler = new DefaultServerRedundancyHandler(
                new Mock<IRedundantServerEndpointResolver>(MockBehavior.Strict).Object,
                new FakeTimeProvider(),
                new ServerRedundancyOptions { MaxRedundantServers = 2 });

            ServerRedundancyInfo info = await ((IServerRedundancyEndpointCache)handler)
                .ReadRedundancyInfoAsync(session.Object, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(info.RedundantServers.Count, Is.EqualTo(2));
            Assert.That(info.RedundantServers[1].ServerUri, Is.EqualTo("urn:b"));
        }

        [Test]
        public async Task PeerDiscoveryRunsWithBoundedConcurrencyAsync()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int started = 0;
            int inFlight = 0;
            int maxInFlight = 0;
            var resolver = new Mock<IRedundantServerEndpointResolver>();
            resolver.Setup(r => r.ResolveAsync(
                    It.IsAny<string>(), It.IsAny<ConfiguredEndpoint>(), It.IsAny<CancellationToken>()))
                .Returns((string uri, ConfiguredEndpoint _, CancellationToken _) =>
                {
                    Interlocked.Increment(ref started);
                    int now = Interlocked.Increment(ref inFlight);
                    InterlockedMax(ref maxInFlight, now);
                    return new ValueTask<ConfiguredEndpoint?>(CompleteAsync(uri));
                });

            async Task<ConfiguredEndpoint?> CompleteAsync(string uri)
            {
                await gate.Task.ConfigureAwait(false);
                Interlocked.Decrement(ref inFlight);
                return CreateEndpoint(uri, "opc.tcp://localhost:4840/" + uri);
            }

            var handler = new DefaultServerRedundancyHandler(resolver.Object, new FakeTimeProvider());
            Mock<ISession> session = CreateSession((int)RedundancySupport.Hot, null, CreateUris("urn:p", 20));
            var cache = (IServerRedundancyEndpointCache)handler;
            ServerRedundancyInfo basic = await cache
                .ReadRedundancyInfoAsync(session.Object, CancellationToken.None)
                .ConfigureAwait(false);

            Task<ServerRedundancyInfo> resolving = cache.ResolveCachedEndpointsAsync(
                basic, session.Object.ConfiguredEndpoint, CancellationToken.None).AsTask();
            Assert.That(Volatile.Read(ref started),
                Is.EqualTo(ServerRedundancyOptions.DefaultMaxConcurrentPeerDiscoveries),
                "only the bounded number of discoveries starts at once");

            gate.SetResult(true);
            ServerRedundancyInfo resolved = await resolving.WaitAsync(TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);

            Assert.That(Volatile.Read(ref started), Is.EqualTo(20));
            Assert.That(Volatile.Read(ref maxInFlight),
                Is.LessThanOrEqualTo(ServerRedundancyOptions.DefaultMaxConcurrentPeerDiscoveries));
            for (int ii = 0; ii < resolved.RedundantServers.Count; ii++)
            {
                Assert.That(resolved.RedundantServers[ii].Endpoint, Is.Not.Null);
            }
        }

        [Test]
        public async Task ResolvedEndpointsOfMembersNoLongerListedAreDroppedAsync()
        {
            var resolver = new Mock<IRedundantServerEndpointResolver>();
            resolver.Setup(r => r.ResolveAsync(
                    It.IsAny<string>(), It.IsAny<ConfiguredEndpoint>(), It.IsAny<CancellationToken>()))
                .Returns((string uri, ConfiguredEndpoint _, CancellationToken _) =>
                    new ValueTask<ConfiguredEndpoint?>(
                        CreateEndpoint(uri, "opc.tcp://localhost:4840/" + uri)));
            var handler = new DefaultServerRedundancyHandler(resolver.Object, new FakeTimeProvider());
            var cache = (IServerRedundancyEndpointCache)handler;

            for (int round = 0; round < 10; round++)
            {
                Mock<ISession> session = CreateSession(
                    (int)RedundancySupport.Hot, null, CreateUris("urn:r" + round + "p", 5));
                ServerRedundancyInfo basic = await cache
                    .ReadRedundancyInfoAsync(session.Object, CancellationToken.None)
                    .ConfigureAwait(false);
                await cache.ResolveCachedEndpointsAsync(
                    basic, session.Object.ConfiguredEndpoint, CancellationToken.None).ConfigureAwait(false);

                Assert.That(handler.ResolvedEndpointCount, Is.EqualTo(5),
                    "only the members of the latest set are kept");
            }
        }

        [Test]
        public async Task DiscoveredServerCacheIsBoundedAsync()
        {
            int getEndpointsCalls = 0;
            var discoveryUrls = new string[100];
            for (int ii = 0; ii < discoveryUrls.Length; ii++)
            {
                discoveryUrls[ii] = "opc.tcp://host" + ii + ":4840";
            }
            var discovery = new Discovery(
                serverUri => [new ApplicationDescription
                {
                    ApplicationUri = serverUri,
                    DiscoveryUrls = discoveryUrls.ToArrayOf()
                }],
                () =>
                {
                    Interlocked.Increment(ref getEndpointsCalls);
                    return [];
                });
            var resolver = new DefaultRedundantServerEndpointResolver(NUnitTelemetryContext.Create(), discovery);
            ConfiguredEndpoint current = CreateEndpoint("urn:current", "opc.tcp://current:4840");

            const int servers = 200;
            for (int ii = 0; ii < servers; ii++)
            {
                ConfiguredEndpoint? endpoint = await resolver
                    .ResolveAsync("urn:s" + ii, current)
                    .ConfigureAwait(false);
                Assert.That(endpoint, Is.Null);
            }

            Assert.That(resolver.DiscoveredServerCount,
                Is.LessThanOrEqualTo(DefaultRedundantServerEndpointResolver.MaxDiscoveredServers));
            Assert.That(getEndpointsCalls,
                Is.EqualTo(servers * DefaultRedundantServerEndpointResolver.MaxDiscoveryUrlsPerServer),
                "only the first discovery URLs of a Server are tried");
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value &&
                Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }

        private static string[] CreateUris(string prefix, int count)
        {
            var uris = new string[count];
            for (int ii = 0; ii < count; ii++)
            {
                uris[ii] = prefix + ii;
            }
            return uris;
        }

        private static ConfiguredEndpoint CreateEndpoint(string applicationUri, string endpointUrl)
        {
            var description = new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription
                {
                    ApplicationUri = applicationUri,
                    DiscoveryUrls = new ArrayOf<string>(new[] { endpointUrl })
                }
            };

            return new ConfiguredEndpoint(null, description, configuration: null);
        }

        private static Mock<ISession> CreateSession(
            int redundancySupport,
            RedundantServerDataType[]? redundantServers,
            string[]? serverUris)
        {
            var mock = new Mock<ISession>();
            mock.SetupGet(s => s.ConfiguredEndpoint)
                .Returns(CreateEndpoint("urn:current", "opc.tcp://current:4840"));
            mock.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.Is<ArrayOf<ReadValueId>>(r => r.Count == 5),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(new Variant(redundancySupport), StatusCodes.Good),
                        new DataValue(new Variant((byte)200), StatusCodes.Good),
                        redundantServers == null
                            ? new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown)
                            : new DataValue(
                                new Variant((ArrayOf<ExtensionObject>)Array.ConvertAll(
                                    redundantServers, s => new ExtensionObject(s))),
                                StatusCodes.Good),
                        serverUris == null
                            ? new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown)
                            : new DataValue(new Variant(new ArrayOf<string>(serverUris)), StatusCodes.Good),
                        new DataValue(new Variant(DateTime.MinValue), StatusCodes.Good)
                    ],
                    DiagnosticInfos = []
                });
            return mock;
        }

        private sealed class Discovery(
            Func<string, ArrayOf<ApplicationDescription>> findServers,
            Func<ArrayOf<EndpointDescription>> getEndpoints)
            : IRedundantServerDiscovery
        {
            public ValueTask<ArrayOf<ApplicationDescription>> FindServersAsync(
                Uri discoveryUri,
                EndpointConfiguration configuration,
                string serverUri,
                ITelemetryContext telemetry,
                CancellationToken ct)
            {
                return new ValueTask<ArrayOf<ApplicationDescription>>(findServers(serverUri));
            }

            public ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
                Uri discoveryUri,
                EndpointConfiguration configuration,
                ITelemetryContext telemetry,
                CancellationToken ct)
            {
                return new ValueTask<ArrayOf<EndpointDescription>>(getEndpoints());
            }
        }
    }
}
