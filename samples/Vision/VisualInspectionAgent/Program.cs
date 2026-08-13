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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.AI;
using Opc.Ua.AI.Client;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Configuration;
using Opc.Ua.ISA95.Client;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Vision.VisualInspectionAgent
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            VisualInspectionAgentOptions options;
            try
            {
                options = VisualInspectionAgentOptions.Parse(args);
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            if (options.Mode == VisualInspectionAgentMode.LiveAI && string.IsNullOrWhiteSpace(options.AIEndpoint))
            {
                Console.Error.WriteLine("live-ai requires --ai-endpoint and exits before creating any job.");
                return 2;
            }

            VisualInspectionAgentSession sample = await VisualInspectionAgentSession
                .ConnectAsync(options, cts.Token)
                .ConfigureAwait(false);
            await using (sample.ConfigureAwait(false))
            {
                var runner = new VisualInspectionAgentRunner(sample, options);
                try
                {
                    await runner.RunAsync(cts.Token).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    // Business refusals - an ISA-95 ReturnStatus that is not success, or a
                    // deployment that cannot serve live-ai - are expected outcomes of the demo.
                    // Report them as a diagnostic rather than a crash dump.
                    Console.Error.WriteLine(ex.Message);
                    return 3;
                }
                return 0;
            }
        }
    }

    internal sealed class VisualInspectionAgentRunner
    {
        public VisualInspectionAgentRunner(VisualInspectionAgentSession sample, VisualInspectionAgentOptions options)
        {
            m_sample = sample ?? throw new ArgumentNullException(nameof(sample));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            VisualInspectionCellContext cell = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant(
                $"Connected to {m_options.ServerUrl}; mode={m_options.Mode}; cycles={m_options.Cycles}."));
            Console.WriteLine(FormattableString.Invariant(
                $"Discovered pipeline={cell.Pipeline.PipelineNodeId}, deployment={cell.Snapshot.DeploymentId}, learningJob={cell.Snapshot.LearningJobId}."));

            await VerifyLiveAIBeforeJobsAsync(cell, cancellationToken).ConfigureAwait(false);
            for (int cycle = 1; cycle <= m_options.Cycles; cycle++)
            {
                string cycleId = StableCycleId(cycle);
                string fixture = FixtureFor(cycle);
                Console.WriteLine(FormattableString.Invariant(
                    $"[{cycleId}] State=Capture; fixture={fixture}; cycle/order/result id are correlated."));

                InspectionEvidence evidence = await CaptureAndMeasureWithFallbackAsync(
                    cell,
                    cycleId,
                    fixture,
                    cancellationToken).ConfigureAwait(false);
                InspectionDecision decision = m_policy.Judge(fixture, evidence.Measurements);
                await cell.Feedback.SubmitImageReferenceAsync(
                    VisionFeedbackPurposeEnum.Reconciliation,
                    evidence.Frame,
                    cycleId,
                    cancellationToken).ConfigureAwait(false);
                await cell.Feedback.SubmitInspectionResultAsync(
                    cycleId,
                    decision.Evaluation,
                    decision.Characteristics,
                    cancellationToken).ConfigureAwait(false);
                Console.WriteLine(FormattableString.Invariant(
                    $"[{cycleId}] Verdict={decision.Evaluation}; result submitted to Vision Feedback."));

                if (decision.Evaluation == VisionResultEvaluationEnum.NotDecidable)
                {
                    await HoldForOperatorAsync(cell, cycleId, decision, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await CompleteInspectionJobAsync(cell, cycleId, cancellationToken).ConfigureAwait(false);
                string nextOrder = decision.Evaluation == VisionResultEvaluationEnum.Ok
                    ? InspectionOrderId
                    : ReworkRejectOrderId;
                await StoreAndStartIdempotentAsync(cell.JobControl, nextOrder, cycleId, verifyRetry: true,
                    cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine(FormattableString.Invariant(
                    $"[{cycleId}] Scheduled {(decision.Evaluation == VisionResultEvaluationEnum.Ok ? "next inspection" : "rework/reject")} order {nextOrder}."));
            }
        }

        private async Task<VisualInspectionCellContext> DiscoverAsync(CancellationToken cancellationToken)
        {
            var vision = new VisionClient(m_sample.Session, m_sample.Telemetry);
            VisionNodeEntry? pipelineEntry = null;
            await foreach (VisionNodeEntry entry in vision.EnumeratePipelinesAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (string.Equals(entry.BrowseName.Name, PipelineBrowseName, StringComparison.Ordinal))
                {
                    pipelineEntry = entry;
                    break;
                }
            }
            if (pipelineEntry == null)
            {
                throw new InvalidOperationException("The visual-inspection pipeline was not found.");
            }

            VisionPipelineClient pipeline = vision.Pipeline(pipelineEntry.NodeId);
            VisionPipelineSnapshot snapshot = await pipeline.ReadAsync(cancellationToken).ConfigureAwait(false);
            VisionFeedbackClient feedback = await pipeline.OpenFeedbackAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The pipeline has no Feedback object.");
            VisionSensorClient sensor = vision.Sensor(snapshot.SensorId);
            VisionMediaClient media = await sensor.OpenMediaAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The image sensor has no Media object.");
            NodeId clipEndpoint = NodeId.Null;
            await foreach (VisionNodeEntry endpoint in media.EnumerateClipEndpointsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                clipEndpoint = endpoint.NodeId;
                break;
            }
            if (clipEndpoint.IsNull)
            {
                throw new InvalidOperationException("The image sensor has no clip endpoint.");
            }

            var ai = new AIClient(m_sample.Session, m_sample.Telemetry);
            var deployment = new AIDeploymentClient(ai, snapshot.DeploymentId);
            var isa95 = new Isa95Client(m_sample.Session, m_sample.Telemetry);
            Isa95JobControlDiscovery discovery = await isa95.DiscoverJobControlAsync(cancellationToken)
                .ConfigureAwait(false);
            NodeId receiver = FindFacet(discovery, Isa95JobControlFacet.JobOrderReceiver);
            NodeId provider = FindFacet(discovery, Isa95JobControlFacet.JobResponseProvider);
            NodeId responseReceiver = FindFacet(discovery, Isa95JobControlFacet.JobResponseReceiver);
            Isa95JobControlV2Client jobControl = isa95.CreateJobControlV2Client(receiver, provider, responseReceiver);
            NodeId dialog = await FindObjectByBrowseNameAsync(OperatorDialogBrowseName, cancellationToken)
                .ConfigureAwait(false);
            return new VisualInspectionCellContext(
                pipeline,
                snapshot,
                media,
                clipEndpoint,
                feedback,
                deployment,
                jobControl,
                dialog);
        }

        private async Task VerifyLiveAIBeforeJobsAsync(
            VisualInspectionCellContext cell,
            CancellationToken cancellationToken)
        {
            if (m_options.Mode != VisualInspectionAgentMode.LiveAI)
            {
                return;
            }
            AIDeploymentSnapshot snapshot = await cell.Deployment.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(snapshot.EndpointUri))
            {
                throw new InvalidOperationException("live-ai deployment has no endpoint URI; no jobs were created.");
            }
        }

        private async Task<InspectionEvidence> CaptureAndMeasureAsync(
            VisualInspectionCellContext cell,
            string cycleId,
            string fixture,
            CancellationToken cancellationToken)
        {
            VisionClipResult clip = await cell.Media.GetClipAsync(
                cell.ClipEndpoint,
                fixture,
                DateTimeUtc.From(DateTime.UnixEpoch),
                VisionClipFormatEnum.Png,
                requestInline: true,
                cancellationToken).ConfigureAwait(false);

            if (clip.HasInlineImage)
            {
                ArrayOf<MeasuredCharacteristic> measurements = await InvokeAIForMeasurementsAsync(
                    cell,
                    cycleId,
                    fixture,
                    clip.Image,
                    cancellationToken).ConfigureAwait(false);
                Console.WriteLine(FormattableString.Invariant(
                    $"[{cycleId}] Measurements from AI Invoke: {FormatMeasurements(measurements)}."));
                return new InspectionEvidence(clip.Image, measurements);
            }

            throw new InvalidOperationException("The cell did not return an inline fixture image.");
        }

        private async Task<InspectionEvidence> CaptureAndMeasureFromFixtureAsync(
            VisualInspectionCellContext cell,
            string cycleId,
            string fixture,
            CancellationToken cancellationToken)
        {
            VisionImageReferenceDataType frame = CreateFixtureImageReference(fixture);
            ArrayOf<MeasuredCharacteristic> measurements = await InvokeAIForMeasurementsAsync(
                cell,
                cycleId,
                fixture,
                frame,
                cancellationToken).ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant(
                $"[{cycleId}] Measurements from AI Invoke: {FormatMeasurements(measurements)}."));
            return new InspectionEvidence(frame, measurements);
        }

        private async Task<InspectionEvidence> CaptureAndMeasureWithFallbackAsync(
            VisualInspectionCellContext cell,
            string cycleId,
            string fixture,
            CancellationToken cancellationToken)
        {
            try
            {
                return await CaptureAndMeasureAsync(cell, cycleId, fixture, cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"[{cycleId}] Cell clip endpoint rejected direct fixture capture ({ex.StatusCode}); using the packaged fixture image reference and still invoking the cell AI deployment."));
                return await CaptureAndMeasureFromFixtureAsync(cell, cycleId, fixture, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private Task<ArrayOf<MeasuredCharacteristic>> InvokeAIForMeasurementsAsync(
            VisualInspectionCellContext cell,
            string cycleId,
            string fixture,
            InspectionEvidence evidence,
            CancellationToken cancellationToken)
        {
            return InvokeAIForMeasurementsAsync(
                cell,
                cycleId,
                fixture,
                evidence.Frame,
                cancellationToken);
        }

        private async Task<ArrayOf<MeasuredCharacteristic>> InvokeAIForMeasurementsAsync(
            VisualInspectionCellContext cell,
            string cycleId,
            string fixture,
            VisionImageReferenceDataType frame,
            CancellationToken cancellationToken)
        {
            if (cell.Snapshot.DeploymentId.IsNull)
            {
                throw new InvalidOperationException("The pipeline does not name an AI deployment.");
            }
            string payload = BuildAIPayload(cycleId, fixture, frame);
            for (int attempt = 1; attempt <= MaxOperationalAttempts; attempt++)
            {
                try
                {
                    AIInvokeResult result = await cell.Deployment.InvokeAsync(
                        ByteString.From(Encoding.UTF8.GetBytes(payload)),
                        "application/vnd.opcfoundation.visual-inspection.measurements+json",
                        ArrayOf<global::Opc.Ua.KeyValuePair>.Empty,
                        5000,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    Console.WriteLine(FormattableString.Invariant(
                        $"[{cycleId}] AI deployment Invoke returned {result.ResponseContentType}; evidence only."));
                    return ParseMeasurements(result.ResponsePayload);
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNotImplemented)
                {
                    AIInvokeResult result = await InvokeAIMethodByInstanceNodeAsync(
                        cell.Snapshot.DeploymentId,
                        ByteString.From(Encoding.UTF8.GetBytes(payload)),
                        "application/vnd.opcfoundation.visual-inspection.measurements+json",
                        cancellationToken).ConfigureAwait(false);
                    Console.WriteLine(FormattableString.Invariant(
                        $"[{cycleId}] AI deployment Invoke instance method returned {result.ResponseContentType}; evidence only."));
                    return ParseMeasurements(result.ResponsePayload);
                }
                catch (Exception ex) when (attempt < MaxOperationalAttempts && IsOperationalFailure(ex))
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"[{cycleId}] Operational AI/camera error attempt {attempt}: {ex.Message}; retrying."));
                }
            }

            throw new InvalidOperationException("AI Invoke failed after bounded retries; holding without a quality verdict.");
        }

        private async Task<AIInvokeResult> InvokeAIMethodByInstanceNodeAsync(
            NodeId deploymentId,
            ByteString payload,
            string contentType,
            CancellationToken cancellationToken)
        {
            NodeId methodId = await FindChildByBrowseNameAsync(
                deploymentId,
                "Invoke",
                NodeClass.Method,
                cancellationToken).ConfigureAwait(false);
            if (methodId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadMethodInvalid);
            }
            var request = new CallMethodRequest
            {
                ObjectId = deploymentId,
                MethodId = methodId,
                InputArguments =
                [
                    Variant.From(payload),
                    Variant.From(string.Empty),
                    Variant.From(contentType),
                    Variant.FromStructure(ArrayOf<global::Opc.Ua.KeyValuePair>.Empty),
                    Variant.From(5000.0)
                ]
            };
            CallResponse response = await m_sample.Session.CallAsync(
                null,
                [request],
                cancellationToken).ConfigureAwait(false);
            CallMethodResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            ArrayOf<Variant> output = result.OutputArguments;
            return new AIInvokeResult
            {
                ResponsePayload = output.Count > 0 && output[0].TryGetValue(out ByteString responsePayload)
                    ? responsePayload
                    : ByteString.Empty,
                ResponseContentType = output.Count > 1 && output[1].TryGetValue(out string responseContentType)
                    ? responseContentType
                    : string.Empty
            };
        }

        private async Task CompleteInspectionJobAsync(
            VisualInspectionCellContext cell,
            string cycleId,
            CancellationToken cancellationToken)
        {
            await EnsureInspectionOrderExecutingAsync(cell, cycleId, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant(
                $"[{cycleId}] Completing ISA-95 inspection order {InspectionOrderId} from the execution state."));
            await EnsureJobMethodSucceededAsync(
                cell.JobControl.StopAsync(InspectionOrderId, Comment(cycleId, "complete"), cancellationToken),
                "Complete inspection",
                cycleId).ConfigureAwait(false);
            await EnsureJobMethodSucceededAsync(
                cell.JobControl.ClearAsync(InspectionOrderId, Comment(cycleId, "close"), cancellationToken),
                "Close inspection",
                cycleId).ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant(
                $"[{cycleId}] Inspection job completed and closed; no ReturnStatus=0x8."));
        }

        /// <summary>
        /// Re-establishes the inspection order when an earlier run left none executing.
        /// </summary>
        /// <remarks>
        /// A run that ends on <c>NotDecidable</c> holds without scheduling the next inspection,
        /// so the order the cell seeded at start-up is already closed by the time the next run
        /// begins. Completing it then fails, which used to end the sample on an unhandled
        /// exception rather than simply carrying on.
        /// </remarks>
        private static async Task EnsureInspectionOrderExecutingAsync(
            VisualInspectionCellContext cell,
            string cycleId,
            CancellationToken cancellationToken)
        {
            ulong returnStatus = await cell.JobControl.StoreAndStartAsync(
                NewCatalogueOrder(InspectionOrderId, cycleId),
                Comment(cycleId, "ensure-executing"),
                cancellationToken).ConfigureAwait(false);
            if ((returnStatus & Isa95ReturnStatusSuccess) != 0)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"[{cycleId}] No inspection order was executing; re-established {InspectionOrderId}."));
                return;
            }
            if ((returnStatus & Isa95ReturnStatusUnableToAccept) == 0)
            {
                ThrowJobMethodFailure("Ensure inspection order", cycleId, returnStatus);
            }
        }

        private async Task StoreAndStartIdempotentAsync(
            Isa95JobControlV2Client jobControl,
            string orderId,
            string cycleId,
            bool verifyRetry,
            CancellationToken cancellationToken)
        {
            if (await OrderExistsAsync(jobControl, orderId, cancellationToken).ConfigureAwait(false))
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"[{cycleId}] Order {orderId} already exists; retry suppressed to avoid a duplicate."));
                return;
            }
            ulong returnStatus = await jobControl.StoreAndStartAsync(
                NewCatalogueOrder(orderId, cycleId),
                Comment(cycleId, "store-start"),
                cancellationToken).ConfigureAwait(false);
            if ((returnStatus & Isa95ReturnStatusSuccess) == 0)
            {
                if ((returnStatus & Isa95ReturnStatusUnableToAccept) != 0)
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"[{cycleId}] StoreAndStart ReturnStatus=0x{returnStatus:X}; stable order {orderId} already exists, so no duplicate was created."));
                    return;
                }
                ThrowJobMethodFailure("StoreAndStart", cycleId, returnStatus);
            }
            Console.WriteLine(FormattableString.Invariant(
                $"[{cycleId}] StoreAndStart ReturnStatus=0x{returnStatus:X}."));
            if (verifyRetry)
            {
                await StoreAndStartIdempotentAsync(
                    jobControl,
                    orderId,
                    FormattableString.Invariant($"{cycleId}-retry"),
                    verifyRetry: false,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<bool> OrderExistsAsync(
            Isa95JobControlV2Client jobControl,
            string orderId,
            CancellationToken cancellationToken)
        {
            try
            {
                (V2.ISA95JobResponseDataType response, ulong returnStatus) = await jobControl
                    .RequestJobResponseByJobOrderIdAsync(orderId, cancellationToken)
                    .ConfigureAwait(false);
                return (returnStatus & Isa95ReturnStatusSuccess) != 0 && response != null;
            }
            catch (ServiceResultException ex) when (StatusCode.IsUncertain(ex.StatusCode))
            {
                return false;
            }
        }

        private async Task HoldForOperatorAsync(
            VisualInspectionCellContext cell,
            string cycleId,
            InspectionDecision decision,
            CancellationToken cancellationToken)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[{cycleId}] NotDecidable: holding; no job is scheduled until the dialog is answered."));
            ulong before = await ReadSamplesCollectedAsync(cell, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant(
                $"[{cycleId}] SamplesCollected before operator correction: {before}."));
            OperatorDisposition disposition = await m_operatorPolicy.GetDispositionAsync(
                m_options.Mode,
                m_options.OperatorTimeout,
                cancellationToken).ConfigureAwait(false);
            if (disposition == OperatorDisposition.Stop)
            {
                Console.WriteLine(FormattableString.Invariant($"[{cycleId}] Operator requested stop."));
                return;
            }
            if (!cell.OperatorDialogId.IsNull)
            {
                await new AlarmClient(m_sample.Session, m_sample.Telemetry)
                    .RespondAsync(cell.OperatorDialogId, ToDialogResponse(disposition), cancellationToken)
                    .ConfigureAwait(false);
            }
            if (disposition == OperatorDisposition.Reinspect)
            {
                await StoreAndStartIdempotentAsync(
                    cell.JobControl,
                    InspectionOrderId,
                    cycleId,
                    verifyRetry: false,
                    cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            bool retractAll = disposition == OperatorDisposition.AcceptAsNotOk;
            await cell.Feedback.SubmitCorrectionAsync(
                cycleId,
                VisionFeedbackPurposeEnum.GroundTruthLabel,
                ArrayOf<VisionDetectionDataType>.Empty,
                retractAll ? ArrayOf<VisionCharacteristicDataType>.Empty : decision.Characteristics,
                LocalizedText.From(disposition.ToString()),
                ByteString.Empty,
                retractAll,
                cancellationToken).ConfigureAwait(false);
            ulong afterFirst = await ReadSamplesCollectedAsync(cell, cancellationToken).ConfigureAwait(false);
            await cell.Feedback.SubmitCorrectionAsync(
                cycleId,
                VisionFeedbackPurposeEnum.GroundTruthLabel,
                ArrayOf<VisionDetectionDataType>.Empty,
                retractAll ? ArrayOf<VisionCharacteristicDataType>.Empty : decision.Characteristics,
                LocalizedText.From(disposition.ToString()),
                ByteString.Empty,
                retractAll,
                cancellationToken).ConfigureAwait(false);
            ulong afterDuplicate = await ReadSamplesCollectedAsync(cell, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant(
                $"[{cycleId}] Operator {disposition}; SamplesCollected {before}->{afterFirst}->{afterDuplicate} after duplicate correction."));
        }

        private async Task<NodeId> FindObjectByBrowseNameAsync(
            string browseName,
            CancellationToken cancellationToken)
        {
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId>();
            pending.Enqueue(global::Opc.Ua.ObjectIds.ObjectsFolder);
            visited.Add(global::Opc.Ua.ObjectIds.ObjectsFolder);
            while (pending.Count > 0)
            {
                NodeId current = pending.Dequeue();
                (ArrayOf<ArrayOf<ReferenceDescription>> descriptions, ArrayOf<ServiceResult> errors) =
                    await m_sample.Session.ManagedBrowseAsync(
                        requestHeader: null,
                        view: null,
                        nodesToBrowse: [current],
                        maxResultsToReturn: 0,
                        browseDirection: BrowseDirection.Forward,
                        referenceTypeId: default,
                        includeSubtypes: true,
                        nodeClassMask: (uint)NodeClass.Object,
                        ct: cancellationToken).ConfigureAwait(false);
                if (errors.Count > 0 && StatusCode.IsBad(errors[0].StatusCode))
                {
                    throw new ServiceResultException(errors[0]);
                }
                if (descriptions.Count == 0)
                {
                    continue;
                }
                foreach (ReferenceDescription reference in descriptions[0])
                {
                    NodeId nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_sample.Session.NamespaceUris);
                    if (nodeId.IsNull)
                    {
                        continue;
                    }
                    if (string.Equals(reference.BrowseName.Name, browseName, StringComparison.Ordinal))
                    {
                        return nodeId;
                    }
                    if (visited.Add(nodeId))
                    {
                        pending.Enqueue(nodeId);
                    }
                }
            }
            return NodeId.Null;
        }

        private async Task<ulong> ReadSamplesCollectedAsync(
            VisualInspectionCellContext cell,
            CancellationToken cancellationToken)
        {
            if (cell.Snapshot.LearningJobId.IsNull)
            {
                return 0;
            }
            NodeId samples = await FindChildByBrowseNameAsync(
                cell.Snapshot.LearningJobId,
                "SamplesCollected",
                NodeClass.Variable,
                cancellationToken).ConfigureAwait(false);
            if (samples.IsNull)
            {
                return 0;
            }
            DataValue value = await m_sample.Session.ReadValueAsync(samples, cancellationToken).ConfigureAwait(false);
            return value.WrappedValue.TryGetValue(out ulong count) ? count : 0;
        }

        private async Task<NodeId> FindChildByBrowseNameAsync(
            NodeId parent,
            string browseName,
            NodeClass nodeClass,
            CancellationToken cancellationToken)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> descriptions, ArrayOf<ServiceResult> errors) =
                await m_sample.Session.ManagedBrowseAsync(
                    requestHeader: null,
                    view: null,
                    nodesToBrowse: [parent],
                    maxResultsToReturn: 0,
                    browseDirection: BrowseDirection.Forward,
                    referenceTypeId: default,
                    includeSubtypes: true,
                    nodeClassMask: (uint)nodeClass,
                    ct: cancellationToken).ConfigureAwait(false);
            if (errors.Count > 0 && StatusCode.IsBad(errors[0].StatusCode))
            {
                throw new ServiceResultException(errors[0]);
            }
            if (descriptions.Count == 0)
            {
                return NodeId.Null;
            }
            foreach (ReferenceDescription reference in descriptions[0])
            {
                if (string.Equals(reference.BrowseName.Name, browseName, StringComparison.Ordinal))
                {
                    return ExpandedNodeId.ToNodeId(reference.NodeId, m_sample.Session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private static NodeId FindFacet(Isa95JobControlDiscovery discovery, Isa95JobControlFacet facet)
        {
            foreach (Isa95JobControlEndpoint endpoint in discovery.V2Endpoints)
            {
                if (endpoint.Facet == facet)
                {
                    return endpoint.NodeId;
                }
            }
            throw new InvalidOperationException($"ISA-95 V2 facet {facet} was not found.");
        }

        private static async Task EnsureJobMethodSucceededAsync(
            ValueTask<ulong> operation,
            string operationName,
            string cycleId)
        {
            ulong returnStatus = await operation.ConfigureAwait(false);
            if ((returnStatus & Isa95ReturnStatusSuccess) == 0)
            {
                ThrowJobMethodFailure(operationName, cycleId, returnStatus);
            }
            Console.WriteLine(FormattableString.Invariant(
                $"[{cycleId}] {operationName} ReturnStatus=0x{returnStatus:X}."));
        }

        private static void ThrowJobMethodFailure(string operationName, string cycleId, ulong returnStatus)
        {
            throw new InvalidOperationException(FormattableString.Invariant(
                $"[{cycleId}] {operationName} returned ISA-95 ReturnStatus=0x{returnStatus:X}; business failures are not success even when the OPC UA service status is Uncertain."));
        }

        private static ArrayOf<LocalizedText> Comment(string cycleId, string action)
        {
            return new[] { LocalizedText.From(FormattableString.Invariant($"{cycleId}:{action}")) }.ToArrayOf();
        }

        private static V2.ISA95JobOrderDataType NewCatalogueOrder(string orderId, string cycleId)
        {
            return new V2.ISA95JobOrderDataType
            {
                JobOrderID = orderId,
                Priority = string.Equals(orderId, InspectionOrderId, StringComparison.Ordinal) ? (short)10 : (short)20,
                Description = new[]
                {
                    LocalizedText.From(FormattableString.Invariant($"Allowlisted catalogue order for {cycleId}."))
                }.ToArrayOf()
            };
        }

        private static string BuildAIPayload(
            string cycleId,
            string fixture,
            VisionImageReferenceDataType frame)
        {
            var builder = new StringBuilder();
            builder.Append(CultureInfo.InvariantCulture,
                $"{{\"cycleId\":\"{cycleId}\",\"fixture\":\"{fixture}\",\"image\":\"");
            builder.Append(frame.Uri);
            builder.Append("\"}");
            return builder.ToString();
        }

        private static ArrayOf<MeasuredCharacteristic> ParseMeasurements(ByteString responsePayload)
        {
            if (responsePayload.Length == 0)
            {
                throw new InvalidDataException("AI Invoke returned an empty response.");
            }
            using JsonDocument document = JsonDocument.Parse(responsePayload.ToArray());
            JsonElement root = document.RootElement;
            double confidence = ReadRequiredDouble(root, "confidence");
            if (!root.TryGetProperty("measurements", out JsonElement measurements) ||
                measurements.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("AI Invoke response did not contain a measurements array.");
            }
            var parsed = new List<MeasuredCharacteristic>();
            foreach (JsonElement measurement in measurements.EnumerateArray())
            {
                string characteristicId = ReadRequiredString(measurement, "characteristicId");
                double actual = ReadRequiredDouble(measurement, "actual");
                double uncertainty = ReadRequiredDouble(measurement, "uncertainty");
                parsed.Add(new MeasuredCharacteristic(characteristicId, actual, uncertainty, confidence));
            }
            if (parsed.Count == 0)
            {
                throw new InvalidDataException("AI Invoke response contained no measurements.");
            }
            return parsed.ToArrayOf();
        }

        private static VisionImageReferenceDataType CreateFixtureImageReference(string fixture)
        {
            string path = Path.Combine(FixtureDirectory, fixture);
            ByteString png = ByteString.From(File.ReadAllBytes(path));
            return new VisionImageReferenceDataType
            {
                Uri = FormattableString.Invariant($"opcua-inline://visual-inspection-cell/fixtures/{fixture}"),
                Digest = ByteString.From(SHA256.HashData(png.Span)),
                DigestAlgorithm = "SHA-256",
                Format = VisionClipFormatEnum.Png,
                PixelFormat = "RGB8",
                Width = 800,
                Height = 600,
                SizeBytes = (uint)png.Length,
                Timestamp = DateTimeUtc.From(DateTime.UnixEpoch)
            };
        }

        private static string FixtureDirectory
        {
            get
            {
                string output = Path.Combine(AppContext.BaseDirectory, "Fixtures");
                if (Directory.Exists(output))
                {
                    return output;
                }

                string project = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "VisualInspectionCell",
                    "Fixtures"));
                if (Directory.Exists(project))
                {
                    return project;
                }

                return Path.Combine(
                    Environment.CurrentDirectory,
                    "samples",
                    "Vision",
                    "VisualInspectionCell",
                    "Fixtures");
            }
        }

        private static string ReadRequiredString(JsonElement element, string name)
        {
            if (element.TryGetProperty(name, out JsonElement property) &&
                property.ValueKind == JsonValueKind.String &&
                property.GetString() is { Length: > 0 } value)
            {
                return value;
            }
            throw new InvalidDataException($"AI Invoke response missing string property '{name}'.");
        }

        private static double ReadRequiredDouble(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement property))
            {
                throw new InvalidDataException($"AI Invoke response missing numeric property '{name}'.");
            }
            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out double numeric))
            {
                return numeric;
            }
            if (property.ValueKind == JsonValueKind.String &&
                double.TryParse(
                    property.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double text))
            {
                return text;
            }
            throw new InvalidDataException($"AI Invoke response property '{name}' is not numeric.");
        }

        private static bool IsOperationalFailure(Exception exception)
        {
            return exception is ServiceResultException or JsonException or InvalidDataException or IOException;
        }

        private static bool IsExecutableState(string state)
        {
            return string.Equals(state, "Running", StringComparison.Ordinal) ||
                string.Equals(state, "Held", StringComparison.Ordinal) ||
                string.Equals(state, "Suspended", StringComparison.Ordinal);
        }

        private static string DecodeOrderState(ArrayOf<V2.ISA95StateDataType> states)
        {
            string top = "Unknown";
            string sub = string.Empty;
            for (int ii = 0; ii < states.Count; ii++)
            {
                V2.ISA95StateDataType state = states[ii];
                string text = state.StateText.IsNull ? string.Empty : state.StateText.Text;
                if (state.BrowsePath == null || state.BrowsePath.Elements.Count == 0)
                {
                    top = string.IsNullOrEmpty(text) ? StateNameFromTopNumber(state.StateNumber) : text;
                }
                else
                {
                    sub = string.IsNullOrEmpty(text) ? StateNameFromSubNumber(state.StateNumber) : text;
                }
            }
            return string.IsNullOrEmpty(sub) ? top : sub;
        }

        private async Task<string> ReadOrderStateAsync(
            Isa95JobControlV2Client jobControl,
            string orderId,
            CancellationToken cancellationToken)
        {
            try
            {
                (V2.ISA95JobResponseDataType response, ulong returnStatus) = await jobControl
                    .RequestJobResponseByJobOrderIdAsync(orderId, cancellationToken)
                    .ConfigureAwait(false);
                if ((returnStatus & Isa95ReturnStatusSuccess) == 0 || response == null)
                {
                    return FormattableString.Invariant($"Unknown(ReturnStatus=0x{returnStatus:X})");
                }
                return DecodeOrderState(response.JobState);
            }
            catch (ServiceResultException ex) when (StatusCode.IsUncertain(ex.StatusCode))
            {
                return FormattableString.Invariant($"Unknown({ex.StatusCode})");
            }
        }

        private static string StateNameFromTopNumber(uint number)
        {
            return number switch
            {
                1 => "NotAllowedToStart",
                2 => "AllowedToStart",
                3 => "Running",
                4 => "Interrupted",
                5 => "Ended",
                6 => "Aborted",
                _ => "Unknown"
            };
        }

        private static string StateNameFromSubNumber(uint number)
        {
            return number switch
            {
                1 => "Completed",
                2 => "Closed",
                _ => "Unknown"
            };
        }

        private static string FormatMeasurements(ArrayOf<MeasuredCharacteristic> measurements)
        {
            var formatted = new List<string>(measurements.Count);
            for (int ii = 0; ii < measurements.Count; ii++)
            {
                MeasuredCharacteristic measurement = measurements[ii];
                formatted.Add(FormattableString.Invariant(
                    $"{measurement.CharacteristicId}={measurement.Actual:0.00}mm confidence={measurement.Confidence:0.00}"));
            }
            return string.Join(", ", formatted);
        }

        private static string StableCycleId(int cycle)
        {
            return FormattableString.Invariant($"vis-agent-cycle-{cycle:000}");
        }

        private static string FixtureFor(int cycle)
        {
            return ((cycle - 1) % 3) switch
            {
                0 => "bracket-ok.png",
                1 => "bracket-not-ok.png",
                _ => "bracket-ambiguous.png"
            };
        }

        private static int ToDialogResponse(OperatorDisposition disposition)
        {
            return disposition switch
            {
                OperatorDisposition.AcceptAsOk => 0,
                OperatorDisposition.AcceptAsNotOk => 1,
                OperatorDisposition.Reinspect => 2,
                _ => 3
            };
        }

        private const string PipelineBrowseName = "BracketInspectionPipeline";
        private const string OperatorDialogBrowseName = "OperatorDispositionDialog";
        private const string InspectionOrderId = "VIS-INSP-BRACKET-001";
        private const string ReworkRejectOrderId = "VIS-REWORK-REJECT-001";
        private const ulong Isa95ReturnStatusSuccess = 1UL;
        private const ulong Isa95ReturnStatusUnableToAccept = 0x10UL;
        private const int MaxOperationalAttempts = 2;

        private readonly InspectionVerdictPolicy m_policy = new();
        private readonly ScriptedOperatorPolicy m_operatorPolicy = new();
        private readonly VisualInspectionAgentOptions m_options;
        private readonly VisualInspectionAgentSession m_sample;
    }

    internal sealed record VisualInspectionCellContext(
        VisionPipelineClient Pipeline,
        VisionPipelineSnapshot Snapshot,
        VisionMediaClient Media,
        NodeId ClipEndpoint,
        VisionFeedbackClient Feedback,
        AIDeploymentClient Deployment,
        Isa95JobControlV2Client JobControl,
        NodeId OperatorDialogId);

    internal sealed record InspectionEvidence(
        VisionImageReferenceDataType Frame,
        ArrayOf<MeasuredCharacteristic> Measurements);
}
