-- FlowDeskDb read-only migration preflight.
-- Run in SSMS only with an explicitly approved read-only account.
-- Select FlowDeskDb in the SSMS database dropdown before execution.
-- This script does not apply migrations or modify database state.

-- 1. Verify the selected database and record SQL Server version.
SELECT
    DB_NAME() AS CurrentDatabase,
    CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS SqlServerVersion,
    CONVERT(nvarchar(128), SERVERPROPERTY('ProductLevel')) AS SqlServerProductLevel,
    CONVERT(nvarchar(128), SERVERPROPERTY('Edition')) AS SqlServerEdition,
    d.compatibility_level AS DatabaseCompatibilityLevel
FROM sys.databases AS d
WHERE d.database_id = DB_ID();

-- 2. Check expected tables, including EF migration history.
SELECT
    e.TableName,
    CASE WHEN t.object_id IS NULL THEN N'MISSING' ELSE N'PRESENT' END AS TableState
FROM (VALUES
    (N'Users'),
    (N'ApprovalRequests'),
    (N'Departments'),
    (N'ApprovalAssignments'),
    (N'__EFMigrationsHistory')
) AS e(TableName)
LEFT JOIN sys.tables AS t
    ON t.schema_id = SCHEMA_ID(N'dbo')
   AND t.name = e.TableName
ORDER BY e.TableName;

-- 3. Read recorded migration IDs only when the history table exists.
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT MigrationId, ProductVersion
        FROM dbo.__EFMigrationsHistory
        ORDER BY MigrationId;';
ELSE
    SELECT
        N'dbo.__EFMigrationsHistory' AS ObjectName,
        N'NOT PRESENT' AS ObjectState;

-- 4. Read column definitions, defaults, nullability, and identity metadata.
SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    c.column_id AS ColumnOrder,
    c.name AS ColumnName,
    ty.name AS DataType,
    c.max_length AS MaxLengthBytes,
    CASE
        WHEN ty.name IN (N'nchar', N'nvarchar') AND c.max_length <> -1
            THEN c.max_length / 2
        ELSE c.max_length
    END AS MaxLengthCharactersWhenApplicable,
    c.precision AS NumericPrecision,
    c.scale AS NumericScale,
    c.is_nullable AS IsNullable,
    COLUMNPROPERTY(c.object_id, c.name, 'IsIdentity') AS IsIdentity,
    dc.definition AS DefaultDefinition
FROM sys.tables AS t
JOIN sys.schemas AS s
    ON s.schema_id = t.schema_id
JOIN sys.columns AS c
    ON c.object_id = t.object_id
JOIN sys.types AS ty
    ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints AS dc
    ON dc.object_id = c.default_object_id
WHERE s.name = N'dbo'
  AND t.name IN (
      N'Users',
      N'ApprovalRequests',
      N'Departments',
      N'ApprovalAssignments',
      N'__EFMigrationsHistory'
  )
ORDER BY t.name, c.column_id;

-- 5. Read primary-key columns and their order.
SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    kc.name AS PrimaryKeyName,
    c.name AS ColumnName,
    ic.key_ordinal AS KeyOrdinal
FROM sys.key_constraints AS kc
JOIN sys.tables AS t
    ON t.object_id = kc.parent_object_id
JOIN sys.schemas AS s
    ON s.schema_id = t.schema_id
JOIN sys.index_columns AS ic
    ON ic.object_id = t.object_id
   AND ic.index_id = kc.unique_index_id
JOIN sys.columns AS c
    ON c.object_id = ic.object_id
   AND c.column_id = ic.column_id
WHERE kc.type = N'PK'
  AND s.name = N'dbo'
  AND t.name IN (
      N'Users',
      N'ApprovalRequests',
      N'Departments',
      N'ApprovalAssignments'
  )
ORDER BY t.name, kc.name, ic.key_ordinal;

-- 6. Read foreign-key columns and referential actions.
SELECT
    childSchema.name AS ChildSchema,
    childTable.name AS ChildTable,
    fk.name AS ForeignKeyName,
    childColumn.name AS ChildColumn,
    parentSchema.name AS ParentSchema,
    parentTable.name AS ParentTable,
    parentColumn.name AS ParentColumn,
    fk.delete_referential_action_desc AS DeleteAction,
    fk.update_referential_action_desc AS UpdateAction,
    fk.is_disabled AS IsDisabled,
    fk.is_not_trusted AS IsNotTrusted
FROM sys.foreign_keys AS fk
JOIN sys.tables AS childTable
    ON childTable.object_id = fk.parent_object_id
JOIN sys.schemas AS childSchema
    ON childSchema.schema_id = childTable.schema_id
JOIN sys.foreign_key_columns AS fkc
    ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns AS childColumn
    ON childColumn.object_id = fkc.parent_object_id
   AND childColumn.column_id = fkc.parent_column_id
JOIN sys.tables AS parentTable
    ON parentTable.object_id = fk.referenced_object_id
JOIN sys.schemas AS parentSchema
    ON parentSchema.schema_id = parentTable.schema_id
JOIN sys.columns AS parentColumn
    ON parentColumn.object_id = fkc.referenced_object_id
   AND parentColumn.column_id = fkc.referenced_column_id
WHERE childSchema.name = N'dbo'
  AND childTable.name IN (
      N'Users',
      N'ApprovalRequests',
      N'Departments',
      N'ApprovalAssignments'
  )
ORDER BY childTable.name, fk.name, fkc.constraint_column_id;

-- 7. Read check-constraint definitions and trust/enablement state.
SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    cc.name AS CheckConstraintName,
    cc.definition AS CheckDefinition,
    cc.is_disabled AS IsDisabled,
    cc.is_not_trusted AS IsNotTrusted
FROM sys.check_constraints AS cc
JOIN sys.tables AS t
    ON t.object_id = cc.parent_object_id
JOIN sys.schemas AS s
    ON s.schema_id = t.schema_id
WHERE s.name = N'dbo'
  AND t.name IN (
      N'Users',
      N'ApprovalRequests',
      N'Departments',
      N'ApprovalAssignments'
  )
ORDER BY t.name, cc.name;

-- 8. Read index keys, uniqueness, and filtered-index predicates.
SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    i.name AS IndexName,
    i.type_desc AS IndexType,
    i.is_primary_key AS IsPrimaryKey,
    i.is_unique AS IsUnique,
    i.is_unique_constraint AS IsUniqueConstraint,
    i.has_filter AS HasFilter,
    i.filter_definition AS FilterDefinition,
    i.is_disabled AS IsDisabled,
    c.name AS ColumnName,
    ic.key_ordinal AS KeyOrdinal,
    ic.is_included_column AS IsIncludedColumn
FROM sys.indexes AS i
JOIN sys.tables AS t
    ON t.object_id = i.object_id
JOIN sys.schemas AS s
    ON s.schema_id = t.schema_id
LEFT JOIN sys.index_columns AS ic
    ON ic.object_id = i.object_id
   AND ic.index_id = i.index_id
LEFT JOIN sys.columns AS c
    ON c.object_id = ic.object_id
   AND c.column_id = ic.column_id
WHERE s.name = N'dbo'
  AND t.name IN (
      N'Users',
      N'ApprovalRequests',
      N'Departments',
      N'ApprovalAssignments'
  )
  AND i.index_id > 0
ORDER BY t.name, i.name, ic.is_included_column, ic.key_ordinal, ic.index_column_id;

-- 9. Count duplicate email groups without returning email values.
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'Email') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT COUNT_BIG(*) AS DuplicateEmailGroupCount
        FROM (
            SELECT Email
            FROM dbo.Users
            GROUP BY Email
            HAVING COUNT_BIG(*) > 1
        ) AS duplicateGroups;';
ELSE
    SELECT
        N'Users.Email duplicate check' AS CheckName,
        N'NOT RUN: table or column missing or not visible' AS CheckState;

-- 10. Count duplicate department names and non-null codes without returning values.
IF OBJECT_ID(N'dbo.Departments', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Departments', N'Name') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT COUNT_BIG(*) AS DuplicateDepartmentNameGroupCount
        FROM (
            SELECT Name
            FROM dbo.Departments
            GROUP BY Name
            HAVING COUNT_BIG(*) > 1
        ) AS duplicateGroups;';
ELSE
    SELECT
        N'Departments.Name duplicate check' AS CheckName,
        N'NOT RUN: table or column missing or not visible' AS CheckState;

IF OBJECT_ID(N'dbo.Departments', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Departments', N'Code') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT COUNT_BIG(*) AS DuplicateNonNullDepartmentCodeGroupCount
        FROM (
            SELECT Code
            FROM dbo.Departments
            WHERE Code IS NOT NULL
            GROUP BY Code
            HAVING COUNT_BIG(*) > 1
        ) AS duplicateGroups;';
ELSE
    SELECT
        N'Departments.Code duplicate check' AS CheckName,
        N'NOT RUN: table or column missing or not visible' AS CheckState;

-- 11. Count rows with null request owner/department or user department only.
IF OBJECT_ID(N'dbo.ApprovalRequests', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.ApprovalRequests', N'CreatedByUserId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT
            COUNT_BIG(*) AS TotalRequestCount,
            COALESCE(
                SUM(CASE WHEN CreatedByUserId IS NULL THEN CONVERT(bigint, 1)
                         ELSE CONVERT(bigint, 0) END),
                CONVERT(bigint, 0)
            ) AS NullOwnerRequestCount
        FROM dbo.ApprovalRequests;';
ELSE
    SELECT
        N'ApprovalRequests owner counts' AS CheckName,
        N'NOT RUN: table or column missing or not visible' AS CheckState;

IF OBJECT_ID(N'dbo.ApprovalRequests', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.ApprovalRequests', N'DepartmentId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT
            COUNT_BIG(*) AS TotalRequestCount,
            COALESCE(
                SUM(CASE WHEN DepartmentId IS NULL THEN CONVERT(bigint, 1)
                         ELSE CONVERT(bigint, 0) END),
                CONVERT(bigint, 0)
            ) AS NullDepartmentRequestCount
        FROM dbo.ApprovalRequests;';
ELSE
    SELECT
        N'ApprovalRequests department counts' AS CheckName,
        N'NOT RUN: table or column missing or not visible' AS CheckState;

IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'DepartmentId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT
            COUNT_BIG(*) AS TotalUserCount,
            COALESCE(
                SUM(CASE WHEN DepartmentId IS NULL THEN CONVERT(bigint, 1)
                         ELSE CONVERT(bigint, 0) END),
                CONVERT(bigint, 0)
            ) AS NullDepartmentUserCount
        FROM dbo.Users;';
ELSE
    SELECT
        N'Users department counts' AS CheckName,
        N'NOT RUN: table or column missing or not visible' AS CheckState;

-- 12. Count orphaned relationship references; do not return row details.
IF OBJECT_ID(N'dbo.ApprovalRequests', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.ApprovalRequests', N'CreatedByUserId') IS NOT NULL
   AND OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'Id') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT COUNT_BIG(*) AS OrphanRequestOwnerCount
        FROM dbo.ApprovalRequests AS r
        LEFT JOIN dbo.Users AS u
            ON u.Id = r.CreatedByUserId
        WHERE r.CreatedByUserId IS NOT NULL
          AND u.Id IS NULL;';
ELSE
    SELECT
        N'Request owner orphan check' AS CheckName,
        N'NOT RUN: required table or column missing or not visible' AS CheckState;

IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'DepartmentId') IS NOT NULL
   AND OBJECT_ID(N'dbo.Departments', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Departments', N'Id') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT COUNT_BIG(*) AS OrphanUserDepartmentCount
        FROM dbo.Users AS u
        LEFT JOIN dbo.Departments AS d
            ON d.Id = u.DepartmentId
        WHERE u.DepartmentId IS NOT NULL
          AND d.Id IS NULL;';
ELSE
    SELECT
        N'User department orphan check' AS CheckName,
        N'NOT RUN: required table or column missing or not visible' AS CheckState;

IF OBJECT_ID(N'dbo.ApprovalRequests', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.ApprovalRequests', N'DepartmentId') IS NOT NULL
   AND OBJECT_ID(N'dbo.Departments', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Departments', N'Id') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT COUNT_BIG(*) AS OrphanRequestDepartmentCount
        FROM dbo.ApprovalRequests AS r
        LEFT JOIN dbo.Departments AS d
            ON d.Id = r.DepartmentId
        WHERE r.DepartmentId IS NOT NULL
          AND d.Id IS NULL;';
ELSE
    SELECT
        N'Request department orphan check' AS CheckName,
        N'NOT RUN: required table or column missing or not visible' AS CheckState;

IF OBJECT_ID(N'dbo.ApprovalAssignments', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.ApprovalAssignments', N'RequestId') IS NOT NULL
   AND COL_LENGTH(N'dbo.ApprovalAssignments', N'ApproverUserId') IS NOT NULL
   AND OBJECT_ID(N'dbo.ApprovalRequests', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.ApprovalRequests', N'Id') IS NOT NULL
   AND OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'Id') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT
            COALESCE(
                SUM(CASE WHEN r.Id IS NULL THEN CONVERT(bigint, 1)
                         ELSE CONVERT(bigint, 0) END),
                CONVERT(bigint, 0)
            ) AS OrphanAssignmentRequestCount,
            COALESCE(
                SUM(CASE WHEN u.Id IS NULL THEN CONVERT(bigint, 1)
                         ELSE CONVERT(bigint, 0) END),
                CONVERT(bigint, 0)
            ) AS OrphanAssignmentApproverCount
        FROM dbo.ApprovalAssignments AS a
        LEFT JOIN dbo.ApprovalRequests AS r
            ON r.Id = a.RequestId
        LEFT JOIN dbo.Users AS u
            ON u.Id = a.ApproverUserId;';
ELSE
    SELECT
        N'Assignment orphan checks' AS CheckName,
        N'NOT RUN: required table or column missing or not visible' AS CheckState;

-- 14. Count values that would prevent the security CHECK constraints.
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'Role') IS NOT NULL
   AND OBJECT_ID(N'dbo.ApprovalRequests', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.ApprovalRequests', N'Status') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT
            (SELECT COUNT_BIG(*)
             FROM dbo.Users
             WHERE [Role] IS NULL
                OR [Role] NOT IN (N''Employee'', N''Manager'', N''Admin''))
                AS InvalidUserRoleCount,
            (SELECT COUNT_BIG(*)
             FROM dbo.ApprovalRequests
             WHERE [Status] IS NULL
                OR [Status] NOT IN (1, 2, 3, 4))
                AS InvalidRequestStatusCount;';
ELSE
    SELECT
        N'Role and request status constraint preflight' AS CheckName,
        N'NOT RUN: required table or column missing or not visible' AS CheckState;
