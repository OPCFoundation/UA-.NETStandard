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
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.SourceGeneration;
using Opc.Ua.Tests;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// A NodeSet2 Variable or VariableType without a Value element has a
    /// null Value (OPC 10000-6 F.5). The generator used to initialize such
    /// a node with the default value of its DataType ((int)0, (double)0),
    /// so a server built from the NodeSet reported a value the model never
    /// declared.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class NodeSetValueAbsentTests
    {
        private const string NullValue = "baseState.WrappedValue = global::Opc.Ua.Variant.Null;";

        [TestCase("CreatePump1_RevisionCounter", "(int)0")]
        [TestCase("CreatePump1_RatedFlow", "(double)0")]
        [TestCase("CreatePumpType_RatedFlow", "(double)0")]
        public void VariableWithoutValueIsNull(string method, string dataTypeDefault)
        {
            string body = ExtractMethodBody(GenerateDeviceSetPump(), method);

            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Contain(NullValue));
                Assert.That(body, Does.Not.Contain(dataTypeDefault));
            });
        }

        [Test]
        public void VariableWithValueKeepsIt()
        {
            string body = ExtractMethodBody(GenerateDeviceSetPump(), "CreatePump1_SerialNumber");

            Assert.That(
                body,
                Does.Contain("baseState.WrappedValue = global::Opc.Ua.Variant.From(\"SN-0001\");"));
        }

        [Test]
        public void VariableTypeWithoutValueIsNull()
        {
            string body = ExtractMethodBody(GenerateDeviceSetPump(), "CreateFlowVariableType");

            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Contain(NullValue));
                Assert.That(body, Does.Not.Contain("(double)0"));
            });
        }

        private static string GenerateDeviceSetPump()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();
            string resources = Path.Combine(Directory.GetCurrentDirectory(), "Resources");
            const string flowTypePath = "memory://FlowVariableType.NodeSet2.xml";
            fileSystem.Add(flowTypePath, Encoding.UTF8.GetBytes(FlowVariableTypeNodeSet));
            var nodesets = new NodesetFileCollection(
                [
                    (Path.Combine(resources, "Opc.Ua.Di.NodeSet2.xml"), new NodesetFileOptions()),
                    (Path.Combine(resources, "DeviceSetPump.NodeSet2.xml"), new NodesetFileOptions()),
                    (flowTypePath, new NodesetFileOptions())
                ],
                [],
                fileSystem,
                telemetry);
            nodesets.GenerateCode(
                fileSystem,
                string.Empty,
                telemetry,
                new GeneratorOptions { OmitFluentApi = true });
            return string.Join(
                Environment.NewLine,
                fileSystem.CreatedFiles
                    .Where(c => Path.GetExtension(c) == ".cs")
                    .Select(c => Encoding.UTF8.GetString(fileSystem.Get(c))));
        }

        private static string ExtractMethodBody(string code, string methodName)
        {
            Match match = Regex.Match(code, @"\s" + Regex.Escape(methodName) + @"\(\s*this\s");
            Assert.That(match.Success, Is.True, $"Method {methodName} was not generated.");
            int end = code.IndexOf("return state;", match.Index, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(match.Index));
            return code[match.Index..end];
        }

        private const string FlowVariableTypeNodeSet = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                <NamespaceUris>
                    <Uri>http://test.org/UA/FlowVariableType/</Uri>
                </NamespaceUris>
                <Models>
                    <Model ModelUri="http://test.org/UA/FlowVariableType/" PublicationDate="2026-10-01T00:00:00Z" Version="1.0.0" />
                </Models>
                <Aliases>
                    <Alias Alias="Double">i=11</Alias>
                    <Alias Alias="HasSubtype">i=45</Alias>
                </Aliases>
                <UAVariableType NodeId="ns=1;i=2000" BrowseName="1:FlowVariableType" DataType="Double">
                    <DisplayName>FlowVariableType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=63</Reference>
                    </References>
                </UAVariableType>
            </UANodeSet>
            """;
    }
}
