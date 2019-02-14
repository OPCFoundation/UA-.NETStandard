/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Runtime.InteropServices;

#pragma warning disable 1591

namespace OpcRcw.Hda
{

    /// <exclude />
	[ComImport]
	[GuidAttribute("7DE5B060-E089-11d2-A5E6-000086339399")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface CATID_OPCHDAServer10 {}

    /// <exclude />
    public enum OPCHDA_SERVERSTATUS 
    { 
        OPCHDA_UP = 1,
	    OPCHDA_DOWN,
	    OPCHDA_INDETERMINATE 
    }

    /// <exclude />
    public enum OPCHDA_BROWSEDIRECTION 
    {
	    OPCHDA_BROWSE_UP = 1,
	    OPCHDA_BROWSE_DOWN,
	    OPCHDA_BROWSE_DIRECT
    }

    /// <exclude />
    public enum OPCHDA_BROWSETYPE 
    {
	    OPCHDA_BRANCH = 1,
	    OPCHDA_LEAF,
	    OPCHDA_FLAT,
	    OPCHDA_ITEMS
    }

    /// <exclude />
    public enum OPCHDA_ANNOTATIONCAPABILITIES 
    {  
	    OPCHDA_READANNOTATIONCAP   = 0x01,
	    OPCHDA_INSERTANNOTATIONCAP = 0x02 
    }

    /// <exclude />
    public enum OPCHDA_UPDATECAPABILITIES 
    {
	    OPCHDA_INSERTCAP        = 0x01,
	    OPCHDA_REPLACECAP       = 0x02,
	    OPCHDA_INSERTREPLACECAP = 0x04,
	    OPCHDA_DELETERAWCAP     = 0x08,
	    OPCHDA_DELETEATTIMECAP  = 0x10
    }

    /// <exclude />
    public enum OPCHDA_OPERATORCODES 
    {
	    OPCHDA_EQUAL = 1,
	    OPCHDA_LESS,
	    OPCHDA_LESSEQUAL,
	    OPCHDA_GREATER,
	    OPCHDA_GREATEREQUAL,
	    OPCHDA_NOTEQUAL
    }

    /// <exclude />
    public enum OPCHDA_EDITTYPE 
    {
	    OPCHDA_INSERT = 1,
	    OPCHDA_REPLACE,
	    OPCHDA_INSERTREPLACE,
	    OPCHDA_DELETE
    }

    /// <exclude />
    public enum OPCHDA_AGGREGATE 
    {
	    OPCHDA_NOAGGREGATE = 0,
	    OPCHDA_INTERPOLATIVE,
	    OPCHDA_TOTAL,
	    OPCHDA_AVERAGE,
	    OPCHDA_TIMEAVERAGE,
	    OPCHDA_COUNT,
	    OPCHDA_STDEV,
	    OPCHDA_MINIMUMACTUALTIME,
	    OPCHDA_MINIMUM,
	    OPCHDA_MAXIMUMACTUALTIME,
	    OPCHDA_MAXIMUM,
	    OPCHDA_START,
	    OPCHDA_END,
	    OPCHDA_DELTA,
	    OPCHDA_REGSLOPE,
	    OPCHDA_REGCONST,
	    OPCHDA_REGDEV,
	    OPCHDA_VARIANCE,
	    OPCHDA_RANGE,
	    OPCHDA_DURATIONGOOD,
	    OPCHDA_DURATIONBAD,
	    OPCHDA_PERCENTGOOD,
	    OPCHDA_PERCENTBAD,
	    OPCHDA_WORSTQUALITY,
	    OPCHDA_ANNOTATIONS
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCHDA_ANNOTATION 
    {					  
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        [MarshalAs(UnmanagedType.I4)]
        public int dwNumValues;
        public IntPtr ftTimeStamps;
        public IntPtr szAnnotation;
        public IntPtr ftAnnotationTime;
        public IntPtr szUser;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCHDA_MODIFIEDITEM 
    {
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        [MarshalAs(UnmanagedType.I4)]
        public int dwCount;
        public IntPtr pftTimeStamps;
        public IntPtr pdwQualities;
        public IntPtr pvDataValues;
        public IntPtr pftModificationTime;
        public IntPtr pEditType;
        public IntPtr szUser;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCHDA_ATTRIBUTE
    {
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        [MarshalAs(UnmanagedType.I4)]
        public int dwNumValues;
        [MarshalAs(UnmanagedType.I4)]
        public int dwAttributeID;
        public IntPtr ftTimeStamps;
        public IntPtr vAttributeValues;
    };

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCHDA_TIME 
    {
        [MarshalAs(UnmanagedType.I4)]
        public int bString;
        [MarshalAs(UnmanagedType.LPWStr)]
	    public string szTime;
	    public System.Runtime.InteropServices.ComTypes.FILETIME ftTime;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCHDA_ITEM
    {
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        [MarshalAs(UnmanagedType.I4)]
        public int haAggregate;
        [MarshalAs(UnmanagedType.I4)]
        public int dwCount;
        public IntPtr pftTimeStamps;
        public IntPtr pdwQualities;
        public IntPtr pvDataValues;
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("1F1217B1-DEE0-11d2-A5E5-000086339399")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCHDA_Browser
    {
        void GetEnum(
            OPCHDA_BROWSETYPE dwBrowseType,
            [Out] 
            out OpcRcw.Comn.IEnumString ppIEnumString);   

	    void ChangeBrowsePosition(
            OPCHDA_BROWSEDIRECTION dwBrowseDirection,
            [MarshalAs(UnmanagedType.LPWStr)]
		    string szString);

        void GetItemID(
            [MarshalAs(UnmanagedType.LPWStr)]
            string szNode,
            [Out][MarshalAs(UnmanagedType.LPWStr)]
            out string pszItemID);

        void GetBranchPosition(
            [Out][MarshalAs(UnmanagedType.LPWStr)]
            out string pszBranchPos);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("1F1217B0-DEE0-11d2-A5E5-000086339399")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCHDA_Server
    {
        void GetItemAttributes( 
            [Out][MarshalAs(UnmanagedType.I4)]
            out int pdwCount,
            [Out]
            out IntPtr ppdwAttrID,
            [Out]
            out IntPtr ppszAttrName,
            [Out]
            out IntPtr ppszAttrDesc,
            [Out]
            out IntPtr ppvtAttrDataType);

        void GetAggregates(
            [Out][MarshalAs(UnmanagedType.I4)]
            out int pdwCount,
            [Out]
            out IntPtr ppdwAggrID,
            [Out]
            out IntPtr ppszAggrName,
            [Out]
            out IntPtr ppszAggrDesc);

        void GetHistorianStatus(
            [Out]
            out OPCHDA_SERVERSTATUS pwStatus,
            [Out]
            out IntPtr pftCurrentTime,
            [Out]
            out IntPtr pftStartTime,
            [Out][MarshalAs(UnmanagedType.I2)]
            out short pwMajorVersion,
            [Out][MarshalAs(UnmanagedType.I2)]
            out short wMinorVersion,
            [Out][MarshalAs(UnmanagedType.I2)]
            out short pwBuildNumber,
            [Out][MarshalAs(UnmanagedType.I4)]
            out int pdwMaxReturnValues,
            [Out][MarshalAs(UnmanagedType.LPWStr)]
            out string  ppszStatusString,
            [Out][MarshalAs(UnmanagedType.LPWStr)]
            out string ppszVendorInfo);

        void GetItemHandles(
            [MarshalAs(UnmanagedType.I4)]
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr, SizeParamIndex=0)]  
            string[] pszItemID,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phClient,
            [Out]
            out IntPtr pphServer,
            [Out]
            out IntPtr ppErrors);

	    void ReleaseItemHandles(
            [MarshalAs(UnmanagedType.I4)]
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
	        int[] phServer,
            [Out]
            out IntPtr ppErrors);

        void ValidateItemIDs(
            [MarshalAs(UnmanagedType.I4)]
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr, SizeParamIndex=0)]  
            string[] pszItemID,
            [Out]
            out IntPtr ppErrors);

        void CreateBrowse(
            [MarshalAs(UnmanagedType.I4)]
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] pdwAttrID,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex=0)]  
            OPCHDA_OPERATORCODES[] pOperator,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=0)]  
            object[]  vFilter,
            out IOPCHDA_Browser pphBrowser,
            [Out]
            out IntPtr ppErrors);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("1F1217B2-DEE0-11d2-A5E5-000086339399")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCHDA_SyncRead
    {
        void ReadRaw(
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumValues,
            [MarshalAs(UnmanagedType.I4)]
            int bBounds,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=4)]  
            int[] phServer,
            [Out]
            out IntPtr ppItemValues,
            [Out]
            out IntPtr ppErrors);

        void ReadProcessed(
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            System.Runtime.InteropServices.ComTypes.FILETIME ftResampleInterval,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)]  
            int[] phServer, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)]  
            int[] haAggregate,
            [Out]
            out IntPtr ppItemValues,
            [Out]
            out IntPtr ppErrors);

        void ReadAtTime(
            [MarshalAs(UnmanagedType.I4)]
            int dwNumTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)]  
            System.Runtime.InteropServices.ComTypes.FILETIME[] ftTimeStamps,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)]  
            int[] phServer,
            [Out]
            out IntPtr ppItemValues,
            [Out]
            out IntPtr ppErrors);

        void ReadModified(
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumValues,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)] 
            int[] phServer,
            [Out]
            out IntPtr ppItemValues,
            [Out]
            out IntPtr ppErrors);

        void ReadAttribute(
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            [MarshalAs(UnmanagedType.I4)]
            int hServer, 
            [MarshalAs(UnmanagedType.I4)]
            int dwNumAttributes,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)] 
            int[] pdwAttributeIDs,
            [Out]
            out IntPtr ppAttributeValues,
            [Out]
            out IntPtr ppErrors);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("1F1217B3-DEE0-11d2-A5E5-000086339399")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCHDA_SyncUpdate
    {
	    void QueryCapabilities(
            [Out]
		    out OPCHDA_UPDATECAPABILITIES pCapabilities);

        void Insert(
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)] 
            int[] phServer, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)] 
            System.Runtime.InteropServices.ComTypes.FILETIME[] ftTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=0)] 
            object[] vDataValues,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)] 
            int[] pdwQualities,
            [Out]
            out IntPtr ppErrors);

        void Replace(
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)] 
            int[] phServer, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)] 
            System.Runtime.InteropServices.ComTypes.FILETIME[] ftTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=0)] 
            object[] vDataValues,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)] 
            int[] pdwQualities,
            [Out]
            out IntPtr ppErrors);

        void InsertReplace(
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)] 
            int[] phServer, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)] 
            System.Runtime.InteropServices.ComTypes.FILETIME[] ftTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=0)] 
            object[] vDataValues,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)] 
            int[] pdwQualities,
            [Out]
            out IntPtr ppErrors);

        void DeleteRaw(
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phServer,
            [Out]
            out IntPtr ppErrors);

        void DeleteAtTime(
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)] 
            int[] phServer, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)] 
            System.Runtime.InteropServices.ComTypes.FILETIME[] ftTimeStamps,
            [Out]
            out IntPtr ppErrors);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("1F1217B4-DEE0-11d2-A5E5-000086339399")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCHDA_SyncAnnotations
    {
	    void QueryCapabilities(
            [Out]
		    out OPCHDA_ANNOTATIONCAPABILITIES pCapabilities);

        void Read(
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phServer,
            [Out]
            out IntPtr ppAnnotationValues,
            [Out]
            out IntPtr ppErrors);

	    void Insert(
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)] 
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)] 
            System.Runtime.InteropServices.ComTypes.FILETIME[] ftTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)] 
		    OPCHDA_ANNOTATION[] pAnnotationValues,
            [Out]
            out IntPtr ppErrors);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("1F1217B5-DEE0-11d2-A5E5-000086339399")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCHDA_AsyncRead
    {
        void ReadRaw(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumValues,
            [MarshalAs(UnmanagedType.I4)]
            int bBounds,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=5)] 
            int[] phServer,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

        void AdviseRaw(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            ref OPCHDA_TIME htStartTime,
            System.Runtime.InteropServices.ComTypes.FILETIME     ftUpdateInterval,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)] 
            int[] phServer,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

        void ReadProcessed(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            System.Runtime.InteropServices.ComTypes.FILETIME ftResampleInterval,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=4)] 
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=4)] 
            int[] haAggregate,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

        void AdviseProcessed(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            ref OPCHDA_TIME htStartTime,
            System.Runtime.InteropServices.ComTypes.FILETIME ftResampleInterval,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] haAggregate,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumIntervals,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

        void ReadAtTime(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=1)] 
            System.Runtime.InteropServices.ComTypes.FILETIME[]  ftTimeStamps,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)] 
            int[] phServer,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

        void ReadModified(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumValues,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=4)] 
            int[] phServer,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

        void ReadAttribute(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            [MarshalAs(UnmanagedType.I4)]
            int hServer, 
            [MarshalAs(UnmanagedType.I4)]
            int dwNumAttributes,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=4)] 
            int[] dwAttributeIDs,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

	    void Cancel(
             [MarshalAs(UnmanagedType.I4)]
		     int dwCancelID);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("1F1217B6-DEE0-11d2-A5E5-000086339399")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCHDA_AsyncUpdate
    {
	    void QueryCapabilities(
		    out OPCHDA_UPDATECAPABILITIES pCapabilities
	    );

        void Insert(
            [MarshalAs(UnmanagedType.I4)]
            int  dwTransactionID,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)] 
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=1)] 
            System.Runtime.InteropServices.ComTypes.FILETIME[]  ftTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=1)] 
            object[] vDataValues,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)] 
            int[] pdwQualities,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

	    void Replace(
            [MarshalAs(UnmanagedType.I4)]
            int  dwTransactionID,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)] 
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=1)] 
            System.Runtime.InteropServices.ComTypes.FILETIME[]  ftTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=1)] 
            object[] vDataValues,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)] 
            int[] pdwQualities,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

	    void InsertReplace(
            [MarshalAs(UnmanagedType.I4)]
            int  dwTransactionID,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)] 
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=1)] 
            System.Runtime.InteropServices.ComTypes.FILETIME[]  ftTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=1)] 
            object[] vDataValues,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)] 
            int[] pdwQualities,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

        void DeleteRaw(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)] 
            int[] phServer,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

        void DeleteAtTime(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)] 
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=1)] 
            System.Runtime.InteropServices.ComTypes.FILETIME[] ftTimeStamps,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

	    void Cancel(
            [MarshalAs(UnmanagedType.I4)]
		    int dwCancelID);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("1F1217B7-DEE0-11d2-A5E5-000086339399")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCHDA_AsyncAnnotations
    {
	    void QueryCapabilities(
		    out OPCHDA_ANNOTATIONCAPABILITIES pCapabilities);

        void Read(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)] 
            int[] phServer,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

        void Insert(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)] 
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=1)] 
            System.Runtime.InteropServices.ComTypes.FILETIME[] ftTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=1)] 
            OPCHDA_ANNOTATION[] pAnnotationValues,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

	    void Cancel(
             [MarshalAs(UnmanagedType.I4)]
		     int dwCancelID);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("1F1217B8-DEE0-11d2-A5E5-000086339399")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCHDA_Playback
    {
        void ReadRawWithUpdate(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumValues,
            System.Runtime.InteropServices.ComTypes.FILETIME ftUpdateDuration,
            System.Runtime.InteropServices.ComTypes.FILETIME ftUpdateInterval,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=6)] 
            int[] phServer,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

        void ReadProcessedWithUpdate(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID,
            ref OPCHDA_TIME htStartTime,
            ref OPCHDA_TIME htEndTime,
            System.Runtime.InteropServices.ComTypes.FILETIME ftResampleInterval,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumIntervals,
            System.Runtime.InteropServices.ComTypes.FILETIME ftUpdateInterval,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=6)] 
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=6)] 
            int[] haAggregate,
            [Out]
            out int pdwCancelID,
            [Out]
            out IntPtr ppErrors);

	    void Cancel(
            [MarshalAs(UnmanagedType.I4)]
		    int dwCancelID);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("1F1217B9-DEE0-11d2-A5E5-000086339399")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCHDA_DataCallback
    {
        void OnDataChange(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID, 
            [MarshalAs(UnmanagedType.I4)]
            int hrStatus,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=2)] 
            OPCHDA_ITEM[] pItemValues,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phrErrors);

        void OnReadComplete(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID, 
            [MarshalAs(UnmanagedType.I4)]
            int hrStatus,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=2)] 
            OPCHDA_ITEM[] pItemValues,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phrErrors);

        void OnReadModifiedComplete(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID, 
            [MarshalAs(UnmanagedType.I4)]
            int hrStatus,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=2)] 
            OPCHDA_MODIFIEDITEM[] pItemValues,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phrErrors);

        void OnReadAttributeComplete(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID, 
            [MarshalAs(UnmanagedType.I4)]
            int hrStatus,
            [MarshalAs(UnmanagedType.I4)]
            int hClient, 
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=3)] 
            OPCHDA_ATTRIBUTE[] pAttributeValues,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)] 
            int[] phrErrors);

        void OnReadAnnotations(
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID, 
            [MarshalAs(UnmanagedType.I4)]
            int hrStatus,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=2)] 
            OPCHDA_ANNOTATION[] pAnnotationValues,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phrErrors);

        void OnInsertAnnotations (
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID, 
            [MarshalAs(UnmanagedType.I4)]
            int hrStatus,
            [MarshalAs(UnmanagedType.I4)]
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phClients, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phrErrors);

        void OnPlayback (
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID, 
            [MarshalAs(UnmanagedType.I4)]
            int hrStatus,
            [MarshalAs(UnmanagedType.I4)]
            int dwNumItems, 
            IntPtr ppItemValues, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phrErrors);

        void OnUpdateComplete (
            [MarshalAs(UnmanagedType.I4)]
            int dwTransactionID, 
            [MarshalAs(UnmanagedType.I4)]
            int hrStatus,
            [MarshalAs(UnmanagedType.I4)]
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phClients, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)] 
            int[] phrErrors);

        void OnCancelComplete(
            [MarshalAs(UnmanagedType.I4)]
            int dwCancelID);
    }

    /// <exclude />
	public static class Constants
	{
		// category description.
		public const string OPC_CATEGORY_DESCRIPTION_HDA10 = "OPC History Data Access Servers Version 1.0";

		// attribute ids.
		public const int OPCHDA_DATA_TYPE		   = 0x01;
		public const int OPCHDA_DESCRIPTION		   = 0x02;
		public const int OPCHDA_ENG_UNITS		   = 0x03;
		public const int OPCHDA_STEPPED		       = 0x04;
		public const int OPCHDA_ARCHIVING	       = 0x05;
		public const int OPCHDA_DERIVE_EQUATION    = 0x06;
		public const int OPCHDA_NODE_NAME		   = 0x07;
		public const int OPCHDA_PROCESS_NAME	   = 0x08;
		public const int OPCHDA_SOURCE_NAME	       = 0x09;
		public const int OPCHDA_SOURCE_TYPE	       = 0x0a;
		public const int OPCHDA_NORMAL_MAXIMUM     = 0x0b;
		public const int OPCHDA_NORMAL_MINIMUM	   = 0x0c;
		public const int OPCHDA_ITEMID			   = 0x0d;
		public const int OPCHDA_MAX_TIME_INT	   = 0x0e;
		public const int OPCHDA_MIN_TIME_INT	   = 0x0f;
		public const int OPCHDA_EXCEPTION_DEV	   = 0x10;
		public const int OPCHDA_EXCEPTION_DEV_TYPE = 0x11;
		public const int OPCHDA_HIGH_ENTRY_LIMIT   = 0x12;
		public const int OPCHDA_LOW_ENTRY_LIMIT	   = 0x13;

		// attribute names.
		public const string OPCHDA_ATTRNAME_DATA_TYPE		   = "Data Type";
		public const string OPCHDA_ATTRNAME_DESCRIPTION        = "Description";
		public const string OPCHDA_ATTRNAME_ENG_UNITS		   = "Eng Units";
		public const string OPCHDA_ATTRNAME_STEPPED		       = "Stepped";
		public const string OPCHDA_ATTRNAME_ARCHIVING	       = "Archiving";
		public const string OPCHDA_ATTRNAME_DERIVE_EQUATION    = "Derive Equation";
		public const string OPCHDA_ATTRNAME_NODE_NAME		   = "Node Name";
		public const string OPCHDA_ATTRNAME_PROCESS_NAME	   = "Process Name";
		public const string OPCHDA_ATTRNAME_SOURCE_NAME	       = "Source Name";
		public const string OPCHDA_ATTRNAME_SOURCE_TYPE	       = "Source Type";
		public const string OPCHDA_ATTRNAME_NORMAL_MAXIMUM     = "Normal Maximum";
		public const string OPCHDA_ATTRNAME_NORMAL_MINIMUM	   = "Normal Minimum";
		public const string OPCHDA_ATTRNAME_ITEMID			   = "ItemID";
		public const string OPCHDA_ATTRNAME_MAX_TIME_INT	   = "Max Time Interval";
		public const string OPCHDA_ATTRNAME_MIN_TIME_INT	   = "Min Time Interval";
		public const string OPCHDA_ATTRNAME_EXCEPTION_DEV	   = "Exception Deviation";
		public const string OPCHDA_ATTRNAME_EXCEPTION_DEV_TYPE = "Exception Dev Type";
		public const string OPCHDA_ATTRNAME_HIGH_ENTRY_LIMIT   = "High Entry Limit";
		public const string OPCHDA_ATTRNAME_LOW_ENTRY_LIMIT	   = "Low Entry Limit";

		// aggregate names.
		public const string OPCHDA_AGGRNAME_INTERPOLATIVE	  = "Interpolative";
		public const string OPCHDA_AGGRNAME_TOTAL	          = "Total";
		public const string OPCHDA_AGGRNAME_AVERAGE	          = "Average";
		public const string OPCHDA_AGGRNAME_TIMEAVERAGE	      = "Time Average";
		public const string OPCHDA_AGGRNAME_COUNT	          = "Count";
		public const string OPCHDA_AGGRNAME_STDEV	          = "Standard Deviation";
		public const string OPCHDA_AGGRNAME_MINIMUMACTUALTIME = "Minimum Actual Time";
		public const string OPCHDA_AGGRNAME_MINIMUM	          = "Minimum";
		public const string OPCHDA_AGGRNAME_MAXIMUMACTUALTIME = "Maximum Actual Time";
		public const string OPCHDA_AGGRNAME_MAXIMUM	          = "Maximum";
		public const string OPCHDA_AGGRNAME_START	          = "Start";
		public const string OPCHDA_AGGRNAME_END               = "End";
		public const string OPCHDA_AGGRNAME_DELTA	          = "Delta";
		public const string OPCHDA_AGGRNAME_REGSLOPE	      = "Regression Line Slope";
		public const string OPCHDA_AGGRNAME_REGCONST	      = "Regression Line Constant";
		public const string OPCHDA_AGGRNAME_REGDEV            = "Regression Line Error";
		public const string OPCHDA_AGGRNAME_VARIANCE	      = "Variance";
		public const string OPCHDA_AGGRNAME_RANGE	          = "Range";
		public const string OPCHDA_AGGRNAME_DURATIONGOOD	  = "Duration Good";
		public const string OPCHDA_AGGRNAME_DURATIONBAD	      = "Duration Bad";
		public const string OPCHDA_AGGRNAME_PERCENTGOOD	      = "Percent Good";
		public const string OPCHDA_AGGRNAME_PERCENTBAD	      = "Percent Bad";
		public const string OPCHDA_AGGRNAME_WORSTQUALITY	  = "Worst Quality";
		public const string OPCHDA_AGGRNAME_ANNOTATIONS	      = "Annotations";

		// OPCHDA_QUALITY -- these are the high-order 16 bits, OPC DA Quality occupies low-order 16 bits.
		public const int OPCHDA_EXTRADATA		  = 0x00010000;
		public const int OPCHDA_INTERPOLATED	  = 0x00020000;
		public const int OPCHDA_RAW			      = 0x00040000;
		public const int OPCHDA_CALCULATED	      = 0x00080000;
		public const int OPCHDA_NOBOUND		      = 0x00100000;
		public const int OPCHDA_NODATA			  = 0x00200000;
		public const int OPCHDA_DATALOST		  = 0x00400000;
		public const int OPCHDA_CONVERSION		  = 0x00800000;
		public const int OPCHDA_PARTIAL           = 0x01000000;
	}
}
