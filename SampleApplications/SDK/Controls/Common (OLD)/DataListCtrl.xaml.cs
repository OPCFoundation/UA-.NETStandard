/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections;
using System.Text;
using System.Reflection;
using System.Xml;
using Windows.UI.Xaml.Controls;
using Windows.Devices.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Popups;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a hierarchical view of a complex value.
    /// </summary>
    public sealed partial class DataListCtrl : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DataListCtrl"/> class.
        /// </summary>
        public DataListCtrl()
        {
            InitializeComponent();                        
        }

        #region Private Fields
        private bool m_latestValue = true;
        private bool m_expanding;
        private int m_depth;
        private FontFamily m_defaultFont;
        private MonitoredItem m_monitoredItem;

        private const string UnknownType  = "(unknown)";
        private const string NullValue    = "(null)";
        private const string ExpandIcon   = "ExpandPlus";
        private const string CollapseIcon = "ExpandMinus";
		#endregion

        #region Public Interface
        /// <summary>
        /// Whether to update the control when the value changes.
        /// </summary>
        public bool AutoUpdate
        {
            get { return (bool) UpdatesMI.IsChecked;  }
            set { UpdatesMI.IsChecked = value; }
        }

        /// <summary>
        /// Whether to only display the latest value for a monitored item.
        /// </summary>
        public bool LatestValue
        {
            get { return m_latestValue;  }
            set { m_latestValue = value; }
        }
        
        /// <summary>
        /// The monitored item associated with the value.
        /// </summary>
        public MonitoredItem MonitoredItem
        {
            get { return m_monitoredItem;  }
            set { m_monitoredItem = value; }
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
        }
        
        /// <summary>
        /// Displays a value in the control.
        /// </summary>
        public void ShowValue(object value)
        {
            ShowValue(value, false);
        }

        /// <summary>
        /// Displays a value in the control.
        /// </summary>
        public void ShowValue(object value, bool overwrite)
        {
            if (!overwrite)
            {
                Clear();
            }

            m_defaultFont = new FontFamily("Courier New");
            
            m_expanding = false;
            m_depth = 0;
                        
            // show the value.
            int index = 0;
            ShowValue(ref index, ref overwrite, value.ToString());
        }
		#endregion
        
        #region Overridden Methods
        /// <summary>
        /// Enables the menu items.
        /// </summary>
        public void EnableMenuItems(ListViewItem clickedItem)
        {
            RefreshMI.IsEnabled = true;
            ClearMI.IsEnabled   = true;

            if (ItemsLV.SelectedItems.Count == 1)
            {
                ValueState state = ((ListViewItem) ItemsLV.SelectedItems[0]).Tag as ValueState;
                EditMI.IsEnabled = IsEditableType(state.Component);
            }
        }
		#endregion
        
        #region ValueState Class
        /// <summary>
        /// Stores the state associated with an item.
        /// </summary>
        private class ValueState
        {
            public bool Expanded = false;
            public bool Expandable = false;
            public object Value = null;
            public object Component = null;
            public object ComponentId = null;
        }
		#endregion

        #region Private Members
        /// <summary>
        /// Returns true is the value is an editable type.
        /// </summary>
        private bool IsEditableType(object value)
        {
            if (value is bool)       return true;
            if (value is sbyte)      return true;
            if (value is byte)       return true;
            if (value is short)      return true;
            if (value is ushort)     return true;
            if (value is int)        return true;
            if (value is uint)       return true;
            if (value is long)       return true;
            if (value is ulong)      return true;
            if (value is float)      return true;
            if (value is double)     return true;
            if (value is string)     return true;
            if (value is DateTime)   return true;
            if (value is Guid)       return true;

            return false;
        }

        /// <summary>
        /// Returns the list item at the specified index.
        /// </summary>
        private ListViewItem GetListItem(int index, ref bool overwrite, string name, string type)
        {
            ListViewItem listitem = null;
            
            // check if there is an item that could be re-used.
            if (!m_expanding && index < ItemsLV.Items.Count)
            {                
                return (ListViewItem) ItemsLV.Items[index];
            }
            
            overwrite = false;

            listitem = new ListViewItem();
            listitem.Name = name;

            listitem.Tag = new ValueState();

            if (!m_expanding)
            {
                ItemsLV.Items.Add(listitem);
            }
            else
            {
                ItemsLV.Items.Insert(index, listitem);
            }
                
            return listitem;
        }
        
        /// <summary>
        /// Returns true if the type can be expanded.
        /// </summary>
        private bool IsExpandableType(object value)
        {
            // check for null.
            if (value == null)
            {
                return false;
            }

            // check for Variant.
            if (value is Variant)
            {
                return IsExpandableType(((Variant)value).Value);
            }
            
            // check for bytes.
            byte[] bytes = value as byte[];

            if (bytes != null)
            {
                return false;
            }           
            
            // check for xml element.
            XmlElement xml = value as XmlElement;

            if (xml != null)
            {
                if (xml.ChildNodes.Count == 1 && xml.ChildNodes[0] is XmlText)
                {
                    return false;
                }

                return xml.HasChildNodes;
            }           
            
            // check for array.
            Array array = value as Array;

            if (array != null)
            {
                return array.Length > 0;
            }           
            
            // check for list.
            IList list = value as IList;

            if (list != null)
            {
                return list.Count > 0;
            }
            
            // check for encodeable object.
            IEncodeable encodeable = value as IEncodeable;

            if (encodeable != null)
            {
                return true;
            }

            // check for extension object.
            ExtensionObject extension = value as ExtensionObject;
            
            if (extension != null)
            {
                return IsExpandableType(extension.Body);
            }
            
            // check for data value.
            DataValue datavalue = value as DataValue;
            
            if (datavalue != null)
            {
                return true;
            }
            
            // check for event value.
            EventFieldList eventFields = value as EventFieldList;
            
            if (eventFields != null)
            {
                return true;
            }

            // must be a simple value.
            return false;
        }

        /// <summary>
        /// Formats a value for display in the control.
        /// </summary>
        private string GetValueText(object value)
        {
            // check for null.
            if (value == null)
            {
                return "(null)";
            }
            
            // format bytes.
            byte[] bytes = value as byte[];

            if (bytes != null)
            {
                StringBuilder buffer = new StringBuilder();

                for (int ii = 0; ii < bytes.Length; ii++)
                {
                    if (ii != 0 && ii%16 == 0)
                    {
                        buffer.Append(" ");
                    }

                    buffer.AppendFormat("{0:X2} ", bytes[ii]);
                }

                return buffer.ToString();
            }           
            
            // format xml element.
            XmlElement xml = value as XmlElement;

            if (xml != null)
            {
                // return the entire element if not expandable.
                if (!IsExpandableType(xml))
                {
                    return xml.OuterXml;
                }
                
                // show only the start tag.
                string text = xml.OuterXml;

                int index = text.IndexOf('>');

                if (index != -1)
                {
                    text = text.Substring(0, index);
                }
                
                return text;
            }           
            
            // format array.
            Array array = value as Array;

            if (array != null)
            {
                return Utils.Format("{1}[{0}]", array.Length, value.GetType().GetElementType().Name);
            }           
            
            // format list.
            IList list = value as IList;

            if (list != null)
            {
                string type = value.GetType().Name;

                if (type.EndsWith("Collection"))
                {
                    type = type.Substring(0, type.Length - "Collection".Length);
                }
                else
                {
                    type = "Object";
                }

                return Utils.Format("{1}[{0}]", list.Count, type);
            }
            
            // format encodeable object.
            IEncodeable encodeable = value as IEncodeable;

            if (encodeable != null)
            {
                return encodeable.GetType().Name;
            }

            // format extension object.
            ExtensionObject extension = value as ExtensionObject;
            
            if (extension != null)
            {
                return GetValueText(extension.Body);
            }
            
            // check for event value.
            EventFieldList eventFields = value as EventFieldList;
            
            if (eventFields != null)
            {
                if (m_monitoredItem != null)
                {
                    return String.Format("{0}", m_monitoredItem.GetEventType(eventFields));
                }

                return eventFields.GetType().Name;
            }

            // check for data value.
            DataValue dataValue = value as DataValue;
            
            if (dataValue != null)
            {
                if (StatusCode.IsBad(dataValue.StatusCode))
                {
                    return String.Format("{0}", dataValue.StatusCode);
                }

                return String.Format("{0}", dataValue.Value);
            }
            
            // use default formatting.
            return Utils.Format("{0}", value);
        }

        /// <summary>
        /// Updates the list with the specified value.
        /// </summary>
        private void UpdateList(
            ref int  index, 
            ref bool overwrite,
            object   value,
            object   componentValue,
            object   componentId,
            string   name,
            string   type)
        {
            // get the list item to update.
            ListViewItem listitem = GetListItem(index, ref overwrite, name, type);
                                    
            // move to next item.
            index++;

            ValueState state = listitem.Tag as ValueState;
            
            // recursively update sub-values if item is expanded.
            if (overwrite)
            {
                if (state.Expanded && state.Expandable)
                {
                    m_depth++;
                    ShowValue(ref index, ref overwrite, componentValue.ToString());
                    m_depth--;
                }
            }

            // update state.
            state.Expandable  = IsExpandableType(componentValue);
            state.Value       = value;
            state.Component   = componentValue;
            state.ComponentId = componentId;
        }

        /// <summary>
        /// Shows property of an encodeable object in the control.
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, IEncodeable value, PropertyInfo property)
        {            
            // get the name of the property.
            string name = Utils.GetDataMemberName(property);

            if (name == null)
            {
                return;
            }
            
            // get the property value.
            object propertyValue = null;

            MethodInfo[] accessors = property.GetAccessors();

            for (int ii = 0; ii < accessors.Length; ii++)
            {
                if (accessors[ii].ReturnType == property.PropertyType)
                {
                    propertyValue = accessors[ii].Invoke(value, null);
                    break;
                }
            }
           
            if (propertyValue is Variant)
            {
                propertyValue = ((Variant)propertyValue).Value;
            }
            
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                propertyValue,
                property,
                name,
                property.PropertyType.Name);
        }
        
        /// <summary>
        /// Shows the element of an array in the control.
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, Array value, int element)
        {            
            // get the name of the element.
            string name = Utils.Format("[{0}]", element);
                      
            // get the element value.
            object elementValue = value.GetValue(element);
            
            // get the type name.
            string type = null;

            if (elementValue != null)
            {
                type = elementValue.GetType().Name;
            }
            
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                elementValue,
                element,
                name,
                type);
        }

        /// <summary>
        /// Asks for confirmation before expanding a long list.
        /// </summary>
        private async Task<bool> PromptOnLongList(int length)
        {
            if (length < 256)
            {
                return true;
            }
            MessageDlg dialog = new MessageDlg("It may take a long time to display the list are you sure you want to continue?", MessageDlgButton.Yes, MessageDlgButton.No);
            MessageDlgButton result = await dialog.ShowAsync();
            if (result != MessageDlgButton.Yes)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Shows the element of a list in the control.
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, IList value, int element)
        {            
            // get the name of the element.
            string name = Utils.Format("[{0}]", element);
                      
            // get the element value.
            object elementValue = value[element];
            
            // get the type name.
            string type = null;

            if (elementValue != null)
            {
                type = elementValue.GetType().Name;
            }
                        
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                elementValue,
                element,
                name,
                type);
        }
        
        /// <summary>
        /// Shows an XML element in the control.
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, XmlElement value, int childIndex)
        {        
            // ignore children that are not elements.
            XmlElement child = value.ChildNodes[childIndex] as XmlElement;

            if (child == null)
            {
                return;
            }
            
            // get the name of the element.
            string name = Utils.Format("{0}", child.Name);
            
            // get the type name.
            string type = value.GetType().Name;
                        
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                child,
                childIndex,
                name,
                type);
        }        
                
        /// <summary>
        /// Shows an event in the control.
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, EventFieldList value, int fieldIndex)
        {        
            // ignore children that are not elements.
            object field = value.EventFields[fieldIndex].Value;

            if (field == null)
            {
                return;
            }
            
            // get the name of the element.
            string name = null;

            if (m_monitoredItem != null)
            {                
                name = m_monitoredItem.GetFieldName(fieldIndex);
            }
            
            // get the type name.
            string type = value.GetType().Name;
                        
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                field,
                fieldIndex,
                name,
                type);
        }        
        
        /// <summary>
        /// Shows a byte array in the control. 
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, byte[] value, int blockStart)
        {           
            // get the name of the element.
            string name = Utils.Format("[{0:X4}]", blockStart);
                      
            int bytesLeft = value.Length - blockStart;
            
            if (bytesLeft > 16)
            {
                bytesLeft = 16;
            }

            // get the element value.
            byte[] blockValue = new byte[bytesLeft];
            Array.Copy(value, blockStart, blockValue, 0, bytesLeft);
            
            // get the type name.
            string type = value.GetType().Name;
                        
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                blockValue,
                blockStart,
                name,
                type);
        }
        
        /// <summary>
        /// Shows a data value in the control. 
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, DataValue value, int component)
        {      
            string name = null;
            object componentValue = null;

            switch (component)
            {
                case 0:
                {
                    name = "Value";
                    componentValue = value.Value;

                    ExtensionObject extension = componentValue as ExtensionObject;
                    
                    if (extension != null)
                    {
                        componentValue = extension.Body;
                    }

                    break;
                }

                case 1:
                {
                    name = "StatusCode";
                    componentValue = value.StatusCode;
                    break;
                }

                case 2:
                {
                    if (value.SourceTimestamp != DateTime.MinValue)
                    {
                        name = "SourceTimestamp";
                        componentValue = value.SourceTimestamp;
                    }

                    break;
                }

                case 3:
                {
                    if (value.ServerTimestamp != DateTime.MinValue)
                    {
                        name = "ServerTimestamp";
                        componentValue = value.ServerTimestamp;
                    }

                    break;
                }
            }

            // don't display empty components.
            if (name == null)
            {
                return;
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }
           
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                componentValue,
                component,
                name,
                type);
        }

        /// <summary>
        /// Shows a node id in the control. 
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, NodeId value, int component)
        {      
            string name = null;
            object componentValue = null;

            switch (component)
            {
                case 0:
                {
                    name = "IdType";
                    componentValue = value.IdType;
                    break;
                }

                case 1:
                {
                    name = "Identifier";
                    componentValue = value.Identifier;
                    break;
                }

                case 2:
                {
                    name = "NamespaceIndex";
                    componentValue = value.NamespaceIndex;
                    break;
                }
            }

            // don't display empty components.
            if (name == null)
            {
                return;
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }
           
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                componentValue,
                component,
                name,
                type);
        }
        
        /// <summary>
        /// Shows am expanded node id in the control. 
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, ExpandedNodeId value, int component)
        {      
            string name = null;
            object componentValue = null;

            switch (component)
            {
                case 0:
                {
                    name = "IdType";
                    componentValue = value.IdType;
                    break;
                }

                case 1:
                {
                    name = "Identifier";
                    componentValue = value.Identifier;
                    break;
                }

                case 2:
                {
                    name = "NamespaceIndex";
                    componentValue = value.NamespaceIndex;
                    break;
                }

                case 3:
                {
                    name = "NamespaceUri";
                    componentValue = value.NamespaceUri;
                    break;
                }
            }

            // don't display empty components.
            if (name == null)
            {
                return;
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }
           
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                componentValue,
                component,
                name,
                type);
        }
        
        /// <summary>
        /// Shows qualified name in the control. 
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, QualifiedName value, int component)
        {      
            string name = null;
            object componentValue = null;

            switch (component)
            {
                case 0:
                {
                    name = "Name";
                    componentValue = value.Name;
                    break;
                }

                case 1:
                {
                    name = "NamespaceIndex";
                    componentValue = value.NamespaceIndex;
                    break;
                }
            }

            // don't display empty components.
            if (name == null)
            {
                return;
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }
           
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                componentValue,
                component,
                name,
                type);
        }
        
        /// <summary>
        /// Shows localized text in the control. 
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, LocalizedText value, int component)
        {      
            string name = null;
            object componentValue = null;

            switch (component)
            {
                case 0:
                {
                    name = "Text";
                    componentValue = value.Text;
                    break;
                }

                case 1:
                {
                    name = "Locale";
                    componentValue = value.Locale;
                    break;
                }
            }

            // don't display empty components.
            if (name == null)
            {
                return;
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }
           
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                componentValue,
                component,
                name,
                type);
        }
        
        /// <summary>
        /// Shows a string in the control. 
        /// </summary>
        private void ShowValue(ref int index, ref bool overwrite, string value)
        {      
            string name = "Value";
            object componentValue = value;

            // don't display empty components.
            if (name == null)
            {
                return;
            }

            // get the type name.
            string type = "(unknown)";

            if (componentValue != null)
            {
                type = componentValue.GetType().Name;
            }
           
            // update the list view.
            UpdateList(
                ref index,
                ref overwrite,
                value,
                componentValue,
                0,
                name,
                type);
        }

        /// <summary>
        /// Shows a value in control.
        /// </summary>
        private async Task ShowValue(int index, bool overwrite, object value)
        {
            if (value == null)
            {
                return;
            }

            // show monitored items.
            MonitoredItem monitoredItem = value as MonitoredItem;

            if (monitoredItem != null)
            {
                m_monitoredItem = monitoredItem;
                ShowValue(ref index, ref overwrite, monitoredItem.LastValue.ToString());
                return;
            }

            // show data changes
            MonitoredItemNotification datachange = value as MonitoredItemNotification;

            if (datachange != null)
            {
                ShowValue(ref index, ref overwrite, datachange.Value.ToString());
                return;
            }

            // show events
            EventFieldList eventFields = value as EventFieldList;

            if (eventFields != null)
            {
                for (int ii = 0; ii < eventFields.EventFields.Count; ii++)
                {
                    ShowValue(ref index, ref overwrite, eventFields, ii);
                }

                return;
            }

            // show extension bodies.
            ExtensionObject extension = value as ExtensionObject;

            if (extension != null)
            {
                ShowValue(ref index, ref overwrite, extension.Body.ToString());
                return;
            }

            // show encodeables.
            IEncodeable encodeable = value as IEncodeable;

            if (encodeable != null)
            {
                PropertyInfo[] properties = encodeable.GetType().GetProperties();

                foreach (PropertyInfo property in properties)
                {
                    ShowValue(ref index, ref overwrite, encodeable, property);
                }

                return;
            }

            // show bytes.
            byte[] bytes = value as byte[];

            if (bytes != null)
            {
                bool result = await PromptOnLongList(bytes.Length / 16);
                if (!result)
                {
                    return;
                }

                for (int ii = 0; ii < bytes.Length; ii += 16)
                {
                    ShowValue(ref index, ref overwrite, bytes, ii);
                }

                return;
            }

            // show arrays
            Array array = value as Array;

            if (array != null)
            {
                bool result = await PromptOnLongList(array.Length);
                if (!result)
                {
                    return;
                }

                for (int ii = 0; ii < array.Length; ii++)
                {
                    ShowValue(ref index, ref overwrite, array, ii);
                }

                return;
            }

            // show lists
            IList list = value as IList;

            if (list != null)
            {
                bool result = await PromptOnLongList(list.Count);
                if (!result)
                {
                    return;
                }

                for (int ii = 0; ii < list.Count; ii++)
                {
                    ShowValue(ref index, ref overwrite, list, ii);
                }

                return;
            }

            // show xml elements
            XmlElement xml = value as XmlElement;

            if (xml != null)
            {
                bool result = await PromptOnLongList(xml.ChildNodes.Count);
                if (!result)
                {
                    return;
                }

                for (int ii = 0; ii < xml.ChildNodes.Count; ii++)
                {
                    ShowValue(ref index, ref overwrite, xml, ii);
                }

                return;
            }

            // show data value.
            DataValue datavalue = value as DataValue;

            if (datavalue != null)
            {
                ShowValue(ref index, ref overwrite, datavalue, 0);
                ShowValue(ref index, ref overwrite, datavalue, 1);
                ShowValue(ref index, ref overwrite, datavalue, 2);
                ShowValue(ref index, ref overwrite, datavalue, 3);
                return;
            }

            // show node id value.
            NodeId nodeId = value as NodeId;

            if (nodeId != null)
            {
                ShowValue(ref index, ref overwrite, nodeId, 0);
                ShowValue(ref index, ref overwrite, nodeId, 1);
                ShowValue(ref index, ref overwrite, nodeId, 2);
                return;
            }

            // show expanded node id value.
            ExpandedNodeId expandedNodeId = value as ExpandedNodeId;

            if (expandedNodeId != null)
            {
                ShowValue(ref index, ref overwrite, expandedNodeId, 0);
                ShowValue(ref index, ref overwrite, expandedNodeId, 1);
                ShowValue(ref index, ref overwrite, expandedNodeId, 2);
                ShowValue(ref index, ref overwrite, expandedNodeId, 3);
                return;
            }

            // show qualified name value.
            QualifiedName qualifiedName = value as QualifiedName;

            if (qualifiedName != null)
            {
                ShowValue(ref index, ref overwrite, qualifiedName, 0);
                ShowValue(ref index, ref overwrite, qualifiedName, 1);
                return;
            }

            // show qualified name value.
            LocalizedText localizedText = value as LocalizedText;

            if (localizedText != null)
            {
                ShowValue(ref index, ref overwrite, localizedText, 0);
                ShowValue(ref index, ref overwrite, localizedText, 1);
                return;
            }

            // show variant.
            Variant? variant = value as Variant?;

            if (variant != null)
            {
                ShowValue(ref index, ref overwrite, variant.Value.Value.ToString());
                return;
            }

            // show unknown types as strings.
            ShowValue(ref index, ref overwrite, String.Format("{0}", value));
        }
        #endregion
    }
}
