using System.Data;
using PharmaLinkApp.Database;

namespace PharmaLinkApp.Services
{
    /// <summary>
    /// Ratings and comments (requirements 15, 21, 24 and 8).
    ///
    /// A review points back at the order it came from, so only a customer who
    /// actually received the medicine can rate it, and the UNIQUE constraint on
    /// (CustomerId, MedicineId, OrderId) stops the same purchase being rated
    /// twice. Moderation hides a review instead of deleting it.
    /// </summary>
    public class ReviewService
    {
        private readonly DbHelper _db = new DbHelper();

        // ---------------------------------------------------------------------
        //  CUSTOMER
        // ---------------------------------------------------------------------

        /// <summary>
        /// Writes a review, but only when the WHERE EXISTS clause can prove the
        /// customer bought that medicine on that order and the order has been
        /// delivered. The form disables the button too, but this is the rule
        /// that actually holds.
        /// </summary>
        public bool AddReview(int customerId, int medicineId, int orderId, int rating, string comment, out string message)
        {
            const string sql = @"
INSERT INTO Reviews (CustomerId, MedicineId, OrderId, Rating, Comment)
SELECT  @CustomerId, @MedicineId, @OrderId, @Rating, @Comment
WHERE   EXISTS (SELECT 1
                FROM   OrderItems oi
                       INNER JOIN Orders o ON o.OrderId = oi.OrderId
                WHERE  oi.OrderId    = @OrderId
                  AND  oi.MedicineId = @MedicineId
                  AND  o.CustomerId  = @CustomerId
                  AND  o.Status      = 'Delivered');";

            try
            {
                int rows = _db.ExecuteNonQuery(sql,
                    DbHelper.P("@CustomerId", customerId),
                    DbHelper.P("@MedicineId", medicineId),
                    DbHelper.P("@OrderId", orderId),
                    DbHelper.P("@Rating", rating),
                    DbHelper.P("@Comment", comment));

                if (rows == 1)
                {
                    message = "Thank you, your review has been posted.";
                    return true;
                }

                message = "You can only review a medicine from an order that has been delivered to you.";
                return false;
            }
            catch (Exception ex)
            {
                // The UNIQUE constraint fires when the same purchase is rated twice.
                if (ex.Message.Contains("UQ_Reviews_OneEach"))
                    message = "You have already reviewed this medicine on this order.";
                else
                    message = ex.Message;
                return false;
            }
        }

        /// <summary>The medicines on one delivered order that have not been reviewed yet.</summary>
        public DataTable GetReviewableItems(int orderId, int customerId)
        {
            const string sql = @"
SELECT  m.MedicineId, m.MedicineName, m.Strength, oi.Quantity, oi.UnitPrice
FROM    OrderItems oi
        INNER JOIN Orders    o ON o.OrderId    = oi.OrderId
        INNER JOIN Medicines m ON m.MedicineId = oi.MedicineId
WHERE   oi.OrderId   = @OrderId
  AND   o.CustomerId = @CustomerId
  AND   o.Status     = 'Delivered'
  AND   NOT EXISTS (SELECT 1 FROM Reviews r
                    WHERE r.OrderId    = oi.OrderId
                      AND r.MedicineId = oi.MedicineId
                      AND r.CustomerId = @CustomerId)
ORDER BY m.MedicineName;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@OrderId", orderId),
                DbHelper.P("@CustomerId", customerId));
        }

        /// <summary>
        /// Requirement 23. Reviews store only a CustomerId, so the join to Users
        /// is what turns a number into the reviewer's name on screen. Hidden
        /// reviews are excluded here rather than deleted at source.
        /// </summary>
        public DataTable GetForMedicine(int medicineId)
        {
            const string sql = @"
SELECT  r.ReviewId, u.FullName AS ReviewerName, r.Rating, r.Comment, r.ReviewDate
FROM    Reviews r
        INNER JOIN Users     u ON u.UserId     = r.CustomerId
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
WHERE   r.MedicineId = @MedicineId
  AND   r.IsHidden   = 0
ORDER BY r.ReviewDate DESC;";

            return _db.ExecuteTable(sql, DbHelper.P("@MedicineId", medicineId));
        }

        public decimal GetAverageForMedicine(int medicineId)
        {
            return _db.ExecuteScalarDecimal(@"
SELECT ISNULL(CAST(AVG(CAST(Rating AS DECIMAL(4,2))) AS DECIMAL(4,2)), 0)
FROM   Reviews WHERE MedicineId = @Id AND IsHidden = 0;",
                DbHelper.P("@Id", medicineId));
        }

        // ---------------------------------------------------------------------
        //  PHARMACY OWNER  (requirement 15 - read only by design)
        // ---------------------------------------------------------------------

        public DataTable GetForPharmacy(int pharmacyId, int minRating, int maxRating)
        {
            const string sql = @"
SELECT  r.ReviewId, u.FullName AS ReviewerName, m.MedicineName, m.Strength,
        r.Rating, r.Comment, r.ReviewDate, r.OrderId
FROM    Reviews r
        INNER JOIN Users     u ON u.UserId     = r.CustomerId
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
WHERE   m.PharmacyId = @PharmacyId
  AND   r.IsHidden   = 0
  AND   r.Rating BETWEEN @MinRating AND @MaxRating
ORDER BY r.ReviewDate DESC;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@MinRating", minRating),
                DbHelper.P("@MaxRating", maxRating));
        }

        public decimal GetAverageForPharmacy(int pharmacyId)
        {
            return _db.ExecuteScalarDecimal(@"
SELECT  ISNULL(CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2)), 0)
FROM    Reviews r
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
WHERE   m.PharmacyId = @Id AND r.IsHidden = 0;",
                DbHelper.P("@Id", pharmacyId));
        }

        public int CountForPharmacy(int pharmacyId)
        {
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)
FROM    Reviews r INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
WHERE   m.PharmacyId = @Id AND r.IsHidden = 0;",
                DbHelper.P("@Id", pharmacyId));
        }

        // ---------------------------------------------------------------------
        //  SUPER ADMIN MODERATION  (requirement 8)
        // ---------------------------------------------------------------------

        /// <summary>
        /// The moderation queue. Every row carries the order number that proves
        /// the purchase, so a review always traces back to a real delivery.
        /// </summary>
        public DataTable GetModerationQueue(int maxRating, bool includeHidden)
        {
            const string sql = @"
SELECT  r.ReviewId, u.FullName AS Reviewer, m.MedicineName, ph.PharmacyName,
        r.Rating, r.Comment, r.ReviewDate, r.OrderId, r.IsHidden
FROM    Reviews r
        INNER JOIN Users      u  ON u.UserId     = r.CustomerId
        INNER JOIN Medicines  m  ON m.MedicineId = r.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
WHERE   r.Rating <= @MaxRating
  AND   (@IncludeHidden = 1 OR r.IsHidden = 0)
ORDER BY r.ReviewDate DESC;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@MaxRating", maxRating),
                DbHelper.P("@IncludeHidden", includeHidden ? 1 : 0));
        }

        /// <summary>
        /// Hiding sets IsHidden to 1 rather than deleting the row, so the review
        /// disappears from the customer screens and from every average rating
        /// calculation, but survives if the pharmacy disputes the decision.
        /// </summary>
        public bool SetHidden(int reviewId, bool hidden)
        {
            return _db.ExecuteNonQuery(
                "UPDATE Reviews SET IsHidden = @Hidden WHERE ReviewId = @Id;",
                DbHelper.P("@Hidden", hidden ? 1 : 0),
                DbHelper.P("@Id", reviewId)) == 1;
        }
    }
}
