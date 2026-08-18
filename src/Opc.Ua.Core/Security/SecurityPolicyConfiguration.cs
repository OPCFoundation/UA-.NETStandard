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

namespace Opc.Ua
{
    /// <summary>
    /// Carries a pending security policy registration through the container.
    /// </summary>
    public sealed class SecurityPolicyConfiguration
    {
        /// <summary>
        /// Initializes the pending registration.
        /// </summary>
        /// <param name="securityPolicy">The security policy to register.</param>
        /// <param name="replaceExisting">Whether to deliberately replace an existing policy with the same name or URI.</param>
        public SecurityPolicyConfiguration(SecurityPolicyInfo securityPolicy, bool replaceExisting)
        {
            SecurityPolicy = securityPolicy ?? throw new ArgumentNullException(nameof(securityPolicy));
            ReplaceExisting = replaceExisting;
        }

        /// <summary>
        /// The security policy to register.
        /// </summary>
        public SecurityPolicyInfo SecurityPolicy { get; }

        /// <summary>
        /// Whether an existing policy with the same name or URI is deliberately replaced.
        /// </summary>
        public bool ReplaceExisting { get; }

        /// <summary>
        /// Applies the registration to a registry.
        /// </summary>
        /// <param name="registry">The registry to configure.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Apply(ISecurityPolicyRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(SecurityPolicy, ReplaceExisting);
        }
    }
}
