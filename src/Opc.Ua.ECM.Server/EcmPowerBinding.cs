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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Redaction;
using static Opc.Ua.ECM.Server.EcmModelNodes;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Binds generated ECM standby and optional WOL nodes to borrowed device providers.
    /// The address-space owner must register the configured graph and dispose the binding at shutdown.
    /// </summary>
    /// <remarks>
    /// Refreshes, bound PauseTime writes and bound StartPause, EndPause, SwitchToEnergySavingMode and SwitchOffWOL
    /// controls remain serialized until their node-change notifications complete, including unavailable-state
    /// notifications. Notification sinks on the bound graph must not await or synchronously block on this binding's
    /// <see cref="RefreshAsync"/>, any of those writes or controls, or <see cref="DisposeAsync"/>.
    /// Those operations cannot complete until the active operation and its notification sink finish.
    /// Synchronous sinks run inline and may block the caller before an asynchronous operation returns.
    /// </remarks>
    public sealed class EcmPowerBinding : IAsyncDisposable
    {
        /// <summary>
        /// Creates bindings on an unregistered generated graph.
        /// Control requires an Operator, Engineer or Supervisor role unless an explicit authorizer is supplied.
        /// </summary>
        public EcmPowerBinding(
            ISystemContext context,
            EnergyStandbyManagementState standby,
            IEcmStandbyProvider provider,
            EnergyDevicePowerOffState? powerOff = null,
            IEcmWolProvider? wolProvider = null,
            ILockService? exclusiveAccess = null,
            Func<ISystemContext, CancellationToken, ValueTask<bool>>? authorize = null)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            Standby = standby ?? throw new ArgumentNullException(nameof(standby));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if ((powerOff == null) != (wolProvider == null))
            {
                throw new ArgumentException("WOL nodes and provider must be configured together.");
            }
            PowerOff = powerOff;
            m_wolProvider = wolProvider;
            m_exclusiveAccess = exclusiveAccess;
            m_authorize = authorize;
            m_logger = context.Telemetry.CreateLogger<EcmPowerBinding>();
            m_ecmNamespace = context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM);

            Standby.AddStartPause(context)
                .AddEndPause(context)
                .AddSwitchToEnergySavingMode(context)
                .AddEnergySavingModes(context);
            Required(Standby.EnergySavingModeStatus).AddCurrentTransitionData(context);
            Required(Required(Standby.StandbyManagementStatus).EnumStrings).Value = ArrayOf.Wrapped<LocalizedText>(
            [
                new("Energy saving disabled"),
                new("Power Off"),
                new("Ready to operate"),
                new("Moving to Energy Saving Mode"),
                new("Energy saving mode"),
                new("Moving to ready to operate"),
                new("Moving to Sleep mode WOL"),
                new("Sleep mode WOL"),
                new("Wake up WOL")
            ]);
            Required(Standby.PauseTime).AccessLevel = AccessLevels.CurrentReadOrWrite;
            Required(Standby.PauseTime).UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            if (PowerOff != null && wolProvider != null)
            {
                EcmWolBinding.ConfigureMetadata(PowerOff, wolProvider);
            }
            if (exclusiveAccess != null)
            {
                Standby.CreateChild(context, new QualifiedName(
                    Di.BrowseNames.Lock, context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi)));
                Required(Standby.Lock).BindToLockService(Standby.NodeId, exclusiveAccess);
            }
            Required(Standby.PauseTime).OnSimpleWriteValueAsync = WritePauseTimeAsync;
            Required(Standby.StartPause).OnCallAsync = async (caller, method, objectId, pauseTime, ct) =>
            {
                EcmControlResult result = await ExecuteAsync(
                    caller, EcmPowerOperation.StartPause, pauseTime, 0, ct).ConfigureAwait(false);
                return new StartPauseMethodStateResult
                {
                    ServiceResult = new ServiceResult(result.Status),
                    ModeID = result.ModeId,
                    CurrentTimeToDestination = result.CurrentTimeToDestination,
                    RegularTimeToOperate = result.RegularTimeToOperate,
                    TimeMinLengthOfStay = result.TimeMinLengthOfStay,
                    ReturnCode = result.ReturnCode.GetValueOrDefault()
                };
            };
            Required(Standby.SwitchToEnergySavingMode).OnCallAsync = async (caller, method, objectId, modeId, ct) =>
            {
                EcmControlResult result = await ExecuteAsync(
                    caller, EcmPowerOperation.SwitchMode, 0, modeId, ct).ConfigureAwait(false);
                return new SwitchToEnergySavingModeMethodStateResult
                {
                    ServiceResult = new ServiceResult(result.Status),
                    EffectiveModeID = result.ModeId,
                    CurrentTimeToDestination = result.CurrentTimeToDestination,
                    RegularTimeToOperate = result.RegularTimeToOperate,
                    TimeMinLengthOfStay = result.TimeMinLengthOfStay,
                    ReturnCode = result.ReturnCode.GetValueOrDefault()
                };
            };
            Required(Standby.EndPause).OnCallAsync = async (caller, method, objectId, ct) =>
            {
                EcmControlResult result = await ExecuteAsync(
                    caller, EcmPowerOperation.EndPause, 0, 0, ct).ConfigureAwait(false);
                return new EndPauseMethodStateResult
                {
                    ServiceResult = new ServiceResult(result.Status),
                    CurrentTimeToOperate = result.CurrentTimeToOperate,
                    ReturnCode = result.ReturnCode.GetValueOrDefault()
                };
            };
            if (PowerOff != null && wolProvider != null)
            {
                Required(PowerOff.SwitchOffWOL).OnCallAsync = async (caller, method, objectId, ct) =>
                {
                    EcmControlResult result = await ExecuteAsync(
                        caller, EcmPowerOperation.SwitchOffWol, 0, 0, ct).ConfigureAwait(false);
                    return new SwitchOffWOLMethodStateResult
                    {
                        ServiceResult = new ServiceResult(result.Status),
                        ModeID = result.ModeId,
                        CurrentTimeToDestination = result.CurrentTimeToDestination,
                        RegularTimeToOperate = result.RegularTimeToOperate,
                        TimeMinLengthOfStay = result.TimeMinLengthOfStay,
                        ReturnCode = result.ReturnCode.GetValueOrDefault()
                    };
                };
            }
        }

        /// <summary>
        /// Gets the generated standby instance.
        /// </summary>
        public EnergyStandbyManagementState Standby { get; }

        /// <summary>
        /// Gets the optional generated WOL instance.
        /// </summary>
        public EnergyDevicePowerOffState? PowerOff { get; }

        /// <summary>
        /// Publishes a coherent provider snapshot, including dynamic mode characteristics.
        /// The owner schedules refreshes at its required acquisition rate.
        /// </summary>
        /// <remarks>
        /// Publication awaits notification sinks within the serialized operation.
        /// Sinks must follow the same-binding operation restrictions documented on <see cref="EcmPowerBinding"/>.
        /// </remarks>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            await m_operations.ExecuteAsync(RefreshGuardedAsync, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops operations and removes the handlers. Borrowed providers are not disposed.
        /// </summary>
        /// <remarks>
        /// Notification sinks must not await or synchronously block on disposal of this binding.
        /// Disposal waits for the active operation, including its notification sinks, to finish.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await m_operations.StopAsync(RemoveCallbacks).ConfigureAwait(false);
            }
            finally
            {
                await m_operations.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reports acquisition failures without releasing the serialized publication scope early.
        /// </summary>
        private async ValueTask<bool> RefreshGuardedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (m_logger.IsEnabled(LogLevel.Error))
                {
                    m_logger.ProviderFailed(exception.ToString());
                }
                await MarkUnavailableAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Releases handlers after all active provider work has stopped.
        /// </summary>
        private void RemoveCallbacks()
        {
            Required(Standby.PauseTime).OnSimpleWriteValueAsync = null;
            Required(Standby.StartPause).OnCallAsync = null;
            Required(Standby.EndPause).OnCallAsync = null;
            Required(Standby.SwitchToEnergySavingMode).OnCallAsync = null;
            if (PowerOff != null)
            {
                Required(PowerOff.SwitchOffWOL).OnCallAsync = null;
            }
        }

        /// <summary>
        /// Sends PauseTime writes through the same provider and authorization pipeline as methods.
        /// </summary>
        private async ValueTask<AttributeWriteResult> WritePauseTimeAsync(
            ISystemContext caller,
            NodeState node,
            Variant value,
            CancellationToken cancellationToken)
        {
            if (!value.TryGetValue(out double pauseTime))
            {
                return new AttributeWriteResult(StatusCodes.BadTypeMismatch);
            }
            EcmControlResult result = await ExecuteAsync(caller,
                pauseTime == 0 ? EcmPowerOperation.EndPause : EcmPowerOperation.StartPause,
                pauseTime, 0, cancellationToken).ConfigureAwait(false);
            return new AttributeWriteResult(new ServiceResult(result.Status)) { SuppressCacheUpdate = true };
        }

        /// <summary>
        /// Serializes commands, checks access, calls the provider once and publishes its actual state.
        /// </summary>
        private ValueTask<EcmControlResult> ExecuteAsync(
            ISystemContext caller,
            EcmPowerOperation operation,
            double pauseTime,
            byte modeId,
            CancellationToken cancellationToken)
        {
            return m_operations.ExecuteAsync(ct => ExecuteCoreAsync(caller, operation, pauseTime, modeId, ct),
                cancellationToken);
        }

        /// <summary>
        /// Executes an authorized command within the shared binding operation scope.
        /// </summary>
        private async ValueTask<EcmControlResult> ExecuteCoreAsync(
            ISystemContext caller,
            EcmPowerOperation operation,
            double pauseTime,
            byte modeId,
            CancellationToken cancellationToken)
        {
            if (double.IsNaN(pauseTime) || double.IsInfinity(pauseTime) || pauseTime < 0)
            {
                return new EcmControlResult(StatusCodes.BadOutOfRange, null);
            }
            try
            {
                if (!await EcmPowerAccess.IsAuthorizedAsync(
                    caller, Standby.NodeId, m_authorize, m_exclusiveAccess, cancellationToken).ConfigureAwait(false))
                {
                    return new EcmControlResult(StatusCodes.BadUserAccessDenied, null);
                }
                EcmControlResult result = operation switch
                {
                    EcmPowerOperation.StartPause => await m_provider.StartPauseAsync(
                        pauseTime, cancellationToken).ConfigureAwait(false),
                    EcmPowerOperation.EndPause => await m_provider.EndPauseAsync(cancellationToken)
                        .ConfigureAwait(false),
                    EcmPowerOperation.SwitchMode => await m_provider.SwitchToEnergySavingModeAsync(
                        modeId, cancellationToken).ConfigureAwait(false),
                    EcmPowerOperation.SwitchOffWol when m_wolProvider != null =>
                        await m_wolProvider.SwitchOffWolAsync(cancellationToken).ConfigureAwait(false),
                    _ => new EcmControlResult(StatusCodes.BadNotSupported, null)
                };
                if (operation == EcmPowerOperation.StartPause && StatusCode.IsUncertain(result.Status) &&
                    result.ModeId != 0)
                {
                    throw new InvalidOperationException("Rejected StartPause requires ModeID zero.");
                }
                if (operation == EcmPowerOperation.SwitchOffWol)
                {
                    EcmWolBinding.ValidateResult(result);
                }
                if (StatusCode.IsGood(result.Status))
                {
                    await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
                }
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (m_logger.IsEnabled(LogLevel.Error))
                {
                    m_logger.ProviderFailed(exception.ToString());
                }
                await MarkUnavailableAsync().ConfigureAwait(false);
                return new EcmControlResult(StatusCodes.BadUnexpectedError, null);
            }
        }

        /// <summary>
        /// Reads and publishes one provider acquisition.
        /// </summary>
        private async ValueTask RefreshCoreAsync(CancellationToken cancellationToken)
        {
            EcmPowerSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            ValidateSnapshot(snapshot);
            Required(Standby.StandbyManagementStatus).Value = (byte)snapshot.Status;
            Required(Standby.PauseTime).Value = snapshot.PauseTime;
            var modeStatus = Required(Standby.EnergySavingModeStatus);
            Required(modeStatus.StateInformation).Value = new EnergyStateInformationDataType
            {
                IDSource = snapshot.SourceModeId,
                IDDestination = snapshot.DestinationModeId,
                RegularTimeToOperate = snapshot.RegularTimeToOperate,
                ModePowerConsumption = snapshot.ModePowerConsumption
            };
            Required(modeStatus.CurrentTransitionData).Value = new StandbyModeTransitionDataType
            {
                IDDestination = snapshot.DestinationModeId,
                CurrentTimeToDestination = snapshot.CurrentTimeToDestination,
                CurrentTimeToOperate = snapshot.CurrentTimeToOperate,
                EnergyConsumptionToDestination = snapshot.EnergyConsumptionToDestination
            };
            for (int ii = 0; ii < snapshot.Modes.Count; ii++)
            {
                PublishMode(snapshot.Modes[ii]);
            }
            SetAcquisitionQuality(snapshot.Quality, snapshot.SourceTimestamp);
            if (m_exclusiveAccess != null)
            {
                await Required(Standby.Lock).RefreshLockStateAsync(
                    m_exclusiveAccess, Standby.NodeId, m_context, cancellationToken).ConfigureAwait(false);
            }
            // The snapshot is committed; cancellation must not interrupt its notifications.
            await Standby.ClearChangeMasksAsync(
                m_context, true, CancellationToken.None).ConfigureAwait(false);
            m_initialized = true;
        }

        /// <summary>
        /// Rejects inconsistent provider state before modifying the published graph.
        /// </summary>
        private void ValidateSnapshot(EcmPowerSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new InvalidOperationException("The ECM provider returned no snapshot.");
            }
            EcmControlResult.ValidateDuration(snapshot.PauseTime, nameof(snapshot.PauseTime));
            EcmControlResult.ValidateDuration(
                snapshot.CurrentTimeToDestination, nameof(snapshot.CurrentTimeToDestination));
            EcmControlResult.ValidateDuration(snapshot.CurrentTimeToOperate, nameof(snapshot.CurrentTimeToOperate));
            EcmControlResult.ValidateDuration(snapshot.RegularTimeToOperate, nameof(snapshot.RegularTimeToOperate));
            EcmControlResult.ValidateDuration(snapshot.ModePowerConsumption, nameof(snapshot.ModePowerConsumption));
            EcmControlResult.ValidateDuration(
                snapshot.EnergyConsumptionToDestination, nameof(snapshot.EnergyConsumptionToDestination));
            if (snapshot.SourceTimestamp.Kind != DateTimeKind.Utc ||
                (byte)snapshot.Status > (byte)EcmStandbyStatus.WakeUpWol)
            {
                throw new InvalidOperationException("A provider must supply a UTC acquisition and a supported state.");
            }
            bool moving = snapshot.Status is EcmStandbyStatus.MovingToEnergySavingMode or
                EcmStandbyStatus.MovingToReadyToOperate or EcmStandbyStatus.MovingToSleepWol or
                EcmStandbyStatus.WakeUpWol;
            if (!moving && (snapshot.CurrentTimeToDestination != 0 || snapshot.EnergyConsumptionToDestination != 0))
            {
                throw new InvalidOperationException(
                    "Arrived states require zero remaining transition time and energy.");
            }
            if (snapshot.Status == EcmStandbyStatus.ReadyToOperate &&
                (snapshot.SourceModeId != 0xFF || snapshot.DestinationModeId != 0xFF ||
                    snapshot.RegularTimeToOperate != 0))
            {
                throw new InvalidOperationException("Ready state requires source/destination 0xFF and zero recovery.");
            }
            if (snapshot.SourceModeId == 0xFF && snapshot.RegularTimeToOperate != 0)
            {
                throw new InvalidOperationException("Source mode 0xFF requires zero regular recovery.");
            }
            if (snapshot.Status == EcmStandbyStatus.EnergySavingMode &&
                snapshot.SourceModeId != snapshot.DestinationModeId)
            {
                throw new InvalidOperationException("An arrived energy-saving state requires matching mode IDs.");
            }
            if (snapshot.Status == EcmStandbyStatus.Disabled &&
                (snapshot.SourceModeId != 0xF0 || snapshot.DestinationModeId != 0xF0))
            {
                throw new InvalidOperationException("Disabled state requires source/destination 0xF0.");
            }
            var ids = new HashSet<byte>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int ii = 0; ii < snapshot.Modes.Count; ii++)
            {
                EcmEnergySavingMode mode = snapshot.Modes[ii] ??
                    throw new InvalidOperationException("The ECM provider returned a null mode.");
                if (!ids.Add(mode.Id) || !names.Add(mode.Name))
                {
                    throw new InvalidOperationException("The ECM provider returned duplicate mode identities.");
                }
                if (m_initialized && (!m_modes.TryGetValue(mode.Id, out EnergySavingModeState? existing) ||
                    existing.BrowseName.Name != mode.Name))
                {
                    throw new InvalidOperationException("A provider cannot replace published mode identities.");
                }
                if (m_characteristics.TryGetValue(mode.Id, out EcmEnergySavingMode? previous) &&
                    (previous.DynamicData != mode.DynamicData || (!previous.DynamicData && previous != mode)))
                {
                    throw new InvalidOperationException("Only dynamic modes may revise published characteristics.");
                }
            }
            if ((snapshot.Status == EcmStandbyStatus.EnergySavingMode &&
                    !ids.Contains(snapshot.DestinationModeId)) ||
                (snapshot.Status == EcmStandbyStatus.MovingToEnergySavingMode &&
                    !ids.Contains(snapshot.DestinationModeId)) ||
                (snapshot.Status is EcmStandbyStatus.MovingToSleepWol or EcmStandbyStatus.SleepWol &&
                    (m_wolProvider == null || snapshot.DestinationModeId != 0xFE)) ||
                (snapshot.Status is EcmStandbyStatus.MovingToReadyToOperate or EcmStandbyStatus.WakeUpWol &&
                    snapshot.DestinationModeId != 0xFF))
            {
                throw new InvalidOperationException("The published state does not identify a supported destination.");
            }
            if ((snapshot.Status == EcmStandbyStatus.MovingToEnergySavingMode &&
                    snapshot.SourceModeId != 0xFF && !ids.Contains(snapshot.SourceModeId)) ||
                (snapshot.Status == EcmStandbyStatus.MovingToReadyToOperate &&
                    !ids.Contains(snapshot.SourceModeId)) ||
                (snapshot.Status == EcmStandbyStatus.SleepWol && snapshot.SourceModeId != 0xFE) ||
                (snapshot.Status == EcmStandbyStatus.WakeUpWol &&
                    (m_wolProvider == null || snapshot.SourceModeId != 0xFE)))
            {
                throw new InvalidOperationException("The published state does not identify a supported source.");
            }
            if (m_initialized && m_modes.Count != snapshot.Modes.Count)
            {
                throw new InvalidOperationException("A provider cannot remove published mode identities.");
            }
        }

        /// <summary>
        /// Publishes current characteristics without replacing stable mode identities.
        /// </summary>
        private void PublishMode(EcmEnergySavingMode mode)
        {
            if (!m_modes.TryGetValue(mode.Id, out EnergySavingModeState? node))
            {
                node = m_context.CreateInstanceOfEnergySavingModeType(
                    Required(Standby.EnergySavingModes), new QualifiedName(mode.Name, m_ecmNamespace));
                node.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                Required(Required(node.ModePowerConsumption).EngineeringUnits).Value = new EUInformation(
                    "kW", "kilowatt", "http://www.opcfoundation.org/UA/units/un/cefact") { UnitId = 0x4B5754 };
                Required(Required(node.EnergyConsumptionToPause).EngineeringUnits).Value = new EUInformation(
                    "kWh", "kilowatt hour", "http://www.opcfoundation.org/UA/units/un/cefact") { UnitId = 0x4B5748 };
                Required(Required(node.EnergyConsumptionToOperate).EngineeringUnits).Value = new EUInformation(
                    "kWh", "kilowatt hour", "http://www.opcfoundation.org/UA/units/un/cefact") { UnitId = 0x4B5748 };
                Required(Standby.EnergySavingModes).AddChild(node);
                m_modes.Add(mode.Id, node);
            }
            Required(node.ID).Value = mode.Id;
            Required(node.DynamicData).Value = mode.DynamicData;
            Required(node.TimeMinPause).Value = mode.TimeMinPause;
            Required(node.TimeToPause).Value = mode.TimeToPause;
            Required(node.TimeMinLengthOfStay).Value = mode.TimeMinLengthOfStay;
            Required(node.TimeMaxLengthOfStay).Value = mode.TimeMaxLengthOfStay;
            Required(node.RegularTimeToOperate).Value = mode.RegularTimeToOperate;
            Required(node.ModePowerConsumption).Value = mode.ModePowerConsumption;
            Required(node.EnergyConsumptionToPause).Value = mode.EnergyConsumptionToPause;
            Required(node.EnergyConsumptionToOperate).Value = mode.EnergyConsumptionToOperate;
            m_characteristics[mode.Id] = mode;
        }

        /// <summary>
        /// Preserves the last values and acquisition timestamps when a provider becomes unavailable.
        /// </summary>
        private async ValueTask MarkUnavailableAsync()
        {
            SetAcquisitionQuality(StatusCodes.BadNoCommunication, null);
            // Unavailable state is committed and must be reported before the triggering failure escapes.
            await Standby.ClearChangeMasksAsync(
                m_context, true, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Applies acquisition quality without changing stable identity, unit or enumeration metadata.
        /// </summary>
        private void SetAcquisitionQuality(StatusCode quality, DateTime? timestamp)
        {
            SetQuality(Required(Standby.StandbyManagementStatus), quality, timestamp);
            SetQuality(Required(Standby.PauseTime), quality, timestamp);
            SetQuality(Required(Required(Standby.EnergySavingModeStatus).StateInformation), quality, timestamp);
            SetQuality(Required(Required(Standby.EnergySavingModeStatus).CurrentTransitionData), quality, timestamp);
            foreach (EnergySavingModeState mode in m_modes.Values)
            {
                SetQuality(Required(mode.TimeMinPause), quality, timestamp);
                SetQuality(Required(mode.TimeToPause), quality, timestamp);
                SetQuality(Required(mode.TimeMinLengthOfStay), quality, timestamp);
                SetQuality(Required(mode.TimeMaxLengthOfStay), quality, timestamp);
                SetQuality(Required(mode.RegularTimeToOperate), quality, timestamp);
                SetQuality(Required(mode.ModePowerConsumption), quality, timestamp);
                SetQuality(Required(mode.EnergyConsumptionToPause), quality, timestamp);
                SetQuality(Required(mode.EnergyConsumptionToOperate), quality, timestamp);
            }
        }

        /// <summary>
        /// Updates one acquisition while retaining the last source time when a provider fails.
        /// </summary>
        private static void SetQuality(BaseVariableState variable, StatusCode quality, DateTime? timestamp)
        {
            variable.StatusCode = quality;
            if (timestamp.HasValue)
            {
                variable.Timestamp = timestamp.Value;
            }
        }

        /// <summary>The owning address-space context.</summary>
        private readonly ISystemContext m_context;

        /// <summary>The borrowed standby provider.</summary>
        private readonly IEcmStandbyProvider m_provider;

        /// <summary>The borrowed WOL provider.</summary>
        private readonly IEcmWolProvider? m_wolProvider;

        /// <summary>The optional DI exclusive-access service.</summary>
        private readonly ILockService? m_exclusiveAccess;

        /// <summary>The optional application policy.</summary>
        private readonly Func<ISystemContext, CancellationToken, ValueTask<bool>>? m_authorize;

        /// <summary>The runtime logger.</summary>
        private readonly ILogger m_logger;

        /// <summary>The actual ECM namespace index.</summary>
        private readonly ushort m_ecmNamespace;

        /// <summary>The mode nodes with stable identities.</summary>
        private readonly Dictionary<byte, EnergySavingModeState> m_modes = [];

        /// <summary>The last published characteristics and immutable dynamic-data declarations.</summary>
        private readonly Dictionary<byte, EcmEnergySavingMode> m_characteristics = [];

        /// <summary>Serializes and drains all provider callbacks.</summary>
        private readonly EcmBindingOperations m_operations = new();

        /// <summary>Whether the mode identities have been published.</summary>
        private bool m_initialized;
    }

    /// <summary>
    /// Provider operations serialized by one power binding.
    /// </summary>
    internal enum EcmPowerOperation
    {
        /// <summary>
        /// Starts a pause.
        /// </summary>
        StartPause,

        /// <summary>
        /// Ends a pause.
        /// </summary>
        EndPause,

        /// <summary>
        /// Selects a mode.
        /// </summary>
        SwitchMode,

        /// <summary>
        /// Requests simulated or application-provided sleep.
        /// </summary>
        SwitchOffWol
    }

    /// <summary>
    /// Source-generated ECM provider diagnostics.
    /// </summary>
    internal static partial class EcmPowerBindingLog
    {
        /// <summary>
        /// Reports a provider failure with classified exception text.
        /// </summary>
        [LoggerMessage(
            EventId = EcmServerEventIds.EcmPowerBinding,
            Level = LogLevel.Error,
            Message = "The ECM power provider failed: {Error}")]
        public static partial void ProviderFailed(this ILogger logger, [SensitiveText] string error);
    }
}
