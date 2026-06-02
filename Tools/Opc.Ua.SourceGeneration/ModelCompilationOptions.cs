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
using Microsoft.CodeAnalysis.Diagnostics;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Model compiler options
    /// </summary>
    internal sealed record class ModelCompilationOptions
    {
        /// <summary>
        /// Model options
        /// </summary>
        public DesignFileOptions Options { get; set; }

        /// <summary>
        /// -useAllowSubtypes
        /// </summary>
        public bool UseAllowSubtypes { get; set; }

        /// <summary>
        /// -exclude [id;id;id]
        /// </summary>
        public IReadOnlyList<string> Exclude { get; set; }

        /// <summary>
        /// Opt out of <see cref="ObjectTypeProxyGenerator"/>. By
        /// default the source generator emits a typed asynchronous
        /// client wrapper (<c>{TypeName}Client</c>) for every
        /// <c>ObjectType</c> in the model — including types without
        /// methods, so downstream models can derive from the emitted
        /// proxies. Each generated proxy inherits from the proxy of its
        /// parent ObjectType (forming a chain that mirrors the OPC UA
        /// type hierarchy and ultimately roots at the hand-authored
        /// <c>Opc.Ua.ObjectTypeClient</c> base). Methods that share a
        /// name with an ancestor method are emitted with the C#
        /// <c>new</c> modifier so the derived signature shadows the
        /// ancestor. Set this to <c>true</c> to suppress proxy
        /// emission. Surfaced from MSBuild via the
        /// <c>ModelSourceGeneratorOmitObjectTypeProxies</c> property.
        /// </summary>
        public bool OmitObjectTypeProxies { get; set; }

        /// <summary>
        /// Optional override for the C# namespace used by classes emitted
        /// by the <see cref="ObjectTypeProxyGenerator"/>. By default
        /// proxies are emitted into the model's own namespace (i.e. the
        /// C# prefix of the model's target namespace) — for example the
        /// standard UA NodeSet emits into <c>Opc.Ua</c>. Set this option
        /// to redirect proxy emission into a different namespace.
        /// Surfaced from MSBuild via the
        /// <c>ModelSourceGeneratorObjectTypeProxyNamespace</c> property.
        /// </summary>
        public string ObjectTypeProxyNamespace { get; set; }

        /// <summary>
        /// When set to <c>true</c>, the generator uses the modelling rules
        /// from the referenced type definition unconditionally for all
        /// structural code generation decisions and the emitted runtime
        /// <c>ModellingRuleId</c>.  When off (default), the generator
        /// enforces OPC UA modelling rule promotion semantics —
        /// instances may only promote, never demote, the type
        /// definition's rule.
        /// Surfaced from MSBuild via the
        /// <c>ModelSourceGeneratorUseTypeDefinitionModellingRules</c> property.
        /// </summary>
        public bool UseTypeDefinitionModellingRules { get; set; }

        /// <summary>
        /// Override for whether the generator emits the
        /// <c>[assembly: ModelDependencyAttribute]</c> + corresponding
        /// <c>[assembly: ModelSnapshotAttribute]</c> metadata for the
        /// model(s) being generated.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Auto</c> (default): emit only when the consuming
        /// compilation produces a library
        /// (<see cref="OutputKind.DynamicallyLinkedLibrary"/> /
        /// <see cref="OutputKind.NetModule"/>). Applications never
        /// expose their model metadata to downstream consumers — they
        /// are leaf consumers themselves — so the attribute payload is
        /// silently skipped to save assembly bloat.
        /// </para>
        /// <para>
        /// <c>Always</c>: emit regardless of OutputKind. Useful for
        /// integration-test applications that simulate library
        /// behaviour.
        /// </para>
        /// <para>
        /// <c>Never</c>: do not emit, even from libraries. Useful when
        /// the consumer assembly is sealed and downstream usage is
        /// known impossible.
        /// </para>
        /// <para>
        /// Surfaced from MSBuild via the
        /// <c>ModelSourceGeneratorEmitDependencyMetadata</c> property.
        /// </para>
        /// </remarks>
        public EmitDependencyMetadataMode EmitDependencyMetadata { get; set; }
            = EmitDependencyMetadataMode.Auto;

        /// <summary>
        /// When set to <c>true</c>, the per-ObjectType typed accessor
        /// extension classes (<c>{TypeName}StateComponents</c> +
        /// <c>{TypeName}StateProperties</c>) are emitted alongside
        /// the model output. Off by default because the emitted
        /// accessors reference
        /// <c>Opc.Ua.Server.Fluent.IComponentAccessor</c> (server-side
        /// assembly) — model-only libraries would fail to compile.
        /// Set in projects that ship a server-side integration
        /// (Applications/*, Libraries/Opc.Ua.*.Server/*) via the
        /// <c>ModelSourceGeneratorEmitFluentAccessors</c> MSBuild
        /// property.
        /// </summary>
        public bool EmitFluentAccessors { get; set; }

        /// <summary>
        /// Get options from options provider
        /// </summary>
        public static ModelCompilationOptions From(AnalyzerConfigOptionsProvider provider)
        {
            return new ModelCompilationOptions
            {
                Options = new DesignFileOptions
                {
                    Version = provider.GlobalOptions.GetString(
                        nameof(DesignFileOptions.Version)) ??
                        "v105",
                    StartId = (uint)provider.GlobalOptions.GetInteger(
                        nameof(DesignFileOptions.StartId)),
                    ModelVersion = provider.GlobalOptions.GetString(
                        nameof(DesignFileOptions.ModelVersion)),
                    ModelPublicationDate = provider.GlobalOptions.GetString(
                        nameof(DesignFileOptions.ModelPublicationDate)),
                    ReleaseCandidate = provider.GlobalOptions.GetBool(
                        nameof(DesignFileOptions.ReleaseCandidate)),
                    GenerateNodeManager = provider.GlobalOptions.GetBool(
                        nameof(DesignFileOptions.GenerateNodeManager))
                },
                Exclude = provider.GlobalOptions.GetStrings(nameof(Exclude)),
                UseAllowSubtypes = provider.GlobalOptions.GetBool(nameof(UseAllowSubtypes)),
                OmitObjectTypeProxies = provider.GlobalOptions.GetBool(
                    nameof(OmitObjectTypeProxies)),
                ObjectTypeProxyNamespace = provider.GlobalOptions.GetString(
                    nameof(ObjectTypeProxyNamespace)),
                UseTypeDefinitionModellingRules = provider.GlobalOptions.GetBool(
                    nameof(UseTypeDefinitionModellingRules)),
                EmitDependencyMetadata = ParseEmitMode(provider.GlobalOptions.GetString(
                    nameof(EmitDependencyMetadata))),
                EmitFluentAccessors = provider.GlobalOptions.GetBool(
                    nameof(EmitFluentAccessors))
            };
        }

        private static EmitDependencyMetadataMode ParseEmitMode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return EmitDependencyMetadataMode.Auto;
            }
            return raw.Trim().ToLowerInvariant() switch
            {
                "always" or "true" => EmitDependencyMetadataMode.Always,
                "never" or "false" => EmitDependencyMetadataMode.Never,
                _ => EmitDependencyMetadataMode.Auto
            };
        }
    }

    /// <summary>
    /// Whether the generator emits cross-assembly model-dependency
    /// metadata attributes (<c>ModelDependencyAttribute</c> +
    /// <c>ModelSnapshotAttribute</c>).
    /// </summary>
    internal enum EmitDependencyMetadataMode
    {
        /// <summary>
        /// Emit only when building a library.
        /// </summary>
        Auto,
        /// <summary>
        /// Always emit (override).
        /// </summary>
        Always,
        /// <summary>
        /// Never emit (override).
        /// </summary>
        Never
    }
}
