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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    /// <summary>
    /// Explicit local initialization of persistent short links. No GET or inspection publishes state.
    /// </summary>
    public interface IXRegistryShortLinkMaintenance
    {
        /// <summary>
        /// Atomically backfills the live entity catalog, or returns zero if already initialized.
        /// No prepared mutation may remain outstanding during this operation.
        /// </summary>
        ValueTask<int> InitializeShortLinksAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default);
    }

    public sealed partial class XRegistryTransactionalEndpoint :
        IXRegistryAddressResolver,
        IXRegistryShortLinkMaintenance
    {
        /// <inheritdoc/>
        public async ValueTask<XRegistryAddressResolution> ResolveAddressAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            request.ThrowIfNull(nameof(request));
            if (!await AuthorizeAsync(request.Context, request.IsMutation, cancellationToken).ConfigureAwait(false))
            {
                return new XRegistryAddressResolution(
                    request, Error("unauthorized", "Address access is not authorized.", 403));
            }
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                JsonObject snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
                RequireShortLinks(snapshot);
                var model = new XRegistryModelRules(SnapshotModel(snapshot));
                return new XRegistryAddressResolution(ResolveShortLink(snapshot, model, request));
            }
            catch (XRegistryRejectionException exception)
            {
                return new XRegistryAddressResolution(
                    request, Error(exception.Code, exception.Message, exception.StatusCode));
            }
            finally
            {
                m_serial.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<int> InitializeShortLinksAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            context.ThrowIfNull(nameof(context));
            if (!await AuthorizeAsync(context, true, cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException("Short-link initialization requires write authorization.");
            }
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref m_preparedOperations) != 0)
                {
                    throw new InvalidOperationException(
                        "Complete outstanding preparations before initializing short links.");
                }
                JsonObject prior = await LoadAsync(cancellationToken).ConfigureAwait(false);
                if (prior["shortlinks"] is not null)
                {
                    return 0;
                }
                var candidate = (JsonObject)prior.DeepClone();
                candidate["format"] = 2;
                candidate["shortlinks"] = new JsonObject
                {
                    ["root"] = Url("/"),
                    ["prefix"] = m_options.ShortLinkPrefix,
                    ["next"] = 1UL,
                    ["paths"] = new JsonObject()
                };
                var transaction = new Transaction(
                    candidate,
                    prior,
                    new XRegistryRequest(XRegistryAction.Merge, "/") { Context = context },
                    m_time.GetUtcNow().UtcDateTime);
                UpdateShortLinks(transaction, new XRegistryModelRules(SnapshotModel(candidate)));
                candidate["generation"] = XRegistryModelRules.Unsigned(prior["generation"]).AddOne();
                ByteString replacement = EncodeSnapshot(candidate);
                cancellationToken.ThrowIfCancellationRequested();
                m_indeterminate = true;
                bool published = await m_store.CommitAsync(m_expected, replacement, cancellationToken)
                    .ConfigureAwait(false);
                m_indeterminate = false;
                if (!published)
                {
                    m_snapshot = null;
                    throw new InvalidOperationException("The registry changed before short-link initialization.");
                }
                m_snapshot = candidate;
                m_expected = replacement;
                m_observationGeneration = Guid.NewGuid().ToString("N");
                return XRegistryModelRules.Object(candidate["shortlinks"]!["paths"]).Count;
            }
            finally
            {
                m_serial.Release();
            }
        }

        private static void ValidateShortLinkOptions(XRegistryTransactionalOptions options)
        {
            if (options.ShortLinkPrefix is null ||
                options.MaxShortLinks < 1 ||
                XRegistryPath.Normalize(options.ShortLinkPrefix) != options.ShortLinkPrefix ||
                XRegistryPath.GetSegments(options.ShortLinkPrefix).Count != 1 ||
                options.ShortLinkPrefix.Length > 64 ||
                options.ShortLinkPrefix is "/model" or "/modelsource" or "/capabilities" or
                    "/capabilitiesoffered" or "/export" or "/.xregistry" ||
                options.ShortLinkPrefix.Skip(1).Any(character =>
                    character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-')))
            {
                throw new ArgumentException(
                    "Short links require a positive quota and one unambiguous ASCII path segment.",
                    nameof(options));
            }
        }

        private void RequireShortLinks(JsonObject snapshot)
        {
            if (m_options.ShortLinksEnabled && snapshot["shortlinks"] is null)
            {
                throw new XRegistryRejectionException("shortlinks_not_initialized",
                    "Explicit InitializeShortLinksAsync maintenance is required before enabling shortself.", 503);
            }
        }

        private void ValidateShortLinkState(JsonObject snapshot)
        {
            if (snapshot["shortlinks"] is not { } value)
            {
                if (XRegistryModelRules.Unsigned(snapshot["format"]) == 2)
                {
                    throw new InvalidDataException("The short-link state format requires its committed alias catalog.");
                }
                return;
            }
            if (XRegistryModelRules.Unsigned(snapshot["format"]) != 2)
            {
                throw new InvalidDataException("Persistent short links require the versioned alias state format.");
            }
            JsonObject catalog = XRegistryModelRules.Object(value);
            if (XRegistryModelRules.Text(catalog["prefix"]) != m_options.ShortLinkPrefix ||
                XRegistryModelRules.Text(catalog["root"]) != Url("/"))
            {
                throw new InvalidDataException("Persisted short links cannot change their registry root or prefix.");
            }
            if (catalog["next"] is not JsonValue sequence || !sequence.TryGetValue(out ulong next) || next == 0)
            {
                throw new InvalidDataException("The short-link sequence is invalid.");
            }
            JsonObject paths = XRegistryModelRules.Object(catalog["paths"]);
            if (paths.Count > m_options.MaxShortLinks)
            {
                throw new InvalidDataException("The short-link catalog exceeds its configured quota.");
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string path, JsonNode? entry) in paths)
            {
                string id = XRegistryModelRules.Text(entry?["id"]);
                if (XRegistryPath.Normalize(path) != path ||
                    !ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong number) ||
                    number == 0 ||
                    number >= next ||
                    number.ToString(CultureInfo.InvariantCulture) != id ||
                    !ids.Add(id) ||
                    entry?["identity"] is not JsonValue identity ||
                    !identity.TryGetValue(out string? token) ||
                    string.IsNullOrEmpty(token))
                {
                    throw new InvalidDataException("The short-link catalog contains an invalid or repeated identity.");
                }
            }
        }

        private static XRegistryRequest ResolveShortLink(
            JsonObject snapshot, XRegistryModelRules model, XRegistryRequest request)
        {
            string address = request.AddressPath ?? request.Path;
            if (snapshot["shortlinks"] is not JsonObject catalog)
            {
                if (request.AddressPath is not null)
                {
                    throw new XRegistryRejectionException(
                        "action_not_supported", "Address resolution is unavailable.", 405);
                }
                return request;
            }
            string prefix = XRegistryModelRules.Text(catalog["prefix"]);
            if (address != prefix && !address.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                if (request.AddressPath is not null)
                {
                    throw new XRegistryRejectionException(
                        "bad_request", "The original address is not a registered alias.");
                }
                return request;
            }
            string? target = null;
            foreach ((string path, JsonNode? entry) in XRegistryModelRules.Object(catalog["paths"]))
            {
                if (address == prefix + "/" + XRegistryModelRules.Text(entry?["id"]))
                {
                    target = path;
                    break;
                }
            }
            if (target is null)
            {
                throw new XRegistryRejectionException(
                    "not_found", "The short link no longer identifies an entity.", 404);
            }
            if (request.AddressPath is not null && request.Path != target)
            {
                throw new XRegistryRejectionException(
                    "address_changed", "The resolved entity does not match its alias.", 409);
            }
            _ = model.Resolve(target);
            return request.AtResolvedPath(target);
        }

        private void UpdateShortLinks(Transaction transaction, XRegistryModelRules model)
        {
            if (transaction.Snapshot["shortlinks"] is not JsonObject catalog)
            {
                return;
            }
            string prefix = XRegistryModelRules.Text(catalog["prefix"]);
            if (XRegistryModelRules.Object(SnapshotModel(transaction.Snapshot)["groups"]).ContainsKey(prefix[1..]))
            {
                throw new XRegistryRejectionException(
                    "model_error", "A model collection collides with the short-link mount.");
            }
            JsonObject paths = XRegistryModelRules.Object(catalog["paths"]);
            Dictionary<string, string> desired = ShortLinkEntities(transaction, model);
            if (desired.Count > m_options.MaxShortLinks)
            {
                throw new XRegistryRejectionException("too_large", "The short-link catalog quota is exhausted.", 413);
            }
            foreach (string retired in paths.Select(pair => pair.Key)
                .Where(path => !desired.ContainsKey(path))
                .ToArray())
            {
                paths.Remove(retired);
            }
            ulong next = catalog["next"]!.GetValue<ulong>();
            foreach ((string path, string identity) in desired.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string storage = StoragePath(model.Resolve(path));
                if (paths[path] is JsonObject existing &&
                    XRegistryModelRules.Text(existing["identity"]) == identity &&
                    !transaction.Created.Contains(storage))
                {
                    continue;
                }
                if (next == ulong.MaxValue)
                {
                    throw new XRegistryRejectionException("too_large", "The short-link sequence is exhausted.", 413);
                }
                paths[path] = new JsonObject
                {
                    ["id"] = next.ToString(CultureInfo.InvariantCulture),
                    ["identity"] = identity
                };
                next++;
            }
            catalog["next"] = next;
        }

        private Dictionary<string, string> ShortLinkEntities(Transaction transaction, XRegistryModelRules model)
        {
            var desired = new Dictionary<string, string>(StringComparer.Ordinal);
            Transaction view = ReferenceView(transaction, model);
            foreach ((string path, JsonNode? value) in view.Entries)
            {
                XRegistryTarget target = model.Resolve(path);
                desired.Add(path, target.Kind == XRegistryEntityKind.Version
                    ? XRegistryModelRules.Text(value?["incarnation"]) : "entity");
                if (target.Kind == XRegistryEntityKind.Meta)
                {
                    desired.Add(Parent(path), "entity");
                }
            }
            return desired;
        }

        private void ValidateShortLinkEntities(JsonObject snapshot, XRegistryModelRules model)
        {
            if (snapshot["shortlinks"] is not JsonObject catalog)
            {
                return;
            }
            var transaction = new Transaction(snapshot, snapshot,
                new XRegistryRequest(XRegistryAction.Read, "/"), m_time.GetUtcNow().UtcDateTime);
            Dictionary<string, string> entities = ShortLinkEntities(transaction, model);
            JsonObject paths = XRegistryModelRules.Object(catalog["paths"]);
            if (paths.Count != entities.Count ||
                entities.Any(pair => paths[pair.Key]?["identity"] is not JsonNode value ||
                    XRegistryModelRules.Text(value) != pair.Value))
            {
                throw new InvalidDataException("The alias catalog does not match the committed entity incarnations.");
            }
        }

        private void AddShortLink(Transaction transaction, string path, JsonObject metadata)
        {
            if (!m_options.ShortLinksEnabled)
            {
                metadata.Remove("shortself");
                return;
            }
            JsonObject catalog = XRegistryModelRules.Object(transaction.Snapshot["shortlinks"]);
            string id = catalog["paths"]?[path]?["id"] is JsonNode value
                ? XRegistryModelRules.Text(value)
                : throw new InvalidDataException("The entity is missing its committed short-link identity.");
            metadata["shortself"] = XRegistryModelRules.Text(catalog["root"]) +
                XRegistryModelRules.Text(catalog["prefix"]) +
                "/" +
                id;
        }
    }
}
