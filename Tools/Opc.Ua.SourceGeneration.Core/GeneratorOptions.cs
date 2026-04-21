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

using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generator options
    /// </summary>
    public sealed class GeneratorOptions
    {
        /// <summary>
        /// Optimize generated code for compile speed.
        /// </summary>
        public bool OptimizeForCompileSpeed { get; set; }

        /// <summary>
        /// Exclusions to apply on the input
        /// </summary>
        public IReadOnlyList<string> Exclusions { get; set; } = [];

        /// <summary>
        /// Generation should be cancelled
        /// </summary>
        public CancellationToken Cancellation { get; set; }

        /// <summary>
        /// Write utf8 string literals when needed
        /// </summary>
        public bool UseUtf8StringLiterals { get; set; } = true;

        /// <summary>
        /// When set to <c>true</c>, the
        /// <see cref="ObjectTypeProxyGenerator"/> is suppressed and no
        /// <c>*TypeClient</c> proxy classes are emitted. Off by default —
        /// proxies are emitted for every <c>ObjectType</c> in the model
        /// alongside the standard model output.
        /// </summary>
        public bool OmitObjectTypeProxies { get; set; }

        /// <summary>
        /// Optional override for the C# namespace used by classes emitted
        /// by the <see cref="ObjectTypeProxyGenerator"/>. When unset,
        /// the model's target namespace prefix is used.
        /// </summary>
        public string ObjectTypeProxyNamespace { get; set; }

        /// <summary>
        /// Maps an OPC UA namespace URI (key) to the C# namespace (value)
        /// in which the corresponding source-generated <c>*TypeClient</c>
        /// proxies live. Used by the
        /// <see cref="ObjectTypeProxyGenerator"/> when a generated
        /// proxy must derive from a base proxy that is defined in a
        /// different (referenced) assembly.
        /// </summary>
        /// <remarks>
        /// The standard mapping
        /// <c>http://opcfoundation.org/UA/ -&gt; Opc.Ua.Client</c> is
        /// always added by the generator and does not need to be
        /// configured explicitly.
        /// </remarks>
        public IDictionary<string, string> ObjectTypeProxyExternalNamespaces { get; }
            = new Dictionary<string, string>();
    }
}
