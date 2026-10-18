/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Converts closed wire discriminators without reflection, normalization or numeric enum fallback.
    /// </summary>
    internal static class FlowWireNames
    {
        /// <summary>
        /// Gets an operation's exact name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static string Operation(FlowOperation value)
        {
            return value switch
            {
                FlowOperation.Hello => "hello",
                FlowOperation.Open => "open",
                FlowOperation.Accept => "accept",
                FlowOperation.Ready => "ready",
                FlowOperation.Prepare => "prepare",
                FlowOperation.AcceptUpdate => "accept-update",
                FlowOperation.Commit => "commit",
                FlowOperation.Ack => "ack",
                FlowOperation.AbortUpdate => "abort-update",
                FlowOperation.Credit => "credit",
                FlowOperation.End => "end",
                FlowOperation.Reset => "reset",
                FlowOperation.Ping => "ping",
                FlowOperation.Pong => "pong",
                FlowOperation.Status => "status",
                _ => throw new FlowCodecException(FlowCodecError.UnknownOperation)
            };
        }

        /// <summary>
        /// Parses an exact operation name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowOperation Operation(string value)
        {
            return value switch
            {
                "hello" => FlowOperation.Hello,
                "open" => FlowOperation.Open,
                "accept" => FlowOperation.Accept,
                "ready" => FlowOperation.Ready,
                "prepare" => FlowOperation.Prepare,
                "accept-update" => FlowOperation.AcceptUpdate,
                "commit" => FlowOperation.Commit,
                "ack" => FlowOperation.Ack,
                "abort-update" => FlowOperation.AbortUpdate,
                "credit" => FlowOperation.Credit,
                "end" => FlowOperation.End,
                "reset" => FlowOperation.Reset,
                "ping" => FlowOperation.Ping,
                "pong" => FlowOperation.Pong,
                "status" => FlowOperation.Status,
                _ => throw new FlowCodecException(FlowCodecError.UnknownOperation)
            };
        }

        /// <summary>
        /// Gets a carrier's exact name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static string Carrier(FlowCarrier value)
        {
            return value switch
            {
                FlowCarrier.DedicatedUasc => "dedicated-uasc",
                FlowCarrier.SharedUasc => "shared-uasc",
                FlowCarrier.Quic => "quic",
                FlowCarrier.WebTransport => "webtransport",
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }

        /// <summary>
        /// Parses an exact carrier name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowCarrier Carrier(string value)
        {
            return value switch
            {
                "dedicated-uasc" => FlowCarrier.DedicatedUasc,
                "shared-uasc" => FlowCarrier.SharedUasc,
                "quic" => FlowCarrier.Quic,
                "webtransport" => FlowCarrier.WebTransport,
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }

        /// <summary>
        /// Gets an exact protection name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static string Protection(FlowWireProtection value)
        {
            return value switch
            {
                FlowWireProtection.Confidential => "confidential",
                FlowWireProtection.Integrity => "integrity",
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }

        /// <summary>
        /// Parses an exact protection name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowWireProtection Protection(string value)
        {
            return value switch
            {
                "confidential" => FlowWireProtection.Confidential,
                "integrity" => FlowWireProtection.Integrity,
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }

        /// <summary>
        /// Gets an exact direction name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static string Direction(FlowWireDirection value)
        {
            return value switch
            {
                FlowWireDirection.OpenerToAcceptor => "opener-to-acceptor",
                FlowWireDirection.AcceptorToOpener => "acceptor-to-opener",
                FlowWireDirection.Duplex => "duplex",
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }

        /// <summary>
        /// Parses an exact direction name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowWireDirection Direction(string value)
        {
            return value switch
            {
                "opener-to-acceptor" => FlowWireDirection.OpenerToAcceptor,
                "acceptor-to-opener" => FlowWireDirection.AcceptorToOpener,
                "duplex" => FlowWireDirection.Duplex,
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }

        /// <summary>
        /// Gets an exact delivery name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static string Delivery(FlowWireDelivery value)
        {
            return value switch
            {
                FlowWireDelivery.Ordered => "ordered",
                FlowWireDelivery.Datagram => "datagram",
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }

        /// <summary>
        /// Parses an exact delivery name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowWireDelivery Delivery(string value)
        {
            return value switch
            {
                "ordered" => FlowWireDelivery.Ordered,
                "datagram" => FlowWireDelivery.Datagram,
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }

        /// <summary>
        /// Gets an exact authorization name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static string Authorization(FlowWireAuthorization value)
        {
            return value switch
            {
                FlowWireAuthorization.Configured => "configured",
                FlowWireAuthorization.Session => "session",
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }

        /// <summary>
        /// Parses an exact authorization name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowWireAuthorization Authorization(string value)
        {
            return value switch
            {
                "configured" => FlowWireAuthorization.Configured,
                "session" => FlowWireAuthorization.Session,
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }

        /// <summary>
        /// Gets an exact outcome name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static string Status(FlowWireStatus value)
        {
            return value switch
            {
                FlowWireStatus.Good => "good",
                FlowWireStatus.Unsupported => "unsupported",
                FlowWireStatus.InvalidArgument => "invalid_argument",
                FlowWireStatus.InvalidState => "invalid_state",
                FlowWireStatus.NotAuthorized => "not_authorized",
                FlowWireStatus.SecurityFailure => "security_failure",
                FlowWireStatus.TooLarge => "too_large",
                FlowWireStatus.Timeout => "timeout",
                FlowWireStatus.Closed => "closed",
                FlowWireStatus.UnknownChannel => "unknown_channel",
                FlowWireStatus.WouldBlock => "would_block",
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }

        /// <summary>
        /// Parses an exact outcome name.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowWireStatus Status(string value)
        {
            return value switch
            {
                "good" => FlowWireStatus.Good,
                "unsupported" => FlowWireStatus.Unsupported,
                "invalid_argument" => FlowWireStatus.InvalidArgument,
                "invalid_state" => FlowWireStatus.InvalidState,
                "not_authorized" => FlowWireStatus.NotAuthorized,
                "security_failure" => FlowWireStatus.SecurityFailure,
                "too_large" => FlowWireStatus.TooLarge,
                "timeout" => FlowWireStatus.Timeout,
                "closed" => FlowWireStatus.Closed,
                "unknown_channel" => FlowWireStatus.UnknownChannel,
                "would_block" => FlowWireStatus.WouldBlock,
                _ => throw new FlowCodecException(FlowCodecError.InvalidEnumeration)
            };
        }
    }
}
