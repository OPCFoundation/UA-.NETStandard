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

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for the Kubernetes readiness and liveness HTTP bridge.
    /// </summary>
    public sealed class KubernetesReadinessOptions
    {
        /// <summary>
        /// Gets or sets the listener host. Use <c>+</c> to bind all interfaces.
        /// </summary>
        public string Host { get; set; } = "+";

        /// <summary>
        /// Gets or sets the listener port.
        /// </summary>
        public int Port { get; set; } = 8080;

        /// <summary>
        /// Gets or sets the readiness path.
        /// </summary>
        public string ReadinessPath { get; set; } = "/readyz";

        /// <summary>
        /// Gets or sets the liveness path.
        /// </summary>
        public string LivenessPath { get; set; } = "/livez";

        /// <summary>
        /// Gets or sets the minimum OPC 10000-4 §6.6.2.4.2 <c>ServiceLevel</c> considered ready.
        /// </summary>
        public byte ReadyMinimumServiceLevel { get; set; } = 200;
    }
}
