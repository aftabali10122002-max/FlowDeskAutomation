BEGIN TRANSACTION;
CREATE TABLE [Designations] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [IsEnabled] bit NOT NULL,
    CONSTRAINT [PK_Designations] PRIMARY KEY ([Id])
);

CREATE TABLE [LeaveHolidayCalendars] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [Region] nvarchar(100) NULL,
    [IsEnabled] bit NOT NULL,
    CONSTRAINT [PK_LeaveHolidayCalendars] PRIMARY KEY ([Id])
);

CREATE TABLE [LeaveTypes] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [DisplayName] nvarchar(100) NOT NULL,
    [IsPaid] bit NOT NULL,
    [IsEnabled] bit NOT NULL,
    CONSTRAINT [PK_LeaveTypes] PRIMARY KEY ([Id])
);

CREATE TABLE [LeaveWorkSchedules] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [IsEnabled] bit NOT NULL,
    CONSTRAINT [PK_LeaveWorkSchedules] PRIMARY KEY ([Id])
);

CREATE TABLE [EmployeeEmploymentProfiles] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [DesignationId] int NULL,
    [EmploymentType] int NULL,
    [HireDate] date NULL,
    [EffectiveFrom] date NOT NULL,
    [EffectiveTo] date NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_EmployeeEmploymentProfiles] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_EmployeeEmploymentProfiles_EffectiveRange] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]),
    CONSTRAINT [FK_EmployeeEmploymentProfiles_Designations_DesignationId] FOREIGN KEY ([DesignationId]) REFERENCES [Designations] ([Id]),
    CONSTRAINT [FK_EmployeeEmploymentProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
);

CREATE TABLE [LeaveHolidays] (
    [Id] int NOT NULL IDENTITY,
    [LeaveHolidayCalendarId] int NOT NULL,
    [Date] date NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    CONSTRAINT [PK_LeaveHolidays] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LeaveHolidays_LeaveHolidayCalendars_LeaveHolidayCalendarId] FOREIGN KEY ([LeaveHolidayCalendarId]) REFERENCES [LeaveHolidayCalendars] ([Id])
);

CREATE TABLE [LeavePolicies] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(120) NOT NULL,
    [Version] int NOT NULL,
    [Scope] int NOT NULL,
    [DepartmentId] int NULL,
    [DesignationId] int NULL,
    [EmploymentType] int NULL,
    [EffectiveFrom] date NOT NULL,
    [EffectiveTo] date NULL,
    [DayCalculationMethod] int NOT NULL,
    [LeaveWorkScheduleId] int NULL,
    [LeaveHolidayCalendarId] int NULL,
    [AllowsHalfDays] bit NOT NULL,
    [HalfDayUnits] decimal(5,2) NULL,
    [IsAccrualEnabled] bit NOT NULL,
    [AccrualFrequency] int NULL,
    [AllowsCarryForward] bit NOT NULL,
    [CarryForwardCap] decimal(9,2) NULL,
    [CarryForwardExpiryMonths] int NULL,
    [ProbationPeriodDays] int NULL,
    [AllowsLeaveDuringProbation] bit NOT NULL,
    [AllowsNegativeBalance] bit NOT NULL,
    CONSTRAINT [PK_LeavePolicies] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_LeavePolicies_AccrualSettings] CHECK (([IsAccrualEnabled] = 1 AND [AccrualFrequency] IN (1, 2, 3)) OR ([IsAccrualEnabled] = 0 AND [AccrualFrequency] IS NULL)),
    CONSTRAINT [CK_LeavePolicies_CarryForwardSettings] CHECK (([AllowsCarryForward] = 1 AND [CarryForwardCap] >= 0 AND ([CarryForwardExpiryMonths] IS NULL OR [CarryForwardExpiryMonths] > 0)) OR ([AllowsCarryForward] = 0 AND [CarryForwardCap] IS NULL AND [CarryForwardExpiryMonths] IS NULL)),
    CONSTRAINT [CK_LeavePolicies_DayCalculation] CHECK (([DayCalculationMethod] = 1) OR ([DayCalculationMethod] = 2 AND [LeaveWorkScheduleId] IS NOT NULL)),
    CONSTRAINT [CK_LeavePolicies_EffectiveRange] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]),
    CONSTRAINT [CK_LeavePolicies_HalfDaySettings] CHECK (([AllowsHalfDays] = 0 AND [HalfDayUnits] IS NULL) OR ([AllowsHalfDays] = 1 AND [HalfDayUnits] > 0 AND [HalfDayUnits] < 1)),
    CONSTRAINT [CK_LeavePolicies_ProbationPeriod] CHECK ([ProbationPeriodDays] IS NULL OR [ProbationPeriodDays] > 0),
    CONSTRAINT [CK_LeavePolicies_ScopeEnum] CHECK ([Scope] IN (1, 2, 3, 4)),
    CONSTRAINT [CK_LeavePolicies_ScopeTarget] CHECK (([Scope] = 1 AND [DepartmentId] IS NULL AND [DesignationId] IS NULL AND [EmploymentType] IS NULL) OR ([Scope] = 2 AND [DepartmentId] IS NOT NULL AND [DesignationId] IS NULL AND [EmploymentType] IS NULL) OR ([Scope] = 3 AND [DepartmentId] IS NULL AND [DesignationId] IS NOT NULL AND [EmploymentType] IS NULL) OR ([Scope] = 4 AND [DepartmentId] IS NULL AND [DesignationId] IS NULL AND [EmploymentType] IS NOT NULL)),
    CONSTRAINT [CK_LeavePolicies_Version] CHECK ([Version] > 0),
    CONSTRAINT [FK_LeavePolicies_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]),
    CONSTRAINT [FK_LeavePolicies_Designations_DesignationId] FOREIGN KEY ([DesignationId]) REFERENCES [Designations] ([Id]),
    CONSTRAINT [FK_LeavePolicies_LeaveHolidayCalendars_LeaveHolidayCalendarId] FOREIGN KEY ([LeaveHolidayCalendarId]) REFERENCES [LeaveHolidayCalendars] ([Id]),
    CONSTRAINT [FK_LeavePolicies_LeaveWorkSchedules_LeaveWorkScheduleId] FOREIGN KEY ([LeaveWorkScheduleId]) REFERENCES [LeaveWorkSchedules] ([Id])
);

CREATE TABLE [LeaveWorkScheduleDays] (
    [Id] int NOT NULL IDENTITY,
    [LeaveWorkScheduleId] int NOT NULL,
    [DayOfWeek] int NOT NULL,
    [IsWorkingDay] bit NOT NULL,
    [StandardHours] decimal(4,2) NOT NULL,
    CONSTRAINT [PK_LeaveWorkScheduleDays] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_LeaveWorkScheduleDays_DayOfWeek] CHECK ([DayOfWeek] BETWEEN 0 AND 6),
    CONSTRAINT [CK_LeaveWorkScheduleDays_StandardHours] CHECK ([StandardHours] >= 0 AND [StandardHours] <= 24),
    CONSTRAINT [CK_LeaveWorkScheduleDays_WorkingDayHours] CHECK (([IsWorkingDay] = 1 AND [StandardHours] > 0) OR ([IsWorkingDay] = 0 AND [StandardHours] = 0)),
    CONSTRAINT [FK_LeaveWorkScheduleDays_LeaveWorkSchedules_LeaveWorkScheduleId] FOREIGN KEY ([LeaveWorkScheduleId]) REFERENCES [LeaveWorkSchedules] ([Id])
);

CREATE TABLE [LeavePolicyAssignments] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [LeavePolicyId] int NOT NULL,
    [AssignedByAdminUserId] int NOT NULL,
    [EffectiveFrom] date NOT NULL,
    [EffectiveTo] date NULL,
    CONSTRAINT [PK_LeavePolicyAssignments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_LeavePolicyAssignments_EffectiveRange] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]),
    CONSTRAINT [FK_LeavePolicyAssignments_LeavePolicies_LeavePolicyId] FOREIGN KEY ([LeavePolicyId]) REFERENCES [LeavePolicies] ([Id]),
    CONSTRAINT [FK_LeavePolicyAssignments_Users_AssignedByAdminUserId] FOREIGN KEY ([AssignedByAdminUserId]) REFERENCES [Users] ([Id]),
    CONSTRAINT [FK_LeavePolicyAssignments_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
);

CREATE TABLE [LeavePolicyEntitlements] (
    [Id] int NOT NULL IDENTITY,
    [LeavePolicyId] int NOT NULL,
    [LeaveTypeId] int NOT NULL,
    [AnnualEntitlement] decimal(9,2) NOT NULL,
    CONSTRAINT [PK_LeavePolicyEntitlements] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_LeavePolicyEntitlements_AnnualEntitlement] CHECK ([AnnualEntitlement] >= 0),
    CONSTRAINT [FK_LeavePolicyEntitlements_LeavePolicies_LeavePolicyId] FOREIGN KEY ([LeavePolicyId]) REFERENCES [LeavePolicies] ([Id]),
    CONSTRAINT [FK_LeavePolicyEntitlements_LeaveTypes_LeaveTypeId] FOREIGN KEY ([LeaveTypeId]) REFERENCES [LeaveTypes] ([Id])
);

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Code', N'DisplayName', N'IsEnabled', N'IsPaid') AND [object_id] = OBJECT_ID(N'[LeaveTypes]'))
    SET IDENTITY_INSERT [LeaveTypes] ON;
INSERT INTO [LeaveTypes] ([Id], [Code], [DisplayName], [IsEnabled], [IsPaid])
VALUES (1, N'ANNUAL', N'Annual Leave', CAST(1 AS bit), CAST(1 AS bit)),
(2, N'SICK', N'Sick Leave', CAST(1 AS bit), CAST(1 AS bit)),
(3, N'CASUAL', N'Casual Leave', CAST(1 AS bit), CAST(1 AS bit)),
(4, N'UNPAID', N'Unpaid Leave', CAST(1 AS bit), CAST(0 AS bit));
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Code', N'DisplayName', N'IsEnabled', N'IsPaid') AND [object_id] = OBJECT_ID(N'[LeaveTypes]'))
    SET IDENTITY_INSERT [LeaveTypes] OFF;

CREATE UNIQUE INDEX [IX_Designations_Code] ON [Designations] ([Code]);

CREATE UNIQUE INDEX [IX_Designations_Name] ON [Designations] ([Name]);

CREATE INDEX [IX_EmployeeEmploymentProfiles_DesignationId] ON [EmployeeEmploymentProfiles] ([DesignationId]);

CREATE UNIQUE INDEX [IX_EmployeeEmploymentProfiles_UserId_EffectiveFrom] ON [EmployeeEmploymentProfiles] ([UserId], [EffectiveFrom]);

CREATE INDEX [IX_EmployeeEmploymentProfiles_UserId_EffectiveTo] ON [EmployeeEmploymentProfiles] ([UserId], [EffectiveTo]);

CREATE UNIQUE INDEX [IX_LeaveHolidayCalendars_Code] ON [LeaveHolidayCalendars] ([Code]);

CREATE UNIQUE INDEX [IX_LeaveHolidays_LeaveHolidayCalendarId_Date] ON [LeaveHolidays] ([LeaveHolidayCalendarId], [Date]);

CREATE UNIQUE INDEX [IX_LeavePolicies_Code_Version] ON [LeavePolicies] ([Code], [Version]);

CREATE INDEX [IX_LeavePolicies_DepartmentId] ON [LeavePolicies] ([DepartmentId]);

CREATE INDEX [IX_LeavePolicies_DesignationId] ON [LeavePolicies] ([DesignationId]);

CREATE INDEX [IX_LeavePolicies_LeaveHolidayCalendarId] ON [LeavePolicies] ([LeaveHolidayCalendarId]);

CREATE INDEX [IX_LeavePolicies_LeaveWorkScheduleId] ON [LeavePolicies] ([LeaveWorkScheduleId]);

CREATE UNIQUE INDEX [IX_LeavePolicies_Scope_DepartmentId_DesignationId_EmploymentType_EffectiveFrom] ON [LeavePolicies] ([Scope], [DepartmentId], [DesignationId], [EmploymentType], [EffectiveFrom]) WHERE [DepartmentId] IS NOT NULL AND [DesignationId] IS NOT NULL AND [EmploymentType] IS NOT NULL;

CREATE INDEX [IX_LeavePolicies_Scope_EffectiveFrom_EffectiveTo] ON [LeavePolicies] ([Scope], [EffectiveFrom], [EffectiveTo]);

CREATE INDEX [IX_LeavePolicyAssignments_AssignedByAdminUserId] ON [LeavePolicyAssignments] ([AssignedByAdminUserId]);

CREATE INDEX [IX_LeavePolicyAssignments_LeavePolicyId] ON [LeavePolicyAssignments] ([LeavePolicyId]);

CREATE UNIQUE INDEX [IX_LeavePolicyAssignments_UserId_EffectiveFrom] ON [LeavePolicyAssignments] ([UserId], [EffectiveFrom]);

CREATE INDEX [IX_LeavePolicyAssignments_UserId_EffectiveTo] ON [LeavePolicyAssignments] ([UserId], [EffectiveTo]);

CREATE UNIQUE INDEX [IX_LeavePolicyEntitlements_LeavePolicyId_LeaveTypeId] ON [LeavePolicyEntitlements] ([LeavePolicyId], [LeaveTypeId]);

CREATE INDEX [IX_LeavePolicyEntitlements_LeaveTypeId] ON [LeavePolicyEntitlements] ([LeaveTypeId]);

CREATE UNIQUE INDEX [IX_LeaveTypes_Code] ON [LeaveTypes] ([Code]);

CREATE UNIQUE INDEX [IX_LeaveWorkScheduleDays_LeaveWorkScheduleId_DayOfWeek] ON [LeaveWorkScheduleDays] ([LeaveWorkScheduleId], [DayOfWeek]);

CREATE UNIQUE INDEX [IX_LeaveWorkSchedules_Code] ON [LeaveWorkSchedules] ([Code]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261023173120_Phase8LeaveManagementFoundation', N'10.0.0');

COMMIT;
GO

