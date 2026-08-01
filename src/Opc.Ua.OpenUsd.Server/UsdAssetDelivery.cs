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
using System.Threading;
using Opc.Ua;
using Opc.Ua.OpenUsd;

namespace Opc.Ua.OpenUsd.Server
{
    /// <summary>
    /// One artist-authored USD layer the server delivers through the address space
    /// (spec §5.15, conformance unit OU-AssetDelivery).
    /// </summary>
    public sealed class ServedAsset
    {
        public ServedAsset(string identifier, OpenUsdAssetKindEnum kind, byte[] bytes, string mediaType = "model/vnd.usda")
        {
            Identifier = identifier;
            Kind = kind;
            Bytes = bytes;
            MediaType = mediaType;
        }

        public string Identifier { get; }
        public OpenUsdAssetKindEnum Kind { get; }
        public byte[] Bytes { get; }
        public string MediaType { get; }
    }

    /// <summary>
    /// Serves the draft OPC UA — OpenUSD Bindings asset-content-delivery capability
    /// (spec §5.15): builds a stage's <c>Assets</c> folder of <c>OpenUsdAssetType</c>
    /// instances, each exposing an artist-authored USD layer as a read-only Part 5
    /// <see cref="FileState"/> (Open/Read/Close streaming) plus a SHA-256 digest, so a
    /// generic connector can fetch, verify, cache, compose, and render the twin with no
    /// external asset resolver.
    /// </summary>
    public static class UsdAssetDelivery
    {
        /// <summary>
        /// Attaches an <c>Assets</c> folder (if absent) to the stage and materialises one
        /// <c>OpenUsdAssetType</c> per served layer. Call BEFORE the facility's per-instance
        /// NodeId assignment + registration; returns the created assets so a caller can link a
        /// component binding's <c>ComponentAssetNode</c> to one after NodeIds are assigned.
        /// </summary>
        public static ArrayOf<OpenUsdAssetState> AttachStageAssets(
            ISystemContext context, OpenUsdStageState stage, ushort openUsdNs, ArrayOf<ServedAsset> assets)
        {
            var created = new List<OpenUsdAssetState>();
            // Assets is an optional member, so it is not auto-created by the factory;
            // supply a generated node (passing null! leaves it unnamed/untyped).
            FolderState folder = stage.Assets ?? stage.CreateOrReplaceAssets(
                context, context.CreateOpenUsdStageType_Assets(stage, forInstance: true));

            foreach (ServedAsset a in assets)
            {
                OpenUsdAssetState asset = context.CreateInstanceOfOpenUsdAssetType(
                    folder, new QualifiedName(SafeBrowseName(a.Identifier), openUsdNs));
                asset.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                folder.AddChild(asset);

                asset.CreateOrReplaceAssetIdentifier(context, null!).Value = a.Identifier;
                asset.CreateOrReplaceAssetKind(context, null!).Value = a.Kind;
                asset.CreateOrReplaceMediaType(
                    context, context.CreateOpenUsdAssetType_MediaType(asset, forInstance: true)).Value = a.MediaType;

                byte[] digest;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    digest = sha.ComputeHash(a.Bytes);
                }
#pragma warning restore CA1850
                asset.CreateOrReplaceDigest(
                    context, context.CreateOpenUsdAssetType_Digest(asset, forInstance: true)).Value = (ByteString)digest;
                asset.CreateOrReplaceDigestAlgorithm(
                    context, context.CreateOpenUsdAssetType_DigestAlgorithm(asset, forInstance: true))
                    .Value = OpenUsdDigestAlgorithmEnum.Sha256;

                // The asset node itself IS the Part 5 file (OpenUsdAssetType : FileType);
                // wire the read-only streaming handlers directly onto it.
                WireReadOnlyFile(asset, a.Bytes, a.MediaType);
                created.Add(asset);
            }
            return created;
        }

        // Attaches read-only Open/Read/Close/GetPosition/SetPosition handlers over an
        // in-memory byte[] to an existing (generator-materialised) FileState.
        private static void WireReadOnlyFile(FileState file, byte[] bytes, string mediaType)
        {
            var backing = new ReadOnlyFileBacking(bytes);

            if (file.Size != null)
            {
                file.Size.Value = (ulong)bytes.Length;
            }
            if (file.Writable != null)
            {
                file.Writable.Value = false;
            }
            if (file.UserWritable != null)
            {
                file.UserWritable.Value = false;
            }
            if (file.MimeType != null)
            {
                file.MimeType.Value = mediaType;
            }
            if (file.Open != null)
            {
                file.Open.OnCall = backing.OnOpen;
            }
            if (file.Read != null)
            {
                file.Read.OnCall = backing.OnRead;
            }
            if (file.Close != null)
            {
                file.Close.OnCall = backing.OnClose;
            }
            if (file.GetPosition != null)
            {
                file.GetPosition.OnCall = backing.OnGetPosition;
            }
            if (file.SetPosition != null)
            {
                file.SetPosition.OnCall = backing.OnSetPosition;
            }
            // Read-only: any Write method stays unwired (server rejects the call).
        }

        // Sanitises an asset identifier into a valid single-segment BrowseName.
        private static string SafeBrowseName(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return "Asset";
            }
            var sb = new System.Text.StringBuilder(identifier.Length);
            foreach (char c in identifier)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }
            string s = sb.ToString();
            return char.IsLetter(s[0]) || s[0] == '_' ? s : "_" + s;
        }

        /// <summary>
        /// Read-only in-memory backing for a Part 5 <see cref="FileState"/>: hands out
        /// per-Open file handles over an independent position cursor, bounded to a small
        /// number of concurrent handles.
        /// </summary>
        private sealed class ReadOnlyFileBacking
        {
            private const int MaxConcurrentHandles = 8;
            private readonly byte[] m_bytes;
            private readonly Lock m_gate = new();
            private readonly Dictionary<uint, long> m_positions = new();
            private uint m_nextHandle;

            public ReadOnlyFileBacking(byte[] bytes)
            {
                m_bytes = bytes;
            }

            public ServiceResult OnOpen(ISystemContext context, MethodState method,
                NodeId objectId, byte mode, ref uint fileHandle)
            {
                // Part 5 Open mode: bit 0 = Read. Reject write/append/erase (this is read-only).
                if ((mode & 0x01) == 0 || (mode & 0x06) != 0)
                {
                    fileHandle = 0;
                    return ServiceResult.Create(StatusCodes.BadInvalidArgument,
                        "This file is read-only; open with Read mode only.");
                }
                lock (m_gate)
                {
                    if (m_positions.Count >= MaxConcurrentHandles)
                    {
                        fileHandle = 0;
                        return ServiceResult.Create(StatusCodes.BadTooManyOperations,
                            "Too many concurrent file handles.");
                    }
                    fileHandle = ++m_nextHandle;
                    m_positions[fileHandle] = 0;
                }
                return ServiceResult.Good;
            }

            public ServiceResult OnRead(ISystemContext context, MethodState method,
                NodeId objectId, uint fileHandle, int length, ref ByteString data)
            {
                if (length < 0)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidArgument, "Negative length.");
                }
                lock (m_gate)
                {
                    if (!m_positions.TryGetValue(fileHandle, out long pos))
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidState, "File handle not open.");
                    }
                    long remaining = m_bytes.Length - pos;
                    int n = (int)Math.Max(0, Math.Min(length, remaining));
                    var buffer = new byte[n];
                    Array.Copy(m_bytes, pos, buffer, 0, n);
                    m_positions[fileHandle] = pos + n;
                    data = (ByteString)buffer;
                }
                return ServiceResult.Good;
            }

            public ServiceResult OnClose(ISystemContext context, MethodState method,
                NodeId objectId, uint fileHandle)
            {
                lock (m_gate)
                {
                    m_positions.Remove(fileHandle);
                }
                return ServiceResult.Good;
            }

            public ServiceResult OnGetPosition(ISystemContext context, MethodState method,
                NodeId objectId, uint fileHandle, ref ulong position)
            {
                lock (m_gate)
                {
                    if (!m_positions.TryGetValue(fileHandle, out long pos))
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidState, "File handle not open.");
                    }
                    position = (ulong)pos;
                }
                return ServiceResult.Good;
            }

            public ServiceResult OnSetPosition(ISystemContext context, MethodState method,
                NodeId objectId, uint fileHandle, ulong position)
            {
                lock (m_gate)
                {
                    if (!m_positions.ContainsKey(fileHandle))
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidState, "File handle not open.");
                    }
                    m_positions[fileHandle] = (long)Math.Min(position, (ulong)m_bytes.Length);
                }
                return ServiceResult.Good;
            }
        }
    }
}
