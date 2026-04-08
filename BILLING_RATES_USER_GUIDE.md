# Billing Rate Management - Quick Reference

## Admin Dashboard - Billing Rates Tab

### How to Add a New Rate

1. Navigate to **Billing Rates** tab in Admin Dashboard
2. Fill the form:
   - **Rate Name**: Short description (e.g., "Standard", "Premium")
   - **Rate/Min**: Cost per minute (e.g., 0.50)
   - **Currency**: Select USD, EUR, GBP, or INR
   - **Effective From**: (Optional) Start date for the rate
   - **Effective To**: (Optional) End date for the rate
   - **Active**: Check to activate, uncheck to deactivate
   - **Set as Default**: Check to make this the default rate
   - **Notes**: Optional notes for future reference
3. Click **Add Rate**
4. Success message appears, form clears, grid updates

### How to Edit an Existing Rate

1. Find the rate in the grid below the form
2. Click **✎ Edit** button
3. Form auto-populates with current values
4. Modify any values
5. Click **Update Rate** button
6. Success message appears, grid updates
7. Form clears automatically (ready for new entry)

### How to Set as Default Rate

1. Find the desired rate in the grid
2. Click **★ Default** button (only visible if not already default)
3. Confirm the action
4. The new rate becomes default:
   - "Default" column shows TRUE
   - **★ Default** button disappears from this rate
   - **★ Default** button reappears on previously default rate

### How to Delete a Rate

1. Find the rate in the grid
2. Click **🗑 Delete** button
3. Confirm deletion in popup dialog
4. Rate is deleted and grid updates

**Note**: You cannot delete if:
- It's the only rate in the system
- It's the only default rate

An error message will explain why the deletion was prevented.

---

## Important Rules

⚠️ **System Constraints** (Always enforced):
- **At least one rate must exist** - You cannot delete the last rate
- **At least one default rate must exist** - You cannot delete or unset the only default
- **Rates cannot be negative** - The form will reject negative amounts
- **Name is required** - Cannot save without a rate name

✅ **Automatic Behaviors**:
- When you set a rate as default, the previous default is automatically updated
- When you add a rate as default, it automatically becomes the only default
- All changes are logged to the system log (Category: Billing)

---

## Form Fields Explained

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| Rate Name | Text | Yes | Short description of the rate |
| Rate/Min | Decimal | Yes | Cost per minute in selected currency |
| Currency | Dropdown | Yes | USD, EUR, GBP, INR |
| Effective From | Date | No | When this rate becomes effective |
| Effective To | Date | No | When this rate expires |
| Active | Checkbox | No | Uncheck to deactivate without deleting |
| Set as Default | Checkbox | No | Check to make this the default rate |
| Notes | Text | No | Any additional information |

---

## Grid Columns

| Column | Meaning |
|--------|---------|
| ID | System ID (auto-generated) |
| Name | Rate name/description |
| Rate/Min | Cost per minute |
| Currency | Currency code |
| Effective From | Rate start date |
| Effective To | Rate end date |
| Active | 1=active, 0=inactive |
| Default | 1=default, 0=not default |
| Created | When the rate was created |
| Notes | Optional notes |
| Actions | Edit, Set Default, Delete buttons |

---

## Typical Workflows

### Scenario 1: New Service Launch
1. Admin creates new rate: "Premium Support" @ 2.00 USD/min
2. Admin DOES NOT set as default initially (leave unchecked)
3. Admin tests with a few sessions using the new rate
4. Once verified, admin clicks **★ Default** to make it the default

### Scenario 2: Seasonal Rate Change
1. Admin creates new rate: "Holiday Rate" @ 1.50 USD/min
2. Admin sets **Effective From**: Dec 15, **Effective To**: Jan 2
3. Admin checks **Set as Default**
4. The rate is immediately available but only shows when selected
5. After the dates pass, can deactivate or delete

### Scenario 3: Rate Adjustment
1. Admin finds "Standard Rate" in grid
2. Clicks **✎ Edit**
3. Changes rate from 0.50 to 0.55 USD/min
4. Adds note: "Increased due to inflation"
5. Clicks **Update Rate**
6. All future sessions use 0.55; history preserved

---

## Status Messages

### ✅ Success Messages (Green)
- "Billing rate 'Standard Rate' added successfully (ID: 5)"
- "Billing rate 'Premium Rate' updated successfully."
- "'Standard Rate' is now the default rate."
- "Billing rate 'Old Rate' deleted successfully."

### ❌ Error Messages (Red)
- "Rate name is required."
- "Rate must be a valid positive number."
- "Cannot delete the last billing rate. At least one rate must exist."
- "Cannot delete the only default rate. At least one default rate must exist."
- "Failed to insert billing rate. Please try again."

---

## Audit Trail

All billing rate changes are automatically logged to the **Session Logs** tab:
- Category: "Billing"
- Type: "RateCreated", "RateUpdated", "RateDeleted", or "DefaultRateSet"
- Shows which admin made the change

To view history:
1. Go to **Session Logs** tab
2. Set Category filter to "Billing"
3. Set date range
4. Click **Load Logs**

---

## Common Issues & Solutions

| Issue | Solution |
|-------|----------|
| Can't delete a rate | Ensure at least 2 rates exist and 2 defaults don't exist. You need at least 1 default. |
| Can't unset as default | There must be at least one other default before unsetting. Create another default first. |
| Effective dates not working | Dates are optional. Leave blank if rate should apply always. |
| Form won't save | Check that Rate Name and Rate/Min are filled in with valid values. |
| Changes not visible | Click **Refresh** button to reload from database. |

---

## Keyboard Shortcuts

- **Tab** - Move between form fields
- **Enter** - Submit form (Add/Update)
- **Escape** - Clear form

---

## Permission Notes

Only **Admin** users can:
- View the Billing Rates tab
- Add rates
- Edit rates
- Delete rates
- Set default rate

Regular **Client Users** can:
- View their current session billing based on the default rate
- Cannot access the Billing Rates tab

---

## Best Practices

1. **Always maintain a simple default rate** - Keep at least one straightforward rate as the system default
2. **Use effective dates for temporary rates** - Instead of deleting, set an end date
3. **Document rate changes** - Use the Notes field to explain rate changes
4. **Review logs monthly** - Check Session Logs > Billing category for audit trail
5. **Test before activating** - Create a rate as inactive first, test it, then activate
6. **Keep currency consistent** - Don't mix currencies; use the same currency for all rates
7. **Never have zero rates** - The system requires at least one rate to function

---

## Support

If you encounter issues:
1. Check the error message carefully
2. Review the Common Issues table above
3. Look at Session Logs for audit trail
4. Contact system administrator if needed

---

**Last Updated**: $(date)
**Version**: 1.0
**Status**: Production Ready
