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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Unit tests for the coalescing <see cref="CertificateChangePump{TState}"/>
    /// shared by the client certificate-rotation reconnect and the server's
    /// trust-material enforcement.
    /// </summary>
    [TestFixture]
    [Category("CertificateChangePump")]
    [Parallelizable(ParallelScope.All)]
    public sealed class CertificateChangePumpTests
    {
        private static CertificateChangeEvent Event(
            CertificateChangeKind kind,
            TrustListIdentifier? trustList = null)
        {
            return new CertificateChangeEvent(
                kind,
                trustList ?? TrustListIdentifier.Peers,
                CertificateType: null,
                OldCertificate: null,
                NewCertificate: null,
                IssuerChain: null);
        }

        [Test]
        public async Task FilteredEventsAreDroppedWithoutProcessingAsync()
        {
            var subject = new CertificateChangeSubject();
            int processed = 0;
            using var pump = new CertificateChangePump<CertificateChangeEvent>(
                evt => evt.Kind == CertificateChangeKind.CrlUpdated,
                (_, evt) => evt,
                (_, _) =>
                {
                    Interlocked.Increment(ref processed);
                    return default;
                });
            pump.Subscribe(subject);

            subject.Notify(Event(CertificateChangeKind.ApplicationCertificateUpdated));
            subject.Notify(Event(CertificateChangeKind.CertificateRejected));

            await Task.Delay(200).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref processed), Is.Zero);
        }

        [Test]
        public async Task BurstIsCoalescedIntoFoldedStateAsync()
        {
            var subject = new CertificateChangeSubject();
            var firstPassEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstPass = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var passes = new List<HashSet<TrustListIdentifier>>();
            var passesLock = new object();

            using var pump = new CertificateChangePump<HashSet<TrustListIdentifier>>(
                _ => true,
                (scopes, evt) =>
                {
                    scopes ??= [];
                    scopes.Add(evt.TrustList);
                    return scopes;
                },
                async (scopes, _) =>
                {
                    lock (passesLock)
                    {
                        passes.Add(scopes);
                    }
                    firstPassEntered.TrySetResult(true);
                    await releaseFirstPass.Task.ConfigureAwait(false);
                });
            pump.Subscribe(subject);

            // The first event starts the pump; it blocks inside the first
            // processing pass.
            subject.Notify(Event(CertificateChangeKind.CrlUpdated, TrustListIdentifier.Peers));
            await firstPassEntered.Task.ConfigureAwait(false);

            // A burst arriving while the pass runs folds into ONE pending
            // state (a scope union), drained by a single follow-up pass.
            subject.Notify(Event(CertificateChangeKind.CrlUpdated, TrustListIdentifier.Users));
            subject.Notify(Event(CertificateChangeKind.TrustListUpdated, TrustListIdentifier.Https));
            subject.Notify(Event(CertificateChangeKind.CrlUpdated, TrustListIdentifier.Users));
            releaseFirstPass.TrySetResult(true);

            // Wait for the drain to settle.
            await WaitUntilAsync(() =>
            {
                lock (passesLock)
                {
                    return passes.Count == 2;
                }
            }).ConfigureAwait(false);

            lock (passesLock)
            {
                Assert.That(passes, Has.Count.EqualTo(2), "burst must coalesce into one follow-up pass");
                Assert.That(passes[0], Is.EquivalentTo(new[] { TrustListIdentifier.Peers }));
                Assert.That(
                    passes[1],
                    Is.EquivalentTo(new[] { TrustListIdentifier.Users, TrustListIdentifier.Https }));
            }
        }

        [Test]
        public async Task ProcessExceptionDoesNotTearDownPumpAsync()
        {
            var subject = new CertificateChangeSubject();
            int calls = 0;
            int errors = 0;
            using var pump = new CertificateChangePump<CertificateChangeEvent>(
                _ => true,
                (_, evt) => evt,
                (_, _) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        throw new InvalidOperationException("first pass fails");
                    }
                    return default;
                },
                _ => Interlocked.Increment(ref errors));
            pump.Subscribe(subject);

            subject.Notify(Event(CertificateChangeKind.CrlUpdated));
            await WaitUntilAsync(() => Volatile.Read(ref calls) >= 1).ConfigureAwait(false);
            await WaitUntilAsync(() => Volatile.Read(ref errors) == 1).ConfigureAwait(false);

            // The pump must still process subsequent events.
            subject.Notify(Event(CertificateChangeKind.CrlUpdated));
            await WaitUntilAsync(() => Volatile.Read(ref calls) >= 2).ConfigureAwait(false);
        }

        [Test]
        public async Task DisposeUnsubscribesAndDiscardsPendingAsync()
        {
            var subject = new CertificateChangeSubject();
            int processed = 0;
            var pump = new CertificateChangePump<CertificateChangeEvent>(
                _ => true,
                (_, evt) => evt,
                (_, _) =>
                {
                    Interlocked.Increment(ref processed);
                    return default;
                });
            pump.Subscribe(subject);

            pump.Dispose();

            subject.Notify(Event(CertificateChangeKind.CrlUpdated));
            await Task.Delay(200).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref processed), Is.Zero);
            Assert.That(
                () => pump.Subscribe(subject),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public async Task PumpStateCallbackReportsTaskThenNullAsync()
        {
            var subject = new CertificateChangeSubject();
            var states = new List<Task?>();
            var statesLock = new object();
            using var pump = new CertificateChangePump<CertificateChangeEvent>(
                _ => true,
                (_, evt) => evt,
                (_, _) => default,
                onProcessError: null,
                onPumpStateChanged: task =>
                {
                    lock (statesLock)
                    {
                        states.Add(task);
                    }
                });
            pump.Subscribe(subject);

            subject.Notify(Event(CertificateChangeKind.TrustListUpdated));

            await WaitUntilAsync(() =>
            {
                lock (statesLock)
                {
                    return states.Count == 2;
                }
            }).ConfigureAwait(false);

            lock (statesLock)
            {
                Assert.That(states[0], Is.Not.Null, "drain start must publish the pump task");
                Assert.That(states[1], Is.Null, "drain completion must clear the pump task");
            }
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("Timed out waiting for the pump to settle.");
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
        }
    }
}
