/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays the properties for an X509 certificate.
    /// </summary>
    public partial class CertificatePropertiesListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        #region Constructors
        /// <summary>
        /// Initalize the control.
        /// </summary>
        public CertificatePropertiesListCtrl()
        {
            InitializeComponent();

            SetColumns(m_ColumnNames);
        }
        #endregion
        
        #region Private Fields
        // The columns to display in the control.		
		private readonly object[][] m_ColumnNames = new object[][]
		{ 
			new object[] { "Field", HorizontalAlignment.Left, null },  
			new object[] { "Value", HorizontalAlignment.Left, null },
		};
        #endregion
        
        #region FieldInfo Class
        private class FieldInfo
        {
            public string Name;
            public string Value;

            public FieldInfo(string name, object value)
            {
                Name = name;
                Value = Utils.Format("{0}", value);
            }
        }
        #endregion

        #region Public Interface 
        /// <summary>
        /// Removes all items in the list.
        /// </summary>
        internal void Clear()
        {
            ItemsLV.Items.Clear();
            Instructions = String.Empty;
            AdjustColumns();            
        }

        /// <summary>
        /// Displays the properties of a certificate.
        /// </summary>
        internal void Initialize(X509Certificate2 certificate)
        {
            ItemsLV.Items.Clear();

            if (certificate == null)
            {
                Instructions = "No certificate properties to display";
                AdjustColumns();
                return;
            }

            AddItem(new FieldInfo("Version", certificate.Version));            
            AddItem(new FieldInfo("Subject", certificate.Subject));      
            AddItem(new FieldInfo("FriendlyName", certificate.FriendlyName));
            AddItem(new FieldInfo("Thumbprint", certificate.Thumbprint));
            AddItem(new FieldInfo("Issuer", certificate.Issuer));
            AddItem(new FieldInfo("SerialNumber", certificate.SerialNumber));
            AddItem(new FieldInfo("NotBefore", Utils.Format("{0:yyyy-MM-dd}", certificate.NotBefore)));
            AddItem(new FieldInfo("NotAfter", Utils.Format("{0:yyyy-MM-dd}", certificate.NotAfter)));
            AddItem(new FieldInfo("KeySize", certificate.PublicKey.Key.KeySize));
            AddItem(new FieldInfo("KeyExchangeAlgorithm", certificate.PublicKey.Key.KeyExchangeAlgorithm));
            AddItem(new FieldInfo("SignatureAlgorithm", certificate.SignatureAlgorithm.FriendlyName));

            foreach (X509Extension extension in certificate.Extensions)
            {
                X509BasicConstraintsExtension basicContraints = extension as X509BasicConstraintsExtension; 
                
                if (basicContraints != null)
                {
                    StringBuilder buffer = new StringBuilder();

                    if (basicContraints.CertificateAuthority)
                    {
                        buffer.Append("CA");
                    }
                    else
                    {
                        buffer.Append("End Entity");
                    }

                    if (basicContraints.HasPathLengthConstraint)
                    {
                        buffer.AppendFormat(", PathLength={0}", basicContraints.PathLengthConstraint);
                    }

                    AddItem(new FieldInfo("BasicConstraints", buffer.ToString()));
                    continue;
                }

                X509KeyUsageExtension keyUsage = extension as X509KeyUsageExtension;

                if (keyUsage != null)
                {
                    StringBuilder buffer = new StringBuilder();

                    foreach (X509KeyUsageFlags usageFlag in Enum.GetValues(typeof(X509KeyUsageFlags)))
                    {
                        if ((keyUsage.KeyUsages & usageFlag) != 0)
                        {
                            if (buffer.Length > 0)
                            {
                                buffer.Append(", ");
                            }

                            buffer.AppendFormat("{0}", usageFlag);
                        }
                    }

                    AddItem(new FieldInfo("KeyUsage", buffer.ToString()));
                    continue;
                }

                X509EnhancedKeyUsageExtension enhancedKeyUsage = extension as X509EnhancedKeyUsageExtension;
                
                if (enhancedKeyUsage != null)
                {
                    StringBuilder buffer = new StringBuilder();

                    foreach (Oid usageOid in enhancedKeyUsage.EnhancedKeyUsages)
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Append(", ");
                        }

                        if (!String.IsNullOrEmpty(usageOid.FriendlyName))
                        {
                            buffer.AppendFormat("{0}", usageOid.FriendlyName);
                        }
                        else
                        {
                            buffer.AppendFormat("{0}", usageOid.Value);
                        }
                    }

                    AddItem(new FieldInfo("EnhancedKeyUsage", buffer.ToString()));
                    continue;
                }

                X509SubjectKeyIdentifierExtension subjectKeyId = extension as X509SubjectKeyIdentifierExtension;
                
                if (subjectKeyId != null)
                {
                    AddItem(new FieldInfo("SubjectKeyIdentifier", subjectKeyId.SubjectKeyIdentifier));
                    continue;
                }

                if (extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltNameOid || extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltName2Oid)
                {
                    X509SubjectAltNameExtension alternateName = new X509SubjectAltNameExtension(extension, extension.Critical);
                    AddItem(new FieldInfo("SubjectAlternateName", alternateName.Format(false)));
                    continue;
                }

                if (extension.Oid.Value == X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifier2Oid)
                {
                    X509AuthorityKeyIdentifierExtension keyId = new X509AuthorityKeyIdentifierExtension(extension, extension.Critical);
                    AddItem(new FieldInfo("AuthorityKeyIdentifier", keyId.Format(false)));
                    continue;
                }

                string name = extension.Oid.FriendlyName;

                if (String.IsNullOrEmpty(name))
                {
                    name = extension.Oid.Value;
                }

                string value = Utils.ToHexString(extension.RawData);
                
                AddItem(new FieldInfo(name, value));
            }              

            AdjustColumns();
        }

        /// <summary>
        /// Displays the properties of a certificate.
        /// </summary>
        internal void Initialize(Opc.Ua.Security.SecuredApplication application)
        {
            ItemsLV.Items.Clear();

            if (application == null)
            {
                Instructions = "No application properties to display";
                AdjustColumns();
                return;
            }

            AddItem(new FieldInfo("ApplicationName", application.ApplicationName));
            AddItem(new FieldInfo("ApplicationUri", application.ApplicationUri));   
            AddItem(new FieldInfo("ProductName", application.ProductName));   
            AddItem(new FieldInfo("ApplicationType", application.ApplicationType));   
            AddItem(new FieldInfo("ConfigurationFile", application.ConfigurationFile));  
            AddItem(new FieldInfo("ExecutableFile", application.ExecutableFile));  

            AdjustColumns();
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Updates an item in the control.
        /// </summary>
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
            FieldInfo info = item as FieldInfo;

            if (info == null)
            {
                base.UpdateItem(listItem, item);
                return;
            }

			listItem.SubItems[0].Text = String.Format("{0}", info.Name);
			listItem.SubItems[1].Text = String.Format("{0}", info.Value);
            
            listItem.Tag = item;
        }
        #endregion
    }
}
