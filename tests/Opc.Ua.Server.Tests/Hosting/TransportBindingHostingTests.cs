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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Verifies that the hosted OPC UA server consumes the
    /// <see cref="ITransportBindingRegistry"/> registered in dependency
    /// injection (populated by <c>AddHttpsTransport()</c> /
    /// <c>AddKestrelOpcTcpTransport()</c> / <c>AddCustomTransport()</c> etc.)
    /// rather than silently falling back to a TCP-only registry.
    /// </summary>
    [TestFixture]
    [Category("Hosting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class TransportBindingHostingTests
    {
        [Test]
        public async Task HostedServerUsesDependencyInjectedTransportBindingRegistryAsync()
        {
            string testRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(TransportBindingHostingTests),
                Guid.NewGuid().ToString("N"));
            string pkiRoot = Path.Combine(testRoot, "pki");
            Directory.CreateDirectory(testRoot);

            var spy = new SpyTransportBindingRegistry(DefaultTransportBindingRegistry.WithDefaultTcp());

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "TransportBindingHostTest";
                    o.ApplicationUri = "urn:localhost:TransportBindingHostTest";
                    o.ProductUri = "urn:localhost:TransportBindingHostTest:product";
                    o.PkiRoot = pkiRoot;
                    o.AutoAcceptUntrustedCertificates = true;
                    o.IncludeUnsecurePolicyNone = true;
                    o.EndpointUrls.Clear();
                    o.EndpointUrls.Add(
                        "opc.tcp://localhost:" +
                        GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                        "/TransportBindingHostTest");
                });
            services.AddSingleton<ITransportBindingRegistry>(spy);

            using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService hostedService = provider.GetServices<IHostedService>().Single();

            try
            {
                await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);

                bool queried = await WaitForAsync(() => spy.Queried, TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
                Assert.That(queried, Is.True,
                    "The hosted server must resolve and use the DI-registered transport binding registry.");
            }
            finally
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await hostedService.StopAsync(cts.Token).ConfigureAwait(false);

                if (Directory.Exists(testRoot))
                {
                    try
                    {
                        Directory.Delete(testRoot, recursive: true);
                    }
                    catch (IOException)
                    {
                        // Best-effort cleanup.
                    }
                }
            }
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return condition();
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private sealed class SpyTransportBindingRegistry : ITransportBindingRegistry
        {
            private readonly ITransportBindingRegistry m_inner;

            public SpyTransportBindingRegistry(ITransportBindingRegistry inner)
            {
                m_inner = inner;
            }

            public bool Queried { get; private set; }

            public void RegisterListenerFactory(ITransportListenerFactory factory)
            {
                m_inner.RegisterListenerFactory(factory);
            }

            public void RegisterChannelFactory(ITransportChannelFactory factory)
            {
                m_inner.RegisterChannelFactory(factory);
            }

            public bool RemoveListenerFactory(string uriScheme)
            {
                return m_inner.RemoveListenerFactory(uriScheme);
            }

            public bool RemoveChannelFactory(string uriScheme)
            {
                return m_inner.RemoveChannelFactory(uriScheme);
            }

            public ITransportListenerFactory? GetListenerFactory(string uriScheme)
            {
                Queried = true;
                return m_inner.GetListenerFactory(uriScheme);
            }

            public ITransportChannelFactory? GetChannelFactory(string uriScheme)
            {
                return m_inner.GetChannelFactory(uriScheme);
            }

            public ITransportListener? CreateListener(string uriScheme, ITelemetryContext telemetry)
            {
                Queried = true;
                return m_inner.CreateListener(uriScheme, telemetry);
            }

            public ITransportChannel? CreateChannel(string uriScheme, ITelemetryContext telemetry)
            {
                return m_inner.CreateChannel(uriScheme, telemetry);
            }

            public bool HasListenerFactory(string uriScheme)
            {
                Queried = true;
                return m_inner.HasListenerFactory(uriScheme);
            }

            public bool HasChannelFactory(string uriScheme)
            {
                return m_inner.HasChannelFactory(uriScheme);
            }
        }
    }
}
