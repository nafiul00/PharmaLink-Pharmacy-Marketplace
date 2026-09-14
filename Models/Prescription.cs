// One namespace for the whole model layer, so a form gets it all with one using.
namespace PharmaLinkApp.Models
{
    /// <summary>A prescription scan; the order waits while this is Pending.</summary>
    public class Prescription
    {
        public int PrescriptionId { get; set; }          // PK of the uploaded document
        public int OrderId { get; set; }                 // ON DELETE CASCADE, so no scan outlives its order
        public int CustomerId { get; set; }              // held too, so the row still names a person on its own
        public string ImagePath { get; set; } = "";      // relative path only; ResolveImagePath rebuilds the full one
        public string DoctorName { get; set; } = "";     // optional, nullable column, hence the empty string default
        public DateTime UploadedAt { get; set; }         // database default, so the clock is the server's
        public string VerifyStatus { get; set; } = "";   // CK_Prescriptions_Status: Pending, Approved or Rejected
        public string CustomerName { get; set; } = "";   // joined from Users so the queue names who sent the scan
    }
}
