// One namespace for the whole model layer, so a form gets it all with one using.
namespace PharmaLinkApp.Models
{
    /// <summary>A medicine category from the Super Admin's master list.</summary>
    public class Category
    {
        public int CategoryId { get; set; }              // PK; Medicines.CategoryId points here, so it cannot just be deleted
        public string CategoryName { get; set; } = "";   // UQ_Categories_Name stops two entries called Antibiotic
        public string Description { get; set; } = "";    // nullable column, so empty string saves every screen a null check
        public bool IsActive { get; set; } = true;       // soft delete: retiring keeps the row so old medicines still resolve

        /// <summary>Polymorphism: a ComboBox shows the name, not the type.</summary>
        public override string ToString() => CategoryName;
    }
}
