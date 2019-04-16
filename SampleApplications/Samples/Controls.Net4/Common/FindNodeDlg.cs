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
    public partial class FindNodeDlg : Form
    {
        public FindNodeDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        private Session m_session;

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public NodeIdCollection ShowDialog(Session session, NodeId startNodeId)
        {
            m_session = session;

            StartNode.Text    = String.Format("{0}", startNodeId);
            RelativePath.Text = null;

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return null;
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {                
                BrowsePathCollection browsePaths = new BrowsePathCollection();
                
                BrowsePath browsePath = new BrowsePath();

                browsePath.StartingNode = NodeId.Parse(StartNode.Text);
                browsePath.RelativePath = Opc.Ua.RelativePath.Parse(RelativePath.Text, m_session.TypeTree);
                
                browsePaths.Add(browsePath);

                BrowsePathResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                m_session.TranslateBrowsePathsToNodeIds(
                    null,
                    browsePaths,
                    out results,
                    out diagnosticInfos);

                if (results != null && results.Count == 1)
                {
                    // NodesCTRL.SetNodeList(results[0].MatchingNodeIds);
                }    
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
}
