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
using Opc.Ua.Extrusion.Corrugator;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-11 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates the OPC 40084-11 corrugator, preserving its actual HaulOff inheritance.
        /// </summary>
        public ValueTask<ExtrusionDevice<Corrugator_InterfaceState>> CreateCorrugatorAsync(
            ExtrusionDeviceOptions options,
            Action<Corrugator_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "Corrugator", "Corrugator",
                static (context, parent, name) => context.CreateInstanceOfCorrugator_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
