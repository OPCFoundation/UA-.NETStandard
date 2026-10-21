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
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Capabilities;

/// <summary>
/// Checks concrete nodes using at most two scalar attributes or one browse page.
/// Namespace presence alone never establishes support; no calls or writes are executed.
/// </summary>
internal sealed class SessionCapabilityProbe : ICapabilityProbe
{
    public async Task<CapabilityResult> ProbeAsync(
        ISession session,
        CapabilityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.NodeId.IsNull)
        {
            return new CapabilityResult(CapabilityState.RequiresConfiguration, "Select a concrete target node.");
        }
        if (!Enum.IsDefined(request.Operation))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        try
        {
            return request.Operation == CapabilityOperation.Browse
                ? await BrowseAsync(session, request.NodeId, cancellationToken).ConfigureAwait(false)
                : await ReadPermissionsAsync(session, request, cancellationToken).ConfigureAwait(false);
        }
        catch (ServiceResultException error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CapabilityResult.FromFailure(error.StatusCode);
        }
        catch (Exception error) when (error is IOException or TimeoutException or SocketException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new CapabilityResult(CapabilityState.Unknown,
                "The transport or probe cleanup did not complete. Check the connection and retry.");
        }
    }

    private static async Task<CapabilityResult> BrowseAsync(
        ISession session,
        NodeId nodeId,
        CancellationToken cancellationToken)
    {
        BrowseResponse response = await session.BrowseAsync(
            null,
            null,
            1,
            [
                new BrowseDescription
                {
                    NodeId = nodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.References,
                    IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.NodeClass
                }
            ],
            cancellationToken).ConfigureAwait(false);
        if (response.Results.Count != 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (response.ResponseHeader is { } failure && !StatusCode.IsGood(failure.ServiceResult))
            {
                return CapabilityResult.FromFailure(failure.ServiceResult);
            }
            return MissingEvidence();
        }

        BrowseResult result = response.Results[0];
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (response.ResponseHeader is not { } header)
            {
                return MissingEvidence();
            }
            if (!StatusCode.IsGood(header.ServiceResult))
            {
                return CapabilityResult.FromFailure(header.ServiceResult);
            }
            return StatusCode.IsGood(result.StatusCode)
                ? new CapabilityResult(CapabilityState.Supported,
                    "Browsing this target succeeded. Mutation permissions are checked separately by the tool.")
                : CapabilityResult.FromFailure(result.StatusCode);
        }
        finally
        {
            if (!result.ContinuationPoint.IsEmpty)
            {
                await ReleaseContinuationPointAsync(session, result.ContinuationPoint).ConfigureAwait(false);
            }
        }
    }

    private static async Task ReleaseContinuationPointAsync(ISession session, ByteString continuationPoint)
    {
        // Releasing the one page is bounded even if the requesting document was cancelled.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            BrowseNextResponse released = await session.BrowseNextAsync(
                null, true, [continuationPoint], cleanup.Token).ConfigureAwait(false);
            if (released.ResponseHeader is not { } header)
            {
                throw new ServiceResultException(StatusCodes.BadUnknownResponse);
            }
            if (!StatusCode.IsGood(header.ServiceResult))
            {
                throw new ServiceResultException(header.ServiceResult);
            }
            foreach (BrowseResult release in released.Results)
            {
                if (!StatusCode.IsGood(release.StatusCode))
                {
                    throw new ServiceResultException(release.StatusCode);
                }
            }
        }
        catch (ServiceResultException error)
        {
            throw new IOException("The browse continuation point could not be released.", error);
        }
    }

    private static async Task<CapabilityResult> ReadPermissionsAsync(
        ISession session,
        CapabilityRequest request,
        CancellationToken cancellationToken)
    {
        bool method = request.Operation == CapabilityOperation.CallMethod;
        bool events = request.Operation == CapabilityOperation.SubscribeEvents;
        ArrayOf<ReadValueId> attributes = events
            ? [new ReadValueId { NodeId = request.NodeId, AttributeId = Attributes.EventNotifier }]
            :
            [
                new ReadValueId
                {
                    NodeId = request.NodeId,
                    AttributeId = method ? Attributes.Executable : Attributes.AccessLevel
                },
                new ReadValueId
                {
                    NodeId = request.NodeId,
                    AttributeId = method ? Attributes.UserExecutable : Attributes.UserAccessLevel
                }
            ];
        ReadResponse response = await session.ReadAsync(
            null, 0, TimestampsToReturn.Neither, attributes, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (response.ResponseHeader is not { } header)
        {
            return MissingEvidence();
        }
        if (!StatusCode.IsGood(header.ServiceResult))
        {
            return CapabilityResult.FromFailure(header.ServiceResult);
        }
        if (response.Results.Count != attributes.Count)
        {
            return MissingEvidence();
        }
        foreach (DataValue value in response.Results)
        {
            if (!StatusCode.IsGood(value.StatusCode))
            {
                return CapabilityResult.FromFailure(value.StatusCode);
            }
        }
        if (events)
        {
            if (!response.Results[0].WrappedValue.TryGetValue(out byte notifier))
            {
                return MissingEvidence();
            }
            return (notifier & EventNotifiers.SubscribeToEvents) != 0
                ? new CapabilityResult(CapabilityState.Supported,
                    "The target advertises event subscriptions. Permissions are checked when subscribing.")
                : Advertised(supported: false, allowed: false);
        }
        if (method)
        {
            return response.Results[0].WrappedValue.TryGetValue(out bool executable)
                && response.Results[1].WrappedValue.TryGetValue(out bool userExecutable)
                ? Advertised(executable, userExecutable)
                : MissingEvidence();
        }
        byte mask = request.Operation switch
        {
            CapabilityOperation.ReadValue => AccessLevels.CurrentRead,
            CapabilityOperation.WriteValue => AccessLevels.CurrentWrite,
            CapabilityOperation.ReadHistory => AccessLevels.HistoryRead,
            CapabilityOperation.UpdateHistory => AccessLevels.HistoryWrite,
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        return response.Results[0].WrappedValue.TryGetValue(out byte access)
            && response.Results[1].WrappedValue.TryGetValue(out byte userAccess)
            ? Advertised((access & mask) != 0, (userAccess & mask) != 0)
            : MissingEvidence();
    }

    private static CapabilityResult Advertised(bool supported, bool allowed)
    {
        if (!supported)
        {
            return new CapabilityResult(CapabilityState.Unsupported,
                "The target does not advertise this operation. Select another target.");
        }
        return allowed
            ? new CapabilityResult(CapabilityState.Supported,
                "The target advertises this operation for the current identity. Execution may still fail.")
            : new CapabilityResult(CapabilityState.Denied,
                "The target supports this operation, but the current identity lacks permission.");
    }

    private static CapabilityResult MissingEvidence()
    {
        return new CapabilityResult(CapabilityState.Unknown,
            "The server returned incomplete or incorrectly typed capability evidence. Refresh to retry.");
    }
}
