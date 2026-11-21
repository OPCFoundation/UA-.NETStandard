// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using NUnit.Framework;
using Opc.Ua.MigrationAnalyzer.Analyzers;
using Opc.Ua.MigrationAnalyzer.CodeFixer;

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0020 (EncodeableFactory renames: GlobalFactory and Create).
    /// </summary>
    [TestFixture]
    public class UA0020Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnGlobalFactoryAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static EncodeableFactory M() { var f = EncodeableFactory.GlobalFactory; return f; }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0020EncodeableFactoryRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0020 = diags.SingleOrDefault(d => d.Id == "UA0020");
            Assert.That(ua0020, Is.Not.Null,
                "Expected UA0020 to fire on EncodeableFactory.GlobalFactory access.");
            Assert.That(
                ua0020!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("GlobalFactory"));
        }

        [Test]
        public async Task ReportsDiagnosticOnEncodeableFactoryCreateInvocationAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static EncodeableFactory M(EncodeableFactory factory) => factory.Create();
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0020EncodeableFactoryRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0020 = diags.SingleOrDefault(d => d.Id == "UA0020");
            Assert.That(ua0020, Is.Not.Null,
                "Expected UA0020 to fire on EncodeableFactory.Create() invocation.");
            Assert.That(
                ua0020!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("Create"));
        }

        [Test]
        public async Task DoesNotReportOnForkInvocationAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static EncodeableFactory M(EncodeableFactory factory) => factory.Fork();
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0020EncodeableFactoryRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0020"), Is.False,
                "factory.Fork() must not trigger UA0020.");
        }

        [Test]
        public async Task DoesNotReportOnServiceMessageContextFactoryAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static EncodeableFactory M(ServiceMessageContext ctx) => ctx.Factory;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0020EncodeableFactoryRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0020"), Is.False,
                "ServiceMessageContext.Factory access must not trigger UA0020.");
        }

        [Test]
        public async Task FixRewritesCreateInvocationToForkAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static EncodeableFactory M(EncodeableFactory factory) => factory.Create();
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static EncodeableFactory M(EncodeableFactory factory) => factory.Fork();
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0020EncodeableFactoryRenameAnalyzer(),
                new UA0020EncodeableFactoryRenameCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }

        [Test]
        public async Task FixDoesNotRegisterActionForGlobalFactoryFormAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static EncodeableFactory M() => EncodeableFactory.GlobalFactory;
                }
                """;

            CodeAction[] actions = await CollectFixActionsAsync(
                new UA0020EncodeableFactoryRenameAnalyzer(),
                new UA0020EncodeableFactoryRenameCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(actions, Is.Empty,
                "Form A (GlobalFactory) must not register any code-fix actions.");
        }

        [Test]
        public async Task ReportsDiagnosticOnShimGlobalFactoryAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static EncodeableFactory M()
                    {
                #pragma warning disable CS0618
                        return EncodeableFactoryShim.GlobalFactory;
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0020EncodeableFactoryRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0020"), Is.True,
                "Expected UA0020 to fire on a property carrying [OpcUaShim(\"UA0020\")].");
        }

        [Test]
        public async Task ReportsDiagnosticOnShimCreateInvocationAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static EncodeableFactory M(EncodeableFactory factory)
                    {
                #pragma warning disable CS0618
                        return EncodeableFactoryShim.Create(factory);
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0020EncodeableFactoryRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0020"), Is.True,
                "Expected UA0020 to fire on a Create invocation carrying [OpcUaShim(\"UA0020\")].");
        }

        private static async Task<CodeAction[]> CollectFixActionsAsync(
            Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer analyzer,
            CodeFixProvider codeFix,
            string source)
        {
            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(analyzer, source)
                .ConfigureAwait(false);
            Diagnostic[] userDiags = [.. diags.Where(d => d.Location.SourceTree?.FilePath == "Test.cs")];
            Assert.That(userDiags, Is.Not.Empty,
                "Expected at least one diagnostic on Test.cs for the fix-actions probe.");

            Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation =
                AnalyzerHarness.Compile(source);
            SyntaxTree testTree = compilation.SyntaxTrees
                .First(t => t.FilePath == "Test.cs");

            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            Solution solution = workspace.CurrentSolution
                .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
                .AddDocument(documentId, "Test.cs", testTree.GetText());
            Document document = solution.GetDocument(documentId)!;

            var actions = new List<CodeAction>();
            foreach (Diagnostic diag in userDiags)
            {
                var ctx = new CodeFixContext(
                    document,
                    diag,
                    (action, _) => actions.Add(action),
                    CancellationToken.None);
                await codeFix.RegisterCodeFixesAsync(ctx).ConfigureAwait(false);
            }
            return [.. actions];
        }
    }
}
