# Billing Rate Management — Admin Guide

## Overview

The **Billing Rates** tab in the Admin Dashboard provides full CRUD management of billing rates used to calculate session costs. All changes are logged automatically.

---

## First-Time Setup

### Add Your First Rate

1. Click the **Billing Rates** tab in Admin Dashboard
2. Fill the form:
   - Rate Name: `Standard`
   - Rate/Min: `0.50`
   - Currency: `USD`
   - Check **Active**
   - Check **Set as Default**
3. Click **Add Rate**
4. Rate appears in the grid — Default column shows `1`

### Verify It Works

- Grid shows your rate with correct values
- Default column = `1` (true)
- Add a second rate — both appear in grid
- Click **Refresh** — both rates persist

---

## How to Use Each Feature

### Add a New Rate

1. Navigate to **Billing Rates** tab
2. Fill the form:
   - **Rate Name** — short description (e.g., "Standard", "Premium")
   - **Rate/Min** — cost per minute (e.g., 0.50)
   - **Currency** — USD, EUR, GBP, or INR
   - **Effective From** — (optional) start date for the rate
   - **Effective To** — (optional) expiry date
   - **Active** — check to activate
   - **Set as Default** — check to make this the default rate
   - **Notes** — optional notes
3. Click **Add Rate**
4. Success message appears, form clears, grid updates

### Edit an Existing Rate

1. Find the rate in the grid
2. Click **✎ Edit**
3. Form auto-populates with current values
4. Modify values as needed
5. Click **Update Rate**
6. Form clears automatically

### Set as Default Rate

1. Find the desired rate in the grid
2. Click **★ Default** (only visible if not already default)
3. Confirm the action
4. Previous default's button reappears; new default button disappears

### Delete a Rate

1. Find the rate in the grid
2. Click **🗑 Delete**
3. Confirm in the popup dialog

> Cannot delete if it is the only rate or the only default rate — an error message will explain.

---

## Common Tasks

### Change the Default Rate
1. Find the new rate in the grid → click **★ Default** → confirm. Done.

### Update a Rate Amount
1. Click **✎ Edit** → change Rate/Min → click **Update Rate**.

### Create a Seasonal Rate
1. Fill form with name, rate, **Effective From / To** dates
2. Do **not** check Set as Default (keep it optional)
3. Click **Add Rate** — rate is available but not the default
4. After the dates pass, deactivate or delete

### Temporarily Disable a Rate
1. Click **✎ Edit** → uncheck **Active** → click **Update Rate**
2. Rate is excluded from billing calculations but data is preserved

---

## Integration with Session Billing

When a new session starts the system looks up the current default billing rate:

```
Monday:   default = "Standard"  (0.50/min) → sessions use 0.50/min
Tuesday:  change default = "Premium" (1.00/min) → new sessions use 1.00/min
          Monday's sessions are unchanged — history preserved
```

Changing the default only affects **new** sessions. Completed sessions keep their original rate.

---

## Form Fields Reference

| Field | Type | Required | Notes |
|---|---|---|---|
| Rate Name | Text | Yes | Short description |
| Rate/Min | Decimal | Yes | Cost per minute |
| Currency | Dropdown | Yes | USD, EUR, GBP, INR |
| Effective From | Date | No | When rate becomes effective |
| Effective To | Date | No | When rate expires |
| Active | Checkbox | No | Uncheck to deactivate without deleting |
| Set as Default | Checkbox | No | Only one default allowed at a time |
| Notes | Text | No | Additional information |

---

## Grid Columns

| Column | Meaning |
|---|---|
| ID | System ID (auto-generated) |
| Name | Rate name/description |
| Rate/Min | Cost per minute |
| Currency | Currency code |
| Effective From / To | Date range (optional) |
| Active | 1 = active, 0 = inactive |
| Default | 1 = default, 0 = not default |
| Created | When the rate was created |
| Notes | Optional notes |
| Actions | ✎ Edit, ★ Default, 🗑 Delete |

---

## System Rules

⚠️ **Always enforced:**
- At least one rate must exist — cannot delete the last rate
- At least one default rate must exist — cannot delete or unset the only default
- Rates cannot be negative
- Rate name is required

✅ **Automatic behaviors:**
- Setting a new default automatically unsets the previous default
- All changes are logged to System Logs (Category: Billing)
- Form clears after each successful Add

---

## Status Messages

**Success (green):**
- `"Billing rate 'Standard Rate' added successfully (ID: 5)"`
- `"Billing rate 'Premium Rate' updated successfully."`
- `"'Standard Rate' is now the default rate."`
- `"Billing rate 'Old Rate' deleted successfully."`

**Error (red):**
- `"Rate name is required."`
- `"Rate must be a valid positive number."`
- `"Cannot delete the last billing rate. At least one rate must exist."`
- `"Cannot delete the only default rate. At least one default rate must exist."`

---

## Audit Trail

All billing rate changes are logged to the **Session Logs** tab:
- Category: `Billing`
- Type: `RateCreated`, `RateUpdated`, `RateDeleted`, or `DefaultRateSet`
- Includes which admin made the change

To view history:
1. Go to **Session Logs** tab → set Category to `Billing` → set date range → click **Load Logs**

---

## FAQ

**Q: Can I have multiple default rates?**
A: No. Only one default at a time — setting a new default auto-unsets the old one.

**Q: Can I delete the current default?**
A: No. Change the default first, then delete.

**Q: Do old sessions get the new rate if I change the default?**
A: No. Completed sessions keep their original rate. Only new sessions use the new default.

**Q: Can I undo a deletion?**
A: No. Use deactivate (uncheck Active) instead of delete if you are unsure.

**Q: What if I accidentally delete all rates?**
A: Technically impossible — the system prevents deletion of the last rate.

**Q: Can two rates have the same name?**
A: Yes, but not recommended. Use unique names to avoid confusion.

**Q: What is the maximum rate value?**
A: `DECIMAL(10,2)` supports up to 99,999,999.99 per minute.

**Q: Can I create a rate for future dates?**
A: Yes. Set **Effective From** to a future date — the rate will be available immediately but you control when to activate it as default.

---

## Common Issues & Solutions

| Issue | Solution |
|---|---|
| Can't delete a rate | Ensure at least 2 rates exist and the one being deleted is not the only default |
| Can't unset as default | Create or set another rate as default first |
| Changes not visible | Click **Refresh** to reload from database |
| Form won't save | Check that Rate Name and Rate/Min are filled with valid values |

---

## Keyboard Shortcuts

| Key | Action |
|---|---|
| Tab | Move between form fields |
| Enter | Submit form (Add / Update) |
| Escape | Clear form |

---

## Permissions

Only **Admin** users can view the Billing Rates tab and manage rates. Client Users see their session billing based on the current default rate but cannot access this tab.

---

## Best Practices

1. Keep at least one simple default rate at all times
2. Use Effective Dates for temporary rates instead of deleting them
3. Use the Notes field to explain why a rate was changed
4. Create a rate as inactive first, test it, then activate
5. Keep all rates in the same currency — do not mix currencies
6. Review Session Logs → Billing monthly for audit trail
