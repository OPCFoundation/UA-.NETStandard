using Opc.Ua;
using Opc.Ua.Client.Controls;
using System;
using System.Reflection;
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
            if (localValue == typeof(bool)) return Convert.ToBoolean(text);
            if (localValue == typeof(sbyte)) return Convert.ToSByte(text);
            if (localValue == typeof(byte)) return Convert.ToByte(text);
            if (localValue == typeof(short)) return Convert.ToInt16(text);
            if (localValue == typeof(ushort)) return Convert.ToUInt16(text);
            if (localValue == typeof(int)) return Convert.ToInt32(text);
            if (localValue == typeof(uint)) return Convert.ToUInt32(text);
            if (localValue == typeof(long)) return Convert.ToInt64(text);
            if (localValue == typeof(ulong)) return Convert.ToUInt64(text);
            if (localValue == typeof(float)) return Convert.ToSingle(text);
            if (localValue == typeof(double)) return Convert.ToDouble(text);
            if (localValue == typeof(string)) return text;
            if (localValue == typeof(DateTime)) return DateTime.ParseExact(text, "yyyy-MM-dd HH:mm:ss.fff", null);
            if (localValue == typeof(Guid)) return new Guid(text);
            if (localValue == typeof(QualifiedName)) return new QualifiedName(text);
            if (localValue == typeof(LocalizedText)) return new LocalizedText(text);

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
