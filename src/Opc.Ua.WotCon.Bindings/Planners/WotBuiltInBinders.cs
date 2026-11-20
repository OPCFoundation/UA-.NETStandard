/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua.WotCon.Bindings.Planners
{
    /// <summary>
    /// A factory for the eight planner / validator binders shipped in this
    /// dependency-light assembly (HTTP, CoAP, MQTT, Modbus TCP, BACnet, PROFINET,
    /// LoRaWAN and OPC UA). Executable binders (HTTP, MQTT, Modbus, OPC UA) become
    /// executable only when their optional executor is registered alongside them;
    /// the remaining binders are always planner-only.
    /// </summary>
    public static class WotBuiltInBinders
    {
        /// <summary>
        /// Creates the full set of shipped planner binders.
        /// </summary>
        public static IReadOnlyList<IWotProtocolBinder> CreateAll()
        {
            return
            [
                new HttpBindingPlanner(),
                new CoapBindingPlanner(),
                new MqttBindingPlanner(),
                new ModbusBindingPlanner(),
                new BacnetBindingPlanner(),
                new ProfinetBindingPlanner(),
                new LoRaWanBindingPlanner(),
                new OpcUaBindingPlanner()
            ];
        }
    }
}
