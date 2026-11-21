// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua
{
    /// <summary>
    /// A <see cref="ServiceResultException"/> raised when a server sheds an
    /// establishment request under transient overload (typically
    /// <c>BadServerTooBusy</c>), optionally carrying a retry-after hint.
    /// </summary>
    /// <remarks>
    /// The fault builder surfaces <see cref="RetryAfter"/> to the client via
    /// <see cref="RetryAfterHeader"/> on the fault's
    /// <see cref="ResponseHeader.AdditionalHeader"/>, so a cooperating client can
    /// back off deterministically without requesting diagnostics.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1032:Implement standard exception constructors",
        Justification = "A ServerBusyException must carry a ServiceResult describing " +
            "the overload; the parameterless and message-only constructors are " +
            "intentionally omitted so callers always supply that result.")]
    [SuppressMessage(
        "Usage",
        "RCS1194:Implement exception constructors",
        Justification = "A ServerBusyException must carry a ServiceResult describing " +
            "the overload; the parameterless and message-only constructors are " +
            "intentionally omitted so callers always supply that result.")]
    public sealed class ServerBusyException : ServiceResultException
    {
        /// <summary>
        /// Initializes the exception from a service result and an optional
        /// retry-after hint.
        /// </summary>
        /// <param name="status">
        /// The service result describing the overload.
        /// </param>
        /// <param name="retryAfter">
        /// The suggested minimum delay before retrying, or <c>null</c> when unknown.
        /// </param>
        public ServerBusyException(ServiceResult status, TimeSpan? retryAfter)
            : base(status)
        {
            RetryAfter = retryAfter;
        }

        /// <summary>
        /// The suggested minimum delay before retrying, or <c>null</c> when unknown.
        /// </summary>
        public TimeSpan? RetryAfter { get; }
    }
}
