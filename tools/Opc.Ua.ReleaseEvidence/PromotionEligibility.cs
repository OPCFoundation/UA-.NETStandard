// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed record PromotionVerificationInput(
        string RepositoryRoot,
        string CandidateRoot,
        string Evidence,
        string Expected,
        string VerificationBundle,
        string TrustPolicy,
        string Request,
        string Work);

    /// <summary>
    /// Unforgeable outside the verified core factory; never deserialized from an assessment file.
    /// </summary>
    internal sealed partial class VerifiedPromotion
    {
        private VerifiedPromotion(
            PromotionRequest request,
            string requestDigest,
            string candidateRoot,
            bool officialTransportAuthorized)
        {
            Request = request;
            RequestDigest = requestDigest;
            CandidateRoot = candidateRoot;
            OfficialTransportAuthorized = officialTransportAuthorized;
        }

        public PromotionRequest Request { get; }

        public string RequestDigest { get; }

        public string CandidateRoot { get; }

        public bool OfficialTransportAuthorized { get; }
    }
}
