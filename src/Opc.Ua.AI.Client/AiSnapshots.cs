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

namespace Opc.Ua.AI.Client
{
    public sealed record AINodeEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinition);

    public sealed record AIModelSnapshot
    {
        public NodeId NodeId { get; init; } = NodeId.Null;

        public string? ModelId { get; init; }

        public string? Name { get; init; }

        public string? Version { get; init; }

        public string? Framework { get; init; }

        public string? Format { get; init; }

        public string? License { get; init; }

        public ByteString Digest { get; init; } = ByteString.Empty;

        public string? DigestAlgorithm { get; init; }

        public DateTimeUtc CreatedAt { get; init; }

        public DateTimeUtc LastModifiedAt { get; init; }

        public NodeId CardId { get; init; } = NodeId.Null;

        public NodeId PublisherId { get; init; } = NodeId.Null;

        public NodeId SourceId { get; init; } = NodeId.Null;
    }

    public sealed record AIModelCardSnapshot
    {
        public NodeId NodeId { get; init; } = NodeId.Null;

        public string? IntendedUse { get; init; }

        public string? OutOfScopeUse { get; init; }

        public string? Limitations { get; init; }

        public string? EthicalConsiderations { get; init; }

        public string? TrainingDataCutoff { get; init; }

        public string? DataJurisdiction { get; init; }

        public ArrayOf<SafetyAssessmentDataType> SafetyAssessment { get; init; } = [];
    }

    public sealed record AIModelResourceSnapshot
    {
        public NodeId NodeId { get; init; } = NodeId.Null;

        public string? ArtifactUri { get; init; }

        public string? ContentType { get; init; }

        public ulong SizeBytes { get; init; }

        public ByteString Digest { get; init; } = ByteString.Empty;

        public string? DigestAlgorithm { get; init; }
    }

    public sealed record AIModelSourceSnapshot
    {
        public NodeId NodeId { get; init; } = NodeId.Null;

        public string? SourceId { get; init; }

        public string? EndpointUri { get; init; }

        public ApiDialectEnum ApiDialect { get; init; }

        public AuthenticationKindEnum AuthenticationKind { get; init; }

        public string? CredentialReference { get; init; }
    }

    public sealed record AIModelPublisherSnapshot
    {
        public NodeId NodeId { get; init; } = NodeId.Null;

        public string? Name { get; init; }

        public string? ContactUri { get; init; }

        public string? License { get; init; }
    }

    public sealed record AIDatasetSnapshot
    {
        public NodeId NodeId { get; init; } = NodeId.Null;

        public string? DatasetId { get; init; }

        public string? Name { get; init; }

        public DatasetSourceEnum SourceKind { get; init; }

        public string? ArtifactUri { get; init; }

        public string? ContentType { get; init; }

        public ulong SizeBytes { get; init; }

        public uint SampleCount { get; init; }
    }

    public sealed record AIDeploymentSnapshot
    {
        public NodeId NodeId { get; init; } = NodeId.Null;

        public string? DeploymentId { get; init; }

        public InferenceLocationEnum InferenceLocation { get; init; }

        public DeploymentStateEnum State { get; init; }

        public string? DataJurisdiction { get; init; }

        public bool EgressPermitted { get; init; }

        public ulong MaxInlinePayloadSize { get; init; }

        public string? EndpointUri { get; init; }

        public NodeId ModelId { get; init; } = NodeId.Null;

        public NodeId FallbackDeploymentId { get; init; } = NodeId.Null;
    }

    public sealed record AIInferenceJobSnapshot
    {
        public NodeId NodeId { get; init; } = NodeId.Null;

        public string? JobId { get; init; }

        public NodeId DeploymentId { get; init; } = NodeId.Null;

        public ByteString ResponsePayload { get; init; } = ByteString.Empty;

        public string? ResponseContentType { get; init; }

        public NodeId ModelUsed { get; init; } = NodeId.Null;

        public FinishReasonEnum FinishReason { get; init; }
    }

    public sealed record AILearningJobSnapshot
    {
        public NodeId NodeId { get; init; } = NodeId.Null;

        public string? JobId { get; init; }

        public LearningJobStateEnum State { get; init; }

        public double Progress { get; init; }

        public NodeId CandidateModelId { get; init; } = NodeId.Null;

        public NodeId TargetDeploymentId { get; init; } = NodeId.Null;
    }

    public sealed record AIEvaluationRunSnapshot
    {
        public NodeId NodeId { get; init; } = NodeId.Null;

        public string? RunId { get; init; }

        public NodeId EvaluatedModelId { get; init; } = NodeId.Null;

        public bool Passed { get; init; }

        public ArrayOf<EvaluationMetricDataType> Metrics { get; init; } = [];

        public string? ReportUri { get; init; }
    }

    public sealed record AITransferSnapshot
    {
        public NodeId NodeId { get; init; } = NodeId.Null;

        public string? TransferId { get; init; }

        public TransferStateEnum State { get; init; }

        public ulong BytesTransferred { get; init; }

        public NodeId ModelUsed { get; init; } = NodeId.Null;

        public string? ResponseContentType { get; init; }
    }
}
