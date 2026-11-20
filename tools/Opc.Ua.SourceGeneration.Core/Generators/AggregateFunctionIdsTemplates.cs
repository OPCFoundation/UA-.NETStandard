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
                    public static global::System.Collections.Generic.IReadOnlyList<(global::Opc.Ua.NodeId AggregateId, string BrowseName)> All
                        => s_all;

                    private static readonly global::System.Collections.ObjectModel.ReadOnlyCollection<(global::Opc.Ua.NodeId AggregateId, string BrowseName)> s_all =
                        new global::System.Collections.ObjectModel.ReadOnlyCollection<(global::Opc.Ua.NodeId AggregateId, string BrowseName)>(
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
