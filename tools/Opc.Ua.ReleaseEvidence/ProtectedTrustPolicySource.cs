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

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Loads trust policy from an independently anchored digest outside candidate-controlled input roots.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    internal sealed class ProtectedTrustPolicySource(EvidenceFiles files) : ITrustPolicySource
    {
        /// <summary>
        /// Loads the anchored policy and validates protected tool locations, or returns null when no anchor is
        /// configured.
        /// </summary>
        public async Task<TrustedPolicySnapshot?> LoadAsync(
            string? path, string[] candidateRoots, CancellationToken cancellationToken)
        {
            if (path == null)
            {
                return null;
            }
            string? anchor = Environment.GetEnvironmentVariable("OPCUA_RELEASE_TRUST_POLICY_SHA256");
            if (string.IsNullOrEmpty(anchor))
            {
                return null;
            }
            RequireOutside(path, candidateRoots);
            if (!VerificationControls.IsDigest(anchor) ||
                await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != anchor)
            {
                throw new InvalidDataException("Protected bootstrap digest does not authenticate the trust policy.");
            }
            TrustedPolicySnapshot policy = await files.ReadModelAsync(
                path, VerificationJsonContext.Default.TrustedPolicySnapshot, cancellationToken).ConfigureAwait(false);
            if (policy.Authority !=
                "github:OPCFoundation/UA-.NETStandard:.github/workflows/release.yml:release")
            {
                throw new InvalidDataException("Trust bootstrap does not identify the approved release authority.");
            }
            RequireOutside(policy.Tool.Path, candidateRoots);
            RequireOutside(policy.TrustedRoot.Path, candidateRoots);
            if (policy.NugetVerifier != null)
            {
                RequireOutside(policy.NugetVerifier.Path, candidateRoots);
            }
            if (policy.CosignVerifier != null)
            {
                RequireOutside(policy.CosignVerifier.Path, candidateRoots);
            }
            return policy;
        }

        /// <summary>
        /// Requires a fully qualified local path outside every candidate root and rejects link-based redirection.
        /// </summary>
        internal static void RequireOutside(string path, string[] candidateRoots)
        {
            if (!Path.IsPathFullyQualified(path))
            {
                throw new InvalidDataException("Protected bootstrap paths must be fully qualified.");
            }
            EvidenceFiles.RejectLinks(path);
            string fullPath = Path.GetFullPath(path);
            foreach (string root in candidateRoots)
            {
                string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                if (string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Trust material or verifier is inside candidate inputs.");
                }
            }
        }
    }
}
