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
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="ObjectMethodProxyGenerator"/> class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ObjectMethodProxyGeneratorTests
    {
        private const string kTestNamespaceUri = "http://test.org/UA/";
        private const string kTestNamespacePrefix = "Test";

        private Mock<IFileSystem> m_mockFileSystem;
        private Mock<IModelDesign> m_mockModelDesign;
        private Mock<ITelemetryContext> m_mockTelemetry;
        private Namespace m_targetNamespace;
        private GeneratorContext m_context;

        [SetUp]
        public void SetUp()
        {
            m_mockFileSystem = new Mock<IFileSystem>();
            m_mockModelDesign = new Mock<IModelDesign>();
            m_mockTelemetry = new Mock<ITelemetryContext>();

            m_targetNamespace = new Namespace
            {
                Value = kTestNamespaceUri,
                Prefix = kTestNamespacePrefix,
                Name = "TestNamespace"
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(m_targetNamespace);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([m_targetNamespace]);
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([]);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);
        }

        [Test]
        public void Constructor_NullContext_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ObjectMethodProxyGenerator(null));
        }

        [Test]
        public void Constructor_ValidContext_CreatesInstance()
        {
            GeneratorContext context = CreateContext();
            var generator = new ObjectMethodProxyGenerator(context);
            Assert.That(generator, Is.Not.Null);
        }

        [Test]
        public void Emit_NoObjectTypes_DoesNotCreateFile()
        {
            GeneratorContext context = CreateContext();
            var generator = new ObjectMethodProxyGenerator(context);

            Resource[] resources = [.. generator.Emit()];

            Assert.That(resources, Is.Empty);
            m_mockFileSystem.Verify(
                fs => fs.OpenWrite(It.IsAny<string>()),
                Times.Never);
        }

        [Test]
        public void Emit_ObjectTypeWithoutMethods_StillEmitsClass()
        {
            ObjectTypeDesign objectType = CreateObjectType("EmptyType");
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);

            using var stream = new MemoryStream();
            m_mockFileSystem
                .Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Returns(stream);

            GeneratorContext context = CreateContext();
            var generator = new ObjectMethodProxyGenerator(context);

            Resource[] resources = [.. generator.Emit()];

            Assert.That(resources, Has.Length.EqualTo(1));
            string content = Encoding.UTF8.GetString(stream.ToArray());
            // Even with no declared methods we still emit the *TypeClient
            // so derived models can inherit from it.
            Assert.That(content, Does.Contain("public partial class EmptyTypeClient"));
            Assert.That(content, Does.Contain(": global::Opc.Ua.Client.ObjectTypeClient"));
        }

        [Test]
        public void Emit_ExcludedObjectType_IsSkipped()
        {
            ObjectTypeDesign objectType = CreateObjectType(
                "ExcludedType",
                CreateMethod("DoIt"));
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);
            m_mockModelDesign
                .Setup(m => m.IsExcluded(objectType))
                .Returns(true);

            GeneratorContext context = CreateContext();
            var generator = new ObjectMethodProxyGenerator(context);

            Resource[] resources = [.. generator.Emit()];

            Assert.That(resources, Is.Empty);
            m_mockFileSystem.Verify(
                fs => fs.OpenWrite(It.IsAny<string>()),
                Times.Never);
        }

        [Test]
        public void Emit_ObjectTypeWithMethod_CreatesExpectedFile()
        {
            MethodDesign method = CreateMethod(
                "DoIt",
                inputs: [
                    CreateParameter("requestId", BasicDataType.Int32)
                ],
                outputs: [
                    CreateParameter("result", BasicDataType.NodeId)
                ]);
            ObjectTypeDesign objectType = CreateObjectType("FooType", method);
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);

            using var stream = new MemoryStream();
            string capturedPath = null;
            m_mockFileSystem
                .Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Callback<string>(p => capturedPath = p)
                .Returns(stream);

            GeneratorContext context = CreateContext();
            var generator = new ObjectMethodProxyGenerator(context);

            Resource[] resources = [.. generator.Emit()];

            Assert.That(resources, Has.Length.EqualTo(1));
            Assert.That(capturedPath, Is.Not.Null);
            Assert.That(
                capturedPath,
                Does.Contain($"{kTestNamespacePrefix}.MethodProxies.g.cs"));

            string content = Encoding.UTF8.GetString(stream.ToArray());
            Assert.That(
                content,
                Does.Contain($"namespace {kTestNamespacePrefix}"));
            Assert.That(content, Does.Contain("public partial class FooTypeClient"));
            Assert.That(
                content,
                Does.Contain(": global::Opc.Ua.Client.ObjectTypeClient"));
            Assert.That(content, Does.Contain("DoItAsync"));
            Assert.That(content, Does.Contain("int requestId"));
            Assert.That(
                content,
                Does.Contain("global::Opc.Ua.Variant.From(requestId)"));
            Assert.That(
                content,
                Does.Contain($"global::{kTestNamespacePrefix}.MethodIds.FooType_DoIt"));
            Assert.That(content, Does.Contain("await CallMethodAsync"));
            Assert.That(content, Does.Contain("ConfigureAwait(false)"));
            Assert.That(content, Does.Contain("TryGet(out global::Opc.Ua.NodeId _result"));
            Assert.That(content, Does.Contain("return _result;"));
        }

        [Test]
        public void Emit_VoidMethod_EmitsAsyncWithoutReturnType()
        {
            MethodDesign method = CreateMethod("Reset");
            ObjectTypeDesign objectType = CreateObjectType("FooType", method);
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);

            using var stream = new MemoryStream();
            m_mockFileSystem
                .Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Returns(stream);

            new ObjectMethodProxyGenerator(CreateContext()).Emit();
            string content = Encoding.UTF8.GetString(stream.ToArray());

            Assert.That(
                content,
                Does.Contain("public async global::System.Threading.Tasks.ValueTask ResetAsync("));
            Assert.That(content, Does.Contain("_ = await CallMethodAsync"));
            Assert.That(content, Does.Not.Contain("return _"));
        }

        [Test]
        public void Emit_MultiOutputMethod_EmitsValueTuple()
        {
            MethodDesign method = CreateMethod(
                "Query",
                outputs: [
                    CreateParameter("count", BasicDataType.Int32),
                    CreateParameter("name", BasicDataType.String)
                ]);
            ObjectTypeDesign objectType = CreateObjectType("FooType", method);
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);

            using var stream = new MemoryStream();
            m_mockFileSystem
                .Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Returns(stream);

            new ObjectMethodProxyGenerator(CreateContext()).Emit();
            string content = Encoding.UTF8.GetString(stream.ToArray());

            Assert.That(
                content,
                Does.Contain("ValueTask<(int count, string name)>"));
            Assert.That(content, Does.Contain("return (_count, _name);"));
        }

        [Test]
        public void Emit_CustomNamespaceOption_OverridesDefault()
        {
            MethodDesign method = CreateMethod("DoIt");
            ObjectTypeDesign objectType = CreateObjectType("FooType", method);
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);

            using var stream = new MemoryStream();
            m_mockFileSystem
                .Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Returns(stream);

            GeneratorContext context = CreateContext(new GeneratorOptions
            {
                GenerateObjectMethodProxies = true,
                ObjectMethodProxyNamespace = "My.Custom.Ns"
            });

            new ObjectMethodProxyGenerator(context).Emit();
            string content = Encoding.UTF8.GetString(stream.ToArray());

            Assert.That(content, Does.Contain("namespace My.Custom.Ns"));
        }

        [Test]
        public void Emit_StructureInput_UsesFromStructureForBoxing()
        {
            MethodDesign method = CreateMethod(
                "Submit",
                inputs: [
                    CreateParameter(
                        "payload",
                        BasicDataType.UserDefined,
                        symbolicName: "MyStruct")
                ]);
            ObjectTypeDesign objectType = CreateObjectType("FooType", method);
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);

            using var stream = new MemoryStream();
            m_mockFileSystem
                .Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Returns(stream);

            new ObjectMethodProxyGenerator(CreateContext()).Emit();
            string content = Encoding.UTF8.GetString(stream.ToArray());

            Assert.That(
                content,
                Does.Contain("global::Opc.Ua.Variant.FromStructure(payload)"));
        }

        [Test]
        public void Emit_ObjectTypeWithBaseType_EmitsInheritance()
        {
            ObjectTypeDesign baseType = CreateObjectType("FooBaseType");
            ObjectTypeDesign derived = CreateObjectType("FooType", CreateMethod("DoIt"));
            derived.BaseTypeNode = baseType;
            m_mockModelDesign
                .Setup(m => m.GetNodeDesigns())
                .Returns([baseType, derived]);

            using var stream = new MemoryStream();
            m_mockFileSystem
                .Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Returns(stream);

            new ObjectMethodProxyGenerator(CreateContext()).Emit();
            string content = Encoding.UTF8.GetString(stream.ToArray());

            Assert.That(
                content,
                Does.Contain("public partial class FooTypeClient : global::"
                    + kTestNamespacePrefix +
                    ".FooBaseTypeClient"));
        }

        [Test]
        public void Emit_MethodNameShadowsAncestor_EmitsNewModifier()
        {
            // Both base and derived declare a method called DoIt; the
            // generator must emit `public new async ... DoItAsync(...)` on
            // the derived proxy so that it hides (not overrides) the base
            // implementation.
            MethodDesign baseDoIt = CreateMethod("DoIt");
            ObjectTypeDesign baseType = CreateObjectType("FooBaseType", baseDoIt);
            MethodDesign derivedDoIt = CreateMethod(
                "DoIt",
                inputs: [CreateParameter("requestId", BasicDataType.Int32)]);
            ObjectTypeDesign derived = CreateObjectType("FooType", derivedDoIt);
            derived.BaseTypeNode = baseType;
            m_mockModelDesign
                .Setup(m => m.GetNodeDesigns())
                .Returns([baseType, derived]);

            using var stream = new MemoryStream();
            m_mockFileSystem
                .Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Returns(stream);

            new ObjectMethodProxyGenerator(CreateContext()).Emit();
            string content = Encoding.UTF8.GetString(stream.ToArray());

            // The derived class section contains the `new` modifier on the
            // shadowed method.
            Assert.That(content, Does.Contain("public new async"));
        }

        [Test]
        public void Emit_RootObjectType_DerivesFromObjectTypeClient()
        {
            // An ObjectType with no parent (BaseTypeNode == null) inherits
            // from the hand-authored `Opc.Ua.Client.ObjectTypeClient` base.
            ObjectTypeDesign rootType = CreateObjectType("RootType", CreateMethod("Ping"));
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([rootType]);

            using var stream = new MemoryStream();
            m_mockFileSystem
                .Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Returns(stream);

            new ObjectMethodProxyGenerator(CreateContext()).Emit();
            string content = Encoding.UTF8.GetString(stream.ToArray());

            Assert.That(
                content,
                Does.Contain(
                    "public partial class RootTypeClient : global::Opc.Ua.Client.ObjectTypeClient"));
        }

        private GeneratorContext CreateContext(GeneratorOptions options = null)
        {
            m_context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = "C:\\output",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = options ??
                    new GeneratorOptions
                    {
                        GenerateObjectMethodProxies = true
                    }
            };
            return m_context;
        }

        private static ObjectTypeDesign CreateObjectType(
            string name,
            params MethodDesign[] methods)
        {
            var qname = new XmlQualifiedName(name, kTestNamespaceUri);
            var type = new ObjectTypeDesign
            {
                SymbolicName = qname,
                SymbolicId = qname
            };
            if (methods != null && methods.Length > 0)
            {
                foreach (MethodDesign method in methods)
                {
                    method.SymbolicId = new XmlQualifiedName(
                        $"{name}_{method.SymbolicName.Name}",
                        kTestNamespaceUri);
                }
                type.Children = new ListOfChildren
                {
                    Items = [.. methods]
                };
            }
            return type;
        }

        private static MethodDesign CreateMethod(
            string name,
            Parameter[] inputs = null,
            Parameter[] outputs = null)
        {
            var qname = new XmlQualifiedName(name, kTestNamespaceUri);
            return new MethodDesign
            {
                SymbolicName = qname,
                SymbolicId = qname,
                InputArguments = inputs ?? [],
                OutputArguments = outputs ?? []
            };
        }

        private static Parameter CreateParameter(
            string name,
            BasicDataType basicDataType,
            string symbolicName = null,
            ValueRank valueRank = ValueRank.Scalar,
            bool isOptional = false)
        {
            string typeName = symbolicName ?? basicDataType.ToString();
            var dataType = new DataTypeDesign
            {
                SymbolicName = new XmlQualifiedName(typeName, kTestNamespaceUri),
                SymbolicId = new XmlQualifiedName(typeName, kTestNamespaceUri),
                BasicDataType = basicDataType
            };
            return new Parameter
            {
                Name = name,
                DataTypeNode = dataType,
                ValueRank = valueRank,
                IsOptional = isOptional
            };
        }
    }
}
