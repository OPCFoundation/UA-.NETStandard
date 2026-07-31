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
 *
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
using System.Threading.Tasks;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The kind of external document being resolved.
    /// </summary>
    public enum WotResolutionKind
    {
        /// <summary>
        /// A JSON-LD <c>@context</c> document.
        /// </summary>
        Context,

        /// <summary>
        /// An external DataSchema referenced by <c>uav:externalSchema</c>.
        /// </summary>
        Schema,

        /// <summary>
        /// A referenced Thing Description or Thing Model document.
        /// </summary>
        Thing
    }

    /// <summary>
    /// Bounded options that govern external document resolution.
    /// </summary>
    public sealed class WotResolverOptions
    {
        /// <summary>
        /// Gets or sets the maximum resolution depth.
        /// </summary>
        public int MaxDepth { get; set; } = 16;

        /// <summary>
        /// Gets or sets the maximum number of documents resolved.
        /// </summary>
        public int MaxDocuments { get; set; } = 256;

        /// <summary>
        /// Gets or sets the maximum accepted size of a single resolved document.
        /// </summary>
        public int MaxDocumentBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum total size of all resolved documents.
        /// </summary>
        public long MaxTotalBytes { get; set; } = 128L * 1024 * 1024;

        /// <summary>
        /// Validates the option values and throws when a limit is not positive.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when any configured limit is not strictly positive.
        /// </exception>
        public void Validate()
        {
            EnsurePositive(MaxDepth, nameof(MaxDepth));
            EnsurePositive(MaxDocuments, nameof(MaxDocuments));
            EnsurePositive(MaxDocumentBytes, nameof(MaxDocumentBytes));
            if (MaxTotalBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxTotalBytes),
                    MaxTotalBytes,
                    "The configured limit must be a positive value.");
            }
        }

        private static void EnsurePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "The configured limit must be a positive value.");
            }
        }
    }

    /// <summary>
    /// The result of resolving one external document.
    /// </summary>
    public sealed class WotResolverResult
    {
        private WotResolverResult(
            bool found,
            ReadOnlyMemory<byte> content,
            string? contentType)
        {
            Found = found;
            Content = content;
            ContentType = contentType;
        }

        /// <summary>
        /// Gets a value indicating whether the document was found.
        /// </summary>
        public bool Found { get; }

        /// <summary>
        /// Gets the resolved UTF-8 document bytes.
        /// </summary>
        public ReadOnlyMemory<byte> Content { get; }

        /// <summary>
        /// Gets the media type of the resolved document, if known.
        /// </summary>
        public string? ContentType { get; }

        /// <summary>
        /// A shared result indicating the document was not found.
        /// </summary>
        public static WotResolverResult NotFound { get; } =
            new WotResolverResult(false, ReadOnlyMemory<byte>.Empty, null);

        /// <summary>
        /// Creates a successful result from resolved bytes.
        /// </summary>
        /// <param name="content">The resolved UTF-8 document bytes.</param>
        /// <param name="contentType">The media type of the document, if known.</param>
        public static WotResolverResult FromBytes(
            ReadOnlyMemory<byte> content,
            string? contentType = null)
        {
            return new WotResolverResult(true, content, contentType);
        }
    }

    /// <summary>
    /// Resolves external JSON-LD <c>@context</c> documents. Implementations
    /// supply their own transport; this library performs no network I/O.
    /// </summary>
    public interface IWotContextResolver
    {
        /// <summary>
        /// Resolves a context document by reference.
        /// </summary>
        /// <param name="reference">The context reference (absolute or relative IRI).</param>
        /// <param name="context">The active resolution context.</param>
        /// <param name="cancellationToken">A token that cancels the resolution operation.</param>
        /// <returns>The resolution result.</returns>
        ValueTask<WotResolverResult> ResolveContextAsync(
            string reference,
            WotResolutionContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Resolves external DataSchema documents referenced by an affordance.
    /// Implementations supply their own transport; this library performs no
    /// network I/O.
    /// </summary>
    public interface IWotSchemaResolver
    {
        /// <summary>
        /// Resolves a schema document by reference.
        /// </summary>
        /// <param name="reference">The schema reference (absolute or relative IRI or path).</param>
        /// <param name="context">The active resolution context.</param>
        /// <param name="cancellationToken">A token that cancels the resolution operation.</param>
        /// <returns>The resolution result.</returns>
        ValueTask<WotResolverResult> ResolveSchemaAsync(
            string reference,
            WotResolutionContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Resolves referenced Thing Description or Thing Model documents.
    /// Implementations supply their own transport; this library performs no
    /// network I/O.
    /// </summary>
    public interface IWotThingResolver
    {
        /// <summary>
        /// Resolves a referenced TD/TM document by reference.
        /// </summary>
        /// <param name="reference">The document reference (absolute or relative IRI).</param>
        /// <param name="context">The active resolution context.</param>
        /// <param name="cancellationToken">A token that cancels the resolution operation.</param>
        /// <returns>The resolution result.</returns>
        ValueTask<WotResolverResult> ResolveThingAsync(
            string reference,
            WotResolutionContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// A resolver that never resolves anything. Use it as an explicit
    /// "no external resolution" policy; it performs no I/O.
    /// </summary>
    public sealed class NullWotResolver
        : IWotContextResolver, IWotSchemaResolver, IWotThingResolver
    {
        /// <summary>
        /// The shared instance.
        /// </summary>
        public static NullWotResolver Instance { get; } = new NullWotResolver();

        /// <inheritdoc/>
        public ValueTask<WotResolverResult> ResolveContextAsync(
            string reference,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
        }

        /// <inheritdoc/>
        public ValueTask<WotResolverResult> ResolveSchemaAsync(
            string reference,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
        }

        /// <inheritdoc/>
        public ValueTask<WotResolverResult> ResolveThingAsync(
            string reference,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
        }
    }

    /// <summary>
    /// Tracks resolution depth, the set of documents currently being resolved
    /// (for cycle detection) and cumulative resource usage. A context is
    /// created per conversion and is not shared across threads.
    /// </summary>
    public sealed class WotResolutionContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WotResolutionContext"/> class.
        /// </summary>
        /// <param name="options">The bounded resolution options.</param>
        public WotResolutionContext(WotResolverOptions? options = null)
        {
            Options = options ?? new WotResolverOptions();
            Options.Validate();
            m_active = new HashSet<ResolutionKey>();
            m_activeStack = [];
            m_diagnostics = [];
        }

        /// <summary>
        /// Gets the bounded resolution options.
        /// </summary>
        public WotResolverOptions Options { get; }

        /// <summary>
        /// Gets the current resolution depth.
        /// </summary>
        public int Depth => m_depth;

        /// <summary>
        /// Gets the number of documents entered so far.
        /// </summary>
        public int DocumentCount => m_documentCount;

        /// <summary>
        /// Gets the cumulative resolved byte count.
        /// </summary>
        public long TotalBytes => m_totalBytes;

        /// <summary>
        /// Gets the diagnostics accumulated during resolution.
        /// </summary>
        public IReadOnlyList<WotDiagnostic> Diagnostics => m_diagnostics;

        /// <summary>
        /// Attempts to begin resolving <paramref name="reference"/>. On success
        /// the reference is pushed and the caller must invoke
        /// <see cref="Leave(string)"/> in a finally block. On failure a
        /// diagnostic describing the cycle or limit is produced.
        /// </summary>
        /// <param name="kind">The kind of document being resolved.</param>
        /// <param name="reference">The document reference.</param>
        /// <param name="diagnostic">The blocking diagnostic when the method returns <c>false</c>.</param>
        /// <returns><c>true</c> when resolution may proceed.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool TryEnter(
            WotResolutionKind kind,
            string reference,
            out WotDiagnostic? diagnostic)
        {
            if (reference is null)
            {
                throw new ArgumentNullException(nameof(reference));
            }

            var location = new WotLocation(reference: reference);

            var key = new ResolutionKey(kind, reference);

            if (m_active.Contains(key))
            {
                diagnostic = Add(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResolverCycle,
                    $"External {kind} resolution detected a cycle at '{reference}'.",
                    location);
                return false;
            }

            if (m_depth >= Options.MaxDepth)
            {
                diagnostic = Add(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResolverDepthExceeded,
                    $"External {kind} resolution exceeded the maximum depth of {Options.MaxDepth}.",
                    location);
                return false;
            }

            if (m_documentCount >= Options.MaxDocuments)
            {
                diagnostic = Add(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResolverLimitExceeded,
                    $"External resolution exceeded the maximum document count of {Options.MaxDocuments}.",
                    location);
                return false;
            }

            m_active.Add(key);
            m_activeStack.Add(key);
            m_depth++;
            m_documentCount++;
            diagnostic = null;
            return true;
        }

        /// <summary>
        /// Ends resolving <paramref name="reference"/>. Must be paired with a
        /// successful <see cref="TryEnter(WotResolutionKind, string, out WotDiagnostic?)"/>.
        /// </summary>
        /// <param name="reference">The document reference.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Leave(string reference)
        {
            if (reference is null)
            {
                throw new ArgumentNullException(nameof(reference));
            }
            for (int ii = m_activeStack.Count - 1; ii >= 0; ii--)
            {
                ResolutionKey key = m_activeStack[ii];
                if (string.Equals(key.Reference, reference, StringComparison.Ordinal))
                {
                    m_activeStack.RemoveAt(ii);
                    if (m_active.Remove(key))
                    {
                        m_depth--;
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Records that <paramref name="byteCount"/> bytes were resolved and
        /// verifies the per-document and cumulative byte limits.
        /// </summary>
        /// <param name="reference">The document reference.</param>
        /// <param name="byteCount">The size of the resolved document.</param>
        /// <param name="diagnostic">The blocking diagnostic when the method returns <c>false</c>.</param>
        /// <returns><c>true</c> when the byte counts remain within the configured limits.</returns>
        public bool TryAddBytes(string reference, int byteCount, out WotDiagnostic? diagnostic)
        {
            var location = new WotLocation(reference: reference);
            if (byteCount > Options.MaxDocumentBytes)
            {
                diagnostic = Add(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResolverLimitExceeded,
                    $"Resolved document '{reference}' of {byteCount} bytes exceeds the " +
                    $"per-document limit of {Options.MaxDocumentBytes}.",
                    location);
                return false;
            }

            if (m_totalBytes + byteCount > Options.MaxTotalBytes)
            {
                diagnostic = Add(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResolverLimitExceeded,
                    $"Cumulative resolved size exceeded the total limit of {Options.MaxTotalBytes} bytes.",
                    location);
                return false;
            }

            m_totalBytes += byteCount;
            diagnostic = null;
            return true;
        }

        private WotDiagnostic Add(
            WotDiagnosticSeverity severity,
            WotDiagnosticCode code,
            string message,
            WotLocation location)
        {
            var diagnostic = new WotDiagnostic(severity, code, message, location);
            m_diagnostics.Add(diagnostic);
            return diagnostic;
        }

        private readonly struct ResolutionKey : IEquatable<ResolutionKey>
        {
            public ResolutionKey(WotResolutionKind kind, string reference)
            {
                Kind = kind;
                Reference = reference;
            }

            public WotResolutionKind Kind { get; }

            public string Reference { get; }

            public bool Equals(ResolutionKey other)
            {
                return Kind == other.Kind &&
                    string.Equals(Reference, other.Reference, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj)
            {
                return obj is ResolutionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(Reference);
                }
            }
        }

        private readonly HashSet<ResolutionKey> m_active;
        private readonly List<ResolutionKey> m_activeStack;
        private readonly List<WotDiagnostic> m_diagnostics;
        private int m_depth;
        private int m_documentCount;
        private long m_totalBytes;
    }
}
