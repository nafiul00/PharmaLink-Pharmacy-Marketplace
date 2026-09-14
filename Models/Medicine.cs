// One namespace for the whole model layer, so a form gets it all with one using.
namespace PharmaLinkApp.Models
{
    /// <summary>A product on one shop's shelf, with its own price and stock.</summary>
    public class Medicine
    {
        public int MedicineId { get; set; }              // PK; Cart, OrderItems, Reviews and Offers all point here
        public int PharmacyId { get; set; }              // matched against UserSession.PharmacyId, so owners stay apart
        public int CategoryId { get; set; }              // FK onto Categories; the id, so a rename is one UPDATE
        public string MedicineName { get; set; } = "";   // the brand the customer searches for, such as Napa
        public string GenericName { get; set; } = "";    // the ingredient, so a search finds every brand of one drug
        public string Manufacturer { get; set; } = "";   // tells two identically named products apart
        public string Strength { get; set; } = "";       // third column of UQ_Medicines_PerShop, so 500mg and 665mg coexist
        public decimal UnitPrice { get; set; }           // decimal never double, because money must be exact
        public int Stock { get; set; }                   // CK_Medicines_Stock forbids a negative, so overselling is refused
        public int MinStock { get; set; }                // per product reorder line, since 'low' differs by product
        public bool RequiresRx { get; set; }             // the one flag that makes checkout demand a prescription
        public DateTime ExpiryDate { get; set; }         // the editor refuses a date that is not in the future
        public string Description { get; set; } = "";    // nullable column, so empty string saves the null checks
        public string ImagePath { get; set; } = "";      // a path, not the image, so the database stays small
        public bool IsActive { get; set; } = true;       // soft delete: delisting keeps the row old orders still need

        // The four below are joined in, so one grid query covers every column.
        public string CategoryName { get; set; } = "";   // Categories.CategoryName, so no grid holds the whole list
        public string PharmacyName { get; set; } = "";   // which shop is selling it, the heart of a marketplace listing
        public string Area { get; set; } = "";           // Pharmacies.Area, what the customer filters the market by
        public decimal DiscountPercent { get; set; }     // today's winning offer; 0 collapses the price below to UnitPrice

        // ABSTRACTION: derived, never stored, no setter, so it cannot fall out of step.
        public decimal PriceAfterDiscount =>
            // 100m is a decimal literal; plain 100 would divide as integers.
            decimal.Round(UnitPrice * (1 - DiscountPercent / 100m), 2);

        // The same two-column test the low stock SQL makes, for code holding an object.
        public bool IsLowStock => Stock < MinStock;
    }
}
