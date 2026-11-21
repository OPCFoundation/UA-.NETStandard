// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

// CA2000: the HttpClient and readiness server are released deterministically
// per test (using / await using); there is no cross-test resource leak.
#pragma warning disable CA2000

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for the readiness startup task that starts the <see cref="KubernetesReadinessServer"/> once the
    /// OPC UA server has started.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KubernetesReadinessStartupTaskTests
    {
        [Test]
        public void ConstructorRejectsNullServer()
        {
            Assert.That(
                () => new KubernetesReadinessStartupTask(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task OnServerStartedRejectsNullServerAsync()
        {
            await using var readiness = new KubernetesReadinessServer(
                new ConstantServiceLevelProvider(255),
                new KubernetesReadinessOptions());
            var task = new KubernetesReadinessStartupTask(readiness);

            Assert.That(
                async () => await task.OnServerStartedAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task OnServerStartedStartsReadinessServerAsync()
        {
            await using ReadinessTestServer readiness = await ReadinessTestServer.StartAsync(
                port => new KubernetesReadinessServer(
                    new ConstantServiceLevelProvider(255),
                    new KubernetesReadinessOptions { Host = "localhost", Port = port }),
                server => new KubernetesReadinessStartupTask(server)
                    .OnServerStartedAsync(Mock.Of<IServerInternal>(), CancellationToken.None)).ConfigureAwait(false);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using HttpResponseMessage response = await client
                .GetAsync(new Uri($"http://localhost:{readiness.Port}/readyz"))
                .ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }
}
