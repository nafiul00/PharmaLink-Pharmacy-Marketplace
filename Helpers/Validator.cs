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
        // Both patterns are static readonly, so each one is built once for the lifetime
        // of the application rather than on every keystroke, and RegexOptions.Compiled
        // asks the runtime to turn the pattern into real instructions the first time it
        // is used. That is worth doing precisely because these two run so often.

        // Deliberately permissive: something, an @, something, a dot, then at least two
        // more characters. It is the C# side of CK_Users_Email, which the database
        // expresses as Email LIKE '%_@_%._%'. A stricter pattern would reject valid but
        // unusual addresses, and the address still has to survive UQ_Users_Email.
        private static readonly Regex EmailPattern =
            new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.Compiled);

        // Anchored at both ends, so it matches only when the WHOLE string is digits.
        // Without ^ and $ it would happily find a digit anywhere in "07x1234".
        private static readonly Regex DigitsOnly =
            new Regex(@"^\d+$", RegexOptions.Compiled);

        // The base test the others build on. IsNullOrWhiteSpace treats null, "" and a
        // field holding only spaces as the same thing, which is what a user means by
        // "I left it empty". It is the C# side of every NOT NULL column in the schema.
        public static bool IsBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value);
        }

        /// <summary>Matches the CK_Users_Email CHECK constraint on the Users table.</summary>
        public static bool IsEmail(string value)
        {
            // Blank first, so an empty box reports "required" rather than "badly formed".
            // Trim() before matching because the pattern is anchored and a trailing space
            // would fail it; the login query trims for the same reason.
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
            // The one rule with NO database counterpart, and it could not have one: the
            // column stores a hash, and a hash of a weak password is indistinguishable
            // from a hash of a strong one. Password strength can only be judged here,
            // while the plain text is still in hand.
            if (IsBlank(value) || value.Length < 6) return false;
            // value.Any(char.IsDigit) walks the characters and stops at the first digit.
            // char.IsDigit is passed as a method group, which reads as "any character
            // that is a digit" rather than spelling out a loop.
            return value.Any(char.IsDigit);
        }

        /// <summary>A price: parses and is greater than zero. Mirrors CK_Medicines_Price.</summary>
        public static bool IsPositiveDecimal(string value, out decimal result)
        {
            // TryParse, not Parse, so "abc" returns false instead of throwing; it sets
            // result to 0 on failure, and the caller ignores result when false comes back.
            // The out parameter hands the parsed number straight back, so the caller never
            // has to parse the same text twice.
            //
            // The "> 0" half is the same rule CK_Medicines_Price enforces on UnitPrice,
            // and CK_OrderItems_Price enforces on the price stored with an order line.
            return decimal.TryParse(value, out result) && result > 0m;
        }

        /// <summary>A stock figure: zero is allowed. Mirrors CK_Medicines_Stock and CK_Medicines_MinStock.</summary>
        public static bool IsNonNegativeInt(string value, out int result)
        {
            // Zero IS valid here, unlike the price above: a shop legitimately holds none
            // of something. That is exactly the difference between CK_Medicines_Stock
            // (>= 0) and CK_Medicines_Price (> 0), and MedicineEditorForm uses this
            // method for both Stock and MinStock.
            return int.TryParse(value, out result) && result >= 0;
        }

        /// <summary>A quantity: must be at least one. Mirrors CK_Cart_Qty and CK_OrderItems_Qty.</summary>
        public static bool IsPositiveInt(string value, out int result)
        {
            // Used by every Add to Cart box and by the restock box. Zero is refused
            // because CK_Cart_Qty requires Quantity > 0: a basket line for no units is
            // not a thing the database will store, so the form refuses it first.
            return int.TryParse(value, out result) && result > 0;
        }

        /// <summary>Commission is a platform rate, capped by CK_Pharmacies_Comm at 30 percent.</summary>
        public static bool IsCommissionRate(string value, out decimal result)
        {
            // 0 to 30 inclusive, the same bounds as CK_Pharmacies_Comm
            // (CommissionRate >= 0 AND CommissionRate <= 30). Zero is allowed, because a
            // commission-free shop is a decision the Super Admin is entitled to make.
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
            // A length test rather than a pattern, on purpose: the real DGDA format is not
            // something this project can claim to know in full, and rejecting a genuine
            // licence would be worse than accepting an odd looking one. The database adds
            // what actually matters, UQ_Pharmacies_License, so two shops can never
            // register the same licence number however it is shaped.
            return !IsBlank(value) && value.Trim().Length >= 6;
        }

        /// <summary>An expiry date has to be in the future. Used by MedicineEditorForm.</summary>
        public static bool IsFutureDate(DateTime value)
        {
            // .Date on both sides strips the time, so "today" is rejected as a whole day
            // rather than being accepted for the rest of the afternoon. Strictly greater
            // than, because a medicine expiring today should not go on sale today.
            //
            // The database does not check this one: ExpiryDate is a plain NOT NULL DATE
            // column, so like IsMobile this rule is enforced in the form alone.
            return value.Date > DateTime.Today;
        }
    }
}
