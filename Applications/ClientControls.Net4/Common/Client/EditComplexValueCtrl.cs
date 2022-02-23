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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Reflection;

namespace Opc.Ua.Client.Controls.Common
{
    /// <summary>
    /// Allows the user to edit a complex value.
    /// </summary>
    public partial class EditComplexValueCtrl : UserControl
    {
        /// <summary>
        /// Constructs the object.
        /// </summary>
        public EditComplexValueCtrl()
        {
            InitializeComponent();
            MaxDisplayTextLength = 100;
            ValuesDV.AutoGenerateColumns = false;
            ImageList = new ClientUtils().ImageList;

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Values");

            m_dataset.Tables[0].Columns.Add("AccessInfo", typeof(AccessInfo));
            m_dataset.Tables[0].Columns.Add("Name", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataType", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(string));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));

            ValuesDV.DataSource = m_dataset.Tables[0];
        }

        #region Private Fields
        private DataSet m_dataset;
        private Session m_session;
        private AccessInfo m_value;
        private bool m_readOnly;
        private int m_maxDisplayTextLength;
        private event EventHandler m_ValueChanged;
        #endregion

        private class AccessInfo
        {
            public AccessInfo Parent { get; set; }
            public PropertyInfo PropertyInfo { get; set; }
            public int[] Indexes;
            public TypeInfo TypeInfo;
            public object Value;
            public string Name;
        }

        #region Public Members
        /// <summary>
        /// The maximum length of a value string displayed in a column.
        /// </summary>
        [DefaultValue(100)]
        public int MaxDisplayTextLength
        {
            get
            {
                return m_maxDisplayTextLength;
            }

            set
            {
                if (value < 20)
                {
                    m_maxDisplayTextLength = 20;
                }

                m_maxDisplayTextLength = value;
            }
        }

        /// <summary>
        /// Returns true if the Back command can be called.
        /// </summary>
        public bool CanGoBack
        {
            get
            {
                return (NavigationMENU.Items.Count > 1);
            }
        }

        /// <summary>
        /// Returns true if the ArraySize can be changed.
        /// </summary>
        public bool CanSetArraySize
        {
            get
            {
                if (m_readOnly)
                {
                    return false;
                }

                AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;

                if (info != null)
                {
                    return info.TypeInfo.ValueRank >= 0;
                }

                return false;
            }
        }

        /// <summary>
        /// Returns true if the data type can be changed.
        /// </summary>
        public bool CanChangeType
        {
            get
            {
                if (m_readOnly)
                {
                    return false;
                }

                if (NavigationMENU.Items.Count > 0)
                {
                    AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;

                    if (info != null)
                    {
                        return info.Parent != null && info.Parent.TypeInfo != null && info.Parent.TypeInfo.BuiltInType == BuiltInType.Variant;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Returns the current data type.
        /// </summary>
        public BuiltInType CurrentType
        {
            get
            {
                if (NavigationMENU.Items.Count > 0)
                {
                    AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;

                    if (info != null)
                    {
                        Variant? value = info.Value as Variant?;

                        if (value != null && value.Value.TypeInfo != null)
                        {
                            return value.Value.TypeInfo.BuiltInType;
                        }

                        return info.TypeInfo.BuiltInType;
                    }
                }

                return BuiltInType.Variant;
            }
        }

        /// <summary>
        /// Raised when the value is changed.
        /// </summary>
        public event EventHandler ValueChanged
        {
            add { m_ValueChanged += value; }
            remove { m_ValueChanged -= value; }
        }

        /// <summary>
        /// Changes the session used for editing the value.
        /// </summary>
        public void ChangeSession(Session session)
        {
            m_session = session;
        }

        /// <summary>
        /// Moves the displayed value back.
        /// </summary>
        public void Back()
        {
            if (!CanGoBack)
            {
                return;
            } 
            
            NavigationMENU_Click(NavigationMENU.Items[NavigationMENU.Items.Count - 2], null);
        }


        /// <summary>
        /// Changes the array size.
        /// </summary>
        public void SetArraySize()
        {
            if (!CanSetArraySize)
            {
                return;
            }

            EndEdit();

            AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;

            TypeInfo currentType = info.TypeInfo;
            object currentValue = info.Value;

            if (info.Value is Variant)
            {
                Variant variant = (Variant)info.Value;
                currentValue = variant.Value;

                if (currentValue != null)
                {
                    currentType = variant.TypeInfo;

                    if (currentType == null)
                    {
                        currentType = TypeInfo.Construct(currentValue);
                    }
                }
            }

            int[] dimensions = null;

            Array array = currentValue as Array;

            if (array != null)
            {
                dimensions = new int[array.Rank];

                for (int ii = 0; ii < array.Rank; ii++)
                {
                    dimensions[ii] = array.GetLength(ii);
                }
            }

            IList list = currentValue as IList;

            if (array == null && list != null)
            {
                dimensions = new int[1];
                dimensions[0] = list.Count;
            }

            Matrix matrix = currentValue as Matrix;

            if (matrix != null)
            {
                dimensions = matrix.Dimensions;
                array = matrix.ToArray();
            }

            SetTypeDlg.SetTypeResult result = new SetTypeDlg().ShowDialog(currentType, dimensions);

            if (result == null)
            {
                return;
            }

            // convert to new type.
            object newValue = currentValue;

            if (result.ArrayDimensions == null || result.ArrayDimensions.Length < 1)
            {
                newValue = Convert(currentValue, currentType, result.TypeInfo, result.UseDefaultOnError);
            }
            else
            {
                if (array == null && list != null)
                {
                    Type elementType = GetListElementType(list);

                    for (int ii = result.ArrayDimensions[0]; ii < list.Count; ii++)
                    {
                        list.RemoveAt(ii);
                    }

                    for (int ii = list.Count; ii < result.ArrayDimensions[0]; ii++)
                    {
                        list.Add(Activator.CreateInstance(elementType));
                    }

                    newValue = list;
                }

                if (array != null)
                {
                    Array newArray = null;

                    if (currentValue is Array)
                    {
                        newArray = Array.CreateInstance(currentValue.GetType().GetElementType(), result.ArrayDimensions);
                    }
                    else
                    {
                        newArray = TypeInfo.CreateArray(result.TypeInfo.BuiltInType, result.ArrayDimensions);
                    }

                    int maxCount = result.ArrayDimensions[0];

                    for (int ii = 1; ii < result.ArrayDimensions.Length; ii++)
                    {
                        maxCount *= result.ArrayDimensions[ii];
                    }

                    int count = 0;

                    foreach (object element in array)
                    {
                        if (maxCount <= count)
                        {
                            break;
                        }

                        object newElement = Convert(element, currentType, result.TypeInfo, result.UseDefaultOnError);
                        int[] indexes = GetIndexFromCount(count++, result.ArrayDimensions);
                        newArray.SetValue(newElement, indexes);
                    }

                    newValue = newArray;
                }
            }

            NavigationMENU.Items.RemoveAt(NavigationMENU.Items.Count - 1);

            info.TypeInfo = result.TypeInfo;
            info.Value = newValue;
            ShowValue(info);
        }

        /// <summary>
        /// Changes the data type.
        /// </summary>
        public void SetType(BuiltInType builtInType)
        {
            if (!CanChangeType)
            {
                return;
            }
            
            AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;

            TypeInfo currentType = info.TypeInfo;
            object currentValue = info.Value;

            try
            {
                EndEdit();
                currentValue = info.Value;
            }
            catch (Exception)
            {
                currentValue = TypeInfo.GetDefaultValue(currentType.BuiltInType);
            }

            if (info.Value is Variant)
            {
                Variant variant = (Variant)info.Value;
                currentValue = variant.Value;

                if (currentValue != null)
                {
                    currentType = variant.TypeInfo;

                    if (currentType == null)
                    {
                        currentType = TypeInfo.Construct(currentValue);
                    }
                }
            }
            
            TypeInfo targetType = new TypeInfo(builtInType, currentType.ValueRank);
            object newValue  = Convert(currentValue, currentType, targetType, true);

            NavigationMENU.Items.RemoveAt(NavigationMENU.Items.Count - 1);

            info.TypeInfo = targetType;
            info.Value = newValue;
            ShowValueNoNotify(info);
        }

        /// <summary>
        /// Converts the old type to the new type.
        /// </summary>
        private object Convert(object oldValue, TypeInfo oldType, TypeInfo newType, bool useDefaultOnError)
        {
            object newValue = oldValue;

            if (oldType.BuiltInType != newType.BuiltInType)
            {
                try
                {
                    newValue = TypeInfo.Cast(oldValue, oldType, newType.BuiltInType);
                }
                catch (Exception e)
                {
                    if (!useDefaultOnError)
                    {
                        throw new FormatException("Could not cast value to requested type.", e);
                    }

                    newValue = TypeInfo.GetDefaultValue(newType.BuiltInType);
                }
            }

            return newValue;
        }

        /// <summary>
        /// Displays the value in the control.
        /// </summary>
        public void ShowValue(
            NodeId nodeId,
            uint attributeId,
            string name, 
            object value, 
            bool readOnly)
        {
            m_readOnly = readOnly;
            NavigationMENU.Items.Clear();

            if (m_readOnly)
            {
                ValuesDV.EditMode = DataGridViewEditMode.EditProgrammatically;
                TextValueTB.ReadOnly = true;
            }

            Type type = null;

            // determine the expected data type for non-value attributes.
            if (attributeId != 0 && attributeId != Attributes.Value)
            {
                BuiltInType builtInType = TypeInfo.GetBuiltInType(Attributes.GetDataTypeId(attributeId));
                int valueRank = Attributes.GetValueRank(attributeId);
                type = TypeInfo.GetSystemType(builtInType, valueRank);
            }

            // determine the expected data type for value attributes.
            else if (!NodeId.IsNull(nodeId))
            {
                IVariableBase variable = m_session.NodeCache.Find(nodeId) as IVariableBase;

                if (variable != null)
                {
                    BuiltInType builtInType = TypeInfo.GetBuiltInType(variable.DataType, m_session.TypeTree);
                    int valueRank = variable.ValueRank;
                    type = TypeInfo.GetSystemType(builtInType, valueRank);

                    if (builtInType == BuiltInType.ExtensionObject && valueRank < 0)
                    {
                        type = TypeInfo.GetSystemType(variable.DataType, m_session.Factory);
                    }
                }
            }

            // use the value.
            else if (value != null)
            {
                type = value.GetType();
            }

            // go with default.
            else
            {
                type = typeof(string);
            }

            // assign a name.
            if (String.IsNullOrEmpty(name))
            {
                if (attributeId != 0)
                {
                    name = Attributes.GetBrowseName(attributeId);
                }
                else
                {
                    name = type.Name;
                }
            }

            AccessInfo info = new AccessInfo();
            info.Value = Utils.Clone(value);
            info.TypeInfo = TypeInfo.Construct(type);

            if (value == null && info.TypeInfo.ValueRank < 0)
            {
                info.Value = TypeInfo.GetDefaultValue(info.TypeInfo.BuiltInType);
            }

            info.Name = name;
            m_value = info;

            ShowValue(info);
        }

        /// <summary>
        /// Displays the value in the control.
        /// </summary>
        public void ShowValue(
            string name,
            NodeId dataType,
            int valueRank,
            object value)
        {
            if (value == null && m_session != null)
            {
                BuiltInType builtInType = TypeInfo.GetBuiltInType(dataType, m_session.TypeTree);

                if (builtInType == BuiltInType.ExtensionObject)
                {
                    Type type = m_session.Factory.GetSystemType(dataType);

                    if (type != null)
                    {
                        if (valueRank < 0)
                        {
                            value = Activator.CreateInstance(type);
                        }
                        else
                        {
                            value = Array.CreateInstance(type, new int[valueRank]);
                        }
                    }
                }
                else
                {
                    value = TypeInfo.GetDefaultValue(dataType, valueRank, m_session.TypeTree);
                }
            }

            ShowValue(null, name, value);
        }

        /// <summary>
        /// Displays the value in the control.
        /// </summary>
        public void ShowValue(
            TypeInfo expectedType,
            string name,
            object value)
        {
            m_readOnly = false;
            NavigationMENU.Items.Clear();

            // assign a type.
            if (expectedType == null)
            {
                if (value == null)
                {
                    expectedType = TypeInfo.Scalars.String;
                }
                else
                {
                    expectedType = TypeInfo.Construct(value);
                }
            }

            // assign a name.
            if (String.IsNullOrEmpty(name))
            {
                name = expectedType.ToString();
            }
            
            AccessInfo info = new AccessInfo();
            info.Value = Utils.Clone(value);
            info.TypeInfo = expectedType;

            if (value == null && info.TypeInfo.ValueRank < 0)
            {
                info.Value = TypeInfo.GetDefaultValue(info.TypeInfo.BuiltInType);
            }

            // ensure value is the target type.
            info.Value = TypeInfo.Cast(info.Value, expectedType.BuiltInType);

            info.Name = name;
            m_value = info;

            ShowValue(info);
        }

        /// <summary>
        /// Returns the edited value.
        /// </summary>
        public object GetValue()
        {
            return m_value.Value;
        }

        /// <summary>
        /// Validates the value currently being edited.
        /// </summary>
        public void EndEdit()
        {
            if (NavigationMENU.Items.Count < 1)
            {
                return;
            }

            if (!TextValueTB.Visible)
            {
                ValuesDV.EndEdit();
                return;
            }

            AccessInfo info = NavigationMENU.Items[NavigationMENU.Items.Count - 1].Tag as AccessInfo;
            object newValue = TypeInfo.Cast(TextValueTB.Text, info.TypeInfo.BuiltInType);
            info.Value = newValue;
            UpdateParent(info);
        }

        /// <summary>
        /// Displays the value in the control.
        /// </summary>
        private void ShowValue(AccessInfo parent)
        {
            ShowValueNoNotify(parent);

            if (m_ValueChanged != null)
            {
                m_ValueChanged(this, null);
            }
        }

        /// <summary>
        /// Displays the value in the control.
        /// </summary>
        private void ShowValueNoNotify(AccessInfo parent)
        {
            m_dataset.Tables[0].Clear();
            ValuesDV.Visible = true;
            TextValueTB.Visible = false;

            ToolStripItem item = NavigationMENU.Items.Add(parent.Name);
            item.Click += new EventHandler(NavigationMENU_Click);
            item.Tag = parent;

            TypeInfo typeInfo = parent.TypeInfo;
            object value = parent.Value;

            if (value is Variant)
            {
                Variant variant = (Variant)value;
                value = variant.Value;

                if (value != null)
                {
                    parent.TypeInfo = typeInfo = variant.TypeInfo;

                    if (typeInfo == null)
                    {
                        parent.TypeInfo = typeInfo = TypeInfo.Construct(value);
                    }
                }
            }

            if (typeInfo.ValueRank >= 0)
            {
                Matrix matrix = value as Matrix;

                if (matrix != null)
                {
                    value = matrix.ToArray();
                }

                System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;

                if (enumerable != null)
                {
                    // get the dimensions of any array.
                    int[] dimensions = null;

                    // calculate them.
                    if (matrix == null)
                    {
                        Array array = enumerable as Array;

                        if (array != null)
                        {
                            dimensions = new int[array.Rank];

                            for (int ii = 0; ii < array.Rank; ii++)
                            {
                                dimensions[ii] = array.GetLength(ii);
                            }
                        }
                        else
                        {
                            dimensions = new int[1];
                            System.Collections.IList list = enumerable as System.Collections.IList;

                            if (list != null)
                            {
                                dimensions[0] = list.Count;
                            }
                        }
                    }

                    // get them from the matrix.
                    else
                    {
                        dimensions = matrix.Dimensions;
                    }

                    // display the array elements.
                    int count = 0;
                    TypeInfo elementType = new TypeInfo(typeInfo.BuiltInType, ValueRanks.Scalar);

                    ValuesDV.Visible = true;
                    TextValueTB.Visible = false;

                    foreach (object element in enumerable)
                    {
                        int[] indexes = GetIndexFromCount(count++, dimensions);

                        AccessInfo info = new AccessInfo();
                        info.Parent = parent;
                        info.Indexes = indexes;
                        info.TypeInfo = elementType;
                        info.Value = element;

                        ShowIndexedValue(info);
                    }
                }

                return;
            }

            // check for null.
            if (value == null)
            {
                if (parent.Parent != null && parent.Parent.Value is Array)
                {
                    parent.Value = value = Activator.CreateInstance(parent.Parent.Value.GetType().GetElementType());
                }
                else if (parent.Parent != null && parent.PropertyInfo != null)
                {
                    parent.Value = value = Activator.CreateInstance(parent.PropertyInfo.PropertyType);
                }
                else
                {
                    ShowTextValue(value, parent.TypeInfo);
                    return;
                }
            }

            object structure = value;

            // check for extension object.
            ExtensionObject extension = structure as ExtensionObject;

            if (extension != null)
            {
                structure = extension.Body;
            }

            // check for XmlElements.
            if (structure is XmlElement)
            {
                ShowTextValue((XmlElement)structure);
                return;
            }

            // check for ByteString.
            if (structure is byte[])
            {
                ShowTextValue((byte[])structure);
                return;
            }

            // check for NodeId.
            if (structure is NodeId)
            {
                ShowTextValue(((NodeId)structure).ToString());
                return;
            }

            // check for ExpandedNodeId.
            if (structure is ExpandedNodeId)
            {
                ShowTextValue(((ExpandedNodeId)structure).ToString());
                return;
            }

            // check for QualifiedName.
            if (structure is QualifiedName)
            {
                ShowTextValue(((QualifiedName)structure).ToString());
                return;
            }

            // check for Guid.
            if (structure is Guid)
            {
                ShowTextValue(((Guid)structure).ToString());
                return;
            }

            // check for Uuid.
            if (structure is Uuid)
            {
                ShowTextValue(((Uuid)structure).ToString());
                return;
            }

            // check for StatusCode.
            if (structure is StatusCode)
            {
                ShowTextValue(Utils.Format("0x{0:X8}", ((StatusCode)structure).Code));
                return;
            }

            ValuesDV.Visible = true;
            TextValueTB.Visible = false;

            // use reflection to display the properties of the structure.
            bool isStructure = false;
            PropertyInfo[] properties = structure.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (PropertyInfo property in properties)
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object element = property.GetValue(structure, null);

                string name = null;

                foreach (object attribute in property.GetCustomAttributes(true))
                {
                    if (typeof(System.Runtime.Serialization.DataMemberAttribute).IsInstanceOfType(attribute))
                    {
                        name = ((System.Runtime.Serialization.DataMemberAttribute)attribute).Name;

                        if (name == null)
                        {
                            name = property.Name;
                        }

                        break;
                    }
                }

                if (name == null)
                {
                    continue;
                }

                AccessInfo info = new AccessInfo();
                info.Parent = parent;
                info.PropertyInfo = property;
                info.TypeInfo = TypeInfo.Construct(property.PropertyType);
                info.Value = element;
                info.Name = name;

                ShowNamedValue(info);
                isStructure = true;
            }

            if (!isStructure)
            {
                ShowTextValue(parent.Value, parent.TypeInfo);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns the index based on the current count.
        /// </summary>
        private int[] GetIndexFromCount(int count, int[] dimensions)
        {
            int[] indexes = new int[(dimensions != null) ? dimensions.Length : 1];

            for (int ii = indexes.Length - 1; ii >= 0; ii--)
            {
                indexes[ii] = count % dimensions[ii];
                count /= dimensions[ii];
            }

            return indexes;
        }

        /// <summary>
        /// Adds the value at an array index to the control.
        /// </summary>
        private void ShowIndexedValue(AccessInfo info)
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            StringBuilder buffer = new StringBuilder();
            buffer.Append("[");

            if (info.Indexes != null)
            {
                for (int ii = 0; ii < info.Indexes.Length; ii++)
                {
                    if (ii > 0)
                    {
                        buffer.Append(",");
                    }

                    buffer.Append(info.Indexes[ii]);
                }
            }

            buffer.Append("]");
            info.Name = buffer.ToString();

            row[0] = info;
            row[1] = info.Name;
            row[2] = GetDataTypeString(info);
            row[3] = ValueToString(info.Value, info.TypeInfo);
            row[4] = ImageList.Images[ClientUtils.GetImageIndex(Attributes.Value, info.Value)];

            m_dataset.Tables[0].Rows.Add(row);
        }

        /// <summary>
        /// Returns the element type for a list.
        /// </summary>
        private Type GetListElementType(IList list)
        {
            if (list != null)
            {
                for (Type type = list.GetType(); type != null; type = type.BaseType)
                {
                    if (type.IsGenericType)
                    {
                        Type[] argTypes = type.GetGenericArguments();

                        if (argTypes.Length > 0)
                        {
                            return argTypes[0];
                        }
                    }
                }
            }

            return typeof(object);
        }

        /// <summary>
        /// Returns the data type of the value.
        /// </summary>
        private Type GetDataType(AccessInfo accessInfo)
        {
            if (accessInfo == null || accessInfo.TypeInfo == null)
            {
                return null;
            }

            if (accessInfo.TypeInfo.BuiltInType == BuiltInType.ExtensionObject)
            {
                if (accessInfo.Value != null)
                {
                    return accessInfo.Value.GetType();
                }

                if (accessInfo.PropertyInfo != null)
                {
                    return accessInfo.PropertyInfo.PropertyType;
                }

                if (accessInfo.Parent != null)
                {
                    if (accessInfo.Parent.Value is Array)
                    {
                        Array array = (Array)accessInfo.Parent.Value;
                        return array.GetType().GetElementType();
                    }

                    if (accessInfo.Parent.Value is IList)
                    {
                        IList list = (IList)accessInfo.Parent.Value;
                        return GetListElementType(list);
                    }
                }
            }

            return TypeInfo.GetSystemType(accessInfo.TypeInfo.BuiltInType, accessInfo.TypeInfo.ValueRank);
        }

        /// <summary>
        /// Returns the data type of the value.
        /// </summary>
        private string GetDataTypeString(AccessInfo accessInfo)
        {
            Type type = GetDataType(accessInfo);

            if (type == null)
            {
                return accessInfo.TypeInfo.ToString();
            }

            return type.Name;
        }

        /// <summary>
        /// Adds the value with the specified name to the control.
        /// </summary>
        private void ShowNamedValue(AccessInfo info)
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            row[0] = info;
            row[1] = (info.Name != null) ? info.Name : "unknown";
            row[2] = GetDataTypeString(info);
            row[3] = ValueToString(info.Value, info.TypeInfo);
            row[4] = ImageList.Images[ClientUtils.GetImageIndex(Attributes.Value, info.Value)];

            m_dataset.Tables[0].Rows.Add(row);
        }

        /// <summary>
        /// Displays a value in the control.
        /// </summary>
        private void ShowTextValue(object value, TypeInfo typeInfo)
        {
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.ByteString:
                {
                    ShowTextValue((byte[])value);
                    break;
                }

                case BuiltInType.XmlElement:
                {
                    ShowTextValue((XmlElement)value);
                    break;
                }

                case BuiltInType.String:
                {
                    ShowTextValue((string)value);
                    break;
                }

                default:
                {
                    ShowTextValue(ValueToString(value, typeInfo));
                    break;
                }
            }
        }

        /// <summary>
        /// Displays a string in the control.
        /// </summary>
        private void ShowTextValue(string value)
        {
            ValuesDV.Visible = false;
            TextValueTB.Visible = true;

            if (value != null && value.Length > MaxDisplayTextLength)
            {
                TextValueTB.ScrollBars = ScrollBars.Both;
            }
            else
            {
                TextValueTB.ScrollBars = ScrollBars.None;
            }

            TextValueTB.Font = new Font("Segoe UI", TextValueTB.Font.Size);
            TextValueTB.Text = value;
        }

        /// <summary>
        /// Displays a complete byte string in the control.
        /// </summary>
        private void ShowTextValue(byte[] value)
        {
            ValuesDV.Visible = false;
            TextValueTB.Visible = true;

            StringBuilder buffer = new StringBuilder();

            if (value != null)
            {
                for (int ii = 0; ii < value.Length; ii++)
                {
                    if (buffer.Length > 0 && (ii % 30) == 0)
                    {
                        buffer.Append("\r\n");
                    }

                    buffer.AppendFormat("{0:X2} ", value[ii]);
                }
            }

            TextValueTB.Font = new Font("Courier New", TextValueTB.Font.Size);
            TextValueTB.Text = buffer.ToString();
        }

        /// <summary>
        /// Displays a complete XML element in the control.
        /// </summary>
        private void ShowTextValue(XmlElement value)
        {
            ValuesDV.Visible = false;
            TextValueTB.Visible = true;

            StringBuilder buffer = new StringBuilder();

            if (value != null)
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.OmitXmlDeclaration = true;
                settings.NewLineHandling = NewLineHandling.Replace;
                settings.NewLineChars = "\r\n";
                settings.IndentChars = "    ";

                using (XmlWriter writer = XmlWriter.Create(buffer, settings))
                {
                    using (XmlNodeReader reader = new XmlNodeReader(value))
                    {
                        writer.WriteNode(reader, false);
                    }
                }
            }

            TextValueTB.Font = new Font("Courier New", TextValueTB.Font.Size);
            TextValueTB.Text = buffer.ToString();
        }

        /// <summary>
        /// Converts a value to a string for display in the grid.
        /// </summary>
        private string ValueToString(object value, TypeInfo typeInfo)
        {
            if (value == null)
            {
                return String.Empty;
            }

            if (value is Variant)
            {
                Variant variant = (Variant)value;
                value = variant.Value;

                if (value != null)
                {
                    typeInfo = variant.TypeInfo;

                    if (typeInfo == null)
                    {
                        typeInfo = TypeInfo.Construct(value);
                    }
                }
            }

            if (typeInfo.ValueRank >= 0)
            {
                StringBuilder buffer = new StringBuilder();

                System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;

                if (enumerable != null)
                {
                    buffer.Append("{ ");

                    foreach (object element in enumerable)
                    {
                        if (buffer.Length > 2)
                        {
                            buffer.Append(" | ");
                        }

                        if (buffer.Length > MaxDisplayTextLength)
                        {
                            buffer.Append("...");
                            break;
                        }

                        buffer.Append(ValueToString(element, new TypeInfo(typeInfo.BuiltInType, ValueRanks.Scalar)));
                    }

                    buffer.Append(" }");
                }

                return buffer.ToString();
            }

            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.String:
                {
                    string text = (string)value;

                    if (text != null && text.Length > MaxDisplayTextLength)
                    {
                        return text.Substring(0, MaxDisplayTextLength) + "...";
                    }

                    return text;
                }

                case BuiltInType.ByteString:
                {
                    StringBuilder buffer = new StringBuilder();

                    byte[] bytes = (byte[])value;

                    for (int ii = 0; ii < bytes.Length; ii++)
                    {
                        if (buffer.Length > MaxDisplayTextLength)
                        {
                            buffer.Append("...");
                            break;
                        }

                        buffer.AppendFormat("{0:X2}", bytes[ii]);
                    }

                    return buffer.ToString();
                }

                case BuiltInType.Enumeration:
                {
                    return ((int)value).ToString();
                }

                case BuiltInType.ExtensionObject:
                {
                    string text = null;

                    ExtensionObject extension = value as ExtensionObject;

                    if (extension != null)
                    {
                        if (extension.Body is byte[])
                        {
                            return ValueToString(extension.Body, new TypeInfo(BuiltInType.ByteString, ValueRanks.Scalar));
                        }

                        if (extension.Body is XmlElement)
                        {
                            return ValueToString(extension.Body, new TypeInfo(BuiltInType.XmlElement, ValueRanks.Scalar));
                        }

                        if (extension.Body is IEncodeable)
                        {
                            text = new Variant(extension).ToString();
                        }
                    }

                    if (text == null)
                    {
                        IEncodeable encodeable = value as IEncodeable;

                        if (encodeable != null)
                        {
                            text = new Variant(encodeable).ToString();
                        }
                    }

                    if (text != null && text.Length > MaxDisplayTextLength)
                    {
                        return text.Substring(0, MaxDisplayTextLength) + "...";
                    }

                    return text;
                }
            }

            return (string)TypeInfo.Cast(value, BuiltInType.String);
        }

        /// <summary>
        /// Whether the value can be edited in the grid view.
        /// </summary>
        private bool IsSimpleValue(AccessInfo info)
        {
            if (info == null || info.TypeInfo == null)
            {
                return true;
            }

            TypeInfo typeInfo = info.TypeInfo;
            object value = info.Value;

            if (value is Variant)
            {
                Variant variant = (Variant)info.Value;
                typeInfo = variant.TypeInfo;
                value = variant.Value;

                if (typeInfo == null)
                {
                    typeInfo = TypeInfo.Construct(value);
                }
            }

            if (typeInfo.ValueRank >= 0)
            {
                return false;
            }
            
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.String:
                {
                    string text = value as string;

                    if (text != null && text.Length >= MaxDisplayTextLength)
                    {
                        return false;
                    }

                    return true;
                }

                case BuiltInType.ByteString:
                case BuiltInType.XmlElement:
                case BuiltInType.QualifiedName:
                case BuiltInType.LocalizedText:
                case BuiltInType.DataValue:
                case BuiltInType.ExtensionObject:
                {
                    return false;
                }
            }

            return true;
        }
        #endregion

        private void NavigationMENU_Click(object sender, EventArgs e)
        {
            try
            {
                EndEdit();

                ToolStripItem item = sender as ToolStripItem;

                if (item != null)
                {
                    // remove all menu items appearing after the selected item.
                    for (int ii = NavigationMENU.Items.Count-1; ii >= 0; ii--)
                    {
                        ToolStripItem target = NavigationMENU.Items[ii];
                        NavigationMENU.Items.Remove(target);

                        if (Object.ReferenceEquals(target, item))
                        {
                            break;
                        }
                    }

                    // show the current value.
                    AccessInfo info = (AccessInfo)item.Tag;
                    ShowValue(info);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ValuesDV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewCell cell in ValuesDV.SelectedCells)
                {
                    DataRowView source = ValuesDV.Rows[cell.RowIndex].DataBoundItem as DataRowView;

                    if (cell.ColumnIndex == 3)
                    {
                        AccessInfo info = (AccessInfo)source.Row[0];
                        ShowValue(info);
                    }
                    
                    break;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ValuesDV_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            try
            {
                if (this.Visible && e.ColumnIndex == 3)
                {
                    DataRowView source = ValuesDV.Rows[e.RowIndex].DataBoundItem as DataRowView;
                    AccessInfo info = (AccessInfo)source.Row[0];

                    if (IsSimpleValue(info))
                    {
                        TypeInfo.Cast(e.FormattedValue, info.TypeInfo.BuiltInType);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
                e.Cancel = true;
            }
        }

        private void ValuesDV_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (this.Visible && e.RowIndex >= 0 && e.ColumnIndex == 3)
                {
                    DataRowView source = ValuesDV.Rows[e.RowIndex].DataBoundItem as DataRowView;
                    AccessInfo info = (AccessInfo)source.Row[0];

                    if (IsSimpleValue(info))
                    {
                        object newValue = TypeInfo.Cast((string)source.Row[3], info.TypeInfo.BuiltInType);
                        info.Value = newValue;
                        UpdateParent(info);
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Recusivesly updates the parent values.
        /// </summary>
        private void UpdateParent(AccessInfo info)
        {
            if (info.Parent == null)
            {
                return;
            }

            object parentValue = info.Parent.Value;

            if (info.Parent.TypeInfo.BuiltInType == BuiltInType.Variant && info.Parent.TypeInfo.ValueRank < 0)
            {
                parentValue = ((Variant)info.Parent.Value).Value;
            }

            if (info.PropertyInfo != null && info.Parent.TypeInfo.ValueRank < 0)
            {
                ExtensionObject extension = parentValue as ExtensionObject;

                if (extension != null)
                {
                    parentValue = extension.Body;
                }

                info.PropertyInfo.SetValue(parentValue, info.Value, null);
            }

            else if (info.Indexes != null)
            {
                int[] indexes = info.Indexes;
                Array array = parentValue as Array;

                Matrix matrix = parentValue as Matrix;

                if (matrix != null)
                {
                    int count = 0;
                    int block = 1;

                    for (int ii = info.Indexes.Length-1; ii >= 0 ; ii--)
                    {
                        count += info.Indexes[ii] * block;
                        block *= matrix.Dimensions[ii];
                    }

                    array = matrix.Elements;
                    indexes = new int[] { count };
                }

                if (array != null)
                {
                    if (info.Parent.TypeInfo.BuiltInType == BuiltInType.Variant && info.Parent.TypeInfo.ValueRank >= 0)
                    {
                        array.SetValue(new Variant(info.Value), indexes);
                    }
                    else
                    {
                        array.SetValue(info.Value, indexes);
                    }
                }
                else
                {
                    IList list = parentValue as IList;

                    if (info.Parent.TypeInfo.BuiltInType == BuiltInType.Variant && info.Parent.TypeInfo.ValueRank >= 0)
                    {
                        list[indexes[0]] = new Variant(info.Value);
                    }
                    else
                    {
                        list[indexes[0]] = info.Value;
                    }
                }
            }

            if (info.Parent != null)
            {
                UpdateParent(info.Parent);
            }
        }
    }
}
