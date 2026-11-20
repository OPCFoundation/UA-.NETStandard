/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Options for the OPC 40223 node manager.
    /// </summary>
    public sealed class PumpsServerOptions
    {
        /// <summary>
        /// Gets or sets whether every pump is also organized into the
        /// OPC 40001-1 <c>Machines</c> folder. Defaults to
        /// <see langword="true"/>.
        /// </summary>
        /// <remarks>
        /// A pump is a machine in the OPC 40001-1 sense, so it is expected to
        /// be discoverable from the <c>Machines</c> folder as well as from the
        /// Device Integration <c>DeviceSet</c>. Turning this off leaves it reachable from the
        /// <c>DeviceSet</c> alone, which a Machinery client will not find.
        /// </remarks>
        public bool OrganizeIntoMachinesFolder { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the OPC 10000-200 Industrial Automation model
        /// is loaded alongside Machinery. Defaults to <see langword="true"/>.
        /// </summary>
        /// <remarks>
        /// OPC 40001-1 types <c>MonitoringType/Status/Stacklight</c> with the
        /// IA <c>BasicStacklightType</c>. Without IA in the address space that
        /// type definition does not resolve and the Machinery model fails to
        /// load, so there is rarely a reason to turn this off.
        /// </remarks>
        public bool LoadIndustrialAutomationModel { get; set; } = true;

        /// <summary>
        /// Gets or sets extra model or vendor-extension namespace URIs the owner registers.
        /// For composed hosting, configure the instance namespace on the Machinery owner instead.
        /// </summary>
        public ArrayOf<string> AdditionalNamespaceUris { get; set; }

        internal PumpsServerOptions Validate()
        {
            foreach (string uri in AdditionalNamespaceUris)
            {
                if (string.IsNullOrEmpty(uri))
                {
                    throw new ArgumentException(
                        "An additional namespace URI must not be null or empty.",
                        nameof(AdditionalNamespaceUris));
                }
            }
            return this;
        }
    }
}
