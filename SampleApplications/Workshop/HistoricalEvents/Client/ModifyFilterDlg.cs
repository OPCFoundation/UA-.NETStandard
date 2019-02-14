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
using System.Windows.Forms;
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Quickstarts.HistoricalEvents.Client
{
    /// <summary>
    /// Prompts the user to select an area to use as an event filter.
    /// </summary>
    public partial class ModifyFilterDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public ModifyFilterDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private FilterDeclaration m_filter;

        /// <summary>
        /// The supported filter operators.
        /// </summary>
        private static FilterOperator[] s_Operators = new FilterOperator[]
        {
            FilterOperator.Equals,
            FilterOperator.GreaterThan,
            FilterOperator.GreaterThanOrEqual,
            FilterOperator.LessThan,
            FilterOperator.LessThanOrEqual,
            FilterOperator.Like,
            FilterOperator.IsNull,
        };

        /// <summary>
        /// Stores the state of FilterDeclarationField element.
        /// </summary>
        private class FilterItem
        {
            public FilterItem(FilterDeclarationField declaration)
            {
                DisplayInList = declaration.DisplayInList;
                FilterEnabled = declaration.FilterEnabled;
                FilterOperator = declaration.FilterOperator;
                FilterValue = declaration.FilterValue;
                Declaration = declaration;
            }
              
            public bool DisplayInList;
            public bool FilterEnabled;
            public FilterOperator FilterOperator;
            public Variant FilterValue;
            public FilterDeclarationField Declaration;
        }
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the available areas in a tree view.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns></returns>
        public bool ShowDialog(FilterDeclaration filter)
        {
            m_filter = filter;

            Populate();

            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            // update filter.
            List<FilterDeclarationField> fields = new List<FilterDeclarationField>();

            for (int ii = 0; ii < EventFieldsLV.Items.Count; ii++)
            {
                ListViewItem item = EventFieldsLV.Items[ii];
                FilterItem field = item.Tag as FilterItem;

                if (field != null)
                {
                    field.Declaration.DisplayInList = field.DisplayInList;
                    field.Declaration.FilterEnabled = field.FilterEnabled;
                    field.Declaration.FilterOperator = field.FilterOperator;
                    field.Declaration.FilterValue = field.FilterValue;
                    fields.Add(field.Declaration);
                }
            }

            filter.Fields = fields;

            // return the result.
            return true;
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Displays an X for a boolean value.
        /// </summary>
        private string ToCheck(bool value)
        {
            if (value)
            {
                return "X";
            }

            return string.Empty;
        }

        /// <summary>
        /// Populates the list view with the current set of fields.
        /// </summary>
        private void Populate()
        {
            EventFieldsLV.Items.Clear();

            for (int ii = 0; ii < m_filter.Fields.Count; ii++)
            {
                FilterDeclarationField field = m_filter.Fields[ii];
                
                ListViewItem item = new ListViewItem(field.InstanceDeclaration.DisplayPath);

                item.SubItems.Add(ToCheck(field.DisplayInList));
                item.SubItems.Add(ToCheck(field.FilterEnabled));
                item.SubItems.Add(field.FilterOperator.ToString());
                item.SubItems.Add(field.FilterValue.ToString());
                item.SubItems.Add(field.InstanceDeclaration.DataTypeDisplayText);

                item.Tag = new FilterItem(field);
                
                EventFieldsLV.Items.Add(item);
            }

            // resize columns to fit text.
            for (int ii = 0; ii < EventFieldsLV.Columns.Count; ii++)
            {
                EventFieldsLV.Columns[ii].Width = -2;
            } 
        }
        #endregion

        #region Event Handlers
        private void PopupMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            CheckState displayInList = CheckState.Indeterminate;
            CheckState filterEnabled = CheckState.Indeterminate;

            if (EventFieldsLV.SelectedItems.Count > 0)
            {
                FilterItem field = EventFieldsLV.SelectedItems[0].Tag as FilterItem;
                
                if (field != null)
                {
                    displayInList = (field.DisplayInList) ? CheckState.Checked : CheckState.Unchecked;
                    filterEnabled = (field.FilterEnabled) ? CheckState.Checked : CheckState.Unchecked;
                }
            }

            for (int ii = 1; ii < EventFieldsLV.SelectedItems.Count; ii++)
            {
                FilterItem field = EventFieldsLV.SelectedItems[ii].Tag as FilterItem;

                if (field != null)
                {
                    if (field.DisplayInList && (displayInList != CheckState.Checked) || !field.DisplayInList && (displayInList != CheckState.Unchecked))
                    {
                        displayInList = CheckState.Indeterminate;
                    }

                    if (field.FilterEnabled && (filterEnabled != CheckState.Checked) || !field.FilterEnabled && (filterEnabled != CheckState.Unchecked))
                    {
                        filterEnabled = CheckState.Indeterminate;
                    }
                }
            }

            DisplayInListViewMI.CheckState = displayInList;
            FilterEnabledMI.CheckState = filterEnabled;
        }

        private void DisplayInListViewMI_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                if (EventFieldsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                foreach (ListViewItem item in EventFieldsLV.SelectedItems)
                {
                    FilterItem field = item.Tag as FilterItem;

                    if (field != null)
                    {
                        field.DisplayInList = DisplayInListViewMI.CheckState == CheckState.Checked;
                        item.SubItems[1].Text = ToCheck(field.DisplayInList);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void FilterEnabledMI_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                if (EventFieldsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                foreach (ListViewItem item in EventFieldsLV.SelectedItems)
                {
                    FilterItem field = item.Tag as FilterItem;

                    if (field != null)
                    {
                        field.FilterEnabled = FilterEnabledMI.CheckState == CheckState.Checked;
                        item.SubItems[2].Text = ToCheck(field.FilterEnabled);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void DeleteFieldMI_Click(object sender, EventArgs e)
        {
            try
            {
                List<ListViewItem> items = new List<ListViewItem>();

                foreach (ListViewItem item in EventFieldsLV.SelectedItems)
                {
                    items.Add(item);
                }

                foreach (ListViewItem item in items)
                {
                    item.Remove();
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void FilterOperandMI_DropDownOpening(object sender, EventArgs e)
        {
            try
            {
                // check if the drop down has been initialized.
                if (Object.ReferenceEquals(sender, FilterOperandMI))
                {
                    if (FilterOperandMI.DropDownItems.Count == 0)
                    {
                        foreach (FilterOperator current in s_Operators)
                        {
                            ToolStripMenuItem item = new ToolStripMenuItem(current.ToString());
                            item.Tag = current;
                            item.Click += new EventHandler(FilterOperandMI_DropDownOpening);
                            FilterOperandMI.DropDownItems.Add(item);
                        }
                    }

                    return;
                }

                // update the filter operator for all selected items.
                FilterOperator op = (FilterOperator)((ToolStripMenuItem)sender).Tag;

                foreach (ListViewItem item in EventFieldsLV.SelectedItems)
                {
                    FilterItem field = item.Tag as FilterItem;

                    if (field != null)
                    {
                        field.FilterOperator = op;
                        item.SubItems[3].Text = op.ToString();
                    }
                }

                EventFieldsLV.Columns[3].Width = -2;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void SetFilterValueMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (EventFieldsLV.SelectedItems.Count != 1)
                {
                    return;
                }

                FilterItem field = EventFieldsLV.SelectedItems[0].Tag as FilterItem;

                if (field == null)
                {
                    return;
                }

                if (field.Declaration.InstanceDeclaration.ValueRank == ValueRanks.Scalar)
                {
                    Variant? value = new SetValueDlg().ShowDialog(field.FilterValue, field.Declaration.InstanceDeclaration.BuiltInType);

                    if (value != null)
                    {
                        field.FilterValue = value.Value;
                        EventFieldsLV.SelectedItems[0].SubItems[4].Text = value.Value.ToString();
                        EventFieldsLV.Columns[4].Width = -2;
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
