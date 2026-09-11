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
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    internal sealed class XRegistrySyncState(string jobId, string configurationFingerprint)
    {
        public string JobId { get; } = jobId;

        public string ConfigurationFingerprint { get; } = configurationFingerprint;

        public string ScopeFingerprint { get; set; } = string.Empty;

        public long Generation { get; set; }

        public long Sequence { get; set; }

        public SortedDictionary<string, XRegistrySyncBaseline> Baselines { get; } = new(StringComparer.Ordinal);

        public SortedDictionary<string, XRegistrySyncIntent> Intents { get; } = new(StringComparer.Ordinal);

        public SortedDictionary<string, XRegistrySyncConflict> Conflicts { get; } = new(StringComparer.Ordinal);

        public SortedDictionary<string, XRegistrySyncTombstone> Tombstones { get; } = new(StringComparer.Ordinal);

        public string NextId(string category)
        {
            Sequence = checked(Sequence + 1);
            return ConfigurationFingerprint + "-" + category + "-" + Sequence.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal sealed record XRegistrySyncBaseline(
        XRegistrySyncObservation OpcUa,
        XRegistrySyncObservation Http,
        DateTimeOffset ConfirmedAt);

    internal enum XRegistrySyncIntentKind
    {
        Replace,
        Create,
        Delete
    }

    internal enum XRegistrySyncIntentState
    {
        Prepared,
        Responded,
        Verified,
        Rejected,
        Conflicted
    }

    internal sealed record XRegistrySyncIntent(
        string Id,
        string Path,
        XRegistrySyncSide Destination,
        XRegistrySyncIntentKind Kind,
        XRegistryRequest Request,
        ArrayOf<XRegistrySyncObservation> Source,
        ArrayOf<XRegistrySyncObservation> DestinationBefore,
        DateTimeOffset PreparedAt)
    {
        public XRegistrySyncIntentState State { get; init; }

        public XRegistryResponse? Response { get; init; }

        public string? Failure { get; init; }

        public bool Pending => State is XRegistrySyncIntentState.Prepared or XRegistrySyncIntentState.Responded;
    }

    internal sealed record XRegistrySyncTombstone(
        string Id,
        string Path,
        XRegistrySyncBaseline Baseline,
        DateTimeOffset ConfirmedAt);

    internal sealed class XRegistrySyncStateCodec(int maximumBytes = 67_108_864, int maximumDepth = 64)
    {
        public ByteString Encode(XRegistrySyncState state)
        {
            var payload = new JsonObject
            {
                ["job"] = state.JobId,
                ["configuration"] = state.ConfigurationFingerprint,
                ["scope"] = state.ScopeFingerprint,
                ["generation"] = state.Generation,
                ["sequence"] = state.Sequence,
                ["baselines"] = new JsonArray([.. state.Baselines.Values.Select(Baseline).Cast<JsonNode?>()]),
                ["intents"] = new JsonArray([.. state.Intents.Values.Select(Intent).Cast<JsonNode?>()]),
                ["conflicts"] = new JsonArray([.. state.Conflicts.Values.Select(Conflict).Cast<JsonNode?>()]),
                ["tombstones"] = new JsonArray([.. state.Tombstones.Values.Select(Tombstone).Cast<JsonNode?>()])
            };
            var envelope = new JsonObject
            {
                ["format"] = 1,
                ["sha256"] = XRegistrySyncJson.Fingerprint(payload, maximumBytes, maximumDepth),
                ["payload"] = payload
            };
            return XRegistrySyncJson.Encode(envelope, maximumBytes, maximumDepth);
        }

        public XRegistrySyncState Decode(ByteString bytes)
        {
            try
            {
                return DecodeCore(bytes);
            }
            catch (Exception exception) when (exception is JsonException or FormatException or
                InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
            {
                throw new InvalidDataException("Synchronization state is malformed; explicit recovery is required.",
                    exception);
            }
        }

        public static JsonObject Observation(XRegistrySyncObservation value)
        {
            return new JsonObject
            {
                ["path"] = value.Path,
                ["kind"] = (int)value.Kind,
                ["fingerprint"] = value.Fingerprint,
                ["epoch"] = value.Epoch,
                ["metadata"] = XRegistrySyncJson.Object(value.Metadata),
                ["document"] = value.Document.IsNull ? null : Convert.ToBase64String(value.Document.ToArray())
            };
        }

        private XRegistrySyncState DecodeCore(ByteString bytes)
        {
            using JsonDocument document = XRegistrySyncJson.Parse(bytes, maximumBytes, maximumDepth);
            JsonElement envelope = document.RootElement;
            _ = XRegistrySyncJson.Integer(envelope, "format", 1, 1);
            JsonElement payload = envelope.GetProperty("payload");
            string checksum = XRegistrySyncJson.Fingerprint(
                XRegistrySyncJson.Object(payload), maximumBytes, maximumDepth);
            if (!string.Equals(checksum, XRegistrySyncJson.String(envelope, "sha256"), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Synchronization state checksum is invalid; explicit recovery is required.");
            }
            var state = new XRegistrySyncState(
                XRegistrySyncJson.String(payload, "job"), XRegistrySyncJson.String(payload, "configuration"))
            {
                ScopeFingerprint = XRegistrySyncJson.String(payload, "scope"),
                Generation = NonnegativeLong(payload, "generation"),
                Sequence = NonnegativeLong(payload, "sequence")
            };
            if (string.IsNullOrWhiteSpace(state.JobId) || state.ConfigurationFingerprint.Length != 64)
            {
                throw new InvalidDataException("Synchronization state has no valid owner identity.");
            }
            foreach (JsonElement element in Array(payload, "baselines"))
            {
                XRegistrySyncBaseline baseline = ReadBaseline(element);
                Add(state.Baselines, baseline.OpcUa.Path, baseline);
            }
            foreach (JsonElement element in Array(payload, "intents"))
            {
                XRegistrySyncIntent intent = ReadIntent(element);
                Add(state.Intents, intent.Id, intent);
            }
            foreach (JsonElement element in Array(payload, "conflicts"))
            {
                XRegistrySyncConflict conflict = ReadConflict(element);
                Add(state.Conflicts, conflict.Id, conflict);
            }
            foreach (JsonElement element in Array(payload, "tombstones"))
            {
                var tombstone = new XRegistrySyncTombstone(
                    XRegistrySyncJson.String(element, "id"),
                    Path(element),
                    ReadBaseline(element.GetProperty("baseline")),
                    Date(element, "confirmedAt"));
                if (tombstone.Path != tombstone.Baseline.OpcUa.Path)
                {
                    throw new InvalidDataException("A tombstone has an inconsistent entity identity.");
                }
                Add(state.Tombstones, tombstone.Id, tombstone);
            }
            return state;
        }

        private JsonObject Intent(XRegistrySyncIntent value)
        {
            var codec = new XRegistryProtocolCodec(maximumBytes, maximumDepth);
            return new JsonObject
            {
                ["id"] = value.Id,
                ["path"] = value.Path,
                ["destination"] = (int)value.Destination,
                ["kind"] = (int)value.Kind,
                ["state"] = (int)value.State,
                ["request"] = Convert.ToBase64String(codec.EncodeRequest(value.Request).ToArray()),
                ["requestDigest"] = codec.ComputeRequestDigest(value.Request),
                ["source"] = new JsonArray([.. value.Source.Span.ToArray().Select(Observation).Cast<JsonNode?>()]),
                ["destinationBefore"] =
                    new JsonArray([.. value.DestinationBefore.Span.ToArray()
                        .Select(Observation).Cast<JsonNode?>()]),
                ["preparedAt"] = Text(value.PreparedAt),
                ["response"] = value.Response is null
                    ? null
                    : Convert.ToBase64String(codec.EncodeResponse(value.Response).ToArray()),
                ["failure"] = value.Failure
            };
        }

        private static JsonObject Baseline(XRegistrySyncBaseline value)
        {
            return new JsonObject
            {
                ["opcUa"] = Observation(value.OpcUa),
                ["http"] = Observation(value.Http),
                ["confirmedAt"] = Text(value.ConfirmedAt)
            };
        }

        private static JsonObject Conflict(XRegistrySyncConflict value)
        {
            return new JsonObject
            {
                ["id"] = value.Id,
                ["path"] = value.Path,
                ["reason"] = value.Reason,
                ["observedAt"] = Text(value.ObservedAt),
                ["opcUa"] = value.OpcUa is null ? null : Observation(value.OpcUa),
                ["http"] = value.Http is null ? null : Observation(value.Http),
                ["baseline"] = value.BaselineFingerprint,
                ["operationId"] = value.OperationId,
                ["status"] = (int)value.Status,
                ["resolution"] = (int)value.Resolution,
                ["resolutionRequestedAt"] = value.ResolutionRequestedAt is DateTimeOffset requested
                    ? Text(requested)
                    : null
            };
        }

        private static JsonObject Tombstone(XRegistrySyncTombstone value)
        {
            return new JsonObject
            {
                ["id"] = value.Id,
                ["path"] = value.Path,
                ["baseline"] = Baseline(value.Baseline),
                ["confirmedAt"] = Text(value.ConfirmedAt)
            };
        }

        private XRegistrySyncIntent ReadIntent(JsonElement element)
        {
            var codec = new XRegistryProtocolCodec(maximumBytes, maximumDepth);
            XRegistryRequest request = codec.DecodeRequest(
                Bytes(element, "request"), XRegistryCallContext.Anonymous);
            string id = XRegistrySyncJson.String(element, "id");
            if ((request.OperationId is not null && request.OperationId != id) ||
                !request.IsMutation ||
                codec.ComputeRequestDigest(request) != XRegistrySyncJson.String(element, "requestDigest"))
            {
                throw new InvalidDataException("A stored synchronization intent is inconsistent.");
            }
            var intent = new XRegistrySyncIntent(
                id,
                Path(element),
                (XRegistrySyncSide)XRegistrySyncJson.Integer(element, "destination", 0, 1),
                (XRegistrySyncIntentKind)XRegistrySyncJson.Integer(element, "kind", 0, 2),
                request,
                [.. Array(element, "source").Select(ReadObservation)],
                [.. Array(element, "destinationBefore").Select(ReadObservation)],
                Date(element, "preparedAt"))
            {
                State = (XRegistrySyncIntentState)XRegistrySyncJson.Integer(element, "state", 0, 4),
                Response = OptionalBytes(element, "response") is { IsNull: false } response
                    ? codec.DecodeResponse(response)
                    : null,
                Failure = XRegistrySyncJson.OptionalString(element, "failure")
            };
            if ((intent.Kind == XRegistrySyncIntentKind.Delete && intent.DestinationBefore.Count == 0) ||
                (intent.Kind != XRegistrySyncIntentKind.Delete && intent.Source.Count == 0) ||
                (intent.State == XRegistrySyncIntentState.Responded && intent.Response is null))
            {
                throw new InvalidDataException("A stored synchronization intent has no verification evidence.");
            }
            return intent;
        }

        private XRegistrySyncBaseline ReadBaseline(JsonElement element)
        {
            var baseline = new XRegistrySyncBaseline(
                ReadObservation(element.GetProperty("opcUa")),
                ReadObservation(element.GetProperty("http")),
                Date(element, "confirmedAt"));
            if (baseline.OpcUa.Path != baseline.Http.Path ||
                baseline.OpcUa.Kind != baseline.Http.Kind ||
                baseline.OpcUa.Fingerprint != baseline.Http.Fingerprint)
            {
                throw new InvalidDataException("A synchronization baseline does not describe equal live states.");
            }
            return baseline;
        }

        private XRegistrySyncObservation ReadObservation(JsonElement element)
        {
            string epoch = XRegistrySyncJson.String(element, "epoch");
            XRegistrySyncJson.ValidateEpoch(epoch);
            string path = Path(element);
            var kind = (XRegistrySyncEntityKind)XRegistrySyncJson.Integer(element, "kind", 0, 3);
            JsonElement metadata = element.GetProperty("metadata");
            _ = XRegistrySyncJson.Object(metadata);
            var observation = new XRegistrySyncObservation(
                path, kind, XRegistrySyncJson.String(element, "fingerprint"),
                epoch, metadata, OptionalBytes(element, "document"));
            string fingerprint = XRegistrySyncModel.Fingerprint(
                path, kind, metadata, observation.Document, maximumBytes, maximumDepth);
            if (observation.Fingerprint != fingerprint)
            {
                throw new InvalidDataException("A stored observation fingerprint is inconsistent.");
            }
            return observation;
        }

        private XRegistrySyncConflict ReadConflict(JsonElement element)
        {
            var conflict = new XRegistrySyncConflict(
                XRegistrySyncJson.String(element, "id"),
                Path(element),
                XRegistrySyncJson.String(element, "reason"),
                Date(element, "observedAt"))
            {
                OpcUa = OptionalObservation(element, "opcUa"),
                Http = OptionalObservation(element, "http"),
                BaselineFingerprint = XRegistrySyncJson.OptionalString(element, "baseline"),
                OperationId = XRegistrySyncJson.OptionalString(element, "operationId"),
                Status = (XRegistrySyncConflictStatus)XRegistrySyncJson.Integer(element, "status", 0, 3),
                Resolution = (XRegistrySyncConflictPolicy)XRegistrySyncJson.Integer(element, "resolution", 0, 2),
                ResolutionRequestedAt = XRegistrySyncJson.OptionalString(element, "resolutionRequestedAt") is null
                    ? null
                    : Date(element, "resolutionRequestedAt")
            };
            if ((conflict.OpcUa is not null && conflict.OpcUa.Path != conflict.Path) ||
                (conflict.Http is not null && conflict.Http.Path != conflict.Path) ||
                (conflict.Status == XRegistrySyncConflictStatus.ResolutionRequested &&
                    (conflict.Resolution == XRegistrySyncConflictPolicy.Manual ||
                        conflict.ResolutionRequestedAt is null)))
            {
                throw new InvalidDataException("A stored conflict has inconsistent guarded observations.");
            }
            return conflict;
        }

        private XRegistrySyncObservation? OptionalObservation(JsonElement element, string name)
        {
            JsonElement value = element.GetProperty(name);
            return value.ValueKind == JsonValueKind.Null ? null : ReadObservation(value);
        }

        private static ByteString OptionalBytes(JsonElement element, string name)
        {
            return XRegistrySyncJson.OptionalString(element, name) is null ? default : Bytes(element, name);
        }

        private static ByteString Bytes(JsonElement element, string name)
        {
            return new ByteString(Convert.FromBase64String(XRegistrySyncJson.String(element, name)));
        }

        private static JsonElement.ArrayEnumerator Array(JsonElement element, string name)
        {
            JsonElement value = element.GetProperty(name);
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException($"Expected array '{name}'.");
            }
            return value.EnumerateArray();
        }

        private static long NonnegativeLong(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) ||
                !value.TryGetInt64(out long number) ||
                number < 0)
            {
                throw new JsonException($"Invalid nonnegative counter '{name}'.");
            }
            return number;
        }

        private static DateTimeOffset Date(JsonElement element, string name)
        {
            if (!DateTimeOffset.TryParseExact(XRegistrySyncJson.String(element, name), "O",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value))
            {
                throw new JsonException($"Invalid timestamp '{name}'.");
            }
            return value;
        }

        private static string Text(DateTimeOffset value)
        {
            return value.ToString("O", CultureInfo.InvariantCulture);
        }

        private static string Path(JsonElement element)
        {
            string value = XRegistrySyncJson.String(element, "path");
            if (XRegistryPath.Normalize(value) != value)
            {
                throw new InvalidDataException("Stored entity paths must be canonical.");
            }
            return value;
        }

        private static void Add<T>(SortedDictionary<string, T> items, string id, T value)
        {
            if (string.IsNullOrWhiteSpace(id) || items.ContainsKey(id))
            {
                throw new InvalidDataException("Duplicate or absent synchronization state identity.");
            }
            items.Add(id, value);
        }
    }
}
