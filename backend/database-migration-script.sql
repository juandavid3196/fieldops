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
    VALUES ('20260921233932_InitialTenancy', '10.0.12');
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
    VALUES ('20260922004838_InitialIdentityAndTenancy', '10.0.12');
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
    VALUES ('20260922154556_AddCustomersAndCatalog', '10.0.12');
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
    VALUES ('20260922155938_AddTechniciansAndAvailability', '10.0.12');
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
    VALUES ('20260922161604_AddCustomerOrganizationForeignKeys', '10.0.12');
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
    VALUES ('20260922161833_AddRequestsAndAssessments', '10.0.12');
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
    VALUES ('20260922162931_AddQuotes', '10.0.12');
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
    VALUES ('20260922164235_AddWorkOrdersAndVisits', '10.0.12');
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
    VALUES ('20260922165620_AddInvoicesAndPayments', '10.0.12');
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
    VALUES ('20260922170641_AddNotificationsAndAudit', '10.0.12');
    END IF;
END $EF$;
COMMIT;

