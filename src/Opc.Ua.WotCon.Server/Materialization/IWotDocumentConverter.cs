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
using System.Collections.Immutable;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// The result of converting one registry document to a NodeSet2 model.
    /// </summary>
    public sealed class WotConversionOutput
    {
        /// <summary>Initializes a successful or failed conversion output.</summary>
        public WotConversionOutput(
            UANodeSet? nodeSet,
            ImmutableArray<string> errors,
            ExpandedNodeId? rootNodeId = null)
        {
            NodeSet = nodeSet;
            Errors = errors.IsDefault ? ImmutableArray<string>.Empty : errors;
            RootNodeId = rootNodeId;
        }

        /// <summary>Gets the produced NodeSet2, or <c>null</c> on failure.</summary>
        public UANodeSet? NodeSet { get; }

        /// <summary>Gets the conversion error messages.</summary>
        public ImmutableArray<string> Errors { get; }

        /// <summary>
        /// Gets the root node of the projection (the type a Thing Model
        /// materializes or the top-level instance a Thing Description projects),
        /// as an absolute <see cref="ExpandedNodeId"/> whose namespace URI is
        /// resolved from the produced NodeSet, or <c>null</c> when the document
        /// has no identifiable root.
        /// </summary>
        public ExpandedNodeId? RootNodeId { get; }

        /// <summary>Gets whether the conversion succeeded.</summary>
        public bool Succeeded => NodeSet is not null && Errors.IsEmpty;

        /// <summary>Creates a successful output.</summary>
        public static WotConversionOutput Success(UANodeSet nodeSet)
            => new WotConversionOutput(
                nodeSet,
                ImmutableArray<string>.Empty,
                WotNodeSetConverter.TrySelectProjectionRoot(nodeSet));

        /// <summary>Creates a failed output.</summary>
        public static WotConversionOutput Failure(params string[] errors)
            => new WotConversionOutput(null, errors.ToImmutableArray());
    }

    /// <summary>
    /// Converts a stored registry document to a NodeSet2 model. The default
    /// implementation delegates to <see cref="WotNodeSetConverter"/> and resolves
    /// TM references from the registry snapshot; a test double can substitute a
    /// deterministic conversion.
    /// </summary>
    public interface IWotDocumentConverter
    {
        /// <summary>Converts a resource's default document to a NodeSet2 model.</summary>
        WotConversionOutput Convert(
            WotResource resource,
            ReadOnlyMemory<byte> content,
            WotRegistrySnapshot snapshot);
    }

    /// <summary>
    /// The production converter over <see cref="WotNodeSetConverter"/>.
    /// </summary>
    public sealed class WotNodeSetDocumentConverter : IWotDocumentConverter
    {
        /// <summary>Initializes a new converter with the supplied options.</summary>
        public WotNodeSetDocumentConverter(WotNodeSetConverterOptions? options = null)
        {
            m_options = options ?? new WotNodeSetConverterOptions();
        }

        /// <inheritdoc/>
        public WotConversionOutput Convert(
            WotResource resource,
            ReadOnlyMemory<byte> content,
            WotRegistrySnapshot snapshot)
        {
            try
            {
                using WotDocument document = WotDocument.Parse(content, m_options);
                var resolver = new SnapshotThingResolver(snapshot);
                var resolution = new WotResolutionContext();
                WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                    document, m_options, resolver, resolution);
                var errors = ImmutableArray.CreateBuilder<string>();
                foreach (WotDiagnostic diagnostic in result.Diagnostics)
                {
                    if (diagnostic.Severity == WotDiagnosticSeverity.Error)
                    {
                        errors.Add(diagnostic.ToString());
                    }
                }
                if (result.Value is null && errors.Count == 0)
                {
                    errors.Add("The document could not be converted to a NodeSet.");
                }
                if (errors.Count != 0 || result.Value is null)
                {
                    return new WotConversionOutput(null, errors.ToImmutable());
                }
                return new WotConversionOutput(
                    result.Value,
                    ImmutableArray<string>.Empty,
                    WotNodeSetConverter.TrySelectProjectionRoot(result.Value));
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
            {
                return WotConversionOutput.Failure(ex.Message);
            }
        }

        private readonly WotNodeSetConverterOptions m_options;
    }
}
