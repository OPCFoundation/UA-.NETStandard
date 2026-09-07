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

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Tests.Hosting
{
    /// <summary>
    /// Verifies that <c>AddWotRegistryServer</c> and
    /// <c>AddWotProtocolBinders</c>/<c>AddWotBinder</c>/<c>AddWotBindingExecutor</c>
    /// register the aggregating <see cref="WotProtocolBinderRegistry"/> exactly
    /// once and expose the same singleton instance as both
    /// <see cref="IWotBinderRegistry"/> and <see cref="IWotBindingChannelFactory"/>,
    /// regardless of registration order.
    /// </summary>
    [TestFixture]
    public sealed class OpcUaWotRegistryServerBuilderExtensionsTests
    {
        [Test]
        public void RegistryThenBindersExposesTheSameSingletonForBothInterfaces()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotRegistryServer();
            builder.AddWotProtocolBinders();

            using ServiceProvider sp = services.BuildServiceProvider();

            IWotBinderRegistry registry = sp.GetRequiredService<IWotBinderRegistry>();
            IWotBindingChannelFactory channelFactory = sp.GetRequiredService<IWotBindingChannelFactory>();
            WotProtocolBinderRegistry concrete = sp.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(registry, Is.SameAs(concrete));
            Assert.That(channelFactory, Is.SameAs(concrete));
        }

        [Test]
        public void BindersThenRegistryExposesTheSameSingletonForBothInterfaces()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotProtocolBinders();
            builder.AddWotRegistryServer();

            using ServiceProvider sp = services.BuildServiceProvider();

            IWotBinderRegistry registry = sp.GetRequiredService<IWotBinderRegistry>();
            IWotBindingChannelFactory channelFactory = sp.GetRequiredService<IWotBindingChannelFactory>();
            WotProtocolBinderRegistry concrete = sp.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(registry, Is.SameAs(concrete));
            Assert.That(channelFactory, Is.SameAs(concrete));
        }

        [Test]
        public void RegistryOnlyStillExposesAWorkingRegistryAndChannelFactory()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotRegistryServer();

            using ServiceProvider sp = services.BuildServiceProvider();

            IWotBinderRegistry registry = sp.GetRequiredService<IWotBinderRegistry>();
            IWotBindingChannelFactory channelFactory = sp.GetRequiredService<IWotBindingChannelFactory>();
            WotProtocolBinderRegistry concrete = sp.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(registry, Is.SameAs(concrete));
            Assert.That(channelFactory, Is.SameAs(concrete));
            Assert.That(registry.Capabilities, Is.Empty,
                "With no binders registered, the shared registry advertises no capabilities.");
        }
    }
}
