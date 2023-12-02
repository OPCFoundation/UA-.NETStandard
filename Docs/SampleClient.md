## Build A Simple Opc Client
Brief description or introduction of your project.
## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
  - [OPC Configuration](#opc-configuration)
  - [C# Project Setup](#c-project-setup)
  - [Endpoint Configuration](#endpoint-configuration)
- [Application Object and Client Configuration](#application-object-and-client-configuration)
- [Security Configuration and Certificate Handling](#security-configuration-and-certificate-handling)
- [Create Endpoint URL](#create-endpoint-url)
- [Read Value from NodeId](#read-value-from-nodeid)
- [Subscribe to NodeId Changes](#subscribe-to-nodeid-changes)

## Overview
Provide a high-level overview of your project. What does it do? What problem does it solve?


## Create an OPC UA Client Using the Kep Server
        1. Open the First OPC Configuration Manager and export the server certificate from the Certificate Section in the Configuration Manager.
        2. After exporting certificates, import into  client certificate sections.
        3. Create a C# project and install the nuget package of the Opc Foundation UA client.
        4. Add an endpoint url in the OPCS configuration manager, choose your security mode, and update the Choose Network type.
               
## Create Application Object First
        ApplicationInstance application = new ApplicationInstance();
        application.ApplicationName = "My OPC UA Client";
        application.ApplicationType = ApplicationType.Client;    

## Create Client Configuration
        
        ApplicationConfiguration config = new ApplicationConfiguration();
        config.ApplicationName = application.ApplicationName;
        config.ApplicationType = application.ApplicationType;
        config.ApplicationUri = Utils.Format(@"urn:{0}:{1}", System.Net.Dns.GetHostName(), application.ApplicationName);
        config.ProductUri = "https://github.com/opcfoundation/UA-.NETStandard";
        config.ClientConfiguration = new ClientConfiguration();
        string currentDir = System.IO.Directory.GetCurrentDirectory();
          

## Add Security Configuration Certificates
        // add client certificate.
        // First Save Certificate in particular Directory .
        // and give path to it.

        config.SecurityConfiguration = new SecurityConfiguration()
            {
        
                ApplicationCertificate= new CertificateIdentifier
                {
                    StoreType= @"Directory",
                    StorePath= currentDir + @"/Cert/TrustedIssuer/",
                    SubjectName= config.ApplicationUri
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = @"Directory",
                    StorePath = currentDir + @"/Cert/TrustedIssuer/",
                    
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = @"Directory",
                    StorePath = currentDir + @"/Cert/TrustedIssuer/",

                },
                RejectedCertificateStore = new CertificateStoreIdentifier
                {
                    StoreType = @"Directory",
                    StorePath = currentDir + @"/Cert/TrustedIssuer/",
                  
                },
                AutoAcceptUntrustedCertificates=true,
                AddAppCertToTrustedStore = true,
                RejectSHA1SignedCertificates=false,
        };

## Validate Path Of Certificate
        config.Validate(ApplicationType.Client).GetAwaiter().GetResult();

## Create Endpoint Url
          
        var endpointUrl = "opc.tcp://localhost:49320";
        var endpoint = new ConfiguredEndpoint(null, new EndpointDescription(endpointUrl), EndpointConfiguration.Create(config));

          
        // create session with username and password
        var session = Session.Create(config, endpoint, true, "MySession", 60000,
                 new UserIdentity("username","password"),
                null).Result;
            session.KeepAliveInterval = 2000;

## Read Value from NodeId
        DataValue value = session.ReadValue("ns=2;s=Channel1.Device1.AI_MOV1_POS");

## Subscribe From Nodeid
        // Create a subscription object with a sampling interval of 1000 ms
        Subscription subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 3000 };
        session.AddSubscription(subscription);
        subscription.Create();

        // Create a monitored item for the tag with a sampling interval of 1000 ms
        MonitoredItem monitoredItem = new MonitoredItem(subscription.DefaultItem)
            {
               StartNodeId = "ns=2;s=Channel1.Device1.AI_MOV1_POS",
               AttributeId = Attributes.Value,
               SamplingInterval = 1000
            };
        monitoredItem.Notification += OnMonitoredItemNotification;
        subscription.AddItem(monitoredItem);
        subscription.ApplyChanges();

        // Handle the notification event when the value of the tag changes
        void OnMonitoredItemNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
               dynamic value = e.NotificationValue;
               Console.WriteLine("Value of tag " + value.Value.Value);
        }
