using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProductHub_MVC.Data;

#nullable disable

namespace ProductHub_MVC.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260530120000_AddUserSessions")]
    public partial class AddUserSessions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[UserSessions]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UserSessions] (
        [SessionId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_UserSessions] PRIMARY KEY DEFAULT NEWID(),
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

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_UserSessions_OneActivePerUser' AND object_id = OBJECT_ID(N'[dbo].[UserSessions]'))
BEGIN
    CREATE UNIQUE INDEX [UX_UserSessions_OneActivePerUser]
    ON [dbo].[UserSessions]([UserId])
    WHERE [IsActive] = 1;
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_UserSessions_OneActivePerUser' AND object_id = OBJECT_ID(N'[dbo].[UserSessions]'))
    DROP INDEX [UX_UserSessions_OneActivePerUser] ON [dbo].[UserSessions];

IF OBJECT_ID(N'[dbo].[UserSessions]', N'U') IS NOT NULL
    DROP TABLE [dbo].[UserSessions];
");
        }
    }
}
