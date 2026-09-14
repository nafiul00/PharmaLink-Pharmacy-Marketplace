// One namespace for the whole model layer, so a form gets it all with one using.
namespace PharmaLinkApp.Models
{
    /// <summary>A rating and comment, tied to the order that proves it.</summary>
    public class Review
    {
        public int ReviewId { get; set; }     // primary key of the review row
        public int CustomerId { get; set; }   // the reviewer, a foreign key onto Users
        public int MedicineId { get; set; }   // the product rated; the average groups on this
        // OrderId proves the purchase; it is the third column of UQ_Reviews_OneEach.
        public int OrderId { get; set; }
        public byte Rating { get; set; }                 // TINYINT in SQL; CK_Reviews_Rating holds it BETWEEN 1 AND 5
        public string Comment { get; set; } = "";        // nullable column, so empty string keeps the rating usable alone
        public DateTime ReviewDate { get; set; }         // defaulted by the database to the server's clock
        public bool IsHidden { get; set; }               // moderation keeps the row; average rating queries skip hidden ones

        // The three below are joined in for display, not Reviews columns.
        public string ReviewerName { get; set; } = "";   // Users.FullName, so the list reads as a name
        public string MedicineName { get; set; } = "";   // lets one screen list reviews across products
        public string PharmacyName { get; set; } = "";   // reached through Medicines; the only way to group by shop
    }
}
