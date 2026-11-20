/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Server;

namespace Opc.Ua.Vision.Intent.Tests.Infrastructure
{
    /// <summary>
    /// Everything the ground-truth detector and agent sink need to
    /// materialise <c>DetectionResultType</c> instances under the
    /// pipeline's <c>Results</c> folder. Populated by the test-cell
    /// configurator after the pipeline has been added.
    /// </summary>
    internal sealed class TestInferenceTarget
    {
        public TestInferenceTarget(
            AsyncCustomNodeManager nodeManager,
            ISystemContext systemContext,
            ushort instanceNamespaceIndex,
            NodeId pipelineNodeId,
            NodeId sensorNodeId,
            NodeId deploymentNodeId,
            FolderState resultsFolder,
            string cameraFrameId,
            string worldFrameId,
            double[] cameraPositionInWorld)
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
            DeploymentNodeId = deploymentNodeId.IsNull
                ? throw new ArgumentException("Deployment NodeId must not be null.", nameof(deploymentNodeId))
                : deploymentNodeId;
            ResultsFolder = resultsFolder ?? throw new ArgumentNullException(nameof(resultsFolder));
            CameraFrameId = cameraFrameId ?? throw new ArgumentNullException(nameof(cameraFrameId));
            WorldFrameId = worldFrameId ?? throw new ArgumentNullException(nameof(worldFrameId));
            if (cameraPositionInWorld == null || cameraPositionInWorld.Length != 3)
            {
                throw new ArgumentException(
                    "cameraPositionInWorld must be a 3-vector.",
                    nameof(cameraPositionInWorld));
            }
            m_cameraX = cameraPositionInWorld[0];
            m_cameraY = cameraPositionInWorld[1];
            m_cameraZ = cameraPositionInWorld[2];
        }

        public AsyncCustomNodeManager NodeManager { get; }

        public ISystemContext SystemContext { get; }

        public ushort InstanceNamespaceIndex { get; }

        public NodeId PipelineNodeId { get; }

        public NodeId SensorNodeId { get; }

        public NodeId DeploymentNodeId { get; }

        public FolderState ResultsFolder { get; }

        public string CameraFrameId { get; }

        public string WorldFrameId { get; }

        /// <summary>
        /// Translates a world-frame position into the camera frame the
        /// test cell wires. Because the cell authors identity-oriented
        /// camera and base frames (no rotations), the transform is a
        /// pure translation — this is chosen so pose composition is
        /// exact and the test can pin the residual to machine
        /// precision.
        /// </summary>
        public (double X, double Y, double Z) WorldToCamera(double x, double y, double z)
        {
            return (x - m_cameraX, y - m_cameraY, z - m_cameraZ);
        }

        private readonly double m_cameraX;
        private readonly double m_cameraY;
        private readonly double m_cameraZ;
    }
}
