/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// An access rule for an HTTP URL used by a WCF service.
    /// </summary>
    [DataContract(Namespace=Namespaces.OpcUaConfig)]
    public class HttpAccessRule
    {
        #region Public Properties
        /// <summary>
        /// The access right affected by the rule.
        /// </summary>
        [DataMember(Order = 0)]
        public string UrlPrefix
        {
            get { return m_urlPrefix; }
            set { m_urlPrefix = value; }
        }

        /// <summary>
        /// The access right affected by the rule.
        /// </summary>
        [DataMember(Order = 1)]
        public ApplicationAccessRight Right
        {
            get { return m_right;  }
            set { m_right = value; }
        }

        /// <summary>
        /// The name of the NT account principal which the access rule applies to.
        /// </summary>
        [DataMember(Order = 3)]
        public String IdentityName
        {
            get { return m_identityName;  }
            set { m_identityName = value; }
        }
        #endregion
        
        #region WIN32 Declarations
        private enum HTTP_SERVICE_CONFIG_QUERY_TYPE : int
        {
	        HttpServiceConfigQueryExact = 0,
	        HttpServiceConfigQueryNext  = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HTTP_SERVICE_CONFIG_URLACL_KEY
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pUrlPrefix;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HTTP_SERVICE_CONFIG_URLACL_QUERY
        {
            public HTTP_SERVICE_CONFIG_QUERY_TYPE  QueryDesc;
            public HTTP_SERVICE_CONFIG_URLACL_KEY  KeyDesc;
            public int                             dwToken;
        }

        private struct HTTP_SERVICE_CONFIG_SSL_QUERY
        {
            public HTTP_SERVICE_CONFIG_QUERY_TYPE QueryDesc;
            public HTTP_SERVICE_CONFIG_SSL_KEY KeyDesc;
            public int dwToken;
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct sockaddr_in
        {
            public short sin_family;
            public ushort sin_port;
            public uint sin_addr;
            public ulong sin_zero;
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct HTTP_SERVICE_CONFIG_SSL_KEY 
        {
            public IntPtr pIpPort;
        }
        
        const Int16 AF_INET = 2;
        const Int16 AF_INET6 = 23;

        [StructLayout(LayoutKind.Sequential)]
        struct SOCKADDR_IN
        {
            public Int16 family;
            public UInt16 port;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.U1)]
            public Byte[] addr;
            public Int32 nothing;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SOCKADDR_IN6
        {
            public Int16 family;
            public UInt16 port;
            public Int32 flowInfo;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16, ArraySubType = UnmanagedType.U1)]
            public Byte[] addr;
            public Int32 scopeID;
        }

        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
        private struct HTTP_SERVICE_CONFIG_SSL_PARAM
        {
            public int SslHashLength;
            public IntPtr pSslHash;
            public Guid AppId;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pSslCertStoreName;
            public uint DefaultCertCheckMode;
            public int DefaultRevocationFreshnessTime;
            public int DefaultRevocationUrlRetrievalTimeout;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pDefaultSslCtlIdentifier;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pDefaultSslCtlStoreName;
            public uint DefaultFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HTTP_SERVICE_CONFIG_SSL_SET 
        {
            public HTTP_SERVICE_CONFIG_SSL_KEY   KeyDesc;
            public HTTP_SERVICE_CONFIG_SSL_PARAM ParamDesc;
        } 

        private enum HttpError : int
        {
	        NO_ERROR				  = 0,
	        ERROR_FILE_NOT_FOUND	  = 2,
	        ERROR_INVALID_DATA		  = 13,
	        ERROR_HANDLE_EOF		  = 38,
	        ERROR_INVALID_PARAMETER   = 87,
            ERROR_INSUFFICIENT_BUFFER = 122,
            ERROR_ALREADY_EXISTS      = 183,
            ERROR_NO_MORE_ITEMS       = 259,
	        ERROR_INVALID_DLL		  = 1154,
            ERROR_NOT_FOUND           = 1168
        }

        private const int HTTP_SERVICE_CONFIG_SSL_FLAG_USE_DS_MAPPER = 0x00000001;
        private const int HTTP_SERVICE_CONFIG_SSL_FLAG_NEGOTIATE_CLIENT_CERT = 0x00000002;
        private const int HTTP_SERVICE_CONFIG_SSL_FLAG_NO_RAW_FILTER = 0x00000004;

		private enum HTTP_SERVICE_CONFIG_ID : int
		{
			HttpServiceConfigIPListenList = 0,
			HttpServiceConfigSSLCertInfo  = 1,
			HttpServiceConfigUrlAclInfo   = 2
		}

        /// <summary>
        /// Declares the native methods used by the class.
        /// </summary>
        private static class NativeMethods
        {
            [DllImport("Httpapi.dll", SetLastError = true)]
            public static extern HttpError HttpQueryServiceConfiguration(
                IntPtr ServiceHandle,
                HTTP_SERVICE_CONFIG_ID ConfigId,
                IntPtr pInputConfigInfo,
                int InputConfigInfoLength,
                IntPtr pOutputConfigInfo,
                int OutputConfigInfoLength,
                out int pReturnLength,
                IntPtr pOverlapped);

            [DllImport("Httpapi.dll", SetLastError = true)]
            public static extern HttpError HttpSetServiceConfiguration(
                IntPtr ServiceHandle,
                HTTP_SERVICE_CONFIG_ID ConfigId,
                IntPtr pConfigInformation,
                int ConfigInformationLength,
                IntPtr pOverlapped);


            [DllImport("Httpapi.dll", SetLastError = true)]
            public static extern HttpError HttpDeleteServiceConfiguration(
                IntPtr ServiceHandle,
                HTTP_SERVICE_CONFIG_ID ConfigId,
                IntPtr pConfigInformation,
                int ConfigInformationLength,
                IntPtr pOverlapped);

            [DllImport("Kernel32.dll", EntryPoint = "RtlZeroMemory", SetLastError = true)]
            public static extern void ZeroMemory(IntPtr dest, int size);

            [DllImport("Httpapi.dll", SetLastError = true)]
            public static extern HttpError HttpInitialize(
                HTTPAPI_VERSION version,
                HttpInitFlag flags,
                IntPtr reserved);

            [DllImport("Httpapi.dll", SetLastError = true)]
            public static extern HttpError HttpTerminate(
                HttpInitFlag flags,
                IntPtr reserved); 
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HTTP_SERVICE_CONFIG_URLACL_PARAM 
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pStringSecurityDescriptor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HTTP_SERVICE_CONFIG_URLACL_SET 
        {
            public HTTP_SERVICE_CONFIG_URLACL_KEY     KeyDesc;
            public HTTP_SERVICE_CONFIG_URLACL_PARAM   ParamDesc;
        }
        
		private enum HttpInitFlag : int
		{
			HTTP_INITIALIZE_SERVER = 1,
			HTTP_INITIALIZE_CONFIG = 2
		}

        [StructLayout(LayoutKind.Sequential, Pack=2)]
        private struct HTTPAPI_VERSION
        {
	        public HTTPAPI_VERSION(ushort majorVersion, ushort minorVersion)
	        {
		        major = majorVersion;
		        minor = minorVersion;
	        }

	        public ushort major;
	        public ushort minor;
        }       
        #endregion

        /// <summary>
        /// Fetches the current SSL certificate configuration.
        /// </summary>
        public static List<SslCertificateBinding> GetSslCertificateBindings()
        {
            List<SslCertificateBinding> bindings = new List<SslCertificateBinding>();

            // initialize library.
            HttpError error = NativeMethods.HttpInitialize(
                new HTTPAPI_VERSION(1,0), 
                HttpInitFlag.HTTP_INITIALIZE_CONFIG, 
                IntPtr.Zero);
            
            if (error != HttpError.NO_ERROR)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Could not initialize HTTP library.\r\nError={0}",
                    error);
            }

            // set up the iterator.
            HTTP_SERVICE_CONFIG_SSL_QUERY query = new HTTP_SERVICE_CONFIG_SSL_QUERY();

            query.QueryDesc = HTTP_SERVICE_CONFIG_QUERY_TYPE.HttpServiceConfigQueryNext;
            query.KeyDesc.pIpPort = IntPtr.Zero;

            IntPtr pInput = Marshal.AllocHGlobal(Marshal.SizeOf<HTTP_SERVICE_CONFIG_SSL_QUERY>());
            NativeMethods.ZeroMemory(pInput, Marshal.SizeOf<HTTP_SERVICE_CONFIG_SSL_QUERY>());

            IntPtr pOutput = IntPtr.Zero;

            try
            {
                // loop through each record.
                for (query.dwToken = 0; error == HttpError.NO_ERROR; query.dwToken++)
                {
                    // get the size of buffer to allocate.
                    Marshal.StructureToPtr(query, pInput, true);

                    int requiredBufferLength = 0;

                    error = NativeMethods.HttpQueryServiceConfiguration(
                        IntPtr.Zero,
                        HTTP_SERVICE_CONFIG_ID.HttpServiceConfigSSLCertInfo,
                        pInput,
                        Marshal.SizeOf<HTTP_SERVICE_CONFIG_SSL_QUERY>(),
                        pOutput,
                        requiredBufferLength,
                        out requiredBufferLength,
                        IntPtr.Zero);

                    if (error == HttpError.ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    if (error != HttpError.ERROR_INSUFFICIENT_BUFFER)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadUnexpectedError,
                            "Could not read SSL configuration information.\r\nError={0}",
                            error);
                    }

                    // allocate the buffer.
                    pOutput = Marshal.AllocHGlobal(requiredBufferLength);
                    NativeMethods.ZeroMemory(pOutput, requiredBufferLength);

                    // get the actual data.
                    error = NativeMethods.HttpQueryServiceConfiguration(
                        IntPtr.Zero,
                        HTTP_SERVICE_CONFIG_ID.HttpServiceConfigSSLCertInfo,
                        pInput,
                        Marshal.SizeOf<HTTP_SERVICE_CONFIG_SSL_QUERY>(),
                        pOutput,
                        requiredBufferLength,
                        out requiredBufferLength,
                        IntPtr.Zero);

                    if (error != HttpError.NO_ERROR)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadUnexpectedError,
                            "Could not read SSL configuration information.\r\nError={0}",
                            error);
                    }

                    HTTP_SERVICE_CONFIG_SSL_SET sslSet = (HTTP_SERVICE_CONFIG_SSL_SET)Marshal.PtrToStructure< HTTP_SERVICE_CONFIG_SSL_SET>(pOutput);

                    short family = Marshal.ReadInt16(sslSet.KeyDesc.pIpPort);
                    SslCertificateBinding binding = new SslCertificateBinding();

                    if (family == AF_INET)
                    {
                        SOCKADDR_IN inet = (SOCKADDR_IN)Marshal.PtrToStructure<SOCKADDR_IN>(sslSet.KeyDesc.pIpPort);
                        binding.IPAddress = new IPAddress(inet.addr);
                        binding.Port = inet.port;
                    }

                    if (family == AF_INET6)
                    {
                        SOCKADDR_IN6 inet = (SOCKADDR_IN6)Marshal.PtrToStructure<SOCKADDR_IN6>(sslSet.KeyDesc.pIpPort);
                        binding.IPAddress = new IPAddress(inet.addr, inet.scopeID);
                        binding.Port = inet.port;
                    }

                    binding.Port = (ushort)(((binding.Port & 0xFF00) >> 8) | ((binding.Port & 0x00FF) << 8));

                    byte[] bytes = new byte[sslSet.ParamDesc.SslHashLength];
                    Marshal.Copy(sslSet.ParamDesc.pSslHash, bytes, 0, bytes.Length);

                    binding.Thumbprint = Utils.ToHexString(bytes);
                    binding.ApplicationId = sslSet.ParamDesc.AppId;
                    binding.StoreName = sslSet.ParamDesc.pSslCertStoreName;
                    binding.DefaultCertCheckMode = sslSet.ParamDesc.DefaultCertCheckMode;
                    binding.DefaultRevocationFreshnessTime = sslSet.ParamDesc.DefaultRevocationFreshnessTime;
                    binding.DefaultRevocationUrlRetrievalTimeout = sslSet.ParamDesc.DefaultRevocationUrlRetrievalTimeout;
                    binding.DefaultSslCtlIdentifier = sslSet.ParamDesc.pDefaultSslCtlIdentifier;
                    binding.DefaultSslCtlStoreName = sslSet.ParamDesc.pDefaultSslCtlStoreName;
                    binding.DefaultFlags = sslSet.ParamDesc.DefaultFlags;

                    bindings.Add(binding);

                    Marshal.FreeHGlobal(pOutput);
                    pOutput = IntPtr.Zero;
                }
            }
            finally
            {
                if (pInput != IntPtr.Zero)
                {
                    Marshal.DestroyStructure<HTTP_SERVICE_CONFIG_SSL_QUERY>(pInput);
                    Marshal.FreeHGlobal(pInput);
                }

                if (pOutput != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pOutput);
                }

                NativeMethods.HttpTerminate(HttpInitFlag.HTTP_INITIALIZE_CONFIG, IntPtr.Zero);
            }

            return bindings;
        }

        /// <summary>
        /// Serializes an IPAddress. 
        /// </summary>
        private static IntPtr ToIntPtr(IPAddress address, ushort port)
        {
            IntPtr pAddress = IntPtr.Zero;

            if (address == null)
            {
                return pAddress;
            }
            
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                SOCKADDR_IN inet = new SOCKADDR_IN();
                inet.family = AF_INET;
                inet.addr = address.GetAddressBytes();
                inet.port = (ushort)(((port & 0xFF00) >> 8) | ((port & 0x00FF) << 8));

                pAddress = Marshal.AllocHGlobal(Marshal.SizeOf<SOCKADDR_IN>());
                Marshal.StructureToPtr(inet, pAddress, false);
            }

            else if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                SOCKADDR_IN6 inet = new SOCKADDR_IN6();
                inet.family = AF_INET6;
                inet.addr = address.GetAddressBytes();
                inet.port = (ushort)(((port & 0xFF00) >> 8) | ((port & 0x00FF) << 8));
                inet.scopeID = (int)address.ScopeId;

                pAddress = Marshal.AllocHGlobal(Marshal.SizeOf<SOCKADDR_IN6>());
                Marshal.StructureToPtr(inet, pAddress, false);
            }

            return pAddress;
        }

        /// <summary>
        /// Creates a new SSL certificate binding.
        /// </summary>
        public static void SetSslCertificateBinding(SslCertificateBinding binding)
        {
            if (binding == null) throw new ArgumentNullException("binding");

            // initialize library.
            HttpError error = NativeMethods.HttpInitialize(
                new HTTPAPI_VERSION(1,0), 
                HttpInitFlag.HTTP_INITIALIZE_CONFIG, 
                IntPtr.Zero);
            
            if (error != HttpError.NO_ERROR)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Could not initialize HTTP library.\r\nError={0}",
                    error);
            }

            IntPtr pAddress = IntPtr.Zero;
            IntPtr pThumprint = IntPtr.Zero;
            IntPtr pConfigInfo = IntPtr.Zero;

            try
            {
                pAddress = ToIntPtr(binding.IPAddress, binding.Port);

                byte[] thumbprint = Utils.FromHexString(binding.Thumbprint);
                pThumprint = Marshal.AllocCoTaskMem(thumbprint.Length);
                Marshal.Copy(thumbprint, 0, pThumprint, thumbprint.Length);
                
                HTTP_SERVICE_CONFIG_SSL_SET configSslSet = new HTTP_SERVICE_CONFIG_SSL_SET();

                configSslSet.KeyDesc.pIpPort = pAddress;
                configSslSet.ParamDesc.pSslHash = pThumprint;
                configSslSet.ParamDesc.SslHashLength = thumbprint.Length;
                configSslSet.ParamDesc.AppId = binding.ApplicationId;
                configSslSet.ParamDesc.pSslCertStoreName = binding.StoreName;
                configSslSet.ParamDesc.DefaultCertCheckMode = binding.DefaultCertCheckMode;
                configSslSet.ParamDesc.DefaultFlags = binding.DefaultFlags;
                configSslSet.ParamDesc.DefaultRevocationFreshnessTime = binding.DefaultRevocationFreshnessTime;
                configSslSet.ParamDesc.DefaultRevocationUrlRetrievalTimeout = binding.DefaultRevocationUrlRetrievalTimeout;
                configSslSet.ParamDesc.pDefaultSslCtlIdentifier = binding.DefaultSslCtlIdentifier;
                configSslSet.ParamDesc.pDefaultSslCtlStoreName = binding.DefaultSslCtlStoreName;

                int size = Marshal.SizeOf<HTTP_SERVICE_CONFIG_SSL_SET>();
                pConfigInfo = Marshal.AllocCoTaskMem(size);
                Marshal.StructureToPtr(configSslSet, pConfigInfo, false);

                error = NativeMethods.HttpSetServiceConfiguration(
                    IntPtr.Zero,
                    HTTP_SERVICE_CONFIG_ID.HttpServiceConfigSSLCertInfo,
                    pConfigInfo,
                    size,
                    IntPtr.Zero);

                if (error == HttpError.ERROR_ALREADY_EXISTS)
                {
                    error = NativeMethods.HttpDeleteServiceConfiguration(
                        IntPtr.Zero,
                        HTTP_SERVICE_CONFIG_ID.HttpServiceConfigSSLCertInfo,
                        pConfigInfo,
                        size,
                        IntPtr.Zero);

                    if (error != HttpError.NO_ERROR)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadUnexpectedError,
                            "Could not delete existing SSL certificate binding.\r\nError={0}",
                            error);
                    }

                    error = NativeMethods.HttpSetServiceConfiguration(
                        IntPtr.Zero,
                        HTTP_SERVICE_CONFIG_ID.HttpServiceConfigSSLCertInfo,
                        pConfigInfo,
                        size,
                        IntPtr.Zero);
                }

                if (error != HttpError.NO_ERROR)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadUnexpectedError,
                        "Could not create SSL certificate binding.\r\nError={0}",
                        error);
                }
            }
            finally
            {
                if (pConfigInfo != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pConfigInfo);
                }

                if (pAddress != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pAddress);
                }

                if (pThumprint != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pThumprint);
                }

                NativeMethods.HttpTerminate(HttpInitFlag.HTTP_INITIALIZE_CONFIG, IntPtr.Zero);
            }
        }

        /// <summary>
        /// Deletes a new SSL certificate binding.
        /// </summary>
        public static void DeleteSslCertificateBinding(IPAddress address, ushort port)
        {
            if (address == null) throw new ArgumentNullException("address");

            // initialize library.
            HttpError error = NativeMethods.HttpInitialize(
                new HTTPAPI_VERSION(1, 0),
                HttpInitFlag.HTTP_INITIALIZE_CONFIG,
                IntPtr.Zero);

            if (error != HttpError.NO_ERROR)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Could not initialize HTTP library.\r\nError={0}",
                    error);
            }

            IntPtr pAddress = IntPtr.Zero;
            IntPtr pConfigInfo = IntPtr.Zero;

            try
            {
                pAddress = ToIntPtr(address, port);

                HTTP_SERVICE_CONFIG_SSL_SET configSslSet = new HTTP_SERVICE_CONFIG_SSL_SET();
                configSslSet.KeyDesc.pIpPort = pAddress;

                int size = Marshal.SizeOf<HTTP_SERVICE_CONFIG_SSL_SET>();
                pConfigInfo = Marshal.AllocCoTaskMem(size);
                Marshal.StructureToPtr(configSslSet, pConfigInfo, false);

                error = NativeMethods.HttpDeleteServiceConfiguration(
                    IntPtr.Zero,
                    HTTP_SERVICE_CONFIG_ID.HttpServiceConfigSSLCertInfo,
                    pConfigInfo,
                    size,
                    IntPtr.Zero);

                if (error != HttpError.NO_ERROR)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadUnexpectedError,
                        "Could not delete existing SSL certificate binding.\r\nError={0}",
                        error);
                }
            }
            finally
            {
                if (pConfigInfo != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pConfigInfo);
                }

                if (pAddress != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pAddress);
                }

                NativeMethods.HttpTerminate(HttpInitFlag.HTTP_INITIALIZE_CONFIG, IntPtr.Zero);
            }
        }

        #region Static Methods
        /// <summary>
        /// Gets the application access rules for the specified URL.
        /// </summary>
        public static IList<HttpAccessRule> GetAccessRules(string url)
        {  
            List<HttpAccessRule> accessRules = new List<HttpAccessRule>();
            
            HttpError error = NativeMethods.HttpInitialize(
                new HTTPAPI_VERSION(1, 0),
                HttpInitFlag.HTTP_INITIALIZE_CONFIG,
                IntPtr.Zero);

            if (error != HttpError.NO_ERROR)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Could not initialize HTTP library.\r\nError={0}",
                    error);
            }

            HTTP_SERVICE_CONFIG_URLACL_QUERY query = new HTTP_SERVICE_CONFIG_URLACL_QUERY();
                        
            query.QueryDesc = HTTP_SERVICE_CONFIG_QUERY_TYPE.HttpServiceConfigQueryNext;

            if (!String.IsNullOrEmpty(url))
            {
                query.QueryDesc = HTTP_SERVICE_CONFIG_QUERY_TYPE.HttpServiceConfigQueryExact;
                query.KeyDesc.pUrlPrefix = url;
            }
            
            IntPtr pInput = Marshal.AllocHGlobal(Marshal.SizeOf<HTTP_SERVICE_CONFIG_URLACL_QUERY>());
            NativeMethods.ZeroMemory(pInput, Marshal.SizeOf<HTTP_SERVICE_CONFIG_URLACL_QUERY>());

            IntPtr pOutput = IntPtr.Zero;

            try
            {
                for (query.dwToken = 0; error == HttpError.NO_ERROR; query.dwToken++)
                {                    
                    Marshal.StructureToPtr(query, pInput, true);

                    int requiredBufferLength = 0;
                                        
                    error = NativeMethods.HttpQueryServiceConfiguration(
                        IntPtr.Zero,
                        HTTP_SERVICE_CONFIG_ID.HttpServiceConfigUrlAclInfo,
                        pInput,
                        Marshal.SizeOf<HTTP_SERVICE_CONFIG_URLACL_QUERY>(),
                        pOutput,
                        requiredBufferLength,
                        out requiredBufferLength,
                        IntPtr.Zero);
                    
                    if (error == HttpError.ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }
                    
                    if (!String.IsNullOrEmpty(url))
                    {
                        if (error == HttpError.ERROR_FILE_NOT_FOUND)
                        {
                            break;
                        }
                    }

                    if (error != HttpError.ERROR_INSUFFICIENT_BUFFER)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadUnexpectedError,
                            "Could not read access rules for HTTP url.\r\nError={1}, Url={0}",
                            url,
                            error);
                    }

                    pOutput = Marshal.AllocHGlobal(requiredBufferLength);
                    NativeMethods.ZeroMemory(pOutput, requiredBufferLength);

                    error = NativeMethods.HttpQueryServiceConfiguration(
                        IntPtr.Zero,
                        HTTP_SERVICE_CONFIG_ID.HttpServiceConfigUrlAclInfo,
                        pInput,
                        Marshal.SizeOf<HTTP_SERVICE_CONFIG_URLACL_QUERY>(),
                        pOutput,
                        requiredBufferLength,
                        out requiredBufferLength,
                        IntPtr.Zero);
                    
                    if (error != HttpError.NO_ERROR)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadUnexpectedError,
                            "Could not read access rules for HTTP url.\r\nError={1}, Url={0}",
                            url,
                            error);
                    }
                    
                    HTTP_SERVICE_CONFIG_URLACL_SET result =  (HTTP_SERVICE_CONFIG_URLACL_SET)Marshal.PtrToStructure<HTTP_SERVICE_CONFIG_URLACL_SET>(pOutput);
                    
                    // parse the SDDL and update the access list.
                    ParseSddl(result.KeyDesc.pUrlPrefix, result.ParamDesc.pStringSecurityDescriptor, accessRules);
                    
                    Marshal.FreeHGlobal(pOutput);
                    pOutput = IntPtr.Zero;

                    // all done if requesting the results for a single url.
                    if (!String.IsNullOrEmpty(url))
                    {
                        break;
                    }
                }
            }
            finally
            {
                if (pInput != IntPtr.Zero)
                {
                    Marshal.DestroyStructure<HTTP_SERVICE_CONFIG_URLACL_QUERY>(pInput);
                    Marshal.FreeHGlobal(pInput);
                }

                if (pOutput != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pOutput);
                }

                NativeMethods.HttpTerminate(HttpInitFlag.HTTP_INITIALIZE_CONFIG, IntPtr.Zero);
            }            

            return accessRules;
        }
        
        /// <summary>
        /// Sets the application access rules for the specified URL. 
        /// </summary>
        public static void SetAccessRules(string url, IList<HttpAccessRule> rules, bool replaceExisting)
        {
            HttpError error = NativeMethods.HttpInitialize(
                new HTTPAPI_VERSION(1, 0),
                HttpInitFlag.HTTP_INITIALIZE_CONFIG,
                IntPtr.Zero);

            if (error != HttpError.NO_ERROR)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Could not initialize HTTP library.\r\nError={0}",
                    error);
            }

            // fetch existing rules if not replacing them.
            if (!replaceExisting)
            {
                IList<HttpAccessRule> existingRules = GetAccessRules(url);

                if (existingRules.Count > 0)
                {                
                    List<HttpAccessRule> mergedRules = new List<HttpAccessRule>(existingRules);
                    mergedRules.AddRange(rules);
                    rules = mergedRules;
                }
            }

            HTTP_SERVICE_CONFIG_URLACL_SET update = new HTTP_SERVICE_CONFIG_URLACL_SET();

            update.KeyDesc.pUrlPrefix = url;
            update.ParamDesc.pStringSecurityDescriptor = FormatSddl(rules);
            
            IntPtr pStruct = IntPtr.Zero;
            int updateSize = Marshal.SizeOf<HTTP_SERVICE_CONFIG_URLACL_SET>();

            try
            {
                pStruct = Marshal.AllocHGlobal(updateSize);
                NativeMethods.ZeroMemory(pStruct, updateSize);

                Marshal.StructureToPtr(update, pStruct, false);

                error = NativeMethods.HttpDeleteServiceConfiguration(
                    IntPtr.Zero,
                    HTTP_SERVICE_CONFIG_ID.HttpServiceConfigUrlAclInfo,
                    pStruct,
                    updateSize,
                    IntPtr.Zero);
                       
                if (error != HttpError.ERROR_FILE_NOT_FOUND && error != HttpError.NO_ERROR)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadUnexpectedError,
                        "Could not delete existing access rules for HTTP url.\r\nError={1}, Url={0}",
                        url,
                        error);
                }
                
                if (rules.Count > 0)
                {
                    error = NativeMethods.HttpSetServiceConfiguration(
                        IntPtr.Zero,
                        HTTP_SERVICE_CONFIG_ID.HttpServiceConfigUrlAclInfo,
                        pStruct,
                        updateSize,
                        IntPtr.Zero);

                    if (error != HttpError.NO_ERROR)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadUnexpectedError,
                            "Could not set the access rules for HTTP url.\r\nError={1}, Url={0}",
                            url,
                            error);
                    }        
                }
            }
            finally
            {
                if (pStruct != IntPtr.Zero)
                {
                    Marshal.DestroyStructure<HTTP_SERVICE_CONFIG_URLACL_SET>(pStruct);
                    Marshal.FreeHGlobal(pStruct);
                }

                NativeMethods.HttpTerminate(HttpInitFlag.HTTP_INITIALIZE_CONFIG, IntPtr.Zero);
            }
        }
        
        /// <summary>
        /// Sets the application access rules for the specified URL (replaces the hostname with a wildcard). 
        /// </summary>
        public static void SetAccessRules(Uri url, IList<ApplicationAccessRule> accessRules, bool replaceExisting)
        {
            StringBuilder wildcard = new StringBuilder();

            wildcard.Append(url.Scheme);
            wildcard.Append("://+:");
            wildcard.Append(url.Port);
            wildcard.Append(url.PathAndQuery);

            List<HttpAccessRule> httpRules = new List<HttpAccessRule>();

            foreach (ApplicationAccessRule accessRule in accessRules)
            {
                // urls do not support deny rules.
                if (accessRule.RuleType == AccessControlType.Deny)
                {
                    continue;
                }
                
                string identityName = accessRule.IdentityName;

                if (accessRule.IdentityName.StartsWith("S-"))
                {
                    Utils.Trace("Could not translate SID: {0}", accessRule.IdentityName);
                    continue;
                }

                HttpAccessRule httpRule = new HttpAccessRule();
                
                httpRule.Right = accessRule.Right;
                httpRule.IdentityName = identityName;

                httpRules.Add(httpRule);
            }

            SetAccessRules(wildcard.ToString(), httpRules, replaceExisting);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Extracts the access rules from the SDDL string.
        /// </summary>
        private static void ParseSddl(string url, string sddl, List<HttpAccessRule> accessRules)
        {
            IList<AccessControlEntity> entities = AccessControlEntity.Parse(sddl);

            for (int ii = 0; ii < entities.Count; ii++)
            {
                AccessControlEntity entity = entities[ii];

                if (entity.AccessType != "A")
                {
                    continue;
                }

                ApplicationAccessRight rights = ApplicationAccessRight.None;

                switch (entity.Rights)
                {
                    case "GA":                 
                    case "GXGW":             
                    case "GWGX":
                    {
                        rights = ApplicationAccessRight.Configure;
                        break;
                    }
                   
                    case "GX":  
                    {
                        rights = ApplicationAccessRight.Run;
                        break;
                    }
                }
                
                if (rights == ApplicationAccessRight.None)
                {
                    continue;
                }
            }
        }
        
        /// <summary>
        /// Extracts the access rules from the SDDL string.
        /// </summary>
        private static string FormatSddl(IList<HttpAccessRule> accessRules)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("D:");

            for (int ii = 0; ii < accessRules.Count; ii++)
            {
                builder.Append("(");  // start of ACE
                builder.Append("A;"); // access type
                builder.Append(";");  // flags

                switch (accessRules[ii].Right)
                {
                    case ApplicationAccessRight.Configure:
                    {
                        builder.Append("GXGW;");  // rights
                        break;
                    }
                        
                    case ApplicationAccessRight.Run:
                    case ApplicationAccessRight.Update:
                    {
                        builder.Append("GX;");  // rights
                        break;
                    }
                }
                
                builder.Append(";"); // object guid
                builder.Append(";"); // inherited object guid
                
                builder.Append(')'); // end of ace.
            }

            return builder.ToString();
        }
        #endregion

        #region Private Fields
        private string m_urlPrefix;
        private ApplicationAccessRight m_right;
        private string m_identityName;
        #endregion
    }

    #region AccessControlEntity Class
    /// <summary>
    /// A class that stores the components of ACE within a DACL.
    /// </summary>
    public class AccessControlEntity
    {
        /// <summary>
        /// The access type granted by the ACE.
        /// </summary>
        public string AccessType
        {
            get { return m_accessType;  }
            set { m_accessType = value; }
        }
        
        /// <summary>
        /// Any flags associated with the ACE.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1726:UsePreferredTerms", MessageId = "Flags")]
        public string Flags
        {
            get { return m_flags;  }
            set { m_flags = value; }
        }
        
        /// <summary>
        /// The rights allowed/restricted by the ACE.
        /// </summary>
        public string Rights
        {
            get { return m_rights;  }
            set { m_rights = value; }
        }
        
        /// <summary>
        /// The object associated with the ACE.
        /// </summary>
        public string ObjectGuid
        {
            get { return m_objectGuid;  }
            set { m_objectGuid = value; }
        }
        
        /// <summary>
        /// The inherited object associated with the ACE.
        /// </summary>
        public string InheritObjectGuid
        {
            get { return m_inheritObjectGuid;  }
            set { m_inheritObjectGuid = value; }
        }
        
        /// <summary>
        /// The SID for the account which is affected by the ACE.
        /// </summary>
        public string AccountSid
        {
            get { return m_accountSid;  }
            set { m_accountSid = value; }
        }        
        
        /// <summary>
        /// Extracts a list of ACEs from a SDDL string.
        /// </summary>
        public static IList<AccessControlEntity> Parse(string sddl)
        {
            List<AccessControlEntity> entities = new List<AccessControlEntity>();

            if (!sddl.StartsWith("D:", StringComparison.Ordinal))
            {
                throw new ArgumentException(Utils.Format("Could not parse SDDL string: {0}", sddl));
            }

            sddl = sddl.Substring(2, sddl.Length-2);

            string[] aces = sddl.Split('(', ')');

            for (int ii = 0; ii < aces.Length; ii++)
            {
                if (String.IsNullOrEmpty(aces[ii]))
                {
                    continue;
                }
                
                AccessControlEntity entity = new AccessControlEntity();
                entity.Initialize(aces[ii]);
                entities.Add(entity);
            }

            return entities;
        }

        /// <summary>
        /// Extracts a single ACE from a SDDL string fragment.
        /// </summary>
        public void Initialize(string sddl)
        {
            string[] fields = sddl.Split(';');

            if (fields.Length != 6)
            {
                throw new ArgumentException(Utils.Format("Could not parse SDDL ACE string: {0}", sddl));
            }
            
            m_accessType = fields[0];
            m_flags = fields[1];
            m_rights = fields[2];
            m_objectGuid = fields[3];
            m_inheritObjectGuid = fields[4];
            m_accountSid = fields[5];
        }

        private string m_accessType;
        private string m_flags;
        private string m_rights;
        private string m_objectGuid;
        private string m_inheritObjectGuid;
        private string m_accountSid;
    }
    #endregion

    #region SslCertificateBinding Class
    /// <summary>
    /// Stores the details of an SSL certification configuration binding.
    /// </summary>
    public class SslCertificateBinding
    {
        /// <summary>
        /// The IP Address.
        /// </summary>
        public IPAddress IPAddress { get; set; }

        /// <summary>
        /// The port number.
        /// </summary>
        public ushort Port { get; set; }

        /// <summary>
        /// The certificate thumbprint.
        /// </summary>
        public string Thumbprint { get; set; }

        /// <summary>
        /// The application id.
        /// </summary>
        public Guid ApplicationId { get; set; }

        /// <summary>
        /// The names of the store to use.
        /// </summary>
        public string StoreName { get; set; }

        /// <summary>
        /// The default revocation check mode.
        /// </summary>
        public uint DefaultCertCheckMode { get; set; }

        /// <summary>
        /// The default revocation freshness time.
        /// </summary>
        public int DefaultRevocationFreshnessTime { get; set; }

        /// <summary>
        /// The default revocation URL timeout.
        /// </summary>
        public int DefaultRevocationUrlRetrievalTimeout { get; set; }

        /// <summary>
        /// The default certificate trust list identifier.
        /// </summary>
        public string DefaultSslCtlIdentifier { get; set; }

        /// <summary>
        /// The default certificate trust list store.
        /// </summary>
        public string DefaultSslCtlStoreName { get; set; }

        /// <summary>
        /// The default flags.
        /// </summary>
        public uint DefaultFlags { get; set; }
    }
    #endregion
}
