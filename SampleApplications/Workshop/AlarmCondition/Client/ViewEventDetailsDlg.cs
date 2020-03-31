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

using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts.AlarmConditionClient
{
    /// <summary>
    /// Displays all fields associated with an event notification.
    /// </summary>
    public partial class ViewEventDetailsDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public ViewEventDetailsDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Shows all fields for the current condition.
        /// </summary>
        public bool ShowDialog(MonitoredItem monitoredItem, EventFieldList eventFields)
        {
            // build a sorted list of non-null fields.
            List<string> fieldNames = new List<string>();
            List<Variant> fieldValues = new List<Variant>();

            // use the filter from the monitored item to determine what is in each field.
            EventFilter filter = monitoredItem.Status.Filter as EventFilter;

            if (filter != null)
            {
                if (eventFields.EventFields[0].Value != null)
                {
                    fieldNames.Add("ConditionId");
                    fieldValues.Add(eventFields.EventFields[0]);
                }

                for (int ii = 1; ii < filter.SelectClauses.Count; ii++)
                {
                    object fieldValue = eventFields.EventFields[ii].Value;

                    if (fieldValue == null)
                    {
                        continue;
                    }

                    StringBuilder displayName = new StringBuilder();

                    for (int jj = 0; jj < filter.SelectClauses[ii].BrowsePath.Count; jj++)
                    {
                        if (displayName.Length > 0)
                        {
                            displayName.Append('/');
                        }

                        displayName.Append(filter.SelectClauses[ii].BrowsePath[jj].Name);
                    }

                    fieldNames.Add(displayName.ToString());
                    fieldValues.Add(eventFields.EventFields[ii]);
                }
            }

            // populate lists.
            for (int ii = 0; ii < fieldNames.Count; ii++)
            {
                ListViewItem item = new ListViewItem(fieldNames[ii]);

                item.SubItems.Add(Utils.Format("{0}", fieldValues[ii].Value));
                item.SubItems.Add(Utils.Format("{0}", fieldValues[ii].Value.GetType().Name));

                FieldsLV.Items.Add(item);
            }

            // adjust columns.
            for (int ii = 0; ii < FieldsLV.Columns.Count; ii++)
            {
                FieldsLV.Columns[ii].Width = -2;
            }

            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }
        #endregion
    }
}
