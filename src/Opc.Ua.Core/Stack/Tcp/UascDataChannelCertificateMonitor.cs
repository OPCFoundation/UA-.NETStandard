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
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Keeps configured flows bound to valid application certificates even when no Session exists.
    /// </summary>
    internal sealed class UascDataChannelCertificateMonitor : IDisposable, IObserver<CertificateChangeEvent>
    {
        /// <summary>
        /// Retains validated certificate evidence and starts one bounded, coalescing revalidation worker.
        /// </summary>
        internal UascDataChannelCertificateMonitor(
            Certificate localCertificate,
            CertificateCollection peerChain,
            ICertificateValidatorEx validator,
            TimeProvider clock,
            BackgroundTaskScope background,
            Func<ServiceResult, CancellationToken, ValueTask> close)
        {
            if (peerChain.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            m_validator = validator;
            m_clock = clock;
            m_close = close;
            m_stopToken = m_stop.Token;
            DateTime localExpiry = localCertificate.NotAfter.ToUniversalTime();
            DateTime peerExpiry = peerChain[0].NotAfter.ToUniversalTime();
            m_expiry = new DateTimeOffset(localExpiry < peerExpiry ? localExpiry : peerExpiry);
            if (m_expiry <= clock.GetUtcNow())
            {
                throw new ServiceResultException(StatusCodes.BadCertificateTimeInvalid);
            }
            m_localRaw = new ByteString(localCertificate.RawData);
            m_peerChain = peerChain.AddRef();
            m_timer = clock.CreateTimer(
                _ => m_signals.Writer.TryWrite(true), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            m_subscription = (validator as ICertificateLifecycle)?.CertificateChanges.Subscribe(this);
            if (!background.Run(nameof(RevalidateAsync), RevalidateAsync))
            {
                Dispose();
                throw new ServiceResultException(StatusCodes.BadConnectionClosed);
            }
            ArmTimer();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            m_subscription?.Dispose();
            m_timer.Dispose();
            m_signals.Writer.TryComplete();
            m_stop.Cancel();
            m_stop.Dispose();
            lock (m_evidenceGate)
            {
                m_peerChain.Dispose();
            }
        }

        /// <inheritdoc/>
        public void OnNext(CertificateChangeEvent value)
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }
            if (value.Kind == CertificateChangeKind.ApplicationCertificateUpdated &&
                value.OldCertificate != null &&
                m_localRaw.Span.SequenceEqual(value.OldCertificate.RawData))
            {
                Volatile.Write(ref m_localReplaced, 1);
                m_signals.Writer.TryWrite(true);
            }
            else if (value.Kind is CertificateChangeKind.TrustListUpdated or CertificateChangeKind.CrlUpdated &&
                value.TrustList == TrustListIdentifier.Peers)
            {
                m_signals.Writer.TryWrite(true);
            }
        }

        /// <inheritdoc/>
        public void OnError(Exception error)
        {
            Volatile.Write(ref m_monitorFailed, 1);
            m_signals.Writer.TryWrite(true);
        }

        /// <inheritdoc/>
        public void OnCompleted()
        {
            // Periodic validation remains active when an optional event source completes.
        }

        /// <summary>
        /// Revalidates retained evidence outside channel serialization and closes only this association on failure.
        /// </summary>
        private async ValueTask RevalidateAsync(CancellationToken shutdown)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown, m_stopToken);
            CancellationToken token = cancellation.Token;
            try
            {
                while (await m_signals.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (m_signals.Reader.TryRead(out _))
                    {
                    }
                    ServiceResult? failure = null;
                    if (Volatile.Read(ref m_localReplaced) != 0 || Volatile.Read(ref m_monitorFailed) != 0)
                    {
                        failure = new ServiceResult(StatusCodes.BadSecurityChecksFailed);
                    }
                    else if (m_clock.GetUtcNow() >= m_expiry)
                    {
                        failure = new ServiceResult(StatusCodes.BadCertificateTimeInvalid);
                    }
                    else
                    {
                        CertificateCollection snapshot;
                        lock (m_evidenceGate)
                        {
                            if (Volatile.Read(ref m_disposed) != 0)
                            {
                                return;
                            }
                            snapshot = m_peerChain.AddRef();
                        }
                        using (snapshot)
                        {
                            CertificateValidationResult result = await m_validator.ValidateAsync(
                                snapshot, TrustListIdentifier.Peers, ct: token).ConfigureAwait(false);
                            if (!result.IsValid)
                            {
                                failure = new ServiceResult(result.StatusCode);
                            }
                        }
                    }
                    if (failure != null)
                    {
                        await m_close(failure, token).ConfigureAwait(false);
                        return;
                    }
                    ArmTimer();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The association's disposal owns cancellation of this worker.
            }
            catch (Exception exception)
            {
                if (Volatile.Read(ref m_disposed) == 0)
                {
                    await m_close(
                        ServiceResult.Create(
                            exception, StatusCodes.BadSecurityChecksFailed, "Flow certificate revalidation failed."),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                Dispose();
            }
        }

        /// <summary>
        /// Schedules the earlier of certificate expiry and the next bounded trust recheck.
        /// </summary>
        private void ArmTimer()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }
            TimeSpan remaining = m_expiry - m_clock.GetUtcNow();
            TimeSpan due = remaining <= TimeSpan.Zero ? TimeSpan.Zero :
                remaining < TimeSpan.FromMinutes(1) ? remaining : TimeSpan.FromMinutes(1);
            try
            {
                m_timer.Change(due, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref m_disposed) != 0)
            {
                // Closure can race the final rescheduling decision.
            }
        }

        private readonly ICertificateValidatorEx m_validator;
        private readonly TimeProvider m_clock;
        private readonly Func<ServiceResult, CancellationToken, ValueTask> m_close;
        private readonly ByteString m_localRaw;
        private readonly CertificateCollection m_peerChain;
        private readonly DateTimeOffset m_expiry;
        private readonly ITimer m_timer;
        private readonly IDisposable? m_subscription;
        private readonly CancellationTokenSource m_stop = new();
        private readonly CancellationToken m_stopToken;
        private readonly Lock m_evidenceGate = new();
        private readonly Channel<bool> m_signals = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
        private int m_disposed;
        private int m_localReplaced;
        private int m_monitorFailed;
    }
}
