// One namespace for the whole model layer, so a form gets it all with one using.
namespace PharmaLinkApp.Models
{
    /// <summary>Percentage discount on one medicine, live between two dates.</summary>
    public class Offer
    {
        public int OfferId { get; set; }                 // primary key of the Offers row
        public int MedicineId { get; set; }              // FK onto Medicines: a discount belongs to a product, not a shop
        public string OfferTitle { get; set; } = "";     // campaign name the owner types, display only
        public decimal DiscountPercent { get; set; }     // CK_Offers_Percent keeps it above 0 and at or below 70
        public DateTime StartDate { get; set; }          // first day the discount is live
        public DateTime EndDate { get; set; }            // last day; BETWEEN is inclusive, so it runs to end of this date
        public bool IsActive { get; set; } = true;       // lets an owner pause a campaign without losing the row

        // The four below are not Offers columns; they are joined in for display only.
        public string MedicineName { get; set; } = "";   // what is discounted, joined from Medicines
        public string PharmacyName { get; set; } = "";   // whose shelf it sits on, joined through Medicines
        public decimal OriginalPrice { get; set; }       // Medicines.UnitPrice, the struck-through figure
        public decimal DiscountedPrice { get; set; }     // worked out by the query, so no screen does the sum itself
    }
}
