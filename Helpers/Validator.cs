using System.Text.RegularExpressions;   // Regex, for the two shape rules a length test cannot express

// Same Helpers namespace: no database, no UI, safe to call while typing.
namespace PharmaLinkApp.Helpers
{
    /// <summary>One home for every rule, so rules cannot drift apart.</summary>
    public static class Validator
    {
        // static readonly + Compiled, so each pattern is built once, not per keystroke.
        private static readonly Regex EmailPattern =
            new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.Compiled);   // permissive twin of CK_Users_Email

        // Anchored at both ends, so only a whole string of digits matches.
        private static readonly Regex DigitsOnly =
            new Regex(@"^\d+$", RegexOptions.Compiled);   // the + also rules out an empty string

        // The base test the others build on; mirrors every NOT NULL column in the schema.
        public static bool IsBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value);   // null, "" and "   " all count as empty
        }

        /// <summary>Matches the CK_Users_Email CHECK constraint on Users.</summary>
        public static bool IsEmail(string value)
        {
            // Blank first, so an empty box says "required" rather than "badly formed".
            return !IsBlank(value) && EmailPattern.IsMatch(value.Trim());   // Trim: the pattern is anchored
        }

        /// <summary>Bangladeshi mobiles are eleven digits starting 01.</summary>
        public static bool IsMobile(string value)
        {
            if (IsBlank(value)) return false;   // guard first, so value.Trim() below cannot be a null reference
            string digits = value.Trim();       // trimmed once, so all three tests judge the same text
            // All three are needed, and only here: Phone has no CHECK in the database.
            return digits.Length == 11 && DigitsOnly.IsMatch(digits) && digits.StartsWith("01");
        }

        /// <summary>At least six characters and at least one digit.</summary>
        public static bool IsStrongPassword(string value)
        {
            // The one rule with no database twin: a hash cannot reveal a weak password.
            if (IsBlank(value) || value.Length < 6) return false;
            // Method group reads as "any character that is a digit", and stops at the first.
            return value.Any(char.IsDigit);
        }

        /// <summary>A price: above zero, like CK_Medicines_Price.</summary>
        public static bool IsPositiveDecimal(string value, out decimal result)
        {
            // TryParse, so "abc" returns false instead of throwing; out saves a second parse.
            return decimal.TryParse(value, out result) && result > 0m;
        }

        /// <summary>Stock: zero is allowed, unlike price.</summary>
        public static bool IsNonNegativeInt(string value, out int result)
        {
            // Zero is valid, the difference between CK_Medicines_Stock and the price rule.
            return int.TryParse(value, out result) && result >= 0;
        }

        /// <summary>A quantity: at least one, like CK_Cart_Qty.</summary>
        public static bool IsPositiveInt(string value, out int result)
        {
            // Zero is refused because a basket line for no units is not a storable row.
            return int.TryParse(value, out result) && result > 0;
        }

        /// <summary>Commission rate, capped at 30 by CK_Pharmacies_Comm.</summary>
        public static bool IsCommissionRate(string value, out decimal result)
        {
            // Zero allowed: a commission-free shop is the Super Admin's call to make.
            return decimal.TryParse(value, out result) && result >= 0m && result <= 30m;
        }

        /// <summary>Discounts are capped by CK_Offers_Percent at 70 percent.</summary>
        public static bool IsDiscountPercent(string value, out decimal result)
        {
            // The 0 and 70 bounds are CK_Offers_Percent's own, so the two can never disagree.
            return decimal.TryParse(value, out result) && result > 0m && result <= 70m;
        }

        /// <summary>A DGDA licence looks like DGDA-DH-10021.</summary>
        public static bool IsLicenseNo(string value)
        {
            // A length test, not a pattern: UQ_Pharmacies_License adds what really matters.
            return !IsBlank(value) && value.Trim().Length >= 6;
        }

        /// <summary>An expiry date has to be in the future.</summary>
        public static bool IsFutureDate(DateTime value)
        {
            // .Date strips the time, so today is rejected as a whole day, not by the hour.
            return value.Date > DateTime.Today;
        }
    }
}
