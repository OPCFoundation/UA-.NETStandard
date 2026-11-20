// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        /// One <c>public const {uint|string} X = global::{ns}.Objects.{Y};</c>
        /// line per state or transition NodeId entry. The constant type follows the
        /// type of the aliased <c>Objects</c> constant, which is <c>string</c> for
        /// models that use string NodeIds. Guid and Opaque identifiers cannot be
        /// C# constants, so those entries are emitted as <c>static readonly</c>.
        /// </summary>
        public static readonly TemplateString IdEntry = TemplateString.Parse(
            $$"""
            public {{Tokens.IdModifier}} {{Tokens.IdType}} {{Tokens.Name}} = global::{{Tokens.NamespacePrefix}}.Objects.{{Tokens.Identifier}};

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
