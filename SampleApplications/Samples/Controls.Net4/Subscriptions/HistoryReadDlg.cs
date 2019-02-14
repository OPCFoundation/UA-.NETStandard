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
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class HistoryReadDlg : Form
    {
        #region Constructors
        public HistoryReadDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public void Show(Session session, ReadValueIdCollection valueIds)
        {
            if (session == null) throw new ArgumentNullException("session");
            
            m_session = session;

            BrowseCTRL.SetView(m_session, BrowseViewType.Objects, null);
            ReadValuesCTRL.Initialize(session, valueIds);

            MoveBTN_Click(BackBTN, null);

            Show();
            BringToFront();
        }

        private void Read()
        {
            ReadValueIdCollection nodesToRead = ReadValuesCTRL.GetValueIds();

            if (nodesToRead == null || nodesToRead.Count == 0)
            {
                return;
            }

            DataValueCollection values = null;
            DiagnosticInfoCollection diagnosticInfos = null;
            
            ResponseHeader responseHeader = m_session.Read(
                null,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                out values,
                out diagnosticInfos);

            ClientBase.ValidateResponse(values, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);  
     
            ReadResultsCTRL.ShowValue(values, true);
        }
        #endregion
        
        #region Event Handlers
        private void BrowseCTRL_ItemsSelected(object sender, NodesSelectedEventArgs e)
        {
            try
            {
                foreach (ReferenceDescription reference in e.References)
                {
                    if (reference.ReferenceTypeId == ReferenceTypeIds.HasProperty || reference.IsForward)
                    {
                        ReadValuesCTRL.AddValueId(reference);
                    }
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void MoveBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (sender == NextBTN)
                {
                    Read();

                    BackBTN.Visible         = true;
                    NextBTN.Visible         = false;
                    ReadBTN.Visible         = true;
                    ReadValuesCTRL.Visible  = true;
                    ReadResultsCTRL.Visible = true;
                    BrowseCTRL.Visible      = false;
                }

                else if (sender == BackBTN)
                {
                    BackBTN.Visible          = false;
                    NextBTN.Visible          = true;
                    ReadBTN.Visible          = false;
                    ReadResultsCTRL.Visible  = false;
                    BrowseCTRL.Visible       = true;
                    ReadValuesCTRL.Visible   = true;
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ReadMI_Click(object sender, EventArgs e)
        {
            try
            {
                Read();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void CancelBTN_Click(object sender, EventArgs e)
        {
            try
            {
                Close();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
