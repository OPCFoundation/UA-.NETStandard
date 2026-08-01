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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Http;
using Opc.Ua.WotCon.Bindings.Modbus;
using Opc.Ua.WotCon.Bindings.Mqtt;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for the WoT binding DI builder extension methods.
    /// </summary>
    [TestFixture]
    public sealed class OpcUaWotBindingBuilderExtensionsTests
    {
        private sealed class TestBuilder : IOpcUaBuilder
        {
            public TestBuilder(IServiceCollection services) => Services = services;

            public IServiceCollection Services { get; }
        }

        private static TestBuilder NewBuilder()
        {
            return new TestBuilder(new ServiceCollection());
        }

        private sealed class TestExecutor : IWotBindingExecutor
        {
            public WotBindingIdentity Identity { get; } = new WotBindingIdentity("test.executor", "1.0", "urn:test");

            public bool CanExecute(WotCompiledForm form)
            {
                return false;
            }

            public ValueTask<IWotBindingChannel> ActivateAsync(
                WotCompiledForm form,
                WotExecutorContext context,
                CancellationToken cancellationToken = default)
            {
                throw new System.NotSupportedException();
            }
        }

        [Test]
        public void AddWotProtocolBindersNullBuilderThrowsArgumentNullException()
        {
            Assert.That(
                () => OpcUaWotBindingBuilderExtensions.AddWotProtocolBinders(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddWotProtocolBindersRegistersEightBuiltInBinders()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestBuilder(services);
            builder.AddWotProtocolBinders();
            using ServiceProvider sp = services.BuildServiceProvider();

            WotProtocolBinderRegistry registry = sp.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(registry.Binders, Has.Count.EqualTo(8));
        }

        [Test]
        public void AddWotProtocolBindersIsIdempotent()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestBuilder(services);
            builder.AddWotProtocolBinders();
            builder.AddWotProtocolBinders();
            using ServiceProvider sp = services.BuildServiceProvider();

            WotProtocolBinderRegistry registry = sp.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(registry.Binders, Has.Count.EqualTo(8));
        }

        [Test]
        public void AddWotProtocolBindersReturnsBuilderForChaining()
        {
            IOpcUaBuilder builder = NewBuilder();

            IOpcUaBuilder returned = builder.AddWotProtocolBinders();

            Assert.That(returned, Is.SameAs(builder));
        }

        [Test]
        public void AddWotBinderNullBuilderThrowsArgumentNullException()
        {
            Assert.That(
                () => OpcUaWotBindingBuilderExtensions.AddWotBinder(null!, new ProfinetBindingPlanner()),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddWotBinderNullBinderThrowsArgumentNullException()
        {
            IOpcUaBuilder builder = NewBuilder();

            Assert.That(
                () => builder.AddWotBinder(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddWotBinderRegistersCustomBinder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestBuilder(services);
            builder.AddWotBinder(new ProfinetBindingPlanner());
            using ServiceProvider sp = services.BuildServiceProvider();

            WotProtocolBinderRegistry registry = sp.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(registry.Binders.Any(b => b.Identity.Id == "w3c.profinet"), Is.True);
        }

        [Test]
        public void AddWotBindingExecutorNullBuilderThrowsArgumentNullException()
        {
            var executor = new TestExecutor();

            Assert.That(
                () => OpcUaWotBindingBuilderExtensions.AddWotBindingExecutor(null!, executor),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddWotBindingExecutorNullExecutorThrowsArgumentNullException()
        {
            IOpcUaBuilder builder = NewBuilder();

            Assert.That(
                () => builder.AddWotBindingExecutor(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddWotBindingExecutorRegistersExecutorInServiceCollection()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestBuilder(services);
            var executor = new TestExecutor();
            builder.AddWotBindingExecutor(executor);
            using ServiceProvider sp = services.BuildServiceProvider();

            System.Collections.Generic.IEnumerable<IWotBindingExecutor> executors =
                sp.GetServices<IWotBindingExecutor>();

            Assert.That(executors.Any(e => e is TestExecutor), Is.True);
        }

        [Test]
        public void AddWotCredentialProviderNullBuilderThrowsArgumentNullException()
        {
            Assert.That(
                () => OpcUaWotBindingBuilderExtensions.AddWotCredentialProvider(
                    null!, NullWotCredentialProvider.Instance),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddWotCredentialProviderNullProviderThrowsArgumentNullException()
        {
            IOpcUaBuilder builder = NewBuilder();

            Assert.That(
                () => builder.AddWotCredentialProvider(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddWotCredentialProviderRegistersProviderInServiceCollection()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestBuilder(services);
            builder.AddWotCredentialProvider(NullWotCredentialProvider.Instance);
            using ServiceProvider sp = services.BuildServiceProvider();

            IWotCredentialProvider? resolved = sp.GetService<IWotCredentialProvider>();

            Assert.That(resolved, Is.Not.Null);
        }

        [Test]
        public void AddHttpWotBindingNullBuilderThrowsArgumentNullException()
        {
            Assert.That(
                () => OpcUaHttpWotBindingBuilderExtensions.AddHttpWotBinding(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddHttpWotBindingRegistersBindersAndHttpExecutor()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestBuilder(services);
            builder.AddHttpWotBinding();
            using ServiceProvider sp = services.BuildServiceProvider();

            WotProtocolBinderRegistry registry = sp.GetRequiredService<WotProtocolBinderRegistry>();
            System.Collections.Generic.IEnumerable<IWotBindingExecutor> executors =
                sp.GetServices<IWotBindingExecutor>();

            Assert.That(registry.Binders, Is.Not.Empty);
            Assert.That(executors.Any(e => e is HttpWotBindingExecutor), Is.True);
        }

        [Test]
        public void AddHttpWotBindingWithConfigureDelegateCallsDelegate()
        {
            IOpcUaBuilder builder = NewBuilder();
            bool called = false;

            builder.AddHttpWotBinding(opts => called = true);

            Assert.That(called, Is.True);
        }

        [Test]
        public void AddModbusWotBindingNullBuilderThrowsArgumentNullException()
        {
            Assert.That(
                () => OpcUaModbusWotBindingBuilderExtensions.AddModbusWotBinding(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddModbusWotBindingRegistersBindersAndModbusExecutor()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestBuilder(services);
            builder.AddModbusWotBinding();
            using ServiceProvider sp = services.BuildServiceProvider();

            WotProtocolBinderRegistry registry = sp.GetRequiredService<WotProtocolBinderRegistry>();
            System.Collections.Generic.IEnumerable<IWotBindingExecutor> executors =
                sp.GetServices<IWotBindingExecutor>();

            Assert.That(registry.Binders, Is.Not.Empty);
            Assert.That(executors.Any(e => e is ModbusWotBindingExecutor), Is.True);
        }

        [Test]
        public void AddModbusWotBindingWithConfigureDelegateCallsDelegate()
        {
            IOpcUaBuilder builder = NewBuilder();
            bool called = false;

            builder.AddModbusWotBinding(opts => called = true);

            Assert.That(called, Is.True);
        }

        [Test]
        public void AddOpcUaWotBindingNullBuilderThrowsArgumentNullException()
        {
            Assert.That(
                () => OpcUaTargetWotBindingBuilderExtensions.AddOpcUaWotBinding(null!, _ => { }),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddOpcUaWotBindingNullConfigureThrowsArgumentNullException()
        {
            IOpcUaBuilder builder = NewBuilder();

            Assert.That(
                () => builder.AddOpcUaWotBinding(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddOpcUaWotBindingWithConfigureDelegateCallsDelegate()
        {
            IOpcUaBuilder builder = NewBuilder();
            bool called = false;

            builder.AddOpcUaWotBinding(opts =>
            {
                called = true;
                opts.DisposeSession = false;
            });

            Assert.That(called, Is.True);
        }

        [Test]
        public void AddOpcUaWotBindingRegistersBindersAndOpcUaExecutor()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestBuilder(services);
            builder.AddOpcUaWotBinding(opts => opts.DisposeSession = false);
            using ServiceProvider sp = services.BuildServiceProvider();

            WotProtocolBinderRegistry registry = sp.GetRequiredService<WotProtocolBinderRegistry>();
            System.Collections.Generic.IEnumerable<IWotBindingExecutor> executors =
                sp.GetServices<IWotBindingExecutor>();

            Assert.That(registry.Binders, Is.Not.Empty);
            Assert.That(executors.Any(e => e is OpcUaWotBindingExecutor), Is.True);
        }

        [Test]
        public void AddMqttWotBindingNullBuilderThrowsArgumentNullException()
        {
            Assert.That(
                () => OpcUaMqttWotBindingBuilderExtensions.AddMqttWotBinding(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddMqttWotBindingRegistersBindersAndMqttExecutor()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestBuilder(services);
            builder.AddMqttWotBinding();
            using ServiceProvider sp = services.BuildServiceProvider();

            WotProtocolBinderRegistry registry = sp.GetRequiredService<WotProtocolBinderRegistry>();
            System.Collections.Generic.IEnumerable<IWotBindingExecutor> executors =
                sp.GetServices<IWotBindingExecutor>();

            Assert.That(registry.Binders, Is.Not.Empty);
            Assert.That(executors.Any(e => e is MqttWotBindingExecutor), Is.True);
        }

        [Test]
        public void AddMqttWotBindingWithConfigureDelegateCallsDelegate()
        {
            IOpcUaBuilder builder = NewBuilder();
            bool called = false;

            builder.AddMqttWotBinding(opts => called = true);

            Assert.That(called, Is.True);
        }

        [Test]
        public void RegistryResolvesBindersBothAsIWotBinderRegistryAndIWotBindingChannelFactory()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestBuilder(services);
            builder.AddWotProtocolBinders();
            using ServiceProvider sp = services.BuildServiceProvider();

            IWotBinderRegistry binderRegistry = sp.GetRequiredService<IWotBinderRegistry>();
            IWotBindingChannelFactory factory = sp.GetRequiredService<IWotBindingChannelFactory>();

            Assert.That(object.ReferenceEquals(binderRegistry, factory), Is.True);
        }
    }
}
