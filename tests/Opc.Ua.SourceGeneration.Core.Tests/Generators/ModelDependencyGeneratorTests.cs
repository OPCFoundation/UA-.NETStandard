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
using System.Xml;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;
using Opc.Ua.SourceGeneration.Dependency;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="ModelDependencyGenerator"/> templating output.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ModelDependencyGeneratorTests
    {
        private const string TestUri = "http://test.org/UA/";
        private const string TestPrefix = "Test";

        private Mock<IFileSystem> m_mockFileSystem;
        private Mock<IModelDesign> m_mockModelDesign;
        private Mock<ITelemetryContext> m_mockTelemetry;
        private MemoryStream m_memoryStream;
        private string m_capturedPath;

        [SetUp]
        public void SetUp()
        {
            m_mockFileSystem = new Mock<IFileSystem>();
            m_mockModelDesign = new Mock<IModelDesign>();
            m_mockTelemetry = new Mock<ITelemetryContext>();
            m_memoryStream = new MemoryStream();
            m_capturedPath = null;

            m_mockFileSystem.Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Callback<string>(path => m_capturedPath = path)
                .Returns(m_memoryStream);
        }

        [TearDown]
        public void TearDown()
        {
            m_memoryStream?.Dispose();
        }

        [Test]
        public void Constructor_NullContext_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ModelDependencyGenerator(null));
        }

        [Test]
        public void Emit_NoTargetNamespace_ReturnsEmptyAndDoesNotOpenFile()
        {
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns((Namespace)null);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([]);

            var generator = new ModelDependencyGenerator(BuildContext());

            IEnumerable<Resource> result = generator.Emit();

            Assert.That(result, Is.Empty);
            m_mockFileSystem.Verify(fs => fs.OpenWrite(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Emit_TargetNamespaceWithoutPrefix_ReturnsEmpty()
        {
            var target = new Namespace { Value = TestUri, Prefix = null };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(target);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([target]);

            var generator = new ModelDependencyGenerator(BuildContext());

            Assert.That(generator.Emit(), Is.Empty);
            m_mockFileSystem.Verify(fs => fs.OpenWrite(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Emit_SelfOnly_WritesOneAttributeUnderExpectedFileName()
        {
            ConfigureSelf("1.05.04", "2024-05-01T00:00:00Z");

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            string output = ReadOutput();
            Assert.That(m_capturedPath, Does.Contain("Test.ModelDependencies.g.cs"));
            Assert.That(
                output,
                Does.Contain("[assembly: global::Opc.Ua.ModelDependencyAttribute("));
            Assert.That(output, Does.Contain("\"http://test.org/UA/\""));
            Assert.That(output, Does.Contain("\"Test\""));
            Assert.That(output, Does.Contain("\"1.05.04\""));
            Assert.That(output, Does.Contain("\"2024-05-01T00:00:00Z\""));
        }

        [Test]
        public void Emit_NullVersionAndDate_RendersBareNullLiterals()
        {
            ConfigureSelf(version: null, publicationDate: null);

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            string output = ReadOutput();
            Assert.That(
                output,
                Does.Contain("[assembly: global::Opc.Ua.ModelDependencyAttribute("));
            Assert.That(output, Does.Contain("\"http://test.org/UA/\""));
            Assert.That(output, Does.Contain("\"Test\""));
            // Version and publication date render as bare 'null' literals
            // (without quotes) on their own line.
            Assert.That(output, Does.Match(@"\bnull\b"));
            // Defensive: ensure we never emit the literal string "null".
            Assert.That(output, Does.Not.Contain("\"null\""));
        }

        [Test]
        public void Emit_OpcUaRootInDeclaredNamespaces_IsSkipped()
        {
            Namespace target = ConfigureSelf();
            var opcUa = new Namespace
            {
                Value = Types.Namespaces.OpcUa,
                Prefix = "Opc.Ua",
                Name = "OpcUa"
            };
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([target, opcUa]);

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            string output = ReadOutput();
            Assert.That(output, Does.Not.Contain(Types.Namespaces.OpcUa));
            Assert.That(output, Does.Contain(TestUri));
        }

        [Test]
        public void Emit_DeclaredAndReferencedDeps_DedupesAndPreservesOrder()
        {
            Namespace target = ConfigureSelf();
            var declared = new Namespace
            {
                Value = "http://example.org/UA/Declared/",
                Prefix = "Example.Declared",
                Name = "Declared",
                Version = "1.0",
                PublicationDate = "2024-01-01T00:00:00Z"
            };
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([target, declared]);

            var referenced = new Dictionary<string, ModelDependencyReference>
            {
                // Duplicate of declared — must be skipped.
                ["http://example.org/UA/Declared/"] = new ModelDependencyReference(
                    "ExampleAssembly",
                    "http://example.org/UA/Declared/",
                    "Example.Declared",
                    "1.0",
                    "2024-01-01T00:00:00Z"),
                // Unique — must be emitted last.
                ["http://example.org/UA/Referenced/"] = new ModelDependencyReference(
                    "ExampleAssembly",
                    "http://example.org/UA/Referenced/",
                    "Example.Referenced",
                    "2.0",
                    "2024-06-01T00:00:00Z")
            };

            var generator = new ModelDependencyGenerator(BuildContext(referenced));
            generator.Emit();

            string output = ReadOutput();
            int selfIdx = output.IndexOf(TestUri, StringComparison.Ordinal);
            int declaredIdx = output.IndexOf("Declared/", StringComparison.Ordinal);
            int referencedIdx = output.IndexOf("Referenced/", StringComparison.Ordinal);
            Assert.Multiple(() =>
            {
                Assert.That(selfIdx, Is.GreaterThan(0), "self entry missing");
                Assert.That(declaredIdx, Is.GreaterThan(selfIdx), "declared after self");
                Assert.That(referencedIdx, Is.GreaterThan(declaredIdx), "referenced after declared");
                int firstDeclared = output.IndexOf(
                    "\"http://example.org/UA/Declared/\"",
                    StringComparison.Ordinal);
                int lastDeclared = output.LastIndexOf(
                    "\"http://example.org/UA/Declared/\"",
                    StringComparison.Ordinal);
                Assert.That(firstDeclared, Is.EqualTo(lastDeclared),
                    "Declared dependency must be emitted exactly once");
            });
        }

        [Test]
        public void Emit_OutputContainsSharedCodeHeaderBanner()
        {
            ConfigureSelf();

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            string output = ReadOutput();
            Assert.Multiple(() =>
            {
                Assert.That(output, Does.StartWith("// <auto-generated />"));
                Assert.That(output, Does.Contain("OPC Foundation MIT License"));
                Assert.That(output, Does.Contain("#nullable enable annotations"));
            });
        }

        [Test]
        public void Emit_QuoteCharacterInValue_IsEscaped()
        {
            var target = new Namespace
            {
                Value = "http://test.org/UA/",
                Prefix = "Test",
                Name = "Test",
                Version = "v\"1\"",
                PublicationDate = null
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(target);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([target]);
            m_mockModelDesign.Setup(m => m.TargetVersion).Returns((string)null);
            m_mockModelDesign.Setup(m => m.TargetPublicationDate).Returns((DateTime?)null);

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            string output = ReadOutput();
            Assert.That(output, Does.Contain("\"v\\\"1\\\"\""));
        }

        [Test]
        public void EmitDeclarationBackedMethodSerializesEffectiveArguments()
        {
            Namespace target = ConfigureSelf();
            const string opcUaNamespace = Types.Namespaces.OpcUa;
            var declaration = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("ExecuteMethodType", TestUri),
                SymbolicName = new XmlQualifiedName("ExecuteMethodType", TestUri),
                NumericId = 42,
                NumericIdSpecified = true,
                InputArguments =
                [
                    new Parameter
                    {
                        Name = "Name",
                        DataType = new XmlQualifiedName("String", opcUaNamespace),
                        ValueRank = ValueRank.Scalar
                    }
                ],
                OutputArguments =
                [
                    new Parameter
                    {
                        Name = "Status",
                        DataType = new XmlQualifiedName("Int16", opcUaNamespace),
                        ValueRank = ValueRank.Scalar
                    }
                ]
            };
            var method = new MethodDesign
            {
                BrowseName = "Execute",
                SymbolicId = new XmlQualifiedName("ControllerType_Execute", TestUri),
                SymbolicName = new XmlQualifiedName("Execute", TestUri),
                MethodDeclarationNode = declaration,
                InputArguments = [],
                OutputArguments = []
            };
            var objectType = new ObjectTypeDesign
            {
                ClassName = "Controller",
                SymbolicId = new XmlQualifiedName("ControllerType", TestUri),
                SymbolicName = new XmlQualifiedName("ControllerType", TestUri),
                HasChildren = true,
                Children = new ListOfChildren { Items = [method] }
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(target);
            m_mockModelDesign.Setup(m => m.Nodes).Returns([objectType]);

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            ModelDependencyV1 payload = ReadSelfPayload();
            Assert.That(payload, Is.Not.Null);
            DependencyChild child = payload.Nodes
                .Single(node => node.SymbolicName == "ControllerType")
                .Children
                .Single(candidate => candidate.SymbolicName == "Execute");

            Assert.Multiple(() =>
            {
                Assert.That(child.InputArguments, Has.Count.EqualTo(1));
                Assert.That(child.InputArguments[0].Name, Is.EqualTo("Name"));
                Assert.That(child.InputArguments[0].DataTypeName, Is.EqualTo("String"));
                Assert.That(child.OutputArguments, Has.Count.EqualTo(1));
                Assert.That(child.OutputArguments[0].Name, Is.EqualTo("Status"));
                Assert.That(child.OutputArguments[0].DataTypeName, Is.EqualTo("Int16"));
                Assert.That(child.MethodStateName, Is.EqualTo("ExecuteMethodType"));
                Assert.That(child.MethodStateNamespace, Is.EqualTo(TestUri));
                Assert.That(child.MethodDeclarationName, Is.EqualTo("ExecuteMethodType"));
                Assert.That(child.MethodDeclarationNamespace, Is.EqualTo(TestUri));
                Assert.That(child.MethodDeclarationNumericId, Is.EqualTo(42));
            });
        }

        [Test]
        public void EmitVariableSerializesEffectiveMetadata()
        {
            Namespace target = ConfigureSelf();
            var document = new XmlDocument();
            System.Xml.XmlElement defaultValue = document.CreateElement(
                "uax",
                "ListOfString",
                Types.Namespaces.OpcUaXsd);
            System.Xml.XmlElement first = document.CreateElement(
                "uax",
                "String",
                Types.Namespaces.OpcUaXsd);
            first.InnerText = "First";
            defaultValue.AppendChild(first);
            System.Xml.XmlElement second = document.CreateElement(
                "uax",
                "String",
                Types.Namespaces.OpcUaXsd);
            second.InnerText = "Second";
            defaultValue.AppendChild(second);
            var variable = new VariableDesign
            {
                BrowseName = "Values",
                SymbolicId = new XmlQualifiedName("ControllerType_Values", TestUri),
                SymbolicName = new XmlQualifiedName("Values", TestUri),
                TypeDefinition = new XmlQualifiedName(
                    "BaseDataVariableType",
                    Types.Namespaces.OpcUa),
                DataType = new XmlQualifiedName("String", Types.Namespaces.OpcUa),
                ValueRank = ValueRank.Array,
                ValueRankSpecified = true,
                AccessLevel = AccessLevel.ReadWrite,
                AccessLevelSpecified = true,
                RawAccessLevel = 5,
                RawUserAccessLevel = 1,
                MinimumSamplingInterval = 250,
                MinimumSamplingIntervalSpecified = true,
                Historizing = true,
                HistorizingSpecified = true,
                DefaultValue = defaultValue
            };
            var objectType = new ObjectTypeDesign
            {
                ClassName = "Controller",
                SymbolicId = new XmlQualifiedName("ControllerType", TestUri),
                SymbolicName = new XmlQualifiedName("ControllerType", TestUri),
                HasChildren = true,
                Children = new ListOfChildren { Items = [variable] }
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(target);
            m_mockModelDesign.Setup(m => m.Nodes).Returns([objectType]);

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            ModelDependencyV1 payload = ReadSelfPayload();
            Assert.That(payload, Is.Not.Null);
            DependencyChild child = payload.Nodes
                .Single(node => node.SymbolicName == "ControllerType")
                .Children
                .Single(candidate => candidate.SymbolicName == "Values");

            Assert.Multiple(() =>
            {
                Assert.That(child.AccessLevel, Is.EqualTo((byte)AccessLevel.ReadWrite));
                Assert.That(child.AccessLevelSpecified, Is.True);
                Assert.That(child.RawAccessLevel, Is.EqualTo(5u));
                Assert.That(child.RawUserAccessLevel, Is.EqualTo(1u));
                Assert.That(child.MinimumSamplingInterval, Is.EqualTo(250));
                Assert.That(child.MinimumSamplingIntervalSpecified, Is.True);
                Assert.That(child.Historizing, Is.True);
                Assert.That(child.HistorizingSpecified, Is.True);
                Assert.That(child.DefaultValueXml, Does.Contain("First"));
                Assert.That(child.DefaultValueXml, Does.Contain("Second"));
            });
        }

        [Test]
        public void PayloadWithoutMethodIdentityRemainsReadable()
        {
            var payload = new ModelDependencyV1
            {
                ModelUri = TestUri,
                FluentAccessorsEmitted = false
            };
            payload.Nodes.Add(new DependencyNode
            {
                SymbolicName = "ControllerType",
                SymbolicNamespace = TestUri,
                ClassName = "Controller",
                Kind = DependencyNodeKind.ObjectType,
                Children =
                [
                    new DependencyChild
                    {
                        BrowseName = "Execute",
                        SymbolicName = "Execute",
                        InstanceKind = 4,
                        InputArguments =
                        [
                            new DependencyMethodArg(
                                "Name",
                                "String",
                                Types.Namespaces.OpcUa,
                                (int)ValueRank.Scalar)
                        ]
                    }
                ]
            });

            ModelDependencyV1 decoded =
                ModelDependencyV1.FromBase64Payload(payload.ToBase64Payload());

            Assert.That(decoded, Is.Not.Null);
            DependencyChild child = decoded.Nodes.Single().Children.Single();
            Assert.Multiple(() =>
            {
                Assert.That(decoded.FluentAccessorsEmitted, Is.False);
                Assert.That(child.InputArguments, Has.Count.EqualTo(1));
                Assert.That(child.MethodStateName, Is.Empty);
                Assert.That(child.MethodDeclarationName, Is.Empty);
            });
        }

        private Namespace ConfigureSelf(
            string version = "1.05.04",
            string publicationDate = "2024-05-01T00:00:00Z")
        {
            var target = new Namespace
            {
                Value = TestUri,
                Prefix = TestPrefix,
                Name = "Test",
                Version = version,
                PublicationDate = publicationDate
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(target);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([target]);
            m_mockModelDesign.Setup(m => m.TargetVersion).Returns((string)null);
            m_mockModelDesign.Setup(m => m.TargetPublicationDate).Returns((DateTime?)null);
            return target;
        }

        private GeneratorContext BuildContext(
            IReadOnlyDictionary<string, ModelDependencyReference> referencedModels = null)
        {
            var context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = "C:\\output",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };
            if (referencedModels != null)
            {
                context = context with { ReferencedModels = referencedModels };
            }
            return context;
        }

        private string ReadOutput()
        {
            // The generator wraps the captured stream in a StreamWriter and
            // disposes it on Emit(); the stream's Position is at the end.
            return Encoding.UTF8.GetString(m_memoryStream.ToArray());
        }

        private ModelDependencyV1 ReadSelfPayload()
        {
            string output = ReadOutput();
            int payloadEnd = output.IndexOf("\")]", StringComparison.Ordinal);
            Assert.That(payloadEnd, Is.GreaterThanOrEqualTo(0));
            int payloadStart = output.LastIndexOf('"', payloadEnd - 1);
            Assert.That(payloadStart, Is.GreaterThanOrEqualTo(0));
            string encodedPayload = output[(payloadStart + 1)..payloadEnd];
            return ModelDependencyV1.FromBase64Payload(encodedPayload);
        }
    }
}
