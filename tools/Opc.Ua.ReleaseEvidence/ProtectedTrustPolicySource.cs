// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed class ProtectedTrustPolicySource(EvidenceFiles files) : ITrustPolicySource
    {
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
