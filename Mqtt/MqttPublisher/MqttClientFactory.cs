using System;
using System.Net;
using System.Net.Security;
using System.Threading.Tasks;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Exceptions;
using uPLibrary.Networking.M2Mqtt.Messages;
using Microsoft.Azure.Devices.Common.Security;
using Opc.Ua;

namespace MqttSamplePublisher
{
    public class MqttClientFactory
    {
        public event EventHandler<LogMessageEventArgs> LogMessage;

        private void Log(string format, params object[] args)
        {
            if (LogMessage != null)
            {
                string message = format;

                if (args != null && args.Length > 0)
                {
                    message = String.Format(CultureInfo.InvariantCulture, format, args);
                }

                LogMessage(typeof(MqttClientFactory), new LogMessageEventArgs(message));
            }
        }

        private bool ServerCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // always trust the broker certificate.
            return true;
        }

        private async Task<X509Certificate2> FindIssuer(ApplicationConfiguration configuration, string subjectName)
        {
            using (DirectoryCertificateStore store = (DirectoryCertificateStore)configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore())
            {
                foreach (var ii in await store.Enumerate())
                {
                    if (Utils.CompareDistinguishedName(ii.Subject, subjectName))
                    {
                        return store.LoadPrivateKey(ii.Thumbprint, null, null);
                    }
                }
            }

            return null;
        }

        private int CreateWithAzureSharedKey(ApplicationConfiguration configuration, BrokerSettings settings, X509Certificate2 issuer, out MqttClient client)
        {
            Log("MQTT: Connecting to broker {0} with AzureSharedKey credential {1}", settings.Broker, settings.CredentialName);

            client = null;

            client = new MqttClient(
                settings.Broker, 
                8883, 
                true, 
                MqttSslProtocols.TLSv1_2, 
                ServerCertificateValidationCallback, 
                null);

            string userName = settings.Broker + "/" + settings.ClientId;

            // Azure IoT SharedKeys must be generated with this Microsoft specific function.
            var sasBuilder = new SharedAccessSignatureBuilder()
            {
                Key = settings.CredentialSecret,
                Target = String.Format("{0}/devices/{1}", settings.Broker, WebUtility.UrlEncode(settings.ClientId)),
                TimeToLive = TimeSpan.FromDays(Convert.ToDouble(1))
            };

            // SharedAccessSignature sr=<device id>&sig=<shared key>&se=<expiry>
            string password = sasBuilder.ToSignature();

            var result = client.Connect(settings.ClientId, userName, password);

            return result;
        }

        private X509Certificate2 FindCertificate(ApplicationConfiguration configuration, BrokerSettings settings, X509Certificate2 issuer)
        {
            X509Certificate2 certificate = null;

            using (var store = new DirectoryCertificateStore())
            {
                store.Open(configuration.SecurityConfiguration.ApplicationCertificate.StorePath);

                foreach (var ii in store.Enumerate().Result)
                {
                    if (!Utils.CompareDistinguishedName(ii.Issuer, issuer.Issuer))
                    {
                        continue;
                    }

                    // the common name of the client certificate must match the client id.
                    string commonName = null;

                    var fields = Utils.ParseDistinguishedName(ii.Subject);

                    foreach (var jj in fields)
                    {
                        if (jj.StartsWith("CN="))
                        {
                            commonName = jj.Substring(3);
                            break;
                        }

                    }

                    if (commonName == settings.ClientId)
                    {
                        certificate = store.LoadPrivateKey(ii.Thumbprint, null, settings.CredentialSecret);
                        break;
                    }
                }
            }

            return certificate;
        }

        private int CreateWithCertificate(ApplicationConfiguration configuration, BrokerSettings settings, X509Certificate2 issuer, out MqttClient client)
        {
            client = null;

            Log("MQTT: Connecting to broker {0} with Certificate credential {1}", settings.Broker, settings.CredentialName);

            // when using TLS on windows the certificate always have to be in the Windows store
            using (var userStore = new X509CertificateStore())
            {
                userStore.Open("CurrentUser\\Root");

                if (userStore.FindByThumbprint(issuer.Thumbprint) == null)
                {
                    using (var machineStore = new X509CertificateStore())
                    {
                        machineStore.Open("LocalMachine\\Root");

                        if (machineStore.FindByThumbprint(issuer.Thumbprint) == null)
                        {
                            Log("MQTT: Issuer certificate must be in the CurrentUser\\Root or LocalMachine\\Root.");
                            return MqttMsgConnack.CONN_REFUSED_IDENT_REJECTED;
                        }
                    }
                }
            }

            var certificate = FindCertificate(configuration, settings, issuer);

            client = new MqttClient(
                settings.Broker,
                8883,
                true,
                issuer,
                certificate,
                MqttSslProtocols.TLSv1_2);

            var result = client.Connect(settings.ClientId, settings.CredentialName, String.Empty);

            return result;
        }

        private int CreateWithUserName(ApplicationConfiguration configuration, BrokerSettings settings, X509Certificate2 issuer, out MqttClient client)
        {
            Log("MQTT: Connecting to broker {0} with UserName credential {1}", settings.Broker, settings.CredentialName);

            int result = 0;

            client = new MqttClient(
                settings.Broker,
                (settings.UseTls) ? 8883 : 1883,
                settings.UseTls,
                issuer,
                configuration.SecurityConfiguration.ApplicationCertificate.Certificate,
                (settings.UseTls) ? MqttSslProtocols.TLSv1_2 : MqttSslProtocols.None);

            result = client.Connect(settings.ClientId, settings.CredentialName, settings.CredentialSecret);

            return result;
        }
        
        public async Task<MqttClient> Create(ApplicationConfiguration configuration, BrokerSettings settings)
        {
            int result = 0;
            MqttClient client = null;

            X509Certificate2 issuer = await FindIssuer(configuration, "CN=mqtt-prototype-ca");

            await Task.Run(() => {

                switch (settings.CredentialType)
                {
                    case BrokerCredentialType.UserName:
                    {
                        result = CreateWithUserName(configuration, settings, issuer, out client);
                        break;
                    }

                    case BrokerCredentialType.Certificate:
                    {
                        result = CreateWithCertificate(configuration, settings, issuer, out client);
                        break;
                    }

                    case BrokerCredentialType.AzureSharedAccessKey:
                    {
                        result = CreateWithAzureSharedKey(configuration, settings, issuer, out client);
                        break;
                    }
                }

            }).ConfigureAwait(false);

            if (result != MqttMsgConnack.CONN_ACCEPTED)
            {
                string errorText = String.Empty;

                switch (result)
                {
                    case MqttMsgConnack.CONN_REFUSED_IDENT_REJECTED: { errorText = "CONN_REFUSED_IDENT_REJECTED"; break; }
                    case MqttMsgConnack.CONN_REFUSED_NOT_AUTHORIZED: { errorText = "CONN_REFUSED_NOT_AUTHORIZED"; break; }
                    case MqttMsgConnack.CONN_REFUSED_PROT_VERS: { errorText = "CONN_REFUSED_PROT_VERS"; break; }
                    case MqttMsgConnack.CONN_REFUSED_SERVER_UNAVAILABLE: { errorText = "CONN_REFUSED_SERVER_UNAVAILABLE"; break; }
                    case MqttMsgConnack.CONN_REFUSED_USERNAME_PASSWORD: { errorText = "CONN_REFUSED_USERNAME_PASSWORD"; break; }
                }

                throw new MqttConnectionException(errorText, null);
            }

            return client;
        }

        public async Task CreateCertificate(ApplicationConfiguration configuration, string subjectName)
        {
            string issuerName = "CN=mqtt-prototype-ca";
            X509Certificate2 issuer = await FindIssuer(configuration, issuerName);

            if (issuer == null)
            {
                Log("INFO: Creating new issuer certificate: {0}", issuerName);

                issuer = CertificateFactory.CreateCertificate(
                   configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType,
                   configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath,
                   null,
                   null,
                   null,
                   issuerName,
                   null,
                   CertificateFactory.defaultKeySize,
                   DateTime.UtcNow - TimeSpan.FromDays(1),
                   CertificateFactory.defaultLifeTime,
                   CertificateFactory.defaultHashSize,
                   true,
                   null,
                   null);
            }

            Log("INFO: Creating new certificate: {0}", subjectName);

            var certificate = CertificateFactory.CreateCertificate(
                configuration.SecurityConfiguration.ApplicationCertificate.StoreType,
                configuration.SecurityConfiguration.ApplicationCertificate.StorePath,
                null,
                configuration.ApplicationUri,
                null,
                subjectName,
                null,
                CertificateFactory.defaultKeySize,
                DateTime.UtcNow - TimeSpan.FromDays(1),
                CertificateFactory.defaultLifeTime,
                CertificateFactory.defaultHashSize,
                false,
                issuer,
                null);
        }
    }
}
