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

using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Authenticates release-record attestations using the pinned GitHub statement verifier.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    /// <param name="runner">The service for bounded external tool execution.</param>
    internal sealed class GitHubRecordSignatureVerifier(
        EvidenceFiles files, ProcessRunner runner) : IRecordSignatureVerifier
    {
        /// <summary>
        /// Requires one authenticated release-record subject and matching record digests in the subject and predicate.
        /// </summary>
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

        /// <summary>
        /// Identifies the version-one OPC Foundation release-record attestation predicate.
        /// </summary>
        internal const string PredicateType = "https://opcfoundation.org/attestations/release-record/v1";
    }
}
