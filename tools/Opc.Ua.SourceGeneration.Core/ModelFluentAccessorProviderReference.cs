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

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Information read from a referenced assembly's
    /// <see cref="ModelFluentAccessorProviderAttribute"/>.
    /// </summary>
    public readonly record struct ModelFluentAccessorProviderReference
    {
        /// <summary>
        /// Creates a provider reference.
        /// </summary>
        public ModelFluentAccessorProviderReference(
            string assemblyName,
            string modelUri,
            string prefix)
        {
            AssemblyName = assemblyName ?? string.Empty;
            ModelUri = modelUri ?? string.Empty;
            Prefix = prefix ?? string.Empty;
        }

        /// <summary>
        /// The assembly containing the generated accessors.
        /// </summary>
        public string AssemblyName { get; }

        /// <summary>
        /// The OPC UA model URI.
        /// </summary>
        public string ModelUri { get; }

        /// <summary>
        /// The C# namespace containing the generated accessors.
        /// </summary>
        public string Prefix { get; }

        /// <summary>
        /// Returns true when the provider has a model URI and C# prefix.
        /// </summary>
        public bool IsValid =>
            !string.IsNullOrEmpty(ModelUri) &&
            !string.IsNullOrEmpty(Prefix);
    }
}
