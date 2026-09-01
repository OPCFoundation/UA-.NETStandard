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

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Vision;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Wires the generated Vision method delegates to the injected
    /// providers, applies specification-mandated status codes, and
    /// records failures via the source-generated logger.
    /// </summary>
    internal sealed class VisionMethodDispatcher
    {
        public VisionMethodDispatcher(VisionRegistry registry, ILogger logger)
        {
            m_registry = registry;
            m_logger = logger;
        }

        public void AttachMediaMethods(NodeId sensorNodeId, VisionMediaManagementState media)
        {
            if (media.GetStreamEndpoint != null)
            {
                media.GetStreamEndpoint.OnCallAsync = CreateGetStreamEndpointHandler(sensorNodeId);
            }
            if (media.ReleaseStreamEndpoint != null)
            {
                media.ReleaseStreamEndpoint.OnCallAsync = CreateReleaseStreamEndpointHandler(sensorNodeId);
            }
            if (media.ConfigureStreamEndpoint != null)
            {
                media.ConfigureStreamEndpoint.OnCallAsync = CreateConfigureStreamEndpointHandler(sensorNodeId);
            }
            if (media.SelectEndpoint != null)
            {
                media.SelectEndpoint.OnCallAsync = CreateSelectEndpointHandler(sensorNodeId);
            }
            if (media.GetClip != null)
            {
                media.GetClip.OnCallAsync = CreateGetClipHandler(sensorNodeId);
            }
        }

        public void AttachPipelineMethods(NodeId pipelineNodeId, InferencePipelineState pipeline)
        {
            if (pipeline.RunInference != null)
            {
                pipeline.RunInference.OnCallAsync = CreateRunInferenceHandler(pipelineNodeId);
            }
            if (pipeline.StartContinuous != null)
            {
                pipeline.StartContinuous.OnCallMethod2Async = CreateStartContinuousHandler(pipelineNodeId);
            }
            if (pipeline.Stop != null)
            {
                pipeline.Stop.OnCallMethod2Async = CreateStopHandler(pipelineNodeId);
            }
        }

        public void AttachFeedbackMethods(NodeId pipelineNodeId, VisionFeedbackState feedback)
        {
            if (feedback.SubmitDetections != null)
            {
                feedback.SubmitDetections.OnCallAsync = CreateSubmitDetectionsHandler(pipelineNodeId);
            }
            if (feedback.SubmitInspectionResult != null)
            {
                feedback.SubmitInspectionResult.OnCallAsync = CreateSubmitInspectionHandler(pipelineNodeId);
            }
            if (feedback.SubmitCorrection != null)
            {
                feedback.SubmitCorrection.OnCallAsync = CreateSubmitCorrectionHandler(pipelineNodeId);
            }
            if (feedback.SubmitImageReference != null)
            {
                feedback.SubmitImageReference.OnCallAsync = CreateSubmitImageReferenceHandler(pipelineNodeId);
            }
        }

        private GetStreamEndpointMethodStateMethodAsyncCallHandler CreateGetStreamEndpointHandler(NodeId sensorNodeId)
        {
            return (context, method, objectId, endpoint, profileName, protocol, ct) =>
                DispatchGetStreamEndpointAsync(sensorNodeId, endpoint, profileName, protocol, ct);
        }

        private async ValueTask<GetStreamEndpointMethodStateResult> DispatchGetStreamEndpointAsync(
            NodeId sensorNodeId,
            NodeId endpoint,
            string profileName,
            VisionStreamProtocolEnum protocol,
            CancellationToken cancellationToken)
        {
            IVisionMediaProvider? provider = ResolveMediaProvider(sensorNodeId);
            if (provider == null)
            {
                m_logger.MediaProviderMissing(sensorNodeId);
                return new GetStreamEndpointMethodStateResult
                {
                    ServiceResult = StatusCodes.BadNotSupported
                };
            }
            try
            {
                VisionStreamLease lease = await provider.GetStreamAsync(
                    new VisionStreamRequest(endpoint, profileName, protocol),
                    cancellationToken).ConfigureAwait(false);
                return new GetStreamEndpointMethodStateResult
                {
                    ServiceResult = lease.ServiceResult,
                    Session = lease.Session,
                    EndpointOut = lease.EndpointOut
                };
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Do not catch general exception types.
            catch (System.Exception ex)
#pragma warning restore CA1031
            {
                m_logger.MethodFailed("GetStreamEndpoint", ex);
                return new GetStreamEndpointMethodStateResult
                {
                    ServiceResult = StatusCodes.BadInternalError
                };
            }
        }

        private ReleaseStreamEndpointMethodStateMethodAsyncCallHandler CreateReleaseStreamEndpointHandler(NodeId sensorNodeId)
        {
            return async (context, method, objectId, sessionToken, ct) =>
            {
                IVisionMediaProvider? provider = ResolveMediaProvider(sensorNodeId);
                if (provider == null)
                {
                    m_logger.MediaProviderMissing(sensorNodeId);
                    return new ReleaseStreamEndpointMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotSupported
                    };
                }
                try
                {
                    ServiceResult result = await provider.ReleaseStreamAsync(sessionToken, ct).ConfigureAwait(false);
                    return new ReleaseStreamEndpointMethodStateResult { ServiceResult = result };
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Do not catch general exception types.
                catch (System.Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.MethodFailed("ReleaseStreamEndpoint", ex);
                    return new ReleaseStreamEndpointMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInternalError
                    };
                }
            };
        }

        private ConfigureStreamEndpointMethodStateMethodAsyncCallHandler CreateConfigureStreamEndpointHandler(NodeId sensorNodeId)
        {
            return async (context, method, objectId, endpoint, codec, width, height, frameRate, bitrate, ct) =>
            {
                IVisionMediaProvider? provider = ResolveMediaProvider(sensorNodeId);
                if (provider == null)
                {
                    m_logger.MediaProviderMissing(sensorNodeId);
                    return new ConfigureStreamEndpointMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotSupported
                    };
                }
                try
                {
                    ServiceResult result = await provider.ConfigureStreamAsync(
                        new VisionStreamConfigurationRequest(endpoint, codec, width, height, frameRate, bitrate),
                        ct).ConfigureAwait(false);
                    return new ConfigureStreamEndpointMethodStateResult { ServiceResult = result };
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Do not catch general exception types.
                catch (System.Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.MethodFailed("ConfigureStreamEndpoint", ex);
                    return new ConfigureStreamEndpointMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInternalError
                    };
                }
            };
        }

        private SelectEndpointMethodStateMethodAsyncCallHandler CreateSelectEndpointHandler(NodeId sensorNodeId)
        {
            return async (context, method, objectId, streamEndpoint, clipEndpoint, ct) =>
            {
                IVisionMediaProvider? provider = ResolveMediaProvider(sensorNodeId);
                if (provider == null)
                {
                    m_logger.MediaProviderMissing(sensorNodeId);
                    return new SelectEndpointMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotSupported
                    };
                }
                try
                {
                    ServiceResult result = await provider.SelectEndpointAsync(streamEndpoint, clipEndpoint, ct)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsGood(result) &&
                        m_registry.TryGetSensor(sensorNodeId, out SensorRegistration? sensor) &&
                        sensor?.Sensor.Media is VisionMediaManagementState media)
                    {
                        if (media.PreferredStreamEndpoint != null && !streamEndpoint.IsNull)
                        {
                            media.PreferredStreamEndpoint.Value = streamEndpoint;
                            media.PreferredStreamEndpoint.ClearChangeMasks(context, false);
                        }
                        if (media.PreferredClipEndpoint != null && !clipEndpoint.IsNull)
                        {
                            media.PreferredClipEndpoint.Value = clipEndpoint;
                            media.PreferredClipEndpoint.ClearChangeMasks(context, false);
                        }
                    }
                    return new SelectEndpointMethodStateResult { ServiceResult = result };
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Do not catch general exception types.
                catch (System.Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.MethodFailed("SelectEndpoint", ex);
                    return new SelectEndpointMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInternalError
                    };
                }
            };
        }

        private GetClipMethodStateMethodAsyncCallHandler CreateGetClipHandler(NodeId sensorNodeId)
        {
            return async (context, method, objectId, endpoint, resultId, timestamp, format, requestInline, ct) =>
            {
                IVisionMediaProvider? provider = ResolveMediaProvider(sensorNodeId);
                if (provider == null)
                {
                    m_logger.MediaProviderMissing(sensorNodeId);
                    return new GetClipMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotSupported
                    };
                }
                ClipEndpointState? clipEndpoint = FindClipEndpoint(sensorNodeId, endpoint);
                if (clipEndpoint != null && requestInline && !IsInlineDeliveryEnabled(clipEndpoint))
                {
                    return new GetClipMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotSupported
                    };
                }
                try
                {
                    VisionClipResult clipResult = await provider.GetClipAsync(
                        new VisionClipRequest(endpoint, resultId, timestamp, format, requestInline),
                        ct).ConfigureAwait(false);
                    GetClipMethodStateResult result = EnforceInlineLimit(clipEndpoint, clipResult);
                    PublishLatestClip(context, clipEndpoint, result, timestamp);
                    return result;
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Do not catch general exception types.
                catch (System.Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.MethodFailed("GetClip", ex);
                    return new GetClipMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInternalError
                    };
                }
            };
        }

        private RunInferenceMethodStateMethodAsyncCallHandler CreateRunInferenceHandler(NodeId pipelineNodeId)
        {
            return async (context, method, objectId, timestamp, ct) =>
            {
                if (!m_registry.TryGetPipeline(pipelineNodeId, out PipelineRegistration? pipeline) ||
                    pipeline == null)
                {
                    return new RunInferenceMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNodeIdUnknown
                    };
                }
                IVisionInferenceProvider? provider = pipeline.InferenceProvider;
                if (provider == null)
                {
                    m_logger.InferenceProviderMissing(pipelineNodeId);
                    return new RunInferenceMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotSupported
                    };
                }
                NodeId sensorNodeId = ReadPipelineSensor(pipeline.Pipeline);
                NodeId deploymentNodeId = ReadPipelineDeployment(pipeline.Pipeline);
                try
                {
                    VisionInferenceRunResult result = await provider.RunInferenceAsync(
                        new VisionInferenceRunRequest(pipelineNodeId, sensorNodeId, deploymentNodeId, timestamp),
                        ct).ConfigureAwait(false);
                    return new RunInferenceMethodStateResult
                    {
                        ServiceResult = result.ServiceResult,
                        ResultId = result.ResultId ?? string.Empty
                    };
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Do not catch general exception types.
                catch (System.Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.MethodFailed("RunInference", ex);
                    return new RunInferenceMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInternalError
                    };
                }
            };
        }

        private GenericMethodCalledEventHandler2Async CreateStartContinuousHandler(NodeId pipelineNodeId)
        {
            return async (context, method, objectId, inputArguments, outputArguments, ct) =>
            {
                if (!m_registry.TryGetPipeline(pipelineNodeId, out PipelineRegistration? pipeline) ||
                    pipeline == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }
                IVisionInferenceProvider? provider = pipeline.InferenceProvider;
                if (provider == null)
                {
                    m_logger.InferenceProviderMissing(pipelineNodeId);
                    return StatusCodes.BadNotSupported;
                }
                try
                {
                    return await provider.StartContinuousAsync(pipelineNodeId, ct).ConfigureAwait(false);
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Do not catch general exception types.
                catch (System.Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.MethodFailed("StartContinuous", ex);
                    return StatusCodes.BadInternalError;
                }
            };
        }

        private GenericMethodCalledEventHandler2Async CreateStopHandler(NodeId pipelineNodeId)
        {
            return async (context, method, objectId, inputArguments, outputArguments, ct) =>
            {
                if (!m_registry.TryGetPipeline(pipelineNodeId, out PipelineRegistration? pipeline) ||
                    pipeline == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }
                IVisionInferenceProvider? provider = pipeline.InferenceProvider;
                if (provider == null)
                {
                    m_logger.InferenceProviderMissing(pipelineNodeId);
                    return StatusCodes.BadNotSupported;
                }
                try
                {
                    return await provider.StopAsync(pipelineNodeId, ct).ConfigureAwait(false);
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Do not catch general exception types.
                catch (System.Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.MethodFailed("Stop", ex);
                    return StatusCodes.BadInternalError;
                }
            };
        }

        private SubmitDetectionsMethodStateMethodAsyncCallHandler CreateSubmitDetectionsHandler(NodeId pipelineNodeId)
        {
            return async (context, method, objectId, purpose, detections, frameRef, inlineImage, sceneIsEmpty, ct) =>
            {
                IVisionFeedbackSink? sink = ResolveFeedbackSink(pipelineNodeId);
                if (sink == null)
                {
                    m_logger.FeedbackSinkMissing(pipelineNodeId);
                    return new SubmitDetectionsMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotSupported
                    };
                }
                if (detections.Count == 0 != sceneIsEmpty)
                {
                    // Part 9.5. An empty Detections is accepted only when SceneIsEmpty
                    // says the frame was examined and found to contain nothing, and is
                    // refused otherwise - the flag is what tells a deliberate empty
                    // observation from a lost payload. The converse is equally
                    // inconsistent: SceneIsEmpty with detections attached asserts two
                    // contradictory things about the same frame.
                    return new SubmitDetectionsMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    };
                }
                try
                {
                    ServiceResult result = await sink.SubmitDetectionsAsync(
                        new VisionSubmitDetectionsRequest(
                            pipelineNodeId,
                            purpose,
                            detections,
                            frameRef,
                            inlineImage,
                            sceneIsEmpty),
                        ct).ConfigureAwait(false);
                    if (ServiceResult.IsGood(result))
                    {
                        m_logger.FeedbackAccepted("SubmitDetections", pipelineNodeId);
                    }
                    return new SubmitDetectionsMethodStateResult { ServiceResult = result };
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Do not catch general exception types.
                catch (System.Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.MethodFailed("SubmitDetections", ex);
                    return new SubmitDetectionsMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInternalError
                    };
                }
            };
        }

        private SubmitInspectionResultMethodStateMethodAsyncCallHandler CreateSubmitInspectionHandler(NodeId pipelineNodeId)
        {
            return async (context, method, objectId, resultId, evaluation, characteristics, ct) =>
            {
                IVisionFeedbackSink? sink = ResolveFeedbackSink(pipelineNodeId);
                if (sink == null)
                {
                    m_logger.FeedbackSinkMissing(pipelineNodeId);
                    return new SubmitInspectionResultMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotSupported
                    };
                }
                try
                {
                    ServiceResult result = await sink.SubmitInspectionResultAsync(
                        new VisionSubmitInspectionResultRequest(
                            pipelineNodeId,
                            resultId ?? string.Empty,
                            evaluation,
                            characteristics),
                        ct).ConfigureAwait(false);
                    if (ServiceResult.IsGood(result))
                    {
                        m_logger.FeedbackAccepted("SubmitInspectionResult", pipelineNodeId);
                    }
                    return new SubmitInspectionResultMethodStateResult { ServiceResult = result };
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Do not catch general exception types.
                catch (System.Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.MethodFailed("SubmitInspectionResult", ex);
                    return new SubmitInspectionResultMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInternalError
                    };
                }
            };
        }

        private SubmitCorrectionMethodStateMethodAsyncCallHandler CreateSubmitCorrectionHandler(NodeId pipelineNodeId)
        {
            return async (context, method, objectId, resultId, purpose, detections, characteristics, reason, inlineImage, retractAll, ct) =>
            {
                IVisionFeedbackSink? sink = ResolveFeedbackSink(pipelineNodeId);
                if (sink == null)
                {
                    m_logger.FeedbackSinkMissing(pipelineNodeId);
                    return new SubmitCorrectionMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotSupported
                    };
                }
                if (string.IsNullOrEmpty(resultId))
                {
                    // ResultId identifies the result being corrected. Forwarding an empty one
                    // would ask the sink to correct an unnamed result, so refuse instead of
                    // quietly substituting a value the caller never supplied.
                    return new SubmitCorrectionMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    };
                }
                bool hasDetections = detections.Count > 0;
                bool hasCharacteristics = characteristics.Count > 0;
                if (retractAll)
                {
                    // Part 9.5. RetractAll asserts the referenced result should contain
                    // nothing at all, so carrying a replacement contradicts it.
                    if (hasDetections || hasCharacteristics)
                    {
                        return new SubmitCorrectionMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadInvalidArgument
                        };
                    }
                }
                else if (hasDetections == hasCharacteristics)
                {
                    // Part 9.5 relaxed this to AT MOST one non-empty: both populated is
                    // still contradictory, and neither is only meaningful with RetractAll,
                    // which is the branch above.
                    return new SubmitCorrectionMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    };
                }
                try
                {
                    ServiceResult result = await sink.SubmitCorrectionAsync(
                        new VisionSubmitCorrectionRequest(
                            pipelineNodeId,
                            resultId,
                            purpose,
                            detections,
                            characteristics,
                            reason,
                            inlineImage,
                            retractAll),
                        ct).ConfigureAwait(false);
                    if (ServiceResult.IsGood(result))
                    {
                        m_logger.FeedbackAccepted("SubmitCorrection", pipelineNodeId);
                    }
                    return new SubmitCorrectionMethodStateResult { ServiceResult = result };
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Do not catch general exception types.
                catch (System.Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.MethodFailed("SubmitCorrection", ex);
                    return new SubmitCorrectionMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInternalError
                    };
                }
            };
        }

        private SubmitImageReferenceMethodStateMethodAsyncCallHandler CreateSubmitImageReferenceHandler(NodeId pipelineNodeId)
        {
            return async (context, method, objectId, purpose, image, resultId, ct) =>
            {
                IVisionFeedbackSink? sink = ResolveFeedbackSink(pipelineNodeId);
                if (sink == null)
                {
                    m_logger.FeedbackSinkMissing(pipelineNodeId);
                    return new SubmitImageReferenceMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotSupported
                    };
                }
                try
                {
                    ServiceResult result = await sink.SubmitImageReferenceAsync(
                        new VisionSubmitImageReferenceRequest(
                            pipelineNodeId,
                            purpose,
                            image,
                            resultId ?? string.Empty),
                        ct).ConfigureAwait(false);
                    if (ServiceResult.IsGood(result))
                    {
                        m_logger.FeedbackAccepted("SubmitImageReference", pipelineNodeId);
                    }
                    return new SubmitImageReferenceMethodStateResult { ServiceResult = result };
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Do not catch general exception types.
                catch (System.Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.MethodFailed("SubmitImageReference", ex);
                    return new SubmitImageReferenceMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInternalError
                    };
                }
            };
        }

        private IVisionMediaProvider? ResolveMediaProvider(NodeId sensorNodeId)
        {
            return m_registry.TryGetSensor(sensorNodeId, out SensorRegistration? sensor)
                ? sensor?.MediaProvider
                : null;
        }

        private IVisionFeedbackSink? ResolveFeedbackSink(NodeId pipelineNodeId)
        {
            return m_registry.TryGetPipeline(pipelineNodeId, out PipelineRegistration? pipeline)
                ? pipeline?.FeedbackSink
                : null;
        }

        private ClipEndpointState? FindClipEndpoint(NodeId sensorNodeId, NodeId endpointNodeId)
        {
            if (endpointNodeId.IsNull ||
                !m_registry.TryGetSensor(sensorNodeId, out SensorRegistration? sensor) ||
                sensor == null)
            {
                return null;
            }
            for (int ii = 0; ii < sensor.ClipEndpoints.Count; ii++)
            {
                ClipEndpointState clip = sensor.ClipEndpoints[ii];
                if (clip.NodeId == endpointNodeId)
                {
                    return clip;
                }
            }
            return null;
        }

        private static bool IsInlineDeliveryEnabled(ClipEndpointState clip)
        {
            return clip.InlineDeliveryEnabled?.Value == true;
        }

        /// <summary>
        /// Publishes a clip that <c>GetClip</c> just produced onto the endpoint's
        /// <c>LatestClip</c> and <c>LatestClipMetadata</c> variables.
        /// </summary>
        /// <remarks>
        /// Without this the two variables are created by the builder and then never
        /// written, so <c>LatestClip</c> reports <c>Bad_NoDataAvailable</c> for the life of
        /// the Server and a consumer that follows the model - read the published frame
        /// first, call the method only if there is none - never gets a frame at all. A
        /// clip the Server has just encoded is by definition the latest one, so the
        /// dispatcher publishes it here rather than leaving every provider to remember to.
        /// </remarks>
        private static void PublishLatestClip(
            ISystemContext context,
            ClipEndpointState? clip,
            GetClipMethodStateResult result,
            DateTimeUtc timestamp)
        {
            if (clip == null || !IsInlineDeliveryEnabled(clip) || ServiceResult.IsBad(result.ServiceResult))
            {
                return;
            }
            ByteString inline = result.InlineImage;
            if (inline.IsNull || inline.IsEmpty)
            {
                return;
            }
            DateTimeUtc sourceTimestamp = timestamp;
            if (clip.LatestClip != null)
            {
                clip.LatestClip.Value = inline;
                clip.LatestClip.StatusCode = StatusCodes.Good;
                clip.LatestClip.Timestamp = sourceTimestamp;
                clip.LatestClip.ClearChangeMasks(context, false);
            }
            if (clip.LatestClipMetadata != null)
            {
                clip.LatestClipMetadata.Value = result.Image;
                clip.LatestClipMetadata.StatusCode = StatusCodes.Good;
                clip.LatestClipMetadata.Timestamp = sourceTimestamp;
                clip.LatestClipMetadata.ClearChangeMasks(context, false);
            }
        }

        private static GetClipMethodStateResult EnforceInlineLimit(
            ClipEndpointState? clip,
            VisionClipResult providerResult)
        {
            ByteString inline = providerResult.InlineImage;
            if (!inline.IsNull &&
                !inline.IsEmpty &&
                clip?.MaxInlineClipSize is PropertyState<uint> limit &&
                limit.Value > 0u &&
                inline.Length > (int)limit.Value)
            {
                return new GetClipMethodStateResult
                {
                    ServiceResult = StatusCodes.BadEncodingLimitsExceeded,
                    Image = providerResult.Image,
                    EndpointOut = providerResult.EndpointOut,
                    InlineImage = default
                };
            }
            return new GetClipMethodStateResult
            {
                ServiceResult = providerResult.ServiceResult,
                Image = providerResult.Image,
                EndpointOut = providerResult.EndpointOut,
                InlineImage = inline
            };
        }

        private static NodeId ReadPipelineSensor(InferencePipelineState pipeline)
        {
            NodeId value = pipeline.Sensor?.Value ?? NodeId.Null;
            return value.IsNull ? NodeId.Null : value;
        }

        private static NodeId ReadPipelineDeployment(InferencePipelineState pipeline)
        {
            NodeId value = pipeline.Deployment?.Value ?? NodeId.Null;
            return value.IsNull ? NodeId.Null : value;
        }

        private readonly VisionRegistry m_registry;
        private readonly ILogger m_logger;
    }
}
