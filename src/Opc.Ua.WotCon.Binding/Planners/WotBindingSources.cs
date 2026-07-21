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

namespace Opc.Ua.WotCon.Binding.Planners
{
    /// <summary>
    /// The version-pinned specification sources every shipped planner is built
    /// against. Each source captures the exact document URL, its version / date
    /// and its standards maturity so operators can audit precisely which mapping
    /// is enforced. The W3C Binding Templates registry is a pilot and is
    /// intentionally never reported as Current; drafts expose their Editor's Draft
    /// maturity and the OPC UA binding exposes its OPC specification maturity.
    /// </summary>
    public static class WotBindingSources
    {
        /// <summary>The date this catalogue of sources was last pinned.</summary>
        public const string Retrieved = "2026-07-21";

        /// <summary>
        /// The W3C WoT Thing Description 1.1 Recommendation, which contains the
        /// normative HTTP protocol binding (<c>htv:</c> terms and default methods).
        /// </summary>
        public static WotBindingSource Http { get; } = new WotBindingSource(
            "https://www.w3.org/TR/wot-thing-description11/",
            "1.1",
            WotBindingMaturity.Recommendation,
            commit: "REC-wot-thing-description11-20231205",
            retrieved: Retrieved,
            note: "Normative HTTP mapping in TD 1.1; htv: terms per http://www.w3.org/2011/http#. " +
                "The standalone W3C WoT Binding Templates HTTP note is an Editor's Draft; the " +
                "W3C Binding Registry is a pilot and currently empty.");

        /// <summary>The W3C WoT Binding Templates CoAP Editor's Draft.</summary>
        public static WotBindingSource Coap { get; } = new WotBindingSource(
            "https://w3c.github.io/wot-binding-templates/bindings/protocols/coap/",
            "editors-draft",
            WotBindingMaturity.EditorsDraft,
            commit: "w3c/wot-binding-templates@main",
            retrieved: Retrieved,
            note: "cov: terms per the CoAP binding Editor's Draft. Planner-only in this build.");

        /// <summary>The W3C WoT Binding Templates MQTT Editor's Draft.</summary>
        public static WotBindingSource Mqtt { get; } = new WotBindingSource(
            "https://w3c.github.io/wot-binding-templates/bindings/protocols/mqtt/",
            "editors-draft",
            WotBindingMaturity.EditorsDraft,
            commit: "w3c/wot-binding-templates@main",
            retrieved: Retrieved,
            note: "mqv: terms (controlPacket, topic, qos, retain) per the MQTT binding Editor's Draft.");

        /// <summary>The W3C WoT Binding Templates Modbus Editor's Draft.</summary>
        public static WotBindingSource Modbus { get; } = new WotBindingSource(
            "https://w3c.github.io/wot-binding-templates/bindings/protocols/modbus/",
            "editors-draft",
            WotBindingMaturity.EditorsDraft,
            commit: "w3c/wot-binding-templates@main",
            retrieved: Retrieved,
            note: "modv: terms (function, entity, address, quantity, unitID, type) per " +
                "https://www.w3.org/2019/wot/modbus# and the Modbus binding Editor's Draft.");

        /// <summary>The W3C WoT Binding Templates BACnet Editor's Draft.</summary>
        public static WotBindingSource Bacnet { get; } = new WotBindingSource(
            "https://w3c.github.io/wot-binding-templates/bindings/protocols/bacnet/",
            "editors-draft",
            WotBindingMaturity.EditorsDraft,
            commit: "w3c/wot-binding-templates@main",
            retrieved: Retrieved,
            note: "bacv: terms per the BACnet binding Editor's Draft. Schema/document-level " +
                "planning only; reported as non-executable.");

        /// <summary>The W3C WoT Binding Templates PROFINET contribution (unofficial draft).</summary>
        public static WotBindingSource Profinet { get; } = new WotBindingSource(
            "https://w3c.github.io/wot-binding-templates/bindings/protocols/",
            "unofficial-draft",
            WotBindingMaturity.UnofficialDraft,
            commit: "w3c/wot-binding-templates@main",
            retrieved: Retrieved,
            note: "pnv: terms (slot, subslot, index, api) per the PROFINET contribution. Not yet a " +
                "published W3C binding; schema/document-level planning only, reported as non-executable.");

        /// <summary>The W3C WoT Binding Templates LoRaWAN contribution (unofficial draft).</summary>
        public static WotBindingSource LoRaWan { get; } = new WotBindingSource(
            "https://w3c.github.io/wot-binding-templates/bindings/protocols/",
            "unofficial-draft",
            WotBindingMaturity.UnofficialDraft,
            commit: "w3c/wot-binding-templates@main",
            retrieved: Retrieved,
            note: "lorawan: terms (DevEUI, fPort) per the LoRaWAN contribution. Not yet a published " +
                "W3C binding; schema/document-level planning only, reported as non-executable.");

        /// <summary>The OPC UA WoT Connectivity binding (OPC 10101).</summary>
        public static WotBindingSource OpcUa { get; } = new WotBindingSource(
            "https://reference.opcfoundation.org/WoT/v100/docs/",
            "OPC 10101 1.00",
            WotBindingMaturity.OpcSpecification,
            commit: "OPC-10101-1.00",
            retrieved: Retrieved,
            note: "uav: terms (id, componentOf, mapToNodeId, mapToType, mapByFieldPath, " +
                "eventFields) per the official OPC UA WoT Connectivity binding (OPC 10101).");
    }
}
