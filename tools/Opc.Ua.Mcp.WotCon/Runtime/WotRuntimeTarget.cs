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
using System.Globalization;
using Opc.Ua.Client;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// The exact transport operation and resource passed to the shared network policy.
    /// </summary>
    public sealed record WotRuntimeTarget
    {
        /// <summary>
        /// Creates a validated target from one already compiled form.
        /// </summary>
        private WotRuntimeTarget(Uri endpoint, string operation, string resource)
        {
            Endpoint = endpoint;
            Operation = operation;
            Resource = resource;
            CredentialResource = "wot:" + endpoint.GetLeftPart(UriPartial.Authority) + "#" + resource;
            WotRuntimeJson.Text(CredentialResource, 2048);
        }

        /// <summary>
        /// Gets the exact endpoint, retaining its original DNS name for TLS.
        /// </summary>
        public Uri Endpoint { get; }

        /// <summary>
        /// Gets the exact network-rule operation, such as GET, publish or ReadHoldingRegisters.
        /// </summary>
        public string Operation { get; }

        /// <summary>
        /// Gets the exact HTTP path/query, MQTT topic, Modbus unit/function/range or OPC UA node selector.
        /// </summary>
        public string Resource { get; }

        /// <summary>
        /// Gets the stable resource binding used by host secret aliases and issued references.
        /// Operation capabilities remain independently required by the secret gateway.
        /// </summary>
        public string CredentialResource { get; }

        /// <summary>
        /// Derives policy selectors without activating an executor or resolving DNS.
        /// OPC UA identifiers are normalized against the borrowed, generation-bound session.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static WotRuntimeTarget FromForm(WotCompiledForm form, ISession? session = null)
        {
            ArgumentNullException.ThrowIfNull(form);
            _ = Capability(form.OpToken);
            string address = form.Binding.Id == "w3c.http" ? form.Addressing.Target : form.Endpoint.BaseUri;
            WotRuntimeJson.Text(address, 2048);
            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? endpoint) ||
                !string.IsNullOrEmpty(endpoint.UserInfo) ||
                !string.IsNullOrEmpty(endpoint.Fragment) ||
                string.IsNullOrEmpty(endpoint.IdnHost))
            {
                throw new ArgumentException("The compiled form needs a credential-free absolute endpoint.");
            }
            string method = form.OperationInfo.Method;
            string resource;
            switch (form.Binding.Id)
            {
                case "w3c.http" when endpoint.Scheme is "http" or "https":
                    if (form.Operation is WoTBindingCapabilityEnum.ReadProperty or
                        WoTBindingCapabilityEnum.ObserveProperty or WoTBindingCapabilityEnum.SubscribeEvent &&
                        method != "GET")
                    {
                        throw Unsupported("The existing HTTP read/poll executor supports GET forms only.");
                    }
                    if (form.Operation == WoTBindingCapabilityEnum.WriteProperty && method is "GET" or "HEAD")
                    {
                        throw Unsupported("The selected HTTP method cannot transmit a property-write payload.");
                    }
                    resource = endpoint.PathAndQuery;
                    break;
                case "w3c.mqtt" when endpoint.Scheme is "mqtt" or "mqtts":
                    resource = form.Addressing.Target;
                    WotRuntimeJson.Text(resource, 2048);
                    if (resource.Contains('#', StringComparison.Ordinal) ||
                        resource.Contains('+', StringComparison.Ordinal))
                    {
                        throw new ArgumentException("Live MQTT topics must be exact, without wildcards.");
                    }
                    string expected = form.Operation is WoTBindingCapabilityEnum.WriteProperty or
                        WoTBindingCapabilityEnum.InvokeAction ? "publish" : "subscribe";
                    if (!string.Equals(method, expected, StringComparison.Ordinal))
                    {
                        throw Unsupported("The authored MQTT packet does not match the executor operation.");
                    }
                    break;
                case "w3c.modbus" when endpoint.Scheme is "modbus" or "modbus+tcp":
                    int unit = Number(form, "unitId", 0, 255);
                    int start = Number(form, "address", 0, ushort.MaxValue);
                    int count = Number(form, "quantity", 1, 2000);
                    if (start + count - 1 > ushort.MaxValue)
                    {
                        throw new ArgumentException("The Modbus range exceeds the protocol address space.");
                    }
                    resource = string.Create(CultureInfo.InvariantCulture,
                        $"unit={unit};function={method};address={start};quantity={count}");
                    break;
                case "opc.opcua" when endpoint.Scheme is "opc.tcp" or "opc.https" or "opc.wss":
                    ArgumentNullException.ThrowIfNull(session);
                    resource = Node(session, form.Addressing.Target);
                    if (form.Operation == WoTBindingCapabilityEnum.InvokeAction)
                    {
                        if (!form.Addressing.Metadata.TryGetValue("callObjectId", out string? owner))
                        {
                            throw new ArgumentException("An OPC UA action requires a captured callObjectId receiver.");
                        }
                        resource = "object=" + Node(session, owner) + ";method=" + resource;
                    }
                    break;
                default:
                    throw Unsupported("This protocol is planning-only; no live executor is enabled.");
            }
            WotRuntimeJson.Text(method, 128);
            WotRuntimeJson.Text(resource, 2048);
            return new WotRuntimeTarget(endpoint, method, resource);
        }

        /// <summary>
        /// Maps the five approved operations to their separately granted mutation capabilities.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static McpCapability Capability(string operation)
        {
            return operation switch
            {
                "readproperty" or "observeproperty" or "subscribeevent" => McpCapability.WotRuntime,
                "writeproperty" => McpCapability.WotWrite,
                "invokeaction" => McpCapability.WotInvoke,
                _ => throw new ArgumentException("Select a supported read, write, invoke, observe or event operation.")
            };
        }

        /// <summary>
        /// Makes transport defaults explicit for schemes the general URI parser does not register.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static int EffectivePort(Uri endpoint)
        {
            return endpoint.Port > 0 ? endpoint.Port : endpoint.Scheme switch
            {
                "http" => 80,
                "https" or "opc.https" or "opc.wss" => 443,
                "mqtt" => 1883,
                "mqtts" => 8883,
                "modbus" or "modbus+tcp" => 502,
                "opc.tcp" => 4840,
                _ => throw new ArgumentException("The endpoint has no supported transport port.")
            };
        }

        /// <summary>
        /// Resolves an exact namespace-qualified resource without a browse or namespace-table mutation.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static string Node(ISession session, string text)
        {
            WotRuntimeJson.Text(text, 2048);
            var expanded = ExpandedNodeId.Parse(text);
            var node = ExpandedNodeId.ToNodeId(expanded, session.NamespaceUris);
            if (expanded.ServerIndex != 0 ||
                node.IsNull ||
                session.NamespaceUris.GetString(node.NamespaceIndex) is null)
            {
                throw new ArgumentException("The OPC UA target must identify a node in the selected session.");
            }
            return NodeId.ToExpandedNodeId(node, session.NamespaceUris).ToString();
        }

        /// <summary>
        /// Reads validated integral addressing metadata without narrowing or choosing fallback values.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static int Number(WotCompiledForm form, string name, int minimum, int maximum)
        {
            if (!form.Addressing.Metadata.TryGetValue(name, out string? text) ||
                !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
                value < minimum ||
                value > maximum)
            {
                throw new ArgumentException("The compiled Modbus addressing is invalid.");
            }
            return value;
        }

        /// <summary>
        /// Reports an unsupported executor path without including document content.
        /// </summary>
        private static ServiceResultException Unsupported(string message)
        {
            return new ServiceResultException(StatusCodes.BadNotSupported, message);
        }
    }
}
