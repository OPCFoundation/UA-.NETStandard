/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;

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
                return CertificateStoreType.X509Store;
            }

            if (storePath.StartsWith("CurrentUser\\", StringComparison.OrdinalIgnoreCase))
            {
                return CertificateStoreType.X509Store;
            }

            if (storePath.StartsWith("User\\", StringComparison.OrdinalIgnoreCase))
            {
                return CertificateStoreType.X509Store;
            }

            if (storePath.StartsWith("Service\\", StringComparison.OrdinalIgnoreCase))
            {
                return CertificateStoreType.X509Store;
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
                return new CertificateIdentifierCollection();
            }

            switch (storeType)
            {
                case CertificateStoreType.X509Store:
                    {
                        store = new X509CertificateStore();
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
            store.Open(this.StorePath);
            return store;
        }

        /// <summary>
        /// Opens the store.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The store.</returns>
        public static ICertificateStore OpenStore(string path)
        {
            ICertificateStore store = CertificateStoreIdentifier.CreateStore(CertificateStoreIdentifier.DetermineStoreType(path));
            store.Open(path);
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
        public const string X509Store = "X509Store";

        /// <summary>
        /// A directory certificate store.
        /// </summary>
        public const string Directory = "Directory";
    }
    #endregion
}
