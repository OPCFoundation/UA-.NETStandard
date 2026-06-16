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
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Tests for <see cref="SecurityTokenWindow"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.3.1", Summary = "PubSub replay-window")]
    public class SecurityTokenWindowTests
    {
        private static readonly uint[] s_expectedTokenIds = new uint[] { 1U, 7U };

        private static byte[] MakeNonce(byte seed)
        {
            byte[] nonce = new byte[12];
            for (int i = 0; i < nonce.Length; i++)
            {
                nonce[i] = (byte)(seed + i);
            }
            return nonce;
        }

        [Test]
        public void TryAccept_AcceptsFirstMessageForRegisteredToken()
        {
            var window = new SecurityTokenWindow();
            window.RegisterToken(1U);
            Assert.That(window.TryAccept(1U, 1UL, MakeNonce(1)), Is.True);
        }

        [Test]
        public void TryAccept_RejectsUnknownToken()
        {
            var window = new SecurityTokenWindow();
            Assert.That(window.TryAccept(99U, 1UL, MakeNonce(1)), Is.False);
        }

        [Test]
        public void TryAccept_RejectsReplayedSequenceNumber()
        {
            var window = new SecurityTokenWindow();
            window.RegisterToken(1U);
            Assert.Multiple(() =>
            {
                Assert.That(window.TryAccept(1U, 1UL, MakeNonce(1)), Is.True);
                Assert.That(window.TryAccept(1U, 1UL, MakeNonce(2)), Is.False);
            });
        }

        [Test]
        public void TryAccept_RejectsReusedNonce()
        {
            var window = new SecurityTokenWindow();
            window.RegisterToken(1U);
            byte[] nonce = MakeNonce(7);
            Assert.Multiple(() =>
            {
                Assert.That(window.TryAccept(1U, 1UL, nonce), Is.True);
                Assert.That(window.TryAccept(1U, 2UL, nonce), Is.False);
            });
        }

        [Test]
        public void TryAccept_AcceptsDifferentTokenWithSameSequence()
        {
            var window = new SecurityTokenWindow();
            window.RegisterToken(1U);
            window.RegisterToken(2U);
            Assert.Multiple(() =>
            {
                Assert.That(window.TryAccept(1U, 5UL, MakeNonce(1)), Is.True);
                Assert.That(window.TryAccept(2U, 5UL, MakeNonce(2)), Is.True);
            });
        }

        [Test]
        public void RetireToken_RejectsSubsequentMessages()
        {
            var window = new SecurityTokenWindow();
            window.RegisterToken(1U);
            window.RetireToken(1U);
            Assert.That(window.TryAccept(1U, 1UL, MakeNonce(1)), Is.False);
        }

        [Test]
        public void Reset_ClearsAllState()
        {
            var window = new SecurityTokenWindow();
            window.RegisterToken(1U);
            Assert.That(window.TryAccept(1U, 1UL, MakeNonce(1)), Is.True);
            window.Reset();
            Assert.Multiple(() =>
            {
                Assert.That(window.RegisteredTokens, Is.Empty);
                Assert.That(window.TryAccept(1U, 1UL, MakeNonce(1)), Is.False);
            });
        }

        [Test]
        public void TryAccept_HistoryEvictionAllowsOldEntriesToBeReused()
        {
            var window = new SecurityTokenWindow(historySize: 4);
            window.RegisterToken(1U);
            for (ulong seq = 1; seq <= 8; seq++)
            {
                Assert.That(
                    window.TryAccept(1U, seq, MakeNonce((byte)seq)),
                    Is.True,
                    $"seq {seq} should be accepted");
            }
            // First sequence has been evicted by now — accepting it
            // again is allowed (the window cannot remember it).
            Assert.That(window.TryAccept(1U, 1UL, MakeNonce(80)), Is.True);
        }

        [Test]
        public void Constructor_RejectsNonPositiveHistorySize()
        {
            Assert.That(
                () => new SecurityTokenWindow(historySize: 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new SecurityTokenWindow(historySize: -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Properties_ReflectConfiguration()
        {
            var clock = TimeProvider.System;
            var window = new SecurityTokenWindow(historySize: 16, timeProvider: clock);
            Assert.Multiple(() =>
            {
                Assert.That(window.HistorySize, Is.EqualTo(16));
                Assert.That(window.TimeProvider, Is.SameAs(clock));
            });
        }

        [Test]
        public void RegisteredTokens_ReturnsRegisteredIds()
        {
            var window = new SecurityTokenWindow();
            window.RegisterToken(1U);
            window.RegisterToken(7U);
            Assert.That(window.RegisteredTokens, Is.EquivalentTo(s_expectedTokenIds));
        }

        [Test]
        public void RegisterToken_IsIdempotent()
        {
            var window = new SecurityTokenWindow();
            window.RegisterToken(1U);
            window.RegisterToken(1U);
            Assert.That(window.RegisteredTokens, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task TryAccept_ConcurrentInvocationDoesNotLoseEntries()
        {
            var window = new SecurityTokenWindow(historySize: 100_000);
            window.RegisterToken(1U);
            const int parallelism = 8;
            const int perTask = 500;
            var accepted = new ConcurrentBag<ulong>();
            Task[] workers = new Task[parallelism];
            for (int t = 0; t < parallelism; t++)
            {
                int taskIndex = t;
                workers[t] = Task.Run(() =>
                {
                    for (int i = 0; i < perTask; i++)
                    {
                        ulong sequenceNumber = (ulong)(taskIndex * perTask + i + 1);
                        byte[] nonce = new byte[12];
                        // Distinct nonce per (task, i) pair.
                        nonce[0] = (byte)taskIndex;
                        nonce[1] = (byte)(i & 0xff);
                        nonce[2] = (byte)((i >> 8) & 0xff);
                        if (window.TryAccept(1U, sequenceNumber, nonce))
                        {
                            accepted.Add(sequenceNumber);
                        }
                    }
                });
            }
            await Task.WhenAll(workers);
            Assert.That(accepted, Has.Count.EqualTo(parallelism * perTask));
        }
    }
}
