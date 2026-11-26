// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.SourceGeneration.Dependency;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Verifies decode coordination, mutation isolation and weak cache ownership.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    public class DependencyDecodeCacheTests
    {
        /// <summary>
        /// Cold callers share one decode, including callers with equal-content keys.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentColdCallersDecodeOnce(bool copyKeys)
        {
            int decodes = 0;
            var cache = new DependencyDecodeCache(copyKeys, (_, _) =>
            {
                Interlocked.Increment(ref decodes);
                return new ModelDependencyV1();
            });
            string key = Guid.NewGuid().ToString();
            using var start = new Barrier(8);
            Task<ModelDependencyV1?>[] calls = [.. Enumerable.Range(0, 8)
                .Select(_ => Task.Factory.StartNew(
                    () =>
                    {
                        string payload = copyKeys ? new string(key.ToCharArray()) : key;
                        if (!start.SignalAndWait(TimeSpan.FromSeconds(30)))
                        {
                            throw new TimeoutException("Concurrent callers did not start.");
                        }
                        return cache.GetDependency(payload);
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default))];

            ModelDependencyV1?[] results = await Task.WhenAll(calls).ConfigureAwait(false);

            Assert.That(decodes, Is.EqualTo(1));
            Assert.That(results, Is.All.SameAs(results[0]));
        }

        /// <summary>
        /// Public mutable results never participate in the generator's content cache.
        /// </summary>
        [Test]
        public void PublicMutationDoesNotAffectOtherPayloadKeysOrGeneration()
        {
            string payload = new ModelDependencyV1 { ModelUri = "urn:original" }.ToBase64Payload();
            ModelDependencyReference first = CreateReference(payload);
            ModelDependencyReference second = CreateReference(new string(payload.ToCharArray()));
            ModelDependencyV1? decoded = first.GetDependency();
            Assert.That(decoded, Is.Not.Null);
            decoded.ModelUri = "urn:changed";

            Assert.That(first.GetDependency(), Is.SameAs(decoded));
            Assert.That(second.GetDependency()!.ModelUri, Is.EqualTo("urn:original"));
            ModelDependencyV1? shared = first.GetDependencyForGeneration();
            Assert.That(shared!.ModelUri, Is.EqualTo("urn:original"));
            Assert.That(second.GetDependencyForGeneration(), Is.SameAs(shared));
        }

        /// <summary>
        /// Live metadata keys do not pin expanded graphs through either cache.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CompletedGraphCanBeCollectedWithLiveKey(bool shareByContent)
        {
            var cache = new DependencyDecodeCache(shareByContent, (_, _) => new ModelDependencyV1());
            string key = Guid.NewGuid().ToString();
            WeakReference graph = DecodeWithoutRetaining(cache, key);

            Collect();

            Assert.That(graph.IsAlive, Is.False);
            Assert.That(cache.GetDependency(key), Is.Not.Null);
            GC.KeepAlive(key);
            GC.KeepAlive(cache);
        }

        /// <summary>
        /// The content index does not keep payload strings alive.
        /// </summary>
        [Test]
        public void ContentIndexDoesNotRetainKeys()
        {
            var cache = new DependencyDecodeCache(true, (_, _) => new ModelDependencyV1());
            (WeakReference key, WeakReference graph) = DecodeTemporaryKey(cache);

            Collect();

            Assert.That(key.IsAlive, Is.False);
            Assert.That(graph.IsAlive, Is.False);
            GC.KeepAlive(cache);
        }

        /// <summary>
        /// Churn evicts content entries without breaking an existing identity entry.
        /// </summary>
        [Test]
        public void ContentIndexEvictsOldEntries()
        {
            var cache = new DependencyDecodeCache(true, (_, _) => new ModelDependencyV1());
            string[] keys = [.. Enumerable.Range(0, 256).Select(_ => Guid.NewGuid().ToString())];
            ModelDependencyV1? first = cache.GetDependency(keys[0]);
            foreach (string key in keys.Skip(1))
            {
                cache.GetDependency(key);
            }

            Assert.That(cache.GetDependency(new string(keys[0].ToCharArray())), Is.Not.SameAs(first));
            Assert.That(cache.GetDependency(keys[0]), Is.SameAs(first));
            GC.KeepAlive(keys);
        }

        /// <summary>
        /// Cancellation does not poison a holder for subsequent runs.
        /// </summary>
        [Test]
        public void CancelledDecodeCanBeRetried()
        {
            using var cancellation = new CancellationTokenSource();
            int decodes = 0;
            var cache = new DependencyDecodeCache(true, (_, token) =>
            {
                if (Interlocked.Increment(ref decodes) == 1)
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                return new ModelDependencyV1();
            });
            string key = Guid.NewGuid().ToString();

            Assert.Throws<OperationCanceledException>(() => cache.GetDependency(key, cancellation.Token));
            Assert.That(cache.GetDependency(key), Is.Not.Null);
            Assert.That(decodes, Is.EqualTo(2));
        }

        /// <summary>
        /// A waiting caller can cancel without waiting for another caller's decode.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WaitingCallerCanCancelBeforeDecodeCompletes(bool shareByContent)
        {
            using var cancellation = new CancellationTokenSource();
            using var releaseDecode = new ManualResetEventSlim();
            var decoding = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiting = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
            int decodes = 0;
            var cache = new DependencyDecodeCache(shareByContent, (_, _) =>
            {
                Interlocked.Increment(ref decodes);
                decoding.SetResult(true);
                if (!releaseDecode.Wait(TimeSpan.FromSeconds(30), CancellationToken.None))
                {
                    throw new TimeoutException("The decode was not released.");
                }
                return new ModelDependencyV1();
            });
            string key = Guid.NewGuid().ToString();
            Task<ModelDependencyV1?> first = Task.Run(() => cache.GetDependency(key));
            Task? second = null;
            try
            {
                Assert.That(
                    await Task.WhenAny(decoding.Task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false),
                    Is.SameAs(decoding.Task));
                second = Task.Factory.StartNew(
                    () =>
                    {
                        waiting.SetResult(Thread.CurrentThread);
                        string payload = shareByContent ? new string(key.ToCharArray()) : key;
                        Assert.Throws<OperationCanceledException>(() => cache.GetDependency(payload, cancellation.Token));
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                Thread caller = await waiting.Task.ConfigureAwait(false);
                Assert.That(
                    SpinWait.SpinUntil(
                        () => (caller.ThreadState & ThreadState.WaitSleepJoin) != 0,
                        TimeSpan.FromSeconds(5)),
                    Is.True);

                cancellation.Cancel();

                Assert.That(
                    await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false),
                    Is.SameAs(second),
                    "Cancellation must finish while the first decode is still held open.");
                await second.ConfigureAwait(false);
                Assert.That(first.IsCompleted, Is.False);
            }
            finally
            {
                releaseDecode.Set();
                await first.ConfigureAwait(false);
                if (second != null)
                {
                    await second.ConfigureAwait(false);
                }
            }

            Assert.That(cache.GetDependency(key), Is.SameAs(await first.ConfigureAwait(false)));
            Assert.That(decodes, Is.EqualTo(1));
        }

        /// <summary>
        /// Malformed legacy payloads remain null, but security failures still propagate.
        /// </summary>
        [Test]
        public void NullResultsAreCachedAndSecurityFailuresPropagate()
        {
            int decodes = 0;
            var cache = new DependencyDecodeCache(true, (_, _) =>
            {
                decodes++;
                return null;
            });
            string key = Guid.NewGuid().ToString();
            Assert.That(cache.GetDependency(key), Is.Null);
            Assert.That(cache.GetDependency(new string(key.ToCharArray())), Is.Null);
            Assert.That(decodes, Is.EqualTo(1));

            var failing = new DependencyDecodeCache(true, (_, _) =>
                throw new InvalidOperationException("Cannot preserve security metadata."));
            Assert.Throws<InvalidOperationException>(() => failing.GetDependency(key));
            Assert.Throws<InvalidOperationException>(() => failing.GetDependency(key));
        }

        private static ModelDependencyReference CreateReference(string payload)
        {
            return new ModelDependencyReference(
                "Test", "urn:original", "Test", string.Empty, string.Empty, payload: payload);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference DecodeWithoutRetaining(DependencyDecodeCache cache, string key)
        {
            return new WeakReference(cache.GetDependency(key));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (WeakReference Key, WeakReference Graph) DecodeTemporaryKey(DependencyDecodeCache cache)
        {
            string key = Guid.NewGuid().ToString();
            return (new WeakReference(key), DecodeWithoutRetaining(cache, key));
        }

        private static void Collect()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }
}
