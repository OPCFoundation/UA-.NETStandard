/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server.Hosting
{
    /// <summary>
    /// A delegate that runs once a DI node manager's address space is
    /// fully initialised. Configurators are registered through
    /// <c>ConfigureDevicesFor&lt;TNodeManager&gt;</c> and dispatched by
    /// the <see cref="IDiPostSetupRunner"/> in registration order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each configurator declares its <see cref="TargetManagerType"/>
    /// so only matching manager subclasses receive the callback. A
    /// configurator registered with <c>TargetManagerType = typeof(DiNodeManager)</c>
    /// runs against the plain DI manager only; one registered with
    /// <c>typeof(PumpNodeManager)</c> runs only against pump managers.
    /// Subclass matching uses <see cref="Type.IsAssignableFrom(Type)"/>
    /// so a configurator for a base class also matches its derived
    /// classes.
    /// </para>
    /// </remarks>
    public interface IDiPostSetupConfigurator
    {
        /// <summary>
        /// The DI node-manager type this configurator targets. Set to
        /// <c>typeof(DiNodeManager)</c> for plain DI managers, or a
        /// subclass type (e.g. <c>typeof(PumpNodeManager)</c>) to
        /// restrict the delegate to a specific companion-spec manager.
        /// </summary>
        Type TargetManagerType { get; }

        /// <summary>
        /// Performs the post-setup work against
        /// <see cref="IDiPostSetupContext.Manager"/>. Exceptions
        /// thrown from this method abort the hosted server startup;
        /// they are not caught or swallowed.
        /// </summary>
        ValueTask RunAsync(IDiPostSetupContext context);
    }
}
