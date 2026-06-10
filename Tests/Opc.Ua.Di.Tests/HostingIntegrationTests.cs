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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Unit tests for the DI server hosting integration
    /// (<c>AddOpcUaDi</c>, <c>ConfigureDevicesFor</c>,
    /// <see cref="DiPostSetupRunner"/>). These tests exercise the
    /// service-collection registrations and the runner orchestration
    /// without booting a full hosted server — see
    /// <see cref="DeviceBuilderTests"/> for end-to-end coverage.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Hosting")]
    public sealed class HostingIntegrationTests
    {
        private static readonly int[] s_expectedConfigOrder = [1, 2, 3];

        // ---------------------------------------------------------------
        // Service registration
        // ---------------------------------------------------------------

        [Test]
        public void AddOpcUaDiRegistersDiNodeManagerFactory()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "test"; })
                .AddOpcUaDi();

            ServiceProvider provider = services.BuildServiceProvider();
            var factory = provider.GetService<DiNodeManagerFactory>();
            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void AddOpcUaDiRegistersPostSetupRunner()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "test"; })
                .AddOpcUaDi();

            ServiceProvider provider = services.BuildServiceProvider();
            var runner = provider.GetService<IDiPostSetupRunner>();
            Assert.That(runner, Is.Not.Null);
            Assert.That(runner, Is.InstanceOf<DiPostSetupRunner>());
        }

        [Test]
        public void AddOpcUaDiCalledTwiceThrows()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder serverBuilder = services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "test"; });

            serverBuilder.AddOpcUaDi();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => serverBuilder.AddOpcUaDi())!;
            Assert.That(ex.Message, Does.Contain("AddOpcUaDi has already been called"));
        }

        [Test]
        public void ConfigureDevicesForRegistersConfigurator()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "test"; })
                .ConfigureDevicesFor<DiNodeManager>(_ => { });

            ServiceProvider provider = services.BuildServiceProvider();
            IEnumerable<IDiPostSetupConfigurator> configurators =
                provider.GetServices<IDiPostSetupConfigurator>();

            Assert.That(configurators, Has.Exactly(1).Items);
            Assert.That(
                System.Linq.Enumerable.First(configurators).TargetManagerType,
                Is.EqualTo(typeof(DiNodeManager)));
        }

        [Test]
        public void MultipleConfigureDevicesForCallsAccumulate()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "test"; })
                .ConfigureDevicesFor<DiNodeManager>(_ => { })
                .ConfigureDevicesFor<DiNodeManager>(_ => { });

            ServiceProvider provider = services.BuildServiceProvider();
            IEnumerable<IDiPostSetupConfigurator> configurators =
                provider.GetServices<IDiPostSetupConfigurator>();

            Assert.That(configurators, Has.Exactly(2).Items);
        }

        // ---------------------------------------------------------------
        // Runner orchestration
        // ---------------------------------------------------------------

        [Test]
        public async Task RunnerInvokesMatchingConfiguratorsInOrder()
        {
            DiServerFixture fixture = new DiServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                var calls = new List<int>();
                DiPostSetupRunner runner = BuildRunner(
                    Configurator(typeof(DiNodeManager), _ => { calls.Add(1); return default; }),
                    Configurator(typeof(DiNodeManager), _ => { calls.Add(2); return default; }),
                    Configurator(typeof(DiNodeManager), _ => { calls.Add(3); return default; }));

                await runner.RunAsync(fixture.Manager, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(calls, Is.EqualTo(s_expectedConfigOrder));
            }
            finally
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RunnerFiltersByTargetManagerType()
        {
            DiServerFixture fixture = new DiServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                bool diRan = false;
                bool unrelatedRan = false;
                DiPostSetupRunner runner = BuildRunner(
                    Configurator(typeof(DiNodeManager), _ => { diRan = true; return default; }),
                    Configurator(
                        typeof(UnrelatedSubclass),
                        _ => { unrelatedRan = true; return default; }));

                await runner.RunAsync(fixture.Manager, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(diRan, Is.True);
                Assert.That(unrelatedRan, Is.False,
                    "Configurator targeting an unrelated subclass should not run.");
            }
            finally
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RunnerPropagatesConfiguratorException()
        {
            DiServerFixture fixture = new DiServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                DiPostSetupRunner runner = BuildRunner(
                    Configurator(typeof(DiNodeManager), _ =>
                        throw new InvalidOperationException("boom")));

                InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await runner.RunAsync(
                        fixture.Manager, CancellationToken.None).ConfigureAwait(false))!;

                Assert.That(ex.Message, Does.Contain("DI post-setup configurator"));
                Assert.That(ex.InnerException, Is.Not.Null);
                Assert.That(ex.InnerException!.Message, Is.EqualTo("boom"));
            }
            finally
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConfigureDevicesForRunsConfiguratorOnDiNodeManager()
        {
            DiServerFixture fixture = new DiServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                IServiceCollection services = new ServiceCollection();
                bool wasCalled = false;
                services.AddOpcUa()
                    .AddServer(o => { o.ApplicationName = "test"; })
                    .ConfigureDevicesFor<DiNodeManager>(ctx =>
                    {
                        wasCalled = true;
                        Assert.That(ctx.Manager, Is.SameAs(fixture.Manager));
                    });
                services.AddSingleton<IDiPostSetupRunner, DiPostSetupRunner>();

                ServiceProvider provider = services.BuildServiceProvider();
                IDiPostSetupRunner runner = provider.GetRequiredService<IDiPostSetupRunner>();

                await runner.RunAsync(fixture.Manager, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(wasCalled, Is.True);
            }
            finally
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ContextExposesNarrowGetRequiredService()
        {
            DiServerFixture fixture = new DiServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                IServiceCollection services = new ServiceCollection();
                services.AddSingleton<SomeApplicationService>();
                services.AddOpcUa()
                    .AddServer(o => { o.ApplicationName = "test"; })
                    .ConfigureDevicesFor<DiNodeManager>(ctx =>
                    {
                        SomeApplicationService svc = ctx.GetRequiredService<SomeApplicationService>();
                        svc.Touched = true;
                    });
                services.AddSingleton<IDiPostSetupRunner, DiPostSetupRunner>();

                ServiceProvider provider = services.BuildServiceProvider();
                IDiPostSetupRunner runner = provider.GetRequiredService<IDiPostSetupRunner>();
                SomeApplicationService svc =
                    provider.GetRequiredService<SomeApplicationService>();

                await runner.RunAsync(fixture.Manager, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(svc.Touched, Is.True);
            }
            finally
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        // ---------------------------------------------------------------
        // Test helpers
        // ---------------------------------------------------------------

        private static DiPostSetupRunner BuildRunner(params IDiPostSetupConfigurator[] configurators)
        {
            IServiceCollection services = new ServiceCollection();
            foreach (IDiPostSetupConfigurator c in configurators)
            {
                services.AddSingleton(c);
            }
            ServiceProvider sp = services.BuildServiceProvider();
            return new DiPostSetupRunner(sp, sp.GetServices<IDiPostSetupConfigurator>());
        }

        private static InlineConfigurator Configurator(
            Type target,
            Func<IDiPostSetupContext, ValueTask> run)
            => new InlineConfigurator(target, run);

        private sealed class InlineConfigurator : IDiPostSetupConfigurator
        {
            private readonly Func<IDiPostSetupContext, ValueTask> m_run;

            public InlineConfigurator(Type target, Func<IDiPostSetupContext, ValueTask> run)
            {
                TargetManagerType = target;
                m_run = run;
            }

            public Type TargetManagerType { get; }

            public ValueTask RunAsync(IDiPostSetupContext context) => m_run(context);
        }

        // Dummy subclass used to verify TargetManagerType filtering.
        // Referenced only via typeof(); never instantiated, hence CA1812.
#pragma warning disable CA1812
        private sealed class UnrelatedSubclass : DiNodeManager
        {
            private UnrelatedSubclass(IServerInternal s, ApplicationConfiguration c)
                : base(s, c) { }
        }

        /// <summary>
        /// Activated by Microsoft.Extensions.DependencyInjection.AddSingleton;
        /// the analyzer doesn't see runtime DI activation, hence CA1812.
        /// </summary>
        private sealed class SomeApplicationService
        {
            public bool Touched { get; set; }
        }
#pragma warning restore CA1812
    }
}
