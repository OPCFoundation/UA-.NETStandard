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

using NUnit.Framework;
using Opc.Ua.Aas.V3;

namespace Opc.Ua.Aas.Tests.Values
{
    /// <summary>
    /// Tests the clause 6.3.2 AASValueString guard.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasValueStringGuardTests
    {
        [TestCase(Opc.Ua.Aas.V3.DataTypes.AASQualifierDataType)]
        [TestCase(Opc.Ua.Aas.V3.DataTypes.AASExtensionDataType)]
        [TestCase(Opc.Ua.Aas.V3.DataTypes.AASDataSpecificationIec61360DataType)]
        public void StructureCarriersAreAccepted(uint dataTypeId)
        {
            var carrier = new ExpandedNodeId(dataTypeId, 0, Opc.Ua.Aas.V3.Namespaces.AasV3, 0);

            Assert.That(AasValueStringGuard.IsLegitimateStructureCarrier(carrier), Is.True);
        }

        [Test]
        public void VariableDataTypeOfAasValueStringIsRejected()
        {
            Assert.That(
                () => AasValueStringGuard.AssertVariableDataTypeAllowed(
                    Opc.Ua.Aas.V3.DataTypeIds.AASValueString,
                    "MyProperty.Value"),
                Throws.ArgumentException.With.Message.Contains("MyProperty.Value"));
        }

        [Test]
        public void VariableWithAssignedXsdDataTypePasses()
        {
            ExpandedNodeId dataTypeId =
                AasXsdTypeMap.ToDataTypeId(AASDataTypeDefXsdDataType.String);

            Assert.That(
                () => AasValueStringGuard.AssertVariableDataTypeAllowed(dataTypeId, "MyProperty.Value"),
                Throws.Nothing);
        }

        [Test]
        public void SessionLocalAasValueStringIsRejected()
        {
            var namespaceUris = new NamespaceTable();
            ushort index = namespaceUris.GetIndexOrAppend(Opc.Ua.Aas.V3.Namespaces.AasV3);
            var local = new NodeId(Opc.Ua.Aas.V3.DataTypes.AASValueString, index);

            Assert.That(
                () => AasValueStringGuard.AssertVariableDataTypeAllowed(
                    local,
                    namespaceUris,
                    "LocalValue"),
                Throws.ArgumentException.With.Message.Contains("LocalValue"));
        }
    }
}
