using System;
using SessionManagement.WCF;

namespace SessionManagement.Client
{
    /// <summary>
    /// WCF proxy for SessionAdmin.
    /// Shared connection logic and all common operations are in SessionServiceClientBase.
    /// Only admin-specific operations that SessionClient does not need are added here.
    /// </summary>
    public sealed class SessionServiceClient : SessionServiceClientBase
    {
        // ─────────────────────────────────────────────────────────
        //  UC-03  —  User Management (admin-only)
        // ─────────────────────────────────────────────────────────

        public UserUpdateResponse UpdateClientUser(int userId, string fullName,
            string phone, string address, int adminUserId,
            string profilePictureBase64 = null)
        {
            if (!EnsureConnection())
                return new UserUpdateResponse
                { Success = false, ErrorMessage = "Not connected to server." };
            try
            { return _proxy.UpdateClientUser(userId, fullName, phone, address, adminUserId, profilePictureBase64); }
            catch (Exception ex)
            { Log($"UpdateClientUser: {ex.Message}");
              return new UserUpdateResponse
              { Success = false, ErrorMessage = $"Connection error: {ex.Message}" }; }
        }

        public UserDeleteResponse DeleteClientUser(int userId, int adminUserId)
        {
            if (!EnsureConnection())
                return new UserDeleteResponse
                { Success = false, ErrorMessage = "Not connected to server." };
            try { return _proxy.DeleteClientUser(userId, adminUserId); }
            catch (Exception ex)
            { Log($"DeleteClientUser: {ex.Message}");
              return new UserDeleteResponse
              { Success = false, ErrorMessage = $"Connection error: {ex.Message}" }; }
        }

        public PasswordResetResponse ResetClientUserPassword(int userId,
            string newPassword, int adminUserId)
        {
            if (!EnsureConnection())
                return new PasswordResetResponse
                { Success = false, ErrorMessage = "Not connected to server." };
            try { return _proxy.ResetClientUserPassword(userId, newPassword, adminUserId); }
            catch (Exception ex)
            { Log($"ResetClientUserPassword: {ex.Message}");
              return new PasswordResetResponse
              { Success = false, ErrorMessage = $"Connection error: {ex.Message}" }; }
        }

        public UserStatusToggleResponse ToggleUserStatus(int userId, int adminUserId)
        {
            if (!EnsureConnection())
                return new UserStatusToggleResponse
                { Success = false, ErrorMessage = "Not connected to server." };
            try { return _proxy.ToggleUserStatus(userId, adminUserId); }
            catch (Exception ex)
            { Log($"ToggleUserStatus: {ex.Message}");
              return new UserStatusToggleResponse
              { Success = false, ErrorMessage = $"Connection error: {ex.Message}" }; }
        }

        // ─────────────────────────────────────────────────────────
        //  PAYMENT COLLECTION  (admin-only)
        // ─────────────────────────────────────────────────────────

        public BillingRecordInfo[] GetBillingRecords(bool unpaidOnly)
        {
            if (!EnsureConnection()) return new BillingRecordInfo[0];
            try { return _proxy.GetBillingRecords(unpaidOnly); }
            catch (Exception ex)
            { Log($"GetBillingRecords: {ex.Message}"); return new BillingRecordInfo[0]; }
        }

        public bool MarkBillingRecordPaid(int billingRecordId, int adminUserId)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.MarkBillingRecordPaid(billingRecordId, adminUserId); }
            catch (Exception ex)
            { Log($"MarkBillingRecordPaid: {ex.Message}"); return false; }
        }

        // ─────────────────────────────────────────────────────────
        //  BILLING RATE MANAGEMENT  (admin-only)
        // ─────────────────────────────────────────────────────────

        public BillingRateInfo[] GetAllBillingRates()
        {
            if (!EnsureConnection()) return new BillingRateInfo[0];
            try { return _proxy.GetAllBillingRates(); }
            catch (Exception ex)
            { Log($"GetAllBillingRates: {ex.Message}"); return new BillingRateInfo[0]; }
        }

        public int InsertBillingRate(string name, decimal ratePerMinute, string currency,
            DateTime? effectiveFrom, DateTime? effectiveTo, bool isDefault, int adminUserId, string notes)
        {
            if (!EnsureConnection()) return -1;
            try { return _proxy.InsertBillingRate(name, ratePerMinute, currency,
                effectiveFrom, effectiveTo, isDefault, adminUserId, notes); }
            catch (Exception ex)
            { Log($"InsertBillingRate: {ex.Message}"); return -1; }
        }

        public bool UpdateBillingRate(int billingRateId, string name, decimal ratePerMinute,
            string currency, DateTime? effectiveFrom, DateTime? effectiveTo,
            bool isActive, bool isDefault, string notes)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.UpdateBillingRate(billingRateId, name, ratePerMinute,
                currency, effectiveFrom, effectiveTo, isActive, isDefault, notes); }
            catch (Exception ex)
            { Log($"UpdateBillingRate: {ex.Message}"); return false; }
        }

        public bool DeleteBillingRate(int billingRateId)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.DeleteBillingRate(billingRateId); }
            catch (Exception ex)
            { Log($"DeleteBillingRate: {ex.Message}"); return false; }
        }

        public bool SetDefaultBillingRate(int billingRateId)
        {
            if (!EnsureConnection()) return false;
            try { return _proxy.SetDefaultBillingRate(billingRateId); }
            catch (Exception ex)
            { Log($"SetDefaultBillingRate: {ex.Message}"); return false; }
        }
    }
}
