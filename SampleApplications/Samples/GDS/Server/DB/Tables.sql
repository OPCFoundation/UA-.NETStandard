SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
USE gdsdb
GO

IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ApplicationNames]') AND type in (N'U'))
DROP TABLE [dbo].ApplicationNames
GO

IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ServerEndpoints]') AND type in (N'U'))
DROP TABLE [dbo].ServerEndpoints
GO

IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Realms]') AND type in (N'U'))
DROP TABLE [dbo].Realms
GO

IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Certificates]') AND type in (N'U'))
DROP TABLE [dbo].Certificates
GO

IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CertificateRequests]') AND type in (N'U'))
DROP TABLE [dbo].CertificateRequests
GO

IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Applications]') AND type in (N'U'))
DROP TABLE [dbo].Applications
GO

IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CertificateStores]') AND type in (N'U'))
DROP TABLE [dbo].CertificateStores
GO

CREATE TABLE [dbo].CertificateStores (    
	[ID] INT NOT NULL PRIMARY KEY IDENTITY, 
    [Path] NVARCHAR(256) NOT NULL,
    [AuthorityId] NVARCHAR(50)  NULL,
    CONSTRAINT [AK_CertificateStores_Path] UNIQUE NONCLUSTERED ([Path] ASC)
);

CREATE TABLE [dbo].Applications (
	[ID] INT NOT NULL PRIMARY KEY IDENTITY, 
    [ApplicationId] uniqueidentifier NOT NULL,
    [ApplicationUri] NVARCHAR(1000) NOT NULL,
    [ApplicationName] NVARCHAR(100) NOT NULL,
    [ApplicationType] INT NOT NULL,
    [ProductUri] NVARCHAR(1000) NOT NULL,
    [ServerCapabilities] NVARCHAR(500) NOT NULL,
    [Certificate] VARBINARY(MAX) NULL,
    [HttpsCertificate] VARBINARY(MAX) NULL,
    [TrustListId] INT NULL,
    [HttpsTrustListId] INT NULL,
    CONSTRAINT [AK_Applications_ApplicationId] UNIQUE NONCLUSTERED ([ApplicationId] ASC),
	CONSTRAINT [FK_Applications_TrustListId] FOREIGN KEY (TrustListId) REFERENCES [CertificateStores]([ID]) ON DELETE NO ACTION ON UPDATE NO ACTION,
	CONSTRAINT [FK_Applications_HttpsTrustListId] FOREIGN KEY (HttpsTrustListId) REFERENCES [CertificateStores]([ID]) ON DELETE NO ACTION ON UPDATE NO ACTION,
);

CREATE TABLE [dbo].[CertificateRequests]
(    
	[ID] INT NOT NULL PRIMARY KEY IDENTITY, 
    [RequestId]     UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] INT              NOT NULL,
    [AuthorityId]   NVARCHAR(50)     NULL,
    [State]         INT              DEFAULT ((0)) NOT NULL,
    [Certificate]   VARBINARY (MAX)  NULL,
    [PrivateKey]    VARBINARY (MAX)  NULL,
    CONSTRAINT [AK_CertificateRequests_RequestId] UNIQUE NONCLUSTERED ([RequestId] ASC), 
    CONSTRAINT [FK_CertificateRequests_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [Applications]([ID])
)

CREATE TABLE [dbo].ServerEndpoints (    
	[ID] INT NOT NULL PRIMARY KEY IDENTITY, 
    [ApplicationId] INT NOT NULL,
    [DiscoveryUrl] NVARCHAR(500) NOT NULL,
	CONSTRAINT [FK_ServerEndpoints_ApplicationId] FOREIGN KEY ([ApplicationId]) REFERENCES [Applications]([ID]) ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE [dbo].ApplicationNames (    
	[ID] INT NOT NULL PRIMARY KEY IDENTITY, 
    [ApplicationId] INT NOT NULL,
    [Locale] NVARCHAR(10) NOT NULL,
    [Text] NVARCHAR(500) NOT NULL,
	CONSTRAINT [FK_ApplicationNames_ApplicationId] FOREIGN KEY ([ApplicationId]) REFERENCES [Applications]([ID]) ON DELETE CASCADE ON UPDATE CASCADE
);