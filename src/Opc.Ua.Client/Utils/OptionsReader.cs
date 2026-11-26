// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Opc.Ua
{
    /// <summary>
    /// Convert option changes to queue of changels
    /// </summary>
    /// <typeparam name="TChange"></typeparam>
    /// <typeparam name="TOptions"></typeparam>
    internal sealed class OptionsReader<TChange, [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions> : IDisposable
        where TChange : struct
        where TOptions : class
    {
        /// <summary>
        /// Create change monitor
        /// </summary>
        /// <param name="options"></param>
        /// <param name="convert"></param>
        public OptionsReader(IOptionsMonitor<TOptions> options,
            Func<TOptions, TChange?> convert)
        {
            m_changes = Channel.CreateUnbounded<TChange>(
                new UnboundedChannelOptions
                {
                    SingleReader = true
                });
            m_subscription = options.OnChange((o, n) =>
            {
                TChange? change = convert(o);
                if (change != null)
                {
                    m_changes.Writer.TryWrite(change.Value);
                }
            });
        }

        /// <summary>
        /// Stops observing the options monitor.
        /// </summary>
        /// <remarks>
        /// An <see cref="IOptionsMonitor{TOptions}"/> is normally a singleton, so a
        /// reader that never released its registration would be held by it for the
        /// life of the process and every options change would fan out to readers
        /// nobody is draining.
        /// </remarks>
        public void Dispose()
        {
            m_subscription?.Dispose();
        }

        /// <summary>
        /// Wait for changes
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<bool> WaitAsync(CancellationToken ct)
        {
            return m_changes.Reader.WaitToReadAsync(ct);
        }

        /// <summary>
        /// Get next change
        /// </summary>
        /// <param name="change"></param>
        /// <returns></returns>
        public bool TryGetNextChange(out TChange change)
        {
            return m_changes.Reader.TryRead(out change);
        }

        private readonly Channel<TChange> m_changes;
        private readonly IDisposable? m_subscription;
    }

    /// <summary>
    /// Convert option changes to queue of changels
    /// </summary>
    /// <typeparam name="TOptions"></typeparam>
    internal sealed class OptionsReader<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions> : IDisposable
        where TOptions : class
    {
        /// <summary>
        /// Create change monitor
        /// </summary>
        /// <param name="options"></param>
        /// <param name="capacity"></param>
        public OptionsReader(IOptionsMonitor<TOptions> options, int capacity = 5)
        {
            m_changes = Channel.CreateBounded<TOptions>(
                new BoundedChannelOptions(capacity)
                {
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });

            m_subscription = options.OnChange((o, n) =>
            {
                if (o == null)
                {
                    return;
                }
                TOptions? last = Interlocked.Exchange(ref m_current, o);
                if (o.Equals(last))
                {
                    return;
                }
                m_changes.Writer.TryWrite(o);
            });
        }

        /// <summary>
        /// Stops observing the options monitor.
        /// </summary>
        /// <remarks>
        /// An <see cref="IOptionsMonitor{TOptions}"/> is normally a singleton, so a
        /// reader that never released its registration would be held by it for the
        /// life of the process and every options change would fan out to readers
        /// nobody is draining.
        /// </remarks>
        public void Dispose()
        {
            m_subscription?.Dispose();
        }

        /// <summary>
        /// Wait for changes
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<bool> WaitAsync(CancellationToken ct)
        {
            return m_changes.Reader.WaitToReadAsync(ct);
        }

        /// <summary>
        /// Get next change
        /// </summary>
        /// <param name="change"></param>
        /// <returns></returns>
        public bool TryGet([MaybeNullWhen(false)] out TOptions? change)
        {
            return m_changes.Reader.TryRead(out change);
        }

        private readonly Channel<TOptions> m_changes;
        private readonly IDisposable? m_subscription;
        private TOptions? m_current;
    }
}
