## PubSub 
## Overview


PubSub Overview

In software architecture, Publish/Subscribe is a messaging pattern where senders do not communicate directly with specific receivers. Instead senders, called Publishers, categorize messages into classes without knowing which receivers, if any, there may be. Similarly receivers, called Subscribers, express interest in one or more classes and only receive messages that are of interest, without knowing which senders, if any, there are.

In February 2018 the OPC Foundation published [Part 14 of the OPC UA Specification](https://reference.opcfoundation.org/v104/Core/docs/Part14/), version 1.04, specifying the OPC UA PubSub communication model. The OPC UA PubSub communication model defines an OPC UA Publish/Subscribe pattern instead of the client/server pattern defined by the services in [Part 4 of the OPC UA Specification](https://reference.opcfoundation.org/v104/Core/docs/Part4/).

OPC UA PubSub is designed to be flexible and is not bound to a particular messaging system.

**Decoupling by use of middleware**

In OPC UA PubSub the participating OPC UA applications can assume the roles of Publishers and Subscribers. Publishers are the sources of data, while Subscribers consume that data. Communication in OPC UA PubSub is message-based. Publishers send messages to a Message-Oriented Middleware, without knowledge of what, if any, Subscribers there may be. Similarly, Subscribers express interest in specific types of data, and process messages that contain this data, without knowledge of what Publishers there are.
![dddd](images%5CMessageOrienbtedMiddleware.png)
Message-Oriented Middleware is a software or hardware infrastructure that supports sending and receiving messages between distributed systems. The implementation of the message distribution depends on the Message-Oriented Middleware.

The image bellow illustrates that for communication Publishers and Subscribers only interact with the Message-Oriented Middleware which provides the means to receive data from one or more senders and forward data to one or more receivers:



