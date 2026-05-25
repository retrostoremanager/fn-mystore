-- Seed consignment.view and consignment.edit permissions
-- consignment.view → Cashier, Employee, Manager, Owner
-- consignment.edit → Employee, Manager, Owner

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
