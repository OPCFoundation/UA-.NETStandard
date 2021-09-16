/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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


using System.IO;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Schema
{
    /// <summary>
    /// Tests for the UANodeSet helper.
    /// </summary>
    [TestFixture, Category("UANodeSet")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class UANodeSetHelpersTests
    {

        #region Test Setup
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {

        }

        [TearDown]
        protected void TearDown()
        {
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Test Structure Field ArrayDimensions attribute is correctly imported repsectively exported
        /// </summary>
        [Test]
        [Category("UANodeSet")]
        public void ArrayDimensionsValidationTest()
        {
            var bufferPath = @"./ArrayDimensionsValidationTest.xml";
            var importBuffer = @"<?xml version='1.0' encoding='utf-8'?>
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
                      <Field Name='1D Array String no max length' DataType='i=12' ValueRank='1' ArrayDimensions='0' />
                      <Field Name='1D Array String within max length' DataType='i=12' ValueRank='1' ArrayDimensions='0' MaxStringLength='256' />
                      <Field Name='1D Array of Simple Structure 1' DataType='ns=1;s=Simple Structure' ValueRank='1' ArrayDimensions='2' />
                      <Field Name='1D Array of Simple Structure 2' DataType='ns=1;s=Simple Structure' ValueRank='1' ArrayDimensions='3' />
                      <Field Name='1D Array of BuildInfo' DataType='i=338' ValueRank='1' ArrayDimensions='0' />
                      <Field Name='1D Array of Simple Structure' DataType='ns=1;s=Simple Structure' ValueRank='1' ArrayDimensions='0' />
                      <Field Name='1D Array of Boolean' DataType='i=1' ValueRank='1' ArrayDimensions='0' />
                      <Field Name='1D Array of Duration' DataType='i=290' ValueRank='1' ArrayDimensions='0' />
                      <Field Name='1D Array of MessageSecurityMode' DataType='i=302' ValueRank='1' ArrayDimensions='0' />
                      <Field Name='2D Array of Structure' DataType='i=22' ValueRank='2' ArrayDimensions='2,3' />
                      <Field Name='2D Array of BuildInfo' DataType='i=338' ValueRank='2' ArrayDimensions='2,3' />
                      <Field Name='2D Array of Simple Structure' DataType='ns=1;s=Simple Structure' ValueRank='2' ArrayDimensions='2,3' />
                      <Field Name='2D Array of Boolean' DataType='i=1' ValueRank='2' ArrayDimensions='2,3' />
                      <Field Name='2D Array of Duration' DataType='i=290' ValueRank='2' ArrayDimensions='2,3' />
                      <Field Name='2D Array of MessageSecurityMode' DataType='i=302' ValueRank='2' ArrayDimensions='2,3' />
                    </Definition>
                  </UADataType>
                </UANodeSet>";

            using (var importStream = new MemoryStream(Encoding.UTF8.GetBytes(importBuffer)))
            {
                var importedNodeSet = Opc.Ua.Export.UANodeSet.Read(importStream);

                var importedNodeStates = new NodeStateCollection();
                var localContext = new SystemContext();

                localContext.NamespaceUris = new NamespaceTable();
                foreach (var namespaceUri in importedNodeSet.NamespaceUris)
                {
                    localContext.NamespaceUris.Append(namespaceUri);
                }

                importedNodeSet.Import(localContext, importedNodeStates);

                Assert.AreEqual(1, importedNodeSet.NamespaceUris.Length);
                Assert.AreEqual(2, importedNodeSet.Items.Length);
                var dataType1 = importedNodeSet.Items[0] as Export.UADataType;
                var dataType2 = importedNodeSet.Items[1] as Export.UADataType;

                Assert.NotNull(dataType1);
                Assert.AreEqual(2, dataType1.Definition.Field.Length);
                Assert.IsEmpty(dataType1.Definition.Field[0].ArrayDimensions);
                Assert.IsTrue(dataType1.Definition.IsUnion);

                Assert.NotNull(dataType2);
                Assert.IsFalse(dataType2.Definition.IsUnion);
                Assert.AreEqual(21, dataType2.Definition.Field.Length);
                Assert.AreEqual("2,3", dataType2.Definition.Field[15].ArrayDimensions);
                Assert.AreEqual(256, dataType2.Definition.Field[5].MaxStringLength);

                // export the nodeSet to a file, reimport it and re-test.
                importedNodeStates.SaveAsNodeSet2(localContext, new FileStream(bufferPath, FileMode.Create));
                try {
                    using (var exportStream = new FileStream(bufferPath, FileMode.Open))
                    {
                        var exportedNodeSet = Opc.Ua.Export.UANodeSet.Read(exportStream);

                        var exportedNodeStates = new NodeStateCollection();
                        localContext.NamespaceUris = new NamespaceTable();
                        foreach (var namespaceUri in exportedNodeSet.NamespaceUris)
                        {
                            localContext.NamespaceUris.Append(namespaceUri);
                        }
                        exportedNodeSet.Import(localContext, exportedNodeStates);

                        Assert.AreEqual(1, exportedNodeSet.NamespaceUris.Length);
                        Assert.AreEqual(2, exportedNodeSet.Items.Length);

                        dataType1 = exportedNodeSet.Items[0] as Export.UADataType;
                        dataType2 = exportedNodeSet.Items[1] as Export.UADataType;

                        Assert.NotNull(dataType1);
                        Assert.AreEqual(2, dataType1.Definition.Field.Length);
                        Assert.IsEmpty(dataType1.Definition.Field[0].ArrayDimensions);
                        Assert.IsTrue(dataType1.Definition.IsUnion);

                        Assert.NotNull(dataType2);
                        Assert.IsFalse(dataType2.Definition.IsUnion);
                        Assert.AreEqual(21, dataType2.Definition.Field.Length);
                        Assert.AreEqual("2,3", dataType2.Definition.Field[15].ArrayDimensions);
                        Assert.AreEqual(256, dataType2.Definition.Field[5].MaxStringLength);
                    }
                }
                finally
                {
                    File.Delete(bufferPath);
                }
            }
        }
    }
    #endregion
}
