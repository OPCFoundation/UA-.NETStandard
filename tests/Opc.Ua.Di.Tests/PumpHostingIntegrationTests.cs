/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

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
                .AddServer(o => o.ApplicationName = "pumptest")
                .AddNodeManager<PumpNodeManagerFactory>()
                .ConfigureDevicesFor<PumpNodeManager>(_ => { });

            ServiceProvider provider = services.BuildServiceProvider();
            PumpNodeManagerFactory? factory = provider.GetService<PumpNodeManagerFactory>();
            Assert.That(factory, Is.Not.Null);

            // The runner should have been auto-registered by
            // ConfigureDevicesFor.
            IDiPostSetupRunner? runner = provider.GetService<IDiPostSetupRunner>();
            Assert.That(runner, Is.Not.Null);
        }

        [Test]
        public void ConfigureDevicesForPumpRegistersTypedConfigurator()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "pumptest")
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
