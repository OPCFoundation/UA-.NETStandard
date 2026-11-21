// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Helpers;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0009: Flag classes annotated with
    /// <c>System.Runtime.Serialization.DataContractAttribute</c> that also have
    /// at least one <c>DataMember</c> property — they are candidates for
    /// migration to <c>[Opc.Ua.DataType]</c> / <c>[Opc.Ua.DataTypeField]</c>.
    /// </summary>
    /// <remarks>
    /// Simplified detection: the analyzer does NOT verify that the class is
    /// actually consumed by <c>ApplicationConfiguration.ParseExtension&lt;T&gt;</c>
    /// or <c>UpdateExtension&lt;T&gt;</c>. Cross-compilation usage scanning would
    /// require a <c>CompilationEndAction</c> and full symbol walk. Trade-off:
    /// more false positives but a much simpler analyzer.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0009DataContractToDataTypeAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0009_DataContractToDataType];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            Dictionary<Compilation, UaSymbols> cache = [];
            var symbols = UaSymbols.For(context.Compilation, cache);
            if (symbols.DataContractType is null || symbols.DataMemberType is null)
            {
                return;
            }
            context.RegisterSymbolAction(c => AnalyzeNamedType(c, symbols), SymbolKind.NamedType);
        }

        private static void AnalyzeNamedType(SymbolAnalysisContext context, UaSymbols symbols)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            if (type.TypeKind != TypeKind.Class)
            {
                return;
            }

            if (!HasAttribute(type, symbols.DataContractType))
            {
                return;
            }

            if (!HasDataMemberProperty(type, symbols.DataMemberType))
            {
                return;
            }

            foreach (Location location in type.Locations)
            {
                if (location.IsInSource)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.UA0009_DataContractToDataType,
                        location,
                        type.Name));
                }
            }
        }

        private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeType)
        {
            SymbolEqualityComparer eq = SymbolEqualityComparer.Default;
            foreach (AttributeData attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass != null && eq.Equals(attr.AttributeClass, attributeType))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasDataMemberProperty(INamedTypeSymbol type, INamedTypeSymbol dataMemberType)
        {
            foreach (ISymbol member in type.GetMembers())
            {
                if (member is IPropertySymbol property && HasAttribute(property, dataMemberType))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
