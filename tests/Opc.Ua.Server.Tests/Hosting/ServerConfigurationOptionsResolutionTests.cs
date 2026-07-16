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

#nullable enable

using System;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Tests the precedence rules that
    /// <see cref="DependencyInjectionStandardServer.ResolveServerConfigurationOptions"/>
    /// uses to select the Optional OPC 10000-12 §7.10.3
    /// <c>ServerConfiguration</c> value options and merge standalone provider
    /// services into them.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("Hosting")]
    [Parallelizable]
    public sealed class ServerConfigurationOptionsResolutionTests
    {
        [Test]
        public void DirectlyRegisteredInstanceExposesConfiguredValuesAndProviders()
        {
            IServerConfigurationResetProvider resetProvider = Mock.Of<IServerConfigurationResetProvider>();
            IApplicationConfigurationFileProvider fileProvider =
                Mock.Of<IApplicationConfigurationFileProvider>();
            var instance = new ServerConfigurationOptions
            {
                HasSecureElement = true,
                InApplicationSetup = false,
                ConfigurationFileActivityTimeout = 12345.0,
                ResetShutdownDelay = TimeSpan.FromSeconds(42),
                ResetProvider = resetProvider,
                ConfigurationFileProvider = fileProvider
            };

            var services = new ServiceCollection();
            services.AddSingleton(instance);
            using ServiceProvider sp = services.BuildServiceProvider();

            ServerConfigurationOptions? resolved =
                DependencyInjectionStandardServer.ResolveServerConfigurationOptions(sp);

            Assert.That(resolved, Is.SameAs(instance));
            Assert.Multiple(() =>
            {
                Assert.That(resolved!.HasSecureElement, Is.True);
                Assert.That(resolved.InApplicationSetup, Is.False);
                Assert.That(resolved.ConfigurationFileActivityTimeout, Is.EqualTo(12345.0));
                Assert.That(resolved.ResetShutdownDelay, Is.EqualTo(TimeSpan.FromSeconds(42)));
                Assert.That(resolved.ResetProvider, Is.SameAs(resetProvider));
                Assert.That(resolved.ConfigurationFileProvider, Is.SameAs(fileProvider));
            });
        }

        [Test]
        public void DirectlyRegisteredInstanceTakesPrecedenceOverOptionsPattern()
        {
            var instance = new ServerConfigurationOptions { HasSecureElement = true };

            var services = new ServiceCollection();
            services.AddSingleton(instance);
            services.AddOptions<ServerConfigurationOptions>()
                .Configure(o =>
                {
                    o.HasSecureElement = false;
                    o.InApplicationSetup = true;
                });
            using ServiceProvider sp = services.BuildServiceProvider();

            ServerConfigurationOptions? resolved =
                DependencyInjectionStandardServer.ResolveServerConfigurationOptions(sp);

            Assert.That(resolved, Is.SameAs(instance),
                "a directly-registered instance must win over the options-pattern value");
            Assert.Multiple(() =>
            {
                Assert.That(resolved!.HasSecureElement, Is.True);
                Assert.That(resolved.InApplicationSetup, Is.Null,
                    "the options-pattern configuration must not leak into the direct instance");
            });
        }

        [Test]
        public void OptionsPatternOnlyPathResolvesConfiguredValue()
        {
            var services = new ServiceCollection();
            services.AddOptions<ServerConfigurationOptions>()
                .Configure(o =>
                {
                    o.HasSecureElement = true;
                    o.ConfigurationFileActivityTimeout = 999.0;
                });
            using ServiceProvider sp = services.BuildServiceProvider();

            ServerConfigurationOptions? resolved =
                DependencyInjectionStandardServer.ResolveServerConfigurationOptions(sp);

            Assert.That(resolved, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(resolved!.HasSecureElement, Is.True);
                Assert.That(resolved.ConfigurationFileActivityTimeout, Is.EqualTo(999.0));
            });
        }

        [Test]
        public void StandaloneProvidersCreateAndAugmentDefaultOptions()
        {
            IServerConfigurationResetProvider resetProvider = Mock.Of<IServerConfigurationResetProvider>();
            IApplicationConfigurationFileProvider fileProvider =
                Mock.Of<IApplicationConfigurationFileProvider>();

            var services = new ServiceCollection();
            services.AddSingleton(resetProvider);
            services.AddSingleton(fileProvider);
            using ServiceProvider sp = services.BuildServiceProvider();

            ServerConfigurationOptions? resolved =
                DependencyInjectionStandardServer.ResolveServerConfigurationOptions(sp);

            Assert.That(resolved, Is.Not.Null,
                "standalone providers must produce a default options object to carry them");
            Assert.Multiple(() =>
            {
                Assert.That(resolved!.ResetProvider, Is.SameAs(resetProvider));
                Assert.That(resolved.ConfigurationFileProvider, Is.SameAs(fileProvider));
                Assert.That(resolved.HasSecureElement, Is.Null,
                    "no value options were registered, so the defaults must remain unset");
            });
        }

        [Test]
        public void StandaloneProvidersAugmentDirectlyRegisteredInstance()
        {
            IServerConfigurationResetProvider resetProvider = Mock.Of<IServerConfigurationResetProvider>();
            IApplicationConfigurationFileProvider fileProvider =
                Mock.Of<IApplicationConfigurationFileProvider>();
            var instance = new ServerConfigurationOptions { HasSecureElement = true };

            var services = new ServiceCollection();
            services.AddSingleton(instance);
            services.AddSingleton(resetProvider);
            services.AddSingleton(fileProvider);
            using ServiceProvider sp = services.BuildServiceProvider();

            ServerConfigurationOptions? resolved =
                DependencyInjectionStandardServer.ResolveServerConfigurationOptions(sp);

            Assert.That(resolved, Is.SameAs(instance));
            Assert.Multiple(() =>
            {
                Assert.That(resolved!.ResetProvider, Is.SameAs(resetProvider),
                    "a standalone provider must be merged into the selected options");
                Assert.That(resolved.ConfigurationFileProvider, Is.SameAs(fileProvider));
                Assert.That(resolved.HasSecureElement, Is.True);
            });
        }

        [Test]
        public void InstanceProvidersAreNotOverwrittenByStandaloneProviders()
        {
            IServerConfigurationResetProvider ownResetProvider =
                Mock.Of<IServerConfigurationResetProvider>();
            IServerConfigurationResetProvider standaloneResetProvider =
                Mock.Of<IServerConfigurationResetProvider>();
            var instance = new ServerConfigurationOptions { ResetProvider = ownResetProvider };

            var services = new ServiceCollection();
            services.AddSingleton(instance);
            services.AddSingleton(standaloneResetProvider);
            using ServiceProvider sp = services.BuildServiceProvider();

            ServerConfigurationOptions? resolved =
                DependencyInjectionStandardServer.ResolveServerConfigurationOptions(sp);

            Assert.That(resolved, Is.SameAs(instance));
            Assert.That(resolved!.ResetProvider, Is.SameAs(ownResetProvider),
                "a provider already set on the options must not be replaced by a standalone one");
        }

        [Test]
        public void NoOptionsOrProvidersReturnsNull()
        {
            var services = new ServiceCollection();
            using ServiceProvider sp = services.BuildServiceProvider();

            ServerConfigurationOptions? resolved =
                DependencyInjectionStandardServer.ResolveServerConfigurationOptions(sp);

            Assert.That(resolved, Is.Null,
                "with nothing registered the default identity-properties-only path must be used");
        }
    }
}
