using System.Data;                  // DataTable, the shape every read on this screen returns
using PharmaLinkApp.Database;       // DbHelper, the only class that opens a SqlConnection

namespace PharmaLinkApp.Services
{
    // -------------------------------------------------------------------------
    //  Layer: service.  Called by DiscountOffersForm (the owner's side) and
    //  CustomerOffersForm (the customer's side). All access via DbHelper.
    //
    //  The discounted price is calculated inside each SQL query rather than in
    //  C#, so the offers screen, the medicine details screen, the cart and the
    //  invoice all print the same figure.
    //
    //  Create, Update, SetActive and Delete each join Medicines and filter on
    //  PharmacyId, so an owner cannot create or change a discount on another
    //  shop's medicine: if the medicine is not his, no row is affected.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Time limited percentage discounts (requirements 14 and 29).
    ///
    /// The discounted price is always calculated inside the SQL query, never in
    /// C#, so the offers screen, the medicine details screen, the cart and the
    /// invoice all print the same number with no chance of the four disagreeing.
    /// </summary>
    public class OfferService
    {
        // One helper for the whole class. DbHelper opens and closes a connection inside
        // every call, so there is no shared state here to go wrong, and readonly means
        // nothing can swap it out later.
        private readonly DbHelper _db = new DbHelper();

        // ---------------------------------------------------------------------
        //  CUSTOMER
        // ---------------------------------------------------------------------

        /// <summary>
        /// Requirement 29. Only offers where today falls between StartDate and
        /// EndDate, on a medicine that is in stock, at a pharmacy that is
        /// Approved. Expired offers are filtered out by the query rather than by
        /// the form, so nothing stale can ever be displayed.
        /// </summary>
        public DataTable GetActiveOffers(int categoryId, string area)
        {
            const string sql = @"
SELECT  o.OfferId, o.OfferTitle, m.MedicineName, m.Strength, c.CategoryName,
        ph.PharmacyName, ph.Area,
        -- The shelf price, carried alongside the discounted one so the card can strike
        -- it through and the saving is visible rather than merely asserted.
        m.UnitPrice                                                          AS OriginalPrice,
        o.DiscountPercent,
        -- 100.0, not 100. Both operands would otherwise be integers and SQL Server would
        -- do integer division, flooring every percentage to 0 and quietly charging the
        -- full price on a discounted item. The decimal point is what forces real division.
        -- CAST to DECIMAL(10,2) rounds to pennies once, here, so the same figure reaches
        -- the offers screen, the cart and the invoice.
        CAST(m.UnitPrice * (1 - o.DiscountPercent / 100.0) AS DECIMAL(10,2)) AS DiscountedPrice,
        -- The mirror of the line above: this multiplies by the percentage rather than by
        -- what is left of it, so OriginalPrice always equals DiscountedPrice plus YouSave
        -- and the two numbers on the card can never contradict each other.
        CAST(m.UnitPrice * (o.DiscountPercent / 100.0) AS DECIMAL(10,2))     AS YouSave,
        o.EndDate,        -- shown as 'ends on', so the customer can see the deadline
        m.MedicineId,     -- carried so the card's Add to cart button has a key to use
        m.Stock           -- lets the card show how many are left without a second query
FROM    Offers o
        -- INNER JOIN throughout: an offer with no medicine, no category or no pharmacy
        -- is not displayable, so a row that fails any join should genuinely disappear.
        INNER JOIN Medicines  m  ON m.MedicineId  = o.MedicineId
        INNER JOIN Categories c  ON c.CategoryId  = m.CategoryId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
WHERE   o.IsActive  = 1                                   -- the owner has not paused it
  AND   m.IsActive  = 1                                   -- and the medicine is not delisted
  -- CAST(GETDATE() AS DATE) drops the time of day. Without it, an offer ending today
  -- would stop matching at one second past midnight this morning, because GETDATE()
  -- carries a time and EndDate does not - so the last day of every offer would be lost.
  AND   CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate
  AND   m.Stock     > 0                                   -- nothing advertised that cannot be bought
  AND   ph.Status   = 'Approved'                          -- and no shop still awaiting approval
  -- The optional filter pattern: 0 and '' mean 'no filter', so the same query serves
  -- the unfiltered page and both drop-downs. Building the WHERE clause in C# was
  -- rejected - that is exactly the string concatenation this project avoids.
  AND   (@CategoryId = 0  OR m.CategoryId = @CategoryId)
  AND   (@Area       = '' OR ph.Area      = @Area)
-- Biggest discount first, because that is what the customer came to this screen for.
ORDER BY o.DiscountPercent DESC;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@CategoryId", categoryId),
                // ?? "" turns a null area into the empty string the query treats as
                // 'every area'. Passing null straight through would become SQL NULL, and
                // "NULL = ''" is not true in SQL, so the filter would match nothing at all.
                DbHelper.P("@Area", area ?? ""));
        }

        // Feeds the count on the customer's home screen. A separate COUNT query rather
        // than GetActiveOffers().Rows.Count, so a badge does not drag an entire result
        // set with every column across the wire just to measure it.
        public int CountActiveOffers()
        {
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)
FROM    Offers o
        -- Medicines is joined only as the bridge to Pharmacies: Offers has no PharmacyId
        -- of its own, so the shop is reached through the medicine it belongs to.
        INNER JOIN Medicines  m  ON m.MedicineId  = o.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
-- The same three live-offer tests as the query above, so the badge and the page agree.
WHERE   o.IsActive = 1 AND ph.Status = 'Approved'
  AND   CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate;");
            // No parameters at all: this count has nothing to filter by, and the params
            // array simply arrives empty, which DbHelper handles.
        }

        // ---------------------------------------------------------------------
        //  PHARMACY OWNER  (requirement 14)
        // ---------------------------------------------------------------------

        /// <summary>The owner's own offers only; the join to Medicines carries the PharmacyId filter.</summary>
        public DataTable GetForPharmacy(int pharmacyId)
        {
            const string sql = @"
SELECT  o.OfferId, o.OfferTitle, m.MedicineName, m.Strength,
        m.UnitPrice                                                          AS OriginalPrice,
        o.DiscountPercent,
        -- Same expression as the customer query, so the owner is looking at exactly the
        -- price his customers see rather than a figure worked out a second way.
        CAST(m.UnitPrice * (1 - o.DiscountPercent / 100.0) AS DECIMAL(10,2)) AS DiscountedPrice,
        o.StartDate, o.EndDate, o.IsActive,
        -- One readable word instead of three columns the owner has to interpret. The
        -- order of the WHEN clauses matters: CASE stops at the first match, so a paused
        -- offer reads 'Paused' whatever its dates say, which is the truth about whether
        -- it is running. Scheduled is tested before Expired because an offer cannot be
        -- both, and ELSE then means 'active and inside its window'.
        CASE WHEN o.IsActive = 0 THEN 'Paused'
             WHEN CAST(GETDATE() AS DATE) <  o.StartDate THEN 'Scheduled'
             WHEN CAST(GETDATE() AS DATE) >  o.EndDate   THEN 'Expired'
             ELSE 'Running' END                                              AS OfferState
FROM    Offers o
        INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
-- THE ownership filter. Offers carries no PharmacyId, so the only way to ask 'is this
-- mine?' is through the medicine, and that is what this join plus this line do.
WHERE   m.PharmacyId = @PharmacyId
-- Newest campaign first: the owner is normally looking for what he just set up.
-- Expired offers are deliberately NOT filtered out here - unlike the customer view,
-- the owner needs to see his history in order to reuse or delete it.
ORDER BY o.StartDate DESC;";

            return _db.ExecuteTable(sql, DbHelper.P("@PharmacyId", pharmacyId));
        }

        /// <summary>
        /// Creates an offer. The INSERT ... SELECT with the PharmacyId in its
        /// WHERE clause is what stops an owner creating a discount on somebody
        /// else's medicine: if the medicine is not his, no row is inserted.
        /// </summary>
        public bool Create(int medicineId, int pharmacyId, string title, decimal discountPercent,
                           DateTime startDate, DateTime endDate)
        {
            const string sql = @"
-- INSERT ... SELECT, not INSERT ... VALUES. The rows to insert are whatever the SELECT
-- produces, and the SELECT is filtered on ownership - so the permission check and the
-- write are one statement and cannot drift apart. Reading the medicine first and then
-- inserting would leave a gap in which the medicine could change hands.
INSERT INTO Offers (MedicineId, OfferTitle, DiscountPercent, StartDate, EndDate)
-- m.MedicineId comes from the row that was found rather than from the parameter, so the
-- value written is one the database has just confirmed exists.
SELECT  m.MedicineId, @Title, @Percent, @StartDate, @EndDate
FROM    Medicines m
-- Both halves are needed: the id says WHICH medicine, the PharmacyId says whose it is.
-- If the medicine belongs to another shop the SELECT returns no rows, nothing is
-- inserted, ExecuteNonQuery returns 0 and the method below reports failure - no
-- exception, just a refusal.
WHERE   m.MedicineId = @MedicineId AND m.PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@Title", title.Trim()),
                // decimal, not double: a percentage feeds straight into a money
                // calculation, and binary floating point would round it imprecisely.
                DbHelper.P("@Percent", discountPercent),
                // .Date strips the time of day before the value is sent. The column is a
                // DATE, and a start of 'today at 14:35' would otherwise mean the offer
                // does not start until the afternoon - the same mismatch the
                // CAST(GETDATE() AS DATE) in the queries above exists to avoid.
                DbHelper.P("@StartDate", startDate.Date),
                DbHelper.P("@EndDate", endDate.Date),
                DbHelper.P("@MedicineId", medicineId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // exactly one row written, so it was his
        }

        public bool Update(int offerId, int pharmacyId, string title, decimal discountPercent,
                           DateTime startDate, DateTime endDate)
        {
            const string sql = @"
-- UPDATE with an alias and a FROM clause: the alias 'o' after UPDATE names which table
-- of the join is being written to, while the join supplies the ownership test. This is
-- the T-SQL form for 'update this table, but only the rows a join can vouch for'.
UPDATE  o
SET     o.OfferTitle = @Title, o.DiscountPercent = @Percent,
        o.StartDate = @StartDate, o.EndDate = @EndDate
-- MedicineId is deliberately not in the SET list. Moving an offer onto a different
-- medicine would side-step the ownership check performed when it was created, so a
-- change of medicine means deleting the offer and creating a new one.
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
-- Same two-part test as Create: the OfferId picks the row, the PharmacyId proves it is
-- the caller's. An owner who guesses another shop's OfferId updates nothing.
WHERE   o.OfferId = @OfferId AND m.PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@Title", title.Trim()),
                DbHelper.P("@Percent", discountPercent),
                DbHelper.P("@StartDate", startDate.Date),
                DbHelper.P("@EndDate", endDate.Date),
                DbHelper.P("@OfferId", offerId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        /// <summary>Pausing an offer keeps the row, so it can be switched back on later.</summary>
        public bool SetActive(int offerId, int pharmacyId, bool active)
        {
            const string sql = @"
-- Setting a flag rather than deleting the row: a paused campaign keeps its title, its
-- percentage and its dates, so switching it back on is one click instead of retyping it.
UPDATE  o SET o.IsActive = @Active
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
WHERE   o.OfferId = @OfferId AND m.PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,
                // BIT column, so the bool becomes 1 or 0 on the way in.
                DbHelper.P("@Active", active ? 1 : 0),
                DbHelper.P("@OfferId", offerId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        public bool Delete(int offerId, int pharmacyId)
        {
            const string sql = @"
-- DELETE with the same alias-plus-FROM shape as the two updates above, so the ownership
-- rule is expressed identically in all four write methods rather than three ways.
-- A hard delete is safe HERE, unlike for a category or a review: nothing references an
-- Offers row. Past orders store the price that was actually charged on the order line,
-- so removing the offer cannot rewrite history.
DELETE  o
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
WHERE   o.OfferId = @OfferId AND m.PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@OfferId", offerId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        // The number on the owner's dashboard tile. 'Running' here means the same three
        // things it means in the CASE expression above - switched on, started, not yet
        // finished - so the tile and the grid cannot disagree about what is live.
        public int CountRunningForPharmacy(int pharmacyId)
        {
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
WHERE   m.PharmacyId = @Id AND o.IsActive = 1
  -- BETWEEN is inclusive at both ends, so an offer counts on its first and last day.
  AND   CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate;",
                DbHelper.P("@Id", pharmacyId));
        }
    }
}
