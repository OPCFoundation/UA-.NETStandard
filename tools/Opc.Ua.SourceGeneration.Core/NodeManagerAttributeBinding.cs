// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

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
        public string? TargetNamespace { get; init; }

        /// <summary>
        /// The name of the user partial class. Used as the class name
        /// of the generated companion partial. The matching factory is
        /// emitted as <c>{TargetClassName}Factory</c>.
        /// </summary>
        public string? TargetClassName { get; init; }

        /// <summary>
        /// Optional model namespace URI selector. When set, the binding
        /// matches the design whose <c>TargetNamespace.Value</c> equals
        /// this URI (case-sensitive, exact match).
        /// </summary>
        public string? NamespaceUri { get; init; }

        /// <summary>
        /// Optional design file selector (file name without extension).
        /// Used when binding by URI is inconvenient.
        /// </summary>
        public string? Design { get; init; }

        /// <summary>
        /// Whether to also emit a <c>{TargetClassName}Factory</c>.
        /// Defaults to <c>true</c>.
        /// </summary>
        public bool GenerateFactory { get; init; } = true;

        /// <summary>
        /// Whether to emit the public
        /// <c>(IServerInternal, ApplicationConfiguration)</c> constructor.
        /// Defaults to <c>true</c>. The protected constructor that takes
        /// the namespace URI array is emitted either way.
        /// </summary>
        public bool GenerateDefaultConstructor { get; init; } = true;

        /// <summary>
        /// Additional namespace URIs (beyond the model namespace) that
        /// the generated constructor reports to the base node manager,
        /// e.g. a separate instance namespace. <c>null</c> when the
        /// attribute did not specify any.
        /// </summary>
        public IReadOnlyList<string>? AdditionalNamespaceUris { get; init; }
    }
}
