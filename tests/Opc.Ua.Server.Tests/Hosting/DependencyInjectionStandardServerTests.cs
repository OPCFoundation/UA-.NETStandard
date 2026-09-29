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
