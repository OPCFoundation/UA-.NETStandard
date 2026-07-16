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
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Opc.Ua.MigrationAnalyzer.Generator
{
    /// <summary>
    /// Emits an <code>internal sealed [Obsolete] class &lt;Name&gt;Collection :
    /// List&lt;TElement&gt;</code> shim into the consumer's compilation for every
    /// <c>&lt;Name&gt;Collection</c> reference that fails to bind, so 1.5.378-style
    /// call sites compile against the 2.0 stack while UA0002 still guides the
    /// eventual rewrite to <c>List&lt;T&gt;</c> / <c>ArrayOf&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Pipeline:
    /// <list type="number">
    /// <item>
    ///   Syntactic filter: <see cref="IdentifierNameSyntax"/> whose
    ///   <c>Identifier.ValueText</c> ends with <c>"Collection"</c> AND appears in a
    ///   type position (object-creation type, variable / parameter / field
    ///   declaration type, generic argument, <c>typeof</c>, cast target).
    /// </item>
    /// <item>
    ///   Semantic transform: skip if the symbol resolves (the type still exists);
    ///   otherwise resolve the element type in priority order:
    ///   <list type="bullet">
    ///   <item><see cref="CollectionShimCatalog.WellKnownOverrides"/> — element-type
    ///   renames across the 1.5.378 → 2.0 boundary (only 4 entries).</item>
    ///   <item><c>Compilation.GetSymbolsWithName</c> — model-compiled
    ///   <c>&lt;UserType&gt;</c>s declared in the consumer's source.</item>
    ///   <item><c>Compilation.GetTypeByMetadataName</c> against <c>System.*</c> and
    ///   <c>Opc.Ua.*</c> — primitive aliases (Int32, Boolean, ...) and built-in
    ///   OPC UA element types (NodeId, Variant, DataValue, ...).</item>
    ///   </list>
    ///   On zero / ambiguous matches, report <c>MIG01</c> instead of emitting.
    /// </item>
    /// <item>
    ///   Collect &amp; deduplicate by <c>(shortName, elementFqn)</c>; emit one
    ///   <c>&lt;name&gt;.g.cs</c> file per unique entry.
    /// </item>
    /// </list>
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class MigrationGenerator : IIncrementalGenerator
    {
        private const string CollectionSuffix = "Collection";
        private const string ShimNamespace = "Opc.Ua";

        /// <inheritdoc/>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 1. Syntactic candidates: every IdentifierNameSyntax that smells like a
            //    legacy <Type>Collection in a type position.
            IncrementalValuesProvider<CandidateSite> candidates = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsLegacyCollectionCandidate(node),
                    transform: static (ctx, ct) => TransformCandidate(ctx, ct))
                .Where(static x => x is not null)
                .Select(static (x, _) => x!.Value);

            // 2. Per compilation: bucket the candidates so we can dedup + emit once.
            IncrementalValueProvider<ImmutableArray<CandidateSite>> collected =
                candidates.Collect();

            context.RegisterSourceOutput(collected, static (spc, sites) => Emit(spc, sites));
        }

        private static bool IsLegacyCollectionCandidate(SyntaxNode node)
        {
            if (node is not IdentifierNameSyntax id)
            {
                return false;
            }

            string text = id.Identifier.ValueText;
            if (text.Length <= CollectionSuffix.Length ||
                !text.EndsWith(CollectionSuffix, System.StringComparison.Ordinal))
            {
                return false;
            }

            return IsInTypePosition(id);
        }

        private static bool IsInTypePosition(SyntaxNode node)
        {
            // Reject obviously-non-type contexts (variable references, method-call
            // receivers that aren't static-typed accesses, etc.) by checking the
            // immediate / shallow ancestor.
            SyntaxNode? parent = node.Parent;
            while (parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NameSyntax)
            {
                parent = parent.Parent;
            }

            return parent switch
            {
                ObjectCreationExpressionSyntax oce when oce.Type == node => true,
                TypeArgumentListSyntax => true,
                ArrayTypeSyntax => true,
                NullableTypeSyntax => true,
                TupleElementSyntax => true,
                VariableDeclarationSyntax vd when vd.Type == node => true,
                ParameterSyntax p when p.Type == node => true,
                PropertyDeclarationSyntax pd when pd.Type == node => true,
                FieldDeclarationSyntax fd when fd.Declaration.Type == node => true,
                MethodDeclarationSyntax md when md.ReturnType == node => true,
                TypeOfExpressionSyntax => true,
                CastExpressionSyntax ce when ce.Type == node => true,
                DefaultExpressionSyntax de when de.Type == node => true,
                BaseListSyntax => true,
                SimpleBaseTypeSyntax => true,
                IsPatternExpressionSyntax => true,
                DeclarationExpressionSyntax => true,
                _ => false
            };
        }

        private static CandidateSite? TransformCandidate(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var id = (IdentifierNameSyntax)ctx.Node;
            string shortName = id.Identifier.ValueText;

            // If the type already binds in the compilation, no shim needed.
            SymbolInfo info = ctx.SemanticModel.GetSymbolInfo(id, ct);
            if (info.Symbol is INamedTypeSymbol)
            {
                return null;
            }

            string elementShortName = shortName.Substring(0, shortName.Length - CollectionSuffix.Length);
            Location location = id.GetLocation();

            // (a) Catalog override fast-path.
            if (CollectionShimCatalog.WellKnownOverrides.TryGetValue(shortName, out string? elementFqn))
            {
                return new CandidateSite(
                    shortName: shortName,
                    elementDisplay: elementFqn,
                    resolved: true,
                    location: location);
            }

            // (b) Semantic lookup for model-compiled element types (declared in the
            // consumer's source — captures the legacy model-compiler pattern where
            // Foo.BarCollection sat next to Foo.Bar in the same source tree).
            ImmutableArray<INamedTypeSymbol> matches = [.. ctx.SemanticModel.Compilation
                .GetSymbolsWithName(elementShortName, SymbolFilter.Type, ct)
                .OfType<INamedTypeSymbol>()];
            if (matches.Length == 1)
            {
                INamedTypeSymbol elementType = matches[0];
                return new CandidateSite(
                    shortName: shortName,
                    elementDisplay: elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    resolved: true,
                    location: location);
            }

            // (c) Metadata-reference lookup for primitives + standard OPC UA types.
            // GetSymbolsWithName above only inspects source declarations, so types
            // like System.Int32 (from mscorlib) or Opc.Ua.NodeId (from the
            // Opc.Ua.Types reference) won't be found there. We try the well-known
            // namespaces a 1.5.378 <Type>Collection might wrap, in priority order:
            //   1. System.*       — covers primitive aliases (Int32, Boolean, ...).
            //   2. Opc.Ua.*       — covers all built-in OPC UA element types
            //                        (NodeId, Variant, DataValue, ExpandedNodeId,
            //                         EndpointDescription, ReadValueId, ...).
            INamedTypeSymbol? metadataType =
                ctx.SemanticModel.Compilation.GetTypeByMetadataName("System." + elementShortName)
                ?? ctx.SemanticModel.Compilation.GetTypeByMetadataName("Opc.Ua." + elementShortName);
            if (metadataType is not null)
            {
                return new CandidateSite(
                    shortName: shortName,
                    elementDisplay: metadataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    resolved: true,
                    location: location);
            }

            // (d) Unresolvable - propagate the location so we can emit MIG01.
            return new CandidateSite(
                shortName: shortName,
                elementDisplay: elementShortName,
                resolved: false,
                location: location);
        }

        private static void Emit(SourceProductionContext spc, ImmutableArray<CandidateSite> sites)
        {
            if (sites.IsDefaultOrEmpty)
            {
                return;
            }

            // Dedup the emit set by (shortName, elementDisplay); report MIG01 only
            // when no resolved entry for the same shortName exists.
            HashSet<string> resolvedShortNames = new(System.StringComparer.Ordinal);
            Dictionary<string, (string ElementDisplay, Location Location)> emitTargets =
                new(System.StringComparer.Ordinal);
            List<(string ShortName, string ElementShortName, Location Location)> unresolvedSites = [];

            foreach (CandidateSite site in sites)
            {
                if (site.Resolved)
                {
                    resolvedShortNames.Add(site.ShortName);
                    if (!emitTargets.ContainsKey(site.ShortName))
                    {
                        emitTargets[site.ShortName] = (site.ElementDisplay, site.Location);
                    }
                }
                else
                {
                    unresolvedSites.Add((site.ShortName, site.ElementDisplay, site.Location));
                }
            }

            foreach (KeyValuePair<string, (string ElementDisplay, Location Location)> entry in emitTargets)
            {
                string source = RenderShimSource(entry.Key, entry.Value.ElementDisplay);
                spc.AddSource($"{entry.Key}.g.cs", SourceText.From(source, Encoding.UTF8));
            }

            foreach ((string shortName, string elementShortName, Location location) in unresolvedSites)
            {
                if (resolvedShortNames.Contains(shortName))
                {
                    // Already covered by a sibling resolved site; suppress the noise.
                    continue;
                }

                spc.ReportDiagnostic(Diagnostic.Create(
                    GeneratorDiagnostics.UnresolvableElementType,
                    location,
                    elementShortName,
                    shortName));
            }
        }

        private static string RenderShimSource(string shortName, string elementDisplay)
        {
            // Strip the leading "global::" if the catalog supplied it for readability
            // in the [Obsolete] message + XML doc, but keep the qualified form on
            // the base type / ctors so name resolution can't go wrong.
            string elementDisplayShort = elementDisplay.StartsWith("global::", System.StringComparison.Ordinal)
                ? elementDisplay.Substring("global::".Length)
                : elementDisplay;

            return $$"""
                // <auto-generated/>
                #nullable enable

                #pragma warning disable RCS0056 // generated code has long obsolete messages

                namespace {{ShimNamespace}}
                {
                    /// <summary>
                    /// Source-generated shim for the legacy '{{shortName}}' wrapper that was
                    /// removed in 2.0. Inherits from <c>List&lt;{{elementDisplayShort}}&gt;</c>
                    /// so 1.5.378-style call sites compile, and converts implicitly to
                    /// <c>ArrayOf&lt;{{elementDisplayShort}}&gt;</c> so 2.0 APIs that expect
                    /// <c>ArrayOf</c> still accept the instance. Use <c>List&lt;{{elementDisplayShort}}&gt;</c>
                    /// for mutable storage or <c>ArrayOf&lt;{{elementDisplayShort}}&gt;</c> for
                    /// read-only consumers. Tracked by analyzer rule <c>UA0002</c>.
                    /// </summary>
                    [global::System.Obsolete(
                        "'{{shortName}}' was removed in 2.0. Use 'List<{{elementDisplayShort}}>' " +
                        "or 'ArrayOf<{{elementDisplayShort}}>' instead. (UA0002)")]
                    internal sealed class {{shortName}} : global::System.Collections.Generic.List<{{elementDisplay}}>
                    {
                        public {{shortName}}() { }
                        public {{shortName}}(int capacity) : base(capacity) { }
                        public {{shortName}}(
                            global::System.Collections.Generic.IEnumerable<{{elementDisplay}}> collection)
                            : base(collection) { }
                        public static implicit operator global::Opc.Ua.ArrayOf<{{elementDisplay}}>(
                            {{shortName}}? value)
                            => value is null ? default : value.ToArrayOf();
                    }
                }

                """;
        }

        /// <summary>
        /// Per-call-site capture flowed through the incremental pipeline.
        /// </summary>
        /// <remarks>
        /// Roslyn requires every value-type flowing through an
        /// <c>IncrementalValueProvider</c> to be equatable for caching correctness.
        /// The <c>Location</c> isn't part of the equality contract because it changes
        /// across edits even when the candidate set is otherwise stable; the
        /// generator only uses it as the report site for <c>MIG01</c>.
        /// </remarks>
        private readonly struct CandidateSite : System.IEquatable<CandidateSite>
        {
            public CandidateSite(string shortName, string elementDisplay, bool resolved, Location location)
            {
                ShortName = shortName;
                ElementDisplay = elementDisplay;
                Resolved = resolved;
                Location = location;
            }

            public string ShortName { get; }

            public string ElementDisplay { get; }

            public bool Resolved { get; }

            public Location Location { get; }

            public bool Equals(CandidateSite other)
            {
                return string.Equals(ShortName, other.ShortName, System.StringComparison.Ordinal) &&
                    string.Equals(ElementDisplay, other.ElementDisplay, System.StringComparison.Ordinal) &&
                    Resolved == other.Resolved;
            }

            public override bool Equals(object? obj)
            {
                return obj is CandidateSite other && Equals(other);
            }

            public override int GetHashCode()
            {
                int hash = 17;
                hash = (hash * 31) + System.StringComparer.Ordinal.GetHashCode(ShortName);
                hash = (hash * 31) + System.StringComparer.Ordinal.GetHashCode(ElementDisplay);
                hash = (hash * 31) + Resolved.GetHashCode();
                return hash;
            }

            public static bool operator ==(CandidateSite left, CandidateSite right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(CandidateSite left, CandidateSite right)
            {
                return !left.Equals(right);
            }
        }
    }
}
