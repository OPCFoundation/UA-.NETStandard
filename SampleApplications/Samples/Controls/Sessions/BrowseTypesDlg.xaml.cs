/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Windows.UI.Xaml.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class BrowseTypesDlg : Page
    {
        #region Constructors
        public BrowseTypesDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        //private ILocalNode m_selectedType;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public void Show(
            Session session,
            NodeId  typeId)
        {
            if (session == null) throw new ArgumentNullException("session");

            m_session = session;                    
            
            //TypeNavigatorCTRL.Initialize(m_session, typeId);
            //TypeHierarchyCTRL.Initialize(m_session, typeId);
        }
        #endregion
        
        #region Private Methods
        #endregion
        
        #region Event Handler
        private void TypeNavigatorCTRL_TypeSelected(object sender, EventArgs e)
        {
            try
            {
                //m_selectedType = e.Node;

                //if (m_selectedType != null)
                //{
                //    TypeHierarchyCTRL.Initialize(m_session, m_selectedType.NodeId);
                //}
                //else
                //{
                //    TypeHierarchyCTRL.Initialize(m_session, null);
                //}
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        #endregion
    }
}
