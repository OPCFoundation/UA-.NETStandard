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
using Opc.Ua.Robotics.Client.Intent;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// A per-call resolution context. It resolves the controller selector once
    /// and reads the controller info exactly once, then resolves every scoped
    /// name reference in the request against that single snapshot. Resolution
    /// is read-only: it never submits work and never requests or releases
    /// command authority.
    /// </summary>
    internal sealed class RoboticsResolutionContext
    {
        private RoboticsResolutionContext(
            RobotIntentControllerClient client,
            RobotIntentControllerInfo info)
        {
            Client = client;
            Info = info;
            Scope = new RoboticsScopeResolver(info.Lookups);
        }

        /// <summary>
        /// Gets the resolved controller client.
        /// </summary>
        public RobotIntentControllerClient Client { get; }

        /// <summary>
        /// Gets the controller info snapshot, read exactly once per call.
        /// </summary>
        public RobotIntentControllerInfo Info { get; }

        /// <summary>
        /// Gets the scoped name resolver over the snapshot lookups.
        /// </summary>
        public RoboticsScopeResolver Scope { get; }

        /// <summary>
        /// Resolves the controller selector and reads its info snapshot once.
        /// </summary>
        public static async ValueTask<RoboticsResolutionContext> CreateAsync(
            RoboticsIntentManager manager,
            string controller,
            string? sessionName,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(manager);

            RobotIntentControllerClient client = await manager
                .ResolveControllerAsync(controller, sessionName, ct)
                .ConfigureAwait(false);
            return await CreateAsync(client, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the info snapshot of an already resolved controller once.
        /// </summary>
        public static async ValueTask<RoboticsResolutionContext> CreateAsync(
            RobotIntentControllerClient client,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(client);

            RobotIntentControllerInfo info = await client.ReadAsync(ct).ConfigureAwait(false);
            return new RoboticsResolutionContext(client, info);
        }
    }
}
