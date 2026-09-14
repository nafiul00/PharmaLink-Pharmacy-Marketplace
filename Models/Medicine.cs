namespace PharmaLinkApp.Models
{
    /// <summary>
    /// A product on a pharmacy's shelf. Two pharmacies selling the same brand
    /// are two separate rows, each with its own price and its own stock.
    /// </summary>
    public class Medicine
    {
        // The primary key. Cart, OrderItems, Reviews and Offers all point at it.
        public int MedicineId { get; set; }

        // The owning shop. This is the column every Admin side query filters on, matched
        // against UserSession.PharmacyId, so it is the field that keeps one owner's
        // inventory invisible to another owner.
        public int PharmacyId { get; set; }

        // Which category the medicine belongs to, a foreign key onto Categories. The id
        // is stored rather than the name so a rename is one UPDATE in one place.
        public int CategoryId { get; set; }

        // The brand name the customer searches for, for example Napa.
        public string MedicineName { get; set; } = "";

        // The active ingredient, for example Paracetamol. Held separately so a search can
        // find every brand of the same drug, which is the point of a marketplace.
        public string GenericName { get; set; } = "";

        // Who makes it. Shown on the details screen so the customer can tell two
        // identically named products apart.
        public string Manufacturer { get; set; } = "";

        // Dosage such as 500mg. It is the third column of UQ_Medicines_PerShop
        // (PharmacyId, MedicineName, Strength), which is what lets one shop list the
        // 500mg and the 665mg versions of a brand as two rows without colliding.
        public string Strength { get; set; } = "";

        // The shelf price before any offer. decimal, never double, because money must be
        // exact; CK_Medicines_Price keeps it above zero in the database.
        public decimal UnitPrice { get; set; }

        // Units currently on the shelf. CK_Medicines_Stock forbids a negative value, so
        // an oversold basket is refused rather than quietly stored.
        public int Stock { get; set; }

        // The reorder threshold the owner sets per product. It exists so "low stock"
        // means something different for a fast moving painkiller than for a rare item.
        public int MinStock { get; set; }

        // True when the law requires a doctor's prescription. This single flag is what
        // makes the checkout demand an uploaded prescription before the order is placed.
        public bool RequiresRx { get; set; }

        // The expiry date on the pack. MedicineEditorForm refuses a date that is not in
        // the future, which is the rule Validator.IsFutureDate enforces.
        public DateTime ExpiryDate { get; set; }

        // Free text shown on the details screen. Nullable in the database, so the empty
        // string default keeps every caller free of null checks.
        public string Description { get; set; } = "";

        // Where the product photograph lives, stored as a path rather than as the image
        // itself so the database stays small and the file can be replaced on disk.
        public string ImagePath { get; set; } = "";

        // The soft delete flag. Delisting sets it to false and keeps the row, because
        // OrderItems and Reviews still refer to the medicine and those references must
        // stay valid. Every customer facing query carries AND IsActive = 1.
        public bool IsActive { get; set; } = true;

        // filled by joins when a screen needs them
        // None of the four below is a Medicines column. They are carried on the object so
        // a grid can show the category, the shop, its area and today's discount without a
        // second query per row, and they are simply empty when the query did not join.
        public string CategoryName { get; set; } = "";
        public string PharmacyName { get; set; } = "";
        public string Area { get; set; } = "";
        public decimal DiscountPercent { get; set; }

        // COMPUTED PROPERTIES - abstraction, and the best OOP example in the models.
        // Neither is stored in the database and neither has a setter: they are derived
        // from fields already on the object, so a caller asks a question and never sees
        // the arithmetic. => is expression-bodied syntax, evaluated fresh on every read,
        // which means they can never fall out of step with UnitPrice or Stock.

        // 100m is a DECIMAL literal, not 100. Using 100 would perform integer division
        // in part of the expression and lose the fractional percentage.
        // Rounded to 2dp here because money is displayed and stored to 2dp.
        // MedicineDetailsForm prints this figure as the final price and subtracts it from
        // UnitPrice to show the saving.
        public decimal PriceAfterDiscount =>
            decimal.Round(UnitPrice * (1 - DiscountPercent / 100m), 2);

        // The same two-column comparison the low stock query makes in SQL, available on
        // the object for any screen holding a Medicine rather than a DataTable.
        public bool IsLowStock => Stock < MinStock;
    }
}
