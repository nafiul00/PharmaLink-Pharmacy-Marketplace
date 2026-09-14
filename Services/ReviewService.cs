using System.Data;                  // DataTable, the shape every read on these screens returns
using PharmaLinkApp.Database;       // DbHelper, the only class that opens a SqlConnection

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
        // One helper for the whole class; DbHelper opens and closes a connection per call.
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
        // out string message, like the other methods a customer can legitimately fail:
        // "you have not bought this" is not an error condition, it is an answer, so it
        // comes back as false plus a sentence rather than as a thrown exception.
        public bool AddReview(int customerId, int medicineId, int orderId, int rating, string comment, out string message)
        {
            const string sql = @"
-- INSERT ... SELECT ... WHERE EXISTS, not INSERT ... VALUES. The row is only written
-- if the EXISTS proves the purchase, so the check and the write are one statement and
-- cannot drift apart. If the proof fails, SELECT returns no rows, nothing is inserted
-- and ExecuteNonQuery returns 0 - no exception, just a refusal.
INSERT INTO Reviews (CustomerId, MedicineId, OrderId, Rating, Comment)
-- A SELECT with no FROM: the row being inserted is the five parameters themselves, and
-- the WHERE below decides whether that single row exists at all. Reading the order
-- first and then inserting was rejected - between the two the order could be cancelled,
-- and the review would be written against a purchase that no longer stands.
SELECT  @CustomerId, @MedicineId, @OrderId, @Rating, @Comment
-- EXISTS stops at the first matching row rather than counting them, so it is both the
-- cheapest way to ask the question and the right one: one line on the order is proof.
WHERE   EXISTS (SELECT 1
                -- SELECT 1, not SELECT *: EXISTS only cares whether a row came back, so
                -- there is no reason to make the server materialise any columns.
                FROM   OrderItems oi
                       INNER JOIN Orders o ON o.OrderId = oi.OrderId
                -- All four conditions together are what makes a review VERIFIED:
                WHERE  oi.OrderId    = @OrderId       -- that order
                  AND  oi.MedicineId = @MedicineId    -- really contained this medicine
                  AND  o.CustomerId  = @CustomerId    -- and the order was YOURS
                  AND  o.Status      = 'Delivered');";   // and it actually arrived

            // Wrapped because ONE failure here is not a refusal but a constraint: the
            // UNIQUE index on (CustomerId, MedicineId, OrderId) throws rather than
            // returning 0, so it cannot be handled by the rows check below.
            try
            {
                // The count is captured rather than compared inline, because it has to be
                // tested twice in effect - once for success and once for the message.
                int rows = _db.ExecuteNonQuery(sql,
                    DbHelper.P("@CustomerId", customerId),
                    DbHelper.P("@MedicineId", medicineId),
                    DbHelper.P("@OrderId", orderId),
                    // The 1 to 5 range is enforced by a CHECK constraint on the column,
                    // so an out-of-range value is refused by the database rather than
                    // depending on this method remembering to test it.
                    DbHelper.P("@Rating", rating),
                    // Not trimmed and not rejected when empty: a rating with no words is a
                    // perfectly ordinary review, and the column allows NULL.
                    DbHelper.P("@Comment", comment));

                // Exactly one row means the EXISTS was satisfied and the review is stored.
                if (rows == 1)
                {
                    message = "Thank you, your review has been posted.";
                    return true;
                }

                // Reached when rows is 0: the statement ran perfectly and deliberately
                // wrote nothing. The message covers all four ways the proof can fail at
                // once, because telling a customer WHICH test failed would confirm the
                // existence of orders that are not theirs.
                message = "You can only review a medicine from an order that has been delivered to you.";
                return false;
            }
            catch (Exception ex)
            {
                // The UNIQUE constraint fires when the same purchase is rated twice.
                // DbHelper has already replaced the message with a general sentence, so the
                // constraint name is no longer in ex.Message. The original SqlException is
                // kept as InnerException, and its error number is what identifies a
                // duplicate: 2627 for a UNIQUE constraint, 2601 for a unique index.
                if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx &&
                    (sqlEx.Number == 2627 || sqlEx.Number == 2601))
                    message = "You have already reviewed this medicine on this order.";
                else
                    // Anything else is passed through rather than swallowed. It is already
                    // a readable sentence, because DbHelper wrapped the SqlException in a
                    // DataAccessException before it reached this catch.
                    message = ex.Message;
                return false;
            }
        }

        /// <summary>The medicines on one delivered order that have not been reviewed yet.</summary>
        // This is what the form uses to populate its list, so the customer is only ever
        // offered items AddReview would accept. The button being enabled and the insert
        // succeeding are therefore decided by the same four conditions.
        public DataTable GetReviewableItems(int orderId, int customerId)
        {
            const string sql = @"
SELECT  m.MedicineId, m.MedicineName, m.Strength, oi.Quantity, oi.UnitPrice
-- Driven from OrderItems rather than from Medicines: the question is 'what did this
-- order contain', so the order lines are the starting point and the medicine is looked
-- up from each one.
FROM    OrderItems oi
        INNER JOIN Orders    o ON o.OrderId    = oi.OrderId
        INNER JOIN Medicines m ON m.MedicineId = oi.MedicineId
WHERE   oi.OrderId   = @OrderId
  AND   o.CustomerId = @CustomerId      -- the order has to be the caller's own
  AND   o.Status     = 'Delivered'      -- and delivered, exactly as AddReview requires
  -- NOT EXISTS removes the lines already reviewed, so the list shrinks as the customer
  -- works through it. A LEFT JOIN with a null test would do the same job but would
  -- duplicate an order line if it somehow had two matching reviews; NOT EXISTS cannot.
  AND   NOT EXISTS (SELECT 1 FROM Reviews r
                    -- The same three columns the UNIQUE constraint covers, so 'already
                    -- reviewed' here means exactly what the constraint would refuse.
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
        -- Medicines is joined for the constraint rather than for a column: it guarantees
        -- the review points at a medicine that still exists, so a row with a dangling
        -- reference cannot reach the screen.
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
WHERE   r.MedicineId = @MedicineId
  -- The moderation filter. Hidden reviews are excluded by the QUERY, so a review the
  -- administrator has hidden disappears from every customer screen at once without the
  -- row being destroyed.
  AND   r.IsHidden   = 0
-- Newest first, which is what a reader of reviews expects.
ORDER BY r.ReviewDate DESC;";

            return _db.ExecuteTable(sql, DbHelper.P("@MedicineId", medicineId));
        }

        // The star rating printed beside a medicine.
        public decimal GetAverageForMedicine(int medicineId)
        {
            return _db.ExecuteScalarDecimal(@"
-- The inner CAST is the one that matters: Rating is an INT, and AVG over integers does
-- integer arithmetic, so four fives and one four would average to 4 rather than 4.80.
-- Casting each rating to DECIMAL first is what keeps the fraction. The outer CAST then
-- rounds the result to two places so the figure is stable wherever it is displayed.
-- ISNULL turns 'no reviews yet' into 0: AVG over no rows is NULL, not zero, and that
-- NULL would otherwise have to be handled again on the C# side.
SELECT ISNULL(CAST(AVG(CAST(Rating AS DECIMAL(4,2))) AS DECIMAL(4,2)), 0)
-- IsHidden = 0 here as well, so a hidden review stops counting towards the average the
-- moment it is hidden. This is the real reason hiding is a flag and not a deletion.
FROM   Reviews WHERE MedicineId = @Id AND IsHidden = 0;",
                DbHelper.P("@Id", medicineId));
        }

        // ---------------------------------------------------------------------
        //  PHARMACY OWNER  (requirement 15 - read only by design)
        // ---------------------------------------------------------------------
        // There is no update or delete for an owner anywhere in this class. A shop that
        // could remove its own bad reviews would make every remaining review worthless,
        // so the owner's side of this file is deliberately read-only and moderation is
        // left to the Super Admin methods at the bottom.

        public DataTable GetForPharmacy(int pharmacyId, int minRating, int maxRating)
        {
            const string sql = @"
SELECT  r.ReviewId, u.FullName AS ReviewerName, m.MedicineName, m.Strength,
        -- OrderId is carried so the owner can trace a complaint back to the delivery it
        -- came from rather than having to take it at face value.
        r.Rating, r.Comment, r.ReviewDate, r.OrderId
FROM    Reviews r
        INNER JOIN Users     u ON u.UserId     = r.CustomerId
        -- Medicines is the bridge to the shop: Reviews has no PharmacyId, so 'is this
        -- review about my stock?' is asked of the medicine it was written about.
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
WHERE   m.PharmacyId = @PharmacyId
  AND   r.IsHidden   = 0
  -- A range rather than a single value, so one query serves 'all reviews' as 1 to 5 and
  -- 'complaints only' as 1 to 2. BETWEEN is inclusive at both ends.
  AND   r.Rating BETWEEN @MinRating AND @MaxRating
ORDER BY r.ReviewDate DESC;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@MinRating", minRating),
                DbHelper.P("@MaxRating", maxRating));
        }

        // The shop's overall score, for the owner's dashboard and the pharmacy listing.
        public decimal GetAverageForPharmacy(int pharmacyId)
        {
            return _db.ExecuteScalarDecimal(@"
-- Same double CAST and same ISNULL as the per-medicine average, for the same two
-- reasons: integer AVG would truncate, and AVG over no rows is NULL rather than 0.
SELECT  ISNULL(CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2)), 0)
FROM    Reviews r
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
-- Averaged across every medicine the shop sells, and hidden reviews excluded here too
-- so a moderated review cannot drag a shop's score down.
WHERE   m.PharmacyId = @Id AND r.IsHidden = 0;",
                DbHelper.P("@Id", pharmacyId));
        }

        // How many reviews that average is built from. Shown beside it, because 5.0 from
        // one review and 5.0 from two hundred are not the same claim.
        public int CountForPharmacy(int pharmacyId)
        {
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)
FROM    Reviews r INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
-- The identical WHERE clause to GetAverageForPharmacy, so the count and the average are
-- always computed over exactly the same set of rows.
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
        -- IsHidden is selected as well as filtered on, so the grid can show which rows
        -- have already been dealt with when the administrator is viewing everything.
        -- IsReported shows which rows a pharmacy owner has flagged from Customer Reviews.
        r.Rating, r.Comment, r.ReviewDate, r.OrderId, r.IsHidden, r.IsReported
FROM    Reviews r
        INNER JOIN Users      u  ON u.UserId     = r.CustomerId
        INNER JOIN Medicines  m  ON m.MedicineId = r.MedicineId
        -- Three joins deep, because the administrator needs to know which SHOP a review
        -- concerns and that is only reachable through the medicine.
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
-- Low ratings first as a threshold rather than an exact match: moderation is about
-- complaints, and passing 2 brings back the 1 and 2 star reviews together.
-- A reported review is always in the queue whatever its rating, because an owner can
-- report a five star review too and the Super Admin still has to see it.
WHERE   (r.Rating <= @MaxRating OR r.IsReported = 1)
  -- The optional filter pattern, reversed: 1 means 'show hidden ones too', 0 leaves only
  -- the visible ones. This is how the administrator reviews a decision already taken.
  AND   (@IncludeHidden = 1 OR r.IsHidden = 0)
-- Reported reviews first, because someone is waiting on a decision about them.
ORDER BY r.IsReported DESC, r.ReviewDate DESC;";

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
                // One statement for both directions, so hiding and unhiding cannot get
                // out of step. No PharmacyId in the WHERE, unlike every owner method in
                // this file - moderation is a Super Admin power and is deliberately not
                // scoped to a shop; the caller's role is what authorises it.
                // IsReported goes back to 0 either way: hiding or restoring IS the Super
                // Admin's decision on the report, so the review leaves the reported list.
                "UPDATE Reviews SET IsHidden = @Hidden, IsReported = 0 WHERE ReviewId = @Id;",
                DbHelper.P("@Hidden", hidden ? 1 : 0),
                // The primary key, so this can affect at most one row and == 1 below is
                // both the write and the proof that the review existed.
                DbHelper.P("@Id", reviewId)) == 1;
        }

        /// <summary>
        /// A pharmacy owner flags a review about their own shop for the Super Admin.
        /// It only sets IsReported; the review stays visible until the Super Admin
        /// decides, so an owner can never remove a review themselves.
        /// </summary>
        public bool Report(int reviewId, int pharmacyId)
        {
            return _db.ExecuteNonQuery(@"
-- UPDATE ... FROM with a join, because Reviews has no PharmacyId: the medicine is what
-- ties a review to a shop. The PharmacyId test means an owner can only report reviews
-- about their own medicines, even if a different ReviewId were somehow passed in.
UPDATE  r
SET     r.IsReported = 1
FROM    Reviews r
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
WHERE   r.ReviewId   = @Id
  AND   m.PharmacyId = @PharmacyId
  -- A hidden review has already been dealt with, so there is nothing left to report.
  AND   r.IsHidden   = 0;",
                DbHelper.P("@Id", reviewId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }
    }
}
