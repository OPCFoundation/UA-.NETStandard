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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional opt-in interface that exposes the validator a server checks
    /// peer (application instance) certificates with, so subordinate
    /// components can validate a certificate a client supplies later in the
    /// session without any change to <see cref="IServerInternal"/>;
    /// external/mocked <see cref="IServerInternal"/> implementations remain
    /// unaffected.
    /// </summary>
    /// <remarks>
    /// Consumers should resolve the validator in a null-safe manner, e.g.:
    /// <code>
    /// var validator = (server as ICertificateValidatorProvider)?.CertificateValidator;
    /// </code>
    /// </remarks>
    public interface ICertificateValidatorProvider
    {
        /// <summary>
        /// Gets the validator the server checks peer certificates with, or
        /// <c>null</c> when none is available.
        /// </summary>
        ICertificateValidatorEx? CertificateValidator { get; }
    }
}
