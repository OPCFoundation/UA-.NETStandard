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
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// An instance method and its unmodified executability readings.
    /// </summary>
    /// <param name="NodeId">The instance method identifier.</param>
    /// <param name="BrowseName">The namespace-qualified method name.</param>
    /// <param name="Executable">The executable attribute, including its status.</param>
    /// <param name="UserExecutable">The current user's executable attribute.</param>
    public sealed record MachineVisionMethodCapability(
        NodeId NodeId, QualifiedName BrowseName, DataValue Executable, DataValue UserExecutable);

    /// <summary>
    /// A generated interface resolved against the actual system instance.
    /// </summary>
    /// <param name="Path">The interface's relative browse path.</param>
    /// <param name="NodeId">The instance identifier, or a null identifier when absent.</param>
    /// <param name="Methods">Methods actually published by this instance.</param>
    public sealed record MachineVisionInterfaceCapability(
        string Path, NodeId NodeId, ArrayOf<MachineVisionMethodCapability> Methods);

    /// <summary>
    /// Separates namespace support from the interfaces an individual system exposes.
    /// </summary>
    /// <param name="SupportsControl">Whether the Part 1 namespace is registered.</param>
    /// <param name="SupportsAssets">Whether the Part 2 namespace is registered.</param>
    /// <param name="Interfaces">Resolved system interfaces, empty for namespace-only discovery.</param>
    public sealed record MachineVisionCapabilities(
        bool SupportsControl, bool SupportsAssets, ArrayOf<MachineVisionInterfaceCapability> Interfaces);

    /// <summary>
    /// One optional system reading, retaining absence, quality, and timestamps.
    /// </summary>
    /// <param name="Path">The relative browse path.</param>
    /// <param name="NodeId">The variable identifier, or null when unavailable.</param>
    /// <param name="Value">The server reading or the path-resolution error.</param>
    public sealed record MachineVisionStatusValue(string Path, NodeId NodeId, DataValue Value);

    /// <summary>
    /// One published step model and its current finite state.
    /// </summary>
    /// <param name="Path">The relative step-model path.</param>
    /// <param name="NodeId">The step-model instance identifier.</param>
    /// <param name="State">The state and its service status.</param>
    public sealed record MachineVisionStepSnapshot(string Path, NodeId NodeId, FiniteStateSnapshot State);

    /// <summary>
    /// Optional diagnostics, configuration, safety, and step-model readings.
    /// </summary>
    /// <param name="Values">Namespace-qualified variable readings.</param>
    /// <param name="Steps">Published step models.</param>
    public sealed record MachineVisionStatusSnapshot(
        ArrayOf<MachineVisionStatusValue> Values, ArrayOf<MachineVisionStepSnapshot> Steps);

    public sealed partial class MachineVisionClient
    {
        /// <summary>
        /// Reports namespace support and optionally inspects an actual system's interfaces.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<MachineVisionCapabilities> ReadCapabilitiesAsync(
            NodeId system = default, CancellationToken cancellationToken = default)
        {
            if (system.IsNull)
            {
                return new MachineVisionCapabilities(SupportsControl, SupportsAssets, []);
            }
            if (!SupportsControl)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The Part 1 namespace is unavailable.");
            }
            // Verify the selected node before optional generated accessors can report absent children.
            _ = await BrowseAsync(system, ReferenceTypeIds.HasComponent,
                BrowseDirection.Forward, (uint)NodeClass.Object, cancellationToken).ConfigureAwait(false);
            await ValidateInterfacePathsAsync(system, cancellationToken).ConfigureAwait(false);
            var proxy = new VisionSystemTypeClient(Session, system, m_operations.Telemetry);
            var interfaces = new List<MachineVisionInterfaceCapability>();
            VisionStateMachineTypeClient? control = await proxy
                .GetVisionStateMachineAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            await AddInterfaceAsync("VisionStateMachine", control, interfaces, cancellationToken).ConfigureAwait(false);
            if (control != null)
            {
                await AddInterfaceAsync("VisionStateMachine/PreoperationalStepModel", await control
                    .GetPreoperationalStepModelAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false),
                    interfaces, cancellationToken).ConfigureAwait(false);
                await AddInterfaceAsync("VisionStateMachine/HaltedStepModel", await control
                    .GetHaltedStepModelAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false),
                    interfaces, cancellationToken).ConfigureAwait(false);
                await AddInterfaceAsync("VisionStateMachine/ErrorStepModel", await control
                    .GetErrorStepModelAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false),
                    interfaces, cancellationToken).ConfigureAwait(false);
                VisionAutomaticModeStateMachineTypeClient? automatic = await control
                    .GetAutomaticModeStateMachineAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
                const string automaticPath = "VisionStateMachine/AutomaticModeStateMachine";
                await AddInterfaceAsync(automaticPath, automatic, interfaces, cancellationToken).ConfigureAwait(false);
                if (automatic != null)
                {
                    await AddInterfaceAsync(automaticPath + "/InitializedStepModel", await automatic
                        .GetInitializedStepModelAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false),
                        interfaces, cancellationToken).ConfigureAwait(false);
                    await AddInterfaceAsync(automaticPath + "/ReadyStepModel", await automatic
                        .GetReadyStepModelAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false),
                        interfaces, cancellationToken).ConfigureAwait(false);
                    await AddInterfaceAsync(automaticPath + "/SingleExecutionStepModel", await automatic
                        .GetSingleExecutionStepModelAsync(m_operations.Telemetry, cancellationToken)
                        .ConfigureAwait(false),
                        interfaces, cancellationToken).ConfigureAwait(false);
                    await AddInterfaceAsync(automaticPath + "/ContinuousExecutionStepModel", await automatic
                        .GetContinuousExecutionStepModelAsync(m_operations.Telemetry, cancellationToken)
                        .ConfigureAwait(false), interfaces, cancellationToken).ConfigureAwait(false);
                }
            }
            RecipeManagementTypeClient? recipes = await proxy
                .GetRecipeManagementAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            await AddInterfaceAsync("RecipeManagement", recipes, interfaces, cancellationToken).ConfigureAwait(false);
            await AddInterfaceAsync("RecipeManagement/RecipeTransfer", recipes == null ? null : await recipes
                .GetRecipeTransferAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false),
                interfaces, cancellationToken).ConfigureAwait(false);
            ConfigurationManagementTypeClient? configurations = await proxy
                .GetConfigurationManagementAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            await AddInterfaceAsync("ConfigurationManagement", configurations, interfaces, cancellationToken)
                .ConfigureAwait(false);
            await AddInterfaceAsync("ConfigurationManagement/ConfigurationTransfer",
                configurations == null ? null : await configurations
                    .GetConfigurationTransferAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false),
                interfaces, cancellationToken).ConfigureAwait(false);
            ResultManagementTypeClient? results = await proxy
                .GetResultManagementAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            await AddInterfaceAsync("ResultManagement", results, interfaces, cancellationToken).ConfigureAwait(false);
            await AddInterfaceAsync("ResultManagement/ResultTransfer", results == null ? null : await results
                .GetResultTransferAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false),
                interfaces, cancellationToken).ConfigureAwait(false);
            await AddInterfaceAsync("SafetyStateManagement", await proxy
                .GetSafetyStateManagementAsync(m_operations.Telemetry, cancellationToken).ConfigureAwait(false),
                interfaces, cancellationToken).ConfigureAwait(false);
            return new MachineVisionCapabilities(SupportsControl, SupportsAssets, interfaces.ToArrayOf());
        }

        /// <summary>
        /// Reads optional system values and published step states without changing the system.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask<MachineVisionStatusSnapshot> ReadStatusAsync(
            NodeId system, CancellationToken cancellationToken = default)
        {
            if (system.IsNull)
            {
                throw new ArgumentNullException(nameof(system));
            }
            MachineVisionCapabilities capabilities = await ReadCapabilitiesAsync(system, cancellationToken)
                .ConfigureAwait(false);
            var values = new List<MachineVisionStatusValue>();
            foreach (string path in s_statusPaths)
            {
                values.Add(await ReadStatusValueAsync(system, path, cancellationToken).ConfigureAwait(false));
            }
            var steps = new List<MachineVisionStepSnapshot>();
            for (int i = 0; i < capabilities.Interfaces.Count; i++)
            {
                MachineVisionInterfaceCapability entry = capabilities.Interfaces[i];
                if (entry.NodeId.IsNull || !entry.Path.EndsWith("StepModel", StringComparison.Ordinal))
                {
                    continue;
                }
                var step = new VisionStepModelStateMachineTypeClient(Session, entry.NodeId, m_operations.Telemetry);
                steps.Add(new MachineVisionStepSnapshot(entry.Path, entry.NodeId,
                    await step.GetCurrentFiniteStateAsync(cancellationToken).ConfigureAwait(false)));
            }
            return new MachineVisionStatusSnapshot(values.ToArrayOf(), steps.ToArrayOf());
        }

        private async ValueTask AddInterfaceAsync(
            string path, ObjectTypeClient? proxy, List<MachineVisionInterfaceCapability> interfaces,
            CancellationToken ct)
        {
            if (proxy == null)
            {
                interfaces.Add(new MachineVisionInterfaceCapability(path, NodeId.Null, []));
                return;
            }
            ArrayOf<ReferenceDescription> references = await BrowseAsync(
                proxy.ObjectId, ReferenceTypeIds.HasComponent,
                BrowseDirection.Forward, (uint)NodeClass.Method, ct).ConfigureAwait(false);
            if (references.Count > 100)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The interface exposes more than 100 methods.");
            }
            var reads = new List<ReadValueId>();
            var methods = new List<MachineVisionMethodCapability>();
            foreach (ReferenceDescription reference in references)
            {
                var id = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                reads.Add(new ReadValueId { NodeId = id, AttributeId = Attributes.Executable });
                reads.Add(new ReadValueId { NodeId = id, AttributeId = Attributes.UserExecutable });
            }
            if (reads.Count != 0)
            {
                var requests = reads.ToArrayOf();
                ReadResponse response = await Session.ReadAsync(null, 0, TimestampsToReturn.Both, requests, ct)
                    .ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, requests);
                for (int i = 0; i < references.Count; i++)
                {
                    methods.Add(new MachineVisionMethodCapability(reads[2 * i].NodeId, references[i].BrowseName,
                        response.Results[2 * i], response.Results[(2 * i) + 1]));
                }
            }
            interfaces.Add(new MachineVisionInterfaceCapability(path, proxy.ObjectId, methods.ToArrayOf()));
        }

        private async ValueTask ValidateInterfacePathsAsync(NodeId system, CancellationToken ct)
        {
            ushort ns = checked((ushort)Session.NamespaceUris.GetIndex(MachineVisionModels.ControlNamespaceUri));
            var paths = new List<BrowsePath>();
            foreach (string path in s_interfacePaths)
            {
                var elements = new List<RelativePathElement>();
                foreach (string name in path.Split('/'))
                {
                    elements.Add(new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(name, ns)
                    });
                }
                paths.Add(new BrowsePath
                {
                    StartingNode = system,
                    RelativePath = new RelativePath { Elements = elements.ToArrayOf() }
                });
            }
            var requests = paths.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(null, requests, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, requests);
            foreach (BrowsePathResult result in response.Results)
            {
                if (result.StatusCode == StatusCodes.BadNoMatch || result.StatusCode == StatusCodes.BadNotFound)
                {
                    continue;
                }
                if (StatusCode.IsBad(result.StatusCode))
                {
                    throw new ServiceResultException(result.StatusCode);
                }
                if (result.Targets.Count != 1 || result.Targets[0].RemainingPathIndex != uint.MaxValue)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                        "An interface path did not resolve to one complete target.");
                }
            }
        }

        private async ValueTask<MachineVisionStatusValue> ReadStatusValueAsync(
            NodeId system, string path, CancellationToken ct)
        {
            ushort ns = checked((ushort)Session.NamespaceUris.GetIndex(MachineVisionModels.ControlNamespaceUri));
            var elements = new List<RelativePathElement>();
            foreach (string name in path.Split('/'))
            {
                elements.Add(new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName(name, ns)
                });
            }
            ArrayOf<BrowsePath> paths =
            [
                new BrowsePath
                {
                    StartingNode = system,
                    RelativePath = new RelativePath { Elements = elements.ToArrayOf() }
                }
            ];
            TranslateBrowsePathsToNodeIdsResponse translated =
                await Session.TranslateBrowsePathsToNodeIdsAsync(null, paths, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(translated.Results, paths);
            BrowsePathResult result = translated.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                return new MachineVisionStatusValue(path, NodeId.Null, DataValue.FromStatusCode(result.StatusCode));
            }
            if (result.Targets.Count != 1 || result.Targets[0].RemainingPathIndex != uint.MaxValue)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                    "The system status path did not resolve to one complete target.");
            }
            var node = ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris);
            if (node.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The status target is not local.");
            }
            ArrayOf<DataValue> readings = await m_operations.ReadValuesAsync([node], ct).ConfigureAwait(false);
            return new MachineVisionStatusValue(path, node, readings[0]);
        }

        /// <summary>Optional status paths in the released control namespace.</summary>
        private static readonly string[] s_statusPaths =
        [
            "DiagnosticLevel",
            "SystemState",
            "ConfigurationManagement/ActiveConfiguration",
            "SafetyStateManagement/VisionSafetyTriggered",
            "SafetyStateManagement/VisionSafetyInformation"
        ];

        /// <summary>Paths whose absence is distinguished from access or transport failures.</summary>
        private static readonly string[] s_interfacePaths =
        [
            "VisionStateMachine",
            "VisionStateMachine/PreoperationalStepModel",
            "VisionStateMachine/HaltedStepModel",
            "VisionStateMachine/ErrorStepModel",
            "VisionStateMachine/AutomaticModeStateMachine",
            "VisionStateMachine/AutomaticModeStateMachine/InitializedStepModel",
            "VisionStateMachine/AutomaticModeStateMachine/ReadyStepModel",
            "VisionStateMachine/AutomaticModeStateMachine/SingleExecutionStepModel",
            "VisionStateMachine/AutomaticModeStateMachine/ContinuousExecutionStepModel",
            "RecipeManagement",
            "RecipeManagement/RecipeTransfer",
            "ConfigurationManagement",
            "ConfigurationManagement/ConfigurationTransfer",
            "ResultManagement",
            "ResultManagement/ResultTransfer",
            "SafetyStateManagement"
        ];
    }
}
