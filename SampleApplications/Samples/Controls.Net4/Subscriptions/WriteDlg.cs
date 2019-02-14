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
    public partial class WriteDlg : Form
    {
        #region Constructors
        public WriteDlg()
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
        public void Show(Session session, WriteValueCollection values)
        {
            if (session == null) throw new ArgumentNullException("session");
            
            m_session = session;

            BrowseCTRL.SetView(m_session, BrowseViewType.Objects, null);
            WriteValuesCTRL.Initialize(session, values);

            MoveBTN_Click(BackBTN, null);

            Show();
            BringToFront();
        }

        /// <summary>
        /// Writes the valus to the server.
        /// </summary>
        private void Write()
        {
            WriteValueCollection nodesToWrite = Utils.Clone(WriteValuesCTRL.GetValues()) as WriteValueCollection;

            if (nodesToWrite == null || nodesToWrite.Count == 0)
            {
                return;
            }

            foreach (WriteValue nodeToWrite in nodesToWrite)
            {
                NumericRange indexRange;
                ServiceResult result = NumericRange.Validate(nodeToWrite.IndexRange, out indexRange);

                if (ServiceResult.IsGood(result) && indexRange != NumericRange.Empty)
                {
                    // apply the index range.
                    object valueToWrite = nodeToWrite.Value.Value;

                    result = indexRange.ApplyRange(ref valueToWrite);

                    if (ServiceResult.IsGood(result))
                    {
                        nodeToWrite.Value.Value = valueToWrite;
                    }
                }
            }

            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            ResponseHeader responseHeader = m_session.Write(
                null,
                nodesToWrite,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToWrite);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToWrite);

            WriteResultsCTRL.ShowValue(results, true);
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
                        WriteValuesCTRL.AddValue(reference);
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
                    Write();

                    WriteValuesCTRL.Parent  = SplitterPN.Panel1;

                    BackBTN.Visible         = true;
                    NextBTN.Visible         = false;
                    WriteBTN.Visible         = true;
                    WriteValuesCTRL.Visible  = true;
                    WriteResultsCTRL.Visible = true;
                    BrowseCTRL.Visible      = false;
                }

                else if (sender == BackBTN)
                {
                    WriteValuesCTRL.Parent  = SplitterPN.Panel2;

                    BackBTN.Visible          = false;
                    NextBTN.Visible          = true;
                    WriteBTN.Visible          = false;
                    WriteResultsCTRL.Visible  = false;
                    BrowseCTRL.Visible       = true;
                    WriteValuesCTRL.Visible   = true;
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void WriteMI_Click(object sender, EventArgs e)
        {
            try
            {
                Write();
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
