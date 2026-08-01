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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Direct unit tests for <see cref="HostedNodeManagerLifecycle"/>: the thin,
    /// process-wide forwarding shim that lets DI-registered NodeManager factories
    /// resolve <see cref="INodeManagerLifecycle"/> before the hosted
    /// <see cref="StandardServer"/> instance exists, and forwards every call to
    /// whichever concrete lifecycle provider is currently attached. Exercised
    /// directly against mocked <see cref="INodeManagerLifecycle"/> targets so no
    /// live server is required.
    /// </summary>
    [TestFixture]
    [Category("Hosting")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class HostedNodeManagerLifecycleTests
    {
        [Test]
        public void RegistrationsThrowsWhenNotAttached()
        {
            var hosted = new HostedNodeManagerLifecycle();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _ = hosted.Registrations)!;
            Assert.That(ex.Message, Does.Contain("not running"));
        }

        [Test]
        public void AddAsyncWithAsyncFactoryThrowsWhenNotAttached()
        {
            var hosted = new HostedNodeManagerLifecycle();
            IAsyncNodeManagerFactory factory = Mock.Of<IAsyncNodeManagerFactory>();

            Assert.That(
                async () => await hosted.AddAsync(factory).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public void AddAsyncWithSyncFactoryThrowsWhenNotAttached()
        {
            var hosted = new HostedNodeManagerLifecycle();
            INodeManagerFactory factory = Mock.Of<INodeManagerFactory>();

            Assert.That(
                async () => await hosted.AddAsync(factory).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ReloadAsyncWithAsyncFactoryThrowsWhenNotAttached()
        {
            var hosted = new HostedNodeManagerLifecycle();
            NodeManagerRegistration registration = NewRegistration();
            IAsyncNodeManagerFactory replacement = Mock.Of<IAsyncNodeManagerFactory>();

            Assert.That(
                async () => await hosted.ReloadAsync(registration, replacement).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ReloadAsyncWithSyncFactoryThrowsWhenNotAttached()
        {
            var hosted = new HostedNodeManagerLifecycle();
            NodeManagerRegistration registration = NewRegistration();
            INodeManagerFactory replacement = Mock.Of<INodeManagerFactory>();

            Assert.That(
                async () => await hosted.ReloadAsync(registration, replacement).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ShadowReloadAsyncWithAsyncFactoryThrowsWhenNotAttached()
        {
            var hosted = new HostedNodeManagerLifecycle();
            NodeManagerRegistration registration = NewRegistration();
            IAsyncNodeManagerFactory replacement = Mock.Of<IAsyncNodeManagerFactory>();

            Assert.That(
                async () => await hosted.ShadowReloadAsync(registration, replacement).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ShadowReloadAsyncWithSyncFactoryThrowsWhenNotAttached()
        {
            var hosted = new HostedNodeManagerLifecycle();
            NodeManagerRegistration registration = NewRegistration();
            INodeManagerFactory replacement = Mock.Of<INodeManagerFactory>();

            Assert.That(
                async () => await hosted.ShadowReloadAsync(registration, replacement).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public void RemoveAsyncThrowsWhenNotAttached()
        {
            var hosted = new HostedNodeManagerLifecycle();
            NodeManagerRegistration registration = NewRegistration();

            Assert.That(
                async () => await hosted.RemoveAsync(registration).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public void AttachThrowsOnNullLifecycle()
        {
            var hosted = new HostedNodeManagerLifecycle();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => hosted.Attach(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public void DetachThrowsOnNullLifecycle()
        {
            var hosted = new HostedNodeManagerLifecycle();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => hosted.Detach(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public void AttachSameInstanceTwiceDoesNotThrow()
        {
            var hosted = new HostedNodeManagerLifecycle();
            INodeManagerLifecycle inner = new Mock<INodeManagerLifecycle>().Object;

            hosted.Attach(inner);

            Assert.DoesNotThrow(() => hosted.Attach(inner));
        }

        [Test]
        public void AttachDifferentInstanceWhileAttachedThrows()
        {
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(new Mock<INodeManagerLifecycle>().Object);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => hosted.Attach(new Mock<INodeManagerLifecycle>().Object))!;
            Assert.That(ex.Message, Does.Contain("already attached"));
        }

        [Test]
        public void RegistrationsDelegatesToAttachedLifecycle()
        {
            var hosted = new HostedNodeManagerLifecycle();
            var inner = new Mock<INodeManagerLifecycle>();
            var expected = ArrayOf.Wrapped(NewRegistration());
            inner.Setup(l => l.Registrations).Returns(expected);
            hosted.Attach(inner.Object);

            ArrayOf<NodeManagerRegistration> actual = hosted.Registrations;

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public async Task AddAsyncWithAsyncFactoryDelegatesToAttachedLifecycleAsync()
        {
            var hosted = new HostedNodeManagerLifecycle();
            var inner = new Mock<INodeManagerLifecycle>();
            IAsyncNodeManagerFactory factory = Mock.Of<IAsyncNodeManagerFactory>();
            NodeManagerRegistration expected = NewRegistration();
            using var cts = new CancellationTokenSource();
            inner
                .Setup(l => l.AddAsync(factory, cts.Token))
                .Returns(new ValueTask<NodeManagerRegistration>(expected));
            hosted.Attach(inner.Object);

            NodeManagerRegistration actual = await hosted
                .AddAsync(factory, cts.Token)
                .ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(expected));
            inner.Verify(l => l.AddAsync(factory, cts.Token), Times.Once);
        }

        [Test]
        public async Task AddAsyncWithSyncFactoryDelegatesToAttachedLifecycleAsync()
        {
            var hosted = new HostedNodeManagerLifecycle();
            var inner = new Mock<INodeManagerLifecycle>();
            INodeManagerFactory factory = Mock.Of<INodeManagerFactory>();
            NodeManagerRegistration expected = NewRegistration();
            inner
                .Setup(l => l.AddAsync(factory, CancellationToken.None))
                .Returns(new ValueTask<NodeManagerRegistration>(expected));
            hosted.Attach(inner.Object);

            NodeManagerRegistration actual = await hosted.AddAsync(factory).ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(expected));
        }

        [Test]
        public async Task ReloadAsyncWithAsyncFactoryDelegatesToAttachedLifecycleAsync()
        {
            var hosted = new HostedNodeManagerLifecycle();
            var inner = new Mock<INodeManagerLifecycle>();
            NodeManagerRegistration current = NewRegistration();
            NodeManagerRegistration expected = NewRegistration();
            IAsyncNodeManagerFactory replacement = Mock.Of<IAsyncNodeManagerFactory>();
            inner
                .Setup(l => l.ReloadAsync(current, replacement, CancellationToken.None))
                .Returns(new ValueTask<NodeManagerRegistration>(expected));
            hosted.Attach(inner.Object);

            NodeManagerRegistration actual = await hosted
                .ReloadAsync(current, replacement)
                .ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(expected));
        }

        [Test]
        public async Task ReloadAsyncWithSyncFactoryDelegatesToAttachedLifecycleAsync()
        {
            var hosted = new HostedNodeManagerLifecycle();
            var inner = new Mock<INodeManagerLifecycle>();
            NodeManagerRegistration current = NewRegistration();
            NodeManagerRegistration expected = NewRegistration();
            INodeManagerFactory replacement = Mock.Of<INodeManagerFactory>();
            inner
                .Setup(l => l.ReloadAsync(current, replacement, CancellationToken.None))
                .Returns(new ValueTask<NodeManagerRegistration>(expected));
            hosted.Attach(inner.Object);

            NodeManagerRegistration actual = await hosted
                .ReloadAsync(current, replacement)
                .ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(expected));
        }

        [Test]
        public async Task ShadowReloadAsyncWithAsyncFactoryDelegatesToAttachedLifecycleAsync()
        {
            var hosted = new HostedNodeManagerLifecycle();
            var inner = new Mock<INodeManagerLifecycle>();
            NodeManagerRegistration current = NewRegistration();
            NodeManagerRegistration expected = NewRegistration();
            IAsyncNodeManagerFactory replacement = Mock.Of<IAsyncNodeManagerFactory>();
            inner
                .Setup(l => l.ShadowReloadAsync(current, replacement, CancellationToken.None))
                .Returns(new ValueTask<NodeManagerRegistration>(expected));
            hosted.Attach(inner.Object);

            NodeManagerRegistration actual = await hosted
                .ShadowReloadAsync(current, replacement)
                .ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(expected));
        }

        [Test]
        public async Task ShadowReloadAsyncWithSyncFactoryDelegatesToAttachedLifecycleAsync()
        {
            var hosted = new HostedNodeManagerLifecycle();
            var inner = new Mock<INodeManagerLifecycle>();
            NodeManagerRegistration current = NewRegistration();
            NodeManagerRegistration expected = NewRegistration();
            INodeManagerFactory replacement = Mock.Of<INodeManagerFactory>();
            inner
                .Setup(l => l.ShadowReloadAsync(current, replacement, CancellationToken.None))
                .Returns(new ValueTask<NodeManagerRegistration>(expected));
            hosted.Attach(inner.Object);

            NodeManagerRegistration actual = await hosted
                .ShadowReloadAsync(current, replacement)
                .ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(expected));
        }

        [Test]
        public async Task RemoveAsyncDelegatesToAttachedLifecycleAsync()
        {
            var hosted = new HostedNodeManagerLifecycle();
            var inner = new Mock<INodeManagerLifecycle>();
            NodeManagerRegistration registration = NewRegistration();
            inner
                .Setup(l => l.RemoveAsync(registration, CancellationToken.None))
                .Returns(default(ValueTask));
            hosted.Attach(inner.Object);

            await hosted.RemoveAsync(registration).ConfigureAwait(false);

            inner.Verify(l => l.RemoveAsync(registration, CancellationToken.None), Times.Once);
        }

        [Test]
        public void DetachWithMismatchedLifecycleDoesNotClearCurrent()
        {
            var hosted = new HostedNodeManagerLifecycle();
            var inner = new Mock<INodeManagerLifecycle>();
            inner.Setup(l => l.Registrations).Returns(ArrayOf.Wrapped(Array.Empty<NodeManagerRegistration>()));
            hosted.Attach(inner.Object);

            hosted.Detach(new Mock<INodeManagerLifecycle>().Object);

            Assert.DoesNotThrow(() => _ = hosted.Registrations);
        }

        [Test]
        public void DetachWithMatchingLifecycleClearsCurrent()
        {
            var hosted = new HostedNodeManagerLifecycle();
            INodeManagerLifecycle inner = new Mock<INodeManagerLifecycle>().Object;
            hosted.Attach(inner);

            hosted.Detach(inner);

            Assert.Throws<InvalidOperationException>(() => _ = hosted.Registrations);
        }

        [Test]
        public void ReattachAfterDetachSucceeds()
        {
            var hosted = new HostedNodeManagerLifecycle();
            INodeManagerLifecycle first = new Mock<INodeManagerLifecycle>().Object;
            hosted.Attach(first);
            hosted.Detach(first);

            var second = new Mock<INodeManagerLifecycle>();
            second.Setup(l => l.Registrations).Returns(ArrayOf.Wrapped(Array.Empty<NodeManagerRegistration>()));

            Assert.DoesNotThrow(() => hosted.Attach(second.Object));
            Assert.DoesNotThrow(() => _ = hosted.Registrations);
        }

        private static NodeManagerRegistration NewRegistration()
        {
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager.Setup(m => m.NamespaceUris).Returns(["urn:test:hosted-lifecycle"]);
            return new NodeManagerRegistration(Guid.NewGuid(), 1, nodeManager.Object);
        }
    }
}
