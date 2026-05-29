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

using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// <para>
    /// Encodeable activator base for types that opt into pooling via
    /// <see cref="IPooledEncodeable"/>. Maintains a per-activator,
    /// process-global bounded pool of instances. <see cref="CreateInstance"/>
    /// rents from the pool (or constructs a new instance on miss) and
    /// then calls <see cref="InitializeRent"/> so the derived activator
    /// can clear any per-rent state on the instance (typically a pooled
    /// sentinel field used by the type's <see cref="IPooledEncodeable.Reuse"/>
    /// implementation).
    /// </para>
    /// <para>
    /// <see cref="Return"/> is called by the type's
    /// <see cref="IPooledEncodeable.Reuse"/> body after the instance has
    /// reset its fields. The pool may drop the instance to the GC when
    /// the bound is exceeded.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The pooled encodeable type.</typeparam>
    public abstract class PooledEncodeableType<T> : EncodeableType<T>
        where T : class, IPooledEncodeable, new()
    {
        /// <summary>
        /// Default upper bound on pooled instances per activator.
        /// </summary>
        public const int DefaultMaximumRetained = 1024;

        private readonly BoundedObjectPool<T> m_pool;

        /// <summary>
        /// Construct a pooled activator with the default
        /// <see cref="DefaultMaximumRetained"/> bound.
        /// </summary>
        protected PooledEncodeableType()
            : this(DefaultMaximumRetained)
        {
        }

        /// <summary>
        /// Construct a pooled activator with the given upper bound on
        /// retained instances.
        /// </summary>
        /// <param name="maximumRetained">Upper bound on instances held
        /// by the pool. Must be positive.</param>
        protected PooledEncodeableType(int maximumRetained)
        {
            m_pool = new BoundedObjectPool<T>(maximumRetained, () => new T());
        }

        /// <inheritdoc/>
        public abstract override XmlQualifiedName XmlName { get; }

        /// <summary>
        /// Maximum number of instances this activator's pool retains.
        /// </summary>
        public int MaximumRetained => m_pool.MaximumRetained;

        /// <summary>
        /// Rent an instance of <typeparamref name="T"/> from the pool,
        /// or construct a fresh one on a pool miss. After rent the
        /// <see cref="InitializeRent"/> hook is invoked so the derived
        /// activator can perform any per-rent reset (e.g. clearing the
        /// instance's pooled sentinel back to the live state).
        /// </summary>
        public sealed override IEncodeable CreateInstance()
        {
            T instance = m_pool.Get();
            InitializeRent(instance);
            return instance;
        }

        /// <summary>
        /// Return <paramref name="instance"/> to the activator's pool
        /// for reuse. Intended to be called from the type's
        /// <see cref="IPooledEncodeable.Reuse"/> implementation after
        /// the instance has reset its fields. Not intended for direct
        /// caller use.
        /// </summary>
        public void Return(T instance)
        {
            m_pool.Return(instance);
        }

        /// <summary>
        /// Hook invoked on every rent, immediately after the instance
        /// is obtained from the pool (or newly constructed) and before
        /// it is returned to the caller of <see cref="CreateInstance"/>.
        /// Derived activators override this to clear the instance's
        /// pooled sentinel field. The default implementation is a no-op
        /// for types whose <see cref="IPooledEncodeable.Reuse"/>
        /// implementation does not require a separate per-rent reset.
        /// </summary>
        /// <param name="instance">The freshly rented instance.</param>
        protected virtual void InitializeRent(T instance)
        {
        }
    }
}
