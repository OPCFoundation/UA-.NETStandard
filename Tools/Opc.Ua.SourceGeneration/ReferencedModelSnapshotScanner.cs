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
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Opc.Ua.SourceGeneration.Snapshot;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// A scanned <c>[assembly: Opc.Ua.ModelSnapshotAttribute(uri, payload)]</c>
    /// entry recovered from a referenced assembly's metadata. The payload
    /// is deserialised lazily on first access.
    /// </summary>
    public sealed class ReferencedModelSnapshot
    {
        private readonly string m_payload;
        private ModelSnapshotV1? m_decoded;
        private bool m_decodeAttempted;

        internal ReferencedModelSnapshot(string assemblyName, string modelUri, string payload)
        {
            AssemblyName = assemblyName ?? string.Empty;
            ModelUri = modelUri ?? string.Empty;
            m_payload = payload ?? string.Empty;
        }

        /// <summary>The assembly the attribute was read from.</summary>
        public string AssemblyName { get; }

        /// <summary>The OPC UA model URI recorded in the attribute.</summary>
        public string ModelUri { get; }

        /// <summary>
        /// Deserialises the snapshot payload. Returns the same instance on
        /// repeat calls. Returns <c>null</c> when the payload version is
        /// unknown or the base64 is malformed (in which case downstream
        /// generators fall back to explicit <c>AdditionalFiles</c>).
        /// </summary>
        public ModelSnapshotV1? GetSnapshot()
        {
            if (!m_decodeAttempted)
            {
                m_decoded = ModelSnapshotV1.FromBase64Payload(m_payload);
                m_decodeAttempted = true;
            }
            return m_decoded;
        }
    }

    /// <summary>
    /// Scans referenced assemblies for
    /// <c>[assembly: Opc.Ua.ModelSnapshotAttribute(...)]</c> entries.
    /// Sibling to <see cref="ReferencedModelDependencyScanner"/>; uses the
    /// same incremental-cache-friendly per-symbol traversal.
    /// </summary>
    internal static class ReferencedModelSnapshotScanner
    {
        public const string AttributeMetadataName = "Opc.Ua.ModelSnapshotAttribute";

        /// <summary>
        /// Scan all referenced assemblies for ModelSnapshotAttribute entries.
        /// </summary>
        public static ImmutableArray<ReferencedModelSnapshot> Scan(Compilation compilation)
        {
            if (compilation == null)
            {
                return [];
            }
            INamedTypeSymbol attrType = compilation.GetTypeByMetadataName(
                AttributeMetadataName);
            if (attrType is null)
            {
                return [];
            }

            ImmutableArray<ReferencedModelSnapshot>.Builder results =
                ImmutableArray.CreateBuilder<ReferencedModelSnapshot>();
            foreach (IAssemblySymbol assembly in
                compilation.SourceModule.ReferencedAssemblySymbols)
            {
                foreach (AttributeData attr in assembly.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrType))
                    {
                        continue;
                    }
                    ImmutableArray<TypedConstant> args = attr.ConstructorArguments;
                    if (args.IsDefaultOrEmpty || args.Length < 2)
                    {
                        continue;
                    }
                    string modelUri = args[0].Value as string;
                    string payload = args[1].Value as string;
                    if (string.IsNullOrEmpty(modelUri) || string.IsNullOrEmpty(payload))
                    {
                        continue;
                    }
                    results.Add(new ReferencedModelSnapshot(
                        assembly.Identity.Name,
                        modelUri,
                        payload));
                }
            }
            return results.ToImmutable();
        }
    }
}
