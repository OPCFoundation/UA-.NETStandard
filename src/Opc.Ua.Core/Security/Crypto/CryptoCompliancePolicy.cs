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

namespace Opc.Ua
{
    /// <summary>
    /// How strictly the validation status of a crypto provider is enforced.
    /// </summary>
    /// <remarks>
    /// The default is <see cref="Permissive"/>, which is exactly the behaviour
    /// the stack had before the provider model existed: nothing is filtered and
    /// nothing is warned about. Making a stricter mode the default would drop
    /// endpoints that work today, so tightening is an explicit choice.
    /// </remarks>
    public enum CryptoCompliancePolicy
    {
        /// <summary>
        /// Use whatever is configured and say nothing. Existing behaviour.
        /// </summary>
        Permissive = 0,

        /// <summary>
        /// Use whatever is configured, but report every provider that carries no
        /// validation so the choice is visible in logs, metrics and the address
        /// space.
        /// </summary>
        WarnOnUncertified = 1,

        /// <summary>
        /// Refuse providers that carry no validation, and withhold the security
        /// policies that depend on them.
        /// </summary>
        /// <remarks>
        /// This does not make the stack validated. It restricts it to platform
        /// cryptography and to algorithms that a validated module can perform;
        /// whether the module is actually running in a validated mode remains a
        /// property of how the machine is configured.
        /// </remarks>
        FipsOnly = 2
    }
}
