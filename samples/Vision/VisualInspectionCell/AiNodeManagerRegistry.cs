// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.AI.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Vision.Perception;
using Opc.Ua.Vision.Server;

namespace Vision.VisualInspectionCell
{
    internal sealed class AINodeManagerRegistry : IServerStartupTask
    {
        public AINodeManagerRegistry(ILogger<AINodeManagerRegistry> logger)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public AINodeManager? NodeManager => Volatile.Read(ref m_nodeManager);

        public ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (server is IServerInternal internalServer)
            {
                AINodeManager? manager = internalServer.NodeManager.AsyncNodeManagers
                    .OfType<AINodeManager>()
                    .FirstOrDefault();
                Volatile.Write(ref m_nodeManager, manager);
                if (manager != null)
                {
                    BindVisionPipeline(internalServer, manager);
                }
                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.AIManagerCaptured(manager?.LearningJobId.ToString() ?? string.Empty);
                }
            }
            return default;
        }

        private void BindVisionPipeline(IServerInternal server, AINodeManager ai)
        {
            VisionNodeManager? vision = server.NodeManager.AsyncNodeManagers
                .OfType<VisionNodeManager>()
                .FirstOrDefault();
            InferencePipelineState? pipeline = FindPipeline(vision);
            if (pipeline == null)
            {
                return;
            }
            pipeline.Deployment?.Value = ai.PrimaryDeploymentId;
            pipeline.LearningJob?.Value = ai.LearningJobId;
            pipeline.ClearChangeMasks(vision!.SystemContext, true);
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.VisionPipelineBound(ai.PrimaryDeploymentId.ToString(), ai.LearningJobId.ToString());
            }
        }

        private static InferencePipelineState? FindPipeline(VisionNodeManager? vision)
        {
            FolderState? pipelines = vision?.Root.Pipelines;
            if (pipelines == null || vision == null)
            {
                return null;
            }
            var children = new List<BaseInstanceState>();
            pipelines.GetChildren(vision.SystemContext, children);
            return children.OfType<InferencePipelineState>().FirstOrDefault(
                child => string.Equals(
                    child.BrowseName.Name,
                    VisualInspectionCell.PipelineBrowseName,
                    StringComparison.Ordinal));
        }

        private readonly ILogger<AINodeManagerRegistry> m_logger;
        private AINodeManager? m_nodeManager;
    }

    internal static partial class AINodeManagerRegistryLog
    {
        [LoggerMessage(
            EventId = VisualInspectionCellEventIds.AI + 1,
            Level = LogLevel.Information,
            Message = "Captured AI node manager; LearningJob={LearningJobNodeId}.")]
        public static partial void AIManagerCaptured(
            this ILogger<AINodeManagerRegistry> logger,
            string learningJobNodeId);

        [LoggerMessage(
            EventId = VisualInspectionCellEventIds.AI + 2,
            Level = LogLevel.Information,
            Message = "Bound Vision pipeline to Deployment={DeploymentNodeId}, LearningJob={LearningJobNodeId}.")]
        public static partial void VisionPipelineBound(
            this ILogger<AINodeManagerRegistry> logger,
            string deploymentNodeId,
            string learningJobNodeId);
    }
}
