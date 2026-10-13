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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Adapts immutable native observations into the shared MachineTool transaction and generated GMS descendants.
    /// </summary>
    internal sealed partial class GmsSystemBinding : IMachineToolProvider, IAsyncDisposable
    {
        /// <summary>
        /// Creates one instance's native binding; no provider is looked up globally.
        /// </summary>
        internal GmsSystemBinding(IMachineryBuildContext context, IGmsProvider provider, GmsSystemOptions options)
        {
            m_context = context;
            m_provider = provider;
            m_options = options;
        }

        /// <summary>
        /// Gets the last coherently committed native observation.
        /// </summary>
        internal GmsSystemSnapshot? Observation { get; private set; }

        /// <inheritdoc/>
        public async ValueTask<MachineToolSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            GmsSystemSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false) ??
                throw Invalid("The GMS provider returned no observation.");
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(snapshot, m_input) && m_inherited != null)
            {
                return m_inherited;
            }
            m_options.Validate();
            ValidateInput(snapshot);
            var sensors = new Dictionary<string, GmsSensor>(StringComparer.Ordinal);
            ArrayOf<MachineToolTool> tools = ConvertSensors(snapshot.Sensors, sensors, false);
            MachineToolSnapshot inherited = snapshot.MachineTool with
            {
                Equipment = Has(GmsFeatures.Sensors) ? new MachineToolEquipmentObservation { Tools = tools } : null,
                Prognoses = ConvertPrognoses(snapshot)
            };
            m_input = snapshot;
            m_inputSensors = sensors;
            m_inherited = inherited;
            return inherited;
        }

        /// <summary>
        /// Creates the native root only after the shared builder has validated its initial observation.
        /// </summary>
        internal GMSState CreateRoot(ISystemContext context, NodeState parent, QualifiedName name)
        {
            m_state = context.CreateInstanceOfGMSType(parent, name);
            CreateSystemStructure(m_state, m_input!);
            CreateEquipmentStructure(m_state, m_input!);
            return m_state;
        }

        /// <summary>
        /// Uses the containing Machinery registration for native binding ownership.
        /// </summary>
        internal void Configure(IMachineBuilder<GMSState> builder)
        {
            ConfigureAccessoryIdentification(builder);
            ConfigureWarnings(builder);
            builder.WithRegistrationBinding(async (_, _, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await PublishWarningsAsync(cancellationToken).ConfigureAwait(false);
                return this;
            });
        }

        /// <summary>
        /// Configures native tool factories without replacing the generic inventory, lifetime or life-value protocol.
        /// </summary>
        internal MachineToolEquipmentOptions EquipmentOptions()
        {
            MachineToolFeatures features = Has(GmsFeatures.Sensors)
                ? MachineToolFeatures.Tools | MachineToolFeatures.ToolLife : MachineToolFeatures.None;
            if (Has(GmsFeatures.DynamicSensors))
            {
                features |= MachineToolFeatures.DynamicTools;
            }
            return new MachineToolEquipmentOptions
            {
                Features = features,
                MaximumTools = m_options.MaximumSensors,
                MaximumTextLength = m_options.MaximumTextLength,
                CreateToolAsync = (context, parent, name, value, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    GmsSensor sample = m_inputSensors[value.Key];
                    SensorState state = context.CreateInstanceOfSensorType(parent, name);
                    state.AddQualified(context);
                    SelectFields(state, SensorFields(sample));
                    SensorLease resource = TrackSensor(value.Key, state);
                    return new ValueTask<MachineToolEquipmentNode<ToolState>>(
                        new MachineToolEquipmentNode<ToolState>(state, resource));
                },
                CreateMultiToolAsync = (context, parent, name, value, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    GmsSensor sample = m_inputSensors[value.Key];
                    MultiSensorState state = context.CreateInstanceOfMultiSensorType(parent, name);
                    SelectFields(state, SensorFields(sample));
                    SensorLease resource = TrackSensor(value.Key, state);
                    return new ValueTask<MachineToolEquipmentNode<MultiToolState>>(
                        new MachineToolEquipmentNode<MultiToolState>(state, resource));
                }
            };
        }

        /// <summary>
        /// Prepares all derived writes before the shared transaction changes any existing observation.
        /// </summary>
        internal Action Prepare(ISystemContext context, GMSState state, MachineToolSnapshot snapshot)
        {
            if (!ReferenceEquals(state, m_state) || !ReferenceEquals(snapshot, m_inherited))
            {
                throw Invalid("The native projection must belong to the current GMS acquisition.");
            }
            var writes = new List<Action>();
            var committedSensors = new Dictionary<string, BaseToolState>(StringComparer.Ordinal);
            foreach ((string key, GmsSensor sample) in m_inputSensors)
            {
                if (!m_sensors.TryGetValue(key, out BaseToolState? sensor))
                {
                    throw Invalid("A native sensor factory did not supply its actual generated node.");
                }
                PlanFields(sensor, SensorFields(sample), writes);
                committedSensors.Add(key, sensor);
            }
            GmsSystemSnapshot accepted = m_input!;
            ArrayOf<GmsCalibrationObservation> history = PlanSystems(state, accepted, committedSensors, writes);
            PlanEquipment(accepted, writes);
            PlanProduction(accepted, writes);
            List<PendingWarning> warnings = PrepareWarnings(accepted, committedSensors, writes);
            return () =>
            {
                foreach (Action write in writes)
                {
                    write();
                }
                m_committedSensors = committedSensors;
                m_calibrationHistory = history;
                Observation = accepted;
                m_pendingWarnings = warnings;
                m_pendingWarningIndex = 0;
            };
        }

        /// <summary>
        /// Resolves only committed tools of this instance, never staged or removed factory nodes.
        /// </summary>
        internal bool TryGetSensor(string key, [NotNullWhen(true)] out BaseToolState? sensor)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            return m_committedSensors.TryGetValue(key, out sensor);
        }

        /// <summary>
        /// Transfers the factory index entry to the existing inventory's rollback and removal resource lifecycle.
        /// </summary>
        private SensorLease TrackSensor(string key, BaseToolState state)
        {
            m_sensors.Add(key, state);
            return new SensorLease(this, key, state);
        }

        /// <summary>
        /// Selects only supplied optional native variables on a new unpublished generated node.
        /// </summary>
        private void SelectFields(NodeState parent, ArrayOf<(string Name, DataValue Value)> fields)
        {
            foreach ((string name, DataValue value) in fields)
            {
                if (value.IsNull)
                {
                    continue;
                }
                QualifiedName browseName = GmsName(name);
                if (parent.FindChild(m_context.Context, browseName) == null)
                {
                    _ = AddNativeField(parent, name);
                }
                BaseVariableState node = parent.FindChild(m_context.Context, browseName) as BaseVariableState ??
                    throw Invalid($"The native generated declaration does not contain '{name}'.");
                node.AccessLevel = AccessLevels.CurrentRead;
                node.UserAccessLevel = AccessLevels.CurrentRead;
            }
        }

        /// <summary>
        /// Captures no-throw value assignments; an optional field cannot silently appear or disappear during a read.
        /// </summary>
        private void PlanFields(
            NodeState parent, ArrayOf<(string Name, DataValue Value)> fields, List<Action> writes)
        {
            foreach ((string name, DataValue value) in fields)
            {
                BaseVariableState? node = parent.FindChild(m_context.Context, GmsName(name)) as BaseVariableState;
                if (node == null && value.IsNull)
                {
                    continue;
                }
                if (node == null || value.IsNull)
                {
                    throw Invalid($"The configured presence of '{name}' cannot change within an equipment slot.");
                }
                writes.Add(() => Set(node, value));
            }
        }

        /// <summary>
        /// Applies a raw sample without boxing the Variant or manufacturing successful values.
        /// </summary>
        private static void Set(BaseVariableState node, in DataValue sample)
        {
            node.Value = sample.WrappedValue;
            node.StatusCode = sample.StatusCode;
            node.Timestamp = sample.SourceTimestamp;
        }

        /// <summary>
        /// Enumerates only fields declared by the selected native Sensor or MultiSensor type.
        /// </summary>
        private static ArrayOf<(string Name, DataValue Value)> SensorFields(GmsSensor sensor)
        {
            if (sensor.Tool.Kind == MachineToolToolKind.MultiTool)
            {
                return [(BrowseNames.Alignment, sensor.Alignment), (BrowseNames.Axes, sensor.Axes)];
            }
            return
            [
                (BrowseNames.Class, sensor.Class), (BrowseNames.Capabilities, sensor.Capabilities),
                (BrowseNames.Alignment, sensor.Alignment), (BrowseNames.Axes, sensor.Axes),
                (BrowseNames.IsQualifiedStatus, sensor.IsQualifiedStatus),
                (BrowseNames.AbsoluteProbe, sensor.AbsoluteProbe),
                (BrowseNames.EngineeringUnit, sensor.EngineeringUnit),
                (BrowseNames.MeasuringRange, sensor.MeasuringRange),
                (BrowseNames.WorkingRange, sensor.WorkingRange), (BrowseNames.Resolution, sensor.Resolution)
            ];
        }

        /// <summary>
        /// Resolves the actual model namespace, independent of its server-assigned numeric position.
        /// </summary>
        private QualifiedName GmsName(string name)
        {
            int index = m_context.Context.NamespaceUris.GetIndex(GmsModel.NamespaceUri);
            if (index < 0)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "The GMS model is not loaded.");
            }
            return new QualifiedName(name, (ushort)index);
        }

        /// <summary>
        /// Tests explicit selection of a native capability.
        /// </summary>
        private bool Has(GmsFeatures feature) => (m_options.Features & feature) != 0;

        /// <summary>
        /// Reports a rejected coherent provider observation.
        /// </summary>
        private static ServiceResultException Invalid(string message) => new(StatusCodes.BadInvalidArgument, message);

        /// <summary>The containing namespace owner.</summary>
        private readonly IMachineryBuildContext m_context;

        /// <summary>The application-owned immutable observation provider.</summary>
        private readonly IGmsProvider m_provider;

        /// <summary>The explicit capability selection and finite bounds.</summary>
        private readonly GmsSystemOptions m_options;

        /// <summary>The actual generated root.</summary>
        private GMSState? m_state;

        /// <summary>The currently prepared native observation.</summary>
        private GmsSystemSnapshot? m_input;

        /// <summary>The unique inherited snapshot corresponding to the current preparation.</summary>
        private MachineToolSnapshot? m_inherited;

        /// <summary>The next native sensor descriptions, indexed within one serialized facade update.</summary>
        private Dictionary<string, GmsSensor> m_inputSensors = new(StringComparer.Ordinal);

        /// <summary>Native sensor nodes with rollback ownership held by the shared equipment module.</summary>
        private readonly Dictionary<string, BaseToolState> m_sensors = new(StringComparer.Ordinal);

        /// <summary>An immutable index of committed sensors, replaced only at the coherent commit.</summary>
        private Dictionary<string, BaseToolState> m_committedSensors = new(StringComparer.Ordinal);

        /// <summary>
        /// Releases only the index entry belonging to this shared-inventory-owned native node.
        /// </summary>
        private sealed class SensorLease(GmsSystemBinding owner, string key, BaseToolState state) : IAsyncDisposable
        {
            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                if (owner.m_sensors.TryGetValue(key, out BaseToolState? current) && ReferenceEquals(current, state))
                {
                    owner.m_sensors.Remove(key);
                }
                return default;
            }
        }
    }
}
