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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server.StateMachines;

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// Hardware or committed-process authority for one drying device.
    /// The runtime never advances a timer, increments a counter, or simulates equipment.
    /// </summary>
    public interface IGranulateDryingProvider
    {
        /// <summary>
        /// Reads initial committed state, including the persistently stored remote-control setting.
        /// Missing process values remain BadWaitingForInitialData.
        /// </summary>
        ValueTask<GranulateDryingCommit> ReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes an authorized request and returns its committed effects.
        /// Unsupported operations must return BadNotSupported, not a successful no-op.
        /// A provider must not call PublishAsync recursively from this operation.
        /// </summary>
        ValueTask<GranulateDryingResult> ExecuteAsync(
            GranulateDryingCommand command,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Authorizes mutations using the authenticated UA context, independently of HMI user login.
    /// Read-only observers never acquire control and are not subject to a one-session limit.
    /// </summary>
    public interface IGranulateDryingAuthorization
    {
        /// <summary>
        /// Checks the current local authorization policy without I/O for UserAccessLevel and UserExecutable.
        /// This check must not wait for asynchronous work.
        /// </summary>
        bool CanControl(ISystemContext context);

        /// <summary>
        /// Returns Good only when this authenticated session may perform the requested operation.
        /// This is called on every mutation, including an existing control owner's requests.
        /// </summary>
        ValueTask<StatusCode> AuthorizeAsync(
            ISystemContext context,
            GranulateDryingCommand command,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The standard object targeted by a request or committed value.
    /// </summary>
    public enum GranulateDryingTargetKind
    {
        /// <summary>
        /// The complete device, including Operation and MachineConfiguration.
        /// </summary>
        Device,

        /// <summary>
        /// A drying bin.
        /// </summary>
        Bin,

        /// <summary>
        /// A desiccant bed.
        /// </summary>
        DesiccantBed,

        /// <summary>
        /// A maintenance activity.
        /// </summary>
        Maintenance
    }

    /// <summary>
    /// Identifies a device or a consecutively numbered standard placeholder instance.
    /// </summary>
    /// <param name="Kind">The kind of standard object.</param>
    /// <param name="Number">Zero for the device, otherwise the one-based placeholder number.</param>
    public readonly record struct GranulateDryingTarget(GranulateDryingTargetKind Kind, int Number = 0)
    {
        /// <summary>
        /// Gets the device target.
        /// </summary>
        public static GranulateDryingTarget Device => default;

        /// <summary>
        /// Identifies a bin by its one-based number.
        /// </summary>
        public static GranulateDryingTarget Bin(int number) => new(GranulateDryingTargetKind.Bin, number);

        /// <summary>
        /// Identifies a desiccant bed by its one-based number.
        /// </summary>
        public static GranulateDryingTarget Bed(int number) => new(GranulateDryingTargetKind.DesiccantBed, number);

        /// <summary>
        /// Identifies a maintenance category by its one-based number.
        /// </summary>
        public static GranulateDryingTarget Maintenance(int number) => new(GranulateDryingTargetKind.Maintenance,
            number);
    }

    /// <summary>
    /// OPC 40082-6 operations. The value-write operation carries an exact compiled member path.
    /// The common OPC 40083 binders retain their own operation contracts.
    /// </summary>
    public enum GranulateDryingOperation
    {
        /// <summary>
        /// Write a supported standard variable.
        /// </summary>
        WriteValue,

        /// <summary>
        /// Identify the physical device.
        /// </summary>
        IdentifyDevice,

        /// <summary>
        /// Reset all errors.
        /// </summary>
        ResetAllErrors,

        /// <summary>
        /// Reset one error; ErrorId supplies its identifier.
        /// </summary>
        ResetErrorById,

        /// <summary>
        /// Reset every published resettable energy counter.
        /// </summary>
        ResetEnergyMeasurement,

        /// <summary>
        /// Activate drying protection.
        /// </summary>
        ActivateDryingProtection,

        /// <summary>
        /// Deactivate drying protection.
        /// </summary>
        DeactivateDryingProtection,

        /// <summary>
        /// Activate the material feeder filling the dryer.
        /// </summary>
        ActivateMaterialFeederDryer,

        /// <summary>
        /// Deactivate the material feeder filling the dryer.
        /// </summary>
        DeactivateMaterialFeederDryer,

        /// <summary>
        /// Activate the material feeder supplying the machine.
        /// </summary>
        ActivateMaterialFeederMachine,

        /// <summary>
        /// Deactivate the material feeder supplying the machine.
        /// </summary>
        DeactivateMaterialFeederMachine,

        /// <summary>
        /// Activate both feeders.
        /// </summary>
        ActivateAllFeeders,

        /// <summary>
        /// Deactivate both feeders.
        /// </summary>
        DeactivateAllFeeders,

        /// <summary>
        /// Activate the temperature controller.
        /// </summary>
        ActivateTemperatureController,

        /// <summary>
        /// Deactivate the temperature controller.
        /// </summary>
        DeactivateTemperatureController,

        /// <summary>
        /// Reset the drying timer and its response timestamp.
        /// </summary>
        ResetDryingTime,

        /// <summary>
        /// Activate airflow reduction during drying protection.
        /// </summary>
        ActivateDryingProtectionAirVolumeFlowReduction,

        /// <summary>
        /// Deactivate airflow reduction during drying protection.
        /// </summary>
        DeactivateDryingProtectionAirVolumeFlowReduction,

        /// <summary>
        /// Activate the bed controller.
        /// </summary>
        ActivateDesiccantBedController,

        /// <summary>
        /// Deactivate the bed controller.
        /// </summary>
        DeactivateDesiccantBedController,

        /// <summary>
        /// Request HMI login of the machine user; User contains its fields.
        /// </summary>
        LoginMachineUser,

        /// <summary>
        /// Request logout of the machine user.
        /// </summary>
        LogoutMachineUser,

        /// <summary>
        /// Record completed maintenance with its operator and machine attribution.
        /// </summary>
        GDDMaintenanceReset,

        /// <summary>
        /// Authorize a common-model member handled by an injected typed common provider.
        /// </summary>
        CommonOperation
    }

    /// <summary>
    /// A validated request delivered to the physical device authority.
    /// Session identity comes from authorization, never from the supplied HMI user strings.
    /// </summary>
    /// <param name="Operation">The standard operation.</param>
    /// <param name="Target">The standard object receiving the operation.</param>
    public sealed record GranulateDryingCommand(
        GranulateDryingOperation Operation,
        GranulateDryingTarget Target)
    {
        /// <summary>
        /// Gets the authenticated session identifier.
        /// </summary>
        public NodeId SessionId { get; init; }

        /// <summary>
        /// Gets the slash-separated standard member path for a value write.
        /// </summary>
        public string Member { get; init; } = string.Empty;

        /// <summary>
        /// Gets the type-checked proposed value for a value write.
        /// </summary>
        public Variant Value { get; init; }

        /// <summary>
        /// Gets the active-error identifier for ResetErrorById.
        /// </summary>
        public string ErrorId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the requested HMI user for LoginMachineUser.
        /// </summary>
        public GranulateDryingUser? User { get; init; }

        /// <summary>
        /// Gets the operator and machine attribution for GDDMaintenanceReset.
        /// </summary>
        public GranulateDryingMaintenanceReset? Maintenance { get; init; }
    }

    /// <summary>
    /// The device authority's result. A successful state-changing operation includes a newer commit.
    /// IdentifyDevice may succeed without changing process state.
    /// </summary>
    /// <param name="StatusCode">The exact UA result.</param>
    /// <param name="Commit">Committed effects, or null when the request failed or only identified the device.</param>
    public sealed record GranulateDryingResult(StatusCode StatusCode, GranulateDryingCommit? Commit = null);

    /// <summary>
    /// One absolute value with its original source quality and timestamp.
    /// Values are not deltas; reapplying an identical revision is idempotent.
    /// </summary>
    /// <param name="Target">The object containing the variable.</param>
    /// <param name="Member">The exact slash-separated compiled member path relative to the target.</param>
    /// <param name="Value">The source value, quality and timestamp.</param>
    public readonly record struct GranulateDryingValue(
        GranulateDryingTarget Target,
        string Member,
        DataValue Value);

    /// <summary>
    /// An atomic, monotonic committed update from hardware or a durable process coordinator.
    /// Omitted values keep their last committed state. Null arrays mean unchanged; empty arrays clear.
    /// </summary>
    /// <param name="Revision">The monotonically increasing authority revision.</param>
    /// <param name="Timestamp">The commit time, not a replacement for individual measurement timestamps.</param>
    public sealed record GranulateDryingCommit(ulong Revision, DateTimeUtc Timestamp)
    {
        /// <summary>
        /// Gets absolute variable updates. Duplicate target/member pairs are rejected.
        /// </summary>
        public ArrayOf<GranulateDryingValue> Values { get; init; } = [];

        /// <summary>
        /// Gets the persistently stored remote-control setting, or null when unchanged.
        /// It cannot be changed by a normal UA variable write.
        /// </summary>
        public bool? RemoteControlEnabled { get; init; }

        /// <summary>
        /// Gets the complete active-error list, or a null array when unchanged.
        /// </summary>
        public ArrayOf<GranulateDryingError> Errors { get; init; }

        /// <summary>
        /// Gets the actual HMI user, or null when unchanged.
        /// </summary>
        public GranulateDryingUser? User { get; init; }

        /// <summary>
        /// Gets the committed Machinery item state, or null when unchanged.
        /// </summary>
        public MachineryItemStateValue? MachineState { get; init; }

        /// <summary>
        /// Gets the committed optional Machinery operation mode, or null when unchanged.
        /// </summary>
        public MachineryOperationModeValue? OperationMode { get; init; }
    }

    /// <summary>
    /// An active device error used for both the mandatory error array and alarm publication.
    /// </summary>
    /// <param name="Id">The stable error identifier accepted by ResetErrorById.</param>
    /// <param name="Severity">The UA severity, in the range 1–1000.</param>
    /// <param name="Classification">The OPC 40083 error classification.</param>
    /// <param name="Message">The localized error text.</param>
    /// <param name="Sources">Affected standard objects, including bins where applicable.</param>
    public sealed record GranulateDryingError(
        string Id,
        ushort Severity,
        ushort Classification,
        LocalizedText Message,
        ArrayOf<GranulateDryingTarget> Sources)
    {
        /// <summary>
        /// Gets supplementary operator assistance.
        /// </summary>
        public LocalizedText HelpText { get; init; }
    }

    /// <summary>
    /// Actual or requested HMI user data. These fields are not UA credentials or authorization grants.
    /// </summary>
    public sealed record GranulateDryingUser
    {
        /// <summary>
        /// Gets whether a user is present at the HMI.
        /// </summary>
        public bool IsPresent { get; init; }

        /// <summary>
        /// Gets the HMI user identifier.
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// Gets the user's display name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets the optional card identifier.
        /// </summary>
        public string CardUid { get; init; } = string.Empty;

        /// <summary>
        /// Gets the machine's user-level label.
        /// </summary>
        public string UserLevel { get; init; } = string.Empty;

        /// <summary>
        /// Gets the machine's user-role label.
        /// </summary>
        public string UserRole { get; init; } = string.Empty;

        /// <summary>
        /// Gets the user's preferred locale.
        /// </summary>
        public string Language { get; init; } = string.Empty;
    }

    /// <summary>
    /// The exact attribution carried by the GDD-specific maintenance-reset method.
    /// </summary>
    /// <param name="OperatorName">The operator who performed maintenance.</param>
    /// <param name="MachineManufacturer">The connected production machine's manufacturer.</param>
    /// <param name="MachineSerialNumber">The connected production machine's serial number.</param>
    public sealed record GranulateDryingMaintenanceReset(
        string OperatorName,
        string MachineManufacturer,
        string MachineSerialNumber);
}
