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
using System.Text.Json;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Maps WoT <c>op</c> tokens to and from the generated
    /// <see cref="WoTBindingCapabilityEnum"/> operations.
    /// </summary>
    public static class WotOperations
    {
        /// <summary>
        /// Maps an <c>op</c> token to a capability operation.
        /// </summary>
        public static bool TryMap(string op, out WoTBindingCapabilityEnum operation)
        {
            switch (op)
            {
                case "readproperty":
                    operation = WoTBindingCapabilityEnum.ReadProperty;
                    return true;
                case "writeproperty":
                    operation = WoTBindingCapabilityEnum.WriteProperty;
                    return true;
                case "observeproperty":
                    operation = WoTBindingCapabilityEnum.ObserveProperty;
                    return true;
                case "unobserveproperty":
                    operation = WoTBindingCapabilityEnum.ObserveProperty;
                    return true;
                case "invokeaction":
                    operation = WoTBindingCapabilityEnum.InvokeAction;
                    return true;
                case "subscribeevent":
                    operation = WoTBindingCapabilityEnum.SubscribeEvent;
                    return true;
                case "unsubscribeevent":
                    operation = WoTBindingCapabilityEnum.UnsubscribeEvent;
                    return true;
                default:
                    operation = default;
                    return false;
            }
        }

        /// <summary>
        /// Gets whether the <c>op</c> token is compatible with the affordance kind.
        /// </summary>
        public static bool IsCompatible(WotAffordanceKind kind, string op)
        {
            switch (kind)
            {
                case WotAffordanceKind.Property:
                    return op is "readproperty" or "writeproperty" or "observeproperty" or "unobserveproperty";
                case WotAffordanceKind.Action:
                    return op is "invokeaction" or "queryaction" or "cancelaction";
                case WotAffordanceKind.Event:
                    return op is "subscribeevent" or "unsubscribeevent";
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Immutable, transport-neutral endpoint metadata compiled from a form. The
    /// well-known members expose the parsed endpoint; the
    /// <see cref="Metadata"/> bag carries binding-specific additions.
    /// </summary>
    public sealed class WotEndpointDescriptor
    {
        /// <summary>
        /// Initializes a new immutable endpoint descriptor.
        /// </summary>
        public WotEndpointDescriptor(
            string scheme,
            string? host,
            int port,
            string baseUri,
            ImmutableDictionary<string, string>? metadata = null)
        {
            Scheme = scheme ?? string.Empty;
            Host = host;
            Port = port;
            BaseUri = baseUri ?? string.Empty;
            Metadata = metadata ?? ImmutableDictionary<string, string>.Empty;
        }

        /// <summary>
        /// Gets the endpoint URI scheme (for example <c>http</c>, <c>mqtt</c>).
        /// </summary>
        public string Scheme { get; }

        /// <summary>
        /// Gets the endpoint host, if applicable.
        /// </summary>
        public string? Host { get; }

        /// <summary>
        /// Gets the endpoint port, or <c>-1</c> when not applicable.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Gets the canonical endpoint / base URI.
        /// </summary>
        public string BaseUri { get; }

        /// <summary>
        /// Gets binding-specific endpoint metadata.
        /// </summary>
        public ImmutableDictionary<string, string> Metadata { get; }
    }

    /// <summary>
    /// Immutable, transport-neutral addressing metadata compiled from a form.
    /// </summary>
    public sealed class WotAddressingDescriptor
    {
        /// <summary>
        /// Initializes a new immutable addressing descriptor.
        /// </summary>
        public WotAddressingDescriptor(string target, ImmutableDictionary<string, string>? metadata = null)
        {
            Target = target ?? string.Empty;
            Metadata = metadata ?? ImmutableDictionary<string, string>.Empty;
        }

        /// <summary>
        /// Gets the addressing target: an HTTP path/URL, an MQTT topic, a Modbus
        /// register reference or an OPC UA NodeId, depending on the binding.
        /// </summary>
        public string Target { get; }

        /// <summary>
        /// Gets binding-specific addressing metadata.
        /// </summary>
        public ImmutableDictionary<string, string> Metadata { get; }
    }

    /// <summary>
    /// The immutable event field selection compiled for an event affordance:
    /// the ordered <c>EventFilter</c> select clauses a MonitoredItem is created
    /// with, and where they came from (WoT Binding Section 6.1).
    /// </summary>
    /// <remarks>
    /// The clauses stay in the portable form the document authored — an
    /// ExpandedNodeId per clause and a browse path whose elements name their
    /// NamespaceUri — because a namespace index only means something to the
    /// session that read the namespace table. A channel resolves both against
    /// its own table when it materializes the OPC UA
    /// <c>SimpleAttributeOperand</c> list, which is the only point at which a
    /// table exists.
    /// </remarks>
    public sealed class WotEventSelection
    {
        /// <summary>
        /// Initializes a new immutable event selection.
        /// </summary>
        /// <param name="clauses">The ordered select clauses.</param>
        /// <param name="origin">Where the selection came from.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when no clause is supplied; an empty selection would request
        /// an event notification carrying no field at all.
        /// </exception>
        public WotEventSelection(ArrayOf<WotEventSelectClause> clauses, WotEventSelectionOrigin origin)
        {
            if (clauses.Count == 0)
            {
                throw new ArgumentException(
                    "An event selection carries at least one select clause.", nameof(clauses));
            }
            Clauses = clauses;
            Origin = origin;
        }

        /// <summary>
        /// The documented default selection of WoT Binding Section 6.1: the
        /// eight mandatory <c>BaseEventType</c> fields, which apply when an
        /// affordance states no selection of its own.
        /// </summary>
        public static WotEventSelection Default { get; } = new WotEventSelection(
            WotEventSelectClauses.Default, WotEventSelectionOrigin.Default);

        /// <summary>
        /// Gets the ordered select clauses. The order is the order the fields
        /// are requested in, so a consumer that reports field values
        /// positionally reports them in the order the document states.
        /// </summary>
        public ArrayOf<WotEventSelectClause> Clauses { get; }

        /// <summary>
        /// Gets where the selection came from.
        /// </summary>
        public WotEventSelectionOrigin Origin { get; }
    }

    /// <summary>
    /// Where a compiled event selection came from.
    /// </summary>
    public enum WotEventSelectionOrigin
    {
        /// <summary>
        /// The affordance stated no selection, so the documented default of
        /// WoT Binding Section 6.1 applies.
        /// </summary>
        Default,

        /// <summary>
        /// The affordance stated the complete list through the standardized
        /// <c>uav:eventSelectClauses</c> term, which replaces the default
        /// rather than extending it.
        /// </summary>
        Standard,

        /// <summary>
        /// The form stated extra fields through the superseded
        /// <c>uav:eventFields</c> spelling this implementation minted before
        /// the term was standardized. The extras are appended to the default
        /// set, which is what that spelling always meant.
        /// </summary>
        Legacy
    }

    /// <summary>
    /// Immutable operation metadata compiled from a form.
    /// </summary>
    public sealed class WotOperationDescriptor
    {
        /// <summary>
        /// Initializes a new immutable operation descriptor.
        /// </summary>
        public WotOperationDescriptor(
            WoTBindingCapabilityEnum operation,
            string opToken,
            string method,
            ImmutableDictionary<string, string>? metadata = null,
            TimeSpan? pollInterval = null)
        {
            Operation = operation;
            OpToken = opToken ?? string.Empty;
            Method = method ?? string.Empty;
            Metadata = metadata ?? ImmutableDictionary<string, string>.Empty;
            PollInterval = pollInterval;
        }

        /// <summary>
        /// Gets the polling interval this affordance's form declares for a subscription, when the
        /// protocol binding defines a standard term for it — Modbus does, through
        /// <c>modv:pollingTime</c>. <c>null</c> means the form does not declare one and the
        /// executor's configured default interval applies.
        /// </summary>
        public TimeSpan? PollInterval { get; }

        /// <summary>
        /// Gets the resolved capability operation.
        /// </summary>
        public WoTBindingCapabilityEnum Operation { get; }

        /// <summary>
        /// Gets the originating WoT <c>op</c> token.
        /// </summary>
        public string OpToken { get; }

        /// <summary>
        /// Gets the concrete protocol method: an HTTP verb, a Modbus function
        /// code mnemonic, an MQTT publish / subscribe verb or an OPC UA service.
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Gets binding-specific operation metadata.
        /// </summary>
        public ImmutableDictionary<string, string> Metadata { get; }
    }

    /// <summary>
    /// Immutable payload metadata compiled from a form.
    /// </summary>
    public sealed class WotPayloadDescriptor
    {
        /// <summary>
        /// Initializes a new immutable payload descriptor.
        /// </summary>
        public WotPayloadDescriptor(
            string contentType,
            string codecId,
            ImmutableDictionary<string, string>? metadata = null)
        {
            ContentType = contentType ?? string.Empty;
            CodecId = codecId ?? string.Empty;
            Metadata = metadata ?? ImmutableDictionary<string, string>.Empty;
        }

        /// <summary>
        /// Gets the resolved content type.
        /// </summary>
        public string ContentType { get; }

        /// <summary>
        /// Gets the id of the selected payload codec.
        /// </summary>
        public string CodecId { get; }

        /// <summary>
        /// Gets binding-specific payload metadata (for example numeric type / byte order).
        /// </summary>
        public ImmutableDictionary<string, string> Metadata { get; }
    }

    /// <summary>
    /// An immutable, protocol-neutral description of the OPC 10101 §6.5.4
    /// <c>uav:mapToNodeId</c> / <c>uav:mapToType</c> / <c>uav:mapByFieldPath</c>
    /// target-mapping terms authored on a property affordance. The terms let a
    /// non-OPC-UA source (for example a Modbus register or an HTTP resource) be
    /// projected onto a specific OPC UA target NodeId, or onto a field of a
    /// structured target type, so the mapping itself carries no protocol-specific
    /// addressing. Portable NodeIds are carried as strings; parsing/resolution is
    /// left to the consumer.
    /// </summary>
    public sealed class WotTargetMappingDescriptor
    {
        /// <summary>
        /// Initializes a new immutable target-mapping descriptor.
        /// </summary>
        /// <param name="targetNodeId">The portable target NodeId (<c>uav:mapToNodeId</c>), if any.</param>
        /// <param name="targetTypeNodeId">The portable target type NodeId (<c>uav:mapToType</c>), if any.</param>
        /// <param name="fieldPath">The target field path (<c>uav:mapByFieldPath</c>), if any.</param>
        public WotTargetMappingDescriptor(
            string? targetNodeId = null, string? targetTypeNodeId = null, string? fieldPath = null)
        {
            TargetNodeId = targetNodeId;
            TargetTypeNodeId = targetTypeNodeId;
            FieldPath = fieldPath;
        }

        /// <summary>
        /// An empty descriptor (no target-mapping terms authored).
        /// </summary>
        public static WotTargetMappingDescriptor Empty { get; } = new WotTargetMappingDescriptor();

        /// <summary>
        /// Gets the portable target NodeId (<c>uav:mapToNodeId</c>), if authored.
        /// </summary>
        public string? TargetNodeId { get; }

        /// <summary>
        /// Gets the portable target type NodeId (<c>uav:mapToType</c>), if authored.
        /// </summary>
        public string? TargetTypeNodeId { get; }

        /// <summary>
        /// Gets the field path into the target type (<c>uav:mapByFieldPath</c>), if
        /// authored. Only meaningful together with <see cref="TargetTypeNodeId"/>.
        /// </summary>
        public string? FieldPath { get; }

        /// <summary>
        /// Gets whether no target-mapping term was authored.
        /// </summary>
        public bool IsEmpty => TargetNodeId is null && TargetTypeNodeId is null && FieldPath is null;

        /// <summary>
        /// Parses the <c>uav:mapToNodeId</c> / <c>uav:mapToType</c> /
        /// <c>uav:mapByFieldPath</c> terms from a property-affordance JSON
        /// element (OPC 10101 §6.5.4 defines them on the affordance, not on a
        /// form). A present-but-empty string is preserved as an empty string so
        /// validation can distinguish "absent" from "authored empty".
        /// </summary>
        public static WotTargetMappingDescriptor Parse(JsonElement affordanceElement)
        {
            string? targetNodeId = ReadString(affordanceElement, "uav:mapToNodeId");
            string? targetTypeNodeId = ReadString(affordanceElement, "uav:mapToType");
            string? fieldPath = ReadString(affordanceElement, "uav:mapByFieldPath");
            if (targetNodeId is null && targetTypeNodeId is null && fieldPath is null)
            {
                return Empty;
            }
            return new WotTargetMappingDescriptor(targetNodeId, targetTypeNodeId, fieldPath);
        }

        private static string? ReadString(JsonElement element, string term)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(term, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
            return null;
        }
    }
}
