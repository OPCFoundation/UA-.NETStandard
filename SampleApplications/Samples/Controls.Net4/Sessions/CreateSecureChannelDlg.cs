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

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    /// <summary>
    /// Prompts the user to create a new secure channel.
    /// </summary>
    public partial class CreateSecureChannelDlg : Form
    {
        public CreateSecureChannelDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        private ITransportChannel m_channel;
        private ApplicationConfiguration m_configuration;
        private EndpointDescriptionCollection m_endpoints;
        private ServiceMessageContext m_messageContext;
        
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public ITransportChannel ShowDialog(
            ApplicationConfiguration      configuration,
            EndpointDescriptionCollection endpoints)
        {
            if (endpoints == null)      throw new ArgumentNullException("endpoints");
            if (configuration == null)  throw new ArgumentNullException("configuration");

            m_endpoints      = endpoints;
            m_configuration  = configuration;
            m_messageContext = configuration.CreateMessageContext();

            EndpointCB.Items.Clear();

            foreach (EndpointDescription endpoint in endpoints)
            {
                EndpointCB.Items.Add(endpoint.EndpointUrl);
            }

            if (EndpointCB.Items.Count > 0)
            {
                EndpointCB.SelectedIndex = 0;
            }
            
            OperationTimeoutNC.Value    = configuration.TransportQuotas.OperationTimeout;
            MaxMessageSizeNC.Value      = configuration.TransportQuotas.MaxMessageSize;
            MaxArrayLengthNC.Value      = configuration.TransportQuotas.MaxArrayLength;
            MaxStringLengthNC.Value     = configuration.TransportQuotas.MaxStringLength;
            MaxByteStringLengthNC.Value = configuration.TransportQuotas.MaxByteStringLength;

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }
                       
            // return the channel.
            return m_channel;
        }

        private async void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                EndpointConfiguration configuration = EndpointConfiguration.Create(m_configuration);

                configuration.OperationTimeout = (int)OperationTimeoutNC.Value;
                configuration.UseBinaryEncoding = UseBinaryEncodingCK.Checked;
                configuration.MaxMessageSize = (int)MaxMessageSizeNC.Value;
                configuration.MaxArrayLength = (int)MaxArrayLengthNC.Value;
                configuration.MaxStringLength = (int)MaxStringLengthNC.Value;
                configuration.MaxByteStringLength = (int)MaxByteStringLengthNC.Value;

                ITransportChannel channel = SessionChannel.Create(
                    m_configuration,
                    m_endpoints[EndpointCB.SelectedIndex],
                    configuration,
                    await m_configuration.SecurityConfiguration.ApplicationCertificate.Find(true),
                    m_messageContext);

                // create the channel.                   

                // open the channel.
                Cursor = Cursors.WaitCursor;

                m_channel = channel;

                // close the dialog.
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void EndpointCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                int index = EndpointCB.SelectedIndex;

                if (index == -1)
                {
                    return;
                }

                EndpointDescription endpoint = m_endpoints[index];

                switch (endpoint.EncodingSupport)
                {
                    case BinaryEncodingSupport.Required:
                    {
                        UseBinaryEncodingCK.Checked = true;
                        UseBinaryEncodingCK.Enabled = false;
                        break;
                    }

                    case BinaryEncodingSupport.Optional:
                    {
                        UseBinaryEncodingCK.Checked = true;
                        UseBinaryEncodingCK.Enabled = true;
                        break;
                    }

                    default:
                    {
                        UseBinaryEncodingCK.Checked = false;
                        UseBinaryEncodingCK.Enabled = false;
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void DetailsBTN_Click(object sender, EventArgs e)
        {            
            try
            {
                int index = EndpointCB.SelectedIndex;

                if (index == -1)
                {
                    return;
                }

                new EndpointViewDlg().ShowDialog(m_endpoints[index]);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
}
