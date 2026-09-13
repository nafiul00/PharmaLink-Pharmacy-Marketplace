namespace PharmaLinkApp.Models
{
    /// <summary>
    /// A product on a pharmacy's shelf. Two pharmacies selling the same brand
    /// are two separate rows, each with its own price and its own stock.
    /// </summary>
    public class Medicine
    {
        public int MedicineId { get; set; }
        public int PharmacyId { get; set; }
        public int CategoryId { get; set; }
        public string MedicineName { get; set; } = "";
        public string GenericName { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Strength { get; set; } = "";
        public decimal UnitPrice { get; set; }
        public int Stock { get; set; }
        public int MinStock { get; set; }
        public bool RequiresRx { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string Description { get; set; } = "";
        public string ImagePath { get; set; } = "";
        public bool IsActive { get; set; } = true;

        // filled by joins when a screen needs them
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
        public decimal PriceAfterDiscount =>
            decimal.Round(UnitPrice * (1 - DiscountPercent / 100m), 2);

        // The same two-column comparison the low stock query makes in SQL, available on
        // the object for any screen holding a Medicine rather than a DataTable.
        public bool IsLowStock => Stock < MinStock;
    }
}
