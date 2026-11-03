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

namespace Opc.Ua.IA.Server
{
    /// <summary>
    /// Binds authoritative OPC 10000-200 calibration-target information to a generated node graph.
    /// </summary>
    /// <remarks>
    /// Construct before node-manager registration using an initialized CalibrationTargetState with
    /// an empty features folder. The caller owns registration, scheduling and provider lifetime.
    /// All exposed variables are read-only; external changes are committed by the provider and
    /// published by RefreshAsync. No physical calibration, certificate retrieval or UA write
    /// persistence is performed. Configuration changes require a new binding and node graph.
    /// </remarks>
    public sealed partial class IaCalibrationTarget
    {
        /// <summary>
        /// Configures one generated target without taking ownership of its node manager or provider.
        /// </summary>
        /// <param name="context">The owning node manager's system context and NodeId factory.</param>
        /// <param name="state">The initialized, not-yet-registered calibration-target state.</param>
        /// <param name="provider">The authoritative information provider.</param>
        public IaCalibrationTarget(
            ISystemContext context,
            CalibrationTargetState state,
            IIaCalibrationTargetProvider provider)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            State = state ?? throw new ArgumentNullException(nameof(state));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            IaCalibrationTargetConfiguration source = provider.Configuration ??
                throw new ArgumentException("The provider must supply a configuration.", nameof(provider));
            IaCalibrationValidation.ValidateConfiguration(source);
            Configuration = source with
            {
                Features = source.Features.Span.ToArray(),
                OperationalConditions = source.OperationalConditions.Span.ToArray(),
                ReferenceMeasurementInstruments = source.ReferenceMeasurementInstruments.Span.ToArray()
            };
            if (state.Identification == null || state.CalibrationTargetFeatures == null ||
                state.CalibrationTargetCategory == null)
            {
                throw new ArgumentException(
                    "Create the target using the generated CreateInstanceOfCalibrationTargetType factory.",
                    nameof(state));
            }
            if (context.NodeIdFactory == null)
            {
                throw new ArgumentException(
                    "The owning context must supply a NodeId factory for calibration-target children.",
                    nameof(context));
            }
            RequireEmpty(state.Identification);
            RequireEmpty(state.CalibrationTargetFeatures);
            if (state.OperationalConditions != null)
            {
                RequireEmpty(state.OperationalConditions);
            }
            ConfigureCategory();
            ConfigureIdentification();
            ConfigureOptionalFields();
            ConfigureFeatures(state.CalibrationTargetFeatures, Configuration.Features, m_features);
            if (Configuration.OperationalConditions.Count > 0 ||
                Configuration.Category is IaCalibrationTargetCategory.Reusable or
                    IaCalibrationTargetCategory.ReusableDevice)
            {
                state.AddOperationalConditions(context);
                ConfigureFeatures(state.OperationalConditions!, Configuration.OperationalConditions, m_conditions);
            }
            else
            {
                state.OperationalConditions = null;
            }
            RestrictWrites(state);
        }

        /// <summary>
        /// Gets the caller-owned generated target state.
        /// </summary>
        public CalibrationTargetState State { get; }

        /// <summary>
        /// Gets the validated, fixed topology.
        /// </summary>
        public IaCalibrationTargetConfiguration Configuration { get; }

        /// <summary>
        /// Reads and validates a complete provider image before publishing its absolute values.
        /// </summary>
        /// <remarks>
        /// Concurrent refresh attempts are rejected rather than reordering provider snapshots.
        /// A failed or cancelled read leaves all previously published values intact.
        /// The provider's status and source timestamp are retained, not replaced with refresh time.
        /// UA server timestamps continue to be assigned by the hosting server's read pipeline.
        /// </remarks>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref m_refreshing, 1, 0) != 0)
            {
                throw new InvalidOperationException("A calibration-target refresh is already in progress.");
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                IaCalibrationTargetSnapshot source =
                    await m_provider.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                if (source == null)
                {
                    throw new InvalidOperationException("The calibration provider returned a null snapshot.");
                }
                IaCalibrationTargetSnapshot snapshot = source with
                {
                    Features = CopyReadings(source.Features),
                    OperationalConditions = CopyReadings(source.OperationalConditions)
                };
                IaCalibrationValidation.ValidateSnapshot(Configuration, snapshot);
                cancellationToken.ThrowIfCancellationRequested();

                PublishReadings(m_features, snapshot.Features);
                PublishReadings(m_conditions, snapshot.OperationalConditions);
                if (snapshot.LastValidationDate != null)
                {
                    Publish(State.LastValidationDate!, new DataValue(
                        Variant.From(snapshot.LastValidationDate.Value),
                        snapshot.LastValidationDate.StatusCode,
                        snapshot.LastValidationDate.SourceTimestamp));
                }
                if (snapshot.NextValidationDate != null)
                {
                    Publish(State.NextValidationDate!, new DataValue(
                        Variant.From(snapshot.NextValidationDate.Value),
                        snapshot.NextValidationDate.StatusCode,
                        snapshot.NextValidationDate.SourceTimestamp));
                }
                if (snapshot.Quality != null)
                {
                    Publish(State.Quality!, new DataValue(
                        Variant.From(snapshot.Quality.Value),
                        snapshot.Quality.StatusCode,
                        snapshot.Quality.SourceTimestamp));
                }
                if (snapshot.CertificateUri != null)
                {
                    Publish(State.CertificateUri!, new DataValue(
                        Variant.From(snapshot.CertificateUri.Value),
                        snapshot.CertificateUri.StatusCode,
                        snapshot.CertificateUri.SourceTimestamp));
                }
                State.ClearChangeMasks(m_context, true);
            }
            finally
            {
                Volatile.Write(ref m_refreshing, 0);
            }
        }

        /// <summary>
        /// Creates the concrete category while retaining the generated component's instance identity.
        /// </summary>
        private void ConfigureCategory()
        {
            BaseCalibrationTargetCategoryState previous = State.CalibrationTargetCategory ??
                throw new InvalidOperationException("The generated category was removed.");
            QualifiedName name = previous.BrowseName;
            (BaseCalibrationTargetCategoryState Category, uint TypeId) selected = Configuration.Category switch
            {
                IaCalibrationTargetCategory.Dynamic =>
                    (new DynamicCalibrationTargetCategoryState(State),
                        ObjectTypes.DynamicCalibrationTargetCategoryType),
                IaCalibrationTargetCategory.OneTime =>
                    (new OneTimeCalibrationTargetCategoryState(State),
                        ObjectTypes.OneTimeCalibrationTargetCategoryType),
                IaCalibrationTargetCategory.Reusable =>
                    (new ReusableCalibrationTargetCategoryState(State),
                        ObjectTypes.ReusableCalibrationTargetCategoryType),
                IaCalibrationTargetCategory.ReusableDevice =>
                    (new ReusableDeviceCalibrationTargetCategoryState(State),
                        ObjectTypes.ReusableDeviceCalibrationTargetCategoryType),
                _ => throw new ArgumentOutOfRangeException(nameof(Configuration))
            };
            BaseCalibrationTargetCategoryState category = selected.Category;
            uint typeId = selected.TypeId;
            category.NodeId = previous.NodeId;
            category.BrowseName = name;
            category.SymbolicName = previous.SymbolicName;
            category.DisplayName = previous.DisplayName;
            category.TypeDefinitionId = NodeId.Create(typeId, Namespaces.IA, m_context.NamespaceUris);
            category.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            State.CalibrationTargetCategory = category;
            foreach (NodeId instrument in Configuration.ReferenceMeasurementInstruments)
            {
                category.AddReference(
                    NodeId.Create(
                        ReferenceTypes.HasReferenceMeasurementInstrument,
                        Namespaces.IA,
                        m_context.NamespaceUris),
                    false,
                    instrument);
            }
        }

        /// <summary>
        /// Selects optional target properties and marks their initial values as unavailable.
        /// </summary>
        private void ConfigureOptionalFields()
        {
            IaCalibrationTargetFields fields = IaCalibrationValidation.EffectiveFields(Configuration);
            if ((fields & IaCalibrationTargetFields.LastValidationDate) != 0)
            {
                State.AddLastValidationDate(m_context);
                Waiting(State.LastValidationDate!);
            }
            else
            {
                State.LastValidationDate = null;
            }
            if ((fields & IaCalibrationTargetFields.NextValidationDate) != 0)
            {
                State.AddNextValidationDate(m_context);
                Waiting(State.NextValidationDate!);
            }
            else
            {
                State.NextValidationDate = null;
            }
            if ((fields & IaCalibrationTargetFields.Quality) != 0)
            {
                State.AddQuality(m_context);
                Waiting(State.Quality!);
            }
            else
            {
                State.Quality = null;
            }
            if ((fields & IaCalibrationTargetFields.CertificateUri) != 0)
            {
                State.AddCertificateUri(m_context);
                Waiting(State.CertificateUri!);
            }
            else
            {
                State.CertificateUri = null;
            }
        }

        /// <summary>
        /// Creates named typed variables with engineering units, precision and range resolution.
        /// </summary>
        private void ConfigureFeatures(
            NodeState folder,
            ArrayOf<IaCalibrationFeature> features,
            Dictionary<QualifiedName, BaseVariableState> variables)
        {
            foreach (IaCalibrationFeature feature in features)
            {
                BaseVariableState variable;
                var unit = new EUInformation
                {
                    NamespaceUri = feature.EngineeringUnits.NamespaceUri,
                    UnitId = feature.EngineeringUnits.UnitId,
                    DisplayName = feature.EngineeringUnits.DisplayName,
                    Description = feature.EngineeringUnits.Description
                };
                if (feature.Kind == IaCalibrationFeatureKind.CalibrationValue)
                {
                    CalibrationValueState value =
                        m_context.CreateInstanceOfCalibrationValueType(folder, feature.BrowseName);
                    value.DataType = new NodeId((uint)feature.NumericType);
                    value.ValueRank = feature.ValueRank;
                    value.EngineeringUnits!.Value = unit;
                    if (feature.ValuePrecision is double precision)
                    {
                        value.AddValuePrecision(m_context);
                        value.ValuePrecision!.Value = precision;
                    }
                    variable = value;
                }
                else
                {
                    CapacityRangeState range =
                        m_context.CreateInstanceOfCapacityRangeType(folder, feature.BrowseName);
                    range.EngineeringUnits!.Value = unit;
                    range.Resolution!.Value = feature.Resolution!.Value;
                    if (feature.ValuePrecision is double precision)
                    {
                        range.AddValuePrecision(m_context);
                        range.ValuePrecision!.Value = precision;
                    }
                    variable = range;
                }
                variable.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                Waiting(variable);
                folder.AddChild(variable);
                variables.Add(feature.BrowseName, variable);
            }
        }

        /// <summary>
        /// Rejects reusing a target whose feature topology has already been populated.
        /// </summary>
        private void RequireEmpty(NodeState folder)
        {
            var children = new List<BaseInstanceState>();
            folder.GetChildren(m_context, children);
            if (children.Count != 0)
            {
                throw new ArgumentException("Bind an initialized target with empty feature and condition folders.");
            }
        }

        /// <summary>
        /// Prevents every UA value or attribute write without pretending to persist it.
        /// </summary>
        private void RestrictWrites(NodeState node)
        {
            node.WriteMask = 0;
            node.UserWriteMask = 0;
            if (node is BaseVariableState variable)
            {
                variable.AccessLevel = AccessLevels.CurrentRead;
                variable.UserAccessLevel = AccessLevels.CurrentRead;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_context, children);
            foreach (BaseInstanceState child in children)
            {
                RestrictWrites(child);
            }
        }

        /// <summary>
        /// Copies mutable structured and array payloads out of provider-owned buffers.
        /// </summary>
        private static ArrayOf<IaCalibrationReading> CopyReadings(ArrayOf<IaCalibrationReading> readings)
        {
            var copy = new IaCalibrationReading[readings.Count];
            for (int ii = 0; ii < readings.Count; ii++)
            {
                IaCalibrationReading reading = readings[ii];
                copy[ii] = reading with
                {
                    Value = reading.Value.WithWrappedValue(IaValueCopy.Own(reading.Value.WrappedValue))
                };
            }
            return copy;
        }

        /// <summary>
        /// Publishes an already validated complete set of feature readings.
        /// </summary>
        private static void PublishReadings(
            Dictionary<QualifiedName, BaseVariableState> variables,
            ArrayOf<IaCalibrationReading> readings)
        {
            foreach (IaCalibrationReading reading in readings)
            {
                Publish(variables[reading.BrowseName], reading.Value);
            }
        }

        /// <summary>
        /// Preserves the provider's absolute value, status and source timestamp.
        /// </summary>
        private static void Publish(BaseVariableState variable, in DataValue value)
        {
            variable.WrappedValue = value.WrappedValue;
            variable.StatusCode = value.StatusCode;
            variable.Timestamp = value.SourceTimestamp;
        }

        /// <summary>
        /// Avoids presenting generated default values as real calibration information.
        /// </summary>
        private static void Waiting(BaseVariableState variable)
        {
            variable.StatusCode = StatusCodes.BadWaitingForInitialData;
            variable.Timestamp = DateTimeUtc.MinValue;
        }

        /// <summary>The owning node manager's context.</summary>
        private readonly ISystemContext m_context;

        /// <summary>The caller-owned authoritative provider.</summary>
        private readonly IIaCalibrationTargetProvider m_provider;

        /// <summary>The calibrated feature variables.</summary>
        private readonly Dictionary<QualifiedName, BaseVariableState> m_features = [];

        /// <summary>The optional operational-condition variables.</summary>
        private readonly Dictionary<QualifiedName, BaseVariableState> m_conditions = [];

        /// <summary>Excludes overlapping asynchronous refresh attempts.</summary>
        private int m_refreshing;
    }
}
