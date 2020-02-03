/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.Serialization;


namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{

    public class NodeIds
    {
        public static ExpandedNodeId TestUnionComplexType_TypeId = new ExpandedNodeId(100, Namespaces.OpcUaEncoderTests);
        public string TestUnionComplexType_TypeId_String = $"nsu={Namespaces.OpcUaEncoderTests};i=100";
        public static ExpandedNodeId TestUnionComplexType_BinaryEncodingId = new ExpandedNodeId(101, Namespaces.OpcUaEncoderTests);
        public string TestUnionComplexType_BinaryEncodingId_String = $"nsu={Namespaces.OpcUaEncoderTests};i=101";
        public static ExpandedNodeId TestUnionComplexType_XmlEncodingId = new ExpandedNodeId(102, Namespaces.OpcUaEncoderTests);
        public string TestUnionComplexType_XmlEncodingId_String = $"nsu={Namespaces.OpcUaEncoderTests};i=102";
        public static ExpandedNodeId TestUnionComplexType_JsonEncodingId = new ExpandedNodeId(103, Namespaces.OpcUaEncoderTests);
        public string TestUnionComplexType_JsonEncodingId_String = $"nsu={Namespaces.OpcUaEncoderTests};i=103";
    }

    public class DataTypes
    {
        public DataTypeDefinition UnionType = new DataTypeDefinition() {

        };

    }

    [DataContract(Namespace = Namespaces.OpcUaEncoderTests)]
    public enum EnumSample
    {
        /// <remarks />
        [EnumMember(Value = "Open_1")]
        Open = 1,

        /// <remarks />
        [EnumMember(Value = "Covered_2")]
        Covered = 2,

        /// <remarks />
        [EnumMember(Value = "Ten_10")]
        Ten = 10,

        /// <remarks />
        [EnumMember(Value = "Red_100")]
        Red = 100
    }


    /// <summary>
    /// Sample types for unit tests, base on BaseComplexType and derived types.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaEncoderTests)]
    [StructureDefinition(
        DefaultEncodingId = null,
        BaseDataType = StructureBaseDataType.Union,
        StructureType = StructureType.Union)]
    [StructureTypeId(
        BinaryEncodingId = null,
        XmlEncodingId = null,
        ComplexTypeId = null
        )]

    public class TestUnionComplexType : UnionComplexType
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        TestUnionComplexType()
        {
        }
        #endregion

        #region Public Properties
        [DataMember(Name = "UInt32", IsRequired = false, Order = 1)]
        public UInt32 UInt32 { get; set; }

        [DataMember(Name = "Int32", IsRequired = false, Order = 2)]
        public Int32 Int32 { get; set; }

        [DataMember(Name = "Name", IsRequired = false, Order = 3)]
        public string Name { get; set; }
        #endregion
    }
}
