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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Opc.Ua.MigrationAnalyzer.Tests
{
    /// <summary>
    /// <para>
    /// Lightweight, no-extra-package analyzer + code-fix test harness. Lifts the
    /// useful bits of Microsoft.CodeAnalysis.Testing without taking the dependency.
    /// </para>
    /// <para>
    /// Each test feeds a small C# source snippet (concatenated with
    /// <see cref="OpcUaStubs.Source"/>) into the harness; the harness compiles
    /// the snippet, runs the given <see cref="DiagnosticAnalyzer"/>, asserts
    /// the diagnostics, optionally applies the matching <see cref="CodeFixProvider"/>,
    /// and verifies the fixed source matches an expected string.
    /// </para>
    /// </summary>
    public static class AnalyzerHarness
    {
        private static readonly ImmutableArray<MetadataReference> s_baseReferences = BuildBaseReferences();

        private static ImmutableArray<MetadataReference> BuildBaseReferences()
        {
            // On .NET Core / .NET 5+, the runtime exposes the full set of trusted
            // platform assemblies via AppContext, which is exactly the closure of
            // reference assemblies we need to compile arbitrary test snippets.
            string trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
            string[] tpaList = [.. trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(p => !string.IsNullOrEmpty(p))];
            if (tpaList.Length > 0)
            {
                return [.. tpaList.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
            }
            // On .NET Framework, TPA is not exposed. Fall back to scanning the
            // runtime directory for the BCL assemblies — that's where mscorlib /
            // System / System.Core etc. live at runtime, and they are sufficient
            // for our small test snippets (the analyzer doesn't need full .NET
            // surface area to bind types from the OpcUaStubs source).
            string runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            if (string.IsNullOrEmpty(runtimeDir) || !Directory.Exists(runtimeDir))
            {
                return [];
            }
            return [.. Directory.EnumerateFiles(runtimeDir, "*.dll")
                .Where(IsBclAssembly)
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
        }

        private static bool IsBclAssembly(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft.CSharp", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Compile <paramref name="userSource"/> together with the OPC UA stub surface
        /// and return the resulting <see cref="Compilation"/>.
        /// </summary>
        public static CSharpCompilation Compile(string userSource, string assemblyName = "TestAssembly")
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp13);
            SyntaxTree[] trees =
            [
                CSharpSyntaxTree.ParseText(OpcUaStubs.Source, parseOptions, "OpcUaStubs.cs"),
                CSharpSyntaxTree.ParseText(userSource, parseOptions, "Test.cs")
            ];
            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: false,
                nullableContextOptions: NullableContextOptions.Enable);
            return CSharpCompilation.Create(assemblyName, trees, s_baseReferences, options);
        }

        /// <summary>
        /// Compile <paramref name="userSource"/> WITHOUT the OPC UA stub surface, so
        /// analyzers that branch on "is the legacy symbol present in the compilation?"
        /// take their symbol-absent fallback. Used by tests that exercise UA0021's
        /// syntactic-fallback path (when the consumer is on bare 1.6 and the legacy
        /// <c>CertificateValidator</c>/<c>CertificateValidationEventArgs</c> types are
        /// no longer defined anywhere).
        /// </summary>
        public static CSharpCompilation CompileWithoutStubs(string userSource, string assemblyName = "TestAssembly")
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp13);
            SyntaxTree[] trees =
            [
                CSharpSyntaxTree.ParseText(userSource, parseOptions, "Test.cs")
            ];
            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: false,
                nullableContextOptions: NullableContextOptions.Enable);
            return CSharpCompilation.Create(assemblyName, trees, s_baseReferences, options);
        }

        /// <summary>
        /// Run <paramref name="analyzer"/> against <paramref name="userSource"/> and
        /// return only the analyzer's diagnostics (compiler diagnostics are filtered out).
        /// </summary>
        public static Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            DiagnosticAnalyzer analyzer,
            string userSource)
        {
            CSharpCompilation compilation = Compile(userSource);
            return RunAsync(analyzer, compilation);
        }

        /// <summary>
        /// Run <paramref name="analyzer"/> against <paramref name="userSource"/> compiled
        /// WITHOUT the OPC UA stub surface. See <see cref="CompileWithoutStubs"/>.
        /// </summary>
        public static Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsWithoutStubsAsync(
            DiagnosticAnalyzer analyzer,
            string userSource)
        {
            CSharpCompilation compilation = CompileWithoutStubs(userSource);
            return RunAsync(analyzer, compilation);
        }

        private static Task<ImmutableArray<Diagnostic>> RunAsync(
            DiagnosticAnalyzer analyzer,
            CSharpCompilation compilation)
        {
            CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
                [analyzer],
                new CompilationWithAnalyzersOptions(
                    options: null!,
                    onAnalyzerException: null,
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: true,
                    reportSuppressedDiagnostics: true));
            return withAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        /// <summary>
        /// Apply <paramref name="codeFix"/> to every diagnostic raised by
        /// <paramref name="analyzer"/> against <paramref name="userSource"/> and
        /// return the fixed source string for "Test.cs".
        /// </summary>
        public static async Task<string> ApplyFixAsync(
            DiagnosticAnalyzer analyzer,
            CodeFixProvider codeFix,
            string userSource)
        {
            CSharpCompilation compilation = Compile(userSource);
            CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
                [analyzer],
                new CompilationWithAnalyzersOptions(
                    options: null!,
                    onAnalyzerException: null,
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: true,
                    reportSuppressedDiagnostics: true));
            ImmutableArray<Diagnostic> diags = await withAnalyzers.GetAnalyzerDiagnosticsAsync()
                .ConfigureAwait(false);

            SyntaxTree testTree = compilation.SyntaxTrees.First(t => t.FilePath == "Test.cs");
            Document document = CreateDocument(testTree, codeFix);

            foreach (Diagnostic diag in diags)
            {
                if (diag.Location.SourceTree?.FilePath != "Test.cs")
                {
                    continue;
                }
                List<CodeAction> actions = [];
                var ctx = new CodeFixContext(
                    document,
                    diag,
                    (action, _) => actions.Add(action),
                    CancellationToken.None);
                await codeFix.RegisterCodeFixesAsync(ctx).ConfigureAwait(false);
                if (actions.Count == 0)
                {
                    continue;
                }
                CodeAction first = actions[0];
                ImmutableArray<CodeActionOperation> ops = await first
                    .GetOperationsAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                Solution? newSolution = null;
                foreach (CodeActionOperation op in ops)
                {
                    if (op is ApplyChangesOperation applyOp)
                    {
                        newSolution = applyOp.ChangedSolution;
                        break;
                    }
                }
                if (newSolution != null)
                {
                    document = newSolution.GetDocument(document.Id)!;
                }
            }

            SourceText resultText = await document.GetTextAsync().ConfigureAwait(false);
            return resultText.ToString();
        }

        private static Document CreateDocument(SyntaxTree tree, CodeFixProvider _)
        {
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            Solution solution = workspace.CurrentSolution
                .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
                .AddMetadataReferences(projectId, s_baseReferences)
                .AddDocument(documentId, "Test.cs", tree.GetText());
            return solution.GetDocument(documentId)!;
        }
    }
}
