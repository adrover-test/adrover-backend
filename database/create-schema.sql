CREATE TABLE [Enquiries] (
    [Id] uniqueidentifier NOT NULL,
    [Phone] nvarchar(20) NOT NULL,
    [Email] nvarchar(255) NOT NULL,
    [Name] nvarchar(255) NOT NULL,
    [Message] nvarchar(max) NOT NULL,
    [Subject] nvarchar(255) NOT NULL,
    [SourceChannel] nvarchar(50) NULL,
    [Status] nvarchar(50) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [RespondedAt] datetime2 NULL,
    CONSTRAINT [PK_Enquiries] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Users] (
    [Id] uniqueidentifier NOT NULL,
    [Email] nvarchar(255) NOT NULL,
    [Phone] nvarchar(20) NOT NULL,
    [FullName] nvarchar(255) NOT NULL,
    [PasswordHash] nvarchar(max) NOT NULL,
    [Role] int NOT NULL,
    [ProfileImageUrl] nvarchar(500) NULL,
    [IsActive] bit NOT NULL,
    [IsEmailVerified] bit NOT NULL,
    [IsPhoneVerified] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [LastLoginAt] datetime2 NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Advertisements] (
    [Id] uniqueidentifier NOT NULL,
    [ClientId] uniqueidentifier NOT NULL,
    [Name] nvarchar(255) NOT NULL,
    [Brand] nvarchar(255) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [TargetArea] nvarchar(500) NOT NULL,
    [VideoUrl] nvarchar(500) NULL,
    [ImageUrl] nvarchar(500) NULL,
    [VideoDurationSeconds] decimal(18,2) NOT NULL,
    [StartDate] datetime2 NOT NULL,
    [EndDate] datetime2 NOT NULL,
    [TargetPlays] bigint NOT NULL,
    [ActualPlays] bigint NOT NULL,
    [AssignedCabs] int NOT NULL,
    [Status] int NOT NULL,
    [ApprovalAmount] decimal(18,2) NOT NULL,
    [PaidAmount] decimal(18,2) NOT NULL,
    [PaymentId] uniqueidentifier NULL,
    [RejectionReason] nvarchar(1000) NULL,
    [ApprovedBy] nvarchar(255) NULL,
    [ApprovedAt] datetime2 NULL,
    [RejectedAt] datetime2 NULL,
    [SubmittedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_Advertisements] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Advertisements_Users_ClientId] FOREIGN KEY ([ClientId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [AuditLogs] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NULL,
    [Action] nvarchar(255) NOT NULL,
    [EntityName] nvarchar(255) NOT NULL,
    [EntityId] nvarchar(100) NULL,
    [OldValues] nvarchar(max) NULL,
    [NewValues] nvarchar(max) NULL,
    [IpAddress] nvarchar(45) NULL,
    [UserAgent] nvarchar(500) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AuditLogs_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
);
GO


CREATE TABLE [Notifications] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Title] nvarchar(255) NOT NULL,
    [Message] nvarchar(max) NOT NULL,
    [Type] nvarchar(100) NOT NULL,
    [RelatedResourceId] uniqueidentifier NULL,
    [IsRead] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ReadAt] datetime2 NULL,
    CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [RefreshTokens] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Token] nvarchar(500) NOT NULL,
    [ExpiresAt] datetime2 NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [RevokedAt] datetime2 NULL,
    CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [Wallets] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Balance] decimal(18,2) NOT NULL,
    [Currency] nvarchar(10) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [LastUpdated] datetime2 NOT NULL,
    CONSTRAINT [PK_Wallets] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Wallets_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [AdPlays] (
    [Id] uniqueidentifier NOT NULL,
    [AdvertisementId] uniqueidentifier NOT NULL,
    [DriverPhone] nvarchar(20) NOT NULL,
    [VehicleNumber] nvarchar(20) NOT NULL,
    [Latitude] float NOT NULL,
    [Longitude] float NOT NULL,
    [DurationSeconds] int NOT NULL,
    [PlayedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_AdPlays] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AdPlays_Advertisements_AdvertisementId] FOREIGN KEY ([AdvertisementId]) REFERENCES [Advertisements] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [Payments] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [AdvertisementId] uniqueidentifier NULL,
    [OrderId] nvarchar(100) NOT NULL,
    [PaymentId] nvarchar(100) NULL,
    [Signature] nvarchar(500) NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Currency] nvarchar(10) NOT NULL,
    [Status] int NOT NULL,
    [PaymentMethod] nvarchar(50) NOT NULL,
    [CardLastFour] nvarchar(4) NULL,
    [CardIssuer] nvarchar(50) NULL,
    [Description] nvarchar(max) NOT NULL,
    [Metadata] nvarchar(max) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ProcessedAt] datetime2 NULL,
    [FailedAt] datetime2 NULL,
    CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Payments_Advertisements_AdvertisementId] FOREIGN KEY ([AdvertisementId]) REFERENCES [Advertisements] ([Id]) ON DELETE SET NULL,
    CONSTRAINT [FK_Payments_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [WalletTransactions] (
    [Id] uniqueidentifier NOT NULL,
    [WalletId] uniqueidentifier NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [BalanceBefore] decimal(18,2) NOT NULL,
    [BalanceAfter] decimal(18,2) NOT NULL,
    [TransactionType] nvarchar(50) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [ReferenceId] nvarchar(100) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_WalletTransactions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_WalletTransactions_Wallets_WalletId] FOREIGN KEY ([WalletId]) REFERENCES [Wallets] ([Id]) ON DELETE CASCADE
);
GO


CREATE INDEX [IX_AdPlays_AdvertisementId] ON [AdPlays] ([AdvertisementId]);
GO


CREATE INDEX [IX_AdPlays_PlayedAt] ON [AdPlays] ([PlayedAt]);
GO


CREATE INDEX [IX_Advertisements_ClientId] ON [Advertisements] ([ClientId]);
GO


CREATE INDEX [IX_Advertisements_Status] ON [Advertisements] ([Status]);
GO


CREATE INDEX [IX_Advertisements_SubmittedAt] ON [Advertisements] ([SubmittedAt]);
GO


CREATE INDEX [IX_AuditLogs_CreatedAt] ON [AuditLogs] ([CreatedAt]);
GO


CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
GO


CREATE INDEX [IX_Enquiries_CreatedAt] ON [Enquiries] ([CreatedAt]);
GO


CREATE INDEX [IX_Enquiries_Email] ON [Enquiries] ([Email]);
GO


CREATE INDEX [IX_Enquiries_Phone] ON [Enquiries] ([Phone]);
GO


CREATE INDEX [IX_Notifications_CreatedAt] ON [Notifications] ([CreatedAt]);
GO


CREATE INDEX [IX_Notifications_IsRead] ON [Notifications] ([IsRead]);
GO


CREATE INDEX [IX_Notifications_UserId] ON [Notifications] ([UserId]);
GO


CREATE UNIQUE INDEX [IX_Payments_AdvertisementId] ON [Payments] ([AdvertisementId]) WHERE [AdvertisementId] IS NOT NULL;
GO


CREATE UNIQUE INDEX [IX_Payments_OrderId] ON [Payments] ([OrderId]);
GO


CREATE INDEX [IX_Payments_PaymentId] ON [Payments] ([PaymentId]);
GO


CREATE INDEX [IX_Payments_Status] ON [Payments] ([Status]);
GO


CREATE INDEX [IX_Payments_UserId] ON [Payments] ([UserId]);
GO


CREATE UNIQUE INDEX [IX_RefreshTokens_Token] ON [RefreshTokens] ([Token]);
GO


CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
GO


CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
GO


CREATE UNIQUE INDEX [IX_Users_Phone] ON [Users] ([Phone]);
GO


CREATE UNIQUE INDEX [IX_Wallets_UserId] ON [Wallets] ([UserId]);
GO


CREATE INDEX [IX_WalletTransactions_CreatedAt] ON [WalletTransactions] ([CreatedAt]);
GO


CREATE INDEX [IX_WalletTransactions_WalletId] ON [WalletTransactions] ([WalletId]);
GO


