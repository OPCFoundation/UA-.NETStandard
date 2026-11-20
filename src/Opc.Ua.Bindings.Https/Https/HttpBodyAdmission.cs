/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Charges the bytes an HTTPS request body or WebSocket message actually buffers
    /// to the server's reassembly pool and to a per-peer ceiling.
    /// </summary>
    /// <remarks>
    /// Reserving the largest possible body before a byte arrived let idle peers
    /// without credentials drain the reassembly pool that opc.tcp channels and
    /// other HTTPS clients share. The charge grows only with buffered bytes and is
    /// released once the request is decoded. A charge the pool refuses first asks
    /// the listener to reclaim an unused channel, as a refused connection does.
    /// An admission belongs to one request or message and is not thread safe.
    /// </remarks>
    internal sealed class HttpBodyAdmission : IDisposable
    {
        /// <summary>
        /// Creates an admission that charges <paramref name="provider"/> for
        /// <paramref name="owner"/> and <paramref name="ledger"/> for <paramref name="peer"/>.
        /// </summary>
        public HttpBodyAdmission(
            IServerResourceIsolationProvider? provider,
            ResourceIsolationOwner? owner,
            PeerBodyLedger? ledger,
            IPAddress? peer,
            Func<bool>? reclaim)
        {
            m_provider = owner != null ? provider : null;
            m_owner = owner;
            m_ledger = ledger;
            m_peer = peer;
            m_reclaim = reclaim;
        }

        /// <summary>
        /// The bytes currently charged.
        /// </summary>
        public long ChargedBytes => m_charged;

        /// <summary>
        /// Charges <paramref name="amount"/> more bytes. Returns false, charging nothing,
        /// when the peer ceiling or the reassembly pool refuses them.
        /// </summary>
        public bool TryCharge(long amount)
        {
            if (amount <= 0)
            {
                return true;
            }
            ChargeResult result = TryChargeOnce(amount);
            if (result == ChargeResult.PoolRefused && m_reclaim?.Invoke() == true)
            {
                result = TryChargeOnce(amount);
            }
            return result == ChargeResult.Charged;
        }

        /// <summary>
        /// Returns every charged byte. The admission can be charged again afterwards.
        /// </summary>
        public void Release()
        {
            IDisposable? lease = m_lease;
            List<IDisposable>? leases = m_leases;
            long charged = m_charged;
            m_lease = null;
            m_leases = null;
            m_charged = 0;
            try
            {
                m_ledger?.Remove(m_peer, charged);
            }
            finally
            {
                try
                {
                    lease?.Dispose();
                }
                finally
                {
                    if (leases != null)
                    {
                        foreach (IDisposable more in leases)
                        {
                            more.Dispose();
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Release();
        }

        private ChargeResult TryChargeOnce(long amount)
        {
            if (m_ledger != null && !m_ledger.TryAdd(m_peer, amount))
            {
                return ChargeResult.PeerRefused;
            }
            if (m_provider != null)
            {
                if (!m_provider.TryAcquire(
                    ResourceIsolationStage.ReassemblyBytes, m_owner!, amount, out IDisposable? lease, out _))
                {
                    m_ledger?.Remove(m_peer, amount);
                    return ChargeResult.PoolRefused;
                }
                if (m_lease == null)
                {
                    m_lease = lease;
                }
                else
                {
                    (m_leases ??= []).Add(lease);
                }
            }
            m_charged += amount;
            return ChargeResult.Charged;
        }

        /// <summary>
        /// Reads a body into a buffer that grows only with the bytes that arrive,
        /// charging each buffer to <paramref name="admission"/> before allocating it.
        /// A peer can therefore hold only about eight times what it sent, and never
        /// more than the announced length. The read ends with
        /// <see cref="StatusCodes.BadTimeout"/> once <paramref name="deadline"/> elapses,
        /// with <see cref="StatusCodes.BadRequestTooLarge"/> when the body exceeds
        /// <paramref name="maxLength"/> and with <see cref="StatusCodes.BadServerTooBusy"/>
        /// when a charge is refused.
        /// </summary>
        /// <param name="body">The body stream.</param>
        /// <param name="contentLength">The announced length, which only caps buffer growth.</param>
        /// <param name="maxLength">The largest body accepted; non-positive is unlimited.</param>
        /// <param name="admission">The admission charged for the buffers, or null to charge nothing.</param>
        /// <param name="deadline">How long the whole body may take to arrive.</param>
        /// <param name="timeProvider">The clock of the deadline.</param>
        /// <param name="abort">
        /// Ends the pending read when the deadline elapses, for example with
        /// <see cref="CancelPendingBodyRead"/>, so that the read ends with an exception
        /// and the caller can still answer. The deadline timer is
        /// only created when a read does not complete at once.
        /// </param>
        /// <param name="abortState">The state passed to <paramref name="abort"/>.</param>
        /// <param name="ct">Cancels the read.</param>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<byte[]> ReadBodyAsync(
            Stream body,
            long? contentLength,
            int maxLength,
            HttpBodyAdmission? admission,
            TimeSpan deadline,
            TimeProvider timeProvider,
            Action<object?> abort,
            object? abortState,
            CancellationToken ct)
        {
            if (maxLength > 0 && contentLength > maxLength)
            {
                throw TooLarge(maxLength);
            }
            long limit = maxLength > 0 ? maxLength : kMaxArrayLength;
            bool announced = contentLength.HasValue;
            long growthLimit = announced && contentLength!.Value < limit ? contentLength.Value : limit;
            if (growthLimit == 0)
            {
                return [];
            }

            long started = timeProvider.GetTimestamp();
            CancellationTokenSource? timeout = null;
            CancellationTokenRegistration expiry = default;
            byte[] buffer = Allocate(admission, Math.Min(growthLimit, kInitialBufferSize));
            long length = 0;
            try
            {
                while (true)
                {
                    byte[] target = buffer;
                    bool probing = false;
                    if (length == buffer.Length)
                    {
                        if (length == growthLimit)
                        {
                            // An announced length is framed by the server: the body ends here.
                            // Otherwise one more byte means the body exceeds the limit.
                            if (announced)
                            {
                                break;
                            }
                            target = new byte[1];
                            probing = true;
                        }
                        else
                        {
                            long capacity = Math.Min(growthLimit, kGrowthFactor * length);
                            byte[] grown = Allocate(admission, capacity);
                            Buffer.BlockCopy(buffer, 0, grown, 0, (int)length);
                            buffer = target = grown;
                        }
                    }
                    int offset = probing ? 0 : (int)length;
#if NET5_0_OR_GREATER
                    ValueTask<int> pending = body.ReadAsync(target.AsMemory(offset, target.Length - offset), ct);
#else
                    var pending = new ValueTask<int>(body.ReadAsync(target, offset, target.Length - offset, ct));
#endif
                    int read;
                    if (pending.IsCompletedSuccessfully)
                    {
                        read = pending.Result;
                    }
                    else
                    {
                        if (timeout == null)
                        {
                            // The body has to wait for the peer: from now on the deadline counts.
                            TimeSpan remaining = deadline - timeProvider.GetElapsedTime(started);
                            timeout = timeProvider.CreateCancellationTokenSource(
                                remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                            expiry = timeout.Token.Register(abort, abortState);
                        }
                        try
                        {
                            read = await pending.ConfigureAwait(false);
                        }
                        catch (Exception) when (timeout.IsCancellationRequested)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadTimeout,
                                "The request body did not arrive within {0} ms.",
                                (long)deadline.TotalMilliseconds);
                        }
                    }
                    if (probing && read > 0)
                    {
                        throw TooLarge(maxLength);
                    }
                    if (read == 0)
                    {
                        break;
                    }
                    length += read;
                }
            }
            finally
            {
                expiry.Dispose();
                timeout?.Dispose();
            }

            if (length == buffer.Length)
            {
                return buffer;
            }
            byte[] exact = Allocate(admission, length);
            Buffer.BlockCopy(buffer, 0, exact, 0, (int)length);
            return exact;
        }

        private static byte[] Allocate(HttpBodyAdmission? admission, long size)
        {
            if (admission != null && !admission.TryCharge(size))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadServerTooBusy,
                    "The server cannot buffer the request body now.");
            }
            return new byte[size];
        }

        /// <summary>
        /// The <c>abort</c> callback of <see cref="ReadBodyAsync"/> for the body of an
        /// <see cref="HttpContext"/> passed as its state. It cancels only the pending
        /// read of the request body pipe, which Kestrel ends with an
        /// <see cref="OperationCanceledException"/>, so the connection stays open and the
        /// handler can still answer the late body with 408. Kestrel 2 on .NET Framework
        /// has no request body pipe, so there the connection is aborted.
        /// </summary>
        public static readonly Action<object?> CancelPendingBodyRead = static state =>
        {
            var context = (HttpContext)state!;
#if NET5_0_OR_GREATER
            context.Request.BodyReader.CancelPendingRead();
#else
            context.Abort();
#endif
        };

        private static ServiceResultException TooLarge(int maxLength)
        {
            return ServiceResultException.Create(
                StatusCodes.BadRequestTooLarge,
                "Request body exceeds the configured MaxMessageSize ({0} bytes).",
                maxLength);
        }

        private enum ChargeResult
        {
            Charged,
            PeerRefused,
            PoolRefused
        }

        /// <summary>
        /// The first buffer a body is read into, before more bytes arrived.
        /// </summary>
        private const int kInitialBufferSize = 16 * 1024;

        /// <summary>
        /// How much a full buffer grows, up to the announced length or the limit.
        /// </summary>
        private const long kGrowthFactor = 8;

        /// <summary>
        /// The largest single-dimension byte array the runtime allocates.
        /// </summary>
        private const long kMaxArrayLength = 0x7FFFFFC7;

        private readonly IServerResourceIsolationProvider? m_provider;
        private readonly ResourceIsolationOwner? m_owner;
        private readonly PeerBodyLedger? m_ledger;
        private readonly IPAddress? m_peer;
        private readonly Func<bool>? m_reclaim;
        private IDisposable? m_lease;
        private List<IDisposable>? m_leases;
        private long m_charged;
    }

    /// <summary>
    /// Bounds the body bytes one peer address holds buffered across all its
    /// HTTPS requests and WebSocket messages, so one peer cannot take the
    /// reassembly pool that every other client shares.
    /// </summary>
    internal sealed class PeerBodyLedger
    {
        /// <summary>
        /// Creates a ledger with a per-peer ceiling; a non-positive ceiling is unlimited.
        /// </summary>
        public PeerBodyLedger(long ceiling)
        {
            Ceiling = ceiling;
        }

        /// <summary>
        /// The most bytes one peer address may hold.
        /// </summary>
        public long Ceiling { get; }

        /// <summary>
        /// The bytes <paramref name="peer"/> holds.
        /// </summary>
        public long GetCharged(IPAddress? peer)
        {
            if (peer == null)
            {
                return 0;
            }
            lock (m_charged)
            {
                return m_charged.TryGetValue(Normalize(peer), out long charged) ? charged : 0;
            }
        }

        /// <summary>
        /// Adds <paramref name="amount"/> to the bytes <paramref name="peer"/> holds
        /// unless that exceeds the ceiling.
        /// </summary>
        public bool TryAdd(IPAddress? peer, long amount)
        {
            if (peer == null || Ceiling <= 0 || amount <= 0)
            {
                return true;
            }
            IPAddress key = Normalize(peer);
            lock (m_charged)
            {
                m_charged.TryGetValue(key, out long charged);
                if (amount > Ceiling - charged)
                {
                    return false;
                }
                m_charged[key] = charged + amount;
                return true;
            }
        }

        /// <summary>
        /// Returns <paramref name="amount"/> bytes <paramref name="peer"/> held.
        /// </summary>
        public void Remove(IPAddress? peer, long amount)
        {
            if (peer == null || Ceiling <= 0 || amount <= 0)
            {
                return;
            }
            IPAddress key = Normalize(peer);
            lock (m_charged)
            {
                if (!m_charged.TryGetValue(key, out long charged))
                {
                    return;
                }
                if (charged <= amount)
                {
                    m_charged.Remove(key);
                }
                else
                {
                    m_charged[key] = charged - amount;
                }
            }
        }

        private static IPAddress Normalize(IPAddress peer)
        {
            return peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer;
        }

        private readonly Dictionary<IPAddress, long> m_charged = [];
    }
}
