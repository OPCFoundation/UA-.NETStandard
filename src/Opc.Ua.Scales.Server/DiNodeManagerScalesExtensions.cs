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

namespace Opc.Ua.Scales.Server
{
    /// <summary>
    /// Direct Scales composition on an existing DI/Machinery owner.
    /// </summary>
    public static class DiNodeManagerScalesExtensions
    {
        /// <summary>
        /// Gets or creates the owner's shared Scales runtime after its models have loaded.
        /// </summary>
        /// <remarks>
        /// The first call selects the runtime options. The stock Machinery manager releases the
        /// runtime automatically; a custom DI manager calls
        /// <see cref="DiNodeManagerMachineryExtensions.DisposeMachineryResourcesAsync"/> on shutdown.
        /// The runtime contributes capabilities to the owner without another manager or namespace.
        /// </remarks>
        /// <param name="manager">The owner that loaded DI, IA, Machinery, PackML and Scales.</param>
        /// <param name="options">Runtime options, or defaults for a new runtime.</param>
        public static ScalesRuntime CreateScalesRuntime(
            this DiNodeManager manager,
            ScalesServerOptions? options = null)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            options?.Validate();
            NodeId scaleType = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.ScaleDeviceType, manager.SystemContext.NamespaceUris);
            if (manager.FindPredefinedNode(scaleType) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Load the Scales model on the owner with AddScalesToMachinery before creating scales.");
            }
            if (manager is ScalesNodeManager standalone)
            {
                return standalone.Runtime;
            }
            ScalesRuntime runtime = s_runtimes.GetValue(
                manager, owner => new Lazy<ScalesRuntime>(
                    () => new ScalesRuntime(owner, options ?? new ScalesServerOptions()))).Value;
            runtime.RegisterLifetime();
            return runtime;
        }

        /// <summary>The single runtime associated with each composed owner.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, Lazy<ScalesRuntime>> s_runtimes = new();
    }
}
