// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
