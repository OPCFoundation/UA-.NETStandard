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
using System.IO;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for cross-pass <c>[NodeManager]</c> binding diagnostics.
    /// Bindings are resolved across two independent generation passes
    /// (NodeSet2 and ModelDesign); the unmatched-binding diagnostics must be
    /// reported once, after both passes, against a shared "used" set —
    /// reporting per pass false-positives a binding matched by the other pass
    /// (regression for issue #3937).
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [Parallelizable(ParallelScope.All)]
    public class NodeManagerBindingReportingTests
    {
        [Test]
        public void BindingMatchedByAnyPassIsNotReportedUnmatched()
        {
            // The binding is recorded in the shared used-set (matched by one
            // pass). Even though this reporter call also spans the other pass
            // that never matched it, it must stay silent. This is the core
            // #3937 case: a NodeSet2 model matched, a ModelDesign model did not.
            NodeManagerAttributeBinding matched = CreateBinding(namespaceUri: "urn:types");
            HashSet<NodeManagerAttributeBinding> used = [matched];
            var reports = new List<string>();

            Generators.ReportUnmatchedNodeManagerBindings(
                [matched],
                used,
                totalModelCount: 2,
                (_, message) => reports.Add(message));

            Assert.That(reports, Is.Empty);
        }

        [Test]
        public void BindingMatchedByNoPassIsReportedOnce()
        {
            NodeManagerAttributeBinding orphan = CreateBinding(namespaceUri: "urn:missing");
            var reports = new List<string>();

            Generators.ReportUnmatchedNodeManagerBindings(
                [orphan],
                [],
                totalModelCount: 2,
                (_, message) => reports.Add(message));

            Assert.That(reports, Has.Count.EqualTo(1));
            Assert.That(reports[0], Does.Contain("did not match any model"));
            Assert.That(reports[0], Does.Contain("NamespaceUri='urn:missing'"));
        }

        [Test]
        public void SelectorlessBindingWithMultipleModelsIsReportedAsAmbiguity()
        {
            NodeManagerAttributeBinding selectorless = CreateBinding();
            var reports = new List<string>();

            Generators.ReportUnmatchedNodeManagerBindings(
                [selectorless],
                [],
                totalModelCount: 2,
                (_, message) => reports.Add(message));

            Assert.That(reports, Has.Count.EqualTo(1));
            Assert.That(reports[0], Does.Contain("multiple models"));
            Assert.That(reports[0], Does.Contain("Specify NamespaceUri"));
        }

        [Test]
        public void SelectorlessBindingWithSingleModelIsReportedAsUnmatched()
        {
            // With a single model a selector-less binding is not ambiguous; it
            // simply did not match (for example the model was skipped).
            NodeManagerAttributeBinding selectorless = CreateBinding();
            var reports = new List<string>();

            Generators.ReportUnmatchedNodeManagerBindings(
                [selectorless],
                [],
                totalModelCount: 1,
                (_, message) => reports.Add(message));

            Assert.That(reports, Has.Count.EqualTo(1));
            Assert.That(reports[0], Does.Contain("did not match any model"));
            Assert.That(reports[0], Does.Contain("(no selector)"));
        }

        [Test]
        public void ReportIsNoOpWhenBindingsAreNullOrEmpty()
        {
            Assert.DoesNotThrow(() =>
                Generators.ReportUnmatchedNodeManagerBindings(
                    null, null, 2, (_, _) => Assert.Fail("should not report")));
            Assert.DoesNotThrow(() =>
                Generators.ReportUnmatchedNodeManagerBindings(
                    [], null, 2, (_, _) => Assert.Fail("should not report")));
        }

        [Test]
        public void GenerateCodeDefersUnmatchedReportWhenSharedSetProvided()
        {
            // In shared (multi-pass) mode the pass must not self-report an
            // unmatched binding; the caller reports once after all passes.
            var reports = new List<string>();
            RunDesignGenerate(
                CreateBinding(namespaceUri: "urn:not:in:test:model"),
                sharedUsedBindings: [],
                report: (_, message) => reports.Add(message));

            Assert.That(
                reports,
                Is.Empty,
                "a shared-mode pass must defer unmatched-binding reporting");
        }

        [Test]
        public void GenerateCodeReportsUnmatchedWhenNoSharedSet()
        {
            // Single-pass mode (no shared set): the pass reports directly.
            var reports = new List<string>();
            RunDesignGenerate(
                CreateBinding(namespaceUri: "urn:not:in:test:model"),
                sharedUsedBindings: null,
                report: (_, message) => reports.Add(message));

            Assert.That(reports, Has.Count.EqualTo(1));
            Assert.That(reports[0], Does.Contain("did not match any model"));
        }

        private static NodeManagerAttributeBinding CreateBinding(
            string namespaceUri = null,
            string design = null,
            string className = "TestNodeManager")
        {
            return new NodeManagerAttributeBinding
            {
                TargetNamespace = "Test",
                TargetClassName = className,
                NamespaceUri = namespaceUri,
                Design = design
            };
        }

        private static void RunDesignGenerate(
            NodeManagerAttributeBinding binding,
            HashSet<NodeManagerAttributeBinding> sharedUsedBindings,
            System.Action<NodeManagerAttributeBinding, string> report)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();
            string resources = Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources");

            new DesignFileCollection
            {
                Targets = [Path.Combine(resources, "TestModel.xml")],
                IdentifierFilePath = Path.Combine(resources, "TestModel.csv")
            }.GenerateCode(
                fileSystem,
                string.Empty,
                telemetry,
                nodeManagerBindings: [binding],
                reportBindingDiagnostic: report,
                sharedUsedBindings: sharedUsedBindings);
        }
    }
}
