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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Vision.OpenUsd;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Tests for the OpenUSD DI extension. The extension registers the
    /// scene camera capture provider as a singleton so a host can wire
    /// it into a Vision server or a simulator.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdSceneCameraCaptureServiceCollectionExtensionsTests
    {
        [Test]
        public void AddOpenUsdSceneCameraCaptureProviderThrowsOnNullServices()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OpenUsdSceneCameraCaptureServiceCollectionExtensions
                    .AddOpenUsdSceneCameraCaptureProvider(null!));
        }

        [Test]
        public void AddOpenUsdSceneCameraCaptureProviderInvokesConfigureDelegate()
        {
            IServiceCollection services = new ServiceCollection();
            bool invoked = false;
            services.AddOpenUsdSceneCameraCaptureProvider(_ => invoked = true);

            using ServiceProvider provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<OpenUsdSceneCaptureOptions>();

            Assert.Multiple(() =>
            {
                Assert.That(invoked, Is.True);
                Assert.That(options, Is.Not.Null);
            });
        }

        [Test]
        public void AddOpenUsdSceneCameraCaptureProviderRegistersSingletonProvider()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpenUsdSceneCameraCaptureProvider();

            using ServiceProvider provider = services.BuildServiceProvider();
            var one = provider.GetRequiredService<ISceneCameraCaptureProvider>();
            var two = provider.GetRequiredService<ISceneCameraCaptureProvider>();

            Assert.That(one, Is.SameAs(two));
        }

        [Test]
        public void AddOpenUsdSceneCameraCaptureProviderIsIdempotent()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpenUsdSceneCameraCaptureProvider();
            services.AddOpenUsdSceneCameraCaptureProvider();

            int optionsCount = services.Count(s => s.ServiceType == typeof(OpenUsdSceneCaptureOptions));
            int providerCount = services.Count(s => s.ServiceType == typeof(ISceneCameraCaptureProvider));

            Assert.Multiple(() =>
            {
                Assert.That(optionsCount, Is.EqualTo(1),
                    "TryAddSingleton must not double-register the options record.");
                Assert.That(providerCount, Is.EqualTo(1),
                    "TryAddSingleton must not double-register the provider.");
            });
        }

        [Test]
        public void AddOpenUsdSceneCameraCaptureProviderWithoutConfigureRegistersDefaultOptions()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpenUsdSceneCameraCaptureProvider();

            using ServiceProvider provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<OpenUsdSceneCaptureOptions>();
            var defaults = new OpenUsdSceneCaptureOptions();

            Assert.Multiple(() =>
            {
                Assert.That(options.MaxFrameWidth, Is.EqualTo(defaults.MaxFrameWidth));
                Assert.That(options.MaxFrameHeight, Is.EqualTo(defaults.MaxFrameHeight));
            });
        }
    }
}
