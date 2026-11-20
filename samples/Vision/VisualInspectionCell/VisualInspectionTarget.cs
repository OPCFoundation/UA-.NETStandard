/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua;
using Opc.Ua.Server;

namespace Vision.VisualInspectionCell
{
    internal sealed class VisualInspectionTarget
    {
        public VisualInspectionTarget(
            AsyncCustomNodeManager nodeManager,
            ISystemContext systemContext,
            ushort instanceNamespaceIndex,
            NodeId pipelineNodeId,
            NodeId sensorNodeId,
            NodeId deploymentNodeId,
            NodeId learningJobNodeId,
            FolderState resultsFolder)
        {
            NodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
            SystemContext = systemContext ?? throw new ArgumentNullException(nameof(systemContext));
            InstanceNamespaceIndex = instanceNamespaceIndex;
            PipelineNodeId = pipelineNodeId.IsNull
                ? throw new ArgumentException("Pipeline NodeId must not be null.", nameof(pipelineNodeId))
                : pipelineNodeId;
            SensorNodeId = sensorNodeId.IsNull
                ? throw new ArgumentException("Sensor NodeId must not be null.", nameof(sensorNodeId))
                : sensorNodeId;
            DeploymentNodeId = deploymentNodeId;
            LearningJobNodeId = learningJobNodeId;
            ResultsFolder = resultsFolder ?? throw new ArgumentNullException(nameof(resultsFolder));
        }

        public AsyncCustomNodeManager NodeManager { get; }

        public ISystemContext SystemContext { get; }

        public ushort InstanceNamespaceIndex { get; }

        public NodeId PipelineNodeId { get; }

        public NodeId SensorNodeId { get; }

        public NodeId DeploymentNodeId { get; }

        public NodeId LearningJobNodeId { get; }

        public FolderState ResultsFolder { get; }
    }
}
