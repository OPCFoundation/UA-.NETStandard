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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Direct unit tests for
    /// <see cref="OpcUaServerDiBuilderExtensions"/> — focusing on
    /// argument validation, TryAddSingleton semantics, and
    /// per-overload behaviour of <c>ConfigureDevicesFor</c>. The
    /// orchestration paths are covered by
    /// <see cref="HostingIntegrationTests"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Hosting")]
    public sealed class OpcUaServerDiBuilderExtensionsTests
    {
        [Test]
        public void AddOpcUaDiThrowsOnNullBuilder()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => OpcUaServerDiBuilderExtensions.AddOpcUaDi(builder: null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("builder"));
        }

        [Test]
        public void ConfigureDevicesForSyncThrowsOnNullBuilder()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => OpcUaServerDiBuilderExtensions
                    .ConfigureDevicesFor<DiNodeManager>(
                        builder: null!,
                        configure: (Action<IDiPostSetupContext>)(_ => { })))!;
            Assert.That(ex.ParamName, Is.EqualTo("builder"));
        }

        [Test]
        public void ConfigureDevicesForSyncThrowsOnNullConfigure()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder serverBuilder = services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "test"; });

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => serverBuilder.ConfigureDevicesFor<DiNodeManager>(
                    (Action<IDiPostSetupContext>)null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("configure"));
        }

        [Test]
        public void ConfigureDevicesForAsyncThrowsOnNullBuilder()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => OpcUaServerDiBuilderExtensions
                    .ConfigureDevicesFor<DiNodeManager>(
                        builder: null!,
                        configure: (Func<IDiPostSetupContext, ValueTask>)(_ => default)))!;
            Assert.That(ex.ParamName, Is.EqualTo("builder"));
        }

        [Test]
        public void ConfigureDevicesForAsyncThrowsOnNullConfigure()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder serverBuilder = services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "test"; });

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => serverBuilder.ConfigureDevicesFor<DiNodeManager>(
                    (Func<IDiPostSetupContext, ValueTask>)null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("configure"));
        }

        [Test]
        public void ConfigureDevicesForWithoutAddOpcUaDiRegistersRunner()
        {
            // The runner is registered via TryAddSingleton from inside
            // ConfigureDevicesFor as well — AddOpcUaDi() is not required
            // for the configurator pipeline to be reachable.
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "test"; })
                .ConfigureDevicesFor<DiNodeManager>(_ => { });

            ServiceProvider provider = services.BuildServiceProvider();
            var runner = provider.GetService<IDiPostSetupRunner>();

            Assert.That(runner, Is.Not.Null);
            Assert.That(runner, Is.InstanceOf<DiPostSetupRunner>());
        }

        [Test]
        public void AddOpcUaDiRegistersConcreteDiPostSetupRunner()
        {
            // AddOpcUaDi must register the concrete DiPostSetupRunner
            // (not just any IDiPostSetupRunner), so resolution from the
            // hosted-service graph returns the canonical implementation.
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "test"; })
                .AddOpcUaDi();

            ServiceProvider provider = services.BuildServiceProvider();
            IDiPostSetupRunner runner = provider.GetRequiredService<IDiPostSetupRunner>();

            Assert.That(runner.GetType(), Is.EqualTo(typeof(DiPostSetupRunner)));
        }

        [Test]
        public async Task ConfigureDevicesForSyncWrapsActionAsCompletedValueTask()
        {
            // The synchronous overload wraps the action in a Func that
            // returns ValueTask.CompletedTask. Verify by inspecting the
            // registered configurator's TargetManagerType and observing
            // that the action runs synchronously to completion.
            IServiceCollection services = new ServiceCollection();
            bool ran = false;
            services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "test"; })
                .ConfigureDevicesFor<DiNodeManager>(_ => { ran = true; });

            ServiceProvider provider = services.BuildServiceProvider();
            IDiPostSetupConfigurator configurator =
                provider.GetServices<IDiPostSetupConfigurator>().Single();

            Assert.That(configurator.TargetManagerType, Is.EqualTo(typeof(DiNodeManager)));

            ValueTask task = configurator.RunAsync(new FakePostSetupContext());
            Assert.That(task.IsCompletedSuccessfully, Is.True,
                "Sync ConfigureDevicesFor should return a completed ValueTask.");
            await task.ConfigureAwait(false);
            Assert.That(ran, Is.True);
        }

        [Test]
        public void MultipleConfigureDevicesForRegistrationsAccumulate()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => { o.ApplicationName = "test"; })
                .ConfigureDevicesFor<DiNodeManager>(_ => { })
                .ConfigureDevicesFor<DiNodeManager>((Func<IDiPostSetupContext, ValueTask>)
                    (_ => default))
                .ConfigureDevicesFor<DiNodeManager>(_ => { });

            ServiceProvider provider = services.BuildServiceProvider();
            IEnumerable<IDiPostSetupConfigurator> configurators =
                provider.GetServices<IDiPostSetupConfigurator>();

            Assert.That(configurators.Count(), Is.EqualTo(3));
            Assert.That(
                configurators.Select(c => c.TargetManagerType),
                Is.All.EqualTo(typeof(DiNodeManager)));
        }

        /// <summary>
        /// Minimal stub used to drive a registered configurator's
        /// <see cref="IDiPostSetupConfigurator.RunAsync"/> without
        /// booting a full server. The sync ConfigureDevicesFor wrapper
        /// only invokes the user-supplied action; it never touches
        /// the context.
        /// </summary>
        private sealed class FakePostSetupContext : IDiPostSetupContext
        {
            public DiNodeManager Manager => throw new NotSupportedException();

            public System.Threading.CancellationToken CancellationToken => default;

            public T GetRequiredService<T>() where T : notnull
                => throw new NotSupportedException();

            public ValueTask<Opc.Ua.Di.Server.Builders.IDeviceBuilder<DeviceState>> CreateDeviceAsync(
                QualifiedName browseName,
                NodeState? parent = null)
                => throw new NotSupportedException();

            public ValueTask<Opc.Ua.Di.Server.Builders.IDeviceBuilder<TDevice>> CreateDeviceAsync<TDevice>(
                QualifiedName browseName,
                NodeId typeDefinitionId,
                Func<NodeState, TDevice> factory,
                NodeState? parent = null)
                where TDevice : ComponentState
                => throw new NotSupportedException();

            public Opc.Ua.Di.Server.Builders.IDeviceBuilder<TDevice> Device<TDevice>(NodeId nodeId)
                where TDevice : ComponentState
                => throw new NotSupportedException();

            public Opc.Ua.Di.Server.Builders.IDeviceBuilder<TDevice> DeviceByBrowseName<TDevice>(
                QualifiedName browseName,
                NodeState? parent = null)
                where TDevice : ComponentState
                => throw new NotSupportedException();
        }
    }
}
