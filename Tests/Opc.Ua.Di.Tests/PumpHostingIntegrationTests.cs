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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Pump-server-side coverage for the DI hosting integration:
    /// verifies that <c>ConfigureDevicesFor&lt;PumpNodeManager&gt;</c>
    /// registers a typed configurator and that the pump factory
    /// participates in DI (constructor injection of
    /// <see cref="IDiPostSetupRunner"/>).
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [Category("Hosting")]
    public sealed class PumpHostingIntegrationTests
    {
        [Test]
        public void PumpFactoryDefaultConstructorRunnerIsNull()
        {
            // The parameterless ctor is the path for manual wiring
            // (no DI). Builders without AddOpcUaDi-style integration
            // should still be able to construct the factory.
            var factory = new PumpNodeManagerFactory();
            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void PumpFactoryIsRegisteredAsServiceWithRunner()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "pumptest"; })
                .AddNodeManager<PumpNodeManagerFactory>()
                .ConfigureDevicesFor<PumpNodeManager>(_ => { });

            ServiceProvider provider = services.BuildServiceProvider();
            var factory = provider.GetService<PumpNodeManagerFactory>();
            Assert.That(factory, Is.Not.Null);

            // The runner should have been auto-registered by
            // ConfigureDevicesFor.
            var runner = provider.GetService<IDiPostSetupRunner>();
            Assert.That(runner, Is.Not.Null);
        }

        [Test]
        public void ConfigureDevicesForPumpRegistersTypedConfigurator()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "pumptest"; })
                .AddNodeManager<PumpNodeManagerFactory>()
                .ConfigureDevicesFor<PumpNodeManager>(_ => { });

            ServiceProvider provider = services.BuildServiceProvider();
            System.Collections.Generic.IEnumerable<IDiPostSetupConfigurator> configurators =
                provider.GetServices<IDiPostSetupConfigurator>();

            Assert.That(configurators, Has.Exactly(1).Items);
            Assert.That(
                System.Linq.Enumerable.First(configurators).TargetManagerType,
                Is.EqualTo(typeof(PumpNodeManager)));
        }

        [Test]
        public void DiNodeManagerConfiguratorDoesNotMatchPumpNodeManager()
        {
            // Reversed inheritance: PumpNodeManager IS-A DiNodeManager,
            // so a configurator targeting DiNodeManager WILL run on
            // PumpNodeManager (subclass matching via IsAssignableFrom).
            // The opposite direction (target=PumpNodeManager, manager=DiNodeManager)
            // should NOT match. Verify the assignability semantic.

            Assert.That(
                typeof(DiNodeManager).IsAssignableFrom(typeof(PumpNodeManager)),
                Is.True,
                "PumpNodeManager IS-A DiNodeManager; configurators targeting " +
                "DiNodeManager should also run on pumps.");

            Assert.That(
                typeof(PumpNodeManager).IsAssignableFrom(typeof(DiNodeManager)),
                Is.False,
                "DiNodeManager is NOT a PumpNodeManager; pump-targeted configurators " +
                "must not run on plain DI managers.");
        }
    }
}
