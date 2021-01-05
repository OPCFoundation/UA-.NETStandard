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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A class that provide various common utility functions and shared resources.
    /// </summary>
    public partial class GuiUtils : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GuiUtils"/> class.
        /// </summary>
        public GuiUtils()
        {
            InitializeComponent();
        }

        /// <summary>
        /// The list of icon images.
        /// </summary>
        public System.Windows.Forms.ImageList ImageList;

        /// <summary>
        /// Displays the details of an exception.
        /// </summary>
        public static void HandleException(string caption, MethodBase method, Exception e)
        {
            if (String.IsNullOrEmpty(caption))
            {
                caption = method.Name;
            }

            ExceptionDlg.Show(caption, e);
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
        /// Uses the command line to override the UA TCP implementation specified in the configuration.
        /// </summary>
        /// <param name="configuration">The configuration instance that stores the configurable information for a UA application.
        /// </param>
        public static void OverrideUaTcpImplementation(ApplicationConfiguration configuration)
        {
            // check if UA TCP configuration included.
            TransportConfiguration transport = null;

            for (int ii = 0; ii < configuration.TransportConfigurations.Count; ii++)
            {
                if (configuration.TransportConfigurations[ii].UriScheme == Utils.UriSchemeOpcTcp)
                {
                    transport = configuration.TransportConfigurations[ii];
                    break;
                }
            }
        }

        /// <summary>
        /// Displays the UA-TCP configuration in the form.
        /// </summary>
        /// <param name="form">The form to display the UA-TCP configuration.</param>
        /// <param name="configuration">The configuration instance that stores the configurable information for a UA application.</param>
        public static void DisplayUaTcpImplementation(Form form, ApplicationConfiguration configuration)
        {
            // check if UA TCP configuration included.
            TransportConfiguration transport = null;

            for (int ii = 0; ii < configuration.TransportConfigurations.Count; ii++)
            {
                if (configuration.TransportConfigurations[ii].UriScheme == Utils.UriSchemeOpcTcp)
                {
                    transport = configuration.TransportConfigurations[ii];
                    break;
                }
            }

            // check if UA TCP implementation explicitly specified.
            if (transport != null)
            {
                string text = form.Text;

                int index = text.LastIndexOf("(UA TCP - ");

                if (index >= 0)
                {
                    text = text.Substring(0, index);
                }

                form.Text = Utils.Format("{0} (UA TCP - C#)", text);
            }
        }

        /// <summary>
        /// Handles a domain validation error.
        /// </summary>
        /// <param name="caption">The caller's text is used as the caption of the <see cref="MessageBox"/> shown to provide details about the error.</param>
        public static bool HandleDomainCheckError(string caption, ServiceResult serviceResult, X509Certificate2 certificate = null)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.AppendFormat("Certificate could not be validated!\r\n");
            buffer.AppendFormat("Validation error(s): \r\n");
            buffer.AppendFormat("\t{0}\r\n", serviceResult.StatusCode);
            if (certificate != null)
            {
                buffer.AppendFormat("\r\nSubject: {0}\r\n", certificate.Subject);
                buffer.AppendFormat("Issuer: {0}\r\n", X509Utils.CompareDistinguishedName(certificate.Subject, certificate.Issuer)
                    ? "Self-signed" : certificate.Issuer);
                buffer.AppendFormat("Valid From: {0}\r\n", certificate.NotBefore);
                buffer.AppendFormat("Valid To: {0}\r\n", certificate.NotAfter);
                buffer.AppendFormat("Thumbprint: {0}\r\n\r\n", certificate.Thumbprint);
                var domains = X509Utils.GetDomainsFromCertficate(certificate);
                if (domains.Count > 0)
                {
                    bool comma = false;
                    buffer.AppendFormat("Domains:");
                    foreach (var domain in domains)
                    {
                        if (comma)
                        {
                            buffer.Append(",");
                        }
                        buffer.AppendFormat(" {0}", domain);
                        comma = true;
                    }
                    buffer.AppendLine();
                }
            }
            buffer.Append("This certificate validation error indicates that the hostname used to connect");
            buffer.Append(" is not listed as a valid hostname in the server certificate.");
            buffer.Append("\r\n\r\nIgnore error and disable the hostname verification?");

            if (MessageBox.Show(buffer.ToString(), caption, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        /// <param name="form">The caller's form is used as the caption of the <see cref="MessageBox"/> shown to provide details about the error.</param>
        /// <param name="validator">The validator (not used).</param>
        /// <param name="e">The <see cref="Opc.Ua.CertificateValidationEventArgs"/> instance event arguments provided when a certificate validation error occurs.</param>
        public static void HandleCertificateValidationError(Form form, CertificateValidator validator, CertificateValidationEventArgs e)
        {
            HandleCertificateValidationError(form.Text, validator, e);
        }

        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        /// <param name="caption">The caller's text is used as the caption of the <see cref="MessageBox"/> shown to provide details about the error.</param>
        /// <param name="validator">The validator (not used).</param>
        /// <param name="e">The <see cref="Opc.Ua.CertificateValidationEventArgs"/> instance event arguments provided when a certificate validation error occurs.</param>
        public static void HandleCertificateValidationError(string caption, CertificateValidator validator, CertificateValidationEventArgs e)
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append("Certificate could not be validated!\r\n");
            buffer.Append("Validation error(s): \r\n");
            ServiceResult error = e.Error;
            while (error != null)
            {
                buffer.AppendFormat("- {0}\r\n", error.ToString().Split('\r', '\n').FirstOrDefault());
                error = error.InnerResult;
            }
            buffer.AppendFormat("\r\nSubject: {0}\r\n", e.Certificate.Subject);
            buffer.AppendFormat("Issuer: {0}\r\n", (e.Certificate.Subject == e.Certificate.Issuer) ? "Self-signed" : e.Certificate.Issuer);
            buffer.AppendFormat("Valid From: {0}\r\n", e.Certificate.NotBefore);
            buffer.AppendFormat("Valid To: {0}\r\n", e.Certificate.NotAfter);
            buffer.AppendFormat("Thumbprint: {0}\r\n\r\n", e.Certificate.Thumbprint);
            buffer.Append("Certificate validation errors may indicate an attempt to intercept any data you send ");
            buffer.Append("to a server or to allow an untrusted client to connect to your server.");
            buffer.Append("\r\n\r\nAccept anyway?");

            if (MessageBox.Show(buffer.ToString(), caption, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                e.AcceptAll = true;
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

                if (type == typeof(System.Xml.XmlElement))
                {
                    System.Xml.XmlDocument document = new System.Xml.XmlDocument();
                    document.InnerXml = "<Null/>";
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

            if (valueRank >= 0)
            {
                return new ComplexValueEditDlg().ShowDialog(value);
            }

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
                {
                    return new NumericValueEditDlg().ShowDialog(value, TypeInfo.GetSystemType(builtinType, valueRank));
                }

                case BuiltInType.Number:
                {
                    return new NumericValueEditDlg().ShowDialog(value, TypeInfo.GetSystemType(BuiltInType.Double, valueRank));
                }

                case BuiltInType.Integer:
                {
                    return new NumericValueEditDlg().ShowDialog(value, TypeInfo.GetSystemType(BuiltInType.Int64, valueRank));
                }

                case BuiltInType.UInteger:
                {
                    return new NumericValueEditDlg().ShowDialog(value, TypeInfo.GetSystemType(BuiltInType.UInt64, valueRank));
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

                    if (new DateTimeValueEditDlg().ShowDialog(ref datetime))
                    {
                        return datetime;
                    }

                    return null;
                }

                case BuiltInType.QualifiedName:
                {
                    QualifiedName qname = (QualifiedName)value;

                    string name = new StringValueEditDlg().ShowDialog(qname.Name);

                    if (name != null)
                    {
                        return new QualifiedName(name, qname.NamespaceIndex);
                    }

                    return null;
                }

                case BuiltInType.String:
                {
                    return new StringValueEditDlg().ShowDialog((string)value);
                }

                case BuiltInType.LocalizedText:
                {
                    LocalizedText ltext = (LocalizedText)value;

                    string text = new StringValueEditDlg().ShowDialog(ltext.Text);

                    if (text != null)
                    {
                        return new LocalizedText(ltext.Locale, text);
                    }

                    return null;
                }
            }

            return new ComplexValueEditDlg().ShowDialog(value);
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
