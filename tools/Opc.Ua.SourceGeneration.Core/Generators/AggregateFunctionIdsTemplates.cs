// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Templates for the <see cref="AggregateFunctionIdsGenerator"/>.
    /// </summary>
    internal static class AggregateFunctionIdsTemplates
    {
        /// <summary>
        /// Output file with the <c>AggregateFunctionIds</c> class.
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            namespace {{Tokens.Namespace}}
            {
                /// <summary>
                /// The aggregate functions (instances of <c>AggregateFunctionType</c>,
                /// OPC 10000-13) that the {{Tokens.Namespace}} model declares.
                /// </summary>
                [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
                [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
                public static partial class AggregateFunctionIds
                {
                    /// <summary>
                    /// The NodeId and BrowseName of every aggregate function the model
                    /// declares, in the order the model declares them.
                    /// </summary>
                    public static global::Opc.Ua.ArrayOf<(global::Opc.Ua.NodeId AggregateId, string BrowseName)> All { get; } =
                        new global::Opc.Ua.ArrayOf<(global::Opc.Ua.NodeId AggregateId, string BrowseName)>(
                            new (global::Opc.Ua.NodeId AggregateId, string BrowseName)[]
                            {
                                {{Tokens.ListOfIdentifiers}}
                            });
                }
            }

            """);

        /// <summary>
        /// One <c>(ObjectIds.X, BrowseNames.X),</c> entry per aggregate function.
        /// </summary>
        public static readonly TemplateString Entry = TemplateString.Parse(
            $$"""
            (global::{{Tokens.NamespacePrefix}}.ObjectIds.{{Tokens.SymbolicId}}, global::{{Tokens.NamespacePrefix}}.BrowseNames.{{Tokens.SymbolicName}}),

            """);
    }
}
