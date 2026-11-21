/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PackML.Server
{
    /// <summary>
    /// An operation submitted to an equipment provider.
    /// </summary>
    public enum PackMLOperation
    {
        /// <summary>
        /// A state-machine command.
        /// </summary>
        Command,

        /// <summary>
        /// A unit mode request.
        /// </summary>
        SetUnitMode,

        /// <summary>
        /// A machine speed setpoint.
        /// </summary>
        SetMachSpeed,

        /// <summary>
        /// Product configuration.
        /// </summary>
        SetProduct,

        /// <summary>
        /// Job parameters.
        /// </summary>
        SetParameter,

        /// <summary>
        /// An explicitly configured remote interface.
        /// </summary>
        RemoteCommand,

        /// <summary>
        /// A material interlock request.
        /// </summary>
        SetInterlock,

        /// <summary>
        /// An application administration request, never a state Reset.
        /// </summary>
        ResetCounters
    }

    /// <summary>
    /// Typed operation payload. Only fields belonging to Operation are consumed.
    /// </summary>
    public sealed record PackMLRequest
    {
        /// <summary>
        /// Gets the operation.
        /// </summary>
        public PackMLOperation Operation { get; init; }

        /// <summary>
        /// Gets the controller command.
        /// </summary>
        public PackMLCommand Command { get; init; }

        /// <summary>
        /// Gets the requested mode.
        /// </summary>
        public int Mode { get; init; }

        /// <summary>
        /// Gets the requested speed in primary packages per minute.
        /// </summary>
        public float MachineSpeed { get; init; }

        /// <summary>
        /// Gets the product definitions.
        /// </summary>
        public ArrayOf<PackMLProductDataType> Products { get; init; }

        /// <summary>
        /// Gets job or Start parameters.
        /// </summary>
        public ArrayOf<PackMLDescriptorDataType> Parameters { get; init; }

        /// <summary>
        /// Gets the remote interface requests.
        /// </summary>
        public ArrayOf<PackMLRemoteInterfaceDataType> RemoteInterfaces { get; init; }

        /// <summary>
        /// Gets the material interlock index.
        /// </summary>
        public int InterlockId { get; init; }

        /// <summary>
        /// Gets whether the target material is interlocked.
        /// </summary>
        public bool InterlockState { get; init; }

        /// <summary>
        /// Gets the runtime-assigned command identity used to correlate completion.
        /// </summary>
        public long RequestId { get; init; }

        /// <summary>
        /// Gets the runtime-observed state for provider state/interlock policy.
        /// Caller-supplied values are replaced before authorization.
        /// </summary>
        public uint CurrentState { get; init; }

        /// <summary>
        /// Gets the runtime-observed mode for provider authorization.
        /// </summary>
        public int CurrentMode { get; init; }
    }

    /// <summary>
    /// Equipment measurements with acquisition quality and time.
    /// Unavailable data must carry a bad or uncertain status.
    /// </summary>
    public sealed record PackMLEquipmentSnapshot
    {
        /// <summary>
        /// Gets the equipment-confirmed mode, not the last requested mode.
        /// </summary>
        public int CurrentMode { get; init; } = 1;

        /// <summary>
        /// Gets actual speed in primary packages per minute.
        /// </summary>
        public float CurrentMachineSpeed { get; init; }

        /// <summary>
        /// Gets downstream blockage.
        /// </summary>
        public bool EquipmentBlocked { get; init; }

        /// <summary>
        /// Gets upstream starvation.
        /// </summary>
        public bool EquipmentStarved { get; init; }

        /// <summary>
        /// Gets readiness for each configured material. True means ready.
        /// </summary>
        public ArrayOf<bool> MaterialReadiness { get; init; }

        /// <summary>
        /// Gets equipment-reported remote interface parameters, independently of accepted commands.
        /// </summary>
        public ArrayOf<PackMLRemoteInterfaceDataType> RemoteParameters { get; init; }

        /// <summary>
        /// Gets source quality for this coherent snapshot.
        /// </summary>
        public StatusCode StatusCode { get; init; } = StatusCodes.BadWaitingForInitialData;

        /// <summary>
        /// Gets the acquisition time supplied by the equipment.
        /// </summary>
        public DateTimeUtc SourceTimestamp { get; init; }
    }

    /// <summary>
    /// Application-owned equipment and authorization boundary. Acceptance does
    /// not complete an acting state. Implementations retain their own storage
    /// and hardware ownership and must honor cancellation during shutdown.
    /// </summary>
    public interface IPackMLEquipmentProvider
    {
        /// <summary>
        /// Authorizes the operation for the calling context before any mutation.
        /// Counter reset requires the application's administrative permission.
        /// Return a Good status to permit it or a Bad status to deny it.
        /// </summary>
        ValueTask<ServiceResult> AuthorizeAsync(
            ISystemContext context,
            PackMLRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Accepts or rejects an operation. Returns only after equipment has
        /// accepted the request; failures must not be reported as Good.
        /// An Uncertain result is a contract error, not acceptance.
        /// </summary>
        ValueTask<ServiceResult> ExecuteAsync(
            PackMLRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Acquires current equipment data. The runtime never derives actual
        /// speed or material readiness from a successful command.
        /// </summary>
        ValueTask<PackMLEquipmentSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    }
}
