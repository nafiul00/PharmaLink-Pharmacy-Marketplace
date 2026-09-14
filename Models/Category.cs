namespace PharmaLinkApp.Models
{
    /// <summary>
    /// A medicine category from the master list the Super Admin maintains.
    /// A referenced category is deactivated rather than deleted, so the foreign
    /// keys pointing at it from Medicines stay valid.
    /// </summary>
    public class Category
    {
        // The primary key. Medicines.CategoryId is a foreign key onto this column, which
        // is why a category that is in use cannot simply be deleted.
        public int CategoryId { get; set; }

        // The name shown in every dropdown and grid. UQ_Categories_Name makes it unique,
        // so the list cannot end up with two entries called Antibiotic.
        public string CategoryName { get; set; } = "";

        // A short note for the Super Admin's own reference. Nullable in the database,
        // which is why the property is initialised to an empty string rather than left
        // null: every screen can then print it without a null check.
        public string Description { get; set; } = "";

        // The soft delete flag. Retiring a category sets this to false and keeps the row,
        // so historic medicines still resolve their category name. Default true, matching
        // DF_Categories_IsActive, so a newly created category is usable straight away.
        public bool IsActive { get; set; } = true;

        /// <summary>ComboBox controls display this.</summary>
        // The SECOND of the project's two ToString overrides (the other is on Pharmacy).
        // Run time polymorphism: a ComboBox bound to Category objects calls ToString()
        // on each one and the runtime dispatches to this version, so the dropdown shows
        // "Antibiotic" instead of the type name.
        //
        // MedicineEditorForm does not rely on it - that form sets DisplayMember and
        // ValueMember explicitly, which is the more precise technique because it also
        // gives SelectedValue the id. This override is the fallback for any list that
        // binds Category objects without naming a display column.
        public override string ToString() => CategoryName;
    }
}
