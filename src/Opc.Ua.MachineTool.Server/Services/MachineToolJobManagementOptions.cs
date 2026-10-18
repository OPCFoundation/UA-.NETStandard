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

using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Selects complete, application-owned providers for the machine's declared Machinery Jobs AddIn.
    /// </summary>
    public sealed record MachineToolJobManagementOptions
    {
        /// <summary>
        /// Gets the explicit receiver of standard ISA-95 job-order operations.
        /// </summary>
        public IIsa95JobOrderReceiverV2? Receiver { get; init; }

        /// <summary>
        /// Gets the explicit response provider; receiver-only configurations are rejected.
        /// </summary>
        public IIsa95JobResponseProviderV2? ResponseProvider { get; init; }

        /// <summary>
        /// Gets the order catalog, or uses the receiver's or response provider's catalog when implemented.
        /// </summary>
        public IIsa95JobOrderCatalog? Catalog { get; init; }

        /// <summary>
        /// Gets whether the shared binder validates predefined Machinery job parameters.
        /// </summary>
        public bool PredefinedParameters { get; init; } = true;

        /// <summary>
        /// Gets the per-instance catalog without borrowing a different machine's container registration.
        /// </summary>
        internal IIsa95JobOrderCatalog? EffectiveCatalog =>
            Catalog ?? Receiver as IIsa95JobOrderCatalog ?? ResponseProvider as IIsa95JobOrderCatalog;

        /// <summary>
        /// Validates complete provider selection before any machine factory runs.
        /// </summary>
        internal void Validate()
        {
            if (Receiver == null || ResponseProvider == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "MachineTool job management requires both an order receiver and a response provider.");
            }
            if (EffectiveCatalog == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "MachineTool job management requires a per-instance job-order catalog.");
            }
        }
    }
}
