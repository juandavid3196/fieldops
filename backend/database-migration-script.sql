CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260921233932_InitialTenancy') THEN
    CREATE TABLE organizations (
        id uuid NOT NULL,
        name character varying(160) NOT NULL,
        legal_name character varying(200),
        tax_id character varying(60),
        timezone character varying(80) NOT NULL,
        currency character(3) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_organizations PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260921233932_InitialTenancy') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260921233932_InitialTenancy', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE TYPE user_status AS ENUM ('pending', 'active', 'suspended', 'disabled');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ALTER COLUMN updated_at SET DEFAULT (now());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ALTER COLUMN timezone SET DEFAULT 'UTC';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ALTER COLUMN is_active SET DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ALTER COLUMN currency SET DEFAULT 'USD';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ALTER COLUMN created_at SET DEFAULT (now());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ALTER COLUMN id SET DEFAULT (gen_random_uuid());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ADD default_tax_rate numeric(7,4) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ADD email character varying(254);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ADD invoice_prefix character varying(20) NOT NULL DEFAULT 'INV';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ADD next_invoice_number bigint NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ADD next_quote_number bigint NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ADD next_work_order_number bigint NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ADD phone character varying(40);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ADD quote_prefix character varying(20) NOT NULL DEFAULT 'Q';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ADD require_customer_signature boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ADD work_order_prefix character varying(20) NOT NULL DEFAULT 'WO';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE TABLE branches (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        name character varying(140) NOT NULL,
        code character varying(30) NOT NULL,
        email character varying(254),
        phone character varying(40),
        address_line1 character varying(180),
        address_line2 character varying(180),
        city character varying(100),
        state_region character varying(100),
        postal_code character varying(30),
        country_code character(2),
        timezone character varying(80),
        business_hours jsonb NOT NULL DEFAULT ('{}'::jsonb),
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_branches PRIMARY KEY (id),
        CONSTRAINT ak_branches_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT fk_branches_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE TABLE permissions (
        id smallint GENERATED BY DEFAULT AS IDENTITY,
        code character varying(100) NOT NULL,
        description text NOT NULL,
        CONSTRAINT pk_permissions PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE TABLE roles (
        id smallint GENERATED BY DEFAULT AS IDENTITY,
        code character varying(50) NOT NULL,
        name character varying(100) NOT NULL,
        description text,
        is_canonical boolean NOT NULL,
        CONSTRAINT pk_roles PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE TABLE users (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        email character varying(254) NOT NULL,
        password_hash text NOT NULL,
        first_name character varying(100) NOT NULL,
        last_name character varying(100) NOT NULL,
        phone character varying(40),
        status user_status NOT NULL,
        email_verified_at timestamp with time zone,
        last_login_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_users PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE TABLE role_permissions (
        role_id smallint NOT NULL,
        permission_id smallint NOT NULL,
        CONSTRAINT pk_role_permissions PRIMARY KEY (role_id, permission_id),
        CONSTRAINT fk_role_permissions_permissions_permission_id FOREIGN KEY (permission_id) REFERENCES permissions (id) ON DELETE CASCADE,
        CONSTRAINT fk_role_permissions_roles_role_id FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE TABLE organization_users (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        user_id uuid NOT NULL,
        role_id smallint NOT NULL,
        status user_status NOT NULL,
        is_all_branches boolean NOT NULL,
        invited_by_user_id uuid,
        joined_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_organization_users PRIMARY KEY (id),
        CONSTRAINT ak_organization_users_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT fk_organization_users_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_organization_users_roles_role_id FOREIGN KEY (role_id) REFERENCES roles (id),
        CONSTRAINT fk_organization_users_users_invited_by_user_id FOREIGN KEY (invited_by_user_id) REFERENCES users (id),
        CONSTRAINT fk_organization_users_users_user_id FOREIGN KEY (user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE TABLE user_invitations (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        email character varying(254) NOT NULL,
        role_id smallint NOT NULL,
        token_hash text NOT NULL,
        invited_by_user_id uuid NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        accepted_at timestamp with time zone,
        revoked_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_user_invitations PRIMARY KEY (id),
        CONSTRAINT fk_user_invitations_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_user_invitations_roles_role_id FOREIGN KEY (role_id) REFERENCES roles (id),
        CONSTRAINT fk_user_invitations_users_invited_by_user_id FOREIGN KEY (invited_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE TABLE organization_user_branches (
        organization_user_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        CONSTRAINT pk_organization_user_branches PRIMARY KEY (organization_user_id, branch_id),
        CONSTRAINT fk_organization_user_branches_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE CASCADE,
        CONSTRAINT fk_organization_user_branches_organization_users_organization_ FOREIGN KEY (organization_user_id) REFERENCES organization_users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE TABLE invitation_branches (
        invitation_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        CONSTRAINT pk_invitation_branches PRIMARY KEY (invitation_id, branch_id),
        CONSTRAINT fk_invitation_branches_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id),
        CONSTRAINT fk_invitation_branches_user_invitations_invitation_id FOREIGN KEY (invitation_id) REFERENCES user_invitations (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    INSERT INTO roles (id, code, description, is_canonical, name)
    VALUES (1, 'owner', NULL, TRUE, 'Owner');
    INSERT INTO roles (id, code, description, is_canonical, name)
    VALUES (2, 'dispatcher', NULL, TRUE, 'Dispatcher');
    INSERT INTO roles (id, code, description, is_canonical, name)
    VALUES (3, 'technician', NULL, TRUE, 'Technician');
    INSERT INTO roles (id, code, description, is_canonical, name)
    VALUES (4, 'accounting', NULL, TRUE, 'Accounting');
    INSERT INTO roles (id, code, description, is_canonical, name)
    VALUES (5, 'operations_manager', NULL, TRUE, 'Operations Manager');
    INSERT INTO roles (id, code, description, is_canonical, name)
    VALUES (6, 'viewer', NULL, TRUE, 'Viewer');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    ALTER TABLE organizations ADD CONSTRAINT ck_organizations_default_tax_rate CHECK (default_tax_rate BETWEEN 0 AND 100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_branches_org_active ON branches (organization_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE UNIQUE INDEX ix_branches_organization_id_code ON branches (organization_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_invitation_branches_branch_id ON invitation_branches (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_organization_user_branches_branch_id ON organization_user_branches (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_organization_users_invited_by_user_id ON organization_users (invited_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE UNIQUE INDEX ix_organization_users_organization_id_user_id ON organization_users (organization_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_organization_users_role_id ON organization_users (role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_organization_users_user_id ON organization_users (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE UNIQUE INDEX ix_permissions_code ON permissions (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_role_permissions_permission_id ON role_permissions (permission_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE UNIQUE INDEX ix_roles_code ON roles (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_user_invitations_invited_by_user_id ON user_invitations (invited_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_user_invitations_organization_id ON user_invitations (organization_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_user_invitations_role_id ON user_invitations (role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE UNIQUE INDEX ix_user_invitations_token_hash ON user_invitations (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    CREATE UNIQUE INDEX ix_users_email ON users (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    PERFORM setval(
        pg_get_serial_sequence('roles', 'id'),
        GREATEST(
            (SELECT MAX(id) FROM roles) + 1,
            nextval(pg_get_serial_sequence('roles', 'id'))),
        false);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922004838_InitialIdentityAndTenancy') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922004838_InitialIdentityAndTenancy', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE TYPE catalog_item_type AS ENUM ('service', 'product');
    CREATE TYPE customer_type AS ENUM ('person', 'company');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE TABLE customers (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        type customer_type NOT NULL,
        display_name character varying(180) NOT NULL,
        legal_name character varying(200),
        tax_id character varying(60),
        primary_email character varying(254),
        primary_phone character varying(40),
        billing_address jsonb,
        notes text,
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_customers PRIMARY KEY (id),
        CONSTRAINT ak_customers_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT fk_customers_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE TABLE service_categories (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        name character varying(120) NOT NULL,
        description text,
        is_active boolean NOT NULL DEFAULT TRUE,
        CONSTRAINT pk_service_categories PRIMARY KEY (id),
        CONSTRAINT ak_service_categories_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT fk_service_categories_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE TABLE customer_contacts (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        customer_id uuid NOT NULL,
        first_name character varying(100) NOT NULL,
        last_name character varying(100),
        email character varying(254),
        phone character varying(40),
        title character varying(100),
        is_primary boolean NOT NULL DEFAULT FALSE,
        portal_user_id uuid,
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_customer_contacts PRIMARY KEY (id),
        CONSTRAINT ak_customer_contacts_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT fk_customer_contacts_customers_organization_id_customer_id FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id),
        CONSTRAINT fk_customer_contacts_users_portal_user_id FOREIGN KEY (portal_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE TABLE customer_notes (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        customer_id uuid NOT NULL,
        author_user_id uuid NOT NULL,
        note text NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_customer_notes PRIMARY KEY (id),
        CONSTRAINT fk_customer_notes_customers_organization_id_customer_id FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id),
        CONSTRAINT fk_customer_notes_users_author_user_id FOREIGN KEY (author_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE TABLE properties (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        customer_id uuid NOT NULL,
        branch_id uuid,
        name character varying(140) NOT NULL,
        address_line1 character varying(180) NOT NULL,
        address_line2 character varying(180),
        city character varying(100) NOT NULL,
        state_region character varying(100),
        postal_code character varying(30),
        country_code character(2) NOT NULL,
        latitude numeric(9,6),
        longitude numeric(9,6),
        access_instructions text,
        service_notes text,
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_properties PRIMARY KEY (id),
        CONSTRAINT ak_properties_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT fk_properties_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id),
        CONSTRAINT fk_properties_customers_organization_id_customer_id FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE TABLE catalog_items (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        category_id uuid,
        type catalog_item_type NOT NULL,
        sku character varying(60),
        name character varying(160) NOT NULL,
        description text,
        unit character varying(40) NOT NULL DEFAULT 'unit',
        unit_cost numeric(14,2) NOT NULL DEFAULT 0.0,
        unit_price numeric(14,2) NOT NULL,
        tax_rate numeric(7,4) NOT NULL DEFAULT 0.0,
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_catalog_items PRIMARY KEY (id),
        CONSTRAINT ak_catalog_items_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT ck_catalog_items_tax_rate CHECK (tax_rate BETWEEN 0 AND 100),
        CONSTRAINT ck_catalog_items_unit_cost CHECK (unit_cost >= 0),
        CONSTRAINT ck_catalog_items_unit_price CHECK (unit_price >= 0),
        CONSTRAINT fk_catalog_items_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_catalog_items_service_categories_organization_id_category_id FOREIGN KEY (organization_id, category_id) REFERENCES service_categories (organization_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE INDEX ix_catalog_items_organization_id_category_id ON catalog_items (organization_id, category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE UNIQUE INDEX ix_catalog_items_organization_id_sku ON catalog_items (organization_id, sku);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE INDEX ix_contacts_org_email ON customer_contacts (organization_id, email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE INDEX ix_customer_contacts_organization_id_customer_id ON customer_contacts (organization_id, customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE INDEX ix_customer_contacts_portal_user_id ON customer_contacts (portal_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE INDEX ix_customer_notes_author_user_id ON customer_notes (author_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE INDEX ix_customer_notes_organization_id_customer_id ON customer_notes (organization_id, customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE INDEX ix_customers_org_name ON customers (organization_id, display_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE INDEX ix_properties_branch_id ON properties (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE INDEX ix_properties_customer ON properties (organization_id, customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    CREATE UNIQUE INDEX ix_service_categories_organization_id_name ON service_categories (organization_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922154556_AddCustomersAndCatalog') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922154556_AddCustomersAndCatalog', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE TABLE skills (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        name character varying(120) NOT NULL,
        description text,
        is_active boolean NOT NULL DEFAULT TRUE,
        CONSTRAINT pk_skills PRIMARY KEY (id),
        CONSTRAINT ak_skills_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT fk_skills_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE TABLE technician_profiles (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        organization_user_id uuid,
        employee_code character varying(50),
        first_name character varying(100) NOT NULL,
        last_name character varying(100) NOT NULL,
        email character varying(254),
        phone character varying(40),
        status character varying(30) NOT NULL DEFAULT ('active'),
        color_hex character(7),
        notes text,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_technician_profiles PRIMARY KEY (id),
        CONSTRAINT ak_technician_profiles_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT ck_technician_profiles_status CHECK (status IN ('active','inactive','suspended')),
        CONSTRAINT fk_technician_profiles_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id),
        CONSTRAINT fk_technician_profiles_organization_users_organization_id_orga FOREIGN KEY (organization_id, organization_user_id) REFERENCES organization_users (organization_id, id),
        CONSTRAINT fk_technician_profiles_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE TABLE technician_exceptions (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        technician_id uuid NOT NULL,
        starts_at timestamp with time zone NOT NULL,
        ends_at timestamp with time zone NOT NULL,
        is_available boolean NOT NULL DEFAULT FALSE,
        reason character varying(200),
        CONSTRAINT pk_technician_exceptions PRIMARY KEY (id),
        CONSTRAINT ck_technician_exceptions_start_end CHECK (starts_at < ends_at),
        CONSTRAINT fk_technician_exceptions_technician_profiles_technician_id FOREIGN KEY (technician_id) REFERENCES technician_profiles (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE TABLE technician_skills (
        technician_id uuid NOT NULL,
        skill_id uuid NOT NULL,
        proficiency smallint,
        years_experience numeric(4,1),
        CONSTRAINT pk_technician_skills PRIMARY KEY (technician_id, skill_id),
        CONSTRAINT ck_technician_skills_proficiency CHECK (proficiency BETWEEN 1 AND 5),
        CONSTRAINT fk_technician_skills_skills_skill_id FOREIGN KEY (skill_id) REFERENCES skills (id),
        CONSTRAINT fk_technician_skills_technician_profiles_technician_id FOREIGN KEY (technician_id) REFERENCES technician_profiles (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE TABLE technician_weekly_availability (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        technician_id uuid NOT NULL,
        day_of_week smallint NOT NULL,
        start_time time without time zone NOT NULL,
        end_time time without time zone NOT NULL,
        capacity_percent smallint NOT NULL DEFAULT 100,
        CONSTRAINT pk_technician_weekly_availability PRIMARY KEY (id),
        CONSTRAINT ck_technician_weekly_availability_capacity_percent CHECK (capacity_percent BETWEEN 1 AND 100),
        CONSTRAINT ck_technician_weekly_availability_day_of_week CHECK (day_of_week BETWEEN 0 AND 6),
        CONSTRAINT ck_technician_weekly_availability_start_end CHECK (start_time < end_time),
        CONSTRAINT fk_technician_weekly_availability_technician_profiles_technici FOREIGN KEY (technician_id) REFERENCES technician_profiles (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE TABLE technician_breaks (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        availability_id uuid NOT NULL,
        start_time time without time zone NOT NULL,
        end_time time without time zone NOT NULL,
        CONSTRAINT pk_technician_breaks PRIMARY KEY (id),
        CONSTRAINT ck_technician_breaks_start_end CHECK (start_time < end_time),
        CONSTRAINT fk_technician_breaks_technician_weekly_availabilities_availabi FOREIGN KEY (availability_id) REFERENCES technician_weekly_availability (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE UNIQUE INDEX ix_skills_organization_id_name ON skills (organization_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE INDEX ix_technician_breaks_availability_id ON technician_breaks (availability_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE INDEX ix_technician_exceptions_technician_id ON technician_exceptions (technician_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE INDEX ix_technician_profiles_branch_id ON technician_profiles (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE UNIQUE INDEX ix_technician_profiles_organization_id_employee_code ON technician_profiles (organization_id, employee_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE INDEX ix_technician_profiles_organization_id_organization_user_id ON technician_profiles (organization_id, organization_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE INDEX ix_technicians_branch_status ON technician_profiles (organization_id, branch_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE INDEX ix_technician_skills_skill_id ON technician_skills (skill_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    CREATE INDEX ix_technician_weekly_availability_technician_id ON technician_weekly_availability (technician_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922155938_AddTechniciansAndAvailability') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922155938_AddTechniciansAndAvailability', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161604_AddCustomerOrganizationForeignKeys') THEN
    ALTER TABLE customer_contacts ADD CONSTRAINT fk_customer_contacts_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161604_AddCustomerOrganizationForeignKeys') THEN
    ALTER TABLE customer_notes ADD CONSTRAINT fk_customer_notes_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161604_AddCustomerOrganizationForeignKeys') THEN
    ALTER TABLE properties ADD CONSTRAINT fk_properties_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161604_AddCustomerOrganizationForeignKeys') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922161604_AddCustomerOrganizationForeignKeys', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE TYPE assessment_status AS ENUM ('scheduled', 'completed', 'cancelled', 'no_show');
    CREATE TYPE message_visibility AS ENUM ('customer', 'internal');
    CREATE TYPE request_status AS ENUM ('new', 'needs_review', 'assessment_scheduled', 'ready_for_quote', 'quoted', 'converted', 'cancelled');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE TABLE service_requests (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        branch_id uuid,
        request_number bigint NOT NULL,
        customer_id uuid,
        contact_id uuid,
        property_id uuid,
        category_id uuid,
        guest_name character varying(180),
        guest_email character varying(254),
        guest_phone character varying(40),
        service_address jsonb,
        description text NOT NULL,
        preferred_start timestamp with time zone,
        preferred_end timestamp with time zone,
        status request_status NOT NULL,
        source character varying(30) NOT NULL DEFAULT 'public_form',
        assigned_dispatcher_user_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        cancelled_at timestamp with time zone,
        CONSTRAINT pk_service_requests PRIMARY KEY (id),
        CONSTRAINT ak_service_requests_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT ck_service_requests_preferred_range CHECK (preferred_end IS NULL OR preferred_start IS NULL OR preferred_start < preferred_end),
        CONSTRAINT fk_service_requests_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id),
        CONSTRAINT fk_service_requests_customer_contacts_organization_id_contact_ FOREIGN KEY (organization_id, contact_id) REFERENCES customer_contacts (organization_id, id),
        CONSTRAINT fk_service_requests_customers_organization_id_customer_id FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id),
        CONSTRAINT fk_service_requests_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_service_requests_properties_organization_id_property_id FOREIGN KEY (organization_id, property_id) REFERENCES properties (organization_id, id),
        CONSTRAINT fk_service_requests_service_categories_organization_id_categor FOREIGN KEY (organization_id, category_id) REFERENCES service_categories (organization_id, id),
        CONSTRAINT fk_service_requests_users_assigned_dispatcher_user_id FOREIGN KEY (assigned_dispatcher_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE TABLE assessments (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        request_id uuid NOT NULL,
        technician_id uuid,
        scheduled_start timestamp with time zone NOT NULL,
        scheduled_end timestamp with time zone NOT NULL,
        status assessment_status NOT NULL,
        diagnosis text,
        recommended_scope text,
        internal_notes text,
        completed_at timestamp with time zone,
        created_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_assessments PRIMARY KEY (id),
        CONSTRAINT ak_assessments_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT ck_assessments_schedule_range CHECK (scheduled_start < scheduled_end),
        CONSTRAINT fk_assessments_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_assessments_service_requests_organization_id_request_id FOREIGN KEY (organization_id, request_id) REFERENCES service_requests (organization_id, id),
        CONSTRAINT fk_assessments_technician_profiles_technician_id FOREIGN KEY (technician_id) REFERENCES technician_profiles (id),
        CONSTRAINT fk_assessments_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE TABLE request_attachments (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        request_id uuid NOT NULL,
        file_name character varying(255) NOT NULL,
        storage_key text NOT NULL,
        mime_type character varying(120) NOT NULL,
        size_bytes bigint NOT NULL,
        uploaded_by_user_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_request_attachments PRIMARY KEY (id),
        CONSTRAINT ck_request_attachments_size_bytes CHECK (size_bytes > 0),
        CONSTRAINT fk_request_attachments_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_request_attachments_service_requests_organization_id_reques FOREIGN KEY (organization_id, request_id) REFERENCES service_requests (organization_id, id),
        CONSTRAINT fk_request_attachments_users_uploaded_by_user_id FOREIGN KEY (uploaded_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE TABLE request_messages (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        request_id uuid NOT NULL,
        author_user_id uuid,
        author_contact_id uuid,
        visibility message_visibility NOT NULL,
        body text NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_request_messages PRIMARY KEY (id),
        CONSTRAINT fk_request_messages_customer_contacts_author_contact_id FOREIGN KEY (author_contact_id) REFERENCES customer_contacts (id),
        CONSTRAINT fk_request_messages_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_request_messages_service_requests_organization_id_request_id FOREIGN KEY (organization_id, request_id) REFERENCES service_requests (organization_id, id),
        CONSTRAINT fk_request_messages_users_author_user_id FOREIGN KEY (author_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE TABLE request_status_history (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        request_id uuid NOT NULL,
        from_status request_status,
        to_status request_status NOT NULL,
        changed_by_user_id uuid,
        reason text,
        changed_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_request_status_history PRIMARY KEY (id),
        CONSTRAINT fk_request_status_history_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_request_status_history_service_requests_organization_id_req FOREIGN KEY (organization_id, request_id) REFERENCES service_requests (organization_id, id),
        CONSTRAINT fk_request_status_history_users_changed_by_user_id FOREIGN KEY (changed_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE TABLE assessment_attachments (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        assessment_id uuid NOT NULL,
        file_name character varying(255) NOT NULL,
        storage_key text NOT NULL,
        mime_type character varying(120) NOT NULL,
        size_bytes bigint NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_assessment_attachments PRIMARY KEY (id),
        CONSTRAINT fk_assessment_attachments_assessments_assessment_id FOREIGN KEY (assessment_id) REFERENCES assessments (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_assessment_attachments_assessment_id ON assessment_attachments (assessment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_assessments_created_by_user_id ON assessments (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_assessments_organization_id_request_id ON assessments (organization_id, request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_assessments_schedule ON assessments (organization_id, scheduled_start, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_assessments_technician_id ON assessments (technician_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_request_attachments_organization_id_request_id ON request_attachments (organization_id, request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_request_attachments_uploaded_by_user_id ON request_attachments (uploaded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_request_messages_author_contact_id ON request_messages (author_contact_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_request_messages_author_user_id ON request_messages (author_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_request_messages_organization_id_request_id ON request_messages (organization_id, request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_request_status_history_changed_by_user_id ON request_status_history (changed_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_request_status_history_organization_id_request_id ON request_status_history (organization_id, request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_requests_customer ON service_requests (organization_id, customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_requests_pipeline ON service_requests (organization_id, status, created_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_service_requests_assigned_dispatcher_user_id ON service_requests (assigned_dispatcher_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_service_requests_branch_id ON service_requests (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_service_requests_organization_id_category_id ON service_requests (organization_id, category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_service_requests_organization_id_contact_id ON service_requests (organization_id, contact_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE INDEX ix_service_requests_organization_id_property_id ON service_requests (organization_id, property_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    CREATE UNIQUE INDEX ix_service_requests_organization_id_request_number ON service_requests (organization_id, request_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922161833_AddRequestsAndAssessments') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922161833_AddRequestsAndAssessments', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE TYPE quote_status AS ENUM ('draft', 'sent', 'approved', 'rejected', 'clarification_requested', 'expired', 'cancelled');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE TABLE quote_lines (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        quote_version_id uuid NOT NULL,
        catalog_item_id uuid,
        line_type catalog_item_type NOT NULL,
        description text NOT NULL,
        quantity numeric(12,3) NOT NULL,
        unit character varying(40) NOT NULL,
        unit_cost numeric(14,2) NOT NULL DEFAULT 0.0,
        unit_price numeric(14,2) NOT NULL,
        tax_rate numeric(7,4) NOT NULL DEFAULT 0.0,
        line_subtotal numeric(14,2) NOT NULL,
        line_tax numeric(14,2) NOT NULL,
        line_total numeric(14,2) NOT NULL,
        sort_order integer NOT NULL DEFAULT 0,
        CONSTRAINT pk_quote_lines PRIMARY KEY (id),
        CONSTRAINT ck_quote_lines_quantity CHECK (quantity > 0),
        CONSTRAINT ck_quote_lines_unit_price CHECK (unit_price >= 0),
        CONSTRAINT fk_quote_lines_catalog_items_catalog_item_id FOREIGN KEY (catalog_item_id) REFERENCES catalog_items (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE TABLE quote_responses (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        quote_version_id uuid NOT NULL,
        response quote_status NOT NULL,
        responder_name character varying(180) NOT NULL,
        responder_contact_id uuid,
        comment text,
        responded_at timestamp with time zone NOT NULL DEFAULT (now()),
        ip_address inet,
        CONSTRAINT pk_quote_responses PRIMARY KEY (id),
        CONSTRAINT ck_quote_responses_response CHECK (response IN ('approved','rejected','clarification_requested')),
        CONSTRAINT fk_quote_responses_customer_contacts_responder_contact_id FOREIGN KEY (responder_contact_id) REFERENCES customer_contacts (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE TABLE quote_versions (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        quote_id uuid NOT NULL,
        version_no integer NOT NULL,
        scope text NOT NULL,
        customer_notes text,
        internal_notes text,
        subtotal numeric(14,2) NOT NULL,
        tax_total numeric(14,2) NOT NULL,
        total numeric(14,2) NOT NULL,
        currency character(3) NOT NULL,
        valid_until date,
        sent_at timestamp with time zone,
        is_immutable boolean NOT NULL DEFAULT FALSE,
        created_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_quote_versions PRIMARY KEY (id),
        CONSTRAINT ak_quote_versions_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT ck_quote_versions_subtotal CHECK (subtotal >= 0),
        CONSTRAINT ck_quote_versions_tax_total CHECK (tax_total >= 0),
        CONSTRAINT ck_quote_versions_total CHECK (total >= 0),
        CONSTRAINT ck_quote_versions_version_no CHECK (version_no > 0),
        CONSTRAINT fk_quote_versions_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_quote_versions_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE TABLE quotes (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        branch_id uuid,
        request_id uuid NOT NULL,
        customer_id uuid,
        property_id uuid,
        quote_number bigint NOT NULL,
        status quote_status NOT NULL,
        current_version_no integer NOT NULL DEFAULT 0,
        approved_version_id uuid,
        created_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_quotes PRIMARY KEY (id),
        CONSTRAINT ak_quotes_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT fk_quotes_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id),
        CONSTRAINT fk_quotes_customers_organization_id_customer_id FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id),
        CONSTRAINT fk_quotes_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_quotes_properties_organization_id_property_id FOREIGN KEY (organization_id, property_id) REFERENCES properties (organization_id, id),
        CONSTRAINT fk_quotes_quote_versions_approved_version_id FOREIGN KEY (approved_version_id) REFERENCES quote_versions (id),
        CONSTRAINT fk_quotes_service_requests_organization_id_request_id FOREIGN KEY (organization_id, request_id) REFERENCES service_requests (organization_id, id),
        CONSTRAINT fk_quotes_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quote_lines_catalog_item_id ON quote_lines (catalog_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quote_lines_quote_version_id ON quote_lines (quote_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quote_responses_quote_version_id ON quote_responses (quote_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quote_responses_responder_contact_id ON quote_responses (responder_contact_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quote_versions_created_by_user_id ON quote_versions (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quote_versions_organization_id_quote_id ON quote_versions (organization_id, quote_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE UNIQUE INDEX ix_quote_versions_quote_id_version_no ON quote_versions (quote_id, version_no);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quotes_approved_version_id ON quotes (approved_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quotes_branch_id ON quotes (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quotes_created_by_user_id ON quotes (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quotes_organization_id_customer_id ON quotes (organization_id, customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quotes_organization_id_property_id ON quotes (organization_id, property_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE UNIQUE INDEX ix_quotes_organization_id_quote_number ON quotes (organization_id, quote_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quotes_organization_id_request_id ON quotes (organization_id, request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    CREATE INDEX ix_quotes_status ON quotes (organization_id, status, created_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    ALTER TABLE quote_lines ADD CONSTRAINT fk_quote_lines_quote_versions_quote_version_id FOREIGN KEY (quote_version_id) REFERENCES quote_versions (id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    ALTER TABLE quote_responses ADD CONSTRAINT fk_quote_responses_quote_versions_quote_version_id FOREIGN KEY (quote_version_id) REFERENCES quote_versions (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    ALTER TABLE quote_versions ADD CONSTRAINT fk_quote_versions_quotes_organization_id_quote_id FOREIGN KEY (organization_id, quote_id) REFERENCES quotes (organization_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922162931_AddQuotes') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922162931_AddQuotes', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TYPE visit_status AS ENUM ('unscheduled', 'scheduled', 'assigned', 'on_the_way', 'in_progress', 'paused', 'completed', 'needs_correction', 'approved', 'cancelled');
    CREATE TYPE work_order_status AS ENUM ('draft', 'ready_to_schedule', 'scheduled', 'in_progress', 'completed', 'approved_for_billing', 'cancelled');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE work_orders (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        work_order_number bigint NOT NULL,
        quote_version_id uuid NOT NULL,
        customer_id uuid NOT NULL,
        property_id uuid NOT NULL,
        status work_order_status NOT NULL,
        priority smallint NOT NULL DEFAULT 3,
        scope_snapshot text NOT NULL,
        internal_instructions text,
        preferred_start timestamp with time zone,
        preferred_end timestamp with time zone,
        created_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_work_orders PRIMARY KEY (id),
        CONSTRAINT ak_work_orders_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT ck_work_orders_priority CHECK (priority BETWEEN 1 AND 5),
        CONSTRAINT fk_work_orders_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id),
        CONSTRAINT fk_work_orders_customers_organization_id_customer_id FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id),
        CONSTRAINT fk_work_orders_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_work_orders_properties_organization_id_property_id FOREIGN KEY (organization_id, property_id) REFERENCES properties (organization_id, id),
        CONSTRAINT fk_work_orders_quote_versions_quote_version_id FOREIGN KEY (quote_version_id) REFERENCES quote_versions (id),
        CONSTRAINT fk_work_orders_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE visits (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        work_order_id uuid NOT NULL,
        visit_number integer NOT NULL,
        status visit_status NOT NULL,
        scheduled_start timestamp with time zone,
        scheduled_end timestamp with time zone,
        actual_started_at timestamp with time zone,
        actual_completed_at timestamp with time zone,
        pause_seconds integer NOT NULL DEFAULT 0,
        completion_summary text,
        completion_without_signature_reason text,
        review_notes text,
        reviewed_by_user_id uuid,
        reviewed_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_visits PRIMARY KEY (id),
        CONSTRAINT ak_visits_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT ck_visits_pause_seconds CHECK (pause_seconds >= 0),
        CONSTRAINT ck_visits_schedule_range CHECK (scheduled_end IS NULL OR scheduled_start IS NULL OR scheduled_start < scheduled_end),
        CONSTRAINT ck_visits_visit_number CHECK (visit_number > 0),
        CONSTRAINT fk_visits_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_visits_users_reviewed_by_user_id FOREIGN KEY (reviewed_by_user_id) REFERENCES users (id),
        CONSTRAINT fk_visits_work_orders_organization_id_work_order_id FOREIGN KEY (organization_id, work_order_id) REFERENCES work_orders (organization_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE work_order_checklist_templates (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        work_order_id uuid NOT NULL,
        label character varying(240) NOT NULL,
        is_required boolean NOT NULL DEFAULT TRUE,
        sort_order integer NOT NULL DEFAULT 0,
        CONSTRAINT pk_work_order_checklist_templates PRIMARY KEY (id),
        CONSTRAINT fk_work_order_checklist_templates_work_orders_work_order_id FOREIGN KEY (work_order_id) REFERENCES work_orders (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE work_order_required_skills (
        work_order_id uuid NOT NULL,
        skill_id uuid NOT NULL,
        minimum_proficiency smallint,
        CONSTRAINT pk_work_order_required_skills PRIMARY KEY (work_order_id, skill_id),
        CONSTRAINT ck_work_order_required_skills_minimum_proficiency CHECK (minimum_proficiency BETWEEN 1 AND 5),
        CONSTRAINT fk_work_order_required_skills_skills_skill_id FOREIGN KEY (skill_id) REFERENCES skills (id),
        CONSTRAINT fk_work_order_required_skills_work_orders_work_order_id FOREIGN KEY (work_order_id) REFERENCES work_orders (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE customer_signoffs (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        visit_id uuid NOT NULL,
        signer_name character varying(180),
        signer_contact_id uuid,
        signature_storage_key text,
        accepted boolean NOT NULL,
        comments text,
        absence_reason text,
        signed_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_customer_signoffs PRIMARY KEY (id),
        CONSTRAINT fk_customer_signoffs_customer_contacts_signer_contact_id FOREIGN KEY (signer_contact_id) REFERENCES customer_contacts (id),
        CONSTRAINT fk_customer_signoffs_visits_visit_id FOREIGN KEY (visit_id) REFERENCES visits (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE visit_assignments (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        visit_id uuid NOT NULL,
        technician_id uuid NOT NULL,
        assigned_by_user_id uuid NOT NULL,
        is_primary boolean NOT NULL DEFAULT TRUE,
        assigned_at timestamp with time zone NOT NULL DEFAULT (now()),
        unassigned_at timestamp with time zone,
        CONSTRAINT pk_visit_assignments PRIMARY KEY (id),
        CONSTRAINT fk_visit_assignments_technician_profiles_technician_id FOREIGN KEY (technician_id) REFERENCES technician_profiles (id),
        CONSTRAINT fk_visit_assignments_users_assigned_by_user_id FOREIGN KEY (assigned_by_user_id) REFERENCES users (id),
        CONSTRAINT fk_visit_assignments_visits_visit_id FOREIGN KEY (visit_id) REFERENCES visits (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE visit_evidence (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        visit_id uuid NOT NULL,
        file_name character varying(255) NOT NULL,
        storage_key text NOT NULL,
        mime_type character varying(120) NOT NULL,
        size_bytes bigint NOT NULL,
        evidence_type character varying(30) NOT NULL,
        caption text,
        uploaded_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_visit_evidence PRIMARY KEY (id),
        CONSTRAINT ck_visit_evidence_evidence_type CHECK (evidence_type IN ('before','during','after','incident','other')),
        CONSTRAINT ck_visit_evidence_size_bytes CHECK (size_bytes > 0),
        CONSTRAINT fk_visit_evidence_users_uploaded_by_user_id FOREIGN KEY (uploaded_by_user_id) REFERENCES users (id),
        CONSTRAINT fk_visit_evidence_visits_visit_id FOREIGN KEY (visit_id) REFERENCES visits (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE visit_incidents (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        visit_id uuid NOT NULL,
        type character varying(80) NOT NULL,
        description text NOT NULL,
        additional_work_requested boolean NOT NULL DEFAULT FALSE,
        created_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_visit_incidents PRIMARY KEY (id),
        CONSTRAINT fk_visit_incidents_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id),
        CONSTRAINT fk_visit_incidents_visits_visit_id FOREIGN KEY (visit_id) REFERENCES visits (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE visit_materials (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        visit_id uuid NOT NULL,
        catalog_item_id uuid,
        description text NOT NULL,
        quantity numeric(12,3) NOT NULL,
        unit character varying(40) NOT NULL,
        unit_cost numeric(14,2) NOT NULL DEFAULT 0.0,
        billable boolean NOT NULL DEFAULT FALSE,
        CONSTRAINT pk_visit_materials PRIMARY KEY (id),
        CONSTRAINT ck_visit_materials_quantity CHECK (quantity > 0),
        CONSTRAINT fk_visit_materials_catalog_items_catalog_item_id FOREIGN KEY (catalog_item_id) REFERENCES catalog_items (id),
        CONSTRAINT fk_visit_materials_visits_visit_id FOREIGN KEY (visit_id) REFERENCES visits (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE visit_status_history (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        visit_id uuid NOT NULL,
        from_status visit_status,
        to_status visit_status NOT NULL,
        changed_by_user_id uuid,
        reason text,
        changed_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_visit_status_history PRIMARY KEY (id),
        CONSTRAINT fk_visit_status_history_users_changed_by_user_id FOREIGN KEY (changed_by_user_id) REFERENCES users (id),
        CONSTRAINT fk_visit_status_history_visits_visit_id FOREIGN KEY (visit_id) REFERENCES visits (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE visit_time_entries (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        visit_id uuid NOT NULL,
        technician_id uuid NOT NULL,
        started_at timestamp with time zone NOT NULL,
        ended_at timestamp with time zone,
        entry_type character varying(20) NOT NULL,
        CONSTRAINT pk_visit_time_entries PRIMARY KEY (id),
        CONSTRAINT ck_visit_time_entries_entry_type CHECK (entry_type IN ('work','pause','travel')),
        CONSTRAINT ck_visit_time_entries_start_end CHECK (ended_at IS NULL OR started_at < ended_at),
        CONSTRAINT fk_visit_time_entries_technician_profiles_technician_id FOREIGN KEY (technician_id) REFERENCES technician_profiles (id),
        CONSTRAINT fk_visit_time_entries_visits_visit_id FOREIGN KEY (visit_id) REFERENCES visits (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE TABLE visit_checklist_items (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        visit_id uuid NOT NULL,
        template_item_id uuid,
        label character varying(240) NOT NULL,
        is_required boolean NOT NULL DEFAULT TRUE,
        is_completed boolean NOT NULL DEFAULT FALSE,
        completed_by_user_id uuid,
        completed_at timestamp with time zone,
        notes text,
        sort_order integer NOT NULL DEFAULT 0,
        CONSTRAINT pk_visit_checklist_items PRIMARY KEY (id),
        CONSTRAINT fk_visit_checklist_items_users_completed_by_user_id FOREIGN KEY (completed_by_user_id) REFERENCES users (id),
        CONSTRAINT fk_visit_checklist_items_visits_visit_id FOREIGN KEY (visit_id) REFERENCES visits (id) ON DELETE CASCADE,
        CONSTRAINT fk_visit_checklist_items_work_order_checklist_templates_templa FOREIGN KEY (template_item_id) REFERENCES work_order_checklist_templates (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_customer_signoffs_signer_contact_id ON customer_signoffs (signer_contact_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE UNIQUE INDEX ix_customer_signoffs_visit_id ON customer_signoffs (visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_assignments_technician ON visit_assignments (technician_id, assigned_at) WHERE unassigned_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE UNIQUE INDEX ix_visit_assignments_active_technician_unique ON visit_assignments (visit_id, technician_id) WHERE unassigned_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_assignments_assigned_by_user_id ON visit_assignments (assigned_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE UNIQUE INDEX ix_visit_assignments_visit_id_technician_id_unassigned_at ON visit_assignments (visit_id, technician_id, unassigned_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_checklist_items_completed_by_user_id ON visit_checklist_items (completed_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_checklist_items_template_item_id ON visit_checklist_items (template_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_checklist_items_visit_id ON visit_checklist_items (visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_evidence_uploaded_by_user_id ON visit_evidence (uploaded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_evidence_visit_id ON visit_evidence (visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_incidents_created_by_user_id ON visit_incidents (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_incidents_visit_id ON visit_incidents (visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_materials_catalog_item_id ON visit_materials (catalog_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_materials_visit_id ON visit_materials (visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_status_history_changed_by_user_id ON visit_status_history (changed_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_status_history_visit_id ON visit_status_history (visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_time_entries_technician_id ON visit_time_entries (technician_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visit_time_entries_visit_id ON visit_time_entries (visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visits_organization_id_work_order_id ON visits (organization_id, work_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visits_reviewed_by_user_id ON visits (reviewed_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_visits_schedule ON visits (organization_id, scheduled_start, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE UNIQUE INDEX ix_visits_work_order_id_visit_number ON visits (work_order_id, visit_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_work_order_checklist_templates_work_order_id ON work_order_checklist_templates (work_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_work_order_required_skills_skill_id ON work_order_required_skills (skill_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_work_orders_branch_id ON work_orders (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_work_orders_created_by_user_id ON work_orders (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_work_orders_organization_id_customer_id ON work_orders (organization_id, customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_work_orders_organization_id_property_id ON work_orders (organization_id, property_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE UNIQUE INDEX ix_work_orders_organization_id_work_order_number ON work_orders (organization_id, work_order_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE UNIQUE INDEX ix_work_orders_quote_version_id ON work_orders (quote_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    CREATE INDEX ix_work_orders_status ON work_orders (organization_id, branch_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922164235_AddWorkOrdersAndVisits') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922164235_AddWorkOrdersAndVisits', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE TYPE invoice_status AS ENUM ('draft', 'sent', 'partially_paid', 'paid', 'overdue', 'void');
    CREATE TYPE payment_method AS ENUM ('cash', 'bank_transfer', 'card_external', 'check', 'other');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE TABLE invoices (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        invoice_number bigint NOT NULL,
        work_order_id uuid NOT NULL,
        customer_id uuid NOT NULL,
        status invoice_status NOT NULL,
        issue_date date,
        due_date date,
        currency character(3) NOT NULL,
        subtotal numeric(14,2) NOT NULL,
        tax_total numeric(14,2) NOT NULL,
        total numeric(14,2) NOT NULL,
        amount_paid numeric(14,2) NOT NULL DEFAULT 0.0,
        balance_due numeric(14,2) NOT NULL,
        notes text,
        sent_at timestamp with time zone,
        voided_at timestamp with time zone,
        void_reason text,
        created_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_invoices PRIMARY KEY (id),
        CONSTRAINT ak_invoices_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT ck_invoices_amount_paid CHECK (amount_paid >= 0),
        CONSTRAINT ck_invoices_amount_paid_le_total CHECK (amount_paid <= total),
        CONSTRAINT ck_invoices_balance_due CHECK (balance_due >= 0),
        CONSTRAINT ck_invoices_date_range CHECK (due_date IS NULL OR issue_date IS NULL OR due_date >= issue_date),
        CONSTRAINT ck_invoices_subtotal CHECK (subtotal >= 0),
        CONSTRAINT ck_invoices_tax_total CHECK (tax_total >= 0),
        CONSTRAINT ck_invoices_total CHECK (total >= 0),
        CONSTRAINT fk_invoices_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id),
        CONSTRAINT fk_invoices_customers_organization_id_customer_id FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id),
        CONSTRAINT fk_invoices_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_invoices_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id),
        CONSTRAINT fk_invoices_work_orders_work_order_id FOREIGN KEY (work_order_id) REFERENCES work_orders (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE TABLE payments (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        customer_id uuid NOT NULL,
        payment_number bigint NOT NULL,
        method payment_method NOT NULL,
        amount numeric(14,2) NOT NULL,
        currency character(3) NOT NULL,
        paid_at timestamp with time zone NOT NULL,
        external_reference character varying(160),
        notes text,
        recorded_by_user_id uuid,
        receipt_storage_key text,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_payments PRIMARY KEY (id),
        CONSTRAINT ak_payments_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT ck_payments_amount CHECK (amount > 0),
        CONSTRAINT fk_payments_customers_organization_id_customer_id FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id),
        CONSTRAINT fk_payments_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_payments_users_recorded_by_user_id FOREIGN KEY (recorded_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE TABLE invoice_lines (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        invoice_id uuid NOT NULL,
        source_quote_line_id uuid,
        source_visit_material_id uuid,
        description text NOT NULL,
        quantity numeric(12,3) NOT NULL,
        unit character varying(40) NOT NULL,
        unit_price numeric(14,2) NOT NULL,
        tax_rate numeric(7,4) NOT NULL DEFAULT 0.0,
        line_subtotal numeric(14,2) NOT NULL,
        line_tax numeric(14,2) NOT NULL,
        line_total numeric(14,2) NOT NULL,
        sort_order integer NOT NULL DEFAULT 0,
        CONSTRAINT pk_invoice_lines PRIMARY KEY (id),
        CONSTRAINT ck_invoice_lines_quantity CHECK (quantity > 0),
        CONSTRAINT ck_invoice_lines_unit_price CHECK (unit_price >= 0),
        CONSTRAINT fk_invoice_lines_invoices_invoice_id FOREIGN KEY (invoice_id) REFERENCES invoices (id) ON DELETE CASCADE,
        CONSTRAINT fk_invoice_lines_quote_lines_source_quote_line_id FOREIGN KEY (source_quote_line_id) REFERENCES quote_lines (id),
        CONSTRAINT fk_invoice_lines_visit_materials_source_visit_material_id FOREIGN KEY (source_visit_material_id) REFERENCES visit_materials (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE TABLE payment_allocations (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        payment_id uuid NOT NULL,
        invoice_id uuid NOT NULL,
        amount numeric(14,2) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_payment_allocations PRIMARY KEY (id),
        CONSTRAINT ck_payment_allocations_amount CHECK (amount > 0),
        CONSTRAINT fk_payment_allocations_invoices_invoice_id FOREIGN KEY (invoice_id) REFERENCES invoices (id),
        CONSTRAINT fk_payment_allocations_payments_payment_id FOREIGN KEY (payment_id) REFERENCES payments (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE INDEX ix_invoice_lines_invoice_id ON invoice_lines (invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE INDEX ix_invoice_lines_source_quote_line_id ON invoice_lines (source_quote_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE INDEX ix_invoice_lines_source_visit_material_id ON invoice_lines (source_visit_material_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE INDEX ix_invoices_branch_id ON invoices (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE INDEX ix_invoices_created_by_user_id ON invoices (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE INDEX ix_invoices_organization_id_customer_id ON invoices (organization_id, customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE UNIQUE INDEX ix_invoices_organization_id_invoice_number ON invoices (organization_id, invoice_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE INDEX ix_invoices_status_due ON invoices (organization_id, status, due_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE INDEX ix_invoices_work_order_id ON invoices (work_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE INDEX ix_payment_allocations_invoice_id ON payment_allocations (invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE UNIQUE INDEX ix_payment_allocations_payment_id_invoice_id ON payment_allocations (payment_id, invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE INDEX ix_payments_customer_date ON payments (organization_id, customer_id, paid_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE UNIQUE INDEX ix_payments_organization_id_payment_number ON payments (organization_id, payment_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    CREATE INDEX ix_payments_recorded_by_user_id ON payments (recorded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922165620_AddInvoicesAndPayments') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922165620_AddInvoicesAndPayments', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170641_AddNotificationsAndAudit') THEN
    CREATE TYPE notification_status AS ENUM ('pending', 'sent', 'failed', 'read');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170641_AddNotificationsAndAudit') THEN
    CREATE TABLE audit_logs (
        id bigint GENERATED BY DEFAULT AS IDENTITY,
        organization_id uuid NOT NULL,
        actor_user_id uuid,
        action character varying(100) NOT NULL,
        entity_type character varying(100) NOT NULL,
        entity_id uuid,
        branch_id uuid,
        before_data jsonb,
        after_data jsonb,
        metadata jsonb NOT NULL DEFAULT ('{}'::jsonb),
        ip_address inet,
        occurred_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_audit_logs PRIMARY KEY (id),
        CONSTRAINT fk_audit_logs_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id),
        CONSTRAINT fk_audit_logs_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_audit_logs_users_actor_user_id FOREIGN KEY (actor_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170641_AddNotificationsAndAudit') THEN
    CREATE TABLE notifications (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        recipient_user_id uuid,
        recipient_contact_id uuid,
        channel character varying(20) NOT NULL,
        template_code character varying(80) NOT NULL,
        subject character varying(240),
        payload jsonb NOT NULL DEFAULT ('{}'::jsonb),
        status notification_status NOT NULL,
        scheduled_at timestamp with time zone NOT NULL DEFAULT (now()),
        sent_at timestamp with time zone,
        failure_reason text,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_notifications PRIMARY KEY (id),
        CONSTRAINT ck_notifications_channel CHECK (channel IN ('email','sms','in_app')),
        CONSTRAINT fk_notifications_customer_contacts_recipient_contact_id FOREIGN KEY (recipient_contact_id) REFERENCES customer_contacts (id),
        CONSTRAINT fk_notifications_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_notifications_users_recipient_user_id FOREIGN KEY (recipient_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170641_AddNotificationsAndAudit') THEN
    CREATE INDEX ix_audit_actor ON audit_logs (organization_id, actor_user_id, occurred_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170641_AddNotificationsAndAudit') THEN
    CREATE INDEX ix_audit_entity ON audit_logs (organization_id, entity_type, entity_id, occurred_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170641_AddNotificationsAndAudit') THEN
    CREATE INDEX ix_audit_logs_actor_user_id ON audit_logs (actor_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170641_AddNotificationsAndAudit') THEN
    CREATE INDEX ix_audit_logs_branch_id ON audit_logs (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170641_AddNotificationsAndAudit') THEN
    CREATE INDEX ix_notifications_organization_id ON notifications (organization_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170641_AddNotificationsAndAudit') THEN
    CREATE INDEX ix_notifications_recipient_contact_id ON notifications (recipient_contact_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170641_AddNotificationsAndAudit') THEN
    CREATE INDEX ix_notifications_recipient_user_id ON notifications (recipient_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922170641_AddNotificationsAndAudit') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922170641_AddNotificationsAndAudit', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    ALTER TABLE organizations ADD address_line1 character varying(180);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    ALTER TABLE organizations ADD city character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    ALTER TABLE organizations ADD country_code character(2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    ALTER TABLE organizations ADD postal_code character varying(30);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    ALTER TABLE organizations ADD prices_include_tax boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    ALTER TABLE organizations ADD state_region character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    ALTER TABLE organizations ADD website character varying(255);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    ALTER TABLE branches ADD is_main boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    ALTER TABLE branches ADD service_postal_codes varchar(30)[] NOT NULL DEFAULT ('{}');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    ALTER TABLE branches ADD uses_company_billing boolean NOT NULL DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    DO $$
    BEGIN
        IF EXISTS (
            SELECT 1 FROM organizations o
            WHERE NOT EXISTS (
                SELECT 1 FROM branches b
                WHERE b.organization_id = o.id AND b.is_active)) THEN
            RAISE EXCEPTION 'Cannot backfill the main branch: an organization has no active branch.';
        END IF;

        UPDATE branches
        SET is_main = true
        WHERE id IN (
            SELECT DISTINCT ON (organization_id) id
            FROM branches
            WHERE is_active
            ORDER BY organization_id, created_at, id);
    END
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    CREATE TABLE organization_logos (
        organization_id uuid NOT NULL,
        content_type character varying(40) NOT NULL,
        content bytea NOT NULL,
        size_bytes integer NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_organization_logos PRIMARY KEY (organization_id),
        CONSTRAINT ck_organization_logos_content_type CHECK (content_type IN ('image/png','image/jpeg','image/svg+xml')),
        CONSTRAINT ck_organization_logos_size_bytes CHECK (size_bytes BETWEEN 1 AND 2097152),
        CONSTRAINT fk_organization_logos_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    CREATE UNIQUE INDEX ux_branches_org_main ON branches (organization_id) WHERE is_main;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    ALTER TABLE branches ADD CONSTRAINT ck_branches_main_active CHECK (NOT is_main OR is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928220538_AddCompanySetupCompletion') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260928220538_AddCompanySetupCompletion', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929145541_AddUserInvitationAccess') THEN
    DROP INDEX ix_user_invitations_organization_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929145541_AddUserInvitationAccess') THEN
    ALTER TABLE user_invitations ADD first_name character varying(100) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929145541_AddUserInvitationAccess') THEN
    ALTER TABLE user_invitations ADD is_all_branches boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929145541_AddUserInvitationAccess') THEN
    ALTER TABLE user_invitations ADD last_name character varying(100) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929145541_AddUserInvitationAccess') THEN
    ALTER TABLE user_invitations ADD link_team_profile boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929145541_AddUserInvitationAccess') THEN
    ALTER TABLE user_invitations ALTER COLUMN first_name DROP DEFAULT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929145541_AddUserInvitationAccess') THEN
    ALTER TABLE user_invitations ALTER COLUMN last_name DROP DEFAULT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929145541_AddUserInvitationAccess') THEN
    CREATE UNIQUE INDEX ux_user_invitations_open_email ON user_invitations (organization_id, email) WHERE accepted_at IS NULL AND revoked_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929145541_AddUserInvitationAccess') THEN
    ALTER TABLE user_invitations ADD CONSTRAINT ck_user_invitations_expires_after_created CHECK (expires_at > created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929145541_AddUserInvitationAccess') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260929145541_AddUserInvitationAccess', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260930201621_AddPasswordRecovery') THEN
    ALTER TABLE users ADD password_changed_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260930201621_AddPasswordRecovery') THEN
    CREATE TABLE password_reset_tokens (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        user_id uuid NOT NULL,
        token_hash text NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        used_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_password_reset_tokens PRIMARY KEY (id),
        CONSTRAINT ck_password_reset_tokens_expires_after_created CHECK (expires_at > created_at),
        CONSTRAINT fk_password_reset_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260930201621_AddPasswordRecovery') THEN
    CREATE UNIQUE INDEX ix_password_reset_tokens_token_hash ON password_reset_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260930201621_AddPasswordRecovery') THEN
    CREATE UNIQUE INDEX ux_password_reset_tokens_open_user ON password_reset_tokens (user_id) WHERE used_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260930201621_AddPasswordRecovery') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260930201621_AddPasswordRecovery', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260930220004_AddProductsServicesCatalog') THEN
    ALTER TABLE catalog_items ADD is_taxable boolean NOT NULL DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260930220004_AddProductsServicesCatalog') THEN
    ALTER TABLE catalog_items ADD normalized_name character varying(160) GENERATED ALWAYS AS (lower(name)) STORED NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260930220004_AddProductsServicesCatalog') THEN
    CREATE TABLE catalog_item_images (
        catalog_item_id uuid NOT NULL,
        organization_id uuid NOT NULL,
        content_type character varying(40) NOT NULL,
        content bytea NOT NULL,
        size_bytes integer NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_catalog_item_images PRIMARY KEY (catalog_item_id),
        CONSTRAINT ck_catalog_item_images_content_type CHECK (content_type IN ('image/png','image/jpeg')),
        CONSTRAINT ck_catalog_item_images_size_bytes CHECK (size_bytes BETWEEN 1 AND 5242880),
        CONSTRAINT fk_catalog_item_images_catalog_items_organization_id_catalog_i FOREIGN KEY (organization_id, catalog_item_id) REFERENCES catalog_items (organization_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_catalog_item_images_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260930220004_AddProductsServicesCatalog') THEN
    CREATE UNIQUE INDEX ux_catalog_items_org_type_name ON catalog_items (organization_id, type, normalized_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260930220004_AddProductsServicesCatalog') THEN
    CREATE INDEX ix_catalog_item_images_organization_id_catalog_item_id ON catalog_item_images (organization_id, catalog_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260930220004_AddProductsServicesCatalog') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260930220004_AddProductsServicesCatalog', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    ALTER TABLE customers ADD branch_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    UPDATE customers c
    SET branch_id = b.id
    FROM branches b
    WHERE b.organization_id = c.organization_id AND b.is_main;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    ALTER TABLE customers ALTER COLUMN branch_id SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    ALTER TABLE customer_contacts ADD prefers_email boolean NOT NULL DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    ALTER TABLE customer_contacts ADD prefers_sms boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    CREATE TABLE customer_tags (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        name character varying(40) NOT NULL,
        normalized_name character varying(40) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_customer_tags PRIMARY KEY (id),
        CONSTRAINT ak_customer_tags_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT fk_customer_tags_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    CREATE TABLE customer_tag_assignments (
        customer_id uuid NOT NULL,
        tag_id uuid NOT NULL,
        organization_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_customer_tag_assignments PRIMARY KEY (customer_id, tag_id),
        CONSTRAINT fk_customer_tag_assignments_customer_tags_organization_id_tag_ FOREIGN KEY (organization_id, tag_id) REFERENCES customer_tags (organization_id, id),
        CONSTRAINT fk_customer_tag_assignments_customers_organization_id_customer FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id),
        CONSTRAINT fk_customer_tag_assignments_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    CREATE INDEX ix_customers_branch_id ON customers (branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    CREATE INDEX ix_customers_org_branch ON customers (organization_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    CREATE INDEX ix_contacts_org_phone ON customer_contacts (organization_id, phone);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    CREATE UNIQUE INDEX ux_customer_contacts_primary ON customer_contacts (customer_id) WHERE is_primary;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    ALTER TABLE customer_contacts ADD CONSTRAINT ck_customer_contacts_preferred_channel CHECK (prefers_email OR prefers_sms);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    CREATE INDEX ix_customer_tag_assignments_org_tag ON customer_tag_assignments (organization_id, tag_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    CREATE INDEX ix_customer_tag_assignments_organization_id_customer_id ON customer_tag_assignments (organization_id, customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    CREATE UNIQUE INDEX ux_customer_tags_org_normalized_name ON customer_tags (organization_id, normalized_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    ALTER TABLE customers ADD CONSTRAINT fk_customers_branches_branch_id FOREIGN KEY (branch_id) REFERENCES branches (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    ALTER TABLE customers ADD CONSTRAINT fk_customers_branches_organization_id_branch_id FOREIGN KEY (organization_id, branch_id) REFERENCES branches (organization_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001004947_AddCustomerManagement') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261001004947_AddCustomerManagement', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001152110_AddCustomerPropertyDetail') THEN
    ALTER TABLE properties ADD is_primary boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001152110_AddCustomerPropertyDetail') THEN
    UPDATE properties p
    SET is_primary = true
    FROM (
        SELECT DISTINCT ON (customer_id) id
        FROM properties
        WHERE is_active
        ORDER BY customer_id, created_at, id
    ) f
    WHERE p.id = f.id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001152110_AddCustomerPropertyDetail') THEN
    ALTER TABLE properties ADD CONSTRAINT ck_properties_primary_active CHECK (NOT is_primary OR is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001152110_AddCustomerPropertyDetail') THEN
    CREATE UNIQUE INDEX ux_properties_customer_primary ON properties (customer_id) WHERE is_primary;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001152110_AddCustomerPropertyDetail') THEN
    CREATE INDEX ix_properties_organization_id_branch_id ON properties (organization_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001152110_AddCustomerPropertyDetail') THEN
    ALTER TABLE properties ADD CONSTRAINT fk_properties_branches_organization_id_branch_id FOREIGN KEY (organization_id, branch_id) REFERENCES branches (organization_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001152110_AddCustomerPropertyDetail') THEN
    CREATE INDEX ix_customer_notes_customer ON customer_notes (organization_id, customer_id, created_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001152110_AddCustomerPropertyDetail') THEN
    DROP INDEX ix_customer_notes_organization_id_customer_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001152110_AddCustomerPropertyDetail') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261001152110_AddCustomerPropertyDetail', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001162526_AddTechnicianAccountLinkAndPrimarySkill') THEN
    ALTER TABLE technician_skills ADD is_primary boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001162526_AddTechnicianAccountLinkAndPrimarySkill') THEN
    CREATE UNIQUE INDEX ux_technician_skills_primary ON technician_skills (technician_id) WHERE is_primary;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001162526_AddTechnicianAccountLinkAndPrimarySkill') THEN
    CREATE UNIQUE INDEX ux_technician_profiles_org_user ON technician_profiles (organization_user_id) WHERE organization_user_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001162526_AddTechnicianAccountLinkAndPrimarySkill') THEN
    CREATE UNIQUE INDEX ux_technician_profiles_org_email
    ON technician_profiles (organization_id, lower(email))
    WHERE email IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001162526_AddTechnicianAccountLinkAndPrimarySkill') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261001162526_AddTechnicianAccountLinkAndPrimarySkill', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001204229_AddTechnicianExceptionStatusAndSkillNameIndex') THEN
    DROP INDEX ix_skills_organization_id_name;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001204229_AddTechnicianExceptionStatusAndSkillNameIndex') THEN
    ALTER TABLE technician_exceptions ADD created_at timestamp with time zone NOT NULL DEFAULT (now());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001204229_AddTechnicianExceptionStatusAndSkillNameIndex') THEN
    ALTER TABLE technician_exceptions ADD status character varying(20) NOT NULL DEFAULT 'active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001204229_AddTechnicianExceptionStatusAndSkillNameIndex') THEN
    ALTER TABLE technician_exceptions ADD updated_at timestamp with time zone NOT NULL DEFAULT (now());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001204229_AddTechnicianExceptionStatusAndSkillNameIndex') THEN
    ALTER TABLE technician_exceptions ADD CONSTRAINT ck_technician_exceptions_status CHECK (status IN ('active','cancelled'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001204229_AddTechnicianExceptionStatusAndSkillNameIndex') THEN
    CREATE UNIQUE INDEX ux_skills_org_normalized_name ON skills (organization_id, lower(name));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001204229_AddTechnicianExceptionStatusAndSkillNameIndex') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261001204229_AddTechnicianExceptionStatusAndSkillNameIndex', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE request_attachments DROP CONSTRAINT ck_request_attachments_size_bytes;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE service_requests ADD availability_preferences jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE service_requests ADD catalog_item_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE service_requests ADD consent_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE service_requests ADD has_active_damage boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE service_requests ADD urgency character varying(20) NOT NULL DEFAULT 'standard';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE request_attachments ALTER COLUMN storage_key DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE request_attachments ADD content bytea;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE organizations ADD next_request_number bigint NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE organizations ADD request_prefix character varying(20) NOT NULL DEFAULT 'REQ';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE organizations ADD public_slug character varying(60);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    DO $$
    DECLARE
        org record;
        base_slug text;
        candidate text;
        suffix text;
        n integer;
    BEGIN
        FOR org IN SELECT id, name FROM organizations ORDER BY created_at, id LOOP
            base_slug := lower(regexp_replace(normalize(org.name, NFD), '[̀-ͯ]', '', 'g'));
            base_slug := btrim(regexp_replace(base_slug, '[^a-z0-9]+', '-', 'g'), '-');

            IF base_slug = '' THEN
                base_slug := 'organization';
            END IF;

            n := 1;

            LOOP
                IF n = 1 THEN
                    candidate := regexp_replace(left(base_slug, 60), '-+$', '');
                ELSE
                    suffix := '-' || n::text;
                    candidate := regexp_replace(left(base_slug, 60 - length(suffix)), '-+$', '') || suffix;
                END IF;

                EXIT WHEN NOT EXISTS (SELECT 1 FROM organizations WHERE public_slug = candidate);
                n := n + 1;
            END LOOP;

            UPDATE organizations SET public_slug = candidate WHERE id = org.id;
        END LOOP;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    UPDATE organizations SET public_slug = '' WHERE public_slug IS NULL;
    ALTER TABLE organizations ALTER COLUMN public_slug SET NOT NULL;
    ALTER TABLE organizations ALTER COLUMN public_slug SET DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    UPDATE organizations AS o
    SET next_request_number = r.max_number + 1
    FROM (
        SELECT organization_id, max(request_number) AS max_number
        FROM service_requests
        GROUP BY organization_id
    ) AS r
    WHERE r.organization_id = o.id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    CREATE INDEX ix_service_requests_organization_id_catalog_item_id ON service_requests (organization_id, catalog_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE service_requests ADD CONSTRAINT ck_service_requests_urgency CHECK (urgency IN ('standard', 'urgent', 'emergency'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE request_attachments ADD CONSTRAINT ck_request_attachments_content_or_storage CHECK (content IS NOT NULL OR storage_key IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE request_attachments ADD CONSTRAINT ck_request_attachments_mime_type CHECK (mime_type IN ('image/jpeg', 'image/png', 'application/pdf'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE request_attachments ADD CONSTRAINT ck_request_attachments_size_bytes CHECK (size_bytes > 0 AND size_bytes <= 10485760);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    CREATE UNIQUE INDEX ux_organizations_public_slug ON organizations (public_slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE organizations ADD CONSTRAINT ck_organizations_public_slug CHECK (public_slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(public_slug) BETWEEN 1 AND 60);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    ALTER TABLE service_requests ADD CONSTRAINT fk_service_requests_catalog_items_organization_id_catalog_item FOREIGN KEY (organization_id, catalog_item_id) REFERENCES catalog_items (organization_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261001232236_AddPublicServiceRequest') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261001232236_AddPublicServiceRequest', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005204548_AddAssessmentPurpose') THEN
    ALTER TABLE assessments ADD purpose character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005204548_AddAssessmentPurpose') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261005204548_AddAssessmentPurpose', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    DROP INDEX ix_quotes_organization_id_request_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE quote_versions ADD discount_total numeric(14,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE quote_versions ADD terms text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE quote_lines ADD is_optional boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE quote_lines ADD name character varying(160) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE quote_lines ADD organization_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    UPDATE quote_lines AS line
    SET organization_id = version.organization_id,
        name = LEFT(line.description, 160)
    FROM quote_versions AS version
    WHERE version.id = line.quote_version_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE quote_lines ALTER COLUMN organization_id SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE assessment_attachments ALTER COLUMN storage_key DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE assessment_attachments ADD content bytea;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE assessment_attachments ADD organization_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    UPDATE assessment_attachments AS attachment
    SET organization_id = assessment.organization_id
    FROM assessments AS assessment
    WHERE assessment.id = attachment.assessment_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE assessment_attachments ALTER COLUMN organization_id SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    CREATE TABLE quote_access_tokens (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        quote_version_id uuid NOT NULL,
        token_hash text NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        created_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_quote_access_tokens PRIMARY KEY (id),
        CONSTRAINT ck_quote_access_tokens_expires_after_created CHECK (expires_at > created_at),
        CONSTRAINT fk_quote_access_tokens_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_quote_access_tokens_quote_versions_organization_id_quote_ve FOREIGN KEY (organization_id, quote_version_id) REFERENCES quote_versions (organization_id, id),
        CONSTRAINT fk_quote_access_tokens_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    CREATE UNIQUE INDEX ux_quotes_request_open ON quotes (organization_id, request_id) WHERE status <> 'cancelled';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    CREATE UNIQUE INDEX ux_quote_versions_one_mutable ON quote_versions (quote_id) WHERE NOT is_immutable;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE quote_versions ADD CONSTRAINT ck_quote_versions_discount_total CHECK (discount_total >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    CREATE INDEX ix_quote_lines_organization_id_quote_version_id ON quote_lines (organization_id, quote_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    CREATE INDEX ix_assessment_attachments_organization_id_assessment_id ON assessment_attachments (organization_id, assessment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE assessment_attachments ADD CONSTRAINT ck_assessment_attachments_content_or_storage CHECK (content IS NOT NULL OR storage_key IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE assessment_attachments ADD CONSTRAINT ck_assessment_attachments_mime_type CHECK (mime_type IN ('image/jpeg', 'image/png'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE assessment_attachments ADD CONSTRAINT ck_assessment_attachments_size_bytes CHECK (size_bytes > 0 AND size_bytes <= 10485760);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    CREATE INDEX ix_quote_access_tokens_created_by_user_id ON quote_access_tokens (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    CREATE INDEX ix_quote_access_tokens_organization_id_quote_version_id ON quote_access_tokens (organization_id, quote_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    CREATE UNIQUE INDEX ix_quote_access_tokens_token_hash ON quote_access_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    CREATE INDEX ix_quote_access_tokens_version ON quote_access_tokens (quote_version_id) WHERE revoked_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE assessment_attachments ADD CONSTRAINT fk_assessment_attachments_assessments_organization_id_assessme FOREIGN KEY (organization_id, assessment_id) REFERENCES assessments (organization_id, id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE assessment_attachments ADD CONSTRAINT fk_assessment_attachments_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE quote_lines ADD CONSTRAINT fk_quote_lines_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    ALTER TABLE quote_lines ADD CONSTRAINT fk_quote_lines_quote_versions_organization_id_quote_version_id FOREIGN KEY (organization_id, quote_version_id) REFERENCES quote_versions (organization_id, id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005223228_AddQuoteBuilder') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261005223228_AddQuoteBuilder', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses DROP CONSTRAINT fk_quote_responses_customer_contacts_responder_contact_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses DROP CONSTRAINT fk_quote_responses_quote_versions_quote_version_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quotes DROP CONSTRAINT fk_quotes_quote_versions_approved_version_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    DROP INDEX ix_quotes_approved_version_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    DROP INDEX ix_quote_versions_organization_id_quote_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    DROP INDEX ix_quote_responses_quote_version_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    DROP INDEX ix_quote_responses_responder_contact_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    DROP INDEX ix_quote_lines_organization_id_quote_version_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD discount_total numeric(14,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD organization_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD subtotal numeric(14,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD tax_total numeric(14,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD total numeric(14,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_versions ADD CONSTRAINT ak_quote_versions_organization_id_quote_id_id UNIQUE (organization_id, quote_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD CONSTRAINT ak_quote_responses_organization_id_quote_version_id_id UNIQUE (organization_id, quote_version_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_lines ADD CONSTRAINT ak_quote_lines_organization_id_quote_version_id_id UNIQUE (organization_id, quote_version_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    CREATE TABLE quote_response_optional_lines (
        quote_response_id uuid NOT NULL,
        quote_line_id uuid NOT NULL,
        organization_id uuid NOT NULL,
        quote_version_id uuid NOT NULL,
        CONSTRAINT pk_quote_response_optional_lines PRIMARY KEY (quote_response_id, quote_line_id),
        CONSTRAINT fk_quote_response_optional_lines_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_quote_response_optional_lines_quote_lines_organization_id_q FOREIGN KEY (organization_id, quote_version_id, quote_line_id) REFERENCES quote_lines (organization_id, quote_version_id, id),
        CONSTRAINT fk_quote_response_optional_lines_quote_responses_organization_ FOREIGN KEY (organization_id, quote_version_id, quote_response_id) REFERENCES quote_responses (organization_id, quote_version_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    CREATE INDEX ix_quotes_organization_id_id_approved_version_id ON quotes (organization_id, id, approved_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    CREATE INDEX ix_quote_responses_organization_id_responder_contact_id ON quote_responses (organization_id, responder_contact_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    CREATE UNIQUE INDEX ux_quote_responses_clarification ON quote_responses (quote_version_id) WHERE response = 'clarification_requested';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    CREATE UNIQUE INDEX ux_quote_responses_final ON quote_responses (quote_version_id) WHERE response IN ('approved','rejected');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD CONSTRAINT ck_quote_responses_discount_total CHECK (discount_total >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD CONSTRAINT ck_quote_responses_subtotal CHECK (subtotal >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD CONSTRAINT ck_quote_responses_tax_total CHECK (tax_total >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD CONSTRAINT ck_quote_responses_total CHECK (total >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD CONSTRAINT ck_quote_responses_totals CHECK ((response = 'approved') = (subtotal IS NOT NULL AND discount_total IS NOT NULL AND tax_total IS NOT NULL AND total IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD CONSTRAINT ck_quote_responses_totals_null CHECK (response = 'approved' OR (subtotal IS NULL AND discount_total IS NULL AND tax_total IS NULL AND total IS NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    CREATE INDEX ix_quote_response_optional_lines_organization_id_quote_version ON quote_response_optional_lines (organization_id, quote_version_id, quote_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    CREATE INDEX ix_quote_response_optional_lines_organization_id_quote_version1 ON quote_response_optional_lines (organization_id, quote_version_id, quote_response_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD CONSTRAINT fk_quote_responses_customer_contacts_organization_id_responder FOREIGN KEY (organization_id, responder_contact_id) REFERENCES customer_contacts (organization_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD CONSTRAINT fk_quote_responses_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quote_responses ADD CONSTRAINT fk_quote_responses_quote_versions_organization_id_quote_versio FOREIGN KEY (organization_id, quote_version_id) REFERENCES quote_versions (organization_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    ALTER TABLE quotes ADD CONSTRAINT fk_quotes_quote_versions_organization_id_id_approved_version_id FOREIGN KEY (organization_id, id, approved_version_id) REFERENCES quote_versions (organization_id, quote_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006150432_AddCustomerQuoteApproval') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261006150432_AddCustomerQuoteApproval', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders DROP CONSTRAINT fk_work_orders_branches_branch_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders DROP CONSTRAINT fk_work_orders_quote_versions_quote_version_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    DROP INDEX ix_work_orders_branch_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD estimated_duration_minutes integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD job_type character varying(20) NOT NULL DEFAULT 'one_time';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD notify_customer_when_scheduled boolean NOT NULL DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD recurrence_count smallint;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD recurrence_frequency character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD send_arrival_reminder boolean NOT NULL DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD send_technician_details boolean NOT NULL DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD service_category_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD title character varying(160) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE TABLE checklist_templates (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        service_category_id uuid,
        name character varying(120) NOT NULL,
        is_active boolean NOT NULL DEFAULT TRUE,
        created_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_checklist_templates PRIMARY KEY (id),
        CONSTRAINT ak_checklist_templates_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT fk_checklist_templates_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_checklist_templates_service_categories_organization_id_serv FOREIGN KEY (organization_id, service_category_id) REFERENCES service_categories (organization_id, id),
        CONSTRAINT fk_checklist_templates_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE UNIQUE INDEX ux_checklist_templates_org_name ON checklist_templates (organization_id, lower(name));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE TABLE work_order_planned_materials (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        work_order_id uuid NOT NULL,
        quote_line_id uuid,
        catalog_item_id uuid,
        description character varying(240) NOT NULL,
        quantity numeric(12,3) NOT NULL,
        unit character varying(40) NOT NULL,
        source character varying(20) NOT NULL,
        sort_order integer NOT NULL DEFAULT 0,
        CONSTRAINT pk_work_order_planned_materials PRIMARY KEY (id),
        CONSTRAINT ck_work_order_planned_materials_quantity CHECK (quantity > 0),
        CONSTRAINT ck_work_order_planned_materials_source CHECK (source IN ('truck_stock','warehouse','to_purchase')),
        CONSTRAINT fk_work_order_planned_materials_catalog_items_organization_id_ FOREIGN KEY (organization_id, catalog_item_id) REFERENCES catalog_items (organization_id, id),
        CONSTRAINT fk_work_order_planned_materials_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_work_order_planned_materials_quote_lines_quote_line_id FOREIGN KEY (quote_line_id) REFERENCES quote_lines (id),
        CONSTRAINT fk_work_order_planned_materials_work_orders_organization_id_wo FOREIGN KEY (organization_id, work_order_id) REFERENCES work_orders (organization_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE TABLE checklist_template_items (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        template_id uuid NOT NULL,
        label character varying(240) NOT NULL,
        sort_order integer NOT NULL DEFAULT 0,
        CONSTRAINT pk_checklist_template_items PRIMARY KEY (id),
        CONSTRAINT fk_checklist_template_items_checklist_templates_organization_i FOREIGN KEY (organization_id, template_id) REFERENCES checklist_templates (organization_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_checklist_template_items_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE INDEX ix_work_orders_org_created ON work_orders (organization_id, created_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE INDEX ix_work_orders_organization_id_quote_version_id ON work_orders (organization_id, quote_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE INDEX ix_work_orders_organization_id_service_category_id ON work_orders (organization_id, service_category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD CONSTRAINT ck_work_orders_estimated_duration_minutes CHECK (estimated_duration_minutes BETWEEN 30 AND 720 AND estimated_duration_minutes % 30 = 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD CONSTRAINT ck_work_orders_job_type CHECK (job_type IN ('one_time','recurring'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD CONSTRAINT ck_work_orders_preferred_window CHECK ((preferred_start IS NULL) = (preferred_end IS NULL) AND (preferred_start IS NULL OR preferred_start < preferred_end));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD CONSTRAINT ck_work_orders_recurrence_count CHECK (recurrence_count BETWEEN 2 AND 24);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD CONSTRAINT ck_work_orders_recurrence_frequency CHECK (recurrence_frequency IN ('weekly','biweekly','monthly','quarterly'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD CONSTRAINT ck_work_orders_recurrence_job_type CHECK ((job_type = 'recurring') = (recurrence_frequency IS NOT NULL AND recurrence_count IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD CONSTRAINT ck_work_orders_recurrence_pair CHECK ((recurrence_frequency IS NULL) = (recurrence_count IS NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE INDEX ix_checklist_template_items_organization_id_template_id ON checklist_template_items (organization_id, template_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE INDEX ix_checklist_templates_created_by_user_id ON checklist_templates (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE INDEX ix_checklist_templates_organization_id_service_category_id ON checklist_templates (organization_id, service_category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE INDEX ix_work_order_planned_materials_organization_id_catalog_item_id ON work_order_planned_materials (organization_id, catalog_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE INDEX ix_work_order_planned_materials_organization_id_work_order_id ON work_order_planned_materials (organization_id, work_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE INDEX ix_work_order_planned_materials_quote_line_id ON work_order_planned_materials (quote_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    CREATE INDEX ix_work_order_planned_materials_work_order_id ON work_order_planned_materials (work_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD CONSTRAINT fk_work_orders_branches_organization_id_branch_id FOREIGN KEY (organization_id, branch_id) REFERENCES branches (organization_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD CONSTRAINT fk_work_orders_quote_versions_organization_id_quote_version_id FOREIGN KEY (organization_id, quote_version_id) REFERENCES quote_versions (organization_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    ALTER TABLE work_orders ADD CONSTRAINT fk_work_orders_service_categories_organization_id_service_cate FOREIGN KEY (organization_id, service_category_id) REFERENCES service_categories (organization_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006165531_AddWorkOrderCreation') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261006165531_AddWorkOrderCreation', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    DROP INDEX ix_visit_assignments_visit_id_technician_id_unassigned_at;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    ALTER INDEX ix_visit_assignments_active_technician_unique RENAME TO ux_visit_assignments_active;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    ALTER TABLE visits ADD arrival_window_end timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    ALTER TABLE visits ADD arrival_window_start timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    ALTER TABLE visits ADD dispatch_note character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    ALTER TABLE visits ADD preferred_end timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    ALTER TABLE visits ADD preferred_start timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    UPDATE visits v
    SET preferred_start = w.preferred_start, preferred_end = w.preferred_end
    FROM work_orders w
    WHERE w.id = v.work_order_id
      AND w.organization_id = v.organization_id
      AND v.visit_number = 1
      AND v.preferred_start IS NULL
      AND w.preferred_start IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    UPDATE visit_assignments a
    SET is_primary = false
    WHERE a.unassigned_at IS NULL
      AND a.is_primary
      AND EXISTS (
        SELECT 1 FROM visit_assignments b
        WHERE b.visit_id = a.visit_id
          AND b.unassigned_at IS NULL
          AND b.is_primary
          AND (b.assigned_at, b.id) < (a.assigned_at, a.id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    CREATE INDEX ix_visits_org_status_preferred ON visits (organization_id, status, preferred_start);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    ALTER TABLE visits ADD CONSTRAINT ck_visits_arrival_window_pair CHECK ((arrival_window_start IS NULL) = (arrival_window_end IS NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    ALTER TABLE visits ADD CONSTRAINT ck_visits_arrival_window_range CHECK (arrival_window_start IS NULL OR (scheduled_start IS NOT NULL AND arrival_window_start <= scheduled_start AND scheduled_start <= arrival_window_end));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    ALTER TABLE visits ADD CONSTRAINT ck_visits_preferred_range CHECK ((preferred_start IS NULL) = (preferred_end IS NULL) AND (preferred_start IS NULL OR preferred_start < preferred_end));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    ALTER TABLE visits ADD CONSTRAINT ck_visits_schedule_pair CHECK ((scheduled_start IS NULL) = (scheduled_end IS NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    CREATE UNIQUE INDEX ux_visit_assignments_primary ON visit_assignments (visit_id) WHERE is_primary AND unassigned_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006222333_AddDispatchCalendar') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261006222333_AddDispatchCalendar', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    DROP INDEX ix_visit_materials_visit_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    ALTER TABLE visit_evidence DROP CONSTRAINT ck_visit_evidence_size_bytes;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    ALTER TABLE visit_materials ADD planned_material_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    ALTER TABLE visit_evidence ALTER COLUMN storage_key DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    ALTER TABLE visit_evidence ADD content bytea;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    CREATE INDEX ix_visit_materials_planned_material_id ON visit_materials (planned_material_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    CREATE UNIQUE INDEX ux_visit_materials_planned ON visit_materials (visit_id, planned_material_id) WHERE planned_material_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    ALTER TABLE visit_evidence ADD CONSTRAINT ck_visit_evidence_content_or_storage CHECK (content IS NOT NULL OR storage_key IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    ALTER TABLE visit_evidence ADD CONSTRAINT ck_visit_evidence_mime_type CHECK (mime_type IN ('image/jpeg', 'image/png'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    ALTER TABLE visit_evidence ADD CONSTRAINT ck_visit_evidence_size_bytes CHECK (size_bytes > 0 AND size_bytes <= 10485760);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    ALTER TABLE visit_materials ADD CONSTRAINT fk_visit_materials_work_order_planned_materials_planned_materi FOREIGN KEY (planned_material_id) REFERENCES work_order_planned_materials (id) ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007205058_AddMobileJobProgress') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261007205058_AddMobileJobProgress', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD acknowledgement_method character varying(30) NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD recorded_by_user_id uuid NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD review_confirmed boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD signature_content bytea;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD signature_mime_type character varying(40);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD signer_relationship character varying(40);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    CREATE INDEX ix_customer_signoffs_recorded_by_user_id ON customer_signoffs (recorded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD CONSTRAINT ck_customer_signoffs_acknowledgement_method CHECK (acknowledgement_method IN ('signed','customer_absent','customer_refused','remote_confirmation'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD CONSTRAINT ck_customer_signoffs_signature_content_pair CHECK ((signature_content IS NULL) = (signature_mime_type IS NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD CONSTRAINT ck_customer_signoffs_signature_mime_type CHECK (signature_mime_type = 'image/png');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD CONSTRAINT ck_customer_signoffs_signature_size CHECK (signature_content IS NULL OR octet_length(signature_content) <= 524288);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD CONSTRAINT ck_customer_signoffs_signed_has_signature CHECK ((acknowledgement_method = 'signed') = (signature_content IS NOT NULL OR signature_storage_key IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD CONSTRAINT ck_customer_signoffs_signer_relationship CHECK (signer_relationship IN ('customer','family_member','tenant','property_manager','employee','other'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    ALTER TABLE customer_signoffs ADD CONSTRAINT fk_customer_signoffs_users_recorded_by_user_id FOREIGN KEY (recorded_by_user_id) REFERENCES users (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007232936_AddCustomerSignoffAcknowledgement') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261007232936_AddCustomerSignoffAcknowledgement', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    ALTER TABLE work_orders ADD billing_follow_up_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    ALTER TABLE work_orders ADD billing_follow_up_by_user_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    ALTER TABLE work_orders ADD billing_review_note character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    ALTER TABLE invoices ADD discount_total numeric(14,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    ALTER TABLE invoices ADD payment_terms character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    CREATE INDEX ix_work_orders_billing_follow_up_by_user_id ON work_orders (billing_follow_up_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    ALTER TABLE work_orders ADD CONSTRAINT ck_work_orders_billing_follow_up_pair CHECK ((billing_follow_up_at IS NULL) = (billing_follow_up_by_user_id IS NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    CREATE UNIQUE INDEX ux_invoices_work_order_active ON invoices (organization_id, work_order_id) WHERE status <> 'void';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    ALTER TABLE invoices ADD CONSTRAINT ck_invoices_discount_total CHECK (discount_total >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    ALTER TABLE invoices ADD CONSTRAINT ck_invoices_payment_terms CHECK (payment_terms IN ('due_upon_receipt','net_15','net_30'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    ALTER TABLE work_orders ADD CONSTRAINT fk_work_orders_users_billing_follow_up_by_user_id FOREIGN KEY (billing_follow_up_by_user_id) REFERENCES users (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008154953_AddBillingReview') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261008154953_AddBillingReview', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008184122_AddInvoiceDelivery') THEN
    ALTER TABLE invoices ADD customer_snapshot jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008184122_AddInvoiceDelivery') THEN
    ALTER TABLE invoices ADD delivery_message character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008184122_AddInvoiceDelivery') THEN
    ALTER TABLE invoices ADD recipient_email character varying(254);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008184122_AddInvoiceDelivery') THEN
    CREATE TABLE invoice_access_tokens (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        invoice_id uuid NOT NULL,
        token_hash text NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        created_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_invoice_access_tokens PRIMARY KEY (id),
        CONSTRAINT ck_invoice_access_tokens_expires_after_created CHECK (expires_at > created_at),
        CONSTRAINT fk_invoice_access_tokens_invoices_organization_id_invoice_id FOREIGN KEY (organization_id, invoice_id) REFERENCES invoices (organization_id, id),
        CONSTRAINT fk_invoice_access_tokens_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_invoice_access_tokens_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008184122_AddInvoiceDelivery') THEN
    DO $$
    BEGIN
        IF EXISTS (
            SELECT 1 FROM invoices
            WHERE status::text NOT IN ('draft', 'void') AND customer_snapshot IS NULL) THEN
            RAISE EXCEPTION 'Cannot add the customer snapshot rule: an invoice that is neither draft nor void has no customer snapshot.';
        END IF;
    END
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008184122_AddInvoiceDelivery') THEN
    ALTER TABLE invoices ADD CONSTRAINT ck_invoices_customer_snapshot CHECK (status = 'draft' OR status = 'void' OR customer_snapshot IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008184122_AddInvoiceDelivery') THEN
    CREATE INDEX ix_invoice_access_tokens_created_by_user_id ON invoice_access_tokens (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008184122_AddInvoiceDelivery') THEN
    CREATE INDEX ix_invoice_access_tokens_invoice ON invoice_access_tokens (invoice_id) WHERE revoked_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008184122_AddInvoiceDelivery') THEN
    CREATE INDEX ix_invoice_access_tokens_organization_id_invoice_id ON invoice_access_tokens (organization_id, invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008184122_AddInvoiceDelivery') THEN
    CREATE UNIQUE INDEX ix_invoice_access_tokens_token_hash ON invoice_access_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008184122_AddInvoiceDelivery') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261008184122_AddInvoiceDelivery', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    DO $$
    BEGIN
        IF EXISTS (SELECT 1 FROM payments) THEN
            RAISE EXCEPTION 'AddInvoicePayments: payments has rows; received_by_user_id and idempotency_key cannot be backfilled.';
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    ALTER INDEX ix_payment_allocations_invoice_id RENAME TO ix_payment_allocations_invoice;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    ALTER TABLE payments ADD idempotency_key uuid NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    ALTER TABLE payments ADD received_by_user_id uuid NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    ALTER TABLE organizations ADD next_payment_number bigint NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    ALTER TABLE organizations ADD payment_prefix character varying(20) NOT NULL DEFAULT 'PAY';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    CREATE INDEX ix_payments_org_paid_at ON payments (organization_id, paid_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    CREATE UNIQUE INDEX ix_payments_organization_id_idempotency_key ON payments (organization_id, idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    CREATE INDEX ix_payments_received_by_user_id ON payments (received_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    CREATE INDEX ix_invoices_org_issue_date ON invoices (organization_id, issue_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    ALTER TABLE payments ADD CONSTRAINT fk_payments_users_received_by_user_id FOREIGN KEY (received_by_user_id) REFERENCES users (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009001747_AddInvoicePayments') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261009001747_AddInvoicePayments', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE TYPE payment_attempt_status AS ENUM ('pending', 'succeeded', 'failed', 'partially_refunded', 'refunded');
    CREATE TYPE payment_status AS ENUM ('succeeded', 'partially_refunded', 'refunded');
    ALTER TYPE payment_method ADD VALUE 'card_online';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ALTER COLUMN received_by_user_id DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ADD card_brand character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ADD card_last4 character(4);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ADD receipt_number character varying(60);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ADD receipt_sent_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ADD refunded_amount numeric(14,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ADD status payment_status NOT NULL DEFAULT 'succeeded'::payment_status;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE organizations ADD bank_account_last4 character varying(4);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE organizations ADD bank_account_number_ciphertext bytea;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE organizations ADD bank_details_updated_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE organizations ADD bank_name character varying(120);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE organizations ADD bank_routing_number character(9);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE organizations ADD stripe_account_id character varying(255);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE TABLE invoice_payment_attempts (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        invoice_id uuid NOT NULL,
        method payment_method NOT NULL,
        status payment_attempt_status NOT NULL DEFAULT 'pending'::payment_attempt_status,
        amount numeric(14,2) NOT NULL,
        currency character(3) NOT NULL,
        idempotency_key uuid NOT NULL,
        provider_payment_intent_id character varying(255),
        payment_id uuid,
        failure_category character varying(40),
        refunded_amount numeric(14,2) NOT NULL DEFAULT 0.0,
        expires_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_invoice_payment_attempts PRIMARY KEY (id),
        CONSTRAINT ck_invoice_payment_attempts_amount CHECK (amount > 0),
        CONSTRAINT ck_invoice_payment_attempts_method CHECK (method::text IN ('card_online','bank_transfer')),
        CONSTRAINT ck_invoice_payment_attempts_payment_link CHECK ((status = 'succeeded' OR status = 'partially_refunded' OR status = 'refunded') = (payment_id IS NOT NULL)),
        CONSTRAINT ck_invoice_payment_attempts_refunded_amount CHECK (refunded_amount >= 0 AND refunded_amount <= amount),
        CONSTRAINT fk_invoice_payment_attempts_invoices_organization_id_invoice_id FOREIGN KEY (organization_id, invoice_id) REFERENCES invoices (organization_id, id),
        CONSTRAINT fk_invoice_payment_attempts_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_invoice_payment_attempts_payments_organization_id_payment_id FOREIGN KEY (organization_id, payment_id) REFERENCES payments (organization_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE TABLE invoice_reviews (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        work_order_id uuid NOT NULL,
        invoice_id uuid NOT NULL,
        technician_id uuid,
        rating smallint NOT NULL,
        comment character varying(500),
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_invoice_reviews PRIMARY KEY (id),
        CONSTRAINT ck_invoice_reviews_rating CHECK (rating BETWEEN 1 AND 5),
        CONSTRAINT fk_invoice_reviews_invoices_organization_id_invoice_id FOREIGN KEY (organization_id, invoice_id) REFERENCES invoices (organization_id, id),
        CONSTRAINT fk_invoice_reviews_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_invoice_reviews_technician_profiles_organization_id_technic FOREIGN KEY (organization_id, technician_id) REFERENCES technician_profiles (organization_id, id),
        CONSTRAINT fk_invoice_reviews_work_orders_organization_id_work_order_id FOREIGN KEY (organization_id, work_order_id) REFERENCES work_orders (organization_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE TABLE payment_webhook_events (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        provider character varying(20) NOT NULL DEFAULT 'stripe',
        provider_event_id character varying(255) NOT NULL,
        event_type character varying(100) NOT NULL,
        organization_id uuid,
        attempt_id uuid,
        outcome character varying(40) NOT NULL,
        received_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_payment_webhook_events PRIMARY KEY (id),
        CONSTRAINT ck_payment_webhook_events_outcome CHECK (outcome IN ('applied','no_effect','ignored','needs_attention')),
        CONSTRAINT fk_payment_webhook_events_invoice_payment_attempts_attempt_id FOREIGN KEY (attempt_id) REFERENCES invoice_payment_attempts (id),
        CONSTRAINT fk_payment_webhook_events_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE UNIQUE INDEX ix_payments_organization_id_receipt_number ON payments (organization_id, receipt_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ADD CONSTRAINT ck_payments_card_online_last4 CHECK ((method::text = 'card_online') = (card_last4 IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ADD CONSTRAINT ck_payments_card_online_no_receiver CHECK ((method::text = 'card_online') = (received_by_user_id IS NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ADD CONSTRAINT ck_payments_refunded_amount_range CHECK (refunded_amount BETWEEN 0 AND amount);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ADD CONSTRAINT ck_payments_status_refunded_full CHECK ((status = 'refunded') = (refunded_amount = amount));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE payments ADD CONSTRAINT ck_payments_status_succeeded_no_refund CHECK ((status = 'succeeded') = (refunded_amount = 0));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    ALTER TABLE organizations ADD CONSTRAINT ck_organizations_bank_details_all_or_none CHECK ((bank_name IS NULL) = (bank_account_number_ciphertext IS NULL) AND (bank_name IS NULL) = (bank_account_last4 IS NULL) AND (bank_name IS NULL) = (bank_routing_number IS NULL) AND (bank_name IS NULL) = (bank_details_updated_at IS NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE INDEX ix_invoice_payment_attempts_invoice ON invoice_payment_attempts (invoice_id, created_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE UNIQUE INDEX ix_invoice_payment_attempts_organization_id_idempotency_key ON invoice_payment_attempts (organization_id, idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE INDEX ix_invoice_payment_attempts_organization_id_invoice_id ON invoice_payment_attempts (organization_id, invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE INDEX ix_invoice_payment_attempts_organization_id_payment_id ON invoice_payment_attempts (organization_id, payment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE UNIQUE INDEX ix_invoice_payment_attempts_provider_payment_intent_id ON invoice_payment_attempts (provider_payment_intent_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE UNIQUE INDEX ux_invoice_payment_attempts_pending ON invoice_payment_attempts (invoice_id, method) WHERE status = 'pending';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE INDEX ix_invoice_reviews_organization_id_invoice_id ON invoice_reviews (organization_id, invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE INDEX ix_invoice_reviews_organization_id_technician_id ON invoice_reviews (organization_id, technician_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE INDEX ix_invoice_reviews_organization_id_work_order_id ON invoice_reviews (organization_id, work_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE UNIQUE INDEX ix_invoice_reviews_work_order_id ON invoice_reviews (work_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE INDEX ix_payment_webhook_events_attempt_id ON payment_webhook_events (attempt_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE INDEX ix_payment_webhook_events_organization_id ON payment_webhook_events (organization_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    CREATE UNIQUE INDEX ix_payment_webhook_events_provider_provider_event_id ON payment_webhook_events (provider, provider_event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261009214009_AddCustomerInvoicePayments') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261009214009_AddCustomerInvoicePayments', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    ALTER TABLE customer_contacts ADD portal_linked_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    ALTER TABLE customer_contacts ADD portal_updates_seen_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    CREATE TABLE customer_portal_invitations (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        contact_id uuid NOT NULL,
        email character varying(254) NOT NULL,
        token_hash text NOT NULL,
        invited_by_user_id uuid NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        accepted_at timestamp with time zone,
        revoked_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_customer_portal_invitations PRIMARY KEY (id),
        CONSTRAINT ck_customer_portal_invitations_expires_after_created CHECK (expires_at > created_at),
        CONSTRAINT fk_customer_portal_invitations_customer_contacts_organization_ FOREIGN KEY (organization_id, contact_id) REFERENCES customer_contacts (organization_id, id),
        CONSTRAINT fk_customer_portal_invitations_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_customer_portal_invitations_users_invited_by_user_id FOREIGN KEY (invited_by_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    CREATE TABLE visit_reschedule_requests (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        visit_id uuid NOT NULL,
        contact_id uuid NOT NULL,
        original_scheduled_start timestamp with time zone NOT NULL,
        preferred_date date NOT NULL,
        time_window character varying(20) NOT NULL,
        reason character varying(500) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_visit_reschedule_requests PRIMARY KEY (id),
        CONSTRAINT ck_visit_reschedule_requests_time_window CHECK (time_window IN ('morning','afternoon','evening','any')),
        CONSTRAINT fk_visit_reschedule_requests_customer_contacts_organization_id FOREIGN KEY (organization_id, contact_id) REFERENCES customer_contacts (organization_id, id),
        CONSTRAINT fk_visit_reschedule_requests_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_visit_reschedule_requests_visits_organization_id_visit_id FOREIGN KEY (organization_id, visit_id) REFERENCES visits (organization_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    CREATE UNIQUE INDEX ux_customer_contacts_org_portal_user ON customer_contacts (organization_id, portal_user_id) WHERE portal_user_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    ALTER TABLE customer_contacts ADD CONSTRAINT ck_customer_contacts_portal_link CHECK ((portal_user_id IS NULL) = (portal_linked_at IS NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    CREATE INDEX ix_customer_portal_invitations_invited_by_user_id ON customer_portal_invitations (invited_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    CREATE INDEX ix_customer_portal_invitations_organization_id_contact_id ON customer_portal_invitations (organization_id, contact_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    CREATE UNIQUE INDEX ix_customer_portal_invitations_token_hash ON customer_portal_invitations (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    CREATE UNIQUE INDEX ux_customer_portal_invitations_open ON customer_portal_invitations (contact_id) WHERE accepted_at IS NULL AND revoked_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    CREATE INDEX ix_visit_reschedule_requests_organization_id_contact_id ON visit_reschedule_requests (organization_id, contact_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    CREATE INDEX ix_visit_reschedule_requests_organization_id_visit_id ON visit_reschedule_requests (organization_id, visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    CREATE UNIQUE INDEX ux_visit_reschedule_requests_pending ON visit_reschedule_requests (visit_id, original_scheduled_start);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261010153810_AddCustomerPortal') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261010153810_AddCustomerPortal', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261015163137_AddNotificationsRecipientIndex') THEN
    DROP INDEX ix_notifications_organization_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261015163137_AddNotificationsRecipientIndex') THEN
    CREATE INDEX ix_notifications_recipient ON notifications (organization_id, recipient_user_id, created_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261015163137_AddNotificationsRecipientIndex') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261015163137_AddNotificationsRecipientIndex', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261015223026_AddAuditOrgOccurredIndex') THEN
    CREATE INDEX ix_audit_org_occurred ON audit_logs (organization_id, occurred_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261015223026_AddAuditOrgOccurredIndex') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261015223026_AddAuditOrgOccurredIndex', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261016204340_AddUserPhotos') THEN
    CREATE TABLE user_photos (
        user_id uuid NOT NULL,
        content_type character varying(40) NOT NULL,
        content bytea NOT NULL,
        size_bytes integer NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_user_photos PRIMARY KEY (user_id),
        CONSTRAINT ck_user_photos_content_type CHECK (content_type IN ('image/png','image/jpeg')),
        CONSTRAINT ck_user_photos_size_bytes CHECK (size_bytes BETWEEN 1 AND 5242880),
        CONSTRAINT fk_user_photos_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261016204340_AddUserPhotos') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261016204340_AddUserPhotos', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE TABLE conversations (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        type character varying(10) NOT NULL,
        work_order_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        last_message_at timestamp with time zone,
        CONSTRAINT pk_conversations PRIMARY KEY (id),
        CONSTRAINT ak_conversations_organization_id_id UNIQUE (organization_id, id),
        CONSTRAINT ck_conversations_job_work_order CHECK ((type = 'job') = (work_order_id IS NOT NULL)),
        CONSTRAINT ck_conversations_type CHECK (type IN ('job', 'team')),
        CONSTRAINT fk_conversations_branches_organization_id_branch_id FOREIGN KEY (organization_id, branch_id) REFERENCES branches (organization_id, id),
        CONSTRAINT fk_conversations_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_conversations_work_orders_organization_id_work_order_id FOREIGN KEY (organization_id, work_order_id) REFERENCES work_orders (organization_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE TABLE conversation_messages (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        organization_id uuid NOT NULL,
        conversation_id uuid NOT NULL,
        author_user_id uuid NOT NULL,
        client_message_id uuid NOT NULL,
        body character varying(2000) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_conversation_messages PRIMARY KEY (id),
        CONSTRAINT ck_conversation_messages_body CHECK (length(btrim(body)) > 0),
        CONSTRAINT fk_conversation_messages_conversations_organization_id_convers FOREIGN KEY (organization_id, conversation_id) REFERENCES conversations (organization_id, id),
        CONSTRAINT fk_conversation_messages_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_conversation_messages_users_author_user_id FOREIGN KEY (author_user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE TABLE conversation_read_states (
        conversation_id uuid NOT NULL,
        user_id uuid NOT NULL,
        organization_id uuid NOT NULL,
        last_read_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_conversation_read_states PRIMARY KEY (conversation_id, user_id),
        CONSTRAINT fk_conversation_read_states_conversations_organization_id_conv FOREIGN KEY (organization_id, conversation_id) REFERENCES conversations (organization_id, id),
        CONSTRAINT fk_conversation_read_states_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id),
        CONSTRAINT fk_conversation_read_states_users_user_id FOREIGN KEY (user_id) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE INDEX ix_conversation_messages_author_user_id ON conversation_messages (author_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE INDEX ix_conversation_messages_conversation ON conversation_messages (conversation_id, created_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE INDEX ix_conversation_messages_organization_id_conversation_id ON conversation_messages (organization_id, conversation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE UNIQUE INDEX ux_conversation_messages_client ON conversation_messages (conversation_id, author_user_id, client_message_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE INDEX ix_conversation_read_states_organization_id_conversation_id ON conversation_read_states (organization_id, conversation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE INDEX ix_conversation_read_states_user_id ON conversation_read_states (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE INDEX ix_conversations_org_branch_last ON conversations (organization_id, branch_id, last_message_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE INDEX ix_conversations_organization_id_work_order_id ON conversations (organization_id, work_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE UNIQUE INDEX ux_conversations_job ON conversations (work_order_id) WHERE type = 'job';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    CREATE UNIQUE INDEX ux_conversations_team ON conversations (branch_id) WHERE type = 'team';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261018165115_AddTechnicianMessages') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261018165115_AddTechnicianMessages', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261020215611_AddGlobalSearchUnaccent') THEN
    CREATE EXTENSION IF NOT EXISTS unaccent;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261020215611_AddGlobalSearchUnaccent') THEN
    CREATE OR REPLACE FUNCTION public.fieldops_unaccent(value text) RETURNS text
      LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
      AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, value) $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261020215611_AddGlobalSearchUnaccent') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261020215611_AddGlobalSearchUnaccent', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261021170544_AddTechnicianBranchHoursInheritance') THEN
    ALTER TABLE technician_profiles ADD uses_branch_hours boolean NOT NULL DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261021170544_AddTechnicianBranchHoursInheritance') THEN
    UPDATE technician_profiles SET uses_branch_hours = false
    WHERE EXISTS (SELECT 1 FROM technician_weekly_availability w WHERE w.technician_id = technician_profiles.id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261021170544_AddTechnicianBranchHoursInheritance') THEN
    ALTER TABLE technician_exceptions ALTER COLUMN reason TYPE character varying(250);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261021170544_AddTechnicianBranchHoursInheritance') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261021170544_AddTechnicianBranchHoursInheritance', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261021194822_AddUserPreferredLanguage') THEN
    ALTER TABLE users ADD preferred_language character varying(10) NOT NULL DEFAULT 'en';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261021194822_AddUserPreferredLanguage') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261021194822_AddUserPreferredLanguage', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261022154336_AddUserPreferredTheme') THEN
    ALTER TABLE users ADD preferred_theme character varying(10) NOT NULL DEFAULT 'system';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261022154336_AddUserPreferredTheme') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261022154336_AddUserPreferredTheme', '10.0.13');
    END IF;
END $EF$;
COMMIT;

