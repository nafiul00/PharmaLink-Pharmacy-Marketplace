/* =====================================================================
   PharmaLink - every SELECT query the application runs, in one script.
   Copied from the C# files in the Services folder. Run against PharmaLinkDB in SSMS.

   Highlight the DECLARE block plus ONE query and press F5.
   The variables look up the seeded demo accounts, so no ids are guessed.
   ===================================================================== */
USE PharmaLinkDB;
GO

DECLARE @CustomerId  INT = (SELECT UserId FROM Users WHERE Email = 'rahim@gmail.com');
DECLARE @OwnerId     INT = (SELECT UserId FROM Users WHERE Email = 'kamrul@mitfordpharma.com');
DECLARE @PharmacyId  INT = (SELECT PharmacyId FROM Pharmacies WHERE OwnerId = @OwnerId);
DECLARE @MedicineId  INT = (SELECT TOP 1 MedicineId FROM Medicines WHERE PharmacyId = @PharmacyId ORDER BY MedicineId);
DECLARE @OrderId     INT = (SELECT TOP 1 OrderId FROM Orders WHERE CustomerId = @CustomerId ORDER BY OrderId DESC);
DECLARE @CategoryId  INT = 0;        -- 0 = all categories
DECLARE @Email       NVARCHAR(100) = 'rahim@gmail.com';
DECLARE @Phone       NVARCHAR(20)  = '01700000000';
DECLARE @LicenseNo   NVARCHAR(50)  = 'DGDA-DH-10021';
DECLARE @Keyword     NVARCHAR(100) = '';   -- '' = no filter
DECLARE @Status      NVARCHAR(20)  = '';   -- '' = any status
DECLARE @UserType    NVARCHAR(20)  = '';   -- '' = any role
DECLARE @Area        NVARCHAR(50)  = '';   -- '' = all areas
DECLARE @MinPrice    DECIMAL(10,2) = 0;
DECLARE @MaxPrice    DECIMAL(10,2) = 0;    -- 0 = any price
DECLARE @InStock     INT = 1;
DECLARE @FromDate    DATETIME = DATEADD(DAY, -30, CAST(GETDATE() AS DATE));
DECLARE @ToDate      DATETIME = DATEADD(SECOND, -1, DATEADD(DAY, 1, CAST(CAST(GETDATE() AS DATE) AS DATETIME)));
DECLARE @DeliveryCharge DECIMAL(10,2) = 60;
DECLARE @Threshold   DECIMAL(4,2) = 2.5;
DECLARE @MinReviews  INT = 2;
DECLARE @MinRevenue  DECIMAL(12,2) = 0;
DECLARE @TopN        INT = 10;
DECLARE @MaxRating   INT = 2;
DECLARE @IncludeHidden INT = 0;
DECLARE @MinRating   INT = 1;
DECLARE @MaxRatingOwner INT = 5;
DECLARE @ActiveOnly  INT = 1;
DECLARE @IncludeDelisted INT = 0;
DECLARE @VerifyStatus NVARCHAR(20) = '';
DECLARE @ApprovedOnly INT = 1;

SELECT @CustomerId AS CustomerId, @OwnerId AS OwnerId, @PharmacyId AS PharmacyId,
       @MedicineId AS MedicineId, @OrderId AS OrderId;


/* =====================================================================
   0. Browse every table
   ===================================================================== */
SELECT * FROM Users;
SELECT * FROM Pharmacies;
SELECT * FROM Categories;
SELECT * FROM Medicines;
SELECT * FROM Offers;
SELECT * FROM Cart;
SELECT * FROM Orders;
SELECT * FROM OrderItems;
SELECT * FROM Prescriptions;
SELECT * FROM Reviews;


/* =====================================================================
   1. AuthService - login, registration checks, users
   ===================================================================== */

-- 1.1 Login: account + owned pharmacy by email (LoginForm)
SELECT  u.UserId, u.FullName, u.Email, u.PasswordHash, u.PasswordSalt,
        u.Phone, u.Address, u.UserType, u.Status, u.CreatedAt,
        p.PharmacyId, p.PharmacyName, p.Status AS PharmacyStatus
FROM    Users u
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
WHERE   u.Email = @Email;

-- 1.2 Is this email already registered? (SignUpForm)
SELECT COUNT(*) FROM Users WHERE Email = @Email;

-- 1.3 Is this phone already registered? (SignUpForm, MyProfileForm)
SELECT COUNT(*) FROM Users WHERE Phone = @Phone;

-- 1.4 Is this licence already registered? (SignUpForm)
SELECT COUNT(*) FROM Pharmacies WHERE LicenseNo = @LicenseNo;

-- 1.5 One user's profile (MyProfileForm)
SELECT  u.UserId, u.FullName, u.Email, u.Phone, u.Address, u.UserType,
        u.Status, u.CreatedAt, p.PharmacyId, p.PharmacyName
FROM    Users u
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
WHERE   u.UserId = @CustomerId;

-- 1.6 A user's password salt (ChangePassword)
SELECT PasswordSalt FROM Users WHERE UserId = @CustomerId;

-- 1.7 All users with optional filters (SuperAdminManageUsersForm)
SELECT  u.UserId, u.FullName, u.Email, u.Phone, u.UserType, u.Status,
        ISNULL(p.PharmacyName, '-') AS PharmacyName, u.CreatedAt
FROM    Users u
        LEFT JOIN Pharmacies p ON p.OwnerId = u.UserId
WHERE   u.UserType <> 'SuperAdmin'
  AND   (@Keyword  = '' OR u.FullName LIKE '%' + @Keyword + '%' OR u.Email LIKE '%' + @Keyword + '%')
  AND   (@Status   = '' OR u.Status   = @Status)
  AND   (@UserType = '' OR u.UserType = @UserType)
ORDER BY u.UserType, u.FullName;


/* =====================================================================
   2. PharmacyService - shops
   ===================================================================== */

-- 2.1 All pharmacies with owner, medicine count, rating (SuperAdminManageShopsForm)
SELECT  p.PharmacyId, p.PharmacyName, u.FullName AS OwnerName, u.Email AS OwnerEmail,
        p.LicenseNo, p.Area, p.ContactPhone, p.CommissionRate, p.Status, p.RegisteredAt,
        (SELECT COUNT(*) FROM Medicines m WHERE m.PharmacyId = p.PharmacyId) AS Medicines,
        ISNULL((SELECT CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2))
                FROM   Reviews r
                       INNER JOIN Medicines m2 ON m2.MedicineId = r.MedicineId
                WHERE  m2.PharmacyId = p.PharmacyId AND r.IsHidden = 0), 0) AS AverageRating
FROM    Pharmacies p
        INNER JOIN Users u ON u.UserId = p.OwnerId
WHERE   (@Keyword = '' OR p.PharmacyName LIKE '%' + @Keyword + '%'
                       OR p.LicenseNo    LIKE '%' + @Keyword + '%'
                       OR u.FullName     LIKE '%' + @Keyword + '%')
  AND   (@Status  = '' OR p.Status = @Status)
  AND   (@Area    = '' OR p.Area   = @Area)
ORDER BY CASE p.Status WHEN 'Pending' THEN 0 WHEN 'Approved' THEN 1 ELSE 2 END, p.PharmacyName;

-- 2.2 Pharmacies waiting for approval (SuperAdminDashboard)
SELECT  p.PharmacyId, p.PharmacyName, u.FullName AS OwnerName, p.LicenseNo,
        p.Area, p.ContactPhone, p.RegisteredAt
FROM    Pharmacies p
        INNER JOIN Users u ON u.UserId = p.OwnerId
WHERE   p.Status = 'Pending'
ORDER BY p.RegisteredAt;

-- 2.3 How many orders a pharmacy has (guard before Delete)
SELECT COUNT(*) FROM Orders WHERE PharmacyId = @PharmacyId;

-- 2.4 Owner of a pharmacy (read inside Delete)
SELECT OwnerId FROM Pharmacies WHERE PharmacyId = @PharmacyId;

-- 2.5 One pharmacy's full profile (PharmacyProfileForm)
SELECT  p.PharmacyId, p.OwnerId, p.PharmacyName, p.LicenseNo, p.Area, p.Address,
        p.ContactPhone, p.LogoPath, p.CommissionRate, p.Status, p.RegisteredAt,
        u.FullName AS OwnerName
FROM    Pharmacies p
        INNER JOIN Users u ON u.UserId = p.OwnerId
WHERE   p.PharmacyId = @PharmacyId;

-- 2.6 Distinct areas (area dropdowns)
SELECT DISTINCT Area FROM Pharmacies WHERE (@ApprovedOnly = 0 OR Status = 'Approved') ORDER BY Area;

-- 2.7 Approved pharmacies (customer pharmacy dropdown)
SELECT PharmacyId, PharmacyName, Area FROM Pharmacies WHERE Status = 'Approved' ORDER BY PharmacyName;

-- 2.8 Count of pharmacies in a status
SELECT COUNT(*) FROM Pharmacies WHERE Status = 'Pending';


/* =====================================================================
   3. CategoryService
   ===================================================================== */

-- 3.1 Categories with how many medicines use each (ManageCategoriesForm)
SELECT  c.CategoryId, c.CategoryName, c.Description, c.IsActive,
        (SELECT COUNT(*) FROM Medicines m WHERE m.CategoryId = c.CategoryId) AS MedicineCount
FROM    Categories c
WHERE   (@ActiveOnly = 0 OR c.IsActive = 1)
ORDER BY c.CategoryName;

-- 3.2 Active categories (category dropdowns)
SELECT CategoryId, CategoryName, Description, IsActive FROM Categories WHERE IsActive = 1 ORDER BY CategoryName;

-- 3.3 Duplicate category name check (0 = ignore no row)
SELECT COUNT(*) FROM Categories WHERE CategoryName = N'Antibiotic' AND CategoryId <> 0;

-- 3.4 Is a category used by any medicine?
SELECT COUNT(*) FROM Medicines WHERE CategoryId = 1;


/* =====================================================================
   4. MedicineService - catalogue and stock
   ===================================================================== */

-- 4.1 Customer search with five optional filters and today's best offer (CustomerHomeForm)
SELECT  m.MedicineId, m.MedicineName, m.GenericName, m.Strength, m.Manufacturer,
        c.CategoryName, ph.PharmacyName, ph.Area, m.UnitPrice,
        ISNULL(d.Pct, 0) AS DiscountPercent,
        CAST(m.UnitPrice * (1 - ISNULL(d.Pct, 0) / 100.0) AS DECIMAL(10,2)) AS PriceYouPay,
        m.Stock, m.RequiresRx, m.ExpiryDate
FROM    Medicines m
        INNER JOIN Categories c  ON c.CategoryId  = m.CategoryId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   m.IsActive   = 1
  AND   ph.Status    = 'Approved'
  AND   m.ExpiryDate > CAST(GETDATE() AS DATE)
  AND   (@Keyword    = ''  OR m.MedicineName LIKE '%' + @Keyword + '%'
                           OR m.GenericName  LIKE '%' + @Keyword + '%'
                           OR m.Manufacturer LIKE '%' + @Keyword + '%')
  AND   (@CategoryId = 0   OR m.CategoryId = @CategoryId)
  AND   (@MaxPrice   = 0   OR m.UnitPrice BETWEEN @MinPrice AND @MaxPrice)
  AND   (@Area       = ''  OR ph.Area = @Area)
  AND   (0           = 0   OR ph.PharmacyId = @PharmacyId)   -- change the first 0 to @PharmacyId to pin one shop
  AND   (@InStock    = 0   OR m.Stock > 0)
ORDER BY m.MedicineName, PriceYouPay;

-- 4.2 One medicine's details with today's offer (MedicineDetailsForm)
SELECT  m.MedicineId, m.PharmacyId, m.CategoryId, m.MedicineName, m.GenericName,
        m.Manufacturer, m.Strength, m.UnitPrice, m.Stock, m.MinStock, m.RequiresRx,
        m.ExpiryDate, m.Description, m.ImagePath, m.IsActive,
        c.CategoryName, ph.PharmacyName, ph.Area,
        ISNULL(d.Pct, 0) AS DiscountPercent
FROM    Medicines m
        INNER JOIN Categories c  ON c.CategoryId  = m.CategoryId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   m.MedicineId = @MedicineId;

-- 4.3 An owner's medicines (AdminMedicineForm)
SELECT  m.MedicineId, m.MedicineName, m.GenericName, c.CategoryName, m.Manufacturer,
        m.Strength, m.UnitPrice, m.Stock, m.MinStock, m.RequiresRx, m.ExpiryDate,
        m.IsActive,
        CASE WHEN m.Stock < m.MinStock THEN 'Low Stock' ELSE 'Healthy' END AS StockStatus
FROM    Medicines m
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId
WHERE   m.PharmacyId = @PharmacyId
  AND   (@IncludeDelisted = 1 OR m.IsActive = 1)
  AND   (@Keyword = '' OR m.MedicineName LIKE '%' + @Keyword + '%'
                       OR m.GenericName  LIKE '%' + @Keyword + '%')
ORDER BY m.MedicineName;

-- 4.4 Duplicate brand + strength in one shop (MedicineEditorForm)
SELECT COUNT(*) FROM Medicines
WHERE  PharmacyId = @PharmacyId
  AND  MedicineName = N'Napa'
  AND  ISNULL(Strength, '') = ISNULL(N'500mg', '')
  AND  MedicineId <> 0;

-- 4.5 An owner's active medicines for a dropdown (DiscountOffersForm, AdminEarningsForm)
SELECT  MedicineId, MedicineName, Strength, UnitPrice
FROM    Medicines
WHERE   PharmacyId = @PharmacyId AND IsActive = 1
ORDER BY MedicineName;

-- 4.6 One medicine for editing, only if it is this shop's (MedicineEditorForm)
SELECT  m.MedicineId, m.PharmacyId, m.CategoryId, m.MedicineName, m.GenericName,
        m.Manufacturer, m.Strength, m.UnitPrice, m.Stock, m.MinStock, m.RequiresRx,
        m.ExpiryDate, m.Description, m.ImagePath, m.IsActive, c.CategoryName
FROM    Medicines m
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId
WHERE   m.MedicineId = @MedicineId AND m.PharmacyId = @PharmacyId;

-- 4.7 Low stock alert (AdminInventoryForm, AdminDashboard)
SELECT  m.MedicineId, m.MedicineName, m.Strength, c.CategoryName,
        m.Stock, m.MinStock, (m.MinStock - m.Stock) AS ShortfallUnits
FROM    Medicines m
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId
WHERE   m.PharmacyId = @PharmacyId
  AND   m.Stock      < m.MinStock
  AND   m.IsActive   = 1
ORDER BY ShortfallUnits DESC;

-- 4.8 Inventory: units left, units sold, stock value (AdminInventoryForm)
SELECT  m.MedicineId, m.MedicineName, m.Strength, c.CategoryName,
        m.Stock                                      AS UnitsRemaining,
        ISNULL(sold.UnitsSold, 0)                    AS UnitsSold,
        m.MinStock, m.UnitPrice,
        CAST(m.Stock * m.UnitPrice AS DECIMAL(12,2)) AS StockValue,
        CASE WHEN m.Stock < m.MinStock THEN 'Low Stock' ELSE 'Healthy' END AS StockStatus,
        m.ExpiryDate
FROM    Medicines m
        INNER JOIN Categories c ON c.CategoryId = m.CategoryId
        LEFT JOIN (SELECT oi.MedicineId, SUM(oi.Quantity) AS UnitsSold
                   FROM   OrderItems oi
                          INNER JOIN Orders o ON o.OrderId = oi.OrderId
                   WHERE  o.Status <> 'Cancelled'
                   GROUP BY oi.MedicineId) sold ON sold.MedicineId = m.MedicineId
WHERE   m.PharmacyId = @PharmacyId AND m.IsActive = 1
ORDER BY m.MedicineName;

-- 4.9 Count of low-stock medicines (dashboard tile)
SELECT COUNT(*) FROM Medicines WHERE PharmacyId = @PharmacyId AND Stock < MinStock AND IsActive = 1;

-- 4.10 Count of listed medicines (dashboard subtitle)
SELECT COUNT(*) FROM Medicines WHERE PharmacyId = @PharmacyId AND IsActive = 1;


/* =====================================================================
   5. OfferService
   ===================================================================== */

-- 5.1 Offers running today (CustomerOffersForm)
SELECT  o.OfferId, o.OfferTitle, m.MedicineName, m.Strength, c.CategoryName,
        ph.PharmacyName, ph.Area,
        m.UnitPrice                                                          AS OriginalPrice,
        o.DiscountPercent,
        CAST(m.UnitPrice * (1 - o.DiscountPercent / 100.0) AS DECIMAL(10,2)) AS DiscountedPrice,
        CAST(m.UnitPrice * (o.DiscountPercent / 100.0) AS DECIMAL(10,2))     AS YouSave,
        o.EndDate, m.MedicineId, m.Stock
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
ORDER BY o.DiscountPercent DESC;

-- 5.2 Count of offers running today
SELECT  COUNT(*)
FROM    Offers o
        INNER JOIN Medicines  m  ON m.MedicineId  = o.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
WHERE   o.IsActive = 1 AND ph.Status = 'Approved'
  AND   CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate;

-- 5.3 An owner's offers with state (DiscountOffersForm)
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
ORDER BY o.StartDate DESC;

-- 5.4 Count of an owner's running offers
SELECT  COUNT(*)
FROM    Offers o INNER JOIN Medicines m ON m.MedicineId = o.MedicineId
WHERE   m.PharmacyId = @PharmacyId AND o.IsActive = 1
  AND   CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate;


/* =====================================================================
   6. CartService
   ===================================================================== */

-- 6.1 Stock of an on-sale medicine (before adding to cart)
SELECT Stock FROM Medicines WHERE MedicineId = @MedicineId AND IsActive = 1;

-- 6.2 Quantity already in the customer's cart
SELECT ISNULL(Quantity, 0) FROM Cart WHERE CustomerId = @CustomerId AND MedicineId = @MedicineId;

-- 6.3 Stock of a medicine (before changing a cart quantity)
SELECT Stock FROM Medicines WHERE MedicineId = @MedicineId;

-- 6.4 Number of cart lines (cart badge)
SELECT COUNT(*) FROM Cart WHERE CustomerId = @CustomerId;

-- 6.5 Cart lines with today's discount (CartForm, CheckoutForm)
SELECT  ct.CartId, ct.MedicineId, m.MedicineName, m.Strength,
        ph.PharmacyId, ph.PharmacyName, ct.Quantity,
        m.UnitPrice                                                                    AS ListPrice,
        ISNULL(d.Pct, 0)                                                               AS DiscountPercent,
        CAST(m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0) AS DECIMAL(10,2))               AS PriceYouPay,
        CAST(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0) AS DECIMAL(12,2)) AS LineTotal,
        m.Stock, m.RequiresRx
FROM    Cart ct
        INNER JOIN Medicines  m  ON m.MedicineId  = ct.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId
ORDER BY ph.PharmacyName, m.MedicineName;

-- 6.6 Cart grouped by pharmacy = one future order each (CartForm, CheckoutForm)
SELECT  ph.PharmacyId, ph.PharmacyName,
        COUNT(*)                                                                            AS Lines,
        SUM(ct.Quantity)                                                                    AS Units,
        CAST(SUM(ct.Quantity * m.UnitPrice) AS DECIMAL(12,2))                               AS BeforeDiscount,
        CAST(SUM(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2)) AS ItemsTotal,
        @DeliveryCharge                                                                     AS DeliveryCharge,
        ph.CommissionRate
FROM    Cart ct
        INNER JOIN Medicines  m  ON m.MedicineId  = ct.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId
GROUP BY ph.PharmacyId, ph.PharmacyName, ph.CommissionRate
ORDER BY ph.PharmacyName;

-- 6.7 Does the cart hold a prescription-only medicine? (0 = any pharmacy)
SELECT  COUNT(*)
FROM    Cart ct
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
WHERE   ct.CustomerId = @CustomerId
  AND   m.RequiresRx  = 1
  AND   (0 = 0 OR m.PharmacyId = @PharmacyId);

-- 6.8 Cart total after discounts, before delivery
SELECT  ISNULL(CAST(SUM(ct.Quantity * m.UnitPrice * (1 - ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2)), 0)
FROM    Cart ct
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId;

-- 6.9 Total the discounts are saving
SELECT  ISNULL(CAST(SUM(ct.Quantity * m.UnitPrice * (ISNULL(d.Pct,0)/100.0)) AS DECIMAL(12,2)), 0)
FROM    Cart ct
        INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
        OUTER APPLY (SELECT MAX(o.DiscountPercent) AS Pct
                     FROM   Offers o
                     WHERE  o.MedicineId = m.MedicineId
                       AND  o.IsActive   = 1
                       AND  CAST(GETDATE() AS DATE) BETWEEN o.StartDate AND o.EndDate) d
WHERE   ct.CustomerId = @CustomerId;


/* =====================================================================
   7. OrderService - checkout, history, invoice, owner queue
   ===================================================================== */

-- 7.1 Checkout stock re-check: first unsellable line for one shop (returns nothing if all OK)
SELECT TOP 1 m.MedicineName
FROM   Cart ct
       INNER JOIN Medicines m ON m.MedicineId = ct.MedicineId
WHERE  ct.CustomerId = @CustomerId
  AND  m.PharmacyId  = @PharmacyId
  AND  (ct.Quantity > m.Stock OR m.IsActive = 0);

-- 7.2 A pharmacy's current commission rate (read during checkout)
SELECT CommissionRate FROM Pharmacies WHERE PharmacyId = @PharmacyId;

-- 7.3 Customer's order history with CanReview flag (OrderHistoryForm)
SELECT  o.OrderId, o.OrderDate, ph.PharmacyName,
        COUNT(oi.OrderItemId) AS Items,
        o.ItemsTotal, o.DeliveryCharge, o.TotalAmount,
        o.PaymentMethod, o.Status,
        CASE WHEN o.Status = 'Delivered'
              AND EXISTS (SELECT 1 FROM OrderItems x
                          WHERE x.OrderId = o.OrderId
                            AND NOT EXISTS (SELECT 1 FROM Reviews r
                                            WHERE r.OrderId = o.OrderId
                                              AND r.MedicineId = x.MedicineId))
             THEN 1 ELSE 0 END AS CanReview
FROM    Orders o
        INNER JOIN Pharmacies ph ON ph.PharmacyId = o.PharmacyId
        INNER JOIN OrderItems oi ON oi.OrderId    = o.OrderId
WHERE   o.CustomerId = @CustomerId
  AND   (@Status = '' OR o.Status = @Status)
  AND   (0       = 0  OR o.PharmacyId = @PharmacyId)
  AND   o.OrderDate BETWEEN DATEADD(YEAR, -3, @FromDate) AND @ToDate
GROUP BY o.OrderId, o.OrderDate, ph.PharmacyName, o.ItemsTotal, o.DeliveryCharge,
         o.TotalAmount, o.PaymentMethod, o.Status
ORDER BY o.OrderDate DESC;

-- 7.4 Invoice header (InvoiceForm)
SELECT  o.OrderId, o.CustomerId, o.PharmacyId, o.OrderDate, o.ItemsTotal,
        o.DeliveryCharge, o.TotalAmount, o.CommissionAmount, o.DeliveryAddress,
        o.PaymentMethod, o.Status,
        u.FullName AS CustomerName, u.Phone AS CustomerPhone,
        ph.PharmacyName, ph.Address AS PharmacyAddress, ph.LicenseNo AS PharmacyLicense
FROM    Orders o
        INNER JOIN Users u       ON u.UserId      = o.CustomerId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = o.PharmacyId
WHERE   o.OrderId = @OrderId;

-- 7.5 Invoice lines (InvoiceForm)
SELECT  oi.OrderItemId, oi.OrderId, oi.MedicineId, oi.Quantity, oi.UnitPrice, oi.Subtotal,
        m.MedicineName, m.Strength
FROM    OrderItems oi
        INNER JOIN Medicines m ON m.MedicineId = oi.MedicineId
WHERE   oi.OrderId = @OrderId
ORDER BY m.MedicineName;

-- 7.6 Order lines for a grid (OrderHistoryForm, VerifyPrescriptionForm)
SELECT  m.MedicineName, m.Strength, oi.Quantity, oi.UnitPrice, oi.Subtotal
FROM    OrderItems oi
        INNER JOIN Medicines m ON m.MedicineId = oi.MedicineId
WHERE   oi.OrderId = @OrderId
ORDER BY m.MedicineName;

-- 7.7 A pharmacy's order queue with prescription state (AdminDashboard)
SELECT  o.OrderId, o.OrderDate, u.FullName AS CustomerName, u.Phone AS CustomerPhone,
        COUNT(oi.OrderItemId) AS Items, o.ItemsTotal, o.DeliveryCharge, o.TotalAmount,
        o.PaymentMethod, o.Status, o.DeliveryAddress,
        CASE WHEN EXISTS (SELECT 1 FROM Prescriptions p
                          WHERE p.OrderId = o.OrderId AND p.VerifyStatus <> 'Approved')
             THEN 'Waiting on Rx' ELSE 'Clear' END AS RxState
FROM    Orders o
        INNER JOIN Users u       ON u.UserId   = o.CustomerId
        INNER JOIN OrderItems oi ON oi.OrderId = o.OrderId
WHERE   o.PharmacyId = @PharmacyId
  AND   (@Status = '' OR o.Status = @Status)
GROUP BY o.OrderId, o.OrderDate, u.FullName, u.Phone, o.ItemsTotal, o.DeliveryCharge,
         o.TotalAmount, o.PaymentMethod, o.Status, o.DeliveryAddress
ORDER BY o.OrderDate DESC;

-- 7.8 Does this order belong to this customer?
SELECT COUNT(*) FROM Orders WHERE OrderId = @OrderId AND CustomerId = @CustomerId;


/* =====================================================================
   8. PrescriptionService
   ===================================================================== */

-- 8.1 A pharmacy's prescription queue (VerifyPrescriptionForm)
SELECT  p.PrescriptionId, p.OrderId, u.FullName AS Customer, p.DoctorName,
        p.ImagePath, p.UploadedAt, p.VerifyStatus, o.Status AS OrderStatus,
        o.TotalAmount
FROM    Prescriptions p
        INNER JOIN Orders o ON o.OrderId = p.OrderId
        INNER JOIN Users  u ON u.UserId  = p.CustomerId
WHERE   o.PharmacyId = @PharmacyId
  AND   (@VerifyStatus = '' OR p.VerifyStatus = @VerifyStatus)
ORDER BY CASE p.VerifyStatus WHEN 'Pending' THEN 0 ELSE 1 END, p.UploadedAt;

-- 8.2 Count of pending prescriptions for a pharmacy
SELECT  COUNT(*)
FROM    Prescriptions p INNER JOIN Orders o ON o.OrderId = p.OrderId
WHERE   o.PharmacyId = @PharmacyId AND p.VerifyStatus = 'Pending';

-- 8.3 Prescriptions on one order
SELECT  PrescriptionId, OrderId, ImagePath, DoctorName, UploadedAt, VerifyStatus
FROM    Prescriptions WHERE OrderId = @OrderId ORDER BY UploadedAt DESC;

-- 8.4 Does the order have an approved prescription?
SELECT COUNT(*) FROM Prescriptions WHERE OrderId = @OrderId AND VerifyStatus = 'Approved';


/* =====================================================================
   9. ReviewService
   ===================================================================== */

-- 9.1 Unreviewed medicines on a delivered order (GiveRatingForm)
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
ORDER BY m.MedicineName;

-- 9.2 Visible reviews for a medicine (MedicineDetailsForm)
SELECT  r.ReviewId, u.FullName AS ReviewerName, r.Rating, r.Comment, r.ReviewDate
FROM    Reviews r
        INNER JOIN Users     u ON u.UserId     = r.CustomerId
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
WHERE   r.MedicineId = @MedicineId
  AND   r.IsHidden   = 0
ORDER BY r.ReviewDate DESC;

-- 9.3 Average rating of a medicine
SELECT ISNULL(CAST(AVG(CAST(Rating AS DECIMAL(4,2))) AS DECIMAL(4,2)), 0)
FROM   Reviews WHERE MedicineId = @MedicineId AND IsHidden = 0;

-- 9.4 Reviews about a pharmacy's medicines, by star range (AdminReviewsForm)
SELECT  r.ReviewId, u.FullName AS ReviewerName, m.MedicineName, m.Strength,
        r.Rating, r.Comment, r.ReviewDate, r.OrderId
FROM    Reviews r
        INNER JOIN Users     u ON u.UserId     = r.CustomerId
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
WHERE   m.PharmacyId = @PharmacyId
  AND   r.IsHidden   = 0
  AND   r.Rating BETWEEN @MinRating AND @MaxRatingOwner
ORDER BY r.ReviewDate DESC;

-- 9.5 Average rating of a pharmacy
SELECT  ISNULL(CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2)), 0)
FROM    Reviews r
        INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
WHERE   m.PharmacyId = @PharmacyId AND r.IsHidden = 0;

-- 9.6 Number of visible reviews for a pharmacy
SELECT  COUNT(*)
FROM    Reviews r INNER JOIN Medicines m ON m.MedicineId = r.MedicineId
WHERE   m.PharmacyId = @PharmacyId AND r.IsHidden = 0;

-- 9.7 Moderation queue: low-rated or reported (ModerateReviewsForm)
SELECT  r.ReviewId, u.FullName AS Reviewer, m.MedicineName, ph.PharmacyName,
        r.Rating, r.Comment, r.ReviewDate, r.OrderId, r.IsHidden, r.IsReported
FROM    Reviews r
        INNER JOIN Users      u  ON u.UserId      = r.CustomerId
        INNER JOIN Medicines  m  ON m.MedicineId  = r.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
WHERE   (r.Rating <= @MaxRating OR r.IsReported = 1)
  AND   (@IncludeHidden = 1 OR r.IsHidden = 0)
ORDER BY r.IsReported DESC, r.ReviewDate DESC;


/* =====================================================================
   10. ReportService - earnings and platform reports
   ===================================================================== */

-- 10.1 Earnings per pharmacy (0 = every shop) (SuperAdminSalesReportForm)
SELECT  ph.PharmacyId, ph.PharmacyName, ph.Area,
        ord.TotalOrders, itm.UnitsSold, itm.GrossSales, ord.PlatformCommission,
        CAST(itm.GrossSales - ord.PlatformCommission AS DECIMAL(12,2)) AS NetEarnings,
        itm.AverageItemPrice, ph.CommissionRate
FROM    Pharmacies ph
        INNER JOIN (SELECT  o.PharmacyId,
                            COUNT(*)                                       AS TotalOrders,
                            CAST(SUM(o.CommissionAmount) AS DECIMAL(12,2)) AS PlatformCommission
                    FROM    Orders o
                    WHERE   o.Status <> 'Cancelled'
                      AND   o.OrderDate BETWEEN @FromDate AND @ToDate
                      AND   (@Status = '' OR o.Status = @Status)
                    GROUP BY o.PharmacyId) ord ON ord.PharmacyId = ph.PharmacyId
        INNER JOIN (SELECT  o.PharmacyId,
                            SUM(oi.Quantity)                         AS UnitsSold,
                            CAST(SUM(oi.Subtotal) AS DECIMAL(12,2))  AS GrossSales,
                            CAST(AVG(oi.UnitPrice) AS DECIMAL(10,2)) AS AverageItemPrice
                    FROM    Orders o
                            INNER JOIN OrderItems oi ON oi.OrderId = o.OrderId
                    WHERE   o.Status <> 'Cancelled'
                      AND   o.OrderDate BETWEEN @FromDate AND @ToDate
                      AND   (@Status = '' OR o.Status = @Status)
                    GROUP BY o.PharmacyId) itm ON itm.PharmacyId = ph.PharmacyId
WHERE   (0     = 0  OR ph.PharmacyId = @PharmacyId)   -- change the first 0 to @PharmacyId for one shop
  AND   (@Area = '' OR ph.Area       = @Area)
ORDER BY itm.GrossSales DESC;

-- 10.2 Sale lines for one pharmacy (0 = every medicine) (AdminEarningsForm)
SELECT  o.OrderId, o.OrderDate, u.FullName AS Customer,
        m.MedicineName, m.Strength, oi.Quantity, oi.UnitPrice, oi.Subtotal,
        o.PaymentMethod, o.Status
FROM    Orders o
        INNER JOIN OrderItems oi ON oi.OrderId   = o.OrderId
        INNER JOIN Medicines  m  ON m.MedicineId = oi.MedicineId
        INNER JOIN Users      u  ON u.UserId     = o.CustomerId
WHERE   o.PharmacyId = @PharmacyId
  AND   o.Status    <> 'Cancelled'
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate
  AND   (0 = 0 OR m.MedicineId = @MedicineId)
ORDER BY o.OrderDate DESC, o.OrderId;

-- 10.3 Earnings tiles for one pharmacy, all medicines (AdminEarningsForm)
SELECT  ISNULL(SUM(oi.Subtotal), 0) AS GrossSales,
        ISNULL(SUM(oi.Quantity), 0) AS UnitsSold,
        ISNULL((SELECT SUM(o2.CommissionAmount)
                FROM   Orders o2
                WHERE  o2.PharmacyId = @PharmacyId
                  AND  o2.Status <> 'Cancelled'
                  AND  o2.OrderDate BETWEEN @FromDate AND @ToDate), 0) AS Commission
FROM    Orders o
        INNER JOIN OrderItems oi ON oi.OrderId = o.OrderId
WHERE   o.PharmacyId = @PharmacyId
  AND   o.Status    <> 'Cancelled'
  AND   o.OrderDate BETWEEN @FromDate AND @ToDate;

-- 10.4 Low-rated pharmacies (SuperAdminLowRatedShopsForm, SuperAdminDashboard)
SELECT  ph.PharmacyId, ph.PharmacyName, ph.Area,
        u.FullName AS OwnerName, u.Phone AS OwnerPhone,
        COUNT(r.ReviewId)                                         AS TotalReviews,
        CAST(AVG(CAST(r.Rating AS DECIMAL(4,2))) AS DECIMAL(4,2)) AS AverageRating,
        ph.Status
FROM    Pharmacies ph
        INNER JOIN Users     u ON u.UserId     = ph.OwnerId
        INNER JOIN Medicines m ON m.PharmacyId = ph.PharmacyId
        INNER JOIN Reviews   r ON r.MedicineId = m.MedicineId
WHERE   r.IsHidden = 0
GROUP BY ph.PharmacyId, ph.PharmacyName, ph.Area, u.FullName, u.Phone, ph.Status
HAVING  AVG(CAST(r.Rating AS DECIMAL(4,2))) < @Threshold
   AND  COUNT(r.ReviewId) >= @MinReviews
ORDER BY AverageRating ASC;

-- 10.5 Revenue by area
SELECT  ph.Area,
        COUNT(DISTINCT ph.PharmacyId) AS PharmaciesInArea,
        COUNT(DISTINCT o.OrderId)     AS Orders,
        SUM(o.TotalAmount)            AS Revenue,
        SUM(o.CommissionAmount)       AS CommissionEarned
FROM    Pharmacies ph
        INNER JOIN Orders o ON o.PharmacyId = ph.PharmacyId
WHERE   o.Status <> 'Cancelled'
GROUP BY ph.Area
HAVING  SUM(o.TotalAmount) > @MinRevenue
ORDER BY Revenue DESC;

-- 10.6 Platform totals (SuperAdminDashboard tiles)
SELECT
    (SELECT COUNT(*) FROM Pharmacies WHERE Status = 'Approved')   AS ApprovedPharmacies,
    (SELECT COUNT(*) FROM Pharmacies WHERE Status = 'Pending')    AS PendingPharmacies,
    (SELECT COUNT(*) FROM Users      WHERE UserType = 'Customer') AS Customers,
    (SELECT COUNT(*) FROM Orders     WHERE Status <> 'Cancelled') AS Orders,
    (SELECT ISNULL(SUM(TotalAmount), 0)      FROM Orders WHERE Status <> 'Cancelled') AS Revenue,
    (SELECT ISNULL(SUM(CommissionAmount), 0) FROM Orders WHERE Status <> 'Cancelled') AS Commission;

-- 10.7 One pharmacy's totals (AdminDashboard tiles)
SELECT
    (SELECT COUNT(*) FROM Orders WHERE PharmacyId = @PharmacyId AND Status <> 'Cancelled') AS Orders,
    (SELECT ISNULL(SUM(TotalAmount), 0)      FROM Orders WHERE PharmacyId = @PharmacyId AND Status <> 'Cancelled') AS Revenue,
    (SELECT ISNULL(SUM(CommissionAmount), 0) FROM Orders WHERE PharmacyId = @PharmacyId AND Status <> 'Cancelled') AS Commission,
    (SELECT COUNT(*) FROM Orders WHERE PharmacyId = @PharmacyId AND Status = 'Placed')     AS PendingOrders;

-- 10.8 Top-selling medicines (0 = whole platform)
SELECT  TOP (@TopN)
        m.MedicineName, m.Strength, ph.PharmacyName,
        SUM(oi.Quantity) AS UnitsSold,
        SUM(oi.Subtotal) AS Revenue
FROM    OrderItems oi
        INNER JOIN Orders     o  ON o.OrderId     = oi.OrderId
        INNER JOIN Medicines  m  ON m.MedicineId  = oi.MedicineId
        INNER JOIN Pharmacies ph ON ph.PharmacyId = m.PharmacyId
WHERE   o.Status <> 'Cancelled'
  AND   (0 = 0 OR m.PharmacyId = @PharmacyId)
GROUP BY m.MedicineName, m.Strength, ph.PharmacyName
ORDER BY UnitsSold DESC;
