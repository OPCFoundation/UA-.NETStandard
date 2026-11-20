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
using System.Linq;
using System.Text;
using System.Xml;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="AggregateFunctionIdsGenerator"/>. The output
    /// compiled against the real core model is covered by the server's
    /// AggregatorsTests, which use the emitted <c>AggregateFunctionIds.All</c>.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AggregateFunctionIdsGeneratorTests
    {
        private const string kUaNamespaceUri = "http://opcfoundation.org/UA/";

        [Test]
        public void Constructor_NullContext_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AggregateFunctionIdsGenerator(null!));
        }

        [Test]
        public void Emit_NonCoreModel_ReturnsEmpty()
        {
            var mockFileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            GeneratorContext context = CreateContext(
                "http://test.org/UA/",
                "Test",
                mockFileSystem.Object,
                CreateAggregateFunction("AggregateFunction_Custom", "Custom", "AggregateFunctionType"));

            var resources = new AggregateFunctionIdsGenerator(context).Emit().ToList();

            Assert.That(resources, Is.Empty);
            mockFileSystem.VerifyNoOtherCalls();
        }

        [Test]
        public void Emit_CoreModelWithoutAggregateFunctions_ReturnsEmpty()
        {
            var mockFileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            GeneratorContext context = CreateContext(
                kUaNamespaceUri,
                "Opc.Ua",
                mockFileSystem.Object,
                CreateAggregateFunction("Server", "Server", "ServerType"));

            var resources = new AggregateFunctionIdsGenerator(context).Emit().ToList();

            Assert.That(resources, Is.Empty);
            mockFileSystem.VerifyNoOtherCalls();
        }

        [Test]
        public void Emit_CoreModel_ListsAggregateFunctionsInModelOrder()
        {
            using var fileSystem = new VirtualFileSystem();
            GeneratorContext context = CreateContext(
                kUaNamespaceUri,
                "Opc.Ua",
                fileSystem,
                CreateAggregateFunction("AggregateFunction_Count", "Count", "AggregateFunctionType"),
                CreateAggregateFunction("Server", "Server", "ServerType"),
                CreateAggregateFunction("AggregateFunction_AnnotationCount", "AnnotationCount", "AggregateFunctionType"));

            var resources = new AggregateFunctionIdsGenerator(context).Emit().ToList();

            Assert.That(resources, Has.Count.EqualTo(1));
            string output = Encoding.UTF8.GetString(fileSystem.Get(
                System.IO.Path.Combine("out", "Opc.Ua.AggregateFunctionIds.g.cs")));
            const string count =
                "(global::Opc.Ua.ObjectIds.AggregateFunction_Count, global::Opc.Ua.BrowseNames.AggregateFunction_Count),";
            const string annotationCount =
                "(global::Opc.Ua.ObjectIds.AggregateFunction_AnnotationCount, global::Opc.Ua.BrowseNames.AggregateFunction_AnnotationCount),";
            Assert.That(output, Does.Contain("public static partial class AggregateFunctionIds"));
            Assert.That(output, Does.Contain(count));
            Assert.That(output, Does.Contain(annotationCount));
            Assert.That(output, Does.Not.Contain("ObjectIds.Server"));
            Assert.That(
                output.IndexOf(count, StringComparison.Ordinal),
                Is.LessThan(output.IndexOf(annotationCount, StringComparison.Ordinal)));
        }

        [Test]
        public void Emit_InstanceOfAggregateFunctionSubtype_IsListed()
        {
            using var fileSystem = new VirtualFileSystem();
            var aggregateFunctionType = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("AggregateFunctionType", kUaNamespaceUri),
                SymbolicName = new XmlQualifiedName("AggregateFunctionType", kUaNamespaceUri)
            };
            var subtype = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("CustomAggregateFunctionType", kUaNamespaceUri),
                SymbolicName = new XmlQualifiedName("CustomAggregateFunctionType", kUaNamespaceUri),
                BaseTypeNode = aggregateFunctionType
            };
            ObjectDesign custom = CreateAggregateFunction(
                "AggregateFunction_Custom", "Custom", "CustomAggregateFunctionType");
            custom.TypeDefinitionNode = subtype;
            GeneratorContext context = CreateContext(kUaNamespaceUri, "Opc.Ua", fileSystem, custom);

            var resources = new AggregateFunctionIdsGenerator(context).Emit().ToList();

            Assert.That(resources, Has.Count.EqualTo(1));
            string output = Encoding.UTF8.GetString(fileSystem.Get(
                System.IO.Path.Combine("out", "Opc.Ua.AggregateFunctionIds.g.cs")));
            Assert.That(output, Does.Contain("global::Opc.Ua.ObjectIds.AggregateFunction_Custom"));
        }

        private static GeneratorContext CreateContext(
            string namespaceUri,
            string prefix,
            IFileSystem fileSystem,
            params NodeDesign[] nodes)
        {
            var targetNamespace = new Namespace
            {
                Value = namespaceUri,
                Prefix = prefix,
                Name = "TestNamespace"
            };

            var mockModelDesign = new Mock<IModelDesign>();
            mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns(nodes);
            mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);

            return new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = "out",
                ModelDesign = mockModelDesign.Object,
                Telemetry = new Mock<ITelemetryContext>().Object,
                Options = new GeneratorOptions()
            };
        }

        private static ObjectDesign CreateAggregateFunction(
            string symbolicName,
            string browseName,
            string typeDefinition)
        {
            return new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName(symbolicName, kUaNamespaceUri),
                SymbolicName = new XmlQualifiedName(symbolicName, kUaNamespaceUri),
                BrowseName = browseName,
                TypeDefinition = new XmlQualifiedName(typeDefinition, kUaNamespaceUri)
            };
        }
    }
}
