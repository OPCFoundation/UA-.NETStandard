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
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using System.Net;
using System.Net.Sockets;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;

using OpcRcw.Hda;

namespace Opc.Ua.Com
{
	/// <summary>
	/// Exposes WIN32 and COM API functions.
	/// </summary>
	public class ComUtils
	{
		#region NetApi Function Declarations
		[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
			private struct SERVER_INFO_100
		{
			public uint   sv100_platform_id;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string sv100_name;
		} 	

		private const uint LEVEL_SERVER_INFO_100 = 100;
		private const uint LEVEL_SERVER_INFO_101 = 101;

		private const int  MAX_PREFERRED_LENGTH  = -1;

		private const uint SV_TYPE_WORKSTATION   = 0x00000001;
		private const uint SV_TYPE_SERVER        = 0x00000002;

		[DllImport("Netapi32.dll")]
		private static extern int NetServerEnum(
			IntPtr     servername,
			uint       level,
			out IntPtr bufptr,
			int        prefmaxlen,
			out int    entriesread,
			out int    totalentries,
			uint       servertype,
			IntPtr     domain,
			IntPtr     resume_handle);

		[DllImport("Netapi32.dll")]	
		private static extern int NetApiBufferFree(IntPtr buffer);

		/// <summary>
		/// Enumerates computers on the local network.
		/// </summary>
		public static string[] EnumComputers()
		{
			IntPtr pInfo;

			int entriesRead = 0;
			int totalEntries = 0;

			int result = NetServerEnum(
				IntPtr.Zero,
				LEVEL_SERVER_INFO_100,
				out pInfo,
				MAX_PREFERRED_LENGTH,
				out entriesRead,
				out totalEntries,
				SV_TYPE_WORKSTATION | SV_TYPE_SERVER,
				IntPtr.Zero,
				IntPtr.Zero);		

			if (result != 0)
			{
				throw new ApplicationException("NetApi Error = " + String.Format("0x{0:X8}", result));
			}

			string[] computers = new string[entriesRead];

			IntPtr pos = pInfo;

			for (int ii = 0; ii < entriesRead; ii++)
			{
				SERVER_INFO_100 info = (SERVER_INFO_100)Marshal.PtrToStructure(pos, typeof(SERVER_INFO_100));
				
				computers[ii] = info.sv100_name;

				pos = (IntPtr)(pos.ToInt64() + Marshal.SizeOf(typeof(SERVER_INFO_100)));
			}

			NetApiBufferFree(pInfo);

			return computers;
		}
		#endregion

		#region OLE32 Function/Interface Declarations
		private const int MAX_MESSAGE_LENGTH = 1024;

		private const uint FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200;
		private const uint FORMAT_MESSAGE_FROM_SYSTEM    = 0x00001000;

		[DllImport("Kernel32.dll")]
		private static extern int FormatMessageW(
			int    dwFlags,
			IntPtr lpSource,
			int    dwMessageId,
			int    dwLanguageId,
			IntPtr lpBuffer,
			int    nSize,
			IntPtr Arguments);
        
		[DllImport("Kernel32.dll")]
        private static extern int GetSystemDefaultLangID();
                
		[DllImport("Kernel32.dll")]
        private static extern int GetUserDefaultLangID();

		/// <summary>
		/// The WIN32 system default locale.
		/// </summary>
		public const int LOCALE_SYSTEM_DEFAULT = 0x800;

		/// <summary>
		/// The WIN32 user default locale.
		/// </summary>
		public const int LOCALE_USER_DEFAULT   = 0x400; 

		/// <summary>
		/// The base for the WIN32 FILETIME structure.
		/// </summary>
		private static readonly DateTime FILETIME_BaseTime = new DateTime(1601, 1, 1);

		/// <summary>
		/// WIN32 GUID struct declaration.
		/// </summary>
		[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
			private struct GUID
		{
			public int Data1;
			public short Data2;
			public short Data3;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst=8)]
			public byte[] Data4;
		}

        /// <summary>
        /// The size, in bytes, of a VARIANT structure.
        /// </summary>
        private static int VARIANT_SIZE { get { return (IntPtr.Size > 4) ? 0x18 : 0x10; } }
                
		[DllImport("OleAut32.dll")]	
		private static extern int VariantChangeTypeEx( 
			IntPtr pvargDest,  
			IntPtr pvarSrc,  
			int    lcid,             
			ushort wFlags,  
			short  vt);
        
		/// <summary>
		/// Intializes a pointer to a VARIANT.
		/// </summary>
		[DllImport("oleaut32.dll")]
		private static extern void VariantInit(IntPtr pVariant);
        
		/// <summary>
		/// Frees all memory referenced by a VARIANT stored in unmanaged memory.
		/// </summary>
		[DllImport("oleaut32.dll")]
		public static extern void VariantClear(IntPtr pVariant);

		private const int DISP_E_TYPEMISMATCH = -0x7FFDFFFB; // 0x80020005
		private const int DISP_E_OVERFLOW     = -0x7FFDFFF6; // 0x8002000A

		private const int VARIANT_NOVALUEPROP = 0x01;
		private const int VARIANT_ALPHABOOL   = 0x02; // For VT_BOOL to VT_BSTR conversions convert to "True"/"False" instead of

		[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
			private struct SOLE_AUTHENTICATION_SERVICE
		{
			public uint   dwAuthnSvc;
			public uint   dwAuthzSvc;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string pPrincipalName;
			public int    hr;
		} 	
		
		private const uint RPC_C_AUTHN_NONE                = 0;
		private const uint RPC_C_AUTHN_DCE_PRIVATE         = 1;
		private const uint RPC_C_AUTHN_DCE_PUBLIC          = 2;
		private const uint RPC_C_AUTHN_DEC_PUBLIC          = 4;
		private const uint RPC_C_AUTHN_GSS_NEGOTIATE       = 9;
		private const uint RPC_C_AUTHN_WINNT               = 10;
		private const uint RPC_C_AUTHN_GSS_SCHANNEL        = 14;
		private const uint RPC_C_AUTHN_GSS_KERBEROS        = 16;
		private const uint RPC_C_AUTHN_DPA                 = 17;
		private const uint RPC_C_AUTHN_MSN                 = 18;
		private const uint RPC_C_AUTHN_DIGEST              = 21;
		private const uint RPC_C_AUTHN_MQ                  = 100;
		private const uint RPC_C_AUTHN_DEFAULT             = 0xFFFFFFFF;

		private const uint RPC_C_AUTHZ_NONE                = 0;
		private const uint RPC_C_AUTHZ_NAME                = 1;
		private const uint RPC_C_AUTHZ_DCE                 = 2;
		private const uint RPC_C_AUTHZ_DEFAULT             = 0xffffffff;

		private const uint RPC_C_AUTHN_LEVEL_DEFAULT       = 0;
		private const uint RPC_C_AUTHN_LEVEL_NONE          = 1;
		private const uint RPC_C_AUTHN_LEVEL_CONNECT       = 2;
		private const uint RPC_C_AUTHN_LEVEL_CALL          = 3;
		private const uint RPC_C_AUTHN_LEVEL_PKT           = 4;
		private const uint RPC_C_AUTHN_LEVEL_PKT_INTEGRITY = 5;
		private const uint RPC_C_AUTHN_LEVEL_PKT_PRIVACY   = 6;

		private const uint RPC_C_IMP_LEVEL_ANONYMOUS       = 1;
		private const uint RPC_C_IMP_LEVEL_IDENTIFY        = 2;
		private const uint RPC_C_IMP_LEVEL_IMPERSONATE     = 3;
		private const uint RPC_C_IMP_LEVEL_DELEGATE        = 4;
		
		private const uint EOAC_NONE	                   = 0x00;
		private const uint EOAC_MUTUAL_AUTH                = 0x01;
		private const uint EOAC_CLOAKING                   = 0x10;
		private const uint EOAC_STATIC_CLOAKING            = 0x20;
		private const uint EOAC_DYNAMIC_CLOAKING           = 0x40;
		private const uint EOAC_SECURE_REFS	               = 0x02;
		private const uint EOAC_ACCESS_CONTROL             = 0x04;
		private const uint EOAC_APPID	                   = 0x08;

		[DllImport("ole32.dll")]
		private static extern int CoInitializeSecurity(
			IntPtr                        pSecDesc,
			int                           cAuthSvc,
			SOLE_AUTHENTICATION_SERVICE[] asAuthSvc,
			IntPtr                        pReserved1,
			uint                          dwAuthnLevel,
			uint                          dwImpLevel,
			IntPtr                        pAuthList,
			uint                          dwCapabilities,
			IntPtr                        pReserved3);

		[DllImport("ole32.dll")]
		private static extern int CoQueryProxyBlanket(
			[MarshalAs(UnmanagedType.IUnknown)]
			object pProxy,
			ref uint pAuthnSvc,
			ref uint pAuthzSvc,
			[MarshalAs(UnmanagedType.LPWStr)]
			ref string pServerPrincName,
			ref uint pAuthnLevel, 
			ref uint pImpLevel, 
			ref IntPtr pAuthInfo,
			ref uint pCapabilities); 

		[DllImport("ole32.dll")]
		private static extern int CoSetProxyBlanket(
			[MarshalAs(UnmanagedType.IUnknown)]
			object pProxy,
			uint pAuthnSvc,
            uint pAuthzSvc,
			IntPtr pServerPrincName,
			uint pAuthnLevel, 
			uint pImpLevel, 
			IntPtr pAuthInfo,
			uint pCapabilities); 
        
        private static readonly IntPtr COLE_DEFAULT_PRINCIPAL = new IntPtr(-1);
        private static readonly IntPtr COLE_DEFAULT_AUTHINFO = new IntPtr(-1);

		[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
		private struct COSERVERINFO
		{
			public uint         dwReserved1;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string       pwszName;
			public IntPtr       pAuthInfo;
			public uint         dwReserved2;
		};

		[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
		private struct COAUTHINFO
		{
			public uint   dwAuthnSvc;
			public uint   dwAuthzSvc;
			public IntPtr pwszServerPrincName;
			public uint   dwAuthnLevel;
			public uint   dwImpersonationLevel;
			public IntPtr pAuthIdentityData;
			public uint   dwCapabilities;
		}

		[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
		private struct COAUTHIDENTITY
		{
			public IntPtr User;
			public uint   UserLength;
			public IntPtr Domain;
			public uint   DomainLength;
			public IntPtr Password;
			public uint   PasswordLength;
			public uint   Flags;
		}

		[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
		private struct MULTI_QI
		{
			public IntPtr iid;
			[MarshalAs(UnmanagedType.IUnknown)]
			public object pItf;
			public uint   hr;
		}
		
		private const uint CLSCTX_INPROC_SERVER	 = 0x1;
		private const uint CLSCTX_INPROC_HANDLER = 0x2;
		private const uint CLSCTX_LOCAL_SERVER	 = 0x4;
		private const uint CLSCTX_REMOTE_SERVER	 = 0x10;
		private const uint CLSCTX_DISABLE_AAA 	 = 0x8000;

		private static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
		
		private const uint SEC_WINNT_AUTH_IDENTITY_ANSI    = 0x1;
		private const uint SEC_WINNT_AUTH_IDENTITY_UNICODE = 0x2;

		[DllImport("ole32.dll")]
		private static extern void CoCreateInstanceEx(
			ref Guid         clsid,
			[MarshalAs(UnmanagedType.IUnknown)]
			object           punkOuter,
			uint             dwClsCtx,
			[In]
			ref COSERVERINFO pServerInfo,
			uint             dwCount,
			[In, Out]
			MULTI_QI[]       pResults);

		[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
		private struct LICINFO
		{
			public int cbLicInfo; 
			[MarshalAs(UnmanagedType.Bool)]
			public bool fRuntimeKeyAvail;
			[MarshalAs(UnmanagedType.Bool)]
			public bool fLicVerified;
		}

		[ComImport]
		[GuidAttribute("00000001-0000-0000-C000-000000000046")]
		[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
		private interface IClassFactory 
		{
			void CreateInstance(
				[MarshalAs(UnmanagedType.IUnknown)]
				object punkOuter,
				[MarshalAs(UnmanagedType.LPStruct)] 
				Guid riid, 
				[MarshalAs(UnmanagedType.Interface)]
				[Out] out object ppvObject); 

			void LockServer(
				[MarshalAs(UnmanagedType.Bool)]
				bool fLock);
		}

		[ComImport]
		[GuidAttribute("B196B28F-BAB4-101A-B69C-00AA00341D07")]
		[InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)] 
		private interface IClassFactory2 
		{
			void CreateInstance(
				[MarshalAs(UnmanagedType.IUnknown)]
				object punkOuter,
				[MarshalAs(UnmanagedType.LPStruct)] 
				Guid riid, 
				[MarshalAs(UnmanagedType.Interface)]
				[Out] out object ppvObject); 

			void LockServer(
				[MarshalAs(UnmanagedType.Bool)]
				bool fLock);

			void GetLicInfo(
				[In,Out] ref LICINFO pLicInfo);

			void RequestLicKey(
				int dwReserved, 
				[MarshalAs(UnmanagedType.BStr)]
				string pbstrKey);
		 
			void CreateInstanceLic(
				[MarshalAs(UnmanagedType.IUnknown)]
				object punkOuter,
				[MarshalAs(UnmanagedType.IUnknown)]
				object punkReserved,
				[MarshalAs(UnmanagedType.LPStruct)]  
				Guid riid, 
				[MarshalAs(UnmanagedType.BStr)]
				string bstrKey,
				[MarshalAs(UnmanagedType.IUnknown)]
				[Out] out object ppvObject);		
		}

		[DllImport("ole32.dll")]
		private static extern void CoGetClassObject(
			[MarshalAs(UnmanagedType.LPStruct)] 
			Guid clsid,
			uint dwClsContext,
			[In] ref COSERVERINFO pServerInfo,
			[MarshalAs(UnmanagedType.LPStruct)] 
			Guid riid,
			[MarshalAs(UnmanagedType.IUnknown)]
			[Out] out object ppv); 

		private const int LOGON32_PROVIDER_DEFAULT  = 0;
		private const int LOGON32_LOGON_INTERACTIVE = 2;
		private const int LOGON32_LOGON_NETWORK     = 3;

		private const int SECURITY_ANONYMOUS      = 0;
		private const int SECURITY_IDENTIFICATION = 1;
		private const int SECURITY_IMPERSONATION  = 2;
		private const int SECURITY_DELEGATION     = 3;

		[DllImport("advapi32.dll", SetLastError=true)]
		private static extern bool LogonUser(
			string     lpszUsername, 
			string     lpszDomain, 
			string     lpszPassword, 
			int        dwLogonType, 
			int        dwLogonProvider, 
			ref IntPtr phToken);

		[DllImport("kernel32.dll", CharSet=CharSet.Auto)]
		private extern static bool CloseHandle(IntPtr handle);

		[DllImport("advapi32.dll", CharSet=CharSet.Auto, SetLastError=true)]
		private extern static bool DuplicateToken(
			IntPtr ExistingTokenHandle, 
			int SECURITY_IMPERSONATION_LEVEL, 
			ref IntPtr DuplicateTokenHandle);
		#endregion

		#region ServerInfo Class
		/// <summary>
		/// A class used to allocate and deallocate the elements of a COSERVERINFO structure.
		/// </summary>
		class ServerInfo
		{
			#region Public Interface
			/// <summary>
			/// Allocates a COSERVERINFO structure.
			/// </summary>
			public COSERVERINFO Allocate(string hostName, UserIdentity identity)
			{
				// initialize server info structure.
				COSERVERINFO serverInfo = new COSERVERINFO();

				serverInfo.pwszName     = hostName;
				serverInfo.pAuthInfo    = IntPtr.Zero;
				serverInfo.dwReserved1  = 0;
				serverInfo.dwReserved2  = 0;
                
                // no authentication for default identity
				if (UserIdentity.IsDefault(identity))
                {
					return serverInfo;
                }

				m_hUserName  = GCHandle.Alloc(identity.Username, GCHandleType.Pinned);
				m_hPassword  = GCHandle.Alloc(identity.Password, GCHandleType.Pinned);
				m_hDomain    = GCHandle.Alloc(identity.Domain,   GCHandleType.Pinned);

				m_hIdentity = new GCHandle();

				// create identity structure.
				COAUTHIDENTITY authIdentity = new COAUTHIDENTITY();

				authIdentity.User           = m_hUserName.AddrOfPinnedObject();
				authIdentity.UserLength     = (uint)((identity.Username != null)?identity.Username.Length:0);
				authIdentity.Password       = m_hPassword.AddrOfPinnedObject();
				authIdentity.PasswordLength = (uint)((identity.Password != null)?identity.Password.Length:0);
				authIdentity.Domain         = m_hDomain.AddrOfPinnedObject();
				authIdentity.DomainLength   = (uint)((identity.Domain != null)?identity.Domain.Length:0);
				authIdentity.Flags          = SEC_WINNT_AUTH_IDENTITY_UNICODE;

				m_hIdentity = GCHandle.Alloc(authIdentity, GCHandleType.Pinned);
					
				// create authorization info structure.
				COAUTHINFO authInfo = new COAUTHINFO();

				authInfo.dwAuthnSvc           = RPC_C_AUTHN_WINNT;
				authInfo.dwAuthzSvc           = RPC_C_AUTHZ_NONE;
				authInfo.pwszServerPrincName  = IntPtr.Zero;
				authInfo.dwAuthnLevel         = RPC_C_AUTHN_LEVEL_CONNECT;
				authInfo.dwImpersonationLevel = RPC_C_IMP_LEVEL_IMPERSONATE;
				authInfo.pAuthIdentityData    = m_hIdentity.AddrOfPinnedObject();
                authInfo.dwCapabilities       = EOAC_NONE; // EOAC_DYNAMIC_CLOAKING;

				m_hAuthInfo = GCHandle.Alloc(authInfo, GCHandleType.Pinned);
			
				// update server info structure.
				serverInfo.pAuthInfo = m_hAuthInfo.AddrOfPinnedObject();

				return serverInfo;
			}

			/// <summary>
			/// Deallocated memory allocated when the COSERVERINFO structure was created.
			/// </summary>
			public void Deallocate()
			{
				if (m_hUserName.IsAllocated) m_hUserName.Free();
				if (m_hPassword.IsAllocated) m_hPassword.Free();
				if (m_hDomain.IsAllocated)   m_hDomain.Free();
				if (m_hIdentity.IsAllocated) m_hIdentity.Free();
				if (m_hAuthInfo.IsAllocated) m_hAuthInfo.Free();
			}
			#endregion

			#region Private Members
			private GCHandle m_hUserName;
			private GCHandle m_hPassword;
			private GCHandle m_hDomain;
			private GCHandle m_hIdentity;
			private GCHandle m_hAuthInfo;
			#endregion
		}
		#endregion

		#region Initialization Functions
		/// <summary>
		/// Initializes COM security.
		/// </summary>
		public static void InitializeSecurity()
		{
			int error = CoInitializeSecurity(
				IntPtr.Zero,
				-1,
				null,
				IntPtr.Zero,
				RPC_C_AUTHN_LEVEL_CONNECT,
				RPC_C_IMP_LEVEL_IMPERSONATE,
				IntPtr.Zero,
				EOAC_DYNAMIC_CLOAKING,
				IntPtr.Zero);		

            // this call will fail in the debugger if the 
            // 'Debug | Enable Visual Studio Hosting Process'  
            // option is checked in the project properties. 
			if (error != 0)
			{
				// throw new ExternalException("CoInitializeSecurity: " + GetSystemMessage(error), error);
			}
		}

		/// <summary>
		/// Determines if the host is the local host.
		/// </summary>
		private static bool IsLocalHost(string hostName)
		{
			// lookup requested host.
		    IPHostEntry requestedHost = Dns.GetHostEntry(hostName);

		    if (requestedHost == null || requestedHost.AddressList == null)
		    {
			    return true;
		    }

	        // check for loopback.
	        for (int ii = 0; ii < requestedHost.AddressList.Length; ii++)
	        {
		        IPAddress requestedIP = requestedHost.AddressList[ii];

		        if (requestedIP == null || requestedIP.Equals(IPAddress.Loopback))
		        {
			        return true;
		        }
	        }

	        // lookup local host.
	        IPHostEntry localHost = Dns.GetHostEntry(Dns.GetHostName());

	        if (localHost == null || localHost.AddressList == null)
	        {
		        return false;
	        }
    		
	        // check for localhost.
	        for (int ii = 0; ii < requestedHost.AddressList.Length; ii++)
	        {
		        IPAddress requestedIP = requestedHost.AddressList[ii];

		        for (int jj = 0; jj < localHost.AddressList.Length; jj++)
		        {
			        if (requestedIP.Equals(localHost.AddressList[jj]))
			        {
				        return true;
			        }
		        }
	        }

	        // must be remote.
	        return false;
		}

		/// <summary>
		/// Creates an instance of a COM server using the specified license key.
		/// </summary>
		public static object CreateInstance(Guid clsid, string hostName, UserIdentity identity)
		{			
			return CreateInstance1(clsid, hostName, identity);
		}

		/// <summary>
		/// Creates an instance of a COM server.
		/// </summary>
        public static object CreateInstance1(Guid clsid, string hostName, UserIdentity identity)
		{
			ServerInfo   serverInfo   = new ServerInfo();
			COSERVERINFO coserverInfo = serverInfo.Allocate(hostName, identity);

			GCHandle hIID = GCHandle.Alloc(IID_IUnknown, GCHandleType.Pinned);

			MULTI_QI[] results = new MULTI_QI[1];

			results[0].iid  = hIID.AddrOfPinnedObject();
			results[0].pItf = null;
			results[0].hr   = 0;

			try
			{
				// check whether connecting locally or remotely.
				uint clsctx = CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER;

				if (!String.IsNullOrEmpty(hostName) && hostName != "localhost")
				{
					clsctx = CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER;
				}

				// create an instance.
				CoCreateInstanceEx(
					ref clsid,
					null,
					clsctx,
					ref coserverInfo,
					1,
					results);
			}
			finally
			{
				if (hIID.IsAllocated) hIID.Free();
				serverInfo.Deallocate();
			}

			if (results[0].hr != 0)
			{
                throw ServiceResultException.Create(
                    StatusCodes.BadCommunicationError, 
                    "Could not create COM server '{0}' on host '{1}'. Reason: {2}.", clsid, hostName, 
                    GetSystemMessage((int)results[0].hr, LOCALE_SYSTEM_DEFAULT));
			}

			return results[0].pItf;
		}

        // COM impersonation is a nice feature but variations between behavoirs on different
        // windows platforms make it virtually impossible to support. This code is left here 
        // in case it becomes a critical requirement in the future.
        #if COM_IMPERSONATION_SUPPORT
        /// <summary>
        /// Returns the WindowsIdentity associated with a UserIdentity.
        /// </summary>
        public static WindowsPrincipal GetPrincipalFromUserIdentity(UserIdentity user)
        {
            if (UserIdentity.IsDefault(user))
            {
                return null;
            }

            // validate the credentials.
		    IntPtr token = IntPtr.Zero;

		    bool result = LogonUser(
                user.Username,
                user.Domain,
                user.Password, 
			    LOGON32_LOGON_NETWORK, 
			    LOGON32_PROVIDER_DEFAULT,
			    ref token);

		    if (!result)
		    {                    
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected, 
                    "Could not logon as user '{0}'. Reason: {1}.", 
                    user.Username, 
                    GetSystemMessage(Marshal.GetLastWin32Error(), LOCALE_SYSTEM_DEFAULT));
		    }
            
            try
            {
                // create the windows identity.
                WindowsIdentity identity = new WindowsIdentity(token);

                // validate the identity.
                identity.Impersonate();

                // return a principal.
                return new WindowsPrincipal(identity);
            }
            finally
            {                    
			    CloseHandle(token);
            }
        }

        /// <summary>
        /// Sets the security settings for the proxy.
        /// </summary>
        public static void SetProxySecurity(object server, UserIdentity user)
        {
            // allocate the 
            GCHandle hUserName = GCHandle.Alloc(user.Username, GCHandleType.Pinned);
            GCHandle hPassword = GCHandle.Alloc(user.Password, GCHandleType.Pinned);
            GCHandle hDomain   = GCHandle.Alloc(user.Domain,   GCHandleType.Pinned);

            GCHandle hIdentity = new GCHandle();

            // create identity structure.
            COAUTHIDENTITY authIdentity = new COAUTHIDENTITY();

            authIdentity.User           = hUserName.AddrOfPinnedObject();
            authIdentity.UserLength     = (uint)((user.Username != null) ? user.Username.Length : 0);
            authIdentity.Password       = hPassword.AddrOfPinnedObject();
            authIdentity.PasswordLength = (uint)((user.Password != null) ? user.Password.Length : 0);
            authIdentity.Domain         = hDomain.AddrOfPinnedObject();
            authIdentity.DomainLength   = (uint)((user.Domain != null) ? user.Domain.Length : 0);
            authIdentity.Flags          = SEC_WINNT_AUTH_IDENTITY_UNICODE;

            hIdentity = GCHandle.Alloc(authIdentity, GCHandleType.Pinned);
            
            try
            {
                SetProxySecurity(server, hIdentity.AddrOfPinnedObject());
            }
            finally
            {
                hUserName.Free();
                hPassword.Free();
                hDomain.Free();
                hIdentity.Free();
            }
        }

        /// <summary>
        /// Sets the security settings for the proxy.
        /// </summary>
        public static void SetProxySecurity(object server, IntPtr pAuthInfo)
        {
            // get the existing proxy settings.
            uint pAuthnSvc = 0;
            uint pAuthzSvc = 0;
            string pServerPrincName = "";
            uint pAuthnLevel = 0;
            uint pImpLevel = 0;
            IntPtr pAuthInfo2 = IntPtr.Zero;
            uint pCapabilities = 0;

            CoQueryProxyBlanket(
                server,
                ref pAuthnSvc,
                ref pAuthzSvc,
                ref pServerPrincName,
                ref pAuthnLevel,
                ref pImpLevel,
                ref pAuthInfo2,
                ref pCapabilities);

            pAuthnSvc = RPC_C_AUTHN_WINNT;
            pAuthzSvc = RPC_C_AUTHZ_NONE;
            pAuthnLevel = RPC_C_AUTHN_LEVEL_CONNECT;
            pImpLevel = RPC_C_IMP_LEVEL_IMPERSONATE;
            pCapabilities = EOAC_DYNAMIC_CLOAKING;
            
            // update proxy security settings.
            CoSetProxyBlanket(
                server,
                pAuthnSvc,
                pAuthzSvc,
                COLE_DEFAULT_PRINCIPAL,
                pAuthnLevel,
                pImpLevel,
                pAuthInfo,
                pCapabilities);
        }
		
		/// <summary>
		/// Creates an instance of a COM server using the specified license key.
		/// </summary>
		public static object CreateInstance2(Guid clsid, string hostName, UserIdentity identity)
		{
            // validate the host name before proceeding (exception thrown if host is not valid).
            bool isLocalHost = IsLocalHost(hostName);

            // allocate the connection info.
			ServerInfo    serverInfo   = new ServerInfo();
			COSERVERINFO  coserverInfo = serverInfo.Allocate(hostName, identity);
			object        instance     = null; 
			IClassFactory factory      = null; 
			
			try
			{
                // create the factory.
                object unknown = null;

				CoGetClassObject(
					clsid,
					(isLocalHost)?CLSCTX_LOCAL_SERVER:CLSCTX_REMOTE_SERVER,
					ref coserverInfo,
					IID_IUnknown,
					out unknown);

                // SetProxySecurity(unknown, coserverInfo.pAuthInfo);
                
                factory = (IClassFactory)unknown;

                // check for valid factory.
                if (factory == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadCommunicationError, "Could not load IClassFactory for COM server '{0}' on host '{1}'.", clsid, hostName);
                }

                // SetProxySecurity(factory, coserverInfo.pAuthInfo);

			    factory.CreateInstance(null, IID_IUnknown, out instance);

                // SetProxySecurity(instance, coserverInfo.pAuthInfo);
			}
			finally
			{
				serverInfo.Deallocate();
			}

            return instance;
		}	

		/// <summary>
		/// Creates an instance of a COM server using the specified license key.
		/// </summary>
		public static object CreateInstanceWithLicenseKey(Guid clsid, string hostName, UserIdentity identity, string licenseKey)
		{
			ServerInfo     serverInfo   = new ServerInfo();
			COSERVERINFO   coserverInfo = serverInfo.Allocate(hostName, identity);
			object         instance     = null; 
			IClassFactory2 factory      = null; 

			try
			{
				// check whether connecting locally or remotely.
				uint clsctx = CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER;

				if (hostName != null && hostName.Length > 0)
				{
					clsctx = CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER;
				}

				// get the class factory.
				object unknown = null;

				CoGetClassObject(
					clsid,
					clsctx,
					ref coserverInfo,
					typeof(IClassFactory2).GUID,
					out unknown);

                // SetProxySecurity(unknown, coserverInfo.pAuthInfo);
                
                factory = (IClassFactory2)unknown;

                // check for valid factory.
                if (factory == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadCommunicationError, "Could not load IClassFactory2 for COM server '{0}' on host '{1}'.", clsid, hostName);
                }

                // SetProxySecurity(factory, coserverInfo.pAuthInfo);

				// create instance.
				factory.CreateInstanceLic(
					null,
					null,
					IID_IUnknown,
					licenseKey,
                    out instance);

                // SetProxySecurity(instance, coserverInfo.pAuthInfo);
			}
			finally
			{
				serverInfo.Deallocate();
                ComUtils.ReleaseServer(factory);
			}
			  
			return instance;
		}	
        #endif
		#endregion

        #region Conversion Functions
        /// <summary>
        /// Converts a HDA Aggregate ID to a UA aggregate ID
        /// </summary>
        /// <param name="aggregateId">The aggregate id.</param>
        /// <returns></returns>
        public static NodeId GetHdaAggregateId(uint aggregateId)
        {
            switch ((OPCHDA_AGGREGATE)aggregateId)
            {
                case OPCHDA_AGGREGATE.OPCHDA_INTERPOLATIVE: { return Opc.Ua.ObjectIds.AggregateFunction_Interpolative; }
                case OPCHDA_AGGREGATE.OPCHDA_TOTAL: { return Opc.Ua.ObjectIds.AggregateFunction_Total; }
                case OPCHDA_AGGREGATE.OPCHDA_AVERAGE: { return Opc.Ua.ObjectIds.AggregateFunction_Average; }
                case OPCHDA_AGGREGATE.OPCHDA_TIMEAVERAGE: { return Opc.Ua.ObjectIds.AggregateFunction_TimeAverage; }
                case OPCHDA_AGGREGATE.OPCHDA_COUNT: { return Opc.Ua.ObjectIds.AggregateFunction_Count; }
                case OPCHDA_AGGREGATE.OPCHDA_MINIMUMACTUALTIME: { return Opc.Ua.ObjectIds.AggregateFunction_MinimumActualTime; }
                case OPCHDA_AGGREGATE.OPCHDA_MINIMUM: { return Opc.Ua.ObjectIds.AggregateFunction_Minimum; }
                case OPCHDA_AGGREGATE.OPCHDA_MAXIMUMACTUALTIME: { return Opc.Ua.ObjectIds.AggregateFunction_MaximumActualTime; }
                case OPCHDA_AGGREGATE.OPCHDA_MAXIMUM: { return Opc.Ua.ObjectIds.AggregateFunction_Maximum; }
                case OPCHDA_AGGREGATE.OPCHDA_START: { return Opc.Ua.ObjectIds.AggregateFunction_Start; }
                case OPCHDA_AGGREGATE.OPCHDA_END: { return Opc.Ua.ObjectIds.AggregateFunction_End; }
                case OPCHDA_AGGREGATE.OPCHDA_DELTA: { return Opc.Ua.ObjectIds.AggregateFunction_Delta; }
                case OPCHDA_AGGREGATE.OPCHDA_RANGE: { return Opc.Ua.ObjectIds.AggregateFunction_Range; }
                case OPCHDA_AGGREGATE.OPCHDA_DURATIONGOOD: { return Opc.Ua.ObjectIds.AggregateFunction_DurationGood; }
                case OPCHDA_AGGREGATE.OPCHDA_DURATIONBAD: { return Opc.Ua.ObjectIds.AggregateFunction_DurationBad; }
                case OPCHDA_AGGREGATE.OPCHDA_PERCENTGOOD: { return Opc.Ua.ObjectIds.AggregateFunction_PercentGood; }
                case OPCHDA_AGGREGATE.OPCHDA_PERCENTBAD: { return Opc.Ua.ObjectIds.AggregateFunction_PercentBad; }
                case OPCHDA_AGGREGATE.OPCHDA_WORSTQUALITY: { return Opc.Ua.ObjectIds.AggregateFunction_WorstQuality; }
                case OPCHDA_AGGREGATE.OPCHDA_ANNOTATIONS: { return Opc.Ua.ObjectIds.AggregateFunction_AnnotationCount; }
            }

            return null;
        }

        /// <summary>
        /// Converts a UA Aggregate ID to a HDA aggregate ID
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <returns></returns>
        public static uint GetHdaAggregateId(NodeId nodeId)
        {
            // check for valid node.
            if (nodeId == null)
            {
                return 0;
            }

            // check for UA defined aggregate.
            if (nodeId.NamespaceIndex == 0)
            {
                if (nodeId.IdType != IdType.Numeric)
                {
                    return 0;
                }

                switch ((uint)nodeId.Identifier)
                {
                    case Opc.Ua.Objects.AggregateFunction_Interpolative: { return (uint)OPCHDA_AGGREGATE.OPCHDA_INTERPOLATIVE; }
                    case Opc.Ua.Objects.AggregateFunction_Total: { return (uint)OPCHDA_AGGREGATE.OPCHDA_TOTAL; }
                    case Opc.Ua.Objects.AggregateFunction_Average: { return (uint)OPCHDA_AGGREGATE.OPCHDA_AVERAGE; }
                    case Opc.Ua.Objects.AggregateFunction_TimeAverage: { return (uint)OPCHDA_AGGREGATE.OPCHDA_TIMEAVERAGE; }
                    case Opc.Ua.Objects.AggregateFunction_Count: { return (uint)OPCHDA_AGGREGATE.OPCHDA_COUNT; }
                    case Opc.Ua.Objects.AggregateFunction_MinimumActualTime: { return (uint)OPCHDA_AGGREGATE.OPCHDA_MINIMUMACTUALTIME; }
                    case Opc.Ua.Objects.AggregateFunction_Minimum: { return (uint)OPCHDA_AGGREGATE.OPCHDA_MINIMUMACTUALTIME; }
                    case Opc.Ua.Objects.AggregateFunction_MaximumActualTime: { return (uint)OPCHDA_AGGREGATE.OPCHDA_MAXIMUMACTUALTIME; }
                    case Opc.Ua.Objects.AggregateFunction_Maximum: { return (uint)OPCHDA_AGGREGATE.OPCHDA_MAXIMUM; }
                    case Opc.Ua.Objects.AggregateFunction_Start: { return (uint)OPCHDA_AGGREGATE.OPCHDA_START; }
                    case Opc.Ua.Objects.AggregateFunction_End: { return (uint)OPCHDA_AGGREGATE.OPCHDA_END; }
                    case Opc.Ua.Objects.AggregateFunction_Delta: { return (uint)OPCHDA_AGGREGATE.OPCHDA_DELTA; }
                    case Opc.Ua.Objects.AggregateFunction_Range: { return (uint)OPCHDA_AGGREGATE.OPCHDA_RANGE; }
                    case Opc.Ua.Objects.AggregateFunction_DurationGood: { return (uint)OPCHDA_AGGREGATE.OPCHDA_DURATIONGOOD; }
                    case Opc.Ua.Objects.AggregateFunction_DurationBad: { return (uint)OPCHDA_AGGREGATE.OPCHDA_DURATIONBAD; }
                    case Opc.Ua.Objects.AggregateFunction_PercentGood: { return (uint)OPCHDA_AGGREGATE.OPCHDA_PERCENTGOOD; }
                    case Opc.Ua.Objects.AggregateFunction_PercentBad: { return (uint)OPCHDA_AGGREGATE.OPCHDA_PERCENTBAD; }
                    case Opc.Ua.Objects.AggregateFunction_WorstQuality: { return (uint)OPCHDA_AGGREGATE.OPCHDA_WORSTQUALITY; }
                    case Opc.Ua.Objects.AggregateFunction_AnnotationCount: { return (uint)OPCHDA_AGGREGATE.OPCHDA_ANNOTATIONS; }
                }
            }

            return 0;
        }

        /// <summary>
        /// Converts the UA access level to DA access rights.
        /// </summary>
        public static int GetAccessRights(byte accessLevel)
        {
            int accessRights = 0;

            if ((accessLevel & AccessLevels.CurrentRead) != 0)
            {
                accessRights |= OpcRcw.Da.Constants.OPC_READABLE;
            }

            if ((accessLevel & AccessLevels.CurrentWrite) != 0)
            {
                accessRights |= OpcRcw.Da.Constants.OPC_WRITEABLE;
            }

            return accessRights;
        }
        
		/// <summary>
		/// Tests if the specified string matches the specified pattern.
		/// </summary>
		public static bool Match(string target, string pattern, bool caseSensitive)
		{
			// an empty pattern always matches.
			if (pattern == null || pattern.Length == 0)
			{
				return true;
			}

			// an empty string never matches.
			if (target == null || target.Length == 0)
			{
				return false;
			}

			// check for exact match
			if (caseSensitive)
			{
				if (target == pattern)
				{
					return true;
				}
			}
			else
			{
				if (target.ToLower() == pattern.ToLower())
				{
					return true;
				}
			}
 
			char c;
			char p;
			char l;

			int pIndex = 0;
			int tIndex = 0;

			while (tIndex < target.Length && pIndex < pattern.Length)
			{
				p = ConvertCase(pattern[pIndex++], caseSensitive);

				if (pIndex > pattern.Length)
				{
					return (tIndex >= target.Length); // if end of string true
				}
	
				switch (p)
				{
					// match zero or more char.
					case '*':
					{
                        while (pIndex < pattern.Length && pattern[pIndex] == '*')
                        {
                            pIndex++;
                        }

						while (tIndex < target.Length) 
						{   
							if (Match(target.Substring(tIndex++), pattern.Substring(pIndex), caseSensitive))
							{
								return true;
							}
						}
			
						return Match(target, pattern.Substring(pIndex), caseSensitive);
					}

					// match any one char.
					case '?':
					{
						// check if end of string when looking for a single character.
						if (tIndex >= target.Length) 
						{
							return false;  
						}

						// check if end of pattern and still string data left.
						if (pIndex >= pattern.Length && tIndex < target.Length-1)
						{
							return false;
						}

						tIndex++;
						break;
					}

					// match char set 
					case '[': 
					{
						c = ConvertCase(target[tIndex++], caseSensitive);

						if (tIndex > target.Length)
						{
							return false; // syntax 
						}

						l = '\0'; 

						// match a char if NOT in set []
						if (pattern[pIndex] == '!') 
						{
							++pIndex;

							p = ConvertCase(pattern[pIndex++], caseSensitive);

							while (pIndex < pattern.Length) 
							{
								if (p == ']') // if end of char set, then 
								{
									break; // no match found 
								}

								if (p == '-') 
								{
									// check a range of chars? 
									p = ConvertCase(pattern[pIndex], caseSensitive);

									// get high limit of range 
									if (pIndex > pattern.Length || p == ']')
									{
										return false; // syntax 
									}

									if (c >= l && c <= p) 
									{
										return false; // if in range, return false
									}
								} 

								l = p;
						
								if (c == p) // if char matches this element 
								{
									return false; // return false 
								}
								
								p = ConvertCase(pattern[pIndex++], caseSensitive);
							} 
						}

						// match if char is in set []
						else 
						{
							p = ConvertCase(pattern[pIndex++], caseSensitive);

							while (pIndex < pattern.Length) 
							{
								if (p == ']') // if end of char set, then no match found 
								{
									return false;
								}

								if (p == '-') 
								{   
									// check a range of chars? 
									p = ConvertCase(pattern[pIndex], caseSensitive);
							
									// get high limit of range 
									if (pIndex > pattern.Length || p == ']')
									{
										return false; // syntax 
									}

									if (c >= l  &&  c <= p) 
									{
										break; // if in range, move on 
									}
								} 

								l = p;
						
								if (c == p) // if char matches this element move on 
								{
									break;           
								}
								
								p = ConvertCase(pattern[pIndex++], caseSensitive);
							} 

							while (pIndex < pattern.Length && p != ']') // got a match in char set skip to end of set
							{
								p = pattern[pIndex++];             
							}
						}

						break; 
					}

					// match digit.
					case '#':
					{
						c = target[tIndex++]; 

						if (!Char.IsDigit(c))
						{
							return false; // not a digit
						}

						break;
					}

					// match exact char.
					default: 
					{
						c = ConvertCase(target[tIndex++], caseSensitive); 
				
						if (c != p) // check for exact char
						{
							return false; // not a match
						}

						// check if end of pattern and still string data left.
						if (pIndex >= pattern.Length && tIndex < target.Length-1)
						{
							return false;
						}

						break;
					}
				} 
			} 

			return true;
		} 

		// ConvertCase
		private static char ConvertCase(char c, bool caseSensitive)
		{
			return (caseSensitive)?c:Char.ToUpper(c);
		}

		/// <summary>
		/// Unmarshals and frees an array of HRESULTs.
		/// </summary>
		public static int[] GetStatusCodes(ref IntPtr pArray, int size, bool deallocate)
		{
			if (pArray == IntPtr.Zero || size <= 0)
			{
				return null;
			}

			// unmarshal HRESULT array.
			int[] output = new int[size];
			Marshal.Copy(pArray, output, 0, size);

			if (deallocate)
			{
				Marshal.FreeCoTaskMem(pArray);
				pArray = IntPtr.Zero;
			}

			return output;
		}

		/// <summary>
		/// Unmarshals and frees an array of 32 bit integers.
		/// </summary>
		public static int[] GetInt32s(ref IntPtr pArray, int size, bool deallocate)
		{
			if (pArray == IntPtr.Zero || size <= 0)
			{
				return null;
			}

			int[] array = new int[size];
			Marshal.Copy(pArray, array, 0, size);

			if (deallocate)
			{
				Marshal.FreeCoTaskMem(pArray);
				pArray = IntPtr.Zero;
			}

			return array;
        }

        /// <summary>
        /// Unmarshals and frees an array of 32 bit integers.
        /// </summary>
        public static int[] GetUInt32s(ref IntPtr pArray, int size, bool deallocate)
        {
            if (pArray == IntPtr.Zero || size <= 0)
            {
                return null;
            }

            int[] array = new int[size];
            Marshal.Copy(pArray, array, 0, size);

            if (deallocate)
            {
                Marshal.FreeCoTaskMem(pArray);
                pArray = IntPtr.Zero;
            }

            return array;
        }


		/// <summary>
		/// Allocates and marshals an array of 32 bit integers.
		/// </summary>
		public static IntPtr GetInt32s(int[] input)
		{
			IntPtr output = IntPtr.Zero;

			if (input != null)
			{
				output = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(Int32))*input.Length);
				Marshal.Copy(input, 0, output, input.Length);
			}

			return output;
		}

		/// <summary>
		/// Unmarshals and frees a array of 16 bit integers.
		/// </summary>
		public static short[] GetInt16s(ref IntPtr pArray, int size, bool deallocate)
		{
			if (pArray == IntPtr.Zero || size <= 0)
			{
				return null;
			}

			short[] array = new short[size];
			Marshal.Copy(pArray, array, 0, size);

			if (deallocate)
			{
				Marshal.FreeCoTaskMem(pArray);
				pArray = IntPtr.Zero;
			}

			return array;
		}

		/// <summary>
		/// Allocates and marshals an array of 16 bit integers.
		/// </summary>
		public static IntPtr GetInt16s(short[] input)
		{
			IntPtr output = IntPtr.Zero;

			if (input != null)
			{
				output = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(Int16))*input.Length);
				Marshal.Copy(input, 0, output, input.Length);
			}

			return output;
		}

		/// <summary>
		/// Marshals an array of strings into a unmanaged memory buffer
		/// </summary>
		/// <param name="values">The array of strings to marshal</param>
		/// <returns>The pointer to the unmanaged memory buffer</returns>
		public static IntPtr GetUnicodeStrings(string[] values)
		{
			int size = (values != null)?values.Length:0;

			if (size <= 0)
			{
				return IntPtr.Zero;
			}

			IntPtr pValues = IntPtr.Zero;

			int[] pointers = new int[size];
			
			for (int ii = 0; ii < size; ii++)
			{
				pointers[ii] = (int)Marshal.StringToCoTaskMemUni(values[ii]);
			}

			pValues = Marshal.AllocCoTaskMem(values.Length*Marshal.SizeOf(typeof(IntPtr)));
			Marshal.Copy(pointers, 0, pValues, size);

			return pValues;
		}

		/// <summary>
		/// Unmarshals and frees a array of unicode strings.
		/// </summary>
		public static string[] GetUnicodeStrings(ref IntPtr pArray, int size, bool deallocate)
		{
			if (pArray == IntPtr.Zero || size <= 0)
			{
				return null;
			}

            IntPtr[] pointers = new IntPtr[size];
			Marshal.Copy(pArray, pointers, 0, size);

			string[] strings = new string[size];

			for (int ii = 0; ii < size; ii++)
			{
				IntPtr pString = pointers[ii];
				strings[ii] = Marshal.PtrToStringUni(pString);
				if (deallocate) Marshal.FreeCoTaskMem(pString);
			}

			if (deallocate)
			{
				Marshal.FreeCoTaskMem(pArray);
				pArray = IntPtr.Zero;
			}

			return strings;
		}

		/// <summary>
		/// Marshals a DateTime as a WIN32 FILETIME.
		/// </summary>
		/// <param name="datetime">The DateTime object to marshal</param>
		/// <returns>The WIN32 FILETIME</returns>
		public static System.Runtime.InteropServices.ComTypes.FILETIME GetFILETIME(DateTime datetime)
		{
			System.Runtime.InteropServices.ComTypes.FILETIME filetime;

			if (datetime <= FILETIME_BaseTime)
			{
				filetime.dwHighDateTime = 0;
				filetime.dwLowDateTime  = 0;
				return filetime;
			}

			// adjust for WIN32 FILETIME base.
			long ticks = 0;
			ticks = datetime.Subtract(new TimeSpan(FILETIME_BaseTime.Ticks)).Ticks;
		
			filetime.dwHighDateTime = (int)((ticks>>32) & 0xFFFFFFFF);
			filetime.dwLowDateTime  = (int)(ticks & 0xFFFFFFFF);

			return filetime;
		}

        /// <summary>
        /// Unmarshals an array of FILETIMEs
        /// </summary>
        public static System.Runtime.InteropServices.ComTypes.FILETIME[] GetFILETIMEs(ref IntPtr pArray, int size)
        {
            if (pArray == IntPtr.Zero || size <= 0)
            {
                return null;
            }

            System.Runtime.InteropServices.ComTypes.FILETIME[] values = new System.Runtime.InteropServices.ComTypes.FILETIME[size];

            IntPtr pos = pArray;

            for (int ii = 0; ii < size; ii++)
            {
                try
                {
                    values[ii] = (System.Runtime.InteropServices.ComTypes.FILETIME)Marshal.PtrToStructure(pos, typeof(System.Runtime.InteropServices.ComTypes.FILETIME));
                }
                catch (Exception)
                {
                }

                pos = (IntPtr)(pos.ToInt64() + 8);
            }


            Marshal.FreeCoTaskMem(pArray);
            pArray = IntPtr.Zero;

            return values;
        }	

		/// <summary>
		/// Unmarshals a WIN32 FILETIME from a pointer.
		/// </summary>
		/// <param name="pFiletime">A pointer to a FILETIME structure.</param>
		/// <returns>A DateTime object.</returns>
		public static DateTime GetDateTime(IntPtr pFiletime)
		{
			if (pFiletime == IntPtr.Zero)
			{
				return DateTime.MinValue;
			}

			return GetDateTime((System.Runtime.InteropServices.ComTypes.FILETIME)Marshal.PtrToStructure(pFiletime, typeof(System.Runtime.InteropServices.ComTypes.FILETIME)));
		}

		/// <summary>
		/// Unmarshals a WIN32 FILETIME.
		/// </summary>
		public static DateTime GetDateTime(System.Runtime.InteropServices.ComTypes.FILETIME filetime)
		{
            // check for invalid value.
            if (filetime.dwHighDateTime < 0)
            {
                return DateTime.MinValue;
            }

			// convert FILETIME structure to a 64 bit integer.
			long buffer = (long)filetime.dwHighDateTime;

			if (buffer < 0)
			{
				buffer += ((long)UInt32.MaxValue+1);
			}

			long ticks = (buffer<<32);

			buffer = (long)filetime.dwLowDateTime;

			if (buffer < 0)
			{
				buffer += ((long)UInt32.MaxValue+1);
			}

			ticks += buffer;

			// check for invalid value.
			if (ticks == 0)
			{
				return DateTime.MinValue;
			}

			// adjust for WIN32 FILETIME base.			
			return FILETIME_BaseTime.Add(new TimeSpan(ticks));
		}        

		/// <summary>
		/// Marshals an array of DateTimes into an unmanaged array of FILETIMEs
		/// </summary>
		/// <param name="datetimes">The array of DateTimes to marshal</param>
		/// <returns>The IntPtr array of FILETIMEs</returns>
		public static IntPtr GetFILETIMEs(DateTime[] datetimes)
		{
			int count = (datetimes != null)?datetimes.Length:0;

			if (count <= 0)
			{
				return IntPtr.Zero;
			}

			IntPtr pFiletimes = Marshal.AllocCoTaskMem(count*Marshal.SizeOf(typeof(System.Runtime.InteropServices.ComTypes.FILETIME)));

			IntPtr pos = pFiletimes;

			for (int ii = 0; ii < count; ii++)
			{
				Marshal.StructureToPtr(GetFILETIME(datetimes[ii]), pos, false);
				pos = (IntPtr)(pos.ToInt64() + Marshal.SizeOf(typeof(System.Runtime.InteropServices.ComTypes.FILETIME)));
			}

			return pFiletimes;
		}	

		/// <summary>
		/// Unmarshals an array of WIN32 FILETIMEs as DateTimes.
		/// </summary>
		public static DateTime[] GetDateTimes(ref IntPtr pArray, int size, bool deallocate)
		{
			if (pArray == IntPtr.Zero || size <= 0)
			{
				return null;
			}

			DateTime[] datetimes = new DateTime[size];

			IntPtr pos = pArray;

			for (int ii = 0; ii < size; ii++)
			{
				datetimes[ii] = GetDateTime(pos);
				pos = (IntPtr)(pos.ToInt64() + Marshal.SizeOf(typeof(System.Runtime.InteropServices.ComTypes.FILETIME)));
			}

			if (deallocate)
			{
				Marshal.FreeCoTaskMem(pArray);
				pArray = IntPtr.Zero;
			}

			return datetimes;
		}	

		/// <summary>
		/// Unmarshals an array of WIN32 GUIDs as Guid.
		/// </summary>
		public static Guid[] GetGUIDs(ref IntPtr pInput, int size, bool deallocate)
		{
			if (pInput == IntPtr.Zero || size <= 0)
			{
				return null;
			}

			Guid[] guids = new Guid[size];

			IntPtr pos = pInput;

			for (int ii = 0; ii < size; ii++)
			{
				GUID input = (GUID)Marshal.PtrToStructure(pInput, typeof(GUID));

				guids[ii] = new Guid(input.Data1, input.Data2, input.Data3, input.Data4);
				
				pos = (IntPtr)(pos.ToInt64() + Marshal.SizeOf(typeof(GUID)));
			}

			if (deallocate)
			{
				Marshal.FreeCoTaskMem(pInput);
				pInput = IntPtr.Zero;
			}

			return guids;
		}	
        
		/// <summary>
		/// Converts an object into a value that can be marshalled to a VARIANT.
		/// </summary>
		/// <param name="source">The object to convert.</param>
		/// <returns>The converted object.</returns>
        public static object GetVARIANT(object source)
        {
			// check for invalid args.
			if (source == null)
			{
				return null;
			}

            return GetVARIANT(source, TypeInfo.Construct(source));
        }

        /// <summary>
        /// Converts an object into a value that can be marshalled to a VARIANT.
        /// </summary>
        /// <param name="source">The object to convert.</param>
        /// <returns>The converted object.</returns>
        public static object GetVARIANT(Variant source)
        {
            return GetVARIANT(source.Value, source.TypeInfo);
        }
   
        /// <summary>
        /// Converts an object into a value that can be marshalled to a VARIANT.
        /// </summary>
        /// <param name="source">The object to convert.</param>
        /// <param name="typeInfo">The type info.</param>
        /// <returns>The converted object.</returns>
        public static object GetVARIANT(object source, TypeInfo typeInfo)
		{
			// check for invalid args.
			if (source == null)
			{
				return null;
			}
            
            try
            {
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                    case BuiltInType.Byte:
                    case BuiltInType.Int16:
                    case BuiltInType.UInt16:
                    case BuiltInType.Int32:
                    case BuiltInType.UInt32:
                    case BuiltInType.Int64:
                    case BuiltInType.UInt64:
                    case BuiltInType.Float:
                    case BuiltInType.Double:
                    case BuiltInType.String:
                    case BuiltInType.DateTime:
                    {
                        return source;
                    }

                    case BuiltInType.ByteString:
                    {
                        if (typeInfo.ValueRank < 0)
                        {
                            return source;
                        }
                        
                        return VariantToObjectArray((Array)source);
                    }

                    case BuiltInType.Guid:
                    case BuiltInType.LocalizedText:
                    case BuiltInType.QualifiedName:
                    case BuiltInType.NodeId:
                    case BuiltInType.ExpandedNodeId:                  
                    case BuiltInType.XmlElement:  
                    {
                        return TypeInfo.Cast(source, typeInfo, BuiltInType.String);
                    }

                    case BuiltInType.StatusCode: 
                    {
                        return TypeInfo.Cast(source, typeInfo, BuiltInType.UInt32);
                    }
                       
                    case BuiltInType.Variant:
                    {
                        if (typeInfo.ValueRank < 0)
                        {
                            return GetVARIANT(((Variant)source).Value);
                        }

                        return VariantToObjectArray((Array)source);
                    }

                    case BuiltInType.ExtensionObject:
                    {
                        if (typeInfo.ValueRank < 0)
                        {
                            byte[] body = null;

                            ExtensionObject extension = (ExtensionObject)source;

                            switch (extension.Encoding)
                            {
                                case ExtensionObjectEncoding.Binary: 
                                {
                                    body = (byte[])extension.Body;
                                    break;
                                }

                                case ExtensionObjectEncoding.Xml: 
                                {
                                    body = new UTF8Encoding().GetBytes(((XmlElement)extension.Body).OuterXml);
                                    break;
                                }

                                case ExtensionObjectEncoding.EncodeableObject: 
                                {
                                    BinaryEncoder encoder = new BinaryEncoder(ServiceMessageContext.GlobalContext);
                                    encoder.WriteEncodeable(null, (IEncodeable)extension.Body, null);
                                    body = encoder.CloseAndReturnBuffer();
                                    break;
                                }
                            }

                            return body;
                        }

                        return VariantToObjectArray((Array)source);
                    }
                       
                    case BuiltInType.DataValue:
                    case BuiltInType.DiagnosticInfo:
                    {
                        return "(unsupported)";
                    }
                }
            }
            catch (Exception e)
            {
                return e.Message;
            }

			// no conversion required.
			return source;
		}

        /// <summary>
        /// Converts a Variant array to an Object array.
        /// </summary>
        private static Array VariantToObjectArray(Array input)
        {
            int[] dimensions = new int[input.Rank];

            for (int ii = 0; ii < dimensions.Length; ii++)
            {
                dimensions[ii] = input.GetLength(ii);
            }
            
            Array output = Array.CreateInstance(typeof(object), dimensions);
                                
            int length = output.Length;
            int[] indexes = new int[dimensions.Length];

            for (int ii = 0; ii < length; ii++)
            {
                int divisor = output.Length;

                for (int jj = 0; jj < indexes.Length; jj++)
                {
                    divisor /= dimensions[jj];
                    indexes[jj] = (ii/divisor)%dimensions[jj];
                }
                
                object value = input.GetValue(indexes);
                output.SetValue(GetVARIANT(value), indexes);
            }

            return output;
        }

		/// <summary>
		/// Marshals an array objects into an unmanaged array of VARIANTs.
		/// </summary>
		/// <param name="values">An array of the objects to be marshalled</param>
		/// <param name="preprocess">Whether the objects should have troublesome types removed before marhalling.</param>
		/// <returns>An pointer to the array in unmanaged memory</returns>
		public static IntPtr GetVARIANTs(object[] values, bool preprocess)
		{
			int count = (values != null)?values.Length:0;

			if (count <= 0)
			{
				return IntPtr.Zero;
			}

			IntPtr pValues = Marshal.AllocCoTaskMem(count*VARIANT_SIZE);

			IntPtr pos = pValues;

			for (int ii = 0; ii < count; ii++)
			{
				if (preprocess)
				{
					Marshal.GetNativeVariantForObject(GetVARIANT(values[ii]), pos);
				}
				else
				{
					Marshal.GetNativeVariantForObject(values[ii], pos);
				}

				pos = (IntPtr)(pos.ToInt64() + VARIANT_SIZE);
			}

			return pValues;
		}	

		/// <summary>
		/// Unmarshals an array of VARIANTs as objects.
		/// </summary>
		public static object[] GetVARIANTs(ref IntPtr pArray, int size, bool deallocate)
		{
			// this method unmarshals VARIANTs one at a time because a single bad value throws 
			// an exception with GetObjectsForNativeVariants(). This approach simply sets the 
			// offending value to null.

			if (pArray == IntPtr.Zero || size <= 0)
			{
				return null;
			}

			object[] values = new object[size];

			IntPtr pos = pArray;

			for (int ii = 0; ii < size; ii++)
			{
				try 
				{
					values[ii] = Marshal.GetObjectForNativeVariant(pos);
                    values[ii] = ProcessComValue(values[ii]);
					if (deallocate) VariantClear(pos);
				}
				catch (Exception)
				{
					values[ii] = null;
				}

				pos = (IntPtr)(pos.ToInt64() + VARIANT_SIZE);
			}

			if (deallocate)
			{
				Marshal.FreeCoTaskMem(pArray);
				pArray = IntPtr.Zero;
			}

			return values;
		}	

		/// <summary>
		/// Converts a LCID to a Locale string.
		/// </summary>
		public static string GetLocale(int input)
		{
			try   
			{ 
				if (input == LOCALE_SYSTEM_DEFAULT || input == LOCALE_USER_DEFAULT || input == 0)
				{
					return CultureInfo.InvariantCulture.Name;
				}

				return new CultureInfo(input).Name; 
			}
			catch (Exception e)
			{ 
			    throw new ServiceResultException(StatusCodes.Bad, "Unrecognized locale provided.", e);
			}
		}

		/// <summary>
		/// Converts a Locale string to a LCID.
		/// </summary>
		public static int GetLocale(string input)
		{
			// check for the default culture.
			if (input == null || input == "")
			{
				return 0;
			}

			CultureInfo locale = null;

			try   { locale = new CultureInfo(input);     }
			catch { locale = CultureInfo.CurrentCulture; }
		
			return locale.LCID;
		}

		/// <summary>
		/// Converts the VARTYPE to a UA Data Type ID.
		/// </summary>
		public static NodeId GetDataTypeId(short input)
		{				
			switch ((VarEnum)Enum.ToObject(typeof(VarEnum), (input & ~(short)VarEnum.VT_ARRAY)))
			{
				case VarEnum.VT_I1:      return DataTypes.SByte;
				case VarEnum.VT_UI1:     return DataTypes.Byte;
				case VarEnum.VT_I2:      return DataTypes.Int16;
				case VarEnum.VT_UI2:     return DataTypes.UInt16;
				case VarEnum.VT_I4:      return DataTypes.Int32;
				case VarEnum.VT_UI4:     return DataTypes.UInt32;
				case VarEnum.VT_I8:      return DataTypes.Int64;
				case VarEnum.VT_UI8:     return DataTypes.UInt64;
				case VarEnum.VT_R4:      return DataTypes.Float;
				case VarEnum.VT_R8:      return DataTypes.Double;
				case VarEnum.VT_BOOL:    return DataTypes.Boolean;
				case VarEnum.VT_DATE:    return DataTypes.DateTime;
				case VarEnum.VT_BSTR:    return DataTypes.String;
				case VarEnum.VT_CY:      return DataTypes.String;
				case VarEnum.VT_EMPTY:   return DataTypes.BaseDataType;
				
                case VarEnum.VT_VARIANT: 
                { 
                    return DataTypes.BaseDataType;
                }
			}

			return NodeId.Null;
		}
        
		/// <summary>
		/// Converts the VARTYPE to a UA ValueRank.
		/// </summary>
		public static int GetValueRank(short input)
		{				
            if ((((short)VarEnum.VT_ARRAY & input) != 0))
            {
                return ValueRanks.OneDimension;
            }

            return ValueRanks.Scalar;
		}

		/// <summary>
		/// Converts the VARTYPE to a UA Data Type ID.
		/// </summary>
		public static NodeId GetDataTypeId(short input, out bool isArray)
		{				
			isArray = (((short)VarEnum.VT_ARRAY & input) != 0);
			return GetDataTypeId(input);
		}
        
		/// <summary>
		/// Converts the VARTYPE to a SystemType
		/// </summary>
		public static Type GetSystemType(short input)
		{				
			bool isArray = (((short)VarEnum.VT_ARRAY & input) != 0);
            VarEnum varType = (VarEnum)Enum.ToObject(typeof(VarEnum), (input & ~(short)VarEnum.VT_ARRAY));

            if (!isArray)
            {
			    switch (varType)
			    {
				    case VarEnum.VT_I1:      return typeof(sbyte);
				    case VarEnum.VT_UI1:     return typeof(byte);
				    case VarEnum.VT_I2:      return typeof(short);
				    case VarEnum.VT_UI2:     return typeof(ushort);
				    case VarEnum.VT_I4:      return typeof(int);
				    case VarEnum.VT_UI4:     return typeof(uint);
				    case VarEnum.VT_I8:      return typeof(long);
				    case VarEnum.VT_UI8:     return typeof(ulong);
				    case VarEnum.VT_R4:      return typeof(float);
				    case VarEnum.VT_R8:      return typeof(double);
				    case VarEnum.VT_BOOL:    return typeof(bool);
				    case VarEnum.VT_DATE:    return typeof(DateTime);
				    case VarEnum.VT_BSTR:    return typeof(string);
				    case VarEnum.VT_CY:      return typeof(decimal);
				    case VarEnum.VT_EMPTY:   return typeof(object);
				    case VarEnum.VT_VARIANT: return typeof(object);
			    }
            }
            else
            {
			    switch (varType)
			    {
				    case VarEnum.VT_I1:      return typeof(sbyte[]);
				    case VarEnum.VT_UI1:     return typeof(byte[]);
				    case VarEnum.VT_I2:      return typeof(short[]);
				    case VarEnum.VT_UI2:     return typeof(ushort[]);
				    case VarEnum.VT_I4:      return typeof(int[]);
				    case VarEnum.VT_UI4:     return typeof(uint[]);
				    case VarEnum.VT_I8:      return typeof(long[]);
				    case VarEnum.VT_UI8:     return typeof(ulong[]);
				    case VarEnum.VT_R4:      return typeof(float[]);
				    case VarEnum.VT_R8:      return typeof(double[]);
				    case VarEnum.VT_BOOL:    return typeof(bool[]);
				    case VarEnum.VT_DATE:    return typeof(DateTime[]);
				    case VarEnum.VT_BSTR:    return typeof(string[]);
				    case VarEnum.VT_CY:      return typeof(decimal[]);
				    case VarEnum.VT_EMPTY:   return typeof(object[]);
				    case VarEnum.VT_VARIANT: return typeof(object[]);
			    }
            }

			return null;
		}
        
		/// <summary>
		/// Returns the VARTYPE for the value.
		/// </summary>
		public static VarEnum GetVarType(object input)
		{
            if (input == null)
            {
                return VarEnum.VT_EMPTY;
            }

            return GetVarType(input.GetType());
        }

		/// <summary>
		/// Converts the system type to a VARTYPE.
		/// </summary>
		public static VarEnum GetVarType(System.Type type)
		{
            if (type == null)
            {
                return VarEnum.VT_EMPTY;
            }

            if (type == null)               return VarEnum.VT_EMPTY;
			if (type == typeof(sbyte))      return VarEnum.VT_I1;
			if (type == typeof(byte))       return VarEnum.VT_UI1;
			if (type == typeof(short))      return VarEnum.VT_I2;
			if (type == typeof(ushort))     return VarEnum.VT_UI2;
			if (type == typeof(int))        return VarEnum.VT_I4;
			if (type == typeof(uint))       return VarEnum.VT_UI4;
			if (type == typeof(long))       return VarEnum.VT_I8;
			if (type == typeof(ulong))      return VarEnum.VT_UI8;
			if (type == typeof(float))      return VarEnum.VT_R4;
			if (type == typeof(double))     return VarEnum.VT_R8;
			if (type == typeof(decimal))    return VarEnum.VT_CY;
			if (type == typeof(bool))       return VarEnum.VT_BOOL;
			if (type == typeof(DateTime))   return VarEnum.VT_DATE;
			if (type == typeof(string))     return VarEnum.VT_BSTR;
			if (type == typeof(sbyte[]))    return VarEnum.VT_ARRAY | VarEnum.VT_I1;
			if (type == typeof(byte[]))     return VarEnum.VT_ARRAY | VarEnum.VT_UI1;
			if (type == typeof(short[]))    return VarEnum.VT_ARRAY | VarEnum.VT_I2;
			if (type == typeof(ushort[]))   return VarEnum.VT_ARRAY | VarEnum.VT_UI2;
			if (type == typeof(int[]))      return VarEnum.VT_ARRAY | VarEnum.VT_I4;
			if (type == typeof(uint[]))     return VarEnum.VT_ARRAY | VarEnum.VT_UI4;
			if (type == typeof(long[]))     return VarEnum.VT_ARRAY | VarEnum.VT_I8;
			if (type == typeof(ulong[]))    return VarEnum.VT_ARRAY | VarEnum.VT_UI8;
			if (type == typeof(float[]))    return VarEnum.VT_ARRAY | VarEnum.VT_R4;
			if (type == typeof(double[]))   return VarEnum.VT_ARRAY | VarEnum.VT_R8;
			if (type == typeof(decimal[]))  return VarEnum.VT_ARRAY | VarEnum.VT_CY;
			if (type == typeof(bool[]))     return VarEnum.VT_ARRAY | VarEnum.VT_BOOL;
			if (type == typeof(DateTime[])) return VarEnum.VT_ARRAY | VarEnum.VT_DATE;
			if (type == typeof(string[]))   return VarEnum.VT_ARRAY | VarEnum.VT_BSTR;
			if (type == typeof(object[]))   return VarEnum.VT_ARRAY | VarEnum.VT_VARIANT;
			
			return VarEnum.VT_EMPTY;
		}

        /// <summary>
        /// Converts the TypeInfo to a VARTYPE.
        /// </summary>
        public static VarEnum GetVarType(TypeInfo typeInfo)
        {
            if (typeInfo == null)
            {
                return VarEnum.VT_EMPTY;
            }

            VarEnum vtType = VarEnum.VT_EMPTY;

            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.Boolean: { vtType = VarEnum.VT_BOOL; break; }
                case BuiltInType.SByte: { vtType = VarEnum.VT_I1; break; } 
                case BuiltInType.Byte: { vtType = VarEnum.VT_UI1; break; }
                case BuiltInType.Int16: { vtType = VarEnum.VT_I2; break; }
                case BuiltInType.UInt16: { vtType = VarEnum.VT_UI2; break; }
                case BuiltInType.Int32: { vtType = VarEnum.VT_I4; break; }
                case BuiltInType.UInt32: { vtType = VarEnum.VT_UI4; break; }
                case BuiltInType.Int64: { vtType = VarEnum.VT_I8; break; }
                case BuiltInType.UInt64: { vtType = VarEnum.VT_UI8; break; }
                case BuiltInType.Float: { vtType = VarEnum.VT_R4; break; }
                case BuiltInType.Double: { vtType = VarEnum.VT_R8; break; }
                case BuiltInType.String: { vtType = VarEnum.VT_BSTR; break; }
                case BuiltInType.DateTime: { vtType = VarEnum.VT_DATE; break; }
                case BuiltInType.Guid: { vtType = VarEnum.VT_BSTR; break; }
                case BuiltInType.ByteString: { vtType = VarEnum.VT_ARRAY | VarEnum.VT_UI1; break; }
                case BuiltInType.XmlElement: { vtType = VarEnum.VT_BSTR; break; }
                case BuiltInType.NodeId: { vtType = VarEnum.VT_BSTR; break; }
                case BuiltInType.ExpandedNodeId: { vtType = VarEnum.VT_BSTR; break; }
                case BuiltInType.QualifiedName: { vtType = VarEnum.VT_BSTR; break; }
                case BuiltInType.LocalizedText: { vtType = VarEnum.VT_BSTR; break; }
                case BuiltInType.StatusCode: { vtType = VarEnum.VT_UI4; break; }
                case BuiltInType.ExtensionObject: { vtType = VarEnum.VT_ARRAY | VarEnum.VT_UI1; break; }
                case BuiltInType.Enumeration: { vtType = VarEnum.VT_I4; break; }
                case BuiltInType.Number: { vtType = VarEnum.VT_R8; break; }
                case BuiltInType.Integer: { vtType = VarEnum.VT_I8; break; }
                case BuiltInType.UInteger: { vtType = VarEnum.VT_UI8; break; }
                
                case BuiltInType.Variant: 
                {
                    if (typeInfo.ValueRank == ValueRanks.Scalar)
                    {
                        return VarEnum.VT_EMPTY;
                    }

                    vtType = VarEnum.VT_VARIANT;
                    break;
                }

                default:
                {
                    return VarEnum.VT_EMPTY;
                }
            }

            if (typeInfo.ValueRank > 0)
            {
                vtType |= VarEnum.VT_ARRAY;
            }

            return vtType;
        }
        
		/// <summary>
		/// Converts a value to the specified type using COM conversion rules.
		/// </summary>
        public static int ChangeTypeForCOM(object source, VarEnum targetType, out object target)
        {
            return ChangeTypeForCOM(source, GetVarType(source), targetType, out target);
        }

		/// <summary>
		/// Converts a value to the specified type using COM conversion rules.
		/// </summary>
		public static int ChangeTypeForCOM(object source, VarEnum sourceType, VarEnum targetType, out object target)
		{
            target = source;
            
            // check for trivial case.
            if (sourceType == targetType)
            {
                return ResultIds.S_OK;
            }

			// check for conversions to date time from string.
            string stringValue = source as string;

			if (stringValue != null && targetType == VarEnum.VT_DATE)
			{
				try   
                { 
                    target = System.Convert.ToDateTime(stringValue);
                    return ResultIds.S_OK;
                }
				catch 
                {
                    target = null;
                    return ResultIds.DISP_E_OVERFLOW;
                }
			}

			// check for conversions from date time to boolean.
            if (sourceType == VarEnum.VT_DATE && targetType == VarEnum.VT_BOOL)
			{
				target = !(new DateTime(1899, 12, 30, 0, 0, 0).Equals((DateTime)source));
                return ResultIds.S_OK;
			}

			// check for conversions from float to double.
            if (sourceType == VarEnum.VT_R4 && targetType == VarEnum.VT_R8)
			{
				target = System.Convert.ToDouble((float)source);
                return ResultIds.S_OK;
			}

			// check for array conversion.
            Array array = source as Array;
            bool targetIsArray = ((targetType & VarEnum.VT_ARRAY) != 0);

			if (array != null && targetIsArray)
			{
                VarEnum elementType = (VarEnum)((short)targetType & ~(short)VarEnum.VT_ARRAY);

                Array convertedArray = Array.CreateInstance(GetSystemType((short)elementType), array.Length);

                for (int ii = 0; ii < array.Length; ii++)
                {
                    object elementValue = null;
                    int error = ChangeTypeForCOM(array.GetValue(ii), elementType, out elementValue);

                    if (error < 0)
                    {
                        target = null;
                        return ResultIds.DISP_E_OVERFLOW;
                    }
                        
                    convertedArray.SetValue(elementValue, ii);
                }

				target = convertedArray;
                return ResultIds.S_OK;
			}
			else if (array == null && !targetIsArray)
			{
				IntPtr pvargDest = Marshal.AllocCoTaskMem(16);
				IntPtr pvarSrc   = Marshal.AllocCoTaskMem(16);
				
				VariantInit(pvargDest);
				VariantInit(pvarSrc);

				Marshal.GetNativeVariantForObject(source, pvarSrc);

				try
				{
					// change type.
					int error = VariantChangeTypeEx(
						pvargDest, 
						pvarSrc, 
						Thread.CurrentThread.CurrentCulture.LCID, 
						VARIANT_NOVALUEPROP | VARIANT_ALPHABOOL, 
						(short)targetType);

					// check error code.
					if (error != 0)
					{
                        target = null;
                        return error;
					}

					// unmarshal result.
					object result = Marshal.GetObjectForNativeVariant(pvargDest);
					
					// check for invalid unsigned <=> signed conversions.
					switch (targetType)
					{
						case VarEnum.VT_I1:
						case VarEnum.VT_I2:
						case VarEnum.VT_I4:
						case VarEnum.VT_I8:
						case VarEnum.VT_UI1:
						case VarEnum.VT_UI2:
						case VarEnum.VT_UI4:
						case VarEnum.VT_UI8:
						{
							// ignore issue for conversions from boolean.
                            if (sourceType == VarEnum.VT_BOOL)
							{	
								break;					
							}

							decimal sourceAsDecimal = 0;
							decimal resultAsDecimal = System.Convert.ToDecimal(result);

							try   { sourceAsDecimal = System.Convert.ToDecimal(source); }
							catch { sourceAsDecimal = 0; }
							
							if ((sourceAsDecimal < 0 && resultAsDecimal > 0) || (sourceAsDecimal > 0 && resultAsDecimal < 0))
							{
                                target = null;
                                return ResultIds.E_RANGE;
							}
                            
							// conversion from datetime should have failed.
                            if (sourceType == VarEnum.VT_DATE)
							{	
                                if (resultAsDecimal == 0)
                                {
                                    target = null;
                                    return ResultIds.E_RANGE;
                                }
							}

							break;
						}		
							
						case VarEnum.VT_R8:
						{						
							// fix precision problem introduced with conversion from float to double.
                            if (sourceType == VarEnum.VT_R4)
							{	
								result = System.Convert.ToDouble(source.ToString());
							}

							break;			
						}
					}

					target = result;
                    return ResultIds.S_OK;
				}
				finally
				{
					VariantClear(pvargDest);
					VariantClear(pvarSrc);

					Marshal.FreeCoTaskMem(pvargDest);
					Marshal.FreeCoTaskMem(pvarSrc);
				}
			}
			else if (array != null && targetType == VarEnum.VT_BSTR)
			{
				int count = ((Array)source).Length;

				StringBuilder buffer = new StringBuilder();

				buffer.Append("{");

				foreach (object element in (Array)source)
				{
                    object elementValue = null;
               
                    int error = ChangeTypeForCOM(element, VarEnum.VT_BSTR, out elementValue);

                    if (error < 0)
                    {
                        target = null;
                        return error;
                    }
                    
					buffer.Append((string)elementValue);

					count--;

					if (count > 0)
					{
						buffer.Append(" | ");
					}
				}

				buffer.Append("}");

				target = buffer.ToString();
                return ResultIds.S_OK;
			}
            
			// no conversions between scalar and array types allowed.
            target = null;
            return ResultIds.E_BADTYPE;
		}
        
		/// <summary>
		/// Returns true is the object is a valid COM type.
		/// </summary>
		public static bool IsValidComType(object input)
		{				
            Array array = input as Array;

            if (array != null)
            {
                foreach (object value in array)
                {
                    if (!IsValidComType(value))
                    {
                        return false;
                    }
                }

                return true;
            }
            
            return GetVarType(input) != VarEnum.VT_EMPTY;
		}

        /// <summary>
        /// Converts a COM value to something that UA clients can deal with.
        /// </summary>
        public static object ProcessComValue(object value)
        {     
            // flatten any multi-dimensional array.
            Array array = value as Array;

            if (array != null)
            {
                if (array.Rank > 1)
                {
                    value = array = Utils.FlattenArray(array);
                }
                
                // convert array of decimal to strings.
                if (array != null && array.GetType().GetElementType() == typeof(decimal))
                {
                    string[] clone = new string[array.Length];

                    for (int ii = 0; ii < array.Length; ii++)
                    {
                        clone[ii] = Convert.ToString(array.GetValue(ii));
                    }
                    
                    value = clone;                        
                }
            }

            // convert scalar decimal to a string.
            if (value is decimal)
            {
                value = Convert.ToString(value);
            }

            // convert scalar DBNull to a null.
            if (value is DBNull)
            {
                value = null;
            }

            return value;
        }

		/// <summary>
		/// Converts a DA quality code to a status code.
		/// </summary>
		public static StatusCode GetQualityCode(short quality)
		{
			StatusCode code = 0;

			// convert quality status.
			switch ((short)(quality & 0x00FC))
			{			
                case OpcRcw.Da.Qualities.OPC_QUALITY_GOOD:                     { code = StatusCodes.Good;                              break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_LOCAL_OVERRIDE:           { code = StatusCodes.GoodLocalOverride;                 break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_UNCERTAIN:                { code = StatusCodes.Uncertain;                         break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_SUB_NORMAL:               { code = StatusCodes.UncertainSubNormal;                break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_SENSOR_CAL:               { code = StatusCodes.UncertainSensorNotAccurate;        break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_EGU_EXCEEDED:             { code = StatusCodes.UncertainEngineeringUnitsExceeded; break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_LAST_USABLE:              { code = StatusCodes.UncertainLastUsableValue;          break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_BAD:                      { code = StatusCodes.Bad;                               break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_CONFIG_ERROR:             { code = StatusCodes.BadConfigurationError;             break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_NOT_CONNECTED:            { code = StatusCodes.BadNotConnected;                   break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_COMM_FAILURE:             { code = StatusCodes.BadNoCommunication;                break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_DEVICE_FAILURE:           { code = StatusCodes.BadDeviceFailure;                  break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_SENSOR_FAILURE:           { code = StatusCodes.BadSensorFailure;                  break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_LAST_KNOWN:               { code = StatusCodes.BadOutOfService;                   break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_OUT_OF_SERVICE:           { code = StatusCodes.BadOutOfService;                   break; }
                case OpcRcw.Da.Qualities.OPC_QUALITY_WAITING_FOR_INITIAL_DATA: { code = StatusCodes.BadWaitingForInitialData;          break; }
			}
            
			// convert the limit status.
			switch ((short)(quality & 0x0003))
			{		
				case OpcRcw.Da.Qualities.OPC_LIMIT_LOW:   { code.LimitBits = LimitBits.Low;      break; }
				case OpcRcw.Da.Qualities.OPC_LIMIT_HIGH:  { code.LimitBits = LimitBits.High;     break; }
				case OpcRcw.Da.Qualities.OPC_LIMIT_CONST: { code.LimitBits = LimitBits.Constant; break; }
			}

			// return the combined code.
			return code;
		}

		/// <summary>
		/// Converts a UA status code to a DA quality code.
		/// </summary>
		public static short GetQualityCode(StatusCode input)
		{
			short code = 0;

			// convert quality status.
			switch (input.CodeBits)
			{			
				case StatusCodes.Good:                              { code = OpcRcw.Da.Qualities.OPC_QUALITY_GOOD;           break; }
				case StatusCodes.GoodLocalOverride:                 { code = OpcRcw.Da.Qualities.OPC_QUALITY_LOCAL_OVERRIDE; break; }
				case StatusCodes.Uncertain:                         { code = OpcRcw.Da.Qualities.OPC_QUALITY_UNCERTAIN;      break; }
				case StatusCodes.UncertainSubNormal:                { code = OpcRcw.Da.Qualities.OPC_QUALITY_SUB_NORMAL;     break; }
				case StatusCodes.UncertainSensorNotAccurate:        { code = OpcRcw.Da.Qualities.OPC_QUALITY_SENSOR_CAL;     break; }
				case StatusCodes.UncertainEngineeringUnitsExceeded: { code = OpcRcw.Da.Qualities.OPC_QUALITY_EGU_EXCEEDED;   break; }
				case StatusCodes.UncertainLastUsableValue:          { code = OpcRcw.Da.Qualities.OPC_QUALITY_LAST_USABLE;    break; }
				case StatusCodes.Bad:                               { code = OpcRcw.Da.Qualities.OPC_QUALITY_BAD;            break; }
				case StatusCodes.BadConfigurationError:             { code = OpcRcw.Da.Qualities.OPC_QUALITY_CONFIG_ERROR;   break; }
				case StatusCodes.BadNotConnected:                   { code = OpcRcw.Da.Qualities.OPC_QUALITY_NOT_CONNECTED;  break; }
                case StatusCodes.BadNoCommunication:                { code = OpcRcw.Da.Qualities.OPC_QUALITY_COMM_FAILURE;   break; }
				case StatusCodes.BadOutOfService:                   { code = OpcRcw.Da.Qualities.OPC_QUALITY_OUT_OF_SERVICE; break; }
				case StatusCodes.BadDeviceFailure:                  { code = OpcRcw.Da.Qualities.OPC_QUALITY_DEVICE_FAILURE; break; }
				case StatusCodes.BadSensorFailure:                  { code = OpcRcw.Da.Qualities.OPC_QUALITY_SENSOR_FAILURE; break; }
				case StatusCodes.BadWaitingForInitialData:          { code = OpcRcw.Da.Qualities.OPC_QUALITY_WAITING_FOR_INITIAL_DATA; break; }

                default:
                {
                    if (StatusCode.IsBad(input))
                    {
                        code = OpcRcw.Da.Qualities.OPC_QUALITY_BAD;
                        break;
                    }
                    
                    if (StatusCode.IsUncertain(input))
                    {
                        code = OpcRcw.Da.Qualities.OPC_QUALITY_UNCERTAIN;
                        break;
                    }

                    code = OpcRcw.Da.Qualities.OPC_QUALITY_GOOD;
                    break;
                }
			}

			// convert the limit status.
			switch (input.LimitBits)
			{		
                case LimitBits.Low:      { code |= OpcRcw.Da.Qualities.OPC_LIMIT_LOW;   break; }
                case LimitBits.High:     { code |= OpcRcw.Da.Qualities.OPC_LIMIT_HIGH;  break; }
                case LimitBits.Constant: { code |= OpcRcw.Da.Qualities.OPC_LIMIT_CONST; break; }
			}

			// return the combined code.
			return code;
		}

        /// <summary>
        /// Converts a HDA quality code to a StatusCode.
        /// </summary>
        public static StatusCode GetHdaQualityCode(uint quality)
        {
            uint hdaCode = quality & 0xFFFF0000;
            
            // check for bits indicating an out right error.
            if ((hdaCode & OpcRcw.Hda.Constants.OPCHDA_NOBOUND) != 0)
            {
                return StatusCodes.BadBoundNotFound;
            }

            if ((hdaCode & OpcRcw.Hda.Constants.OPCHDA_NODATA) != 0)
            {
                return StatusCodes.BadNoData;
            }

            if ((hdaCode & OpcRcw.Hda.Constants.OPCHDA_DATALOST) != 0)
            {
                return StatusCodes.BadDataLost;
            }

            if ((hdaCode & OpcRcw.Hda.Constants.OPCHDA_CONVERSION) != 0)
            {
                return StatusCodes.BadTypeMismatch;
            }

            // Get DA part (lower 2 bytes).
            StatusCode code = GetQualityCode((short)(quality & 0x0000FFFF));
            
            // check for bits that are placed in the info bits.
            AggregateBits aggregateBits = 0;
            
            if ((hdaCode & OpcRcw.Hda.Constants.OPCHDA_EXTRADATA) != 0)
            {
                aggregateBits |= AggregateBits.ExtraData;
            }

            if ((hdaCode & OpcRcw.Hda.Constants.OPCHDA_INTERPOLATED) != 0)
            {
                aggregateBits |= AggregateBits.Interpolated;
            }

            if ((hdaCode & OpcRcw.Hda.Constants.OPCHDA_RAW) != 0)
            {
                aggregateBits |= AggregateBits.Raw;
            }

            if ((hdaCode & OpcRcw.Hda.Constants.OPCHDA_CALCULATED) != 0)
            {
                aggregateBits |= AggregateBits.Calculated;
            }

            if ((hdaCode & OpcRcw.Hda.Constants.OPCHDA_PARTIAL) != 0)
            {
                aggregateBits |= AggregateBits.Partial;
            }

            if (aggregateBits != 0)
            {
                return code.SetAggregateBits(aggregateBits);
            }

            return code;
        }

        /// <summary>
        /// Converts a UA status code to a HDA quality code.
        /// </summary>
        public static uint GetHdaQualityCode(StatusCode input)
        {
            // check for fatal errors.
            switch (input.CodeBits)
            {
                case StatusCodes.BadBoundNotFound: { return OpcRcw.Hda.Constants.OPCHDA_NOBOUND; }
                case StatusCodes.BadBoundNotSupported: { return OpcRcw.Hda.Constants.OPCHDA_NOBOUND; }
                case StatusCodes.BadNoData: { return OpcRcw.Hda.Constants.OPCHDA_NODATA; }
                case StatusCodes.BadDataLost: { return OpcRcw.Hda.Constants.OPCHDA_DATALOST; }
            }

            // handle normal case.
            uint code = Utils.ToUInt32(GetQualityCode(input));
            
            // check for bits that are placed in the info bits.
            AggregateBits aggregateBits = input.AggregateBits;

            if ((aggregateBits & AggregateBits.ExtraData) != 0)
            {
                code |= OpcRcw.Hda.Constants.OPCHDA_EXTRADATA;
            }

            // set the source of the data.
            if ((aggregateBits & AggregateBits.Interpolated) != 0)
            {
                code |= OpcRcw.Hda.Constants.OPCHDA_INTERPOLATED;
            }
            else if ((aggregateBits & AggregateBits.Calculated) != 0)
            {
                code |= OpcRcw.Hda.Constants.OPCHDA_CALCULATED;
            }
            else if ((aggregateBits & AggregateBits.Partial) != 0)
            {
                code |= OpcRcw.Hda.Constants.OPCHDA_PARTIAL;
            }
            else
            {
                code |= OpcRcw.Hda.Constants.OPCHDA_RAW;
            }

            // return the combined code.
            return code;
        }

        /// <summary>
        /// Returns the symbolic name for the specified error.
        /// </summary>
        public static string GetErrorText(Type type, int error)
        {                       
			FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            
			foreach (FieldInfo field in fields)
			{
                if (error == (int)field.GetValue(type))
				{
					return field.Name;
				}
			}

		    return String.Format("0x{0:X8}", error);
        }

        /// <summary>
        /// Gets the error code for the exception.
        /// </summary>
        /// <param name="e">The exception.</param>
        /// <param name="defaultCode">The default code.</param>
        /// <returns>The error code</returns>
        /// <remarks>This method ignores the exception but makes it possible to keep track of ignored exceptions.</remarks>
        public static int GetErrorCode(Exception e, int defaultCode)
        {
            return defaultCode;
        }

        /// <summary>
		/// Releases the server if it is a true COM server.
		/// </summary>
		public static void ReleaseServer(object server)
		{
			if (server != null && server.GetType().IsCOMObject)
			{
				Marshal.ReleaseComObject(server);
			}
		}

		/// <summary>
		/// Retrieves the system message text for the specified error.
		/// </summary>
		public static string GetSystemMessage(int error, int localeId)
		{
            int langId = 0;

            switch (localeId)
            {
                case LOCALE_SYSTEM_DEFAULT:
                {
                    langId = GetSystemDefaultLangID();
                    break;
                }

                case LOCALE_USER_DEFAULT:
                {
                    langId = GetUserDefaultLangID();
                    break;
                }

                default:
                {
                    langId = (0xFFFF & localeId);
                    break;
                }
            }

			IntPtr buffer = Marshal.AllocCoTaskMem(MAX_MESSAGE_LENGTH);

			int result = FormatMessageW(
				(int)FORMAT_MESSAGE_FROM_SYSTEM,
				IntPtr.Zero,
				error,
				langId,
				buffer,
				MAX_MESSAGE_LENGTH-1,
				IntPtr.Zero);

            if (result > 0)
            {
			    string msg = Marshal.PtrToStringUni(buffer);
			    Marshal.FreeCoTaskMem(buffer);

			    if (msg != null && msg.Length > 0)
			    {
				    return msg.Trim();
			    }
            }

			return String.Format("0x{0:X8}", error);
        }

        /// <summary>
        /// Converts an exception to an exception that returns a COM error code.
        /// </summary>
        public static Exception CreateComException(Exception e, int errorId)
        {
            return new COMException(e.Message, errorId);
        }
        
		/// <summary>
		/// Creates a COM exception.
        /// </summary>
		public static Exception CreateComException(string message, int errorId)
		{
			return new COMException(message, errorId);
		}

		/// <summary>
		/// Converts an exception to an exception that returns a COM error code.
		/// </summary>
		public static Exception CreateComException(int errorId)
		{
			return new COMException(String.Format("0x{0:X8}", errorId), errorId);
		}

		/// <summary>
		/// Converts an exception to an exception that returns a COM error code.
		/// </summary>
		public static Exception CreateComException(Exception e)
		{
			// nothing special required for external exceptions.
			if (e is COMException)
			{
				return e;
			}

			// convert other exceptions to E_FAIL.
			return new COMException(e.Message, ResultIds.E_FAIL);
		}

        /// <summary>
        /// Creates an error message for a failed COM function call.
        /// </summary>
        public static Exception CreateException(Exception e, string function)
        {
            return ServiceResultException.Create(
                StatusCodes.BadCommunicationError, 
                "Call to {0} failed. Error: {1}.", 
                function,
                ComUtils.GetSystemMessage(Marshal.GetHRForException(e), LOCALE_SYSTEM_DEFAULT));
        }
                
        /// <summary>
        /// Checks if the error is an RPC error.
        /// </summary>
        public static bool IsRpcError(Exception e)
        {
            int error = Marshal.GetHRForException(e);

            // Assume that any 0x8007 is a fatal communication error.
            // May need to update this check if the assumption proves to be incorrect.
            if ((error & 0xFFFF0000) == 0x80070000)
            {
                error &= 0xFFFF;

                //  check the RPC error range define in WinError.h
                if (error >= 1700 && error < 1918)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if the error for the exception is one of the recognized errors.
        /// </summary>
        public static bool IsUnknownError(Exception e, params int[] knownErrors)
        {
            int error = Marshal.GetHRForException(e);

            if (knownErrors != null)
            {
                for (int ii = 0; ii < knownErrors.Length; ii++)
                {
                    if (knownErrors[ii] == error)
                    {
                        return false;
                    }
                }            
            }

            return true;
        }
		#endregion

        #region Utility Functions
		/// <summary>
		/// Compares a string locale to a WIN32 localeId
		/// </summary>
		public static bool CompareLocales(int localeId, string locale, bool ignoreRegion)
		{
            // parse locale.
            CultureInfo culture = null;

			try   
			{ 
                culture = new CultureInfo(locale);
			}
			catch (Exception)
			{ 
                return false;
			}

            // only match the language portion of the locale id.
            if (ignoreRegion)
            {
                if ((localeId & culture.LCID & 0x3FF) == (localeId & 0x3FF))
                {
                    return true;
                }
            }

            // check for exact match.
            else
            {   
                if (localeId == culture.LCID)
                {
                    return true;
                }
            }

			return false; 
		}

        /// <summary>
        /// Reports an unexpected exception during a COM operation. 
        /// </summary>
        public static void TraceComError(Exception e, string format, params object[] args)
        {
            string message = Utils.Format(format, args);

            int code = Marshal.GetHRForException(e);
            
            string error = ResultIds.GetBrowseName(code);

            if (error == null)
            {
                Utils.Trace(e, message);
                return;
            }

            Utils.Trace(e, "{0}: {1}", error, message);
        }
        #endregion
	}
}
