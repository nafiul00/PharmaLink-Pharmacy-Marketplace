namespace PharmaLinkApp.Models
{
    /// <summary>
    /// A rating and comment. OrderId is carried so the application can prove the
    /// reviewer actually bought the item, and IsHidden lets the Super Admin
    /// moderate abuse without destroying the audit trail.
    /// </summary>
    public class Review
    {
        // The primary key of the review row.
        public int ReviewId { get; set; }

        // Who wrote it, and what they were rating. Both are foreign keys, so a review
        // cannot name a customer or a medicine that does not exist, and they are the
        // first two columns of UQ_Reviews_OneEach.
        public int CustomerId { get; set; }
        public int MedicineId { get; set; }
        // THE COLUMN THAT MAKES A REVIEW VERIFIED. Without OrderId a review would just
        // be an opinion attached to a medicine; carrying it means every rating traces
        // back to a real, delivered order. It is also the third column of
        // UQ_Reviews_OneEach (CustomerId, MedicineId, OrderId), which is what allows a
        // repeat purchase to be reviewed again while blocking the same purchase twice.
        public int OrderId { get; set; }

        // The star rating, one to five. It is a byte because the database column is
        // TINYINT, and CK_Reviews_Rating restricts it to BETWEEN 1 AND 5, so a zero or a
        // six cannot be stored however the row is written.
        public byte Rating { get; set; }

        // The written comment. Nullable in the database, so the empty string default
        // keeps the rating usable on its own when the customer left no words.
        public string Comment { get; set; } = "";

        // When it was left, defaulted by the database to the server's clock.
        public DateTime ReviewDate { get; set; }

        // The moderation flag. Hiding a review sets this to true and keeps the row, so
        // the audit trail survives; the average rating queries exclude hidden rows, which
        // is how moderation changes a shop's score without deleting anything.
        public bool IsHidden { get; set; }

        // Joined in for display: who reviewed, what they reviewed and whose shop it was.
        // The last one is what lets the Super Admin look at ratings by pharmacy even
        // though Reviews has no PharmacyId column of its own.
        public string ReviewerName { get; set; } = "";
        public string MedicineName { get; set; } = "";
        public string PharmacyName { get; set; } = "";
    }
}
