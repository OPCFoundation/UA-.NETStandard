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
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Builders;

namespace Vision.VisualInspectionCell
{
    internal sealed partial class VisualInspectionCell
    {
        public VisualInspectionCell(
            VisualInspectionCellOptions options,
            VisualInspectionMediaProvider mediaProvider,
            VisualInspectionInferenceProvider inferenceProvider,
            VisualInspectionFeedbackSink feedbackSink,
            OperatorDialogController operatorDialog,
            ILogger<VisualInspectionCell> logger)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_mediaProvider = mediaProvider ?? throw new ArgumentNullException(nameof(mediaProvider));
            m_inferenceProvider = inferenceProvider ?? throw new ArgumentNullException(nameof(inferenceProvider));
            m_feedbackSink = feedbackSink ?? throw new ArgumentNullException(nameof(feedbackSink));
            m_operatorDialog = operatorDialog ?? throw new ArgumentNullException(nameof(operatorDialog));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async ValueTask ConfigureAsync(IVisionBuildContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.Nodes.AddFrame("FixtureFrame", frame => frame
                .WithFrameId(FixtureFrameId)
                .WithRole(VisionFrameRoleEnum.World)
                .WithTransform(new VisionPose3DDataType
                {
                    FrameId = FixtureFrameId,
                    Position = s_zeroPosition.ToArrayOf(),
                    Orientation = s_identityOrientation.ToArrayOf(),
                    Covariance = ArrayOf<double>.Empty
                }));
            AddSensor(context);
            AddPipeline(context);
            await AddOperatorDialogAsync(context, cancellationToken).ConfigureAwait(false);
            AttachRuntimeTargets(context);
            m_logger.CellConfigured(PipelineBrowseName, m_options.InferenceLocation);
        }

        private void AddSensor(IVisionBuildContext context)
        {
            context.Nodes.AddImageSensor(SensorBrowseName, sensor => sensor
                .WithSensorId("fixture-camera-01")
                .WithModality(VisionSensorModalityEnum.Area2D)
                .WithRealityKind(VisionRealityKindEnum.Simulated)
                .WithManufacturer("OPC Foundation")
                .WithModel("Fixture PNG camera")
                .WithSerialNumber("FIXTURE-CAM-0001")
                .WithDeviceUri("file://visual-inspection-cell/fixtures")
                .WithFrameId(FixtureFrameId)
                .WithResolution(VisualInspectionMediaProvider.Width, VisualInspectionMediaProvider.Height)
                .WithPixelFormat(VisualInspectionMediaProvider.PixelFormat)
                .WithIntrinsics(new VisionIntrinsicsDataType
                {
                    Fx = 10.0,
                    Fy = 10.0,
                    Cx = 400.0,
                    Cy = 300.0,
                    Skew = 0.0,
                    DistortionModel = VisionDistortionModelEnum.None,
                    DistortionCoefficients = ArrayOf<double>.Empty,
                    Width = VisualInspectionMediaProvider.Width,
                    Height = VisualInspectionMediaProvider.Height
                })
                .AddClipEndpoint(ClipEndpointBrowseName, endpoint => endpoint
                    .WithEndpointId("fixture-pngs")
                    .WithEndpointUri("opcua-inline://visual-inspection-cell/fixtures")
                    .WithClipFormat(VisionClipFormatEnum.Png)
                    .WithQuality(100u)
                    .WithResolution(VisualInspectionMediaProvider.Width, VisualInspectionMediaProvider.Height)
                    .WithInlineDelivery(enabled: true, maxInlineClipSize: 1_048_576u)
                    .WithDefaultProfileName("FixturePng"))
                .UseMediaProvider(m_mediaProvider));
        }

        private void AddPipeline(IVisionBuildContext context)
        {
            NodeId sensorNodeId = FindSensor(context)?.NodeId ?? NodeId.Null;
            (NodeId deployment, NodeId learningJob) = ResolveAiBindings(context);
            bool offServer = m_options.InferenceLocation == VisualInspectionInferenceLocation.EdgeOffServer;
            context.Nodes.AddPipeline(PipelineBrowseName, pipeline =>
            {
                pipeline.WithPipelineId(PipelineId)
                    .WithSensor(sensorNodeId)
                    .WithDeployment(deployment)
                    .WithLearningJob(learningJob)
                    .UseFeedbackSink(m_feedbackSink);
                if (offServer)
                {
                    pipeline.UseInferenceProvider(m_inferenceProvider, onServer: false);
                }
                else
                {
                    pipeline.UseInferenceProvider(m_inferenceProvider, onServer: true);
                }
            });
        }

        private void AttachRuntimeTargets(IVisionBuildContext context)
        {
            InferencePipelineState pipeline = FindPipeline(context) ?? throw new InvalidOperationException(
                "The visual-inspection pipeline was not registered.");
            ImageSensorState sensor = FindSensor(context) ?? throw new InvalidOperationException(
                "The visual-inspection sensor was not registered.");
            FolderState results = pipeline.Results ?? throw new InvalidOperationException(
                "The Vision builder did not materialise the pipeline Results folder.");
            NodeId deployment = pipeline.Deployment?.Value ?? NodeId.Null;
            NodeId learningJob = pipeline.LearningJob?.Value ?? NodeId.Null;
            var target = new VisualInspectionTarget(
                context.Manager,
                context.Context,
                context.InstanceNamespaceIndex,
                pipeline.NodeId,
                sensor.NodeId,
                deployment,
                learningJob,
                results);
            m_inferenceProvider.Attach(target);
            m_feedbackSink.Attach(target);
        }

        private async ValueTask AddOperatorDialogAsync(
            IVisionBuildContext context,
            CancellationToken cancellationToken)
        {
            var dialog = new DialogConditionState(null);
            dialog.Create(
                context.Context,
                NodeId.Null,
                new QualifiedName(OperatorDialogBrowseName, context.InstanceNamespaceIndex),
                new LocalizedText("Operator disposition dialog"),
                true);
            dialog.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
            dialog.TypeDefinitionId = global::Opc.Ua.ObjectTypeIds.DialogConditionType;
            dialog.NodeId = new NodeId(OperatorDialogBrowseName, context.InstanceNamespaceIndex);
            NodeInstanceExtensions.AssignInstanceChildNodeIds(context.Context, dialog, dialog.NodeId);
            dialog.CreateOrReplacePrompt(context.Context, null!).Value =
                LocalizedText.From(
                    "Visual inspection is not decidable. Choose the operator disposition.");
            dialog.CreateOrReplaceResponseOptionSet(context.Context, null!).Value =
            [
                LocalizedText.From("AcceptAsOk"),
                LocalizedText.From("AcceptAsNotOk"),
                LocalizedText.From("Reinspect"),
                LocalizedText.From("Stop")
            ];
            dialog.CreateOrReplaceDefaultResponse(context.Context, null!).Value = 2;
            dialog.CreateOrReplaceOkResponse(context.Context, null!).Value = 0;
            dialog.CreateOrReplaceCancelResponse(context.Context, null!).Value = 3;
            dialog.Retain!.Value = false;
            dialog.Message!.Value = LocalizedText.From(
                "Human disposition required for a not-decidable inspection.");
            dialog.Severity!.Value = (ushort)EventSeverity.MediumHigh;
            dialog.EventNotifier = EventNotifiers.SubscribeToEvents;
            context.Root.AddChild(dialog);
            await context.Manager.AddPredefinedNodeAsync(dialog, cancellationToken).ConfigureAwait(false);
            m_operatorDialog.Attach(context.Context, dialog);
            OperatorDialogNodeId = dialog.NodeId;
        }

        private static (NodeId Deployment, NodeId LearningJob) ResolveAiBindings(IVisionBuildContext context)
        {
            if (context.Manager.Server.NodeManager.AsyncNodeManagers.OfType<AiNodeManager>().FirstOrDefault() is { } ai)
            {
                return (ai.PrimaryDeploymentId, ai.LearningJobId);
            }
            return (NodeId.Null, NodeId.Null);
        }

        private static ImageSensorState? FindSensor(IVisionBuildContext context)
        {
            return FindChild<ImageSensorState>(context.Root.Sensors, context, SensorBrowseName);
        }

        private static InferencePipelineState? FindPipeline(IVisionBuildContext context)
        {
            return FindChild<InferencePipelineState>(context.Root.Pipelines, context, PipelineBrowseName);
        }

        private static T? FindChild<T>(FolderState? folder, IVisionBuildContext context, string browseName)
            where T : BaseInstanceState
        {
            if (folder == null)
            {
                return null;
            }
            var children = new List<BaseInstanceState>();
            folder.GetChildren(context.Context, children);
            var qualified = new QualifiedName(browseName, context.InstanceNamespaceIndex);
            return children.OfType<T>().FirstOrDefault(child => child.BrowseName == qualified);
        }

        public NodeId OperatorDialogNodeId { get; private set; } = NodeId.Null;

        public const string SensorBrowseName = "BracketFixtureCamera";
        public const string ClipEndpointBrowseName = "FixtureImages";
        public const string PipelineBrowseName = "BracketInspectionPipeline";
        public const string PipelineId = "pipe-bracket-inspection";
        public const string FixtureFrameId = "fixture_table";
        public const string OperatorDialogBrowseName = "OperatorDispositionDialog";

        private static readonly double[] s_zeroPosition = [0.0, 0.0, 0.0];
        private static readonly double[] s_identityOrientation = [0.0, 0.0, 0.0, 1.0];

        private readonly VisualInspectionCellOptions m_options;
        private readonly VisualInspectionMediaProvider m_mediaProvider;
        private readonly VisualInspectionInferenceProvider m_inferenceProvider;
        private readonly VisualInspectionFeedbackSink m_feedbackSink;
        private readonly OperatorDialogController m_operatorDialog;
        private readonly ILogger<VisualInspectionCell> m_logger;
    }

    internal static partial class VisualInspectionCellLog
    {
        [LoggerMessage(EventId = VisualInspectionCellEventIds.Configurator + 1,
            Level = LogLevel.Information,
            Message = "Configured visual inspection pipeline {PipelineBrowseName} with {InferenceLocation} perception.")]
        public static partial void CellConfigured(
            this ILogger<VisualInspectionCell> logger,
            string pipelineBrowseName,
            VisualInspectionInferenceLocation inferenceLocation);
    }
}
