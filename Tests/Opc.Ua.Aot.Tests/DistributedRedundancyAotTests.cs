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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Redundancy.K8s;
using Opc.Ua.Redundancy;

// CA2007: AOT tests run without a SynchronizationContext.
#pragma warning disable CA2007

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// NativeAOT exercises for distributed redundancy serialization paths.
    /// </summary>
    public class DistributedRedundancyAotTests
    {
        [Test]
        public async Task KubernetesSourceGeneratedJsonRoundTripsLeaseAndEndpointSliceAsync()
        {
            var lease = new KubernetesLease
            {
                Metadata = new KubernetesObjectMetadata
                {
                    Name = "opcua-leader",
                    Namespace = "default",
                    ResourceVersion = "12"
                },
                Spec = new KubernetesLeaseSpec
                {
                    HolderIdentity = "replica-a",
                    LeaseDurationSeconds = 30,
                    AcquireTime = "2026-06-27T08:00:00Z",
                    RenewTime = "2026-06-27T08:00:10Z",
                    LeaseTransitions = 2
                }
            };
            string leaseJson = JsonSerializer.Serialize(
                lease,
                KubernetesJsonContext.Default.KubernetesLease);
            KubernetesLease? decodedLease = JsonSerializer.Deserialize(
                leaseJson,
                KubernetesJsonContext.Default.KubernetesLease);

            await Assert.That(decodedLease).IsNotNull();
            await Assert.That(decodedLease!.Spec.HolderIdentity).IsEqualTo("replica-a");

            var slices = new KubernetesEndpointSliceList
            {
                Items =
                [
                    new KubernetesEndpointSlice
                    {
                        Endpoints =
                        [
                            new KubernetesEndpoint
                            {
                                Addresses = ["10.0.0.11"],
                                Conditions = new KubernetesEndpointConditions { Ready = true }
                            }
                        ],
                        Ports = [new KubernetesEndpointPort { Name = "opcua-tcp", Port = 4840 }]
                    }
                ]
            };
            string sliceJson = JsonSerializer.Serialize(
                slices,
                KubernetesJsonContext.Default.KubernetesEndpointSliceList);
            KubernetesEndpointSliceList? decodedSlices = JsonSerializer.Deserialize(
                sliceJson,
                KubernetesJsonContext.Default.KubernetesEndpointSliceList);

            await Assert.That(decodedSlices).IsNotNull();
            await Assert.That(decodedSlices!.Items[0].Endpoints[0].Addresses[0]).IsEqualTo("10.0.0.11");
        }

        [Test]
        public async Task KubernetesLeaseAndPeerDiscoveryUseMockedInClusterClientAsync()
        {
            var client = new FakeKubernetesApiClient();
            var leaderOptions = new KubernetesLeaderElectionOptions();
            leaderOptions.Kubernetes.Namespace = "default";
            leaderOptions.Kubernetes.NodeId = "replica-a";
            leaderOptions.LeaseName = "opcua-leader";
            var election = new KubernetesLeaseLeaderElection(client, leaderOptions);

            bool acquired = await election.TryAcquireOrRenewAsync();

            await Assert.That(acquired).IsTrue();
            await Assert.That(client.Lease).IsNotNull();
            await Assert.That(client.Lease!.Spec.HolderIdentity).IsEqualTo("replica-a");

            var discoveryOptions = new KubernetesPeerDiscoveryOptions();
            discoveryOptions.Kubernetes.Namespace = "default";
            discoveryOptions.ServiceName = "opcua";
            discoveryOptions.LocalAddress = "10.0.0.10";
            client.EndpointSlices = new KubernetesEndpointSliceList
            {
                Items =
                [
                    new KubernetesEndpointSlice
                    {
                        Endpoints =
                        [
                            new KubernetesEndpoint
                            {
                                Addresses = ["10.0.0.10", "10.0.0.11"],
                                Conditions = new KubernetesEndpointConditions { Ready = true }
                            },
                            new KubernetesEndpoint
                            {
                                Addresses = ["10.0.0.12"],
                                Conditions = new KubernetesEndpointConditions { Ready = false }
                            }
                        ],
                        Ports = [new KubernetesEndpointPort { Name = "opcua-tcp", Port = 4840 }]
                    }
                ]
            };
            var discovery = new KubernetesPeerDiscovery(client, discoveryOptions);

            ArrayOf<string> peers = await discovery.RefreshAsync();

            await Assert.That(peers.Count).IsEqualTo(1);
            await Assert.That(peers[0]).IsEqualTo("opc.tcp://10.0.0.11:4840");
        }

        [Test]
        public async Task BaseMirrorStoresRoundTripProtectedSessionAndSubscriptionAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(31));
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Warning));
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);
            var sessionStore = new SharedKeyValueSessionStore(kv, context, protector);
            var token = new NodeId(Guid.NewGuid(), 1);
            var entry = new SharedSessionEntry
            {
                SessionId = new NodeId(Guid.NewGuid(), 1),
                AuthenticationToken = token,
                SessionName = "aot-session",
                CreatedAt = DateTime.UtcNow,
                LastActivatedAt = DateTime.UtcNow,
                ServerNonce = ByteString.From(new byte[] { 1, 2, 3 }),
                ClientNonce = ByteString.From(new byte[] { 4, 5, 6 }),
                SecurityPolicyUri = SecurityPolicies.None,
                SecurityMode = (int)MessageSecurityMode.None,
                EndpointUrl = "opc.tcp://localhost:4840",
                SessionTimeout = 60000,
                ClientDescription = new ApplicationDescription
                {
                    ApplicationUri = "urn:aot-client",
                    ApplicationType = ApplicationType.Client
                },
                SecretMaterial = ByteString.From(new byte[] { 7, 8, 9 })
            };

            await sessionStore.PutAsync(entry);
            SharedSessionEntry? restored = await sessionStore.TryGetAsync(token);

            await Assert.That(restored).IsNotNull();
            await Assert.That(restored!.SessionName).IsEqualTo("aot-session");

            await using var subscriptionStore = new SharedKeyValueSubscriptionStore(kv, context, protector);
            var subscription = new StoredSubscription
            {
                Id = 42,
                IsDurable = true,
                MaxLifetimeCount = 120,
                MaxKeepaliveCount = 12,
                MaxMessageCount = 8,
                MaxNotificationsPerPublish = 16,
                PublishingInterval = 250,
                Priority = 7,
                SentMessages = [],
                UserIdentityToken = new AnonymousIdentityToken(),
                MonitoredItems =
                [
                    new StoredMonitoredItem
                    {
                        AttributeId = Attributes.Value,
                        ClientHandle = 100,
                        Id = 500,
                        NodeId = new NodeId("Temperature", 2),
                        MonitoringMode = MonitoringMode.Reporting,
                        QueueSize = 10,
                        SamplingInterval = 100,
                        SubscriptionId = 42,
                        TimestampsToReturn = TimestampsToReturn.Both,
                        FilterToUse = new DataChangeFilter
                        {
                            Trigger = DataChangeTrigger.StatusValue,
                            DeadbandType = (uint)DeadbandType.Absolute,
                            DeadbandValue = 1
                        }
                    }
                ]
            };

            await subscriptionStore.StoreSubscriptionsAsync([subscription]);
            RestoreSubscriptionResult result = await subscriptionStore.RestoreSubscriptionsAsync();
            List<IStoredSubscription> subscriptions = [.. result.Subscriptions];

            await Assert.That(result.Success).IsTrue();
            await Assert.That(subscriptions.Count).IsEqualTo(1);
            await Assert.That(subscriptions[0].Id).IsEqualTo(42u);
        }

        private static byte[] MakeKey(byte seed)
        {
            byte[] key = new byte[32];
            for (int ii = 0; ii < key.Length; ii++)
            {
                key[ii] = (byte)(seed + ii);
            }
            return key;
        }

        private sealed class FakeKubernetesApiClient : IKubernetesApiClient
        {
            public bool IsInCluster => true;

            public KubernetesLease? Lease { get; set; }

            public KubernetesEndpointSliceList EndpointSlices { get; set; } = new();

            public ValueTask<KubernetesLease?> GetLeaseAsync(
                string namespaceName,
                string name,
                CancellationToken ct)
            {
                return new ValueTask<KubernetesLease?>(Lease);
            }

            public ValueTask<KubernetesLease> CreateLeaseAsync(
                string namespaceName,
                KubernetesLease lease,
                CancellationToken ct)
            {
                Lease = lease;
                return new ValueTask<KubernetesLease>(lease);
            }

            public ValueTask<KubernetesLease> ReplaceLeaseAsync(
                string namespaceName,
                string name,
                KubernetesLease lease,
                CancellationToken ct)
            {
                Lease = lease;
                return new ValueTask<KubernetesLease>(lease);
            }

            public ValueTask DeleteLeaseAsync(string namespaceName, string name, CancellationToken ct)
            {
                Lease = null;
                return default;
            }

            public ValueTask<KubernetesEndpointSliceList> ListEndpointSlicesAsync(
                string namespaceName,
                string serviceName,
                CancellationToken ct)
            {
                return new ValueTask<KubernetesEndpointSliceList>(EndpointSlices);
            }
        }
    }
}
