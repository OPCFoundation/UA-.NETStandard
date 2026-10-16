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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises the typed client through real sessions instead of inspecting a server-side shadow.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class MachineToolClientTests
    {
        /// <summary>
        /// Observation bounds reject invalid requests without creating network or subscription resources.
        /// </summary>
        [TestCase(0, 1)]
        [TestCase(-1, 1)]
        [TestCase(60.001, 1)]
        [TestCase(1, 0)]
        [TestCase(1, 101)]
        public void ObservationBoundsRejectInvalidLimits(double seconds, int count)
        {
            var options = new MachineToolObservationOptions
            {
                Window = TimeSpan.FromSeconds(seconds),
                MaximumNotifications = count
            };

            Assert.That(options.Validate, Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Both exact upper bounds and the smallest useful finite window are accepted.
        /// </summary>
        [TestCase(60, 100)]
        [TestCase(0.001, 1)]
        public void ObservationBoundsAcceptValidLimits(double seconds, int count)
        {
            var options = new MachineToolObservationOptions
            {
                Window = TimeSpan.FromSeconds(seconds),
                MaximumNotifications = count
            };

            Assert.That(options.Validate, Throws.Nothing);
        }

        /// <summary>
        /// Discovery and generated-proxy navigation retain raw sample quality and optional absence.
        /// </summary>
        [Test]
        public async Task ClientDiscoversAndReadsMachineSamplesOverTcpAsync()
        {
            await using var server = new MachineToolRuntimeFixture();
            await server.StartAsync().ConfigureAwait(false);
            DateTimeUtc acquired = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            var provider = new InMemoryMachineToolProvider(new MachineToolSnapshot
            {
                Operation = new MachineToolOperation
                {
                    Mode = MachineOperationMode.Automatic,
                    Sample = new MachineToolSampleInfo(acquired, StatusCodes.UncertainLastUsableValue)
                },
                ActiveProgram = new MachineToolProgram
                {
                    Name = "MAIN.NC",
                    Phase = MachineToolProductionPhase.Running,
                    Sample = new MachineToolSampleInfo(acquired, StatusCodes.Good)
                }
            });
            await using MachineToolRuntime<MachineToolState> machine = await server.Context
                .AddMachineTool("ClientMachine")
                .WithProvider(provider)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Client test");
                    identity.SerialNumber = "MT-CLIENT";
                    identity.ProductInstanceUri = "urn:test:mt-client";
                }).BuildAsync().ConfigureAwait(false);
            server.Context.DeviceSet.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, false, machine.NodeId);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            transport.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await transport.LoadClientConfigurationAsync(server.Root, "MachineToolClient").ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await transport.ConnectAsync(server.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
                ArrayOf<MachineToolDeviceEntry> devices = await client.DiscoverAsync().ConfigureAwait(false);
                Assert.That(devices.Count, Is.EqualTo(1));
                Assert.That(devices[0].NodeId, Is.EqualTo(machine.NodeId));
                Assert.That(devices[0].Kind, Is.EqualTo(MachineToolDeviceKind.MachineTool));
                Assert.That(client.AvailableParts, Is.EqualTo(MachineToolParts.MachineTool));

                MachineToolReadSnapshot snapshot = await client.ReadSnapshotAsync(machine.NodeId).ConfigureAwait(false);
                Assert.That(snapshot.OperationMode.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(snapshot.OperationMode.SourceTimestamp, Is.EqualTo(acquired));
                Assert.That(snapshot.OperationMode.WrappedValue.TryGetValue(out int mode), Is.True);
                Assert.That(mode, Is.EqualTo((int)MachineOperationMode.Automatic));
                Assert.That(snapshot.ActiveProgramName.WrappedValue.TryGetValue(out string program), Is.True);
                Assert.That(program, Is.EqualTo("MAIN.NC"));
                Assert.That(snapshot.FeedOverride.IsNull, Is.True);
                Assert.That(snapshot.IsWarmUp.IsNull, Is.True);
                Assert.That(session.TryGetSubscriptionManager(out ISubscriptionManager? subscriptions), Is.True);
                int before = subscriptions!.Count;
                MachineToolDataObservation observation = await client.ObserveDataAsync(
                    machine.NodeId,
                    new MachineToolObservationOptions
                    {
                        Window = TimeSpan.FromSeconds(10),
                        MaximumNotifications = 3
                    }).ConfigureAwait(false);
                Assert.That(observation.Changes.Count, Is.EqualTo(3));
                Assert.That(observation.LimitReached, Is.True);
                DataValue observedMode = observation.Changes.ToArray()!
                    .Single(change => change.NodeId == machine.State.Monitoring!.MachineTool!.OperationMode!.NodeId)
                    .Value;
                Assert.That(observedMode.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(observedMode.SourceTimestamp, Is.EqualTo(acquired));
                Assert.That(subscriptions.Count, Is.EqualTo(before),
                    "One-shot observation must release only the subscription it created.");
                MachineToolDataObservation selected = await client.ObserveValuesAsync(
                    [machine.State.Monitoring!.MachineTool!.OperationMode!.NodeId],
                    new MachineToolObservationOptions
                    {
                        Window = TimeSpan.FromSeconds(10),
                        MaximumNotifications = 1
                    }).ConfigureAwait(false);
                Assert.That(selected.Changes.Count, Is.EqualTo(1));
                Assert.That(selected.Changes[0].Value.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(selected.Changes[0].Value.SourceTimestamp, Is.EqualTo(acquired));
                Assert.That(subscriptions.Count, Is.EqualTo(before));
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await client.ObserveValuesAsync(ArrayOf<NodeId>.Empty).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(async () =>
                    await client.ObserveValuesAsync([machine.NodeId, machine.NodeId]).ConfigureAwait(false));
                var oversizedSelection = new NodeId[257];
                for (int index = 0; index < oversizedSelection.Length; index++)
                {
                    oversizedSelection[index] = new NodeId((uint)(index + 1), machine.NodeId.NamespaceIndex);
                }
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await client.ObserveValuesAsync(new ArrayOf<NodeId>(oversizedSelection)).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await client.DiscoverAsync(0).ConfigureAwait(false));
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await client.DiscoverAsync(cancellationToken: cancellation.Token).ConfigureAwait(false));
                using var pendingCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                Task<MachineToolDataObservation> pending = client.ObserveDataAsync(machine.NodeId,
                    new MachineToolObservationOptions { Window = TimeSpan.FromMinutes(1) },
                    pendingCancellation.Token).AsTask();
                try
                {
                    while (subscriptions.Count == before)
                    {
                        await Task.Delay(20, pendingCancellation.Token).ConfigureAwait(false);
                    }
                    await client.DisposeAsync().ConfigureAwait(false);
                    Assert.That(subscriptions.Count, Is.EqualTo(before),
                        "Disposing a facade must cancel and drain its own subscription resources.");
                    Assert.CatchAsync<OperationCanceledException>(async () =>
                        await pending.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false));
                }
                finally
                {
                    pendingCancellation.Cancel();
                    Assert.CatchAsync<OperationCanceledException>(async () =>
                        await pending.ConfigureAwait(false));
                }
                Assert.That(subscriptions.Count, Is.EqualTo(before));
                Assert.That(session.Connected, Is.True, "Disposing a borrowed facade must not close its caller's session.");
                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await client.DiscoverAsync().ConfigureAwait(false));
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Factory facades borrow the real container-cached connection shared by other consumers.
        /// </summary>
        [Test]
        public async Task ClientFactoryBorrowsContainerSessionWithoutDisconnectingOtherConsumersAsync()
        {
            await using var server = new MachineToolRuntimeFixture();
            await server.StartAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await using ServiceProvider provider = await CreateClientProviderAsync(server, transport, telemetry)
                .ConfigureAwait(false);
            MachineToolClientFactory factory = provider.GetRequiredService<MachineToolClientFactory>();
            Func<CancellationToken, Task<ManagedSession>> acquire =
                provider.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
            ManagedSession shared = await acquire(CancellationToken.None).ConfigureAwait(false);
            int closing = 0;
            shared.ConnectionStateChanged += (_, change) =>
            {
                if (change.NewState == ConnectionState.Closing)
                {
                    Interlocked.Increment(ref closing);
                }
            };
            await using MachineToolClient first = await factory.CreateAsync().ConfigureAwait(false);
            await using MachineToolClient second = await provider
                .GetRequiredService<Func<CancellationToken, Task<MachineToolClient>>>()(CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(first.Session, Is.SameAs(shared));
            Assert.That(second.Session, Is.SameAs(shared));
            Assert.That(shared.TryGetSubscriptionManager(out ISubscriptionManager? subscriptions), Is.True);
            int before = subscriptions!.Count;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Task<MachineToolDataObservation> pending = first.ObserveValuesAsync(
                [Ua.VariableIds.Server_ServerStatus_CurrentTime],
                new MachineToolObservationOptions { Window = TimeSpan.FromMinutes(1) },
                deadline.Token).AsTask();
            while (subscriptions.Count == before)
            {
                await Task.Delay(10, deadline.Token).ConfigureAwait(false);
            }

            await first.DisposeAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);

            Assert.CatchAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
            Assert.That(subscriptions.Count, Is.EqualTo(before));
            Assert.That(shared.Disposed, Is.False);
            Assert.That(shared.Connected, Is.True);
            Assert.That(closing, Is.Zero);
            Assert.That(await acquire(CancellationToken.None).ConfigureAwait(false), Is.SameAs(shared));
            MachineToolDataObservation observed = await second.ObserveValuesAsync(
                [Ua.VariableIds.Server_ServerStatus_CurrentTime],
                new MachineToolObservationOptions { Window = TimeSpan.FromSeconds(5), MaximumNotifications = 1 })
                .ConfigureAwait(false);
            Assert.That(observed.Changes, Has.Count.EqualTo(1));
            Assert.That(observed.Changes[0].Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(subscriptions.Count, Is.EqualTo(before));
            Assert.That(() => first.DiscoverAsync().AsTask(), Throws.InstanceOf<ObjectDisposedException>());

            await second.DisposeAsync().ConfigureAwait(false);
            Assert.That(shared.Connected, Is.True);
            await provider.DisposeAsync().ConfigureAwait(false);
            Assert.That(shared.Disposed, Is.True, "Only the container owns the cached session.");
            Assert.That(closing, Is.EqualTo(1));
        }

        /// <summary>
        /// Cancellation immediately after cached acquisition must not invalidate a different facade.
        /// </summary>
        [Test]
        public async Task ClientFactoryCancellationAfterAcquisitionPreservesSharedSessionAsync()
        {
            await using var server = new MachineToolRuntimeFixture();
            await server.StartAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await using ServiceProvider provider = await CreateClientProviderAsync(server, transport, telemetry)
                .ConfigureAwait(false);
            Func<CancellationToken, Task<ManagedSession>> acquire =
                provider.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
            ManagedSession shared = await acquire(CancellationToken.None).ConfigureAwait(false);
            await using MachineToolClient other = await provider.GetRequiredService<MachineToolClientFactory>()
                .CreateAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            int acquisitions = 0;
            int closing = 0;
            shared.ConnectionStateChanged += (_, change) =>
            {
                if (change.NewState == ConnectionState.Closing)
                {
                    Interlocked.Increment(ref closing);
                }
            };
            var factory = new MachineToolClientFactory(async token =>
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                ManagedSession cached = await acquire(token).ConfigureAwait(false);
                Assert.That(cached, Is.SameAs(shared));
                acquisitions++;
                await cancellation.CancelAsync().ConfigureAwait(false);
                return cached;
            }, telemetry);

            OperationCanceledException error = Assert.ThrowsAsync<OperationCanceledException>(
                () => factory.CreateAsync(cancellation.Token))!;

            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(acquisitions, Is.EqualTo(1));
            Assert.That(closing, Is.Zero);
            Assert.That(shared.Disposed, Is.False);
            Assert.That(shared.Connected, Is.True);
            Assert.That(other.Session, Is.SameAs(shared));
            DataValue value = await other.Session.ReadValueAsync(Ua.VariableIds.Server_ServerStatus_CurrentTime)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(await acquire(CancellationToken.None).ConfigureAwait(false), Is.SameAs(shared));
        }

        /// <summary>
        /// A cached session becoming unavailable does not transfer disposal responsibility to a failing factory.
        /// </summary>
        [Test]
        public async Task ClientFactoryCreationFailureDoesNotDisposeBorrowedSessionAsync()
        {
            await using var server = new MachineToolRuntimeFixture();
            await server.StartAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await using ServiceProvider provider = await CreateClientProviderAsync(server, transport, telemetry)
                .ConfigureAwait(false);
            Func<CancellationToken, Task<ManagedSession>> acquire =
                provider.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
            ManagedSession shared = await acquire(CancellationToken.None).ConfigureAwait(false);
            Assert.That(await shared.CloseAsync(CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            Assert.That(shared.Disposed, Is.False);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => provider.GetRequiredService<MachineToolClientFactory>().CreateAsync())!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            Assert.That(shared.Disposed, Is.False);
            Assert.That(await acquire(CancellationToken.None).ConfigureAwait(false), Is.SameAs(shared));
            await provider.DisposeAsync().ConfigureAwait(false);
            Assert.That(shared.Disposed, Is.True);
        }

        /// <summary>
        /// A pre-cancelled request must not invoke even an accessor that ignores its token.
        /// </summary>
        [Test]
        public void ClientFactoryRejectsCancellationBeforeAcquisition()
        {
            bool acquired = false;
            var factory = new MachineToolClientFactory(_ =>
            {
                acquired = true;
                throw new InvalidOperationException("The cancelled request must not acquire a session.");
            }, NUnitTelemetryContext.Create());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            OperationCanceledException error = Assert.ThrowsAsync<OperationCanceledException>(
                () => factory.CreateAsync(cancellation.Token))!;

            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(acquired, Is.False);
        }

        /// <summary>
        /// The internal explicit-ownership path still disposes a genuinely independent session once.
        /// </summary>
        [Test]
        public async Task ExplicitOwnedFacadeStillClosesItsIndependentSessionAsync()
        {
            await using var server = new MachineToolRuntimeFixture();
            await server.StartAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry)
            {
                SessionFactory = new ManagedSessionFactory(telemetry)
                    .WithSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance)
            };
            await transport.LoadClientConfigurationAsync(server.Root, "MachineToolOwnedClient").ConfigureAwait(false);
            await using ISession session = await transport.ConnectAsync(server.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            var independent = (ManagedSession)session;
            int closing = 0;
            independent.ConnectionStateChanged += (_, change) =>
            {
                if (change.NewState == ConnectionState.Closing)
                {
                    Interlocked.Increment(ref closing);
                }
            };
            ConstructorInfo constructor = typeof(MachineToolClient).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(ISession), typeof(ITelemetryContext), typeof(bool), typeof(TimeProvider)],
                modifiers: null)!;
            await using var client = (MachineToolClient)constructor.Invoke([independent, telemetry, true, null]);

            await client.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);

            Assert.That(independent.Disposed, Is.True);
            Assert.That(independent.Connected, Is.False);
            Assert.That(closing, Is.EqualTo(1));
        }

        private static async Task<ServiceProvider> CreateClientProviderAsync(
            MachineToolRuntimeFixture server,
            ClientFixture transport,
            ITelemetryContext telemetry)
        {
            transport.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await transport.LoadClientConfigurationAsync(server.Root, "MachineToolFactoryClient").ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await transport.GetEndpointAsync(server.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            var services = new ServiceCollection();
            services.AddSingleton(telemetry);
            services.AddOpcUa().AddClient(options =>
            {
                options.Configuration = transport.Config;
                options.Session = new ManagedSessionOptions
                {
                    Endpoint = endpoint,
                    SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance
                };
            }).AddMachineToolClient().AddMachineToolClient();
            return services.BuildServiceProvider();
        }
    }
}
