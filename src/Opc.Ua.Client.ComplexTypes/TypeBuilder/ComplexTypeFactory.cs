// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        public ComplexTypeBuilderFactory(string? assemblyName = null)
        {
            m_moduleFactory = new AssemblyModule(assemblyName);
        }

        /// <summary>
        /// Create a new type builder which uses Reflection.Emit.
        /// </summary>
        public IComplexTypeBuilder Create(
            string targetNamespace,
            int targetNamespaceIndex,
            string? moduleName = null)
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
                .Select(t => (IType)new GeneratedType(t))
                .Concat(m_moduleFactory.GetRuntimeTypes())
                .ToList();
        }

        /// <summary>
        /// Wrapper of types generated
        /// </summary>
        private sealed record class GeneratedType(Type Type) : IType
        {
            /// <inheritdoc/>
            // The dynamically generated complex type carries a DataContractAttribute,
            // so TypeInfo.GetXmlName always returns a non-null value here.
            public XmlQualifiedName XmlName => TypeInfo.GetXmlName(Type)!;
        }
    }
}
