// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Annex B helpers for binding OPC 40010 topology to Robot Intent controllers.
    /// </summary>
    public static class IntentRoboticsInteropExtensions
    {
        /// <summary>
        /// Adds HasIntentController and IntentControllerOf between a MotionDeviceSystem and an intent controller.
        /// </summary>
        public static IMotionDeviceSystemBuilder HasIntentController(
            this IMotionDeviceSystemBuilder motionDeviceSystem,
            IIntentControllerBuilder intentController)
        {
            if (motionDeviceSystem == null)
            {
                throw new System.ArgumentNullException(nameof(motionDeviceSystem));
            }
            if (intentController == null)
            {
                throw new System.ArgumentNullException(nameof(intentController));
            }
            NamespaceTable namespaceUris = motionDeviceSystem.BuildContext.Context.NamespaceUris;
            var referenceTypeId = NodeId.Create(
                global::Opc.Ua.RobotIntent.ReferenceTypes.HasIntentController,
                global::Opc.Ua.RobotIntent.Namespaces.RobotIntent,
                namespaceUris);
            if (!motionDeviceSystem.State.ReferenceExists(referenceTypeId, false, intentController.State.NodeId))
            {
                motionDeviceSystem.State.AddReference(referenceTypeId, false, intentController.State.NodeId);
                intentController.State.AddReference(referenceTypeId, true, motionDeviceSystem.State.NodeId);
            }
            IntentControllerFacetMetadata.MarkInterop40010Binding(
                intentController.State,
                motionDeviceSystem.State);
            BindOperationalMode(intentController.State, motionDeviceSystem.State);
            return motionDeviceSystem;
        }

        private static void BindOperationalMode(
            IntentControllerState intentController,
            MotionDeviceSystemState motionDeviceSystem)
        {
            BaseVariableState? source = FindOperationalMode(motionDeviceSystem);
            if (source == null || intentController.OperationalMode == null)
            {
                return;
            }
            OperationalModeEnum mode = ToIntentOperationalMode(source);
            intentController.OperationalMode.Value = mode;
            if (intentController.OperationalMode.OnReadValueAsync == null &&
                intentController.OperationalMode.OnSimpleReadValueAsync == null)
            {
                RoboticsBuilderUtilities.BindRead(
                    intentController.OperationalMode,
                    _ => new ValueTask<DataValue>(new DataValue((int)ToIntentOperationalMode(source))));
            }
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

        private static TChild? FindChild<TChild>(NodeState parent, string browseName)
            where TChild : BaseInstanceState
        {
            var children = new System.Collections.Generic.List<BaseInstanceState>();
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

        private static System.Collections.Generic.List<T> GetDescendants<T>(NodeState root)
            where T : BaseInstanceState
        {
            var matches = new System.Collections.Generic.List<T>();
            AddDescendants(root, matches);
            return matches;
        }

        private static void AddDescendants<T>(
            NodeState node,
            System.Collections.Generic.List<T> matches)
            where T : BaseInstanceState
        {
            var children = new System.Collections.Generic.List<BaseInstanceState>();
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
