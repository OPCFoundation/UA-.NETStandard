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
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    [TestFixture]
    public sealed class RepositorySamplePortTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task OccupiedPortFailureIsExplicitAndBounded(bool denied)
        {
            var binder = new Mock<IRepositorySamplePortBinder>(MockBehavior.Strict);
            binder.Setup(value => value.BindAsync(It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromException<IRepositorySamplePortBinding>(
                    new SocketException((int)(denied ? SocketError.AccessDenied : SocketError.AddressAlreadyInUse))));
            var allocator = new RepositorySamplePortAllocator(binder.Object);

            await Assert.ThatAsync(
                () => allocator.ReserveAsync(CancellationToken.None).AsTask(),
                Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
            binder.Verify(value => value.BindAsync(It.IsAny<CancellationToken>()), Times.Exactly(16));
        }

        [Test]
        public async Task ReleasedSocketRemainsReservedUntilLeaseDisposal()
        {
            int port = Interlocked.Increment(ref s_port);
            var bindings = new List<Mock<IRepositorySamplePortBinding>>();
            var binder = new Mock<IRepositorySamplePortBinder>(MockBehavior.Strict);
            binder.Setup(value => value.BindAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                Mock<IRepositorySamplePortBinding> binding = Binding(port);
                bindings.Add(binding);
                return ValueTask.FromResult(binding.Object);
            });
            var allocator = new RepositorySamplePortAllocator(binder.Object);
            var competitor = new RepositorySamplePortAllocator(binder.Object);
            IRepositorySamplePortLease first = await allocator.ReserveAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await using (first.ConfigureAwait(false))
            {
                await first.ReleaseSocketForLaunchAsync().ConfigureAwait(false);
                bindings[0].Verify(value => value.DisposeAsync(), Times.Once);
                await Assert.ThatAsync(
                    () => competitor.ReserveAsync(CancellationToken.None).AsTask(),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
                Assert.That(bindings, Has.Count.EqualTo(17));
                await first.DisposeAsync().ConfigureAwait(false);
                IRepositorySamplePortLease second = await competitor.ReserveAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                await using (second.ConfigureAwait(false))
                {
                    Assert.That(second.Port, Is.EqualTo(port));
                    await first.DisposeAsync().ConfigureAwait(false);
                    await Assert.ThatAsync(
                        () => allocator.ReserveAsync(CancellationToken.None).AsTask(),
                        Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
                }
            }
            foreach (Mock<IRepositorySamplePortBinding> binding in bindings)
            {
                binding.Verify(value => value.DisposeAsync(), Times.Once);
            }
        }

        [Test]
        public async Task CancellationDuringBindingDisposesTheLateReturnedSocket()
        {
            int port = Interlocked.Increment(ref s_port);
            var returned = new TaskCompletionSource<IRepositorySamplePortBinding>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IRepositorySamplePortBinding> binding = Binding(port);
            var binder = new Mock<IRepositorySamplePortBinder>(MockBehavior.Strict);
            binder.Setup(value => value.BindAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IRepositorySamplePortBinding>(
                    returned.Task.WaitAsync(RepositorySampleTestContext.Bound, CancellationToken.None)));
            var allocator = new RepositorySamplePortAllocator(binder.Object);
            using var cancellation = new CancellationTokenSource();
            Task<IRepositorySamplePortLease> reserving = allocator.ReserveAsync(cancellation.Token).AsTask();
            await cancellation.CancelAsync().ConfigureAwait(false);
            returned.TrySetResult(binding.Object);

            await Assert.ThatAsync(
                () => reserving.WaitAsync(RepositorySampleTestContext.Bound),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            binding.Verify(value => value.DisposeAsync(), Times.Once);

            Mock<IRepositorySamplePortBinding> next = Binding(port);
            binder.Setup(value => value.BindAsync(It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(next.Object));
            IRepositorySamplePortLease lease = await allocator.ReserveAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await using (lease.ConfigureAwait(false))
            {
                Assert.That(lease.Port, Is.EqualTo(port));
            }
            next.Verify(value => value.DisposeAsync(), Times.Once);
        }

        [TestCase(0)]
        [TestCase(1023)]
        [TestCase(65536)]
        public async Task InvalidBindingPortIsRejectedAndDisposed(int port)
        {
            Mock<IRepositorySamplePortBinding> binding = Binding(port);
            var binder = new Mock<IRepositorySamplePortBinder>(MockBehavior.Strict);
            binder.Setup(value => value.BindAsync(It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(binding.Object));
            var allocator = new RepositorySamplePortAllocator(binder.Object);

            await Assert.ThatAsync(
                () => allocator.ReserveAsync(CancellationToken.None).AsTask(),
                Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
            binding.Verify(value => value.DisposeAsync(), Times.Once);
            binder.Verify(value => value.BindAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void ConstructionDoesNotBind()
        {
            var binder = new Mock<IRepositorySamplePortBinder>(MockBehavior.Strict);
            _ = new RepositorySamplePortAllocator(binder.Object);
            binder.VerifyNoOtherCalls();
        }

        private static Mock<IRepositorySamplePortBinding> Binding(int port)
        {
            var binding = new Mock<IRepositorySamplePortBinding>(MockBehavior.Strict);
            binding.SetupGet(value => value.Port).Returns(port);
            binding.Setup(value => value.DisposeAsync()).Returns(() => ValueTask.CompletedTask);
            return binding;
        }

        private static int s_port = 59200;
    }
}
