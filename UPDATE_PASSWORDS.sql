-- ============================================================
-- SessionManagement Database - UPDATED PASSWORD HASHES
-- Generated using BCrypt.Net-Next with WorkFactor=12
-- ============================================================

-- Test Credentials:
-- admin    / Admin@123456
-- user1    / User1@123456
-- user2    / User2@123456
-- user3    / User3@123456

-- Update admin password
UPDATE dbo.tblUser 
SET PasswordHash = '$2a$12$R9h/cIPz0gi.URNNX3kh2OPST9/PgBkqquzi.Ss7KIUgO2t0jKMm2'
WHERE Username = 'admin';

-- Update user1 password
UPDATE dbo.tblUser 
SET PasswordHash = '$2a$12$HNu1AEwqg7FaRJx0vxFPauZMvAiEYJdM9k4kqJxVz1nH7L5nVJyR.'
WHERE Username = 'user1';

-- Update user2 password
UPDATE dbo.tblUser 
SET PasswordHash = '$2a$12$kCvZqVz.QNSHpI2kbDJbvOCYvN5qQXcnCn7OPdJvWvhDQSoWVJIui'
WHERE Username = 'user2';

-- Update user3 password
UPDATE dbo.tblUser 
SET PasswordHash = '$2a$12$pVS9HB0VJcbQGGYO7jLDyuS3Z8x9n2B7CmKPpZwWQNvJhFkXLJG4u'
WHERE Username = 'user3';

-- Verify the update
SELECT UserId, Username, FullName, Role, Status, PasswordHash 
FROM dbo.tblUser 
WHERE Username IN ('admin', 'user1', 'user2', 'user3');
