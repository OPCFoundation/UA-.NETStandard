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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.ServiceModel;
using System.Reflection;
using System.IdentityModel.Claims;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel.Security;
using System.ServiceModel.Channels;

using Opc.Ua.Bindings;

namespace Opc.Ua.Sample.Controls
{
    /// <summary>
    /// Prompts the user to create a new secure channel.
    /// </summary>
    public partial class EndpointViewDlg : Form
    {
        public EndpointViewDlg()
        {
            InitializeComponent();
        }
        
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public bool ShowDialog(EndpointDescription endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException("endpoint");
        
            EndpointTB.Text   = endpoint.EndpointUrl;
            ServerNameTB.Text = endpoint.Server.ApplicationName.Text;
            ServerUriTB.Text  = endpoint.Server.ApplicationUri;

            try
            {
                X509Certificate2 certificate = CertificateFactory.Create(endpoint.ServerCertificate, true);
                ServerCertificateTB.Text = String.Format("{0}", certificate.Subject);
            }
            catch
            {
                ServerCertificateTB.Text = "<bad certificate>";
            }
                
           
            SecurityModeTB.Text      = String.Format("{0}", endpoint.SecurityMode);;
            SecurityPolicyUriTB.Text = String.Format("{0}", endpoint.SecurityPolicyUri);
            
            UserIdentityTypeTB.Text = "";

            foreach (UserTokenPolicy policy in endpoint.UserIdentityTokens)
            {
                UserIdentityTypeTB.Text += String.Format("{0} ", policy.TokenType);
            }

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }
                       
            return true;
        }
    }
}
