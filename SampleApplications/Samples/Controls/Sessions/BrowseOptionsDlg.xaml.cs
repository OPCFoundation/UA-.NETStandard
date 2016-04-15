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
using Windows.UI.Popups;

namespace Opc.Ua.Sample.Controls
{
    public partial class BrowseOptionsDlg : Page
    {
        #region Constructors
        public BrowseOptionsDlg()
        {
            InitializeComponent();

            foreach (object value in Enum.GetValues(typeof(BrowseDirection)))
            {
                BrowseDirectionCB.Items.Add(value);
            }

            BrowseDirectionCB.SelectedIndex = 0;
        }
        #endregion
        
        #region Private Fields
        private Browser m_browser;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Prompts the user to specify the browse options.
        /// </summary>
        public bool ShowDialog(Browser browser)
        {
            if (browser == null) throw new ArgumentNullException("browser");

            m_browser = browser;
            ReferenceTypeCTRL.Initialize(m_browser.Session, null);

            ViewIdTB.Text         = null;
            ViewTimestampDP.Date = ViewTimestampDP.MinYear;
            ViewVersionNC.Value   = 0;

            if (browser.View != null)
            {
                ViewIdTB.Text         = String.Format("{0}", browser.View.ViewId);
                ViewVersionNC.Value   = browser.View.ViewVersion;
                ViewVersionCK.IsChecked = browser.View.ViewVersion != 0;

                if (browser.View.Timestamp > ViewTimestampDP.MinYear)
                {                
                    ViewTimestampDP.Date = browser.View.Timestamp ;
                    ViewTimestampCK.IsChecked = true;
                }
            }

            MaxReferencesReturnedNC.Value    = browser.MaxReferencesReturned;
            BrowseDirectionCB.SelectedItem   = browser.BrowseDirection;
            ReferenceTypeCTRL.SelectedTypeId = browser.ReferenceTypeId;
            IncludeSubtypesCK.IsChecked        = browser.IncludeSubtypes;
            NodeClassMaskCK.IsChecked          = browser.NodeClassMask != 0;             

            NodeClassList.Items.Clear();

            foreach (NodeClass value in Enum.GetValues(typeof(NodeClass)))
            {
                if (value == NodeClass.Unspecified)
                {
                    continue;
                }
                NodeClassList.Items.Add(value);
                int index = NodeClassList.Items.IndexOf(value);
            }

            return true;
        }
#endregion
        
#region Event Handlers
        private void ViewIdTB_TextChanged(object sender, EventArgs e)
        {
            ViewTimestampCK.IsEnabled = ViewVersionCK.IsEnabled = !String.IsNullOrEmpty(ViewIdTB.Text);
        }

        private void NodeClassMask_CheckedChanged(object sender, EventArgs e)
        {            
            NodeClassList.IsEnabled = (bool) NodeClassMaskCK.IsChecked;     
        }

        private void ViewVersionCK_CheckedChanged(object sender, EventArgs e)
        {
            ViewVersionNC.IsEnabled = (bool)ViewVersionCK.IsChecked;
        }

        private void ViewTimestampCK_CheckedChanged(object sender, EventArgs e)
        {
            ViewTimestampDP.IsEnabled = (bool)ViewTimestampCK.IsChecked;
        }

        private async void BrowseBTN_Click(object sender, EventArgs e)
        {
            try
            {
                Browser browser = new Browser(m_browser.Session);

                browser.BrowseDirection = BrowseDirection.Forward;
                browser.NodeClassMask = (int)NodeClass.View | (int)NodeClass.Object;
                browser.ReferenceTypeId = ReferenceTypeIds.Organizes;
                browser.IncludeSubtypes = true;

                ReferenceDescription reference = new SelectNodeDlg().ShowDialog(browser, Objects.ViewsFolder);

                if (reference != null)
                {
                    if (reference.NodeClass != NodeClass.View)
                    {
                        MessageDlg dialog = new MessageDlg("Please select a valid view node id.");
                        await dialog.ShowAsync();
                        return;
                    }

                    ViewIdTB.Text = Utils.Format("{0}", reference.NodeId);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private async void OkBTN_Click(object sender, EventArgs e)
        {
            NodeId viewId = null;

            try
            {
                viewId = NodeId.Parse(ViewIdTB.Text);
            }
            catch (Exception)
            {
                MessageDlg dialog = new MessageDlg("Please enter a valid node id for the view id.");
                await dialog.ShowAsync();
            }

            try
            {
                ViewDescription view = null;

                if (!NodeId.IsNull(viewId) || ((bool)ViewTimestampCK.IsChecked || (bool)ViewVersionCK.IsChecked))
                {
                    view = new ViewDescription();

                    view.ViewId = viewId;
                    view.Timestamp = DateTime.MinValue;
                    view.ViewVersion = 0;

                    if ((bool)ViewTimestampCK.IsChecked && (ViewTimestampDP.Date > ViewTimestampDP.MinYear))
                    {
                        view.Timestamp = Convert.ToDateTime(ViewTimestampDP.Date);
                    }

                    if ((bool)ViewVersionCK.IsChecked)
                    {
                        view.ViewVersion = (uint)ViewVersionNC.Value;
                    }
                }

                m_browser.View = view;
                m_browser.MaxReferencesReturned = (uint)MaxReferencesReturnedNC.Value;
                m_browser.BrowseDirection = (BrowseDirection)BrowseDirectionCB.SelectedItem;
                m_browser.NodeClassMask = (int)NodeClass.View | (int)NodeClass.Object;
                m_browser.ReferenceTypeId = ReferenceTypeCTRL.SelectedTypeId;
                m_browser.IncludeSubtypes = (bool)IncludeSubtypesCK.IsChecked;
                m_browser.NodeClassMask = 0;

                int nodeClassMask = 0;

                foreach (NodeClass nodeClass in NodeClassList.Items)
                {
                    nodeClassMask |= (int)nodeClass;
                }

                m_browser.NodeClassMask = nodeClassMask;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        #endregion
    }
}
