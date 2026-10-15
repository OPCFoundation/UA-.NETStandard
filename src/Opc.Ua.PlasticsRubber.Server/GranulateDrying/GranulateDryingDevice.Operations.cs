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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.GranulateDrying.Server
{
    public sealed partial class GranulateDryingDevice
    {
        /// <summary>
        /// Binds the exact generated GDD methods; typed argument methods use generated OnCallAsync handlers.
        /// </summary>
        private void BindOperations()
        {
            GranulateDryingTarget device = GranulateDryingTarget.Device;
            OperationState operation = State.Operation!;
            Bind(operation.IdentifyDevice, device, GranulateDryingOperation.IdentifyDevice);
            Bind(operation.ResetAllErrors, device, GranulateDryingOperation.ResetAllErrors);
            Bind(operation.ResetEnergyMeasurement, device, GranulateDryingOperation.ResetEnergyMeasurement);
            if (operation.ResetErrorById != null)
            {
                m_boundMethods.Add(operation.ResetErrorById);
                operation.ResetErrorById.OnCallAsync = async (context, _, target, id, ct) =>
                    new ResetErrorByIdMethodStateResult
                    {
                        ServiceResult = target == operation.NodeId
                            ? await ExecuteAsync(context, new GranulateDryingCommand(
                                GranulateDryingOperation.ResetErrorById, device) { ErrorId = id }, ct)
                                .ConfigureAwait(false)
                            : StatusCodes.BadNodeIdInvalid
                    };
            }
            if (State.UserLogin is { } login)
            {
                m_boundMethods.Add(login.LoginMachineUser!);
                login.LoginMachineUser!.OnCallAsync =
                    async (context, _, target, id, name, cardUid, level, role, language, ct) =>
                        new LoginMachineUserMethodStateResult
                        {
                            ServiceResult = target == login.NodeId
                                ? await ExecuteAsync(context, new GranulateDryingCommand(
                                    GranulateDryingOperation.LoginMachineUser, device)
                                {
                                    User = new GranulateDryingUser
                                    {
                                        IsPresent = true,
                                        Id = id,
                                        Name = name,
                                        CardUid = cardUid,
                                        UserLevel = level,
                                        UserRole = role,
                                        Language = language
                                    }
                                }, ct).ConfigureAwait(false)
                                : StatusCodes.BadNodeIdInvalid
                        };
                Bind(login.LogoutMachineUser, device, GranulateDryingOperation.LogoutMachineUser);
            }
            for (int ii = 1; ii <= Options.Bins.Count; ii++)
            {
                BinState bin = Bin(ii);
                GranulateDryingTarget target = GranulateDryingTarget.Bin(ii);
                Bind(bin.ActivateDryingProtection, target, GranulateDryingOperation.ActivateDryingProtection);
                Bind(bin.DeactivateDryingProtection, target, GranulateDryingOperation.DeactivateDryingProtection);
                Bind(bin.ActivateMaterialFeederDryer, target, GranulateDryingOperation.ActivateMaterialFeederDryer);
                Bind(bin.DeactivateMaterialFeederDryer, target, GranulateDryingOperation.DeactivateMaterialFeederDryer);
                Bind(bin.ActivateMaterialFeederMachine, target, GranulateDryingOperation.ActivateMaterialFeederMachine);
                Bind(bin.DeactivateMaterialFeederMachine, target,
                    GranulateDryingOperation.DeactivateMaterialFeederMachine);
                Bind(bin.ActivateAllFeeders, target, GranulateDryingOperation.ActivateAllFeeders);
                Bind(bin.DeactivateAllFeeders, target, GranulateDryingOperation.DeactivateAllFeeders);
                Bind(bin.ActivateTemperatureController, target, GranulateDryingOperation.ActivateTemperatureController);
                Bind(bin.DeactivateTemperatureController, target,
                    GranulateDryingOperation.DeactivateTemperatureController);
                Bind(bin.ResetDryingTime, target, GranulateDryingOperation.ResetDryingTime);
                Bind(bin.ActivateDryingProtectionAirVolumeFlowReduction, target,
                    GranulateDryingOperation.ActivateDryingProtectionAirVolumeFlowReduction);
                Bind(bin.DeactivateDryingProtectionAirVolumeFlowReduction, target,
                    GranulateDryingOperation.DeactivateDryingProtectionAirVolumeFlowReduction);
            }
            for (int ii = 1; ii <= Options.DesiccantBeds.Count; ii++)
            {
                DesiccantBedState bed = Bed(ii);
                GranulateDryingTarget target = GranulateDryingTarget.Bed(ii);
                Bind(bed.ActivateDesiccantBedController, target,
                    GranulateDryingOperation.ActivateDesiccantBedController);
                Bind(bed.DeactivateDesiccantBedController, target,
                    GranulateDryingOperation.DeactivateDesiccantBedController);
            }
            for (int ii = 1; ii <= Options.Maintenance.Count; ii++)
            {
                GDDMaintenanceState maintenance = Maintenance(ii);
                GranulateDryingTarget target = GranulateDryingTarget.Maintenance(ii);
                m_boundMethods.Add(maintenance.GDDMaintenanceReset!);
                maintenance.GDDMaintenanceReset!.OnCallAsync =
                    async (context, _, targetId, operatorName, manufacturer, serialNumber, ct) =>
                        new GDDMaintenanceResetMethodStateResult
                        {
                            ServiceResult = targetId == maintenance.NodeId
                                ? await ExecuteAsync(context, new GranulateDryingCommand(
                                    GranulateDryingOperation.GDDMaintenanceReset, target)
                                {
                                    Maintenance = new GranulateDryingMaintenanceReset(
                                        operatorName, manufacturer, serialNumber)
                                }, ct).ConfigureAwait(false)
                                : StatusCodes.BadNodeIdInvalid
                        };
            }
        }

        /// <summary>
        /// Binds a selected parameterless standard method.
        /// </summary>
        private void Bind(MethodState? method, GranulateDryingTarget target, GranulateDryingOperation operation)
        {
            if (method == null)
            {
                return;
            }
            m_boundMethods.Add(method);
            method.OnCallMethod2Async = (context, _, targetId, input, _, ct) =>
            {
                if (method.Parent?.NodeId != targetId)
                {
                    return new ValueTask<ServiceResult>(StatusCodes.BadNodeIdInvalid);
                }
                if (!input.IsEmpty)
                {
                    return new ValueTask<ServiceResult>(StatusCodes.BadTooManyArguments);
                }
                return ExecuteAsync(context, new GranulateDryingCommand(operation, target), ct);
            };
        }

        /// <summary>
        /// Serializes authorized requests and publishes only effects returned by the authority.
        /// A provider failure never claims control or mutates the publication.
        /// </summary>
        private async ValueTask<ServiceResult> ExecuteAsync(
            ISystemContext context,
            GranulateDryingCommand command,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref m_disposed))
            {
                return StatusCodes.BadShutdown;
            }
            using GranulateDryingOperationLifetime.Lease pending = Pending(cancellationToken);
            bool entered = false;
            try
            {
                await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
                entered = true;
                ThrowIfDisposed();
                command = command with { SessionId = SessionId(context) };
                WatchControlSession(command.SessionId);
                await AuthorizeAsync(context, command, pending.Token).ConfigureAwait(false);
                if (command.Operation == GranulateDryingOperation.ResetErrorById &&
                    m_current.Errors.FindIndex(error => error.Id == command.ErrorId) < 0)
                {
                    return StatusCodes.BadNotFound;
                }
                GranulateDryingResult result = await m_provider.ExecuteAsync(command, pending.Token)
                    .ConfigureAwait(false);
                if (result == null)
                {
                    return StatusCodes.BadUnexpectedError;
                }
                if (StatusCode.IsBad(result.StatusCode))
                {
                    return result.StatusCode;
                }
                if (!StatusCode.IsGood(result.StatusCode))
                {
                    return StatusCodes.BadUnexpectedError;
                }
                if (result.Commit == null)
                {
                    if (command.Operation != GranulateDryingOperation.IdentifyDevice)
                    {
                        return StatusCodes.BadInvalidState;
                    }
                }
                else
                {
                    if (m_current.Initialized && result.Commit.Revision <= m_current.Revision)
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    // The provider has committed; caller cancellation must not hide completed physical effects.
                    await ApplyCommitAsync(result.Commit, command, CancellationToken.None, result.StatusCode)
                        .ConfigureAwait(false);
                }
                ClaimControl(context, command.SessionId);
                return result.StatusCode;
            }
            catch (ServiceResultException exception)
            {
                return exception.Result;
            }
            catch (OperationCanceledException)
            {
                return cancellationToken.IsCancellationRequested
                    ? StatusCodes.BadRequestCancelledByClient
                    : StatusCodes.BadShutdown;
            }
            catch (ObjectDisposedException)
            {
                return StatusCodes.BadShutdown;
            }
            finally
            {
                if (entered)
                {
                    EndControlSession();
                    m_gate.Release();
                }
            }
        }

        /// <summary>
        /// Enforces persistent remote enablement, local ACL, ownership, and asynchronous per-request policy.
        /// </summary>
        private async ValueTask AuthorizeAsync(
            ISystemContext context,
            GranulateDryingCommand command,
            CancellationToken cancellationToken)
        {
            if (!m_current.RemoteControlEnabled)
            {
                throw Error(command.Operation == GranulateDryingOperation.WriteValue
                    ? StatusCodes.BadNotWritable : StatusCodes.BadNotExecutable,
                    "RemoteControlOpcUa is disabled.");
            }
            if (!CanMutate(context))
            {
                throw Error(StatusCodes.BadUserAccessDenied, "This session is not authorized to control the device.");
            }
            StatusCode permission = await m_authorization.AuthorizeAsync(context, command, cancellationToken)
                .ConfigureAwait(false);
            if (!StatusCode.IsGood(permission))
            {
                throw new ServiceResultException(
                    StatusCode.IsBad(permission) ? permission : StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>
        /// Claims a per-device lease only after a successful authorized operation.
        /// </summary>
        private void ClaimControl(ISystemContext context, NodeId session)
        {
            if (!m_current.RemoteControlEnabled)
            {
                return;
            }
            lock (m_controlLock)
            {
                if (m_activeControlSession == session && m_activeSessionClosed)
                {
                    return;
                }
                m_controlOwner = session;
            }
        }

        /// <summary>
        /// Watches closure before asynchronous hardware work, so completion cannot reclaim a closed session's lease.
        /// </summary>
        private void WatchControlSession(NodeId session)
        {
            lock (m_controlLock)
            {
                m_activeControlSession = session;
                m_activeSessionClosed = false;
            }
            if (m_sessions == null && Context is ServerSystemContext { Server.SessionManager: { } sessions })
            {
                m_sessions = sessions;
                m_sessions.SessionClosing += OnSessionClosing;
            }
            if (m_sessions != null && !m_sessions.GetSessions().Any(active => active.Id == session))
            {
                lock (m_controlLock)
                {
                    m_activeSessionClosed = true;
                }
            }
        }

        /// <summary>
        /// Ends the closure watch for the completed serialized operation.
        /// </summary>
        private void EndControlSession()
        {
            lock (m_controlLock)
            {
                m_activeControlSession = NodeId.Null;
                m_activeSessionClosed = false;
            }
        }

        /// <summary>
        /// Rejects successful-looking provider responses that omit the required standard effect.
        /// </summary>
        private void ValidateEffects(GranulateDryingCommand command, CommitState next, StatusCode operationStatus)
        {
            string? statusMember = command.Operation switch
            {
                GranulateDryingOperation.ActivateDryingProtection or
                    GranulateDryingOperation.DeactivateDryingProtection => "DryingProtectionStatus",
                GranulateDryingOperation.ActivateMaterialFeederDryer or
                    GranulateDryingOperation.DeactivateMaterialFeederDryer => "MaterialFeederDryerStatus",
                GranulateDryingOperation.ActivateMaterialFeederMachine or
                    GranulateDryingOperation.DeactivateMaterialFeederMachine => "MaterialFeederMachineStatus",
                GranulateDryingOperation.ActivateAllFeeders or
                    GranulateDryingOperation.DeactivateAllFeeders => "AllFeedersStatus",
                GranulateDryingOperation.ActivateTemperatureController or
                    GranulateDryingOperation.DeactivateTemperatureController => "TemperatureControllerStatus",
                GranulateDryingOperation.ActivateDesiccantBedController or
                    GranulateDryingOperation.DeactivateDesiccantBedController => "DesiccantBedControllerStatus",
                GranulateDryingOperation.ActivateDryingProtectionAirVolumeFlowReduction or
                    GranulateDryingOperation.DeactivateDryingProtectionAirVolumeFlowReduction =>
                    "DryingProtectionAirVolumeFlowMode",
                _ => null
            };
            if (statusMember != null)
            {
                bool activate = command.Operation is
                    GranulateDryingOperation.ActivateDryingProtection or
                    GranulateDryingOperation.ActivateMaterialFeederDryer or
                    GranulateDryingOperation.ActivateMaterialFeederMachine or
                    GranulateDryingOperation.ActivateAllFeeders or
                    GranulateDryingOperation.ActivateTemperatureController or
                    GranulateDryingOperation.ActivateDesiccantBedController or
                    GranulateDryingOperation.ActivateDryingProtectionAirVolumeFlowReduction;
                if (!TryValue(next.Values, command.Target, statusMember, out DataValue status) ||
                    !TryEnumeration(status.WrappedValue, out int number) ||
                    (activate ? number is not (1 or 3) : number is not (0 or 2)))
                {
                    throw Error(StatusCodes.BadInvalidState,
                        "The provider must commit the actual or requested FunctionStatus response.");
                }
            }
            switch (command.Operation)
            {
                case GranulateDryingOperation.WriteValue:
                    if (!TryValue(next.Values, command.Target, command.Member, out DataValue written) ||
                        (!Equivalent(written.WrappedValue, command.Value) && command.Member != "DisplayLanguage" &&
                            operationStatus != StatusCodes.GoodClamped))
                    {
                        throw Error(StatusCodes.BadInvalidState,
                            "The provider did not commit the accepted standard variable write.");
                    }
                    break;
                case GranulateDryingOperation.ResetEnergyMeasurement:
                    ValidateEnergyReset(next.Values);
                    break;
                case GranulateDryingOperation.ResetDryingTime:
                    if (!TryValue(next.Values, command.Target, "ResetDryingTimeResponseStatus",
                        out DataValue response) ||
                        !response.WrappedValue.TryGetValue(out DateTimeUtc received)
                            || received == DateTimeUtc.MinValue)
                    {
                        throw Error(StatusCodes.BadInvalidState, "The drying-timer reset needs its receipt timestamp.");
                    }
                    if (TryValue(next.Values, command.Target, "MaterialDryingTime", out DataValue initial) &&
                        TryValue(next.Values, command.Target, "RemainingMaterialDryingTime", out DataValue remaining) &&
                        !Equivalent(initial.WrappedValue, remaining.WrappedValue))
                    {
                        throw Error(StatusCodes.BadInvalidState,
                            "The remaining drying time was not reset to its preset.");
                    }
                    break;
                case GranulateDryingOperation.LoginMachineUser:
                    if (next.User is not { IsPresent: true } user ||
                        user.Id != command.User!.Id || user.Name != command.User.Name)
                    {
                        throw Error(StatusCodes.BadInvalidState,
                            "A successful login must publish the accepted HMI user.");
                    }
                    break;
                case GranulateDryingOperation.LogoutMachineUser:
                    if (next.User is not { IsPresent: false })
                    {
                        throw Error(StatusCodes.BadInvalidState,
                            "A successful logout must clear ActualUser.IsPresent.");
                    }
                    break;
                case GranulateDryingOperation.GDDMaintenanceReset:
                    if (!TryValue(next.Values, command.Target, "Status", out DataValue maintenanceStatus) ||
                        !TryEnumeration(maintenanceStatus.WrappedValue, out int maintenance) || maintenance != 0)
                    {
                        throw Error(StatusCodes.BadInvalidState, "Completed maintenance must be published as NOT_DUE.");
                    }
                    break;
            }
        }

        /// <summary>
        /// Requires the one reset operation to cover every selected device/component resettable counter.
        /// Lifetime energy counters are deliberately untouched.
        /// </summary>
        private void ValidateEnergyReset(Dictionary<NodeId, DataValue> values)
        {
            bool found = false;
            foreach (ValueBinding binding in m_values.Values)
            {
                if (binding.Node.BrowseName.Name == "EnergyConsumptionResettable")
                {
                    found = true;
                    DataValue value = values[binding.Node.NodeId];
                    if (!StatusCode.IsGood(value.StatusCode) ||
                        !value.WrappedValue.TryGetValue(out double energy) || energy != 0)
                    {
                        throw Error(StatusCodes.BadInvalidState,
                            "The energy reset did not cover every component counter.");
                    }
                }
            }
            if (!found)
            {
                throw Error(StatusCodes.BadNotSupported, "The device has no resettable energy measurements.");
            }
        }

        /// <summary>
        /// Compares a generated enumeration and its wire Int32 representation without boxing.
        /// </summary>
        private static bool Equivalent(Variant left, Variant right)
        {
            return left.Equals(right) ||
                (TryEnumeration(left, out int first) && TryEnumeration(right, out int second) && first == second);
        }
    }
}
