using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using tramsac99.Data;
using tramsac99.Services;

var builder = WebApplication.CreateBuilder(args);

// Why changed: add cookie auth so User/Admin can log in with roles
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/User/Account/Login";
        options.AccessDeniedPath = "/User/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient<ExternalEvNewsService>(); // Why changed: fetch EV news from external RSS feeds.

builder.Services.AddScoped<LightAiService>();
builder.Services.Configure<SmtpEmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<tramsac99.Services.ChargingHierarchyService>(); // Why changed: sync station-pole statuses after removing charging-port UI.
builder.Services.AddScoped<PayOsCheckoutService>(); // Why changed: create payOS checkout links for station registration fees.

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication(); // Why changed: must run before authorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Why changed: execute support schema upgrade in separate SQL batches so SQL Server can see new columns immediately.
    var supportUpgradeSqlCommands = new[]
    {
        @"IF OBJECT_ID(N'[dbo].[SupportRequests]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SupportRequests]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [SenderUserId] INT NULL,
        [SenderUserName] NVARCHAR(100) NULL,
        [FullName] NVARCHAR(120) NOT NULL,
        [Email] NVARCHAR(150) NOT NULL,
        [PhoneNumber] NVARCHAR(30) NULL,
        [Subject] NVARCHAR(200) NOT NULL,
        [Message] NVARCHAR(MAX) NOT NULL,
        [Status] NVARCHAR(30) NOT NULL CONSTRAINT [DF_SupportRequests_Status] DEFAULT (N'Mới'),
        [IsRead] BIT NOT NULL CONSTRAINT [DF_SupportRequests_IsRead] DEFAULT ((0)),
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_SupportRequests_CreatedAt] DEFAULT (GETDATE()),
        [ReadAt] DATETIME2 NULL,
        [ResolvedAt] DATETIME2 NULL,
        [AdminReply] NVARCHAR(1000) NULL,
        [LastStatusChangedAt] DATETIME2 NULL,
        [IsUserSeen] BIT NOT NULL CONSTRAINT [DF_SupportRequests_IsUserSeen] DEFAULT ((1)),
        [UserSeenAt] DATETIME2 NULL
    );
END",
        @"IF COL_LENGTH('dbo.SupportRequests', 'SenderUserId') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [SenderUserId] INT NULL;",
        @"IF COL_LENGTH('dbo.SupportRequests', 'SenderUserName') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [SenderUserName] NVARCHAR(100) NULL;",
        @"IF COL_LENGTH('dbo.SupportRequests', 'PhoneNumber') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [PhoneNumber] NVARCHAR(30) NULL;",
        @"IF COL_LENGTH('dbo.SupportRequests', 'Status') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [Status] NVARCHAR(30) NOT NULL CONSTRAINT [DF_SupportRequests_Status_Auto] DEFAULT (N'Mới');",
        @"IF COL_LENGTH('dbo.SupportRequests', 'IsRead') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [IsRead] BIT NOT NULL CONSTRAINT [DF_SupportRequests_IsRead_Auto] DEFAULT ((0));",
        @"IF COL_LENGTH('dbo.SupportRequests', 'CreatedAt') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_SupportRequests_CreatedAt_Auto] DEFAULT (GETDATE());",
        @"IF COL_LENGTH('dbo.SupportRequests', 'ReadAt') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [ReadAt] DATETIME2 NULL;",
        @"IF COL_LENGTH('dbo.SupportRequests', 'ResolvedAt') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [ResolvedAt] DATETIME2 NULL;",
        @"IF COL_LENGTH('dbo.SupportRequests', 'AdminReply') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [AdminReply] NVARCHAR(1000) NULL;",
        @"IF COL_LENGTH('dbo.SupportRequests', 'LastStatusChangedAt') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [LastStatusChangedAt] DATETIME2 NULL;",
        @"IF COL_LENGTH('dbo.SupportRequests', 'IsUserSeen') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [IsUserSeen] BIT NOT NULL CONSTRAINT [DF_SupportRequests_IsUserSeen_Auto] DEFAULT ((1));",
        @"IF COL_LENGTH('dbo.SupportRequests', 'UserSeenAt') IS NULL
    ALTER TABLE [dbo].[SupportRequests] ADD [UserSeenAt] DATETIME2 NULL;",
        @"IF COL_LENGTH('dbo.SupportRequests', 'Status') IS NOT NULL
    UPDATE [dbo].[SupportRequests] SET [Status] = N'Mới' WHERE [Status] IS NULL;",
        @"IF COL_LENGTH('dbo.SupportRequests', 'LastStatusChangedAt') IS NOT NULL
   AND COL_LENGTH('dbo.SupportRequests', 'IsUserSeen') IS NOT NULL
BEGIN
    UPDATE [dbo].[SupportRequests]
    SET [LastStatusChangedAt] = CASE
            WHEN [LastStatusChangedAt] IS NOT NULL THEN [LastStatusChangedAt]
            WHEN [ResolvedAt] IS NOT NULL THEN [ResolvedAt]
            WHEN [ReadAt] IS NOT NULL THEN [ReadAt]
            ELSE [CreatedAt]
        END,
        [IsUserSeen] = CASE
            WHEN [Status] = N'Đã xử lý' AND [IsUserSeen] IS NULL THEN 0
            WHEN [IsUserSeen] IS NULL THEN 1
            ELSE [IsUserSeen]
        END
    WHERE [LastStatusChangedAt] IS NULL OR [IsUserSeen] IS NULL;
END",
        @"IF OBJECT_ID(N'[dbo].[SupportRequests]', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.SupportRequests', 'Status') IS NOT NULL
   AND COL_LENGTH('dbo.SupportRequests', 'CreatedAt') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_SupportRequests_Status_CreatedAt'
          AND object_id = OBJECT_ID(N'[dbo].[SupportRequests]')
   )
BEGIN
    CREATE INDEX [IX_SupportRequests_Status_CreatedAt]
        ON [dbo].[SupportRequests]([Status], [CreatedAt]);
END"
    };

    var stationWorkflowUpgradeSqlCommands = new[]
    {
        // Why changed: keep owner link on charging station for "Tram cua toi".
        @"IF COL_LENGTH('dbo.ChargingStations', 'OwnerUserId') IS NULL
        ALTER TABLE [dbo].[ChargingStations] ADD [OwnerUserId] INT NULL;",

        // Why changed: old dev table may exist without UserId, so drop and recreate cleanly.
        @"IF OBJECT_ID(N'[dbo].[StationRegistrationRequests]', N'U') IS NOT NULL
       AND COL_LENGTH('dbo.StationRegistrationRequests', 'UserId') IS NULL
    BEGIN
        DROP TABLE [dbo].[StationRegistrationRequests];
    END",

        // Why changed: old dev table may exist without UserId, so drop and recreate cleanly.
        @"IF OBJECT_ID(N'[dbo].[StationOperationRequests]', N'U') IS NOT NULL
       AND COL_LENGTH('dbo.StationOperationRequests', 'UserId') IS NULL
    BEGIN
        DROP TABLE [dbo].[StationOperationRequests];
    END",

        // Why changed: create registration request table for user -> admin -> payment workflow.
        @"IF OBJECT_ID(N'[dbo].[StationRegistrationRequests]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[StationRegistrationRequests]
        (
            [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            [UserId] INT NOT NULL,
            [StationName] NVARCHAR(200) NOT NULL,
            [OperatorName] NVARCHAR(200) NOT NULL,
            [ContactEmail] NVARCHAR(150) NOT NULL,
            [ContactPhone] NVARCHAR(30) NOT NULL,
            [Address] NVARCHAR(300) NOT NULL,
            [Latitude] FLOAT NOT NULL CONSTRAINT [DF_StationRegistrationRequests_Latitude] DEFAULT ((0)),
            [Longitude] FLOAT NOT NULL CONSTRAINT [DF_StationRegistrationRequests_Longitude] DEFAULT ((0)),
            [Description] NVARCHAR(1000) NULL,
            [ImageUrl] NVARCHAR(300) NULL,
            [InitialPoleCount] INT NOT NULL CONSTRAINT [DF_StationRegistrationRequests_InitialPoleCount] DEFAULT ((0)),
            [InitialPoleChargerType] NVARCHAR(100) NULL,
            [InitialPoleMaxPower] NVARCHAR(50) NULL,
            [InitialPoleNote] NVARCHAR(1000) NULL,
            [ApprovalStatus] NVARCHAR(30) NOT NULL CONSTRAINT [DF_StationRegistrationRequests_ApprovalStatus] DEFAULT (N'Chờ duyệt'),
            [PaymentStatus] NVARCHAR(30) NOT NULL CONSTRAINT [DF_StationRegistrationRequests_PaymentStatus] DEFAULT (N'Chưa thanh toán'),
            [PayOsOrderCode] BIGINT NULL,
            [PayOsCheckoutUrl] NVARCHAR(500) NULL,
            [FeeAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_StationRegistrationRequests_FeeAmount] DEFAULT ((5000)),
            [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_StationRegistrationRequests_CreatedAt] DEFAULT (GETDATE()),
            [ReviewedAt] DATETIME2 NULL,
            [PaidAt] DATETIME2 NULL,
            [CompletedAt] DATETIME2 NULL,
            [AdminNote] NVARCHAR(1000) NULL,
            [CreatedStationId] INT NULL
        );
    END",

        @"IF COL_LENGTH('dbo.StationRegistrationRequests', 'InitialPoleChargerType') IS NULL
        ALTER TABLE [dbo].[StationRegistrationRequests] ADD [InitialPoleChargerType] NVARCHAR(100) NULL;",

        // Why changed: keep charger type field in sync for charging poles on older DBs.
        @"IF COL_LENGTH('dbo.ChargingPoles', 'ChargerType') IS NULL
        ALTER TABLE [dbo].[ChargingPoles] ADD [ChargerType] NVARCHAR(100) NULL;",

        // Why changed: make old DBs switch FeeAmount default from 10000 to 5000.
        @"IF OBJECT_ID(N'[dbo].[StationRegistrationRequests]', N'U') IS NOT NULL
   AND EXISTS (
        SELECT 1
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.default_object_id = dc.object_id
        INNER JOIN sys.tables t
            ON t.object_id = c.object_id
        WHERE t.name = N'StationRegistrationRequests'
          AND c.name = N'FeeAmount'
          AND dc.name <> N'DF_StationRegistrationRequests_FeeAmount_5000'
   )
BEGIN
    DECLARE @ConstraintName NVARCHAR(200);

    SELECT @ConstraintName = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.default_object_id = dc.object_id
    INNER JOIN sys.tables t
        ON t.object_id = c.object_id
    WHERE t.name = N'StationRegistrationRequests'
      AND c.name = N'FeeAmount';

    IF @ConstraintName IS NOT NULL
    BEGIN
        EXEC('ALTER TABLE [dbo].[StationRegistrationRequests] DROP CONSTRAINT [' + @ConstraintName + ']');
    END
END",

        @"IF OBJECT_ID(N'[dbo].[StationRegistrationRequests]', N'U') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.default_object_id = dc.object_id
        INNER JOIN sys.tables t
            ON t.object_id = c.object_id
        WHERE t.name = N'StationRegistrationRequests'
          AND c.name = N'FeeAmount'
          AND dc.name = N'DF_StationRegistrationRequests_FeeAmount_5000'
   )
BEGIN
    ALTER TABLE [dbo].[StationRegistrationRequests]
    ADD CONSTRAINT [DF_StationRegistrationRequests_FeeAmount_5000]
    DEFAULT ((5000)) FOR [FeeAmount];
END",

        // Why changed: create station operation request table for status update / add pole requests.
        @"IF OBJECT_ID(N'[dbo].[StationOperationRequests]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[StationOperationRequests]
        (
            [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            [StationId] INT NOT NULL,
            [UserId] INT NOT NULL,
            [RequestType] NVARCHAR(50) NOT NULL,
            [Status] NVARCHAR(30) NOT NULL CONSTRAINT [DF_StationOperationRequests_Status] DEFAULT (N'Chờ duyệt'),
            [RequestedStationStatus] NVARCHAR(50) NULL,
            [PoleId] INT NULL,
            [PoleCode] NVARCHAR(50) NULL,
            [PoleMaxPower] NVARCHAR(50) NULL,
            [RequestedPoleStatus] NVARCHAR(50) NULL,
            [UserNote] NVARCHAR(1000) NULL,
            [AdminNote] NVARCHAR(1000) NULL,
            [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_StationOperationRequests_CreatedAt] DEFAULT (GETDATE()),
            [ReviewedAt] DATETIME2 NULL,
            [CompletedAt] DATETIME2 NULL
        );
    END",

        // Why changed: keep new pole-management request fields in sync for update/delete flows.
        @"IF COL_LENGTH('dbo.StationOperationRequests', 'PoleId') IS NULL
        ALTER TABLE [dbo].[StationOperationRequests] ADD [PoleId] INT NULL;",

        @"IF COL_LENGTH('dbo.StationOperationRequests', 'RequestedPoleStatus') IS NULL
        ALTER TABLE [dbo].[StationOperationRequests] ADD [RequestedPoleStatus] NVARCHAR(50) NULL;",

        // Why changed: add index only after the column definitely exists.
        @"IF OBJECT_ID(N'[dbo].[StationRegistrationRequests]', N'U') IS NOT NULL
       AND COL_LENGTH('dbo.StationRegistrationRequests', 'UserId') IS NOT NULL
       AND NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_StationRegistrationRequests_UserId_ApprovalStatus_PaymentStatus_CreatedAt'
              AND object_id = OBJECT_ID(N'[dbo].[StationRegistrationRequests]')
       )
    BEGIN
        CREATE INDEX [IX_StationRegistrationRequests_UserId_ApprovalStatus_PaymentStatus_CreatedAt]
            ON [dbo].[StationRegistrationRequests]([UserId], [ApprovalStatus], [PaymentStatus], [CreatedAt]);
    END",

        // Why changed: keep payOS order lookup fast and unique.
        @"IF OBJECT_ID(N'[dbo].[StationRegistrationRequests]', N'U') IS NOT NULL
       AND COL_LENGTH('dbo.StationRegistrationRequests', 'PayOsOrderCode') IS NOT NULL
       AND NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_StationRegistrationRequests_PayOsOrderCode'
              AND object_id = OBJECT_ID(N'[dbo].[StationRegistrationRequests]')
       )
    BEGIN
        CREATE UNIQUE INDEX [IX_StationRegistrationRequests_PayOsOrderCode]
            ON [dbo].[StationRegistrationRequests]([PayOsOrderCode])
            WHERE [PayOsOrderCode] IS NOT NULL;
    END",

        // Why changed: add request list index only after required columns exist.
        @"IF OBJECT_ID(N'[dbo].[StationOperationRequests]', N'U') IS NOT NULL
       AND COL_LENGTH('dbo.StationOperationRequests', 'StationId') IS NOT NULL
       AND COL_LENGTH('dbo.StationOperationRequests', 'Status') IS NOT NULL
       AND COL_LENGTH('dbo.StationOperationRequests', 'CreatedAt') IS NOT NULL
       AND NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_StationOperationRequests_StationId_Status_CreatedAt'
              AND object_id = OBJECT_ID(N'[dbo].[StationOperationRequests]')
       )
    BEGIN
        CREATE INDEX [IX_StationOperationRequests_StationId_Status_CreatedAt]
            ON [dbo].[StationOperationRequests]([StationId], [Status], [CreatedAt]);
    END"
    };

    var passwordResetUpgradeSqlCommands = new[]
    {
        @"IF OBJECT_ID(N'[dbo].[PasswordResetTokens]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PasswordResetTokens]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [UserId] INT NOT NULL,
        [Token] NVARCHAR(200) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_PasswordResetTokens_CreatedAt] DEFAULT (GETDATE()),
        [ExpiresAt] DATETIME2 NOT NULL,
        [UsedAt] DATETIME2 NULL,
        [RequestedByIp] NVARCHAR(50) NULL
    );
END",
        @"IF COL_LENGTH('dbo.PasswordResetTokens', 'UserId') IS NULL
    ALTER TABLE [dbo].[PasswordResetTokens] ADD [UserId] INT NOT NULL CONSTRAINT [DF_PasswordResetTokens_UserId] DEFAULT ((0));",
        @"IF COL_LENGTH('dbo.PasswordResetTokens', 'Token') IS NULL
    ALTER TABLE [dbo].[PasswordResetTokens] ADD [Token] NVARCHAR(200) NOT NULL CONSTRAINT [DF_PasswordResetTokens_Token] DEFAULT (N'');",
        @"IF COL_LENGTH('dbo.PasswordResetTokens', 'CreatedAt') IS NULL
    ALTER TABLE [dbo].[PasswordResetTokens] ADD [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_PasswordResetTokens_CreatedAt_Auto] DEFAULT (GETDATE());",
        @"IF COL_LENGTH('dbo.PasswordResetTokens', 'ExpiresAt') IS NULL
    ALTER TABLE [dbo].[PasswordResetTokens] ADD [ExpiresAt] DATETIME2 NOT NULL CONSTRAINT [DF_PasswordResetTokens_ExpiresAt] DEFAULT (DATEADD(HOUR, 1, GETDATE()));",
        @"IF COL_LENGTH('dbo.PasswordResetTokens', 'UsedAt') IS NULL
    ALTER TABLE [dbo].[PasswordResetTokens] ADD [UsedAt] DATETIME2 NULL;",
        @"IF COL_LENGTH('dbo.PasswordResetTokens', 'RequestedByIp') IS NULL
    ALTER TABLE [dbo].[PasswordResetTokens] ADD [RequestedByIp] NVARCHAR(50) NULL;",
        @"IF OBJECT_ID(N'[dbo].[PasswordResetTokens]', N'U') IS NOT NULL
       AND NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_PasswordResetTokens_Token'
              AND object_id = OBJECT_ID(N'[dbo].[PasswordResetTokens]')
       )
    BEGIN
        CREATE UNIQUE INDEX [IX_PasswordResetTokens_Token]
            ON [dbo].[PasswordResetTokens]([Token]);
    END",
        @"IF OBJECT_ID(N'[dbo].[PasswordResetTokens]', N'U') IS NOT NULL
       AND NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_PasswordResetTokens_UserId_ExpiresAt_UsedAt'
              AND object_id = OBJECT_ID(N'[dbo].[PasswordResetTokens]')
       )
    BEGIN
        CREATE INDEX [IX_PasswordResetTokens_UserId_ExpiresAt_UsedAt]
            ON [dbo].[PasswordResetTokens]([UserId], [ExpiresAt], [UsedAt]);
    END"
    };

    foreach (var sql in supportUpgradeSqlCommands)
    {
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    foreach (var sql in stationWorkflowUpgradeSqlCommands)
    {
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    foreach (var sql in passwordResetUpgradeSqlCommands)
    {
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    DbSeeder.Seed(db);
}

app.Run();
