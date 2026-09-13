namespace PharmaLinkApp.Models
{
    /// <summary>
    /// A rating and comment. OrderId is carried so the application can prove the
    /// reviewer actually bought the item, and IsHidden lets the Super Admin
    /// moderate abuse without destroying the audit trail.
    /// </summary>
    public class Review
    {
        public int ReviewId { get; set; }
        public int CustomerId { get; set; }
        public int MedicineId { get; set; }
        // THE COLUMN THAT MAKES A REVIEW VERIFIED. Without OrderId a review would just
        // be an opinion attached to a medicine; carrying it means every rating traces
        // back to a real, delivered order. It is also the third column of
        // UQ_Reviews_OneEach (CustomerId, MedicineId, OrderId), which is what allows a
        // repeat purchase to be reviewed again while blocking the same purchase twice.
        public int OrderId { get; set; }
        public byte Rating { get; set; }
        public string Comment { get; set; } = "";
        public DateTime ReviewDate { get; set; }
        public bool IsHidden { get; set; }

        public string ReviewerName { get; set; } = "";
        public string MedicineName { get; set; } = "";
        public string PharmacyName { get; set; } = "";
    }
}
