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

namespace OpcRcw.Comn
{   
    /// <exclude />
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]    
    public struct CONNECTDATA 
    {
        [MarshalAs(UnmanagedType.IUnknown)]
        object pUnk;
        [MarshalAs(UnmanagedType.I4)]
        int dwCookie;
    }

    /// <exclude />
    [ComImport]
    [GuidAttribute("B196B287-BAB4-101A-B69C-00AA00341D07")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IEnumConnections
    {
        void RemoteNext(
            [MarshalAs(UnmanagedType.I4)]
            int cConnections,
            [Out]
            IntPtr rgcd,
            [Out][MarshalAs(UnmanagedType.I4)]
            out int pcFetched);

        void Skip(
            [MarshalAs(UnmanagedType.I4)]
            int cConnections);

        void Reset();

        void Clone(
            [Out]
            out IEnumConnections ppEnum);
    }

    /// <exclude />
    [ComImport]
    [GuidAttribute("B196B286-BAB4-101A-B69C-00AA00341D07")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IConnectionPoint
    {
        void GetConnectionInterface(
            [Out]
            out Guid pIID);

        void GetConnectionPointContainer(
            [Out]
            out IConnectionPointContainer ppCPC);

        void Advise(
            [MarshalAs(UnmanagedType.IUnknown)]
            object pUnkSink,
            [Out][MarshalAs(UnmanagedType.I4)]
            out int pdwCookie);

        void Unadvise(
            [MarshalAs(UnmanagedType.I4)]
            int dwCookie);

        void EnumConnections(
            [Out]
            out IEnumConnections ppEnum);
    }

    /// <exclude />
    [ComImport]
    [GuidAttribute("B196B285-BAB4-101A-B69C-00AA00341D07")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IEnumConnectionPoints 
    {
        void RemoteNext(
            [MarshalAs(UnmanagedType.I4)]
            int cConnections,
            [Out]
            IntPtr ppCP,
            [Out][MarshalAs(UnmanagedType.I4)]
            out int pcFetched);

        void Skip(
            [MarshalAs(UnmanagedType.I4)]
            int cConnections);

        void Reset();

        void Clone(
            [Out]
            out IEnumConnectionPoints ppEnum);
    }

    /// <exclude />
    [ComImport]
    [GuidAttribute("B196B284-BAB4-101A-B69C-00AA00341D07")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IConnectionPointContainer
    {
        void EnumConnectionPoints(
            [Out]
            out IEnumConnectionPoints ppEnum);

        void FindConnectionPoint(
            ref Guid riid,
            [Out]
            out IConnectionPoint ppCP);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("F31DFDE1-07B6-11d2-B2D8-0060083BA1FB")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCShutdown
    {
        void ShutdownRequest(
			[MarshalAs(UnmanagedType.LPWStr)]
			string szReason);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("F31DFDE2-07B6-11d2-B2D8-0060083BA1FB")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
	public interface IOPCCommon 
	{
		void SetLocaleID(
			[MarshalAs(UnmanagedType.I4)]
			int dwLcid);

		void GetLocaleID(
			[Out][MarshalAs(UnmanagedType.I4)]
			out int pdwLcid);

		void QueryAvailableLocaleIDs( 
			[Out][MarshalAs(UnmanagedType.I4)]
			out int pdwCount,	
			[Out]
			out IntPtr pdwLcid);

		void GetErrorString( 
			[MarshalAs(UnmanagedType.I4)]
			int dwError,
			[Out][MarshalAs(UnmanagedType.LPWStr)]
			out String ppString);

		void SetClientName(
			[MarshalAs(UnmanagedType.LPWStr)] 
			String szName);
	}

    /// <exclude />
	[ComImport]
	[GuidAttribute("13486D50-4821-11D2-A494-3CB306C10000")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
	public interface IOPCServerList 
    {
        void EnumClassesOfCategories(
		    [MarshalAs(UnmanagedType.I4)]
            int cImplemented,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)]
            Guid[] rgcatidImpl,
		    [MarshalAs(UnmanagedType.I4)]
            int cRequired,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=2)]
            Guid[] rgcatidReq,
		    [Out][MarshalAs(UnmanagedType.IUnknown)]
            out object ppenumClsid);

        void GetClassDetails(
            ref Guid clsid, 
            [Out][MarshalAs(UnmanagedType.LPWStr)]
            out string ppszProgID,
            [Out][MarshalAs(UnmanagedType.LPWStr)]
            out string ppszUserType);

        void CLSIDFromProgID(
		    [MarshalAs(UnmanagedType.LPWStr)]
            string szProgId,
            [Out]
            out Guid clsid);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("55C382C8-21C7-4e88-96C1-BECFB1E3F483")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCEnumGUID 
    {
        void Next(
		    [MarshalAs(UnmanagedType.I4)]
            int celt,
            [Out]
            IntPtr rgelt,
            [Out][MarshalAs(UnmanagedType.I4)]
            out int pceltFetched);

        void Skip(
		    [MarshalAs(UnmanagedType.I4)]
            int celt);

        void Reset();

        void Clone(
            [Out]
            out IOPCEnumGUID ppenum);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("0002E000-0000-0000-C000-000000000046")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IEnumGUID 
    {
        void Next(
		    [MarshalAs(UnmanagedType.I4)]
            int celt,
            [Out]
            IntPtr rgelt,
            [Out][MarshalAs(UnmanagedType.I4)]
            out int pceltFetched);

        void Skip(
		    [MarshalAs(UnmanagedType.I4)]
            int celt);

        void Reset();

        void Clone(
            [Out]
            out IEnumGUID ppenum);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("00000100-0000-0000-C000-000000000046")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IEnumUnknown 
    {
        void RemoteNext(
		    [MarshalAs(UnmanagedType.I4)]
            int celt,
            [Out]
            IntPtr rgelt,
            [Out][MarshalAs(UnmanagedType.I4)]
            out int pceltFetched);

        void Skip(
		    [MarshalAs(UnmanagedType.I4)]
            int celt);

        void Reset();

        void Clone(
            [Out]
            out IEnumUnknown ppenum);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("00000101-0000-0000-C000-000000000046")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IEnumString 
    {
        [PreserveSig]
        int RemoteNext(
		    [MarshalAs(UnmanagedType.I4)]
            int celt,
            IntPtr rgelt,
            [Out][MarshalAs(UnmanagedType.I4)]
            out int pceltFetched);

        void Skip(
		    [MarshalAs(UnmanagedType.I4)]
            int celt);

        void Reset();

        void Clone(
            [Out]
            out IEnumString ppenum);
    }

    /// <exclude />
	[ComImport]
	[GuidAttribute("9DD0B56C-AD9E-43ee-8305-487F3188BF7A")]
	[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
    public interface IOPCServerList2
    {
        void EnumClassesOfCategories(
            [MarshalAs(UnmanagedType.I4)]
            int cImplemented,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)]
            Guid[] rgcatidImpl,
            [MarshalAs(UnmanagedType.I4)]
            int cRequired,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStruct, SizeParamIndex=0)]
            Guid[] rgcatidReq,
            [Out]
            out IOPCEnumGUID ppenumClsid);

        void GetClassDetails(
            ref Guid clsid, 
		    [Out][MarshalAs(UnmanagedType.LPWStr)]
            out string ppszProgID,
            [Out][MarshalAs(UnmanagedType.LPWStr)]
            out string ppszUserType,
            [Out][MarshalAs(UnmanagedType.LPWStr)]
            out string ppszVerIndProgID);

        void CLSIDFromProgID(
		    [MarshalAs(UnmanagedType.LPWStr)]
            string szProgId,
            [Out]
            out Guid clsid);
    }
}
