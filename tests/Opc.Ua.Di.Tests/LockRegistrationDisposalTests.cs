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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Verifies lock registration disposal under concurrent cleanup and provider failures.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Locking")]
    [NonParallelizable]
    public sealed class LockRegistrationDisposalTests
    {
        /// <summary>
        /// Starts a real DI node manager for exercising the public registration path.
        /// </summary>
        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new DiServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the DI server after registration tests finish.
        /// </summary>
        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// A delayed concurrent disposal cannot release a replacement lock on the same element.
        /// </summary>
        [Test]
        public async Task ConcurrentDisposeCannotReleaseReplacementLockAsync()
        {
            IDeviceBuilder<DeviceState> builder = await CreateElementAsync("ConcurrentDispose")
                .ConfigureAwait(false);
            DeviceState element = builder.Device;
            using var state = new DefaultLockService();
            using var firstEntered = new ManualResetEventSlim();
            using var secondEntered = new ManualResetEventSlim();
            using var releaseFirst = new ManualResetEventSlim();
            using var releaseSecond = new ManualResetEventSlim();
            int calls = 0;
            var service = CreateProvider(element.NodeId, id =>
            {
                int call = Interlocked.Increment(ref calls);
                if (call == 1)
                {
                    firstEntered.Set();
                    releaseFirst.Wait();
                }
                else if (call == 2)
                {
                    secondEntered.Set();
                    releaseSecond.Wait();
                }
                state.ReleaseElement(id);
            });

            IDisposable registration = await RegisterAsync(element, service.Object).ConfigureAwait(false);
            Task firstDisposer = Task.Run(registration.Dispose);
            Task? secondDisposer = null;
            IDisposable? replacement = null;
            try
            {
                Assert.That(firstEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Task secondTask = Task.Run(registration.Dispose);
                secondDisposer = secondTask;
                Assert.That(
                    SpinWait.SpinUntil(
                        () => secondEntered.IsSet || secondTask.IsCompleted,
                        TimeSpan.FromSeconds(5)),
                    Is.True);

                releaseFirst.Set();
                await firstDisposer.ConfigureAwait(false);
                replacement = await RegisterAsync(element, service.Object).ConfigureAwait(false);
                Assert.That(state.InitLock(Context("replacement"), element.NodeId, "replacement"), Is.Zero);

                releaseSecond.Set();
                await secondTask.ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(calls, Is.EqualTo(1));
                    Assert.That(state.GetState(element.NodeId).Locked, Is.True);
                });
            }
            finally
            {
                releaseFirst.Set();
                releaseSecond.Set();
                if (secondDisposer != null)
                {
                    await Task.WhenAll(firstDisposer, secondDisposer).ConfigureAwait(false);
                }
                else
                {
                    await firstDisposer.ConfigureAwait(false);
                }
                replacement?.Dispose();
            }
        }

        /// <summary>
        /// A failed provider release leaves the registration available for a later cleanup retry.
        /// </summary>
        [Test]
        public async Task FailedElementReleaseCanBeRetriedAsync()
        {
            IDeviceBuilder<DeviceState> builder = await CreateElementAsync("ReleaseRetry")
                .ConfigureAwait(false);
            DeviceState element = builder.Device;
            using var state = new DefaultLockService();
            var failure = new InvalidOperationException("release failed");
            int calls = 0;
            var service = CreateProvider(element.NodeId, id =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw failure;
                }
                state.ReleaseElement(id);
            });

            IDisposable registration = await RegisterAsync(element, service.Object).ConfigureAwait(false);

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
                () => registration.Dispose())!;
            Assert.That(thrown, Is.SameAs(failure));
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await RegisterAsync(element, service.Object).ConfigureAwait(false));

            registration.Dispose();
            IDisposable replacement = await RegisterAsync(element, service.Object).ConfigureAwait(false);
            replacement.Dispose();

            Assert.That(calls, Is.EqualTo(3));
        }

        private async Task<IDeviceBuilder<DeviceState>> CreateElementAsync(string name)
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(name, m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);
            builder.Device.Lock = new LockingServicesState(builder.Device);
            return builder;
        }

        private Task<IDisposable> RegisterAsync(DeviceState element, ILockActivityService service) =>
            m_fixture.Manager.RegisterLockableElementAsync(element, service).AsTask();

        private static Mock<ILockActivityService> CreateProvider(
            NodeId elementId,
            Action<NodeId> release)
        {
            var service = new Mock<ILockActivityService>();
            service.SetupGet(value => value.LockDuration).Returns(TimeSpan.FromMinutes(1));
            service.Setup(value => value.ReleaseElement(elementId)).Callback(() => release(elementId));
            return service;
        }

        private static SessionSystemContext Context(string session)
        {
            return new SessionSystemContext(new Mock<ITelemetryContext>().Object)
            {
                SessionId = new NodeId(session, 2),
                UserIdentity = new UserIdentity(new AnonymousIdentityToken())
            };
        }

        private DiServerFixture m_fixture = null!;
    }
}
