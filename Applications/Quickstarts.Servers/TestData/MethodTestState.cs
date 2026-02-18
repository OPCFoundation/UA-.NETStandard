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
using Opc.Ua;

namespace TestData
{
    public partial class MethodTestState
    {
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            ScalarMethod1.OnCall = OnScalarValue1;
            ScalarMethod2.OnCall = OnScalarValue2;
            ScalarMethod3.OnCall = OnScalarValue3;
            ArrayMethod1.OnCall = OnArrayValue1;
            ArrayMethod2.OnCall = OnArrayValue2;
            ArrayMethod3.OnCall = OnArrayValue3;
            UserScalarMethod1.OnCall = OnUserScalarValue1;
            UserScalarMethod2.OnCall = OnUserScalarValue2;
            UserArrayMethod1.OnCall = OnUserArrayValue1;
            UserArrayMethod2.OnCall = OnUserArrayValue2;
        }

        private ServiceResult OnScalarValue1(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            bool booleanIn,
            sbyte sByteIn,
            byte byteIn,
            short int16In,
            ushort uInt16In,
            int int32In,
            uint uInt32In,
            long int64In,
            ulong uInt64In,
            float floatIn,
            double doubleIn,
            ref bool booleanOut,
            ref sbyte sByteOut,
            ref byte byteOut,
            ref short int16Out,
            ref ushort uInt16Out,
            ref int int32Out,
            ref uint uInt32Out,
            ref long int64Out,
            ref ulong uInt64Out,
            ref float floatOut,
            ref double doubleOut)
        {
            booleanOut = booleanIn;
            sByteOut = sByteIn;
            byteOut = byteIn;
            int16Out = int16In;
            uInt16Out = uInt16In;
            int32Out = int32In;
            uInt32Out = uInt32In;
            int64Out = int64In;
            uInt64Out = uInt64In;
            floatOut = floatIn;
            doubleOut = doubleIn;

            return ServiceResult.Good;
        }

        private ServiceResult OnScalarValue2(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string stringIn,
            DateTime dateTimeIn,
            Uuid guidIn,
            ByteString byteStringIn,
            XmlElement xmlElementIn,
            NodeId nodeIdIn,
            ExpandedNodeId expandedNodeIdIn,
            QualifiedName qualifiedNameIn,
            LocalizedText localizedTextIn,
            StatusCode statusCodeIn,
            ref string stringOut,
            ref DateTime dateTimeOut,
            ref Uuid guidOut,
            ref ByteString byteStringOut,
            ref XmlElement xmlElementOut,
            ref NodeId nodeIdOut,
            ref ExpandedNodeId expandedNodeIdOut,
            ref QualifiedName qualifiedNameOut,
            ref LocalizedText localizedTextOut,
            ref StatusCode statusCodeOut)
        {
            stringOut = stringIn;
            dateTimeOut = dateTimeIn;
            guidOut = guidIn;
            byteStringOut = byteStringIn;
            xmlElementOut = xmlElementIn;
            nodeIdOut = nodeIdIn;
            expandedNodeIdOut = expandedNodeIdIn;
            qualifiedNameOut = qualifiedNameIn;
            localizedTextOut = localizedTextIn;
            statusCodeOut = statusCodeIn;

            return ServiceResult.Good;
        }

        private ServiceResult OnScalarValue3(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            Variant variantIn,
            int enumerationIn,
            ExtensionObject structureIn,
            ref Variant variantOut,
            ref int enumerationOut,
            ref ExtensionObject structureOut)
        {
            variantOut = variantIn;
            enumerationOut = enumerationIn;
            structureOut = structureIn;

            return ServiceResult.Good;
        }

        private ServiceResult OnArrayValue1(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<bool> booleanIn,
            ArrayOf<sbyte> sByteIn,
            ArrayOf<byte> byteIn,
            ArrayOf<short> int16In,
            ArrayOf<ushort> uInt16In,
            ArrayOf<int> int32In,
            ArrayOf<uint> uInt32In,
            ArrayOf<long> int64In,
            ArrayOf<ulong> uInt64In,
            ArrayOf<float> floatIn,
            ArrayOf<double> doubleIn,
            ref ArrayOf<bool> booleanOut,
            ref ArrayOf<sbyte> sByteOut,
            ref ArrayOf<byte> byteOut,
            ref ArrayOf<short> int16Out,
            ref ArrayOf<ushort> uInt16Out,
            ref ArrayOf<int> int32Out,
            ref ArrayOf<uint> uInt32Out,
            ref ArrayOf<long> int64Out,
            ref ArrayOf<ulong> uInt64Out,
            ref ArrayOf<float> floatOut,
            ref ArrayOf<double> doubleOut)
        {
            booleanOut = booleanIn;
            sByteOut = sByteIn;
            byteOut = byteIn;
            int16Out = int16In;
            uInt16Out = uInt16In;
            int32Out = int32In;
            uInt32Out = uInt32In;
            int64Out = int64In;
            uInt64Out = uInt64In;
            floatOut = floatIn;
            doubleOut = doubleIn;

            return ServiceResult.Good;
        }

        private ServiceResult OnArrayValue2(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<string> stringIn,
            ArrayOf<DateTime> dateTimeIn,
            ArrayOf<Uuid> guidIn,
            ArrayOf<ByteString> byteStringIn,
            ArrayOf<XmlElement> xmlElementIn,
            ArrayOf<NodeId> nodeIdIn,
            ArrayOf<ExpandedNodeId> expandedNodeIdIn,
            ArrayOf<QualifiedName> qualifiedNameIn,
            ArrayOf<LocalizedText> localizedTextIn,
            ArrayOf<StatusCode> statusCodeIn,
            ref ArrayOf<string> stringOut,
            ref ArrayOf<DateTime> dateTimeOut,
            ref ArrayOf<Uuid> guidOut,
            ref ArrayOf<ByteString> byteStringOut,
            ref ArrayOf<XmlElement> xmlElementOut,
            ref ArrayOf<NodeId> nodeIdOut,
            ref ArrayOf<ExpandedNodeId> expandedNodeIdOut,
            ref ArrayOf<QualifiedName> qualifiedNameOut,
            ref ArrayOf<LocalizedText> localizedTextOut,
            ref ArrayOf<StatusCode> statusCodeOut)
        {
            stringOut = stringIn;
            dateTimeOut = dateTimeIn;
            guidOut = guidIn;
            byteStringOut = byteStringIn;
            xmlElementOut = xmlElementIn;
            nodeIdOut = nodeIdIn;
            expandedNodeIdOut = expandedNodeIdIn;
            qualifiedNameOut = qualifiedNameIn;
            localizedTextOut = localizedTextIn;
            statusCodeOut = statusCodeIn;

            return ServiceResult.Good;
        }

        private ServiceResult OnArrayValue3(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> variantIn,
            ArrayOf<int> enumerationIn,
            ArrayOf<ExtensionObject> structureIn,
            ref ArrayOf<Variant> variantOut,
            ref ArrayOf<int> enumerationOut,
            ref ArrayOf<ExtensionObject> structureOut)
        {
            variantOut = variantIn;
            enumerationOut = enumerationIn;
            structureOut = structureIn;

            return ServiceResult.Good;
        }

        private ServiceResult OnUserScalarValue1(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            bool booleanIn,
            sbyte sByteIn,
            byte byteIn,
            short int16In,
            ushort uInt16In,
            int int32In,
            uint uInt32In,
            long int64In,
            ulong uInt64In,
            float floatIn,
            double doubleIn,
            string stringIn,
            ref bool booleanOut,
            ref sbyte sByteOut,
            ref byte byteOut,
            ref short int16Out,
            ref ushort uInt16Out,
            ref int int32Out,
            ref uint uInt32Out,
            ref long int64Out,
            ref ulong uInt64Out,
            ref float floatOut,
            ref double doubleOut,
            ref string stringOut)
        {
            booleanOut = booleanIn;
            sByteOut = sByteIn;
            byteOut = byteIn;
            int16Out = int16In;
            uInt16Out = uInt16In;
            int32Out = int32In;
            uInt32Out = uInt32In;
            int64Out = int64In;
            uInt64Out = uInt64In;
            floatOut = floatIn;
            doubleOut = doubleIn;
            stringOut = stringIn;

            return ServiceResult.Good;
        }

        private ServiceResult OnUserScalarValue2(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            DateTime dateTimeIn,
            Uuid guidIn,
            ByteString byteStringIn,
            XmlElement xmlElementIn,
            NodeId nodeIdIn,
            ExpandedNodeId expandedNodeIdIn,
            QualifiedName qualifiedNameIn,
            LocalizedText localizedTextIn,
            StatusCode statusCodeIn,
            Variant variantIn,
            ref DateTime dateTimeOut,
            ref Uuid guidOut,
            ref ByteString byteStringOut,
            ref XmlElement xmlElementOut,
            ref NodeId nodeIdOut,
            ref ExpandedNodeId expandedNodeIdOut,
            ref QualifiedName qualifiedNameOut,
            ref LocalizedText localizedTextOut,
            ref StatusCode statusCodeOut,
            ref Variant variantOut)
        {
            dateTimeOut = dateTimeIn;
            guidOut = guidIn;
            byteStringOut = byteStringIn;
            xmlElementOut = xmlElementIn;
            nodeIdOut = nodeIdIn;
            expandedNodeIdOut = expandedNodeIdIn;
            qualifiedNameOut = qualifiedNameIn;
            localizedTextOut = localizedTextIn;
            statusCodeOut = statusCodeIn;
            variantOut = variantIn;

            return ServiceResult.Good;
        }

        private ServiceResult OnUserArrayValue1(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<bool> booleanIn,
            ArrayOf<sbyte> sByteIn,
            ArrayOf<byte> byteIn,
            ArrayOf<short> int16In,
            ArrayOf<ushort> uInt16In,
            ArrayOf<int> int32In,
            ArrayOf<uint> uInt32In,
            ArrayOf<long> int64In,
            ArrayOf<ulong> uInt64In,
            ArrayOf<float> floatIn,
            ArrayOf<double> doubleIn,
            ArrayOf<string> stringIn,
            ref ArrayOf<bool> booleanOut,
            ref ArrayOf<sbyte> sByteOut,
            ref ArrayOf<byte> byteOut,
            ref ArrayOf<short> int16Out,
            ref ArrayOf<ushort> uInt16Out,
            ref ArrayOf<int> int32Out,
            ref ArrayOf<uint> uInt32Out,
            ref ArrayOf<long> int64Out,
            ref ArrayOf<ulong> uInt64Out,
            ref ArrayOf<float> floatOut,
            ref ArrayOf<double> doubleOut,
            ref ArrayOf<string> stringOut)
        {
            booleanOut = booleanIn;
            sByteOut = sByteIn;
            byteOut = byteIn;
            int16Out = int16In;
            uInt16Out = uInt16In;
            int32Out = int32In;
            uInt32Out = uInt32In;
            int64Out = int64In;
            uInt64Out = uInt64In;
            floatOut = floatIn;
            doubleOut = doubleIn;
            stringOut = stringIn;

            return ServiceResult.Good;
        }

        private ServiceResult OnUserArrayValue2(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<DateTime> dateTimeIn,
            ArrayOf<Uuid> guidIn,
            ArrayOf<ByteString> byteStringIn,
            ArrayOf<XmlElement> xmlElementIn,
            ArrayOf<NodeId> nodeIdIn,
            ArrayOf<ExpandedNodeId> expandedNodeIdIn,
            ArrayOf<QualifiedName> qualifiedNameIn,
            ArrayOf<LocalizedText> localizedTextIn,
            ArrayOf<StatusCode> statusCodeIn,
            ArrayOf<Variant> variantIn,
            ref ArrayOf<DateTime> dateTimeOut,
            ref ArrayOf<Uuid> guidOut,
            ref ArrayOf<ByteString> byteStringOut,
            ref ArrayOf<XmlElement> xmlElementOut,
            ref ArrayOf<NodeId> nodeIdOut,
            ref ArrayOf<ExpandedNodeId> expandedNodeIdOut,
            ref ArrayOf<QualifiedName> qualifiedNameOut,
            ref ArrayOf<LocalizedText> localizedTextOut,
            ref ArrayOf<StatusCode> statusCodeOut,
            ref ArrayOf<Variant> variantOut)
        {
            dateTimeOut = dateTimeIn;
            guidOut = guidIn;
            byteStringOut = byteStringIn;
            xmlElementOut = xmlElementIn;
            nodeIdOut = nodeIdIn;
            expandedNodeIdOut = expandedNodeIdIn;
            qualifiedNameOut = qualifiedNameIn;
            localizedTextOut = localizedTextIn;
            statusCodeOut = statusCodeIn;
            variantOut = variantIn;

            return ServiceResult.Good;
        }
    }
}
