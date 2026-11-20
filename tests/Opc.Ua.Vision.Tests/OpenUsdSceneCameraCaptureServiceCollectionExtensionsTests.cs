// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            OpenUsdSceneCaptureOptions options = provider.GetRequiredService<OpenUsdSceneCaptureOptions>();

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
            ISceneCameraCaptureProvider one = provider.GetRequiredService<ISceneCameraCaptureProvider>();
            ISceneCameraCaptureProvider two = provider.GetRequiredService<ISceneCameraCaptureProvider>();

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
            OpenUsdSceneCaptureOptions options = provider.GetRequiredService<OpenUsdSceneCaptureOptions>();
            var defaults = new OpenUsdSceneCaptureOptions();

            Assert.Multiple(() =>
            {
                Assert.That(options.MaxFrameWidth, Is.EqualTo(defaults.MaxFrameWidth));
                Assert.That(options.MaxFrameHeight, Is.EqualTo(defaults.MaxFrameHeight));
            });
        }
    }
}
