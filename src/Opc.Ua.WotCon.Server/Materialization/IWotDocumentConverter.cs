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
using System.Threading;
using System.Threading.Tasks;
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
        /// <summary>
        /// Initializes a successful or failed conversion output.
        /// </summary>
        public WotConversionOutput(
            UANodeSet? nodeSet,
            ImmutableArray<string> errors,
            ExpandedNodeId rootNodeId = default,
            WoTPhaseEnum failurePhase = WoTPhaseEnum.FormatValidation)
        {
            NodeSet = nodeSet;
            Errors = errors.IsDefault ? [] : errors;
            RootNodeId = rootNodeId;
            FailurePhase = failurePhase;
        }

        /// <summary>
        /// Gets the produced NodeSet2, or <c>null</c> on failure.
        /// </summary>
        public UANodeSet? NodeSet { get; }

        /// <summary>
        /// Gets the conversion error messages.
        /// </summary>
        public ImmutableArray<string> Errors { get; }

        /// <summary>
        /// Gets the root node of the projection (the type a Thing Model
        /// materializes or the top-level instance a Thing Description projects),
        /// as an absolute <see cref="ExpandedNodeId"/> whose namespace URI is
        /// resolved from the produced NodeSet, or <c>ExpandedNodeId.Null</c>
        /// when the document has no identifiable root.
        /// </summary>
        public ExpandedNodeId RootNodeId { get; }

        /// <summary>
        /// Gets the refresh phase to report when conversion failed.
        /// </summary>
        public WoTPhaseEnum FailurePhase { get; }

        /// <summary>
        /// Gets whether the conversion succeeded.
        /// </summary>
        public bool Succeeded => NodeSet is not null && Errors.IsEmpty;

        /// <summary>
        /// Creates a successful output.
        /// </summary>
        public static WotConversionOutput Success(UANodeSet nodeSet)
        {
            return new WotConversionOutput(
                        nodeSet,
                        [],
                        WotNodeSetConverter.TrySelectProjectionRoot(nodeSet));
        }

        /// <summary>
        /// Creates a failed output.
        /// </summary>
        public static WotConversionOutput Failure(params string[] errors)
        {
            return new WotConversionOutput(null, [.. errors]);
        }

        /// <summary>
        /// Creates a failed output for the supplied refresh phase.
        /// </summary>
        public static WotConversionOutput Failure(WoTPhaseEnum phase, params string[] errors)
        {
            return new WotConversionOutput(null, [.. errors], failurePhase: phase);
        }
    }

    /// <summary>
    /// Converts a stored registry document to a NodeSet2 model. The default
    /// implementation delegates to <see cref="WotNodeSetConverter"/> and resolves
    /// TM references from the registry snapshot; a test double can substitute a
    /// deterministic conversion.
    /// </summary>
    public interface IWotDocumentConverter
    {
        /// <summary>
        /// Converts a resource's default document to a NodeSet2 model.
        /// </summary>
        ValueTask<WotConversionOutput> ConvertAsync(
            WotResource resource,
            ByteString content,
            WotRegistrySnapshot snapshot,
            IReadOnlyDictionary<string, ByteString> contents,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// The production converter over <see cref="WotNodeSetConverter"/>.
    /// </summary>
    public sealed class WotNodeSetDocumentConverter : IWotDocumentConverter
    {
        /// <summary>
        /// Initializes a new converter with the supplied options.
        /// </summary>
        /// <param name="options">The converter options.</param>
        /// <param name="addressSpace">
        /// The loaded-AddressSpace half of the WoT Binding Section 5.1.5 local
        /// context, consulted after the sibling documents. Without it a
        /// document can only bind to a type a sibling projects, so every
        /// companion-model type binding of Section 5.2.1 is unresolvable.
        /// </param>
        /// <param name="schemaProviders">
        /// The providers an <c>uav:externalSchema</c> reference is resolved
        /// through, in order. None is the default: the reference is an
        /// arbitrary IRI in a document the Server did not write, so nothing is
        /// fetched unless a host says what may be asked.
        /// </param>
        public WotNodeSetDocumentConverter(
            WotNodeSetConverterOptions? options = null,
            IWotNodeResolver? addressSpace = null,
            IEnumerable<IWotSchemaResolver>? schemaProviders = null)
        {
            m_options = options ?? new WotNodeSetConverterOptions();
            m_addressSpace = addressSpace;
            m_schemaResolver = new WotExternalSchemaResolver(
                schemaProviders is null ? [] : [.. schemaProviders]);
        }

        /// <summary>
        /// Gets or sets the loaded-AddressSpace half of the WoT Binding
        /// Section 5.1.5 local context.
        /// </summary>
        /// <remarks>
        /// Settable because the AddressSpace only exists once the Server is
        /// running, while the converter is built during composition. A host
        /// sets it as soon as it has an <c>IServerInternal</c>, in the same
        /// place it sets the coordinator's other server-derived state.
        /// </remarks>
        public IWotNodeResolver? AddressSpace
        {
            get
            {
                lock (m_resolverLock)
                {
                    return m_addressSpace;
                }
            }
            set
            {
                lock (m_resolverLock)
                {
                    m_addressSpace = value;

                    // Force the composed context to be rebuilt so a resolver
                    // set after the first conversion still takes effect.
                    m_nodeResolver = null;
                    m_composed = null;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WotConversionOutput> ConvertAsync(
            WotResource resource,
            ByteString content,
            WotRegistrySnapshot snapshot,
            IReadOnlyDictionary<string, ByteString> contents,
            CancellationToken cancellationToken)
        {
            try
            {
                using var document = WotDocument.Parse(content.Span.ToArray(), m_options);
                var resolver = new SnapshotThingResolver(snapshot, contents);

                // WoT Binding Section 5.1.5: the local context has two parts,
                // consulted in this order - the sibling documents of this
                // conversion, then a loaded AddressSpace. Composing them here
                // is what lets a Section 5.2.1 binding name either a type
                // another registry document projects or one a companion model
                // defines and the Server already holds.
                //
                // A refresh converts every resource of a snapshot in turn, so
                // the resolver is reused for as long as the snapshot it indexes
                // is the one being converted. Building it per conversion would
                // make a refresh cost one registry-wide index per document.
                IWotNodeResolver nodeResolver = GetLocalContext(snapshot, contents);
                // One resolution context per top-level conversion, seeded from
                // the configured converter options, so depth/document/byte
                // bounds and cycle detection apply across every link resolved
                // while converting this resource.
                var resolution = new WotResolutionContext(m_options.ToResolverOptions());
                WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                    document, m_options, resolver, resolution, nodeResolver, m_schemaResolver,
                    cancellationToken)
                    .ConfigureAwait(false);
                ImmutableArray<string>.Builder errors = ImmutableArray.CreateBuilder<string>();
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
                    WoTPhaseEnum phase = HasProjectionFailure(result.Diagnostics)
                        ? WoTPhaseEnum.Projection
                        : WoTPhaseEnum.FormatValidation;
                    return new WotConversionOutput(
                        null, errors.ToImmutable(), failurePhase: phase);
                }
                return new WotConversionOutput(
                    result.Value,
                    [],
                    WotNodeSetConverter.TrySelectProjectionRoot(result.Value));
            }
            // One malformed document fails its own conversion and is reported
            // as such. It must never abort the refresh, because that would let
            // a single bad document take every unrelated resource in the
            // registry down with it.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return WotConversionOutput.Failure(ex.Message);
            }
        }

        private static bool HasProjectionFailure(IReadOnlyList<WotDiagnostic> diagnostics)
        {
            foreach (WotDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == WotDiagnosticSeverity.Error &&
                    diagnostic.Code == WotDiagnosticCode.UnresolvedParentPlacement)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the resolver for the supplied snapshot, reusing the previous
        /// one while the snapshot is unchanged.
        /// </summary>
        /// <remarks>
        /// A snapshot is immutable and a refresh converts every one of its
        /// resources in turn, so the sibling index is the same for all of them.
        /// Rebuilding it per conversion would make a refresh parse the registry
        /// once per document. Only the most recent snapshot is held, so nothing
        /// accumulates as generations advance.
        /// </remarks>
        private SnapshotWotNodeResolver GetNodeResolver(
            WotRegistrySnapshot snapshot,
            IReadOnlyDictionary<string, ByteString> contents)
        {
            lock (m_resolverLock)
            {
                if (m_nodeResolver is null ||
                    !ReferenceEquals(m_nodeResolver.Snapshot, snapshot))
                {
                    m_nodeResolver = new SnapshotWotNodeResolver(
                        snapshot, contents, m_options);
                    m_composed = m_addressSpace is null
                        ? m_nodeResolver
                        : new WotCompositeNodeResolver(m_nodeResolver, m_addressSpace);
                }
                return m_nodeResolver;
            }
        }

        /// <summary>
        /// Gets the composed local context for the supplied snapshot.
        /// </summary>
        private IWotNodeResolver GetLocalContext(
            WotRegistrySnapshot snapshot,
            IReadOnlyDictionary<string, ByteString> contents)
        {
            GetNodeResolver(snapshot, contents);
            lock (m_resolverLock)
            {
                return m_composed!;
            }
        }

        private readonly WotNodeSetConverterOptions m_options;
        private readonly WotExternalSchemaResolver m_schemaResolver;
        private readonly System.Threading.Lock m_resolverLock = new();
        private IWotNodeResolver? m_addressSpace;
        private SnapshotWotNodeResolver? m_nodeResolver;
        private IWotNodeResolver? m_composed;
    }
}
