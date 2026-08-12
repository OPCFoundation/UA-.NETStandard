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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.AI.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

namespace Vision.VisualInspectionCell
{
    internal sealed partial class AiNodeManagerRegistry : IServerStartupTask
    {
        public AiNodeManagerRegistry(ILogger<AiNodeManagerRegistry> logger)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public AiNodeManager? NodeManager => Volatile.Read(ref m_nodeManager);

        public ValueTask OnServerStartedAsync(
            IServerContext server, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (server is IServerInternal internalServer)
            {
                AiNodeManager? manager = internalServer.NodeManager.AsyncNodeManagers
                    .OfType<AiNodeManager>()
                    .FirstOrDefault();
                Volatile.Write(ref m_nodeManager, manager);
                if (manager != null)
                {
                    BindVisionPipeline(internalServer, manager);
                }
                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.AiManagerCaptured(manager?.LearningJobId.ToString() ?? string.Empty);
                }
            }
            return default;
        }

        private void BindVisionPipeline(IServerInternal server, AiNodeManager ai)
        {
            VisionNodeManager? vision = server.NodeManager.AsyncNodeManagers
                .OfType<VisionNodeManager>()
                .FirstOrDefault();
            InferencePipelineState? pipeline = FindPipeline(vision);
            if (pipeline == null)
            {
                return;
            }
            if (pipeline.Deployment != null)
            {
                pipeline.Deployment.Value = ai.PrimaryDeploymentId;
            }
            if (pipeline.LearningJob != null)
            {
                pipeline.LearningJob.Value = ai.LearningJobId;
            }
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

        private readonly ILogger<AiNodeManagerRegistry> m_logger;
        private AiNodeManager? m_nodeManager;
    }

    internal static partial class AiNodeManagerRegistryLog
    {
        [LoggerMessage(EventId = VisualInspectionCellEventIds.Ai + 1,
            Level = LogLevel.Information,
            Message = "Captured AI node manager; LearningJob={LearningJobNodeId}.")]
        public static partial void AiManagerCaptured(
            this ILogger<AiNodeManagerRegistry> logger,
            string learningJobNodeId);

        [LoggerMessage(EventId = VisualInspectionCellEventIds.Ai + 2,
            Level = LogLevel.Information,
            Message = "Bound Vision pipeline to Deployment={DeploymentNodeId}, LearningJob={LearningJobNodeId}.")]
        public static partial void VisionPipelineBound(
            this ILogger<AiNodeManagerRegistry> logger,
            string deploymentNodeId,
            string learningJobNodeId);
    }
}
