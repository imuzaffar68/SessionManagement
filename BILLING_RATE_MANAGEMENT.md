# Billing Rate Management Implementation

## Overview
A complete billing rate management system has been implemented with the following capabilities:
- **Create** new billing rates
- **Read/View** all billing rates with filtering
- **Update** existing billing rates  
- **Delete** billing rates (with constraints)
- **Set Default** rate (ensuring at least one default always exists)
- **Enforce Data Integrity** - at least one rate and one default rate must always exist

---

## Database Design

### Table: `tblBillingRate`
```sql
CREATE TABLE dbo.tblBillingRate (
    BillingRateId      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name               NVARCHAR(100) NOT NULL,
    RatePerMinute      DECIMAL(10,2) NOT NULL,
    Currency           NVARCHAR(10) NOT NULL,
    EffectiveFrom      DATE NULL,
    EffectiveTo        DATE NULL,
    IsActive           BIT NOT NULL DEFAULT (1),
    IsDefault          BIT NOT NULL DEFAULT (0),
    SetByAdminUserId   INT NULL,
    Notes              NVARCHAR(500) NULL,
    CreatedAt          DATETIME NOT NULL DEFAULT (GETDATE())
);
```

### Key Features:
- `IsActive` flag allows soft-deletion without losing billing history
- `IsDefault` flag designates the current default rate
- `EffectiveFrom/To` dates allow for date-range specific rates
- `SetByAdminUserId` tracks which admin created the rate
- `CreatedAt` timestamp for audit trail

---

## Stored Procedures

### 1. `sp_InsertBillingRate`
**Purpose**: Insert a new billing rate with automatic default handling

**Parameters**:
- `@Name` - Rate name/description
- `@RatePerMinute` - Cost per minute (DECIMAL)
- `@Currency` - Currency code (e.g., "USD", "EUR")
- `@EffectiveFrom` - Effective start date (nullable)
- `@EffectiveTo` - Effective end date (nullable)
- `@IsDefault` - If 1, this becomes the new default (old default updated to 0)
- `@SetByAdminUserId` - Admin user ID
- `@Notes` - Optional notes
- `@NewBillingRateId` OUTPUT - Returns the new rate ID

**Validation**:
- Rate cannot be negative
- If `@IsDefault = 1`, automatically unsets all other defaults before insertion

**Returns**: New `BillingRateId` on success, -1 on error

---

### 2. `sp_UpdateBillingRate`
**Purpose**: Update an existing billing rate with default handling

**Parameters**:
- `@BillingRateId` - Rate to update
- `@Name`, `@RatePerMinute`, `@Currency`, etc. - Updated values
- `@IsDefault` - New default status

**Validation**:
- Rate cannot be negative
- Rate must exist
- Cannot unset as default if it's the only default
- If setting as default, unsets all others

**Returns**: 1 on success, 0 on error

---

### 3. `sp_DeleteBillingRate`
**Purpose**: Delete a billing rate with data integrity protection

**Parameters**:
- `@BillingRateId` - Rate to delete

**Validation**:
- ✓ At least one other rate must exist
- ✓ Cannot delete the only default rate
- ✓ At least one default rate must remain

**Returns**: 1 on success, 0 if constraints violated

---

### 4. `sp_GetAllBillingRates`
**Purpose**: Retrieve all billing rates for display/management

**Returns**: DataTable with all rate columns, ordered by `IsDefault DESC, CreatedAt DESC`

---

### 5. `sp_SetDefaultBillingRate`
**Purpose**: Atomically set a rate as default and unset all others

**Parameters**:
- `@BillingRateId` - Rate to set as default

**Validation**:
- Rate must exist

**Returns**: 1 on success, 0 on error

---

## C# Implementation

### DatabaseHelper Methods

#### `GetAllBillingRates()`
```csharp
public DataTable GetAllBillingRates()
```
- Calls `sp_GetAllBillingRates`
- Returns all billing rates

#### `InsertBillingRate(...)`
```csharp
public int InsertBillingRate(string name, decimal ratePerMinute, string currency,
    DateTime? effectiveFrom, DateTime? effectiveTo, bool isDefault, 
    int adminUserId, string notes = null)
```
- Calls `sp_InsertBillingRate`
- Returns new rate ID or -1 on error

#### `UpdateBillingRate(...)`
```csharp
public bool UpdateBillingRate(int billingRateId, string name, decimal ratePerMinute,
    string currency, DateTime? effectiveFrom, DateTime? effectiveTo, 
    bool isActive, bool isDefault, string notes = null)
```
- Calls `sp_UpdateBillingRate`
- Returns true on success

#### `DeleteBillingRate(int billingRateId)`
```csharp
public bool DeleteBillingRate(int billingRateId)
```
- Calls `sp_DeleteBillingRate`
- Returns true if deleted, false if constraints prevented deletion

#### `SetDefaultBillingRate(int billingRateId)`
```csharp
public bool SetDefaultBillingRate(int billingRateId)
```
- Calls `sp_SetDefaultBillingRate`
- Returns true on success

---

### WCF Service

#### ISessionService Interface Methods
All methods added as `[OperationContract]`:
- `GetAllBillingRates()` → Returns `DataTable`
- `InsertBillingRate(...)` → Returns `int` (new rate ID)
- `UpdateBillingRate(...)` → Returns `bool`
- `DeleteBillingRate(...)` → Returns `bool`
- `SetDefaultBillingRate(...)` → Returns `bool`

#### SessionService Implementation
Wraps database calls with error handling and system logging:
- All exceptions logged to `tblSystemLog`
- Category: "Billing"
- Type: "RateCreated", "RateUpdated", "RateDeleted", "DefaultRateSet", or "Error"

---

## UI Implementation (SessionAdmin)

### New "Billing Rates" Tab

#### Form Section (Top)
**Add/Edit Billing Rate Form**:
- `txtRateName` - Rate name/description
- `txtRatePerMinute` - Rate amount
- `cboCurrency` - Currency dropdown (USD, EUR, GBP, INR)
- `dpEffectiveFrom` - Start date picker
- `dpEffectiveTo` - End date picker
- `chkIsActive` - Active checkbox
- `chkIsDefault` - Set as default checkbox
- `txtNotes` - Optional notes
- `btnAddBillingRate` - Add new rate button
- `btnUpdateBillingRate` - Update existing rate (hidden until edit mode)
- `btnClearBillingRateForm` - Clear form button

**Status Messages**:
- `lblBillingRateError` - Red error messages
- `lblBillingRateSuccess` - Green success messages

#### Data Grid Section (Bottom)
**Columns**:
- ID, Name, Rate/Min, Currency
- Effective From, Effective To
- Active, Default, Created, Notes
- Actions

**Action Buttons**:
- `✎ Edit` - Loads rate into form for editing
- `★ Default` - Sets as default (only visible if not already default)
- `🗑 Delete` - Deletes rate (with confirmation)

**Toolbar**:
- Total count
- Refresh button to reload from database

---

### Code-Behind Implementation

#### View Model: `BillingRateVM`
```csharp
public class BillingRateVM
{
    public int BillingRateId { get; set; }
    public string Name { get; set; }
    public decimal RatePerMinute { get; set; }
    public string Currency { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public int IsActive { get; set; }
    public int IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Notes { get; set; }
}
```

#### Key Methods

**`LoadBillingRates()`**
- Fetches all rates from server
- Populates `_billingRates` collection
- Updates count display

**`btnAddBillingRate_Click()`**
- Validates form inputs
- Calls `_svc.InsertBillingRate(...)`
- Shows success/error message
- Refreshes grid
- Clears form

**`btnEditBillingRate_Click()`**
- Populates form with selected rate data
- Toggles button visibility (Hide Add, Show Update)
- Sets `_selectedBillingRateId` for tracking

**`btnUpdateBillingRate_Click()`**
- Validates form inputs
- Calls `_svc.UpdateBillingRate(...)`
- Updates grid
- Returns to Add mode

**`btnSetDefaultBillingRate_Click()`**
- Confirms action (dialog)
- Calls `_svc.SetDefaultBillingRate(...)`
- Refreshes grid (removes button from previous default, shows on all others)

**`btnDeleteBillingRate_Click()`**
- Confirms deletion
- Calls `_svc.DeleteBillingRate(...)`
- Shows appropriate error if constraints prevent deletion:
  - "At least one rate must exist"
  - "At least one default rate must exist"
- Refreshes grid on success

**`ClearBillingRateForm()`**
- Clears all form fields
- Resets `_selectedBillingRateId` to null
- Returns to Add mode
- Hides error/success messages

---

## Data Integrity Constraints

### Business Rules Enforced

1. **At Least One Rate Must Exist**
   - Prevents deletion if only one rate exists
   - Database check ensures this at all times

2. **At Least One Default Rate Must Exist**
   - Cannot delete the only default rate
   - Cannot unset as default if it's the only default
   - When adding new rate as default, old default is automatically updated

3. **Rate Validation**
   - Rate per minute cannot be negative
   - Rate name is required
   - Currency is required

4. **Default Rate Handling**
   - Only one rate can be default at a time
   - Setting a rate as default automatically unsets all others
   - Checked at both database and application levels

---

## Audit Trail

### System Logging
All operations logged to `tblSystemLog`:

**Insert**:
```
Category: 'Billing'
Type: 'RateCreated'
Message: 'Billing rate created: StandardRate (0.50 USD/min)'
```

**Update**:
```
Category: 'Billing'
Type: 'RateUpdated'
Message: 'Billing rate updated: PremiumRate (1.00 USD/min)'
```

**Delete**:
```
Category: 'Billing'
Type: 'RateDeleted'
Message: 'Billing rate deleted: OldRate (was used in 25 records)'
```

**Set Default**:
```
Category: 'Billing'
Type: 'DefaultRateSet'
Message: 'Default billing rate set to: StandardRate'
```

---

## Usage Example

### Adding a New Billing Rate

1. Click "Billing Rates" tab in Admin Dashboard
2. Fill in the form:
   - Name: "Weekend Rate"
   - Rate: 0.75
   - Currency: USD
   - Effective From: [pick date]
   - Check "Active"
3. Click "Add Rate"
4. Rate appears in grid below

### Changing Default Rate

1. Locate desired rate in grid
2. Click "★ Default" button
3. Confirm action
4. Previous default's button reappears; new default button disappears

### Updating a Rate

1. Click "✎ Edit" button on rate in grid
2. Form populates with rate data
3. Modify values as needed
4. Click "Update Rate" button
5. Confirm changes in success message

### Deleting a Rate

1. Click "🗑 Delete" button on rate
2. Confirm deletion
3. If deletion fails, error message explains why:
   - "Cannot delete the last billing rate"
   - "Cannot delete the only default rate"

---

## Integration with Existing Features

### Session Billing Calculation
The session billing system now:
- Uses `GetCurrentBillingRate()` to fetch default rate
- Falls back to any active rate if no default exists
- Falls back to 0.50 if no rates exist (safety default)

### System Logs
All rate management operations:
- Logged with admin user ID
- Accessible via Session Logs tab (Category filter: "Billing")
- Provides complete audit trail

---

## Files Modified

### SQL Scripts
- `SessionManagement.sql` - Added 5 stored procedures + DROP statements

### C# - Data Layer
- `SessionManagement.Shared\Data\DatabaseHelper.cs` - Added 5 public methods

### C# - WCF
- `SessionManagement.Shared\WCF\IsessionService.cs` - Added 5 operation contracts
- `SessionManagement.Shared\WCF\SessionService.cs` - Added 5 implementations

### C# - UI (Admin)
- `SessionAdmin\SessionServiceClient.cs` - Added 5 proxy methods
- `SessionAdmin\MainWindow.xaml` - Added Billing Rates tab (XAML)
- `SessionAdmin\MainWindow.xaml.cs` - Added methods and view model

---

## Testing Checklist

- [x] Add new rate successfully
- [x] Edit existing rate
- [x] Delete rate (with validation)
- [x] Set rate as default (auto-updates previous)
- [x] Prevent deletion of only rate
- [x] Prevent deletion of only default rate
- [x] Prevent unsetting as default if only default
- [x] Form validation (name, rate required)
- [x] Rate cannot be negative
- [x] Effective date ranges work correctly
- [x] Active/Inactive flag works
- [x] System logs entries created
- [x] Build compiles without errors
- [x] WCF methods callable from client

---

## Error Handling

All operations include:
- Input validation with user-friendly error messages
- Database constraint checking with specific error messages
- Try-catch blocks with logging to `tblSystemLog`
- Graceful error display in UI

---

## Notes

- All changes follow existing code patterns and conventions
- Code organized in regions for clarity
- Comprehensive comments in procedures
- DataTable used for flexibility in client binding
- Nullable DateTime used for optional date ranges
- Enum-like currencies in dropdown (easily extensible)
