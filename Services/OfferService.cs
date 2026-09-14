using System.Data;                  // DataTable, the shape every read on this screen returns
using PharmaLinkApp.Database;       // DbHelper, the only class that opens a SqlConnection

namespace PharmaLinkApp.Services    // every service sits here, so a form needs one using line
{
    // Called by DiscountOffersForm (owner) and CustomerOffersForm (customer).

    /// <summary>Time limited discounts, priced inside the SQL.</summary>
    public class OfferService
    {
        // DbHelper opens and closes a connection per call, so there is no shared state.
        private readonly DbHelper _db = new DbHelper();

        // ------------------------------- CUSTOMER ----------------------------

        /// <summary>Live offers only: in date, in stock, at an approved shop.</summary>
        public DataTable GetActiveOffers(int categoryId, string area)
        {
            // A const local, so the query and the call that runs it are read together.
            const string sql = @"
-- One read builds the whole card: offer, medicine, category and shop.
SELECT  o.OfferId, o.OfferTitle, m.MedicineName, m.Strength, c.CategoryName,   -- what the card names
        ph.PharmacyName, ph.Area,                                              -- Area drives the second filter
        m.UnitPrice                                                          AS OriginalPrice,   -- struck through on the card
        o.DiscountPercent,                                                   -- so the card can headline '20% off'
        -- 100.0, not 100: integers would floor the percentage to 0 and charge full price.
        CAST(m.UnitPrice * (1 - o.DiscountPercent / 100.0) AS DECIMAL(10,2)) AS DiscountedPrice,   -- rounded to pennies once, here
        CAST(m.UnitPrice * (o.DiscountPercent / 100.0) AS DECIMAL(10,2))     AS YouSave,   -- the mirror, so the two always add up
        o.EndDate,        -- shown as 'ends on', so the customer can see the deadline
        m.MedicineId,     -- carried so the card's Add to cart button has a key to use
        m.Stock           -- lets the card show how many are left without a second query
FROM    Offers o          -- the driving table: one row per campaign
        -- INNER JOIN throughout: a row that fails any join is not displayable anyway.
        INNER JOIN Medicines  m  ON m.MedicineId  = o.MedicineId
        INNER JOIN Categories c  ON c.CategoryId  = m.CategoryId   -- a discount inherits the medicine's category
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId   -- the only source of Status and Area
WHERE   o.IsActive  = 1                                   -- the owner has not paused it
  AND   m.IsActive  = 1                                   -- and the medicine is not delisted
  -- CAST drops the time, or an offer ending today dies at midnight.
  AND   CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate
  AND   m.Stock     > 0                                   -- nothing advertised that cannot be bought
  AND   ph.Status   = 'Approved'                          -- and no shop still awaiting approval
  AND   (@CategoryId = 0  OR m.CategoryId = @CategoryId)  -- 0 means 'no filter', so one query serves both
  AND   (@Area       = '' OR ph.Area      = @Area)        -- '' is the text column's version of the same
ORDER BY o.DiscountPercent DESC;";   // biggest discount first, which is what the customer came for

            return _db.ExecuteTable(sql,                          // one round trip, straight into a DataTable
                DbHelper.P("@CategoryId", categoryId),            // 0 arrives here when the filter is on 'All'
                // ?? "" because SQL NULL = '' is not true, so a null would match nothing.
                DbHelper.P("@Area", area ?? ""));
        }

        // A separate COUNT, so a badge does not drag a whole result set across the wire.
        public int CountActiveOffers()
        {
            // ExecuteScalarInt reads one cell, so no DataTable is built for one number.
            return _db.ExecuteScalarInt(@"
-- COUNT(*) counts rows, not values, so no NULL column can skew the total.
SELECT  COUNT(*)
FROM    Offers o          -- Offers drives again, so the badge counts the same rows as the page
        INNER JOIN Medicines  m  ON m.MedicineId  = o.MedicineId    -- the bridge: Offers has no PharmacyId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId    -- joined purely for Status
WHERE   o.IsActive = 1 AND ph.Status = 'Approved'          -- the same live tests as the page
  AND   CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate;");   // and the date window closes it
            // No parameters: DbHelper is happy with an empty params array.
        }

        // ---------------------------- PHARMACY OWNER -------------------------

        /// <summary>The owner's offers, paused and expired ones included.</summary>
        public DataTable GetForPharmacy(int pharmacyId)
        {
            // A different query from the customer's, because the owner needs dead ones too.
            const string sql = @"
-- The offer's columns plus just enough of the medicine to say what it discounts.
SELECT  o.OfferId, o.OfferTitle, m.MedicineName, m.Strength,
        m.UnitPrice                                                          AS OriginalPrice,   -- aliased, so the header reads as a comparison
        o.DiscountPercent,                                                   -- echoed back, so he can check what he set
        -- The same expression as the customer query, so the owner sees their price.
        CAST(m.UnitPrice * (1 - o.DiscountPercent / 100.0) AS DECIMAL(10,2)) AS DiscountedPrice,   -- never worked out a second way
        o.StartDate, o.EndDate, o.IsActive,   -- the raw values, because the edit dialog loads from them
        -- CASE stops at the first match, so a paused offer always reads 'Paused'.
        CASE WHEN o.IsActive = 0 THEN 'Paused'
             WHEN CAST(GETDATE() AS DATE) <  o.StartDate THEN 'Scheduled'   -- switched on, but not live yet
             WHEN CAST(GETDATE() AS DATE) >  o.EndDate   THEN 'Expired'     -- finished, and kept only as history
             ELSE 'Running' END                                              AS OfferState   -- nothing left, so ELSE is the live case
FROM    Offers o          -- the owner is looking at his campaigns, not his medicines
        INNER JOIN Medicines m ON m.MedicineId = o.MedicineId   -- the only path from an offer to a PharmacyId
WHERE   m.PharmacyId = @PharmacyId          -- THE ownership filter, reached through the medicine
ORDER BY o.StartDate DESC;";   // newest first, and expired ones are kept so they can be reused

            return _db.ExecuteTable(sql, DbHelper.P("@PharmacyId", pharmacyId));   // the id comes from the session
        }

        /// <summary>Creates an offer only on the owner's own medicine.</summary>
        public bool Create(int medicineId, int pharmacyId, string title, decimal discountPercent,
                           DateTime startDate, DateTime endDate)   // DateTime here, DATE in the table; .Date bridges them
        {
            // The permission check lives in the SQL, so the body below is only binding.
            const string sql = @"
-- INSERT ... SELECT, so the ownership test and the write are one statement.
INSERT INTO Offers (MedicineId, OfferTitle, DiscountPercent, StartDate, EndDate)
SELECT  m.MedicineId, @Title, @Percent, @StartDate, @EndDate   -- the id comes from the row that was found
FROM    Medicines m       -- read purely to authorise the write
-- Not his medicine means no rows, so nothing is inserted at all.
WHERE   m.MedicineId = @MedicineId AND m.PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,                        // returns rows affected, the success test below
                DbHelper.P("@Title", title.Trim()),                // trimmed, so a stray space is not stored
                // decimal, not double: this percentage feeds a money calculation.
                DbHelper.P("@Percent", discountPercent),
                // .Date strips the time, or an offer would not start until the afternoon.
                DbHelper.P("@StartDate", startDate.Date),
                DbHelper.P("@EndDate", endDate.Date),              // the same, so an offer runs to the end of its day
                DbHelper.P("@MedicineId", medicineId),             // which medicine to discount
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // exactly one row written, so it was his
        }

        /// <summary>Edits an offer; the join proves it belongs to this shop.</summary>
        public bool Update(int offerId, int pharmacyId, string title, decimal discountPercent,
                           DateTime startDate, DateTime endDate)   // the four editable fields; MedicineId is not one
        {
            // One statement, because a separate check and update could be overtaken.
            const string sql = @"
-- The alias after UPDATE names which table of the join is written to.
UPDATE  o
SET     o.OfferTitle = @Title, o.DiscountPercent = @Percent,   -- set together, so the grid never shows half an edit
        o.StartDate = @StartDate, o.EndDate = @EndDate         -- both ends rewritten, so the SET list stays fixed
-- MedicineId is not in the SET list: moving it would skip the create check.
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
WHERE   o.OfferId = @OfferId AND m.PharmacyId = @PharmacyId;";   // a guessed OfferId updates nothing

            return _db.ExecuteNonQuery(sql,                        // 0 rows back means 'not yours', not 'error'
                DbHelper.P("@Title", title.Trim()),                // trimmed on the way in, exactly as in Create
                DbHelper.P("@Percent", discountPercent),           // decimal all the way to the column
                DbHelper.P("@StartDate", startDate.Date),          // time stripped, so the DATE column stores what was meant
                DbHelper.P("@EndDate", endDate.Date),              // likewise, so the offer survives its final day
                DbHelper.P("@OfferId", offerId),                   // which offer to edit
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;       // and the proof it belongs to this shop
        }

        /// <summary>Pausing keeps the row, so it can be switched on again.</summary>
        public bool SetActive(int offerId, int pharmacyId, bool active)
        {
            // One column changes, so the campaign keeps everything the owner typed.
            const string sql = @"
-- A flag, not a delete, so switching back on is one click instead of retyping.
UPDATE  o SET o.IsActive = @Active
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId   -- the same shape as Update
WHERE   o.OfferId = @OfferId AND m.PharmacyId = @PharmacyId;";   // the same pair of tests that guard an edit

            return _db.ExecuteNonQuery(sql,                        // one row when the offer was his, zero when not
                DbHelper.P("@Active", active ? 1 : 0),             // a BIT column, so the bool becomes 1 or 0
                DbHelper.P("@OfferId", offerId),                   // which campaign to pause or resume
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;       // the ownership half of the WHERE
        }

        /// <summary>Removes an offer outright; nothing else points at one.</summary>
        public bool Delete(int offerId, int pharmacyId)
        {
            // The only hard delete here, and the SQL below says why it is safe.
            const string sql = @"
-- Safe here: past orders store the price that was actually charged.
DELETE  o
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId   -- the join only reaches PharmacyId
WHERE   o.OfferId = @OfferId AND m.PharmacyId = @PharmacyId;";   // another shop's OfferId deletes nothing

            return _db.ExecuteNonQuery(sql,                        // == 1 is both the delete and the proof
                DbHelper.P("@OfferId", offerId),                   // the campaign to remove
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;       // and the shop that must own it
        }

        // The owner's dashboard tile. 'Running' means what the CASE above means by it.
        public int CountRunningForPharmacy(int pharmacyId)
        {
            // A scalar read again: the tile wants a number, so nothing builds a table.
            return _db.ExecuteScalarInt(@"
-- Rows, not values, so a NULL column cannot quietly lower the tile's figure.
SELECT  COUNT(*)
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId   -- the usual route to PharmacyId
WHERE   m.PharmacyId = @Id AND o.IsActive = 1   -- his shop and switched on
  AND   CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate;",   // inclusive, so first and last day count
                DbHelper.P("@Id", pharmacyId));                    // named @Id, because this query has only one
        }
    }
}
