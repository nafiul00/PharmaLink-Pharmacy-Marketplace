namespace PharmaLinkApp.Models
{
    /// <summary>
    /// A medicine category from the master list the Super Admin maintains.
    /// A referenced category is deactivated rather than deleted, so the foreign
    /// keys pointing at it from Medicines stay valid.
    /// </summary>
    public class Category
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public string Description { get; set; } = "";
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
