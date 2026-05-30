-- SQL Migration Script to create UserSessions table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[UserSessions]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[UserSessions] (
        [SessionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [UserId] INT NOT NULL,
        [Email] NVARCHAR(256) NOT NULL,
        [DeviceId] VARCHAR(128) NOT NULL,
        [BrowserInfo] VARCHAR(512) NOT NULL,
        [IpAddress] VARCHAR(64) NOT NULL,
        [LoginTime] DATETIME NOT NULL DEFAULT GETDATE(),
        [LogoutTime] DATETIME NULL,
        [IsActive] BIT NOT NULL DEFAULT 1
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_UserSessions_OneActivePerUser' AND object_id = OBJECT_ID(N'[dbo].[UserSessions]'))
BEGIN
    CREATE UNIQUE INDEX [UX_UserSessions_OneActivePerUser]
    ON [dbo].[UserSessions]([UserId])
    WHERE [IsActive] = 1;
END
GO
