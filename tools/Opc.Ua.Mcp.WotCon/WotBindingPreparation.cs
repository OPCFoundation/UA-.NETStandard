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
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Prepares one immutable binding request through the same bounded local document resolver as document tools.
    /// Preparation performs no remote dependency retrieval and registers or activates no protocol executor.
    /// </summary>
    public sealed class WotBindingPreparation : IDisposable
    {
        /// <summary>
        /// Captures document policy and the existing confined input services.
        /// </summary>
        public WotBindingPreparation(
            WotMcpOptions options, OpcUaMcpOptions fileOptions,
            McpFileTransfers files, IHttpClientFactory clients)
        {
            m_options = options?.Snapshot() ?? throw new ArgumentNullException(nameof(options));
            m_fileOptions = fileOptions ?? throw new ArgumentNullException(nameof(fileOptions));
            m_files = files ?? throw new ArgumentNullException(nameof(files));
            m_clients = clients ?? throw new ArgumentNullException(nameof(clients));
            m_gate = new SemaphoreSlim(m_options.MaxConcurrentOperations, m_options.MaxConcurrentOperations);
        }

        /// <summary>
        /// Resolves a uniquely selected supplied Thing Description, preserving its exact original content digest.
        /// Thing Models and incomplete dependency resolution cannot become executable documents.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<WotPreparedBindingDocument> PrepareAsync(
            WotDocumentInput input,
            WotDocumentKind kind,
            string? documentReference,
            WotBindingSelectionContext? selection,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(input.Source);
            ct.ThrowIfCancellationRequested();
            if (input.ResolveRemoteDependencies)
            {
                throw new ArgumentException(
                    "Runtime preparation requires explicitly supplied dependencies; remote retrieval is not enabled.",
                    nameof(input));
            }
            if (!Enum.IsDefined(kind) || kind == WotDocumentKind.ThingModel)
            {
                throw new ArgumentException("Live execution requires a Thing Description.", nameof(kind));
            }
            var snapshot = new WotDocumentInput
            {
                Source = Copy(input.Source),
                Dependencies = input.Dependencies.ConvertAll(Copy),
                BaseUri = input.BaseUri,
                DocumentSetArchive = input.DocumentSetArchive,
                StrictAuthoring = input.StrictAuthoring,
                RequiredConformance = [.. input.RequiredConformance],
                PreservationMode = input.PreservationMode
            };
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var scope = new WotDocumentScope(
                    snapshot, m_options, m_fileOptions, m_files, m_clients, ct);
                await scope.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
                WotDocumentSetEntry? selected = null;
                for (int index = 0; index < scope.Documents.Entries.Count; index++)
                {
                    WotDocumentSetEntry entry = scope.Documents.Entries[index];
                    if (string.IsNullOrWhiteSpace(documentReference) ||
                        string.Equals(documentReference, entry.Href, StringComparison.Ordinal))
                    {
                        if (selected is not null)
                        {
                            throw new ArgumentException(
                                "Select one exact document reference when the archive contains multiple documents.",
                                nameof(documentReference));
                        }
                        selected = entry;
                    }
                }
                if (selected is null)
                {
                    throw new ArgumentException("The selected document reference is not in the supplied set.",
                        nameof(documentReference));
                }
                WotDocument document = selected.Document;
                if (document.Kind == WotDocumentKind.ThingModel ||
                    (document.Kind == WotDocumentKind.Unknown && kind != WotDocumentKind.ThingDescription))
                {
                    throw new ArgumentException("The selected document must identify a Thing Description.");
                }
                var diagnostics = new List<WotDiagnostic>();
                WotBindingPlanRequest request = await WotBindingPlanRequest.FromDocumentAsync(
                    selected.Href, WoTDocumentKindEnum.ThingDescription, document.Utf8Json,
                    scope, scope.Options.MaxJsonDepth, selection, diagnostics, scope.Token).ConfigureAwait(false);
                foreach (WotDiagnostic diagnostic in diagnostics)
                {
                    if (diagnostic.Severity == WotDiagnosticSeverity.Error)
                    {
                        throw new ServiceResultException(StatusCodes.BadConfigurationError,
                            "The selected Thing Description has unresolved or invalid binding dependencies.");
                    }
                }
                scope.Token.ThrowIfCancellationRequested();
                return new WotPreparedBindingDocument(
                    selected.Href, Convert.ToHexString(SHA256.HashData(document.Utf8Json.Span)), request);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        /// <summary>
        /// Prevents input mutation during asynchronous document reads from changing source selection.
        /// </summary>
        private static WotDocumentSource Copy(WotDocumentSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new WotDocumentSource
            {
                Reference = source.Reference,
                Text = source.Text,
                FilePath = source.FilePath,
                ContentType = source.ContentType
            };
        }

        private readonly WotMcpOptions m_options;
        private readonly OpcUaMcpOptions m_fileOptions;
        private readonly McpFileTransfers m_files;
        private readonly IHttpClientFactory m_clients;
        private readonly SemaphoreSlim m_gate;
    }

    /// <summary>
    /// Exact document identity and fully resolved binding request produced without activating transport I/O.
    /// </summary>
    public sealed record WotPreparedBindingDocument(
        string DocumentReference, string Sha256, WotBindingPlanRequest Request);
}
