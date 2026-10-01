-- Creates the MariaDB account BulkyWeb uses. Run it once per machine as root:
--   mariadb -u root -p < scripts/create-mariadb-user.sql
-- Replace CHANGE_ME with your own password first, and never commit the real one.
-- The app creates the Bulky database and its tables itself on first start (DbInitializer).

CREATE USER IF NOT EXISTS 'bulky'@'localhost' IDENTIFIED BY 'CHANGE_ME';

-- Only the Bulky database: the account cannot read or change anything else on the server
GRANT ALL PRIVILEGES ON `Bulky`.* TO 'bulky'@'localhost';

FLUSH PRIVILEGES;
