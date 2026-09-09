// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed class GitHubRecordSignatureVerifier(
        EvidenceFiles files, ProcessRunner runner) : IRecordSignatureVerifier
    {
        public async Task<bool> VerifyAsync(
            string recordPath,
            string bundlePath,
            VerificationAuthority authority,
            TrustedPolicySnapshot policy,
            CancellationToken cancellationToken)
        {
            string digest = await files.DigestAsync(recordPath, cancellationToken).ConfigureAwait(false);
            using JsonDocument? verified = await new GitHubStatementSignatureVerifier(files, runner).VerifyAsync(
                recordPath, bundlePath, PredicateType, authority, policy, cancellationToken).ConfigureAwait(false);
            if (verified == null ||
                await files.DigestAsync(recordPath, cancellationToken).ConfigureAwait(false) != digest)
            {
                return false;
            }
            JsonElement statement = verified.RootElement;
            JsonElement subjects = statement.GetProperty("subject");
            return statement.GetProperty("_type").GetString() == "https://in-toto.io/Statement/v1" &&
                statement.GetProperty("predicateType").GetString() == PredicateType &&
                subjects.GetArrayLength() == 1 &&
                subjects[0].GetProperty("name").GetString() == Path.GetFileName(recordPath) &&
                "sha256:" + subjects[0].GetProperty("digest").GetProperty("sha256").GetString() == digest &&
                statement.GetProperty("predicate").GetProperty("schemaVersion").GetInt32() == 1 &&
                statement.GetProperty("predicate").GetProperty("recordDigest").GetString() == digest;
        }

        internal const string PredicateType = "https://opcfoundation.org/attestations/release-record/v1";
    }
}
