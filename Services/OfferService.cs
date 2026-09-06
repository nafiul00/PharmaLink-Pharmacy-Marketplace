using System.Data;
using PharmaLinkApp.Database;

namespace PharmaLinkApp.Services
{
    /// <summary>
    /// Time limited percentage discounts (requirements 14 and 29).
    ///
    /// The discounted price is always calculated inside the SQL query, never in
    /// C#, so the offers screen, the medicine details screen, the cart and the
    /// invoice all print the same number with no chance of the four disagreeing.
    /// </summary>
    public class OfferService
    {
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
        m.UnitPrice                                                          AS OriginalPrice,
        o.DiscountPercent,
        CAST(m.UnitPrice * (1 - o.DiscountPercent / 100.0) AS DECIMAL(10,2)) AS DiscountedPrice,
        CAST(m.UnitPrice * (o.DiscountPercent / 100.0) AS DECIMAL(10,2))     AS YouSave,
        o.EndDate,
        m.MedicineId,
        m.Stock
FROM    Offers o
        INNER JOIN Medicines  m  ON m.MedicineId  = o.MedicineId
        INNER JOIN Categories c  ON c.CategoryId  = m.CategoryId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
WHERE   o.IsActive  = 1
  AND   m.IsActive  = 1
  AND   CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate
  AND   m.Stock     > 0
  AND   ph.Status   = 'Approved'
  AND   (@CategoryId = 0  OR m.CategoryId = @CategoryId)
  AND   (@Area       = '' OR ph.Area      = @Area)
ORDER BY o.DiscountPercent DESC;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@CategoryId", categoryId),
                DbHelper.P("@Area", area ?? ""));
        }

        public int CountActiveOffers()
        {
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)
FROM    Offers o
        INNER JOIN Medicines  m  ON m.MedicineId  = o.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
WHERE   o.IsActive = 1 AND ph.Status = 'Approved'
  AND   CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate;");
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
        CAST(m.UnitPrice * (1 - o.DiscountPercent / 100.0) AS DECIMAL(10,2)) AS DiscountedPrice,
        o.StartDate, o.EndDate, o.IsActive,
        CASE WHEN o.IsActive = 0 THEN 'Paused'
             WHEN CAST(GETDATE() AS DATE) <  o.StartDate THEN 'Scheduled'
             WHEN CAST(GETDATE() AS DATE) >  o.EndDate   THEN 'Expired'
             ELSE 'Running' END                                              AS OfferState
FROM    Offers o
        INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
WHERE   m.PharmacyId = @PharmacyId
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
INSERT INTO Offers (MedicineId, OfferTitle, DiscountPercent, StartDate, EndDate)
SELECT  m.MedicineId, @Title, @Percent, @StartDate, @EndDate
FROM    Medicines m
WHERE   m.MedicineId = @MedicineId AND m.PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@Title", title.Trim()),
                DbHelper.P("@Percent", discountPercent),
                DbHelper.P("@StartDate", startDate.Date),
                DbHelper.P("@EndDate", endDate.Date),
                DbHelper.P("@MedicineId", medicineId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        public bool Update(int offerId, int pharmacyId, string title, decimal discountPercent,
                           DateTime startDate, DateTime endDate)
        {
            const string sql = @"
UPDATE  o
SET     o.OfferTitle = @Title, o.DiscountPercent = @Percent,
        o.StartDate = @StartDate, o.EndDate = @EndDate
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
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
UPDATE  o SET o.IsActive = @Active
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
WHERE   o.OfferId = @OfferId AND m.PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@Active", active ? 1 : 0),
                DbHelper.P("@OfferId", offerId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        public bool Delete(int offerId, int pharmacyId)
        {
            const string sql = @"
DELETE  o
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
WHERE   o.OfferId = @OfferId AND m.PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@OfferId", offerId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        public int CountRunningForPharmacy(int pharmacyId)
        {
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
WHERE   m.PharmacyId = @Id AND o.IsActive = 1
  AND   CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate;",
                DbHelper.P("@Id", pharmacyId));
        }
    }
}
