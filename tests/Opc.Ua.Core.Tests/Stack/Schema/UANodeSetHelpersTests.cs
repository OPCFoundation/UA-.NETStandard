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
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Schema
{
    /// <summary>
    /// Tests for the UANodeSet helper.
    /// </summary>
    [TestFixture]
    [Category("UANodeSet")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UANodeSetHelpersTests
    {
        /// <summary>
        /// A NodeSet writes a Variable's value as the typed element alone, but
        /// the Variant XML encoding nests that element inside a <c>Value</c>
        /// element. Importing the bare element found no <c>Value</c> to begin
        /// and silently produced a null Variant, so every value in every
        /// imported NodeSet was lost — browsable nodes that read back empty.
        /// </summary>
        [Test]
        public void ImportRestoresVariableValues()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            const string nodeSetXml = @"
                <UANodeSet xmlns='http://opcfoundation.org/UA/2011/03/UANodeSet.xsd'>
                  <NamespaceUris>
                    <Uri>urn:test:values</Uri>
                  </NamespaceUris>
                  <Aliases>
                    <Alias Alias='String'>i=12</Alias>
                    <Alias Alias='Boolean'>i=1</Alias>
                  </Aliases>
                  <UAVariable NodeId='ns=1;s=Text' BrowseName='1:Text' DataType='String'>
                    <DisplayName>Text</DisplayName>
                    <Value>
                      <uax:String xmlns:uax='http://opcfoundation.org/UA/2008/02/Types.xsd'>hello</uax:String>
                    </Value>
                  </UAVariable>
                  <UAVariable NodeId='ns=1;s=Flag' BrowseName='1:Flag' DataType='Boolean'>
                    <DisplayName>Flag</DisplayName>
                    <Value>
                      <uax:Boolean xmlns:uax='http://opcfoundation.org/UA/2008/02/Types.xsd'>true</uax:Boolean>
                    </Value>
                  </UAVariable>
                </UANodeSet>";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(nodeSetXml));
            Export.UANodeSet nodeSet = Export.UANodeSet.Read(stream);
            var context = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };
            foreach (string namespaceUri in nodeSet.NamespaceUris)
            {
                context.NamespaceUris.Append(namespaceUri);
            }
            var imported = new NodeStateCollection();

            nodeSet.Import(context, imported);

            BaseVariableState text = imported.OfType<BaseVariableState>()
                .Single(node => node.BrowseName.Name == "Text");
            BaseVariableState flag = imported.OfType<BaseVariableState>()
                .Single(node => node.BrowseName.Name == "Flag");
            Assert.That(text.WrappedValue.IsNull, Is.False, "The String value must survive the import.");
            Assert.That(text.WrappedValue.GetString(), Is.EqualTo("hello"));
            Assert.That(flag.WrappedValue.IsNull, Is.False, "The Boolean value must survive the import.");
            Assert.That(flag.WrappedValue.GetBoolean(), Is.True);
        }

        /// <summary>
        /// Test Structure Field ArrayDimensions attribute is correctly imported respectively exported
        /// </summary>
        [Test]
        public void ArrayDimensionsValidationTest()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string bufferPath = "./ArrayDimensionsValidationTest.xml";
            const string importBuffer =
                @"<?xml version='1.0' encoding='utf-8'?>
                <UANodeSet xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xsd='http://www.w3.org/2001/XMLSchema' LastModified='2021-09-16T19:10:18.097476Z' xmlns='http://opcfoundation.org/UA/2011/03/UANodeSet.xsd'>
                  <NamespaceUris>
                    <Uri>urn:foobar</Uri>
                  </NamespaceUris>
                  <Aliases>
                    <Alias Alias='HasSubtype'>i=45</Alias>
                    <Alias Alias='HasEncoding'>i=38</Alias>
                  </Aliases>
                  <UADataType NodeId='ns=1;s=Simple Structure' BrowseName='Simple Structure'>
                    <DisplayName>Simple Structure</DisplayName>
                    <References>
                      <Reference ReferenceType='HasEncoding'>ns=1;s=Simple Structure Encoding</Reference>
                      <Reference ReferenceType='HasSubtype' IsForward='false'>i=22</Reference>
                    </References>
                    <Definition Name='Simple Structure' IsUnion='true'>
                      <Field Name='Duration Field' DataType='i=290' />
                      <Field Name='Double Field' DataType='i=11' />
                    </Definition>
                  </UADataType>
                  <UADataType NodeId='ns=1;s=Complex Structure' BrowseName='Complex Structure'>
                    <DisplayName>Complex Structure</DisplayName>
                    <References>
                      <Reference ReferenceType='HasEncoding'>ns=1;s=Complex Structure Encoding</Reference>
                      <Reference ReferenceType='HasSubtype' IsForward='false'>i=22</Reference>
                    </References>
                    <Definition Name='Complex Structure'>
                      <Field Name='Scalar Structure' DataType='i=22' />
                      <Field Name='Scalar BuildInfo' DataType='i=338' />
                      <Field Name='Scalar Simple Structure' DataType='ns=1;s=Simple Structure' />
                      <Field Name='Scalar Boolean' DataType='i=1' />
                      <Field Name='Scalar Duration' DataType='i=290' />
                      <Field Name='Scalar String within max length' DataType='i=12' MaxStringLength='256' />
                      <Field Name='1D Array String no max length' DataType='i=12' ValueRank='1' />
                      <Field Name='1D Array String within max length' DataType='i=12' ValueRank='1' MaxStringLength='256' />
                      <Field Name='1D Array of Simple Structure 1' DataType='ns=1;s=Simple Structure' ValueRank='1' ArrayDimensions='2' />
                      <Field Name='1D Array of Simple Structure 2' DataType='ns=1;s=Simple Structure' ValueRank='1' ArrayDimensions='3' />
                      <Field Name='1D Array of BuildInfo' DataType='i=338' ValueRank='1' />
                      <Field Name='1D Array of Simple Structure' DataType='ns=1;s=Simple Structure' ValueRank='1' />
                      <Field Name='1D Array of Boolean' DataType='i=1' ValueRank='1' />
                      <Field Name='1D Array of Duration' DataType='i=290' ValueRank='1' />
                      <Field Name='1D Array of MessageSecurityMode' DataType='i=302' ValueRank='1' />
                      <Field Name='2D Array of Structure' DataType='i=22' ValueRank='2' ArrayDimensions='2,3' />
                      <Field Name='2D Array of BuildInfo' DataType='i=338' ValueRank='2' ArrayDimensions='2,3' />
                      <Field Name='2D Array of Simple Structure' DataType='ns=1;s=Simple Structure' ValueRank='2' ArrayDimensions='2,3' />
                      <Field Name='2D Array of Boolean' DataType='i=1' ValueRank='2' ArrayDimensions='2,3' />
                      <Field Name='2D Array of Duration' DataType='i=290' ValueRank='2' ArrayDimensions='2,3' />
                      <Field Name='2D Array of MessageSecurityMode' DataType='i=302' ValueRank='2' ArrayDimensions='2,3' />
                    </Definition>
                  </UADataType>
                </UANodeSet>";

            using var importStream = new MemoryStream(Encoding.UTF8.GetBytes(importBuffer));
            var importedNodeSet = Export.UANodeSet.Read(importStream);

            var importedNodeStates = new NodeStateCollection();
            var localContext = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };
            foreach (string namespaceUri in importedNodeSet.NamespaceUris)
            {
                localContext.NamespaceUris.Append(namespaceUri);
            }

            importedNodeSet.Import(localContext, importedNodeStates);

            Assert.That(importedNodeSet.NamespaceUris, Has.Length.EqualTo(1));
            Assert.That(importedNodeSet.Items, Has.Length.EqualTo(2));
            var dataType1 = importedNodeSet.Items[0] as Export.UADataType;
            var dataType2 = importedNodeSet.Items[1] as Export.UADataType;

            Assert.That(dataType1, Is.Not.Null);
            Assert.That(dataType1.Definition.Field, Has.Length.EqualTo(2));
            Assert.IsEmpty(dataType1.Definition.Field[0].ArrayDimensions);
            Assert.That(dataType1.Definition.IsUnion, Is.True);

            Assert.That(dataType2, Is.Not.Null);
            Assert.That(dataType2.Definition.IsUnion, Is.False);
            Assert.That(dataType2.Definition.Field, Has.Length.EqualTo(21));
            Assert.That(dataType2.Definition.Field[15].ArrayDimensions, Is.EqualTo("2,3"));
            Assert.That(dataType2.Definition.Field[5].MaxStringLength, Is.EqualTo(256));

            // export the nodeSet to a file, reimport it and re-test.
            using (var fileStream = new FileStream(bufferPath, FileMode.Create))
            {
                importedNodeStates.SaveAsNodeSet2(localContext, fileStream);
            }
            try
            {
                using var exportStream = new FileStream(bufferPath, FileMode.Open);
                var exportedNodeSet = Export.UANodeSet.Read(exportStream);

                var exportedNodeStates = new NodeStateCollection();
                localContext.NamespaceUris = new NamespaceTable();
                foreach (string namespaceUri in exportedNodeSet.NamespaceUris)
                {
                    localContext.NamespaceUris.Append(namespaceUri);
                }
                exportedNodeSet.Import(localContext, exportedNodeStates);

                Assert.That(exportedNodeSet.NamespaceUris, Has.Length.EqualTo(1));
                Assert.That(exportedNodeSet.Items, Has.Length.EqualTo(2));

                dataType1 = exportedNodeSet.Items[0] as Export.UADataType;
                dataType2 = exportedNodeSet.Items[1] as Export.UADataType;

                Assert.That(dataType1, Is.Not.Null);
                Assert.That(dataType1.Definition.Field, Has.Length.EqualTo(2));
                Assert.IsEmpty(dataType1.Definition.Field[0].ArrayDimensions);
                Assert.That(dataType1.Definition.IsUnion, Is.True);

                Assert.That(dataType2, Is.Not.Null);
                Assert.That(dataType2.Definition.IsUnion, Is.False);
                Assert.That(dataType2.Definition.Field, Has.Length.EqualTo(21));
                Assert.That(dataType2.Definition.Field[15].ArrayDimensions, Is.EqualTo("2,3"));
                Assert.That(dataType2.Definition.Field[5].MaxStringLength, Is.EqualTo(256));
            }
            finally
            {
                File.Delete(bufferPath);
            }
        }

        /// <summary>
        /// Test that parent-child references are correctly established after importing a NodeSet2.
        /// </summary>
        [Test]
        public void ParentChildReferencesTest()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string importBuffer =
                @"<?xml version='1.0' encoding='utf-8'?>
                <UANodeSet xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
                           xmlns:xsd='http://www.w3.org/2001/XMLSchema'
                           LastModified='2024-01-01T00:00:00.000Z'
                           xmlns='http://opcfoundation.org/UA/2011/03/UANodeSet.xsd'>
                  <NamespaceUris>
                    <Uri>http://opcfoundation.org/UA/Test</Uri>
                  </NamespaceUris>
                  <Aliases>
                    <Alias Alias='HasComponent'>i=47</Alias>
                    <Alias Alias='HasProperty'>i=46</Alias>
                    <Alias Alias='HasTypeDefinition'>i=40</Alias>
                  </Aliases>
                  <UAObject NodeId='ns=1;i=1000' BrowseName='1:ParentObject'>
                    <DisplayName>ParentObject</DisplayName>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=58</Reference>
                    </References>
                  </UAObject>
                  <UAVariable DataType='i=12' ParentNodeId='ns=1;i=1000' NodeId='ns=1;i=1001' BrowseName='1:ChildProperty' ValueRank='-1'>
                    <DisplayName>ChildProperty</DisplayName>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=68</Reference>
                      <Reference ReferenceType='HasProperty' IsForward='false'>ns=1;i=1000</Reference>
                    </References>
                  </UAVariable>
                  <UAObject ParentNodeId='ns=1;i=1000' NodeId='ns=1;i=1002' BrowseName='1:ChildObject'>
                    <DisplayName>ChildObject</DisplayName>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=58</Reference>
                      <Reference ReferenceType='HasComponent' IsForward='false'>ns=1;i=1000</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>";

            using var importStream = new MemoryStream(Encoding.UTF8.GetBytes(importBuffer));
            var importedNodeSet = Export.UANodeSet.Read(importStream);

            var importedNodeStates = new NodeStateCollection();
            var localContext = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };
            foreach (string namespaceUri in importedNodeSet.NamespaceUris)
            {
                localContext.NamespaceUris.Append(namespaceUri);
            }

            importedNodeSet.Import(localContext, importedNodeStates, linkParentChild: true);

            // Verify that all nodes were imported
            Assert.That(importedNodeStates, Has.Count.EqualTo(3));

            // Find the parent object
            BaseObjectState parentObject = null;
            BaseVariableState childProperty = null;
            BaseObjectState childObject = null;

            foreach (NodeState node in importedNodeStates)
            {
                if (node.BrowseName.Name == "ParentObject")
                {
                    parentObject = node as BaseObjectState;
                }
                else if (node.BrowseName.Name == "ChildProperty")
                {
                    childProperty = node as BaseVariableState;
                }
                else if (node.BrowseName.Name == "ChildObject")
                {
                    childObject = node as BaseObjectState;
                }
            }

            Assert.That(parentObject, Is.Not.Null, "ParentObject should be imported");
            Assert.That(childProperty, Is.Not.Null, "ChildProperty should be imported");
            Assert.That(childObject, Is.Not.Null, "ChildObject should be imported");

            // Verify parent-child relationships are established
            Assert.That(childProperty.Parent, Is.EqualTo(parentObject), "ChildProperty's Parent should be set to ParentObject");
            Assert.That(childObject.Parent, Is.EqualTo(parentObject), "ChildObject's Parent should be set to ParentObject");

            // Verify GetChildren works
            var children = new List<BaseInstanceState>();
            parentObject.GetChildren(localContext, children);

            Assert.That(children, Has.Count.EqualTo(2), "ParentObject should have 2 children");
            Assert.That(children, Does.Contain(childProperty), "Children should contain ChildProperty");
            Assert.That(children, Does.Contain(childObject), "Children should contain ChildObject");
        }

        /// <summary>
        /// A node whose declared parent is not part of the same import batch —
        /// because it lives in another NodeSet or another NodeManager — must
        /// not have that parent silently discarded. The caller needs it so it
        /// can be wired as an external reference.
        /// </summary>
        [Test]
        public void ImportKeepsAParentThatIsNotInTheSameBatch()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string importBuffer =
                @"<?xml version='1.0' encoding='utf-8'?>
                <UANodeSet xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
                           xmlns:xsd='http://www.w3.org/2001/XMLSchema'
                           LastModified='2024-01-01T00:00:00.000Z'
                           xmlns='http://opcfoundation.org/UA/2011/03/UANodeSet.xsd'>
                  <NamespaceUris>
                    <Uri>http://opcfoundation.org/UA/Test</Uri>
                    <Uri>http://opcfoundation.org/UA/Elsewhere</Uri>
                  </NamespaceUris>
                  <Aliases>
                    <Alias Alias='HasComponent'>i=47</Alias>
                    <Alias Alias='HasTypeDefinition'>i=40</Alias>
                  </Aliases>
                  <UAObject ParentNodeId='ns=2;i=5001' NodeId='ns=1;i=1001' BrowseName='1:Orphan'>
                    <DisplayName>Orphan</DisplayName>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=58</Reference>
                      <Reference ReferenceType='HasComponent' IsForward='false'>ns=2;i=5001</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>";

            using var importStream = new MemoryStream(Encoding.UTF8.GetBytes(importBuffer));
            Export.UANodeSet importedNodeSet = Export.UANodeSet.Read(importStream);

            var importedNodeStates = new NodeStateCollection();
            var localContext = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };
            foreach (string namespaceUri in importedNodeSet.NamespaceUris)
            {
                localContext.NamespaceUris.Append(namespaceUri);
            }

            importedNodeSet.Import(localContext, importedNodeStates, linkParentChild: true);

            Assert.That(importedNodeStates, Has.Count.EqualTo(1));
            var orphan = (BaseInstanceState)importedNodeStates[0];

            Assert.That(orphan.Parent, Is.Null,
                "The parent is not in this batch, so it cannot be linked as an in-memory parent.");

            NodeId expectedParent = new(5001u, (ushort)localContext.NamespaceUris.GetIndex(
                "http://opcfoundation.org/UA/Elsewhere"));

            Assert.Multiple(() => {
                Assert.That(
                    Export.UANodeSet.TryGetUnresolvedParentNodeId(orphan, out NodeId unresolvedParent),
                    Is.True,
                    "The declared parent must survive the import so a caller can wire it " +
                    "as an external reference.");

                Assert.That(unresolvedParent, Is.EqualTo(expectedParent));
            });
        }

        /// <summary>
        /// The record must be precise: a parent that was linked normally is not
        /// an unresolved parent, or a caller would wire references that already
        /// exist.
        /// </summary>
        [Test]
        public void ImportRecordsNoUnresolvedParentWhenTheParentIsInTheBatch()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string importBuffer =
                @"<?xml version='1.0' encoding='utf-8'?>
                <UANodeSet xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
                           xmlns:xsd='http://www.w3.org/2001/XMLSchema'
                           LastModified='2024-01-01T00:00:00.000Z'
                           xmlns='http://opcfoundation.org/UA/2011/03/UANodeSet.xsd'>
                  <NamespaceUris>
                    <Uri>http://opcfoundation.org/UA/Test</Uri>
                  </NamespaceUris>
                  <Aliases>
                    <Alias Alias='HasComponent'>i=47</Alias>
                    <Alias Alias='HasTypeDefinition'>i=40</Alias>
                  </Aliases>
                  <UAObject NodeId='ns=1;i=1000' BrowseName='1:ParentObject'>
                    <DisplayName>ParentObject</DisplayName>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=58</Reference>
                    </References>
                  </UAObject>
                  <UAObject ParentNodeId='ns=1;i=1000' NodeId='ns=1;i=1002' BrowseName='1:ChildObject'>
                    <DisplayName>ChildObject</DisplayName>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=58</Reference>
                      <Reference ReferenceType='HasComponent' IsForward='false'>ns=1;i=1000</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>";

            using var importStream = new MemoryStream(Encoding.UTF8.GetBytes(importBuffer));
            Export.UANodeSet importedNodeSet = Export.UANodeSet.Read(importStream);

            var importedNodeStates = new NodeStateCollection();
            var localContext = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };
            foreach (string namespaceUri in importedNodeSet.NamespaceUris)
            {
                localContext.NamespaceUris.Append(namespaceUri);
            }

            importedNodeSet.Import(localContext, importedNodeStates, linkParentChild: true);

            foreach (NodeState node in importedNodeStates)
            {
                Assert.Multiple(() => {
                    Assert.That(
                        Export.UANodeSet.TryGetUnresolvedParentNodeId(node, out NodeId unresolvedParent),
                        Is.False,
                        $"'{node.BrowseName.Name}' resolved inside the batch, so nothing is unresolved.");

                    Assert.That(unresolvedParent.IsNull, Is.True);
                });
            }
        }

        /// <summary>
        /// Imported Method argument Variables must be adopted through
        /// <see cref="MethodState"/>'s declared children so Call validation
        /// sees the authored argument definitions.
        /// </summary>
        [Test]
        public void ImportBindsMethodArgumentProperties()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string importBuffer =
                @"<?xml version='1.0' encoding='utf-8'?>
                <UANodeSet xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
                           xmlns:xsd='http://www.w3.org/2001/XMLSchema'
                           xmlns:uax='http://opcfoundation.org/UA/2008/02/Types.xsd'
                           xmlns='http://opcfoundation.org/UA/2011/03/UANodeSet.xsd'>
                  <NamespaceUris>
                    <Uri>urn:test:method-arguments</Uri>
                  </NamespaceUris>
                  <Aliases>
                    <Alias Alias='Argument'>i=296</Alias>
                    <Alias Alias='HasComponent'>i=47</Alias>
                    <Alias Alias='HasProperty'>i=46</Alias>
                    <Alias Alias='HasTypeDefinition'>i=40</Alias>
                  </Aliases>
                  <UAVariable NodeId='ns=1;i=1003' BrowseName='1:InputArguments'
                              ParentNodeId='ns=1;i=1000' DataType='i=12'
                              ValueRank='-1'>
                    <DisplayName>Custom InputArguments</DisplayName>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=68</Reference>
                      <Reference ReferenceType='HasProperty' IsForward='false'>ns=1;i=1000</Reference>
                    </References>
                  </UAVariable>
                  <UAVariable NodeId='ns=1;i=1001' BrowseName='InputArguments'
                              ParentNodeId='ns=1;i=1000' DataType='Argument'
                              ValueRank='1' ArrayDimensions='1'
                              UserWriteMask='1' AccessRestrictions='1'
                              DesignToolOnly='true'>
                    <DisplayName>Imported inputs</DisplayName>
                    <Description>Input metadata</Description>
                    <Category>Method metadata</Category>
                    <Documentation>https://example.org/input-arguments</Documentation>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=68</Reference>
                      <Reference ReferenceType='HasProperty' IsForward='false'>ns=1;i=1000</Reference>
                    </References>
                    <RolePermissions>
                      <RolePermission Permissions='1'>i=15644</RolePermission>
                    </RolePermissions>
                    <Value>
                      <uax:ListOfExtensionObject>
                        <uax:ExtensionObject>
                          <uax:TypeId>
                            <uax:Identifier>i=297</uax:Identifier>
                          </uax:TypeId>
                          <uax:Body>
                            <uax:Argument>
                              <uax:Name>revision</uax:Name>
                              <uax:DataType>
                                <uax:Identifier>i=12</uax:Identifier>
                              </uax:DataType>
                              <uax:ValueRank>-1</uax:ValueRank>
                              <uax:ArrayDimensions />
                            </uax:Argument>
                          </uax:Body>
                        </uax:ExtensionObject>
                      </uax:ListOfExtensionObject>
                    </Value>
                  </UAVariable>
                  <UAVariable NodeId='ns=1;i=1002' BrowseName='OutputArguments'
                              ParentNodeId='ns=1;i=1000' DataType='Argument'
                              ValueRank='1' ArrayDimensions='1'>
                    <DisplayName>Imported outputs</DisplayName>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=68</Reference>
                      <Reference ReferenceType='HasProperty' IsForward='false'>ns=1;i=1000</Reference>
                    </References>
                    <Value>
                      <uax:ListOfExtensionObject>
                        <uax:ExtensionObject>
                          <uax:TypeId>
                            <uax:Identifier>i=297</uax:Identifier>
                          </uax:TypeId>
                          <uax:Body>
                            <uax:Argument>
                              <uax:Name>accepted</uax:Name>
                              <uax:DataType>
                                <uax:Identifier>i=1</uax:Identifier>
                              </uax:DataType>
                              <uax:ValueRank>-1</uax:ValueRank>
                              <uax:ArrayDimensions />
                            </uax:Argument>
                          </uax:Body>
                        </uax:ExtensionObject>
                      </uax:ListOfExtensionObject>
                    </Value>
                  </UAVariable>
                  <UAMethod NodeId='ns=1;i=1000' BrowseName='1:Load'>
                    <DisplayName>Load</DisplayName>
                    <References>
                      <Reference ReferenceType='HasProperty'>ns=1;i=1003</Reference>
                      <Reference ReferenceType='HasProperty'>ns=1;i=1002</Reference>
                    </References>
                  </UAMethod>
                </UANodeSet>";

            using var importStream = new MemoryStream(Encoding.UTF8.GetBytes(importBuffer));
            Export.UANodeSet importedNodeSet = Export.UANodeSet.Read(importStream);

            var importedNodeStates = new NodeStateCollection();
            var localContext = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };
            foreach (string namespaceUri in importedNodeSet.NamespaceUris)
            {
                localContext.NamespaceUris.Append(namespaceUri);
            }

            localContext.ServerUris = new StringTable();
            localContext.EncodeableFactory = EncodeableFactory.Create();

            importedNodeSet.Import(localContext, importedNodeStates, linkParentChild: true);

            MethodState method = importedNodeStates.OfType<MethodState>().Single();
            ushort namespaceIndex = (ushort)localContext.NamespaceUris.GetIndex(
                "urn:test:method-arguments");
            var inputId = new NodeId(1001u, namespaceIndex);
            var outputId = new NodeId(1002u, namespaceIndex);
            var customInputId = new NodeId(1003u, namespaceIndex);
            var children = new List<BaseInstanceState>();
            method.GetChildren(localContext, children);

            Assert.Multiple(() =>
            {
                Assert.That(method.InputArguments, Is.Not.Null);
                Assert.That(method.OutputArguments, Is.Not.Null);
                Assert.That(method.InputArguments!.NodeId, Is.EqualTo(inputId));
                Assert.That(method.OutputArguments!.NodeId, Is.EqualTo(outputId));
                Assert.That(
                    method.InputArguments.ReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasProperty));
                Assert.That(method.InputArguments.DisplayName.Text, Is.EqualTo("Imported inputs"));
                Assert.That(method.InputArguments.Description.Text, Is.EqualTo("Input metadata"));
                Assert.That(
                    method.InputArguments.UserWriteMask,
                    Is.EqualTo(AttributeWriteMask.AccessLevel));
                Assert.That(
                    method.InputArguments.AccessRestrictions,
                    Is.EqualTo(AccessRestrictionType.SigningRequired));
                Assert.That(method.InputArguments.DesignToolOnly, Is.True);
                Assert.That(
                    method.InputArguments.NodeSetDocumentation,
                    Is.EqualTo("https://example.org/input-arguments"));
                Assert.That(
                    method.InputArguments.Categories,
                    Has.Count.EqualTo(1));
                Assert.That(
                    method.InputArguments.Categories,
                    Has.Member("Method metadata"));
                Assert.That(method.InputArguments.RolePermissions, Has.Count.EqualTo(1));
                Assert.That(
                    method.InputArguments.RolePermissions[0].RoleId,
                    Is.EqualTo(ObjectIds.WellKnownRole_Anonymous));
                Assert.That(
                    method.InputArguments.RolePermissions[0].Permissions,
                    Is.EqualTo((uint)PermissionType.Browse));
                Assert.That(method.InputArguments.Value, Has.Count.EqualTo(1));
                Assert.That(method.InputArguments.Value[0].Name, Is.EqualTo("revision"));
                Assert.That(method.OutputArguments.Value, Has.Count.EqualTo(1));
                Assert.That(method.OutputArguments.Value[0].Name, Is.EqualTo("accepted"));
                Assert.That(
                    importedNodeStates.Single(node => node.NodeId == inputId),
                    Is.SameAs(method.InputArguments));
                Assert.That(
                    importedNodeStates.Single(node => node.NodeId == outputId),
                    Is.SameAs(method.OutputArguments));
                Assert.That(children, Has.Count.EqualTo(3));
                Assert.That(children, Does.Contain(method.InputArguments));
                Assert.That(children, Does.Contain(method.OutputArguments));
                Assert.That(
                    children.Single(child => child.NodeId == customInputId),
                    Is.TypeOf<PropertyState>());
            });
        }

        /// <summary>
        /// Linking must preserve the importer's previous behavior for unnamed
        /// children and sibling BrowseName collisions.
        /// </summary>
        [Test]
        public void ImportPreservesUnnamedAndDuplicateBrowseNameChildren()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string importBuffer =
                @"<?xml version='1.0' encoding='utf-8'?>
                <UANodeSet xmlns='http://opcfoundation.org/UA/2011/03/UANodeSet.xsd'>
                  <NamespaceUris>
                    <Uri>urn:test:duplicate-children</Uri>
                  </NamespaceUris>
                  <Aliases>
                    <Alias Alias='HasComponent'>i=47</Alias>
                    <Alias Alias='HasTypeDefinition'>i=40</Alias>
                  </Aliases>
                  <UAObject NodeId='ns=1;i=2000' BrowseName='1:Parent'>
                    <DisplayName>Parent</DisplayName>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=58</Reference>
                    </References>
                  </UAObject>
                  <UAObject NodeId='ns=1;i=2001' BrowseName='1:Duplicate'
                            ParentNodeId='ns=1;i=2000'>
                    <DisplayName>First</DisplayName>
                    <References>
                      <Reference ReferenceType='HasComponent' IsForward='false'>ns=1;i=2000</Reference>
                    </References>
                  </UAObject>
                  <UAObject NodeId='ns=1;i=2002' BrowseName='1:Duplicate'
                            ParentNodeId='ns=1;i=2000'>
                    <DisplayName>Second</DisplayName>
                    <References>
                      <Reference ReferenceType='HasComponent' IsForward='false'>ns=1;i=2000</Reference>
                    </References>
                  </UAObject>
                  <UAObject NodeId='ns=1;i=2003' ParentNodeId='ns=1;i=2000'>
                    <DisplayName>Unnamed</DisplayName>
                    <References>
                      <Reference ReferenceType='HasComponent' IsForward='false'>ns=1;i=2000</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>";

            using var importStream = new MemoryStream(Encoding.UTF8.GetBytes(importBuffer));
            Export.UANodeSet importedNodeSet = Export.UANodeSet.Read(importStream);
            var importedNodeStates = new NodeStateCollection();
            var localContext = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };
            foreach (string namespaceUri in importedNodeSet.NamespaceUris)
            {
                localContext.NamespaceUris.Append(namespaceUri);
            }

            importedNodeSet.Import(localContext, importedNodeStates, linkParentChild: true);

            BaseObjectState parent = importedNodeStates
                .OfType<BaseObjectState>()
                .Single(node => node.BrowseName.Name == "Parent");
            var children = new List<BaseInstanceState>();
            parent.GetChildren(localContext, children);

            Assert.Multiple(() =>
            {
                Assert.That(children, Has.Count.EqualTo(3));
                Assert.That(
                    children.Count(child => child.BrowseName.Name == "Duplicate"),
                    Is.EqualTo(2));
                Assert.That(
                    children.Count(child => child.BrowseName.IsNull),
                    Is.EqualTo(1));
                Assert.That(
                    importedNodeStates.Count(node =>
                        node.NodeId.NamespaceIndex == parent.NodeId.NamespaceIndex),
                    Is.EqualTo(4));
            });
        }

        /// <summary>
        /// Test that parent-child references are NOT established by default (backward compatibility).
        /// </summary>
        [Test]
        public void ParentChildReferencesDefaultBehaviorTest()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string importBuffer =
                @"<?xml version='1.0' encoding='utf-8'?>
                <UANodeSet xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' 
                           xmlns:xsd='http://www.w3.org/2001/XMLSchema' 
                           LastModified='2024-01-01T00:00:00.000Z' 
                           xmlns='http://opcfoundation.org/UA/2011/03/UANodeSet.xsd'>
                  <NamespaceUris>
                    <Uri>http://opcfoundation.org/UA/Test</Uri>
                  </NamespaceUris>
                  <Aliases>
                    <Alias Alias='HasComponent'>i=47</Alias>
                    <Alias Alias='HasTypeDefinition'>i=40</Alias>
                  </Aliases>
                  <UAObject NodeId='ns=1;i=1000' BrowseName='1:ParentObject'>
                    <DisplayName>ParentObject</DisplayName>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=58</Reference>
                    </References>
                  </UAObject>
                  <UAObject ParentNodeId='ns=1;i=1000' NodeId='ns=1;i=1002' BrowseName='1:ChildObject'>
                    <DisplayName>ChildObject</DisplayName>
                    <References>
                      <Reference ReferenceType='HasTypeDefinition'>i=58</Reference>
                      <Reference ReferenceType='HasComponent' IsForward='false'>ns=1;i=1000</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>";

            using var importStream = new MemoryStream(Encoding.UTF8.GetBytes(importBuffer));
            var importedNodeSet = Export.UANodeSet.Read(importStream);

            var importedNodeStates = new NodeStateCollection();
            var localContext = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };
            foreach (string namespaceUri in importedNodeSet.NamespaceUris)
            {
                localContext.NamespaceUris.Append(namespaceUri);
            }

            // Import without linkParentChild parameter (default is false)
            importedNodeSet.Import(localContext, importedNodeStates);

            // Verify that all nodes were imported
            Assert.That(importedNodeStates, Has.Count.EqualTo(2));

            // Find the parent object
            BaseObjectState parentObject = null;
            BaseObjectState childObject = null;

            foreach (NodeState node in importedNodeStates)
            {
                if (node.BrowseName.Name == "ParentObject")
                {
                    parentObject = node as BaseObjectState;
                }
                else if (node.BrowseName.Name == "ChildObject")
                {
                    childObject = node as BaseObjectState;
                }
            }

            Assert.That(parentObject, Is.Not.Null, "ParentObject should be imported");
            Assert.That(childObject, Is.Not.Null, "ChildObject should be imported");

            // Verify parent-child relationships are NOT established (backward compatibility)
            Assert.That(childObject.Parent, Is.Null, "ChildObject's Parent should be null by default");

            // Verify GetChildren returns empty (backward compatibility)
            var children = new List<BaseInstanceState>();
            parentObject.GetChildren(localContext, children);

            Assert.That(children, Is.Empty, "ParentObject should have 0 children by default (backward compatibility)");
        }
    }
}
