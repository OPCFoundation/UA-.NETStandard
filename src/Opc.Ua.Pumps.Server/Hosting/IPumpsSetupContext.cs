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

using System.Threading;
using Opc.Ua.Di.Server.Hosting;

namespace Opc.Ua.Pumps.Server.Hosting
{
    /// <summary>
    /// Context handed to every <c>ConfigurePumps</c> delegate once the
    /// OPC 40223 address space is initialised.
    /// </summary>
    /// <remarks>
    /// A thin typed view over <see cref="IDiPostSetupContext"/>: the Pumps
    /// node manager runs in the Device Integration post-setup pipeline like
    /// every other DI-aware manager, and this only saves the caller the cast.
    /// The full DI context is still reachable through
    /// <see cref="DiContext"/>.
    /// </remarks>
    public interface IPumpsSetupContext
    {
        /// <summary>
        /// Gets the initialised node manager. Its predefined nodes are
        /// populated and its type tree resolved, so a configurator can create
        /// pumps and they are visible immediately.
        /// </summary>
        PumpsNodeManager Manager { get; }

        /// <summary>
        /// Gets the underlying Device Integration post-setup context.
        /// </summary>
        IDiPostSetupContext DiContext { get; }

        /// <summary>
        /// Gets the hosting cancellation token. Honour it: the hosted service
        /// uses it to cancel startup on shutdown.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Resolves a required application service.
        /// </summary>
        /// <typeparam name="T">The service contract.</typeparam>
        T GetRequiredService<T>() where T : notnull;
    }
}
