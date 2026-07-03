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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

// CA2000: the startup task is disposed deterministically per test; there is no
// cross-test resource leak.
#pragma warning disable CA2000

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for the peer discovery startup task that runs the background EndpointSlice refresh loop for the
    /// lifetime of the OPC UA server.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KubernetesPeerDiscoveryStartupTaskTests
    {
        [Test]
        public void ConstructorRejectsNullDiscovery()
        {
            Assert.That(
                () => new KubernetesPeerDiscoveryStartupTask(null!, new KubernetesPeerDiscoveryOptions()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorRejectsNullOptions()
        {
            Assert.That(
                () => new KubernetesPeerDiscoveryStartupTask(Mock.Of<IKubernetesPeerDiscovery>(), null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task OnServerStartedRejectsNullServerAsync()
        {
            await using var task = new KubernetesPeerDiscoveryStartupTask(
                Mock.Of<IKubernetesPeerDiscovery>(),
                new KubernetesPeerDiscoveryOptions());

            Assert.That(
                async () => await task.OnServerStartedAsync(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task OnServerStartedRunsRefreshLoopUntilDisposedAsync()
        {
            var discovery = new Mock<IKubernetesPeerDiscovery>();
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            discovery.Setup(x => x.RefreshAsync(It.IsAny<CancellationToken>()))
                .Callback(() => signal.TrySetResult())
                .ReturnsAsync(default(ArrayOf<string>));
            var options = new KubernetesPeerDiscoveryOptions { RefreshInterval = TimeSpan.FromMilliseconds(5) };

            var task = new KubernetesPeerDiscoveryStartupTask(discovery.Object, options);
            await task.OnServerStartedAsync(Mock.Of<IServerInternal>(), CancellationToken.None);
            await signal.Task;
            await task.DisposeAsync();

            discovery.Verify(x => x.RefreshAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }
    }
}
