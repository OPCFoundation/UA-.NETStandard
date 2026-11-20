/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.AI.Client
{
    public sealed record AIInvokeResult
    {
        public ByteString ResponsePayload { get; init; } = ByteString.Empty;

        public string? ResponseContentType { get; init; }

        public NodeId ModelUsed { get; init; } = NodeId.Null;

        public UsageDataType? Usage { get; init; }

        public FinishReasonEnum FinishReason { get; init; }

        public ArrayOf<SafetyAssessmentDataType> SafetyAssessment { get; init; } = [];

        public double RetryAfter { get; init; }

        public bool TransferRequired { get; init; }

        public NodeId TransferId { get; init; } = NodeId.Null;
    }

    public sealed record AIBeginTransferResult
    {
        public NodeId TransferId { get; init; } = NodeId.Null;

        public bool Accepted { get; init; }
    }

    public sealed record AISourceModelListResult
    {
        public ArrayOf<ModelReferenceDataType> Models { get; init; } = [];

        public ByteString ContinuationPoint { get; init; } = ByteString.Empty;
    }

    public sealed record AISourceConnectionResult
    {
        public bool Reachable { get; init; }

        public LocalizedText Detail { get; init; } = LocalizedText.Null;
    }
}
