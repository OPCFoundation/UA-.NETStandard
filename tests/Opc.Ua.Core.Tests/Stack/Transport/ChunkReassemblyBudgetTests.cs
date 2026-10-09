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
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Unit tests for <see cref="ChunkReassemblyBudget"/>, the shared bound on
    /// what the chunks of incomplete messages may hold across channels.
    /// </summary>
    [TestFixture]
    [Category("TransportChannelDeterministic")]
    [Parallelizable]
    public sealed class ChunkReassemblyBudgetTests
    {
        [TestCase(0L)]
        [TestCase(-1L)]
        public void ConstructorRejectsABudgetThatIsNotPositive(long maxBytes)
        {
            Assert.That(
                () => new ChunkReassemblyBudget(maxBytes),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("maxBytes"));
        }

        [TestCase(-1L)]
        [TestCase(1025L)]
        public void ConstructorRejectsAShareOutsideTheBudget(long maxBytesWithoutSession)
        {
            Assert.That(
                () => new ChunkReassemblyBudget(1024, maxBytesWithoutSession),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo("maxBytesWithoutSession"));
        }

        [Test]
        public void ChannelsWithoutASessionShareHalfTheBudgetByDefault()
        {
            var budget = new ChunkReassemblyBudget(1000);

            Assert.That(budget.MaxBytes, Is.EqualTo(1000));
            Assert.That(budget.MaxBytesWithoutSession, Is.EqualTo(500));
            Assert.That(budget.ReservedBytes, Is.Zero);
        }

        [Test]
        public void ReservationsAreCountedUntilTheBudgetIsFull()
        {
            var budget = new ChunkReassemblyBudget(1000, 1000);

            Assert.That(budget.TryReserve(600, hasSession: false), Is.True);
            Assert.That(budget.TryReserve(400, hasSession: false), Is.True);
            Assert.That(budget.ReservedBytes, Is.EqualTo(1000));

            Assert.That(budget.TryReserve(1, hasSession: true), Is.False);
            Assert.That(budget.ReservedBytes, Is.EqualTo(1000), "a refused reservation must not count.");
        }

        [Test]
        public void ChannelsWithoutASessionCannotTakeTheShareOfSessions()
        {
            var budget = new ChunkReassemblyBudget(1000);

            Assert.That(budget.TryReserve(500, hasSession: false), Is.True);
            Assert.That(budget.TryReserve(1, hasSession: false), Is.False);

            // what is left above the share is for the channels of sessions.
            Assert.That(budget.TryReserve(500, hasSession: true), Is.True);
            Assert.That(budget.TryReserve(1, hasSession: true), Is.False);
            Assert.That(budget.ReservedBytes, Is.EqualTo(1000));
        }

        [Test]
        public void SessionsFillingTheShareShutOutChannelsWithoutASession()
        {
            var budget = new ChunkReassemblyBudget(1000);

            Assert.That(budget.TryReserve(700, hasSession: true), Is.True);

            Assert.That(budget.TryReserve(1, hasSession: false), Is.False);
            Assert.That(budget.TryReserve(300, hasSession: true), Is.True);
        }

        [Test]
        public void AZeroShareRefusesEveryChannelWithoutASession()
        {
            var budget = new ChunkReassemblyBudget(1000, 0);

            Assert.That(budget.TryReserve(1, hasSession: false), Is.False);
            Assert.That(budget.TryReserve(0, hasSession: false), Is.True);
            Assert.That(budget.TryReserve(1000, hasSession: true), Is.True);
        }

        [Test]
        public void ReleasedBytesCanBeReservedAgain()
        {
            var budget = new ChunkReassemblyBudget(1000, 1000);
            Assert.That(budget.TryReserve(1000, hasSession: false), Is.True);

            budget.Release(250);

            Assert.That(budget.ReservedBytes, Is.EqualTo(750));
            Assert.That(budget.TryReserve(250, hasSession: false), Is.True);
            Assert.That(budget.TryReserve(1, hasSession: false), Is.False);
        }

        [Test]
        public void ReleasingMoreThanIsReservedThrowsAndReleasesNothing()
        {
            var budget = new ChunkReassemblyBudget(1000);
            Assert.That(budget.TryReserve(100, hasSession: true), Is.True);

            Assert.That(() => budget.Release(101), Throws.InvalidOperationException);
            Assert.That(budget.ReservedBytes, Is.EqualTo(100));
        }

        [Test]
        public void NegativeByteCountsAreRejected()
        {
            var budget = new ChunkReassemblyBudget(1000);

            Assert.That(
                () => budget.TryReserve(-1, hasSession: true),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => budget.Release(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0, ChunkReassemblyBudget.MaxDefaultMaxBytes)]
        [TestCase(-1, ChunkReassemblyBudget.MaxDefaultMaxBytes)]
        [TestCase(64 * 1024, ChunkReassemblyBudget.MinDefaultMaxBytes)]
        [TestCase(2 * 1024 * 1024, ChunkReassemblyBudget.MinDefaultMaxBytes)]
        [TestCase(4 * 1024 * 1024, ChunkReassemblyBudget.MinDefaultMaxBytes)]
        [TestCase(16 * 1024 * 1024, 256L * 1024 * 1024)]
        [TestCase(100 * 1024 * 1024, ChunkReassemblyBudget.MaxDefaultMaxBytes)]
        [TestCase(512 * 1024 * 1024, 2048L * 1024 * 1024)]
        [TestCase(int.MaxValue, 4L * int.MaxValue)]
        public void DefaultBudgetScalesWithTheMaximumMessageSize(int maxMessageSize, long expected)
        {
            Assert.That(ChunkReassemblyBudget.GetDefaultMaxBytes(maxMessageSize), Is.EqualTo(expected));
        }

        [TestCase(0, ChunkReassemblyBudget.MaxDefaultMaxBytes)]
        [TestCase(4 * 1024 * 1024, ChunkReassemblyBudget.MinDefaultMaxBytes)]
        [TestCase(16 * 1024 * 1024, 256L * 1024 * 1024)]
        public void DefaultFactoryUsesEndpointConfiguration(int maxMessageSize, long expected)
        {
            var configuration = EndpointConfiguration.Create();
            configuration.MaxMessageSize = maxMessageSize;

            ChunkReassemblyBudget budget = ChunkReassemblyBudget.CreateDefault(configuration);

            Assert.That(budget.MaxBytes, Is.EqualTo(expected));
            Assert.That(budget.MaxBytesWithoutSession, Is.EqualTo(expected / 2));
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(ChunkReassemblyBudget.CreateDefault(configuration), Is.Not.SameAs(budget));
        }

        [Test]
        public void DefaultFactoryUsesTransportDefaultsWithoutConfiguration()
        {
            ChunkReassemblyBudget budget = ChunkReassemblyBudget.CreateDefault(null);

            Assert.That(budget.MaxBytes, Is.EqualTo(ChunkReassemblyBudget.MinDefaultMaxBytes));
            Assert.That(budget.MaxBytesWithoutSession, Is.EqualTo(ChunkReassemblyBudget.MinDefaultMaxBytes / 2));
            Assert.That(budget.ReservedBytes, Is.Zero);
        }

        [Test]
        public void DefaultBudgetLetsAChannelWithoutASessionAssembleAMaximumMessage()
        {
            foreach (int maxMessageSize in new[] { 8192, 4 * 1024 * 1024, 256 * 1024 * 1024, int.MaxValue })
            {
                var budget = new ChunkReassemblyBudget(ChunkReassemblyBudget.GetDefaultMaxBytes(maxMessageSize));

                // Chunks may occupy buffers of up to twice their size.
                Assert.That(budget.MaxBytesWithoutSession, Is.GreaterThanOrEqualTo(2L * maxMessageSize));
            }
        }

        [Test]
        public async Task ConcurrentReservationsNeverExceedTheBudgetAsync()
        {
            const int workers = 8;
            const int attemptsPerWorker = 10_000;
            var budget = new ChunkReassemblyBudget(64 * 1024, 64 * 1024);
            long maxObserved = 0;
            using var start = new ManualResetEventSlim();

            var tasks = new Task[workers];
            for (int worker = 0; worker < workers; worker++)
            {
                tasks[worker] = Task.Run(() =>
                {
                    start.Wait();
                    for (int attempt = 0; attempt < attemptsPerWorker; attempt++)
                    {
                        if (budget.TryReserve(1000, hasSession: true))
                        {
                            long reserved = budget.ReservedBytes;
                            long observed;
                            do
                            {
                                observed = Interlocked.Read(ref maxObserved);
                            } while (reserved > observed &&
                                Interlocked.CompareExchange(ref maxObserved, reserved, observed) != observed);

                            budget.Release(1000);
                        }
                    }
                });
            }

            start.Set();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            Assert.That(Interlocked.Read(ref maxObserved), Is.LessThanOrEqualTo(budget.MaxBytes));
            Assert.That(budget.ReservedBytes, Is.Zero);
        }
    }
}
