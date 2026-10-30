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

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Finite local bounds and monotonic deadlines for a version-one flow context.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed record DataChannelOptions
    {
        /// <summary>
        /// Gets the maximum simultaneous flows, including provisional associations.
        /// </summary>
        public int MaxFlows { get; init; } = 128;

        /// <summary>
        /// Gets the maximum complete payload length.
        /// </summary>
        public int MaxMessageSize { get; init; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum binary CONTROL body length.
        /// </summary>
        public int MaxControlBytes { get; init; } = 65536;

        /// <summary>
        /// Gets the per-direction encoded-byte budget of a flow.
        /// </summary>
        public uint MaxReceiveBytes { get; init; } = 32 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum queued or application-owned messages per direction of a flow.
        /// </summary>
        public int MaxQueueMessages { get; init; } = 1024;

        /// <summary>
        /// Gets the aggregate encoded-byte capacity per direction of this context.
        /// </summary>
        public uint MaxContextBytes { get; init; } = 64 * 1024 * 1024;

        /// <summary>
        /// Gets the aggregate retained-message capacity per direction of this context.
        /// </summary>
        public int MaxContextMessages { get; init; } = 4096;

        /// <summary>
        /// Gets the bounded reliable control queue capacity.
        /// </summary>
        public int MaxPendingControls { get; init; } = 256;

        /// <summary>
        /// Gets the maximum native stream associations retained for anti-reuse in one context.
        /// Exhaustion requires a new context.
        /// </summary>
        public int MaxStreamHistory { get; init; } = 65536;

        /// <summary>
        /// Gets the protected HELLO confirmation timeout after the carrier security handshake.
        /// </summary>
        public TimeSpan HelloTimeout { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets the flow-establishment timeout.
        /// </summary>
        public TimeSpan OpenTimeout { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets the revision timeout before abort or committed-outcome retry.
        /// </summary>
        public TimeSpan UpdateTimeout { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets the orderly send-direction drain timeout and the maximum UASC ingress DATA-capacity wait.
        /// </summary>
        public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets the maximum interval between continuing-authorization checks.
        /// </summary>
        public TimeSpan AuthorizationRecheckInterval { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Gets the maximum grant lifetime before redemption.
        /// </summary>
        public TimeSpan GrantLifetime { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets the receiver-monotonic old datagram revision grace.
        /// </summary>
        public TimeSpan DatagramRevisionGrace { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Gets the unanswered-probe timeout.
        /// </summary>
        public TimeSpan PingTimeout { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets the local, non-negotiated idle interval before a context PING is sent.
        /// </summary>
        public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets whether the application explicitly permits integrity-only UASC.
        /// The resource authorizer must independently permit it.
        /// </summary>
        public bool AllowIntegrity { get; init; }

        /// <summary>
        /// Validates limits before creating state or reserving resources.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal void Validate()
        {
            if (MaxFlows is <= 0 or > 128 ||
                MaxMessageSize <= 0 ||
                MaxControlBytes <= 0 ||
                MaxReceiveBytes <= 17 ||
                MaxQueueMessages is <= 0 or > 1024 ||
                MaxContextBytes < MaxReceiveBytes ||
                MaxContextMessages <= 0 ||
                MaxPendingControls <= 0 ||
                MaxStreamHistory <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxFlows), "Flow bounds must be positive and finite.");
            }
            ValidateDuration(HelloTimeout, nameof(HelloTimeout));
            ValidateDuration(OpenTimeout, nameof(OpenTimeout));
            ValidateDuration(UpdateTimeout, nameof(UpdateTimeout));
            ValidateDuration(DrainTimeout, nameof(DrainTimeout));
            ValidateDuration(PingTimeout, nameof(PingTimeout));
            ValidateDuration(DatagramRevisionGrace, nameof(DatagramRevisionGrace));
            ValidateDuration(AuthorizationRecheckInterval, nameof(AuthorizationRecheckInterval));
            ValidateDuration(GrantLifetime, nameof(GrantLifetime));
            ValidateDuration(KeepAliveInterval, nameof(KeepAliveInterval));
            if (KeepAliveInterval < TimeSpan.FromSeconds(1))
            {
                throw new ArgumentOutOfRangeException(nameof(KeepAliveInterval));
            }
            if (AuthorizationRecheckInterval > TimeSpan.FromMinutes(1) ||
                GrantLifetime > TimeSpan.FromSeconds(10))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(AuthorizationRecheckInterval),
                    "Authorization and grant deadlines cannot exceed the protocol maximum.");
            }
        }

        /// <summary>
        /// Rejects infinite, zero or timer-unrepresentable durations.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void ValidateDuration(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }

    /// <summary>
    /// A bounded protocol outcome at an application-facing asynchronous boundary.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelException : Exception
    {
        /// <summary>
        /// Creates an unspecified invalid-state failure.
        /// </summary>
        public DataChannelException()
            : this(DataChannelResult.InvalidState)
        {
        }

        /// <summary>
        /// Creates a local diagnostic failure without changing the wire outcome vocabulary.
        /// </summary>
        public DataChannelException(string? message)
            : base(message)
        {
            Result = DataChannelResult.InvalidState;
        }

        /// <summary>
        /// Wraps a local failure while retaining its cause.
        /// </summary>
        public DataChannelException(string? message, Exception? innerException)
            : base(message, innerException)
        {
            Result = DataChannelResult.InvalidState;
        }

        /// <summary>
        /// Creates an exception without including resource, identity or grant material.
        /// </summary>
        public DataChannelException(DataChannelResult result)
            : base("The data channel operation did not succeed.")
        {
            Result = result;
        }

        /// <summary>
        /// Gets the generated model outcome, not a provisional UA StatusCode.
        /// </summary>
        public DataChannelResult Result { get; }
    }
}
