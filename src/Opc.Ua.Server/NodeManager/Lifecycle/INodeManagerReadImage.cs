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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Immutable application state retained with the exact NodeManager routing image of a request.
    /// Implementations must remain readable after their owner is retired.
    /// </summary>
    public interface INodeManagerReadImage
    {
        /// <summary>
        /// Gets the exact NodeManager that owns this state.
        /// </summary>
        IAsyncNodeManager Owner { get; }
    }

    /// <summary>
    /// Provides application state from the request's captured routing image, or the live image outside a request.
    /// </summary>
    public interface INodeManagerReadImageSource
    {
        /// <summary>
        /// Gets the image for an exact owner, or null when no state has been published for that owner.
        /// </summary>
        /// <param name="owner">The exact NodeManager that owns the state.</param>
        /// <returns>The image for the owner, or null if none has been published.</returns>
        INodeManagerReadImage? GetReadImage(IAsyncNodeManager owner);
    }
}
