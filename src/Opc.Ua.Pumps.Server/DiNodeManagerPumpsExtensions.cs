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
using System.Runtime.CompilerServices;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Direct Pumps composition on an existing DI/Machinery address-space owner.
    /// </summary>
    public static class DiNodeManagerPumpsExtensions
    {
        /// <summary>
        /// Gets or creates the owner's shared Pumps runtime after its models have loaded.
        /// </summary>
        /// <remarks>
        /// The first call selects the runtime options. Every DI owner releases the runtime
        /// automatically through <see cref="DiNodeManager.RegisterResource"/>.
        /// No additional node manager or instance namespace is created.
        /// </remarks>
        /// <param name="manager">The owner that loaded DI, IA, Machinery and Pumps.</param>
        /// <param name="options">Runtime options, or defaults for a new runtime.</param>
        public static PumpsRuntime CreatePumpsRuntime(
            this DiNodeManager manager,
            PumpsServerOptions? options = null)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            options?.Validate();
            NodeId pumpType = ExpandedNodeId.ToNodeId(ObjectTypeIds.PumpType, manager.SystemContext.NamespaceUris);
            if (manager.FindPredefinedNode(pumpType) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Load the Pumps model on the owner with AddPumpsToMachinery before creating pumps.");
            }
            if (manager is PumpsNodeManager standalone)
            {
                return standalone.Runtime;
            }
            PumpsRuntime runtime = s_runtimes.GetValue(
                manager, owner => new Lazy<PumpsRuntime>(
                    () => new PumpsRuntime(owner, options ?? new PumpsServerOptions()))).Value;
            runtime.RegisterLifetime();
            return runtime;
        }

        /// <summary>The runtime associated with each composed owner.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, Lazy<PumpsRuntime>> s_runtimes = new();
    }
}
