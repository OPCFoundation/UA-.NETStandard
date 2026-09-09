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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.SourceGeneration.Dependency;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// A model normally belongs to exactly one assembly: whoever emits its
    /// types. That leaves a node manager for it homeless whenever the
    /// assembly that owns the model cannot reference
    /// <c>Opc.Ua.Server</c> — a model-only package shared with clients, for
    /// instance. Binding a <c>[NodeManager]</c> to a model a reference
    /// already supplies is the way out: the manager and its fluent surface
    /// are emitted in the server-side assembly while the model types keep
    /// coming from the reference.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    public class NodeManagerBindingToReferencedModelTests
    {
        private const string ModelUri = "urn:opcfoundation.org:2024-01:TestModel";
        private const string Prefix = "TestModel";

        [Test]
        public void ABoundManagerIsEmittedForAModelAReferenceSupplies()
        {
            Dictionary<string, string> files = Generate(WithBinding(), out _);

            Assert.That(files.Keys, Has.Some.EndsWith(".NodeManager.g.cs"),
                "The bound manager must be emitted even though the model is not");

            string mgr = files.Single(
                kv => kv.Key.EndsWith(".NodeManager.g.cs", StringComparison.Ordinal)).Value;
            Assert.That(mgr, Does.Contain("namespace Consumer.Managers"));
            Assert.That(mgr, Does.Contain("class BoundNodeManager"));

            // The predefined nodes come from the referenced assembly's
            // model composer — the whole point of the mode.
            Assert.That(mgr, Does.Contain("LoadPredefinedNodesAsync"));
            Assert.That(mgr, Does.Match(
                @"global::TestModel\.TestModelExtensions\.AddTestModel\("));
        }

        [Test]
        public void TheModelTypesAreNotReEmitted()
        {
            Dictionary<string, string> files = Generate(WithBinding(), out _);

            // Everything that would collide with the referenced assembly.
            Assert.That(files.Keys, Has.None.EndsWith(".NodeStates.g.cs"));
            Assert.That(files.Keys, Has.None.EndsWith(".NodeStates.ex.g.cs"));
            Assert.That(files.Keys, Has.None.EndsWith(".Constants.g.cs"));
            Assert.That(files.Keys, Has.None.EndsWith(".DataTypes.g.cs"));
            Assert.That(files.Keys, Has.None.EndsWith(".Identifiers.g.cs"));
            Assert.That(files.Keys, Has.None.EndsWith(".EventRecords.g.cs"));
        }

        [Test]
        public void TheFluentSurfaceIsEmittedAlongsideTheManager()
        {
            Dictionary<string, string> files = Generate(WithBinding(), out _);

            Assert.That(files.Keys, Has.Some.EndsWith(".FluentBuilders.g.cs"),
                "The typed builder the manager's Configure overload takes " +
                "has to be emitted with it");

            string builders = files.Single(
                kv => kv.Key.EndsWith(".FluentBuilders.g.cs", StringComparison.Ordinal)).Value;
            Assert.That(builders, Does.Contain("IBoundNodeManagerBuilder"),
                "The typed builder must be named after the bound manager");
        }

        [Test]
        public void TheBindingIsNotReportedAsUnmatched()
        {
            Generate(WithBinding(), out List<string> diagnostics);

            Assert.That(diagnostics, Is.Empty,
                "A binding that produced a manager must not be reported unmatched");
        }

        [Test]
        public void WithoutABindingTheReferencedModelIsStillSkipped()
        {
            Dictionary<string, string> files = Generate(bindings: [], out _);

            Assert.That(files, Is.Empty,
                "A model a reference supplies and nothing binds to stays skipped");
        }

        /// <summary>
        /// The skip is what stops duplicate type emission, so it must
        /// survive: a binding for a <em>different</em> model may not drag
        /// this one into generation.
        /// </summary>
        [Test]
        public void ABindingForAnotherModelDoesNotUnlockThisOne()
        {
            var foreign = new NodeManagerAttributeBinding
            {
                TargetNamespace = "Consumer.Managers",
                TargetClassName = "ForeignNodeManager",
                NamespaceUri = "urn:example.org:SomeOtherModel"
            };

            Dictionary<string, string> files = Generate([foreign], out List<string> diagnostics);

            Assert.That(files, Is.Empty);
            Assert.That(diagnostics, Has.Count.EqualTo(1),
                "The unmatched binding must still be reported");
        }

        private static IReadOnlyList<NodeManagerAttributeBinding> WithBinding()
        {
            return
            [
                new NodeManagerAttributeBinding
                {
                    TargetNamespace = "Consumer.Managers",
                    TargetClassName = "BoundNodeManager",
                    NamespaceUri = ModelUri,
                    GenerateFactory = false
                }
            ];
        }

        private static Dictionary<string, string> Generate(
            IReadOnlyList<NodeManagerAttributeBinding> bindings,
            out List<string> diagnostics)
        {
            const string designFile = "TestModel.xml";
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();
            string resources = Path.Combine(Directory.GetCurrentDirectory(), "Resources");

            var captured = new List<string>();

            // A referenced assembly that supplies the model types but no
            // fluent accessors — the shape of a model-only package.
            var dependency = new ModelDependencyV1
            {
                ModelUri = ModelUri,
                FluentAccessorsEmitted = false
            };
            var referenced = new ModelDependencyReference(
                "Referenced.Model",
                ModelUri,
                Prefix,
                "1.0",
                "2026-01-01",
                payload: dependency.ToBase64Payload());

            Generators.GenerateCode(
                new DesignFileCollection
                {
                    Targets = [Path.Combine(resources, designFile)],
                    IdentifierFilePath = Path.Combine(
                        resources,
                        Path.GetFileNameWithoutExtension(designFile) + ".csv")
                },
                fileSystem,
                string.Empty,
                telemetry,
                options: null,
                useAllowSubtypes: false,
                identifierFiles: null,
                referencedModels: new Dictionary<string, ModelDependencyReference>(StringComparer.Ordinal)
                {
                    [ModelUri] = referenced
                },
                nodeManagerBindings: bindings,
                reportBindingDiagnostic: (_, message) => captured.Add(message));

            diagnostics = captured;
            return fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) == ".cs")
                .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));
        }
    }
}
