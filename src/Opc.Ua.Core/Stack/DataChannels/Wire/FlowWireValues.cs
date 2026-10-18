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

using System;

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Identifies a common-record body.
    /// </summary>
    internal enum FlowRecordKind : byte
    {
        /// <summary>Uninterpreted application bytes.</summary>
        Data,

        /// <summary>A typed binary CONTROL body.</summary>
        Control
    }

    /// <summary>
    /// Identifies a selected wire carrier, not an authenticated connection.
    /// </summary>
    internal enum FlowCarrier
    {
        /// <summary>Dedicated UASC FLO bodies.</summary>
        DedicatedUasc,

        /// <summary>Shared UASC FLO bodies.</summary>
        SharedUasc,

        /// <summary>Native QUIC.</summary>
        Quic,

        /// <summary>Native WebTransport.</summary>
        WebTransport
    }

    /// <summary>
    /// Identifies a control operation within the private wire seam.
    /// </summary>
    internal enum FlowOperation
    {
        /// <summary>Context confirmation.</summary>
        Hello = 0,

        /// <summary>Flow proposal.</summary>
        Open = 1,

        /// <summary>Flow acceptance.</summary>
        Accept = 2,

        /// <summary>Readiness confirmation.</summary>
        Ready = 3,

        /// <summary>Revision proposal.</summary>
        Prepare = 4,

        /// <summary>Revision acceptance.</summary>
        AcceptUpdate = 5,

        /// <summary>Revision barrier.</summary>
        Commit = 6,

        /// <summary>Revision acknowledgement.</summary>
        Ack = 7,

        /// <summary>Pre-commit cancellation.</summary>
        AbortUpdate = 8,

        /// <summary>Shared-carrier credit.</summary>
        Credit = 9,

        /// <summary>Directional end.</summary>
        End = 10,

        /// <summary>Immediate flow closure.</summary>
        Reset = 11,

        /// <summary>Probe request.</summary>
        Ping = 12,

        /// <summary>Probe response.</summary>
        Pong = 13,

        /// <summary>Recorded operation outcome.</summary>
        Status = 14
    }

    /// <summary>
    /// Identifies the wire direction relative to the opener.
    /// </summary>
    internal enum FlowWireDirection
    {
        /// <summary>Opener sends data.</summary>
        OpenerToAcceptor,

        /// <summary>Acceptor sends data.</summary>
        AcceptorToOpener,

        /// <summary>Both peers send data.</summary>
        Duplex
    }

    /// <summary>
    /// Identifies requested wire delivery.
    /// </summary>
    internal enum FlowWireDelivery
    {
        /// <summary>Ordered records.</summary>
        Ordered,

        /// <summary>Single-record datagrams.</summary>
        Datagram
    }

    /// <summary>
    /// Identifies the protection claimed or requested by a wire value.
    /// </summary>
    internal enum FlowWireProtection
    {
        /// <summary>Confidential delivery.</summary>
        Confidential,

        /// <summary>Integrity-only delivery.</summary>
        Integrity
    }

    /// <summary>
    /// Identifies the syntax of an OPEN authorization route.
    /// </summary>
    internal enum FlowWireAuthorization
    {
        /// <summary>Configured authorization with a null grant.</summary>
        Configured,

        /// <summary>Session authorization with a Guid grant.</summary>
        Session
    }

    /// <summary>
    /// Represents a wire outcome, without allocating OPC UA StatusCodes.
    /// </summary>
    internal enum FlowWireStatus
    {
        /// <summary>Successful outcome.</summary>
        Good,

        /// <summary>Unsupported operation or capability.</summary>
        Unsupported,

        /// <summary>Invalid argument.</summary>
        InvalidArgument,

        /// <summary>Invalid state.</summary>
        InvalidState,

        /// <summary>Authorization refused.</summary>
        NotAuthorized,

        /// <summary>Security failure.</summary>
        SecurityFailure,

        /// <summary>Size limit exceeded.</summary>
        TooLarge,

        /// <summary>Timeout.</summary>
        Timeout,

        /// <summary>Closed flow.</summary>
        Closed,

        /// <summary>Unknown channel.</summary>
        UnknownChannel,

        /// <summary>Admission would block.</summary>
        WouldBlock
    }

    /// <summary>
    /// Holds the four revisable wire fields; admission belongs to the flow owner.
    /// </summary>
    internal readonly struct FlowWireLimits
    {
        /// <summary>
        /// Creates a proposed set of wire limits.
        /// </summary>
        internal FlowWireLimits(uint maxMessageSize, uint maxReceiveBytes, double maxRate, byte priority)
        {
            MaxMessageSize = maxMessageSize;
            MaxReceiveBytes = maxReceiveBytes;
            MaxRate = maxRate == 0 ? 0 : maxRate;
            Priority = priority;
        }

        /// <summary>
        /// Gets the maximum application payload length.
        /// </summary>
        internal uint MaxMessageSize { get; }

        /// <summary>
        /// Gets the encoded-byte receive budget.
        /// </summary>
        internal uint MaxReceiveBytes { get; }

        /// <summary>
        /// Gets the encoded-byte rate.
        /// </summary>
        internal double MaxRate { get; }

        /// <summary>
        /// Gets the local scheduling priority.
        /// </summary>
        internal byte Priority { get; }
    }

    /// <summary>
    /// Holds typed wire descriptor fields independently of the generated information model.
    /// </summary>
    internal sealed class FlowWireParameters
    {
        /// <summary>
        /// Creates an unadmitted wire descriptor.
        /// </summary>
        internal FlowWireParameters(
            string resource,
            string protocol,
            string contentType,
            FlowWireDirection direction,
            FlowWireDelivery delivery,
            FlowWireProtection protection,
            FlowWireLimits limits,
            bool allowQueueExpiry)
        {
            Resource = resource ?? throw new ArgumentNullException(nameof(resource));
            Protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
            Direction = direction;
            Delivery = delivery;
            Protection = protection;
            Limits = limits;
            AllowQueueExpiry = allowQueueExpiry;
        }

        /// <summary>
        /// Gets the resource identifier without URI normalization.
        /// </summary>
        internal string Resource { get; }

        /// <summary>
        /// Gets the payload protocol identifier.
        /// </summary>
        internal string Protocol { get; }

        /// <summary>
        /// Gets the content format.
        /// </summary>
        internal string ContentType { get; }

        /// <summary>
        /// Gets the opener-relative direction.
        /// </summary>
        internal FlowWireDirection Direction { get; }

        /// <summary>
        /// Gets the requested delivery.
        /// </summary>
        internal FlowWireDelivery Delivery { get; }

        /// <summary>
        /// Gets the requested protection.
        /// </summary>
        internal FlowWireProtection Protection { get; }

        /// <summary>
        /// Gets the revisable limits.
        /// </summary>
        internal FlowWireLimits Limits { get; }

        /// <summary>
        /// Gets whether pre-transmission queue expiry is permitted.
        /// </summary>
        internal bool AllowQueueExpiry { get; }
    }

    /// <summary>
    /// Base for the closed set of typed control shapes; this is not a model extensibility API.
    /// </summary>
    internal abstract class FlowControl
    {
        /// <summary>
        /// Initializes a typed operation.
        /// </summary>
        private protected FlowControl(FlowOperation operation, uint flow)
        {
            Operation = operation;
            Flow = flow;
        }

        /// <summary>
        /// Gets the operation discriminator.
        /// </summary>
        internal FlowOperation Operation { get; }

        /// <summary>
        /// Gets the addressed flow, or zero for connection management.
        /// </summary>
        internal uint Flow { get; }
    }

    /// <summary>
    /// Carries claimed context information for later comparison with authenticated carrier evidence.
    /// </summary>
    internal sealed class FlowHello : FlowControl
    {
        /// <summary>
        /// Creates a HELLO value without authenticating it.
        /// </summary>
        internal FlowHello(
            uint version,
            FlowCarrier carrier,
            string application,
            Guid context,
            ulong generation,
            ByteString challenge,
            FlowWireProtection protection,
            uint maxMessageSize,
            uint maxReceiveBytes,
            uint maxFlows)
            : base(FlowOperation.Hello, 0)
        {
            Version = version;
            Carrier = carrier;
            Application = application ?? throw new ArgumentNullException(nameof(application));
            Context = context;
            Generation = generation;
            Challenge = challenge;
            Protection = protection;
            MaxMessageSize = maxMessageSize;
            MaxReceiveBytes = maxReceiveBytes;
            MaxFlows = maxFlows;
        }

        /// <summary>
        /// Gets the wire version.
        /// </summary>
        internal uint Version { get; }

        /// <summary>
        /// Gets the claimed carrier.
        /// </summary>
        internal FlowCarrier Carrier { get; }

        /// <summary>
        /// Gets the claimed application identifier.
        /// </summary>
        internal string Application { get; }

        /// <summary>
        /// Gets the context identifier.
        /// </summary>
        internal Guid Context { get; }

        /// <summary>
        /// Gets the context generation.
        /// </summary>
        internal ulong Generation { get; }

        /// <summary>
        /// Gets the 32-byte challenge. Storage must remain immutable while in use.
        /// </summary>
        internal ByteString Challenge { get; }

        /// <summary>
        /// Gets the claimed carrier protection.
        /// </summary>
        internal FlowWireProtection Protection { get; }

        /// <summary>
        /// Gets the peer's proposed payload limit, not an effective local limit.
        /// </summary>
        internal uint MaxMessageSize { get; }

        /// <summary>
        /// Gets the peer's proposed receive limit.
        /// </summary>
        internal uint MaxReceiveBytes { get; }

        /// <summary>
        /// Gets the peer's proposed flow limit.
        /// </summary>
        internal uint MaxFlows { get; }
    }

    /// <summary>
    /// Carries an OPEN proposal; grant validity and stream ownership are not established by decoding.
    /// </summary>
    internal sealed class FlowOpen : FlowControl
    {
        /// <summary>
        /// Creates an OPEN value.
        /// </summary>
        internal FlowOpen(
            uint flow,
            ulong stream,
            FlowWireParameters parameters,
            FlowWireAuthorization authorization,
            Guid? grant)
            : base(FlowOperation.Open, flow)
        {
            Stream = stream;
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            Authorization = authorization;
            Grant = grant;
        }

        /// <summary>
        /// Gets the claimed stream identifier.
        /// </summary>
        internal ulong Stream { get; }

        /// <summary>
        /// Gets the proposed descriptor.
        /// </summary>
        internal FlowWireParameters Parameters { get; }

        /// <summary>
        /// Gets the authorization route.
        /// </summary>
        internal FlowWireAuthorization Authorization { get; }

        /// <summary>
        /// Gets the grant identifier or null for configured authorization.
        /// </summary>
        internal Guid? Grant { get; }
    }

    /// <summary>
    /// Carries an ACCEPT descriptor and stream echo.
    /// </summary>
    internal sealed class FlowAccept : FlowControl
    {
        /// <summary>
        /// Creates an ACCEPT value.
        /// </summary>
        internal FlowAccept(uint flow, ulong stream, FlowWireParameters parameters)
            : base(FlowOperation.Accept, flow)
        {
            Stream = stream;
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        /// <summary>
        /// Gets the claimed stream identifier.
        /// </summary>
        internal ulong Stream { get; }

        /// <summary>
        /// Gets the returned descriptor.
        /// </summary>
        internal FlowWireParameters Parameters { get; }
    }

    /// <summary>
    /// Carries a complete mutable-limit proposal.
    /// </summary>
    internal sealed class FlowPrepare : FlowControl
    {
        /// <summary>
        /// Creates a PREPARE value.
        /// </summary>
        internal FlowPrepare(uint flow, FlowWireLimits limits)
            : base(FlowOperation.Prepare, flow)
        {
            Limits = limits;
        }

        /// <summary>
        /// Gets the proposed limits.
        /// </summary>
        internal FlowWireLimits Limits { get; }
    }

    /// <summary>
    /// Carries an old-revision message barrier.
    /// </summary>
    internal sealed class FlowCommit : FlowControl
    {
        /// <summary>
        /// Creates a COMMIT value.
        /// </summary>
        internal FlowCommit(uint flow, uint lastOldMessageId)
            : base(FlowOperation.Commit, flow)
        {
            LastOldMessageId = lastOldMessageId;
        }

        /// <summary>
        /// Gets the last old-revision DATA identifier.
        /// </summary>
        internal uint LastOldMessageId { get; }
    }

    /// <summary>
    /// Carries encoded-byte credit increments, not updated credit balances.
    /// </summary>
    internal sealed class FlowCredit : FlowControl
    {
        /// <summary>
        /// Creates a CREDIT value.
        /// </summary>
        internal FlowCredit(uint flow, uint channelCredit, uint connectionCredit)
            : base(FlowOperation.Credit, flow)
        {
            ChannelCredit = channelCredit;
            ConnectionCredit = connectionCredit;
        }

        /// <summary>
        /// Gets the per-flow increment.
        /// </summary>
        internal uint ChannelCredit { get; }

        /// <summary>
        /// Gets the aggregate increment.
        /// </summary>
        internal uint ConnectionCredit { get; }
    }

    /// <summary>
    /// Carries a PING or PONG and an uninterpreted timestamp.
    /// </summary>
    internal sealed class FlowProbe : FlowControl
    {
        /// <summary>
        /// Creates a probe value.
        /// </summary>
        internal FlowProbe(FlowOperation operation, uint flow, ulong timestamp)
            : base(operation, flow)
        {
            if (operation is not (FlowOperation.Ping or FlowOperation.Pong))
            {
                throw new ArgumentOutOfRangeException(nameof(operation));
            }
            Timestamp = timestamp;
        }

        /// <summary>
        /// Gets the opaque timestamp.
        /// </summary>
        internal ulong Timestamp { get; }
    }

    /// <summary>
    /// Carries a RESET or STATUS outcome.
    /// </summary>
    internal sealed class FlowOutcome : FlowControl
    {
        /// <summary>
        /// Creates an outcome value.
        /// </summary>
        internal FlowOutcome(FlowOperation operation, uint flow, FlowWireStatus status)
            : base(operation, flow)
        {
            if (operation is not (FlowOperation.Reset or FlowOperation.Status))
            {
                throw new ArgumentOutOfRangeException(nameof(operation));
            }
            Status = status;
        }

        /// <summary>
        /// Gets the protocol outcome.
        /// </summary>
        internal FlowWireStatus Status { get; }
    }

    /// <summary>
    /// Carries an operation whose only additional member is its flow identifier.
    /// </summary>
    internal sealed class FlowSignal : FlowControl
    {
        /// <summary>
        /// Creates a READY, ACCEPT-UPDATE, ACK, ABORT-UPDATE or END value.
        /// </summary>
        internal FlowSignal(FlowOperation operation, uint flow)
            : base(operation, flow)
        {
            if (operation is not (FlowOperation.Ready or FlowOperation.AcceptUpdate or
                FlowOperation.Ack or FlowOperation.AbortUpdate or FlowOperation.End))
            {
                throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
    }
}
