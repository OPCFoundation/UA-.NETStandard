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

namespace Opc.Ua.AI.Inference
{
    /// <summary>
    /// The backends behind the deployments this Server publishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two deployments mean two backends, and that is the whole point rather than
    /// an implementation detail. A fallback reached through the same client, the
    /// same connection and the same credentials as the primary is not a fallback -
    /// it is a retry, and it fails for every reason the primary just failed for.
    /// </para>
    /// <para>
    /// Keeping them as separate instances is what makes it possible to say, and to
    /// test, that the two are independently reachable.
    /// </para>
    /// </remarks>
    public sealed class InferenceBackends
    {
        /// <summary>
        /// Creates the set.
        /// </summary>
        /// <param name="primary">Backend behind the primary deployment.</param>
        /// <param name="fallback">
        /// Backend behind the fallback deployment, or <c>null</c> when this Server
        /// publishes no fallback.
        /// </param>
        public InferenceBackends(IInferenceBackend primary, IInferenceBackend? fallback = null)
        {
            Primary = primary ?? throw new ArgumentNullException(nameof(primary));
            Fallback = fallback;
        }

        /// <summary>
        /// Backend behind the primary deployment.
        /// </summary>
        public IInferenceBackend Primary { get; }

        /// <summary>
        /// Backend behind the fallback deployment, if there is one.
        /// </summary>
        public IInferenceBackend? Fallback { get; }
    }
}
