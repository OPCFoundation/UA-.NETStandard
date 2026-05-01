/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;

namespace Opc.Ua.Security
{
    /// <summary>
    /// Static encode/decode helpers for all DataContract types in SecuredApplication.cs.
    /// Uses XmlEncoder/XmlParser (OPC UA encoders) instead of DataContractSerializer.
    /// XML format is semantically compatible with the existing DataContractSerializer format.
    /// </summary>
    internal static class SecuredApplicationEncoding
    {
        /// <summary>
        /// Encodes the contents of a <see cref="SecuredApplication"/> into the encoder.
        /// The caller must have already pushed the root element.
        /// </summary>
        internal static void EncodeContents(
            XmlEncoder encoder,
            SecuredApplication app)
        {
            // Order 0: ApplicationName
            if (app.ApplicationName != null)
            {
                encoder.WriteString("ApplicationName", app.ApplicationName);
            }

            // Order 1: ApplicationUri
            if (app.ApplicationUri != null)
            {
                encoder.WriteString("ApplicationUri", app.ApplicationUri);
            }

            // Order 2: ApplicationType (enum written as "Name_Value" string)
            encoder.WriteString("ApplicationType", app.ApplicationType.ToString());

            // Order 3: ProductName (EmitDefaultValue=false)
            if (app.ProductName != null)
            {
                encoder.WriteString("ProductName", app.ProductName);
            }

            // Order 4: ConfigurationMode (EmitDefaultValue=false)
            if (app.ConfigurationMode != null)
            {
                encoder.WriteString("ConfigurationMode", app.ConfigurationMode);
            }

            // Order 5: LastExportTime
            encoder.WriteDateTime("LastExportTime", new DateTimeUtc(app.LastExportTime));

            // Order 6: ConfigurationFile (EmitDefaultValue=false)
            if (app.ConfigurationFile != null)
            {
                encoder.WriteString("ConfigurationFile", app.ConfigurationFile);
            }

            // Order 7: ExecutableFile (EmitDefaultValue=false)
            if (app.ExecutableFile != null)
            {
                encoder.WriteString("ExecutableFile", app.ExecutableFile);
            }

            // Order 8: ApplicationCertificate (EmitDefaultValue=false)
            if (app.ApplicationCertificate != null)
            {
                encoder.Push("ApplicationCertificate", Namespaces.OpcUaSecurity);
                EncodeCertificateIdentifier(encoder, app.ApplicationCertificate);
                encoder.Pop();
            }

            // Order 9: TrustedCertificateStore (EmitDefaultValue=false)
            if (app.TrustedCertificateStore != null)
            {
                encoder.Push("TrustedCertificateStore", Namespaces.OpcUaSecurity);
                EncodeCertificateStoreIdentifier(encoder, app.TrustedCertificateStore);
                encoder.Pop();
            }

            // Order 10: TrustedCertificates (EmitDefaultValue=false)
            if (app.TrustedCertificates != null)
            {
                encoder.Push("TrustedCertificates", Namespaces.OpcUaSecurity);
                EncodeCertificateList(encoder, app.TrustedCertificates);
                encoder.Pop();
            }

            // Order 11: IssuerCertificateStore (EmitDefaultValue=false)
            if (app.IssuerCertificateStore != null)
            {
                encoder.Push("IssuerCertificateStore", Namespaces.OpcUaSecurity);
                EncodeCertificateStoreIdentifier(encoder, app.IssuerCertificateStore);
                encoder.Pop();
            }

            // Order 12: IssuerCertificates (EmitDefaultValue=false)
            if (app.IssuerCertificates != null)
            {
                encoder.Push("IssuerCertificates", Namespaces.OpcUaSecurity);
                EncodeCertificateList(encoder, app.IssuerCertificates);
                encoder.Pop();
            }

            // Order 13: RejectedCertificatesStore (EmitDefaultValue=false)
            if (app.RejectedCertificatesStore != null)
            {
                encoder.Push("RejectedCertificatesStore", Namespaces.OpcUaSecurity);
                EncodeCertificateStoreIdentifier(encoder, app.RejectedCertificatesStore);
                encoder.Pop();
            }

            // Order 14: BaseAddresses (EmitDefaultValue=false)
            if (app.BaseAddresses != null && app.BaseAddresses.Count > 0)
            {
                encoder.Push("BaseAddresses", Namespaces.OpcUaSecurity);
                foreach (string address in app.BaseAddresses)
                {
                    encoder.WriteString("BaseAddress", address);
                }

                encoder.Pop();
            }

            // Order 15: SecurityProfiles (EmitDefaultValue=false)
            if (app.SecurityProfiles != null && app.SecurityProfiles.Count > 0)
            {
                encoder.Push("SecurityProfiles", Namespaces.OpcUaSecurity);
                foreach (SecurityProfile profile in app.SecurityProfiles)
                {
                    encoder.Push("SecurityProfile", Namespaces.OpcUaSecurity);
                    EncodeSecurityProfile(encoder, profile);
                    encoder.Pop();
                }

                encoder.Pop();
            }

            // Order 16: Extensions (EmitDefaultValue=false)
            if (app.Extensions != null && app.Extensions.Count > 0)
            {
                encoder.Push("Extensions", Namespaces.OpcUaSecurity);
                foreach (System.Xml.XmlElement extension in app.Extensions)
                {
                    if (extension != null)
                    {
                        encoder.WriteXmlElement("Extension", XmlElement.From(extension));
                    }
                }

                encoder.Pop();
            }

            // Order 17: ApplicationCertificates (EmitDefaultValue=false)
            if (app.ApplicationCertificates != null)
            {
                encoder.Push("ApplicationCertificates", Namespaces.OpcUaSecurity);
                EncodeCertificateList(encoder, app.ApplicationCertificates);
                encoder.Pop();
            }
        }

        /// <summary>
        /// Decodes the contents of a <see cref="SecuredApplication"/> from the decoder.
        /// The caller must have already entered the root element context.
        /// </summary>
        internal static void DecodeContents(
            XmlParser decoder,
            SecuredApplication app)
        {
            app.ApplicationName = decoder.ReadString("ApplicationName");
            app.ApplicationUri = decoder.ReadString("ApplicationUri");

            string appTypeStr = decoder.ReadString("ApplicationType");
            if (appTypeStr != null)
            {
                app.ApplicationType = ParseEnum<ApplicationType>(appTypeStr);
            }

            app.ProductName = decoder.ReadString("ProductName");
            app.ConfigurationMode = decoder.ReadString("ConfigurationMode");

            DateTimeUtc lastExport = decoder.ReadDateTime("LastExportTime");
            if (lastExport != DateTimeUtc.MinValue)
            {
                app.LastExportTime = lastExport.ToDateTime();
            }

            app.ConfigurationFile = decoder.ReadString("ConfigurationFile");
            app.ExecutableFile = decoder.ReadString("ExecutableFile");

            // Order 8: ApplicationCertificate
            if (decoder.Peek("ApplicationCertificate"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                app.ApplicationCertificate = DecodeCertificateIdentifier(decoder);
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "ApplicationCertificate",
                        Namespaces.OpcUaSecurity));
            }

            // Order 9: TrustedCertificateStore
            if (decoder.Peek("TrustedCertificateStore"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                app.TrustedCertificateStore = DecodeCertificateStoreIdentifier(decoder);
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "TrustedCertificateStore",
                        Namespaces.OpcUaSecurity));
            }

            // Order 10: TrustedCertificates
            if (decoder.Peek("TrustedCertificates"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                app.TrustedCertificates = DecodeCertificateList(decoder);
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "TrustedCertificates",
                        Namespaces.OpcUaSecurity));
            }

            // Order 11: IssuerCertificateStore
            if (decoder.Peek("IssuerCertificateStore"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                app.IssuerCertificateStore = DecodeCertificateStoreIdentifier(decoder);
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "IssuerCertificateStore",
                        Namespaces.OpcUaSecurity));
            }

            // Order 12: IssuerCertificates
            if (decoder.Peek("IssuerCertificates"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                app.IssuerCertificates = DecodeCertificateList(decoder);
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "IssuerCertificates",
                        Namespaces.OpcUaSecurity));
            }

            // Order 13: RejectedCertificatesStore
            if (decoder.Peek("RejectedCertificatesStore"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                app.RejectedCertificatesStore = DecodeCertificateStoreIdentifier(decoder);
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "RejectedCertificatesStore",
                        Namespaces.OpcUaSecurity));
            }

            // Order 14: BaseAddresses
            if (decoder.Peek("BaseAddresses"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                app.BaseAddresses = DecodeListOfBaseAddresses(decoder);
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "BaseAddresses",
                        Namespaces.OpcUaSecurity));
            }

            // Order 15: SecurityProfiles
            if (decoder.Peek("SecurityProfiles"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                app.SecurityProfiles = DecodeListOfSecurityProfiles(decoder);
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "SecurityProfiles",
                        Namespaces.OpcUaSecurity));
            }

            // Order 16: Extensions
            if (decoder.Peek("Extensions"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                app.Extensions = DecodeListOfExtensions(decoder);
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "Extensions",
                        Namespaces.OpcUaSecurity));
            }

            // Order 17: ApplicationCertificates
            if (decoder.Peek("ApplicationCertificates"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                app.ApplicationCertificates = DecodeCertificateList(decoder);
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "ApplicationCertificates",
                        Namespaces.OpcUaSecurity));
            }
        }

        internal static void EncodeCertificateIdentifier(
            XmlEncoder encoder,
            CertificateIdentifier cert)
        {
            if (cert.StoreType != null)
            {
                encoder.WriteString("StoreType", cert.StoreType);
            }

            if (cert.StorePath != null)
            {
                encoder.WriteString("StorePath", cert.StorePath);
            }

            if (cert.SubjectName != null)
            {
                encoder.WriteString("SubjectName", cert.SubjectName);
            }

            if (cert.Thumbprint != null)
            {
                encoder.WriteString("Thumbprint", cert.Thumbprint);
            }

            if (cert.RawData != null)
            {
                encoder.WriteByteString("RawData", new ByteString(cert.RawData));
            }

            if (cert.ValidationOptions != 0)
            {
                encoder.WriteInt32("ValidationOptions", cert.ValidationOptions);
            }

            if (cert.OfflineRevocationList != null)
            {
                encoder.WriteByteString(
                    "OfflineRevocationList",
                    new ByteString(cert.OfflineRevocationList));
            }

            if (cert.OnlineRevocationList != null)
            {
                encoder.WriteString("OnlineRevocationList", cert.OnlineRevocationList);
            }
        }

        internal static CertificateIdentifier DecodeCertificateIdentifier(
            XmlParser decoder)
        {
            var cert = new CertificateIdentifier
            {
                StoreType = decoder.ReadString("StoreType"),
                StorePath = decoder.ReadString("StorePath"),
                SubjectName = decoder.ReadString("SubjectName"),
                Thumbprint = decoder.ReadString("Thumbprint")
            };

            ByteString rawData = decoder.ReadByteString("RawData");
            if (!rawData.IsNull)
            {
                cert.RawData = rawData.Memory.ToArray();
            }

            cert.ValidationOptions = decoder.ReadInt32("ValidationOptions");

            ByteString offlineRevocation = decoder.ReadByteString("OfflineRevocationList");
            if (!offlineRevocation.IsNull)
            {
                cert.OfflineRevocationList = offlineRevocation.Memory.ToArray();
            }

            cert.OnlineRevocationList = decoder.ReadString("OnlineRevocationList");

            return cert;
        }

        internal static void EncodeCertificateStoreIdentifier(
            XmlEncoder encoder,
            CertificateStoreIdentifier store)
        {
            if (store.StoreType != null)
            {
                encoder.WriteString("StoreType", store.StoreType);
            }

            if (store.StorePath != null)
            {
                encoder.WriteString("StorePath", store.StorePath);
            }

            if (store.ValidationOptions != 0)
            {
                encoder.WriteInt32("ValidationOptions", store.ValidationOptions);
            }
        }

        internal static CertificateStoreIdentifier DecodeCertificateStoreIdentifier(
            XmlParser decoder)
        {
            return new CertificateStoreIdentifier
            {
                StoreType = decoder.ReadString("StoreType"),
                StorePath = decoder.ReadString("StorePath"),
                ValidationOptions = decoder.ReadInt32("ValidationOptions")
            };
        }

        internal static void EncodeCertificateList(
            XmlEncoder encoder,
            CertificateList list)
        {
            if (list.Certificates != null && list.Certificates.Count > 0)
            {
                encoder.Push("Certificates", Namespaces.OpcUaSecurity);
                foreach (CertificateIdentifier cert in list.Certificates)
                {
                    encoder.Push("CertificateIdentifier", Namespaces.OpcUaSecurity);
                    EncodeCertificateIdentifier(encoder, cert);
                    encoder.Pop();
                }

                encoder.Pop();
            }

            encoder.WriteInt32("ValidationOptions", list.ValidationOptions);
        }

        internal static CertificateList DecodeCertificateList(XmlParser decoder)
        {
            var list = new CertificateList();

            if (decoder.Peek("Certificates"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                var certs = new ListOfCertificateIdentifier();
                while (decoder.Peek("CertificateIdentifier"))
                {
                    decoder.ReadStartElement();
                    decoder.PushNamespace(Namespaces.OpcUaSecurity);
                    certs.Add(DecodeCertificateIdentifier(decoder));
                    decoder.PopNamespace();
                    decoder.Skip(
                        new System.Xml.XmlQualifiedName(
                            "CertificateIdentifier",
                            Namespaces.OpcUaSecurity));
                }

                list.Certificates = certs;
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "Certificates",
                        Namespaces.OpcUaSecurity));
            }

            list.ValidationOptions = decoder.ReadInt32("ValidationOptions");

            return list;
        }

        internal static void EncodeSecurityProfile(
            XmlEncoder encoder,
            SecurityProfile profile)
        {
            if (profile.ProfileUri != null)
            {
                encoder.WriteString("ProfileUri", profile.ProfileUri);
            }

            encoder.WriteBoolean("Enabled", profile.Enabled);
        }

        internal static SecurityProfile DecodeSecurityProfile(XmlParser decoder)
        {
            return new SecurityProfile
            {
                ProfileUri = decoder.ReadString("ProfileUri"),
                Enabled = decoder.ReadBoolean("Enabled")
            };
        }

        internal static ListOfBaseAddresses DecodeListOfBaseAddresses(XmlParser decoder)
        {
            var list = new ListOfBaseAddresses();
            while (decoder.Peek("BaseAddress"))
            {
                list.Add(decoder.ReadString("BaseAddress"));
            }

            return list;
        }

        internal static ListOfSecurityProfiles DecodeListOfSecurityProfiles(
            XmlParser decoder)
        {
            var list = new ListOfSecurityProfiles();
            while (decoder.Peek("SecurityProfile"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaSecurity);
                list.Add(DecodeSecurityProfile(decoder));
                decoder.PopNamespace();
                decoder.Skip(
                    new System.Xml.XmlQualifiedName(
                        "SecurityProfile",
                        Namespaces.OpcUaSecurity));
            }

            return list;
        }

        internal static ListOfExtensions DecodeListOfExtensions(XmlParser decoder)
        {
            var list = new ListOfExtensions();
            while (decoder.Peek("Extension"))
            {
                XmlElement xmlElement = decoder.ReadXmlElement("Extension");
                if (!xmlElement.IsNull)
                {
                    list.Add(xmlElement.ToXmlElement());
                }
            }

            return list;
        }

        private static T ParseEnum<T>(string value) where T : struct
        {
            if (value == null)
            {
                return default;
            }

            if (Enum.TryParse(value, out T result))
            {
                return result;
            }

            // DataContractSerializer uses "Name_Value" format for enum members.
            int underscoreIndex = value.LastIndexOf('_');
            if (underscoreIndex > 0 &&
                int.TryParse(value[(underscoreIndex + 1)..], out int intValue))
            {
                return (T)(object)intValue;
            }

            return default;
        }
    }
}
