using System;
using System.Text;
using System.Threading.Tasks;
using Amqp;
using Amqp.Framing;
using Amqp.Sasl;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Opc.Ua.Publisher
{
    [CollectionDataContract(Name = "ListOfAmqpConnectionConfigurations", Namespace = Namespaces.OpcUaConfig, ItemName = "AmqpConnectionConfiguration")]
    public partial class AmqpConnectionCollection : List<AmqpConnection>
    {
        public AmqpConnectionCollection()
        {
        }

        public static AmqpConnectionCollection Load(ApplicationConfiguration configuration)
        {
            return configuration.ParseExtension<AmqpConnectionCollection>();
        }
    }

    [DataContract(Name = "AmqpConnectionConfiguration", Namespace = Namespaces.OpcUaConfig)]
    public class AmqpConnection
    {
        #region Serialized Configuration Properties

        [DataMember(Order = 1, IsRequired = true)]
        public string Name { get; set; }

        [DataMember(Order = 2, IsRequired = true)]
        public string Host { get; set; }

        [DataMember(Order = 3, IsRequired = false)]
        public int Port { get; set; }

        [DataMember(Order = 4, IsRequired = true)]
        public string Endpoint { get; set; }

        [DataMember(Order = 5, IsRequired = false)]
        public string WebSocketEndpoint { get; set; }

        [DataMember(Order = 6, IsRequired = false)]
        public string KeyName { get; set; }

        [DataMember(Order = 7, IsRequired = false)]
        public string KeyValue { get; set; }

        [DataMember(Order = 8, IsRequired = false)]
        public string KeyEncoding { get; set; }

        [DataMember(Order = 9, IsRequired = false)]
        public bool UseCbs { get; set; }

        [DataMember(Order = 10, IsRequired = false)]
        public string TokenType { get; set; }

        [DataMember(Order = 11, IsRequired = false)]
        public string TokenScope { get; set; }

        [DataMember(Order = 12, IsRequired = false)]
        public int TokenLifetime { get; set; }

        #endregion

        #region Private members

        private Connection m_connection;
        private Session m_session;
        private SenderLink m_link;
        private Queue<ArraySegment<byte>> messages;

        private DateTime m_currentExpiryTime;
        private Timer m_tokenRenewalTimer;
        private bool m_closed;
        private int m_counter;

        #endregion

        #region Constructor

        /// <summary>
        /// Default Constructor 
        /// </summary>
        public AmqpConnection()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Initialize the connection class
        /// </summary>
        private void Initialize()
        {
            messages = new Queue<ArraySegment<byte>>();
        }

        #endregion

        /// <summary>
        /// Open the connection
        /// </summary>
        /// <returns></returns>
        public async Task OpenAsync()
        {
            Close();

            ConnectionFactory factory = new ConnectionFactory();
            factory.AMQP.ContainerId = Guid.NewGuid().ToString();
            if (UseCbs)
            {
                factory.SASL.Profile = SaslProfile.External;
            }

            m_connection = await factory.CreateAsync(GetAddress());
            m_connection.Closed += new ClosedCallback(OnConnectionClosed);

            if (UseCbs && KeyName != null && KeyValue != null)
            {
                await StartCbs();
            }
            else
            {
                await ResetLinkAsync();
            }

            Utils.Trace("AMQP Connection opened, connected to '{0}'...", Endpoint);

            m_closed = false;

            // Push out the messages we have so far
            await Task.Run(new Action(SendAll));
        }

        /// <summary>
        /// Publish a JSON message
        /// </summary>
        /// <param name="body"></param>
        public void Publish(ArraySegment<byte> body)
        {
            lock (messages)
            {
                messages.Enqueue(body);
            }

            if (IsClosed())
            {
                Task.Run(OpenAsync);
            }
            else
            {
                // Push out the messages we have so far
                Task.Run(new Action(SendAll));
            }
        }

        /// <summary>
        /// Work until all messages have been send...
        /// </summary>
        protected void SendAll()
        {
            try
            {
                lock (messages)
                {
                    while (!IsClosed() && messages.Count != 0)
                    {
                        bool sent = SendOne(messages.Peek());
                        if (sent)
                        {
                            messages.Dequeue();
                        }
                    }
                }
            }
            catch (Exception)
            {
                Close();
            }
        }

        /// <summary>
        /// Send message
        /// </summary>
        /// <param name="body"></param>
        /// <returns>Whether message was sent</returns>
        protected bool SendOne(ArraySegment<byte> body)
        {
            m_counter++;

            if (IsClosed())
            {
                return false;
            }

            var istrm = new MemoryStream(body.Array, body.Offset, body.Count, false);

            Message message = new Message()
            {
                BodySection = new Data() { Binary = istrm.ToArray() }
            };

            message.Properties = new Properties()
            {
                MessageId = Guid.NewGuid().ToString(),
                ContentType = "application/opcua+json"
            };

            if (m_link != null)
            {
                m_link.Send(message, 6000);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Close and therefore dispose of all resources
        /// </summary>
        public void Close()
        {
            m_closed = true;
            Dispose(true);
        }

        /// <summary>
        /// is the connection closed?
        /// </summary>
        /// <returns>true or false</returns>
        public bool IsClosed()
        {
            return m_closed;
        }

        /// <summary>
        /// Destructor
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Close all resources
        /// </summary>
        /// <param name="disposing"></param>
        public virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (m_tokenRenewalTimer != null)
                {
                    m_tokenRenewalTimer.Dispose();
                    m_tokenRenewalTimer = null;
                }

                if (m_link != null)
                {
                    try
                    {
                        m_link.Close(3000);
                    }
                    catch(Exception)
                    {
                    }
                    m_link = null;
                }

                if (m_session != null)
                {
                    try
                    {
                        m_session.Close(3000);
                    }
                    catch (Exception)
                    {
                    }
                    m_session = null;
                }

                if (m_connection != null)
                {
                    try
                    {
                        m_connection.Close(3000);
                    }
                    catch (Exception)
                    {
                    }
                    m_connection = null;
                }
            }
        }

        /// <summary>
        /// Returns the amqp.net lite broker address to connect to.
        /// </summary>
        /// <returns>Address to connect to</returns>
        protected Address GetAddress()
        {
            if (Port == 0)
            {
                // Set default port
                if (WebSocketEndpoint != null)
                    Port = 443;
                else
                    Port = 5671;
            }

            if (WebSocketEndpoint != null)
            {
                return new Address(Host, Port, null, null, WebSocketEndpoint, "wss");
            }
            else if (UseCbs)
            {
                return new Address(Host, Port);
            }
            else
            {
                return new Address(Host, Port, KeyName, KeyValue);
            }
        }

        /// <summary>
        /// Start cbs protocol on the underlying connection
        /// </summary>
        /// <returns>Task to wait on</returns>
        protected async Task StartCbs()
        {
            if (m_connection == null)
            {
                throw new Exception("No connection to run cbs renewal on!");
            }

            if (TokenType == null || TokenScope == null)
            {
                throw new Exception("Must specifiy token scope and type");
            }

            if (TokenLifetime == 0)
            {
                TokenLifetime = 60000;
            }

            // Ensure we have a token
            await RenewTokenAsync(GenerateSharedAccessToken());

            // then start the periodic renewal
            int interval = (int)(TokenLifetime * 0.8);
            m_tokenRenewalTimer = new Timer(OnTokenRenewal, null, interval, interval);
        }

        /// <summary>
        /// Return decoded key from configured key value
        /// </summary>
        /// <returns>decoded key</returns>
        protected byte[] DecodeKey()
        {
            if (!KeyEncoding.Equals("base64", StringComparison.CurrentCultureIgnoreCase))
            {
                return Encoding.UTF8.GetBytes(KeyValue);
            }
            else
            {
                return Convert.FromBase64String(KeyValue);
            }
        }

        /// <summary>
        /// Generate token for member values
        /// </summary>
        /// <returns>Token string</returns>
        protected string GenerateSharedAccessToken()
        {
            m_currentExpiryTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(TokenLifetime);
            return GenerateSharedAccessToken(KeyName, DecodeKey(), TokenScope, TokenLifetime);
        }


        /// <summary>
        /// Callback for connection close events
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="error"></param>
        protected virtual void OnConnectionClosed(AmqpObject sender, Error error)
        {
            if (error != null)
            {
                Debug.WriteLine("Connection Closed {0} {1}", error.Condition, error.Description);
            }
            m_closed = true;
        }

        /// <summary>
        /// Callback for session close event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="error"></param>
        protected virtual void OnSessionClosed(AmqpObject sender, Error error)
        {
            if (error != null)
            {
                Debug.WriteLine("Session Closed {0} {1}", error.Condition, error.Description);
            }
        }

        /// <summary>
        /// Callback for link close events
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="error"></param>
        protected virtual void OnLinkClosed(AmqpObject sender, Error error)
        {
            if (error != null)
            {
                Debug.WriteLine("Link Closed {0} {1}", error.Condition, error.Description);
            }
        }

        /// <summary>
        /// Timer callback for token renewal
        /// </summary>
        /// <param name="state"></param>
        private void OnTokenRenewal(object state)
        {
            try
            {
                lock (messages)
                {
                    bool result = RenewTokenAsync(GenerateSharedAccessToken()).Wait(60000);
                    if (!result)
                    {
                        Utils.Trace( "Unexpected timeout error renewing token.");
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error renewing token.");

                var ae = e as AggregateException;

                if (ae != null)
                {
                    foreach (var ie in ae.InnerExceptions)
                    {
                        Utils.Trace("[{0}] {1}", ie.GetType().Name, ie.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Reset the link and session
        /// </summary>
        /// <returns>Task to wait on</returns>
        private async Task ResetLinkAsync()
        {
            SenderLink link;
            Session session;

            session = new Session(m_connection);
            session.Closed += new ClosedCallback(OnSessionClosed);

            link = new SenderLink(session, Guid.NewGuid().ToString(), Endpoint);
            link.Closed += new ClosedCallback(OnLinkClosed);

            if (m_link != null)
            {
                await m_link.CloseAsync();
            }

            if (m_session != null)
            {
                await m_session.CloseAsync();
            }

            m_session = session;
            m_link = link;
        }

        /// <summary>
        /// renews the cbs token
        /// </summary>
        /// <param name="sharedAccessToken">token to renew</param>
        /// <returns>Task to wait on</returns>
        private async Task RenewTokenAsync(string sharedAccessToken)
        {
            var session = new Session(m_connection);
            string cbsClientAddress = "cbs-client-reply-to";
            var cbsSender = new SenderLink(session, "cbs-sender", "$cbs");
            var receiverAttach = new Attach()
            {
                Source = new Source() { Address = "$cbs" },
                Target = new Target() { Address = cbsClientAddress }
            };
            var cbsReceiver = new ReceiverLink(session, "cbs-receiver", receiverAttach, null);

            // construct the put-token message
            var request = new Message(sharedAccessToken);
            request.Properties = new Properties();
            request.Properties.MessageId = "1";
            request.Properties.ReplyTo = cbsClientAddress;
            request.ApplicationProperties = new ApplicationProperties();

            request.ApplicationProperties["operation"] = "put-token";
            request.ApplicationProperties["type"] = TokenType;
            request.ApplicationProperties["name"] = TokenScope;

            await cbsSender.SendAsync(request);

            // receive the response
            var response = await cbsReceiver.ReceiveAsync();
            if (response == null || response.Properties == null || response.ApplicationProperties == null)
            {
                throw new Exception("invalid response received");
            }

            int statusCode = (int)response.ApplicationProperties["status-code"];

            await cbsSender.CloseAsync();
            await cbsReceiver.CloseAsync();
            await session.CloseAsync();

            if (statusCode != (int)HttpStatusCode.Accepted && statusCode != (int)HttpStatusCode.OK)
            {
                throw new Exception("put-token message was not accepted. Error code: " + statusCode);
            }

            // Now create new link
            await ResetLinkAsync();
        }

        /// <summary>
        /// Sas token generation
        /// </summary>
        /// <param name="keyName"></param>
        /// <param name="key"></param>
        /// <param name="tokenScope"></param>
        /// <param name="ttl"></param>
        /// <returns>shared access token</returns>
        private static string GenerateSharedAccessToken(string keyName, byte[] key, string tokenScope, int ttl)
        {
            // http://msdn.microsoft.com/en-us/library/azure/dn170477.aspx
            // signature is computed from joined encoded request Uri string and expiry string

            DateTime expiryTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(ttl);
            string expiry =
                ((long)(expiryTime - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds).ToString();
            string encodedScope = Uri.EscapeDataString(tokenScope);
            string sig;

            // the connection string signature is base64 encoded
            using (var hmac = new HMACSHA256(key))
            {
                sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(encodedScope + "\n" + expiry)));
            }

            return string.Format(
                "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}",
                encodedScope,
                Uri.EscapeDataString(sig),
                Uri.EscapeDataString(expiry),
                Uri.EscapeDataString(keyName)
                );
        }
    }
}
