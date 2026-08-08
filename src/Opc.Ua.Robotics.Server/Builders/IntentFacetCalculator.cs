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

using System.Collections.Generic;
using System.Linq;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Computes Robot Intent conformance facets from a materialised controller.
    /// </summary>
    public static class RobotIntentFacetCalculator
    {
        /// <summary>
        /// Computes the facets satisfied by <paramref name="controller"/>.
        /// </summary>
        /// <remarks>
        /// Clause 12.2 facet rows mix structural requirements with behavioural requirements. This calculator checks
        /// structural evidence available in the materialised address space and the controller capability declaration:
        /// required intent DataTypes in <c>SupportedIntents</c>; joint axes covering <c>0..AxisCount - 1</c>;
        /// trajectory, path, force, real-time, mission, mission-horizon, mission-branching and blending capability
        /// flags; folders or nodes named by a row, including tools, locations, outputs, programs, a bound
        /// safety-state admission gate, description, real-time channels and mission methods; tool TCP frames and
        /// tool-frame roles; description kinematic-chain coverage and limits; queue depth; accepted buffer modes;
        /// pause/resume and retry methods; palletise location patterns; and the RI-Force dependency for surface
        /// finish.
        /// Behavioural parts cannot be settled by reading the address space. The calculator therefore trusts the
        /// server's attestation for the RI-Base refusal rules; trajectory tolerance rules; force regulation;
        /// real-time lease rules; safety-state values being populated from that bound source and safety refusals;
        /// process execution semantics; queue
        /// position maintenance; blending being honoured and <c>Result.AchievedPose</c> reporting the blend point;
        /// pause/retry state reachability; mission execution, base immutability and transition/error-policy
        /// behaviour; and OPC 40010 geometry semantics. The structural OPC 40010 interop link is checked from the
        /// inverse <c>HasIntentController</c> reference on the controller, bound OPC 40010 operational-mode evidence,
        /// and loaded task-control program evidence where available.
        /// </remarks>
        public static ArrayOf<string> Compute(IntentControllerState controller)
        {
            if (controller == null)
            {
                throw new System.ArgumentNullException(nameof(controller));
            }
            var facets = new List<string> { "RI-Base" };
            ArrayOf<IntentCapabilityDataType> capabilities = GetSupportedIntentCapabilities(controller);
            HashSet<NodeId> intents = GetSupportedIntentTypes(capabilities);
            ushort robotIntentNamespaceIndex = GetRobotIntentNamespaceIndex(controller);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Motion-Joint", global::Opc.Ua.RobotIntent.DataTypes.JointMoveIntentDataType,
                controller.Axes != null && AxisIndicesAreContiguous(controller));
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Motion-Linear", global::Opc.Ua.RobotIntent.DataTypes.LinearMoveIntentDataType);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Motion-Circular",
                global::Opc.Ua.RobotIntent.DataTypes.CircularMoveIntentDataType);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Trajectory", global::Opc.Ua.RobotIntent.DataTypes.TrajectoryIntentDataType,
                controller.Capabilities?.TrajectorySupported?.Value == true);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Path", global::Opc.Ua.RobotIntent.DataTypes.CartesianPathIntentDataType,
                controller.Capabilities?.TrajectorySupported?.Value == true);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Force", global::Opc.Ua.RobotIntent.DataTypes.ForceIntentDataType,
                controller.Capabilities?.ForceControlSupported?.Value == true);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Grasp", global::Opc.Ua.RobotIntent.DataTypes.GraspIntentDataType,
                HasIntent(intents, robotIntentNamespaceIndex, global::Opc.Ua.RobotIntent.DataTypes.ReleaseIntentDataType) &&
                HasToolWithTcpFrame(controller));
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-PickPlace", global::Opc.Ua.RobotIntent.DataTypes.PickIntentDataType,
                HasIntent(intents, robotIntentNamespaceIndex, global::Opc.Ua.RobotIntent.DataTypes.PlaceIntentDataType) &&
                HasAny(controller.Locations));
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-ToolChange", global::Opc.Ua.RobotIntent.DataTypes.ToolChangeIntentDataType,
                CountChildren(controller.Tools) > 1);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Output", global::Opc.Ua.RobotIntent.DataTypes.SetOutputIntentDataType,
                controller.Outputs != null);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Program", global::Opc.Ua.RobotIntent.DataTypes.CallProgramIntentDataType,
                controller.Programs != null);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Wait", global::Opc.Ua.RobotIntent.DataTypes.WaitIntentDataType);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Process-ArcWeld",
                global::Opc.Ua.RobotIntent.DataTypes.ArcWeldIntentDataType);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Process-SpotWeld",
                global::Opc.Ua.RobotIntent.DataTypes.SpotWeldIntentDataType);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Process-Dispense",
                global::Opc.Ua.RobotIntent.DataTypes.DispenseIntentDataType);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Process-Fasten",
                global::Opc.Ua.RobotIntent.DataTypes.FastenIntentDataType);
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Process-Palletise",
                global::Opc.Ua.RobotIntent.DataTypes.PalletiseIntentDataType,
                HasAny(controller.Locations));
            AddIf(facets, intents, robotIntentNamespaceIndex, "RI-Process-SurfaceFinish",
                global::Opc.Ua.RobotIntent.DataTypes.SurfaceFinishIntentDataType,
                HasFacet(facets, "RI-Force"));
            if (IntentControllerFacetMetadata.HasSafetyAdmissionGate(controller))
            {
                facets.Add("RI-Safety");
            }
            if (HasCompleteDescription(controller))
            {
                facets.Add("RI-Description");
            }
            if (HasRealTimeChannelSupport(controller))
            {
                facets.Add("RI-RealTimeChannel");
            }
            if (HasAnyCapabilityWithPause(capabilities) && controller.Pause != null && controller.Resume != null)
            {
                facets.Add("RI-Pause");
            }
            if (HasAnyCapabilityWithRetry(capabilities) && controller.Retry != null)
            {
                facets.Add("RI-Retry");
            }
            if (HasQueueSupport(controller, capabilities))
            {
                facets.Add("RI-Queue");
            }
            if (HasBlendingSupport(controller, capabilities))
            {
                facets.Add("RI-Blending");
            }
            bool mission = HasMissionSupport(controller);
            if (mission)
            {
                facets.Add("RI-Mission");
            }
            if (mission &&
                controller.Capabilities?.MissionHorizonSupported?.Value == true &&
                controller.UpdateMission != null)
            {
                facets.Add("RI-Mission-Horizon");
            }
            if (mission && controller.Capabilities?.MissionBranchingSupported?.Value == true)
            {
                facets.Add("RI-Mission-Branching");
            }
            if (HasInterop40010Link(controller, robotIntentNamespaceIndex))
            {
                facets.Add("RI-Interop-40010");
            }
            return facets.ToArrayOf();
        }

        private static void AddIf(
            List<string> facets,
            HashSet<NodeId> intents,
            ushort robotIntentNamespaceIndex,
            string facet,
            uint dataTypeId,
            bool condition = true)
        {
            if (condition && HasIntent(intents, robotIntentNamespaceIndex, dataTypeId))
            {
                facets.Add(facet);
            }
        }

        private static bool HasIntent(HashSet<NodeId> intents, ushort robotIntentNamespaceIndex, uint dataTypeId)
        {
            foreach (NodeId intent in intents)
            {
                if (intent.NamespaceIndex == robotIntentNamespaceIndex &&
                    intent.TryGetValue(out uint numeric) &&
                    numeric == dataTypeId)
                {
                    return true;
                }
            }
            return false;
        }

        private static ArrayOf<IntentCapabilityDataType> GetSupportedIntentCapabilities(
            IntentControllerState controller)
        {
            return controller.Capabilities?.SupportedIntents?.Value ?? [];
        }

        private static HashSet<NodeId> GetSupportedIntentTypes(ArrayOf<IntentCapabilityDataType> capabilities)
        {
            var result = new HashSet<NodeId>();
            for (int ii = 0; ii < capabilities.Count; ii++)
            {
                if (!capabilities[ii].IntentType.IsNull)
                {
                    result.Add(capabilities[ii].IntentType);
                }
            }
            return result;
        }

        private static bool AxisIndicesAreContiguous(IntentControllerState controller)
        {
            uint axisCount = controller.Capabilities?.AxisCount?.Value ?? 0;
            if (axisCount == 0)
            {
                return false;
            }
            var indices = new HashSet<uint>();
            foreach (global::Opc.Ua.RobotIntent.AxisState axis in GetChildren<global::Opc.Ua.RobotIntent.AxisState>(
                controller.Axes))
            {
                if (axis.Index == null || !indices.Add(axis.Index.Value))
                {
                    return false;
                }
            }
            if (axisCount != indices.Count)
            {
                return false;
            }
            for (uint ii = 0; ii < axisCount; ii++)
            {
                if (!indices.Contains(ii))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasToolWithTcpFrame(IntentControllerState controller)
        {
            foreach (ToolState tool in GetChildren<ToolState>(controller.Tools))
            {
                if (tool.TcpFrame != null &&
                    !tool.TcpFrame.Value.IsNull &&
                    HasToolFrame(controller, tool.TcpFrame.Value))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasToolFrame(IntentControllerState controller, NodeId tcpFrame)
        {
            foreach (CoordinateFrameState frame in GetChildren<CoordinateFrameState>(controller.Frames))
            {
                if (frame.NodeId == tcpFrame && frame.Role?.Value == FrameRoleEnum.Tool)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasCompleteDescription(IntentControllerState controller)
        {
            if (controller.Description == null ||
                controller.Description.KinematicChain == null ||
                controller.Description.ReachRadius == null ||
                !(controller.Description.ReachRadius.Value > 0.0) ||
                controller.Description.PayloadLimit == null ||
                !(controller.Description.PayloadLimit.Value > 0.0) ||
                controller.Description.MaxCartesianSpeed == null ||
                !(controller.Description.MaxCartesianSpeed.Value > 0.0))
            {
                return false;
            }
            List<global::Opc.Ua.RobotIntent.AxisState> axes =
                GetChildren<global::Opc.Ua.RobotIntent.AxisState>(controller.Axes);
            if (axes.Count == 0 ||
                controller.Capabilities?.AxisCount == null ||
                controller.Capabilities.AxisCount.Value != axes.Count ||
                controller.Description.KinematicChain.Value.Count != axes.Count)
            {
                return false;
            }
            var axisIds = new HashSet<string>(System.StringComparer.Ordinal);
            for (int ii = 0; ii < axes.Count; ii++)
            {
                string? axisId = axes[ii].AxisId?.Value;
                if (string.IsNullOrWhiteSpace(axisId) || !axisIds.Add(axisId))
                {
                    return false;
                }
            }
            for (int ii = 0; ii < controller.Description.KinematicChain.Value.Count; ii++)
            {
                string? axisId = controller.Description.KinematicChain.Value[ii].AxisId;
                if (string.IsNullOrWhiteSpace(axisId) || !axisIds.Remove(axisId))
                {
                    return false;
                }
            }
            return axisIds.Count == 0;
        }

        private static bool HasQueueSupport(
            IntentControllerState controller,
            ArrayOf<IntentCapabilityDataType> capabilities)
        {
            return controller.MaxQueueDepth?.Value > 0 &&
                HasAnyCapabilityWithBufferMode(capabilities, BufferModeEnum.Buffered);
        }

        private static bool HasBlendingSupport(
            IntentControllerState controller,
            ArrayOf<IntentCapabilityDataType> capabilities)
        {
            return controller.Capabilities?.BlendingSupported?.Value == true &&
                HasAnyCapabilityWithBufferModes(
                    capabilities,
                    BufferModeEnum.BlendingLow,
                    BufferModeEnum.BlendingPrevious,
                    BufferModeEnum.BlendingNext,
                    BufferModeEnum.BlendingHigh);
        }

        private static bool HasMissionSupport(IntentControllerState controller)
        {
            return controller.Capabilities?.MissionsSupported?.Value == true &&
                controller.SubmitMission != null &&
                controller.CancelMission != null;
        }

        private static bool HasRealTimeChannelSupport(IntentControllerState controller)
        {
            return controller.Capabilities?.RealTimeChannelsSupported?.Value == true &&
                GetChildren<RealTimeChannelState>(controller.RealTimeChannels).Count > 0 &&
                controller.OpenRealTimeChannel != null &&
                controller.CloseRealTimeChannel != null;
        }

        private static bool HasAnyCapabilityWithPause(ArrayOf<IntentCapabilityDataType> capabilities)
        {
            for (int ii = 0; ii < capabilities.Count; ii++)
            {
                if (capabilities[ii].PauseSupported)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasAnyCapabilityWithRetry(ArrayOf<IntentCapabilityDataType> capabilities)
        {
            for (int ii = 0; ii < capabilities.Count; ii++)
            {
                if (capabilities[ii].RetrySupported)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasAnyCapabilityWithBufferMode(
            ArrayOf<IntentCapabilityDataType> capabilities,
            BufferModeEnum mode)
        {
            for (int ii = 0; ii < capabilities.Count; ii++)
            {
                if (HasBufferMode(capabilities[ii], mode))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasAnyCapabilityWithBufferModes(
            ArrayOf<IntentCapabilityDataType> capabilities,
            params BufferModeEnum[] modes)
        {
            for (int ii = 0; ii < capabilities.Count; ii++)
            {
                bool hasAll = true;
                for (int jj = 0; jj < modes.Length; jj++)
                {
                    if (!HasBufferMode(capabilities[ii], modes[jj]))
                    {
                        hasAll = false;
                        break;
                    }
                }
                if (hasAll)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasBufferMode(IntentCapabilityDataType capability, BufferModeEnum mode)
        {
            ArrayOf<BufferModeEnum> supported = capability.SupportedBufferModes;
            if (supported.IsNull)
            {
                return false;
            }
            for (int ii = 0; ii < supported.Count; ii++)
            {
                if (supported[ii] == mode)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasFacet(List<string> facets, string facet)
        {
            return facets.Contains(facet);
        }

        private static bool HasInterop40010Link(IntentControllerState controller, ushort robotIntentNamespaceIndex)
        {
            if (!IntentControllerFacetMetadata.TryGetInterop40010Binding(
                controller,
                out MotionDeviceSystemState motionDeviceSystem))
            {
                return false;
            }
            var references = new List<IReference>();
            controller.GetReferences(null!, references);
            for (int ii = 0; ii < references.Count; ii++)
            {
                IReference reference = references[ii];
                if (reference.IsInverse &&
                    IsHasIntentControllerReference(reference.ReferenceTypeId, robotIntentNamespaceIndex) &&
                    reference.TargetId == motionDeviceSystem.NodeId)
                {
                    return OperationalModesAgree(controller, motionDeviceSystem) &&
                        TaskControlProgramsMatchPublishedPrograms(controller, motionDeviceSystem);
                }
            }
            return false;
        }

        private static bool OperationalModesAgree(
            IntentControllerState controller,
            MotionDeviceSystemState motionDeviceSystem)
        {
            BaseVariableState? operationalMode = FindOperationalMode(motionDeviceSystem);
            if (operationalMode == null)
            {
                return false;
            }
            return controller.OperationalMode?.Value == ToIntentOperationalMode(operationalMode);
        }

        private static bool TaskControlProgramsMatchPublishedPrograms(
            IntentControllerState controller,
            MotionDeviceSystemState motionDeviceSystem)
        {
            var taskControlPrograms = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (TaskControlState taskControl in GetDescendants<TaskControlState>(motionDeviceSystem))
            {
                BaseObjectState? parameterSet = FindChild<BaseObjectState>(taskControl, "ParameterSet");
                if (parameterSet == null)
                {
                    return false;
                }
                BaseVariableState? name = FindChild<BaseVariableState>(
                    parameterSet,
                    BrowseNames.TaskProgramName);
                if (name == null)
                {
                    return false;
                }
                string? programName = name.Value.GetString(null!);
                if (string.IsNullOrWhiteSpace(programName))
                {
                    return false;
                }
                taskControlPrograms.Add(programName);
            }
            return TryGetPublishedPrograms(controller, out HashSet<string> publishedPrograms) &&
                taskControlPrograms.SetEquals(publishedPrograms);
        }

        private static bool TryGetPublishedPrograms(
            IntentControllerState controller,
            out HashSet<string> programs)
        {
            programs = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (ProgramState program in GetChildren<ProgramState>(controller.Programs))
            {
                string? programId = program.ProgramId?.Value;
                if (string.IsNullOrWhiteSpace(programId))
                {
                    return false;
                }
                programs.Add(programId);
            }
            return true;
        }

        private static BaseVariableState? FindOperationalMode(
            MotionDeviceSystemState motionDeviceSystem)
        {
            foreach (SafetyStateState safetyState in GetDescendants<SafetyStateState>(motionDeviceSystem))
            {
                BaseObjectState? parameterSet = FindChild<BaseObjectState>(safetyState, "ParameterSet");
                if (parameterSet != null)
                {
                    BaseVariableState? operationalMode = FindChild<BaseVariableState>(
                        parameterSet,
                        BrowseNames.OperationalMode);
                    if (operationalMode != null)
                    {
                        return operationalMode;
                    }
                }
            }
            return null;
        }

        private static OperationalModeEnum ToIntentOperationalMode(BaseVariableState variable)
        {
            if (variable.Value.TryGetValue(out OperationalModeEnumeration mode))
            {
                return ToIntentOperationalMode((int)mode);
            }
            return ToIntentOperationalMode(variable.Value.GetInt32(0));
        }

        private static OperationalModeEnum ToIntentOperationalMode(int value)
        {
            return value switch
            {
                1 => OperationalModeEnum.ManualReducedSpeed,
                2 => OperationalModeEnum.ManualHighSpeed,
                3 => OperationalModeEnum.Automatic,
                4 => OperationalModeEnum.AutomaticExternal,
                _ => OperationalModeEnum.Other
            };
        }

        private static bool IsHasIntentControllerReference(NodeId referenceTypeId, ushort robotIntentNamespaceIndex)
        {
            return referenceTypeId.NamespaceIndex == robotIntentNamespaceIndex &&
                referenceTypeId.TryGetValue(out uint numeric) &&
                numeric == global::Opc.Ua.RobotIntent.ReferenceTypes.HasIntentController;
        }

        private static ushort GetRobotIntentNamespaceIndex(IntentControllerState controller)
        {
            return controller.TypeDefinitionId.NamespaceIndex;
        }

        private static bool HasAny(NodeState? folder)
        {
            return CountChildren(folder) > 0;
        }

        private static int CountChildren(NodeState? folder)
        {
            if (folder == null)
            {
                return 0;
            }
            var children = new List<BaseInstanceState>();
            folder.GetChildren(null!, children);
            return children.Count;
        }

        private static List<T> GetChildren<T>(NodeState? folder)
            where T : BaseInstanceState
        {
            var children = new List<BaseInstanceState>();
            folder?.GetChildren(null!, children);
            return children.OfType<T>().ToList();
        }

        private static TChild? FindChild<TChild>(NodeState parent, string browseName)
            where TChild : BaseInstanceState
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(null!, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii] is TChild typed &&
                    string.Equals(children[ii].BrowseName.Name, browseName, System.StringComparison.Ordinal))
                {
                    return typed;
                }
            }
            return null;
        }

        private static List<T> GetDescendants<T>(NodeState root)
            where T : BaseInstanceState
        {
            var matches = new List<T>();
            AddDescendants(root, matches);
            return matches;
        }

        private static void AddDescendants<T>(NodeState node, List<T> matches)
            where T : BaseInstanceState
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(null!, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii] is T typed)
                {
                    matches.Add(typed);
                }
                AddDescendants(children[ii], matches);
            }
        }
    }
}
