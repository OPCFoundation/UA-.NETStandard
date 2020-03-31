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
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

// The User Control item template is documented at http://go.microsoft.com/fwlink/?LinkId=234236

namespace Opc.Ua.Client.Controls
{
    public sealed partial class SimpleValueEditCtrl : UserControl
    {
        public object localValue;

        public SimpleValueEditCtrl(object value)
        {
            this.InitializeComponent();
            this.textBox.Text = value.ToString();
        }

        public static bool IsSimpleType(Type type)
        {
            if (type == typeof(bool)) return true;
            if (type == typeof(sbyte)) return true;
            if (type == typeof(byte)) return true;
            if (type == typeof(short)) return true;
            if (type == typeof(ushort)) return true;
            if (type == typeof(int)) return true;
            if (type == typeof(uint)) return true;
            if (type == typeof(long)) return true;
            if (type == typeof(ulong)) return true;
            if (type == typeof(float)) return true;
            if (type == typeof(double)) return true;
            if (type == typeof(string)) return true;
            if (type == typeof(DateTime)) return true;
            if (type == typeof(Guid)) return true;

            return false;
        }

        private object Parse(string text)
        {
            Type localValueType = localValue.GetType();
            if (localValueType == typeof(bool)) return Convert.ToBoolean(text);
            if (localValueType == typeof(sbyte)) return Convert.ToSByte(text);
            if (localValueType == typeof(byte)) return Convert.ToByte(text);
            if (localValueType == typeof(short)) return Convert.ToInt16(text);
            if (localValueType == typeof(ushort)) return Convert.ToUInt16(text);
            if (localValueType == typeof(int)) return Convert.ToInt32(text);
            if (localValueType == typeof(uint)) return Convert.ToUInt32(text);
            if (localValueType == typeof(long)) return Convert.ToInt64(text);
            if (localValueType == typeof(ulong)) return Convert.ToUInt64(text);
            if (localValueType == typeof(float)) return Convert.ToSingle(text);
            if (localValueType == typeof(double)) return Convert.ToDouble(text);
            if (localValueType == typeof(string)) return text;
            if (localValueType == typeof(DateTime)) return DateTime.ParseExact(text, "yyyy-MM-dd HH:mm:ss.fff", null);
            if (localValueType == typeof(Guid)) return new Guid(text);
            if (localValueType == typeof(QualifiedName)) return new QualifiedName(text);
            if (localValueType == typeof(LocalizedText)) return new LocalizedText(text);

            throw new ServiceResultException(StatusCodes.BadUnexpectedError, "Cannot convert type.");
        }

        private void button_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            Popup p = this.Parent as Popup;
            p.IsOpen = false;
            
            try
            {
                localValue = Parse(this.textBox.Text);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.textBox.Text, GuiUtils.CallerName(), exception);
            }
        }
    }
}
