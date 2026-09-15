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

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Locates the immutable candidate, evidence, policy, and request inputs used to verify promotion eligibility.
    /// </summary>
    /// <param name="RepositoryRoot">The local repository root containing the policy and schema contracts.</param>
    /// <param name="CandidateRoot">The local root containing immutable candidate artifacts and evidence.</param>
    /// <param name="Evidence">The path to the release-evidence envelope.</param>
    /// <param name="Expected">The path to independently supplied release and artifact expectations.</param>
    /// <param name="VerificationBundle">The path to the preserved independent verification-bundle description.</param>
    /// <param name="TrustPolicy">The path to the independently anchored trusted-policy snapshot.</param>
    /// <param name="Request">The path to the exact promotion-request document.</param>
    /// <param name="Work">The output directory for temporary promotion-verification assessments.</param>
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

        /// <summary>
        /// Gets the exact promotion request accepted by independent verification.
        /// </summary>
        public PromotionRequest Request { get; }

        /// <summary>
        /// Gets the content digest of the verified promotion request.
        /// </summary>
        public string RequestDigest { get; }

        /// <summary>
        /// Gets the root containing the immutable candidate content and evidence.
        /// </summary>
        public string CandidateRoot { get; }

        /// <summary>
        /// Gets whether current verification authorizes use of an official promotion transport.
        /// </summary>
        public bool OfficialTransportAuthorized { get; }
    }
}
