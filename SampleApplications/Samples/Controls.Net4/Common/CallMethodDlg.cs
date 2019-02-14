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
    public partial class CallMethodDlg : Form
    {
        #region Constructors
        public CallMethodDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_SessionClosing = new EventHandler(Session_Closing);
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private EventHandler m_SessionClosing;
        private NodeId m_objectId;
        private NodeId m_methodId;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public void Show(Session session, NodeId objectId, NodeId methodId)
        {
            if (session == null)  throw new ArgumentNullException("session");
            if (methodId == null) throw new ArgumentNullException("methodId");
            
            if (m_session != null)
            {
                m_session.SessionClosing -= m_SessionClosing;
            }

            m_session = session;
            m_session.SessionClosing += m_SessionClosing;
        
            m_objectId = objectId;            
            m_methodId = methodId;

            InputArgumentsCTRL.Update(session, methodId, true);     
            OutputArgumentsCTRL.Update(session, methodId, false);
            
            Node target = session.NodeCache.Find(objectId) as Node;
            Node method = session.NodeCache.Find(methodId) as Node;

            if (target != null && method != null)
            {
                Text = String.Format("Call {0}.{1}", target, method);
            }

            Show();
            BringToFront();
        }
        #endregion
                
        private void Session_Closing(object sender, EventArgs e)
        {
            if (Object.ReferenceEquals(sender, m_session))
            {
                m_session.SessionClosing -= m_SessionClosing;
                m_session = null;
                Close();
            }
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                VariantCollection inputArguments = InputArgumentsCTRL.GetValues();
                                
                CallMethodRequest request = new CallMethodRequest();

                request.ObjectId       = m_objectId;
                request.MethodId       = m_methodId;
                request.InputArguments = inputArguments;

                CallMethodRequestCollection requests = new CallMethodRequestCollection();
                requests.Add(request);

                CallMethodResultCollection results;
                DiagnosticInfoCollection diagnosticInfos;

                ResponseHeader responseHeader = m_session.Call(
                    null,
                    requests,
                    out results,
                    out diagnosticInfos);

                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    throw new ServiceResultException(new ServiceResult(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable));
                }

                OutputArgumentsCTRL.SetValues(results[0].OutputArguments);

                if (results[0].OutputArguments.Count == 0)
                {
                    MessageBox.Show(this, "Method executed successfully.");
                }
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
                if (Modal)
                {
                    DialogResult = DialogResult.Cancel;
                }
                else
                {
                    Close();
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
}
