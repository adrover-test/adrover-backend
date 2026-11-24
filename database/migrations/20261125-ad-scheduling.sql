SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Advertisements', 'ContentId') IS NULL
	ALTER TABLE [dbo].[Advertisements] ADD [ContentId] nvarchar(100) NULL;
IF COL_LENGTH('dbo.Advertisements', 'ContentAssetVersion') IS NULL
	ALTER TABLE [dbo].[Advertisements] ADD [ContentAssetVersion] int NULL;
IF COL_LENGTH('dbo.Advertisements', 'ContentState') IS NULL
	ALTER TABLE [dbo].[Advertisements] ADD [ContentState] nvarchar(32) NOT NULL CONSTRAINT [DF_Advertisements_ContentState] DEFAULT('NotSubmitted') WITH VALUES;
IF COL_LENGTH('dbo.Advertisements', 'ReadyForScheduling') IS NULL
	ALTER TABLE [dbo].[Advertisements] ADD [ReadyForScheduling] bit NOT NULL CONSTRAINT [DF_Advertisements_ReadyForScheduling] DEFAULT(0) WITH VALUES;
IF COL_LENGTH('dbo.Advertisements', 'ReadyForSchedulingAtUtc') IS NULL
	ALTER TABLE [dbo].[Advertisements] ADD [ReadyForSchedulingAtUtc] datetime2 NULL;
IF COL_LENGTH('dbo.Advertisements', 'ContentSubmittedAtUtc') IS NULL
	ALTER TABLE [dbo].[Advertisements] ADD [ContentSubmittedAtUtc] datetime2 NULL;
IF COL_LENGTH('dbo.Advertisements', 'ContentError') IS NULL
	ALTER TABLE [dbo].[Advertisements] ADD [ContentError] nvarchar(1000) NULL;
IF COL_LENGTH('dbo.Advertisements', 'FirstPlayedAt') IS NULL
	ALTER TABLE [dbo].[Advertisements] ADD [FirstPlayedAt] datetime2 NULL;
IF COL_LENGTH('dbo.Advertisements', 'FirstPlaybackDate') IS NULL
	ALTER TABLE [dbo].[Advertisements] ADD [FirstPlaybackDate] date NULL;
IF COL_LENGTH('dbo.Advertisements', 'LastReportedDay') IS NULL
	ALTER TABLE [dbo].[Advertisements] ADD [LastReportedDay] date NULL;

IF OBJECT_ID('dbo.ContentOutbox', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ContentOutbox]
	(
		[Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ContentOutbox] PRIMARY KEY,
		[AdvertisementId] uniqueidentifier NOT NULL,
		[AssetVersion] int NOT NULL,
		[MessageId] nvarchar(150) NOT NULL,
		[VideoBlobPath] nvarchar(1024) NOT NULL,
		[PaymentId] uniqueidentifier NULL,
		[CreatedAtUtc] datetime2 NOT NULL,
		[PublishedAtUtc] datetime2 NULL,
		[NextAttemptAtUtc] datetime2 NOT NULL,
		[Attempts] int NOT NULL,
		[LastError] nvarchar(1000) NULL
	);
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ContentOutbox') AND name = 'IX_ContentOutbox_MessageId')
	CREATE UNIQUE INDEX [IX_ContentOutbox_MessageId] ON [dbo].[ContentOutbox]([MessageId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ContentOutbox') AND name = 'IX_ContentOutbox_AdvertisementId_AssetVersion')
	CREATE UNIQUE INDEX [IX_ContentOutbox_AdvertisementId_AssetVersion] ON [dbo].[ContentOutbox]([AdvertisementId], [AssetVersion]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ContentOutbox') AND name = 'IX_ContentOutbox_PublishedAtUtc_NextAttemptAtUtc')
	CREATE INDEX [IX_ContentOutbox_PublishedAtUtc_NextAttemptAtUtc] ON [dbo].[ContentOutbox]([PublishedAtUtc], [NextAttemptAtUtc]);

IF OBJECT_ID('dbo.ScheduleRuns', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ScheduleRuns]
	(
		[Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ScheduleRuns] PRIMARY KEY,
		[RunDate] date NOT NULL,
		[Status] nvarchar(32) NOT NULL,
		[AdsScheduled] int NOT NULL,
		[BlockedCampaigns] int NOT NULL,
		[ScreensUsed] int NOT NULL,
		[PlannedPlays] bigint NOT NULL,
		[ShortfallPlays] bigint NOT NULL,
		[StartedAtUtc] datetime2 NOT NULL,
		[FinishedAtUtc] datetime2 NULL,
		[LastError] nvarchar(1000) NULL
	);
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ScheduleRuns') AND name = 'IX_ScheduleRuns_RunDate')
	CREATE UNIQUE INDEX [IX_ScheduleRuns_RunDate] ON [dbo].[ScheduleRuns]([RunDate]);

IF OBJECT_ID('dbo.AdScreenAllocations', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[AdScreenAllocations]
	(
		[Id] uniqueidentifier NOT NULL CONSTRAINT [PK_AdScreenAllocations] PRIMARY KEY,
		[RunId] uniqueidentifier NOT NULL,
		[AdvertisementId] uniqueidentifier NULL,
		[ScreenId] nvarchar(100) NOT NULL,
		[ContentId] nvarchar(100) NOT NULL,
		[PlayDate] date NOT NULL,
		[StartTime] time(0) NOT NULL,
		[EndTime] time(0) NOT NULL,
		[PlannedPlays] bigint NOT NULL,
		[Status] nvarchar(32) NOT NULL,
		[LastError] nvarchar(1000) NULL,
		[CreatedAtUtc] datetime2 NOT NULL,
		[UpdatedAtUtc] datetime2 NULL,
		CONSTRAINT [FK_AdScreenAllocations_ScheduleRuns_RunId] FOREIGN KEY ([RunId]) REFERENCES [dbo].[ScheduleRuns]([Id]) ON DELETE CASCADE
	);
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AdScreenAllocations') AND name = 'IX_AdScreenAllocations_RunId_ScreenId_PlayDate_StartTime_ContentId')
	CREATE UNIQUE INDEX [IX_AdScreenAllocations_RunId_ScreenId_PlayDate_StartTime_ContentId] ON [dbo].[AdScreenAllocations]([RunId], [ScreenId], [PlayDate], [StartTime], [ContentId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AdScreenAllocations') AND name = 'IX_AdScreenAllocations_AdvertisementId_PlayDate')
	CREATE INDEX [IX_AdScreenAllocations_AdvertisementId_PlayDate] ON [dbo].[AdScreenAllocations]([AdvertisementId], [PlayDate]);

IF OBJECT_ID('dbo.AdDailyDeliveries', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[AdDailyDeliveries]
	(
		[Id] uniqueidentifier NOT NULL CONSTRAINT [PK_AdDailyDeliveries] PRIMARY KEY,
		[AdvertisementId] uniqueidentifier NOT NULL,
		[ScreenId] nvarchar(100) NOT NULL,
		[PlayDate] date NOT NULL,
		[Plays] bigint NOT NULL,
		[Minutes] decimal(18,2) NULL,
		[ReportedAtUtc] datetime2 NOT NULL
	);
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AdDailyDeliveries') AND name = 'IX_AdDailyDeliveries_AdvertisementId_ScreenId_PlayDate')
	CREATE UNIQUE INDEX [IX_AdDailyDeliveries_AdvertisementId_ScreenId_PlayDate] ON [dbo].[AdDailyDeliveries]([AdvertisementId], [ScreenId], [PlayDate]);

COMMIT TRANSACTION;
