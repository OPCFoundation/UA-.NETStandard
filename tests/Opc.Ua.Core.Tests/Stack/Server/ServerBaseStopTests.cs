/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Verifies shared stop ownership, observable failure and retry without opening a transport.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class ServerBaseStopTests
    {
        /// <summary>
        /// Cancellation affects only the caller, concurrent calls join and only failed stops are retried.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentStopSharesAttemptAndOnlyFailureAllowsRetryAsync(bool failFirst)
        {
            using var server = new StopProbeServer(failFirst);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task first = server.StopAsync().AsTask();
            try
            {
                await server.Entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Task second = server.StopAsync().AsTask();
                using var cancellation = new CancellationTokenSource();
                Task cancelledWait = server.StopAsync(cancellation.Token).AsTask();
                cancellation.Cancel();
                await Assert.ThatAsync(() => cancelledWait,
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(server.Attempts, Is.EqualTo(1));
                server.Release.TrySetResult(true);
                if (failFirst)
                {
                    await Assert.ThatAsync(() => first,
                        Throws.InstanceOf<IOException>().And.SameAs(server.Failure)).ConfigureAwait(false);
                    await Assert.ThatAsync(() => second,
                        Throws.InstanceOf<IOException>().And.SameAs(server.Failure)).ConfigureAwait(false);
                }
                else
                {
                    await Task.WhenAll(first, second).WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await server.StopAsync(timeout.Token).ConfigureAwait(false);
                await server.StopAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(server.Attempts, Is.EqualTo(failFirst ? 2 : 1));
            }
            finally
            {
                server.Release.TrySetResult(true);
            }
        }

        /// <summary>
        /// Provides a deterministic pre-stop barrier and a single recoverable cleanup fault.
        /// </summary>
        private sealed class StopProbeServer(bool failFirst) : ServerBase(NUnitTelemetryContext.Create())
        {
            /// <summary>
            /// Gets the pre-stop entry barrier.
            /// </summary>
            internal TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the pre-stop release barrier.
            /// </summary>
            internal TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the exact injected fault.
            /// </summary>
            internal IOException Failure { get; } = new("Injected shutdown failure.");

            /// <summary>
            /// Gets the number of owned stop attempts.
            /// </summary>
            internal int Attempts { get; private set; }

            /// <inheritdoc/>
            protected override async ValueTask OnServerStoppingAsync(CancellationToken cancellationToken = default)
            {
                Attempts++;
                Entered.TrySetResult(true);
                await Release.Task.ConfigureAwait(false);
                if (failFirst && Attempts == 1)
                {
                    throw Failure;
                }
            }
        }
    }
}
