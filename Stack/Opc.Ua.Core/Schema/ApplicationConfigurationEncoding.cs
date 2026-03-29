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

using System;
using System.Collections.Generic;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Static encode/decode helpers for all DataContract types in ApplicationConfiguration.cs.
    /// Uses XmlEncoder/XmlParser (OPC UA encoders) instead of DataContractSerializer.
    /// XML format is semantically compatible with the existing DataContractSerializer format.
    /// </summary>
    internal static class AppConfigEncoding
    {
        #region ApplicationConfiguration

        /// <summary>
        /// Encodes the contents of an <see cref="ApplicationConfiguration"/> into the encoder.
        /// The caller must have already pushed the root element.
        /// </summary>
        internal static void EncodeContents(XmlEncoder encoder, ApplicationConfiguration config)
        {
            // Order 0: ApplicationName
            if (config.ApplicationName != null)
            {
                encoder.WriteString("ApplicationName", config.ApplicationName);
            }

            // Order 1: ApplicationUri
            if (config.ApplicationUri != null)
            {
                encoder.WriteString("ApplicationUri", config.ApplicationUri);
            }

            // Order 2: ProductUri
            encoder.WriteString("ProductUri", config.ProductUri);

            // Order 3: ApplicationType (enum written as "Name_Value" string)
            encoder.WriteString("ApplicationType", config.ApplicationType.ToString());

            // Order 4: SecurityConfiguration
            if (config.SecurityConfiguration != null)
            {
                encoder.Push("SecurityConfiguration", Namespaces.OpcUaConfig);
                EncodeSecurityConfiguration(encoder, config.SecurityConfiguration);
                encoder.Pop();
            }

            // Order 5: TransportConfigurations
            encoder.Push("TransportConfigurations", Namespaces.OpcUaConfig);
            EncodeTransportConfigurationCollection(encoder, config.TransportConfigurations);
            encoder.Pop();

            // Order 6: TransportQuotas
            if (config.TransportQuotas != null)
            {
                encoder.Push("TransportQuotas", Namespaces.OpcUaConfig);
                EncodeTransportQuotas(encoder, config.TransportQuotas);
                encoder.Pop();
            }

            // Order 7: ServerConfiguration
            if (config.ServerConfiguration != null)
            {
                encoder.Push("ServerConfiguration", Namespaces.OpcUaConfig);
                EncodeServerConfiguration(encoder, config.ServerConfiguration);
                encoder.Pop();
            }

            // Order 8: ClientConfiguration
            if (config.ClientConfiguration != null)
            {
                encoder.Push("ClientConfiguration", Namespaces.OpcUaConfig);
                EncodeClientConfiguration(encoder, config.ClientConfiguration);
                encoder.Pop();
            }

            // Order 9: DiscoveryServerConfiguration
            if (config.DiscoveryServerConfiguration != null)
            {
                encoder.Push("DiscoveryServerConfiguration", Namespaces.OpcUaConfig);
                EncodeDiscoveryServerConfiguration(encoder, config.DiscoveryServerConfiguration);
                encoder.Pop();
            }

            // Order 10: Extensions (ArrayOf<XmlElement>)
            if (!config.Extensions.IsNull)
            {
                encoder.WriteXmlElementArray("Extensions", config.Extensions);
            }

            // Order 11: TraceConfiguration
            if (config.TraceConfiguration != null)
            {
                encoder.Push("TraceConfiguration", Namespaces.OpcUaConfig);
                EncodeTraceConfiguration(encoder, config.TraceConfiguration);
                encoder.Pop();
            }

            // Order 12: DisableHiResClock (EmitDefaultValue=false → skip if false)
            if (config.DisableHiResClock)
            {
                encoder.WriteBoolean("DisableHiResClock", config.DisableHiResClock);
            }
        }

        /// <summary>
        /// Decodes the contents of an <see cref="ApplicationConfiguration"/> from the decoder.
        /// The caller must have already entered the root element context.
        /// </summary>
        internal static void DecodeContents(XmlParser decoder, ApplicationConfiguration config)
        {
            config.ApplicationName = decoder.ReadString("ApplicationName");
            config.ApplicationUri = decoder.ReadString("ApplicationUri");
            config.ProductUri = decoder.ReadString("ProductUri");

            string appTypeStr = decoder.ReadString("ApplicationType");
            if (appTypeStr != null)
            {
                config.ApplicationType = ParseConfigEnum<ApplicationType>(appTypeStr);
            }

            if (decoder.Peek("SecurityConfiguration"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                DecodeSecurityConfiguration(decoder, config.SecurityConfiguration);
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("SecurityConfiguration", Namespaces.OpcUaConfig));
            }

            if (decoder.Peek("TransportConfigurations"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                config.TransportConfigurations = DecodeTransportConfigurationCollection(decoder);
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("TransportConfigurations", Namespaces.OpcUaConfig));
            }

            if (decoder.Peek("TransportQuotas"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                config.TransportQuotas = DecodeTransportQuotas(decoder);
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("TransportQuotas", Namespaces.OpcUaConfig));
            }

            if (decoder.Peek("ServerConfiguration"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                config.ServerConfiguration = new ServerConfiguration();
                DecodeServerConfiguration(decoder, config.ServerConfiguration);
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("ServerConfiguration", Namespaces.OpcUaConfig));
            }

            if (decoder.Peek("ClientConfiguration"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                config.ClientConfiguration = new ClientConfiguration();
                DecodeClientConfiguration(decoder, config.ClientConfiguration);
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("ClientConfiguration", Namespaces.OpcUaConfig));
            }

            if (decoder.Peek("DiscoveryServerConfiguration"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                config.DiscoveryServerConfiguration = new DiscoveryServerConfiguration();
                DecodeDiscoveryServerConfiguration(decoder, config.DiscoveryServerConfiguration);
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("DiscoveryServerConfiguration", Namespaces.OpcUaConfig));
            }

            config.Extensions = decoder.ReadXmlElementArray("Extensions");
            config.TraceConfiguration = DecodeOptionalObject(decoder, "TraceConfiguration",
                (d) =>
                {
                    var tc = new TraceConfiguration();
                    DecodeTraceConfiguration(d, tc);
                    return tc;
                });

            config.DisableHiResClock = decoder.ReadBoolean("DisableHiResClock");
        }

        #endregion

        #region TransportQuotas

        internal static void EncodeTransportQuotas(XmlEncoder encoder, TransportQuotas q)
        {
            encoder.WriteInt32("OperationTimeout", q.OperationTimeout);
            encoder.WriteInt32("MaxStringLength", q.MaxStringLength);
            encoder.WriteInt32("MaxByteStringLength", q.MaxByteStringLength);
            encoder.WriteInt32("MaxArrayLength", q.MaxArrayLength);
            encoder.WriteInt32("MaxMessageSize", q.MaxMessageSize);
            encoder.WriteInt32("MaxBufferSize", q.MaxBufferSize);
            encoder.WriteInt32("MaxEncodingNestingLevels", q.MaxEncodingNestingLevels);
            encoder.WriteInt32("MaxDecoderRecoveries", q.MaxDecoderRecoveries);
            encoder.WriteInt32("ChannelLifetime", q.ChannelLifetime);
            encoder.WriteInt32("SecurityTokenLifetime", q.SecurityTokenLifetime);
        }

        internal static TransportQuotas DecodeTransportQuotas(XmlParser decoder)
        {
            var q = new TransportQuotas();
            q.OperationTimeout = decoder.ReadInt32("OperationTimeout");
            q.MaxStringLength = decoder.ReadInt32("MaxStringLength");
            q.MaxByteStringLength = decoder.ReadInt32("MaxByteStringLength");
            q.MaxArrayLength = decoder.ReadInt32("MaxArrayLength");
            q.MaxMessageSize = decoder.ReadInt32("MaxMessageSize");
            q.MaxBufferSize = decoder.ReadInt32("MaxBufferSize");
            q.MaxEncodingNestingLevels = decoder.ReadInt32("MaxEncodingNestingLevels");
            q.MaxDecoderRecoveries = decoder.ReadInt32("MaxDecoderRecoveries");
            q.ChannelLifetime = decoder.ReadInt32("ChannelLifetime");
            q.SecurityTokenLifetime = decoder.ReadInt32("SecurityTokenLifetime");
            return q;
        }

        #endregion

        #region TraceConfiguration

        internal static void EncodeTraceConfiguration(XmlEncoder encoder, TraceConfiguration tc)
        {
            encoder.WriteString("OutputFilePath", tc.OutputFilePath);
            encoder.WriteBoolean("DeleteOnLoad", tc.DeleteOnLoad);

            if (tc.TraceMasks != 0)
            {
                encoder.WriteInt32("TraceMasks", tc.TraceMasks);
            }
        }

        internal static void DecodeTraceConfiguration(XmlParser decoder, TraceConfiguration tc)
        {
            tc.OutputFilePath = decoder.ReadString("OutputFilePath");
            tc.DeleteOnLoad = decoder.ReadBoolean("DeleteOnLoad");
            tc.TraceMasks = decoder.ReadInt32("TraceMasks");
        }

        #endregion

        #region TransportConfiguration

        internal static void EncodeTransportConfiguration(XmlEncoder encoder, TransportConfiguration tc)
        {
            if (tc.UriScheme != null)
            {
                encoder.WriteString("UriScheme", tc.UriScheme);
            }

            if (tc.TypeName != null)
            {
                encoder.WriteString("TypeName", tc.TypeName);
            }
        }

        internal static TransportConfiguration DecodeTransportConfiguration(XmlParser decoder)
        {
            var tc = new TransportConfiguration();
            tc.UriScheme = decoder.ReadString("UriScheme");
            tc.TypeName = decoder.ReadString("TypeName");
            return tc;
        }

        internal static void EncodeTransportConfigurationCollection(
            XmlEncoder encoder,
            TransportConfigurationCollection items)
        {
            if (items == null)
            {
                return;
            }

            foreach (TransportConfiguration item in items)
            {
                encoder.Push("TransportConfiguration", Namespaces.OpcUaConfig);
                EncodeTransportConfiguration(encoder, item);
                encoder.Pop();
            }
        }

        internal static TransportConfigurationCollection DecodeTransportConfigurationCollection(
            XmlParser decoder)
        {
            var collection = new TransportConfigurationCollection();
            while (decoder.Peek("TransportConfiguration"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                collection.Add(DecodeTransportConfiguration(decoder));
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("TransportConfiguration", Namespaces.OpcUaConfig));
            }

            return collection;
        }

        #endregion

        #region ServerSecurityPolicy

        internal static void EncodeServerSecurityPolicy(XmlEncoder encoder, ServerSecurityPolicy policy)
        {
            encoder.WriteString("SecurityMode", policy.SecurityMode.ToString());
            encoder.WriteString("SecurityPolicyUri", policy.SecurityPolicyUri);
        }

        internal static ServerSecurityPolicy DecodeServerSecurityPolicy(XmlParser decoder)
        {
            var policy = new ServerSecurityPolicy();

            string secModeStr = decoder.ReadString("SecurityMode");
            if (secModeStr != null)
            {
                policy.SecurityMode = ParseConfigEnum<MessageSecurityMode>(secModeStr);
            }

            policy.SecurityPolicyUri = decoder.ReadString("SecurityPolicyUri");
            return policy;
        }

        internal static void EncodeServerSecurityPolicyCollection(
            XmlEncoder encoder,
            string wrapperElement,
            ServerSecurityPolicyCollection policies)
        {
            if (policies == null || policies.Count == 0)
            {
                return;
            }

            encoder.Push(wrapperElement, Namespaces.OpcUaConfig);
            foreach (ServerSecurityPolicy policy in policies)
            {
                encoder.Push("ServerSecurityPolicy", Namespaces.OpcUaConfig);
                EncodeServerSecurityPolicy(encoder, policy);
                encoder.Pop();
            }

            encoder.Pop();
        }

        internal static ServerSecurityPolicyCollection DecodeServerSecurityPolicyCollection(
            XmlParser decoder,
            string wrapperElement)
        {
            var collection = new ServerSecurityPolicyCollection();
            if (decoder.Peek(wrapperElement))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                while (decoder.Peek("ServerSecurityPolicy"))
                {
                    decoder.ReadStartElement();
                    decoder.PushNamespace(Namespaces.OpcUaConfig);
                    collection.Add(DecodeServerSecurityPolicy(decoder));
                    decoder.PopNamespace();
                    decoder.Skip(new XmlQualifiedName("ServerSecurityPolicy", Namespaces.OpcUaConfig));
                }

                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName(wrapperElement, Namespaces.OpcUaConfig));
            }

            return collection;
        }

        #endregion

        #region SecurityConfiguration

        internal static void EncodeSecurityConfiguration(
            XmlEncoder encoder,
            SecurityConfiguration config)
        {
            // Order 0: ApplicationCertificate (legacy, only when IsDeprecatedConfiguration)
            if (config.IsDeprecatedConfiguration && config.ApplicationCertificate != null)
            {
                encoder.Push("ApplicationCertificate", Namespaces.OpcUaConfig);
                EncodeCertificateIdentifier(encoder, config.ApplicationCertificate);
                encoder.Pop();
            }

            // Order 1: ApplicationCertificates (modern, only when !IsDeprecatedConfiguration)
            if (!config.IsDeprecatedConfiguration && config.ApplicationCertificates.Count > 0)
            {
                encoder.Push("ApplicationCertificates", Namespaces.OpcUaConfig);
                foreach (CertificateIdentifier cert in config.ApplicationCertificates)
                {
                    encoder.Push("CertificateIdentifier", Namespaces.OpcUaConfig);
                    EncodeCertificateIdentifier(encoder, cert);
                    encoder.Pop();
                }

                encoder.Pop();
            }

            // Order 2: TrustedIssuerCertificates
            if (config.TrustedIssuerCertificates != null)
            {
                encoder.Push("TrustedIssuerCertificates", Namespaces.OpcUaConfig);
                EncodeCertificateTrustList(encoder, config.TrustedIssuerCertificates);
                encoder.Pop();
            }

            // Order 4: TrustedPeerCertificates
            if (config.TrustedPeerCertificates != null)
            {
                encoder.Push("TrustedPeerCertificates", Namespaces.OpcUaConfig);
                EncodeCertificateTrustList(encoder, config.TrustedPeerCertificates);
                encoder.Pop();
            }

            // Order 6: NonceLength (EmitDefaultValue=false → skip if 0)
            if (config.NonceLength != 0)
            {
                encoder.WriteInt32("NonceLength", config.NonceLength);
            }

            // Order 7: RejectedCertificateStore (EmitDefaultValue=false)
            if (config.RejectedCertificateStore != null)
            {
                encoder.Push("RejectedCertificateStore", Namespaces.OpcUaConfig);
                EncodeCertificateStoreIdentifier(encoder, config.RejectedCertificateStore);
                encoder.Pop();
            }

            // Order 8: MaxRejectedCertificates (EmitDefaultValue=false → skip if 0)
            if (config.MaxRejectedCertificates != 0)
            {
                encoder.WriteInt32("MaxRejectedCertificates", config.MaxRejectedCertificates);
            }

            // Order 9: AutoAcceptUntrustedCertificates (EmitDefaultValue=false → skip if false)
            if (config.AutoAcceptUntrustedCertificates)
            {
                encoder.WriteBoolean("AutoAcceptUntrustedCertificates", true);
            }

            // Order 10: UserRoleDirectory
            encoder.WriteString("UserRoleDirectory", config.UserRoleDirectory);

            // Order 11: RejectSHA1SignedCertificates (EmitDefaultValue=false → skip if false)
            if (config.RejectSHA1SignedCertificates)
            {
                encoder.WriteBoolean("RejectSHA1SignedCertificates", true);
            }

            // Order 12: RejectUnknownRevocationStatus (EmitDefaultValue=false → skip if false)
            if (config.RejectUnknownRevocationStatus)
            {
                encoder.WriteBoolean("RejectUnknownRevocationStatus", true);
            }

            // Order 13: MinimumCertificateKeySize (EmitDefaultValue=false → skip if 0)
            if (config.MinimumCertificateKeySize != 0)
            {
                encoder.WriteUInt16("MinimumCertificateKeySize", config.MinimumCertificateKeySize);
            }

            // Order 14: UseValidatedCertificates (EmitDefaultValue=false → skip if false)
            if (config.UseValidatedCertificates)
            {
                encoder.WriteBoolean("UseValidatedCertificates", true);
            }

            // Order 15: AddAppCertToTrustedStore (EmitDefaultValue=false → skip if false)
            if (config.AddAppCertToTrustedStore)
            {
                encoder.WriteBoolean("AddAppCertToTrustedStore", true);
            }

            // Order 16: SendCertificateChain (EmitDefaultValue=false → skip if false)
            if (config.SendCertificateChain)
            {
                encoder.WriteBoolean("SendCertificateChain", true);
            }

            // Order 17: UserIssuerCertificates (EmitDefaultValue=false)
            if (config.UserIssuerCertificates != null &&
                (config.UserIssuerCertificates.StoreType != null ||
                 config.UserIssuerCertificates.StorePath != null))
            {
                encoder.Push("UserIssuerCertificates", Namespaces.OpcUaConfig);
                EncodeCertificateTrustList(encoder, config.UserIssuerCertificates);
                encoder.Pop();
            }

            // Order 18: TrustedUserCertificates (EmitDefaultValue=false)
            if (config.TrustedUserCertificates != null &&
                (config.TrustedUserCertificates.StoreType != null ||
                 config.TrustedUserCertificates.StorePath != null))
            {
                encoder.Push("TrustedUserCertificates", Namespaces.OpcUaConfig);
                EncodeCertificateTrustList(encoder, config.TrustedUserCertificates);
                encoder.Pop();
            }

            // Order 19: HttpsIssuerCertificates (EmitDefaultValue=false)
            if (config.HttpsIssuerCertificates != null &&
                (config.HttpsIssuerCertificates.StoreType != null ||
                 config.HttpsIssuerCertificates.StorePath != null))
            {
                encoder.Push("HttpsIssuerCertificates", Namespaces.OpcUaConfig);
                EncodeCertificateTrustList(encoder, config.HttpsIssuerCertificates);
                encoder.Pop();
            }

            // Order 20: TrustedHttpsCertificates (EmitDefaultValue=false)
            if (config.TrustedHttpsCertificates != null &&
                (config.TrustedHttpsCertificates.StoreType != null ||
                 config.TrustedHttpsCertificates.StorePath != null))
            {
                encoder.Push("TrustedHttpsCertificates", Namespaces.OpcUaConfig);
                EncodeCertificateTrustList(encoder, config.TrustedHttpsCertificates);
                encoder.Pop();
            }

            // Order 21: SuppressNonceValidationErrors (EmitDefaultValue=false → skip if false)
            if (config.SuppressNonceValidationErrors)
            {
                encoder.WriteBoolean("SuppressNonceValidationErrors", true);
            }
        }

        internal static void DecodeSecurityConfiguration(
            XmlParser decoder,
            SecurityConfiguration config)
        {
            // Order 0: ApplicationCertificate (legacy)
            if (decoder.Peek("ApplicationCertificate"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                config.ApplicationCertificate = DecodeCertificateIdentifier(decoder);
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("ApplicationCertificate", Namespaces.OpcUaConfig));
            }

            // Order 1: ApplicationCertificates (modern)
            if (decoder.Peek("ApplicationCertificates"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                var certs = new CertificateIdentifierCollection();
                while (decoder.Peek("CertificateIdentifier"))
                {
                    decoder.ReadStartElement();
                    decoder.PushNamespace(Namespaces.OpcUaConfig);
                    certs.Add(DecodeCertificateIdentifier(decoder));
                    decoder.PopNamespace();
                    decoder.Skip(new XmlQualifiedName("CertificateIdentifier", Namespaces.OpcUaConfig));
                }

                config.ApplicationCertificates = certs;
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("ApplicationCertificates", Namespaces.OpcUaConfig));
            }

            // Order 2: TrustedIssuerCertificates
            config.TrustedIssuerCertificates = DecodeCertificateTrustList(
                decoder, "TrustedIssuerCertificates") ?? config.TrustedIssuerCertificates;

            // Order 4: TrustedPeerCertificates
            config.TrustedPeerCertificates = DecodeCertificateTrustList(
                decoder, "TrustedPeerCertificates") ?? config.TrustedPeerCertificates;

            // Order 6: NonceLength
            int nonceLength = decoder.ReadInt32("NonceLength");
            if (nonceLength != 0)
            {
                config.NonceLength = nonceLength;
            }

            // Order 7: RejectedCertificateStore
            if (decoder.Peek("RejectedCertificateStore"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                var store = new CertificateStoreIdentifier();
                DecodeCertificateStoreIdentifierContents(decoder, store);
                config.RejectedCertificateStore = store;
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("RejectedCertificateStore", Namespaces.OpcUaConfig));
            }

            // Order 8: MaxRejectedCertificates
            int maxRejected = decoder.ReadInt32("MaxRejectedCertificates");
            if (maxRejected != 0)
            {
                config.MaxRejectedCertificates = maxRejected;
            }

            config.AutoAcceptUntrustedCertificates = decoder.ReadBoolean("AutoAcceptUntrustedCertificates");
            config.UserRoleDirectory = decoder.ReadString("UserRoleDirectory");
            config.RejectSHA1SignedCertificates = decoder.ReadBoolean("RejectSHA1SignedCertificates");
            config.RejectUnknownRevocationStatus = decoder.ReadBoolean("RejectUnknownRevocationStatus");

            ushort minKeySize = decoder.ReadUInt16("MinimumCertificateKeySize");
            if (minKeySize != 0)
            {
                config.MinimumCertificateKeySize = minKeySize;
            }

            config.UseValidatedCertificates = decoder.ReadBoolean("UseValidatedCertificates");
            config.AddAppCertToTrustedStore = decoder.ReadBoolean("AddAppCertToTrustedStore");
            config.SendCertificateChain = decoder.ReadBoolean("SendCertificateChain");

            var userIssuer = DecodeCertificateTrustList(decoder, "UserIssuerCertificates");
            if (userIssuer != null)
            {
                config.UserIssuerCertificates = userIssuer;
            }

            var trustedUser = DecodeCertificateTrustList(decoder, "TrustedUserCertificates");
            if (trustedUser != null)
            {
                config.TrustedUserCertificates = trustedUser;
            }

            var httpsIssuer = DecodeCertificateTrustList(decoder, "HttpsIssuerCertificates");
            if (httpsIssuer != null)
            {
                config.HttpsIssuerCertificates = httpsIssuer;
            }

            var trustedHttps = DecodeCertificateTrustList(decoder, "TrustedHttpsCertificates");
            if (trustedHttps != null)
            {
                config.TrustedHttpsCertificates = trustedHttps;
            }

            config.SuppressNonceValidationErrors = decoder.ReadBoolean("SuppressNonceValidationErrors");
        }

        #endregion

        #region SamplingRateGroup

        internal static void EncodeSamplingRateGroup(XmlEncoder encoder, SamplingRateGroup g)
        {
            encoder.WriteDouble("Start", g.Start);
            encoder.WriteDouble("Increment", g.Increment);
            encoder.WriteInt32("Count", g.Count);
        }

        internal static SamplingRateGroup DecodeSamplingRateGroup(XmlParser decoder)
        {
            var g = new SamplingRateGroup();
            g.Start = decoder.ReadDouble("Start");
            g.Increment = decoder.ReadDouble("Increment");
            g.Count = decoder.ReadInt32("Count");
            return g;
        }

        internal static void EncodeSamplingRateGroupCollection(
            XmlEncoder encoder,
            SamplingRateGroupCollection items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            foreach (SamplingRateGroup item in items)
            {
                encoder.Push("SamplingRateGroup", Namespaces.OpcUaConfig);
                EncodeSamplingRateGroup(encoder, item);
                encoder.Pop();
            }
        }

        internal static SamplingRateGroupCollection DecodeSamplingRateGroupCollection(
            XmlParser decoder)
        {
            var collection = new SamplingRateGroupCollection();
            while (decoder.Peek("SamplingRateGroup"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                collection.Add(DecodeSamplingRateGroup(decoder));
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("SamplingRateGroup", Namespaces.OpcUaConfig));
            }

            return collection;
        }

        #endregion

        #region ServerBaseConfiguration

        internal static void EncodeServerBaseConfiguration(
            XmlEncoder encoder,
            ServerBaseConfiguration config)
        {
            // Order 0: BaseAddresses
            encoder.WriteStringArray("BaseAddresses", config.BaseAddresses);

            // Order 1: AlternateBaseAddresses
            encoder.WriteStringArray("AlternateBaseAddresses", config.AlternateBaseAddresses);

            // Order 2: SecurityPolicies
            EncodeServerSecurityPolicyCollection(encoder, "SecurityPolicies", config.SecurityPolicies);

            // Order 3: MinRequestThreadCount
            encoder.WriteInt32("MinRequestThreadCount", config.MinRequestThreadCount);

            // Order 4: MaxRequestThreadCount
            encoder.WriteInt32("MaxRequestThreadCount", config.MaxRequestThreadCount);

            // Order 5: MaxQueuedRequestCount
            encoder.WriteInt32("MaxQueuedRequestCount", config.MaxQueuedRequestCount);
        }

        internal static void DecodeServerBaseConfiguration(
            XmlParser decoder,
            ServerBaseConfiguration config)
        {
            config.BaseAddresses = decoder.ReadStringArray("BaseAddresses");
            config.AlternateBaseAddresses = decoder.ReadStringArray("AlternateBaseAddresses");
            config.SecurityPolicies = DecodeServerSecurityPolicyCollection(decoder, "SecurityPolicies");
            config.MinRequestThreadCount = decoder.ReadInt32("MinRequestThreadCount");
            config.MaxRequestThreadCount = decoder.ReadInt32("MaxRequestThreadCount");
            config.MaxQueuedRequestCount = decoder.ReadInt32("MaxQueuedRequestCount");
        }

        #endregion

        #region OperationLimits

        internal static void EncodeOperationLimits(XmlEncoder encoder, OperationLimits limits)
        {
            encoder.WriteUInt32("MaxNodesPerRead", limits.MaxNodesPerRead);
            encoder.WriteUInt32("MaxNodesPerHistoryReadData", limits.MaxNodesPerHistoryReadData);
            encoder.WriteUInt32("MaxNodesPerHistoryReadEvents", limits.MaxNodesPerHistoryReadEvents);
            encoder.WriteUInt32("MaxNodesPerWrite", limits.MaxNodesPerWrite);
            encoder.WriteUInt32("MaxNodesPerHistoryUpdateData", limits.MaxNodesPerHistoryUpdateData);
            encoder.WriteUInt32("MaxNodesPerHistoryUpdateEvents", limits.MaxNodesPerHistoryUpdateEvents);
            encoder.WriteUInt32("MaxNodesPerMethodCall", limits.MaxNodesPerMethodCall);
            encoder.WriteUInt32("MaxNodesPerBrowse", limits.MaxNodesPerBrowse);
            encoder.WriteUInt32("MaxNodesPerRegisterNodes", limits.MaxNodesPerRegisterNodes);
            encoder.WriteUInt32(
                "MaxNodesPerTranslateBrowsePathsToNodeIds",
                limits.MaxNodesPerTranslateBrowsePathsToNodeIds);
            encoder.WriteUInt32("MaxNodesPerNodeManagement", limits.MaxNodesPerNodeManagement);
            encoder.WriteUInt32("MaxMonitoredItemsPerCall", limits.MaxMonitoredItemsPerCall);
        }

        internal static OperationLimits DecodeOperationLimits(XmlParser decoder)
        {
            var limits = new OperationLimits();
            limits.MaxNodesPerRead = decoder.ReadUInt32("MaxNodesPerRead");
            limits.MaxNodesPerHistoryReadData = decoder.ReadUInt32("MaxNodesPerHistoryReadData");
            limits.MaxNodesPerHistoryReadEvents = decoder.ReadUInt32("MaxNodesPerHistoryReadEvents");
            limits.MaxNodesPerWrite = decoder.ReadUInt32("MaxNodesPerWrite");
            limits.MaxNodesPerHistoryUpdateData = decoder.ReadUInt32("MaxNodesPerHistoryUpdateData");
            limits.MaxNodesPerHistoryUpdateEvents = decoder.ReadUInt32("MaxNodesPerHistoryUpdateEvents");
            limits.MaxNodesPerMethodCall = decoder.ReadUInt32("MaxNodesPerMethodCall");
            limits.MaxNodesPerBrowse = decoder.ReadUInt32("MaxNodesPerBrowse");
            limits.MaxNodesPerRegisterNodes = decoder.ReadUInt32("MaxNodesPerRegisterNodes");
            limits.MaxNodesPerTranslateBrowsePathsToNodeIds =
                decoder.ReadUInt32("MaxNodesPerTranslateBrowsePathsToNodeIds");
            limits.MaxNodesPerNodeManagement = decoder.ReadUInt32("MaxNodesPerNodeManagement");
            limits.MaxMonitoredItemsPerCall = decoder.ReadUInt32("MaxMonitoredItemsPerCall");
            return limits;
        }

        #endregion

        #region ReverseConnectServerConfiguration

        internal static void EncodeReverseConnectServerConfiguration(
            XmlEncoder encoder,
            ReverseConnectServerConfiguration config)
        {
            // Order 10: Clients
            if (config.Clients != null && config.Clients.Count > 0)
            {
                encoder.Push("Clients", Namespaces.OpcUaConfig);
                EncodeReverseConnectClientCollection(encoder, config.Clients);
                encoder.Pop();
            }

            // Order 20: ConnectInterval
            encoder.WriteInt32("ConnectInterval", config.ConnectInterval);

            // Order 30: ConnectTimeout
            encoder.WriteInt32("ConnectTimeout", config.ConnectTimeout);

            // Order 40: RejectTimeout
            encoder.WriteInt32("RejectTimeout", config.RejectTimeout);
        }

        internal static ReverseConnectServerConfiguration DecodeReverseConnectServerConfiguration(
            XmlParser decoder)
        {
            var config = new ReverseConnectServerConfiguration();
            if (decoder.Peek("Clients"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                config.Clients = DecodeReverseConnectClientCollection(decoder);
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("Clients", Namespaces.OpcUaConfig));
            }

            config.ConnectInterval = decoder.ReadInt32("ConnectInterval");
            config.ConnectTimeout = decoder.ReadInt32("ConnectTimeout");
            config.RejectTimeout = decoder.ReadInt32("RejectTimeout");
            return config;
        }

        #endregion

        #region ReverseConnectClient

        internal static void EncodeReverseConnectClient(XmlEncoder encoder, ReverseConnectClient client)
        {
            encoder.WriteString("EndpointUrl", client.EndpointUrl);
            encoder.WriteInt32("Timeout", client.Timeout);
            encoder.WriteInt32("MaxSessionCount", client.MaxSessionCount);
            encoder.WriteBoolean("Enabled", client.Enabled);
        }

        internal static ReverseConnectClient DecodeReverseConnectClient(XmlParser decoder)
        {
            var client = new ReverseConnectClient();
            client.EndpointUrl = decoder.ReadString("EndpointUrl");
            client.Timeout = decoder.ReadInt32("Timeout");
            client.MaxSessionCount = decoder.ReadInt32("MaxSessionCount");
            client.Enabled = decoder.ReadBoolean("Enabled");
            return client;
        }

        internal static void EncodeReverseConnectClientCollection(
            XmlEncoder encoder,
            ReverseConnectClientCollection clients)
        {
            if (clients == null)
            {
                return;
            }

            foreach (ReverseConnectClient client in clients)
            {
                encoder.Push("ReverseConnectClient", Namespaces.OpcUaConfig);
                EncodeReverseConnectClient(encoder, client);
                encoder.Pop();
            }
        }

        internal static ReverseConnectClientCollection DecodeReverseConnectClientCollection(
            XmlParser decoder)
        {
            var collection = new ReverseConnectClientCollection();
            while (decoder.Peek("ReverseConnectClient"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                collection.Add(DecodeReverseConnectClient(decoder));
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("ReverseConnectClient", Namespaces.OpcUaConfig));
            }

            return collection;
        }

        #endregion

        #region ServerConfiguration

        internal static void EncodeServerConfiguration(XmlEncoder encoder, ServerConfiguration config)
        {
            // Base class fields first (ServerBaseConfiguration)
            EncodeServerBaseConfiguration(encoder, config);

            // Order 3: UserTokenPolicies (ArrayOf<UserTokenPolicy> – IEncodeable items)
            if (!config.UserTokenPolicies.IsNull)
            {
                encoder.WriteEncodeableArray("UserTokenPolicies", config.UserTokenPolicies);
            }

            // Order 4: DiagnosticsEnabled
            encoder.WriteBoolean("DiagnosticsEnabled", config.DiagnosticsEnabled);

            // Order 5: MaxSessionCount
            encoder.WriteInt32("MaxSessionCount", config.MaxSessionCount);

            // Order 6: MaxChannelCount
            encoder.WriteInt32("MaxChannelCount", config.MaxChannelCount);

            // Order 7: MinSessionTimeout
            encoder.WriteInt32("MinSessionTimeout", config.MinSessionTimeout);

            // Order 8: MaxSessionTimeout
            encoder.WriteInt32("MaxSessionTimeout", config.MaxSessionTimeout);

            // Order 9: MaxBrowseContinuationPoints
            encoder.WriteInt32("MaxBrowseContinuationPoints", config.MaxBrowseContinuationPoints);

            // Order 10: MaxQueryContinuationPoints
            encoder.WriteInt32("MaxQueryContinuationPoints", config.MaxQueryContinuationPoints);

            // Order 11: MaxHistoryContinuationPoints
            encoder.WriteInt32("MaxHistoryContinuationPoints", config.MaxHistoryContinuationPoints);

            // Order 12: MaxRequestAge
            encoder.WriteInt32("MaxRequestAge", config.MaxRequestAge);

            // Order 13: MinPublishingInterval
            encoder.WriteInt32("MinPublishingInterval", config.MinPublishingInterval);

            // Order 14: MaxPublishingInterval
            encoder.WriteInt32("MaxPublishingInterval", config.MaxPublishingInterval);

            // Order 15: PublishingResolution
            encoder.WriteInt32("PublishingResolution", config.PublishingResolution);

            // Order 16: MaxSubscriptionLifetime
            encoder.WriteInt32("MaxSubscriptionLifetime", config.MaxSubscriptionLifetime);

            // Order 17: MaxMessageQueueSize
            encoder.WriteInt32("MaxMessageQueueSize", config.MaxMessageQueueSize);

            // Order 18: MaxNotificationQueueSize
            encoder.WriteInt32("MaxNotificationQueueSize", config.MaxNotificationQueueSize);

            // Order 19: MaxNotificationsPerPublish
            encoder.WriteInt32("MaxNotificationsPerPublish", config.MaxNotificationsPerPublish);

            // Order 20: MinMetadataSamplingInterval
            encoder.WriteInt32("MinMetadataSamplingInterval", config.MinMetadataSamplingInterval);

            // Order 21: AvailableSamplingRates (EmitDefaultValue=false)
            if (config.AvailableSamplingRates != null && config.AvailableSamplingRates.Count > 0)
            {
                encoder.Push("AvailableSamplingRates", Namespaces.OpcUaConfig);
                EncodeSamplingRateGroupCollection(encoder, config.AvailableSamplingRates);
                encoder.Pop();
            }

            // Order 22: RegistrationEndpoint (EmitDefaultValue=false)
            if (config.RegistrationEndpoint != null)
            {
                encoder.WriteEncodeable("RegistrationEndpoint", config.RegistrationEndpoint);
            }

            // Order 23: MaxRegistrationInterval
            encoder.WriteInt32("MaxRegistrationInterval", config.MaxRegistrationInterval);

            // Order 24: NodeManagerSaveFile
            encoder.WriteString("NodeManagerSaveFile", config.NodeManagerSaveFile);

            // Order 25: MinSubscriptionLifetime
            encoder.WriteInt32("MinSubscriptionLifetime", config.MinSubscriptionLifetime);

            // Order 26: MaxPublishRequestCount
            encoder.WriteInt32("MaxPublishRequestCount", config.MaxPublishRequestCount);

            // Order 27: MaxSubscriptionCount
            encoder.WriteInt32("MaxSubscriptionCount", config.MaxSubscriptionCount);

            // Order 28: MaxEventQueueSize
            encoder.WriteInt32("MaxEventQueueSize", config.MaxEventQueueSize);

            // Order 29: ServerProfileArray
            encoder.WriteStringArray("ServerProfileArray", config.ServerProfileArray);

            // Order 30: ShutdownDelay
            encoder.WriteInt32("ShutdownDelay", config.ShutdownDelay);

            // Order 31: ServerCapabilities
            encoder.WriteStringArray("ServerCapabilities", config.ServerCapabilities);

            // Order 32: SupportedPrivateKeyFormats
            encoder.WriteStringArray("SupportedPrivateKeyFormats", config.SupportedPrivateKeyFormats);

            // Order 33: MaxTrustListSize
            encoder.WriteInt32("MaxTrustListSize", config.MaxTrustListSize);

            // Order 34: MultiCastDnsEnabled
            encoder.WriteBoolean("MultiCastDnsEnabled", config.MultiCastDnsEnabled);

            // Order 35: ReverseConnect
            if (config.ReverseConnect != null)
            {
                encoder.Push("ReverseConnect", Namespaces.OpcUaConfig);
                EncodeReverseConnectServerConfiguration(encoder, config.ReverseConnect);
                encoder.Pop();
            }

            // Order 36: OperationLimits
            if (config.OperationLimits != null)
            {
                encoder.Push("OperationLimits", Namespaces.OpcUaConfig);
                EncodeOperationLimits(encoder, config.OperationLimits);
                encoder.Pop();
            }

            // Order 37: AuditingEnabled
            encoder.WriteBoolean("AuditingEnabled", config.AuditingEnabled);

            // Order 38: HttpsMutualTls
            encoder.WriteBoolean("HttpsMutualTls", config.HttpsMutualTls);

            // Order 39: DurableSubscriptionsEnabled (EmitDefaultValue=false → skip if false)
            if (config.DurableSubscriptionsEnabled)
            {
                encoder.WriteBoolean("DurableSubscriptionsEnabled", true);
            }

            // Order 40: MaxDurableNotificationQueueSize
            encoder.WriteInt32("MaxDurableNotificationQueueSize", config.MaxDurableNotificationQueueSize);

            // Order 41: MaxDurableEventQueueSize
            encoder.WriteInt32("MaxDurableEventQueueSize", config.MaxDurableEventQueueSize);

            // Order 42: MaxDurableSubscriptionLifetimeInHours
            encoder.WriteInt32(
                "MaxDurableSubscriptionLifetimeInHours",
                config.MaxDurableSubscriptionLifetimeInHours);
        }

        internal static void DecodeServerConfiguration(XmlParser decoder, ServerConfiguration config)
        {
            // Base class fields first
            DecodeServerBaseConfiguration(decoder, config);

            config.UserTokenPolicies = decoder.ReadEncodeableArray<UserTokenPolicy>("UserTokenPolicies");
            config.DiagnosticsEnabled = decoder.ReadBoolean("DiagnosticsEnabled");
            config.MaxSessionCount = decoder.ReadInt32("MaxSessionCount");
            config.MaxChannelCount = decoder.ReadInt32("MaxChannelCount");
            config.MinSessionTimeout = decoder.ReadInt32("MinSessionTimeout");
            config.MaxSessionTimeout = decoder.ReadInt32("MaxSessionTimeout");
            config.MaxBrowseContinuationPoints = decoder.ReadInt32("MaxBrowseContinuationPoints");
            config.MaxQueryContinuationPoints = decoder.ReadInt32("MaxQueryContinuationPoints");
            config.MaxHistoryContinuationPoints = decoder.ReadInt32("MaxHistoryContinuationPoints");
            config.MaxRequestAge = decoder.ReadInt32("MaxRequestAge");
            config.MinPublishingInterval = decoder.ReadInt32("MinPublishingInterval");
            config.MaxPublishingInterval = decoder.ReadInt32("MaxPublishingInterval");
            config.PublishingResolution = decoder.ReadInt32("PublishingResolution");
            config.MaxSubscriptionLifetime = decoder.ReadInt32("MaxSubscriptionLifetime");
            config.MaxMessageQueueSize = decoder.ReadInt32("MaxMessageQueueSize");
            config.MaxNotificationQueueSize = decoder.ReadInt32("MaxNotificationQueueSize");
            config.MaxNotificationsPerPublish = decoder.ReadInt32("MaxNotificationsPerPublish");
            config.MinMetadataSamplingInterval = decoder.ReadInt32("MinMetadataSamplingInterval");

            if (decoder.Peek("AvailableSamplingRates"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                config.AvailableSamplingRates = DecodeSamplingRateGroupCollection(decoder);
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("AvailableSamplingRates", Namespaces.OpcUaConfig));
            }

            config.RegistrationEndpoint = decoder.ReadEncodeable<EndpointDescription>(
                "RegistrationEndpoint");

            config.MaxRegistrationInterval = decoder.ReadInt32("MaxRegistrationInterval");
            config.NodeManagerSaveFile = decoder.ReadString("NodeManagerSaveFile");
            config.MinSubscriptionLifetime = decoder.ReadInt32("MinSubscriptionLifetime");
            config.MaxPublishRequestCount = decoder.ReadInt32("MaxPublishRequestCount");
            config.MaxSubscriptionCount = decoder.ReadInt32("MaxSubscriptionCount");
            config.MaxEventQueueSize = decoder.ReadInt32("MaxEventQueueSize");

            config.ServerProfileArray = decoder.ReadStringArray("ServerProfileArray");
            config.ShutdownDelay = decoder.ReadInt32("ShutdownDelay");
            config.ServerCapabilities = decoder.ReadStringArray("ServerCapabilities");
            config.SupportedPrivateKeyFormats = decoder.ReadStringArray("SupportedPrivateKeyFormats");
            config.MaxTrustListSize = decoder.ReadInt32("MaxTrustListSize");
            config.MultiCastDnsEnabled = decoder.ReadBoolean("MultiCastDnsEnabled");

            config.ReverseConnect = DecodeOptionalObject(decoder, "ReverseConnect",
                DecodeReverseConnectServerConfiguration);

            config.OperationLimits = DecodeOptionalObject(decoder, "OperationLimits",
                DecodeOperationLimits);

            config.AuditingEnabled = decoder.ReadBoolean("AuditingEnabled");
            config.HttpsMutualTls = decoder.ReadBoolean("HttpsMutualTls");
            config.DurableSubscriptionsEnabled = decoder.ReadBoolean("DurableSubscriptionsEnabled");
            config.MaxDurableNotificationQueueSize = decoder.ReadInt32("MaxDurableNotificationQueueSize");
            config.MaxDurableEventQueueSize = decoder.ReadInt32("MaxDurableEventQueueSize");
            config.MaxDurableSubscriptionLifetimeInHours =
                decoder.ReadInt32("MaxDurableSubscriptionLifetimeInHours");

            // Mirror the [OnDeserialized] callback: expand wildcard policies and remove unsupported ones.
            config.ValidateSecurityPolicies();
        }

        #endregion

        #region ClientConfiguration

        internal static void EncodeClientConfiguration(XmlEncoder encoder, ClientConfiguration config)
        {
            // Order 0: DefaultSessionTimeout
            encoder.WriteInt32("DefaultSessionTimeout", config.DefaultSessionTimeout);

            // Order 1: WellKnownDiscoveryUrls (EmitDefaultValue=false)
            if (!config.WellKnownDiscoveryUrls.IsNull)
            {
                encoder.WriteStringArray("WellKnownDiscoveryUrls", config.WellKnownDiscoveryUrls);
            }

            // Order 2: DiscoveryServers (EmitDefaultValue=false) – ArrayOf<EndpointDescription>
            if (!config.DiscoveryServers.IsNull)
            {
                encoder.WriteEncodeableArray("DiscoveryServers", config.DiscoveryServers);
            }

            // Order 3: EndpointCacheFilePath
            encoder.WriteString("EndpointCacheFilePath", config.EndpointCacheFilePath);

            // Order 4: MinSubscriptionLifetime
            encoder.WriteInt32("MinSubscriptionLifetime", config.MinSubscriptionLifetime);

            // Order 5: ReverseConnect
            if (config.ReverseConnect != null)
            {
                encoder.Push("ReverseConnect", Namespaces.OpcUaConfig);
                EncodeReverseConnectClientConfiguration(encoder, config.ReverseConnect);
                encoder.Pop();
            }

            // Order 6: OperationLimits
            if (config.OperationLimits != null)
            {
                encoder.Push("OperationLimits", Namespaces.OpcUaConfig);
                EncodeOperationLimits(encoder, config.OperationLimits);
                encoder.Pop();
            }
        }

        internal static void DecodeClientConfiguration(XmlParser decoder, ClientConfiguration config)
        {
            config.DefaultSessionTimeout = decoder.ReadInt32("DefaultSessionTimeout");
            config.WellKnownDiscoveryUrls = decoder.ReadStringArray("WellKnownDiscoveryUrls");
            config.DiscoveryServers = decoder.ReadEncodeableArray<EndpointDescription>("DiscoveryServers");
            config.EndpointCacheFilePath = decoder.ReadString("EndpointCacheFilePath");
            config.MinSubscriptionLifetime = decoder.ReadInt32("MinSubscriptionLifetime");

            config.ReverseConnect = DecodeOptionalObject(decoder, "ReverseConnect",
                DecodeReverseConnectClientConfiguration);

            config.OperationLimits = DecodeOptionalObject(decoder, "OperationLimits",
                DecodeOperationLimits);
        }

        #endregion

        #region ReverseConnectClientConfiguration

        internal static void EncodeReverseConnectClientConfiguration(
            XmlEncoder encoder,
            ReverseConnectClientConfiguration config)
        {
            // Order 10: ClientEndpoints
            if (config.ClientEndpoints != null && config.ClientEndpoints.Count > 0)
            {
                encoder.Push("ClientEndpoints", Namespaces.OpcUaConfig);
                EncodeReverseConnectClientEndpointCollection(encoder, config.ClientEndpoints);
                encoder.Pop();
            }

            // Order 20: HoldTime
            encoder.WriteInt32("HoldTime", config.HoldTime);

            // Order 30: WaitTimeout
            encoder.WriteInt32("WaitTimeout", config.WaitTimeout);
        }

        internal static ReverseConnectClientConfiguration DecodeReverseConnectClientConfiguration(
            XmlParser decoder)
        {
            var config = new ReverseConnectClientConfiguration();
            if (decoder.Peek("ClientEndpoints"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                config.ClientEndpoints = DecodeReverseConnectClientEndpointCollection(decoder);
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("ClientEndpoints", Namespaces.OpcUaConfig));
            }

            config.HoldTime = decoder.ReadInt32("HoldTime");
            config.WaitTimeout = decoder.ReadInt32("WaitTimeout");
            return config;
        }

        #endregion

        #region ReverseConnectClientEndpoint

        internal static void EncodeReverseConnectClientEndpoint(
            XmlEncoder encoder,
            ReverseConnectClientEndpoint endpoint)
        {
            encoder.WriteString("EndpointUrl", endpoint.EndpointUrl);
        }

        internal static ReverseConnectClientEndpoint DecodeReverseConnectClientEndpoint(
            XmlParser decoder)
        {
            var endpoint = new ReverseConnectClientEndpoint();
            endpoint.EndpointUrl = decoder.ReadString("EndpointUrl");
            return endpoint;
        }

        internal static void EncodeReverseConnectClientEndpointCollection(
            XmlEncoder encoder,
            ReverseConnectClientEndpointCollection endpoints)
        {
            if (endpoints == null)
            {
                return;
            }

            foreach (ReverseConnectClientEndpoint endpoint in endpoints)
            {
                encoder.Push("ClientEndpoint", Namespaces.OpcUaConfig);
                EncodeReverseConnectClientEndpoint(encoder, endpoint);
                encoder.Pop();
            }
        }

        internal static ReverseConnectClientEndpointCollection DecodeReverseConnectClientEndpointCollection(
            XmlParser decoder)
        {
            var collection = new ReverseConnectClientEndpointCollection();
            while (decoder.Peek("ClientEndpoint"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                collection.Add(DecodeReverseConnectClientEndpoint(decoder));
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("ClientEndpoint", Namespaces.OpcUaConfig));
            }

            return collection;
        }

        #endregion

        #region DiscoveryServerConfiguration

        internal static void EncodeDiscoveryServerConfiguration(
            XmlEncoder encoder,
            DiscoveryServerConfiguration config)
        {
            // Base class fields first (ServerBaseConfiguration)
            EncodeServerBaseConfiguration(encoder, config);

            // Order 2: ServerNames (ArrayOf<LocalizedText>, EmitDefaultValue=false)
            if (!config.ServerNames.IsNull)
            {
                encoder.WriteLocalizedTextArray("ServerNames", config.ServerNames);
            }

            // Order 3: DiscoveryServerCacheFile
            encoder.WriteString("DiscoveryServerCacheFile", config.DiscoveryServerCacheFile);

            // Order 4: ServerRegistrations (EmitDefaultValue=false)
            if (config.ServerRegistrations != null && config.ServerRegistrations.Count > 0)
            {
                encoder.Push("ServerRegistrations", Namespaces.OpcUaConfig);
                EncodeServerRegistrationCollection(encoder, config.ServerRegistrations);
                encoder.Pop();
            }
        }

        internal static void DecodeDiscoveryServerConfiguration(
            XmlParser decoder,
            DiscoveryServerConfiguration config)
        {
            DecodeServerBaseConfiguration(decoder, config);
            config.ServerNames = decoder.ReadLocalizedTextArray("ServerNames");
            config.DiscoveryServerCacheFile = decoder.ReadString("DiscoveryServerCacheFile");

            if (decoder.Peek("ServerRegistrations"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                config.ServerRegistrations = DecodeServerRegistrationCollection(decoder);
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("ServerRegistrations", Namespaces.OpcUaConfig));
            }

            // Mirror the [OnDeserialized] callback: expand wildcard policies and remove unsupported ones.
            config.ValidateSecurityPolicies();
        }

        #endregion

        #region ServerRegistration

        internal static void EncodeServerRegistration(XmlEncoder encoder, ServerRegistration reg)
        {
            // Order 1: ApplicationUri (EmitDefaultValue=false)
            if (reg.ApplicationUri != null)
            {
                encoder.WriteString("ApplicationUri", reg.ApplicationUri);
            }

            // Order 2: AlternateDiscoveryUrls (EmitDefaultValue=false)
            if (!reg.AlternateDiscoveryUrls.IsNull)
            {
                encoder.WriteStringArray("AlternateDiscoveryUrls", reg.AlternateDiscoveryUrls);
            }
        }

        internal static ServerRegistration DecodeServerRegistration(XmlParser decoder)
        {
            var reg = new ServerRegistration();
            reg.ApplicationUri = decoder.ReadString("ApplicationUri");
            reg.AlternateDiscoveryUrls = decoder.ReadStringArray("AlternateDiscoveryUrls");
            return reg;
        }

        internal static void EncodeServerRegistrationCollection(
            XmlEncoder encoder,
            ServerRegistrationCollection registrations)
        {
            if (registrations == null)
            {
                return;
            }

            foreach (ServerRegistration reg in registrations)
            {
                encoder.Push("ServerRegistration", Namespaces.OpcUaConfig);
                EncodeServerRegistration(encoder, reg);
                encoder.Pop();
            }
        }

        internal static ServerRegistrationCollection DecodeServerRegistrationCollection(
            XmlParser decoder)
        {
            var collection = new ServerRegistrationCollection();
            while (decoder.Peek("ServerRegistration"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                collection.Add(DecodeServerRegistration(decoder));
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("ServerRegistration", Namespaces.OpcUaConfig));
            }

            return collection;
        }

        #endregion

        #region CertificateStoreIdentifier

        internal static void EncodeCertificateStoreIdentifier(
            XmlEncoder encoder,
            CertificateStoreIdentifier store)
        {
            // Order 0: StoreType (EmitDefaultValue=false)
            if (store.StoreType != null)
            {
                encoder.WriteString("StoreType", store.StoreType);
            }

            // Order 1: StorePath (EmitDefaultValue=false)
            if (store.StorePath != null)
            {
                encoder.WriteString("StorePath", store.StorePath);
            }

            // Order 4: ValidationOptions (internal int, DataMember Name="ValidationOptions")
            if (store.XmlEncodedValidationOptions != 0)
            {
                encoder.WriteInt32("ValidationOptions", store.XmlEncodedValidationOptions);
            }
        }

        internal static void DecodeCertificateStoreIdentifierContents(
            XmlParser decoder,
            CertificateStoreIdentifier store)
        {
            store.StoreType = decoder.ReadString("StoreType");
            store.StorePath = decoder.ReadString("StorePath");
            store.XmlEncodedValidationOptions = decoder.ReadInt32("ValidationOptions");
        }

        #endregion

        #region CertificateTrustList

        internal static void EncodeCertificateTrustList(
            XmlEncoder encoder,
            CertificateTrustList trustList)
        {
            // Base class fields first (CertificateStoreIdentifier)
            EncodeCertificateStoreIdentifier(encoder, trustList);

            // Order 3: TrustedCertificates (EmitDefaultValue=false)
            if (trustList.TrustedCertificates != null && trustList.TrustedCertificates.Count > 0)
            {
                encoder.Push("TrustedCertificates", Namespaces.OpcUaConfig);
                foreach (CertificateIdentifier cert in trustList.TrustedCertificates)
                {
                    encoder.Push("CertificateIdentifier", Namespaces.OpcUaConfig);
                    EncodeCertificateIdentifier(encoder, cert);
                    encoder.Pop();
                }

                encoder.Pop();
            }
        }

        internal static CertificateTrustList DecodeCertificateTrustList(
            XmlParser decoder,
            string elementName)
        {
            if (!decoder.Peek(elementName))
            {
                return null;
            }

            decoder.ReadStartElement();
            decoder.PushNamespace(Namespaces.OpcUaConfig);
            var trustList = new CertificateTrustList();
            DecodeCertificateStoreIdentifierContents(decoder, trustList);

            if (decoder.Peek("TrustedCertificates"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                var certs = new CertificateIdentifierCollection();
                while (decoder.Peek("CertificateIdentifier"))
                {
                    decoder.ReadStartElement();
                    decoder.PushNamespace(Namespaces.OpcUaConfig);
                    certs.Add(DecodeCertificateIdentifier(decoder));
                    decoder.PopNamespace();
                    decoder.Skip(new XmlQualifiedName("CertificateIdentifier", Namespaces.OpcUaConfig));
                }

                trustList.TrustedCertificates = certs;
                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("TrustedCertificates", Namespaces.OpcUaConfig));
            }

            decoder.PopNamespace();
            decoder.Skip(new XmlQualifiedName(elementName, Namespaces.OpcUaConfig));
            return trustList;
        }

        #endregion

        #region CertificateIdentifier

        internal static void EncodeCertificateIdentifier(
            XmlEncoder encoder,
            CertificateIdentifier cert)
        {
            // Order 10: StoreType (EmitDefaultValue=false)
            if (cert.StoreType != null)
            {
                encoder.WriteString("StoreType", cert.StoreType);
            }

            // Order 15: StorePath (EmitDefaultValue=false)
            if (cert.StorePath != null)
            {
                encoder.WriteString("StorePath", cert.StorePath);
            }

            // Order 40: SubjectName (EmitDefaultValue=false)
            if (cert.SubjectName != null)
            {
                encoder.WriteString("SubjectName", cert.SubjectName);
            }

            // Order 50: Thumbprint (EmitDefaultValue=false)
            if (cert.Thumbprint != null)
            {
                encoder.WriteString("Thumbprint", cert.Thumbprint);
            }

            // Order 60: RawData (EmitDefaultValue=false)
            if (cert.RawData != null)
            {
                encoder.WriteByteString("RawData", new ByteString(cert.RawData));
            }

            // Order 70: ValidationOptions (DataMember Name="ValidationOptions", EmitDefaultValue=false)
            if (cert.XmlEncodedValidationOptions != 0)
            {
                encoder.WriteInt32("ValidationOptions", cert.XmlEncodedValidationOptions);
            }

            // Order 80: CertificateType (NodeId, EmitDefaultValue=false)
            if (!cert.CertificateType.IsNull)
            {
                encoder.WriteNodeId("CertificateType", cert.CertificateType);
            }

            // Order 90: CertificateTypeString (EmitDefaultValue=false) – computed from CertificateType
            if (!cert.CertificateType.IsNull)
            {
                string certTypeStr = cert.CertificateTypeString;
                if (certTypeStr != null)
                {
                    encoder.WriteString("CertificateTypeString", certTypeStr);
                }
            }
        }

        internal static CertificateIdentifier DecodeCertificateIdentifier(XmlParser decoder)
        {
            var cert = new CertificateIdentifier();
            cert.StoreType = decoder.ReadString("StoreType");
            cert.StorePath = decoder.ReadString("StorePath");
            cert.SubjectName = decoder.ReadString("SubjectName");
            cert.Thumbprint = decoder.ReadString("Thumbprint");

            ByteString rawData = decoder.ReadByteString("RawData");
            if (!rawData.IsNull)
            {
                cert.RawData = rawData.Memory.ToArray();
            }

            cert.XmlEncodedValidationOptions = decoder.ReadInt32("ValidationOptions");

            NodeId certType = decoder.ReadNodeId("CertificateType");
            if (!certType.IsNull)
            {
                cert.CertificateType = certType;
            }

            // CertificateTypeString setter also sets CertificateType
            string certTypeStr = decoder.ReadString("CertificateTypeString");
            if (certTypeStr != null)
            {
                cert.CertificateTypeString = certTypeStr;
            }

            return cert;
        }

        #endregion

        #region ConfiguredEndpointCollection

        internal static void EncodeConfiguredEndpointCollection(
            XmlEncoder encoder,
            ConfiguredEndpointCollection collection)
        {
            // Order 1: KnownHosts (ArrayOf<string>)
            encoder.WriteStringArray("KnownHosts", collection.KnownHosts);

            // Order 2: Endpoints (List<ConfiguredEndpoint>)
            if (collection.Endpoints != null && collection.Endpoints.Count > 0)
            {
                encoder.Push("Endpoints", Namespaces.OpcUaConfig);
                foreach (ConfiguredEndpoint endpoint in collection.Endpoints)
                {
                    encoder.Push("ConfiguredEndpoint", Namespaces.OpcUaConfig);
                    EncodeConfiguredEndpoint(encoder, endpoint);
                    encoder.Pop();
                }

                encoder.Pop();
            }

            // Order 3: TcpProxyUrl (EmitDefaultValue=false)
            if (collection.TcpProxyUrl != null)
            {
                encoder.WriteString("TcpProxyUrl", collection.TcpProxyUrl.ToString());
            }
        }

        internal static void DecodeConfiguredEndpointCollection(
            XmlParser decoder,
            ConfiguredEndpointCollection collection)
        {
            collection.KnownHosts = decoder.ReadStringArray("KnownHosts");

            if (decoder.Peek("Endpoints"))
            {
                decoder.ReadStartElement();
                decoder.PushNamespace(Namespaces.OpcUaConfig);
                while (decoder.Peek("ConfiguredEndpoint"))
                {
                    decoder.ReadStartElement();
                    decoder.PushNamespace(Namespaces.OpcUaConfig);
                    var endpoint = DecodeConfiguredEndpoint(decoder);
                    endpoint.Collection = collection;
                    collection.Endpoints.Add(endpoint);
                    decoder.PopNamespace();
                    decoder.Skip(new XmlQualifiedName("ConfiguredEndpoint", Namespaces.OpcUaConfig));
                }

                decoder.PopNamespace();
                decoder.Skip(new XmlQualifiedName("Endpoints", Namespaces.OpcUaConfig));
            }

            string tcpProxy = decoder.ReadString("TcpProxyUrl");
            if (tcpProxy != null)
            {
                collection.TcpProxyUrl = new Uri(tcpProxy);
            }
        }

        #endregion

        #region ConfiguredEndpoint

        internal static void EncodeConfiguredEndpoint(XmlEncoder encoder, ConfiguredEndpoint endpoint)
        {
            // Order 1: Endpoint (DataMember Name="Endpoint") = Description (EndpointDescription)
            if (endpoint.Description != null)
            {
                encoder.WriteEncodeable("Endpoint", endpoint.Description);
            }

            // Order 2: Configuration (EndpointConfiguration, IEncodeable)
            if (endpoint.Configuration != null)
            {
                encoder.WriteEncodeable("Configuration", endpoint.Configuration);
            }

            // Order 3: UpdateBeforeConnect
            encoder.WriteBoolean("UpdateBeforeConnect", endpoint.UpdateBeforeConnect);

            // Order 4: BinaryEncodingSupport (plain enum name, not Name_Value)
            encoder.WriteString("BinaryEncodingSupport", endpoint.BinaryEncodingSupport.ToString());

            // Order 5: SelectedUserTokenPolicy (DataMember Name="SelectedUserTokenPolicy")
            encoder.WriteInt32("SelectedUserTokenPolicy", endpoint.SelectedUserTokenPolicyIndex);

            // Order 6: UserIdentity (polymorphic UserIdentityToken, use ExtensionObject)
            if (endpoint.UserIdentity != null)
            {
                encoder.WriteExtensionObject("UserIdentity", new ExtensionObject(endpoint.UserIdentity));
            }

            // Order 8: ReverseConnect
            if (endpoint.ReverseConnect != null)
            {
                encoder.Push("ReverseConnect", Namespaces.OpcUaConfig);
                EncodeReverseConnectEndpoint(encoder, endpoint.ReverseConnect);
                encoder.Pop();
            }

            // Order 9: Extensions (ArrayOf<XmlElement>, EmitDefaultValue=false)
            if (!endpoint.Extensions.IsNull)
            {
                encoder.WriteXmlElementArray("Extensions", endpoint.Extensions);
            }
        }

        internal static ConfiguredEndpoint DecodeConfiguredEndpoint(XmlParser decoder)
        {
            var endpoint = new ConfiguredEndpoint();

            endpoint.SetDescription(decoder.ReadEncodeable<EndpointDescription>("Endpoint"));
            endpoint.Configuration = decoder.ReadEncodeable<EndpointConfiguration>("Configuration");
            endpoint.UpdateBeforeConnect = decoder.ReadBoolean("UpdateBeforeConnect");

            string binaryEncStr = decoder.ReadString("BinaryEncodingSupport");
            if (binaryEncStr != null)
            {
                endpoint.BinaryEncodingSupport = ParseConfigEnum<BinaryEncodingSupport>(binaryEncStr);
            }

            endpoint.SelectedUserTokenPolicyIndex = decoder.ReadInt32("SelectedUserTokenPolicy");

            ExtensionObject userIdentityExt = decoder.ReadExtensionObject("UserIdentity");
            if (userIdentityExt.TryGetEncodeable(out IEncodeable identityBody) && identityBody is UserIdentityToken token)
            {
                endpoint.UserIdentity = token;
            }

            endpoint.ReverseConnect = DecodeOptionalObject(decoder, "ReverseConnect",
                DecodeReverseConnectEndpoint);

            endpoint.Extensions = decoder.ReadXmlElementArray("Extensions");
            return endpoint;
        }

        #endregion

        #region ReverseConnectEndpoint

        internal static void EncodeReverseConnectEndpoint(
            XmlEncoder encoder,
            ReverseConnectEndpoint endpoint)
        {
            encoder.WriteBoolean("Enabled", endpoint.Enabled);
            encoder.WriteString("ServerUri", endpoint.ServerUri);
            encoder.WriteString("Thumbprint", endpoint.Thumbprint);
        }

        internal static ReverseConnectEndpoint DecodeReverseConnectEndpoint(XmlParser decoder)
        {
            var endpoint = new ReverseConnectEndpoint();
            endpoint.Enabled = decoder.ReadBoolean("Enabled");
            endpoint.ServerUri = decoder.ReadString("ServerUri");
            endpoint.Thumbprint = decoder.ReadString("Thumbprint");
            return endpoint;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Parses an enum from a string that may be in either "Name_Value" or plain "Name" format.
        /// Handles the format used by DataContractSerializer for OPC UA config enums.
        /// </summary>
        private static T ParseConfigEnum<T>(string value) where T : struct, Enum
        {
            if (string.IsNullOrEmpty(value))
            {
                return default;
            }

            // Try parsing numeric suffix first: "Name_Value" → extract int after last '_'
            int idx = value.LastIndexOf('_');
            // CA1846: using Substring for net472/netstandard2.0 compat; TODO: use AsSpan when min target supports span-based TryParse
#pragma warning disable CA1846
            if (idx >= 0 && int.TryParse(
                value.Substring(idx + 1),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int intVal))
#pragma warning restore CA1846
            {
                try
                {
                    return (T)(object)intVal;
                }
                catch
                {
                    // Fall through to string parse
                }
            }

            // Plain name (e.g., "Optional" for BinaryEncodingSupport)
            return Enum.TryParse<T>(value, false, out T result) ? result : default;
        }

        /// <summary>
        /// Decodes an optional nested DataContract object using the Peek/ReadStartElement/decode/Skip pattern.
        /// </summary>
        private static T DecodeOptionalObject<T>(
            XmlParser decoder,
            string elementName,
            Func<XmlParser, T> decode) where T : class
        {
            if (!decoder.Peek(elementName))
            {
                return null;
            }

            decoder.ReadStartElement();
            decoder.PushNamespace(Namespaces.OpcUaConfig);
            T result = decode(decoder);
            decoder.PopNamespace();
            decoder.Skip(new XmlQualifiedName(elementName, Namespaces.OpcUaConfig));
            return result;
        }

        #endregion
    }

    /// <summary>
    /// Partial extension to expose an internal setter for <see cref="ConfiguredEndpoint.Description"/>.
    /// </summary>
    public partial class ConfiguredEndpoint
    {
        internal void SetDescription(EndpointDescription description)
        {
            m_description = description ?? new EndpointDescription();
        }
    }
}
