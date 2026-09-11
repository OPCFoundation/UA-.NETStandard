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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    /// <summary>
    /// An opt-in transactional registry endpoint. A private candidate generation is
    /// validated and its response serialized before any state or outcome is published.
    /// Native writers must use this same endpoint, not mutate its projection directly.
    /// </summary>
    public sealed partial class XRegistryTransactionalEndpoint :
        IXRegistryOperationJournalEndpoint, IXRegistryPreparedEndpoint, IDisposable
    {
        /// <summary>
        /// Creates an endpoint over an injected generation store. The caller owns the store.
        /// Domain-specific model features not qualified by this provider fail at startup.
        /// </summary>
        public XRegistryTransactionalEndpoint(
            XRegistryTransactionalOptions options,
            IXRegistryTransactionStore store,
            TimeProvider? timeProvider = null)
        {
            m_options = options.ThrowIfNull(nameof(options));
            m_store = store.ThrowIfNull(nameof(store));
            m_time = timeProvider ?? TimeProvider.System;
            if (string.IsNullOrWhiteSpace(options.RegistryId) ||
                string.IsNullOrWhiteSpace(options.WriteRole) ||
                options.MaxEntities < 1 ||
                options.MaxDocumentBytes < 0 ||
                options.MaxStateBytes < 1024 ||
                options.MaxPreparedOperations < 1 ||
                options.MaxPreparedBytes < 1 ||
                options.Model.ValueKind != JsonValueKind.Object ||
                options.PublicRoot is null ||
                !options.PublicRoot.IsAbsoluteUri ||
                !string.IsNullOrEmpty(options.PublicRoot.UserInfo) ||
                !string.IsNullOrEmpty(options.PublicRoot.Query) ||
                !string.IsNullOrEmpty(options.PublicRoot.Fragment))
            {
                throw new ArgumentException("A valid identity, model, public root and positive bounds are required.",
                    nameof(options));
            }
            try
            {
                _ = new XRegistryModelRules(XRegistryModelRules.Object(JsonNode.Parse(options.Model.GetRawText())));
            }
            catch (XRegistryRejectionException exception)
            {
                throw new ArgumentException(exception.Message, nameof(options), exception);
            }
            m_codec = new XRegistryProtocolCodec(options.MaxStateBytes);
        }

        /// <inheritdoc/>
        public async ValueTask<XRegistryEndpointDescription> InspectAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            context.ThrowIfNull(nameof(context));
            if (!await AuthorizeAsync(context, false, cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException("Registry inspection is not authorized.");
            }
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                JsonObject snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
                var model = new XRegistryModelRules((JsonObject)snapshot["modelsource"]!.DeepClone());
                return new XRegistryEndpointDescription(m_options.RegistryId)
                {
                    Model = Element(model.Model),
                    Capabilities = Element(Capabilities()),
                    Profile = "experimental-transactional-v1",
                    PublicRoot = m_options.PublicRoot,
                    SupportsAtomicMutations = true,
                    SupportsConditionalMutations = true,
                    SupportsWriteTouch = true,
                    SupportsOperationReplay = m_store.SupportsDurableReplay,
                    SupportsPreparedMutations = true
                };
            }
            finally
            {
                m_serial.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<XRegistryResponse> ExecuteAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            IXRegistryPreparedOperation operation = await PrepareAsync(request, cancellationToken)
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lifetime = operation.ConfigureAwait(false);
            return await operation.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<IXRegistryPreparedOperation> PrepareAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            request.ThrowIfNull(nameof(request));
            if (!await AuthorizeAsync(request.Context, request.IsMutation, cancellationToken).ConfigureAwait(false))
            {
                return new PreparedOperation(Error("unauthorized", "This registry operation is not authorized.", 403));
            }
            if (request.Document.Length > m_options.MaxDocumentBytes)
            {
                return new PreparedOperation(
                    Error("bad_request", "The document exceeds the configured byte limit.", 413));
            }
            try
            {
                _ = m_codec.EncodeRequest(request);
            }
            catch (ArgumentException exception)
            {
                return new PreparedOperation(Error("bad_request", exception.Message, 413));
            }
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                JsonObject prior = await LoadAsync(cancellationToken).ConfigureAwait(false);
                string? key = request.OperationId is null ? null : OperationKey(request.OperationId, request.Context);
                string? digest = key is null ? null : m_codec.ComputeRequestDigest(request);
                if (key is not null && prior["operations"]?[key] is JsonObject operation)
                {
                    return new PreparedOperation(XRegistryModelRules.Text(operation["digest"]) == digest
                        ? m_codec.DecodeResponse(ByteString.From(Convert.FromBase64String(
                            XRegistryModelRules.Text(operation["response"]))))
                        : Error("bad_request", "The operation identity was already used for a different request."));
                }
                if (request.IsMutation && Volatile.Read(ref m_preparedOperations) >= m_options.MaxPreparedOperations)
                {
                    return new PreparedOperation(Error("too_many_requests",
                        "The prepared-operation quota is exhausted.", 429));
                }

                JsonObject candidate = request.IsMutation ? (JsonObject)prior.DeepClone() : prior;
                var transaction = new Transaction(candidate, request, m_time.GetUtcNow().UtcDateTime);
                var model = new XRegistryModelRules((JsonObject)candidate["modelsource"]!.DeepClone());
                XRegistryResponse response;
                try
                {
                    XRegistryTarget target = model.Resolve(request.Path);
                    ValidateFlags(target, request);
                    if (request.Action == XRegistryAction.Describe)
                    {
                        return new PreparedOperation(new XRegistryResponse(204) { AllowedActions = Allowed(target) });
                    }
                    if (request.IsMutation)
                    {
                        Mutate(transaction, model, target, Body(request.Metadata), request.Action, request.Document);
                        FinalizeResources(transaction, model);
                        if (transaction.Entries.Count > m_options.MaxEntities)
                        {
                            throw new XRegistryRejectionException(
                                "bad_request", "The registry entity quota is exhausted.", 413);
                        }
                        ApplyCounters(transaction);
                        if (request.Action == XRegistryAction.Delete)
                        {
                            response = new XRegistryResponse(204);
                        }
                        else
                        {
                            response = Read(transaction, model, target) with
                            {
                                Location = transaction.Created.Contains(StoragePath(target)) ? Url(target.Path) : null
                            };
                            if (response.Location is not null)
                            {
                                response = CopyStatus(response, 201);
                            }
                        }
                    }
                    else
                    {
                        response = Read(transaction, model, target);
                    }
                    _ = m_codec.EncodeResponse(response);
                }
                catch (XRegistryRejectionException rejection)
                {
                    response = Error(rejection.Code, rejection.Message, rejection.StatusCode);
                    candidate = (JsonObject)prior.DeepClone();
                }
                catch (JsonException exception)
                {
                    response = Error("bad_request", exception.Message);
                    candidate = (JsonObject)prior.DeepClone();
                }
                catch (ArgumentException exception)
                {
                    response = Error("bad_request", exception.Message);
                    candidate = (JsonObject)prior.DeepClone();
                }

                if (!request.IsMutation || (!response.IsSuccess && key is null))
                {
                    return new PreparedOperation(response);
                }
                if (key is not null)
                {
                    XRegistryModelRules.Object(candidate["operations"])[key] = new JsonObject
                    {
                        ["digest"] = digest,
                        ["response"] = Base64(m_codec.EncodeResponse(response))
                    };
                }
                candidate["generation"] = XRegistryModelRules.Unsigned(prior["generation"])
                    .AddOne();
                ByteString replacement;
                try
                {
                    replacement = EncodeSnapshot(candidate);
                }
                catch (XRegistryRejectionException exception)
                {
                    return new PreparedOperation(Error(exception.Code, exception.Message, exception.StatusCode));
                }
                if (replacement.Length > m_options.MaxPreparedBytes - Interlocked.Read(ref m_preparedBytes))
                {
                    return new PreparedOperation(Error("too_many_requests",
                        "The prepared-generation byte quota is exhausted.", 429));
                }
                Interlocked.Add(ref m_preparedBytes, replacement.Length);
                Interlocked.Increment(ref m_preparedOperations);
                return new PreparedOperation(response, this, request.Context, m_expected, replacement, candidate);
            }
            finally
            {
                m_serial.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<XRegistryOperationOutcome> GetOperationOutcomeAsync(
            string operationId, XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            context.ThrowIfNull(nameof(context));
            if (!await AuthorizeAsync(context, true, cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException("Operation outcomes are not authorized.");
            }
            string key = OperationKey(operationId, context);
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                JsonObject snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
                if (snapshot["operations"]?[key] is not JsonObject operation)
                {
                    return new XRegistryOperationOutcome(XRegistryOperationState.Unknown, null);
                }
                XRegistryResponse response = m_codec.DecodeResponse(ByteString.From(Convert.FromBase64String(
                    XRegistryModelRules.Text(operation["response"]))));
                return new XRegistryOperationOutcome(
                    response.IsSuccess ? XRegistryOperationState.Committed : XRegistryOperationState.Rejected,
                    response);
            }
            finally
            {
                m_serial.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_disposed = true;
            m_serial.Dispose();
        }

        private async ValueTask<XRegistryResponse> CommitPreparedAsync(
            PreparedOperation operation, CancellationToken cancellationToken)
        {
            if (!await AuthorizeAsync(operation.Context!, true, cancellationToken).ConfigureAwait(false))
            {
                return Error("unauthorized", "The prepared operation is no longer authorized.", 403);
            }
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(XRegistryTransactionalEndpoint));
                }
                if (m_indeterminate)
                {
                    throw new IOException("A previous publication is indeterminate; recover the endpoint first.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                // A publication exception must not become an ordinary no-mutation rejection.
                m_indeterminate = true;
                bool published = await m_store.CommitAsync(
                    operation.Expected, operation.Replacement, cancellationToken).ConfigureAwait(false);
                m_indeterminate = false;
                if (!published)
                {
                    m_snapshot = null;
                    return Error("concurrent_change",
                        "The registry changed after preparation. The candidate was not applied; prepare again.", 409);
                }
                m_expected = operation.Replacement;
                m_snapshot = operation.Candidate;
                return operation.Response;
            }
            finally
            {
                m_serial.Release();
            }
        }

        private void ReleasePreparation(int bytes)
        {
            Interlocked.Add(ref m_preparedBytes, -bytes);
            Interlocked.Decrement(ref m_preparedOperations);
        }

        private async ValueTask<bool> AuthorizeAsync(
            XRegistryCallContext context, bool mutation, CancellationToken cancellationToken)
        {
            context.ThrowIfNull(nameof(context));
            if (m_options.AuthorizeAsync is not null)
            {
                return await m_options.AuthorizeAsync(context, mutation, cancellationToken).ConfigureAwait(false);
            }
            if (!mutation)
            {
                return true;
            }
            if (context.IsAuthenticated)
            {
                for (int index = 0; index < context.Roles.Count; index++)
                {
                    if (context.Roles[index] == m_options.WriteRole)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private async ValueTask<JsonObject> LoadAsync(CancellationToken cancellationToken)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(XRegistryTransactionalEndpoint));
            }
            if (m_indeterminate)
            {
                throw new IOException(
                    "A registry publication has an indeterminate outcome; reopen the endpoint to recover.");
            }
            ByteString loaded = await m_store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (m_snapshot is not null &&
                loaded.IsNull == m_expected.IsNull &&
                loaded.Span.SequenceEqual(m_expected.Span))
            {
                return m_snapshot;
            }
            m_expected = loaded;
            if (m_expected.IsNull)
            {
                string now = m_time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
                var root = new JsonObject
                {
                    ["registryid"] = m_options.RegistryId,
                    ["epoch"] = 0,
                    ["createdat"] = now,
                    ["modifiedat"] = now
                };
                try
                {
                    JsonObject model = XRegistryModelRules.Object(JsonNode.Parse(m_options.Model.GetRawText()));
                    root = XRegistryModelRules.Apply(root, Body(m_options.InitialMetadata) ?? [],
                        model["attributes"] as JsonObject, true, "registryid", m_options.RegistryId, "registry");
                }
                catch (XRegistryRejectionException exception)
                {
                    throw new InvalidDataException(
                        "InitialMetadata must satisfy the registry model's required root attributes.", exception);
                }
                m_snapshot = new JsonObject
                {
                    ["format"] = 1,
                    ["generation"] = 0,
                    ["registryid"] = m_options.RegistryId,
                    ["modelsource"] = JsonNode.Parse(m_options.Model.GetRawText()),
                    ["entries"] = new JsonObject
                    {
                        ["/"] = new JsonObject
                        {
                            ["metadata"] = root
                        }
                    },
                    ["operations"] = new JsonObject()
                };
                return m_snapshot;
            }
            if (m_expected.Length > m_options.MaxStateBytes)
            {
                throw new InvalidDataException("The persisted registry exceeds its state quota.");
            }
            try
            {
                JsonObject restored = XRegistryModelRules.Object(JsonNode.Parse(m_expected.Span));
                if (XRegistryModelRules.Unsigned(restored["format"]) != 1 ||
                    XRegistryModelRules.Text(restored["registryid"]) != m_options.RegistryId)
                {
                    throw new InvalidDataException("The persisted registry identity or format is incompatible.");
                }
                _ = XRegistryModelRules.Unsigned(restored["generation"]);
                _ = XRegistryModelRules.Object(restored["operations"]);
                var model = new XRegistryModelRules((JsonObject)restored["modelsource"]!.DeepClone());
                JsonObject entries = XRegistryModelRules.Object(restored["entries"]);
                if (!entries.ContainsKey("/") || entries.Count > m_options.MaxEntities)
                {
                    throw new InvalidDataException(
                        "The persisted registry root is absent or exceeds its entity quota.");
                }
                foreach ((string path, JsonNode? value) in entries)
                {
                    _ = model.Resolve(path);
                    JsonObject entry = XRegistryModelRules.Object(value);
                    _ = XRegistryModelRules.Unsigned(entry["metadata"]?["epoch"]);
                    if (entry["document"] is not null &&
                        Convert.FromBase64String(XRegistryModelRules.Text(entry["document"])).Length >
                            m_options.MaxDocumentBytes)
                    {
                        throw new InvalidDataException("A persisted document exceeds its quota.");
                    }
                }
                m_snapshot = restored;
                return restored;
            }
            catch (Exception exception) when (
                exception is JsonException or XRegistryRejectionException or FormatException)
            {
                throw new InvalidDataException("The persisted registry is invalid; recovery is required.", exception);
            }
        }

        private ByteString EncodeSnapshot(JsonObject snapshot)
        {
            try
            {
                return m_codec.EncodeJson(snapshot);
            }
            catch (ArgumentException)
            {
                throw new XRegistryRejectionException("bad_request",
                    "The generation or retained outcome quota is exhausted; no state has been published.", 413);
            }
        }

        private static string OperationKey(string operationId, XRegistryCallContext context)
        {
            if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 256)
            {
                throw new ArgumentException("Operation identities must be nonempty and at most 256 characters.",
                    nameof(operationId));
            }
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(
                context.Authority.Length.ToString(CultureInfo.InvariantCulture) +
                ":" +
                context.Authority +
                context.Subject.Length.ToString(CultureInfo.InvariantCulture) +
                ":" +
                context.Subject +
                operationId));
        }

        private static JsonElement Element(JsonNode value)
        {
            using var document = JsonDocument.Parse(value.ToJsonString());
            return document.RootElement.Clone();
        }

        private static string Base64(ByteString bytes)
        {
#if NETSTANDARD2_1_OR_GREATER || NET
            return Convert.ToBase64String(bytes.Span);
#else
            return Convert.ToBase64String(bytes.ToArray());
#endif
        }

        private static JsonObject? Body(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }
            return XRegistryModelRules.Object(JsonNode.Parse(value.GetRawText()));
        }

        private static XRegistryResponse CopyStatus(XRegistryResponse source, int status)
        {
            return new XRegistryResponse(status)
            {
                Metadata = source.Metadata,
                Document = source.Document,
                ContentType = source.ContentType,
                Location = source.Location,
                ContentLocation = source.ContentLocation,
                Links = source.Links
            };
        }

        private static XRegistryResponse Error(string code, string detail, int statusCode = 400)
        {
            return new XRegistryResponse(statusCode)
            {
                Error = new XRegistryError(code, detail),
                AllowedActions = statusCode == 405 ? [XRegistryAction.Read, XRegistryAction.Describe] : []
            };
        }

        private string Url(string path)
        {
            return m_options.PublicRoot.AbsoluteUri.TrimEnd('/') + (path == "/" ? string.Empty : path);
        }

        private static string StoragePath(XRegistryTarget target)
        {
            return target.Kind == XRegistryEntityKind.Resource ? target.Path + "/meta" : target.Path;
        }

        private static string Parent(string path)
        {
            int index = path.LastIndexOf('/');
            return index <= 0 ? "/" : path[..index];
        }

        private static string Child(string path, string id)
        {
            return path.TrimEnd('/') + "/" + Uri.EscapeDataString(id);
        }

        private static string Identity(string path)
        {
#if NET9_0_OR_GREATER
            return Uri.UnescapeDataString(path.AsSpan(path.LastIndexOf('/') + 1));
#else
            return Uri.UnescapeDataString(path.Substring(path.LastIndexOf('/') + 1));
#endif
        }

        private static JsonObject Metadata(JsonObject entry)
        {
            return XRegistryModelRules.Object(entry["metadata"]);
        }

        private static JsonObject GetEntry(Transaction transaction, string path)
        {
            return transaction.Entries[path] as JsonObject ??
                throw new XRegistryRejectionException("not_found", "The entity does not exist.", 404);
        }

        private static ArrayOf<XRegistryAction> Allowed(XRegistryTarget target)
        {
            if (target.Kind == XRegistryEntityKind.Special && target.Singular != "modelsource")
            {
                return [XRegistryAction.Read, XRegistryAction.Describe];
            }
            return target.IsCollection
                ? [XRegistryAction.Read, XRegistryAction.Create, XRegistryAction.Merge,
                    XRegistryAction.Delete, XRegistryAction.Describe]
                : [XRegistryAction.Read, XRegistryAction.Replace, XRegistryAction.Merge,
                    XRegistryAction.Delete, XRegistryAction.Describe];
        }

        private static JsonObject Capabilities()
        {
            return new JsonObject
            {
                ["available"] = new JsonObject
                {
                    ["capabilities"] = new JsonObject { ["mutable"] = false },
                    ["capabilitiesoffered"] = new JsonObject { ["mutable"] = false },
                    ["entities"] = new JsonObject { ["mutable"] = true },
                    ["model"] = new JsonObject { ["mutable"] = false },
                    ["modelsource"] = new JsonObject { ["mutable"] = true },
                    ["export"] = new JsonObject { ["mutable"] = false }
                },
                ["flags"] = new JsonArray("inline", "doc", "binary", "collections", "epoch", "specversion"),
                ["ignores"] = new JsonArray(),
                ["mutable"] = new JsonArray(),
                ["versionmodes"] = new JsonArray("manual", "createdat", "modifiedat"),
                ["specversions"] = new JsonArray("1.0-rc4"),
                ["pagination"] = false,
                ["shortself"] = false,
                ["formats"] = new JsonArray(),
                ["compatibilities"] = new JsonObject()
            };
        }

        private static void ValidateFlags(XRegistryTarget target, XRegistryRequest request)
        {
            for (int index = 0; index < request.Parameters.Count; index++)
            {
                XRegistryParameter parameter = request.Parameters[index];
                if (parameter.Name == "collections" &&
                    target.Kind is not (XRegistryEntityKind.Registry or XRegistryEntityKind.Group))
                {
                    throw new XRegistryRejectionException(
                        "bad_flag", "collections applies only to a registry or group.");
                }
                if (parameter.Name == "specversion" && parameter.Value != "1.0-rc4")
                {
                    throw new XRegistryRejectionException("bad_flag", "The requested specversion is unsupported.");
                }
            }
        }

        private sealed class Transaction(JsonObject snapshot, XRegistryRequest request, DateTime now)
        {
            public JsonObject Snapshot { get; } = snapshot;

            public JsonObject Entries { get; } = XRegistryModelRules.Object(snapshot["entries"]);

            public XRegistryRequest Request { get; } = request;

            public string Timestamp { get; } = now.ToString("O", CultureInfo.InvariantCulture);

            public HashSet<string> Touched { get; } = new(StringComparer.Ordinal);

            public HashSet<string> Created { get; } = new(StringComparer.Ordinal);

            public HashSet<string> Resources { get; } = new(StringComparer.Ordinal);
        }

        private sealed class PreparedOperation : IXRegistryPreparedOperation
        {
            public PreparedOperation(XRegistryResponse response)
            {
                Response = response;
            }

            public PreparedOperation(
                XRegistryResponse response, XRegistryTransactionalEndpoint owner, XRegistryCallContext context,
                ByteString expected, ByteString replacement, JsonObject candidate)
                : this(response)
            {
                m_owner = owner;
                Context = context;
                Expected = expected;
                Replacement = replacement;
                Candidate = candidate;
                m_reservedBytes = replacement.Length;
            }

            public XRegistryResponse Response { get; }

            public XRegistryCallContext? Context { get; }

            public ByteString Expected { get; private set; }

            public ByteString Replacement { get; private set; }

            public JsonObject? Candidate { get; private set; }

            public async ValueTask<XRegistryResponse> CommitAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.CompareExchange(ref m_consumed, 1, 0) != 0)
                {
                    throw new InvalidOperationException("The prepared operation was already committed or aborted.");
                }
                try
                {
                    return m_owner is null ? Response :
                        await m_owner.CommitPreparedAsync(this, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Release();
                    Interlocked.Exchange(ref m_consumed, 3);
                }
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.CompareExchange(ref m_consumed, 2, 0) == 0)
                {
                    Release();
                }
                return default;
            }

            private void Release()
            {
                m_owner?.ReleasePreparation(m_reservedBytes);
                Candidate = null;
                Replacement = default;
                Expected = default;
            }

            private readonly XRegistryTransactionalEndpoint? m_owner;
            private readonly int m_reservedBytes;
            private int m_consumed;
        }

        private readonly XRegistryTransactionalOptions m_options;
        private readonly IXRegistryTransactionStore m_store;
        private readonly XRegistryProtocolCodec m_codec;
        private readonly TimeProvider m_time;
        private readonly SemaphoreSlim m_serial = new(1, 1);
        private JsonObject? m_snapshot;
        private ByteString m_expected;
        private bool m_disposed;
        private bool m_indeterminate;
        private int m_preparedOperations;
        private long m_preparedBytes;
    }

    internal static class XRegistryEpochExtensions
    {
        public static JsonNode AddOne(this BigInteger epoch)
        {
            return JsonNode.Parse((epoch + BigInteger.One).ToString(CultureInfo.InvariantCulture))!;
        }
    }
}
