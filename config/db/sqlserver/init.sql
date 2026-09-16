-- Creates the application database and an unprivileged login that owns only that database.
-- Idempotent: re-running it on an existing deployment makes no changes.
IF DB_ID('next_saas') IS NULL
    CREATE DATABASE [next_saas];
GO
IF SUSER_ID('next_saas') IS NULL
    CREATE LOGIN [next_saas] WITH PASSWORD = '$(app_password)', CHECK_POLICY = ON;
GO
USE [next_saas];
GO
IF USER_ID('next_saas') IS NULL
    CREATE USER [next_saas] FOR LOGIN [next_saas];
GO
-- db_owner within next_saas only. The application creates its own schema through EF Core
-- and OrmLite migrations, but holds no server-level rights.
ALTER ROLE db_owner ADD MEMBER [next_saas];
GO
