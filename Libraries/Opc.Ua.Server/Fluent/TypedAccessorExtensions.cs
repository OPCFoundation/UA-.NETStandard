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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Entry-point extensions that pivot a typed
    /// <see cref="INodeBuilder{TState}"/> into a
    /// <see cref="IComponentAccessor{TState}"/> or
    /// <see cref="IPropertyAccessor{TState}"/> view. Generator-emitted
    /// per-type accessor methods hang off the accessor interfaces so
    /// authors get IntelliSense scoped to the children that actually
    /// exist on the owning type.
    /// </summary>
    public static class TypedAccessorExtensions
    {
        /// <summary>
        /// Returns a <see cref="IComponentAccessor{TState}"/> view of
        /// <paramref name="builder"/> so generator-emitted accessor
        /// extension methods can walk the HasComponent children.
        /// </summary>
        /// <typeparam name="TState">Concrete owning state type.</typeparam>
        public static IComponentAccessor<TState> Components<TState>(
            this INodeBuilder<TState> builder)
            where TState : NodeState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return new ComponentAccessor<TState>(builder);
        }

        /// <summary>
        /// Returns a <see cref="IPropertyAccessor{TState}"/> view of
        /// <paramref name="builder"/> so generator-emitted accessor
        /// extension methods can walk the HasProperty children.
        /// </summary>
        /// <typeparam name="TState">Concrete owning state type.</typeparam>
        public static IPropertyAccessor<TState> Properties<TState>(
            this INodeBuilder<TState> builder)
            where TState : NodeState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return new PropertyAccessor<TState>(builder);
        }

        private sealed class ComponentAccessor<TState> : IComponentAccessor<TState>
            where TState : NodeState
        {
            public ComponentAccessor(INodeBuilder<TState> builder) { Builder = builder; }
            public INodeBuilder<TState> Builder { get; }
        }

        private sealed class PropertyAccessor<TState> : IPropertyAccessor<TState>
            where TState : NodeState
        {
            public PropertyAccessor(INodeBuilder<TState> builder) { Builder = builder; }
            public INodeBuilder<TState> Builder { get; }
        }
    }
}
