using System.Text.RegularExpressions;

namespace PharmaLinkApp.Helpers
{
    /// <summary>
    /// Every validation rule the forms enforce lives here, in one place, so the
    /// same rule cannot drift between two screens.
    ///
    /// Each rule is enforced twice on purpose: once here, so the user sees a red
    /// label under the field before anything is sent anywhere, and once again by
    /// a CHECK or UNIQUE constraint in PharmaLinkDB_Setup.sql, so a form that is
    /// bypassed still cannot write a bad row.
    /// </summary>
    public static class Validator
    {
        private static readonly Regex EmailPattern =
            new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.Compiled);

        private static readonly Regex DigitsOnly =
            new Regex(@"^\d+$", RegexOptions.Compiled);

        public static bool IsBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value);
        }

        /// <summary>Matches the CK_Users_Email CHECK constraint on the Users table.</summary>
        public static bool IsEmail(string value)
        {
            return !IsBlank(value) && EmailPattern.IsMatch(value.Trim());
        }

        /// <summary>Bangladeshi mobile numbers are eleven digits and start with 01.</summary>
        public static bool IsMobile(string value)
        {
            if (IsBlank(value)) return false;
            string digits = value.Trim();
            // Three conditions, all required: exactly 11 characters, every one a digit,
            // and the Bangladeshi mobile prefix 01. Length alone would accept letters;
            // DigitsOnly alone would accept a landline or a wrong-length number.
            //
            // Note this rule lives ONLY here, not in the database - Phone is NVARCHAR(20)
            // with a UNIQUE constraint but no CHECK on its shape. So unlike the price and
            // rating rules, this one is enforced once rather than twice, and that is a
            // fair gap to acknowledge: a CK_Users_Phone would close it.
            return digits.Length == 11 && DigitsOnly.IsMatch(digits) && digits.StartsWith("01");
        }

        /// <summary>At least six characters and at least one digit.</summary>
        public static bool IsStrongPassword(string value)
        {
            if (IsBlank(value) || value.Length < 6) return false;
            return value.Any(char.IsDigit);
        }

        public static bool IsPositiveDecimal(string value, out decimal result)
        {
            return decimal.TryParse(value, out result) && result > 0m;
        }

        public static bool IsNonNegativeInt(string value, out int result)
        {
            return int.TryParse(value, out result) && result >= 0;
        }

        public static bool IsPositiveInt(string value, out int result)
        {
            return int.TryParse(value, out result) && result > 0;
        }

        /// <summary>Commission is a platform rate, capped by CK_Pharmacies_Comm at 30 percent.</summary>
        public static bool IsCommissionRate(string value, out decimal result)
        {
            return decimal.TryParse(value, out result) && result >= 0m && result <= 30m;
        }

        /// <summary>Discounts are capped by CK_Offers_Percent at 70 percent.</summary>
        public static bool IsDiscountPercent(string value, out decimal result)
        {
            // The 0 and 70 bounds are NOT arbitrary - they are the same numbers
            // CK_Offers_Percent enforces on the Offers table. That is the "validated
            // twice" pattern: this method gives the user a red label before anything is
            // sent, and the CHECK constraint refuses the row even if this form were
            // bypassed entirely. If the two ever disagreed, the database would win and
            // the user would see an exception instead of a message.
            //
            // TryParse rather than Parse, so typing "abc" returns false instead of
            // throwing; result is set to 0 on failure and callers ignore it.
            return decimal.TryParse(value, out result) && result > 0m && result <= 70m;
        }

        /// <summary>A DGDA licence looks like DGDA-DH-10021.</summary>
        public static bool IsLicenseNo(string value)
        {
            return !IsBlank(value) && value.Trim().Length >= 6;
        }

        public static bool IsFutureDate(DateTime value)
        {
            return value.Date > DateTime.Today;
        }
    }
}
