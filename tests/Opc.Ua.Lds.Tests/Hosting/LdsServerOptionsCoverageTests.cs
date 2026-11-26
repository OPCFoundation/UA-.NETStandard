// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Lds.Server.Hosting;

namespace Opc.Ua.Lds.Tests.Hosting
{
    /// <summary>
    /// Coverage tests for the new option properties on
    /// <see cref="LdsServerOptions"/> (multicast loopback toggle,
    /// extra server capabilities).
    /// </summary>
    [TestFixture]
    [Category("Lds")]
    [Category("Hosting")]
    [Parallelizable]
    public sealed class LdsServerOptionsCoverageTests
    {
        [Test]
        public void DefaultsAreReasonable()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddLdsServer(_ => { });
            using ServiceProvider sp = services.BuildServiceProvider();

            LdsServerOptions opts = sp.GetRequiredService<IOptions<LdsServerOptions>>().Value;
            Assert.That(opts.EnableMulticast, Is.True);
            Assert.That(opts.MulticastLoopbackOnly, Is.False);
            Assert.That(opts.ServerCapabilities, Is.Empty);
        }

        [Test]
        public void MulticastLoopbackOnlyRoundTrip()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddLdsServer(o => o.MulticastLoopbackOnly = true);
            using ServiceProvider sp = services.BuildServiceProvider();

            LdsServerOptions opts = sp.GetRequiredService<IOptions<LdsServerOptions>>().Value;
            Assert.That(opts.MulticastLoopbackOnly, Is.True);
        }

        [Test]
        public void ExtraServerCapabilitiesRoundTrip()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddLdsServer(o =>
            {
                o.ServerCapabilities.Add("DA");
                o.ServerCapabilities.Add("AC");
            });
            using ServiceProvider sp = services.BuildServiceProvider();

            LdsServerOptions opts = sp.GetRequiredService<IOptions<LdsServerOptions>>().Value;
            Assert.That(opts.ServerCapabilities, Has.Count.EqualTo(2));
            Assert.That(opts.ServerCapabilities[0], Is.EqualTo("DA"));
            Assert.That(opts.ServerCapabilities[1], Is.EqualTo("AC"));
        }
    }
}
