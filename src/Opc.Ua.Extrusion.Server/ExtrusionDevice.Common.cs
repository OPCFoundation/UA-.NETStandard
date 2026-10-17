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
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.Extrusion.Server
{
    internal sealed partial class ExtrusionDeviceBinding
    {
        /// <summary>
        /// Reuses the common package's real controllers rather than reproducing OPC 40083 protocols.
        /// </summary>
        private async ValueTask BindCommonAsync(CancellationToken cancellationToken)
        {
            ISystemContext context = m_runtime.SystemContext;
            foreach ((string _, BaseInstanceState node) in ExtrusionNodeAccess.Walk(context, m_node).ToArray())
            {
                switch (node)
                {
                    case StartDeviceState start:
                        if (m_common.StartDevices != null)
                        {
                            m_commonBindings.Add(new StartDeviceBinding(
                                start, context, m_common.StartDevices, RequireAccessPolicy()));
                        }
                        else if (start.StartRequest != null || start.StopRequest != null ||
                            start.StartBlockedByClient != null)
                        {
                            throw MissingProvider(nameof(m_common.StartDevices));
                        }
                        break;
                    case MaintenanceState maintenance:
                        m_commonBindings.Add(new MaintenanceBinding(
                            maintenance, context,
                            m_common.Maintenance ?? throw MissingProvider(nameof(m_common.Maintenance)),
                            RequireAccessPolicy()));
                        break;
                    case MonitoredParameterState parameter:
                        if (m_common.Parameters != null)
                        {
                            var binding = new MonitoredParameterBinding(
                                parameter, context, m_common.Parameters, RequireAccessPolicy(),
                                () => ReadParameterSettings(parameter));
                            m_commonBindings.Add(binding);
                            foreach (BaseInstanceState member in ExtrusionNodeAccess.Children(context, parameter))
                            {
                                if (member is BaseVariableState variable &&
                                    !ReferenceEquals(variable, parameter.Status))
                                {
                                    m_parameters.Add(variable.NodeId, binding);
                                }
                            }
                            BindSnapshotReads(parameter, parameter.Status);
                        }
                        else if (parameter.SetValue != null || parameter.ResetMonitoring != null ||
                            parameter.MinValue != null || parameter.MaxValue != null ||
                            parameter.AutomaticMonitoring != null)
                        {
                            throw MissingProvider(nameof(m_common.Parameters));
                        }
                        break;
                    case ClosedLoopControlState control:
                        m_commonBindings.Add(new ClosedLoopControlBinding(
                            control, context,
                            m_common.ClosedLoopControl ?? throw MissingProvider(nameof(m_common.ClosedLoopControl)),
                            RequireAccessPolicy(), () => ReadCommittedBoolean(control.AutomaticControllerMode)));
                        BindSnapshotReads(control);
                        break;
                    case MachineConfigurationState configuration:
                        m_commonBindings.Add(new MachineConfigurationBinding(
                            configuration, context,
                            m_common.MachineConfiguration
                                ?? throw MissingProvider(nameof(m_common.MachineConfiguration)),
                            RequireAccessPolicy()));
                        break;
                    case MaterialListState materials:
                        if (materials.DensityUnit != null && m_common.MaterialDensityUnit != null)
                        {
                            materials.DensityUnit.Value = (EUInformation)m_common.MaterialDensityUnit.Clone();
                        }
                        if (m_common.Materials != null ||
                            materials.AddMaterial != null || materials.RemoveMaterialById != null)
                        {
                            IMaterialListProvider materialProvider =
                                m_common.Materials ?? throw MissingProvider(nameof(m_common.Materials));
                            if (materials.DensityUnit?.Value == null)
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadConfigurationError,
                                    "The selected material catalogue needs an explicit '{0}'.",
                                    nameof(m_common.MaterialDensityUnit));
                            }
                            m_commonBindings.Add(new MaterialListBinding(
                                m_runtime.Manager, materials, materialProvider, RequireAccessPolicy()));
                        }
                        break;
                    case ProductionDatasetManagementState datasets:
                        ProductionDatasetBinding datasetBinding = await ProductionDatasetBinding.CreateAsync(
                            m_runtime.Manager, datasets,
                            m_common.ProductionDatasets ?? throw MissingProvider(nameof(m_common.ProductionDatasets)),
                            RequireAccessPolicy(), m_common.DatasetOptions is ProductionDatasetOptions selected
                                ? selected with { PreserveExistingSelection = true } : DatasetSelection(datasets),
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        m_commonBindings.Add(datasetBinding);
                        break;
                }
            }
            if (m_node is Line.ExtrusionLine_InterfaceState line &&
                (line.SetMESMessage != null || line.ClearMESMessage != null))
            {
                m_commonBindings.Add(new MesMessageBinding(
                    line, line.MESMessage ?? throw MissingProvider("MESMessage"), line.SetMESMessage,
                        line.ClearMESMessage,
                    context, m_common.MesMessages ?? throw MissingProvider(nameof(m_common.MesMessages)),
                    RequireAccessPolicy()));
            }
        }

        /// <summary>
        /// Reads selected physical parameter members from the commit without replacing custom protocol readers.
        /// </summary>
        private void BindSnapshotReads(NodeState owner, BaseVariableState? derivedValue = null)
        {
            foreach (BaseInstanceState member in ExtrusionNodeAccess.Children(m_runtime.SystemContext, owner))
            {
                if (member is BaseVariableState variable && !ReferenceEquals(variable, derivedValue) &&
                    variable.OnReadValueAsync == null && variable.OnReadValue == null &&
                    variable.OnSimpleReadValueAsync == null && variable.OnSimpleReadValue == null)
                {
                    variable.OnReadValueAsync = ReadAsync;
                }
            }
        }

        /// <summary>
        /// Captures limits and monitoring mode together from the current committed image.
        /// </summary>
        private (MonitoredParameterLimits Limits, bool AutomaticMonitoring) ReadParameterSettings(
            MonitoredParameterState parameter)
        {
            lock (m_sync)
            {
                return ReadParameterSettings(parameter, m_values);
            }
        }

        /// <summary>
        /// Extracts the shared monitored-parameter rules from a committed or staged image.
        /// </summary>
        private static (MonitoredParameterLimits Limits, bool AutomaticMonitoring) ReadParameterSettings(
            MonitoredParameterState parameter, Dictionary<NodeId, DataValue> values)
        {
            var limits = new MonitoredParameterLimits
            {
                SetValue = Number(parameter.SetValue),
                Minimum = Number(parameter.MinValue),
                Maximum = Number(parameter.MaxValue),
                LowerTolerance = Number(parameter.LowerTolerance),
                UpperTolerance = Number(parameter.UpperTolerance),
                LowerTolerance2 = Number(parameter.LowerTolerance2),
                UpperTolerance2 = Number(parameter.UpperTolerance2)
            };
            bool automatic = parameter.AutomaticMonitoring != null &&
                values[parameter.AutomaticMonitoring.NodeId].WrappedValue.TryGetValue(out bool enabled) && enabled;
            return (limits, automatic);

            double? Number(BaseVariableState? variable)
            {
                return variable != null && values[variable.NodeId].WrappedValue.TryGetValue(out double value)
                    ? value : null;
            }
        }

        /// <summary>
        /// Reads an optional controller mode from the committed image.
        /// </summary>
        private bool ReadCommittedBoolean(BaseVariableState? variable)
        {
            lock (m_sync)
            {
                return variable != null &&
                    m_values[variable.NodeId].WrappedValue.TryGetValue(out bool enabled) && enabled;
            }
        }

        /// <summary>
        /// Validates the complete parameter settings before publishing any part of a commit.
        /// </summary>
        private void ValidateCommonParameters(ExtrusionSnapshot snapshot)
        {
            var affected = new HashSet<MonitoredParameterBinding>();
            foreach (ExtrusionValue update in snapshot.Values)
            {
                if (m_parameters.TryGetValue(update.NodeId, out MonitoredParameterBinding? binding))
                {
                    affected.Add(binding);
                }
            }
            if (affected.Count == 0)
            {
                return;
            }
            var prospective = new Dictionary<NodeId, DataValue>(m_values);
            foreach (ExtrusionValue update in snapshot.Values)
            {
                prospective[update.NodeId] = update.Value;
            }
            foreach (MonitoredParameterBinding binding in affected)
            {
                try
                {
                    ReadParameterSettings(binding.Node, prospective).Limits.Validate();
                }
                catch (ArgumentException exception)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadOutOfRange, exception, "The committed parameter limits are inconsistent.");
                }
            }
        }

        /// <summary>
        /// Leaves reads on common capabilities with the common binder, including namespace-zero file properties.
        /// </summary>
        private bool BelongsToCommonCapability(BaseVariableState variable)
        {
            if (m_runtime.SystemContext.NamespaceUris.GetString(variable.BrowseName.NamespaceIndex) ==
                PlasticsRubber.Namespaces.PlasticsRubber)
            {
                return true;
            }
            for (NodeState? current = variable.Parent; current != null && current != m_node;
                current = (current as BaseInstanceState)?.Parent)
            {
                if (current is BaseInstanceState instance &&
                    m_runtime.SystemContext.NamespaceUris.GetString(instance.TypeDefinitionId.NamespaceIndex) ==
                        PlasticsRubber.Namespaces.PlasticsRubber)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Closes the default UA write-through path on values with no dedicated common protocol.
        /// </summary>
        private void BindRemainingWrites()
        {
            foreach ((string _, BaseInstanceState node) in ExtrusionNodeAccess.Walk(m_runtime.SystemContext, m_node))
            {
                if (node is BaseVariableState variable &&
                    (variable.AccessLevel & AccessLevels.CurrentWrite) != 0 &&
                    variable.OnWriteValueAsync == null && variable.OnWriteValue == null &&
                    variable.OnSimpleWriteValueAsync == null && variable.OnSimpleWriteValue == null)
                {
                    variable.OnWriteValueAsync = WriteAsync;
                    variable.OnReadValueAsync ??= ReadAsync;
                }
            }
        }

        /// <summary>
        /// Drives selected common monitoring status from the same absolute actual value, quality and source time.
        /// </summary>
        private void PublishCommonMeasurements(ExtrusionSnapshot snapshot)
        {
            var refreshed = new HashSet<MonitoredParameterBinding>();
            foreach (ExtrusionValue update in snapshot.Values)
            {
                if (!m_parameters.TryGetValue(update.NodeId, out MonitoredParameterBinding? binding) ||
                    !refreshed.Add(binding))
                {
                    continue;
                }
                DataValue observation;
                lock (m_sync)
                {
                    observation = m_values[binding.Node.ActualValue!.NodeId].Copy();
                }
                if (observation.WrappedValue.TryGetValue(out double actual))
                {
                    binding.Publish(actual, observation.StatusCode, observation.SourceTimestamp);
                }
                else
                {
                    binding.RefreshWriteAccess();
                }
            }
        }

        /// <summary>
        /// Uses the real selected dataset nodes to derive grouped common-protocol options.
        /// </summary>
        private static ProductionDatasetOptions DatasetSelection(ProductionDatasetManagementState node)
        {
            ProductionDatasetFeatures features = ProductionDatasetFeatures.None;
            if (node.ProductionDatasetInPreparationStatus != null)
            {
                features |= ProductionDatasetFeatures.Preparation;
            }
            if (node.ProductionDatasetLists != null)
            {
                features |= ProductionDatasetFeatures.Lists;
            }
            if (node.GetProductionDatasetInformation != null || node.SendProductionDatasetInformation != null)
            {
                features |= ProductionDatasetFeatures.Information;
            }
            if (node.ActiveProductionDatasetStatus?.Frozen != null ||
                node.ProductionDatasetInPreparationStatus?.Frozen != null)
            {
                features |= ProductionDatasetFeatures.Frozen;
            }
            if (node.ActiveProductionDatasetStatus?.Modified != null ||
                node.ProductionDatasetInPreparationStatus?.Modified != null)
            {
                features |= ProductionDatasetFeatures.Modified;
            }
            if (node.ActiveProductionDatasetStatus?.Load != null ||
                node.ActiveProductionDatasetStatus?.Save != null ||
                node.ProductionDatasetInPreparationStatus?.Load != null ||
                node.ProductionDatasetInPreparationStatus?.Save != null)
            {
                features |= ProductionDatasetFeatures.LoadSave;
            }
            return new ProductionDatasetOptions { Features = features, PreserveExistingSelection = true };
        }

        /// <summary>
        /// Requires an explicit policy whenever an authority is installed.
        /// </summary>
        private IPlasticsRubberAccessPolicy RequireAccessPolicy()
        {
            return m_common.AccessPolicy ?? throw MissingProvider(nameof(m_common.AccessPolicy));
        }

        /// <summary>
        /// Reports missing selected behavior during creation, not a successful-looking unimplemented method.
        /// </summary>
        private static ServiceResultException MissingProvider(string capability)
        {
            return ServiceResultException.Create(
                StatusCodes.BadConfigurationError, "Selected common capability '{0}' needs an explicit provider.",
                    capability);
        }

        /// <summary>
        /// Releases every owned common controller and preserves all cleanup failures.
        /// </summary>
        private async ValueTask DisposeCommonAsync()
        {
            List<Exception>? failures = null;
            for (int index = m_commonBindings.Count - 1; index >= 0; index--)
            {
                try
                {
                    await m_commonBindings[index].DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures != null)
            {
                throw new AggregateException("Common extrusion capability cleanup failed.", failures);
            }
        }

        /// <summary>The explicitly selected common capability authorities.</summary>
        private readonly ExtrusionCommonProviders m_common;

        /// <summary>Common controllers owned by this device, not their externally owned providers.</summary>
        private readonly List<IAsyncDisposable> m_commonBindings = [];

        /// <summary>Selected common parameter evaluators keyed by actual-value identity.</summary>
        private readonly Dictionary<NodeId, MonitoredParameterBinding> m_parameters = [];
    }
}
