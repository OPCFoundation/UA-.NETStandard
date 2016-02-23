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
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Describes a certificate store.
    /// </summary>
    public partial class CertificateStoreIdentifier : IFormattable
    {
        /// <summary>
        /// Creates a new object that is a copy of the current instance.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            return base.MemberwiseClone();
        }

        #region IFormattable Members
        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="T:System.String"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="T:System.IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="T:System.IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current instance in the specified format.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (!String.IsNullOrEmpty(format))
            {
                throw new FormatException();
            }

            return ToString();
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            if (String.IsNullOrEmpty(this.StoreType))
            {
                return Utils.Format("{0}", this.StorePath);
            }

            return Utils.Format("[{0}]{1}", this.StoreType, this.StorePath);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Options that can be used to suppress certificate validation errors.
        /// </summary>
        public CertificateValidationOptions ValidationOptions
        {
            get { return m_validationOptions; }
            set { m_validationOptions = value; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Detects the type of store represented by the path.
        /// </summary>
        public static string DetermineStoreType(string storePath)
        {
            if (String.IsNullOrEmpty(storePath))
            {
                return CertificateStoreType.Directory;
            }

            if (storePath.StartsWith("LocalMachine\\", StringComparison.OrdinalIgnoreCase))
            {
                return CertificateStoreType.Windows;
            }

            if (storePath.StartsWith("CurrentUser\\", StringComparison.OrdinalIgnoreCase))
            {
                return CertificateStoreType.Windows;
            }

            if (storePath.StartsWith("User\\", StringComparison.OrdinalIgnoreCase))
            {
                return CertificateStoreType.Windows;
            }

            if (storePath.StartsWith("Service\\", StringComparison.OrdinalIgnoreCase))
            {
                return CertificateStoreType.Windows;
            }

            return CertificateStoreType.Directory;
        }

        /// <summary>
        /// Returns an object that can be used to access the store.
        /// </summary>
        public static ICertificateStore CreateStore(string storeType)
        {
            ICertificateStore store = null;

            if (String.IsNullOrEmpty(storeType))
            {
                return (ICertificateStore) new CertificateIdentifierCollection();
            }

            switch (storeType)
            {
                case CertificateStoreType.Windows:
                {
                    store = new WindowsCertificateStore();
                    break;
                }

                case CertificateStoreType.Directory:
                {
                    store = new DirectoryCertificateStore();
                    break;
                }
            }
            return store;
        }

        /// <summary>
        /// Returns an object that can be used to access the store.
        /// </summary>
        public ICertificateStore OpenStore()
        {
            ICertificateStore store = CreateStore(this.StoreType);
            Task t = Task.Run(() => store.Open(this.StorePath));
            t.Wait();
            return store;
        }
        #endregion
    }

    #region CertificateStoreType Class
    /// <summary>
    /// The type of certificate store.
    /// </summary>
    public static class CertificateStoreType
    {
        /// <summary>
        /// A windows certificate store.
        /// </summary>
        public const string Windows = "Windows";

        /// <summary>
        /// A directory certificate store.
        /// </summary>
        public const string Directory = "Directory";
    }
    #endregion
}
