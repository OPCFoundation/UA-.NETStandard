/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Verifies injectable factories reuse caller-owned sessions and respect cancellation.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    public sealed class GmsResultFactoryTests
    {
        /// <summary>
        /// A direct injectable accessor returns a typed workflow without assuming ownership of the session.
        /// </summary>
        [Test]
        public async Task DirectFactoryReusesTheSuppliedSession()
        {
            Mock<ISession> session = GmsResultClientTests.CreateSession([]);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var factory = new GmsResultClientFactory(_ => new ValueTask<ISession>(session.Object), telemetry);
            GmsResultClient client = await factory.CreateAsync(new NodeId("gms", 1));
            Assert.That(client.Session, Is.SameAs(session.Object));
            Assert.That(client.Root.ObjectId, Is.EqualTo(new NodeId("gms", 1)));
            Assert.That((await client.GetLatestAsync()).ResultMetaData!.ResultId, Is.EqualTo("inspection-17"));
            session.Verify(value => value.Dispose(), Times.Never);
        }

        /// <summary>
        /// Cancellation both before and after acquisition prevents a new workflow without disposing borrowed resources.
        /// </summary>
        [Test]
        public async Task FactoryValidatesArgumentsAndCancellation()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Mock<ISession> session = GmsResultClientTests.CreateSession([]);
            using var cancellation = new CancellationTokenSource();
            var factory = new GmsResultClientFactory(_ =>
            {
                cancellation.Cancel();
                return new ValueTask<ISession>(session.Object);
            }, telemetry);
            await Assert.ThrowsAsync<ArgumentException>(async () => await factory.CreateAsync(NodeId.Null));
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await factory.CreateAsync(new NodeId("gms", 1), cancellation.Token));
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await factory.CreateAsync(new NodeId("gms", 1), cancellation.Token));
            Assert.Throws<ArgumentNullException>(() => _ = new GmsResultClientFactory(null!, telemetry));
            Assert.Throws<ArgumentNullException>(() => _ = new GmsResultClientFactory(
                _ => new ValueTask<ISession>(session.Object), null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new GmsResultClientFactory(
                _ => new ValueTask<ISession>(session.Object), telemetry, new GmsResultLimits { MaximumFileBytes = 0 }));
            Assert.Throws<ArgumentNullException>(() => OpcUaGmsResultClientExtensions.AddGmsResultsClient(null!));
            session.Verify(value => value.Dispose(), Times.Never);
        }

        /// <summary>
        /// The client-builder registration obtains the already-connected managed session and registers once.
        /// </summary>
        [Test]
        [NonParallelizable]
        public async Task DependencyInjectionUsesTheExistingManagedSessionOverTcp()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineHandle<GMSState> machine = await fixture.Stage("FactoryGms").WithGmsResults(
                new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()),
                out GmsResultBinding binding).BuildAsync();
            await binding.CreatePublisher(machine).PublishAsync(GmsResultStoreTests.Result("factory-result"));
            ISession session = await fixture.ConnectAsync(managed: true);
            var managed = (ManagedSession)session;
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            var connected = new TaskCompletionSource<ManagedSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            connected.SetResult(managed);
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                _ => connected.Task);
            var builder = new Mock<IOpcUaClientBuilder>(MockBehavior.Strict);
            builder.SetupGet(value => value.Services).Returns(services);
            Assert.That(builder.Object.AddGmsResultsClient(), Is.SameAs(builder.Object));
            builder.Object.AddGmsResultsClient();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetServices<IGmsResultClientFactory>().Count(), Is.EqualTo(1));
            GmsResultClient client = await provider.GetRequiredService<IGmsResultClientFactory>()
                .CreateAsync(machine.NodeId);
            Assert.That(client.Session, Is.SameAs(session));
            Assert.That((await client.GetLatestAsync()).ResultMetaData!.ResultId, Is.EqualTo("factory-result"));
            Assert.That(session.Connected, Is.True);
        }
    }
}
