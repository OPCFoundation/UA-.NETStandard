/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Bindings.Modbus
{
    /// <summary>
    /// Options for the Modbus TCP WoT binding executor.
    /// </summary>
    public sealed class ModbusWotBindingOptions
    {
        /// <summary>
        /// Gets or sets an optional connector used for every initial connection and reconnect.
        /// The returned readable, writable stream transfers to the channel and is disposed with it.
        /// A host can validate resolved addresses and connect to an approved IP without a second DNS lookup.
        /// When absent, the existing TCP connector is used.
        /// </summary>
        public Func<string, int, CancellationToken, ValueTask<Stream>>? StreamConnector { get; set; }

        /// <summary>
        /// Gets or sets the poll interval used for observe operations.
        /// </summary>
        public TimeSpan ObserveInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the retry policy applied after consecutive unhealthy polls during an
        /// observe operation. Defaults to the stack's exponential backoff
        /// (<see cref="ExponentialBackoffChannelReconnectPolicy"/>, 500 ms doubling to 30 s), so an
        /// asset that has gone offline is not polled once per interval. The backoff never polls
        /// faster than <see cref="ObserveInterval"/>.
        /// </summary>
        public IChannelReconnectPolicy? RetryPolicy { get; set; }
    }

    /// <summary>
    /// Maps Modbus device exception codes to OPC UA status codes.
    /// </summary>
    internal static class ModbusStatusMapper
    {
        public static StatusCode Map(ModbusException exception)
        {
            return exception.ExceptionCode switch
            {
                0x01 => StatusCodes.BadNotSupported,
                0x02 => StatusCodes.BadNodeIdUnknown,
                0x03 => StatusCodes.BadInvalidArgument,
                0x04 => StatusCodes.BadInternalError,
                0x06 => StatusCodes.BadResourceUnavailable,
                _ => StatusCodes.BadCommunicationError
            };
        }
    }
}
