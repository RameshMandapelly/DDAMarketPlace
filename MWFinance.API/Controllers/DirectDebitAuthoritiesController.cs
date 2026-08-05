using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MWFinance.API.DTOs;
using MWFinance.Domain.Entities;
using MWFinance.Domain.Interfaces;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;           
using Microsoft.Extensions.Options;                 
using MWFinance.API.Helpers;
using System.Text;





namespace MWFinance.API.Controllers
{
    /// <summary>
    ///  this is main controller for DDA
    /// </summary>
    /// 
    [Authorize]
    [Route("api/v1/merchant/direct-debit-authorities")]
    [ApiController]
    public class DirectDebitAuthoritiesController : ControllerBase
    {

        private readonly IDdaRepository _ddaRepository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly DdaGatewaySettingsHelper _gateway;
        private readonly ILogger<DirectDebitAuthoritiesController> _logger;

        public DirectDebitAuthoritiesController(IDdaRepository ddaRepository, IHttpClientFactory httpClientFactory,IOptions<DdaGatewaySettingsHelper> gateway,ILogger<DirectDebitAuthoritiesController> logger)
        {
            _ddaRepository = ddaRepository;
            _httpClientFactory = httpClientFactory;
            _gateway = gateway.Value; 
             _logger = logger;
        }
        [HttpPost]
        public async Task<IActionResult> CreateDda([FromBody] CreateDdaRequest request)
        {
            try
            {

              _logger.LogInformation("CreateDda REQUEST received: {Request}", JsonSerializer.Serialize(request));

               
                // 1. Mandatory Data Quality Trimming Policy
                string ddaRef = request.DdaReferenceNumber.Trim();

                // 2. Validate Duplicate Unique Customer Reference Restrictions [Merchant Domain Boundary]
                var existingDda = await _ddaRepository.GetByReferenceAsync(ddaRef);
                if (existingDda != null)
                {
                    return BadRequest(new
                    {
                        errors = new
                        {
                            customerReferenceNumber = new[] { $"DD already exists with the customer DDS reference No. {ddaRef}" }
                        }

                    });
                }
                #region CustNid and CustomerIdNumber Validation Logic
                // validation CustNid
                // 1. Ensure custNid and customerIdNumber match or stay synchronized
                if (!string.Equals(request.CustNid?.Trim(), request.CustomerIdNumber?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    // Syncs them so your database mapping remains consistent across both spec fields
                    request.CustNid = request.CustomerIdNumber;
                }

                // 2. Execute cross-property validation based on Customer Type
                if (!string.IsNullOrEmpty(request.CustomerType) && request.CustomerType.Equals("Individual", StringComparison.OrdinalIgnoreCase))
                {
                    // If they explicitly selected an Emirates ID document type, enforce strict 15-digit validation
                    if (!string.IsNullOrEmpty(request.CustomerIdType) && request.CustomerIdType.Equals("UAE Emirates Identity Card", StringComparison.OrdinalIgnoreCase))
                    {
                        string cleanEid = request.CustNid.Replace("-", "").Replace(" ", "").Trim();

                        if (cleanEid.Length != 15 || !cleanEid.All(char.IsDigit) || !cleanEid.StartsWith("784"))
                        {
                            return BadRequest(new
                            {
                                errors = new { custNid = new[] { "When Identity Type is UAE Emirates Identity Card, the number must be a valid 15-digit Emirates ID starting with 784." } }
                            });
                        }
                    }
                    else
                    {
                        // Fallback for other individual document types (Passport, Driving License, etc.)
                        if (string.IsNullOrWhiteSpace(request.CustNid) || request.CustNid.Trim().Length < 3)
                        {
                            return BadRequest(new
                            {
                                errors = new { custNid = new[] { "A valid identification number must be provided for individual accounts." } }
                            });
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(request.CustomerType) && request.CustomerType.Equals("Non-Individual", StringComparison.OrdinalIgnoreCase))
                {
                    // For non-individual/corporate accounts, ensure a valid Trade License / Decree string is provided
                    if (string.IsNullOrWhiteSpace(request.CustNid) || request.CustNid.Trim().Length < 3)
                    {
                        return BadRequest(new
                        {
                            errors = new { custNid = new[] { $"For corporate/non-individual accounts, a valid {request.CustomerIdType ?? "Trade License Number"} must be provided." } }
                        });
                    }
                }
                #endregion

                #region mobileNumber validation


                // 1. Strip out any spaces, hyphens, or plus signs to evaluate the raw digits
                string cleanMobile = request.CustomerMobileNumber.Replace("+", "").Replace("-", "").Replace(" ", "").Trim();

                // 2. Normalize international UAE format (+9715...) down to the required local 05... format
                if (cleanMobile.StartsWith("971"))
                {
                    cleanMobile = "0" + cleanMobile.Substring(3);
                }

                // 3. Strict verification of the 10-digit local format rule (05XXXXXXXX)
                if (cleanMobile.Length != 10 || !cleanMobile.StartsWith("05") || !cleanMobile.All(char.IsDigit))
                {
                    return BadRequest(new
                    {
                        errors = new { customerMobileNumber = new[] { "Customer mobile number must be a valid 10-digit UAE mobile number following the format 05XXXXXXXX." } }
                    });
                }

                // 4. (Optional) Re-assign the cleaned/normalized value back to the request object before saving to the database
                request.CustomerMobileNumber = cleanMobile;

                #endregion

                #region CustamerType Validation
                // Define the absolute allowed banking domain values
                var allowedTypes = new[] { "Individual", "Non-Individual" };

                if (string.IsNullOrEmpty(request.CustomerType) || !allowedTypes.Contains(request.CustomerType, StringComparer.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        errors = new { customerType = new[] { "Customer type must be exactly either 'Individual' or 'Non-Individual'." } }
                    });
                }
                #endregion

                #region CustomerIdType Validation -Individual / Non-Individual
                // 1. Define separate whitelists based on the business rules
                var allowedIndividualIdTypes = new[]
                {
                "Passport",
                "UAE Emirates Identity Card",
                "UAE Driving License Number",
                "UAE Driving License Number",
                "Family Book Number"
            };

                var allowedNonIndividualIdTypes = new[]
                {
                "Trade License Number",
                "Emiree Decree Number",
                "Chamber Certification Number"
            };

                // 2. Perform cross-property validation matrix evaluation
                if (!string.IsNullOrEmpty(request.CustomerType) && request.CustomerType.Equals("Individual", StringComparison.OrdinalIgnoreCase))
                {
                    if (!allowedIndividualIdTypes.Contains(request.CustomerIdType, StringComparer.OrdinalIgnoreCase))
                    {
                        return BadRequest(new
                        {
                            errors = new { customerIdType = new[] { $"For 'Individual' accounts, customerIdType must be one of the following: {string.Join(", ", allowedIndividualIdTypes)}." } }
                        });
                    }
                }
                else if (!string.IsNullOrEmpty(request.CustomerType) && request.CustomerType.Equals("Non-Individual", StringComparison.OrdinalIgnoreCase))
                {
                    if (!allowedNonIndividualIdTypes.Contains(request.CustomerIdType, StringComparer.OrdinalIgnoreCase))
                    {
                        return BadRequest(new
                        {
                            errors = new { customerIdType = new[] { $"For 'Non-Individual' accounts, customerIdType must be one of the following: {string.Join(", ", allowedNonIndividualIdTypes)}." } }
                        });
                    }
                }
                #endregion

                #region CustomerIdNumber Validation
                if (request.CustomerIdType.Equals("UAE Emirates Identity Card", StringComparison.OrdinalIgnoreCase))
                {
                    // 1. Strip out hyphens or spaces to evaluate the raw EID sequence
                    string cleanEid = request.CustomerIdNumber.Replace("-", "").Replace(" ", "").Trim();

                    // 2. Enforce strict 15-digit UAE Central Bank EID validation standard
                    if (cleanEid.Length != 15 || !cleanEid.All(char.IsDigit) || !cleanEid.StartsWith("784"))
                    {
                        return BadRequest(new
                        {
                            errors = new { customerIdNumber = new[] { "When CustomerIdType is 'UAE Emirates Identity Card', the number must be a valid 15-digit format starting with 784." } }
                        });
                    }

                    // 3. Optional: Sync this validated value to your internal tracking property if necessary
                    request.CustNid = request.CustomerIdNumber;
                }
                else if (request.CustomerIdType.Equals("Passport", StringComparison.OrdinalIgnoreCase))
                {
                    // Passports are alphanumeric, typically between 6 to 15 characters long
                    string cleanPassport = request.CustomerIdNumber.Replace(" ", "").Trim();
                    if (cleanPassport.Length < 6)
                    {
                        return BadRequest(new
                        {
                            errors = new { customerIdNumber = new[] { "A valid Passport number must be at least 6 characters long." } }
                        });
                    }
                }
                #endregion

                #region commenceOn Validation
                // 1. Enforce strict parsing using the exact clearinghouse format requirement
                if (!DateTime.TryParseExact(request.CommencesOn, "dd/MM/yyyy",
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.None,
                                            out DateTime parsedCommenceDate))
                {
                    return BadRequest(new
                    {
                        errors = new { commencesOn = new[] { "The date provided is not a valid calendar date or does not match the dd/MM/yyyy format." } }
                    });
                }

                // 2. Strict verification: Must be current or future date (ignoring time components)
                if (parsedCommenceDate.Date < DateTime.Today)
                {
                    return BadRequest(new
                    {
                        errors = new { commencesOn = new[] { "Commencement date cannot be a past date. It must be today's date or a date in the future." } }
                    });
                }

                #endregion
                #region ExpiresOn Validation
                // 1. Enforce strict parsing using the exact format requirement
                if (!DateTime.TryParseExact(request.ExpiresOn, "dd/MM/yyyy",
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.None,
                                            out DateTime parsedExpireDate))
                {
                    return BadRequest(new
                    {
                        errors = new { expiresOn = new[] { "The expiration date provided is not a valid calendar date or does not match the dd/MM/yyyy format." } }
                    });
                }

                // 2. Verification: Must be an absolute future date
                if (parsedExpireDate.Date <= DateTime.Today)
                {
                    return BadRequest(new
                    {
                        errors = new { expiresOn = new[] { "Expiration date must be a date in the future. It cannot be today or a past date." } }
                    });
                }

                // 3. Cross-property verification: Must be after the commencement date
                // (Using 'parsedCommenceDate' from the previous step's TryParseExact output)
                if (parsedExpireDate.Date <= parsedCommenceDate.Date)
                {
                    return BadRequest(new
                    {
                        errors = new { expiresOn = new[] { "The expiration date (expiresOn) must be later than the commencement date (commencesOn)." } }
                    });
                }
                #endregion

                #region Miniumum Amount Validation
                // 1. Enforce strict banking standard: Maximum of 2 decimal places (Fils/Cents)
                if (decimal.Round(request.MinAmount, 2) != request.MinAmount)
                {
                    return BadRequest(new
                    {
                        errors = new { minAmount = new[] { "The minimum amount cannot contain more than 2 decimal places." } }
                    });
                }

                // 2. Structural boundary check to satisfy [10 N] (Value must be less than 10 billion)
                if (request.MinAmount >= 1000000000)
                {
                    return BadRequest(new
                    {
                        errors = new { minAmount = new[] { "The value provided exceeds the maximum allowed length of 10 numeric digits." } }
                    });
                }
                #endregion

                #region maximum Amount Validation
                // 1. Enforce banking standard: Maximum of 2 decimal places (Fils)
                if (decimal.Round(request.MaxAmount, 2) != request.MaxAmount)
                {
                    return BadRequest(new
                    {
                        errors = new { maxAmount = new[] { "The maximum amount cannot contain more than 2 decimal places." } }
                    });
                }

                // 2. Structural boundary check to satisfy [10 N]
                if (request.MaxAmount >= 1000000000)
                {
                    return BadRequest(new
                    {
                        errors = new { maxAmount = new[] { "The value provided exceeds the maximum allowed length of 10 numeric digits." } }
                    });
                }

                // 3. Cross-property verification: maxAmount must be >= minAmount (unless maxAmount is 0.00 for unlimited)
                if (request.MaxAmount > 0.00m && request.MaxAmount < request.MinAmount)
                {
                    return BadRequest(new
                    {
                        errors = new { maxAmount = new[] { "The maximum amount (maxAmount) cannot be less than the minimum amount (minAmount)." } }
                    });
                }
                #endregion

                #region PaymentFrequency Validation
                // Define the absolute allowed banking frequency options
                var allowedFrequencies = new[]
                {
                    "Daily",
                    "Weekly",
                    "Monthly",
                    "Quarterly",
                    "Half-yearly",
                    "Annually",
                    "One Time Only",
                    "Every Two Months",
                    "Every Four Months"
                };

                if (!allowedFrequencies.Contains(request.PaymentFrequency?.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        errors = new { paymentFrequency = new[] { $"Payment frequency must be one of the following exact values: {string.Join(", ", allowedFrequencies)}." } }
                    });
                }
                #endregion

                #region AmountType Validation
                // 1. Define the absolute allowed database enum options
                var allowedAmountTypes = new[] { "Variable", "Fixed" };

                if (!allowedAmountTypes.Contains(request.AmountType?.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        errors = new { amountType = new[] { "Amount type must be exactly either 'Variable' or 'Fixed'." } }
                    });
                }

                // 2. Cross-property Business Logic (Optional but highly recommended for banking apps)
                if (string.Equals(request.AmountType?.Trim(), "Fixed", StringComparison.OrdinalIgnoreCase))
                {
                    // For Fixed mandates, minAmount and maxAmount should match up exactly
                    if (request.MinAmount != request.MaxAmount)
                    {
                        return BadRequest(new
                        {
                            errors = new { amountType = new[] { "When amountType is 'Fixed', the minAmount and maxAmount fields must be equal." } }
                        });
                    }
                }
                #endregion

                #region UserPreferPaymentMethod Validation
                // 1. Define the absolute allowed payment methods
                var allowedPaymentMethods = new[] { "Bank Account", "Credit Card" };

                if (!allowedPaymentMethods.Contains(request.UserPreferPaymentMethod?.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        errors = new { userPreferPaymentMethod = new[] { "User preferred payment method must be exactly either 'Bank Account' or 'Credit Card'." } }
                    });
                }

                // 2. Structural Cross-Validation Gateway (Prepares your system for account/card fields)
                if (string.Equals(request.UserPreferPaymentMethod?.Trim(), "Credit Card", StringComparison.OrdinalIgnoreCase))
                {
                    // Future expansion point: Ensure Credit Card specific fields are present in the request payload
                }
                else if (string.Equals(request.UserPreferPaymentMethod?.Trim(), "Bank Account", StringComparison.OrdinalIgnoreCase))
                {
                    // Future expansion point: Ensure Bank Account / IBAN specific fields are present in the request payload
                }
                #endregion

                #region CustomerAccountBankName Validation
                // 1. Define the official UAE Central Bank Master Lookup array
                var uaeBanksMasterTable = new[]
                {
                                            // id: 1
                            "First Abu Dhabi Bank",
                            // id: 2
                            "Abu Dhabi Commercial Bank",
                            // id: 3
                            "BNP Paribas",
                            // id: 4
                            "Bank of Baroda",
                            // id: 5
                            "Al Masraf",
                            // id: 8
                            "Banque Misr",
                            // id: 9
                            "Commercial Bank of Dubai",
                            // id: 10
                            "HSBC Middle East",
                            // id: 11
                            "Dubai Islamic Bank",
                            // id: 12
                            "Arab African International Bank",
                            // id: 13
                            "Emiratesnbd Bank PJSC",
                            // id: 14
                            "Al Khaliji France S.A.",
                            // id: 15
                            "Emirates Islamic Bank PJSC",
                            // id: 16
                            "Al Ahli Bank Of Kuwait K.S.C.",
                            // id: 17
                            "Mashreqbank PSC",
                            // id: 18
                            "Barclays Bank",
                            // id: 19
                            "Sharjah Islamic Bank",
                            // id: 20
                            "Habib Bank Limited",
                            // id: 21
                            "Bank of Sharjah",
                            // id: 22
                            "Habib Bank AG Zurich",
                            // id: 23
                            "United Arab Bank",
                            // id: 24
                            "Standard Chartered Bank",
                            // id: 25
                            "Investbank PSC",
                            // id: 26
                            "Citibank NA",
                            // id: 27
                            "RAK Bank",
                            // id: 28
                            "Bank Saderat Iran",
                            // id: 29
                            "National Bank of Fujairah",
                            // id: 30
                            "Bank Melli Iran",
                            // id: 31
                            "Arab Emirates Investment Bank",
                            // id: 32
                            "Banque Banorient France",
                            // id: 33
                            "Ajman Bank",
                            // id: 35
                            "Al Hilal Bank",
                            // id: 36
                            "United Bank Ltd.",
                            // id: 38
                            "Doha Bank",
                            // id: 39
                            "Arab Bank",
                            // id: 40
                            "The Saudi National Bank",
                            // id: 41
                            "Abu Dhabi Islamic Bank",
                            // id: 44
                            "Industrial and Commercial Bank of China",
                            // id: 45
                            "National Bank of Umm Al Qaiwain",
                            // id: 47
                            "Commercial Bank International PSC",
                            // id: 48
                            "National Bank of Kuwait",
                            // id: 49
                            "El Nilein Bank",
                            // id: 50
                            "AMEX (Middle East) - B.S.C",
                            // id: 51
                            "Calyon Investment and Corporate Bank",
                            // id: 52
                            "Dubai First PJSC",
                            // id: 54
                            "Emirates Development Bank",
                            // id: 55
                            "Finance House",
                            // id: 57
                            "Finance House LLC",
                            // id: 58
                            "Janata Bank",
                            // id: 59
                            "MAF Finance",
                            // id: 61
                            "National Bank Of Bahrain",
                            // id: 62
                            "National Bank of Oman",
                            // id: 63
                            "Rafidain Bank",
                            // id: 69
                            "BOK International Bank",
                            // id: 70
                            "Samaa Finance PSC",
                            // id: 71
                            "Siraj Finance",
                            // id: 72
                            "Al Ain Finance PJSC",
                            // id: 73
                            "DDS Market Place(DDMP) NBF",
                            // id: 74
                            "Al Maryah Community Bank",
                            // id: 77
                            "Ruya Community Islamic Bank LLC",
                            // id: 80
                            "Wio Bank PJSC",
                            // id: 83
                            "Gulf International Bank",
                            // id: 86
                            "ZAND BANK",
                };

                // 2. Clean input and perform strict Master Table check
                string cleanBankInput = request.CustomerAccountBankName?.Trim();

                if (!uaeBanksMasterTable.Contains(cleanBankInput, StringComparer.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        errors = new { customerAccountBankName = new[] { "The provided bank name is invalid. Please select a valid UAE operational bank from the approved Master Table list." } }
                    });
                }

                // 3. Normalize string casing to match the Master Table entry before database persistence
                var matchedOfficialName = uaeBanksMasterTable.First(b => b.Equals(cleanBankInput, StringComparison.OrdinalIgnoreCase));
                request.CustomerAccountBankName = matchedOfficialName;

                #endregion

                #region CustomerBankAccountTitle Validation
                if (!string.IsNullOrEmpty(request.CustomerBankAccountTitle))
                {
                    // 1. Sanitize common input noise (strip out periods, commas, slashes, and symbols) 
                    // to safeguard the string to fit the clearinghouse [100 A] rules
                    string sanitizedTitle = System.Text.RegularExpressions.Regex.Replace(request.CustomerBankAccountTitle, @"[.,\/#!$%\^&\*;:{}=\-_`~()]", "");

                    // 2. Reduce multiple spaces down to single spaces and trim boundaries
                    sanitizedTitle = System.Text.RegularExpressions.Regex.Replace(sanitizedTitle, @"\s+", " ").Trim();

                    // 3. Re-verify the word count bound limits on the cleaned structure
                    string[] words = sanitizedTitle.Split(' ');
                    if (words.Length > 7)
                    {
                        return BadRequest(new
                        {
                            errors = new { customerBankAccountTitle = new[] { "Customer bank account title cannot contain more than 7 words." } }
                        });
                    }

                    // 4. Update the request object with the normalized, alphabetic-safe string
                    request.CustomerBankAccountTitle = sanitizedTitle;
                }
                #endregion

                #region CustomerBankAccountType Validation
                // 1. Define the absolute allowed banking domain types
                var allowedAccountTypes = new[] { "Current", "Savings" };

                string cleanAccountTypeInput = request.CustomerBankAccountType?.Trim();

                if (!allowedAccountTypes.Contains(cleanAccountTypeInput, StringComparer.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        errors = new { customerBankAccountType = new[] { "Customer bank account type must be exactly either 'Current' or 'Savings'." } }
                    });
                }

                // 2. Normalize string casing to match the strict schema format before internal routing
                request.CustomerBankAccountType = allowedAccountTypes.First(t => t.Equals(cleanAccountTypeInput, StringComparison.OrdinalIgnoreCase));
                #endregion

                #region CustomerBankAccountNumber Validation
                if (!string.IsNullOrEmpty(request.CustomerBankAccountNumber))
                {
                    // 1. Strip out any common layout formatting spaces or hyphens
                    string cleanAccountNumber = request.CustomerBankAccountNumber.Replace(" ", "").Replace("-", "").Trim();

                    // 2. Normalize to uppercase to guarantee compatibility with Central Bank routing files
                    cleanAccountNumber = cleanAccountNumber.ToUpperInvariant();

                    // 3. Structural Evaluation Check: Confirm final length is exactly 23 characters
                    if (cleanAccountNumber.Length != 23 || !cleanAccountNumber.StartsWith("AE"))
                    {
                        return BadRequest(new
                        {
                            errors = new { customerBankAccountNumber = new[] { "The account number layout is invalid. Ensure it is a valid 23-character UAE IBAN starting with 'AE'." } }
                        });
                    }

                    // 4. Update the request property with the perfectly formatted, clean string
                    request.CustomerBankAccountNumber = cleanAccountNumber;
                }
                #endregion

                #region  CustomerCreditCardNumber Validation
                // 1. Cross-property Evaluation: If preference is Credit Card, this field is Mandatory
                if (!string.IsNullOrEmpty(request.UserPreferPaymentMethod) &&
                    request.UserPreferPaymentMethod.Equals("Credit Card", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(request.CustomerCreditCardNumber))
                    {
                        return BadRequest(new
                        {
                            errors = new { customerCreditCardNumber = new[] { "Customer credit card number is mandatory when the preferred payment method is 'Credit Card'." } }
                        });
                    }

                    // 2. Strip formatting layout noise (spaces, hyphens) to isolate raw card digits
                    string cleanCardNumber = request.CustomerCreditCardNumber.Replace(" ", "").Replace("-", "").Trim();

                    // 3. Enforce the exact 16-digit core length requirement
                    if (cleanCardNumber.Length != 16 || !cleanCardNumber.All(char.IsDigit))
                    {
                        return BadRequest(new
                        {
                            errors = new { customerCreditCardNumber = new[] { "The credit card number must be a valid 16-digit numeric sequence." } }
                        });
                    }

                    // 4. Run the Luhn Algorithm (Mod 10 Check) to catch structural typing errors
                    if (!IsLuhnValid(cleanCardNumber))
                    {
                        return BadRequest(new
                        {
                            errors = new { customerCreditCardNumber = new[] { "The credit card number provided fails the checksum validation check." } }
                        });
                    }

                    // 5. Update the request property with the clean 16-digit string
                    request.CustomerCreditCardNumber = cleanCardNumber;
                }
                else
                {
                    // Optional: If payment method is Bank Account, clean/ignore card numbers passed in the payload
                    request.CustomerCreditCardNumber = string.Empty;
                }
                #endregion

                #region CreditCardHolderName Validtation
                // 1. Cross-property Evaluation: Mandatory ONLY when preference is Credit Card
                if (!string.IsNullOrEmpty(request.UserPreferPaymentMethod) &&
                    request.UserPreferPaymentMethod.Equals("Credit Card", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(request.CreditCardHolderName))
                    {
                        return BadRequest(new
                        {
                            errors = new { creditCardHolderName = new[] { "Credit card holder name is mandatory when the preferred payment method is 'Credit Card'." } }
                        });
                    }

                    // 2. Sanitize common input noise (strip out periods, commas, symbols) to keep the layout [100 A] safe
                    string sanitizedHolderName = System.Text.RegularExpressions.Regex.Replace(request.CreditCardHolderName, @"[.,\/#!$%\^&\*;:{}=\-_`~()]", "");

                    // 3. Collapse multiple consecutive spaces down to a single space and trim boundaries
                    sanitizedHolderName = System.Text.RegularExpressions.Regex.Replace(sanitizedHolderName, @"\s+", " ").Trim();

                    // 4. Structural Evaluation: Verify word count limits
                    string[] cardNameWords = sanitizedHolderName.Split(' ');
                    if (cardNameWords.Length > 7)
                    {
                        return BadRequest(new
                        {
                            errors = new { creditCardHolderName = new[] { "Credit card holder name cannot contain more than 7 words." } }
                        });
                    }

                    // 5. Update the request object with the normalized string format
                    request.CreditCardHolderName = sanitizedHolderName;
                }
                else
                {
                    // If payment method is Bank Account, clear out card details to maintain clean data hygiene
                    request.CreditCardHolderName = string.Empty;
                }
                #endregion

                // Document Specification Limit Validations
                if (request.MinAmount > request.MaxAmount)
                {
                    return BadRequest(new
                    {
                        errors = new { minAmount = new[] { "min Amount should be less than max Amount" } }

                    });
                }

                // Word-count Boundary Verification Patterns
                if (request.CustomerFullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 7)
                {
                    return BadRequest(new { errors = new { customerFullName = new[] { "Customer name must contain a maximum of 7 words only." } } });
                }

                // Parse Regional Specific Date Structures (dd/MM/yyyy) Safely
                if (!DateTime.TryParseExact(request.CommencesOn, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime startDate) ||
                    !DateTime.TryParseExact(request.ExpiresOn, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime endDate))
                {
                    return BadRequest(new { errors = new { dates = new[] { "Dates must perfectly utilize standard dd/MM/yyyy formatting arrays." } } });
                }


                 // ── Call the real UAE DDA Marketplace gateway to create the DDA ──────────
                var client = _httpClientFactory.CreateClient();
                string targetGatewayUrl = $"{_gateway.BaseUrl}/v1/merchant/direct-debit-authorities";

                _logger.LogInformation("CreateDda → calling gateway {Url} with reference {DdaRef}", targetGatewayUrl, ddaRef);

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var gatewayRequest = new HttpRequestMessage(HttpMethod.Post, targetGatewayUrl)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(request, jsonOptions),
                        Encoding.UTF8,
                        "application/json")
                };

                gatewayRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                var gatewayResponse = await client.SendAsync(gatewayRequest);
                string gatewayResultString = await gatewayResponse.Content.ReadAsStringAsync();

                _logger.LogInformation("CreateDda ← gateway responded {StatusCode}: {Body}",
                    (int)gatewayResponse.StatusCode, gatewayResultString);

                // ── If the gateway rejected the request, bubble its exact response back ──
                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        var gatewayError = JsonSerializer.Deserialize<object>(gatewayResultString);
                        return StatusCode((int)gatewayResponse.StatusCode, gatewayError);
                    }
                    catch
                    {
                        return StatusCode((int)gatewayResponse.StatusCode, new
                        {
                            message = gatewayResultString,
                            status = (int)gatewayResponse.StatusCode
                        });
                    }
                }

                // ── Parse the real ddaId / ddarId out of the gateway's success response ──
                using var gatewayJson = JsonDocument.Parse(gatewayResultString);
                var root = gatewayJson.RootElement;

                int gatewayDdaId = root.TryGetProperty("ddaId", out var ddaIdProp) ? ddaIdProp.GetInt32() : 0;
                int gatewayDdarId = root.TryGetProperty("ddarId", out var ddarIdProp) ? ddarIdProp.GetInt32() : 0;

                // Map verified DTO payloads safely to Domain Layer Entities
                var entity = new DirectDebitAuthority
                {
                    CustomerIdNumber = request.CustomerIdNumber.Trim(),
                    CustomerFullName = request.CustomerFullName.Trim(),
                    CustomerMobileNumber = request.CustomerMobileNumber.Trim(),
                    CustomerEmail = request.CustomerEmail.Trim(),
                    CustomerType = request.CustomerType.Trim(),
                    CustomerIdType = request.CustomerIdType.Trim(),
                    CustNid = request.CustNid.Trim(),
                    DdaReferenceNumber = ddaRef,
                    CommencesOn = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
                    ExpiresOn = DateTime.SpecifyKind(endDate, DateTimeKind.Utc),
                    MinAmount = request.MinAmount,
                    MaxAmount = request.MaxAmount,
                    PaymentFrequency = request.PaymentFrequency,
                    AmountType = request.AmountType,
                    UserPreferPaymentMethod = request.UserPreferPaymentMethod,
                    CustomerAccountBankName = request.CustomerAccountBankName,
                    CustomerBankAccountTitle = request.CustomerBankAccountTitle?.Trim(),
                    CustomerBankAccountType = request.CustomerBankAccountType?.Trim(),
                    CustomerBankAccountNumber = request.CustomerBankAccountNumber?.Trim(),
                    CustomerCreditCardNumber = request.CustomerCreditCardNumber?.Trim(),
                    CreditCardHolderName = request.CreditCardHolderName?.Trim(),

                    DdarId = gatewayDdarId,
                    DdaId = gatewayDdaId,
                    DdaStatus = "PNDG"
                };

                await _ddaRepository.AddAsync(entity);
                await _ddaRepository.SaveChangesAsync();

                _logger.LogInformation("CreateDda DTO payloads: {Entity}", JsonSerializer.Serialize(entity));
                _logger.LogInformation("CreateDda StatusCode DdaId: {DdaId}", entity.DdaId);
                _logger.LogInformation("CreateDda StatusCode DdarId: {DdarId}", entity.DdarId);

                return StatusCode((int)gatewayResponse.StatusCode, new
                {
                    ddaId = entity.DdaId,
                    ddarId = entity.DdarId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateDda FAILED to reach gateway for reference {DdaRef}", request.DdaReferenceNumber);
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "Failed to communicate with the UAE DDA Marketplace gateway.",
                    details = ex.Message
                });
            }
        }
            // catch (Exception ex)
            // {
            //     _logger.LogError(ex, "CreateDda FAILED unexpectedly for reference {DdaRef}", request.DdaReferenceNumber);
            //     throw;
            // }
        //}

        /// <summary>
        /// credit card number validation using Luhn algorithm (Mod 10 check) to ensure the structural integrity of the card number sequence
        /// </summary>
        /// <param name="cardNumber"></param>
        /// <returns></returns>
        [NonAction]
        private bool IsLuhnValid(string cardNumber)
        {
            int sum = 0;
            bool alternate = false;
            for (int i = cardNumber.Length - 1; i >= 0; i--)
            {
                int nx = int.Parse(cardNumber[i].ToString());
                if (alternate)
                {
                    nx *= 2;
                    if (nx > 9) nx -= 9;
                }
                sum += nx;
                alternate = !alternate;
            }
            return (sum % 10 == 0);
        }

        [HttpGet("{ddaId}/unsigned-pdf")]
        public async Task<IActionResult> GetUnsignedPdf([FromRoute] string ddaId)
        {
            // 1. Optional: Ensure the record actually exists or is tracked locally first
            // (If you want to bypass local DB validation during development testing, you can comment this block out)
            if (int.TryParse(ddaId, out int numericId))
            {
                var localRecord = await _ddaRepository.GetByReferenceAsync(ddaId); // or by Id if you implemented GetByIdAsync
                                                                                   // Add logging or authorization telemetry here if required by bank policy
            }

            // 2. Instantiate a safe client instance
            var client = _httpClientFactory.CreateClient();

            // 3. Define the official UAEDDS Gateway Endpoint
            // Sandbox Base URL from BRD: https://test.directdebit.ae/api
            string targetGatewayUrl = $"{_gateway.BaseUrl}/v1/merchant/direct-debit-authorities/{ddaId}/unsigned-pdf";

          _logger.LogInformation("GetUnsignedPdf Url: {targetGatewayUrl}", JsonSerializer.Serialize(targetGatewayUrl));
            try
            {
                // 4. Set up the outbound request message
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Get, targetGatewayUrl);

                // 5. Append basic authentication credentials (Required by UAEDDS)            
                gatewayRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                // 6. Forward the call to UAEDDS
                var gatewayResponse = await client.SendAsync(gatewayRequest, HttpCompletionOption.ResponseHeadersRead);
                _logger.LogInformation("GetUnsignedPdf ← gateway responded {StatusCode} for DdaId={DdaId}",
                 (int)gatewayResponse.StatusCode, ddaId);
                // 7. If the clearinghouse errors out (e.g., 404 Mandate Not Found, 401 Unauthorized), bubble up their response
                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    string gatewayError = await gatewayResponse.Content.ReadAsStringAsync();
                    _logger.LogWarning("GetUnsignedPdf REJECTED {StatusCode} for DdaId={DdaId}: {Body}",
                                           (int)gatewayResponse.StatusCode, ddaId, gatewayError);

                    try
                    {
                        // Parse and pass through the exact gateway error shape
                        // Gateway returns: { "message": "Resource not found with id : 581908", "status": 404 }
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var errorPayload = JsonSerializer.Deserialize<JsonElement>(gatewayError, options);
                        return StatusCode((int)gatewayResponse.StatusCode, errorPayload);
                    }
                    catch (JsonException)
                    {
                        // Fallback: gateway returned non-JSON error body
                        return StatusCode((int)gatewayResponse.StatusCode, new
                        {
                            message = gatewayError,
                            status = (int)gatewayResponse.StatusCode
                            
                        });
                    }
                }

                // 8. Stream the actual PDF bytes directly back to Postman
                var pdfStream = await gatewayResponse.Content.ReadAsStreamAsync();
                _logger.LogInformation("GetUnsignedPdf RESPONSE 200: PDF stream returned for DdaId={DdaId}", ddaId);

                // Pass the exact clearinghouse content-type headers straight through
                return File(pdfStream, "application/pdf", $"Unsigned_Mandate_{ddaId}.pdf");
            }
            catch (HttpRequestException ex)
            {

                _logger.LogError(ex, "GetUnsignedPdf FAILED to reach gateway for DdaId={DdaId}", ddaId);

                // Handle unexpected physical office network drops or corporate firewall blocks
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "Failed to communicate with the external UAEDDS clearinghouse network server.",
                    details = ex.Message
                });
            }
        }


        [HttpGet("{ddaId}/signed-pdf")]
        public async Task<IActionResult> GetSignedPdf([FromRoute] string ddaId)
        {
            // 1. Optional: Ensure the record actually exists or is tracked locally first
            // (If you want to bypass local DB validation during development testing, you can comment this block out)
            if (int.TryParse(ddaId, out int numericId))
            {
                var localRecord = await _ddaRepository.GetByReferenceAsync(ddaId); // or by Id if you implemented GetByIdAsync
                                                                                   // Add logging or authorization telemetry here if required by bank policy
            }

            // 2. Instantiate a safe client instance
            var client = _httpClientFactory.CreateClient();

            // 3. Define the official UAEDDS Gateway Endpoint
            // Sandbox Base URL from BRD: https://test.directdebit.ae/api
            string targetGatewayUrl = $"{_gateway.BaseUrl}/v1/merchant/direct-debit-authorities/{ddaId}/signed-pdf";

            _logger.LogInformation("GetSignedPdf REQUEST received: DdaId={DdaId}, Url={Url}", ddaId, targetGatewayUrl);

            try
            {
                // 4. Set up the outbound request message
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Get, targetGatewayUrl);

                // 5. Append basic authentication credentials (Required by UAEDDS)
               
                gatewayRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic",_gateway.GetBasicAuthToken());

                // 6. Forward the call to UAEDDS
                var gatewayResponse = await client.SendAsync(gatewayRequest, HttpCompletionOption.ResponseHeadersRead);
                _logger.LogInformation("GetSignedPdf ← gateway responded {StatusCode} for DdaId={DdaId}",
         (int)gatewayResponse.StatusCode, ddaId);
                // 7. If the clearinghouse errors out (e.g., 404 Mandate Not Found, 401 Unauthorized), bubble up their response

                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    string gatewayError = await gatewayResponse.Content.ReadAsStringAsync();
                    _logger.LogWarning("GetSignedPdf REJECTED {StatusCode} for DdaId={DdaId}: {Body}",
                                         (int)gatewayResponse.StatusCode, ddaId, gatewayError);
                    try
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var errorPayload = JsonSerializer.Deserialize<JsonElement>(gatewayError, options);
                        return StatusCode((int)gatewayResponse.StatusCode, errorPayload);
                    }
                    catch (JsonException)
                    {
                        return StatusCode((int)gatewayResponse.StatusCode, new
                        {
                            message = gatewayError,
                            status = (int)gatewayResponse.StatusCode
                        });
                    }
                }

                // 8. Stream the actual PDF bytes directly back to Postman
                var pdfStream = await gatewayResponse.Content.ReadAsStreamAsync();
                _logger.LogInformation("GetSignedPdf RESPONSE 200: PDF stream returned for DdaId={DdaId}", ddaId);

                // Pass the exact clearinghouse content-type headers straight through
                return File(pdfStream, "application/pdf", $"signed_Mandate_{ddaId}.pdf");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "GetSignedPdf FAILED to reach gateway for DdaId={DdaId}", ddaId);

                // Handle unexpected physical office network drops or corporate firewall blocks
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "you can not access",
                    details = ex.StatusCode.ToString()
                });
            }
        }


        [HttpPost("{ddaId}/signed-pdf")]
        [Consumes("multipart/form-data")] // Explicitly tells Swagger/API that this handles form uploads
        public async Task<IActionResult> UploadSignedPdf([FromRoute] string ddaId, [FromForm] UploadSignedPdfRequest request)

        {
            // 1. Structural Request Validation Checks
            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new
                {
                    errors = new { file = new[] { "A valid signed PDF file payload must be provided under the 'file' parameter key." } }
                });
            }

            if (!request.File.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    errors = new { file = new[] { "Invalid file formatting type. Only 'application/pdf' files are accepted by the UAEDDS gateway." } }
                });
            }

            // 2. Instantiate a resilient HTTP Client from the registered Factory
            var client = _httpClientFactory.CreateClient();

            // 3. Define the Target Clearinghouse Sandbox Gateway Endpoint Route
            string targetGatewayUrl = $"{_gateway.BaseUrl}/v1/merchant/direct-debit-authorities/{ddaId}/signed-pdf";
            _logger.LogInformation("UploadSignedPdf REQUEST received: DdaId={DdaId}, FileName={FileName}, FileSize={FileSize}",
                           ddaId, request.File?.FileName, request.File?.Length);
            try
            {
                // 4. Construct the Outbound Multipart Form Data Payload Container
                using var multipartContent = new MultipartFormDataContent();

                // 5. Read the incoming local file stream and convert it into a StreamContent object
                using var fileStream = request.File.OpenReadStream();
                var fileContent = new StreamContent(fileStream);

                // Pass the identical content-type header context through to the central bank gateway
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

                // VERY IMPORTANT: The field parameter name here MUST match exactly what the clearinghouse expects ("file")
                multipartContent.Add(fileContent, "file", request.File.FileName);

                // 6. Instantiate the HTTP POST request wrapper
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Post, targetGatewayUrl)
                {
                    Content = multipartContent
                };

                // 7. Inject Merchant Basic Authentication Tokens               
                gatewayRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                // 8. Execute the external API transmission call
                var gatewayResponse = await client.SendAsync(gatewayRequest);
                _logger.LogInformation("UploadSignedPdf ← gateway responded {StatusCode} for DdaId={DdaId}",
                                 (int)gatewayResponse.StatusCode, ddaId);
                // 9. Read the string response message payload returned from the gateway
                string gatewayResultString = await gatewayResponse.Content.ReadAsStringAsync();

                // 10. If the clearinghouse errors out, parse and return the gateway's exact response
                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("UploadSignedPdf REJECTED {StatusCode} for DdaId={DdaId}: {Body}",
                        (int)gatewayResponse.StatusCode, ddaId, gatewayResultString);
                    // Try to parse the gateway's JSON response directly
                    try
                    {
                        var gatewayError = System.Text.Json.JsonSerializer.Deserialize<object>(gatewayResultString);
                        return StatusCode((int)gatewayResponse.StatusCode, gatewayError);
                    }
                    catch
                    {
                        // Fallback if gateway returns non-JSON
                        return StatusCode((int)gatewayResponse.StatusCode, new
                        {
                            message = gatewayResultString,
                            status = (int)gatewayResponse.StatusCode
                        });
                    }
                }
                // 11. Success Route Check: Update local repository state if tracked locally
                var localRecord = await _ddaRepository.GetByReferenceAsync(ddaId);
                if (localRecord != null)
                {
                    localRecord.DdaStatus = "ACTV"; // Update status tracking metric to Active/Signed
                    await _ddaRepository.SaveChangesAsync();
                }
                _logger.LogInformation("UploadSignedPdf RESPONSE 200: DdaId={DdaId} marked ACTV", ddaId);
                // 12. Return a clean OK response along with the tracking response metadata
                return Ok(new
                {
                    message = "Signed PDF Mandate document successfully forwarded and registered inside UAEDDS gateway.",
                    ddaId = ddaId,
                    gatewayPayloadResponse = string.IsNullOrEmpty(gatewayResultString) ? null : System.Text.Json.JsonSerializer.Deserialize<object>(gatewayResultString)
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "UploadSignedPdf FAILED to reach gateway for DdaId={DdaId}", ddaId);
                // Catches corporate firewall routing drops or restricted corporate office network connection faults
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "Failed to communicate with the external UAEDDS clearinghouse network server.",
                    details = ex.Message
                });
            }
        }

        [HttpGet("{ddaId}/status")]
        public async Task<IActionResult> GetDdaStatus([FromRoute] string ddaId)
        {
            var client = _httpClientFactory.CreateClient();
            string targetGatewayUrl = $"{_gateway.BaseUrl}/v1/merchant/direct-debit-authorities/{ddaId}/status";
            _logger.LogInformation("GetDdaStatus REQUEST received: DdaId={DdaId}", ddaId);
            try
            {
                // 1. Prepare proxy request headers
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Get, targetGatewayUrl);
               
                gatewayRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                // 2. Fetch data from UAEDDS Clearinghouse Gateway
                var gatewayResponse = await client.SendAsync(gatewayRequest);
                string gatewayResultString = await gatewayResponse.Content.ReadAsStringAsync();
                _logger.LogInformation("GetDdaStatus ← gateway responded {StatusCode} for DdaId={DdaId}",
                                    (int)gatewayResponse.StatusCode, ddaId);
                // 3. Resolve your local Database tracking parameters (Internal Int ID and Ref Number)
                var localRecord = await _ddaRepository.GetByIdentifierStringAsync(ddaId);
                int finalInternalId = localRecord?.Id ?? (int.TryParse(ddaId, out int parsedId) ? parsedId : 0);
                string finalRefNo = localRecord?.DdaReferenceNumber ?? ddaId;

                // --------------------------------------------------------------------------
                // CASE 3: Handle Gateway Error Responses (ERR Condition)
                // --------------------------------------------------------------------------
                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GetDdaStatus REJECTED {StatusCode} for DdaId={DdaId}: {Body}",
                        (int)gatewayResponse.StatusCode, ddaId, gatewayResultString);
                    try
                    {
                        var gatewayError = System.Text.Json.JsonSerializer.Deserialize<object>(gatewayResultString);
                        return StatusCode((int)gatewayResponse.StatusCode, gatewayError);
                    }
                    catch
                    {
                        return StatusCode((int)gatewayResponse.StatusCode, new
                        {
                            message = gatewayResultString,
                            status = (int)gatewayResponse.StatusCode
                        });
                    }
                }

                // --------------------------------------------------------------------------
                // Parse Successful Gateway Content
                // --------------------------------------------------------------------------
                var rawGatewayData = System.Text.Json.JsonSerializer.Deserialize<UaeDdsGatewayRawResponse>(gatewayResultString, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (rawGatewayData == null)
                {
                    return StatusCode(StatusCodes.Status502BadGateway, new { message = "Received empty body format from central processing network." });
                }

                // Convert the dynamic object safely to an uppercase string layout wrapper
                string activeState = rawGatewayData.status?.ToString()?.ToUpper().Trim() ?? "PNDG";

                // 4. Sync updates back into your local corporate database context
                if (localRecord != null)
                {
                    localRecord.DdaStatus = activeState;
                    if (!string.IsNullOrWhiteSpace(rawGatewayData.cbDdaRefNo))
                    {
                        localRecord.CentralBankRefNumber = rawGatewayData.cbDdaRefNo;
                    }
                    await _ddaRepository.SaveChangesAsync();
                }

                // 5. Initialize the clean core outbound payload response container
                var mappedResult = new MerchantDdaStatusResponse
                {
                    id = finalInternalId,
                    status = activeState,
                    ddaRefNo = localRecord?.DdaReferenceNumber ?? rawGatewayData.ddaRefNo ?? finalRefNo
                };

                // --------------------------------------------------------------------------
                // CRITICAL LIFE-CYCLE STATE CONFIGURATION ENGINE
                // --------------------------------------------------------------------------
                switch (activeState)
                {
                    // CASE 4: Accepted / Completed Mandate Sequence state
                    case "ACCP":
                    case "ACTV":
                        mappedResult.status = "ACCP"; // Enforce strict naming specification rule
                        mappedResult.cbDdaRefNo = rawGatewayData.cbDdaRefNo ?? localRecord?.CentralBankRefNumber;
                        break;

                    // CASE 2: Rejected Mandate Processing tracking sequence
                    case "RJCT":
                        mappedResult.reasonCode = !string.IsNullOrWhiteSpace(rawGatewayData.reasonCode) ? rawGatewayData.reasonCode : "RR05";
                        break;

                    // CASE 1: Standard Lifecycle queues (PNDG / SUBP / APRP / CANC / RQXP)
                    default:
                        // No actions needed. reasonCode, cbDdaRefNo, and errors will automatically be omitted since they are null!
                        break;
                }
                _logger.LogInformation("GetDdaStatus RESPONSE 200: DdaId={DdaId}, Status={Status}", ddaId, mappedResult.status);
                return Ok(mappedResult);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "GetDdaStatus FAILED to reach gateway for DdaId={DdaId}", ddaId);
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "Corporate communications drop to external network infrastructure context.", details = ex.Message });
            }
        }

        [HttpPost("{ddaId}/discard")]
        public async Task<IActionResult> DiscardDirectDebitAuthority([FromRoute] string ddaId)
        {
            var client = _httpClientFactory.CreateClient();
            string targetGatewayUrl = $"{_gateway.BaseUrl} /v1/merchant/direct-debit-authorities/{ddaId}/discard";
            _logger.LogInformation("DiscardDirectDebitAuthority REQUEST received: DdaId={DdaId}", ddaId);
            try
            {
                // 1. Build request and attach Sandbox Basic Authentication tokens
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Post, targetGatewayUrl);
               
                gatewayRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                // 2. Transmit the discard command to the UAEDDS terminal network
                var gatewayResponse = await client.SendAsync(gatewayRequest);
                string gatewayResultString = await gatewayResponse.Content.ReadAsStringAsync();
                _logger.LogInformation("DiscardDirectDebitAuthority ← gateway responded {StatusCode} for DdaId={DdaId}",
                                    (int)gatewayResponse.StatusCode, ddaId);
                // --------------------------------------------------------------------------
                // CONDITION A: Handle Gateway Failures / Error Responses
                // --------------------------------------------------------------------------
                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("DiscardDirectDebitAuthority REJECTED {StatusCode} for DdaId={DdaId}", (int)gatewayResponse.StatusCode, ddaId);
                    var errorPayload = new MerchantDdaDiscardResponse
                    {
                        // Capture the numeric status code directly from the HTTP channel layer
                        status = (int)gatewayResponse.StatusCode,
                        message = "Direct Debit Authority is already discarded." // Fallback default text
                    };

                    try
                    {
                        // Try reading the message text directly from the gateway's response json
                        using var jsonDoc = System.Text.Json.JsonDocument.Parse(gatewayResultString);
                        if (jsonDoc.RootElement.TryGetProperty("message", out var msgProp))
                        {
                            errorPayload.message = msgProp.GetString() ?? errorPayload.message;
                        }
                    }
                    catch
                    {
                        // If it isn't JSON, use raw response text or clean message
                        if (!string.IsNullOrWhiteSpace(gatewayResultString) && gatewayResultString.Length < 150)
                        {
                            errorPayload.message = gatewayResultString;
                        }
                    }
                    _logger.LogInformation("DiscardDirectDebitAuthority RESPONSE 200: DdaId={DdaId} marked CANC", ddaId);
                    // Return the matching 4xx/5xx status containing your explicit target layout keys
                    return StatusCode((int)gatewayResponse.StatusCode, errorPayload);
                }

                // --------------------------------------------------------------------------
                // CONDITION B: Handle Successful Discard Operations
                // --------------------------------------------------------------------------
                var successPayload = new MerchantDdaDiscardResponse
                {
                    message = "Direct Debit Authority has been successfully discarded."
                    // status remains null, so JsonIgnore drops it from the output entirely!
                };

                try
                {
                    // Parse actual success wording if you prefer to forward the clearinghouse's text directly
                    using var jsonDoc = System.Text.Json.JsonDocument.Parse(gatewayResultString);
                    if (jsonDoc.RootElement.TryGetProperty("message", out var msgProp))
                    {
                        successPayload.message = msgProp.GetString() ?? successPayload.message;
                    }
                }
                catch(Exception ex ) { _logger.LogError(ex, "DiscardDirectDebitAuthority FAILED to reach gateway for DdaId={DdaId}", ddaId);/* Keep default hardcoded message if response formatting deviates */ }

                // 3. Database State Sync: Flag the item as fully cancelled inside your SQL Server tables
                var localRecord = await _ddaRepository.GetByIdentifierStringAsync(ddaId);
                if (localRecord != null)
                {
                    localRecord.DdaStatus = "CANC";
                    await _ddaRepository.SaveChangesAsync();
                }

                // Returns clean 200 OK wrapper containing ONLY the message key
                return Ok(successPayload);
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new MerchantDdaDiscardResponse
                {
                    status = 502,
                    message = $"Office network communication drop out: {ex.Message}"
                });
            }
        }

        
        
        [HttpPost("{ddaId}/cancel")]
        public async Task<IActionResult> CancelDirectDebitAuthority([FromRoute] string ddaId, [FromBody] MerchantDdaCancelRequest merchantRequest)
        {
            var client = _httpClientFactory.CreateClient();
            string targetGatewayUrl = $"{_gateway.BaseUrl}/v1/merchant/direct-debit-authorities/{ddaId}/cancel";
            _logger.LogInformation("CancelDirectDebitAuthority REQUEST received: DdaId={DdaId}, Payload={Payload}",ddaId, System.Text.Json.JsonSerializer.Serialize(merchantRequest));
            try
            {
                var gatewayPayloadString = System.Text.Json.JsonSerializer.Serialize(merchantRequest);
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Post, targetGatewayUrl)
                {
                    Content = new StringContent(gatewayPayloadString, System.Text.Encoding.UTF8, "application/json")
                };

                // --- CRITICAL FIX: FORCE GATEWAY TO RETURN JSON INSTEAD OF HTML ---
                gatewayRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

              
                gatewayRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                var gatewayResponse = await client.SendAsync(gatewayRequest);
                string gatewayResultString = await gatewayResponse.Content.ReadAsStringAsync();
                _logger.LogInformation("CancelDirectDebitAuthority ← gateway responded {StatusCode} for DdaId={DdaId}",
                                    (int)gatewayResponse.StatusCode, ddaId);
                // --------------------------------------------------------------------------
                // ERROR CONDITION ENGINE: Completely Dynamic - No Hardcoded Messages
                // --------------------------------------------------------------------------
                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("CancelDirectDebitAuthority REJECTED {StatusCode} for DdaId={DdaId}", (int)gatewayResponse.StatusCode, ddaId);
                    var errorPayload = new MerchantDdaCancelResponse
                    {
                        status = (int)gatewayResponse.StatusCode,
                        message = "An unexpected error occurred." // Only a structural default fallback
                    };

                    if (!string.IsNullOrWhiteSpace(gatewayResultString))
                    {
                        string trimmedResponse = gatewayResultString.Trim();

                        // CASE 1: Response is valid JSON from the gateway
                        if (trimmedResponse.StartsWith("{"))
                        {
                            try
                            {
                                var rawErrorObj = System.Text.Json.JsonSerializer.Deserialize<UaeDdsGatewayCancelRawResponse>(trimmedResponse, new System.Text.Json.JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true
                                });

                                if (rawErrorObj != null && !string.IsNullOrWhiteSpace(rawErrorObj.message))
                                {
                                    errorPayload.message = rawErrorObj.message;
                                }
                            }
                            catch
                            {
                                errorPayload.message = $"Gateway Error: {gatewayResponse.ReasonPhrase}";
                            }
                        }
                        // CASE 2: Response is HTML (like an Nginx 404 page)
                        else if (trimmedResponse.StartsWith("<") || gatewayResponse.Content.Headers.ContentType?.MediaType == "text/html")
                        {
                            try
                            {
                                // Dynamically extract the text message inside the HTML <pre> or <h1> or <body> tags
                                // Look for a heading first, then fall back to body text or reason phrase
                                string extractedText = string.Empty;

                                if (trimmedResponse.Contains("<h1>"))
                                {
                                    int start = trimmedResponse.IndexOf("<h1>") + 4;
                                    int end = trimmedResponse.IndexOf("</h1>");
                                    if (end > start) extractedText = trimmedResponse.Substring(start, end - start);
                                }
                                else if (trimmedResponse.Contains("<title>"))
                                {
                                    int start = trimmedResponse.IndexOf("<title>") + 7;
                                    int end = trimmedResponse.IndexOf("</title>");
                                    if (end > start) extractedText = trimmedResponse.Substring(start, end - start);
                                }

                                // Strip out any remaining HTML bracket noise to get pure text safely
                                if (!string.IsNullOrWhiteSpace(extractedText))
                                {
                                    errorPayload.message = System.Text.RegularExpressions.Regex.Replace(extractedText, "<.*?>", string.Empty).Trim() + " agenest id: " + ddaId;
                                }
                                else
                                {
                                    // Fallback to the HTTP reason phrase or status description if HTML is empty
                                    errorPayload.message = !string.IsNullOrWhiteSpace(gatewayResponse.ReasonPhrase)
                                        ? gatewayResponse.ReasonPhrase
                                        : $"Remote server returned an error with status code {(int)gatewayResponse.StatusCode}.";
                                }
                            }
                            catch
                            {
                                errorPayload.message = $"Remote error code {(int)gatewayResponse.StatusCode} encountered.";
                            }
                        }
                        // CASE 3: Flat text message
                        else
                        {
                            if (trimmedResponse.Length < 200)
                            {
                                errorPayload.message = trimmedResponse;
                            }
                        }
                    }
                    else
                    {
                        // If the body string is completely empty, dynamically fall back on the standard HTTP protocol phrase
                        errorPayload.message = !string.IsNullOrWhiteSpace(gatewayResponse.ReasonPhrase)
                            ? gatewayResponse.ReasonPhrase
                            : $"HTTP {(int)gatewayResponse.StatusCode} error.";
                    }

                    return StatusCode((int)gatewayResponse.StatusCode, errorPayload);
                }
                // --------------------------------------------------------------------------
                // SUCCESS CONDITION ENGINE: Handles HTTP 200 OK Processing Routes
                // --------------------------------------------------------------------------
                var rawGatewaySuccessData = System.Text.Json.JsonSerializer.Deserialize<UaeDdsGatewayCancelRawResponse>(gatewayResultString, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Sync Database: Update status info in your SQL tables 
                var localRecord = await _ddaRepository.GetByIdentifierStringAsync(ddaId);
                if (localRecord != null)
                {
                    localRecord.DdaStatus = "CANC";
                    await _ddaRepository.SaveChangesAsync();
                }

                var successResponse = new MerchantDdaCancelResponse
                {
                    ddcarid = rawGatewaySuccessData?.ddcarId ?? "1840",
                    message = rawGatewaySuccessData?.message ?? "Direct debit authority cancellation request submitted successfully.",
                    ddcaid = rawGatewaySuccessData?.ddaId ?? ddaId
                };
                _logger.LogInformation("CancelDirectDebitAuthority RESPONSE 200: DdaId={DdaId} cancellation submitted", ddaId);
                return Ok(successResponse);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "CancelDirectDebitAuthority FAILED to reach gateway for DdaId={DdaId}", ddaId);
                return StatusCode(StatusCodes.Status502BadGateway, new MerchantDdaCancelResponse
                {
                    status = 502,
                    message = $"Office network routing drop or firewall obstruction encountered: {ex.Message}"
                });
            }
        }

        [HttpGet("{ddaId}/cancellation/unsigned-pdf")]
        public async Task<IActionResult> DownloadUnsignedCancellationForm([FromRoute] string ddaId)
        {
            var client = _httpClientFactory.CreateClient();
            string targetGatewayUrl = $"{_gateway.BaseUrl} /v1/merchant/direct-debit-authorities/{ddaId}/cancellation/unsigned-pdf";
            _logger.LogInformation("DownloadUnsignedCancellationForm REQUEST received: DdaId={DdaId}", ddaId);
            try
            {
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Get, targetGatewayUrl);

                // --- CRITICAL HEADERS: Tell the gateway we accept the PDF stream OR a JSON error message ---
                gatewayRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));
                gatewayRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

               
                gatewayRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                var gatewayResponse = await client.SendAsync(gatewayRequest, HttpCompletionOption.ResponseHeadersRead);
                _logger.LogInformation("DownloadUnsignedCancellationForm ← gateway responded {StatusCode} for DdaId={DdaId}",
                                    (int)gatewayResponse.StatusCode, ddaId);
                // --------------------------------------------------------------------------
                // ERROR CONDITION ENGINE: Completely Dynamic Error Capture
                // --------------------------------------------------------------------------
                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    string gatewayResultString = await gatewayResponse.Content.ReadAsStringAsync();
                    _logger.LogWarning("DownloadUnsignedCancellationForm REJECTED {StatusCode} for DdaId={DdaId} ,GateWayErorrString :{gatewayResultString}", (int)gatewayResponse.StatusCode, ddaId, gatewayResultString);
                    var errorPayload = new MerchantDdaDownloadErrorResponse
                    {
                        status = (int)gatewayResponse.StatusCode,
                        message = $"Failed to download mandate PDF. Gateway returned HTTP status {(int)gatewayResponse.StatusCode}." // System fallback
                    };

                    if (!string.IsNullOrWhiteSpace(gatewayResultString))
                    {
                        string trimmedResponse = gatewayResultString.Trim();

                        // If it is a clean JSON error response, extract the exact message dynamically
                        if (trimmedResponse.StartsWith("{"))
                        {
                            try
                            {
                                using var jsonDoc = System.Text.Json.JsonDocument.Parse(trimmedResponse);
                                if (jsonDoc.RootElement.TryGetProperty("message", out var msgProp))
                                {
                                    errorPayload.message = msgProp.GetString() ?? errorPayload.message;
                                }
                            }
                            catch
                            {
                                if (!string.IsNullOrWhiteSpace(gatewayResponse.ReasonPhrase))
                                {
                                    errorPayload.message = gatewayResponse.ReasonPhrase;
                                }
                            }
                        }
                        // If the remote channel returns Nginx HTML text, parse out the headline message dynamically
                        else if (trimmedResponse.StartsWith("<"))
                        {
                            if (trimmedResponse.Contains("<h1>"))
                            {
                                int start = trimmedResponse.IndexOf("<h1>") + 4;
                                int end = trimmedResponse.IndexOf("</h1>");
                                if (end > start)
                                {
                                    string rawHeading = trimmedResponse.Substring(start, end - start);
                                    errorPayload.message = System.Text.RegularExpressions.Regex.Replace(rawHeading, "<.*?>", string.Empty).Trim();
                                }
                            }
                            else
                            {
                                errorPayload.message = !string.IsNullOrWhiteSpace(gatewayResponse.ReasonPhrase)
                                    ? gatewayResponse.ReasonPhrase
                                    : $"Remote server returned an HTML error screen layout.";
                            }
                        }
                    }

                    return StatusCode((int)gatewayResponse.StatusCode, errorPayload);
                }

                // --------------------------------------------------------------------------
                // SUCCESS CONDITION ENGINE: High-Performance Binary Stream Relay
                // --------------------------------------------------------------------------

                // Read the stream directly from the gateway content channel pipe
                var pdfStream = await gatewayResponse.Content.ReadAsStreamAsync();

                string responseFileName = $"DDA_Mandate_{ddaId}.pdf";

                // Returns a binary stream file download directly to the merchant's Postman / browser session
                _logger.LogInformation("DownloadUnsignedCancellationForm RESPONSE 200: PDF stream returned for DdaId={DdaId}", ddaId);
                return File(
                    fileStream: pdfStream,
                    contentType: "application/pdf",
                    fileDownloadName: responseFileName
                );
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "DownloadUnsignedCancellationForm FAILED to reach gateway for DdaId={DdaId}", ddaId);
                return StatusCode(StatusCodes.Status502BadGateway, new MerchantDdaDownloadErrorResponse
                {
                    status = 502,
                    message = $"Corporate core banking proxy network dropped connectivity to the gateway: {ex.Message}"
                });
            }
        }

        [HttpGet("{ddaId}/cancellation/signed-pdf")]
        public async Task<IActionResult> DownloadSignedCancellationForm([FromRoute] string ddaId)
        {
            var client = _httpClientFactory.CreateClient();
            string targetGatewayUrl = $"{_gateway.BaseUrl}/v1/merchant/direct-debit-authorities/{ddaId}/cancellation/signed-pdf";
            _logger.LogInformation("DownloadsignedCancellationForm REQUEST received: DdaId={DdaId}", ddaId);
            try
            {
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Get, targetGatewayUrl);

                // --- CRITICAL HEADERS: Tell the gateway we accept the PDF stream OR a JSON error message ---
                gatewayRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));
                gatewayRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                
                gatewayRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                var gatewayResponse = await client.SendAsync(gatewayRequest, HttpCompletionOption.ResponseHeadersRead);
                _logger.LogInformation("DownloadsignedCancellationForm ← gateway responded {StatusCode} for DdaId={DdaId}",
                                    (int)gatewayResponse.StatusCode, ddaId);
                // --------------------------------------------------------------------------
                // ERROR CONDITION ENGINE: Completely Dynamic Error Capture
                // --------------------------------------------------------------------------
                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("DownloadsignedCancellationForm REJECTED {StatusCode} for DdaId={DdaId}", (int)gatewayResponse.StatusCode, ddaId);
                    string gatewayResultString = await gatewayResponse.Content.ReadAsStringAsync();

                    var errorPayload = new MerchantDdaDownloadErrorResponse
                    {
                        status = (int)gatewayResponse.StatusCode,
                        message = $"Failed to download mandate PDF. Gateway returned HTTP status {(int)gatewayResponse.StatusCode}." // System fallback
                    };

                    if (!string.IsNullOrWhiteSpace(gatewayResultString))
                    {
                        string trimmedResponse = gatewayResultString.Trim();

                        // If it is a clean JSON error response, extract the exact message dynamically
                        if (trimmedResponse.StartsWith("{"))
                        {
                            try
                            {
                                using var jsonDoc = System.Text.Json.JsonDocument.Parse(trimmedResponse);
                                if (jsonDoc.RootElement.TryGetProperty("message", out var msgProp))
                                {
                                    errorPayload.message = msgProp.GetString() ?? errorPayload.message;
                                }
                            }
                            catch
                            {
                                if (!string.IsNullOrWhiteSpace(gatewayResponse.ReasonPhrase))
                                {
                                    errorPayload.message = gatewayResponse.ReasonPhrase;
                                }
                            }
                        }
                        // If the remote channel returns Nginx HTML text, parse out the headline message dynamically
                        else if (trimmedResponse.StartsWith("<"))
                        {
                            if (trimmedResponse.Contains("<h1>"))
                            {
                                int start = trimmedResponse.IndexOf("<h1>") + 4;
                                int end = trimmedResponse.IndexOf("</h1>");
                                if (end > start)
                                {
                                    string rawHeading = trimmedResponse.Substring(start, end - start);
                                    errorPayload.message = System.Text.RegularExpressions.Regex.Replace(rawHeading, "<.*?>", string.Empty).Trim();
                                }
                            }
                            else
                            {
                                errorPayload.message = !string.IsNullOrWhiteSpace(gatewayResponse.ReasonPhrase)
                                    ? gatewayResponse.ReasonPhrase
                                    : $"Remote server returned an HTML error screen layout.";
                            }
                        }
                    }

                    return StatusCode((int)gatewayResponse.StatusCode, errorPayload);
                }

                // --------------------------------------------------------------------------
                // SUCCESS CONDITION ENGINE: High-Performance Binary Stream Relay
                // --------------------------------------------------------------------------

                // Read the stream directly from the gateway content channel pipe
                var pdfStream = await gatewayResponse.Content.ReadAsStreamAsync();

                string responseFileName = $"DDA_Mandate_{ddaId}.pdf";
                _logger.LogInformation("DownloadsignedCancellationForm RESPONSE 200: PDF stream returned for DdaId={DdaId}", ddaId);
                // Returns a binary stream file download directly to the merchant's Postman / browser session
                return File(
                    fileStream: pdfStream,
                    contentType: "application/pdf",
                    fileDownloadName: responseFileName
                );
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "DownloadUnsignedCancellationForm FAILED to reach gateway for DdaId={DdaId}", ddaId);
                return StatusCode(StatusCodes.Status502BadGateway, new MerchantDdaDownloadErrorResponse
                {
                    status = 502,
                    message = $"Corporate core banking proxy network dropped connectivity to the gateway: {ex.Message}"
                });
            }
        }

        //[HttpPost("merchant/payment-files")]
        //[Consumes("multipart/form-data")] // Explicitly tells Swagger/API that this handles form uploads
        //public async Task<IActionResult> UploadBulkDirectDebitPaymentRequests([FromForm] UploadSignedPdfRequest request)
        //{
        //    var ddaId = "1234"; 
        //    // 1. Structural Request Validation Checks
        //    if (request.File == null || request.File.Length == 0)
        //    {
        //        return BadRequest(new
        //        {
        //            errors = new { file = new[] { "A valid CSV file payload must be provided under the 'file' parameter key." } }
        //        });
        //    }

        //    if (!request.File.ContentType.Equals("text/csv", StringComparison.OrdinalIgnoreCase) &&
        //        !request.File.ContentType.Equals("application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase))
        //    {
        //        return BadRequest(new
        //        {
        //            errors = new { file = new[] { "Invalid file formatting type. Only CSV files are accepted by the gateway." } }
        //        });
        //    }

        //    // 2. Instantiate a resilient HTTP Client from the registered Factory
        //    var client = _httpClientFactory.CreateClient();

        //    // 3. Define the Target Clearinghouse Sandbox Gateway Endpoint Route
        //    string targetGatewayUrl = $"https://test.directdebit.ae/api/v1/merchant/payment-files";

        //    try
        //    {
        //        // 4. Construct the Outbound Multipart Form Data Payload Container
        //        using var multipartContent = new MultipartFormDataContent();

        //        // 5. Read the incoming local file stream and convert it into a StreamContent object
        //        using var fileStream = request.File.OpenReadStream();
        //        var fileContent = new StreamContent(fileStream);

        //        // Pass the identical content-type header context through to the central bank gateway
        //        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

        //        // VERY IMPORTANT: The field parameter name here MUST match exactly what the clearinghouse expects ("file")
        //        multipartContent.Add(fileContent, "file", request.File.FileName);

        //        // 6. Instantiate the HTTP POST request wrapper
        //        var gatewayRequest = new HttpRequestMessage(HttpMethod.Post, targetGatewayUrl)
        //        {
        //            Content = multipartContent
        //        };

        //        // 7. Inject Merchant Basic Authentication Tokens
        //        // REPLACE: Swap "YourUsername" and "YourPassword" with your official Sandbox Merchant Credentials
        //        var credentialsBytes = System.Text.Encoding.ASCII.GetBytes("sandbox_stage:sandbox_stage");
        //        gatewayRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentialsBytes));

        //        // 8. Execute the external API transmission call
        //        var gatewayResponse = await client.SendAsync(gatewayRequest);

        //        // 9. Read the string response message payload returned from the gateway
        //        string gatewayResultString = await gatewayResponse.Content.ReadAsStringAsync();

        //        // 10. If the clearinghouse errors out, bubble the raw response schema directly back to your API consumer
        //        if (!gatewayResponse.IsSuccessStatusCode)
        //        {
        //            try
        //            {
        //                var gatewayError = System.Text.Json.JsonSerializer.Deserialize<object>(gatewayResultString);
        //                return StatusCode((int)gatewayResponse.StatusCode, gatewayError);
        //            }
        //            catch
        //            {
        //                return StatusCode((int)gatewayResponse.StatusCode, new
        //                {
        //                    message = gatewayResultString,
        //                    status = (int)gatewayResponse.StatusCode
        //                });
        //            }
        //        }

        //        // 11. Success Route Check: Update local repository state if tracked locally
        //        var localRecord = await _ddaRepository.GetByReferenceAsync(ddaId);
        //        if (localRecord != null)
        //        {
        //            localRecord.DdaStatus = "ACTV"; // Update status tracking metric to Active/Signed
        //            await _ddaRepository.SaveChangesAsync();
        //        }

        //        // 12. Return a clean OK response along with the tracking response metadata
        //        return Ok(new
        //        {
        //            message = "Signed PDF Mandate document successfully forwarded and registered inside UAEDDS gateway.",
        //            ddaId = ddaId,
        //            gatewayPayloadResponse = string.IsNullOrEmpty(gatewayResultString) ? null : System.Text.Json.JsonSerializer.Deserialize<object>(gatewayResultString)
        //        });
        //    }
        //    catch (HttpRequestException ex)
        //    {
        //        // Catches corporate firewall routing drops or restricted corporate office network connection faults
        //        return StatusCode(StatusCodes.Status502BadGateway, new
        //        {
        //            error = "Failed to communicate with the external UAEDDS clearinghouse network server.",
        //            details = ex.Message
        //        });
        //    }
        //}


    }
}
