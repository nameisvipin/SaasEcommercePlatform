-- ./docker/init-rls.sql
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'saas_app_user') THEN
        CREATE ROLE saas_app_user WITH LOGIN PASSWORD 'AppUser_StrictRLS_2026!' NOBYPASSRLS;
    END IF;
END
$$;

GRANT CONNECT ON DATABASE saas_ecommerce_db TO saas_app_user;
GRANT USAGE, CREATE ON SCHEMA public TO saas_app_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO saas_app_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO saas_app_user; 