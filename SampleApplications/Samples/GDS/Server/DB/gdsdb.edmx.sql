
-- --------------------------------------------------
-- Entity Designer DDL Script for SQL Server 2005, 2008, 2012 and Azure
-- --------------------------------------------------
-- Date Created: 04/04/2018 18:45:51
-- Generated from EDMX file: C:\Users\mregen\Source\Repos\UA-.NETStandard\SampleApplications\Samples\GDS\Server\gdsdb.edmx
-- --------------------------------------------------

SET QUOTED_IDENTIFIER OFF;
GO
USE [gdsdb];
GO
IF SCHEMA_ID(N'dbo') IS NULL EXECUTE(N'CREATE SCHEMA [dbo]');
GO

-- --------------------------------------------------
-- Dropping existing FOREIGN KEY constraints
-- --------------------------------------------------

IF OBJECT_ID(N'[dbo].[FK_ApplicationNames_ApplicationId]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[ApplicationNames] DROP CONSTRAINT [FK_ApplicationNames_ApplicationId];
GO
IF OBJECT_ID(N'[dbo].[FK_ServerEndpoints_ApplicationId]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[ServerEndpoints] DROP CONSTRAINT [FK_ServerEndpoints_ApplicationId];
GO
IF OBJECT_ID(N'[dbo].[FK_Applications_HttpsTrustListId]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[Applications] DROP CONSTRAINT [FK_Applications_HttpsTrustListId];
GO
IF OBJECT_ID(N'[dbo].[FK_Applications_TrustListId]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[Applications] DROP CONSTRAINT [FK_Applications_TrustListId];
GO
IF OBJECT_ID(N'[dbo].[FK_CertificateRequests_Applications]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[CertificateRequests] DROP CONSTRAINT [FK_CertificateRequests_Applications];
GO

-- --------------------------------------------------
-- Dropping existing tables
-- --------------------------------------------------

IF OBJECT_ID(N'[dbo].[ApplicationNames]', 'U') IS NOT NULL
    DROP TABLE [dbo].[ApplicationNames];
GO
IF OBJECT_ID(N'[dbo].[ServerEndpoints]', 'U') IS NOT NULL
    DROP TABLE [dbo].[ServerEndpoints];
GO
IF OBJECT_ID(N'[dbo].[Applications]', 'U') IS NOT NULL
    DROP TABLE [dbo].[Applications];
GO
IF OBJECT_ID(N'[dbo].[CertificateStores]', 'U') IS NOT NULL
    DROP TABLE [dbo].[CertificateStores];
GO
IF OBJECT_ID(N'[dbo].[CertificateRequests]', 'U') IS NOT NULL
    DROP TABLE [dbo].[CertificateRequests];
GO

-- --------------------------------------------------
-- Creating all tables
-- --------------------------------------------------

-- Creating table 'ApplicationNames'
CREATE TABLE [dbo].[ApplicationNames] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [ApplicationId] int  NOT NULL,
    [Locale] nvarchar(10)  NULL,
    [Text] nvarchar(500)  NOT NULL
);
GO

-- Creating table 'ServerEndpoints'
CREATE TABLE [dbo].[ServerEndpoints] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [ApplicationId] int  NOT NULL,
    [DiscoveryUrl] nvarchar(500)  NOT NULL
);
GO

-- Creating table 'Applications'
CREATE TABLE [dbo].[Applications] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [ApplicationId] uniqueidentifier  NOT NULL,
    [ApplicationUri] nvarchar(1000)  NOT NULL,
    [ApplicationName] nvarchar(1000)  NOT NULL,
    [ApplicationType] int  NOT NULL,
    [ProductUri] nvarchar(1000)  NOT NULL,
    [ServerCapabilities] nvarchar(500)  NULL,
    [Certificate] varbinary(max)  NULL,
    [HttpsCertificate] varbinary(max)  NULL,
    [TrustListId] int  NULL,
    [HttpsTrustListId] int  NULL
);
GO

-- Creating table 'CertificateStores'
CREATE TABLE [dbo].[CertificateStores] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [Path] nvarchar(256)  NOT NULL,
    [AuthorityId] nvarchar(50)  NULL
);
GO

-- Creating table 'CertificateRequests'
CREATE TABLE [dbo].[CertificateRequests] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [RequestId] uniqueidentifier  NOT NULL,
    [ApplicationId] int  NOT NULL,
    [State] int  NOT NULL,
    [CertificateGroupId] nvarchar(100)  NOT NULL,
    [CertificateTypeId] nvarchar(100)  NOT NULL,
    [CertificateSigningRequest] varbinary(max)  NULL,
    [SubjectName] nvarchar(1000)  NULL,
    [DomainNames] nvarchar(max)  NULL,
    [PrivateKeyFormat] nvarchar(3)  NULL,
    [PrivateKeyPassword] nvarchar(100)  NULL,
    [AuthorityId] nvarchar(100)  NULL
);
GO

-- --------------------------------------------------
-- Creating all PRIMARY KEY constraints
-- --------------------------------------------------

-- Creating primary key on [ID] in table 'ApplicationNames'
ALTER TABLE [dbo].[ApplicationNames]
ADD CONSTRAINT [PK_ApplicationNames]
    PRIMARY KEY CLUSTERED ([ID] ASC);
GO

-- Creating primary key on [ID] in table 'ServerEndpoints'
ALTER TABLE [dbo].[ServerEndpoints]
ADD CONSTRAINT [PK_ServerEndpoints]
    PRIMARY KEY CLUSTERED ([ID] ASC);
GO

-- Creating primary key on [ID] in table 'Applications'
ALTER TABLE [dbo].[Applications]
ADD CONSTRAINT [PK_Applications]
    PRIMARY KEY CLUSTERED ([ID] ASC);
GO

-- Creating primary key on [ID] in table 'CertificateStores'
ALTER TABLE [dbo].[CertificateStores]
ADD CONSTRAINT [PK_CertificateStores]
    PRIMARY KEY CLUSTERED ([ID] ASC);
GO

-- Creating primary key on [ID] in table 'CertificateRequests'
ALTER TABLE [dbo].[CertificateRequests]
ADD CONSTRAINT [PK_CertificateRequests]
    PRIMARY KEY CLUSTERED ([ID] ASC);
GO

-- --------------------------------------------------
-- Creating all FOREIGN KEY constraints
-- --------------------------------------------------

-- Creating foreign key on [ApplicationId] in table 'ApplicationNames'
ALTER TABLE [dbo].[ApplicationNames]
ADD CONSTRAINT [FK_ApplicationNames_ApplicationId]
    FOREIGN KEY ([ApplicationId])
    REFERENCES [dbo].[Applications]
        ([ID])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_ApplicationNames_ApplicationId'
CREATE INDEX [IX_FK_ApplicationNames_ApplicationId]
ON [dbo].[ApplicationNames]
    ([ApplicationId]);
GO

-- Creating foreign key on [ApplicationId] in table 'ServerEndpoints'
ALTER TABLE [dbo].[ServerEndpoints]
ADD CONSTRAINT [FK_ServerEndpoints_ApplicationId]
    FOREIGN KEY ([ApplicationId])
    REFERENCES [dbo].[Applications]
        ([ID])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_ServerEndpoints_ApplicationId'
CREATE INDEX [IX_FK_ServerEndpoints_ApplicationId]
ON [dbo].[ServerEndpoints]
    ([ApplicationId]);
GO

-- Creating foreign key on [HttpsTrustListId] in table 'Applications'
ALTER TABLE [dbo].[Applications]
ADD CONSTRAINT [FK_Applications_HttpsTrustListId]
    FOREIGN KEY ([HttpsTrustListId])
    REFERENCES [dbo].[CertificateStores]
        ([ID])
    ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_Applications_HttpsTrustListId'
CREATE INDEX [IX_FK_Applications_HttpsTrustListId]
ON [dbo].[Applications]
    ([HttpsTrustListId]);
GO

-- Creating foreign key on [TrustListId] in table 'Applications'
ALTER TABLE [dbo].[Applications]
ADD CONSTRAINT [FK_Applications_TrustListId]
    FOREIGN KEY ([TrustListId])
    REFERENCES [dbo].[CertificateStores]
        ([ID])
    ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_Applications_TrustListId'
CREATE INDEX [IX_FK_Applications_TrustListId]
ON [dbo].[Applications]
    ([TrustListId]);
GO

-- Creating foreign key on [ApplicationId] in table 'CertificateRequests'
ALTER TABLE [dbo].[CertificateRequests]
ADD CONSTRAINT [FK_CertificateRequests_Applications]
    FOREIGN KEY ([ApplicationId])
    REFERENCES [dbo].[Applications]
        ([ID])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_CertificateRequests_Applications'
CREATE INDEX [IX_FK_CertificateRequests_Applications]
ON [dbo].[CertificateRequests]
    ([ApplicationId]);
GO

-- --------------------------------------------------
-- Script has ended
-- --------------------------------------------------