using Opc.Ua;
using Opc.Ua.Client;
using System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class NodeIdValueEditDlg : Page
    {
        public NodeIdValueEditDlg()
        {
            this.InitializeComponent();
        }
        public NodeId ShowDialog(Session session, NodeId value)
        {
            if (session == null) throw new ArgumentNullException("session");

            ValueCTRL.Browser = new Browser(session);
            ValueCTRL.RootId = Objects.RootFolder;
            ValueCTRL.Identifier = value;

            Popup myPopup = new Popup();
            myPopup.Child = this;
            myPopup.IsOpen = true;

            return ValueCTRL.Identifier;
        }

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public ExpandedNodeId ShowDialog(Session session, ExpandedNodeId value)
        {
            if (session == null) throw new ArgumentNullException("session");

            ValueCTRL.Browser = new Browser(session);
            ValueCTRL.RootId = Objects.RootFolder;
            ValueCTRL.Identifier = ExpandedNodeId.ToNodeId(value, session.NamespaceUris);

            Popup myPopup = new Popup();
            myPopup.Child = this;
            myPopup.IsOpen = true;

            return ValueCTRL.Identifier;
        }
    }
}
