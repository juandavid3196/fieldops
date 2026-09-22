-- FieldOps relational model for PostgreSQL 16+
-- Updated from the consolidated 29-screen functional specification.
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TYPE user_status AS ENUM ('pending','active','suspended','disabled');
CREATE TYPE customer_type AS ENUM ('person','company');
CREATE TYPE catalog_item_type AS ENUM ('service','product');
CREATE TYPE request_status AS ENUM ('new','needs_review','assessment_scheduled','ready_for_quote','quoted','converted','cancelled');
CREATE TYPE assessment_status AS ENUM ('scheduled','completed','cancelled','no_show');
CREATE TYPE quote_status AS ENUM ('draft','sent','approved','rejected','clarification_requested','expired','cancelled');
CREATE TYPE work_order_status AS ENUM ('draft','ready_to_schedule','scheduled','in_progress','completed','approved_for_billing','cancelled');
CREATE TYPE visit_status AS ENUM ('unscheduled','scheduled','assigned','on_the_way','in_progress','paused','completed','needs_correction','approved','cancelled');
CREATE TYPE invoice_status AS ENUM ('draft','sent','partially_paid','paid','overdue','void');
CREATE TYPE payment_method AS ENUM ('cash','bank_transfer','card_external','check','other');
CREATE TYPE message_visibility AS ENUM ('customer','internal');
CREATE TYPE notification_status AS ENUM ('pending','sent','failed','read');

CREATE TABLE organizations (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name varchar(160) NOT NULL,
  legal_name varchar(200), tax_id varchar(60), email varchar(254), phone varchar(40),
  timezone varchar(80) NOT NULL DEFAULT 'UTC', currency char(3) NOT NULL DEFAULT 'USD',
  default_tax_rate numeric(7,4) NOT NULL DEFAULT 0 CHECK (default_tax_rate BETWEEN 0 AND 100),
  quote_prefix varchar(20) NOT NULL DEFAULT 'Q', work_order_prefix varchar(20) NOT NULL DEFAULT 'WO',
  invoice_prefix varchar(20) NOT NULL DEFAULT 'INV', next_quote_number bigint NOT NULL DEFAULT 1,
  next_work_order_number bigint NOT NULL DEFAULT 1, next_invoice_number bigint NOT NULL DEFAULT 1,
  require_customer_signature boolean NOT NULL DEFAULT false, is_active boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE branches (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id),
  name varchar(140) NOT NULL, code varchar(30) NOT NULL, email varchar(254), phone varchar(40),
  address_line1 varchar(180), address_line2 varchar(180), city varchar(100), state_region varchar(100),
  postal_code varchar(30), country_code char(2), timezone varchar(80), business_hours jsonb NOT NULL DEFAULT '{}'::jsonb,
  is_active boolean NOT NULL DEFAULT true, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (organization_id, code), UNIQUE (organization_id, id)
);
CREATE TABLE users (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), email varchar(254) NOT NULL, password_hash text NOT NULL,
  first_name varchar(100) NOT NULL, last_name varchar(100) NOT NULL, phone varchar(40), status user_status NOT NULL DEFAULT 'pending',
  email_verified_at timestamptz, last_login_at timestamptz, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (email)
);
CREATE TABLE roles (id smallserial PRIMARY KEY, code varchar(50) UNIQUE NOT NULL, name varchar(100) NOT NULL, description text, is_canonical boolean NOT NULL DEFAULT true);
CREATE TABLE permissions (id smallserial PRIMARY KEY, code varchar(100) UNIQUE NOT NULL, description text NOT NULL);
CREATE TABLE role_permissions (role_id smallint NOT NULL REFERENCES roles(id) ON DELETE CASCADE, permission_id smallint NOT NULL REFERENCES permissions(id) ON DELETE CASCADE, PRIMARY KEY(role_id,permission_id));
CREATE TABLE organization_users (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), user_id uuid NOT NULL REFERENCES users(id),
  role_id smallint NOT NULL REFERENCES roles(id), status user_status NOT NULL DEFAULT 'active', is_all_branches boolean NOT NULL DEFAULT false,
  invited_by_user_id uuid REFERENCES users(id), joined_at timestamptz, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE(organization_id,user_id), UNIQUE(organization_id,id)
);
CREATE TABLE organization_user_branches (organization_user_id uuid NOT NULL REFERENCES organization_users(id) ON DELETE CASCADE, branch_id uuid NOT NULL REFERENCES branches(id) ON DELETE CASCADE, PRIMARY KEY(organization_user_id,branch_id));
CREATE TABLE user_invitations (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), email varchar(254) NOT NULL,
  role_id smallint NOT NULL REFERENCES roles(id), token_hash text UNIQUE NOT NULL, invited_by_user_id uuid NOT NULL REFERENCES users(id),
  expires_at timestamptz NOT NULL, accepted_at timestamptz, revoked_at timestamptz, created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE invitation_branches (invitation_id uuid NOT NULL REFERENCES user_invitations(id) ON DELETE CASCADE, branch_id uuid NOT NULL REFERENCES branches(id), PRIMARY KEY(invitation_id,branch_id));

CREATE TABLE customers (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), type customer_type NOT NULL,
  display_name varchar(180) NOT NULL, legal_name varchar(200), tax_id varchar(60), primary_email varchar(254), primary_phone varchar(40),
  billing_address jsonb, notes text, is_active boolean NOT NULL DEFAULT true, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE(organization_id,id)
);
CREATE TABLE customer_contacts (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), customer_id uuid NOT NULL,
  first_name varchar(100) NOT NULL, last_name varchar(100), email varchar(254), phone varchar(40), title varchar(100),
  is_primary boolean NOT NULL DEFAULT false, portal_user_id uuid REFERENCES users(id), is_active boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY(organization_id,customer_id) REFERENCES customers(organization_id,id), UNIQUE(organization_id,id)
);
CREATE TABLE properties (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), customer_id uuid NOT NULL,
  branch_id uuid REFERENCES branches(id), name varchar(140) NOT NULL, address_line1 varchar(180) NOT NULL, address_line2 varchar(180),
  city varchar(100) NOT NULL, state_region varchar(100), postal_code varchar(30), country_code char(2) NOT NULL,
  latitude numeric(9,6), longitude numeric(9,6), access_instructions text, service_notes text, is_active boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY(organization_id,customer_id) REFERENCES customers(organization_id,id), UNIQUE(organization_id,id)
);
CREATE TABLE customer_notes (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), customer_id uuid NOT NULL, author_user_id uuid NOT NULL REFERENCES users(id), note text NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), FOREIGN KEY(organization_id,customer_id) REFERENCES customers(organization_id,id));

CREATE TABLE service_categories (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), name varchar(120) NOT NULL, description text, is_active boolean NOT NULL DEFAULT true, UNIQUE(organization_id,name), UNIQUE(organization_id,id));
CREATE TABLE catalog_items (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), category_id uuid,
  type catalog_item_type NOT NULL, sku varchar(60), name varchar(160) NOT NULL, description text, unit varchar(40) NOT NULL DEFAULT 'unit',
  unit_cost numeric(14,2) NOT NULL DEFAULT 0 CHECK(unit_cost>=0), unit_price numeric(14,2) NOT NULL CHECK(unit_price>=0),
  tax_rate numeric(7,4) NOT NULL DEFAULT 0 CHECK(tax_rate BETWEEN 0 AND 100), is_active boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY(organization_id,category_id) REFERENCES service_categories(organization_id,id), UNIQUE(organization_id,sku), UNIQUE(organization_id,id)
);
CREATE TABLE skills (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), name varchar(120) NOT NULL, description text, is_active boolean NOT NULL DEFAULT true, UNIQUE(organization_id,name), UNIQUE(organization_id,id));
CREATE TABLE technician_profiles (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), branch_id uuid NOT NULL REFERENCES branches(id),
  organization_user_id uuid, employee_code varchar(50), first_name varchar(100) NOT NULL, last_name varchar(100) NOT NULL,
  email varchar(254), phone varchar(40), status varchar(30) NOT NULL DEFAULT 'active' CHECK(status IN ('active','inactive','suspended')),
  color_hex char(7), notes text, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY(organization_id,organization_user_id) REFERENCES organization_users(organization_id,id), UNIQUE(organization_id,employee_code), UNIQUE(organization_id,id)
);
CREATE TABLE technician_skills (technician_id uuid NOT NULL REFERENCES technician_profiles(id) ON DELETE CASCADE, skill_id uuid NOT NULL REFERENCES skills(id), proficiency smallint CHECK(proficiency BETWEEN 1 AND 5), years_experience numeric(4,1), PRIMARY KEY(technician_id,skill_id));
CREATE TABLE technician_weekly_availability (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), technician_id uuid NOT NULL REFERENCES technician_profiles(id) ON DELETE CASCADE, day_of_week smallint NOT NULL CHECK(day_of_week BETWEEN 0 AND 6), start_time time NOT NULL, end_time time NOT NULL, capacity_percent smallint NOT NULL DEFAULT 100 CHECK(capacity_percent BETWEEN 1 AND 100), CHECK(start_time<end_time));
CREATE TABLE technician_breaks (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), availability_id uuid NOT NULL REFERENCES technician_weekly_availability(id) ON DELETE CASCADE, start_time time NOT NULL, end_time time NOT NULL, CHECK(start_time<end_time));
CREATE TABLE technician_exceptions (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), technician_id uuid NOT NULL REFERENCES technician_profiles(id) ON DELETE CASCADE, starts_at timestamptz NOT NULL, ends_at timestamptz NOT NULL, is_available boolean NOT NULL DEFAULT false, reason varchar(200), CHECK(starts_at<ends_at));

CREATE TABLE service_requests (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), branch_id uuid REFERENCES branches(id),
  request_number bigint NOT NULL, customer_id uuid, contact_id uuid, property_id uuid, category_id uuid,
  guest_name varchar(180), guest_email varchar(254), guest_phone varchar(40), service_address jsonb,
  description text NOT NULL, preferred_start timestamptz, preferred_end timestamptz, status request_status NOT NULL DEFAULT 'new',
  source varchar(30) NOT NULL DEFAULT 'public_form', assigned_dispatcher_user_id uuid REFERENCES users(id),
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(), cancelled_at timestamptz,
  FOREIGN KEY(organization_id,customer_id) REFERENCES customers(organization_id,id), FOREIGN KEY(organization_id,contact_id) REFERENCES customer_contacts(organization_id,id),
  FOREIGN KEY(organization_id,property_id) REFERENCES properties(organization_id,id), FOREIGN KEY(organization_id,category_id) REFERENCES service_categories(organization_id,id),
  UNIQUE(organization_id,request_number), UNIQUE(organization_id,id), CHECK(preferred_end IS NULL OR preferred_start IS NULL OR preferred_start<preferred_end)
);
CREATE TABLE request_attachments (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), request_id uuid NOT NULL, file_name varchar(255) NOT NULL, storage_key text NOT NULL, mime_type varchar(120) NOT NULL, size_bytes bigint NOT NULL CHECK(size_bytes>0), uploaded_by_user_id uuid REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now(), FOREIGN KEY(organization_id,request_id) REFERENCES service_requests(organization_id,id));
CREATE TABLE request_messages (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), request_id uuid NOT NULL, author_user_id uuid REFERENCES users(id), author_contact_id uuid REFERENCES customer_contacts(id), visibility message_visibility NOT NULL, body text NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), FOREIGN KEY(organization_id,request_id) REFERENCES service_requests(organization_id,id));
CREATE TABLE request_status_history (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), request_id uuid NOT NULL, from_status request_status, to_status request_status NOT NULL, changed_by_user_id uuid REFERENCES users(id), reason text, changed_at timestamptz NOT NULL DEFAULT now(), FOREIGN KEY(organization_id,request_id) REFERENCES service_requests(organization_id,id));

CREATE TABLE assessments (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), request_id uuid NOT NULL,
  technician_id uuid REFERENCES technician_profiles(id), scheduled_start timestamptz NOT NULL, scheduled_end timestamptz NOT NULL,
  status assessment_status NOT NULL DEFAULT 'scheduled', diagnosis text, recommended_scope text, internal_notes text,
  completed_at timestamptz, created_by_user_id uuid NOT NULL REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY(organization_id,request_id) REFERENCES service_requests(organization_id,id), CHECK(scheduled_start<scheduled_end), UNIQUE(organization_id,id)
);
CREATE TABLE assessment_attachments (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), assessment_id uuid NOT NULL REFERENCES assessments(id) ON DELETE CASCADE, file_name varchar(255) NOT NULL, storage_key text NOT NULL, mime_type varchar(120) NOT NULL, size_bytes bigint NOT NULL, created_at timestamptz NOT NULL DEFAULT now());

CREATE TABLE quotes (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), branch_id uuid REFERENCES branches(id),
  request_id uuid NOT NULL, customer_id uuid, property_id uuid, quote_number bigint NOT NULL, status quote_status NOT NULL DEFAULT 'draft',
  current_version_no integer NOT NULL DEFAULT 0, approved_version_id uuid, created_by_user_id uuid NOT NULL REFERENCES users(id),
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(), UNIQUE(organization_id,quote_number), UNIQUE(organization_id,id),
  FOREIGN KEY(organization_id,request_id) REFERENCES service_requests(organization_id,id), FOREIGN KEY(organization_id,customer_id) REFERENCES customers(organization_id,id), FOREIGN KEY(organization_id,property_id) REFERENCES properties(organization_id,id)
);
CREATE TABLE quote_versions (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), quote_id uuid NOT NULL,
  version_no integer NOT NULL CHECK(version_no>0), scope text NOT NULL, customer_notes text, internal_notes text,
  subtotal numeric(14,2) NOT NULL CHECK(subtotal>=0), tax_total numeric(14,2) NOT NULL CHECK(tax_total>=0), total numeric(14,2) NOT NULL CHECK(total>=0),
  currency char(3) NOT NULL, valid_until date, sent_at timestamptz, is_immutable boolean NOT NULL DEFAULT false,
  created_by_user_id uuid NOT NULL REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY(organization_id,quote_id) REFERENCES quotes(organization_id,id), UNIQUE(quote_id,version_no), UNIQUE(organization_id,id)
);
ALTER TABLE quotes ADD CONSTRAINT fk_quotes_approved_version FOREIGN KEY(approved_version_id) REFERENCES quote_versions(id);
CREATE TABLE quote_lines (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), quote_version_id uuid NOT NULL REFERENCES quote_versions(id) ON DELETE CASCADE, catalog_item_id uuid REFERENCES catalog_items(id), line_type catalog_item_type NOT NULL, description text NOT NULL, quantity numeric(12,3) NOT NULL CHECK(quantity>0), unit varchar(40) NOT NULL, unit_cost numeric(14,2) NOT NULL DEFAULT 0, unit_price numeric(14,2) NOT NULL CHECK(unit_price>=0), tax_rate numeric(7,4) NOT NULL DEFAULT 0, line_subtotal numeric(14,2) NOT NULL, line_tax numeric(14,2) NOT NULL, line_total numeric(14,2) NOT NULL, sort_order integer NOT NULL DEFAULT 0);
CREATE TABLE quote_responses (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), quote_version_id uuid NOT NULL REFERENCES quote_versions(id), response quote_status NOT NULL CHECK(response IN ('approved','rejected','clarification_requested')), responder_name varchar(180) NOT NULL, responder_contact_id uuid REFERENCES customer_contacts(id), comment text, responded_at timestamptz NOT NULL DEFAULT now(), ip_address inet);

CREATE TABLE work_orders (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), branch_id uuid NOT NULL REFERENCES branches(id),
  work_order_number bigint NOT NULL, quote_version_id uuid NOT NULL UNIQUE REFERENCES quote_versions(id), customer_id uuid NOT NULL, property_id uuid NOT NULL,
  status work_order_status NOT NULL DEFAULT 'draft', priority smallint NOT NULL DEFAULT 3 CHECK(priority BETWEEN 1 AND 5), scope_snapshot text NOT NULL,
  internal_instructions text, preferred_start timestamptz, preferred_end timestamptz, created_by_user_id uuid NOT NULL REFERENCES users(id),
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY(organization_id,customer_id) REFERENCES customers(organization_id,id), FOREIGN KEY(organization_id,property_id) REFERENCES properties(organization_id,id),
  UNIQUE(organization_id,work_order_number), UNIQUE(organization_id,id)
);
CREATE TABLE work_order_required_skills (work_order_id uuid NOT NULL REFERENCES work_orders(id) ON DELETE CASCADE, skill_id uuid NOT NULL REFERENCES skills(id), minimum_proficiency smallint CHECK(minimum_proficiency BETWEEN 1 AND 5), PRIMARY KEY(work_order_id,skill_id));
CREATE TABLE work_order_checklist_templates (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), work_order_id uuid NOT NULL REFERENCES work_orders(id) ON DELETE CASCADE, label varchar(240) NOT NULL, is_required boolean NOT NULL DEFAULT true, sort_order integer NOT NULL DEFAULT 0);
CREATE TABLE visits (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), work_order_id uuid NOT NULL,
  visit_number integer NOT NULL CHECK(visit_number>0), status visit_status NOT NULL DEFAULT 'unscheduled', scheduled_start timestamptz, scheduled_end timestamptz,
  actual_started_at timestamptz, actual_completed_at timestamptz, pause_seconds integer NOT NULL DEFAULT 0 CHECK(pause_seconds>=0),
  completion_summary text, completion_without_signature_reason text, review_notes text, reviewed_by_user_id uuid REFERENCES users(id), reviewed_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY(organization_id,work_order_id) REFERENCES work_orders(organization_id,id), UNIQUE(work_order_id,visit_number), UNIQUE(organization_id,id),
  CHECK(scheduled_end IS NULL OR scheduled_start IS NULL OR scheduled_start<scheduled_end)
);
CREATE TABLE visit_assignments (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), visit_id uuid NOT NULL REFERENCES visits(id) ON DELETE CASCADE, technician_id uuid NOT NULL REFERENCES technician_profiles(id), assigned_by_user_id uuid NOT NULL REFERENCES users(id), is_primary boolean NOT NULL DEFAULT true, assigned_at timestamptz NOT NULL DEFAULT now(), unassigned_at timestamptz, UNIQUE(visit_id,technician_id,unassigned_at));
CREATE TABLE visit_status_history (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), visit_id uuid NOT NULL REFERENCES visits(id) ON DELETE CASCADE, from_status visit_status, to_status visit_status NOT NULL, changed_by_user_id uuid REFERENCES users(id), reason text, changed_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE visit_time_entries (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), visit_id uuid NOT NULL REFERENCES visits(id) ON DELETE CASCADE, technician_id uuid NOT NULL REFERENCES technician_profiles(id), started_at timestamptz NOT NULL, ended_at timestamptz, entry_type varchar(20) NOT NULL CHECK(entry_type IN ('work','pause','travel')), CHECK(ended_at IS NULL OR started_at<ended_at));
CREATE TABLE visit_checklist_items (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), visit_id uuid NOT NULL REFERENCES visits(id) ON DELETE CASCADE, template_item_id uuid REFERENCES work_order_checklist_templates(id), label varchar(240) NOT NULL, is_required boolean NOT NULL DEFAULT true, is_completed boolean NOT NULL DEFAULT false, completed_by_user_id uuid REFERENCES users(id), completed_at timestamptz, notes text, sort_order integer NOT NULL DEFAULT 0);
CREATE TABLE visit_materials (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), visit_id uuid NOT NULL REFERENCES visits(id) ON DELETE CASCADE, catalog_item_id uuid REFERENCES catalog_items(id), description text NOT NULL, quantity numeric(12,3) NOT NULL CHECK(quantity>0), unit varchar(40) NOT NULL, unit_cost numeric(14,2) NOT NULL DEFAULT 0, billable boolean NOT NULL DEFAULT false);
CREATE TABLE visit_evidence (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), visit_id uuid NOT NULL REFERENCES visits(id) ON DELETE CASCADE, file_name varchar(255) NOT NULL, storage_key text NOT NULL, mime_type varchar(120) NOT NULL, size_bytes bigint NOT NULL CHECK(size_bytes>0), evidence_type varchar(30) NOT NULL CHECK(evidence_type IN ('before','during','after','incident','other')), caption text, uploaded_by_user_id uuid NOT NULL REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE visit_incidents (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), visit_id uuid NOT NULL REFERENCES visits(id) ON DELETE CASCADE, type varchar(80) NOT NULL, description text NOT NULL, additional_work_requested boolean NOT NULL DEFAULT false, created_by_user_id uuid NOT NULL REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE customer_signoffs (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), visit_id uuid NOT NULL UNIQUE REFERENCES visits(id), signer_name varchar(180), signer_contact_id uuid REFERENCES customer_contacts(id), signature_storage_key text, accepted boolean NOT NULL, comments text, absence_reason text, signed_at timestamptz NOT NULL DEFAULT now());

CREATE TABLE invoices (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), branch_id uuid NOT NULL REFERENCES branches(id),
  invoice_number bigint NOT NULL, work_order_id uuid NOT NULL REFERENCES work_orders(id), customer_id uuid NOT NULL, status invoice_status NOT NULL DEFAULT 'draft',
  issue_date date, due_date date, currency char(3) NOT NULL, subtotal numeric(14,2) NOT NULL CHECK(subtotal>=0), tax_total numeric(14,2) NOT NULL CHECK(tax_total>=0),
  total numeric(14,2) NOT NULL CHECK(total>=0), amount_paid numeric(14,2) NOT NULL DEFAULT 0 CHECK(amount_paid>=0), balance_due numeric(14,2) NOT NULL CHECK(balance_due>=0),
  notes text, sent_at timestamptz, voided_at timestamptz, void_reason text, created_by_user_id uuid NOT NULL REFERENCES users(id),
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY(organization_id,customer_id) REFERENCES customers(organization_id,id), UNIQUE(organization_id,invoice_number), UNIQUE(organization_id,id), CHECK(due_date IS NULL OR issue_date IS NULL OR due_date>=issue_date), CHECK(amount_paid<=total)
);
CREATE TABLE invoice_lines (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), invoice_id uuid NOT NULL REFERENCES invoices(id) ON DELETE CASCADE, source_quote_line_id uuid REFERENCES quote_lines(id), source_visit_material_id uuid REFERENCES visit_materials(id), description text NOT NULL, quantity numeric(12,3) NOT NULL CHECK(quantity>0), unit varchar(40) NOT NULL, unit_price numeric(14,2) NOT NULL CHECK(unit_price>=0), tax_rate numeric(7,4) NOT NULL DEFAULT 0, line_subtotal numeric(14,2) NOT NULL, line_tax numeric(14,2) NOT NULL, line_total numeric(14,2) NOT NULL, sort_order integer NOT NULL DEFAULT 0);
CREATE TABLE payments (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), customer_id uuid NOT NULL, payment_number bigint NOT NULL, method payment_method NOT NULL, amount numeric(14,2) NOT NULL CHECK(amount>0), currency char(3) NOT NULL, paid_at timestamptz NOT NULL, external_reference varchar(160), notes text, recorded_by_user_id uuid REFERENCES users(id), receipt_storage_key text, created_at timestamptz NOT NULL DEFAULT now(), FOREIGN KEY(organization_id,customer_id) REFERENCES customers(organization_id,id), UNIQUE(organization_id,payment_number), UNIQUE(organization_id,id));
CREATE TABLE payment_allocations (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), payment_id uuid NOT NULL REFERENCES payments(id) ON DELETE CASCADE, invoice_id uuid NOT NULL REFERENCES invoices(id), amount numeric(14,2) NOT NULL CHECK(amount>0), created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(payment_id,invoice_id));

CREATE TABLE notifications (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), recipient_user_id uuid REFERENCES users(id), recipient_contact_id uuid REFERENCES customer_contacts(id), channel varchar(20) NOT NULL CHECK(channel IN ('email','sms','in_app')), template_code varchar(80) NOT NULL, subject varchar(240), payload jsonb NOT NULL DEFAULT '{}'::jsonb, status notification_status NOT NULL DEFAULT 'pending', scheduled_at timestamptz NOT NULL DEFAULT now(), sent_at timestamptz, failure_reason text, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE audit_logs (id bigserial PRIMARY KEY, organization_id uuid NOT NULL REFERENCES organizations(id), actor_user_id uuid REFERENCES users(id), action varchar(100) NOT NULL, entity_type varchar(100) NOT NULL, entity_id uuid, branch_id uuid REFERENCES branches(id), before_data jsonb, after_data jsonb, metadata jsonb NOT NULL DEFAULT '{}'::jsonb, ip_address inet, occurred_at timestamptz NOT NULL DEFAULT now());

CREATE INDEX ix_branches_org_active ON branches(organization_id,is_active);
CREATE INDEX ix_customers_org_name ON customers(organization_id,display_name);
CREATE INDEX ix_contacts_org_email ON customer_contacts(organization_id,email);
CREATE INDEX ix_properties_customer ON properties(organization_id,customer_id);
CREATE INDEX ix_technicians_branch_status ON technician_profiles(organization_id,branch_id,status);
CREATE INDEX ix_requests_pipeline ON service_requests(organization_id,status,created_at DESC);
CREATE INDEX ix_requests_customer ON service_requests(organization_id,customer_id);
CREATE INDEX ix_assessments_schedule ON assessments(organization_id,scheduled_start,status);
CREATE INDEX ix_quotes_status ON quotes(organization_id,status,created_at DESC);
CREATE INDEX ix_work_orders_status ON work_orders(organization_id,branch_id,status);
CREATE INDEX ix_visits_schedule ON visits(organization_id,scheduled_start,status);
CREATE INDEX ix_assignments_technician ON visit_assignments(technician_id,assigned_at) WHERE unassigned_at IS NULL;
CREATE INDEX ix_invoices_status_due ON invoices(organization_id,status,due_date);
CREATE INDEX ix_payments_customer_date ON payments(organization_id,customer_id,paid_at DESC);
CREATE INDEX ix_audit_entity ON audit_logs(organization_id,entity_type,entity_id,occurred_at DESC);
CREATE INDEX ix_audit_actor ON audit_logs(organization_id,actor_user_id,occurred_at DESC);

INSERT INTO roles(code,name) VALUES
 ('owner','Owner'),('dispatcher','Dispatcher'),('technician','Technician'),('accounting','Accounting'),
 ('operations_manager','Operations Manager'),('viewer','Viewer') ON CONFLICT DO NOTHING;

-- Important cross-row invariants should be enforced in the application/domain layer or deferred triggers:
-- 1. organization_id must match across every relation, including simple UUID foreign keys.
-- 2. only an immutable sent QuoteVersion can be approved; one approval per Quote.
-- 3. WorkOrder creation from approved QuoteVersion is idempotent (UNIQUE quote_version_id).
-- 4. technician assignment validates branch, skills, availability, absences and overlaps.
-- 5. visit completion requires mandatory checklist/evidence; signature may be replaced by a reason.
-- 6. only reviewed/approved work can be invoiced.
-- 7. sum(payment_allocations.amount) cannot exceed payment.amount or invoice.balance_due.
-- 8. invoice amount_paid and balance_due are updated transactionally from allocations.
