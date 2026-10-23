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
    /// Provides live published application state. Use the context-aware extension to select a request's view.
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

    /// <summary>
    /// Selects immutable application state through an explicit request view.
    /// </summary>
    public static class NodeManagerReadImageExtensions
    {
        /// <summary>
        /// Gets the request's published application state, or live state for work without a request pin.
        /// </summary>
        /// <param name="source">The provider of live application state.</param>
        /// <param name="context">The operation selecting the view, or null for live work.</param>
        /// <param name="owner">The exact NodeManager that owns the state.</param>
        /// <returns>The selected image, or null if that view has no image for the owner.</returns>
        public static INodeManagerReadImage? GetReadImage(
            this INodeManagerReadImageSource source,
            OperationContext? context,
            IAsyncNodeManager owner)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (owner is null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (context?.RoutingSnapshot is not { } snapshot)
            {
                return source.GetReadImage(owner);
            }
            return snapshot.ReadImages.TryGetValue(owner, out INodeManagerReadImage? image) ? image : null;
        }
    }
}
