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
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Stress.Tests.Channels.Helpers
{
    /// <summary>
    /// Wait helpers for channel-manager diagnostic snapshots.
    /// </summary>
    public static class WaitForQuiescence
    {
        /// <summary>
        /// Waits until the channel manager diagnostics are stable and no channel is reconnecting.
        /// </summary>
        /// <param name="manager">The channel manager to observe.</param>
        /// <param name="timeout">The maximum time to wait.</param>
        /// <param name="pollInterval">The diagnostics poll interval. Defaults to 50 ms.</param>
        /// <param name="stableWindowSamples">The number of consecutive stable samples required.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="TimeoutException">The manager did not become quiescent before timeout.</exception>
        public static async Task ForManagerAsync(
            IClientChannelManager manager,
            TimeSpan timeout,
            TimeSpan? pollInterval = null,
            int stableWindowSamples = 3,
            CancellationToken ct = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            if (stableWindowSamples <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stableWindowSamples));
            }

            TimeSpan effectivePollInterval = ValidatePollInterval(pollInterval);
            TimeProvider timeProvider = TimeProvider.System;
            DateTimeOffset deadline = timeProvider.GetUtcNow() + timeout;
            SnapshotSignature? previous = null;
            int stableSamples = 0;
            IReadOnlyList<ManagedChannelDiagnostic> lastSnapshot = [];

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                IReadOnlyList<ManagedChannelDiagnostic> snapshot = manager.GetChannelDiagnostics();
                lastSnapshot = snapshot;

                if (!HasTransientState(snapshot))
                {
                    SnapshotSignature signature = CreateSignature(snapshot);
                    stableSamples = previous == signature ? stableSamples + 1 : 1;
                    previous = signature;

                    if (stableSamples >= stableWindowSamples)
                    {
                        return;
                    }
                }
                else
                {
                    previous = null;
                    stableSamples = 0;
                }

                DateTimeOffset now = timeProvider.GetUtcNow();
                if (now >= deadline)
                {
                    throw new TimeoutException(
                        "Channel manager did not become quiescent before timeout." +
                        Environment.NewLine +
                        FormatDiagnostics(lastSnapshot));
                }

                await DelayAsync(effectivePollInterval, deadline, timeProvider, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Waits for a diagnostics entry to reach the expected reference count.
        /// </summary>
        /// <param name="manager">The channel manager to observe.</param>
        /// <param name="key">The diagnostics entry key.</param>
        /// <param name="expectedRefcount">The expected reference count.</param>
        /// <param name="timeout">The maximum time to wait.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>True if the refcount was observed; false when the timeout elapsed.</returns>
        public static Task<bool> EntryRefcountReachesAsync(
            IClientChannelManager manager,
            ManagedChannelKey key,
            int expectedRefcount,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (expectedRefcount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedRefcount));
            }

            return WaitForDiagnosticsAsync(
                manager,
                diagnostics => TryFindDiagnostic(diagnostics, key, out ManagedChannelDiagnostic? diagnostic) &&
                    diagnostic!.Refcount == expectedRefcount,
                timeout,
                ct);
        }

        /// <summary>
        /// Waits for a diagnostics entry to be removed.
        /// </summary>
        /// <param name="manager">The channel manager to observe.</param>
        /// <param name="key">The diagnostics entry key.</param>
        /// <param name="timeout">The maximum time to wait.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>True if the entry disappeared; false when the timeout elapsed.</returns>
        public static Task<bool> EntryGoneAsync(
            IClientChannelManager manager,
            ManagedChannelKey key,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            return WaitForDiagnosticsAsync(
                manager,
                diagnostics => !TryFindDiagnostic(diagnostics, key, out _),
                timeout,
                ct);
        }

        private static async Task<bool> WaitForDiagnosticsAsync(
            IClientChannelManager manager,
            Func<IReadOnlyList<ManagedChannelDiagnostic>, bool> predicate,
            TimeSpan timeout,
            CancellationToken ct)
        {
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            TimeProvider timeProvider = TimeProvider.System;
            DateTimeOffset deadline = timeProvider.GetUtcNow() + timeout;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (predicate(manager.GetChannelDiagnostics()))
                {
                    return true;
                }

                DateTimeOffset now = timeProvider.GetUtcNow();
                if (now >= deadline)
                {
                    return false;
                }

                await DelayAsync(DefaultPollInterval, deadline, timeProvider, ct).ConfigureAwait(false);
            }
        }

        private static async Task DelayAsync(
            TimeSpan requestedDelay,
            DateTimeOffset deadline,
            TimeProvider timeProvider,
            CancellationToken ct)
        {
            TimeSpan remaining = deadline - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            TimeSpan delay = requestedDelay < remaining ? requestedDelay : remaining;
            await Task.Delay(delay, timeProvider, ct).ConfigureAwait(false);
        }

        private static TimeSpan ValidatePollInterval(TimeSpan? pollInterval)
        {
            TimeSpan effectivePollInterval = pollInterval ?? DefaultPollInterval;
            if (effectivePollInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(pollInterval));
            }
            return effectivePollInterval;
        }

        private static bool TryFindDiagnostic(
            IReadOnlyList<ManagedChannelDiagnostic> diagnostics,
            ManagedChannelKey key,
            out ManagedChannelDiagnostic? diagnostic)
        {
            foreach (ManagedChannelDiagnostic candidate in diagnostics)
            {
                if (candidate.Key.Equals(key))
                {
                    diagnostic = candidate;
                    return true;
                }
            }

            diagnostic = null;
            return false;
        }

        private static bool HasTransientState(IReadOnlyList<ManagedChannelDiagnostic> diagnostics)
        {
            foreach (ManagedChannelDiagnostic diagnostic in diagnostics)
            {
                if (IsTransient(diagnostic.State))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTransient(ChannelState state)
        {
            return state is ChannelState.TransportConnecting or
                    ChannelState.TransportReconnecting or
                    ChannelState.TransportConnectedSessionReactivating ||
                string.Equals(state.ToString(), "Reconnecting", StringComparison.Ordinal);
        }

        private static SnapshotSignature CreateSignature(IReadOnlyList<ManagedChannelDiagnostic> diagnostics)
        {
            int totalRefcount = 0;
            var stateBuilder = new StringBuilder();

            foreach (ManagedChannelDiagnostic diagnostic in diagnostics.OrderBy(
                diagnostic => CreateKeyOrderValue(diagnostic.Key),
                StringComparer.Ordinal))
            {
                totalRefcount += diagnostic.Refcount;
                stateBuilder
                    .Append(CreateKeyOrderValue(diagnostic.Key))
                    .Append('=')
                    .Append(diagnostic.State)
                    .Append(';');
            }

            return new SnapshotSignature(diagnostics.Count, totalRefcount, stateBuilder.ToString());
        }

        private static string CreateKeyOrderValue(ManagedChannelKey key)
        {
            int reverseHash = key.ReverseConnectionIdentity != null
                ? RuntimeHelpers.GetHashCode(key.ReverseConnectionIdentity)
                : 0;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{key.EndpointUrl}|{key.SecurityPolicyUri}|{key.SecurityMode}|" +
                $"{key.EndpointConfigurationHash}|{key.ServerCertificateThumbprint}|" +
                $"{key.ClientCertificateThumbprint}|{reverseHash}");
        }

        private static string FormatDiagnostics(IReadOnlyList<ManagedChannelDiagnostic> diagnostics)
        {
            if (diagnostics.Count == 0)
            {
                return "No channel diagnostics entries were present.";
            }

            var builder = new StringBuilder("Channel diagnostics:" + Environment.NewLine);
            foreach (ManagedChannelDiagnostic diagnostic in diagnostics.OrderBy(
                diagnostic => CreateKeyOrderValue(diagnostic.Key),
                StringComparer.Ordinal))
            {
                builder
                    .Append("  Key=")
                    .Append(CreateKeyOrderValue(diagnostic.Key))
                    .Append(", State=")
                    .Append(diagnostic.State)
                    .Append(", Refcount=")
                    .Append(diagnostic.Refcount.ToString(CultureInfo.InvariantCulture))
                    .Append(", Participants=")
                    .Append(diagnostic.ParticipantCount.ToString(CultureInfo.InvariantCulture))
                    .Append(", OpenedAt=")
                    .Append(diagnostic.OpenedAt.ToString("O", CultureInfo.InvariantCulture))
                    .Append(", LastStateChange=")
                    .Append(diagnostic.LastStateChange.ToString("O", CultureInfo.InvariantCulture))
                    .Append(", LastReconnectAttempt=")
                    .Append(diagnostic.LastReconnectAttempt.ToString(CultureInfo.InvariantCulture))
                    .Append(", LastError=")
                    .Append(diagnostic.LastError?.ToString() ?? string.Empty)
                    .AppendLine();
            }

            return builder.ToString();
        }

        private readonly record struct SnapshotSignature(
            int EntryCount,
            int TotalRefcount,
            string StateSignature);

        private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);
    }
}
