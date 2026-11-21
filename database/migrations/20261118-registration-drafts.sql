SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.Advertisements', 'RegistrationProgress') IS NULL
	ALTER TABLE [dbo].[Advertisements] ADD [RegistrationProgress] nvarchar(max) NULL;
COMMIT TRANSACTION;
