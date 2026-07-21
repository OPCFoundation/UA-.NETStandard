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
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>
    /// Maps WoT <c>op</c> tokens to and from the generated
    /// <see cref="WoTBindingCapabilityEnum"/> operations.
    /// </summary>
    public static class WotOperations
    {
        /// <summary>Maps an <c>op</c> token to a capability operation.</summary>
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

        /// <summary>Gets whether the <c>op</c> token is compatible with the affordance kind.</summary>
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
        /// <summary>Initializes a new immutable endpoint descriptor.</summary>
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

        /// <summary>Gets the endpoint URI scheme (for example <c>http</c>, <c>mqtt</c>).</summary>
        public string Scheme { get; }

        /// <summary>Gets the endpoint host, if applicable.</summary>
        public string? Host { get; }

        /// <summary>Gets the endpoint port, or <c>-1</c> when not applicable.</summary>
        public int Port { get; }

        /// <summary>Gets the canonical endpoint / base URI.</summary>
        public string BaseUri { get; }

        /// <summary>Gets binding-specific endpoint metadata.</summary>
        public ImmutableDictionary<string, string> Metadata { get; }
    }

    /// <summary>Immutable, transport-neutral addressing metadata compiled from a form.</summary>
    public sealed class WotAddressingDescriptor
    {
        /// <summary>Initializes a new immutable addressing descriptor.</summary>
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

        /// <summary>Gets binding-specific addressing metadata.</summary>
        public ImmutableDictionary<string, string> Metadata { get; }
    }

    /// <summary>Immutable operation metadata compiled from a form.</summary>
    public sealed class WotOperationDescriptor
    {
        /// <summary>Initializes a new immutable operation descriptor.</summary>
        public WotOperationDescriptor(
            WoTBindingCapabilityEnum operation,
            string opToken,
            string method,
            ImmutableDictionary<string, string>? metadata = null)
        {
            Operation = operation;
            OpToken = opToken ?? string.Empty;
            Method = method ?? string.Empty;
            Metadata = metadata ?? ImmutableDictionary<string, string>.Empty;
        }

        /// <summary>Gets the resolved capability operation.</summary>
        public WoTBindingCapabilityEnum Operation { get; }

        /// <summary>Gets the originating WoT <c>op</c> token.</summary>
        public string OpToken { get; }

        /// <summary>
        /// Gets the concrete protocol method: an HTTP verb, a Modbus function
        /// code mnemonic, an MQTT publish / subscribe verb or an OPC UA service.
        /// </summary>
        public string Method { get; }

        /// <summary>Gets binding-specific operation metadata.</summary>
        public ImmutableDictionary<string, string> Metadata { get; }
    }

    /// <summary>Immutable payload metadata compiled from a form.</summary>
    public sealed class WotPayloadDescriptor
    {
        /// <summary>Initializes a new immutable payload descriptor.</summary>
        public WotPayloadDescriptor(
            string contentType,
            string codecId,
            ImmutableDictionary<string, string>? metadata = null)
        {
            ContentType = contentType ?? string.Empty;
            CodecId = codecId ?? string.Empty;
            Metadata = metadata ?? ImmutableDictionary<string, string>.Empty;
        }

        /// <summary>Gets the resolved content type.</summary>
        public string ContentType { get; }

        /// <summary>Gets the id of the selected payload codec.</summary>
        public string CodecId { get; }

        /// <summary>Gets binding-specific payload metadata (for example numeric type / byte order).</summary>
        public ImmutableDictionary<string, string> Metadata { get; }
    }
}
