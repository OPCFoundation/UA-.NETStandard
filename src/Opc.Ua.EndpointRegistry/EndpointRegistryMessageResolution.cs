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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry
{
    /// <summary>
    /// Supplies authenticated, server-owned Message and Schema observations to the side-effect-free resolver.
    /// Implementations validate transport/session evidence before returning metadata and never fetch a
    /// client-supplied URL merely because it appears in a resolution request.
    /// </summary>
    public interface IEndpointRegistryResolutionProvider
    {
        /// <summary>
        /// Reads one authorized Message metadata observation for the supplied entity reference.
        /// </summary>
        ValueTask<EndpointRegistryMessageObservation?> ReadMessageAsync(
            RegistryEntityReferenceDataType reference,
            CancellationToken cancellationToken);

        /// <summary>
        /// Resolves an explicitly authorized schema binding for an already materialized Message.
        /// </summary>
        ValueTask<SchemaDocumentDataType?> ResolveSchemaAsync(
            MessageDefinitionDataType definition,
            RegistryEntityReferenceDataType origin,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Authenticated raw Message metadata plus its verified federation source.
    /// </summary>
    public sealed class EndpointRegistryMessageObservation
    {
        /// <summary>
        /// Gets or sets the verified source reference.
        /// </summary>
        public RegistryEntityReferenceDataType Source { get; set; } = new();

        /// <summary>
        /// Gets or sets the exact raw Message metadata.
        /// </summary>
        public RegistryObjectValueDataType Metadata { get; set; } = new() { Kind = 5, Members = [] };

        /// <summary>
        /// Gets or sets the observed committed epoch.
        /// </summary>
        public uint Epoch { get; set; }
    }

    /// <summary>
    /// Server-owned inputs for one Message resolution call.
    /// </summary>
    public sealed class EndpointRegistryMessageResolutionContext
    {
        /// <summary>
        /// Gets or sets the local registry origin used to scope relative references.
        /// </summary>
        public RegistryEntityReferenceDataType LocalOrigin { get; set; } = new();

        /// <summary>
        /// Gets or sets the reflection-free native record mapper.
        /// </summary>
        public RegistryRecordMapper Mapper { get; set; } = null!;

        /// <summary>
        /// Gets or sets the authenticated observation provider.
        /// </summary>
        public IEndpointRegistryResolutionProvider Provider { get; set; } = null!;
    }

    /// <summary>
    /// Immutable federation origin key. An origin is either a stable OriginUri or an OPC UA
    /// ApplicationUri plus portable registry root identity.
    /// </summary>
    public sealed class EndpointRegistryOriginKey : IEquatable<EndpointRegistryOriginKey>
    {
        /// <summary>
        /// Creates a key from a typed entity reference's origin fields.
        /// </summary>
        public EndpointRegistryOriginKey(RegistryEntityReferenceDataType value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (!string.IsNullOrEmpty(value.OriginUri))
            {
                if (!string.IsNullOrEmpty(value.ApplicationUri) || !IsNull(value.RegistryNode))
                {
                    throw new ArgumentException("OriginUri cannot be combined with ApplicationUri or RegistryNode.",
                        nameof(value));
                }
                Kind = "uri";
                OriginUri = value.OriginUri;
            }
            else
            {
                if (string.IsNullOrEmpty(value.ApplicationUri) || IsNull(value.RegistryNode))
                {
                    throw new ArgumentException("An OPC UA origin requires ApplicationUri and RegistryNode.",
                        nameof(value));
                }
                Kind = "ua";
                ApplicationUri = value.ApplicationUri;
                RegistryNode = value.RegistryNode;
            }
        }

        /// <summary>
        /// Gets the key kind: <c>uri</c> or <c>ua</c>.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Gets the stable origin URI when <see cref="Kind"/> is <c>uri</c>.
        /// </summary>
        public string? OriginUri { get; }

        /// <summary>
        /// Gets the application URI when <see cref="Kind"/> is <c>ua</c>.
        /// </summary>
        public string? ApplicationUri { get; }

        /// <summary>
        /// Gets the portable registry root NodeId when <see cref="Kind"/> is <c>ua</c>.
        /// </summary>
        public ExpandedNodeId RegistryNode { get; }

        /// <inheritdoc/>
        public bool Equals(EndpointRegistryOriginKey? other)
        {
            return other is not null &&
                string.Equals(Kind, other.Kind, StringComparison.Ordinal) &&
                string.Equals(OriginUri, other.OriginUri, StringComparison.Ordinal) &&
                string.Equals(ApplicationUri, other.ApplicationUri, StringComparison.Ordinal) &&
                ExpandedNodeIdEquals(RegistryNode, other.RegistryNode);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as EndpointRegistryOriginKey);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Kind, StringComparer.Ordinal);
            hash.Add(OriginUri, StringComparer.Ordinal);
            hash.Add(ApplicationUri, StringComparer.Ordinal);
            hash.Add(RegistryNode.ToString(), StringComparer.Ordinal);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Returns whether two typed references name the same immutable origin.
        /// </summary>
        public static bool SameOrigin(RegistryEntityReferenceDataType left, RegistryEntityReferenceDataType right)
        {
            return new EndpointRegistryOriginKey(left).Equals(new EndpointRegistryOriginKey(right));
        }

        private static bool ExpandedNodeIdEquals(ExpandedNodeId left, ExpandedNodeId right)
        {
            return string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
        }

        private static bool IsNull(ExpandedNodeId value)
        {
            return value.IsNull || value == ExpandedNodeId.Null;
        }
    }

    /// <summary>
    /// Resolves typed Endpoint Registry Message references against supplied trusted observations.
    /// </summary>
    public sealed class EndpointRegistryMessageResolver
    {
        /// <summary>
        /// Resolves the request into an effective Message definition and optional schema.
        /// </summary>
        public async ValueTask<NativeMessageResolutionResultDataType> ResolveAsync(
            MessageResolutionRequestDataType request,
            EndpointRegistryMessageResolutionContext context,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request is null || string.IsNullOrEmpty(request.Reference))
            {
                return Failure("missing-inputs", "E_INPUT", "Reference", "a Message reference is required", []);
            }
            if (context.Mapper is null)
            {
                throw new ArgumentException("A native mapper is required.", nameof(context));
            }
            if (context.Provider is null)
            {
                throw new ArgumentException("A resolution provider is required.", nameof(context));
            }
            try
            {
                ValidateSemantics(request.RequiredSemantics);
                var state = new ResolutionState(request, context);
                RegistryObjectValueDataType metadata = await VisitAsync(
                    request.Reference,
                    context.LocalOrigin,
                    state,
                    cancellationToken).ConfigureAwait(false);
                MessageDefinitionDataType definition = (MessageDefinitionDataType)context.Mapper.Project(
                    metadata,
                    nameof(MessageDefinitionDataType));
                SchemaDocumentDataType? schema = null;
                if (NeedsSchema(request, metadata))
                {
                    schema = await context.Provider.ResolveSchemaAsync(
                        definition,
                        context.LocalOrigin,
                        cancellationToken).ConfigureAwait(false);
                    if (schema is null && HasSchemaReference(metadata))
                    {
                        return Failure(
                            "missing-inputs",
                            "E_SCHEMA_PROVIDER_MISSING",
                            "dataschema",
                            "no separately authorized Schema provider was supplied",
                            state.Sources);
                    }
                }
                var success = new NativeMessageResolutionResultDataType
                {
                    StatusCode = StatusCodes.Good,
                    Status = "complete",
                    Issues = [],
                    Sources = [.. state.Sources],
                    Definition = definition
                };
                if (schema is not null)
                {
                    success.Schema = schema;
                }
                return success;
            }
            catch (RegistryRuleException error)
            {
                string status = error.Code is "E_REFERENCE_MISSING" or "E_SCHEMA_PROVIDER_MISSING"
                    ? "missing-inputs"
                    : error.Code is "E_SOURCE_CONFLICT" or "E_SEMANTICS_UNSUPPORTED" or "E_CROSS_ORIGIN_SCHEMA"
                        ? "unsupported-semantics"
                        : "invalid-input";
                return Failure(status, error.Code, error.PathText, error.Detail, []);
            }
            catch (ArgumentException error)
            {
                return Failure("invalid-input", "E_REFERENCE_INVALID", "Reference", error.Message, []);
            }
            catch (InvalidOperationException error)
            {
                return Failure("invalid-input", "E_REFERENCE_INVALID", "Reference", error.Message, []);
            }
        }

        private static async ValueTask<RegistryObjectValueDataType> VisitAsync(
            string referenceUri,
            RegistryEntityReferenceDataType context,
            ResolutionState state,
            CancellationToken cancellationToken)
        {
            EndpointRegistryRules.ValidateMessageReference(referenceUri, "Reference");
            MessageReferenceBindingDataType binding = state.Find(referenceUri, context);
            RegistryEntityReferenceDataType target = binding.Target;
            if (referenceUri.StartsWith("/", StringComparison.Ordinal) &&
                !EndpointRegistryOriginKey.SameOrigin(binding.Context, target))
            {
                throw RegistryRuleException.Fail(
                    "E_REFERENCE_ORIGIN",
                    referenceUri,
                    "a relative reference cannot cross its declaring registry");
            }
            EndpointRegistryMessageObservation? observation = await state.Context.Provider.ReadMessageAsync(
                target,
                cancellationToken).ConfigureAwait(false);
            if (observation is null)
            {
                throw RegistryRuleException.Fail(
                    "E_REFERENCE_MISSING",
                    referenceUri,
                    "the authorized provider observation and metadata were not supplied");
            }
            VerifySource(target, observation.Source, referenceUri);
            string logical = LogicalXid(RequireXid(target, referenceUri));
            string cycleKey = new EndpointRegistryOriginKey(target).GetHashCode().ToString(
                System.Globalization.CultureInfo.InvariantCulture) + "|" + logical;
            if (!state.Active.Add(cycleKey))
            {
                throw RegistryRuleException.Fail(
                    "E_MESSAGE_CYCLE",
                    referenceUri,
                    "recursive base Message identity, including sole-Version aliases");
            }
            RegistryObjectValueDataType current = Clone(observation.Metadata);
            EndpointRegistryRules.ValidateMessage(current);
            VerifyIdentity(current, target, referenceUri);
            if (RegistryRuleValues.Has(current, "basemessage"))
            {
                throw RegistryRuleException.Fail(
                    "E_SOURCE_CONFLICT",
                    referenceUri,
                    "the unresolved prose spelling is not a basemessageuri alias");
            }
            state.AddSource(observation.Source);
            RegistryObjectValueDataType baseMessage = new() { Kind = 5, Members = [] };
            if (TryString(current, "basemessageuri", out string? baseUri))
            {
                baseMessage = await VisitAsync(baseUri!, target, state, cancellationToken).ConfigureAwait(false);
                if (InheritedLocalSchemaNeedsRebinding(baseMessage, current) &&
                    !EndpointRegistryOriginKey.SameOrigin(state.Find(baseUri!, target).Target, target))
                {
                    throw RegistryRuleException.Fail(
                        "E_CROSS_ORIGIN_SCHEMA",
                        baseUri!,
                        "cross-origin inheritance needs explicit schema rebinding; local Xids are never reinterpreted");
                }
            }
            RegistryObjectValueDataType result = EndpointRegistryRules.OverlayMessage(baseMessage, current);
            state.Active.Remove(cycleKey);
            return result;
        }

        private static void ValidateSemantics(ArrayOf<string> required)
        {
            foreach (string item in required)
            {
                if (item != "base-overlay-v1" && item != "schema-binding-v1")
                {
                    throw RegistryRuleException.Fail(
                        "E_SEMANTICS_UNSUPPORTED",
                        "requiredSemantics",
                        item);
                }
            }
        }

        private static bool NeedsSchema(MessageResolutionRequestDataType request, RegistryObjectValueDataType metadata)
        {
            if (request.CheckSchema)
            {
                return true;
            }
            foreach (string item in request.RequiredSemantics)
            {
                if (item == "schema-binding-v1")
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasSchemaReference(RegistryObjectValueDataType metadata)
        {
            return RegistryRuleValues.Has(metadata, "dataschemauri") ||
                RegistryRuleValues.Has(metadata, "dataschemaxid");
        }

        private static bool InheritedLocalSchemaNeedsRebinding(
            RegistryObjectValueDataType baseMessage,
            RegistryObjectValueDataType current)
        {
            return RegistryRuleValues.Has(baseMessage, "dataschemaxid") &&
                !RegistryRuleValues.Has(current, "dataschemaxid") ||
                TryString(baseMessage, "dataschemauri", out string? uri) &&
                uri!.StartsWith("/", StringComparison.Ordinal) &&
                !RegistryRuleValues.Has(current, "dataschemauri");
        }

        private static void VerifySource(
            RegistryEntityReferenceDataType expected,
            RegistryEntityReferenceDataType actual,
            string referenceUri)
        {
            if (!EndpointRegistryOriginKey.SameOrigin(expected, actual) ||
                !string.Equals(expected.Role, actual.Role, StringComparison.Ordinal) ||
                !string.Equals(expected.Xid, actual.Xid, StringComparison.Ordinal))
            {
                throw RegistryRuleException.Fail(
                    "E_REFERENCE_ORIGIN",
                    referenceUri,
                    "provider record is not bound to the selected source");
            }
        }

        private static void VerifyIdentity(
            RegistryObjectValueDataType current,
            RegistryEntityReferenceDataType target,
            string referenceUri)
        {
            string logical = LogicalXid(RequireXid(target, referenceUri));
            if (TryString(current, "xid", out string? xid) &&
                xid != logical &&
                xid != RequireXid(target, referenceUri))
            {
                throw RegistryRuleException.Fail(
                    "E_REFERENCE_IDENTITY",
                    referenceUri,
                    "returned metadata does not bind this exact entity URI or Xid");
            }
            if (!referenceUri.StartsWith("/", StringComparison.Ordinal) &&
                TryString(current, "self", out string? self) &&
                self != referenceUri)
            {
                throw RegistryRuleException.Fail(
                    "E_REFERENCE_IDENTITY",
                    referenceUri,
                    "returned metadata does not bind this exact entity URI or Xid");
            }
            string id = logical[(logical.LastIndexOf('/') + 1)..];
            if (TryString(current, "messageid", out string? messageId) && messageId != id)
            {
                throw RegistryRuleException.Fail(
                    "E_REFERENCE_IDENTITY",
                    referenceUri,
                    "raw Message and sole-Version identity differ from the authorized observation");
            }
        }

        private static string LogicalXid(string xid)
        {
            int versions = xid.IndexOf("/versions/", StringComparison.Ordinal);
            return versions < 0 ? xid : xid[..versions];
        }

        private static string RequireXid(RegistryEntityReferenceDataType target, string path)
        {
            if (string.IsNullOrEmpty(target.Xid))
            {
                throw RegistryRuleException.Fail("E_REFERENCE_INVALID", path, "a concrete target Xid is required");
            }
            return target.Xid;
        }

        private static bool TryString(RegistryObjectValueDataType value, string name, out string? text)
        {
            if (RegistryRuleValues.TryGet(value, name, out RegistryValueDataType? item) &&
                item is RegistryStringValueDataType stringValue &&
                item.Kind == 2)
            {
                text = stringValue.Value;
                return true;
            }
            text = null;
            return false;
        }

        private static RegistryObjectValueDataType Clone(RegistryObjectValueDataType value)
        {
            return (RegistryObjectValueDataType)RegistryValues.Parse(RegistryValues.ToJson(value).Span);
        }

        private static NativeMessageResolutionResultDataType Failure(
            string status,
            string code,
            string path,
            string detail,
            IReadOnlyList<RegistryEntityReferenceDataType> sources)
        {
            return new NativeMessageResolutionResultDataType
            {
                StatusCode = StatusCodes.BadInvalidArgument,
                Status = status,
                Issues =
                [
                    new RegistryDiagnosticDataType
                    {
                        StatusCode = StatusCodes.BadInvalidArgument,
                        Code = code,
                        Path = PathFrom(path),
                        Detail = detail
                    }
                ],
                Sources = [.. sources],
            };
        }

        private static ArrayOf<string> PathFrom(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                return [];
            }
            string value = path[0] == '/' ? path[1..] : path;
            return [.. value.Split('/')];
        }

        private sealed class ResolutionState
        {
            public ResolutionState(
                MessageResolutionRequestDataType request,
                EndpointRegistryMessageResolutionContext context)
            {
                Request = request;
                Context = context;
                foreach (MessageReferenceBindingDataType binding in request.References)
                {
                    if (string.IsNullOrEmpty(binding.ReferenceUri))
                    {
                        throw RegistryRuleException.Fail(
                            "E_INPUT",
                            "references",
                            "each entry binds one reference URI in its declaring origin");
                    }
                    string key = Key(binding.Context, binding.ReferenceUri);
                    if (!m_bindings.TryAdd(key, binding))
                    {
                        throw RegistryRuleException.Fail(
                            "E_REFERENCE_AMBIGUOUS",
                            binding.ReferenceUri,
                            "two descriptors claim the same context and reference");
                    }
                }
            }

            public MessageResolutionRequestDataType Request { get; }

            public EndpointRegistryMessageResolutionContext Context { get; }

            public HashSet<string> Active { get; } = [];

            public List<RegistryEntityReferenceDataType> Sources { get; } = [];

            public MessageReferenceBindingDataType Find(string referenceUri, RegistryEntityReferenceDataType context)
            {
                if (m_bindings.TryGetValue(Key(context, referenceUri), out MessageReferenceBindingDataType? binding))
                {
                    return binding;
                }
                if (!referenceUri.StartsWith("/", StringComparison.Ordinal))
                {
                    throw RegistryRuleException.Fail(
                        "E_REFERENCE_MISSING",
                        referenceUri,
                        "no explicit descriptor was supplied in this origin context");
                }
                return new MessageReferenceBindingDataType
                {
                    Context = CloneReference(context),
                    ReferenceUri = referenceUri,
                    Target = CloneReference(context, referenceUri)
                };
            }

            public void AddSource(RegistryEntityReferenceDataType source)
            {
                string key = SourceKey(source);
                if (m_sources.Add(key))
                {
                    Sources.Add(CloneReference(source));
                }
            }

            private static string Key(RegistryEntityReferenceDataType context, string referenceUri)
            {
                return new EndpointRegistryOriginKey(context).GetHashCode().ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + "|" + referenceUri;
            }

            private static string SourceKey(RegistryEntityReferenceDataType source)
            {
                return new EndpointRegistryOriginKey(source).GetHashCode().ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + "|" + source.Role + "|" + source.Xid;
            }

            private static RegistryEntityReferenceDataType CloneReference(
                RegistryEntityReferenceDataType source,
                string? xid = null)
            {
                var result = (RegistryEntityReferenceDataType)source.Clone();
                if (xid is not null)
                {
                    result.Xid = xid;
                    result.Role = xid.Contains("/versions/") ? "ExactVersion" : "LogicalResource";
                }
                return result;
            }

            private readonly Dictionary<string, MessageReferenceBindingDataType> m_bindings = new(StringComparer.Ordinal);
            private readonly HashSet<string> m_sources = new(StringComparer.Ordinal);
        }
    }
}
