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
using System.IO;
using System.Diagnostics;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Stores information about an account.
    /// </summary>
    public class AccountInfo : IComparable
    {
        #region Public Properties
        /// <summary>
        /// The name of the account.
        /// </summary>
        public string Name
        {
            get { return m_name;  } 
            set { m_name = value; }
        }

        /// <summary>
        /// The domain that the account belongs to.
        /// </summary>
        public string Domain
        {
            get { return m_domain;  } 
            set { m_domain = value; }
        }

        /// <summary>
        /// The SID for the account.
        /// </summary>
        public string Sid
        {
            get { return m_sid;  } 
            set { m_sid = value; }
        }

        /// <summary>
        /// The type of SID used by the account.
        /// </summary>
        public AccountSidType SidType
        {
            get { return m_sidType;  } 
            set { m_sidType = value; }
        }

        /// <summary>
        /// Thr description for the account.
        /// </summary>
        public string Description
        {
            get { return m_description;  } 
            set { m_description = value; }
        }

        /// <summary>
        /// Thr current status for the account.
        /// </summary>
        public string Status
        {
            get { return m_status;  } 
            set { m_status = value; }
        }
        #endregion 
        
        #region Overridden Methods
        /// <summary cref="Object.ToString()" />
        public override string ToString()
        {
            if (String.IsNullOrEmpty(m_name))
            {
                return m_sid;
            }
            
            if (!String.IsNullOrEmpty(m_domain))
            {
                return Utils.Format("{0}{1}{2}", m_domain, Path.DirectorySeparatorChar, m_name);
            }

            return m_name;
        }
        #endregion 

        #region IComparable Members
        /// <summary>
        /// Compares the obj.
        /// </summary>
        public int CompareTo(object obj)
        {
            AccountInfo target = obj as AccountInfo;

            if (Object.ReferenceEquals(target, null))
            {
                return -1;
            }

            if (Object.ReferenceEquals(target, this))
            {
                return 0;
            }
            
            if (m_domain == null)
            {
                return (target.m_domain == null)?0:-1;
            }

            int result = m_domain.CompareTo(target.m_domain);

            if (result != 0)
            {
                return result;
            }

            if (m_name == null)
            {
                return (target.m_name == null)?0:-1;
            }

            result = m_name.CompareTo(target.m_name);

            if (result != 0)
            {
                return result;
            }
            
            if (m_sid == null)
            {
                return (target.m_sid == null)?0:-1;
            }

            return m_sid.CompareTo(target.m_sid);
        }
        #endregion
 
        #region Public Methods
        /// <summary>
        /// Applies the filters to the accounts.
        /// </summary>
        public static IList<AccountInfo> ApplyFilters(AccountFilters filters, IList<AccountInfo> accounts)
        {
            if (filters == null || accounts == null)
            {
                return accounts;
            }

            List<AccountInfo> filteredAccounts = new  List<AccountInfo>();

            for (int ii = 0; ii < accounts.Count; ii++)
            {                
                if (accounts[ii].ApplyFilters(filters))
                {
                    filteredAccounts.Add(accounts[ii]);
                }
            }

            return filteredAccounts;
        }
        
        /// <summary>
        /// Applies the filters to the account
        /// </summary>
        public bool ApplyFilters(AccountFilters filters)
        {
            // filter on name.
            if (!String.IsNullOrEmpty(filters.Name))
            {
                if (!Utils.Match(this.Name, filters.Name, false))
                {
                    return false;
                }
            }

            // filter on domain.
            if (!String.IsNullOrEmpty(filters.Domain))
            {
                if (String.Compare(this.Domain, filters.Domain, true) != 0)
                {
                    return false;
                }
            }
                
            // exclude non-user related accounts.
            if (this.SidType == AccountSidType.Domain || this.SidType > AccountSidType.BuiltIn)
            {
                return false;
            }

            // apply account type filter.
            if (filters.AccountTypeMask != AccountTypeMask.None)
            {
                if ((1<<((int)this.SidType-1) & (int)filters.AccountTypeMask) == 0)
                {
                    return false;
                }
            }

            return true;
        }
#endregion

#region Private Fields
        private string m_name;
        private string m_domain;
        private string m_sid;
        private AccountSidType m_sidType;
        private string m_description;
        private string m_status;
#endregion
    }
    
#region AccountSidType Enumeration
    /// <summary>
    /// The type of SID used by the account.
    /// </summary>
    public enum AccountSidType : byte
    {        
        /// <summary>
        /// An interactive user account.
        /// </summary>
        User = 0x1,

        /// <summary>
        /// An group of users.
        /// </summary>
        Group = 0x2,

        /// <summary>
        /// A domain.
        /// </summary>
        Domain = 0x3,

        /// <summary>
        /// An alias for a group or user.
        /// </summary>
        Alias = 0x4,

        /// <summary>
        /// Built-in identity principals.
        /// </summary>
        BuiltIn = 0x5
    }
#endregion
    
#region AccountFilters Class
    /// <summary>
    /// Filters that can be used to restrict the set of accounts returned.
    /// </summary>
    public class AccountFilters
    {
#region Public Properties
        /// <summary>
        /// The name of the account (supports the '*' wildcard).
        /// </summary>
        public string Name
        {
            get { return m_name;  } 
            set { m_name = value; }
        }

        /// <summary>
        /// The domain that the account belongs to.
        /// </summary>
        public string Domain
        {
            get { return m_domain;  } 
            set { m_domain = value; }
        }
        

        /// <summary>
        /// The types of accounts.
        /// </summary>
        public AccountTypeMask AccountTypeMask
        {
            get { return m_accountTypeMask;  } 
            set { m_accountTypeMask = value; }
        }
#endregion

#region Private Fields
        private string m_name;
        private string m_domain;
        private AccountTypeMask m_accountTypeMask;
#endregion
    }
#endregion
    
#region AccountTypeMask Enumeration
    /// <summary>
    /// The masks that can be use to filter a list of accounts.
    /// </summary>
    [Flags]
    public enum AccountTypeMask
    {        
        /// <summary>
        /// Mask not specified.
        /// </summary>
        None = 0x0,

        /// <summary>
        /// An interactive user account.
        /// </summary>
        User = 0x1,

        /// <summary>
        /// An NT user group.
        /// </summary>
        Group = 0xA,

        /// <summary>
        /// Well-known groups.
        /// </summary>
        WellKnownGroup = 0x10
    }
#endregion
    
#region WellKnownSids Class
    /// <summary>
    /// The well known NT security identifiers.
    /// </summary>
    public static class WellKnownSids
    {
        /// <summary>
        /// Interactive users.
        /// </summary>
        public const string Interactive = "S-1-5-4";

        /// <summary>
        /// Authenticated users.
        /// </summary>
        public const string AuthenticatedUser = "S-1-5-11";

        /// <summary>
        /// Anonymous Logons
        /// </summary>
        public const string AnonymousLogon = "S-1-5-7";

        /// <summary>
        /// The local system account.
        /// </summary>
        public const string LocalSystem = "S-1-5-18";

        /// <summary>
        /// The local service account.
        /// </summary>
        public const string LocalService = "S-1-5-19";

        /// <summary>
        /// The network service account.
        /// </summary>
        public const string NetworkService  = "S-1-5-20";   

        /// <summary>
        /// The administrators group.
        /// </summary>     
        public const string Administrators  = "S-1-5-32-544";

        /// <summary>
        /// The users group.
        /// </summary>   
        public const string Users = "S-1-5-32-545";

        /// <summary>
        /// The guests group.
        /// </summary>   
        public const string Guests = "S-1-5-32-546";
    }
#endregion
}
