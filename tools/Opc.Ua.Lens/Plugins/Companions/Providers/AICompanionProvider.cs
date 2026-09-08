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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AI.Client;
using AiNamespaces = Opc.Ua.AI.Namespaces;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Typed AI catalogue inspection with an explicitly gated, fixed local sample request.
/// Inference hosting and vendor SDKs are not part of the desktop dependency graph.
/// </summary>
internal sealed class AICompanionProvider : ICompanionProvider
{
    public CompanionDescriptor Descriptor { get; } = new(
        "ai", "AI Model Management", AiNamespaces.AI, "Draft AI Model Management and Inference");

    public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
        CompanionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var client = new AIClient(context.Session, context.Telemetry);
        if (!client.IsAINamespaceAvailable)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported, "The server does not expose the AI model.");
        }

        var targets = new List<CompanionTarget>();
        await AppendAsync(client.EnumerateModelsAsync(cancellationToken), "Model", context, targets, cancellationToken)
            .ConfigureAwait(false);
        await AppendAsync(
            client.EnumerateDeploymentsAsync(cancellationToken), "Deployment", context, targets, cancellationToken)
            .ConfigureAwait(false);
        await AppendAsync(client.EnumerateDatasetsAsync(cancellationToken), "Dataset", context, targets, cancellationToken)
            .ConfigureAwait(false);
        await AppendAsync(
            client.EnumerateLearningJobsAsync(cancellationToken), "Learning job", context, targets, cancellationToken)
            .ConfigureAwait(false);
        return [.. targets];
    }

    public async ValueTask<CompanionInspection> InspectAsync(
        CompanionContext context,
        CompanionTarget target,
        CancellationToken cancellationToken)
    {
        ValidateTarget(context, target);
        var client = new AIClient(context.Session, context.Telemetry);
        switch (target.TypeName)
        {
            case "Model":
                AIModelSnapshot model = await client.Model(target.NodeId).ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                return new CompanionInspection(
                    [
                        new("Model ID", Text(model.ModelId)),
                        new("Name", Text(model.Name)),
                        new("Version", Text(model.Version)),
                        new("Framework", Text(model.Framework)),
                        new("Format", Text(model.Format)),
                        new("License", Text(model.License)),
                        new("Digest", Variant.From(model.Digest)),
                        new("Digest algorithm", Text(model.DigestAlgorithm)),
                        new("Created", Variant.From(model.CreatedAt)),
                        new("Modified", Variant.From(model.LastModifiedAt)),
                        new("Source", Variant.From(model.SourceId))
                    ],
                    [],
                    "Read-only model metadata. No model download or execution was requested.");
            case "Deployment":
                AIDeploymentSnapshot deployment = await client.Deployment(target.NodeId).ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                ArrayOf<CompanionOperation> operations = IsLocalBackend(deployment)
                    ? [
                        new("capabilities", "Read capabilities", CompanionOperationSafety.ReadOnly),
                        new("invoke-sample", "Invoke fixed local sample", CompanionOperationSafety.SampleMutation)
                    ]
                    : [new("capabilities", "Read capabilities", CompanionOperationSafety.ReadOnly)];
                return new CompanionInspection(
                    [
                        new("Deployment ID", Text(deployment.DeploymentId)),
                        new("State", Variant.From(deployment.State.ToString())),
                        new("Inference location", Variant.From(deployment.InferenceLocation.ToString())),
                        new("Data jurisdiction", Text(deployment.DataJurisdiction)),
                        new("Egress permitted", Variant.From(deployment.EgressPermitted)),
                        new("Inline byte limit", Variant.From(deployment.MaxInlinePayloadSize)),
                        new("Model", Variant.From(deployment.ModelId)),
                        new("Fallback deployment", Variant.From(deployment.FallbackDeploymentId))
                    ],
                    operations,
                    IsLocalBackend(deployment)
                        ? "Sample Invoke sends fixed synthetic text only after local sample confirmation."
                        : "Sample Invoke requires a configured loopback backend with egress disabled.");
            case "Dataset":
                AIDatasetSnapshot dataset = await client.Dataset(target.NodeId).ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                return new CompanionInspection(
                    [
                        new("Dataset ID", Text(dataset.DatasetId)),
                        new("Name", Text(dataset.Name)),
                        new("Source kind", Variant.From(dataset.SourceKind.ToString())),
                        new("Content type", Text(dataset.ContentType)),
                        new("Size bytes", Variant.From(dataset.SizeBytes)),
                        new("Sample count", Variant.From(dataset.SampleCount))
                    ],
                    [],
                    "Dataset metadata only; no dataset contents were downloaded.");
            case "Learning job":
                AILearningJobSnapshot job = await client.LearningJob(target.NodeId).ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                return new CompanionInspection(
                    [
                        new("Job ID", Text(job.JobId)),
                        new("State", Variant.From(job.State.ToString())),
                        new("Progress", Variant.From(job.Progress)),
                        new("Candidate model", Variant.From(job.CandidateModelId)),
                        new("Target deployment", Variant.From(job.TargetDeploymentId))
                    ],
                    [],
                    "Learning-job observation does not start training or promote a model.");
            default:
                throw new ServiceResultException(StatusCodes.BadNotSupported, "This AI instance kind is not supported.");
        }
    }

    public async ValueTask<CompanionOperationResult> ExecuteAsync(
        CompanionContext context,
        CompanionTarget target,
        string operationId,
        string? input,
        CancellationToken cancellationToken)
    {
        ValidateTarget(context, target);
        if (target.TypeName != "Deployment")
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported, "Select an AI deployment for this task.");
        }
        var client = new AIClient(context.Session, context.Telemetry);
        AIDeploymentClient deployment = client.Deployment(target.NodeId);
        if (operationId == "capabilities")
        {
            ArrayOf<Opc.Ua.AI.CapabilityDataType> capabilities = await deployment
                .GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            if (capabilities.Count > context.MaxFields)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, "Too many capability records.");
            }
            return new CompanionOperationResult(
                "Read the deployment's advertised capabilities.",
                [
                    new("Capability count", Variant.From(capabilities.Count)),
                    new("Capabilities", Variant.FromStructure(capabilities))
                ]);
        }
        if (operationId != "invoke-sample" || !string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Only the fixed local sample request is supported.", nameof(operationId));
        }
        AIDeploymentSnapshot snapshot = await deployment.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!IsLocalBackend(snapshot) ||
            !Uri.TryCreate(context.Session.Endpoint.EndpointUrl, UriKind.Absolute, out Uri? server) ||
            !server.IsLoopback)
        {
            throw new InvalidOperationException("Both the sample server and its no-egress backend must be loopback.");
        }
        ByteString payload = ByteString.From(
            "{\"messages\":[{\"role\":\"user\",\"content\":\"Reply with UaLens sample.\"}]}"u8);
        if (snapshot.MaxInlinePayloadSize != 0 && (ulong)payload.Length > snapshot.MaxInlinePayloadSize)
        {
            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, "The sample exceeds the inline limit.");
        }
        AIInvokeResult result = await deployment.InvokeAsync(
            payload, "application/json", [], 10_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.TransferRequired)
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, "The response needs the separate transfer workflow.");
        }
        if (result.ResponsePayload.Length > 16384)
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, "The sample response exceeds the 16384-byte display limit.");
        }
        return new CompanionOperationResult(
            $"Sample Invoke returned {result.FinishReason}; no further operation was started.",
            [
                new("Finish reason", Variant.From(result.FinishReason.ToString())),
                new("Content type", Text(result.ResponseContentType)),
                new("Response bytes", Variant.From(result.ResponsePayload.Length)),
                new("Response", Text(Encoding.UTF8.GetString(result.ResponsePayload.Span))),
                new("Model used", Variant.From(result.ModelUsed)),
                new("Retry after", Variant.From(result.RetryAfter))
            ]);
    }

    private async Task AppendAsync(
        IAsyncEnumerable<AINodeEntry> source,
        string typeName,
        CompanionContext context,
        List<CompanionTarget> targets,
        CancellationToken cancellationToken)
    {
        if (targets.Count >= context.MaxTargets)
        {
            return;
        }
        await foreach (AINodeEntry entry in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            string name = entry.DisplayName.Text ?? entry.BrowseName.Name ?? entry.NodeId.ToString();
            targets.Add(new CompanionTarget(Descriptor.Id, entry.NodeId, name, typeName));
            if (targets.Count >= context.MaxTargets)
            {
                break;
            }
        }
    }

    private void ValidateTarget(CompanionContext context, CompanionTarget target)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        if (target.ProviderId != Descriptor.Id || target.NodeId.IsNull)
        {
            throw new ArgumentException("The selected target does not belong to the AI provider.", nameof(target));
        }
    }

    private static bool IsLocalBackend(AIDeploymentSnapshot deployment)
    {
        return !deployment.EgressPermitted &&
            Uri.TryCreate(deployment.EndpointUri, UriKind.Absolute, out Uri? endpoint) &&
            endpoint.Scheme is "http" or "https" &&
            endpoint.IsLoopback &&
            string.IsNullOrEmpty(endpoint.UserInfo) &&
            string.IsNullOrEmpty(endpoint.Query) &&
            string.IsNullOrEmpty(endpoint.Fragment);
    }

    private static Variant Text(string? value)
    {
        return value is null ? Variant.Null : Variant.From(value);
    }
}
