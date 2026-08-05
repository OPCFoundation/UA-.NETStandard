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

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WotBinderRegistryServiceCollectionExtensions"/>:
    /// DI registration, singleton semantics and idempotency.
    /// </summary>
    [TestFixture]
    public sealed class WotBinderRegistryDiTests
    {
        [Test]
        public void EnsureWotBinderRegistryRegistersIWotBinderRegistry()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            var registry = provider.GetService<IWotBinderRegistry>();

            Assert.That(registry, Is.Not.Null);
        }

        [Test]
        public void EnsureWotBinderRegistryRegistersIWotBindingChannelFactory()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            var factory = provider.GetService<IWotBindingChannelFactory>();

            Assert.That(factory, Is.Not.Null);
        }

        [Test]
        public void EnsureWotBinderRegistryBothInterfacesResolveSameSingletonInstance()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IWotBinderRegistry>();
            var factory = provider.GetRequiredService<IWotBindingChannelFactory>();

            Assert.That(object.ReferenceEquals(registry, factory), Is.True);
        }

        [Test]
        public void EnsureWotBinderRegistryIsIdempotent()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            services.EnsureWotBinderRegistry();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            // There should be exactly one WotProtocolBinderRegistry singleton,
            // both interfaces resolve to the same instance.
            var registry = provider.GetRequiredService<IWotBinderRegistry>();
            var factory = provider.GetRequiredService<IWotBindingChannelFactory>();

            Assert.That(object.ReferenceEquals(registry, factory), Is.True);
        }

        [Test]
        public void EnsureWotBinderRegistryNullServicesThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => WotBinderRegistryServiceCollectionExtensions.EnsureWotBinderRegistry(null!));
        }

        [Test]
        public void EnsureWotBinderRegistryConcreteTypeIsAlsoResolvable()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            var concrete = provider.GetService<WotProtocolBinderRegistry>();

            Assert.That(concrete, Is.Not.Null);
        }

        [Test]
        public void EnsureWotBinderRegistryWithNoBinders_RegistryHasNoBinders()
        {
            var services = new ServiceCollection();
            services.EnsureWotBinderRegistry();
            var provider = services.BuildServiceProvider();

            var concrete = provider.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(concrete.Binders, Is.Empty);
        }
    }
}
