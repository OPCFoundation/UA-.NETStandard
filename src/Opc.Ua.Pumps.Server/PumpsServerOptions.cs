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
        /// Gets or sets extra namespace URIs the node manager registers, for a
        /// subclass that composes further models into the same address space.
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
