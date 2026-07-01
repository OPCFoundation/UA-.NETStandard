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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Routes the load-direction <b>eligibility</b> keyspaces (health <c>ServiceLevel</c> and the endpoint directory)
    /// to the linearizable store when <see cref="LoadDirectionOptions.StrongEligibility"/> is set; the high-churn load
    /// keyspace is deliberately left eventual.
    /// </summary>
    internal sealed class LoadDirectionStrongKeyspaceProvider : IStrongKeyspaceProvider
    {
        public LoadDirectionStrongKeyspaceProvider(LoadDirectionOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public ArrayOf<string> GetStrongKeyPrefixes()
        {
            return [m_options.ServiceLevelKeyPrefix, m_options.EndpointKeyPrefix];
        }

        private readonly LoadDirectionOptions m_options;
    }
}
