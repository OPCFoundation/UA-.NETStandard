/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Asset content delivery (spec §5.15): when a stage advertises an <c>Assets</c>
    /// folder, the connector streams the server-delivered USD layers (Part 5 FileType),
    /// verifies each layer's digest, and writes them to a local cache preserving the
    /// <c>AssetIdentifier</c> relative paths — so a viewer renders the twin with no
    /// external asset resolver. When the facility is absent the root layer is resolved
    /// externally as before (fully backward compatible).
    /// </summary>
    public sealed partial class OpenUsdConnector
    {
        /// <summary>
        /// One layer fetched from the server into the local cache.
        /// </summary>
        public sealed class FetchedAsset
        {
            public string Identifier { get; set; } = string.Empty;
            public OpenUsdAssetKind Kind { get; set; }
            public string LocalPath { get; set; } = string.Empty;
            public int Length { get; set; }
            public bool DigestVerified { get; set; }
        }

        /// <summary>
        /// Fetches every served stage-asset closure into <paramref name="cacheDir"/>,
        /// verifying each layer's digest (fail-closed) and preserving relative
        /// <c>AssetIdentifier</c> paths. Returns the fetched assets (empty when no stage
        /// advertises an <c>Assets</c> facility). Layers are de-duplicated by identifier
        /// across representations that share a stage.
        /// </summary>
        public async Task<List<FetchedAsset>> FetchServedAssetsAsync(string cacheDir, CancellationToken ct)
        {
            var result = new List<FetchedAsset>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            long totalBytes = 0;
            Directory.CreateDirectory(cacheDir);

            foreach (RepresentationInfo rep in await DiscoverAllRepresentationsAsync(ct).ConfigureAwait(false))
            {
                if (rep.StageNodeId.IsNull)
                {
                    continue;
                }
                Dictionary<string, NodeId> stageChildren =
                    await ChildrenByNameAsync(rep.StageNodeId, ct).ConfigureAwait(false);
                if (!stageChildren.TryGetValue("Assets", out NodeId assetsFolder))
                {
                    continue;
                }
                foreach ((NodeId assetId, NodeId typeDef) in
                    await ChildrenWithTypeAsync(assetsFolder, ct).ConfigureAwait(false))
                {
                    if (assetId.IsNull || typeDef != m_assetTypeId)
                    {
                        continue;
                    }
                    Dictionary<string, NodeId> ap = await ChildrenByNameAsync(assetId, ct).ConfigureAwait(false);
                    string? identifier = await ReadStringAsync(ap, "AssetIdentifier", ct).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(identifier) || !seen.Add(identifier!))
                    {
                        continue;
                    }
                    int kind = await ReadInt32Async(ap, "AssetKind", ct).ConfigureAwait(false);
                    ByteString digest = await ReadByteStringAsync(ap, "Digest", ct).ConfigureAwait(false);
                    int alg = await ReadInt32Async(ap, "DigestAlgorithm", ct).ConfigureAwait(false);

                    // The asset node itself is the Part 5 file (OpenUsdAssetType : FileType):
                    // stream its bytes through its own Open/Read/Close children.
                    byte[] bytes = await ReadServedFileAsync(assetId, ct).ConfigureAwait(false);

                    totalBytes += bytes.Length;
                    if (totalBytes > m_options.MaxTotalAssetBytes)
                    {
                        throw new InvalidOperationException(
                            $"Served asset closure exceeds the maximum total size of {m_options.MaxTotalAssetBytes} bytes.");
                    }

                    // §5.15: the connector verifies each digest and shall not silently mix
                    // unverified delivered bytes into the stage.
                    bool verified = VerifyDeliveredAsset(
                        identifier!, bytes, digest, (OpenUsdDigestAlgorithm)alg, m_options.RequireAssetDigests);

                    string localPath = WriteAssetToCache(cacheDir, identifier!, bytes);
                    result.Add(new FetchedAsset
                    {
                        Identifier = identifier!,
                        Kind = (OpenUsdAssetKind)kind,
                        LocalPath = localPath,
                        Length = bytes.Length,
                        DigestVerified = verified
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// §5.15: "the connector … verif[ies] each digest" and "shall not silently mix
        /// unverified delivered bytes into the stage." Verification therefore starts
        /// <c>false</c>: an asset delivered <b>without</b> a digest is unverifiable and is
        /// never reported as verified — it is refused outright when
        /// <see cref="OpenUsdConnectorOptions.RequireAssetDigests"/> is set (the default),
        /// and otherwise surfaced with <c>DigestVerified = false</c> so it cannot be
        /// silently composed. A digest that is present but does not match always throws.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal static bool VerifyDeliveredAsset(
            string identifier,
            byte[] bytes,
            ByteString digest,
            OpenUsdDigestAlgorithm algorithm,
            bool requireDigests)
        {
            if (digest is { IsNull: false, Length: > 0 })
            {
                if (!VerifyBytesDigest(bytes, digest, algorithm))
                {
                    throw new InvalidOperationException(
                        $"Served asset '{identifier}' failed digest verification — refusing to cache.");
                }
                return true;
            }
            if (requireDigests)
            {
                throw new InvalidOperationException(
                    $"Served asset '{identifier}' was delivered without a digest — refusing to cache.");
            }
            return false;
        }

        // Streams a served layer's bytes through the Part 5 FileType (Open read → Read
        // loop → Close). The File's method NodeIds are its Open/Read/Close children.
        private async Task<byte[]> ReadServedFileAsync(NodeId fileNodeId, CancellationToken ct)
        {
            Dictionary<string, NodeId> methods = await ChildrenByNameAsync(fileNodeId, ct).ConfigureAwait(false);
            if (!methods.TryGetValue("Open", out NodeId openId)
                || !methods.TryGetValue("Read", out NodeId readId)
                || !methods.TryGetValue("Close", out NodeId closeId))
            {
                throw new InvalidOperationException("Served asset File is missing Part 5 Open/Read/Close methods.");
            }

            // Open mode 1 = Read (OPC 10000-5 §11.3.3).
            ArrayOf<Variant> openOut = await m_session.CallAsync(
                fileNodeId, openId, ct, new Variant((byte)1)).ConfigureAwait(false);
                if (openOut.Count == 0)
                {
                    throw new InvalidOperationException("Served asset Open returned no file handle.");
                }
                if (!VariantConversions.TryGetInt64(openOut[0], out long fileHandle))
                {
                    throw new InvalidOperationException("Served asset Open returned no file handle.");
                }
                var handle = unchecked((uint)fileHandle);

                var buffer = new List<byte>();
                try
                {
                    const int chunkSize = 8192;
                    while (true)
                    {
                        ArrayOf<Variant> readOut = await m_session.CallAsync(
                            fileNodeId, readId, ct, new Variant(handle), new Variant(chunkSize)).ConfigureAwait(false);
                        byte[] chunk = readOut.Count > 0 &&
                            VariantConversions.TryGetBytes(readOut[0], out ByteString bytes)
                            ? bytes.ToArray()
                            : Array.Empty<byte>();
                        if (chunk.Length > 0)
                    {
                            if (buffer.Count + (long)chunk.Length > m_options.MaxAssetBytes)
                            {
                                // Fail-closed against a server that streams unbounded data
                                // (never returning a short read) — bound memory and disk use.
                                throw new InvalidOperationException(
                                    $"Served asset exceeds the maximum size of {m_options.MaxAssetBytes} bytes.");
                            }
                            buffer.AddRange(chunk);
                        }
                        if (chunk.Length < chunkSize)
                        {
                            break;
                        }
                    }
                }
            finally
            {
                await m_session.CallAsync(fileNodeId, closeId, ct, new Variant(handle)).ConfigureAwait(false);
            }
            return buffer.ToArray();
        }

        /// <summary>
        /// Streams the served root-layer bytes of a stage (the <c>Assets</c> member whose
        /// <c>AssetKind</c> is <see cref="OpenUsdAssetKind.RootLayer"/>, else the member
        /// whose <c>AssetIdentifier</c> matches the stage's <c>RootLayerIdentifier</c>).
        /// Returns <c>null</c> when the stage does not serve its content, in which case
        /// §5.2 verification cannot be performed and the stage stays unverified.
        /// </summary>
        internal async Task<byte[]?> TryReadRootLayerBytesAsync(
            RepresentationInfo rep, CancellationToken ct)
        {
            if (rep.StageNodeId.IsNull)
            {
                return null;
            }
            Dictionary<string, NodeId> stageChildren;
            try
            {
                stageChildren = await ChildrenByNameAsync(rep.StageNodeId, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                return null;
            }
            if (!stageChildren.TryGetValue("Assets", out NodeId assetsFolder))
            {
                return null;
            }
            NodeId fallback = NodeId.Null;
            foreach ((NodeId assetId, NodeId typeDef) in
                await ChildrenWithTypeAsync(assetsFolder, ct).ConfigureAwait(false))
            {
                if (assetId.IsNull || typeDef != m_assetTypeId)
                {
                    continue;
                }
                Dictionary<string, NodeId> ap = await ChildrenByNameAsync(assetId, ct).ConfigureAwait(false);
                var kind = (OpenUsdAssetKind)await ReadInt32Async(ap, "AssetKind", ct).ConfigureAwait(false);
                if (kind == OpenUsdAssetKind.RootLayer)
                {
                    return await ReadServedFileAsync(assetId, ct).ConfigureAwait(false);
                }
                string? identifier = await ReadStringAsync(ap, "AssetIdentifier", ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(identifier) &&
                    string.Equals(identifier, rep.RootLayerIdentifier, StringComparison.Ordinal))
                {
                    fallback = assetId;
                }
            }
            return fallback.IsNull
                ? null
                : await ReadServedFileAsync(fallback, ct).ConfigureAwait(false);
        }

        private static bool VerifyBytesDigest(byte[] bytes, ByteString digest, OpenUsdDigestAlgorithm alg)
        {
            byte[] computed;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
            using (System.Security.Cryptography.HashAlgorithm h = alg switch
            {
                OpenUsdDigestAlgorithm.Sha384 => System.Security.Cryptography.SHA384.Create(),
                OpenUsdDigestAlgorithm.Sha512 => System.Security.Cryptography.SHA512.Create(),
                _ => System.Security.Cryptography.SHA256.Create()
            })
            {
                computed = h.ComputeHash(bytes);
            }
#pragma warning restore CA1850
            ReadOnlySpan<byte> expected = digest.Span;
            if (computed.Length != expected.Length)
            {
                return false;
            }
            int diff = 0;
            for (int i = 0; i < computed.Length; i++)
            {
                diff |= computed[i] ^ expected[i];
            }
            return diff == 0;
        }

        // Writes a fetched layer to the cache at its (sanitised) relative identifier so
        // that @...@ references resolve locally. Rejects path traversal / absolute paths.
        private static string WriteAssetToCache(string cacheDir, string identifier, byte[] bytes)
        {
            string root = Path.GetFullPath(cacheDir);
            string rel = SanitizeRelativePath(identifier);
            string full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !string.Equals(full, root, StringComparison.Ordinal))
            {
                // Traversal attempt — collapse to the file name inside the cache root.
                full = Path.Combine(root, Path.GetFileName(rel));
            }
            string? dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllBytes(full, bytes);
            return full;
        }

        private static readonly char[] s_pathSeparators = { '/' };

        private static string SanitizeRelativePath(string identifier)
        {
            string s = identifier.Trim().Trim('@').Replace('\\', '/');
            // Drop an anchor prim suffix if authored as @file@</Prim> (strip from the first '<').
            int lt = -1;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<')
                {
                    lt = i;
                    break;
                }
            }
            if (lt >= 0)
            {
                s = s.Substring(0, lt);
            }
            string[] parts = s.Split(s_pathSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p != "." && p != ".." && !p.Any(ch => ch == ':'))
                .ToArray();
            string rel = string.Join(Path.DirectorySeparatorChar.ToString(), parts);
            return string.IsNullOrEmpty(rel) ? "asset.usda" : rel;
        }
    }
}
