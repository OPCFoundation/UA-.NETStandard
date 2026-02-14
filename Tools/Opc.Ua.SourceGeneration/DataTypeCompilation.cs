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
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceProductionContext = SGF.SgfSourceProductionContext;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Data type compilation
    /// </summary>
    internal sealed record class DataTypeCompilation
    {
        /// <summary>
        /// Check whether the generator can handle the node
        /// </summary>
        public static bool Handles(SyntaxNode node, CancellationToken ct)
        {
            return node is ClassDeclarationSyntax m && m.AttributeLists.Count > 0;
        }

        /// <summary>
        /// Create data type compilation
        /// </summary>
        public DataTypeCompilation(
            GeneratorAttributeSyntaxContext context,
            CancellationToken cancellationToken)
        {
            m_cancellationToken = cancellationToken;

            var classSymbol = (INamedTypeSymbol)context.TargetSymbol;

            m_location = classSymbol.Locations.FirstOrDefault();
            m_className = classSymbol.Name;
            m_namespace = classSymbol.ContainingNamespace.Name;
            m_parameterlessContructor = classSymbol.Constructors.Any(c => c.Parameters.Length == 0);

            if (m_parameterlessContructor)
            {
                m_dataContract = classSymbol
                    .GetAttributes()
                    .Where(a => a.AttributeClass.Name == nameof(DataContractAttribute))
                    .Select(a => a?.NamedArguments.ToDictionary(a => a.Key, a => a.Value.Value))
                    .FirstOrDefault();
                m_properties = [.. classSymbol.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Where(m => !m.IsAbstract && !m.IsStatic && !m.IsReadOnly)
                    .Select(DataTypeProperty.ToProperty)];
            }
        }

        /// <summary>
        /// Emit the generated code
        /// </summary>
        public void Emit(SourceProductionContext sourceContext)
        {
            if (!m_parameterlessContructor)
            {
                sourceContext.ReportDiagnostic(
                    Diagnostic.Create(
                        SourceGenerator.Exception,
                        m_location,
                        "Must have a paremeterless constructor"));
                return;
            }

            foreach (DataTypeProperty property in m_properties)
            {
                m_cancellationToken.ThrowIfCancellationRequested();

                // Example of reporting a diagnostic
                if (string.IsNullOrEmpty(property.Type))
                {
                    sourceContext.ReportDiagnostic(
                        Diagnostic.Create(
                            SourceGenerator.GenericWarning,
                            m_location,
                            $"Property '{property.Name}' has an invalid type."));
                }
            }
        }

        private readonly CancellationToken m_cancellationToken;
        private readonly Location m_location;
        private readonly string m_className;
        private readonly string m_namespace;
        private readonly bool m_parameterlessContructor;
        private readonly Dictionary<string, object> m_dataContract;
        private readonly DataTypeProperty[] m_properties;
    }

    internal sealed record class DataTypeProperty(
        string Name,
        string Type,
        bool IsOptional,
        IReadOnlyDictionary<string, object> DataMemberAttribute)
    {
        public static DataTypeProperty ToProperty(IPropertySymbol propertySymbol)
        {
            AttributeData dataMemberAttribute = propertySymbol
                .GetAttributes()
                .FirstOrDefault(a => a.AttributeClass.Name == nameof(DataMemberAttribute));
            return new DataTypeProperty(
                propertySymbol.Name,
                propertySymbol.Type.Name,
                propertySymbol.NullableAnnotation == NullableAnnotation.Annotated,
                dataMemberAttribute?.NamedArguments.ToDictionary(a => a.Key, a => a.Value.Value));
        }
    }
}
