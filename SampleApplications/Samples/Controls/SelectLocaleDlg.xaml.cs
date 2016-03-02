using Opc.Ua.Client;
using Windows.UI.Xaml.Controls;


namespace Opc.Ua.Sample.Controls
{
    #region Constructors
    public sealed partial class SelectLocaleDlg : Page
    {
        public SelectLocaleDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the available areas in a tree view.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns></returns>
        public string ShowDialog(Session session)
        {
            m_session = session;

            LocaleCB.Items.Clear();

            // get the locales from the server.
            DataValue value = m_session.ReadValue(VariableIds.Server_ServerCapabilities_LocaleIdArray);

            if (value != null)
            {
                string[] availableLocales = value.GetValue<string[]>(null);

                if (availableLocales != null)
                {
                    for (int ii = 0; ii < availableLocales.Length; ii++)
                    {
                        LocaleCB.Items.Add(availableLocales[ii]);
                    }
                }
            }

            // select the current locale.
            if (LocaleCB.Items.Count > 0)
            {
                LocaleCB.SelectedIndex = 0;
            }

            // display the dialog.
            if (!Frame.Navigate(typeof(SelectLocaleDlg)))
            {
                return null;
            }

            return LocaleCB.SelectedItem as string;
        }
        #endregion
    }
}


