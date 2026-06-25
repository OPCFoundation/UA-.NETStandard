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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// An <see cref="ISessionManagerFactory"/> that builds a
    /// <see cref="DistributedSessionManager"/> over a shared key/value store.
    /// The session store and nonce registry are created lazily in
    /// <see cref="Create"/>, when the server's populated message context is
    /// available.
    /// </summary>
    public sealed class DistributedSessionManagerFactory : ISessionManagerFactory
    {
        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="keyValueStore">The shared key/value backend.</param>
        /// <param name="protector">
        /// Optional record protector applied to every mirrored session entry
        /// (authenticated encryption); defaults to a no-op pass-through.
        /// </param>
        /// <param name="options">The distributed session options.</param>
        public DistributedSessionManagerFactory(
            ISharedKeyValueStore keyValueStore,
            IRecordProtector? protector = null,
            DistributedSessionOptions? options = null)
        {
            m_keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
            m_protector = protector;
            m_options = options ?? new DistributedSessionOptions();
        }

        /// <inheritdoc/>
        public ISessionManager Create(
            IServerInternal server,
            ApplicationConfiguration configuration,
            TimeProvider timeProvider,
            Func<string, Certificate?> serverCertificateProvider)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            var sessionStore = new SharedKeyValueSessionStore(
                m_keyValueStore, server.MessageContext, m_protector);
            var nonceRegistry = new SharedSingleUseNonceRegistry(m_keyValueStore);

            return new DistributedSessionManager(
                server,
                configuration,
                sessionStore,
                nonceRegistry,
                serverCertificateProvider,
                m_options,
                timeProvider);
        }

        private readonly ISharedKeyValueStore m_keyValueStore;
        private readonly IRecordProtector? m_protector;
        private readonly DistributedSessionOptions m_options;
    }
}
