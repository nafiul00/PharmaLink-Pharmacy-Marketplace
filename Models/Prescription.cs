namespace PharmaLinkApp.Models
{
    /// <summary>
    /// The photograph of a doctor's prescription attached to an order that
    /// contains a medicine whose RequiresRx flag is set. The order cannot be
    /// confirmed while VerifyStatus is still Pending.
    /// </summary>
    public class Prescription
    {
        // The primary key of the uploaded document.
        public int PrescriptionId { get; set; }

        // The order the prescription covers. Declared ON DELETE CASCADE, so a deleted
        // order takes its prescription record with it rather than leaving an orphan
        // pointing at an order number that no longer exists.
        public int OrderId { get; set; }

        // Who uploaded it. Held as well as OrderId so a customer's own uploads can be
        // listed directly, and so the row still names a person if the order is ever
        // examined on its own.
        public int CustomerId { get; set; }

        // Where the scan was copied to on disk. Only the relative path is stored, which
        // keeps the database small and lets PrescriptionService.ResolveImagePath turn it
        // back into a full path at display time. The column is NOT NULL, because a
        // prescription record with no photograph attached would prove nothing.
        public string ImagePath { get; set; } = "";

        // Optional, typed by the customer. Nullable in the database, hence the empty
        // string default so a screen can print it without a null check.
        public string DoctorName { get; set; } = "";

        // When the scan arrived, defaulted by the database to the server's clock so the
        // timestamp cannot come from a machine whose clock is wrong.
        public DateTime UploadedAt { get; set; }

        public string VerifyStatus { get; set; } = "";   // Pending | Approved | Rejected
        // Those three values are all CK_Prescriptions_Status permits, and the column
        // defaults to Pending. This is the field the pharmacy owner changes after looking
        // at the scan, and an order cannot move to Confirmed while it still reads
        // Pending, which is the entire purpose of the table.

        // Joined in from Users so the verification queue can name who sent the scan
        // instead of showing a CustomerId.
        public string CustomerName { get; set; } = "";
    }
}
