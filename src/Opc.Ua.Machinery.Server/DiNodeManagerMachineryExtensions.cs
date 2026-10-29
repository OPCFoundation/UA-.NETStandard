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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Direct Machinery configuration helpers for custom DI node managers.
    /// </summary>
    /// <remarks>
    /// A server that already owns the Device Integration address space cannot
    /// call <c>AddMachinery</c> — that would register a second DI-owning
    /// manager. It loads the models into its own manager with
    /// <see cref="MachineryServer.AddMachineryTypeSystem"/> and drives them
    /// through the context this extension creates, or through
    /// <c>ConfigureMachineryFor&lt;TNodeManager&gt;()</c> in the hosting
    /// pipeline. The DI owner releases the machines' resources automatically.
    /// </remarks>
    public static class DiNodeManagerMachineryExtensions
    {
        /// <summary>
        /// Gives an integrated companion runtime the same lifetime as the Machinery manager.
        /// </summary>
        /// <remarks>
        /// Forwards to <see cref="DiNodeManager.RegisterResource"/>.
        /// </remarks>
        /// <param name="manager">The owning DI/Machinery node manager.</param>
        /// <param name="resource">The companion resource to release with the manager.</param>
        public static void RegisterMachineryResource(this DiNodeManager manager, IAsyncDisposable resource)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            manager.RegisterResource(resource);
        }

        /// <summary>
        /// Creates a Machinery build context after validating that the custom
        /// manager exposes the required DI and Machinery model nodes.
        /// </summary>
        /// <param name="manager">The custom DI node manager.</param>
        /// <param name="options">The Machinery options to build against.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public static IMachineryBuildContext CreateMachineryBuildContext(
            this DiNodeManager manager,
            MachineryServerOptions options,
            CancellationToken cancellationToken = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            return new MachineryBuildContext(manager, options, cancellationToken);
        }

        /// <summary>
        /// Releases the resources the machines built on
        /// <paramref name="manager"/> hold — result transfer handles and
        /// job-management change pumps.
        /// </summary>
        /// <remarks>
        /// Forwards to <see cref="DiNodeManager.DisposeResourcesAsync"/>. Every DI owner releases
        /// these resources automatically; existing explicit calls remain safe to repeat.
        /// </remarks>
        /// <param name="manager">The custom DI node manager.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="manager"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="AggregateException">
        /// One or more resources failed to release.
        /// </exception>
        public static ValueTask DisposeMachineryResourcesAsync(this DiNodeManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            return manager.DisposeResourcesAsync();
        }
    }
}
