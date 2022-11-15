/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Windows.Forms;
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to select an area to use as an event filter.
    /// </summary>
    public partial class SetFilterOperatorDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SetFilterOperatorDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            FilterOperatorCB.Items.Add(FilterOperator.IsNull);
            FilterOperatorCB.Items.Add(FilterOperator.Equals);
            FilterOperatorCB.Items.Add(FilterOperator.GreaterThan);
            FilterOperatorCB.Items.Add(FilterOperator.LessThan);
            FilterOperatorCB.Items.Add(FilterOperator.GreaterThanOrEqual);
            FilterOperatorCB.Items.Add(FilterOperator.LessThanOrEqual);
            FilterOperatorCB.Items.Add(FilterOperator.Like);
            FilterOperatorCB.Items.Add(FilterOperator.Not);
            FilterOperatorCB.Items.Add(FilterOperator.OfType);
            FilterOperatorCB.Items.Add(FilterOperator.BitwiseAnd);
            FilterOperatorCB.Items.Add(FilterOperator.BitwiseOr);
        }
        #endregion
        
        #region Private Fields
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the available areas in a tree view.
        /// </summary>
        public bool ShowDialog(ref FilterOperator filterOperator)
        {
            FilterOperatorCB.SelectedItem = filterOperator;
 
            // display the dialog.
            if (base.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            filterOperator = (FilterOperator)FilterOperatorCB.SelectedItem;
            return true;
        }
        #endregion
        
        #region Private Methods
        #endregion

        #region Event Handlers
        #endregion
    }
}
