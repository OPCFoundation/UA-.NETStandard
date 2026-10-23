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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Client.StateMachines;
using Ai = Opc.Ua.AI;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class AICompanionProvider
    {
        public async ValueTask<CompanionTaskInput> PrepareInputAsync(
            CompanionContext context,
            CompanionTarget target,
            string operationId,
            ArrayOf<CompanionValue> inputs,
            CancellationToken cancellationToken)
        {
            ValidateTarget(context, target);
            using CancellationTokenSource lifetime = AICompanionTaskAccess.Begin(context, cancellationToken);
            CancellationToken token = lifetime.Token;
            CompanionOperation operation = TaskOperation(target.TypeName, operationId);
            RequireInputs(inputs, operation.Inputs);
            await AICompanionTaskAccess.RequireTargetAsync(context, target, token).ConfigureAwait(false);
            if (operation.Safety == CompanionOperationSafety.DeploymentMutation)
            {
                AICompanionTaskAccess.RequireSecureChannel(context);
            }
            if (target.TypeName == "Deployment")
            {
                return await PrepareRequestAsync(context, target, operationId, inputs, token).ConfigureAwait(false);
            }
            if (target.TypeName == "Transfer")
            {
                return await PrepareTransferAsync(context, target, operationId, inputs, token).ConfigureAwait(false);
            }
            if (operationId is "observe-job" or "halt-job")
            {
                uint reads = operationId == "observe-job" ? ObservationCount(inputs[0].Value) : 1;
                FiniteStateSnapshot state = await AICompanionTaskAccess.ReadJobStateAsync(
                    context, target.NodeId, token).ConfigureAwait(false);
                string jobId = await ReadJobIdentityAsync(context, target.NodeId, token).ConfigureAwait(false);
                if (operationId == "halt-job")
                {
                    RequireRunning(state);
                    await AICompanionTaskAccess.RequireExecutableAsync(
                        context, target.NodeId, BrowseNames.Halt, Namespaces.OpcUa, token).ConfigureAwait(false);
                }
                return new AICompanionTaskInput(context, target, operationId,
                    operationId == "halt-job"
                        ? "Request the selected job's standard Program Halt. A method reply is not cancellation proof."
                        : $"Observe at most {reads} snapshots within 30 seconds; no job is started or stopped.",
                    stateId: state.CurrentStateId, subjectIdentity: jobId, observations: reads);
            }
            if (target.TypeName == "Learning job")
            {
                return await PrepareLearningAsync(context, target, operationId, inputs, token).ConfigureAwait(false);
            }
            if (target.TypeName == "Evaluation")
            {
                AIEvaluationRunSnapshot run = await ReadEvaluationAsync(context, target.NodeId, token)
                    .ConfigureAwait(false);
                return new AICompanionTaskInput(context, target, operationId,
                    "Read this existing evaluation's metrics. No evaluation, report download or training is started.",
                    subjectIdentity: run.RunId ?? string.Empty, modelId: run.EvaluatedModelId);
            }
            throw UnsupportedTask();
        }

        public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
            CompanionContext context,
            CompanionTarget target,
            string operationId,
            CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress,
            CancellationToken cancellationToken)
        {
            ValidateTarget(context, target);
            ArgumentNullException.ThrowIfNull(input);
            if (input is not AICompanionTaskInput task)
            {
                throw new ArgumentException("Prepare an AI task before execution.", nameof(input));
            }
            task.RequireMatch(context, target, operationId);
            using CancellationTokenSource lifetime = AICompanionTaskAccess.Begin(context, cancellationToken);
            CancellationToken token = lifetime.Token;
            CompanionOperation operation = TaskOperation(target.TypeName, operationId);
            await AICompanionTaskAccess.RequireTargetAsync(context, target, token).ConfigureAwait(false);
            IndustrialCompanionAccess.CheckFields(context, 12);
            if (operation.Safety == CompanionOperationSafety.DeploymentMutation)
            {
                AICompanionTaskAccess.RequireSecureChannel(context);
            }
            if (target.TypeName == "Deployment")
            {
                await RecheckRequestAsync(context, task, token).ConfigureAwait(false);
                return await ExecuteRequestAsync(context, task, progress, token).ConfigureAwait(false);
            }
            if (target.TypeName == "Transfer")
            {
                return await ExecuteTransferTaskAsync(context, task, token).ConfigureAwait(false);
            }
            if (operationId == "observe-job")
            {
                return await ObserveJobAsync(context, task, progress, token).ConfigureAwait(false);
            }
            if (operationId == "halt-job")
            {
                FiniteStateSnapshot state = await AICompanionTaskAccess.ReadJobStateAsync(
                    context, target.NodeId, token).ConfigureAwait(false);
                RequireRunning(state);
                if (state.CurrentStateId != task.StateId ||
                    await ReadJobIdentityAsync(context, target.NodeId, token).ConfigureAwait(false) !=
                        task.SubjectIdentity)
                {
                    throw StaleTask();
                }
                await AICompanionTaskAccess.RequireExecutableAsync(
                    context, target.NodeId, BrowseNames.Halt, Namespaces.OpcUa, token).ConfigureAwait(false);
                RequirePreparedConnection(context, task, token);
                try
                {
                    await new ProgramStateMachineTypeClient(context.Session, target.NodeId, context.Telemetry)
                        .HaltAsync(token).ConfigureAwait(false);
                }
                catch (Exception failure) when (IsUnknownOutcome(failure))
                {
                    throw UnknownOutcome("Halt", failure);
                }
                return Accepted(
                    "Halt returned successfully; cancellation is not yet observed. Use Observe job.",
                    target.NodeId);
            }
            if (target.TypeName == "Learning job")
            {
                return await ExecuteLearningAsync(context, task, token).ConfigureAwait(false);
            }
            if (target.TypeName == "Evaluation")
            {
                AIEvaluationRunSnapshot run = await ReadEvaluationAsync(context, target.NodeId, token)
                    .ConfigureAwait(false);
                if (run.RunId != task.SubjectIdentity || run.EvaluatedModelId != task.ModelId)
                {
                    throw StaleTask();
                }
                return EvaluationResult(run);
            }
            throw UnsupportedTask();
        }

        private async ValueTask<AICompanionTaskInput> PrepareRequestAsync(
            CompanionContext context, CompanionTarget target, string operationId,
            ArrayOf<CompanionValue> inputs, CancellationToken token)
        {
            NodeId model = AICompanionTaskAccess.Node(inputs[0].Value);
            string capability = AICompanionTaskAccess.String(inputs[1].Value);
            string contentType = AICompanionTaskAccess.String(inputs[3].Value);
            if (capability.Length > 128 ||
                contentType != "application/json" ||
                !inputs[2].Value.TryGetValue(out ByteString payload) ||
                payload.IsNull ||
                payload.IsEmpty ||
                payload.Length > (operationId == "submit-transfer-request"
                    ? AIResponseTaskBuffer.MaximumBytes : MaximumInlineBytes))
            {
                throw new ArgumentException("Supply a bounded, nonempty application/json request and capability.");
            }
            using (var document = JsonDocument.Parse(
                payload.Memory, new JsonDocumentOptions { MaxDepth = 32 }))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException("The AI request must be a JSON object.");
                }
            }
            if (!inputs[4].Value.TryGetValue(
                out ArrayOf<Opc.Ua.KeyValuePair> parameters, context.Session.MessageContext))
            {
                throw new ArgumentException("Parameters must be the standard KeyValuePair array.");
            }
            if (!parameters.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The reference server does not apply call parameters. Nonempty overrides are not supported.");
            }
            double timeout = 0;
            if (operationId == "invoke-request" &&
                (!inputs[5].Value.TryGetValue(out timeout) ||
                    !double.IsFinite(timeout) ||
                    timeout is < 1 or > 30000))
            {
                throw new ArgumentException("Invoke timeout must be 1 through 30000 milliseconds.");
            }
            AIDeploymentSnapshot deployment = await AICompanionTaskAccess.ReadDeploymentAsync(
                context, target.NodeId, token).ConfigureAwait(false);
            await AICompanionTaskAccess.RequireInstanceAsync(context, model, "Model", token).ConfigureAwait(false);
            ValidateRequestDestination(deployment, model, payload.Length, operationId);
            var task = new AICompanionTaskInput(context, target, operationId,
                $"Send exactly {payload.Length} bytes of {contentType} to deployment {target.NodeId}.\n" +
                $"Model: {model}. Capability: {capability}.\n" +
                DestinationReview(context, deployment) +
                "\n" +
                (operationId == "invoke-request"
                    ? $"Invoke timeout: {timeout} ms. Responses above {MaximumInlineBytes} bytes are not displayed."
                    : "Method acceptance is not completion; observe the returned job or transfer separately.") +
                "\nNo PayloadUri, fallback, tool execution or automatic response download is permitted.",
                deployment, payload, contentType, capability, timeout, modelId: model);
            await AuthorizeDestinationAsync(context, task, token).ConfigureAwait(false);
            await CheckCapabilitiesAsync(context, task, token).ConfigureAwait(false);
            await RequireSameDeploymentAsync(context, task, token).ConfigureAwait(false);
            await AICompanionTaskAccess.RequireExecutableAsync(
                context, target.NodeId, RequestMethod(operationId), token).ConfigureAwait(false);
            return task;
        }

        private async ValueTask RecheckRequestAsync(
            CompanionContext context, AICompanionTaskInput task, CancellationToken token)
        {
            await RequireSameDeploymentAsync(context, task, token).ConfigureAwait(false);
            await AuthorizeDestinationAsync(context, task, token).ConfigureAwait(false);
            await CheckCapabilitiesAsync(context, task, token).ConfigureAwait(false);
            await RequireSameDeploymentAsync(context, task, token).ConfigureAwait(false);
            await AICompanionTaskAccess.RequireExecutableAsync(
                context, task.Target.NodeId, RequestMethod(task.OperationId), token).ConfigureAwait(false);
            RequirePreparedConnection(context, task, token);
        }

        private static async ValueTask RequireSameDeploymentAsync(
            CompanionContext context, AICompanionTaskInput task, CancellationToken token)
        {
            AIDeploymentSnapshot current = await AICompanionTaskAccess.ReadDeploymentAsync(
                context, task.Target.NodeId, token).ConfigureAwait(false);
            if (current != task.Deployment)
            {
                throw StaleTask();
            }
            ValidateRequestDestination(current, task.ModelId, task.Payload.Length, task.OperationId);
        }

        private async ValueTask AuthorizeDestinationAsync(
            CompanionContext context, AICompanionTaskInput task, CancellationToken token)
        {
            AIDeploymentSnapshot deployment = task.Deployment ?? throw StaleTask();
            bool local = IsLocalBackend(deployment) &&
                deployment.InferenceLocation is
                    Ai.InferenceLocationEnum.OnServer or Ai.InferenceLocationEnum.InSimulator &&
                Uri.TryCreate(context.Session.Endpoint.EndpointUrl, UriKind.Absolute, out Uri? server) &&
                server.IsLoopback &&
                string.IsNullOrEmpty(server.UserInfo) &&
                string.IsNullOrEmpty(server.Query) &&
                string.IsNullOrEmpty(server.Fragment);
            if (local)
            {
                return;
            }
            if (m_egressPolicy is null)
            {
                throw new UnauthorizedAccessException(
                    "External AI egress is denied without a policy accepting this exact destination and request.");
            }
            if (!IsOnServerWithoutEndpoint(deployment) &&
                (!Uri.TryCreate(deployment.EndpointUri, UriKind.Absolute, out Uri? endpoint) ||
                    (!endpoint.IsLoopback && endpoint.Scheme != Uri.UriSchemeHttps)))
            {
                throw new UnauthorizedAccessException("External AI destinations require HTTPS.");
            }
            await m_egressPolicy.AuthorizeAsync(context, task, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }

        private static void ValidateRequestDestination(
            AIDeploymentSnapshot deployment, NodeId model, int payloadLength, string operationId)
        {
            if (deployment.State is not (Ai.DeploymentStateEnum.Ready or Ai.DeploymentStateEnum.Active))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The deployment is not ready to serve.");
            }
            if (deployment.ModelId.IsNull || deployment.ModelId != model)
            {
                throw new InvalidOperationException("The selected model is not the deployment's current model.");
            }
            if (!deployment.FallbackDeploymentId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Implicit fallback routing cannot be authorized by this single-destination task.");
            }
            bool invalidEndpoint = !IsOnServerWithoutEndpoint(deployment) &&
                (!Uri.TryCreate(deployment.EndpointUri, UriKind.Absolute, out Uri? endpoint) ||
                    endpoint.Scheme is not ("http" or "https") ||
                    !string.IsNullOrEmpty(endpoint.UserInfo) ||
                    !string.IsNullOrEmpty(endpoint.Query) ||
                    !string.IsNullOrEmpty(endpoint.Fragment));
            if (invalidEndpoint ||
                (deployment.InferenceLocation == Ai.InferenceLocationEnum.Cloud && !deployment.EgressPermitted))
            {
                throw new UnauthorizedAccessException(
                    "The AI destination or egress metadata is unsafe; broker/physical URIs are denied.");
            }
            if (operationId != "submit-transfer-request" &&
                (ulong)payloadLength > deployment.MaxInlinePayloadSize)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The request exceeds the deployment's inline byte limit.");
            }
        }

        private static string DestinationReview(CompanionContext context, AIDeploymentSnapshot deployment)
        {
            if (!Uri.TryCreate(context.Session.Endpoint.EndpointUrl, UriKind.Absolute, out Uri? server) ||
                !string.IsNullOrEmpty(server.UserInfo) ||
                !string.IsNullOrEmpty(server.Query) ||
                !string.IsNullOrEmpty(server.Fragment))
            {
                throw new UnauthorizedAccessException("The session destination cannot be safely disclosed.");
            }
            string backend = IsOnServerWithoutEndpoint(deployment)
                ? "the selected OPC UA server (OnServer)" : deployment.EndpointUri!;
            return $"OPC UA destination: {server.AbsoluteUri}\nBackend destination: {backend}\n" +
                $"Inference location: {deployment.InferenceLocation}; " +
                $"egress permitted: {deployment.EgressPermitted}.\n" +
                $"Data jurisdiction: {deployment.DataJurisdiction}. " +
                "Backend retention is not established by this client.";
        }

        private static async ValueTask CheckCapabilitiesAsync(
            CompanionContext context, AICompanionTaskInput task, CancellationToken token)
        {
            await AICompanionTaskAccess.RequireExecutableAsync(
                context, task.Target.NodeId, Ai.BrowseNames.GetCapabilities, token).ConfigureAwait(false);
            var client = new AIClient(context.Session, context.Telemetry);
            ArrayOf<Ai.CapabilityDataType> capabilities = await client.Deployment(task.Target.NodeId)
                .GetCapabilitiesAsync(token).ConfigureAwait(false);
            if (capabilities.Count > context.MaxFields)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, "Too many AI capabilities.");
            }
            var advertised = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (Ai.CapabilityDataType capability in capabilities)
            {
                if (capability is null ||
                    string.IsNullOrWhiteSpace(capability.Name) ||
                    capability.Name.Length > 128 ||
                    !advertised.TryAdd(capability.Name, capability.Supported))
                {
                    throw AICompanionTaskAccess.InvalidData("AI capabilities are malformed or ambiguous.");
                }
            }
            string transport = task.OperationId switch
            {
                "submit-inference-job" => "async-inference",
                "submit-transfer-request" => "chunked-transfer",
                _ => "inline-payload"
            };
            if (!advertised.TryGetValue("reachable", out bool reachable) ||
                !reachable ||
                !advertised.TryGetValue(transport, out bool supported) ||
                !supported ||
                !advertised.TryGetValue(task.Capability, out bool selected) ||
                !selected)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The deployment does not advertise the requested capability and transport as available.");
            }
        }

        private async ValueTask<CompanionOperationResult> ExecuteRequestAsync(
            CompanionContext context, AICompanionTaskInput task,
            IProgress<CompanionTaskProgress>? progress, CancellationToken token)
        {
            var client = new AIClient(context.Session, context.Telemetry);
            AIDeploymentClient deployment = client.Deployment(task.Target.NodeId);
            if (task.OperationId == "submit-transfer-request")
            {
                return await SubmitTransferAsync(context, client, deployment, task, progress, token)
                    .ConfigureAwait(false);
            }
            progress?.Report(new CompanionTaskProgress("Submitting the approved AI request"));
            if (task.OperationId == "submit-inference-job")
            {
                NodeId job;
                try
                {
                    job = await deployment.InvokeAsyncAsync(
                        task.Payload, task.ContentType, [], cancellationToken: token).ConfigureAwait(false);
                }
                catch (Exception failure) when (IsUnknownOutcome(failure))
                {
                    throw UnknownOutcome("InvokeAsync", failure);
                }
                if (job.IsNull)
                {
                    throw new InvalidOperationException(
                        "InvokeAsync returned no job identifier. Submission outcome is unknown; do not resubmit.");
                }
                return Accepted("InvokeAsync returned a job. Completion and result have not been observed.", job);
            }
            AIInvokeResult result;
            try
            {
                result = await deployment.InvokeAsync(
                    task.Payload, task.ContentType, [], task.Timeout, cancellationToken: token).ConfigureAwait(false);
            }
            catch (Exception failure) when (IsUnknownOutcome(failure))
            {
                throw UnknownOutcome("Invoke", failure);
            }
            if (!result.TransferRequired && !result.ModelUsed.IsNull && result.ModelUsed != task.ModelId)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The response names a model other than the approved model.");
            }
            ValidateInvokeResult(context, result);
            return new CompanionOperationResult(
                result.TransferRequired
                    ? "Invoke requires a separate transfer task. No response file was opened or transfer executed."
                    : result.ModelUsed.IsNull && result.FinishReason == Ai.FinishReasonEnum.Stop
                        ? "Invoke returned without model provenance; the inference outcome is not established."
                        : $"Invoke returned {result.FinishReason}. " +
                            "No tool, fallback, retry or further request was executed.",
                [
                    new("Method returned", Variant.From(true)),
                    new("Finish reason", Variant.From(result.FinishReason.ToString())),
                    new("Complete response", Variant.From(
                        !result.TransferRequired &&
                        !result.ModelUsed.IsNull &&
                        result.FinishReason == Ai.FinishReasonEnum.Stop)),
                    new("Content type", Text(result.ResponseContentType)),
                    new("Response bytes", Variant.From(result.ResponsePayload.Length)),
                    new("Response", Variant.From(result.ResponsePayload.Copy())),
                    new("Model used", Variant.From(result.ModelUsed)),
                    new("Retry after", Variant.From(result.RetryAfter)),
                    new("Transfer required", Variant.From(result.TransferRequired)),
                    new("Transfer", Variant.From(result.TransferId)),
                    new("Usage", result.Usage is null ? Variant.Null : Variant.FromStructure(result.Usage)),
                    new("Safety assessment", Variant.FromStructure(result.SafetyAssessment))
                ]);
        }

        private static void ValidateInvokeResult(CompanionContext context, AIInvokeResult result)
        {
            if (result.ResponsePayload.Length > MaximumInlineBytes ||
                result.SafetyAssessment.Count > context.MaxFields)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The AI response exceeds the inline display capacity.");
            }
            if ((int)result.FinishReason is < 0 or > 5 ||
                !double.IsFinite(result.RetryAfter) ||
                result.RetryAfter < 0 ||
                (result.TransferRequired && result.TransferId.IsNull) ||
                (!result.TransferRequired && !result.TransferId.IsNull))
            {
                throw AICompanionTaskAccess.InvalidData("The AI result envelope is inconsistent.");
            }
        }

        private async ValueTask<CompanionOperationResult> SubmitTransferAsync(
            CompanionContext context, AIClient client, AIDeploymentClient deployment, AICompanionTaskInput task,
            IProgress<CompanionTaskProgress>? progress, CancellationToken token)
        {
            AIBeginTransferResult begun;
            try
            {
                begun = await deployment.BeginTransferAsync(task.ContentType, (ulong)task.Payload.Length, token)
                    .ConfigureAwait(false);
            }
            catch (Exception failure) when (IsUnknownOutcome(failure))
            {
                throw UnknownOutcome("BeginTransfer", failure);
            }
            if (!begun.Accepted)
            {
                return Accepted(
                    "BeginTransfer refused the request. No request file was written.", begun.TransferId, false);
            }
            if (begun.TransferId.IsNull)
            {
                throw new InvalidOperationException(
                    "BeginTransfer accepted without an identifier. Resource ownership is unknown; do not retry.");
            }
            await AICompanionTaskAccess.RequireInstanceAsync(context, begun.TransferId, "Transfer", token)
                .ConfigureAwait(false);
            AITransferSnapshot original = await AICompanionTaskAccess.ReadTransferAsync(
                context, begun.TransferId, m_timeProvider, token).ConfigureAwait(false);
            RequireWritableTransfer(original);
            AIInferenceTransferClient transfer = client.Transfer(begun.TransferId);
            bool executeDispatched = false;
            try
            {
                await AICompanionTaskAccess.RequireExecutableAsync(
                    context, begun.TransferId, Ai.BrowseNames.Abort, token).ConfigureAwait(false);
                await AICompanionTaskAccess.RequireExecutableAsync(
                    context, begun.TransferId, Ai.BrowseNames.Execute, token).ConfigureAwait(false);
                progress?.Report(new CompanionTaskProgress("Uploading the approved request"));
                await AITaskFileTransfer.WriteRequestAsync(context, begun.TransferId, task.Payload, token)
                    .ConfigureAwait(false);
                await RecheckRequestAsync(context, task, token).ConfigureAwait(false);
                AITransferSnapshot current = await AICompanionTaskAccess.ReadTransferAsync(
                    context, begun.TransferId, m_timeProvider, token).ConfigureAwait(false);
                RequireWritableTransfer(current);
                if (current.TransferId != original.TransferId)
                {
                    throw StaleTask();
                }
                await AICompanionTaskAccess.RequireExecutableAsync(
                    context, begun.TransferId, Ai.BrowseNames.Execute, token).ConfigureAwait(false);
                RequirePreparedConnection(context, task, token);
                executeDispatched = true;
                bool accepted = await transfer.ExecuteAsync(token).ConfigureAwait(false);
                return Accepted(
                    "Transfer Execute returned. Its state and response have not been observed or downloaded.",
                    begun.TransferId, accepted);
            }
            catch (Exception failure) when (executeDispatched && IsUnknownOutcome(failure))
            {
                throw UnknownOutcome("Transfer Execute", failure);
            }
            catch (Exception failure) when (!executeDispatched && IsTransferFailure(failure))
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await transfer.AbortAsync(cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception cleanupFailure) when (IsTransferFailure(cleanupFailure))
                {
                    throw new AggregateException(
                        "Upload and cleanup of its own transfer both failed; resource state is unknown.",
                        failure, cleanupFailure);
                }
                throw;
            }
        }

        private async ValueTask<AICompanionTaskInput> PrepareTransferAsync(
            CompanionContext context, CompanionTarget target, string operationId,
            ArrayOf<CompanionValue> inputs, CancellationToken token)
        {
            AITransferSnapshot transfer = await AICompanionTaskAccess.ReadTransferAsync(
                context, target.NodeId, m_timeProvider, token).ConfigureAwait(false);
            if (operationId == "abort-transfer")
            {
                if (transfer.State == Ai.TransferStateEnum.Expired)
                {
                    throw new InvalidOperationException("The transfer has already expired.");
                }
                await AICompanionTaskAccess.RequireExecutableAsync(
                    context, target.NodeId, Ai.BrowseNames.Abort, token).ConfigureAwait(false);
                return new AICompanionTaskInput(context, target, operationId,
                    "Abort only this transfer. A method reply does not prove backend inference was cancelled.",
                    subjectIdentity: transfer.TransferId ?? string.Empty, phase: (int)transfer.State);
            }
            ulong maximum = AICompanionTaskAccess.Unsigned(inputs[0].Value);
            if (maximum is < 1 or > AIResponseTaskBuffer.MaximumBytes ||
                !inputs[1].Value.TryGetValue(out ByteString digest) ||
                digest.Length != 32)
            {
                throw new ArgumentException("Supply a 1-1048576 byte cap and a trusted 32-byte SHA-256 digest.");
            }
            RequireReadableTransfer(transfer);
            AITaskResponseFile file = await RequireResponseFileAsync(context, target.NodeId, maximum, token)
                .ConfigureAwait(false);
            return new AICompanionTaskInput(context, target, operationId,
                $"Read this completed transfer into memory with a strict {maximum}-byte cap.\n" +
                $"Expected SHA-256: {Convert.ToHexString(digest.Span)}\n" +
                "The digest is caller-supplied, not a server authenticity claim. Close only the opened file handle; " +
                "do not execute, abort, save or interpret the response.",
                contentType: transfer.ResponseContentType ?? string.Empty, subjectId: file.NodeId,
                subjectIdentity: transfer.TransferId ?? string.Empty, modelId: transfer.ModelUsed,
                phase: (int)transfer.State, maximumBytes: maximum, expectedDigest: digest, expectedSize: file.Size);
        }

        private async ValueTask<CompanionOperationResult> ExecuteTransferTaskAsync(
            CompanionContext context, AICompanionTaskInput task, CancellationToken token)
        {
            AITransferSnapshot current = await AICompanionTaskAccess.ReadTransferAsync(
                context, task.Target.NodeId, m_timeProvider, token).ConfigureAwait(false);
            if (current.TransferId != task.SubjectIdentity || (int)current.State != task.Phase)
            {
                throw StaleTask();
            }
            var client = new AIClient(context.Session, context.Telemetry);
            AIInferenceTransferClient transfer = client.Transfer(task.Target.NodeId);
            if (task.OperationId == "abort-transfer")
            {
                await AICompanionTaskAccess.RequireExecutableAsync(
                    context, task.Target.NodeId, Ai.BrowseNames.Abort, token).ConfigureAwait(false);
                RequirePreparedConnection(context, task, token);
                try
                {
                    await transfer.AbortAsync(token).ConfigureAwait(false);
                }
                catch (Exception failure) when (IsUnknownOutcome(failure))
                {
                    throw UnknownOutcome("Transfer Abort", failure);
                }
                return Accepted(
                    "Abort returned successfully. Backend cancellation and resource deletion are not observed.",
                    task.Target.NodeId);
            }
            RequireReadableTransfer(current);
            AITaskResponseFile file = await RequireResponseFileAsync(
                context, task.Target.NodeId, task.MaximumBytes, token).ConfigureAwait(false);
            if (current.ModelUsed != task.ModelId ||
                (current.ResponseContentType ?? string.Empty) != task.ContentType ||
                file.NodeId != task.SubjectId ||
                file.Size != task.ExpectedSize)
            {
                throw StaleTask();
            }
            using var destination = new AIResponseTaskBuffer(checked((int)task.MaximumBytes));
            RequirePreparedConnection(context, task, token);
            await AITaskFileTransfer.ReadResponseAsync(context, task.SubjectId, destination, token)
                .ConfigureAwait(false);
            AITransferSnapshot after = await AICompanionTaskAccess.ReadTransferAsync(
                context, task.Target.NodeId, m_timeProvider, token).ConfigureAwait(false);
            AITaskResponseFile afterFile = await RequireResponseFileAsync(
                context, task.Target.NodeId, task.MaximumBytes, token).ConfigureAwait(false);
            if (after != current || afterFile != file || (ulong)destination.Length != file.Size)
            {
                throw StaleTask();
            }
            ByteString payload = destination.VerifyAndCopy(task.ExpectedDigest);
            return new CompanionOperationResult(
                "The bounded response matched the caller's SHA-256. The owned file handle was closed; " +
                "no transfer was executed, aborted or saved.",
                [
                    new("Transfer", Variant.From(task.Target.NodeId)),
                    new("Transfer ID", Text(after.TransferId)),
                    new("State", Variant.From(after.State.ToString())),
                    new("Content type", Text(after.ResponseContentType)),
                    new("Model used", Variant.From(after.ModelUsed)),
                    new("Response bytes", Variant.From(payload.Length)),
                    new("SHA-256 verified", Variant.From(true)),
                    new("Response", Variant.From(payload))
                ]);
        }

        private static async ValueTask<AITaskResponseFile> RequireResponseFileAsync(
            CompanionContext context, NodeId transfer, ulong maximum, CancellationToken token)
        {
            NodeId file = await IndustrialCompanionAccess.ResolveChildAsync(
                context, transfer, Ai.Namespaces.AI, Ai.BrowseNames.Response, false, token).ConfigureAwait(false);
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, new CompanionTarget("ai", file, "Response file", "Response file"), "ai",
                [new(ObjectTypeIds.FileType, "Response file")], token).ConfigureAwait(false);
            ArrayOf<CompanionValue> values = await AICompanionTaskAccess.PropertiesAsync(
                context, file, [BrowseNames.Size], Namespaces.OpcUa, token).ConfigureAwait(false);
            ulong size = AICompanionTaskAccess.Unsigned(values[0].Value);
            if (size > maximum)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The response file is larger than the approved byte cap.");
            }
            await AICompanionTaskAccess.RequireExecutableAsync(
                context, file, BrowseNames.Open, Namespaces.OpcUa, token).ConfigureAwait(false);
            await AICompanionTaskAccess.RequireExecutableAsync(
                context, file, BrowseNames.Read, Namespaces.OpcUa, token).ConfigureAwait(false);
            await AICompanionTaskAccess.RequireExecutableAsync(
                context, file, BrowseNames.Close, Namespaces.OpcUa, token).ConfigureAwait(false);
            return new AITaskResponseFile(file, size);
        }

        private static async ValueTask<CompanionOperationResult> ObserveJobAsync(
            CompanionContext context, AICompanionTaskInput task,
            IProgress<CompanionTaskProgress>? progress, CancellationToken token)
        {
            FiniteStateSnapshot state;
            uint reads = 0;
            do
            {
                task.RequireMatch(context, task.Target, task.OperationId);
                AICompanionTaskAccess.RequireConnected(context, token);
                if (await ReadJobIdentityAsync(context, task.Target.NodeId, token).ConfigureAwait(false) !=
                    task.SubjectIdentity)
                {
                    throw StaleTask();
                }
                state = await AICompanionTaskAccess.ReadJobStateAsync(context, task.Target.NodeId, token)
                    .ConfigureAwait(false);
                reads++;
                progress?.Report(new CompanionTaskProgress(
                    AICompanionTaskAccess.IsTerminal(state.CurrentStateId)
                        ? "Observed terminal program state" : "Observed non-terminal program state"));
            }
            while (reads < task.Observations && !AICompanionTaskAccess.IsTerminal(state.CurrentStateId));

            bool terminal = AICompanionTaskAccess.IsTerminal(state.CurrentStateId);
            var values = new List<CompanionValue>
            {
                new("Job", Variant.From(task.Target.NodeId)),
                new("Job ID", Variant.From(task.SubjectIdentity)),
                new("Program state", Variant.From(state.CurrentState)),
                new("Program state ID", Variant.From(state.CurrentStateId)),
                new("Observed terminal", Variant.From(terminal)),
                new("Observations", Variant.From(reads))
            };
            string outcome = terminal ? "Terminal program state observed; result not established." :
                "The bounded observation ended without a terminal program state. The job was not cancelled.";
            if (task.Target.TypeName == "Learning job")
            {
                AITaskLearningSnapshot learning = await AICompanionTaskAccess.ReadLearningAsync(
                    context, task.Target.NodeId, token).ConfigureAwait(false);
                if (learning.Job.JobId != task.SubjectIdentity)
                {
                    throw StaleTask();
                }
                values.Add(new("Learning phase", Variant.From(learning.Job.State.ToString())));
                values.Add(new("Candidate model", Variant.From(learning.Job.CandidateModelId)));
                outcome += " A learning phase is not training proof; the reference sample is accounting-only.";
            }
            else if (terminal)
            {
                ArrayOf<CompanionValue> error = await AICompanionTaskAccess.PropertiesAsync(
                    context, task.Target.NodeId, [Ai.BrowseNames.LastError], token).ConfigureAwait(false);
                bool failed = false;
                if (!AICompanionTaskAccess.IsAbsent(error[0].Value))
                {
                    if (!error[0].Value.TryGetValue(out LocalizedText detail))
                    {
                        throw AICompanionTaskAccess.InvalidData("The job error indicator has the wrong type.");
                    }
                    failed = !string.IsNullOrEmpty(detail.Text);
                }
                values.Add(new("Server error reported", Variant.From(failed)));
                if (failed)
                {
                    outcome = "Terminal program state observed; the server reported an inference failure.";
                }
                else
                {
                    AIInferenceJobSnapshot result = await AICompanionTaskAccess.ReadInferenceResultAsync(
                        context, task.Target.NodeId, token).ConfigureAwait(false);
                    if (result.JobId != task.SubjectIdentity)
                    {
                        throw StaleTask();
                    }
                    if (result.ResponsePayload.Length > MaximumInlineBytes)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The job result exceeds the inline cap; this job has no large-result transfer method.");
                    }
                    FiniteStateSnapshot after = await AICompanionTaskAccess.ReadJobStateAsync(
                        context, task.Target.NodeId, token).ConfigureAwait(false);
                    if (after.CurrentStateId != state.CurrentStateId)
                    {
                        throw StaleTask();
                    }
                    values.Add(new("Finish reason", Variant.From(result.FinishReason.ToString())));
                    values.Add(new("Model used", Variant.From(result.ModelUsed)));
                    values.Add(new("Content type", Text(result.ResponseContentType)));
                    values.Add(new("Response", Variant.From(result.ResponsePayload.Copy())));
                    outcome = result.ModelUsed.IsNull
                        ? "Terminal program state observed, but the result outcome is unknown."
                        : $"Terminal program state observed; inference reported {result.FinishReason}.";
                }
            }
            return new CompanionOperationResult(outcome, [.. values]);
        }

        private static async ValueTask<string> ReadJobIdentityAsync(
            CompanionContext context, NodeId jobId, CancellationToken token)
        {
            ArrayOf<CompanionValue> values = await AICompanionTaskAccess.PropertiesAsync(
                context, jobId, [Ai.BrowseNames.JobId], token).ConfigureAwait(false);
            return AICompanionTaskAccess.String(values[0].Value);
        }

        private async ValueTask<AICompanionTaskInput> PrepareLearningAsync(
            CompanionContext context, CompanionTarget target, string operationId,
            ArrayOf<CompanionValue> inputs, CancellationToken token)
        {
            AITaskLearningSnapshot learning = await AICompanionTaskAccess.ReadLearningAsync(
                context, target.NodeId, token).ConfigureAwait(false);
            RequireLearningPhase(operationId, learning.Job.State);
            NodeId model = AICompanionTaskAccess.Node(inputs[0].Value);
            NodeId dataset = AICompanionTaskAccess.Node(inputs[1].Value);
            if (model != learning.BaseModelId || dataset != learning.DatasetId)
            {
                throw new InvalidOperationException(
                    "Select the job's existing base model and dataset. This server contract cannot reassign them.");
            }
            await AICompanionTaskAccess.RequireInstanceAsync(context, model, "Model", token).ConfigureAwait(false);
            await AICompanionTaskAccess.RequireInstanceAsync(context, dataset, "Dataset", token).ConfigureAwait(false);
            AIDeploymentSnapshot? deployment = null;
            NodeId subject = NodeId.Null;
            if (operationId == "promote-model")
            {
                subject = AICompanionTaskAccess.Node(inputs[2].Value);
                await AICompanionTaskAccess.RequireInstanceAsync(context, subject, "Deployment", token)
                    .ConfigureAwait(false);
                if (learning.Job.CandidateModelId.IsNull)
                {
                    throw new InvalidOperationException("There is no candidate model to promote.");
                }
                await AICompanionTaskAccess.RequireInstanceAsync(
                    context, learning.Job.CandidateModelId, "Model", token).ConfigureAwait(false);
                deployment = await AICompanionTaskAccess.ReadDeploymentAsync(context, subject, token)
                    .ConfigureAwait(false);
                ValidateRequestDestination(deployment, deployment.ModelId, 0, operationId);
            }
            await AICompanionTaskAccess.RequireExecutableAsync(
                context, target.NodeId, LearningMethod(operationId), token).ConfigureAwait(false);
            var task = new AICompanionTaskInput(context, target, operationId,
                $"Request {LearningMethod(operationId)} on the selected learning job.\n" +
                $"Existing base model: {model}; dataset: {dataset}.\n" +
                (deployment is null
                    ? "No dataset contents are fetched by UaLens. Server-side learning destination and egress " +
                        "are not published; an exact host authorization is required."
                    : DestinationReview(context, deployment) +
                        $"\nCandidate: {learning.Job.CandidateModelId}; exact destination deployment: {subject}.") +
                "\nMethod acceptance is not evidence of collection, training, evaluation or promotion completion. " +
                "The reference LearningSamples job is sample accounting, not a training implementation.",
                deployment, subjectId: subject, stateId: learning.Job.CandidateModelId,
                subjectIdentity: learning.Job.JobId ?? string.Empty, modelId: model, datasetId: dataset,
                phase: (int)learning.Job.State);
            await AuthorizeLearningAsync(context, task, token).ConfigureAwait(false);
            return task;
        }

        private async ValueTask AuthorizeLearningAsync(
            CompanionContext context, AICompanionTaskInput task, CancellationToken token)
        {
            if (task.Deployment is not null)
            {
                await AuthorizeDestinationAsync(context, task, token).ConfigureAwait(false);
                return;
            }
            if (!Uri.TryCreate(context.Session.Endpoint.EndpointUrl, UriKind.Absolute, out Uri? server) ||
                !string.IsNullOrEmpty(server.UserInfo) ||
                !string.IsNullOrEmpty(server.Query) ||
                !string.IsNullOrEmpty(server.Fragment) ||
                m_egressPolicy is null)
            {
                throw new UnauthorizedAccessException(
                    "Learning has no execution-destination contract. An exact configured policy is required.");
            }
            await m_egressPolicy.AuthorizeAsync(context, task, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }

        private async ValueTask<CompanionOperationResult> ExecuteLearningAsync(
            CompanionContext context, AICompanionTaskInput task, CancellationToken token)
        {
            AITaskLearningSnapshot learning = await AICompanionTaskAccess.ReadLearningAsync(
                context, task.Target.NodeId, token).ConfigureAwait(false);
            if (learning.Job.JobId != task.SubjectIdentity ||
                (int)learning.Job.State != task.Phase ||
                learning.BaseModelId != task.ModelId ||
                learning.DatasetId != task.DatasetId ||
                learning.Job.CandidateModelId != task.StateId)
            {
                throw StaleTask();
            }
            RequireLearningPhase(task.OperationId, learning.Job.State);
            AIDeploymentSnapshot? deployment = task.Deployment is null ? null :
                await AICompanionTaskAccess.ReadDeploymentAsync(context, task.SubjectId, token).ConfigureAwait(false);
            if (deployment != task.Deployment)
            {
                throw StaleTask();
            }
            await AuthorizeLearningAsync(context, task, token).ConfigureAwait(false);
            await AICompanionTaskAccess.RequireExecutableAsync(
                context, task.Target.NodeId, LearningMethod(task.OperationId), token).ConfigureAwait(false);
            RequirePreparedConnection(context, task, token);
            var client = new AIClient(context.Session, context.Telemetry);
            AILearningJobClient job = client.LearningJob(task.Target.NodeId);
            bool accepted = true;
            NodeId promoted = NodeId.Null;
            try
            {
                switch (task.OperationId)
                {
                    case "start-collection":
                        await job.StartCollectionAsync(token).ConfigureAwait(false);
                        break;
                    case "stop-collection":
                        await job.StopCollectionAsync(token).ConfigureAwait(false);
                        break;
                    case "trigger-training":
                        accepted = await job.TriggerTrainingAsync(token).ConfigureAwait(false);
                        break;
                    case "promote-model":
                        promoted = await job.PromoteModelAsync(task.SubjectId, token).ConfigureAwait(false);
                        break;
                    default:
                        throw UnsupportedTask();
                }
            }
            catch (Exception failure) when (IsUnknownOutcome(failure))
            {
                throw UnknownOutcome(LearningMethod(task.OperationId), failure);
            }
            if (task.OperationId == "promote-model" && (promoted.IsNull || promoted != task.StateId))
            {
                throw new InvalidOperationException(
                    "PromoteModel returned an unexpected model. The promotion outcome is not established.");
            }
            return new CompanionOperationResult(
                "The learning method returned; its requested state change has not been observed. " +
                "This is not a claim that a model was trained, evaluated or deployed.",
                [
                    new("Method returned", Variant.From(true)),
                    new("Accepted", Variant.From(accepted)),
                    new("Observed terminal", Variant.From(false)),
                    new("Job", Variant.From(task.Target.NodeId)),
                    new("Returned model", Variant.From(promoted)),
                    new("Destination deployment", Variant.From(task.SubjectId))
                ]);
        }

        private static void RequireLearningPhase(string operationId, Ai.LearningJobStateEnum phase)
        {
            bool allowed = operationId switch
            {
                "start-collection" => phase == Ai.LearningJobStateEnum.Idle,
                "stop-collection" => phase == Ai.LearningJobStateEnum.Collecting,
                "trigger-training" => phase is Ai.LearningJobStateEnum.Idle or Ai.LearningJobStateEnum.Labelling,
                "promote-model" => phase == Ai.LearningJobStateEnum.Ready,
                _ => false
            };
            if (!allowed)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The learning phase disallows this task.");
            }
        }

        private static async ValueTask<AIEvaluationRunSnapshot> ReadEvaluationAsync(
            CompanionContext context, NodeId runId, CancellationToken token)
        {
            ArrayOf<CompanionValue> values = await AICompanionTaskAccess.PropertiesAsync(
                context, runId,
                [Ai.BrowseNames.RunId, Ai.BrowseNames.EvaluatedModel, Ai.BrowseNames.Metrics, Ai.BrowseNames.Passed],
                token).ConfigureAwait(false);
            if (!values[2].Value.TryGetValue(
                out ArrayOf<Ai.EvaluationMetricDataType> metrics, context.Session.MessageContext) ||
                metrics.Count > context.MaxFields)
            {
                throw AICompanionTaskAccess.InvalidData("Evaluation metrics are missing or exceed the field cap.");
            }
            bool passed = AICompanionTaskAccess.Boolean(values[3].Value);
            foreach (Ai.EvaluationMetricDataType metric in metrics)
            {
                if (metric is null ||
                    string.IsNullOrWhiteSpace(metric.Name) ||
                    metric.Name.Length > 128 ||
                    !double.IsFinite(metric.Value) ||
                    !double.IsFinite(metric.Threshold) ||
                    metric.Comparison is not (">=" or "<=" or ">" or "<" or "==") ||
                    (passed && !metric.Passed))
                {
                    throw AICompanionTaskAccess.InvalidData("The evaluation summary contradicts its metrics.");
                }
            }
            return new AIEvaluationRunSnapshot
            {
                NodeId = runId,
                RunId = AICompanionTaskAccess.String(values[0].Value),
                EvaluatedModelId = AICompanionTaskAccess.Node(values[1].Value),
                Metrics = metrics,
                Passed = passed
            };
        }

        private static CompanionOperationResult EvaluationResult(AIEvaluationRunSnapshot run)
        {
            return new CompanionOperationResult(
                "Read the existing evaluation. The typed model has no start-evaluation method; no report was fetched.",
                [
                    new("Evaluation", Variant.From(run.NodeId)),
                    new("Run ID", Text(run.RunId)),
                    new("Evaluated model", Variant.From(run.EvaluatedModelId)),
                    new("Passed", Variant.From(run.Passed)),
                    new("Metrics", Variant.FromStructure(run.Metrics))
                ]);
        }

        private async ValueTask<CompanionInspection> InspectTaskTargetAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = AICompanionTaskAccess.Begin(context, cancellationToken);
            CancellationToken token = lifetime.Token;
            await AICompanionTaskAccess.RequireTargetAsync(context, target, token).ConfigureAwait(false);
            if (target.TypeName == "Evaluation")
            {
                CompanionOperationResult result = EvaluationResult(
                    await ReadEvaluationAsync(context, target.NodeId, token).ConfigureAwait(false));
                return new CompanionInspection(result.Values, [s_readEvaluation], result.Summary);
            }
            var operations = new List<CompanionOperation>();
            if (target.TypeName == "Transfer")
            {
                AITransferSnapshot transfer = await AICompanionTaskAccess.ReadTransferAsync(
                    context, target.NodeId, m_timeProvider, token).ConfigureAwait(false);
                if (transfer.State == Ai.TransferStateEnum.Completed)
                {
                    operations.Add(s_readTransfer);
                }
                if (transfer.State != Ai.TransferStateEnum.Expired &&
                    await AICompanionTaskAccess.CanExecuteAsync(
                        context, target.NodeId, Ai.BrowseNames.Abort, token).ConfigureAwait(false))
                {
                    operations.Add(s_abortTransfer);
                }
                return new CompanionInspection(
                    [
                        new("Transfer ID", Text(transfer.TransferId)),
                        new("State", Variant.From(transfer.State.ToString())),
                        new("Content type", Text(transfer.ResponseContentType)),
                        new("Model used", Variant.From(transfer.ModelUsed))
                    ],
                    [.. operations],
                    "Transfer metadata only. Reading requires an explicit byte cap and caller-supplied SHA-256. " +
                    "No transfer-to-deployment association is published; existing transfers are never executed.");
            }
            FiniteStateSnapshot state = await AICompanionTaskAccess.ReadJobStateAsync(context, target.NodeId, token)
                .ConfigureAwait(false);
            operations.Add(s_observeJob);
            if (!AICompanionTaskAccess.IsTerminal(state.CurrentStateId) &&
                await AICompanionTaskAccess.CanExecuteAsync(
                    context, target.NodeId, BrowseNames.Halt, Namespaces.OpcUa, token).ConfigureAwait(false))
            {
                operations.Add(s_haltJob);
            }
            return new CompanionInspection(
                [
                    new("Job ID", Variant.From(
                        await ReadJobIdentityAsync(context, target.NodeId, token).ConfigureAwait(false))),
                    new("Program state", Variant.From(state.CurrentState)),
                    new("Program state ID", Variant.From(state.CurrentStateId)),
                    new("Observed terminal", Variant.From(AICompanionTaskAccess.IsTerminal(state.CurrentStateId)))
                ],
                [.. operations],
                "Job state only. Halted is a lifecycle state, not proof that inference succeeded.");
        }

        private static async ValueTask AppendDeploymentTasksAsync(
            CompanionContext context, CompanionTarget target, List<CompanionOperation> operations,
            CancellationToken token)
        {
            if (!await AICompanionTaskAccess.CanExecuteAsync(
                context, target.NodeId, Ai.BrowseNames.GetCapabilities, token).ConfigureAwait(false))
            {
                return;
            }
            for (int index = 0; index < s_requestTasks.Count; index++)
            {
                CompanionOperation task = s_requestTasks[index];
                if (await AICompanionTaskAccess.CanExecuteAsync(
                    context, target.NodeId, RequestMethod(task.Id), token).ConfigureAwait(false))
                {
                    operations.Add(task);
                }
            }
        }

        private static async ValueTask<ArrayOf<CompanionOperation>> LearningTasksAsync(
            CompanionContext context, CompanionTarget target, Ai.LearningJobStateEnum phase, CancellationToken token)
        {
            var operations = new List<CompanionOperation> { s_observeJob };
            for (int index = 0; index < s_learningTasks.Count; index++)
            {
                CompanionOperation task = s_learningTasks[index];
                bool applicable = task.Id switch
                {
                    "start-collection" => phase == Ai.LearningJobStateEnum.Idle,
                    "stop-collection" => phase == Ai.LearningJobStateEnum.Collecting,
                    "trigger-training" => phase is Ai.LearningJobStateEnum.Idle or Ai.LearningJobStateEnum.Labelling,
                    "promote-model" => phase == Ai.LearningJobStateEnum.Ready,
                    _ => false
                };
                if (applicable &&
                    await AICompanionTaskAccess.CanExecuteAsync(
                        context, target.NodeId, LearningMethod(task.Id), token).ConfigureAwait(false))
                {
                    operations.Add(task);
                }
            }
            if (await AICompanionTaskAccess.CanExecuteAsync(
                context, target.NodeId, BrowseNames.Halt, Namespaces.OpcUa, token).ConfigureAwait(false))
            {
                operations.Add(s_haltJob);
            }
            return [.. operations];
        }

        private static async ValueTask AppendTransfersAsync(
            CompanionContext context, AIClient client, List<CompanionTarget> targets, CancellationToken token)
        {
            if (targets.Count >= context.MaxTargets)
            {
                return;
            }
            NodeId folder = await client.GetJobsFolderIdAsync(token).ConfigureAwait(false);
            if (folder.IsNull)
            {
                return;
            }
            var type = ExpandedNodeId.ToNodeId(
                Ai.ObjectTypeIds.InferenceTransferType, context.Session.NamespaceUris);
            var budget = new IndustrialBrowseBudget(context);
            await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                context, folder, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
                NodeClass.Object, budget, token).ConfigureAwait(false))
            {
                NodeId node = IndustrialCompanionAccess.LocalId(context, reference.NodeId);
                if (!node.IsNull && IndustrialCompanionAccess.LocalId(context, reference.TypeDefinition) == type)
                {
                    targets.Add(new CompanionTarget(
                        "ai", node, reference.DisplayName.Text ?? "AI transfer", "Transfer"));
                    if (targets.Count >= context.MaxTargets)
                    {
                        break;
                    }
                }
            }
        }

        private static CompanionOperation TaskOperation(string kind, string operationId)
        {
            ArrayOf<CompanionOperation> operations = kind switch
            {
                "Deployment" => s_requestTasks,
                "Inference job" => [s_observeJob, s_haltJob],
                "Learning job" => [s_observeJob, s_haltJob, .. s_learningTasks],
                "Transfer" => [s_readTransfer, s_abortTransfer],
                "Evaluation" => [s_readEvaluation],
                _ => []
            };
            foreach (CompanionOperation operation in operations)
            {
                if (operation.Id == operationId)
                {
                    return operation;
                }
            }
            throw UnsupportedTask();
        }

        private static void RequireInputs(ArrayOf<CompanionValue> inputs, ArrayOf<CompanionInputDefinition> fields)
        {
            if (inputs.Count != fields.Count)
            {
                throw new ArgumentException("Supply the exact AI task input fields.", nameof(inputs));
            }
            for (int index = 0; index < fields.Count; index++)
            {
                if (inputs[index] is null ||
                    inputs[index].Name != fields[index].Name ||
                    inputs[index].Value.TypeInfo.BuiltInType != fields[index].DataType ||
                    inputs[index].Value.TypeInfo.ValueRank != fields[index].ValueRank)
                {
                    throw new ArgumentException("AI task input names, types or ranks do not match.", nameof(inputs));
                }
            }
        }

        private static uint ObservationCount(Variant value)
        {
            if (!value.TryGetValue(out uint reads) || reads is < 1 or > 8)
            {
                throw new ArgumentException("Choose 1 through 8 observation snapshots.");
            }
            return reads;
        }

        private static void RequireRunning(FiniteStateSnapshot state)
        {
            if (state.CurrentStateId != ObjectIds.ProgramStateMachineType_Running &&
                state.CurrentStateId != ObjectIds.ProgramStateMachineType_Suspended &&
                state.CurrentStateId != ObjectIds.ProgramStateMachineType_Ready)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The selected job cannot be halted.");
            }
        }

        private static void RequireReadableTransfer(AITransferSnapshot transfer)
        {
            if (transfer.State != Ai.TransferStateEnum.Completed)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The transfer response is not complete.");
            }
        }

        private static void RequireWritableTransfer(AITransferSnapshot transfer)
        {
            if (transfer.State is not (Ai.TransferStateEnum.Building or Ai.TransferStateEnum.Ready))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The new transfer is not writable.");
            }
        }

        private static CompanionOperationResult Accepted(string summary, NodeId resource, bool accepted = true)
        {
            return new CompanionOperationResult(summary,
                [
                    new("Method returned", Variant.From(true)),
                    new("Accepted", Variant.From(accepted)),
                    new("Observed terminal", Variant.From(false)),
                    new("Resource", Variant.From(resource))
                ]);
        }

        private static bool IsUnknownOutcome(Exception failure)
        {
            return failure is OperationCanceledException or IOException or TimeoutException or
                ObjectDisposedException ||
                (failure is ServiceResultException service &&
                    (service.StatusCode == StatusCodes.BadTimeout ||
                        service.StatusCode == StatusCodes.BadCommunicationError ||
                        service.StatusCode == StatusCodes.BadConnectionClosed ||
                        service.StatusCode == StatusCodes.BadSessionClosed ||
                        service.StatusCode == StatusCodes.BadSecureChannelClosed));
        }

        private static bool IsTransferFailure(Exception failure)
        {
            return failure is ServiceResultException or OperationCanceledException or IOException or
                InvalidOperationException or TimeoutException or ArgumentException or UnauthorizedAccessException or
                AggregateException;
        }

        private static void RequirePreparedConnection(
            CompanionContext context, AICompanionTaskInput task, CancellationToken token)
        {
            task.RequireMatch(context, task.Target, task.OperationId);
            AICompanionTaskAccess.RequireConnected(context, token);
            AICompanionTaskAccess.RequireSecureChannel(context);
        }

        private static InvalidOperationException UnknownOutcome(string method, Exception failure)
        {
            return new InvalidOperationException(
                $"{method} outcome is unknown after dispatch. No retry was sent; inspect before resubmitting.",
                failure);
        }

        private static InvalidOperationException StaleTask()
        {
            return new InvalidOperationException("The AI task's state or destination changed. Prepare it again.");
        }

        private static ServiceResultException UnsupportedTask()
        {
            return new ServiceResultException(StatusCodes.BadNotSupported, "This typed AI task is not supported.");
        }

        private static string RequestMethod(string operationId)
        {
            return operationId switch
            {
                "invoke-request" => Ai.BrowseNames.Invoke,
                "submit-inference-job" => Ai.BrowseNames.InvokeAsync,
                "submit-transfer-request" => Ai.BrowseNames.BeginTransfer,
                _ => throw UnsupportedTask()
            };
        }

        private static string LearningMethod(string operationId)
        {
            return operationId switch
            {
                "start-collection" => Ai.BrowseNames.StartCollection,
                "stop-collection" => Ai.BrowseNames.StopCollection,
                "trigger-training" => Ai.BrowseNames.TriggerTraining,
                "promote-model" => Ai.BrowseNames.PromoteModel,
                _ => throw UnsupportedTask()
            };
        }

        internal const int MaximumInlineBytes = 16384;

        private static readonly ArrayOf<CompanionInputDefinition> s_requestInputs =
        [
            new("model", "Selected model", BuiltInType.NodeId, "Existing Model NodeId used by this deployment"),
            new("capability", "Requested capability", BuiltInType.String, "Exact advertised capability, e.g. chat"),
            new("payload", "Request payload", BuiltInType.ByteString, "User-approved UTF-8 JSON bytes; never a URI"),
            new("contentType", "Content type", BuiltInType.String, "application/json"),
            new("parameters", "Call parameters", BuiltInType.ExtensionObject,
                "Empty: the current reference server does not apply overrides")
            {
                DataTypeId = new ExpandedNodeId(DataTypes.KeyValuePair),
                ValueRank = ValueRanks.OneDimension,
                ArrayDimensions = [0]
            }
        ];

        private static readonly ArrayOf<CompanionOperation> s_requestTasks =
        [
            new("invoke-request", "Invoke approved user request", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs =
                [
                    .. s_requestInputs,
                    new("timeout", "Server timeout (ms)", BuiltInType.Double, "1 through 30000")
                ]
            },
            new("submit-inference-job", "Submit approved inference job", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = s_requestInputs
            },
            new("submit-transfer-request", "Upload and submit approved request",
                CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = s_requestInputs
            }
        ];

        private static readonly CompanionOperation s_observeJob =
            new("observe-job", "Observe bounded job state/result", CompanionOperationSafety.ReadOnly)
            {
                Inputs = [new("reads", "Maximum snapshots", BuiltInType.UInt32, "1 through 8; 30-second overall cap")]
            };

        private static readonly CompanionOperation s_haltJob =
            new("halt-job", "Request job cancellation (Program Halt)", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = []
            };

        private static readonly CompanionOperation s_readTransfer =
            new("read-response", "Read bounded verified response", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs =
                [
                    new("maximumBytes", "Maximum response bytes", BuiltInType.UInt64, "1 through 1048576"),
                    new("sha256", "Expected SHA-256", BuiltInType.ByteString, "32 bytes from a trusted source")
                ]
            };

        private static readonly CompanionOperation s_abortTransfer =
            new("abort-transfer", "Abort selected transfer", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = []
            };

        private static readonly CompanionOperation s_readEvaluation =
            new("read-evaluation", "Read existing evaluation", CompanionOperationSafety.ReadOnly)
            {
                Inputs = []
            };

        private static readonly ArrayOf<CompanionInputDefinition> s_learningInputs =
        [
            new("model", "Existing base model", BuiltInType.NodeId, "Must match the learning job's BaseModel"),
            new("dataset", "Existing dataset", BuiltInType.NodeId, "Must match the learning job's Dataset")
        ];

        private static readonly ArrayOf<CompanionOperation> s_learningTasks =
        [
            new("start-collection", "Request collection start", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = s_learningInputs
            },
            new("stop-collection", "Request collection stop", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = s_learningInputs
            },
            new("trigger-training", "Request server training (if implemented)",
                CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = s_learningInputs
            },
            new("promote-model", "Request candidate promotion to one deployment",
                CompanionOperationSafety.DeploymentMutation)
            {
                Inputs =
                [
                    .. s_learningInputs,
                    new("deployment", "Exact target deployment", BuiltInType.NodeId, "Non-null; never all deployments")
                ]
            }
        ];
    }

    internal sealed record AITaskResponseFile(NodeId NodeId, ulong Size);
}
