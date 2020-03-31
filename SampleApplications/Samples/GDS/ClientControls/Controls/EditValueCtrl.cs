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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using System.Reflection;
using System.IO;
using System.Runtime.Serialization;

namespace Opc.Ua.Gds.Client.Controls
{
    /// <summary>
    /// Allows the user to edit a complex value.
    /// </summary>
    public partial class EditValueCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs the object.
        /// </summary>
        public EditValueCtrl()
        {
            InitializeComponent();
            MaxDisplayTextLength = 100;
            ValuesDV.AutoGenerateColumns = false;
            ImageList = new ImageListControl().ImageList;
            
            m_dataset = new DataSet();
            m_dataset.Tables.Add("Values");

            m_dataset.Tables[0].Columns.Add("AccessInfo", typeof(AccessInfo));
            m_dataset.Tables[0].Columns.Add("Name", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataType", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(string));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));

            ValuesDV.DataSource = m_dataset.Tables[0];
        }
        #endregion

        #region Private Fields
        private DataSet m_dataset;
        private AccessInfo m_value;
        private bool m_readOnly;
        private int m_maxDisplayTextLength;
        private event EventHandler m_ValueChanged;
        #endregion

        #region AccessInfo Class
        private class AccessInfo
        {
            public AccessInfo Parent;
            public PropertyInfo PropertyInfo;
            public int[] Indexes;
            public Opc.Ua.TypeInfo TypeInfo;
            public object Value;
            public object WrappedValue;
            public string Name;
        }
        #endregion

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
                return (ButtonPanel.Controls.Count > 1);
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

                AccessInfo info = ButtonPanel.Controls[ButtonPanel.Controls.Count - 1].Tag as AccessInfo;

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
                
                if (ButtonPanel.Controls.Count > 0)
                {
                    AccessInfo info = ButtonPanel.Controls[ButtonPanel.Controls.Count - 1].Tag as AccessInfo;

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
                if (ButtonPanel.Controls.Count > 0)
                {
                    AccessInfo info = ButtonPanel.Controls[ButtonPanel.Controls.Count - 1].Tag as AccessInfo;
                    
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
        /// Moves the displayed value back.
        /// </summary>
        public void Back()
        {
            if (!CanGoBack)
            {
                return;
            }

            NavigationMenu_Click(ButtonPanel.Controls[ButtonPanel.Controls.Count - 2], null);
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

            AccessInfo info = ButtonPanel.Controls[ButtonPanel.Controls.Count - 1].Tag as AccessInfo;

            Opc.Ua.TypeInfo currentType = info.TypeInfo;
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
                        currentType = Opc.Ua.TypeInfo.Construct(currentValue);
                    }
                }
            }

            int[] dimensions = null;

            if (currentValue == null)
            {
                dimensions = new int[0];
            }

            Array array = currentValue as Array;

            if (dimensions == null && array != null)
            {
                dimensions = new int[array.Rank];

                for (int ii = 0; ii < array.Rank; ii++)
                {
                    dimensions[ii] = array.GetLength(ii);
                }
            }

            IList list = currentValue as IList;

            if (dimensions == null && list != null)
            {
                dimensions = new int[1];
                dimensions[0] = list.Count;
            }

            Matrix matrix = currentValue as Matrix;

            if (dimensions == null && matrix != null)
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
            object newValue = null;

            if (result.ArrayDimensions == null || result.ArrayDimensions.Length < 1)
            {
                newValue = Convert(currentValue, currentType, result.TypeInfo, result.UseDefaultOnError);
            }
            else
            {
                if (list != null)
                {
                    Type elementType = GetListElementType(list);

                    for (int ii = result.ArrayDimensions[0]; ii < list.Count; ii++)
                    {
                        list.RemoveAt(ii);
                    }

                    for (int ii = list.Count; ii < result.ArrayDimensions[0]; ii++)
                    {
                        list.Add(CreateInstance(elementType));
                    }

                    newValue = list;
                }

                else
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

                    if (array != null)
                    {
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
                    }

                    newValue = newArray;
                }
            }

            ButtonPanel.Controls.RemoveAt(ButtonPanel.Controls.Count - 1);

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
            
            AccessInfo info = ButtonPanel.Controls[ButtonPanel.Controls.Count - 1].Tag as AccessInfo;

            Opc.Ua.TypeInfo currentType = info.TypeInfo;
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
                        currentType = Opc.Ua.TypeInfo.Construct(currentValue);
                    }
                }
            }

            Opc.Ua.TypeInfo targetType = new Opc.Ua.TypeInfo(builtInType, currentType.ValueRank);
            object newValue  = Convert(currentValue, currentType, targetType, true);

            ButtonPanel.Controls.RemoveAt(ButtonPanel.Controls.Count - 1);

            info.TypeInfo = targetType;
            info.Value = newValue;
            ShowValueNoNotify(info);
            ValuesDV.ClearSelection();
        }

        /// <summary>
        /// Converts the old type to the new type.
        /// </summary>
        private object Convert(object oldValue, Opc.Ua.TypeInfo oldType, Opc.Ua.TypeInfo newType, bool useDefaultOnError)
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

        private object Clone(object value)
        {
            if (value == null)
            {
                return null;
            }

            ICloneable cloneable = value as ICloneable;

            if (cloneable != null)
            {
                return cloneable.Clone();
            }

            foreach (object attribute in value.GetType().GetCustomAttributes(true))
            {
                if (typeof(System.Runtime.Serialization.DataContractAttribute).IsInstanceOfType(attribute))
                {
                    DataContractSerializer serializer = new DataContractSerializer(value.GetType());
                    MemoryStream mstrm = new MemoryStream();
                    serializer.WriteObject(mstrm, value);
                    mstrm.Position = 0;
                    return serializer.ReadObject(mstrm);
                }

                if (typeof(System.Xml.Serialization.XmlRootAttribute).IsInstanceOfType(attribute) || typeof(System.Xml.Serialization.XmlTypeAttribute).IsInstanceOfType(attribute))
                {
                    XmlSerializer serializer = new XmlSerializer(value.GetType());
                    MemoryStream mstrm = new MemoryStream();
                    serializer.Serialize(mstrm, value);
                    mstrm.Position = 0;
                    return serializer.Deserialize(mstrm);
                }
            }

            return Utils.Clone(value);
        }

        public void ShowValue(
            Opc.Ua.TypeInfo expectedType,
            string name,
            object value,
            bool readOnly)
        {
            TextValueTB.ReadOnly = m_readOnly = readOnly;
            ButtonPanel.Visible = true;

            while (ButtonPanel.Controls.Count > 1)
            {
                ButtonPanel.Controls.RemoveAt(ButtonPanel.Controls.Count - 1);
            }

            // assign a type.
            if (expectedType == null)
            {
                if (value == null)
                {
                    expectedType = Opc.Ua.TypeInfo.Scalars.String;
                }
                else
                {
                    expectedType = Opc.Ua.TypeInfo.Construct(value);
                }
            }

            // assign a name.
            if (String.IsNullOrEmpty(name))
            {
                name = expectedType.ToString();

                if (value != null && expectedType.BuiltInType == BuiltInType.ExtensionObject)
                {
                    name = value.GetType().Name;

                    ExtensionObject extension = value as ExtensionObject;

                    if (extension != null && extension.Body != null)
                    {
                        name = extension.Body.GetType().Name;
                    }
                }
            }
            
            AccessInfo info = new AccessInfo();
            info.Value = value;
            info.TypeInfo = expectedType;

            if (value == null && info.TypeInfo.ValueRank < 0)
            {
                info.Value = TypeInfo.GetDefaultValue(info.TypeInfo.BuiltInType);
            }

            // ensure value is the target type.
            info.Value = TypeInfo.Cast(info.Value, expectedType.BuiltInType);

            info.Name = name;
            SetWrappedValue(info);

            m_value = info;

            ShowValue(info);
        }

        public void ShowNothing()
        {
            TextValueTB.ReadOnly = m_readOnly = true;
            ButtonPanel.Visible = false;
            ValuesDV.Visible = false;
            TextValueTB.Visible = true;
            TextValueTB.Text = null;
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
            if (ButtonPanel.Controls.Count < 1)
            {
                return;
            }

            if (!TextValueTB.Visible)
            {
                ValuesDV.EndEdit();
                return;
            }

            AccessInfo info = ButtonPanel.Controls[ButtonPanel.Controls.Count - 1].Tag as AccessInfo;

            object newValue = null;

            if (info.PropertyInfo != null && info.PropertyInfo.PropertyType.IsEnum)
            {
                newValue = Enum.Parse(info.PropertyInfo.PropertyType, TextValueTB.Text);
            }
            else
            {
                newValue = TypeInfo.Cast(TextValueTB.Text, info.TypeInfo.BuiltInType);
            }

            info.Value = newValue;
            UpdateParent(info);
        }

        /// <summary>
        /// Displays the value in the control.
        /// </summary>
        private void ShowValue(AccessInfo parent)
        {
            if (m_readOnly && IsSimpleValue(parent))
            {
                return;
            }

            ShowValueNoNotify(parent);
            ValuesDV.ClearSelection();

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

            Button item = null;

            //foreach (Control control in ButtonPanel.Controls)
            //{
            //    control.BackColor = System.Drawing.Color.MidnightBlue;
            //}

            if (parent.Parent != null || ButtonPanel.Controls.Count == 0)
            {
                item = new Button();
             
                item.AutoSize = true;
                item.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
                item.FlatStyle = FlatStyle.Standard;
                item.BackColor = System.Drawing.Color.MidnightBlue;
                item.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
                item.ForeColor = System.Drawing.Color.White;
                item.Click += new EventHandler(NavigationMenu_Click);
                item.Dock = DockStyle.Left;
                item.Margin = new System.Windows.Forms.Padding(0);
                item.Padding = new System.Windows.Forms.Padding(5);
            }
            else
            {
                item = (Button)ButtonPanel.Controls[0];
            }

            item.Text = parent.Name;
            item.Tag = parent;

            ButtonPanel.Controls.Add(item);
            item.TabIndex = 1000 - ButtonPanel.Controls.Count;

            Opc.Ua.TypeInfo typeInfo = parent.TypeInfo;
            object value = parent.Value;

            // substitute the wrapped value.
            if (parent.WrappedValue != null)
            {
                value = parent.WrappedValue;
            }

            if (value is Variant)
            {
                Variant variant = (Variant)value;
                value = variant.Value;

                if (value != null)
                {
                    parent.TypeInfo = typeInfo = variant.TypeInfo;

                    if (typeInfo == null)
                    {
                        parent.TypeInfo = typeInfo = Opc.Ua.TypeInfo.Construct(value);
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
                    Opc.Ua.TypeInfo elementType = new Opc.Ua.TypeInfo(typeInfo.BuiltInType, ValueRanks.Scalar);

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
                        SetWrappedValue(info);

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
                    parent.Value = value = CreateInstance(parent.Parent.Value.GetType().GetElementType());
                }
                else if (parent.Parent != null && parent.PropertyInfo != null)
                {
                    parent.Value = value = CreateInstance(parent.PropertyInfo.PropertyType);
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

                if (structure == null)
                {
                    return;
                }
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
                ShowTextValue(String.Format("0x{0:X8}", ((StatusCode)structure).Code));
                return;
            }

            ValuesDV.Visible = true;
            TextValueTB.Visible = false;

            // use reflection to display the properties of the structure.
            bool isStructure = false;
            PropertyInfo[] properties = GetProperties(structure.GetType());

            foreach (PropertyInfo property in properties)
            {
                object element = property.GetValue(structure, null);

                string name = property.Name;

                foreach (object attribute in property.GetCustomAttributes(true))
                {
                    System.Runtime.Serialization.DataMemberAttribute dma = attribute as System.Runtime.Serialization.DataMemberAttribute;

                    if (dma != null && !String.IsNullOrEmpty(dma.Name))
                    {
                        name = dma.Name;
                        break;
                    }

                    System.Xml.Serialization.XmlElementAttribute xea = attribute as System.Xml.Serialization.XmlElementAttribute;

                    if (xea != null && !String.IsNullOrEmpty(xea.ElementName))
                    {
                        name = xea.ElementName;
                        break;
                    }

                    System.Xml.Serialization.XmlAttributeAttribute xaa = attribute as System.Xml.Serialization.XmlAttributeAttribute;

                    if (xaa != null && !String.IsNullOrEmpty(xaa.AttributeName))
                    {
                        name = xaa.AttributeName;
                        break;
                    }
                }

                AccessInfo info = new AccessInfo();
                info.Parent = parent;
                info.PropertyInfo = property;
                info.TypeInfo = Opc.Ua.TypeInfo.Construct(property.PropertyType);
                info.Value = element;
                info.Name = name;
                SetWrappedValue(info);

                ShowNamedValue(info);
                isStructure = true;
            }

            if (!isStructure)
            {
                ShowTextValue(parent.Value, parent.TypeInfo);
            }
        }

        private PropertyInfo[] GetProperties(Type type)
        {
            PropertyInfo[] properties = null;

            List<PropertyInfo> list = new List<PropertyInfo>();

            Type supertype = type;

            while (supertype != null)
            {
                properties = supertype.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                list.InsertRange(0, SortProperties(properties));
                supertype = supertype.BaseType;
            }

            return list.ToArray();
        }

        private PropertyInfo[] SortProperties(PropertyInfo[] properties)
        {
            List<PropertyInfo> list = new List<PropertyInfo>();
            List<int> ordinals = new List<int>();

            foreach (PropertyInfo property in properties)
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                foreach (object attribute in property.GetCustomAttributes(true))
                {
                    System.Runtime.Serialization.DataMemberAttribute dma = attribute as System.Runtime.Serialization.DataMemberAttribute;

                    if (dma != null)
                    {
                        for (int ii = 0; ii < ordinals.Count; ii++)
                        {
                            if (ordinals[ii] > dma.Order)
                            {
                                list.Insert(ii, property);
                                ordinals.Insert(ii, dma.Order);
                                break;
                            }
                        }

                        list.Add(property);
                        ordinals.Add(dma.Order);
                        break;
                    }

                    System.Xml.Serialization.XmlElementAttribute xea = attribute as System.Xml.Serialization.XmlElementAttribute;

                    if (xea != null)
                    {
                        for (int ii = 0; ii < ordinals.Count; ii++)
                        {
                            if (ordinals[ii] > xea.Order)
                            {
                                list.Insert(ii, property);
                                ordinals.Insert(ii, xea.Order);
                                break;
                            }
                        }

                        list.Add(property);
                        ordinals.Add(xea.Order);
                        break;
                    }

                    System.Xml.Serialization.XmlAttributeAttribute xaa = attribute as System.Xml.Serialization.XmlAttributeAttribute;

                    if (xaa != null)
                    {
                        for (int ii = 0; ii < ordinals.Count; ii++)
                        {
                            if (ordinals[ii] > 0)
                            {
                                list.Insert(ii, property);
                                ordinals.Insert(ii, 0);
                                break;
                            }
                        }

                        list.Add(property);
                        ordinals.Add(0);
                        break;
                    }
                }
            }

            return list.ToArray();
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
            row[3] = ValueToString(info.Value, info.WrappedValue, info.TypeInfo);
            row[4] = ImageList.Images[ImageIndex.Get(Attributes.Value, info.Value)];

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

            if (accessInfo.TypeInfo.BuiltInType == BuiltInType.Enumeration)
            {
                if (accessInfo.Value != null)
                {
                    return accessInfo.Value.GetType();
                }

                if (accessInfo.PropertyInfo != null)
                {
                    return accessInfo.PropertyInfo.PropertyType;
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
        /// Sets a wrapper that controls now a value is displayed.
        /// </summary>
        private void SetWrappedValue(AccessInfo info)
        {
            if (!m_readOnly)
            {
                return;
            }

            if (info.TypeInfo.BuiltInType == BuiltInType.ExtensionObject && info.TypeInfo.ValueRank == ValueRanks.Scalar)
            {
                if (info.Name != null && info.Name.Contains("Certificate"))
                {
                    info.WrappedValue = info.Value;
                }
            }
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
            row[3] = ValueToString(info.Value, info.WrappedValue, info.TypeInfo);
            row[4] = ImageList.Images[ImageIndex.Get(Attributes.Value, info.Value)];

            m_dataset.Tables[0].Rows.Add(row);
        }

        /// <summary>
        /// Displays a value in the control.
        /// </summary>
        private void ShowTextValue(object value, Opc.Ua.TypeInfo typeInfo)
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
                    ShowTextValue(ValueToString(value, null, typeInfo));
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
        private string ValueToString(object value, object wrappedValue, Opc.Ua.TypeInfo typeInfo)
        {
            if (value == null)
            {
                return "<null>";
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
                        typeInfo = Opc.Ua.TypeInfo.Construct(value);
                    }
                }
            }

            if (typeInfo.ValueRank >= 0)
            {
                return "<double click to see array>";
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
                    if (wrappedValue != null)
                    {
                        string text = "<double click to see structure>";

                        if (text != null && text.Length > MaxDisplayTextLength)
                        {
                            return text.Substring(0, MaxDisplayTextLength) + "...";
                        }

                        return text;
                    }

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
                    return value.ToString();
                }

                case BuiltInType.ExtensionObject:
                {
                    return "<double click to see structure>";
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

            Opc.Ua.TypeInfo typeInfo = info.TypeInfo;
            object value = info.Value;

            if (value is Variant)
            {
                Variant variant = (Variant)info.Value;
                typeInfo = variant.TypeInfo;
                value = variant.Value;

                if (typeInfo == null)
                {
                    typeInfo = Opc.Ua.TypeInfo.Construct(value);
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
                case BuiltInType.LocalizedText:
                case BuiltInType.DataValue:
                case BuiltInType.ExtensionObject:
                {
                    return false;
                }
            }

            return true;
        }

        private object CreateInstance(Type type)
        {
            if (typeof(string).Equals(type))
            {
                return String.Empty;
            }

            return Activator.CreateInstance(type);
        }

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
                Variant? variant = parentValue as Variant?;

                if (variant != null)
                {
                    parentValue = variant.Value.Value;
                }

                ExtensionObject extension = parentValue as ExtensionObject;

                if (extension != null)
                {
                    parentValue = extension.Body;
                }

                if (info.PropertyInfo.CanWrite && info.PropertyInfo.PropertyType.IsInstanceOfType(info.Value))
                {
                    info.PropertyInfo.SetValue(parentValue, info.Value, null);
                }
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

                    for (int ii = info.Indexes.Length - 1; ii >= 0; ii--)
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
                        array.SetValue(new Variant(info.Value, null), indexes);
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
                        list[indexes[0]] = new Variant(info.Value, null);
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
        #endregion

        #region Event Handlers
        private void NavigationMenu_Click(object sender, EventArgs e)
        {
            try
            {
                EndEdit();

                Button item = sender as Button;

                if (item != null)
                {
                    // remove all menu items appearing after the selected item.
                    for (int ii = ButtonPanel.Controls.Count - 1; ii >= 0; ii--)
                    {
                        Button target = ButtonPanel.Controls[ii] as Button;
                        ButtonPanel.Controls.RemoveAt(ii);

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
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void ValuesDV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewRow row in ValuesDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    AccessInfo info = (AccessInfo)source.Row[0];
                    ShowValue(info);
                    break;
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
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
                        if (info.TypeInfo.BuiltInType == BuiltInType.Enumeration && info.Value.GetType().IsEnum)
                        {
                            Enum.Parse(info.Value.GetType(), (string)e.FormattedValue);
                        }
                        else
                        {
                            TypeInfo.Cast(e.FormattedValue, info.TypeInfo.BuiltInType);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
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
                        object newValue = null;

                        if (info.TypeInfo.BuiltInType == BuiltInType.Enumeration && info.Value.GetType().IsEnum)
                        {
                            newValue = Enum.Parse(info.Value.GetType(), (string)source.Row[3]);
                        }
                        else
                        {
                            newValue = TypeInfo.Cast((string)source.Row[3], info.TypeInfo.BuiltInType);
                        }

                        info.Value = newValue;
                        UpdateParent(info);
                    }
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void TextValueTB_VisibleChanged(object sender, EventArgs e)
        {
            if (this.Visible)
            {
                foreach (DataGridViewRow row in ValuesDV.Rows)
                {
                    row.Selected = false;
                }
            }
        }
        #endregion
    }
}
