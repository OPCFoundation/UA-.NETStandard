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

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Composes AutoID readers into an existing Device Integration owner.
    /// </summary>
    public static class DiNodeManagerAutoIdExtensions
    {
        /// <summary>
        /// Gets or creates the owner's shared AutoID runtime after its models have loaded.
        /// </summary>
        /// <remarks>
        /// The first call selects the runtime options; later calls return the same runtime. A
        /// stand-alone <see cref="AutoIdNodeManager"/> returns its own <see cref="AutoIdNodeManager.Runtime"/>.
        /// The stock <see cref="MachineryNodeManager"/> disposes a composed runtime on shutdown; a custom
        /// Device Integration owner calls
        /// <see cref="DiNodeManagerMachineryExtensions.DisposeMachineryResourcesAsync"/> from its
        /// asynchronous disposal. No additional node manager is created: the readers are registered
        /// in the owner, below its <c>DeviceSet</c>.
        /// </remarks>
        /// <param name="manager">The owner that loaded the Device Integration and AutoID models.</param>
        /// <param name="options">Runtime options, or defaults for a new runtime.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException">The owner did not load the AutoID model.</exception>
        public static AutoIdRuntime CreateAutoIdRuntime(
            this DiNodeManager manager,
            AutoIdServerOptions? options = null)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            options?.Validate();
            if (manager is AutoIdNodeManager standalone)
            {
                return standalone.Runtime;
            }
            var deviceType = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.AutoIdDeviceType,
                manager.SystemContext.NamespaceUris);
            if (deviceType.IsNull || manager.FindPredefinedNode(deviceType) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Load the AutoID model on the owner with AddAutoIdToMachinery before creating readers.");
            }
            AutoIdRuntime runtime = s_runtimes.GetValue(
                manager,
                owner => new Lazy<AutoIdRuntime>(
                    () => new AutoIdRuntime(owner, options ?? new AutoIdServerOptions()))).Value;
            runtime.RegisterLifetime();
            return runtime;
        }

        /// <summary>The runtime associated with each composed owner.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, Lazy<AutoIdRuntime>> s_runtimes = new();
    }
}
