-- Issue #138: consignment.view/consignment.edit permissions not assigned to any role.
-- Seed consignment permissions and assign to system roles.
-- consignment.view → Owner, Manager, Employee, Cashier
-- consignment.edit → Owner, Manager, Employee (Cashier: view only)

INSERT INTO permission (name, description) VALUES
    ('consignment.view', 'View consignment items and payouts'),
    ('consignment.edit', 'Create, update, and manage consignment items and payouts')
ON CONFLICT (name) DO NOTHING;

INSERT INTO role_permission (role_id, permission_id)
SELECT r.id, p.id FROM role r CROSS JOIN permission p
WHERE r.company_id IS NULL AND r.name IN ('Owner', 'Manager', 'Employee', 'Cashier')
  AND p.name = 'consignment.view'
ON CONFLICT (role_id, permission_id) DO NOTHING;

INSERT INTO role_permission (role_id, permission_id)
SELECT r.id, p.id FROM role r CROSS JOIN permission p
WHERE r.company_id IS NULL AND r.name IN ('Owner', 'Manager', 'Employee')
  AND p.name = 'consignment.edit'
ON CONFLICT (role_id, permission_id) DO NOTHING;
