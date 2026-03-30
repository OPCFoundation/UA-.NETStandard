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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Factory function for the default complex type builder
    /// using classes created with Reflection.Emit.
    /// </summary>
    public class ComplexTypeBuilderFactory : IComplexTypeFactory
    {
        private readonly AssemblyModule m_moduleFactory;

        /// <summary>
        /// Factory creates types in the assembly module.
        /// </summary>
        public ComplexTypeBuilderFactory(string assemblyName = null)
        {
            m_moduleFactory = new AssemblyModule(assemblyName);
        }

        /// <summary>
        /// Create a new type builder which uses Reflection.Emit.
        /// </summary>
        public IComplexTypeBuilder Create(
            string targetNamespace,
            int targetNamespaceIndex,
            string moduleName = null)
        {
            return new ComplexTypeBuilder(
                m_moduleFactory,
                targetNamespace,
                targetNamespaceIndex,
                moduleName);
        }

        /// <summary>
        /// Return array of all types created in this factory.
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "Types are dynamically built via Reflection.Emit and are preserved.")]
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2067",
            Justification = "Types are dynamically built via Reflection.Emit and are preserved.")]
        public IReadOnlyList<IType> GetTypes()
        {
            return m_moduleFactory.GetTypes()
                .Select(t => new GeneratedType(t))
                .ToList();
        }

        /// <summary>
        /// Wrapper of types generated
        /// </summary>
        private sealed record class GeneratedType(Type Type) : IType
        {
            /// <inheritdoc/>
            public XmlQualifiedName XmlName => TypeInfo.GetXmlName(Type);
        }
    }
}
