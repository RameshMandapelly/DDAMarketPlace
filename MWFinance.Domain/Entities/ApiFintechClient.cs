// ============================================================
// FILE LOCATION: MWFinance.Domain / Entities / FintechClientApi.cs
// ============================================================

namespace MWFinance.Domain.Entities
{
    /// <summary>
    /// Represents a Fintech company that is registered to consume the MWFinance API.
    /// Each Fintech client gets one row in this table.
    /// You create their credentials manually (or via an admin tool later).
    /// </summary>
    public class FintechClientApi
    {
        /// <summary>
        /// Auto-incremented primary key (internal use only).
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The public identifier you hand to the Fintech company.
        /// Example: "fintech-xyz-001"
        /// They send this in the login request body.
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// BCrypt hashed version of the Fintech's secret password.
        /// NEVER store the plain text secret here.
        /// When Fintech logs in, you BCrypt.Verify(theirInput, this hash).
        /// </summary>
        public string ClientSecretHash { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable company name for your records.
        /// Example: "XYZ Fintech LLC"
        /// This also goes inside the JWT token so you know who called you.
        /// </summary>
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>
        /// If false, this client cannot login even with correct credentials.
        /// Lets you suspend a Fintech without deleting their record.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Timestamp when this client was registered. Audit trail.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
