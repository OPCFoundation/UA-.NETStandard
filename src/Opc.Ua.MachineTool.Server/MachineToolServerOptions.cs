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
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Selects independently hosted MachineTool and wireless peripheral models.
    /// </summary>
    public sealed class MachineToolServerOptions
    {
        /// <summary>
        /// Gets or sets the model selection; MachineTool alone is the default.
        /// </summary>
        public MachineToolParts Parts { get; set; } = MachineToolParts.MachineTool;

        /// <summary>
        /// Gets or sets the standalone owner's application namespace.
        /// Shared-owner composition uses the existing owner's namespace instead.
        /// </summary>
        public string InstanceNamespaceUri { get; set; } = "urn:opcua-netstandard:machinetool:instances";

        /// <summary>
        /// Gets the Machinery parts required by the selected model closure.
        /// </summary>
        internal MachineryParts RequiredMachineryParts => MachineryParts.BuildingBlocks |
            ((Parts & MachineToolParts.MachineTool) != 0 ? MachineryParts.Jobs : MachineryParts.None) |
            ((Parts & MachineToolParts.Wmtp) != 0 ? MachineryParts.ProcessValues : MachineryParts.None);

        /// <summary>
        /// Rejects empty or unknown model selections and invalid application namespaces.
        /// </summary>
        public void Validate()
        {
            if (Parts == MachineToolParts.None || (Parts & ~MachineToolParts.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Parts));
            }
            if (!Uri.TryCreate(InstanceNamespaceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute application namespace URI is required.",
                    nameof(InstanceNamespaceUri));
            }
            foreach (string model in MachineToolModels.GetNamespaceUris(Parts))
            {
                if (string.Equals(model, InstanceNamespaceUri, StringComparison.Ordinal))
                {
                    throw new ArgumentException("The application namespace must not be a model namespace.",
                        nameof(InstanceNamespaceUri));
                }
            }
            if (string.Equals(InstanceNamespaceUri, Opc.Ua.Namespaces.OpcUa, StringComparison.Ordinal))
            {
                throw new ArgumentException("The application namespace must not be the OPC UA namespace.",
                    nameof(InstanceNamespaceUri));
            }
        }
    }
}
