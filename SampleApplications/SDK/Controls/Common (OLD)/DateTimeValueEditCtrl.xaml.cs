using System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

// The User Control item template is documented at http://go.microsoft.com/fwlink/?LinkId=234236

namespace Opc.Ua.Client.Controls
{
    public sealed partial class DateTimeValueEditCtrl : UserControl
    {
        public DateTime localValue;

        public DateTimeValueEditCtrl(DateTime value)
        {
            this.InitializeComponent();
            this.Date.Date = value.Date;
            this.Time.Time = value.TimeOfDay;
        }

        private void button_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            Popup p = this.Parent as Popup;
            p.IsOpen = false;
            localValue = new DateTime(Date.Date.Year, Date.Date.Month, Date.Date.Day, Time.Time.Hours, Time.Time.Minutes, Time.Time.Seconds);
        }
    }
}
