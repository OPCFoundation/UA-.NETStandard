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

using System.Linq;
using System.Text;
using System.Xml;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Keeps foreign model helper identities distinct when public types share the Core code namespace.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [Parallelizable(ParallelScope.All)]
    public sealed class CoreNamespaceCoGenerationTests
    {
        /// <summary>
        /// Core and ordinary companion-model names remain stable; a Core-code-namespace extension is qualified.
        /// </summary>
        [TestCase("http://opcfoundation.org/UA/", "Opc.Ua", "OpcUa", "global::Opc.Ua.Namespaces.OpcUa")]
        [TestCase("urn:extension", "Opc.Ua.Extension", "Extension",
            "global::Opc.Ua.Extension.Namespaces.Extension")]
        [TestCase("urn:extension", "Opc.Ua", "Extension", "global::Opc.Ua.ExtensionNamespaces.Extension")]
        public void NamespaceConstantsKeepModelsDistinct(
            string uri, string prefix, string name, string expected)
        {
            Namespace[] namespaces = [new Namespace { Value = uri, Prefix = prefix, Name = name }];
            Assert.That(namespaces.GetConstantSymbolForNamespace(uri), Is.EqualTo(expected));
            Assert.That(namespaces.GetNamespacePrefix(uri), Is.EqualTo(prefix));
        }

        /// <summary>
        /// XML namespace references use the same model-scoped constants as their declarations.
        /// </summary>
        [TestCase(false, "Opc.Ua.ExtensionNamespaces.Extension")]
        [TestCase(true, "Opc.Ua.ExtensionNamespaces.ExtensionXsd")]
        public void XmlNamespaceConstantsUseTheSameModelScope(bool separateXmlNamespace, string expected)
        {
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Value = "urn:extension",
                    Prefix = "Opc.Ua",
                    Name = "Extension",
                    XmlNamespace = separateXmlNamespace ? "urn:extension:xml" : "urn:extension"
                }
            ];
            Assert.That(namespaces.GetConstantForXmlNamespace("urn:extension"), Is.EqualTo(expected));
        }

        /// <summary>
        /// The source namespace URI is kept even when its local numeric identifier overlaps Core.
        /// </summary>
        [Test]
        public void ExpandedIdentityUsesTheForeignNamespaceConstant()
        {
            var model = new Namespace { Value = "urn:extension", Prefix = "Opc.Ua", Name = "Extension" };
            var node = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("ExtensionObjectType", model.Value),
                SymbolicId = new XmlQualifiedName("ExtensionObjectType", model.Value),
                NumericId = 1,
                NumericIdSpecified = true
            };
            Assert.That(node.GetExpandedNodeIdAsCode([model]),
                Is.EqualTo("new global::Opc.Ua.ExpandedNodeId(1u, global::Opc.Ua.ExtensionNamespaces.Extension)"));
        }

        /// <summary>
        /// Each helper table has its own declarations and backing dictionaries without moving public types.
        /// </summary>
        [Test]
        public void ConstantsAndIdentifierHelpersAreModelScoped()
        {
            using var files = new VirtualFileSystem();
            var model = new Namespace { Value = "urn:extension", Prefix = "Opc.Ua", Name = "Extension" };
            var node = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("ExtensionObjectType", model.Value),
                SymbolicId = new XmlQualifiedName("ExtensionObjectType", model.Value),
                BrowseName = "ExtensionObjectType",
                NumericId = 1,
                NumericIdSpecified = true
            };
            var design = new Mock<IModelDesign>();
            design.Setup(value => value.TargetNamespace).Returns(model);
            design.Setup(value => value.Namespaces).Returns([model]);
            design.Setup(value => value.Nodes).Returns([node]);
            design.Setup(value => value.GetNodeDesigns()).Returns([node]);
            var context = new GeneratorContext
            {
                FileSystem = files,
                OutputFolder = "output",
                ModelDesign = design.Object,
                Telemetry = NUnitTelemetryContext.Create(),
                Options = new GeneratorOptions()
            };
            _ = new ConstantsGenerator(context).Emit().ToArray();
            _ = new NodeIdGenerator(context).Emit().ToArray();
            string constantsPath = files.CreatedFiles.Single(path => path.EndsWith(".Constants.g.cs",
                System.StringComparison.Ordinal));
            string identifiersPath = files.CreatedFiles.Single(path => path.EndsWith(".Identifiers.g.cs",
                System.StringComparison.Ordinal));
            string constants = Encoding.UTF8.GetString(files.Get(constantsPath));
            string identifiers = Encoding.UTF8.GetString(files.Get(identifiersPath));

            Assert.That(constantsPath, Does.EndWith("Opc.Ua.Extension.Constants.g.cs"));
            Assert.That(identifiersPath, Does.EndWith("Opc.Ua.Extension.Identifiers.g.cs"));
            Assert.That(constants, Does.Contain("namespace Opc.Ua"));
            Assert.That(constants, Does.Contain("class ExtensionBrowseNames"));
            Assert.That(constants, Does.Contain("class ExtensionNamespaces"));
            Assert.That(constants, Does.Contain("class ExtensionModelVersions"));
            Assert.That(identifiers, Does.Contain("class ExtensionObjectTypes"));
            Assert.That(identifiers, Does.Contain("class ExtensionObjectTypeIds"));
            Assert.That(identifiers, Does.Contain("global::Opc.Ua.ExtensionObjectTypes.ExtensionObjectType"));
            Assert.That(identifiers, Does.Contain("global::Opc.Ua.ExtensionNamespaces.Extension"));
        }
    }
}
