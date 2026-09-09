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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Checks bounded promotion identities, artifact membership, alias operations, and exact candidate bytes.
    /// </summary>
    internal static class PromotionRequestValidator
    {
        /// <summary>
        /// Validates request structure, unique member and alias identities, candidate path bounds, and content digests.
        /// </summary>
        public static async Task ValidateAsync(
            PromotionRequest request,
            string candidateRoot,
            EvidenceFiles files,
            CancellationToken cancellationToken)
        {
            if (request.SchemaVersion != 1 ||
                request.Group is not ("nuget" or "containers" or "pump") ||
                request.Members is not { Length: >= 1 and <= 1024 } ||
                request.Attempt < 1 ||
                string.IsNullOrEmpty(request.RunId) ||
                request.RunId.Length > 20 ||
                request.RunId[0] == '0' ||
                !request.RunId.All(char.IsAsciiDigit) ||
                request.SourceSha is not { Length: 40 or 64 } ||
                !request.SourceSha.All(char.IsAsciiHexDigitLower))
            {
                throw new InvalidDataException("Invalid bounded promotion request identity.");
            }
            ValidateDigest(request.CandidateDigest);
            ValidateDigest(request.EvidenceDigest);
            ValidateDigest(request.IntentDigest);
            ValidateDigest(request.PolicyDigest);
            ValidateName(request.Destination);
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (PromotionMember member in request.Members)
            {
                if (member == null || member.Content == null || member.Evidence == null ||
                    member.Evidence.Any(e => e == null))
                {
                    throw new InvalidDataException("A required promotion member field is missing.");
                }
                ValidateName(member.Id);
                ValidateName(member.Version);
                ValidateName(member.Destination);
                if ((request.Group == "nuget" &&
                        (member.Kind is not ("nuget-package" or "nuget-symbols") || member.Platform != null)) ||
                    (request.Group != "nuget" &&
                        (member.Kind is not ("oci-index" or "oci-manifest") ||
                            (member.Kind == "oci-index"
                                ? member.Platform != null
                                : string.IsNullOrWhiteSpace(member.Platform)))))
                {
                    throw new InvalidDataException("Member kind/platform does not identify this release group.");
                }
                if (member.Platform != null)
                {
                    ValidateName(member.Platform);
                }
                if (!identities.Add(MemberIdentity(member)) ||
                    member.Evidence.Length is < 1 or > 4096)
                {
                    throw new InvalidDataException("Duplicate or incomplete promotion member.");
                }
                foreach (PromotionAlias alias in GetAliases(member))
                {
                    ValidateName(alias.Name);
                    if (alias.Name.Length > 128 ||
                        (!char.IsAsciiLetterOrDigit(alias.Name[0]) && alias.Name[0] != '_') ||
                        alias.Name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '.' or '-')))
                    {
                        throw new InvalidDataException("Alias names must use the bounded registry tag syntax.");
                    }
                    if (!aliases.Add(string.Join('\n', member.Destination, member.Id, alias.Name)) ||
                        aliases.Count > 1024)
                    {
                        throw new InvalidDataException("Duplicate alias or excessive alias operation count.");
                    }
                    if (alias.Immutable && alias.ExpectedDigest != null)
                    {
                        throw new InvalidDataException("Immutable aliases require an absence precondition.");
                    }
                    if (alias.ExpectedDigest != null)
                    {
                        ValidateDigest(alias.ExpectedDigest);
                    }
                }
                if (member.Evidence.Select(e => e.Digest).Distinct(StringComparer.Ordinal).Count() !=
                    member.Evidence.Length)
                {
                    throw new InvalidDataException("Duplicate member evidence digest.");
                }
                foreach (PromotionFile item in member.Evidence.Prepend(member.Content))
                {
                    ValidateDigest(item.Digest);
                    EvidenceFiles.ValidateRelative(item.Path);
                    if (paths.TryGetValue(item.Path, out string? previous))
                    {
                        if (previous != item.Digest)
                        {
                            throw new InvalidDataException("One candidate path has conflicting approved digests.");
                        }
                        continue;
                    }
                    paths.Add(item.Path, item.Digest);
                    if (paths.Count > 16384)
                    {
                        throw new InvalidDataException("Promotion exceeds the total object-count bound.");
                    }
                    string path = EvidenceFiles.Confined(candidateRoot, item.Path);
                    long size = new FileInfo(path).Length;
                    total = checked(total + size);
                    if (size > 512L * 1024 * 1024 || total > 8L * 1024 * 1024 * 1024)
                    {
                        throw new InvalidDataException("Promotion exceeds the offline byte bound.");
                    }
                    if (await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != item.Digest)
                    {
                        throw new PromotionRejectedException("A candidate object differs from its approved digest.");
                    }
                }
            }
        }

        /// <summary>
        /// Builds a member identity from destination, artifact kind, identifier, version, and platform.
        /// </summary>
        public static string MemberIdentity(PromotionMember member)
        {
            return string.Join('\n',
                member.Destination, member.Kind, member.Id, member.Version, member.Platform ?? string.Empty);
        }

        /// <summary>
        /// Returns the bounded alias set while rejecting mixed legacy and explicit alias representations.
        /// </summary>
        public static PromotionAlias[] GetAliases(PromotionMember member)
        {
            if (member.Aliases != null)
            {
                if (member.Alias != null || member.ExpectedAliasDigest != null ||
                    member.Aliases.Length > 32 || member.Aliases.Any(a => a == null))
                {
                    throw new InvalidDataException("Use one bounded alias representation, not mixed legacy/new fields.");
                }
                return [.. member.Aliases];
            }
            if (member.Alias != null)
            {
                return [new PromotionAlias(member.Alias, false, member.ExpectedAliasDigest)];
            }
            if (member.ExpectedAliasDigest != null)
            {
                throw new InvalidDataException("An alias precondition requires an alias name.");
            }
            return [];
        }

        /// <summary>
        /// Builds an alias identity from destination, artifact identifier, and the required selected alias name.
        /// </summary>
        public static string AliasIdentity(PromotionMember member)
        {
            if (member.Alias == null)
            {
                throw new InvalidDataException("No alias was authorized for this member.");
            }
            return string.Join('\n', member.Destination, member.Id, member.Alias);
        }

        private static void ValidateDigest(string digest)
        {
            if (digest is not { Length: 71 } || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                digest.AsSpan(7).ContainsAnyExcept("0123456789abcdef".AsSpan()))
            {
                throw new InvalidDataException("Promotion requires an exact lowercase SHA-256 digest.");
            }
        }

        private static void ValidateName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsControl))
            {
                throw new InvalidDataException("Malformed promotion destination or member name.");
            }
        }
    }
}
