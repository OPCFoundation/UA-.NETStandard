// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// Coverage for <see cref="PubSubServerOptions"/> defaults and the
    /// configuration binding source generator.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1", Summary = "PublishSubscribe Object options")]
    public class PubSubServerOptionsTests
    {
        [Test]
        public void Defaults_AreSpecCompliant()
        {
            var options = new PubSubServerOptions();
            Assert.Multiple(() =>
            {
                Assert.That(options.ExposeSecurityKeyService, Is.False);
                Assert.That(options.ExposeConfigurationMethods, Is.True);
                Assert.That(options.DefaultSecurityGroupId, Is.Null);
                Assert.That(options.DefaultSecurityPolicyUri, Is.Null);
                Assert.That(options.DefaultKeyLifetimeMs, Is.EqualTo(3_600_000));
                Assert.That(options.DiagnosticsExposure, Is.EqualTo(PubSubDiagnosticsExposure.Counters));
            });
        }

        [Test]
        public void Configuration_BindingRoundTripsAllProperties()
        {
            var inMemory = new Dictionary<string, string?>
            {
                ["OpcUa:Server:PubSub:ExposeSecurityKeyService"] = "true",
                ["OpcUa:Server:PubSub:ExposeConfigurationMethods"] = "false",
                ["OpcUa:Server:PubSub:DefaultSecurityGroupId"] = "g1",
                ["OpcUa:Server:PubSub:DefaultSecurityPolicyUri"] =
                    "http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes128-CTR",
                ["OpcUa:Server:PubSub:DefaultKeyLifetimeMs"] = "900000",
                ["OpcUa:Server:PubSub:DiagnosticsExposure"] = "Full"
            };
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemory)
                .Build();

            var services = new ServiceCollection();
            services.AddOptions<PubSubServerOptions>().Bind(config.GetSection("OpcUa:Server:PubSub"));

            using ServiceProvider sp = services.BuildServiceProvider();
            PubSubServerOptions opts = sp.GetRequiredService<IOptions<PubSubServerOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(opts.ExposeSecurityKeyService, Is.True);
                Assert.That(opts.ExposeConfigurationMethods, Is.False);
                Assert.That(opts.DefaultSecurityGroupId, Is.EqualTo("g1"));
                Assert.That(opts.DefaultSecurityPolicyUri,
                    Is.EqualTo("http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes128-CTR"));
                Assert.That(opts.DefaultKeyLifetimeMs, Is.EqualTo(900_000));
                Assert.That(opts.DiagnosticsExposure, Is.EqualTo(PubSubDiagnosticsExposure.Full));
            });
        }

        [Test]
        public void Enum_AllValuesDistinct()
        {
            Assert.Multiple(() =>
            {
                Assert.That((int)PubSubDiagnosticsExposure.None, Is.Zero);
                Assert.That((int)PubSubDiagnosticsExposure.Counters, Is.EqualTo(1));
                Assert.That((int)PubSubDiagnosticsExposure.Errors, Is.EqualTo(2));
                Assert.That((int)PubSubDiagnosticsExposure.Full, Is.EqualTo(3));
            });
        }
    }
}
