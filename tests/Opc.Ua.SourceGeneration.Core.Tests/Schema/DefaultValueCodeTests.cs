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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.SourceGeneration;
using Opc.Ua.Tests;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Default values are emitted as C# expressions. These tests pin the exact
    /// expression and compile and evaluate it, so an expression that does not
    /// compile or does not evaluate to the modelled value is caught here rather
    /// than in a consumer's build.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class DefaultValueCodeTests
    {
        private const string OtherUri = "http://test.org/UA/S12Other/";
        private const string TargetUri = "http://test.org/UA/S12/";

        /// <summary>
        /// Regression: a non-Good StatusCode default was emitted as
        /// "(global::Opc.Ua.StatusCode.StatusCode)BadNodeIdUnknown [0x80340000]" -
        /// a type that does not exist followed by StatusCode.ToString().
        /// </summary>
        [TestCase(0x80340000u, "new global::Opc.Ua.StatusCode(0x80340000u)")]
        [TestCase(0x80000000u, "new global::Opc.Ua.StatusCode(0x80000000u)")]
        [TestCase(0x40000000u, "new global::Opc.Ua.StatusCode(0x40000000u)")]
        public void StatusCodeDefaultIsACompilableExpression(uint code, string expected)
        {
            string expression = Scalar(BasicDataType.StatusCode, new StatusCode(code));

            Assert.That(expression, Is.EqualTo(expected));
            Assert.That(((StatusCode)Evaluate(expression)).Code, Is.EqualTo(code));
            Assert.That(
                Scalar(BasicDataType.StatusCode, new StatusCode(code), valueAsVariant: true),
                Is.EqualTo("global::Opc.Ua.Variant.From(" + expected + ")"));
        }

        /// <summary>
        /// Regression: doubles were formatted with "G", which on .NET Framework
        /// (Visual Studio) is 15 digits - double.MaxValue became an out-of-range
        /// literal and other values lost precision. The emitted digits must be the
        /// same on every runtime and evaluate to exactly the modelled value.
        /// </summary>
        [TestCase(double.MaxValue, "(double)1.7976931348623157E+308")]
        [TestCase(double.MinValue, "(double)-1.7976931348623157E+308")]
        [TestCase(double.Epsilon, "(double)4.94065645841247E-324")]
        [TestCase(0.30000000000000004, "(double)0.30000000000000004")]
        [TestCase(3.14159, "(double)3.14159")]
        [TestCase(0.0, "(double)0")]
        [TestCase(-0.0, "(double)-0.0")]
        public void DoubleDefaultRoundTrips(double value, string expected)
        {
            string expression = Scalar(BasicDataType.Double, value);

            Assert.That(expression, Is.EqualTo(expected));
            Assert.That(
                BitConverter.DoubleToInt64Bits((double)Evaluate(expression)),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(value)));
        }

        /// <summary>
        /// Floats: see <see cref="DoubleDefaultRoundTrips"/>.
        /// </summary>
        [TestCase(float.MaxValue, "(float)3.4028235E+38")]
        [TestCase(float.MinValue, "(float)-3.4028235E+38")]
        [TestCase(float.Epsilon, "(float)1.4013E-45")]
        [TestCase(0.1f, "(float)0.1")]
        [TestCase(16777217f, "(float)16777216")]
        [TestCase(-0.0f, "(float)-0.0")]
        public void FloatDefaultRoundTrips(float value, string expected)
        {
            string expression = Scalar(BasicDataType.Float, value);

            Assert.That(expression, Is.EqualTo(expected));
            Assert.That(
                BitConverter.DoubleToInt64Bits((float)Evaluate(expression)),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(value)));
        }

        /// <summary>
        /// Regression: the namespace index of a decoded NodeId was used as an
        /// index into the design's Namespaces array, which is ordered differently
        /// from the table the value was decoded with. The index now resolves
        /// through the decoding table and is looked up by URI at run time.
        /// </summary>
        [Test]
        public void NodeIdDefaultResolvesTheNamespaceOfTheDecodingTable()
        {
            var decodingTable = new NamespaceTable();
            decodingTable.Append(OtherUri);
            decodingTable.Append(TargetUri);
            // The design lists the target first - index 1 of that array is not
            // the namespace the value's index 1 names.
            Namespace[] namespaces =
            [
                new Namespace { Value = TargetUri },
                new Namespace { Value = Ua.Types.Namespaces.OpcUa },
                new Namespace { Value = OtherUri }
            ];

            string expression = new DataTypeDesign { BasicDataType = BasicDataType.NodeId }
                .GetValueAsCode(
                    ValueRank.Scalar,
                    null,
                    new NodeId(5000, 1),
                    false,
                    TargetUri,
                    namespaces,
                    new Mock<IServiceMessageContext>().Object,
                    null,
                    decodingTable,
                    "namespaceUris");

            Assert.That(
                expression,
                Is.EqualTo(
                    "global::Opc.Ua.NodeId.Parse(\"i=5000\").WithNamespaceIndex(" +
                    "namespaceUris.GetIndexOrAppend(\"" + OtherUri + "\"))"));

            var runtimeTable = new NamespaceTable();
            runtimeTable.Append("urn:unrelated");
            runtimeTable.Append(OtherUri);
            Assert.That(Evaluate(expression, runtimeTable), Is.EqualTo(new NodeId(5000, 2)));
        }

        /// <summary>
        /// Without a namespace table in scope (a structure field initializer)
        /// the NodeId can only be emitted literally - it must still compile.
        /// It used to reference a "context" local that does not exist there.
        /// </summary>
        [Test]
        public void NodeIdDefaultWithoutANamespaceTableInScopeCompiles()
        {
            string expression = Scalar(BasicDataType.NodeId, new NodeId(5000, 1));

            Assert.That(expression, Is.EqualTo("global::Opc.Ua.NodeId.Parse(\"ns=1;i=5000\")"));
            Assert.That(Evaluate(expression), Is.EqualTo(new NodeId(5000, 1)));
        }

        /// <summary>
        /// A design-authored value numbers OPC UA 0 and then the design's own
        /// namespaces in declaration order.
        /// </summary>
        [Test]
        public void DesignAuthoredIndexesUseTheDesignNamespaces()
        {
            Namespace[] namespaces =
            [
                new Namespace { Value = TargetUri },
                new Namespace { Value = Ua.Types.Namespaces.OpcUa },
                new Namespace { Value = OtherUri }
            ];

            Assert.That(
                ModelDesignExtensions.ResolveDecodedNamespaceUri(1, null, namespaces),
                Is.EqualTo(TargetUri));
            Assert.That(
                ModelDesignExtensions.ResolveDecodedNamespaceUri(2, null, namespaces),
                Is.EqualTo(OtherUri));
            Assert.That(
                ModelDesignExtensions.ResolveDecodedNamespaceUri(3, null, namespaces),
                Is.Null);
        }

        /// <summary>
        /// End to end: a NodeSet whose own namespace table lists a dependency
        /// first. The NodeId, QualifiedName and ExpandedNodeId values must name
        /// the namespaces the NodeSet meant, not whatever sits at the same index
        /// of the design's namespace list or of the server's table.
        /// </summary>
        [Test]
        public void NodeSetValuesKeepTheirNamespaces()
        {
            Dictionary<string, string> files = GenerateTwoModels();
            string[] lines = [.. files
                .Where(f => f.Key.Contains("S12.", StringComparison.Ordinal))
                .SelectMany(f => f.Value.Split('\n'))
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("state.WrappedValue", StringComparison.Ordinal))];

            Assert.That(lines, Has.Some.EqualTo(
                "state.WrappedValue = global::Opc.Ua.Variant.From(" +
                "global::Opc.Ua.NodeId.Parse(\"i=5000\").WithNamespaceIndex(" +
                "context.NamespaceUris.GetIndexOrAppend(\"" + OtherUri + "\")));"));
            Assert.That(lines, Has.Some.EqualTo(
                "state.WrappedValue = global::Opc.Ua.Variant.From(" +
                "new global::Opc.Ua.QualifiedName(\"Foo\", " +
                "context.NamespaceUris.GetIndexOrAppend(\"" + TargetUri + "\")));"));
            Assert.That(lines, Has.Some.EqualTo(
                "state.WrappedValue = global::Opc.Ua.Variant.From(" +
                "global::Opc.Ua.ExpandedNodeId.Parse(\"nsu=" + OtherUri + ";i=5000\"));"));
        }

        private static string Scalar(
            BasicDataType basicDataType,
            object value,
            bool valueAsVariant = false)
        {
            return new DataTypeDesign { BasicDataType = basicDataType }.GetValueAsCode(
                ValueRank.Scalar,
                null,
                value,
                valueAsVariant,
                TargetUri,
                [],
                new Mock<IServiceMessageContext>().Object);
        }

        /// <summary>
        /// Compiles the expression into a method and returns what it evaluates
        /// to. <c>namespaceUris</c> is in scope as a NamespaceTable parameter.
        /// </summary>
        private static object Evaluate(string expression, NamespaceTable namespaceUris = null)
        {
            string code =
                "public static class Probe { public static object Value(" +
                "global::Opc.Ua.NamespaceTable namespaceUris) => " + expression + "; }";
            CSharpCompilation compilation = OptimizationLevel.Debug
                .CreateCompilation()
                .AddCode([new KeyValuePair<string, string>("Probe.cs", code)], LanguageVersion.Latest);
            using var pe = new MemoryStream();
            EmitResult result = compilation.Emit(pe);
            Assert.That(
                result.Success,
                Is.True,
                expression + Environment.NewLine + string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            Assembly assembly = Assembly.Load(pe.ToArray());
            return assembly.GetType("Probe")
                .GetMethod("Value")
                .Invoke(null, [namespaceUris ?? new NamespaceTable()]);
        }

        private static Dictionary<string, string> GenerateTwoModels()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();
            const string otherPath = "memory://S12Other.NodeSet2.xml";
            const string targetPath = "memory://S12.NodeSet2.xml";
            fileSystem.Add(otherPath, Encoding.UTF8.GetBytes(OtherNodeSet));
            fileSystem.Add(targetPath, Encoding.UTF8.GetBytes(TargetNodeSet));
            var nodesets = new NodesetFileCollection(
                [(otherPath, new NodesetFileOptions()), (targetPath, new NodesetFileOptions())],
                [],
                fileSystem,
                telemetry);
            nodesets.GenerateCode(
                fileSystem,
                string.Empty,
                telemetry,
                new GeneratorOptions { OmitFluentApi = true });
            return fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) == ".cs")
                .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));
        }

        private const string OtherNodeSet = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                <NamespaceUris>
                    <Uri>{OtherUri}</Uri>
                </NamespaceUris>
                <Models>
                    <Model ModelUri="{OtherUri}" PublicationDate="2026-08-12T00:00:00Z" Version="1.0.0" />
                </Models>
                <Aliases>
                    <Alias Alias="HasSubtype">i=45</Alias>
                </Aliases>
                <UAObjectType NodeId="ns=1;i=5000" BrowseName="1:OtherType">
                    <DisplayName>OtherType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
            </UANodeSet>
            """;

        // The dependency is listed first, so ns=1 is OtherUri and ns=2 the target.
        private const string TargetNodeSet = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd"
                xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                <NamespaceUris>
                    <Uri>{OtherUri}</Uri>
                    <Uri>{TargetUri}</Uri>
                </NamespaceUris>
                <Models>
                    <Model ModelUri="{TargetUri}" PublicationDate="2026-08-12T00:00:00Z" Version="1.0.0">
                        <RequiredModel ModelUri="{OtherUri}" Version="1.0.0" PublicationDate="2026-08-12T00:00:00Z" />
                    </Model>
                </Models>
                <Aliases>
                    <Alias Alias="NodeId">i=17</Alias>
                    <Alias Alias="ExpandedNodeId">i=18</Alias>
                    <Alias Alias="QualifiedName">i=20</Alias>
                    <Alias Alias="HasModellingRule">i=37</Alias>
                    <Alias Alias="HasTypeDefinition">i=40</Alias>
                    <Alias Alias="HasSubtype">i=45</Alias>
                    <Alias Alias="HasProperty">i=46</Alias>
                </Aliases>
                <UAObjectType NodeId="ns=2;i=1000" BrowseName="2:S12Type">
                    <DisplayName>S12Type</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                        <Reference ReferenceType="HasProperty">ns=2;i=1001</Reference>
                        <Reference ReferenceType="HasProperty">ns=2;i=1002</Reference>
                        <Reference ReferenceType="HasProperty">ns=2;i=1003</Reference>
                    </References>
                </UAObjectType>
                <UAVariable NodeId="ns=2;i=1001" BrowseName="2:Target" ParentNodeId="ns=2;i=1000" DataType="NodeId">
                    <DisplayName>Target</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=68</Reference>
                        <Reference ReferenceType="HasModellingRule">i=78</Reference>
                    </References>
                    <Value>
                        <uax:NodeId><uax:Identifier>ns=1;i=5000</uax:Identifier></uax:NodeId>
                    </Value>
                </UAVariable>
                <UAVariable NodeId="ns=2;i=1002" BrowseName="2:Name" ParentNodeId="ns=2;i=1000" DataType="QualifiedName">
                    <DisplayName>Name</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=68</Reference>
                        <Reference ReferenceType="HasModellingRule">i=78</Reference>
                    </References>
                    <Value>
                        <uax:QualifiedName><uax:NamespaceIndex>2</uax:NamespaceIndex><uax:Name>Foo</uax:Name></uax:QualifiedName>
                    </Value>
                </UAVariable>
                <UAVariable NodeId="ns=2;i=1003" BrowseName="2:Expanded" ParentNodeId="ns=2;i=1000" DataType="ExpandedNodeId">
                    <DisplayName>Expanded</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=68</Reference>
                        <Reference ReferenceType="HasModellingRule">i=78</Reference>
                    </References>
                    <Value>
                        <uax:ExpandedNodeId><uax:Identifier>ns=1;i=5000</uax:Identifier></uax:ExpandedNodeId>
                    </Value>
                </UAVariable>
            </UANodeSet>
            """;
    }
}
