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
    /// Templates for the <see cref="StateMachineIdsGenerator"/>.
    /// Emits a strongly-typed identifier class per concrete
    /// <c>FiniteStateMachineType</c> subtype, with nested
    /// <c>StateIds</c> / <c>StateNumbers</c> / <c>TransitionIds</c> /
    /// <c>TransitionNumbers</c> partial classes.
    /// </summary>
    internal static class StateMachineIdsTemplates
    {
        /// <summary>
        /// Single output file template. Hosts every state-machine ids
        /// class generated for the model in a single namespace block.
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            namespace {{Tokens.Namespace}}
            {
                {{Tokens.ListOfTypes}}
            }

            """);

        /// <summary>
        /// One ids class per concrete FSM subtype. Wraps four nested
        /// partial classes: <c>StateIds</c> (NodeId references),
        /// <c>StateNumbers</c> (Part 16 StateNumber values),
        /// <c>TransitionIds</c>, and <c>TransitionNumbers</c>.
        /// </summary>
        public static readonly TemplateString IdsClass = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Strongly-typed state and transition identifiers for the
            /// <c>{{Tokens.TypeName}}</c> ObjectType.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public static partial class {{Tokens.ClassName}}
            {
                /// <summary>
                /// Numeric NodeId portion of each state in the model's namespace.
                /// </summary>
                public static partial class StateIds
                {
                    {{Tokens.ListOfIdentifiers}}
                }

                /// <summary>
                /// OPC UA <c>StateNumber</c> property value per state.
                /// </summary>
                public static partial class StateNumbers
                {
                    {{Tokens.ListOfValues}}
                }

                /// <summary>
                /// Numeric NodeId portion of each transition in the model's namespace.
                /// </summary>
                public static partial class TransitionIds
                {
                    {{Tokens.ListOfChildren}}
                }

                /// <summary>
                /// OPC UA <c>TransitionNumber</c> property value per transition.
                /// </summary>
                public static partial class TransitionNumbers
                {
                    {{Tokens.ListOfFields}}
                }
            }
            """);

        /// <summary>
        /// One <c>public const uint X = global::{ns}.Objects.{Y};</c>
        /// line per state or transition NodeId entry.
        /// </summary>
        public static readonly TemplateString IdEntry = TemplateString.Parse(
            $$"""
            public const uint {{Tokens.Name}} = global::{{Tokens.NamespacePrefix}}.Objects.{{Tokens.Identifier}};

            """);

        /// <summary>
        /// One <c>public const uint X = Nu;</c> line per state or
        /// transition number entry that carries a value.
        /// </summary>
        public static readonly TemplateString NumberEntry = TemplateString.Parse(
            $$"""
            public const uint {{Tokens.Name}} = {{Tokens.NumericIdValue}}u;

            """);

        /// <summary>
        /// Comment line for a state / transition whose number property
        /// is not declared in the source model.
        /// </summary>
        public static readonly TemplateString NumberMissingComment = TemplateString.Parse(
            $$"""
            // {{Tokens.Name}}: StateNumber/TransitionNumber not declared in the model.

            """);

        /// <summary>
        /// Comment emitted in place of any entries when the FSM does
        /// not declare any states / transitions of the relevant kind.
        /// </summary>
        public static readonly TemplateString EmptySectionComment = TemplateString.Parse(
            $$"""
            // (none defined in the model)
            """);
    }
}
