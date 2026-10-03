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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Protocol
{
    /// <summary>
    /// A registry's authoritative, caller-contextual operation interface.
    /// Adapters must reject unsupported mutations before making any changes.
    /// Transport failure is distinct from a returned registry rejection.
    /// </summary>
    public interface IXRegistryEndpoint
    {
        /// <summary>
        /// Inspects the effective model and guarantees available to this caller.
        /// A failed inspection must not return an empty or legacy capability profile.
        /// </summary>
        ValueTask<XRegistryEndpointDescription> InspectAsync(
            XRegistryCallContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes one complete registry request, including its response preparation.
        /// A successful mutation is authoritative before this method returns.
        /// </summary>
        ValueTask<XRegistryResponse> ExecuteAsync(
            XRegistryRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional durable replay/outcome support. Operation IDs are scoped to the caller.
    /// A repeated ID with a different request must be rejected, never applied again.
    /// </summary>
    public interface IXRegistryOperationJournalEndpoint : IXRegistryEndpoint
    {
        /// <summary>
        /// Looks up an operation without repeating it. Unknown does not mean rejected.
        /// </summary>
        ValueTask<XRegistryOperationOutcome> GetOperationOutcomeAsync(
            string operationId,
            XRegistryCallContext context,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional preparation across protocol adapters. A preview has no registry
    /// side effects and permits complete response encoding before publication.
    /// </summary>
    public interface IXRegistryPreparedEndpoint : IXRegistryEndpoint
    {
        /// <summary>
        /// Validates and stages a request without applying it. The caller must dispose
        /// the lease, committing it only after preparing its own protocol response.
        /// No lock or concurrency primitive is exposed or held across the lease.
        /// </summary>
        ValueTask<IXRegistryPreparedOperation> PrepareAsync(
            XRegistryRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A single-use prepared operation. Disposal before commit aborts the staged
    /// candidate. It cannot undo a committed operation or an uncertain publication.
    /// </summary>
    public interface IXRegistryPreparedOperation : IAsyncDisposable
    {
        /// <summary>
        /// Immutable response preview. All successful commit response fields must
        /// match this preview; no response shaping may remain deferred until commit.
        /// </summary>
        XRegistryResponse Response { get; }

        /// <summary>
        /// Publishes the prepared candidate at most once. Any intervening registry
        /// mutation must reject the candidate without applying it; implementations
        /// must never silently rebase it. Transport/storage exceptions can mean an
        /// unknown outcome. This permits guarded read-back of affected descendants
        /// between preparation and publication without exposing a lock.
        /// </summary>
        ValueTask<XRegistryResponse> CommitAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional immutable candidate access for preparing native projections and
    /// notifications before an authoritative mutation. Reads use the identity that
    /// prepared the operation; request context cannot select another caller.
    /// Candidate access ends when the operation is committed or disposed.
    /// </summary>
    public interface IXRegistryPreparedSnapshot : IXRegistryPreparedOperation
    {
        /// <summary>
        /// Whether a live mutation candidate exists. Replayed outcomes and rejected
        /// requests have no candidate and must not generate another projection or event.
        /// </summary>
        bool HasCandidate { get; }

        /// <summary>
        /// Describes the registry that would result from committing this candidate.
        /// </summary>
        ValueTask<XRegistryEndpointDescription> InspectCandidateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads or describes the candidate without publishing it. Mutations and
        /// operation-journal requests are rejected; generation guards address the
        /// candidate generation, not the currently committed registry.
        /// </summary>
        ValueTask<XRegistryResponse> ReadCandidateAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Core registry actions, independent of their HTTP or OPC UA encoding.
    /// </summary>
    public enum XRegistryAction
    {
        Read,
        Replace,
        Merge,
        Create,
        Delete,
        Describe
    }

    /// <summary>
    /// Selects the normal model-defined view or its metadata representation.
    /// </summary>
    public enum XRegistryView
    {
        Default,
        Metadata
    }

    /// <summary>
    /// An authenticated identity established by the host, not by request headers.
    /// Credentials remain in the host's identity providers.
    /// </summary>
    public sealed record XRegistryCallContext
    {
        public XRegistryCallContext(string subject)
        {
            Subject = string.IsNullOrWhiteSpace(subject)
                ? throw new ArgumentException("A caller subject is required.", nameof(subject))
                : subject;
        }

        public static XRegistryCallContext Anonymous { get; } = new("anonymous");

        public string Subject { get; }

        public string Authority { get; init; } = string.Empty;

        public bool IsAuthenticated { get; init; }

        public ArrayOf<string> Roles
        {
            get => m_roles;
            init => m_roles = value.IsNull ? ArrayOf<string>.Null : value.Span.ToArray();
        }

        public string? SessionId { get; init; }

        private readonly ArrayOf<string> m_roles;
    }

    /// <summary>
    /// One binding-defined request parameter. Repeated parameters retain their order.
    /// </summary>
    public sealed record XRegistryParameter(string Name, string? Value);

    /// <summary>
    /// A single atomic core request. Metadata retains JSON types and explicit nulls;
    /// an undefined Metadata value means absent. A null Document means absent,
    /// whereas ByteString.Empty is a present, zero-length document.
    /// </summary>
    /// <remarks>
    /// Path is registry-relative and escaped, with no query, fragment or $details
    /// suffix. For document requests Metadata contains decoded header attributes
    /// and has PATCH semantics, even when Action is Replace.
    /// An OperationId requests replay protection only on an endpoint advertising
    /// that guarantee; it is not a caller-selected xregcorrelationid.
    /// </remarks>
    public sealed record XRegistryRequest
    {
        public XRegistryRequest(XRegistryAction action, string path)
        {
            if (action is < XRegistryAction.Read or > XRegistryAction.Describe)
            {
                throw new ArgumentOutOfRangeException(nameof(action));
            }
            Action = action;
            Path = XRegistryPath.Normalize(path);
        }

        public XRegistryAction Action { get; }

        public string Path { get; private init; }

        /// <summary>
        /// Original short-link address when Path has been resolved for model-aware processing.
        /// Authoritative execution must resolve this address again before applying the request.
        /// </summary>
        public string? AddressPath
        {
            get => m_addressPath;
            init => m_addressPath = value is null ? null : XRegistryPath.Normalize(value);
        }

        public XRegistryView View
        {
            get => m_view;
            init => m_view = value is < XRegistryView.Default or > XRegistryView.Metadata
                ? throw new ArgumentOutOfRangeException(nameof(value))
                : value;
        }

        public JsonElement Metadata
        {
            get => m_metadata;
            init => m_metadata = value.ValueKind == JsonValueKind.Undefined ? default : value.Clone();
        }

        public ByteString Document
        {
            get => m_document;
            init => m_document = value.IsNull ? default : ByteString.From(value.Span);
        }

        public string? ContentType { get; init; }

        public ArrayOf<XRegistryParameter> Parameters
        {
            get => m_parameters;
            init => m_parameters = value.IsNull ? ArrayOf<XRegistryParameter>.Null : value.Span.ToArray();
        }

        public XRegistryCallContext Context { get; init; } = XRegistryCallContext.Anonymous;

        public string? OperationId { get; init; }

        /// <summary>
        /// Requires the explicitly addressed Version to be the same incarnation
        /// returned by an earlier read. This is independent of its epoch and timestamps.
        /// Endpoints without this guarantee must reject the request before dispatch.
        /// </summary>
        public string? ExpectedVersionIncarnation { get; init; }

        /// <summary>
        /// Requires the complete registry generation returned by inspection or a read.
        /// The comparison and operation must use the same authoritative snapshot.
        /// Unsupported endpoints must reject the request, never ignore this guard.
        /// </summary>
        public string? ExpectedGeneration { get; init; }

        public bool IsMutation => Action is
            XRegistryAction.Replace or XRegistryAction.Merge or XRegistryAction.Create or XRegistryAction.Delete;

        /// <summary>
        /// Returns the same complete request at another normalized registry-relative address.
        /// All guards, body values, caller scope and operation identity are retained.
        /// </summary>
        public XRegistryRequest AtPath(string path)
        {
            return this with { Path = XRegistryPath.Normalize(path) };
        }

        /// <summary>
        /// Resolves an address while retaining the originally presented path for replay and revalidation.
        /// </summary>
        public XRegistryRequest AtResolvedPath(string path)
        {
            string canonical = XRegistryPath.Normalize(path);
            return canonical == Path && AddressPath is null
                ? this : this with { Path = canonical, AddressPath = AddressPath ?? Path };
        }

        private readonly JsonElement m_metadata;
        private readonly XRegistryView m_view;
        private readonly ByteString m_document;
        private readonly ArrayOf<XRegistryParameter> m_parameters;
        private readonly string? m_addressPath;
    }

    /// <summary>
    /// A core error, rather than an infrastructure or transport exception.
    /// Standard errors use the pinned specification's identifiers; experimental
    /// endpoint guarantees may define additional, explicitly negotiated error codes.
    /// </summary>
    public sealed record XRegistryError(string Code, string Detail)
    {
        public string? Subject { get; init; }
    }

    /// <summary>
    /// A registry link. Binding adapters render links against the configured public root.
    /// </summary>
    public sealed record XRegistryLink(string Relation, string Target)
    {
        /// <summary>
        /// Optional total result-set count from the pagination binding, not the size of this page.
        /// </summary>
        public ulong? Count { get; init; }
    }

    /// <summary>
    /// A completed registry response. StatusCode uses the xRegistry HTTP binding's
    /// status vocabulary, also retained inside the experimental native extension.
    /// Undefined Metadata and null Document represent no response body.
    /// </summary>
    public sealed record XRegistryResponse
    {
        public XRegistryResponse(int statusCode)
        {
            if (statusCode is < 100 or > 599)
            {
                throw new ArgumentOutOfRangeException(nameof(statusCode));
            }
            StatusCode = statusCode;
        }

        public int StatusCode { get; }

        public bool IsSuccess => StatusCode is >= 200 and < 300;

        public JsonElement Metadata
        {
            get => m_metadata;
            init => m_metadata = value.ValueKind == JsonValueKind.Undefined ? default : value.Clone();
        }

        public ByteString Document
        {
            get => m_document;
            init => m_document = value.IsNull ? default : ByteString.From(value.Span);
        }

        public string? ContentType { get; init; }

        public string? Location { get; init; }

        public string? ContentLocation { get; init; }

        public string? CorrelationId { get; init; }

        /// <summary>
        /// Optional expiration of a result set, retained across HTTP and native envelopes.
        /// </summary>
        public DateTimeOffset? Expires { get; init; }

        /// <summary>
        /// Opaque identity of an explicitly addressed Version, stable across its updates
        /// but never reused after deletion. Legacy identities may expire on endpoint restart.
        /// This is an experimental guard, not an HTTP validator or an xRegistry attribute.
        /// </summary>
        public string? VersionIncarnation { get; init; }

        /// <summary>
        /// Opaque generation used by a successful read or Describe operation. A mutation
        /// response does not promise a reusable generation; inspect again after commit.
        /// </summary>
        public string? Generation { get; init; }

        public ArrayOf<XRegistryLink> Links
        {
            get => m_links;
            init => m_links = value.IsNull ? ArrayOf<XRegistryLink>.Null : value.Span.ToArray();
        }

        public ArrayOf<XRegistryAction> AllowedActions
        {
            get => m_allowedActions;
            init => m_allowedActions = value.IsNull ? ArrayOf<XRegistryAction>.Null : value.Span.ToArray();
        }

        public XRegistryError? Error { get; init; }

        private readonly JsonElement m_metadata;
        private readonly ByteString m_document;
        private readonly ArrayOf<XRegistryLink> m_links;
        private readonly ArrayOf<XRegistryAction> m_allowedActions;
    }

    /// <summary>
    /// Measured backend guarantees, separate from the standard capability document.
    /// False means unavailable, not permission to weaken HTTP requirements.
    /// </summary>
    public sealed record XRegistryEndpointDescription
    {
        public XRegistryEndpointDescription(string registryId)
        {
            RegistryId = string.IsNullOrWhiteSpace(registryId)
                ? throw new ArgumentException("A registry identity is required.", nameof(registryId))
                : registryId;
        }

        public string RegistryId { get; }

        public string Profile { get; init; } = "unqualified";

        /// <summary>
        /// Authoritative public root for rebasing protocol navigation links. This is
        /// not a redirect or a credential destination, and may be absent for relative-only endpoints.
        /// </summary>
        public Uri? PublicRoot { get; init; }

        /// <summary>
        /// Optional registry-relative short-link prefix supported by address resolution.
        /// Null means aliases are not advertised; unknown servers cannot be assumed to resolve them.
        /// </summary>
        public string? ShortLinkPrefix { get; init; }

        public JsonElement Model
        {
            get => m_model;
            init => m_model = value.ValueKind == JsonValueKind.Undefined ? default : value.Clone();
        }

        public JsonElement Capabilities
        {
            get => m_capabilities;
            init => m_capabilities = value.ValueKind == JsonValueKind.Undefined ? default : value.Clone();
        }

        public bool SupportsAtomicMutations { get; init; }

        public bool SupportsConditionalMutations { get; init; }

        public bool SupportsWriteTouch { get; init; }

        public bool SupportsOperationReplay { get; init; }

        public bool SupportsPreparedMutations { get; init; }

        /// <summary>
        /// Whether successful prepared mutations expose IXRegistryPreparedSnapshot
        /// for precommit projection and event preparation.
        /// </summary>
        public bool SupportsPreparedSnapshots { get; init; }

        /// <summary>
        /// Whether explicit Version reads return an incarnation and operations can
        /// atomically compare ExpectedVersionIncarnation before reading or mutating it.
        /// </summary>
        public bool SupportsVersionIncarnationGuards { get; init; }

        /// <summary>
        /// Whether reads return their generation and requests atomically enforce
        /// ExpectedGeneration. Tokens may expire on reload or endpoint restart and
        /// must not be interpreted as entity epochs or cross-registry sequence numbers.
        /// </summary>
        public bool SupportsGenerationGuards { get; init; }

        /// <summary>
        /// Generation of the inspected identity, model and capabilities. Required
        /// when SupportsGenerationGuards is true; no read snapshot lease is retained.
        /// </summary>
        public string? Generation { get; init; }

        private readonly JsonElement m_model;
        private readonly JsonElement m_capabilities;
    }

    /// <summary>
    /// Durable outcome states. Unknown includes a lost response whose outcome
    /// cannot yet be established; it must not trigger an unconditional retry.
    /// </summary>
    public enum XRegistryOperationState
    {
        Unknown,
        Committed,
        Rejected
    }

    /// <summary>
    /// An outcome returned from an endpoint's operation journal.
    /// </summary>
    public sealed record XRegistryOperationOutcome(
        XRegistryOperationState State,
        XRegistryResponse? Response);
}
