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

#nullable enable

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Coverage tests for the new
    /// <see cref="ClientReverseConnectOptions"/> registration path on
    /// <see cref="OpcUaClientOptions.ReverseConnect"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [Parallelizable]
    public sealed class ClientReverseConnectOptionsTests
    {
        [Test]
        public void ReverseConnectOptionDefaultsToNull()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt => opt.Configuration = CreateConfig());

            using ServiceProvider sp = services.BuildServiceProvider();
            var opts = sp.GetRequiredService<OpcUaClientOptions>();
            Assert.That(opts.ReverseConnect, Is.Null);
        }

        [Test]
        public void ReverseConnectManagerIsResolvableWithoutOptions()
        {
            // No options set — the manager should be registered but not
            // started (no endpoints, no exception).
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt => opt.Configuration = CreateConfig());

            using ServiceProvider sp = services.BuildServiceProvider();
            var manager = sp.GetRequiredService<ReverseConnectManager>();
            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public void ReverseConnectOptionsMirrorIntoApplicationConfiguration()
        {
            ApplicationConfiguration config = CreateConfig();
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt =>
            {
                opt.Configuration = config;
                opt.ReverseConnect = new ClientReverseConnectOptions
                {
                    HoldTimeMs = 25000,
                    WaitTimeoutMs = 30000
                };
                opt.ReverseConnect.ClientEndpointUrls.Add("opc.tcp://localhost:14841");
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            // Resolve to trigger the activator.
            ReverseConnectManager manager = sp.GetRequiredService<ReverseConnectManager>();
            try
            {
                Assert.That(config.ClientConfiguration, Is.Not.Null);
                Assert.That(config.ClientConfiguration!.ReverseConnect, Is.Not.Null);
                Assert.That(config.ClientConfiguration.ReverseConnect!.HoldTime, Is.EqualTo(25000));
                Assert.That(
                    config.ClientConfiguration.ReverseConnect.WaitTimeout,
                    Is.EqualTo(30000));
                ArrayOf<ReverseConnectClientEndpoint> endpoints =
                    config.ClientConfiguration.ReverseConnect.ClientEndpoints;
                Assert.That(endpoints.IsNull, Is.False);
                Assert.That(endpoints.Count, Is.EqualTo(1));
                Assert.That(endpoints[0].EndpointUrl, Is.EqualTo("opc.tcp://localhost:14841"));
            }
            finally
            {
                manager.Dispose();
            }
        }

        [Test]
        public void ReverseConnectManagerWithoutConfigurationThrowsClearly()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt =>
            {
                opt.ReverseConnect = new ClientReverseConnectOptions();
                opt.ReverseConnect.ClientEndpointUrls.Add("opc.tcp://localhost:14842");
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(
                () => sp.GetRequiredService<ReverseConnectManager>(),
                Throws.InvalidOperationException);
        }

        private static ApplicationConfiguration CreateConfig()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:client",
                ApplicationName = "test",
                ClientConfiguration = new ClientConfiguration()
            };
        }
    }
}
