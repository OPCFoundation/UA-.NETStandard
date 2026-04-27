#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua
{
    using Microsoft.Extensions.Options;
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    /// <summary>
    /// Convert option changes to queue of changels
    /// </summary>
    /// <typeparam name="TChange"></typeparam>
    /// <typeparam name="TOptions"></typeparam>
    internal sealed class OptionsReader<TChange, [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>
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
            _changes = Channel.CreateUnbounded<TChange>(
                new UnboundedChannelOptions
                {
                    SingleReader = true
                });
            options.OnChange((o, n) =>
            {
                var change = convert(o);
                if (change != null)
                {
                    _changes.Writer.TryWrite(change.Value);
                }
            });
        }

        /// <summary>
        /// Wait for changes
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<bool> WaitAsync(CancellationToken ct)
        {
            return _changes.Reader.WaitToReadAsync(ct);
        }

        /// <summary>
        /// Get next change
        /// </summary>
        /// <param name="change"></param>
        /// <returns></returns>
        public bool TryGetNextChange(out TChange change)
        {
            return _changes.Reader.TryRead(out change);
        }

        private readonly Channel<TChange> _changes;
    }

    /// <summary>
    /// Convert option changes to queue of changels
    /// </summary>
    /// <typeparam name="TOptions"></typeparam>
    internal sealed class OptionsReader<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>
        where TOptions : class
    {
        /// <summary>
        /// Create change monitor
        /// </summary>
        /// <param name="options"></param>
        /// <param name="capacity"></param>
        public OptionsReader(IOptionsMonitor<TOptions> options, int capacity = 5)
        {
            _changes = Channel.CreateBounded<TOptions>(
                new BoundedChannelOptions(capacity)
                {
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });

            options.OnChange((o, n) =>
            {
                if (o == null)
                {
                    return;
                }
                var last = Interlocked.Exchange(ref _current, o);
                if (o.Equals(last))
                {
                    return;
                }
                _changes.Writer.TryWrite(o);
            });
        }

        /// <summary>
        /// Wait for changes
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<bool> WaitAsync(CancellationToken ct)
        {
            return _changes.Reader.WaitToReadAsync(ct);
        }

        /// <summary>
        /// Get next change
        /// </summary>
        /// <param name="change"></param>
        /// <returns></returns>
        public bool TryGet([MaybeNullWhen(false)] out TOptions? change)
        {
            return _changes.Reader.TryRead(out change);
        }

        private readonly Channel<TOptions> _changes;
        private TOptions? _current;
    }
}
#endif
