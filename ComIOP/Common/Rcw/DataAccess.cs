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

namespace OpcRcw.Da
{
    /// <exclude />
	[ComImport]
	[GuidAttribute("63D5F430-CFE4-11d1-B2C8-0060083BA1FB")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface CATID_OPCDAServer10 {}

    /// <exclude />
	[ComImport]
	[GuidAttribute("63D5F432-CFE4-11d1-B2C8-0060083BA1FB")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface CATID_OPCDAServer20 {}

    /// <exclude />
	[ComImport]
	[GuidAttribute("CC603642-66D7-48f1-B69A-B625E73652D7")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface CATID_OPCDAServer30 {}

    /// <exclude />
	[ComImport]
	[GuidAttribute("3098EDA4-A006-48b2-A27F-247453959408")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface CATID_XMLDAServer10 {}

    /// <exclude />
    public enum OPCDATASOURCE 
    { 
        OPC_DS_CACHE = 1, 
        OPC_DS_DEVICE 
    }

    /// <exclude />
    public enum OPCBROWSETYPE 
    { 
        OPC_BRANCH = 1, 
        OPC_LEAF, 
        OPC_FLAT
    }

    /// <exclude />
    public enum OPCNAMESPACETYPE 
    { 
        OPC_NS_HIERARCHIAL = 1, 
        OPC_NS_FLAT
    }

    /// <exclude />
    public enum OPCBROWSEDIRECTION 
    { 
        OPC_BROWSE_UP = 1, 
        OPC_BROWSE_DOWN, 
        OPC_BROWSE_TO
    }

    /// <exclude />
    public enum OPCEUTYPE 
    {
        OPC_NOENUM = 0, 
        OPC_ANALOG, 
        OPC_ENUMERATED 
    }

    /// <exclude />
    public enum OPCSERVERSTATE 
    { 
        OPC_STATUS_RUNNING = 1, 
        OPC_STATUS_FAILED, 
        OPC_STATUS_NOCONFIG, 
        OPC_STATUS_SUSPENDED, 
        OPC_STATUS_TEST,
        OPC_STATUS_COMM_FAULT
    }

    /// <exclude />
    public enum OPCENUMSCOPE 
    { 
        OPC_ENUM_PRIVATE_CONNECTIONS = 1, 
        OPC_ENUM_PUBLIC_CONNECTIONS, 
        OPC_ENUM_ALL_CONNECTIONS, 
        OPC_ENUM_PRIVATE, 
        OPC_ENUM_PUBLIC, 
        OPC_ENUM_ALL 
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCGROUPHEADER 
    {
        [MarshalAs(UnmanagedType.I4)]
        public int dwSize;
        [MarshalAs(UnmanagedType.I4)]
        public int dwItemCount;
        [MarshalAs(UnmanagedType.I4)]
        public int hClientGroup;
        [MarshalAs(UnmanagedType.I4)]
        public int dwTransactionID;
        [MarshalAs(UnmanagedType.I4)]
        public int hrStatus;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCITEMHEADER1 
    {
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        [MarshalAs(UnmanagedType.I4)]
        public int dwValueOffset;
        [MarshalAs(UnmanagedType.I2)]
        public short wQuality;
        [MarshalAs(UnmanagedType.I2)]
        public short wReserved;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftTimeStampItem;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCITEMHEADER2 
    {
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        [MarshalAs(UnmanagedType.I4)]
        public int dwValueOffset;
        [MarshalAs(UnmanagedType.I2)]
        public short wQuality;
        [MarshalAs(UnmanagedType.I2)]
        public short wReserved;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCGROUPHEADERWRITE 
    {
        [MarshalAs(UnmanagedType.I4)]
        public int dwItemCount;
        [MarshalAs(UnmanagedType.I4)]
        public int hClientGroup;
        [MarshalAs(UnmanagedType.I4)]
        public int dwTransactionID;
        [MarshalAs(UnmanagedType.I4)]
        public int hrStatus;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCITEMHEADERWRITE 
    {
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        [MarshalAs(UnmanagedType.I4)]
        public int dwError;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCITEMSTATE
    {
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftTimeStamp;
        [MarshalAs(UnmanagedType.I2)]
        public short wQuality;
        [MarshalAs(UnmanagedType.I2)]
        public short wReserved;
        [MarshalAs(UnmanagedType.Struct)]
        public object vDataValue;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCSERVERSTATUS 
    {
        public System.Runtime.InteropServices.ComTypes.FILETIME ftStartTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCurrentTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastUpdateTime;
        public OPCSERVERSTATE dwServerState;
        [MarshalAs(UnmanagedType.I4)]
        public int dwGroupCount; 
        [MarshalAs(UnmanagedType.I4)]
        public int dwBandWidth;
        [MarshalAs(UnmanagedType.I2)]
        public short wMajorVersion;
        [MarshalAs(UnmanagedType.I2)]
        public short wMinorVersion;
        [MarshalAs(UnmanagedType.I2)]
        public short wBuildNumber;
        [MarshalAs(UnmanagedType.I2)]
        public short wReserved;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string szVendorInfo;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCITEMDEF 
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string szAccessPath;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string szItemID;
        [MarshalAs(UnmanagedType.I4)]
        public int bActive;
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        [MarshalAs(UnmanagedType.I4)]
        public int dwBlobSize;
        public IntPtr pBlob;
        [MarshalAs(UnmanagedType.I2)]
        public short vtRequestedDataType;
        [MarshalAs(UnmanagedType.I2)]
        public short wReserved;
    };

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCITEMATTRIBUTES
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string szAccessPath;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string szItemID;
        [MarshalAs(UnmanagedType.I4)]
        public int bActive;
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        [MarshalAs(UnmanagedType.I4)]
        public int hServer;
        [MarshalAs(UnmanagedType.I4)]
        public int dwAccessRights;
        [MarshalAs(UnmanagedType.I4)]
        public int dwBlobSize;
        public IntPtr pBlob;
        [MarshalAs(UnmanagedType.I2)]
        public short vtRequestedDataType;
        [MarshalAs(UnmanagedType.I2)]
        public short vtCanonicalDataType;
        public OPCEUTYPE  dwEUType;
        [MarshalAs(UnmanagedType.Struct)]
        public object vEUInfo;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCITEMRESULT 
    {
        [MarshalAs(UnmanagedType.I4)]
        public int hServer;
        [MarshalAs(UnmanagedType.I2)]
        public short vtCanonicalDataType;
        [MarshalAs(UnmanagedType.I2)]
        public short wReserved;
        [MarshalAs(UnmanagedType.I4)]
        public int dwAccessRights;
        [MarshalAs(UnmanagedType.I4)]
        public int dwBlobSize;
        public IntPtr pBlob;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCITEMPROPERTY
    {
        [MarshalAs(UnmanagedType.I2)]
        public short vtDataType;
        [MarshalAs(UnmanagedType.I2)]
        public short wReserved;
        [MarshalAs(UnmanagedType.I4)]
        public int	dwPropertyID;  
        [MarshalAs(UnmanagedType.LPWStr)] 
        public string szItemID;
        [MarshalAs(UnmanagedType.LPWStr)] 
        public string szDescription;
        [MarshalAs(UnmanagedType.Struct)] 
        public object vValue;
        [MarshalAs(UnmanagedType.I4)] 
        public int	hrErrorID;
        [MarshalAs(UnmanagedType.I4)] 
        public int dwReserved;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCITEMPROPERTIES 
    {
        [MarshalAs(UnmanagedType.I4)] 
        public int hrErrorID;
        [MarshalAs(UnmanagedType.I4)] 
        public int dwNumProperties;
        public IntPtr pItemProperties;
        [MarshalAs(UnmanagedType.I4)] 
		public int dwReserved;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCBROWSEELEMENT
    {
        [MarshalAs(UnmanagedType.LPWStr)] 
        public string szName;
        [MarshalAs(UnmanagedType.LPWStr)] 
        public string szItemID;
        [MarshalAs(UnmanagedType.I4)] 
        public int dwFlagValue;
        [MarshalAs(UnmanagedType.I4)] 
		public int dwReserved; 
        public OPCITEMPROPERTIES ItemProperties;
    }

    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct OPCITEMVQT
    {
        [MarshalAs(UnmanagedType.Struct)] 
        public object vDataValue;
        [MarshalAs(UnmanagedType.I4)] 
        public int bQualitySpecified;
        [MarshalAs(UnmanagedType.I2)] 
        public short wQuality;
        [MarshalAs(UnmanagedType.I2)] 
        public short wReserved;
        [MarshalAs(UnmanagedType.I4)] 
        public int bTimeStampSpecified;
        [MarshalAs(UnmanagedType.I4)] 
        public int dwReserved;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftTimeStamp;
    }

    /// <exclude />
    public enum OPCBROWSEFILTER 
    {
	    OPC_BROWSE_FILTER_ALL = 1,
	    OPC_BROWSE_FILTER_BRANCHES,
	    OPC_BROWSE_FILTER_ITEMS,
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a4d-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCServer
    {
        void AddGroup(
            [MarshalAs(UnmanagedType.LPWStr)] 
            string szName,
            [MarshalAs(UnmanagedType.I4)] 
            int bActive,
            [MarshalAs(UnmanagedType.I4)] 
            int dwRequestedUpdateRate,
            [MarshalAs(UnmanagedType.I4)] 
            int hClientGroup,
            IntPtr pTimeBias,
            IntPtr pPercentDeadband,
            [MarshalAs(UnmanagedType.I4)] 
            int dwLCID,
			[Out][MarshalAs(UnmanagedType.I4)] 
            out int phServerGroup,
			[Out][MarshalAs(UnmanagedType.I4)] 
            out int pRevisedUpdateRate,
            ref Guid riid,
			[Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex=9)] 
            out object ppUnk);

        void GetErrorString( 
            [MarshalAs(UnmanagedType.I4)] 
            int dwError,
            [MarshalAs(UnmanagedType.I4)] 
            int dwLocale,
		    [Out][MarshalAs(UnmanagedType.LPWStr)] 
            out string ppString);

        void GetGroupByName(
            [MarshalAs(UnmanagedType.LPWStr)] 
            string szName,
            ref Guid riid,
		    [Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex=1)] 
            out object ppUnk);

        void GetStatus( 
            [Out]
            out IntPtr ppServerStatus);

        void RemoveGroup(
            [MarshalAs(UnmanagedType.I4)] 
            int hServerGroup,
            [MarshalAs(UnmanagedType.I4)] 
            int bForce);

        void CreateGroupEnumerator(
            OPCENUMSCOPE dwScope, 
            ref Guid riid,
		    [Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex=1)] 
            out object ppUnk);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a4e-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCServerPublicGroups
    {
        void GetPublicGroupByName(
            [MarshalAs(UnmanagedType.LPWStr)] 
            string szName, 
            ref Guid riid,
		    [Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex=1)] 
            out object ppUnk);

        void RemovePublicGroup(
            [MarshalAs(UnmanagedType.I4)] 
            int hServerGroup,
            [MarshalAs(UnmanagedType.I4)] 
            int bForce);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a4f-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCBrowseServerAddressSpace
    {
        void QueryOrganization(
		    [Out] 
            out OPCNAMESPACETYPE pNameSpaceType);

        void ChangeBrowsePosition(
            OPCBROWSEDIRECTION dwBrowseDirection, 
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szString);

        void BrowseOPCItemIDs(
            OPCBROWSETYPE dwBrowseFilterType, 
            [MarshalAs(UnmanagedType.LPWStr)]  
            string  szFilterCriteria, 
            [MarshalAs(UnmanagedType.I2)]  
            short vtDataTypeFilter, 
            [MarshalAs(UnmanagedType.I4)]  
            int dwAccessRightsFilter,
		    [Out] 
            out OpcRcw.Comn.IEnumString ppIEnumString);        

        void GetItemID(
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szItemDataID,
		    [Out][MarshalAs(UnmanagedType.LPWStr)]  
            out string szItemID);

        void BrowseAccessPaths(
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szItemID,
		    [Out] 
            out OpcRcw.Comn.IEnumString pIEnumString);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a50-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCGroupStateMgt
    {
        void GetState(
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pUpdateRate, 
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pActive,
		    [Out][MarshalAs(UnmanagedType.LPWStr)]  
            out string ppName, 
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pTimeBias, 
		    [Out][MarshalAs(UnmanagedType.R4)]  
            out float pPercentDeadband,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pLCID,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int phClientGroup,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int phServerGroup);

        void SetState( 
            IntPtr pRequestedUpdateRate,  
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pRevisedUpdateRate, 
            IntPtr pActive, 
            IntPtr pTimeBias,
            IntPtr pPercentDeadband,
            IntPtr pLCID,
            IntPtr phClientGroup);

        void SetName( 
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szName);

        void CloneGroup(
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szName, 
            ref Guid riid,
		    [Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex=1)] 
            out object ppUnk);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a51-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCPublicGroupStateMgt
    {
        void GetState(  
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pPublic);

        void MoveToPublic();
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a52-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCSyncIO
    {
        void Read(
            OPCDATASOURCE  dwSource,
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)]  
            int[] phServer, 
		    [Out] 
            out IntPtr ppItemValues,
		    [Out] 
            out IntPtr ppErrors);

        void Write(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=0)]  
            object[] pItemValues, 
		    [Out] 
            out IntPtr ppErrors);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a53-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCAsyncIO
    {
        void Read(
            [MarshalAs(UnmanagedType.I4)]  
            int dwConnection,
            OPCDATASOURCE dwSource,
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=2)]  
            int[] phServer,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pTransactionID,
		    [Out] 
            out IntPtr ppErrors);

        void Write(
            [MarshalAs(UnmanagedType.I4)]  
            int dwConnection,
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)]  
            int[] phServer, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=1)]  
            object[] pItemValues, 
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pTransactionID,
		    [Out] 
            out IntPtr ppErrors);

        void Refresh(
            [MarshalAs(UnmanagedType.I4)]  
            int dwConnection,
            OPCDATASOURCE dwSource, 
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pTransactionID);

        void Cancel(
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransactionID);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a54-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCItemMgt
    {
        void AddItems( 
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)]  
            OPCITEMDEF[] pItemArray,
		    [Out] 
            out IntPtr ppAddResults,
		    [Out] 
            out IntPtr ppErrors);

        void ValidateItems( 
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)]  
            OPCITEMDEF[] pItemArray,
            [MarshalAs(UnmanagedType.I4)]  
            int bBlobUpdate,
		    [Out] 
            out IntPtr ppValidationResults,
		    [Out] 
            out IntPtr ppErrors);

        void RemoveItems( 
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
		    [Out] 
            out IntPtr ppErrors);

        void SetActiveState(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.I4)]  
            int  bActive, 
		    [Out] 
            out IntPtr ppErrors);

        void SetClientHandles(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phClient,
		    [Out] 
            out IntPtr ppErrors);

        void SetDatatypes(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I2, SizeParamIndex=0)]  
            short[] pRequestedDatatypes,
		    [Out] 
            out IntPtr ppErrors);

        void CreateEnumerator(
            ref Guid riid,
		    [Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex=0)] 
            out object ppUnk);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a55-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IEnumOPCItemAttributes
    {
        void Next( 
            [MarshalAs(UnmanagedType.I4)]  
            int celt,
		    [Out] 
            out IntPtr ppItemArray,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pceltFetched);

        void Skip( 
            [MarshalAs(UnmanagedType.I4)]  
            int celt);

        void Reset();

        void Clone( 
		    [Out] 
            out IEnumOPCItemAttributes ppEnumItemAttributes);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a70-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCDataCallback
    {
        void OnDataChange(
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransid, 
            [MarshalAs(UnmanagedType.I4)]  
            int hGroup, 
            [MarshalAs(UnmanagedType.I4)]  
            int hrMasterquality,
            [MarshalAs(UnmanagedType.I4)]  
            int hrMastererror,
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=4)]  
            int[] phClientItems, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=4)]  
            object[] pvValues, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I2, SizeParamIndex=4)]  
            short[] pwQualities,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=4)]  
            System.Runtime.InteropServices.ComTypes.FILETIME[] pftTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=4)]  
            int[] pErrors);

        void OnReadComplete(
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransid, 
            [MarshalAs(UnmanagedType.I4)]  
            int hGroup, 
            [MarshalAs(UnmanagedType.I4)]  
            int hrMasterquality,
            [MarshalAs(UnmanagedType.I4)]  
            int hrMastererror,
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=4)]  
            int[] phClientItems, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=4)]  
            object[] pvValues, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I2, SizeParamIndex=4)]  
            short[] pwQualities,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=4)]  
            System.Runtime.InteropServices.ComTypes.FILETIME[] pftTimeStamps,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=4)]  
            int[] pErrors);

        void OnWriteComplete(
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransid, 
            [MarshalAs(UnmanagedType.I4)]  
            int hGroup, 
            [MarshalAs(UnmanagedType.I4)]  
            int hrMastererr, 
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)]  
            int[] pClienthandles, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)]  
            int[] pErrors);

        void OnCancelComplete(
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransid, 
            [MarshalAs(UnmanagedType.I4)]  
            int hGroup);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a71-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCAsyncIO2
    {
        void Read(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransactionID,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pdwCancelID,
		    [Out] 
            out IntPtr ppErrors);

        void Write(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=0)]  
            object[] pItemValues, 
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransactionID,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pdwCancelID,
		    [Out] 
            out IntPtr ppErrors);

        void Refresh2(
            OPCDATASOURCE dwSource,
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransactionID,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pdwCancelID);

        void Cancel2(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCancelID);

        void SetEnable(
            [MarshalAs(UnmanagedType.I4)]  
            int bEnable);

        void GetEnable(
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pbEnable);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39c13a72-011e-11d0-9675-0020afd8adb3")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCItemProperties
    {
        void QueryAvailableProperties( 
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szItemID,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pdwCount,
		    [Out] 
            out IntPtr ppPropertyIDs,
		    [Out] 
            out IntPtr ppDescriptions,
		    [Out] 
            out IntPtr ppvtDataTypes);

        void GetItemProperties( 
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szItemID,
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)]  
            int[] pdwPropertyIDs,
		    [Out] 
            out IntPtr ppvData,
		    [Out] 
            out IntPtr ppErrors);

        void LookupItemIDs( 
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szItemID,
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)]  
            int[] pdwPropertyIDs,
		    [Out] 
            out IntPtr ppszNewItemIDs,
		    [Out] 
            out IntPtr ppErrors);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("5946DA93-8B39-4ec8-AB3D-AA73DF5BC86F")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCItemDeadbandMgt
    {
        void SetItemDeadband( 
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.R4, SizeParamIndex=0)]  
            float[] pPercentDeadband,
		    [Out] 
            out IntPtr ppErrors);

        void GetItemDeadband( 
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
		    [Out] 
            out IntPtr ppPercentDeadband,
		    [Out] 
            out IntPtr ppErrors);

        void ClearItemDeadband(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
		    [Out] 
            out IntPtr ppErrors);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("3E22D313-F08B-41a5-86C8-95E95CB49FFC")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCItemSamplingMgt
    {
        void SetItemSamplingRate(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] pdwRequestedSamplingRate,
		    [Out] 
            out IntPtr ppdwRevisedSamplingRate,
		    [Out] 
            out IntPtr ppErrors);

        void GetItemSamplingRate(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
		    [Out] 
            out IntPtr ppdwSamplingRate,
		    [Out] 
            out IntPtr ppErrors);

        void ClearItemSamplingRate(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
		    [Out] 
            out IntPtr ppErrors);

        void SetItemBufferEnable(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] pbEnable,
		    [Out] 
            out IntPtr ppErrors);

        void GetItemBufferEnable(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer, 
		    [Out] 
            out IntPtr ppbEnable,
		    [Out] 
            out IntPtr ppErrors);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("39227004-A18F-4b57-8B0A-5235670F4468")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCBrowse
    {
        void GetProperties( 
            [MarshalAs(UnmanagedType.I4)]  
            int dwItemCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr, SizeParamIndex=0)]  
            string[] pszItemIDs,
            [MarshalAs(UnmanagedType.I4)]  
            int bReturnPropertyValues,
            [MarshalAs(UnmanagedType.I4)]  
            int dwPropertyCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=3)]  
            int[] dwPropertyIDs,
		    [Out] 
            out IntPtr ppItemProperties);

        void Browse(
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szItemID,
            ref IntPtr pszContinuationPoint,
            [MarshalAs(UnmanagedType.I4)]  
            int dwMaxElementsReturned,
            OPCBROWSEFILTER dwBrowseFilter,
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szElementNameFilter,
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szVendorFilter,
            [MarshalAs(UnmanagedType.I4)]  
            int bReturnAllProperties,
            [MarshalAs(UnmanagedType.I4)]  
            int bReturnPropertyValues,
            [MarshalAs(UnmanagedType.I4)]  
            int dwPropertyCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=8)]  
            int[] pdwPropertyIDs,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pbMoreElements,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pdwCount,
		    [Out] 
            out IntPtr ppBrowseElements);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("85C0B427-2893-4cbc-BD78-E5FC5146F08F")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCItemIO
    {
        void Read(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr, SizeParamIndex=0)]  
            string[] pszItemIDs, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] pdwMaxAge,
		    [Out] 
            out IntPtr ppvValues,
		    [Out] 
            out IntPtr ppwQualities,
		    [Out] 
            out IntPtr ppftTimeStamps,
		    [Out] 
            out IntPtr ppErrors);

        void WriteVQT(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr, SizeParamIndex=0)]  
            string[] pszItemIDs,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)]  
            OPCITEMVQT[] pItemVQT,
		    [Out] 
            out IntPtr ppErrors);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("730F5F0F-55B1-4c81-9E18-FF8A0904E1FA")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCSyncIO2 // : IOPCSyncIO
    {  
        void Read(
            OPCDATASOURCE  dwSource,
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=1)]  
            int[] phServer, 
		    [Out] 
            out IntPtr ppItemValues,
		    [Out] 
            out IntPtr ppErrors);

        void Write(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=0)]  
            object[] pItemValues, 
		    [Out] 
            out IntPtr ppErrors);

        void ReadMaxAge(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] pdwMaxAge,
		    [Out] 
            out IntPtr ppvValues,
		    [Out] 
            out IntPtr ppwQualities,
		    [Out] 
            out IntPtr ppftTimeStamps,
		    [Out] 
            out IntPtr ppErrors);

        void WriteVQT(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)]  
            OPCITEMVQT[] pItemVQT,
		    [Out] 
            out IntPtr ppErrors);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("0967B97B-36EF-423e-B6F8-6BFF1E40D39D")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCAsyncIO3 // : IOPCAsyncIO2
    { 
        void Read(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransactionID,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pdwCancelID,
		    [Out] 
            out IntPtr ppErrors);

        void Write(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.Struct, SizeParamIndex=0)]  
            object[] pItemValues, 
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransactionID,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pdwCancelID,
		    [Out] 
            out IntPtr ppErrors);

        void Refresh2(
            OPCDATASOURCE dwSource,
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransactionID,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pdwCancelID);

        void Cancel2(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCancelID);

        void SetEnable(
            [MarshalAs(UnmanagedType.I4)]  
            int bEnable);

        void GetEnable(
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pbEnable);

        void ReadMaxAge(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] pdwMaxAge,
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransactionID,
		    [Out] 
            [MarshalAs(UnmanagedType.I4)]  
            out int pdwCancelID,
		    [Out] 
            out IntPtr ppErrors);

        void WriteVQT(
            [MarshalAs(UnmanagedType.I4)]  
            int dwCount, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I4, SizeParamIndex=0)]  
            int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)]  
            OPCITEMVQT[] pItemVQT,
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransactionID,
		    [Out] 
            [MarshalAs(UnmanagedType.I4)]  
            out int pdwCancelID,
		    [Out] 
            out IntPtr ppErrors);

        void RefreshMaxAge(
            [MarshalAs(UnmanagedType.I4)]  
            int dwMaxAge,
            [MarshalAs(UnmanagedType.I4)]  
            int dwTransactionID,
		    [Out] 
            [MarshalAs(UnmanagedType.I4)]  
            out int pdwCancelID);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("8E368666-D72E-4f78-87ED-647611C61C9F")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCGroupStateMgt2 // : IOPCGroupStateMgt
    { 
        void GetState(
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pUpdateRate, 
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pActive,
		    [Out][MarshalAs(UnmanagedType.LPWStr)]  
            out string ppName, 
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pTimeBias, 
		    [Out][MarshalAs(UnmanagedType.R4)]  
            out float pPercentDeadband,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pLCID,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int phClientGroup,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int phServerGroup);

        void SetState( 
            IntPtr pRequestedUpdateRate,  
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pRevisedUpdateRate, 
            IntPtr pActive, 
            IntPtr pTimeBias,
            IntPtr pPercentDeadband,
            IntPtr pLCID,
            IntPtr phClientGroup);

        void SetName( 
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szName);

        void CloneGroup(
            [MarshalAs(UnmanagedType.LPWStr)]  
            string szName, 
            ref Guid riid,
		    [Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex=1)] 
            out object ppUnk);

        void SetKeepAlive( 
            [MarshalAs(UnmanagedType.I4)]  
            int dwKeepAliveTime,
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pdwRevisedKeepAliveTime);

        void GetKeepAlive( 
		    [Out][MarshalAs(UnmanagedType.I4)]  
            out int pdwKeepAliveTime);
    }

    /// <exclude />
	public static class Constants
	{
		// category description strings.
		public const string OPC_CATEGORY_DESCRIPTION_DA10    = "OPC Data Access Servers Version 1.0";
		public const string OPC_CATEGORY_DESCRIPTION_DA20    = "OPC Data Access Servers Version 2.0";
		public const string OPC_CATEGORY_DESCRIPTION_DA30    = "OPC Data Access Servers Version 3.0";
		public const string OPC_CATEGORY_DESCRIPTION_XMLDA10 = "OPC XML Data Access Servers Version 1.0";

		// values for access rights mask.
		public const int OPC_READABLE           = 0x01;
		public const int OPC_WRITEABLE          = 0x02;	

		// values for browse element flags.
		public const int OPC_BROWSE_HASCHILDREN = 0x01;
		public const int OPC_BROWSE_ISITEM      = 0x02;

		// well known complex type description systems.   
		public const string OPC_TYPE_SYSTEM_OPCBINARY             = "OPCBinary";
		public const string OPC_TYPE_SYSTEM_XMLSCHEMA             = "XMLSchema";

		// complex data consitency window values.
		public const string OPC_CONSISTENCY_WINDOW_UNKNOWN        = "Unknown";
		public const string OPC_CONSISTENCY_WINDOW_NOT_CONSISTENT = "Not Consistent";

		// complex data write behavoir values.
		public const string OPC_WRITE_BEHAVIOR_BEST_EFFORT        = "Best Effort";
		public const string OPC_WRITE_BEHAVIOR_ALL_OR_NOTHING     = "All or Nothing";
	}

    /// <exclude />
	public static class Qualities
	{
		// Values for fields in the quality word
        public const short OPC_QUALITY_MASK                     = 0xC0;
        public const short OPC_STATUS_MASK                      = 0xFC;
		public const short OPC_LIMIT_MASK                       = 0x03;

		// Values for QUALITY_MASK bit field
		public const short OPC_QUALITY_BAD                      = 0x00;
		public const short OPC_QUALITY_UNCERTAIN                = 0x40;
		public const short OPC_QUALITY_GOOD                     = 0xC0;

		// STATUS_MASK Values for Quality = BAD
		public const short OPC_QUALITY_CONFIG_ERROR    		    = 0x04;
		public const short OPC_QUALITY_NOT_CONNECTED   		    = 0x08;
		public const short OPC_QUALITY_DEVICE_FAILURE  		    = 0x0c;
		public const short OPC_QUALITY_SENSOR_FAILURE  		    = 0x10;
		public const short OPC_QUALITY_LAST_KNOWN      		    = 0x14;
		public const short OPC_QUALITY_COMM_FAILURE    		    = 0x18;
		public const short OPC_QUALITY_OUT_OF_SERVICE  		    = 0x1C;
		public const short OPC_QUALITY_WAITING_FOR_INITIAL_DATA = 0x20;

		// STATUS_MASK Values for Quality = UNCERTAIN
		public const short OPC_QUALITY_LAST_USABLE              = 0x44;
		public const short OPC_QUALITY_SENSOR_CAL               = 0x50;
		public const short OPC_QUALITY_EGU_EXCEEDED             = 0x54;
		public const short OPC_QUALITY_SUB_NORMAL               = 0x58;

		// STATUS_MASK Values for Quality = GOOD
		public const short OPC_QUALITY_LOCAL_OVERRIDE           = 0xD8;

		// Values for Limit Bitfield 
		public const short OPC_LIMIT_OK                         = 0x00;
		public const short OPC_LIMIT_LOW                        = 0x01;
		public const short OPC_LIMIT_HIGH                       = 0x02;
		public const short OPC_LIMIT_CONST                      = 0x03;
	}

	//==========================================================================
    // Properties

    /// <exclude />
	public static class Properties
	{
		// property ids.
		public const int OPC_PROPERTY_DATATYPE            = 1;
		public const int OPC_PROPERTY_VALUE               = 2;
		public const int OPC_PROPERTY_QUALITY             = 3;
		public const int OPC_PROPERTY_TIMESTAMP           = 4;
		public const int OPC_PROPERTY_ACCESS_RIGHTS       = 5;
		public const int OPC_PROPERTY_SCAN_RATE           = 6;
		public const int OPC_PROPERTY_EU_TYPE             = 7;
		public const int OPC_PROPERTY_EU_INFO             = 8;
		public const int OPC_PROPERTY_EU_UNITS            = 100;
		public const int OPC_PROPERTY_DESCRIPTION         = 101;
		public const int OPC_PROPERTY_HIGH_EU             = 102;
		public const int OPC_PROPERTY_LOW_EU              = 103;
		public const int OPC_PROPERTY_HIGH_IR             = 104;
		public const int OPC_PROPERTY_LOW_IR              = 105;
		public const int OPC_PROPERTY_CLOSE_LABEL         = 106;
		public const int OPC_PROPERTY_OPEN_LABEL          = 107;
		public const int OPC_PROPERTY_TIMEZONE            = 108;
		public const int OPC_PROPERTY_CONDITION_STATUS    = 300;
		public const int OPC_PROPERTY_ALARM_QUICK_HELP    = 301;
		public const int OPC_PROPERTY_ALARM_AREA_LIST     = 302;
		public const int OPC_PROPERTY_PRIMARY_ALARM_AREA  = 303;
		public const int OPC_PROPERTY_CONDITION_LOGIC     = 304;
		public const int OPC_PROPERTY_LIMIT_EXCEEDED      = 305;
		public const int OPC_PROPERTY_DEADBAND            = 306;
		public const int OPC_PROPERTY_HIHI_LIMIT          = 307;
		public const int OPC_PROPERTY_HI_LIMIT            = 308;
		public const int OPC_PROPERTY_LO_LIMIT            = 309;
		public const int OPC_PROPERTY_LOLO_LIMIT          = 310;
		public const int OPC_PROPERTY_CHANGE_RATE_LIMIT   = 311;
		public const int OPC_PROPERTY_DEVIATION_LIMIT     = 312;
		public const int OPC_PROPERTY_SOUND_FILE          = 313;

		// complex data properties.
		public const int OPC_PROPERTY_TYPE_SYSTEM_ID      = 600;
		public const int OPC_PROPERTY_DICTIONARY_ID       = 601;
		public const int OPC_PROPERTY_TYPE_ID             = 602;
		public const int OPC_PROPERTY_DICTIONARY          = 603;
		public const int OPC_PROPERTY_TYPE_DESCRIPTION    = 604;
		public const int OPC_PROPERTY_CONSISTENCY_WINDOW  = 605;
		public const int OPC_PROPERTY_WRITE_BEHAVIOR      = 606;
		public const int OPC_PROPERTY_UNCONVERTED_ITEM_ID = 607;
		public const int OPC_PROPERTY_UNFILTERED_ITEM_ID  = 608;
		public const int OPC_PROPERTY_DATA_FILTER_VALUE   = 609;

		// property descriptions.
		public const string OPC_PROPERTY_DESC_DATATYPE            = "Item Canonical Data Type";
		public const string OPC_PROPERTY_DESC_VALUE               = "Item Value";
		public const string OPC_PROPERTY_DESC_QUALITY             = "Item Quality";
		public const string OPC_PROPERTY_DESC_TIMESTAMP           = "Item Timestamp";
		public const string OPC_PROPERTY_DESC_ACCESS_RIGHTS       = "Item Access Rights";
		public const string OPC_PROPERTY_DESC_SCAN_RATE           = "Server Scan Rate";
		public const string OPC_PROPERTY_DESC_EU_TYPE             = "Item EU Type";
		public const string OPC_PROPERTY_DESC_EU_INFO             = "Item EU Info";
		public const string OPC_PROPERTY_DESC_EU_UNITS            = "EU Units";
		public const string OPC_PROPERTY_DESC_DESCRIPTION         = "Item Description";
		public const string OPC_PROPERTY_DESC_HIGH_EU             = "High EU";
		public const string OPC_PROPERTY_DESC_LOW_EU              = "Low EU";
		public const string OPC_PROPERTY_DESC_HIGH_IR             = "High Instrument Range";
		public const string OPC_PROPERTY_DESC_LOW_IR              = "Low Instrument Range";
		public const string OPC_PROPERTY_DESC_CLOSE_LABEL         = "Contact Close Label";
		public const string OPC_PROPERTY_DESC_OPEN_LABEL          = "Contact Open Label";
		public const string OPC_PROPERTY_DESC_TIMEZONE            = "Item Timezone";
		public const string OPC_PROPERTY_DESC_CONDITION_STATUS    = "Condition Status";
		public const string OPC_PROPERTY_DESC_ALARM_QUICK_HELP    = "Alarm Quick Help";
		public const string OPC_PROPERTY_DESC_ALARM_AREA_LIST     = "Alarm Area List";
		public const string OPC_PROPERTY_DESC_PRIMARY_ALARM_AREA  = "Primary Alarm Area";
		public const string OPC_PROPERTY_DESC_CONDITION_LOGIC     = "Condition Logic";
		public const string OPC_PROPERTY_DESC_LIMIT_EXCEEDED      = "Limit Exceeded";
		public const string OPC_PROPERTY_DESC_DEADBAND            = "Deadband";
		public const string OPC_PROPERTY_DESC_HIHI_LIMIT          = "HiHi Limit";
		public const string OPC_PROPERTY_DESC_HI_LIMIT            = "Hi Limit";
		public const string OPC_PROPERTY_DESC_LO_LIMIT            = "Lo Limit";
		public const string OPC_PROPERTY_DESC_LOLO_LIMIT          = "LoLo Limit";
		public const string OPC_PROPERTY_DESC_CHANGE_RATE_LIMIT   = "Rate of Change Limit";
		public const string OPC_PROPERTY_DESC_DEVIATION_LIMIT     = "Deviation Limit";
		public const string OPC_PROPERTY_DESC_SOUND_FILE          = "Sound File";

		// complex data properties.
		public const string OPC_PROPERTY_DESC_TYPE_SYSTEM_ID      = "Type System ID";
		public const string OPC_PROPERTY_DESC_DICTIONARY_ID       = "Dictionary ID";
		public const string OPC_PROPERTY_DESC_TYPE_ID             = "Type ID";
		public const string OPC_PROPERTY_DESC_DICTIONARY          = "Dictionary";
		public const string OPC_PROPERTY_DESC_TYPE_DESCRIPTION    = "Type Description";
		public const string OPC_PROPERTY_DESC_CONSISTENCY_WINDOW  = "Consistency Window";
		public const string OPC_PROPERTY_DESC_WRITE_BEHAVIOR      = "Write Behavior";
		public const string OPC_PROPERTY_DESC_UNCONVERTED_ITEM_ID = "Unconverted Item ID";
		public const string OPC_PROPERTY_DESC_UNFILTERED_ITEM_ID  = "Unfiltered Item ID";
		public const string OPC_PROPERTY_DESC_DATA_FILTER_VALUE   = "Data Filter Value";
	}
}
