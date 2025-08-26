/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Defines numerous re-useable utility functions for clients.
    /// </summary>
    public static partial class CoreClientUtils
    {
        /// <summary>
        /// Discovers the servers on the local machine.
        /// </summary>
        [Obsolete("Use DiscoverServersAsync instead.")]
        public static IList<string> DiscoverServers(
            ApplicationConfiguration configuration)
        {
            return DiscoverServersAsync(
                configuration,
                DefaultDiscoverTimeout).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Discovers the servers on the local machine.
        /// </summary>
        [Obsolete("Use DiscoverServersAsync instead.")]
        public static IList<string> DiscoverServers(
            ApplicationConfiguration configuration,
            int discoverTimeout)
        {
            return DiscoverServersAsync(
                configuration,
                discoverTimeout).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        [Obsolete("Use SelectEndpointAsync instead.")]
        public static EndpointDescription SelectEndpoint(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            bool useSecurity)
        {
            return SelectEndpointAsync(
                application,
                connection,
                useSecurity,
                DefaultDiscoverTimeout).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        [Obsolete("Use SelectEndpointAsync instead.")]
        public static EndpointDescription SelectEndpoint(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            bool useSecurity,
            int discoverTimeout)
        {
            return SelectEndpointAsync(
                application,
                connection,
                useSecurity,
                discoverTimeout).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        [Obsolete("Use SelectEndpointAsync instead.")]
        public static EndpointDescription SelectEndpoint(
            ApplicationConfiguration application,
            string discoveryUrl,
            bool useSecurity)
        {
            return SelectEndpointAsync(
                application,
                discoveryUrl,
                useSecurity,
                DefaultDiscoverTimeout).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        [Obsolete("Use SelectEndpointAsync instead.")]
        public static EndpointDescription SelectEndpoint(
            ApplicationConfiguration application,
            string discoveryUrl,
            bool useSecurity,
            int discoverTimeout)
        {
            return SelectEndpointAsync(
                application,
                discoveryUrl,
                useSecurity,
                discoverTimeout).AsTask().GetAwaiter().GetResult();
        }
    }
}
