/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Identifies an exact synchronous or asynchronous resource for trusted cleanup verification.
    /// </summary>
    /// <remarks>
    /// This value is passed to the trusted verifier only; it is not part of cleanup discovery.
    /// The verifier must not retain the resource after its callback returns.
    /// </remarks>
    public readonly struct NodeManagerCleanupResource : IEquatable<NodeManagerCleanupResource>
    {
        /// <summary>
        /// Creates one resource representation.
        /// </summary>
        private NodeManagerCleanupResource(IDisposable? disposable, IAsyncDisposable? asyncDisposable)
        {
            m_disposable = disposable;
            m_asyncDisposable = asyncDisposable;
        }

        /// <summary>
        /// Gets whether this value contains no resource.
        /// </summary>
        internal bool IsEmpty => m_disposable is null && m_asyncDisposable is null;

        /// <summary>
        /// Creates a reference to the exact resource whose synchronous disposal failed.
        /// </summary>
        /// <param name="resource">The retained resource.</param>
        /// <returns>The resource reference for trusted verification.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resource"/> is null.</exception>
        public static NodeManagerCleanupResource FromDisposable(IDisposable resource)
        {
            return new NodeManagerCleanupResource(
                resource ?? throw new ArgumentNullException(nameof(resource)), null);
        }

        /// <summary>
        /// Creates a reference to the exact resource whose asynchronous disposal failed.
        /// </summary>
        /// <param name="resource">The retained resource.</param>
        /// <returns>The resource reference for trusted verification.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resource"/> is null.</exception>
        public static NodeManagerCleanupResource FromAsyncDisposable(IAsyncDisposable resource)
        {
            return new NodeManagerCleanupResource(
                null, resource ?? throw new ArgumentNullException(nameof(resource)));
        }

        /// <summary>
        /// Gets the exact resource when synchronous disposal was attempted.
        /// </summary>
        /// <param name="resource">The retained resource when present.</param>
        /// <returns>Whether this value represents a synchronous resource.</returns>
        public bool TryGetDisposable([NotNullWhen(true)] out IDisposable? resource)
        {
            resource = m_disposable;
            return resource is not null;
        }

        /// <summary>
        /// Gets the exact resource when asynchronous disposal was attempted.
        /// </summary>
        /// <param name="resource">The retained resource when present.</param>
        /// <returns>Whether this value represents an asynchronous resource.</returns>
        public bool TryGetAsyncDisposable([NotNullWhen(true)] out IAsyncDisposable? resource)
        {
            resource = m_asyncDisposable;
            return resource is not null;
        }

        /// <summary>
        /// Checks whether both values identify the same resource and disposal contract.
        /// </summary>
        public bool Equals(NodeManagerCleanupResource other)
        {
            return ReferenceEquals(m_disposable, other.m_disposable) &&
                ReferenceEquals(m_asyncDisposable, other.m_asyncDisposable);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is NodeManagerCleanupResource other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                m_disposable is null ? 0 : RuntimeHelpers.GetHashCode(m_disposable),
                m_asyncDisposable is null ? 0 : RuntimeHelpers.GetHashCode(m_asyncDisposable));
        }

        /// <summary>
        /// Checks whether both values identify the same resource and disposal contract.
        /// </summary>
        public static bool operator ==(NodeManagerCleanupResource left, NodeManagerCleanupResource right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Checks whether the values identify different resources or disposal contracts.
        /// </summary>
        public static bool operator !=(NodeManagerCleanupResource left, NodeManagerCleanupResource right)
        {
            return !left.Equals(right);
        }

        private readonly IDisposable? m_disposable;
        private readonly IAsyncDisposable? m_asyncDisposable;
    }
}
