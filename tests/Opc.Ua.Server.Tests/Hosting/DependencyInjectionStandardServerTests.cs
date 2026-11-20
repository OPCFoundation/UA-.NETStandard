// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Hosting
{
    [TestFixture]
    [Category("Hosting")]
    [Parallelizable]
    public class DependencyInjectionStandardServerTests
    {
        /// <summary>
        /// A monitored item queue factory resolved from the container is owned by the
        /// container, so the server must not dispose it on stop (restart or a second
        /// server from the same container would otherwise receive a disposed factory).
        /// </summary>
        [Test]
        public void ContainerMonitoredItemQueueFactoryIsNotOwnedByTheServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(isServer: true);
            using var containerFactory = new MonitoredItemQueueFactory(telemetry);
            var services = new ServiceCollection();
            services.AddSingleton<IMonitoredItemQueueFactory>(containerFactory);
            using ServiceProvider provider = services.BuildServiceProvider();
            using var server = new OwnershipProbeServer(provider, telemetry);

            Assert.That(server.Owns(containerFactory), Is.False);
            using var createdFactory = new MonitoredItemQueueFactory(telemetry);
            Assert.That(server.Owns(createdFactory), Is.True);
        }

        private sealed class OwnershipProbeServer : DependencyInjectionStandardServer
        {
            public OwnershipProbeServer(IServiceProvider services, ITelemetryContext telemetry)
                : base(services, telemetry, TimeProvider.System)
            {
            }

            public bool Owns(IMonitoredItemQueueFactory factory)
            {
                return OwnsMonitoredItemQueueFactory(factory);
            }
        }
    }
}
