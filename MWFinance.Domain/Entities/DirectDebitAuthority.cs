using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MWFinance.Domain.Entities
{
    /// <summary>
    /// This is replicat for DB table 
    /// </summary>
    public class DirectDebitAuthority
    {
        

        public int Id { get; set; }
        public string CustomerIdNumber { get; set; } = string.Empty;
        public string CustomerFullName { get; set; } = string.Empty;
        public string CustomerMobileNumber { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerType { get; set; } = string.Empty;
        public string CustomerIdType { get; set; } = string.Empty;
        public string CustNid { get; set; } = string.Empty;
        public string DdaReferenceNumber { get; set; } = string.Empty;
        public DateTime CommencesOn { get; set; }
        public DateTime ExpiresOn { get; set; }
        public decimal MinAmount { get; set; }
        public decimal MaxAmount { get; set; }
        public string PaymentFrequency { get; set; } = string.Empty;
        public string AmountType { get; set; } = string.Empty;
        public string UserPreferPaymentMethod { get; set; } = string.Empty;
        public string CustomerAccountBankName { get; set; } = string.Empty;
        public string? CustomerBankAccountTitle { get; set; }
        public string? CustomerBankAccountType { get; set; }
        public string? CustomerBankAccountNumber { get; set; }
        public string? CustomerCreditCardNumber { get; set; }
        public string? CreditCardHolderName { get; set; }

        // Tracking Variables
        public int? DdarId { get; set; }
        public int DdaId{ get; set; }
        public string DdaStatus { get; set; } = "PNDG"; 
        public string? CentralBankRefNumber { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
