# Quick Reference - Client User Management Features

## User Interface Changes

### DataGrid Actions Column
Located in the **User Management** tab, each user row now has three action buttons:

| Icon | Color | Action | Function |
|------|-------|--------|----------|
| **✎** | Blue | Edit User | Opens dialog to modify user details (Full Name, Phone, Address) |
| **🔑** | Purple | Reset Password | Opens dialog to set new password with confirmation |
| **⊕** | Green | Toggle Status | Toggles user between Active and Disabled status |

## How to Use Each Feature

### 1. Edit Client User
1. Click the blue **✎** button on any user row
2. Edit **Full Name**, **Phone**, or **Address**
3. **Username** is read-only (cannot be changed)
4. Click **Save** to apply changes or **Cancel** to discard
5. Success message appears in the status panel below

### 2. Reset Password
1. Click the purple **🔑** button on any user row
2. Enter the **New Password**
3. Confirm in the **Confirm Password** field
4. Passwords must match and meet requirements:
   - At least 8 characters
   - At least one uppercase letter
   - At least one lowercase letter
   - At least one digit
5. Quick option: Click **Generate (User@123456)** for default password
6. Click **Reset Password** to apply or **Cancel** to discard
7. Password value is shown in success message for communication to user

### 3. Toggle User Status
1. Click the green **⊕** button on any user row
2. Confirm the status change in the popup dialog
3. Status toggles between:
   - **Active** → User can log in
   - **Disabled** → User cannot log in (cannot authenticate)
4. Success message shows the new status

## Response Handling

### Success Messages
- Appear in green text below the user list
- Display operation details (user name, new status, etc.)
- Automatically cleared on next operation

### Error Messages
- Appear in red text below the user list
- Describe what went wrong
- Common errors:
  - "Unable to get user data" - User not selected/found
  - "Passwords do not match" - Confirmation doesn't match
  - "User not found" - User deleted between operations
  - "Failed to update user" - Database error

## Business Rules

### Status Constraints
- Users with **Active** status can authenticate and start sessions
- Users with **Disabled** status cannot authenticate
- Status change is immediate (no logout required for connected users)
- Status changes are logged for audit trail

### Password Requirements
- Minimum 8 characters (enforced in dialog)
- Must contain uppercase letter (A-Z)
- Must contain lowercase letter (a-z)
- Must contain digit (0-9)
- No maximum length restriction
- Spaces allowed

### Edit Restrictions
- **Username** cannot be changed (read-only field)
- Other fields can be left empty or with any content
- Changes take effect immediately

### Delete Operations
- Users cannot be deleted (due to database foreign key constraints)
- Use **Toggle Status → Disabled** to prevent login
- User history/sessions remain in database for audit trail

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| **Enter** in password fields | Submit dialog |
| **Escape** | Cancel dialog |
| **Tab** | Move between fields |

## Audit Trail

All user management operations are logged:
- **Admin ID** - Which admin performed the action
- **Timestamp** - When the action occurred
- **Operation Type** - UserUpdated, PasswordReset, UserStatusChanged
- **Details** - Specific information about the change

View logs in the **Session Logs** tab with category filter set to "Auth"

## Troubleshooting

### Dialog Won't Open
- Ensure a user is selected (row highlighted in blue)
- Verify server connection is active
- Check error message in status panel

### Changes Not Saved
- Verify success message appears (green text)
- Refresh user list to see updated values
- Check error message if operation failed

### Can't Log In After Reset
- Verify password matches exactly (case-sensitive)
- Check user status is "Active" (not "Disabled")
- Verify correct username is being used
- Try password reset again if unsure

## API Methods (Developer Reference)

### Service Calls
```csharp
// Update user profile
UpdateClientUser(userId, fullName, phone, address, adminUserId)

// Reset password
ResetClientUserPassword(userId, newPassword, adminUserId)

// Toggle user status
ToggleUserStatus(userId, adminUserId)
```

### Response Objects
All methods return objects with:
- `Success` (bool) - Operation succeeded
- `UserId` (int) - Affected user
- `ErrorMessage` (string) - Error details
- `NewStatus` (string) - For ToggleUserStatus

## Performance Notes

- Inline actions load immediately without page refresh
- Status changes are atomic (single database operation)
- Password hashing is performed server-side (secure)
- Admin operations logged asynchronously (minimal impact)

## Security Considerations

✅ **Implemented:**
- All operations require admin authentication
- Password hashing using BCrypt
- Audit logging of all changes
- Session validation before operations
- Admin ID logged with each action

⚠️ **Admin Responsibility:**
- Verify admin credentials before allowing access
- Monitor audit logs for unusual activity
- Use strong admin passwords
- Communicate new passwords securely
