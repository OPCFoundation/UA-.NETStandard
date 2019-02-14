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
using System.Text;
using System.Security.Cryptography.X509Certificates;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using System.Runtime.CompilerServices;
using Windows.Storage;
using System.Threading.Tasks;
using System.IO;
using System.Xml;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A class that provide various common utility functions and shared resources.
    /// </summary>
    public partial class GuiUtils
    {
        public static string CallerName([CallerMemberName]string caller = "")
        {
            return caller;
        }
        
        /// <summary>
         /// The list of icon images.
         /// </summary>
        public List<Image> ImageList;

        /// <summary>
        /// Displays the details of an exception.
        /// </summary>
        public delegate void ExceptionMessageDlgEventHandler(string message);
        public static event ExceptionMessageDlgEventHandler ExceptionMessageDlg;
        public static void HandleException(string caption, string callerName, Exception e)
        {
            if (String.IsNullOrEmpty(caption))
            {
                caption = callerName;
            }
            Utils.Trace("HandleException:{0}:{1}:{2}", caption, callerName, e.Message);
            if (ExceptionMessageDlg != null)
            {
                StringBuilder buffer = new StringBuilder();
                buffer.AppendFormat("HandleException: {0}\r\n\r\n", caption);
                buffer.AppendFormat("Caller: {0}\r\n", callerName);
                buffer.AppendFormat("Exception: {0}\r\n", e.Message);
                ExceptionMessageDlg(buffer.ToString());
            }
        }

        /// <summary>
        /// Defines names for the available 16x16 icons.
        /// </summary>
        public static class Icons
        {
            /// <summary>
            /// An attribute
            /// </summary>
            public const string Attribute = "SimpleItem";

            /// <summary>
            /// A property
            /// </summary>
            public const string Property = "Property";

            /// <summary>
            /// A variable
            /// </summary>
            public const string Variable = "Variable";

            /// <summary>
            /// An object
            /// </summary>
            public const string Object = "Object";

            /// <summary>
            /// A method
            /// </summary>
            public const string Method = "Method";

            /// <summary>
            /// A single computer.
            /// </summary>
            public const string Computer = "Computer";

            /// <summary>
            /// A computer network.
            /// </summary>
            public const string Network = "Network";

            /// <summary>
            /// A folder.
            /// </summary>
            public const string Folder = "Folder";

            /// <summary>
            /// A selected folder.
            /// </summary>
            public const string SelectedFolder = "SelectedFolder";

            /// <summary>
            /// A process or application.
            /// </summary>
            public const string Process = "Process";

            /// <summary>
            /// A certificate
            /// </summary>
            public const string Certificate = "Certificate";

            /// <summary>
            /// An invalid certificate
            /// </summary>
            public const string InvalidCertificate = "InvalidCertificate";

            /// <summary>
            /// A certificate store
            /// </summary>
            public const string CertificateStore = "CertificateStore";

            /// <summary>
            /// A group of users.
            /// </summary>
            public const string Users = "Users";

            /// <summary>
            /// A service.
            /// </summary>
            public const string Service = "Service";

            /// <summary>
            /// A logical drive.
            /// </summary>
            public const string Drive = "Drive";

            /// <summary>
            /// The computer desktop.
            /// </summary>
            public const string Desktop = "Desktop";

            /// <summary>
            /// A single user.
            /// </summary>
            public const string SingleUser = "SingleUser";

            /// <summary>
            /// A group of services.
            /// </summary>
            public const string ServiceGroup = "ServiceGroup";

            /// <summary>
            /// A group of users.
            /// </summary>
            public const string UserGroup = "UserGroup";

            /// <summary>
            /// A green check
            /// </summary>
            public const string GreenCheck = "GreenCheck";

            /// <summary>
            /// A red cross
            /// </summary>
            public const string RedCross = "RedCross";

            /// <summary>
            /// A users icon with a red cross through it.
            /// </summary>
            public const string UsersRedCross = "UsersRedCross";
        }

        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        /// <param name="caller">The caller's text is used as the caption of the <see cref="MessageBox"/> shown to provide details about the error.</param>
        /// <param name="validator">The validator (not used).</param>
        /// <param name="e">The <see cref="Opc.Ua.CertificateValidationEventArgs"/> instance event arguments provided when a certificate validation error occurs.</param>
        public static async Task HandleCertificateValidationError(Page caller, CertificateValidator validator, CertificateValidationEventArgs e)
        {
            StringBuilder buffer = new StringBuilder();

            buffer.AppendFormat("Certificate could not be validated: {0}\r\n\r\n", e.Error.StatusCode);
            buffer.AppendFormat("Subject: {0}\r\n", e.Certificate.Subject);
            buffer.AppendFormat("Issuer: {0}\r\n", (e.Certificate.Subject == e.Certificate.Issuer) ? "Self-signed" : e.Certificate.Issuer);
            buffer.AppendFormat("Thumbprint: {0}\r\n\r\n", e.Certificate.Thumbprint);
            buffer.AppendFormat("The security certificate was not issued by a trusted certificate authority.\r\n");
            buffer.AppendFormat("Security certificate problems may indicate an attempt to intercept any data you send\r\n");
            buffer.AppendFormat("to a server or to allow an untrusted client to connect to your server.\r\n");
            buffer.AppendFormat("\r\nAccept anyway?");
            MessageDlg dialog = new MessageDlg(buffer.ToString(), MessageDlgButton.Yes, MessageDlgButton.No);
            MessageDlgButton result = await dialog.ShowAsync();
            if (result == MessageDlgButton.Yes)
            {
                e.Accept = true;
            }
        }
                   
        /// <summary>
        /// Returns a default value for the data type.
        /// </summary>
        public static object GetDefaultValue(NodeId datatypeId, int valueRank)
        {
            Type type = TypeInfo.GetSystemType(datatypeId, EncodeableFactory.GlobalFactory);

            if (type == null)
            {
                return null;
            }

            if (valueRank < 0)
            {
                if (type == typeof(String))
                {
                    return System.String.Empty;
                }
                
                if (type == typeof(byte[]))
                {
                    return new byte[0];
                }

                if (type == typeof(NodeId))
                {
                    return Opc.Ua.NodeId.Null;
                }

                if (type == typeof(ExpandedNodeId))
                {
                    return Opc.Ua.ExpandedNodeId.Null;
                }

                if (type == typeof(QualifiedName))
                {
                    return Opc.Ua.QualifiedName.Null;
                }

                if (type == typeof(LocalizedText))
                {
                    return Opc.Ua.LocalizedText.Null;
                }

                if (type == typeof(Guid))
                {
                    return System.Guid.Empty;
                }

                if (type == typeof(XmlElement)) 
                {
                    XmlDocument document = new XmlDocument();
                    document.LoadXml("<Null/>");
                    return document.DocumentElement;
                }

                return Activator.CreateInstance(type);
            }

            return Array.CreateInstance(type, new int[valueRank]);
        }

        /// <summary>
        /// Displays a dialog that allows a use to edit a value.
        /// </summary>
        public static object EditValue(Session session, object value)
        {
            TypeInfo typeInfo = TypeInfo.Construct(value);

            if (typeInfo != null)
            {
                return EditValue(session, value, (uint)typeInfo.BuiltInType, typeInfo.ValueRank);
            }

            return null;
        }

        /// <summary>
        /// Displays a dialog that allows a use to edit a value.
        /// </summary>
        public static object EditValue(Session session, object value, NodeId datatypeId, int valueRank)
        {
            if (value == null)
            {
                value = GetDefaultValue(datatypeId, valueRank);
            }

            Popup myPopup = new Popup();

            BuiltInType builtinType = TypeInfo.GetBuiltInType(datatypeId, session.TypeTree);

            switch (builtinType)
            {
                case BuiltInType.Boolean:
                case BuiltInType.Byte:
                case BuiltInType.SByte:
                case BuiltInType.Int16:
                case BuiltInType.UInt16:
                case BuiltInType.Int32:
                case BuiltInType.UInt32:
                case BuiltInType.Int64:
                case BuiltInType.UInt64:
                case BuiltInType.Float:
                case BuiltInType.Double:
                case BuiltInType.Enumeration:
                case BuiltInType.String:
                {
                    
                    myPopup.Child = new SimpleValueEditCtrl(value);
                    myPopup.IsOpen = true;
                    value = ((SimpleValueEditCtrl)myPopup.Child).localValue;
                    break;
                }

                case BuiltInType.NodeId:
                {
                    return new NodeIdValueEditDlg().ShowDialog(session, (NodeId)value);
                }

                case BuiltInType.ExpandedNodeId:
                {
                    return new NodeIdValueEditDlg().ShowDialog(session, (ExpandedNodeId)value);
                }

                case BuiltInType.DateTime:
                {
                    DateTime datetime = (DateTime)value;

                    myPopup.Child = new DateTimeValueEditCtrl(datetime);
                    myPopup.IsOpen = true;
                    value = ((DateTimeValueEditCtrl)myPopup.Child).localValue;
                    break;
                }

                case BuiltInType.QualifiedName:
                {
                    QualifiedName qname = (QualifiedName)value;

                    myPopup.Child = new SimpleValueEditCtrl(qname.Name);
                    myPopup.IsOpen = true;
                    string name = ((SimpleValueEditCtrl)myPopup.Child).localValue.ToString();
                    if (name != null)
                    {
                        return new QualifiedName(name, qname.NamespaceIndex);
                    }

                    return null;
                }
                    
                case BuiltInType.LocalizedText:
                {
                    LocalizedText ltext = (LocalizedText)value;

                    myPopup.Child = new SimpleValueEditCtrl(ltext.Text);
                    myPopup.IsOpen = true;
                    string text = ((SimpleValueEditCtrl)myPopup.Child).localValue.ToString();
                    if (text != null)
                    {
                        return new LocalizedText(text, ltext.Locale);
                    }

                    return null;
                }
            }

            return value;
        }
        
        /// <summary>
        /// Returns to display icon for the target of a reference.
        /// </summary>
        public static string GetTargetIcon(Session session, ReferenceDescription reference)
        {
            return GetTargetIcon(session, reference.NodeClass, reference.TypeDefinition);
        }

        /// <summary>
        /// Returns to display icon for the target of a reference.
        /// </summary>
        public static string GetTargetIcon(Session session, NodeClass nodeClass, ExpandedNodeId typeDefinitionId)
        { 
            // make sure the type definition is in the cache.
            INode typeDefinition = session.NodeCache.Find(typeDefinitionId);

            switch (nodeClass)
            {
                case NodeClass.Object:
                {                    
                    if (session.TypeTree.IsTypeOf(typeDefinitionId, ObjectTypes.FolderType))
                    {
                        return "Folder";
                    }

                    return "Object";
                }
                    
                case NodeClass.Variable:
                {                    
                    if (session.TypeTree.IsTypeOf(typeDefinitionId, VariableTypes.PropertyType))
                    {
                        return "Property";
                    }

                    return "Variable";
                }                   
            }

            return nodeClass.ToString();
        }

#region Private Methods
#endregion
    }
}
