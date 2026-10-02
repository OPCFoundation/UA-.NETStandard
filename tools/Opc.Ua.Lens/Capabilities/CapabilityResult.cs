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
using Opc.Ua;

namespace UaLens.Capabilities;

/// <summary>
/// Availability of one operation, not permission to open a document or a guarantee that an operation will succeed.
/// </summary>
internal enum CapabilityState
{
    Unknown,
    Supported,
    Unsupported,
    RequiresConfiguration,
    Denied
}

/// <summary>
/// Read-only checks of a concrete target. Write and call checks inspect advertised permissions,
/// never mutate the server.
/// </summary>
internal enum CapabilityOperation
{
    Browse,
    ReadValue,
    WriteValue,
    CallMethod,
    SubscribeEvents,
    ReadHistory,
    UpdateHistory
}

/// <summary>
/// A session-relative target. Do not persist this request instead of a portable workspace target identity.
/// </summary>
internal sealed record CapabilityRequest(NodeId NodeId, CapabilityOperation Operation);

/// <summary>
/// Evidence and an actionable explanation. Unknown and missing configuration must not be presented as success.
/// </summary>
internal sealed record CapabilityResult
{
    public CapabilityResult(CapabilityState state, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }
        State = state;
        Reason = reason;
    }

    public CapabilityState State { get; }

    public string Reason { get; }

    public bool CanExecute => State == CapabilityState.Supported;

    /// <summary>
    /// Converts failed service evidence without treating transport errors as proof that a feature is absent.
    /// </summary>
    public static CapabilityResult FromFailure(StatusCode status)
    {
        if (status == StatusCodes.BadUserAccessDenied || status == StatusCodes.BadSecurityModeInsufficient)
        {
            return new(CapabilityState.Denied,
                $"The server denied this check ({status}). Use an authorized identity and security policy.");
        }
        if (status == StatusCodes.BadNodeIdUnknown || status == StatusCodes.BadNodeIdInvalid ||
            status == StatusCodes.BadAttributeIdInvalid || status == StatusCodes.BadNotSupported ||
            status == StatusCodes.BadServiceUnsupported)
        {
            return new(CapabilityState.Unsupported,
                $"The requested target or operation is not supported ({status}). Select another target.");
        }
        return new(CapabilityState.Unknown,
            $"The server did not provide usable capability evidence ({status}). Check the connection and retry.");
    }
}
