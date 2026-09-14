namespace PharmaLinkApp.Models
{
    /// <summary>
    /// A percentage discount on one medicine, valid between two dates.
    /// The discounted price itself is always computed inside the SQL query, so
    /// this class only carries what the grid needs to display.
    /// </summary>
    public class Offer
    {
        // The primary key of the Offers row.
        public int OfferId { get; set; }

        // Which medicine the discount applies to, a foreign key onto Medicines. An offer
        // belongs to a product rather than to a shop, so a pharmacy can run a discount on
        // one item without touching the rest of its shelf.
        public int MedicineId { get; set; }

        // The campaign name the owner types, such as Winter Flu Offer. It is shown to the
        // customer on the offers screen and has no effect on the price.
        public string OfferTitle { get; set; } = "";

        // The size of the discount. CK_Offers_Percent keeps it above 0 and at or below
        // 70, which is exactly the range Validator.IsDiscountPercent checks in the form.
        public decimal DiscountPercent { get; set; }

        // The validity window. Every price query tests today's date BETWEEN these two, so
        // an offer starts and stops on its own with nothing to run at midnight.
        // CK_Offers_Dates refuses a row whose EndDate falls before its StartDate.
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // Lets an owner pause a campaign without losing it. The price queries require
        // IsActive = 1 as well as the date window, so pausing takes effect immediately
        // and the row can be switched back on later.
        public bool IsActive { get; set; } = true;

        // The four fields below are not Offers columns. They are what a screen showing an
        // offer needs to make sense of it: which product and shop it belongs to, and the
        // before and after prices. The prices are worked out by the query rather than
        // here, so the offers screen, the details screen, the cart and the invoice can
        // never quote four different numbers for the same discount.
        public string MedicineName { get; set; } = "";
        public string PharmacyName { get; set; } = "";
        public decimal OriginalPrice { get; set; }
        public decimal DiscountedPrice { get; set; }
    }
}
