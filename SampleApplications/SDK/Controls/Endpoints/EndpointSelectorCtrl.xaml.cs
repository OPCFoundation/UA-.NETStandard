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
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A control which displays a list of endpoints
    /// </summary>
    public sealed partial class EndpointSelectorCtrl : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EndpointSelectorCtrl"/> class.
        /// </summary>
        public EndpointSelectorCtrl()
        {
            InitializeComponent();
        }

        #region Private Fields
        private int m_selectedIndex;
        private ApplicationConfiguration m_configuration;
        private ConfiguredEndpointCollection m_endpoints;
        private event ConnectEndpointEventHandler m_ConnectEndpoint;
        private event EventHandler m_EndpointsChanged;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Raised when the user presses the connect button.
        /// </summary>
        public event ConnectEndpointEventHandler ConnectEndpoint
        {
            add    { m_ConnectEndpoint += value; }
            remove { m_ConnectEndpoint -= value; }
        }

        /// <summary>
        /// Raised when the endpoints displayed in the control are changed.
        /// </summary>
        public event EventHandler EndpointsChanged
        {
            add    { m_EndpointsChanged += value; }
            remove { m_EndpointsChanged -= value; }
        }

        /// <summary>
        /// The endpoint currently displayed in the control.
        /// </summary>
        public ConfiguredEndpoint SelectedEndpoint
        {
            get
            {
                ConfiguredEndpoint item = EndpointCB.SelectedItem as ConfiguredEndpoint;

                if (item != null)
                {                    
                    return item;
                }

                if (String.IsNullOrEmpty(EndpointCB.SelectedItem.ToString()))
                {                    
                    return null;
                }                
                        
                return m_endpoints.Create(EndpointCB.SelectedItem.ToString());
            }

            set
            {
                if (value == null)
                {
                    EndpointCB.SelectedItem = null;
                    EndpointCB.SelectedIndex = -1;
                    return;
                }

                for (int ii = 1; ii < EndpointCB.Items.Count; ii++)
                {
                    ConfiguredEndpoint item = EndpointCB.Items[ii] as ConfiguredEndpoint;

                    if (Object.ReferenceEquals(item, value))
                    {
                        EndpointCB.SelectedItem = item;
                        return;
                    }
                }

                // must be a new endpoint.
                m_endpoints.Add(value);

                // raise notification.
                if (m_EndpointsChanged != null)
                {
                    m_EndpointsChanged(this, null);
                }

                EndpointCB.Items.Add(value);
                EndpointCB.SelectedIndex = EndpointCB.Items.IndexOf(value);
            }
        }

        /// <summary>
        /// Initializes the control with a list of endpoints.
        /// </summary>
        public void Initialize(ConfiguredEndpointCollection endpoints, ApplicationConfiguration configuration)
        {
            if (endpoints == null) throw new ArgumentNullException("endpoints");

            m_endpoints = endpoints;
            m_configuration = configuration;

            EndpointCB.Items.Clear();
            EndpointCB.SelectedIndex = -1;
            EndpointCB.Items.Add("<New...>");

            if (endpoints != null)
            {
                foreach (ConfiguredEndpoint endpoint in m_endpoints.Endpoints)
                {
                    EndpointCB.Items.Add(endpoint);
                }
            }

            if (EndpointCB.Items.Count > 1)
            {
                EndpointCB.SelectedIndex = m_selectedIndex = 1;
            }
        }
        #endregion

        #region Event Handlers
        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // get selected endpoint.
                ConfiguredEndpoint endpoint = SelectedEndpoint;

                if (endpoint == null)
                {
                    return;
                }

                // raise event.
                if (m_ConnectEndpoint != null)
                {
                    ConnectEndpointEventArgs args = new ConnectEndpointEventArgs(endpoint, true);

                    await m_ConnectEndpoint(this, args);

                    // save endpoint in drop down.
                    if (args.UpdateControl)
                    {
                        // must update the control because the display text may have changed.
                        Initialize(m_endpoints, m_configuration);
                        SelectedEndpoint = endpoint;
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void EndpointCB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (EndpointCB.SelectedIndex != 0)
                {
                    m_selectedIndex = EndpointCB.SelectedIndex;
                    return;
                }

                // modify configuration.
                ConfiguredEndpoint endpoint = new ConfiguredServerListDlg().ShowDialog(m_configuration, true);

                if (endpoint == null)
                {
                    EndpointCB.SelectedIndex = m_selectedIndex;
                    return;
                }

                m_endpoints.Add(endpoint);

                // raise notification.
                if (m_EndpointsChanged != null)
                {
                    m_EndpointsChanged(this, null);
                }

                // update dropdown.
                Initialize(m_endpoints, m_configuration);

                // update selection.
                for (int ii = 0; ii < m_endpoints.Endpoints.Count; ii++)
                {
                    if (Object.ReferenceEquals(endpoint, m_endpoints.Endpoints[ii]))
                    {
                        EndpointCB.SelectedIndex = ii + 1;
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void EndpointCB_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            ConfiguredEndpoint endpoint = EndpointCB.SelectedValue as ConfiguredEndpoint;
            if (endpoint != null)
            {
                InputTB.Text = endpoint.ToString();
            }
            else
            {
                InputTB.Text = "opc.tcp://";
            }
            InputTB.Visibility = Visibility.Visible;
            EndpointCB.Visibility = Visibility.Collapsed;
        }

        private void InputTB_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                InputTB.Visibility = Visibility.Collapsed;
                EndpointCB.Visibility = Visibility.Visible;
                ConfiguredEndpoint endpoint = EndpointCB.SelectedValue as ConfiguredEndpoint;
                if (endpoint == null ||
                    InputTB.Text != endpoint.ToString())
                {
                    endpoint = m_endpoints.Create(InputTB.Text);
                    SelectedEndpoint = endpoint;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void InputTB_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            try
            {
                if (e.Key == VirtualKey.Enter)
                {
                    InputTB.Visibility = Visibility.Collapsed;
                    EndpointCB.Visibility = Visibility.Visible;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private async void EndpointCB_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            try
            {
                if (e.Key == VirtualKey.Delete)
                {
                    // ignore "New"
                    if (m_selectedIndex == 0) return;

                    ConfiguredEndpoint endpoint = SelectedEndpoint;
                    MessageDlg dialog = new MessageDlg(
                        "Delete Endpoint?\r\n" + endpoint.EndpointUrl,
                        MessageDlgButton.Yes, MessageDlgButton.No);
                    if (await dialog.ShowAsync() == MessageDlgButton.Yes)
                    {
                        int oldIndex = m_selectedIndex;
                        m_endpoints.Remove(endpoint);
                        EndpointCB.Items.Remove(endpoint);
                        if (oldIndex > 0)
                        {
                            EndpointCB.SelectedIndex = m_selectedIndex = oldIndex-1;
                        }

                        // raise notification.
                        if (m_EndpointsChanged != null)
                        {
                            m_EndpointsChanged(this, null);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        #endregion
    }

    #region ConnectEndpointEventArgs Class
    /// <summary>
    /// Contains arguments for a ConnectEndpoint event.
    /// </summary>
    public class ConnectEndpointEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes the object.
        /// </summary>
        public ConnectEndpointEventArgs(ConfiguredEndpoint endpoint, bool updateControl)
        {
            m_endpoint  = endpoint;
            m_updateControl = updateControl;
        }

        /// <summary>
        /// The endpoint selected in the control.
        /// </summary>
        public ConfiguredEndpoint Endpoint
        {
            get { return m_endpoint; }
        }

        /// <summary>
        /// Whether the endpoint should be saved in the control after the event completes.
        /// </summary>
        public bool UpdateControl
        {
            get { return m_updateControl;  }
            set { m_updateControl = value; }
        }
        
#region Private Fields
        private ConfiguredEndpoint m_endpoint;
        private bool m_updateControl;
#endregion
    }

    /// <summary>
    /// The delegate used to receive connect endpoint notifications.
    /// </summary>
    public delegate Task ConnectEndpointEventHandler(object sender,  ConnectEndpointEventArgs e);
#endregion
}
