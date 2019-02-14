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
    public partial class EventFilterDlg : Form
    {
        #region Constructors
        public EventFilterDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private EventFilter m_filter;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public EventFilter ShowDialog(Session session, EventFilter filter, bool editWhereClause)
        {
            if (session == null) throw new ArgumentNullException("session");
            if (filter == null)  throw new ArgumentNullException("filter");
            
            m_session = session;
            m_filter  = filter;

            BrowseCTRL.SetView(m_session, BrowseViewType.EventTypes, null);
            SelectClauseCTRL.Initialize(session, filter.SelectClauses);
            ContentFilterCTRL.Initialize(session, filter.WhereClause);
            FilterOperandsCTRL.Initialize(session, null, -1);

            MoveBTN_Click((editWhereClause)?NextBTN:BackBTN, null);

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return m_filter;
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
                        SelectClauseCTRL.AddSelectClause(reference);
                    }
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ContentFilterCTRL_ItemsSelected(object sender, ListItemActionEventArgs e)
        {
            try
            {
                if (e.Items.Count > 0)
                {
                    foreach (object item in e.Items)
                    {
                        List<ContentFilterElement> elements = ContentFilterCTRL.GetElements();

                        for (int ii = 0; ii < elements.Count; ii++)
                        {
                            if (Object.ReferenceEquals(elements[ii], item))
                            {
                                FilterOperandsCTRL.Initialize(m_session, elements, ii);
                            }
                        }

                        break;
                    }
                }
                else
                {
                    FilterOperandsCTRL.Initialize(m_session, null, -1);       
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
                    BackBTN.Visible            = true;
                    NextBTN.Visible            = false;
                    OkBTN.Visible              = true;
                    ContentFilterCTRL.Visible  = true;
                    FilterOperandsCTRL.Visible = true;
                    BrowseCTRL.Visible         = false;
                    SelectClauseCTRL.Visible   = false;
                }

                else if (sender == BackBTN)
                {           
                    BackBTN.Visible            = false;
                    NextBTN.Visible            = true;
                    OkBTN.Visible              = false;
                    ContentFilterCTRL.Visible  = false;
                    FilterOperandsCTRL.Visible = false;
                    BrowseCTRL.Visible         = true;
                    SelectClauseCTRL.Visible   = true;
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                EventFilter filter = new EventFilter();

                filter.SelectClauses.AddRange(SelectClauseCTRL.GetSelectClauses());
                filter.WhereClause = ContentFilterCTRL.GetFilter();
                
                EventFilter.Result result = filter.Validate(new FilterContext(m_session.NamespaceUris, m_session.TypeTree));

                if (ServiceResult.IsBad(result.Status))
                {
                    throw ServiceResultException.Create(StatusCodes.BadEventFilterInvalid, result.GetLongString());
                }

                m_filter = filter;

                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }

        }
        #endregion
    }
}
