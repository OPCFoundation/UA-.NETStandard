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
        public static ArrayOf<string> Compute(IntentControllerState controller)
        {
            if (controller == null)
            {
                throw new System.ArgumentNullException(nameof(controller));
            }
            var facets = new List<string> { "RI-Base" };
            HashSet<NodeId> intents = GetSupportedIntentTypes(controller);
            AddIf(facets, intents, "RI-Motion-Joint", global::Opc.Ua.RobotIntent.DataTypes.JointMoveIntentDataType,
                controller.Axes != null && AxisIndicesAreContiguous(controller));
            AddIf(facets, intents, "RI-Motion-Linear", global::Opc.Ua.RobotIntent.DataTypes.LinearMoveIntentDataType);
            AddIf(facets, intents, "RI-Motion-Circular",
                global::Opc.Ua.RobotIntent.DataTypes.CircularMoveIntentDataType);
            AddIf(facets, intents, "RI-Trajectory", global::Opc.Ua.RobotIntent.DataTypes.TrajectoryIntentDataType,
                controller.Capabilities?.TrajectorySupported?.Value == true);
            AddIf(facets, intents, "RI-Path", global::Opc.Ua.RobotIntent.DataTypes.CartesianPathIntentDataType,
                controller.Capabilities?.TrajectorySupported?.Value == true);
            AddIf(facets, intents, "RI-Force", global::Opc.Ua.RobotIntent.DataTypes.ForceIntentDataType,
                controller.Capabilities?.ForceControlSupported?.Value == true);
            AddIf(facets, intents, "RI-Grasp", global::Opc.Ua.RobotIntent.DataTypes.GraspIntentDataType,
                HasIntent(intents, global::Opc.Ua.RobotIntent.DataTypes.ReleaseIntentDataType) &&
                HasAny(controller.Tools));
            AddIf(facets, intents, "RI-PickPlace", global::Opc.Ua.RobotIntent.DataTypes.PickIntentDataType,
                HasIntent(intents, global::Opc.Ua.RobotIntent.DataTypes.PlaceIntentDataType) &&
                HasAny(controller.Locations));
            AddIf(facets, intents, "RI-ToolChange", global::Opc.Ua.RobotIntent.DataTypes.ToolChangeIntentDataType,
                CountChildren(controller.Tools) > 1);
            AddIf(facets, intents, "RI-Output", global::Opc.Ua.RobotIntent.DataTypes.SetOutputIntentDataType,
                controller.Outputs != null);
            AddIf(facets, intents, "RI-Program", global::Opc.Ua.RobotIntent.DataTypes.CallProgramIntentDataType,
                controller.Programs != null);
            AddIf(facets, intents, "RI-Wait", global::Opc.Ua.RobotIntent.DataTypes.WaitIntentDataType);
            AddIf(facets, intents, "RI-Mission", global::Opc.Ua.RobotIntent.DataTypes.JointMoveIntentDataType,
                controller.Capabilities?.MissionsSupported?.Value == true && controller.SubmitMission != null);
            if (controller.SafetyState != null)
            {
                facets.Add("RI-Safety");
            }
            if (controller.Description != null)
            {
                facets.Add("RI-Description");
            }
            if (controller.RealTimeChannels != null &&
                controller.Capabilities?.RealTimeChannelsSupported?.Value == true)
            {
                facets.Add("RI-RealTimeChannel");
            }
            return facets.ToArrayOf();
        }

        private static void AddIf(
            List<string> facets,
            HashSet<NodeId> intents,
            string facet,
            uint dataTypeId,
            bool condition = true)
        {
            if (condition && HasIntent(intents, dataTypeId))
            {
                facets.Add(facet);
            }
        }

        private static bool HasIntent(HashSet<NodeId> intents, uint dataTypeId)
        {
            foreach (NodeId intent in intents)
            {
                if (intent.TryGetValue(out uint numeric) && numeric == dataTypeId)
                {
                    return true;
                }
            }
            return false;
        }

        private static HashSet<NodeId> GetSupportedIntentTypes(IntentControllerState controller)
        {
            var result = new HashSet<NodeId>();
            ArrayOf<IntentCapabilityDataType> capabilities =
                controller.Capabilities?.SupportedIntents?.Value ?? [];
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
            var children = new List<BaseInstanceState>();
            controller.Axes!.GetChildren(null!, children);
            var indices = children.OfType<global::Opc.Ua.RobotIntent.AxisState>()
                .Select(axis => axis.Index!.Value)
                .ToHashSet();
            if (controller.Capabilities?.AxisCount?.Value != indices.Count)
            {
                return false;
            }
            for (uint ii = 0; ii < indices.Count; ii++)
            {
                if (!indices.Contains(ii))
                {
                    return false;
                }
            }
            return true;
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
    }
}
