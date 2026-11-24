SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.Enquiries', 'BusinessName') IS NULL
	ALTER TABLE [dbo].[Enquiries] ADD [BusinessName] nvarchar(255) NULL;
COMMIT TRANSACTION;
