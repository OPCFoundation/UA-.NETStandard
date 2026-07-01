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

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Redundancy;

using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.K8s.Tests
{
    /// <summary>
    /// Unit tests for the Kubernetes server builder fluent registration
    /// (<see cref="KubernetesServerBuilderExtensions"/>).
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class KubernetesServerBuilderExtensionsTests
    {
        [Test]
        public async Task UseKubernetesFeaturesRegisterResolvableServicesAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseKubernetes()
                .UseKubernetesLeaderElection(options => options.UseSharedStoreFallback = false)
                .UseKubernetesPeerDiscovery()
                .UseKubernetesReadiness();

            await using ServiceProvider provider = services.BuildServiceProvider();

            // Outside a cluster the factory yields a not-in-cluster client and the
            // leader election falls back to a static non-leader, all without any IO.
            IKubernetesApiClient apiClient = provider.GetRequiredService<IKubernetesApiClient>();
            Assert.That(apiClient.IsInCluster, Is.False);
            Assert.That(provider.GetRequiredService<ILeaderElection>(), Is.Not.Null);
            Assert.That(provider.GetRequiredService<IKubernetesPeerDiscovery>(), Is.Not.Null);
            Assert.That(provider.GetRequiredService<KubernetesReadinessServer>(), Is.Not.Null);
            Assert.That(provider.GetServices<IServerStartupTask>(), Is.Not.Empty);
        }

        [Test]
        public void BuilderExtensionsRejectNullBuilder()
        {
            Assert.That(
                () => ((IOpcUaServerBuilder)null!).UseKubernetes(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaServerBuilder)null!).UseKubernetesLeaderElection(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaServerBuilder)null!).UseKubernetesPeerDiscovery(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaServerBuilder)null!).UseKubernetesReadiness(),
                Throws.ArgumentNullException);
        }
    }
}