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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed class VisionWorkflows
    {
        public VisionWorkflows(IVisionExecutionPolicy? executionPolicy = null, TimeProvider? timeProvider = null)
        {
            m_executionPolicy = executionPolicy;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async ValueTask<CompanionTaskInput> PrepareAsync(
            CompanionContext context, CompanionTarget target, string operationId,
            ArrayOf<CompanionValue> inputs, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = CellCompanionSupport.BeginOperation(
                context, Opc.Ua.Vision.Namespaces.Vision, cancellationToken);
            CancellationToken token = lifetime.Token;
            AICompanionTaskAccess.RequireConnected(context, token);
            _ = new VisionClient(context.Session, context.Telemetry);
            var definition = VisionTaskDefinition.Find(target, operationId);
            VisionWorkflowValues.Validate(context, definition.Operation, inputs);
            inputs = CompanionInputContract.Snapshot(inputs, context.Session.MessageContext);
            VisionWorkflowBinding binding = await VisionWorkflowAccess.ReadBindingAsync(
                context, target, definition, inputs, token).ConfigureAwait(false);
            var task = new VisionWorkflowTask(context, target, definition.Operation, binding, inputs, m_timeProvider);
            await AuthorizeAsync(context, task, token).ConfigureAwait(false);
            await RequireUnchangedAsync(context, task, token).ConfigureAwait(false);
            return task;
        }

        public async ValueTask<CompanionOperationResult> ExecuteAsync(
            CompanionContext context, CompanionTarget target, string operationId, CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress, CancellationToken cancellationToken)
        {
            if (input is not VisionWorkflowTask task || task.Target != target || task.Operation.Id != operationId)
            {
                throw new ArgumentException("Prepare the exact Vision workflow before executing it.", nameof(input));
            }
            using CancellationTokenSource lifetime = CellCompanionSupport.BeginOperation(
                context, Opc.Ua.Vision.Namespaces.Vision, cancellationToken);
            CancellationToken token = lifetime.Token;
            await RequireUnchangedAsync(context, task, token).ConfigureAwait(false);
            await AuthorizeAsync(context, task, token).ConfigureAwait(false);
            await RequireUnchangedAsync(context, task, token).ConfigureAwait(false);
            IndustrialCompanionAccess.CheckFields(context, 16);
            var client = new VisionClient(context.Session, context.Telemetry);
            ArrayOf<CompanionValue> values = task.Inputs;
            progress?.Report(new CompanionTaskProgress("Dispatching the reviewed simulated Vision request."));
            token.ThrowIfCancellationRequested();
            task.RequireCurrent(context, m_timeProvider);
            task.BeginExecution();
            try
            {
                switch (operationId)
                {
                    case "get-clip":
                        return await CaptureAsync(context, client, task, values, token).ConfigureAwait(false);
                    case "probe-stream":
                        return await ProbeStreamAsync(context, task, values, token).ConfigureAwait(false);
                    case "configure-stream":
                        await client.Media(task.Binding.Component).ConfigureStreamEndpointAsync(
                            VisionWorkflowAccess.Node(values[0].Value),
                            VisionWorkflowAccess.EnumValue<VisionVideoCodecEnum>(values[1].Value),
                            VisionWorkflowAccess.Unsigned(values[2].Value, 8192),
                            VisionWorkflowAccess.Unsigned(values[3].Value, 8192),
                            VisionWorkflowValues.Rate(values[4].Value),
                            VisionWorkflowAccess.Unsigned(values[5].Value, 100_000_000), token).ConfigureAwait(false);
                        break;
                    case "select-endpoints":
                        await client.Media(task.Binding.Component).SelectEndpointAsync(
                            VisionWorkflowAccess.Node(values[0].Value, true),
                            VisionWorkflowAccess.Node(values[1].Value, true), token).ConfigureAwait(false);
                        break;
                    case "run-inference":
                        string resultId = await client.Pipeline(target.NodeId).RunInferenceAsync(
                            VisionWorkflowAccess.Timestamp(values[0].Value), token).ConfigureAwait(false);
                        return await ReadInferenceResultAsync(context, task, resultId, token).ConfigureAwait(false);
                    case "run-continuous-window":
                        return await RunWindowAsync(context, client, task, values, token).ConfigureAwait(false);
                    default:
                        await SubmitFeedbackAsync(context, client, task, values, token).ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception failure) when (IsUnknownOutcome(failure))
            {
                throw new InvalidOperationException(
                    $"{operationId} outcome is unknown after dispatch. " +
                    "No request was replayed; inspect before retrying.",
                    failure);
            }
            return Accepted(task, "The method returned successfully. Refresh to observe its effect.");
        }

        public static async Task<ArrayOf<CompanionOperation>> OperationsAsync(
            CompanionContext context, CompanionTarget target, ArrayOf<CompanionOperation> existing,
            CancellationToken cancellationToken)
        {
            var operations = existing.ToList();
            ArrayOf<VisionTaskDefinition> definitions = VisionTaskDefinition.ForTarget(target.TypeName);
            for (int index = 0; index < definitions.Count; index++)
            {
                VisionTaskDefinition definition = definitions[index];
                NodeId component = definition.Component.Length == 0 ? target.NodeId :
                    await IndustrialCompanionAccess.ResolveChildAsync(context, target.NodeId,
                        Opc.Ua.Vision.Namespaces.Vision, definition.Component, true, cancellationToken)
                        .ConfigureAwait(false);
                if (component.IsNull ||
                    !await AICompanionTaskAccess.CanExecuteAsync(
                        context, component, definition.Method, Opc.Ua.Vision.Namespaces.Vision, cancellationToken)
                        .ConfigureAwait(false))
                {
                    continue;
                }
                string cleanup = definition.Operation.Id switch
                {
                    "probe-stream" => "ReleaseStreamEndpoint",
                    "run-continuous-window" => "Stop",
                    _ => string.Empty
                };
                if (cleanup.Length == 0 ||
                    await AICompanionTaskAccess.CanExecuteAsync(
                        context, component, cleanup, Opc.Ua.Vision.Namespaces.Vision, cancellationToken)
                        .ConfigureAwait(false))
                {
                    operations.Add(definition.Operation);
                }
            }
            return [.. operations];
        }

        private async Task RequireUnchangedAsync(
            CompanionContext context, VisionWorkflowTask task, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            task.RequireCurrent(context, m_timeProvider);
            var definition = VisionTaskDefinition.Find(task.Target, task.Operation.Id);
            VisionWorkflowBinding current = await VisionWorkflowAccess.ReadBindingAsync(
                context, task.Target, definition, task.Inputs, token).ConfigureAwait(false);
            if (current != task.Binding)
            {
                throw new InvalidOperationException("The Vision binding or destination changed. Prepare again.");
            }
            if (task.Operation.Id is "run-inference" or "run-continuous-window" &&
                (current.Continuous || current.State != VisionEndpointStateEnum.Ready))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The pipeline must be idle and Ready; an existing run is never taken over.");
            }
            task.RequireCurrent(context, m_timeProvider);
        }

        private async Task AuthorizeAsync(
            CompanionContext context, VisionWorkflowTask task, CancellationToken token)
        {
            if (task.Target.TypeName != "InferencePipeline")
            {
                return;
            }
            AIDeploymentSnapshot? deployment = task.Binding.AiDeployment;
            if (deployment is not null)
            {
                if (deployment.ModelId.IsNull ||
                    !deployment.FallbackDeploymentId.IsNull ||
                    deployment.State is not
                        (Opc.Ua.AI.DeploymentStateEnum.Ready or Opc.Ua.AI.DeploymentStateEnum.Active) ||
                    (!AICompanionProvider.IsOnServerWithoutEndpoint(deployment) &&
                        (!Uri.TryCreate(deployment.EndpointUri, UriKind.Absolute, out Uri? destination) ||
                            destination.Scheme is not ("http" or "https") ||
                            destination.UserInfo.Length != 0 ||
                            destination.Query.Length != 0 ||
                            destination.Fragment.Length != 0 ||
                            (!destination.IsLoopback && destination.Scheme != "https"))) ||
                    (deployment.InferenceLocation == Opc.Ua.AI.InferenceLocationEnum.Cloud &&
                        !deployment.EgressPermitted))
                {
                    throw new UnauthorizedAccessException("The Vision AI deployment has no safe single destination.");
                }
                if (AICompanionProvider.IsLocalBackend(deployment) &&
                    deployment.InferenceLocation is
                        Opc.Ua.AI.InferenceLocationEnum.OnServer or Opc.Ua.AI.InferenceLocationEnum.InSimulator &&
                    Uri.TryCreate(context.Session.Endpoint.EndpointUrl, UriKind.Absolute, out Uri? server) &&
                    server.IsLoopback &&
                    server.UserInfo.Length == 0 &&
                    server.Query.Length == 0 &&
                    server.Fragment.Length == 0)
                {
                    return;
                }
            }
            if (m_executionPolicy is null)
            {
                throw new UnauthorizedAccessException(
                    "This pipeline's execution destination needs an explicit host policy. " +
                    "No backend is assumed local.");
            }
            await m_executionPolicy.AuthorizeAsync(context, task, token).AsTask().WaitAsync(token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }

        private static async Task<CompanionOperationResult> CaptureAsync(
            CompanionContext context, VisionClient client, VisionWorkflowTask task,
            ArrayOf<CompanionValue> values, CancellationToken token)
        {
            NodeId endpoint = VisionWorkflowAccess.Node(values[0].Value);
            VisionClipFormatEnum format = VisionWorkflowAccess.EnumValue<VisionClipFormatEnum>(values[3].Value);
            bool requestInline = VisionWorkflowAccess.Boolean(values[4].Value);
            VisionClipResult clip = await client.Media(task.Binding.Component).GetClipAsync(
                endpoint, VisionWorkflowAccess.Text(values[1].Value, false),
                VisionWorkflowAccess.Timestamp(values[2].Value), format, requestInline, token).ConfigureAwait(false);
            if (clip.EndpointNodeId != endpoint ||
                clip.Image is null ||
                clip.Image.Format != format ||
                (!requestInline && !clip.InlineImage.IsEmpty))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "The acquired clip does not match the reviewed endpoint, format or inline request.");
            }
            VisionWorkflowValues.ValidateImage(clip.Image, clip.InlineImage);
            var fields = new CellCompanionFields(context.MaxFields);
            fields.Add("Endpoint", Variant.From(endpoint));
            fields.Add("Request ID", Variant.From(task.RequestId));
            VisionWorkflowResults.AddImage(fields, clip.Image);
            fields.Add("Inline bytes returned", Variant.From(clip.InlineImage.Length));
            return new CompanionOperationResult(
                "One simulated acquisition returned verified frame metadata. No media URI was opened; " +
                "inline bytes, when supplied, matched the declared size and SHA-256.", fields.ToArray());
        }

        private async Task<CompanionOperationResult> ProbeStreamAsync(
            CompanionContext context, VisionWorkflowTask task, ArrayOf<CompanionValue> values, CancellationToken token)
        {
            var media = new VisionMediaManagementTypeClient(context.Session, task.Binding.Component, context.Telemetry);
            NodeId endpoint = VisionWorkflowAccess.Node(values[0].Value);
            VisionStreamProtocolEnum protocol =
                VisionWorkflowAccess.EnumValue<VisionStreamProtocolEnum>(values[2].Value);
            (VisionStreamSessionDataType session, NodeId endpointOut) = await media.GetStreamEndpointAsync(
                endpoint, VisionWorkflowAccess.Text(values[1].Value, false), protocol, token).ConfigureAwait(false);
            if (session is null || session.SessionToken.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "No usable stream lease token was returned; its release cannot be established.");
            }
            ByteString ownedToken = session.SessionToken.Copy();
            Exception? failure = null;
            try
            {
                if (endpointOut != endpoint ||
                    session.Protocol != protocol ||
                    !Enum.IsDefined(session.Protocol) ||
                    ownedToken.Length > 4096 ||
                    session.ExpiresAt.ToDateTime() <= m_timeProvider.GetUtcNow().UtcDateTime ||
                    session.Uri is not { Length: > 0 and <= 4096 } ||
                    !Uri.TryCreate(session.Uri, UriKind.Absolute, out _))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The stream lease is inconsistent.");
                }
                token.ThrowIfCancellationRequested();
            }
            catch (Exception error) when (IsWorkflowFailure(error))
            {
                failure = error;
                throw;
            }
            finally
            {
                await CleanupAsync(context, task,
                    cleanup => media.ReleaseStreamEndpointAsync(ownedToken, cleanup).AsTask(), failure)
                    .ConfigureAwait(false);
            }
            return new CompanionOperationResult(
                "The selected simulated stream lease was acquired and released. No stream was opened; " +
                "the URI and token were neither displayed nor persisted.",
                [new("Endpoint", Variant.From(endpoint)), new("Protocol", Variant.From(protocol.ToString())),
                    new("Lease released", Variant.From(true)), new("Request ID", Variant.From(task.RequestId))]);
        }

        private async Task<CompanionOperationResult> RunWindowAsync(
            CompanionContext context, VisionClient client, VisionWorkflowTask task,
            ArrayOf<CompanionValue> values, CancellationToken token)
        {
            VisionPipelineClient pipeline = client.Pipeline(task.Target.NodeId);
            await pipeline.StartContinuousAsync(token).ConfigureAwait(false);
            Exception? failure = null;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(VisionWorkflowAccess.Unsigned(values[0].Value, 15)),
                    m_timeProvider, token).ConfigureAwait(false);
            }
            catch (Exception error) when (IsWorkflowFailure(error))
            {
                failure = error;
                throw;
            }
            finally
            {
                await CleanupAsync(context, task,
                    cleanup => StopAndObserveAsync(context, pipeline, task, cleanup), failure)
                    .ConfigureAwait(false);
            }
            return Accepted(task,
                "Start and Stop returned, and Continuous=false was observed. No frame-count or latency is inferred.");
        }

        private static async Task StopAndObserveAsync(
            CompanionContext context, VisionPipelineClient pipeline, VisionWorkflowTask task,
            CancellationToken cancellationToken)
        {
            await VisionWorkflowAccess.RequireRunIdentityAsync(context, task, cancellationToken).ConfigureAwait(false);
            task.RequireOwnedSession(context);
            await pipeline.StopAsync(cancellationToken).ConfigureAwait(false);
            ArrayOf<CompanionValue> state = await VisionWorkflowAccess.PropertiesAsync(
                context, task.Target.NodeId, ["Continuous"], cancellationToken).ConfigureAwait(false);
            if (VisionWorkflowAccess.Boolean(state[0].Value))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "Stop returned, but the pipeline still reports Continuous=true.");
            }
        }

        private async Task CleanupAsync(
            CompanionContext context, VisionWorkflowTask task, Func<CancellationToken, Task> cleanup,
            Exception? failure)
        {
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5), m_timeProvider);
            try
            {
                task.RequireOwnedSession(context);
                await cleanup(lifetime.Token).WaitAsync(lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (failure is not null && IsWorkflowFailure(error))
            {
                throw new AggregateException("The Vision operation failed and owned cleanup was incomplete.",
                    failure, error);
            }
        }

        private static async Task SubmitFeedbackAsync(
            CompanionContext context, VisionClient client, VisionWorkflowTask task,
            ArrayOf<CompanionValue> values, CancellationToken token)
        {
            VisionFeedbackClient feedback = client.Feedback(task.Binding.Component);
            switch (task.Operation.Id)
            {
                case "submit-detections":
                    await feedback.SubmitDetectionsAsync(
                        VisionWorkflowAccess.EnumValue<VisionFeedbackPurposeEnum>(values[0].Value),
                        VisionWorkflowValues.Structures<VisionDetectionDataType>(context, values[1].Value),
                        VisionWorkflowValues.Image(context, values[2].Value, true),
                        VisionWorkflowValues.Bytes(values[3].Value),
                        VisionWorkflowAccess.Boolean(values[4].Value), token)
                        .ConfigureAwait(false);
                    break;
                case "submit-inspection":
                    await feedback.SubmitInspectionResultAsync(VisionWorkflowAccess.Text(values[0].Value),
                        VisionWorkflowAccess.EnumValue<VisionResultEvaluationEnum>(values[1].Value),
                        VisionWorkflowValues.Structures<VisionCharacteristicDataType>(context, values[2].Value), token)
                        .ConfigureAwait(false);
                    break;
                case "submit-correction":
                    if (!values[4].Value.TryGetValue(out LocalizedText reason))
                    {
                        throw new ArgumentException("A typed correction reason is required.");
                    }
                    await feedback.SubmitCorrectionAsync(VisionWorkflowAccess.Text(values[0].Value),
                        VisionWorkflowAccess.EnumValue<VisionFeedbackPurposeEnum>(values[1].Value),
                        VisionWorkflowValues.Structures<VisionDetectionDataType>(context, values[2].Value),
                        VisionWorkflowValues.Structures<VisionCharacteristicDataType>(context, values[3].Value),
                        reason, VisionWorkflowValues.Bytes(values[5].Value),
                        VisionWorkflowAccess.Boolean(values[6].Value), token).ConfigureAwait(false);
                    break;
                case "submit-image-reference":
                    await feedback.SubmitImageReferenceAsync(
                        VisionWorkflowAccess.EnumValue<VisionFeedbackPurposeEnum>(values[0].Value),
                        VisionWorkflowValues.Image(context, values[1].Value)!,
                        VisionWorkflowAccess.Text(values[2].Value, false), token).ConfigureAwait(false);
                    break;
                default:
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "Unsupported Vision feedback task.");
            }
        }

        private static async Task<CompanionOperationResult> ReadInferenceResultAsync(
            CompanionContext context, VisionWorkflowTask task, string resultId, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(resultId) || resultId.Length > 256)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "Inference returned no bounded result identity. Do not replay the request.");
            }
            try
            {
                NodeId result = await VisionWorkflowResults.FindAsync(
                    context, task.Target.NodeId, resultId, token).ConfigureAwait(false);
                return await VisionWorkflowResults.ReadAsync(
                    context, result, resultId, task.Binding.Sensor, task.Target.NodeId, token).ConfigureAwait(false);
            }
            catch (Exception failure) when (IsWorkflowFailure(failure))
            {
                throw new InvalidOperationException(
                    $"Inference returned result ID '{resultId}', but its evidence could not be verified. " +
                    "Inspect that result; do not repeat inference automatically.", failure);
            }
        }

        private static CompanionOperationResult Accepted(VisionWorkflowTask task, string summary)
        {
            return new CompanionOperationResult(summary,
                [new("Target", Variant.From(task.Target.NodeId)), new("Method returned", Variant.From(true)),
                    new("Request ID", Variant.From(task.RequestId))]);
        }

        private static bool IsUnknownOutcome(Exception failure)
        {
            return failure is OperationCanceledException or TimeoutException or
                IOException or ObjectDisposedException ||
                (failure is ServiceResultException service &&
                    (service.StatusCode == StatusCodes.BadTimeout ||
                        service.StatusCode == StatusCodes.BadRequestTimeout ||
                        service.StatusCode == StatusCodes.BadCommunicationError ||
                        service.StatusCode == StatusCodes.BadConnectionClosed ||
                        service.StatusCode == StatusCodes.BadSessionClosed ||
                        service.StatusCode == StatusCodes.BadSecureChannelClosed));
        }

        private static bool IsWorkflowFailure(Exception failure)
        {
            return failure is ServiceResultException or InvalidOperationException or ArgumentException or
                IOException or UnauthorizedAccessException or OperationCanceledException or TimeoutException;
        }

        private readonly IVisionExecutionPolicy? m_executionPolicy;
        private readonly TimeProvider m_timeProvider;
    }
}
