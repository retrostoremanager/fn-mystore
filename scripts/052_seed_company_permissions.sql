-- Issue #177: company.view and company.edit permissions not seeded in DB.
-- GET /company/tax requires company.view; PUT /company/tax requires company.edit.
-- Seed permissions and assign to system roles.
-- company.view → Owner, Manager
-- company.edit → Owner, Manager

INSERT INTO permission (name, description) VALUES
    ('company.view', 'View company settings including tax configuration'),
    ('company.edit', 'Edit company settings including tax configuration')
ON CONFLICT (name) DO NOTHING;

INSERT INTO role_permission (role_id, permission_id)
SELECT r.id, p.id FROM role r CROSS JOIN permission p
WHERE r.company_id IS NULL AND r.name IN ('Owner', 'Manager')
  AND p.name = 'company.view'
ON CONFLICT (role_id, permission_id) DO NOTHING;

INSERT INTO role_permission (role_id, permission_id)
SELECT r.id, p.id FROM role r CROSS JOIN permission p
WHERE r.company_id IS NULL AND r.name IN ('Owner', 'Manager')
  AND p.name = 'company.edit'
ON CONFLICT (role_id, permission_id) DO NOTHING;
