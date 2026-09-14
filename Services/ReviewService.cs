using System.Data;                  // DataTable, the shape every read on these screens returns
using PharmaLinkApp.Database;       // DbHelper, the only class that opens a SqlConnection

namespace PharmaLinkApp.Services   // all review SQL lives here, never inside a Form
{
    /// <summary>Ratings, owner reports and Super Admin moderation.</summary>
    public class ReviewService   // requirements 15, 21, 24 and 8
    {
        private readonly DbHelper _db = new DbHelper();   // opens and closes a connection per call

        // ===== CUSTOMER =====

        /// <summary>Writes a review only when the order proves the purchase.</summary>
        public bool AddReview(int customerId, int medicineId, int orderId, int rating, string comment, out string message)
        {
            // INSERT ... SELECT ... WHERE EXISTS: the proof and the write are one statement.
            const string sql = @"
INSERT INTO Reviews (CustomerId, MedicineId, OrderId, Rating, Comment)   -- the five columns written
SELECT  @CustomerId, @MedicineId, @OrderId, @Rating, @Comment   -- no FROM: the row IS the parameters
-- EXISTS stops at the first matching row, and one order line is proof enough.
WHERE   EXISTS (SELECT 1   -- SELECT 1, because only whether a row exists matters
                FROM   OrderItems oi   -- the lines of the order being cited
                       INNER JOIN Orders o ON o.OrderId = oi.OrderId   -- reaches Status and CustomerId
                WHERE  oi.OrderId    = @OrderId       -- that order
                  AND  oi.MedicineId = @MedicineId    -- really contained this medicine
                  AND  o.CustomerId  = @CustomerId    -- and the order was YOURS
                  -- Delivered, so nothing can be rated before it has actually arrived.
                  AND  o.Status      = 'Delivered');";

            // Wrapped because the UNIQUE index throws rather than returning zero rows.
            try
            {
                int rows = _db.ExecuteNonQuery(sql,   // rows affected: 1 wrote, 0 was refused
                    DbHelper.P("@CustomerId", customerId),   // who is rating
                    DbHelper.P("@MedicineId", medicineId),   // what is being rated
                    DbHelper.P("@OrderId", orderId),   // the order that has to prove it
                    DbHelper.P("@Rating", rating),   // 1 to 5, held by a CHECK constraint
                    DbHelper.P("@Comment", comment));   // nullable: a rating with no words is fine

                // Exactly one row means the EXISTS was satisfied and the review is stored.
                if (rows == 1)
                {
                    message = "Thank you, your review has been posted.";   // the caller shows this
                    return true;   // true means the row really was written
                }

                // rows is 0: the statement ran perfectly and deliberately wrote nothing.
                message = "You can only review a medicine from an order that has been delivered to you.";
                return false;   // naming which test failed would leak other people's orders
            }
            catch (Exception ex)   // only a constraint violation can reach here
            {
                // 2627 is a UNIQUE constraint and 2601 a unique index: the same purchase twice.
                if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx &&
                    (sqlEx.Number == 2627 || sqlEx.Number == 2601))   // the duplicate-review numbers
                    message = "You have already reviewed this medicine on this order.";   // said plainly
                else   // anything else is passed through rather than swallowed
                    message = ex.Message;   // DbHelper already made this a readable sentence
                return false;   // nothing was stored on either branch
            }
        }

        /// <summary>The unreviewed medicines on one delivered order.</summary>
        public DataTable GetReviewableItems(int orderId, int customerId)
        {
            // The same four conditions as AddReview, so the list only offers what it accepts.
            const string sql = @"
SELECT  m.MedicineId, m.MedicineName, m.Strength, oi.Quantity, oi.UnitPrice   -- what the list shows
FROM    OrderItems oi   -- driven from the order lines: 'what did this order contain'
        INNER JOIN Orders    o ON o.OrderId    = oi.OrderId   -- reaches the customer and status
        INNER JOIN Medicines m ON m.MedicineId = oi.MedicineId   -- resolves a line to a product
WHERE   oi.OrderId   = @OrderId   -- the single order being reviewed
  AND   o.CustomerId = @CustomerId      -- the order has to be the caller's own
  AND   o.Status     = 'Delivered'      -- and delivered, exactly as AddReview requires
  -- NOT EXISTS drops lines already reviewed; a JOIN could duplicate one.
  AND   NOT EXISTS (SELECT 1 FROM Reviews r   -- the anti-join that shrinks the list
                    WHERE r.OrderId    = oi.OrderId   -- the three columns the UNIQUE index covers
                      AND r.MedicineId = oi.MedicineId   -- so this matches what it would refuse
                      AND r.CustomerId = @CustomerId)   -- only this customer's own reviews count
-- Alphabetical, so the customer works down a stable list.
ORDER BY m.MedicineName;";

            return _db.ExecuteTable(sql,   // one round trip, bound straight to the list
                DbHelper.P("@OrderId", orderId),   // which order is being reviewed
                DbHelper.P("@CustomerId", customerId));   // and whose order it has to be
        }

        /// <summary>The visible reviews for one medicine, newest first.</summary>
        public DataTable GetForMedicine(int medicineId)   // requirement 23
        {
            // Reviews store a CustomerId, so the join to Users is what puts a name on screen.
            const string sql = @"
SELECT  r.ReviewId, u.FullName AS ReviewerName, r.Rating, r.Comment, r.ReviewDate   -- the reader's view
FROM    Reviews r   -- the question is 'what was said about this medicine'
        INNER JOIN Users     u ON u.UserId     = r.CustomerId   -- turns the id into a name
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId   -- joined for the constraint, not a column
WHERE   r.MedicineId = @MedicineId   -- one medicine only
  -- IsHidden = 0: the moderation filter, so hiding clears every customer screen.
  AND   r.IsHidden   = 0
-- Newest first, which is what a reader of reviews expects.
ORDER BY r.ReviewDate DESC;";

            return _db.ExecuteTable(sql, DbHelper.P("@MedicineId", medicineId));   // one parameter, one trip
        }

        /// <summary>The star rating printed beside a medicine.</summary>
        public decimal GetAverageForMedicine(int medicineId)
        {
            // ExecuteScalarDecimal, because one number comes back rather than a grid.
            return _db.ExecuteScalarDecimal(@"
-- Rating is an INT, so the inner CAST is what stops AVG doing integer arithmetic.
SELECT ISNULL(CAST(AVG(CAST(Rating AS DECIMAL(4,2))) AS DECIMAL(4,2)), 0)   -- no reviews means 0, not NULL
-- IsHidden = 0 here too, so hiding stops a review counting at once.
FROM   Reviews WHERE MedicineId = @Id AND IsHidden = 0;",
                DbHelper.P("@Id", medicineId));   // one medicine, one number
        }

        // ===== PHARMACY OWNER: read only by design =====

        // A shop that could delete its own bad reviews would make every rating worthless.
        public DataTable GetForPharmacy(int pharmacyId, int minRating, int maxRating)
        {
            // Reviews has no PharmacyId, so Medicines is the bridge from a review to a shop.
            const string sql = @"
SELECT  r.ReviewId, u.FullName AS ReviewerName, m.MedicineName, m.Strength,   -- who said it, about what
        r.Rating, r.Comment, r.ReviewDate, r.OrderId   -- OrderId traces it back to a real delivery
FROM    Reviews r   -- one row per review written about this shop's stock
        INNER JOIN Users     u ON u.UserId     = r.CustomerId   -- the reviewer's name
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId   -- the bridge carrying PharmacyId
WHERE   m.PharmacyId = @PharmacyId   -- the isolation rule: this shop's medicines only
  AND   r.IsHidden   = 0   -- so the owner sees exactly what customers see
  -- A range: 'all' is 1 to 5, 'complaints' 1 to 2; BETWEEN includes both.
  AND   r.Rating BETWEEN @MinRating AND @MaxRating
-- Newest first, so a fresh complaint sits at the top.
ORDER BY r.ReviewDate DESC;";

            return _db.ExecuteTable(sql,   // one round trip for the whole grid
                DbHelper.P("@PharmacyId", pharmacyId),   // from UserSession, never from a control
                DbHelper.P("@MinRating", minRating),   // the bottom of the chosen band
                DbHelper.P("@MaxRating", maxRating));   // and the top of it
        }

        /// <summary>The shop's overall score, for the dashboard and listing.</summary>
        public decimal GetAverageForPharmacy(int pharmacyId)
        {
            // Same double CAST and ISNULL as the per-medicine average, for the same reasons.
            return _db.ExecuteScalarDecimal(@"
SELECT  ISNULL(CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2)), 0)   -- integer AVG would truncate
FROM    Reviews r   -- every review the shop has received
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId   -- the bridge to PharmacyId again
-- Averaged across every medicine the shop sells, hidden reviews excluded.
WHERE   m.PharmacyId = @Id AND r.IsHidden = 0;",
                DbHelper.P("@Id", pharmacyId));   // one shop, one number
        }

        // How many reviews the average is built from; 5.0 from one proves little.
        public int CountForPharmacy(int pharmacyId)
        {
            // ExecuteScalarInt, because a count is a whole number and never a money value.
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)   -- counting avoids shipping the rows themselves back
FROM    Reviews r INNER JOIN Medicines m ON m.MedicineId = r.MedicineId   -- the same two tables
-- The same WHERE as GetAverageForPharmacy, so both cover one set of rows.
WHERE   m.PharmacyId = @Id AND r.IsHidden = 0;",
                DbHelper.P("@Id", pharmacyId));   // the same scope as the average above
        }

        // ===== SUPER ADMIN MODERATION (requirement 8) =====

        /// <summary>The moderation queue, worst and reported reviews first.</summary>
        public DataTable GetModerationQueue(int maxRating, bool includeHidden)
        {
            // Every row carries the order number, so a review always traces to a delivery.
            const string sql = @"
SELECT  r.ReviewId, u.FullName AS Reviewer, m.MedicineName, ph.PharmacyName,   -- who, what, which shop
        -- IsHidden shows rows already dealt with, IsReported ones an owner sent in.
        r.Rating, r.Comment, r.ReviewDate, r.OrderId, r.IsHidden, r.IsReported
FROM    Reviews r   -- the queue is a list of reviews, so Reviews drives it
        INNER JOIN Users      u  ON u.UserId     = r.CustomerId   -- the reviewer's name
        INNER JOIN Medicines  m  ON m.MedicineId = r.MedicineId   -- what the review is about
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId   -- three deep, to name the shop
-- A threshold: 2 covers 1 and 2 stars, and a report queues at any rating.
WHERE   (r.Rating <= @MaxRating OR r.IsReported = 1)
  -- The optional filter reversed: 1 shows hidden rows too, 0 hides them.
  AND   (@IncludeHidden = 1 OR r.IsHidden = 0)
-- Reported first, because an owner is waiting on a decision about them.
ORDER BY r.IsReported DESC, r.ReviewDate DESC;";

            return _db.ExecuteTable(sql,   // one round trip that fills the whole queue grid
                DbHelper.P("@MaxRating", maxRating),   // the severity band chosen on the form
                DbHelper.P("@IncludeHidden", includeHidden ? 1 : 0));   // BIT, so the tick box becomes 1 or 0
        }

        /// <summary>Hiding sets IsHidden to 1; the row is never deleted.</summary>
        public bool SetHidden(int reviewId, bool hidden)
        {
            // One statement both ways, so hiding and unhiding cannot get out of step.
            return _db.ExecuteNonQuery(
                // IsReported returns to 0 either way: deciding IS the answer to the report.
                "UPDATE Reviews SET IsHidden = @Hidden, IsReported = 0 WHERE ReviewId = @Id;",
                DbHelper.P("@Hidden", hidden ? 1 : 0),   // the same method hides and restores
                DbHelper.P("@Id", reviewId)) == 1;   // a primary key, so == 1 is the write and its proof
        }

        /// <summary>An owner flags one review about their own shop for the admin.</summary>
        public bool Report(int reviewId, int pharmacyId)
        {
            // It only sets IsReported, so the review stays visible until the admin decides.
            return _db.ExecuteNonQuery(@"
-- UPDATE ... FROM with a join, because Reviews has no PharmacyId of its own.
UPDATE  r   -- the alias, not the table name, is what UPDATE ... FROM targets
SET     r.IsReported = 1   -- the flag the Super Admin's queue orders on
FROM    Reviews r   -- the rows being updated
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId   -- ties a review to a shop
WHERE   r.ReviewId   = @Id   -- the single review the owner selected
  AND   m.PharmacyId = @PharmacyId   -- an owner can only report reviews about their own stock
  -- A hidden review has already been dealt with, so there is nothing left to report.
  AND   r.IsHidden   = 0;",
                DbHelper.P("@Id", reviewId),   // which review is being flagged
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // == 1 proves the row was this shop's
        }
    }
}
