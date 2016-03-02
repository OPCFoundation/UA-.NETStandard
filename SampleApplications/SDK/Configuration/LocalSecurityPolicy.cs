/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Allows to add privileges to Local Security Policy.
    /// You can use this class to add the LogOn as service privilege to an account.
    /// </summary>
    public class LocalSecurityPolicy : IDisposable
    {
        #region DllImport

        [DllImport("advapi32.dll", PreserveSig = true)]
        private static extern UInt32 LsaOpenPolicy(
            ref LSA_UNICODE_STRING SystemName,
            ref LSA_OBJECT_ATTRIBUTES ObjectAttributes,
            Int32 DesiredAccess,
            out IntPtr PolicyHandle
        );

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern long LsaAddAccountRights(
            IntPtr PolicyHandle,
            IntPtr AccountSid,
            LSA_UNICODE_STRING[] UserRights,
            long CountOfRights);

        [DllImport("advapi32")]
        private static extern void FreeSid(IntPtr pSid);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = true)]
        private static extern bool LookupAccountName(
            string lpSystemName, string lpAccountName,
            IntPtr psid,
            ref int cbsid,
            StringBuilder domainName, ref int cbdomainLength, ref int use);

        [DllImport("advapi32.dll")]
        private static extern bool IsValidSid(IntPtr pSid);

        [DllImport("advapi32.dll")]
        private static extern long LsaClose(IntPtr ObjectHandle);

        [DllImport("kernel32.dll")]
        private static extern int GetLastError();

        [DllImport("advapi32.dll")]
        private static extern long LsaNtStatusToWinError(long status);


        #endregion

        #region Struct

        [StructLayout(LayoutKind.Sequential)]
        private struct LSA_UNICODE_STRING
        {
            public UInt16 Length;
            public UInt16 MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LSA_OBJECT_ATTRIBUTES
        {
            public int Length;
            public IntPtr RootDirectory;
            public LSA_UNICODE_STRING ObjectName;
            public UInt32 Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        #endregion

        #region Enum

        private enum LSA_AccessPolicy : long
        {
            POLICY_VIEW_LOCAL_INFORMATION = 0x00000001L,
            POLICY_VIEW_AUDIT_INFORMATION = 0x00000002L,
            POLICY_GET_PRIVATE_INFORMATION = 0x00000004L,
            POLICY_TRUST_ADMIN = 0x00000008L,
            POLICY_CREATE_ACCOUNT = 0x00000010L,
            POLICY_CREATE_SECRET = 0x00000020L,
            POLICY_CREATE_PRIVILEGE = 0x00000040L,
            POLICY_SET_DEFAULT_QUOTA_LIMITS = 0x00000080L,
            POLICY_SET_AUDIT_REQUIREMENTS = 0x00000100L,
            POLICY_AUDIT_LOG_ADMIN = 0x00000200L,
            POLICY_SERVER_ADMIN = 0x00000400L,
            POLICY_LOOKUP_NAMES = 0x00000800L,
            POLICY_NOTIFICATION = 0x00001000L
        }

        #endregion

        #region Const

        const uint STATUS_ACCESS_DENIED = 0xc0000022;
        const uint STATUS_INSUFFICIENT_RESOURCES = 0xc000009a;
        const uint STATUS_NO_MEMORY = 0xc0000017;

        #endregion

        private IntPtr lsaHandle;

        /// <summary>
        /// Constructor for <see cref="LocalSecurityPolicy"/>
        /// </summary>
        public LocalSecurityPolicy()
            : this(null)
        { }

        /// <summary>
        /// Constructor for <see cref="LocalSecurityPolicy"/>
        /// </summary>
        /// <param name="systemName">local system if systemName is null</param>
        public LocalSecurityPolicy(string systemName)
        {
            lsaHandle = IntPtr.Zero;
            LSA_UNICODE_STRING system = InitLsaString(systemName);
            

            //combine all policies
            int access = (int)(
                LSA_AccessPolicy.POLICY_AUDIT_LOG_ADMIN |
                LSA_AccessPolicy.POLICY_CREATE_ACCOUNT |
                LSA_AccessPolicy.POLICY_CREATE_PRIVILEGE |
                LSA_AccessPolicy.POLICY_CREATE_SECRET |
                LSA_AccessPolicy.POLICY_GET_PRIVATE_INFORMATION |
                LSA_AccessPolicy.POLICY_LOOKUP_NAMES |
                LSA_AccessPolicy.POLICY_NOTIFICATION |
                LSA_AccessPolicy.POLICY_SERVER_ADMIN |
                LSA_AccessPolicy.POLICY_SET_AUDIT_REQUIREMENTS |
                LSA_AccessPolicy.POLICY_SET_DEFAULT_QUOTA_LIMITS |
                LSA_AccessPolicy.POLICY_TRUST_ADMIN |
                LSA_AccessPolicy.POLICY_VIEW_AUDIT_INFORMATION |
                LSA_AccessPolicy.POLICY_VIEW_LOCAL_INFORMATION
                );
            //initialize a pointer for the policy handle
            IntPtr policyHandle = IntPtr.Zero;

            //these attributes are not used, but LsaOpenPolicy wants them to exists
            LSA_OBJECT_ATTRIBUTES ObjectAttributes = new LSA_OBJECT_ATTRIBUTES();
            ObjectAttributes.Length = 0;
            ObjectAttributes.RootDirectory = IntPtr.Zero;
            ObjectAttributes.Attributes = 0;
            ObjectAttributes.SecurityDescriptor = IntPtr.Zero;
            ObjectAttributes.SecurityQualityOfService = IntPtr.Zero;

            //get a policy handle
            uint ret = LsaOpenPolicy(ref system, ref ObjectAttributes, access, out lsaHandle);
            if (ret == 0)
                return;
            if (ret == STATUS_ACCESS_DENIED)
            {
                throw new UnauthorizedAccessException();
            }
            if ((ret == STATUS_INSUFFICIENT_RESOURCES) || (ret == STATUS_NO_MEMORY))
            {
                throw new OutOfMemoryException();
            }
            throw new Win32Exception((int)LsaNtStatusToWinError(ret));
        }


        /// <summary>
        /// Add privileges for the given account
        /// </summary>
        /// <param name="account">The account name (domain\userName)</param>
        /// <param name="privilege">The name of the privilege to add</param>
        public void AddPrivilege(string account, string privilege)
        {
            IntPtr pSid = GetSIDInformation(account);
            LSA_UNICODE_STRING[] privileges = new LSA_UNICODE_STRING[1];
            privileges[0] = InitLsaString(privilege);
            long ret = LsaAddAccountRights(lsaHandle, pSid, privileges, 1);
            if (ret != 0)//ret = 0 Success
            {
                if (ret == STATUS_ACCESS_DENIED)
                    throw new UnauthorizedAccessException();
                if ((ret == STATUS_INSUFFICIENT_RESOURCES) || (ret == STATUS_NO_MEMORY))
                    throw new OutOfMemoryException();
                
                throw new Win32Exception((int)LsaNtStatusToWinError((int)ret));
            }
        }

        /// <summary>
        /// Add the privilege for the given account to logon as service.
        /// </summary>
        /// <param name="account">The account name (domain\userName)</param>
        public void AddLogonAsServicePrivilege(string account)
        {
            AddPrivilege(account, "SeServiceLogonRight");
        }


        /// <summary>
        /// Release all unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            if (lsaHandle != IntPtr.Zero)
            {
                LsaClose(lsaHandle);
                lsaHandle = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// 
        /// </summary>
        ~LocalSecurityPolicy()
        { Dispose(); }


        #region helper functions

        IntPtr GetSIDInformation(string account)
        {
            //pointer an size for the SID
			IntPtr sid = IntPtr.Zero;
			int sidSize = 0;
            //StringBuilder and size for the domain name
			StringBuilder domainName = new StringBuilder();
			int nameSize = 0;
			//account-type variable for lookup
			int accountType = 0;

			//get required buffer size
            LookupAccountName(String.Empty, account, sid, ref sidSize, domainName, ref nameSize, ref accountType);
			
			//allocate buffers
			domainName = new StringBuilder(nameSize);
			sid = Marshal.AllocHGlobal(sidSize);

			//lookup the SID for the account
            bool result = LookupAccountName(String.Empty, account, sid, ref sidSize, domainName, ref nameSize, ref accountType);			
            if(!result)
            {
                Marshal.ThrowExceptionForHR(GetLastError());
            }    
            return sid;
        }

        private static LSA_UNICODE_STRING InitLsaString(string s)
        {
            if (string.IsNullOrEmpty(s))
                return new LSA_UNICODE_STRING();

            // Unicode strings max. 32KB
            if (s.Length > 0x7ffe)
                throw new ArgumentException("String too long");
            LSA_UNICODE_STRING lus = new LSA_UNICODE_STRING();
            lus.Buffer = Marshal.StringToHGlobalUni(s);
            lus.Length = (UInt16)(s.Length * sizeof(char));
            lus.MaximumLength = (UInt16)((s.Length + 1) * sizeof(char));			
            return lus;
        }

        #endregion
    }

}
