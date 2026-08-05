using System.ComponentModel.DataAnnotations;
namespace MWFinance.API.DTOs
{

    /// <summary>
    /// This is DTO for Request Object and client validation.
    /// </summary>
    public class CreateDdaRequest
    {

        [Required(ErrorMessage = "Customer ID number (customerIdNumber) is mandatory.")]
        [StringLength(30, ErrorMessage = "Customer ID number cannot exceed 30 characters.")]
        // Permits letters, numbers, spaces, hyphens, and slashes for real-world document formatting
        [RegularExpression(@"^[A-Za-z0-9\s\-\/]+$", ErrorMessage = "Customer ID number can only contain alphanumeric characters, spaces, hyphens, or slashes.")]
        public string CustomerIdNumber { get; set; } = null!;

        [Required(ErrorMessage = "Customer full name is mandatory.")]
        [StringLength(100, ErrorMessage = "Customer full name cannot exceed 100 characters.")]
        // Restricts the payload to a maximum of 7 words while allowing alphanumeric/special character names
        [RegularExpression(@"^(?:\b[A-Za-z0-9\.\-\/]+\b\s*){1,7}$", ErrorMessage = "Customer full name must contain a maximum of 7 words only.")]
        public string CustomerFullName { get; set; } = null!;

     
        
        [Required(ErrorMessage = "Customer mobile number is mandatory.")]
        [StringLength(15, ErrorMessage = "Customer mobile number cannot exceed 15 characters.")]
        // Enforces that the string contains only numbers and standard telephone symbols like plus or hyphens
        [RegularExpression(@"^[0-9\+\-\s]+$", ErrorMessage = "Customer mobile number can only contain digits, spaces, plus signs, or hyphens.")]
        
        public string CustomerMobileNumber { get; set; } = null!; 

        
        [Required(ErrorMessage = "Customer email address is mandatory.")]
        [StringLength(100, ErrorMessage = "Customer email address cannot exceed 100 characters.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        public string CustomerEmail { get; set; } = null!;

        [Required(ErrorMessage = "Customer type is mandatory.")]
        [StringLength(45, ErrorMessage = "Customer type cannot exceed 45 characters.")]
        [RegularExpression(@"^[A-Za-z\- ]+$", ErrorMessage = "Customer type must contain only letters, hyphens, or spaces.")]
        public string CustomerType { get; set; } = null!; // Individual / Non-Individual 

        [Required(ErrorMessage = "Customer ID type is mandatory.")]
        [StringLength(26, ErrorMessage = "Customer ID type cannot exceed 26 characters.")]
        [RegularExpression(@"^[A-Za-z\s]+$", ErrorMessage = "Customer ID type must contain only letters and spaces.")]
        public string CustomerIdType { get; set; } = null!; 

        
        [Required(ErrorMessage = "Customer identity number (custNid) is mandatory.")]
        [StringLength(30, ErrorMessage = "Customer identity number cannot exceed 30 characters.")]
        // Enforces ANS: Letters, Numbers, Spaces, Hyphens, and Slashes
        [RegularExpression(@"^[A-Za-z0-9\s\-\/]+$", ErrorMessage = "Identity number can only contain alphanumeric characters, spaces, hyphens, or slashes.")]
        public string CustNid { get; set; } = null!; 

        
        [Required(ErrorMessage = "DDA Reference Number is mandatory.")]
        [StringLength(26, MinimumLength = 3, ErrorMessage = "The ddaReferenceNumber must be between 3 and 26 characters long.")]
        // Enforces [26 ANS]: Permits letters, numbers, spaces, and safe reference punctuation characters
        [RegularExpression(@"^[A-Za-z0-9\s\-\/._]+$", ErrorMessage = "The reference number can only contain alphanumeric characters, spaces, hyphens, slashes, periods, or underscores.")]
        public string DdaReferenceNumber { get; set; } = null!;

        [Required(ErrorMessage = "Commencement date (commencesOn) is mandatory.")]
        // Validates strict dd/MM/yyyy structural pattern (digits and slashes only)
        [RegularExpression(@"^\d{2}/\d{2}/\d{4}$", ErrorMessage = "commencesOn must be a valid date in the format dd/MM/yyyy.")]
        public string CommencesOn { get; set; } = null!; // dd/MM/yyyy 

        [Required(ErrorMessage = "Expiration date (expiresOn) is mandatory.")]
        // Validates the exact dd/MM/yyyy layout pattern
        [RegularExpression(@"^\d{2}/\d{2}/\d{4}$", ErrorMessage = "expiresOn must be a valid date in the format dd/MM/yyyy.")]
        public string ExpiresOn { get; set; } = null!; // dd/MM/yyyy 

        [Required(ErrorMessage = "Minimum amount (minAmount) is mandatory.")]
        // Restricts value to match the numeric bound size constraints
        [Range(0.00, 99999999.99, ErrorMessage = "The minAmount must be a positive value and cannot exceed 10 numeric digits.")]
        public decimal MinAmount { get; set; }


        [Required(ErrorMessage = "Maximum amount (maxAmount) is mandatory.")]
        [Range(0.00, 99999999.99, ErrorMessage = "The maxAmount must be a positive value and cannot exceed 10 numeric digits.")]
        public decimal MaxAmount { get; set; }


        [Required(ErrorMessage = "Payment frequency (paymentFrequency) is mandatory.")]
        [StringLength(30, ErrorMessage = "Payment frequency cannot exceed 30 characters.")]
        public string PaymentFrequency { get; set; } = null!;

        [Required(ErrorMessage = "Amount type (amountType) is mandatory.")]
        [StringLength(10, ErrorMessage = "Amount type cannot exceed 10 characters.")]
        public string AmountType { get; set; } = null!; // Variable / Fixed 

        [Required(ErrorMessage = "User preferred payment method (userPreferPaymentMethod) is mandatory.")]
        [StringLength(15, ErrorMessage = "User preferred payment method cannot exceed 15 characters.")]
        public string UserPreferPaymentMethod { get; set; } = null!; // Bank Account / Credit Card 

        [Required(ErrorMessage = "Customer bank name (customerAccountBankName) is mandatory.")]
        [StringLength(100, ErrorMessage = "Customer bank name cannot exceed 100 characters.")]
        public string CustomerAccountBankName { get; set; } = null!;

        [Required(ErrorMessage = "Customer bank account title is mandatory.")]
        [StringLength(100, ErrorMessage = "Customer bank account title cannot exceed 100 characters.")]
        // Enforces [100 A]: Permits letters and spaces only, restricted to 1-7 words maximum
        [RegularExpression(@"^(?:[A-Za-z]+\s*){1,7}$", ErrorMessage = "Customer bank account title must contain only letters and a maximum of 7 words.")]
        public string? CustomerBankAccountTitle { get; set; }

        [Required(ErrorMessage = "Customer bank account type is mandatory.")]
        [MaxLength(45, ErrorMessage = "Customer bank account type cannot exceed 45 characters.")]
        [RegularExpression(@"^[A-Za-z\s]+$", ErrorMessage = "Customer bank account type must contain only letters and spaces.")]

        public string? CustomerBankAccountType { get; set; }

        [Required(ErrorMessage = "Customer bank account number is required.")]
        [StringLength(23, MinimumLength = 23, ErrorMessage = "The bank account number must be exactly 23 characters long.")]
        [RegularExpression(@"^AE[A-Za-z0-9]{21}$", ErrorMessage = "Invalid account number format. Must start with 'AE' followed by 21 alphanumeric characters.")]
       
        public string? CustomerBankAccountNumber { get; set; }

        [MaxLength(23, ErrorMessage = "Customer credit card number cannot exceed 23 characters.")]
        //[RegularExpression(@"^\d{16}$", ErrorMessage = "Invalid credit card number format. Must be exactly 16 digits.")]

        [StringLength(23, ErrorMessage = "Customer credit card number cannot exceed 23 characters.")]
        // Permits numbers, spaces, and hyphens for front-end formatting flexibility
        [RegularExpression(@"^[0-9\s\-]+$", ErrorMessage = "Customer credit card number must contain only numbers, spaces, or hyphens.")]
        public string? CustomerCreditCardNumber { get; set; }

        
        [MaxLength(100, ErrorMessage = "Credit card holder name cannot exceed 100 characters.")]
        [RegularExpression(@"^(?:\b[A-Za-z]+\b\s*){1,7}$", ErrorMessage = "Credit card holder name must contain only letters and cannot exceed 7 words.")]

       
        public string? CreditCardHolderName { get; set; }

        

    }
}
