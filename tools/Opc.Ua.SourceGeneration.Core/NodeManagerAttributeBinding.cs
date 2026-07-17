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

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// A discovered <c>[NodeManager]</c> attribute binding from user code.
    /// Carries the user-chosen manager class identity and the selector
    /// (namespace URI or design file name) used to bind it to one of the
    /// design files in the project.
    /// </summary>
    public sealed record class NodeManagerAttributeBinding
    {
        /// <summary>
        /// The fully-qualified namespace of the user partial class.
        /// Used as the namespace of the generated companion partial.
        /// </summary>
        public string TargetNamespace { get; init; }

        /// <summary>
        /// The name of the user partial class. Used as the class name
        /// of the generated companion partial. The matching factory is
        /// emitted as <c>{TargetClassName}Factory</c>.
        /// </summary>
        public string TargetClassName { get; init; }

        /// <summary>
        /// Optional model namespace URI selector. When set, the binding
        /// matches the design whose <c>TargetNamespace.Value</c> equals
        /// this URI (case-sensitive, exact match).
        /// </summary>
        public string NamespaceUri { get; init; }

        /// <summary>
        /// Optional design file selector (file name without extension).
        /// Used when binding by URI is inconvenient.
        /// </summary>
        public string Design { get; init; }

        /// <summary>
        /// Whether to also emit a <c>{TargetClassName}Factory</c>.
        /// Defaults to <c>true</c>.
        /// </summary>
        public bool GenerateFactory { get; init; } = true;
    }
}
