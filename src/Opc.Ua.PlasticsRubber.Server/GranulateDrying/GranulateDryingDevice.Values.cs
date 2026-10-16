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
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Server;
using ItemIds = Opc.Ua.Machinery.MachineryItemState_StateMachineTypeIds;
using ModeIds = Opc.Ua.Machinery.MachineryOperationModeStateMachineTypeIds;

namespace Opc.Ua.GranulateDrying.Server
{
    public sealed partial class GranulateDryingDevice
    {
        /// <summary>
        /// Indexes process values and installs authoritative read hooks before registration.
        /// Static metadata, identification, and file-handle state remain owned by their existing modules.
        /// </summary>
        private void IndexValues(
            GranulateDryingTarget target, NodeState node, string prefix, bool identification = false)
        {
            identification |= node is MachineIdentificationState;
            var children = new List<BaseInstanceState>();
            node.GetChildren(Context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is MethodState or FileState ||
                    child is BinState or DesiccantBedState or GDDMaintenanceState)
                {
                    continue;
                }
                string name = child.BrowseName.Name ?? string.Empty;
                string path = prefix.Length == 0 ? name : prefix + "/" + name;
                if (child is BaseVariableState variable)
                {
                    if (name == Opc.Ua.BrowseNames.NodeVersion ||
                        (target.Kind == GranulateDryingTargetKind.Maintenance && name == "Id"))
                    {
                        continue;
                    }
                    var binding = new ValueBinding(
                        target, path, variable, variable.AccessLevel, variable.UserAccessLevel);
                    m_values.Add((target, path), binding);
                    if (m_current.Values.ContainsKey(variable.NodeId))
                    {
                        continue;
                    }
                    var waiting = new DataValue(variable.WrappedValue,
                        identification && !variable.WrappedValue.IsNull
                            ? StatusCodes.Good : StatusCodes.BadWaitingForInitialData, DateTimeUtc.Now);
                    m_current.Values.Add(variable.NodeId, waiting);
                    variable.StatusCode = waiting.StatusCode;
                    variable.Timestamp = waiting.SourceTimestamp;
                    variable.OnReadValueAsync = (_, _, range, encoding, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return new ValueTask<AttributeReadResult>(ReadValue(binding, range, encoding));
                    };
                    variable.OnReadValue = (ISystemContext _, NodeState _, NumericRange range,
                        QualifiedName encoding, ref Variant value,
                        ref StatusCode quality, ref DateTimeUtc timestamp) =>
                    {
                        AttributeReadResult result = ReadValue(binding, range, encoding);
                        value = result.Value;
                        quality = result.StatusCode;
                        timestamp = result.SourceTimestamp;
                        return result.Result;
                    };
                    variable.OnReadUserAccessLevel = ReadUserAccessLevel;
                    if ((variable.AccessLevel & AccessLevels.CurrentWrite) != 0)
                    {
                        variable.OnSimpleWriteValueAsync = (context, _, value, ct) =>
                            WriteValueAsync(context, binding, value, ct);
                    }
                    if (variable is not FiniteStateVariableState and not FiniteTransitionVariableState)
                    {
                        continue;
                    }
                }
                IndexValues(target, child, path, identification);
            }
        }

        /// <summary>
        /// Returns the immutable commit, not the cache that UA's successful write pipeline may overwrite.
        /// </summary>
        private AttributeReadResult ReadValue(ValueBinding binding, NumericRange range, QualifiedName encoding)
        {
            DataValue committed = Volatile.Read(ref m_current).Values[binding.Node.NodeId];
            if (!encoding.IsNull)
            {
                return new AttributeReadResult(
                    StatusCodes.BadDataEncodingUnsupported, Variant.Null,
                    StatusCodes.BadDataEncodingUnsupported, committed.SourceTimestamp);
            }
            Variant value = CoreUtils.Clone(committed.WrappedValue);
            StatusCode result = range.IsNull ? StatusCodes.Good : range.ApplyRange(ref value);
            return new AttributeReadResult(result, value, committed.StatusCode, committed.SourceTimestamp);
        }

        /// <summary>
        /// Routes a standard value write through the same authorization and commit path as methods.
        /// </summary>
        private async ValueTask<AttributeWriteResult> WriteValueAsync(
            ISystemContext context,
            ValueBinding binding,
            Variant value,
            CancellationToken cancellationToken)
        {
            try
            {
                ValidateValue(binding, value, StatusCodes.Good);
                var command = new GranulateDryingCommand(GranulateDryingOperation.WriteValue, binding.Target)
                {
                    SessionId = SessionId(context),
                    Member = binding.Member,
                    Value = value
                };
                return new AttributeWriteResult(
                    await ExecuteAsync(context, command, cancellationToken).ConfigureAwait(false))
                {
                    SuppressCacheUpdate = true
                };
            }
            catch (ServiceResultException exception)
            {
                return new AttributeWriteResult(exception.Result);
            }
        }

        /// <summary>
        /// Validates the complete batch and its operation postconditions before publication.
        /// </summary>
        private async ValueTask ApplyCommitAsync(
            GranulateDryingCommit commit,
            GranulateDryingCommand? command,
            CancellationToken cancellationToken,
            StatusCode operationStatus = default)
        {
            CommitState previous = m_current;
            if (commit.Timestamp == DateTimeUtc.MinValue)
            {
                throw Error(StatusCodes.BadInvalidTimestamp, "A commit must have an explicit source timestamp.");
            }
            if (previous.Initialized && commit.Revision < previous.Revision)
            {
                throw Error(StatusCodes.BadInvalidState, "The authority revision precedes the published revision.");
            }
            var values = new Dictionary<NodeId, DataValue>(previous.Values);
            var updated = new HashSet<NodeId>();
            foreach (GranulateDryingValue update in commit.Values)
            {
                ValueBinding binding = RequiredValue(update.Target, update.Member);
                if (IsReserved(binding))
                {
                    throw Error(StatusCodes.BadInvalidArgument,
                        "Use the dedicated commit fields for remote control, active errors, users, " +
                        "and Machinery state.");
                }
                if (!updated.Add(binding.Node.NodeId))
                {
                    throw Error(StatusCodes.BadInvalidArgument, "A commit cannot update a value more than once.");
                }
                if (update.Value.SourceTimestamp == DateTimeUtc.MinValue)
                {
                    throw Error(StatusCodes.BadInvalidTimestamp, "A committed measurement needs its source timestamp.");
                }
                ValidateValue(binding, update.Value.WrappedValue, update.Value.StatusCode);
                values[binding.Node.NodeId] = update.Value
                    .WithWrappedValue(CoreUtils.Clone(update.Value.WrappedValue))
                    .WithServerTimestamp(DateTimeUtc.MinValue);
            }

            bool remote = commit.RemoteControlEnabled ?? previous.RemoteControlEnabled;
            if (commit.RemoteControlEnabled.HasValue)
            {
                Stage(values, GranulateDryingTarget.Device, "Operation/RemoteControlOpcUa",
                    Variant.From(remote), commit.Timestamp);
            }
            ArrayOf<GranulateDryingError> errors = commit.Errors.IsNull
                ? previous.Errors : ValidateErrors(commit.Errors);
            if (!commit.Errors.IsNull)
            {
                StageErrors(values, errors, commit.Timestamp);
            }
            GranulateDryingUser? user = commit.User ?? previous.User;
            if (commit.User != null)
            {
                StageUser(values, commit.User, commit.Timestamp);
            }
            if (commit.MachineState.HasValue)
            {
                StageMachineState(values, commit.MachineState.Value, commit.Timestamp);
            }
            if (commit.OperationMode.HasValue)
            {
                StageOperationMode(values, commit.OperationMode.Value, commit.Timestamp);
            }
            ValidateRelationships(values);
            var next = new CommitState(true, commit.Revision, values, remote, errors, user, commit);
            if (previous.Initialized && commit.Revision == previous.Revision)
            {
                if (SameValues(previous, next))
                {
                    return;
                }
                throw Error(StatusCodes.BadInvalidState, "A repeated revision must reproduce the committed state.");
            }
            if (command != null)
            {
                ValidateEffects(command, next, operationStatus);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await PrepareConditionsAsync(next, cancellationToken).ConfigureAwait(false);

            // Read hooks see the old immutable snapshot until the entire validated batch is ready.
            foreach (ValueBinding binding in m_values.Values)
            {
                DataValue value = values[binding.Node.NodeId];
                binding.Node.WrappedValue = value.WrappedValue;
                binding.Node.StatusCode = value.StatusCode;
                binding.Node.Timestamp = value.SourceTimestamp;
                binding.Node.AccessLevel = remote
                    ? binding.SupportedAccessLevel : (byte)(binding.SupportedAccessLevel & ~AccessLevels.CurrentWrite);
                binding.Node.UserAccessLevel = remote ? binding.SupportedUserAccessLevel :
                    (byte)(binding.SupportedUserAccessLevel & ~AccessLevels.CurrentWrite);
            }
            foreach (MethodState method in m_methods)
            {
                method.Executable = remote;
            }
            Volatile.Write(ref m_current, next);
            foreach (MonitoredParameterBinding binding in m_parameters)
            {
                binding.RefreshWriteAccess(remote);
            }
            if (!remote)
            {
                lock (m_controlLock)
                {
                    m_controlOwner = NodeId.Null;
                }
            }
            await PublishConditionsAsync(previous, next, commit.Timestamp).ConfigureAwait(false);
            State.ClearChangeMasks(Context, true);
        }

        /// <summary>
        /// Checks exact wire type, enum domain, and specification-defined numeric bounds.
        /// </summary>
        private void ValidateValue(ValueBinding binding, Variant value, StatusCode quality)
        {
            if (value.IsNull && StatusCode.IsBad(quality))
            {
                return;
            }
            if (TypeInfo.IsInstanceOfDataType(value, binding.Node.DataType, binding.Node.ValueRank,
                Context.NamespaceUris, Context.TypeTable).IsUnknown)
            {
                throw Error(StatusCodes.BadTypeMismatch,
                    "The value does not match the declared DataType and ValueRank.");
            }
            if (value.TryGetValue(out double number) && StatusCode.IsGood(quality))
            {
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    throw Error(StatusCodes.BadOutOfRange, "A good-quality numeric value must be finite.");
                }
                string name = binding.Node.BrowseName.Name ?? string.Empty;
                bool nonnegative = name is "MaterialDryingTime" or "RemainingMaterialDryingTime" or
                    "ExpiredDryingTime" or "IntervalTimeOn" or "IntervalTimeOff" or "TemperatureReduction" or
                    "EnergyConsumptionResettable" or "TotalAirConsumption" or "PowerConsumption";
                if (nonnegative && number < 0)
                {
                    throw Error(StatusCodes.BadOutOfRange,
                        "This duration, reduction, or cumulative value is nonnegative.");
                }
                if (name == "EnergyConsumptionLimit" && (number < 0 || number > 200))
                {
                    throw Error(StatusCodes.BadOutOfRange,
                        "EnergyConsumptionLimit must be in the range 0–200 percent.");
                }
                if ((binding.SupportedAccessLevel & AccessLevels.CurrentWrite) != 0 &&
                    binding.Node.FindChild(Context, new QualifiedName(Opc.Ua.BrowseNames.EURange)) is
                        PropertyState<Range> { Value: { } range } &&
                    (number < range.Low || number > range.High))
                {
                    throw Error(StatusCodes.BadOutOfRange,
                        "The requested value exceeds the equipment engineering range.");
                }
            }
            NodeId type = binding.Node.DataType;
            int maximum =
                type == ExpandedNodeId.ToNodeId(DataTypeIds.FunctionStatusEnumeration, Context.NamespaceUris) ? 3 :
                type == ExpandedNodeId.ToNodeId(DataTypeIds.AirFlowModeEnumeration, Context.NamespaceUris) ||
                type == ExpandedNodeId.ToNodeId(DataTypeIds.DryingModeEnumeration, Context.NamespaceUris) ||
                type == ExpandedNodeId.ToNodeId(DataTypeIds.DryingLevelEnumeration, Context.NamespaceUris) ||
                type == ExpandedNodeId.ToNodeId(PlasticsRubber.DataTypeIds.MaintenanceStatusEnumeration,
                    Context.NamespaceUris) ? 2 :
                type == ExpandedNodeId.ToNodeId(DataTypeIds.AirSourceEnumeration, Context.NamespaceUris) ||
                type == ExpandedNodeId.ToNodeId(DataTypeIds.MainFailureBehaviourEnumeration, Context.NamespaceUris)
                    ? 1 : -1;
            if (maximum >= 0 && (!TryEnumeration(value, out int enumeration) || enumeration < 0
                || enumeration > maximum))
            {
                throw Error(StatusCodes.BadOutOfRange, "The enumeration value is not defined by the companion model.");
            }
            if (binding.Node.BrowseName.Name == "AirFlowMode" &&
                TryEnumeration(value, out int selected) && selected == 1 &&
                (!m_values.ContainsKey((binding.Target, "IntervalTimeOn")) ||
                    !m_values.ContainsKey((binding.Target, "IntervalTimeOff"))))
            {
                throw Error(StatusCodes.BadNotSupported, "INTERVAL airflow needs both interval-time members.");
            }
            if (binding.Node.BrowseName.Name == "DeviceClass" &&
                (!value.TryGetValue(out string deviceClass) || deviceClass != "Granulate Drying Device"))
            {
                throw Error(StatusCodes.BadOutOfRange, "The standard device class cannot be replaced.");
            }
        }

        /// <summary>
        /// Verifies interdependent values using the staged commit rather than stale node caches.
        /// </summary>
        private void ValidateRelationships(Dictionary<NodeId, DataValue> values)
        {
            ValidateParameterValues(values);
            for (int ii = 1; ii <= Options.Bins.Count; ii++)
            {
                GranulateDryingTarget target = GranulateDryingTarget.Bin(ii);
                if (TryValue(values, target, "AirFlowMode", out DataValue mode) &&
                    TryEnumeration(mode.WrappedValue, out int selected) && selected == 1 &&
                    (!m_values.ContainsKey((target, "IntervalTimeOn")) ||
                        !m_values.ContainsKey((target, "IntervalTimeOff"))))
                {
                    throw Error(StatusCodes.BadNotSupported, "INTERVAL airflow needs both interval-time members.");
                }
            }
        }

        /// <summary>
        /// Distinguishes protocol-owned projections from ordinary provider-updatable variables.
        /// </summary>
        private static bool IsReserved(ValueBinding binding)
        {
            return binding.Target.Kind == GranulateDryingTargetKind.Device &&
                (binding.Member is "Operation/RemoteControlOpcUa" or "Operation/ActiveErrors" or
                    "Operation/HighestActiveAlarmSeverity" ||
                binding.Member.StartsWith("UserLogin/", StringComparison.Ordinal) ||
                binding.Member.StartsWith("MachineryBuildingBlocks/MachineryItemState/", StringComparison.Ordinal) ||
                binding.Member.StartsWith("MachineryBuildingBlocks/MachineryOperationMode/", StringComparison.Ordinal));
        }

        /// <summary>
        /// Validates bounded error identities and sources before creating conditions.
        /// </summary>
        private ArrayOf<GranulateDryingError> ValidateErrors(ArrayOf<GranulateDryingError> errors)
        {
            if (errors.Count > Options.MaximumErrors)
            {
                throw Error(StatusCodes.BadTooManyOperations, "The active-error limit was exceeded.");
            }
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (GranulateDryingError error in errors)
            {
                if (error == null || string.IsNullOrWhiteSpace(error.Id) || !identifiers.Add(error.Id) ||
                    error.Severity is < 1 or > 1000 || error.Sources.IsNull || error.Sources.Count == 0)
                {
                    throw Error(StatusCodes.BadInvalidArgument,
                        "Errors need unique IDs, severity 1–1000, and at least one configured source.");
                }
                foreach (GranulateDryingTarget source in error.Sources)
                {
                    _ = Target(source);
                }
            }
            return errors;
        }

        /// <summary>
        /// Builds the standard classified-error array and its exact maximum severity.
        /// </summary>
        private void StageErrors(
            Dictionary<NodeId, DataValue> values,
            ArrayOf<GranulateDryingError> errors,
            DateTimeUtc timestamp)
        {
            var active = new List<ClassifiedActiveErrorDataType>(errors.Count);
            ushort severity = 0;
            foreach (GranulateDryingError error in errors)
            {
                active.Add(new ClassifiedActiveErrorDataType
                {
                    Id = error.Id,
                    Severity = error.Severity,
                    Message = error.Message,
                    Classification = error.Classification,
                    SourceNodes = error.Sources.ToArrayOf(source => Target(source).NodeId)
                });
                severity = Math.Max(severity, error.Severity);
            }
            Stage(values, GranulateDryingTarget.Device, "Operation/ActiveErrors",
                Variant.FromStructure(active.ToArrayOf()), timestamp);
            Stage(values, GranulateDryingTarget.Device, "Operation/HighestActiveAlarmSeverity",
                Variant.From(severity), timestamp);
        }

        /// <summary>
        /// Projects accepted HMI identity independently of UA authentication.
        /// </summary>
        private void StageUser(Dictionary<NodeId, DataValue> values, GranulateDryingUser user, DateTimeUtc timestamp)
        {
            Stage(values, GranulateDryingTarget.Device, "UserLogin/ActualUser/IsPresent",
                Variant.From(user.IsPresent), timestamp);
            Stage(values, GranulateDryingTarget.Device, "UserLogin/ActualUser/Id", Variant.From(user.Id), timestamp);
            Stage(values, GranulateDryingTarget.Device, "UserLogin/ActualUser/Name", Variant.From(user.Name),
                timestamp);
            Stage(values, GranulateDryingTarget.Device, "UserLogin/ActualUser/CardUid",
                Variant.From(user.CardUid), timestamp);
            Stage(values, GranulateDryingTarget.Device, "UserLogin/ActualUser/UserLevel",
                Variant.From(user.UserLevel), timestamp);
            Stage(values, GranulateDryingTarget.Device, "UserLogin/ActualUser/UserRole",
                Variant.From(user.UserRole), timestamp);
            Stage(values, GranulateDryingTarget.Device, "UserLogin/ActualUser/Language",
                Variant.From(user.Language), timestamp);
        }

        /// <summary>
        /// Projects a committed Machinery state using the generated standard identifiers.
        /// </summary>
        private void StageMachineState(
            Dictionary<NodeId, DataValue> values,
            MachineryItemStateValue state,
            DateTimeUtc timestamp)
        {
            (uint id, uint number, string name) = state switch
            {
                MachineryItemStateValue.NotAvailable =>
                    (ItemIds.StateIds.NotAvailable, ItemIds.StateNumbers.NotAvailable, "NotAvailable"),
                MachineryItemStateValue.OutOfService =>
                    (ItemIds.StateIds.OutOfService, ItemIds.StateNumbers.OutOfService, "OutOfService"),
                MachineryItemStateValue.NotExecuting =>
                    (ItemIds.StateIds.NotExecuting, ItemIds.StateNumbers.NotExecuting, "NotExecuting"),
                MachineryItemStateValue.Executing =>
                    (ItemIds.StateIds.Executing, ItemIds.StateNumbers.Executing, "Executing"),
                _ => throw Error(StatusCodes.BadOutOfRange, "Unknown Machinery item state.")
            };
            StageState(values, "MachineryBuildingBlocks/MachineryItemState", id, number, name, timestamp);
        }

        /// <summary>
        /// Projects a committed optional Machinery operation mode.
        /// </summary>
        private void StageOperationMode(
            Dictionary<NodeId, DataValue> values,
            MachineryOperationModeValue mode,
            DateTimeUtc timestamp)
        {
            (uint id, uint number, string name) = mode switch
            {
                MachineryOperationModeValue.None =>
                    (ModeIds.StateIds.None, ModeIds.StateNumbers.None, "None"),
                MachineryOperationModeValue.Maintenance =>
                    (ModeIds.StateIds.Maintenance, ModeIds.StateNumbers.Maintenance, "Maintenance"),
                MachineryOperationModeValue.Setup =>
                    (ModeIds.StateIds.Setup, ModeIds.StateNumbers.Setup, "Setup"),
                MachineryOperationModeValue.Processing =>
                    (ModeIds.StateIds.Processing, ModeIds.StateNumbers.Processing, "Processing"),
                _ => throw Error(StatusCodes.BadOutOfRange, "Unknown Machinery operation mode.")
            };
            StageState(values, "MachineryBuildingBlocks/MachineryOperationMode", id, number, name, timestamp);
        }

        /// <summary>
        /// Updates the state display, identifier, and selected optional number together.
        /// </summary>
        private void StageState(
            Dictionary<NodeId, DataValue> values,
            string path,
            uint id,
            uint number,
            string name,
            DateTimeUtc timestamp)
        {
            ushort ns = Context.NamespaceUris.GetIndexOrAppend(Machinery.Namespaces.Machinery);
            Stage(values, GranulateDryingTarget.Device, path + "/CurrentState",
                Variant.From(new LocalizedText(name)), timestamp);
            Stage(values, GranulateDryingTarget.Device, path + "/CurrentState/Id",
                Variant.From(new NodeId(id, ns)), timestamp);
            if (m_values.ContainsKey((GranulateDryingTarget.Device, path + "/CurrentState/Number")))
            {
                Stage(values, GranulateDryingTarget.Device, path + "/CurrentState/Number",
                    Variant.From(number), timestamp);
            }
        }

        /// <summary>
        /// Stages a protocol-owned value with the commit's source time.
        /// </summary>
        private void Stage(
            Dictionary<NodeId, DataValue> values,
            GranulateDryingTarget target,
            string member,
            Variant value,
            DateTimeUtc timestamp)
        {
            ValueBinding binding = RequiredValue(target, member);
            ValidateValue(binding, value, StatusCodes.Good);
            values[binding.Node.NodeId] = new DataValue(value, StatusCodes.Good, timestamp);
        }

        /// <summary>
        /// Looks up a staged optional value without inventing its default.
        /// </summary>
        private bool TryValue(
            Dictionary<NodeId, DataValue> values,
            GranulateDryingTarget target,
            string member,
            out DataValue value)
        {
            if (m_values.TryGetValue((target, member), out ValueBinding? binding))
            {
                value = values[binding.Node.NodeId];
                return StatusCode.IsGood(value.StatusCode);
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Reads the numeric enumeration without boxing or runtime reflection.
        /// </summary>
        private static bool TryEnumeration(Variant value, out int number)
        {
            if (value.TryGetValue(out number))
            {
                return true;
            }
            if (value.TryGetValue(out EnumValue enumeration))
            {
                number = enumeration.Value;
                return true;
            }
            number = default;
            return false;
        }

        /// <summary>
        /// Checks the full repeated publication, including original source timestamps.
        /// </summary>
        private static bool SameValues(CommitState left, CommitState right)
        {
            if (left.RemoteControlEnabled != right.RemoteControlEnabled ||
                left.Errors.Count != right.Errors.Count || left.Values.Count != right.Values.Count)
            {
                return false;
            }
            for (int index = 0; index < left.Errors.Count; index++)
            {
                GranulateDryingError first = left.Errors[index];
                GranulateDryingError second = right.Errors[index];
                if (first.Id != second.Id || first.Severity != second.Severity ||
                    first.Classification != second.Classification || first.Message != second.Message ||
                    first.HelpText != second.HelpText || first.Sources != second.Sources)
                {
                    return false;
                }
            }
            return left.Values.All(pair => right.Values.TryGetValue(pair.Key, out DataValue value) &&
                pair.Value.Equals(value));
        }
    }
}
