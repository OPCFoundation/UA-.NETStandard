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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies both monitoring CUs against actual configured limits and real CreateMonitoredItems outcomes.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("MachineTool")]
    [Category("Integration")]
    public sealed class MachineToolCapabilityMonitoringTests
    {
        /// <summary>
        /// Twenty real items are accepted only at a sufficient quota; the CU also requires every actual exposed item.
        /// </summary>
        [TestCase(19, 19)]
        [TestCase(20, 20)]
        public async Task MinimumThresholdIsAnEnforcedQuotaNotANameAsync(int perSubscription, int accepted)
        {
            await using var fixture = new CapacityFixture(perSubscription, 10000);
            await fixture.StartAsync().ConfigureAwait(false);
            await using MachineToolRuntime<MachineToolState> runtime =
                await BuildAsync(fixture.Context).ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "CapacityThreshold").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 100, 300, 10, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            var items = new MonitoredItemCreateRequest[20];
            for (int index = 0; index < items.Length; index++)
            {
                items[index] = Item(
                    runtime.State.Production!.ActiveProgram!.Name!.NodeId, Attributes.Value, (uint)index);
            }
            CreateMonitoredItemsResponse created = await session.CreateMonitoredItemsAsync(
                null, subscription.SubscriptionId, TimestampsToReturn.Both, items.ToArrayOf(), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(created.Results.ToArray()!.Count(result => StatusCode.IsGood(result.StatusCode)),
                Is.EqualTo(accepted));
            Assert.That(runtime.Capabilities.Monitoring.MaximumMonitoredItemsPerSubscription,
                Is.EqualTo((uint)perSubscription));
            Assert.That(runtime.Capabilities.Monitoring.PotentialMonitoredItems, Is.EqualTo(17),
                "The current mandatory-only model has seventeen actual variable/notifier targets.");
            Assert.That(runtime.Capabilities.Monitoring.SupportsMinimum, Is.EqualTo(perSubscription == 20),
                "Even a seventeen-target machine must support twenty monitored items for this CU.");
            Assert.That(runtime.Capabilities.ServerProfiles.ToArray()!.Any(profile =>
                profile.EndsWith("/Basic", StringComparison.Ordinal) ||
                profile.EndsWith("/BasicSecure", StringComparison.Ordinal)), Is.False,
                "This fixture really configures only SecurityPolicy None.");
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// With four required sessions and four required subscriptions each, all items must fit two by two.
        /// </summary>
        [TestCase(10000, true)]
        [TestCase(1, false)]
        public async Task FullCapacityMatchesActualHalfSessionAndSubscriptionCreationAsync(int maximumItems, bool all)
        {
            await using var fixture = new CapacityFixture(0, maximumItems);
            await fixture.StartAsync().ConfigureAwait(false);
            await using MachineToolRuntime<MachineToolState> runtime = await BuildAsync(fixture.Context,
                new MachineToolMonitoringRequirements { Sessions = 4, SubscriptionsPerSession = 4 })
                .ConfigureAwait(false);
            List<(NodeId Node, uint Attribute)> nodes = MonitorableNodes(runtime.State, fixture.Context.Context);
            Assert.That(runtime.Capabilities.Monitoring.PotentialMonitoredItems, Is.EqualTo((uint)nodes.Count));
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var firstTransport = new ClientFixture(telemetry);
            await using var secondTransport = new ClientFixture(telemetry);
            await firstTransport.LoadClientConfigurationAsync(fixture.Root, "CapacityA").ConfigureAwait(false);
            await secondTransport.LoadClientConfigurationAsync(fixture.Root, "CapacityB").ConfigureAwait(false);
            using ISession first = await firstTransport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            using ISession second = await secondTransport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            ISession[] sessions = [first, second];
            bool allCreated = true;
            foreach (ISession session in sessions)
            {
                for (int subscriptionIndex = 0; subscriptionIndex < 2; subscriptionIndex++)
                {
                    CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                        null, 100, 300, 10, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
                    var items = new MonitoredItemCreateRequest[nodes.Count];
                    for (int index = 0; index < nodes.Count; index++)
                    {
                        items[index] = Item(nodes[index].Node, nodes[index].Attribute, (uint)index);
                    }
                    CreateMonitoredItemsResponse created = await session.CreateMonitoredItemsAsync(
                        null, subscription.SubscriptionId, TimestampsToReturn.Both,
                        items.ToArrayOf(), CancellationToken.None)
                        .ConfigureAwait(false);
                    allCreated &= created.Results.ToArray()!.All(result => StatusCode.IsGood(result.StatusCode));
                }
            }
            Assert.That(allCreated, Is.EqualTo(all));
            Assert.That(runtime.Capabilities.Monitoring.SupportsAll, Is.EqualTo(allCreated));
            Assert.That(runtime.Capabilities.Monitoring.SupportsMinimum, Is.EqualTo(all));
            await first.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await second.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Basic and BasicSecure are claimed only when the running server publishes the matching secure endpoints.
        /// </summary>
        [TestCase(false, false, false, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, true, true)]
        public async Task SecureProfilesFollowPublishedEndpointsAsync(
            bool basic256, bool aes128WithCertificate, bool basic, bool basicSecure)
        {
            var policies = new List<ServerSecurityPolicy>
            {
                new() { SecurityMode = MessageSecurityMode.None, SecurityPolicyUri = SecurityPolicies.None }
            };
            var tokens = new List<UserTokenPolicy> { new(UserTokenType.Anonymous) };
            if (basic256)
            {
                policies.Add(new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                });
            }
            if (aes128WithCertificate)
            {
                policies.Add(new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep
                });
                tokens.Add(new UserTokenPolicy(UserTokenType.Certificate));
            }
            await using var fixture = new CapacityFixture(1000, 10000, policies.ToArrayOf(), tokens.ToArrayOf());
            await fixture.StartAsync().ConfigureAwait(false);
            await using MachineToolRuntime<MachineToolState> runtime = await BuildAsync(
                fixture.Context, null, MachineToolFeatures.Channels | MachineToolFeatures.Stacklights)
                .ConfigureAwait(false);
            string[] profiles = runtime.Capabilities.ServerProfiles.ToArray()!;
            Assert.That(profiles.Any(p => p.EndsWith("/Basic", StringComparison.Ordinal)), Is.EqualTo(basic));
            Assert.That(profiles.Any(p => p.EndsWith("/BasicSecure", StringComparison.Ordinal)),
                Is.EqualTo(basicSecure));
        }

        /// <summary>
        /// Builds a real mandatory machine with explicit deployment requirements.
        /// </summary>
        private static ValueTask<MachineToolRuntime<MachineToolState>> BuildAsync(
            IMachineryBuildContext context, MachineToolMonitoringRequirements? requirements = null,
            MachineToolFeatures features = default)
        {
            return context.AddMachineTool("Capacity")
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot
                {
                    Stacklights =
                    [
                        new MachineToolStacklight("Main",
                        [
                            new MachineToolLight(true, IA.SignalColor.Red, IA.SignalModeLight.Continuous,
                                new MachineToolSampleInfo(DateTime.UtcNow, StatusCodes.Good))
                        ])
                    ]
                }))
                .WithFeatures(features)
                .WithMonitoringRequirements(requirements ?? new())
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Capacity tests");
                    identity.SerialNumber = "Capacity";
                    identity.ProductInstanceUri = "urn:capacity:machine";
                }).BuildAsync();
        }

        /// <summary>
        /// Enumerates actual readable variables and SubscribeToEvents objects, excluding the nonstandard declaration.
        /// </summary>
        private static List<(NodeId, uint)> MonitorableNodes(NodeState root, ISystemContext context)
        {
            var nodes = new List<(NodeId, uint)>();
            var seen = new HashSet<NodeId>();
            var pending = new Stack<NodeState>();
            pending.Push(root);
            int extension = context.NamespaceUris.GetIndex(MachineToolCapabilityNames.NamespaceUri);
            while (pending.Count != 0)
            {
                NodeState node = pending.Pop();
                if (!seen.Add(node.NodeId) || node.BrowseName.NamespaceIndex == extension)
                {
                    continue;
                }
                if (node is BaseVariableState)
                {
                    nodes.Add((node.NodeId, Attributes.Value));
                }
                else if (node is BaseObjectState obj && (obj.EventNotifier & EventNotifiers.SubscribeToEvents) != 0)
                {
                    nodes.Add((node.NodeId, Attributes.EventNotifier));
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
            return nodes;
        }

        /// <summary>
        /// Creates a real data or event monitored-item request.
        /// </summary>
        private static MonitoredItemCreateRequest Item(NodeId node, uint attribute, uint clientHandle)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = node, AttributeId = attribute },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = clientHandle,
                    QueueSize = 1,
                    SamplingInterval = 100,
                    Filter = attribute == Attributes.EventNotifier
                        ? new ExtensionObject(new EventFilter
                        {
                            SelectClauses =
                            [
                                new SimpleAttributeOperand
                                {
                                    TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                                    AttributeId = Attributes.Value,
                                    BrowsePath = [new QualifiedName(Ua.BrowseNames.EventType)]
                                }
                            ]
                        })
                        : ExtensionObject.Null
                }
            };
        }

        /// <summary>
        /// Reuses the existing Machinery runtime server while configuring actual quotas before startup.
        /// </summary>
        private sealed class CapacityFixture(
            int maximumPerSubscription,
            int maximumItems,
            ArrayOf<ServerSecurityPolicy> securityPolicies = default,
            ArrayOf<UserTokenPolicy> userTokenPolicies = default) : IAsyncDisposable
        {
            /// <summary>
            /// Gets the fixture-owned certificate-store root.
            /// </summary>
            public string Root { get; } = Path.Combine(
                Path.GetTempPath(), "capacity-" + Guid.NewGuid().ToString("N"));

            /// <summary>
            /// Gets the actual server endpoint.
            /// </summary>
            public Uri Endpoint => new($"opc.tcp://localhost:{m_fixture!.Port}");

            /// <summary>
            /// Gets the existing shared Machinery construction context.
            /// </summary>
            public IMachineryBuildContext Context { get; private set; } = null!;

            /// <summary>
            /// Starts the real server only after configuring the quotas under test.
            /// </summary>
            public async ValueTask StartAsync()
            {
                m_fixture = new ServerFixture<MachineToolRuntimeFixture.RuntimeServer>(
                    telemetry => new MachineToolRuntimeFixture.RuntimeServer(telemetry))
                {
                    AutoAccept = true,
                    SecurityNone = true
                };
                await m_fixture.LoadConfigurationAsync(Root).ConfigureAwait(false);
                ServerConfiguration configuration = m_fixture.Config.ServerConfiguration!;
                configuration.MaxSessionCount = 4;
                configuration.MaxSubscriptionCount = 16;
                configuration.MaxMonitoredItemsPerSubscription = maximumPerSubscription;
                configuration.MaxMonitoredItemCount = maximumItems;
                configuration.SecurityPolicies = securityPolicies.IsEmpty ?
                [
                    new ServerSecurityPolicy
                    {
                        SecurityMode = MessageSecurityMode.None, SecurityPolicyUri = SecurityPolicies.None
                    }
                ] : securityPolicies;
                configuration.UserTokenPolicies = userTokenPolicies.IsEmpty ?
                    [new UserTokenPolicy(UserTokenType.Anonymous)] : userTokenPolicies;
                MachineToolRuntimeFixture.RuntimeServer server = await m_fixture.StartAsync().ConfigureAwait(false);
                Context = server.Manager.CreateMachineryBuildContext();
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (m_fixture != null)
                {
                    await m_fixture.StopAsync().ConfigureAwait(false);
                }
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
                }
            }

            /// <summary>The borrowed standard server fixture, owning all network and PKI resources.</summary>
            private ServerFixture<MachineToolRuntimeFixture.RuntimeServer>? m_fixture;
        }
    }
}
