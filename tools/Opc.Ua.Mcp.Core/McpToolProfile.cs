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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Selects a bounded catalog of OPC UA MCP tools.
    /// </summary>
    public enum McpToolProfile
    {
        /// <summary>
        /// Common connection, browse, read, write, call, and configuration workflows.
        /// </summary>
        Core,

        /// <summary>
        /// OPC UA Part 4 service-set and convenience tools.
        /// </summary>
        Services,

        /// <summary>
        /// Configuration, PKI, and NodeSet administration tools.
        /// </summary>
        Administration,

        /// <summary>
        /// PubSub runtime, discovery, action, capture, and decode tools.
        /// </summary>
        PubSub,

        /// <summary>
        /// OPC UA packet capture, decode, and replay tools.
        /// </summary>
        Diagnostics,

        /// <summary>
        /// Robot Intent discovery, monitoring, control, and mission tools.
        /// </summary>
        Robotics,

        /// <summary>
        /// Vision discovery, monitoring, seeing (image content), inference,
        /// feedback, and coordinate-frame tools.
        /// </summary>
        Vision,

        /// <summary>
        /// Every available tool, preserving the current-major default catalog.
        /// </summary>
        Full = 7,

        /// <summary>
        /// Asset Management Basics discovery, monitoring and asset maintenance.
        /// </summary>
        Amb,

        /// <summary>
        /// Machinery building blocks, process values, energy, jobs and results.
        /// </summary>
        Machinery,

        /// <summary>
        /// Weighing, products, recipes and PackML operations.
        /// </summary>
        Scales,

        /// <summary>
        /// Pump discovery, measurements, supervision and maintenance.
        /// </summary>
        Pumps,

        /// <summary>
        /// Device Integration topology, locking, transfers and software updates.
        /// </summary>
        Di,

        /// <summary>
        /// ISA-95 common objects and V1/V2 job control.
        /// </summary>
        Isa95,

        /// <summary>
        /// Relative spatial locations, global positioning and coordinate transforms.
        /// </summary>
        Positioning,

        /// <summary>
        /// WoT Connectivity assets, registry materialization and lifecycle events.
        /// </summary>
        Wot,

        /// <summary>
        /// Generic xRegistry groups, resources, versions and document transfers.
        /// </summary>
        XRegistry,

        /// <summary>
        /// WoT document inspection, bounded dependency resolution, planning and conversion.
        /// </summary>
        WotDocuments,

        /// <summary>
        /// GDS application directory discovery and administration.
        /// </summary>
        Gds,

        /// <summary>
        /// GDS pull and remote push certificate lifecycle.
        /// </summary>
        Certificates,

        /// <summary>
        /// Secret-safe credential and authorization-token issuance.
        /// </summary>
        Credentials,

        /// <summary>
        /// Part 21 device registrar and onboarding ticket administration.
        /// </summary>
        Onboarding,

        /// <summary>
        /// Generic role mappings and node permission administration.
        /// </summary>
        Roles,

        /// <summary>
        /// Generic user and password-policy administration.
        /// </summary>
        Users,

        /// <summary>
        /// Generic file-system and temporary file-transfer workflows.
        /// </summary>
        Files,

        /// <summary>
        /// Bounded alarm and condition observations and explicit controls.
        /// </summary>
        Alarms,

        /// <summary>
        /// Historical values, aggregates, events and annotations.
        /// </summary>
        History,

        /// <summary>
        /// Runtime type discovery and bounded schema generation.
        /// </summary>
        Schemas,

        /// <summary>
        /// Named, policy-controlled PubSub transport runtimes.
        /// </summary>
        PubSubRuntime,

        /// <summary>
        /// Standard remote PubSub address-space configuration.
        /// </summary>
        PubSubConfiguration,

        /// <summary>
        /// Explicit PubSub client adapters and declarative bridges.
        /// </summary>
        PubSubBridge,

        /// <summary>
        /// Secret-safe PubSub security-group and key-service workflows.
        /// </summary>
        PubSubSecurity,

        /// <summary>
        /// Leased, policy-controlled execution of supported WoT protocol forms.
        /// </summary>
        WotRuntime,

        /// <summary>
        /// MachineTool and Wireless Machine Tool Peripheral discovery, monitoring and configured workflows.
        /// </summary>
        MachineTool = 33,

        /// <summary>
        /// Geometric measurement system equipment, production monitoring and result workflows.
        /// </summary>
        Gms = 34,

        /// <summary>
        /// Crane and hoist monitoring and explicitly configured limit requests.
        /// </summary>
        CranesHoists = 35
    }
}
