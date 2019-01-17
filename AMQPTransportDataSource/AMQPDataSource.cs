using Amqp;
using Amqp.Framing;
using Amqp.Sasl;
using DataSource;
using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace AMQPTransportDataSource
{
    public class AMQPDataSource : BaseDataSource
    {
        string m_Format = "json";

        Connection Connection = null;
        Session Session = null;
        BrokerSettings Settings = new BrokerSettings();
        ReceiverLink Receiverlink = null;
        SenderLink Senderlink = null;

        #region PrivateClass

        private class BrokerSettings
        {
            public string Broker = "amqps://opcfoundation-prototyping.org";
            public string UserName = "sender";
            public string Password = "password";
            public bool UseSasl;
            public string AmqpNodeName = "topic://Test";
        }

        #endregion

        #region Private Methods

        private static void OnClosed(AmqpObject sender, Error error)
        {
            //Update a msg to the user
        }

        #endregion

        #region Public Static Members

        public static Connection ReceiverConnection = null;
        public static Session ReceiverSession = null;
        public static ReceiverLink ReceiverLink = null;

        #endregion

        #region Public Methods

        public async Task<bool> Initialize(string format, string address)
        {
            m_Format = format;
            //"amqps://opcfoundation-prototyping.org";
            Settings.Broker = address;
            System.Net.ServicePointManager.ServerCertificateValidationCallback += RemoteCertificateValidation;
            try
            {
                UriBuilder url = new UriBuilder(Settings.Broker);

                if (!Settings.UseSasl)
                {
                    if (!String.IsNullOrEmpty(Settings.UserName))
                    {
                        url.UserName = Uri.EscapeDataString(Settings.UserName);
                        url.Password = Uri.EscapeDataString(Settings.Password);
                    }
                }

                url.Path += Settings.AmqpNodeName;

                var url_address = new Address(url.ToString());

                ConnectionFactory factory = new ConnectionFactory();
                factory.SSL.RemoteCertificateValidationCallback += RemoteCertificateValidation;

                if (Settings.UseSasl)
                {
                    factory.SASL.Profile = SaslProfile.External;
                }

                Connection = await factory.CreateAsync(url_address);
                Connection.Closed += new ClosedCallback(OnClosed);


                Session = new Session(Connection);
                Session.Closed += new ClosedCallback(OnClosed);

                Senderlink = new SenderLink(Session, "sender-spout", Settings.AmqpNodeName);
                Senderlink.Closed += new ClosedCallback(OnClosed);

                Receiverlink = new ReceiverLink(Session, "receiver-drain", Settings.AmqpNodeName);
                Receiverlink.Closed += new ClosedCallback(OnClosed);

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public override bool SendData(byte[] data, Dictionary<string, object> settings)
        {
            try
            {
                Message message = new Message()
                {
                    BodySection = new Data() { Binary = data }
                };
                Senderlink.SendAsync(message);
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public override bool ReceiveData(string queueName)
        {
            try
            {

                Receiverlink.Start(5, OnMessageCallback);
                ReceiverConnection = Connection;
                ReceiverSession = Session;
                ReceiverLink = Receiverlink;
                return true;
            }
            catch (Exception e)
            {

            }
            return false;
        }

        public static bool RemoteCertificateValidation(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }


        public void OnMessageCallback(ReceiverLink receiver, Message message)
        {
            try
            {
                receiver.Accept(message);
                receiver.SetCredit(5);
                string result = System.Text.Encoding.UTF8.GetString(message.Body as byte[]);
                OnDataReceived(result);
            }
            catch (Exception e)
            {

            }
        }

        #endregion


    }
}
