// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="ServiceLevelStartupTask"/> guard paths and the happy-path update of the live
    /// <c>Server.ServiceLevel</c> node from an <see cref="IServiceLevelProvider"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class ServiceLevelStartupTaskTests
    {
        [Test]
        public void ConstructorThrowsOnNullProvider()
        {
            Assert.That(() => new ServiceLevelStartupTask(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void OnServerStartedThrowsOnNullServer()
        {
            using var task = new ServiceLevelStartupTask(new ConstantServiceLevelProvider());

            Assert.That(async () => await task.OnServerStartedAsync(null!).ConfigureAwait(false), Throws.ArgumentNullException);
        }

        [Test]
        public async Task OnServerStartedNoOpsWhenServerObjectMissingAsync()
        {
            using var task = new ServiceLevelStartupTask(new ConstantServiceLevelProvider());
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.ServerObject).Returns((ServerObjectState)null!);

            await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            server.VerifyGet(s => s.ServerObject, Times.Once);
        }

        [Test]
        public async Task OnServerStartedAppliesInitialAndChangedServiceLevelAsync()
        {
            using DiagnosticsServerHarness harness = await DiagnosticsServerHarness.CreateAsync().ConfigureAwait(false);
            var provider = new MutableServiceLevelProvider(150);
            using var task = new ServiceLevelStartupTask(provider);

            await task.OnServerStartedAsync(harness.Server.Object).ConfigureAwait(false);

            Assert.That(harness.ServerObject.ServiceLevel, Is.Not.Null);
            Assert.That(harness.ServerObject.ServiceLevel!.Value, Is.EqualTo((byte)150), "the initial level is applied");

            provider.Raise(42);

            Assert.That(
                harness.ServerObject.ServiceLevel!.Value,
                Is.EqualTo((byte)42),
                "a provider change is mirrored onto the live node");
        }

        [Test]
        public async Task ClockPublishesHealthAndCurrentSessionLoadWithoutAnElectionAsync()
        {
            using DiagnosticsServerHarness harness = await DiagnosticsServerHarness.CreateAsync().ConfigureAwait(false);
            var clock = new FakeTimeProvider();
            await using var election = new StaticLeaderElection(true);
            var sessionManager = new Mock<ISessionManager>();
            List<ISession> sessions = [];
            sessionManager.Setup(manager => manager.GetSessions()).Returns(sessions);
            harness.Server.Setup(server => server.SessionManager).Returns(sessionManager.Object);
            byte health = 245;
            bool ready = true;
            using var provider = new LeaderServiceLevelProvider(election, RedundancySupport.Warm,
                getConnectedClientCount: null, getHealthServiceLevel: () => health, isRoleReady: _ => ready);
            using var task = new ServiceLevelStartupTask(provider, clock);
            await task.OnServerStartedAsync(harness.Server.Object).ConfigureAwait(false);
            PropertyState<byte> serviceLevel = harness.ServerObject.ServiceLevel!;
            List<byte> published = [];
            provider.ServiceLevelChanged += published.Add;
            Assert.That(serviceLevel.Value, Is.EqualTo(245));

            var session = new Mock<ISession>().Object;
            sessions.AddRange([session, session]);
            clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.That(serviceLevel.Value, Is.EqualTo(245));
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(serviceLevel.Value, Is.EqualTo(243));

            health = ServiceLevels.DegradedMaximum;
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.DegradedMaximum));

            ready = false;
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.NoData));

            ready = true;
            health = ServiceLevels.Maximum;
            for (int i = 0; i < 60; i++)
            {
                sessions.Add(session);
            }
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.HealthyMinimum));
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(published, Is.EqualTo(new byte[]
            {
                243, ServiceLevels.DegradedMaximum, ServiceLevels.NoData, ServiceLevels.HealthyMinimum
            }));

            task.Dispose();
            sessions.Clear();
            clock.Advance(TimeSpan.FromSeconds(1));
            provider.SetServiceLevel(ServiceLevels.Maintenance);
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.HealthyMinimum));
        }

        [Test]
        public async Task FailedHealthSamplePublishesNoDataAndRecoversOnNextTickAsync()
        {
            using DiagnosticsServerHarness harness = await DiagnosticsServerHarness.CreateAsync().ConfigureAwait(false);
            var clock = new FakeTimeProvider();
            await using var election = new StaticLeaderElection(true);
            bool sampleFailed = false;
            using var provider = new LeaderServiceLevelProvider(election, getHealthServiceLevel: () =>
                sampleFailed ? throw new InvalidOperationException("Health unavailable") : ServiceLevels.Maximum);
            using var task = new ServiceLevelStartupTask(provider, clock);
            await task.OnServerStartedAsync(harness.Server.Object).ConfigureAwait(false);
            PropertyState<byte> serviceLevel = harness.ServerObject.ServiceLevel!;
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.Maximum));

            sampleFailed = true;
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.NoData));

            sampleFailed = false;
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.Maximum));
        }

        private sealed class MutableServiceLevelProvider : IServiceLevelProvider
        {
            public MutableServiceLevelProvider(byte serviceLevel)
            {
                m_serviceLevel = serviceLevel;
            }

            public event System.Action<byte>? ServiceLevelChanged;

            public byte GetServiceLevel()
            {
                return m_serviceLevel;
            }

            public void Raise(byte serviceLevel)
            {
                m_serviceLevel = serviceLevel;
                ServiceLevelChanged?.Invoke(serviceLevel);
            }

            private byte m_serviceLevel;
        }
    }
}
