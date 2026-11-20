// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Vision.Server
{
    internal static partial class VisionMethodDispatcherLog
    {
        [LoggerMessage(
            EventId = VisionServerEventIds.NodeManagerReady,
            Level = LogLevel.Information,
            Message = "Vision node manager loaded the Vision model.")]
        public static partial void NodeManagerReady(this ILogger logger);

        [LoggerMessage(
            EventId = VisionServerEventIds.MediaProviderMissing,
            Level = LogLevel.Warning,
            Message = "Vision media provider is not wired for sensor {SensorNodeId}.")]
        public static partial void MediaProviderMissing(this ILogger logger, NodeId sensorNodeId);

        [LoggerMessage(
            EventId = VisionServerEventIds.InferenceProviderMissing,
            Level = LogLevel.Warning,
            Message = "Vision inference provider is not wired for pipeline {PipelineNodeId}.")]
        public static partial void InferenceProviderMissing(this ILogger logger, NodeId pipelineNodeId);

        [LoggerMessage(
            EventId = VisionServerEventIds.FeedbackSinkMissing,
            Level = LogLevel.Warning,
            Message = "Vision feedback sink is not wired for pipeline {PipelineNodeId}.")]
        public static partial void FeedbackSinkMissing(this ILogger logger, NodeId pipelineNodeId);

        [LoggerMessage(
            EventId = VisionServerEventIds.MethodFailed,
            Level = LogLevel.Warning,
            Message = "Vision method {Method} failed.")]
        public static partial void MethodFailed(this ILogger logger, string method, Exception exception);

        [LoggerMessage(
            EventId = VisionServerEventIds.FeedbackAccepted,
            Level = LogLevel.Information,
            Message = "Vision feedback method {Method} accepted for pipeline {PipelineNodeId}.")]
        public static partial void FeedbackAccepted(this ILogger logger, string method, NodeId pipelineNodeId);
    }
}
