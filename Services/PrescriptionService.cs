using System.Configuration;         // ConfigurationManager, reads the upload folder from App.config
using System.Data;                  // DataTable, the shape every read returns
using PharmaLinkApp.Database;       // DbHelper, the only class that opens a SqlConnection

// Services sit between Forms and Database; nothing here knows what a button is.
namespace PharmaLinkApp.Services
{
    /// <summary>Prescription control: upload, verify, and the dispatch gate.</summary>
    public class PrescriptionService
    {
        // One helper for the whole class; DbHelper opens a connection per call.
        private readonly DbHelper _db = new DbHelper();

        /// <summary>Where uploaded images are copied to, taken from App.config.</summary>
        public static string UploadFolder
        {
            // Get-only, so the "create it if missing" line runs on every read.
            get
            {
                string configured = ConfigurationManager.AppSettings["PrescriptionFolder"];   // no rebuild needed to move it
                // Missing, empty or blank all mean "nothing was configured".
                if (string.IsNullOrWhiteSpace(configured))
                    configured = Path.Combine("Uploads", "Prescriptions");   // default so a fresh install works

                string full = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);   // uploads live beside the exe
                if (!Directory.Exists(full)) Directory.CreateDirectory(full);   // first upload on a clean install works
                return full;   // absolute, and guaranteed to exist
            }
        }

        /// <summary>Copies the image into the upload folder and records it.</summary>
        public bool Upload(int orderId, int customerId, string sourceImagePath, string doctorName, out string message)
        {
            // out message, not an exception: every failure here is one the customer can fix.
            try
            {
                FileInfo file = new FileInfo(sourceImagePath);   // read the disk once, then ask it three questions

                // CHECK 1: the file may have been moved since the dialog was opened.
                if (!file.Exists)
                {
                    message = "That file no longer exists.";   // names the actual cause
                    return false;   // nothing copied, nothing written
                }

                string extension = file.Extension.ToLowerInvariant();   // invariant, so ".JPG" matches on any culture
                // CHECK 2: an allow-list of formats a PictureBox can display.
                if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
                {
                    message = "Only JPG and PNG images are accepted.";   // states the whole allow-list
                    return false;   // refused before any copy, so no stray file is left
                }

                // CHECK 3: size, written as 2 * 1024 * 1024 so it reads as "2 MB".
                if (file.Length > 2 * 1024 * 1024)
                {
                    message = "The image must be smaller than 2 MB.";   // gives the limit, not just "too large"
                    return false;   // last gate before the file is copied for real
                }

                // Order number plus a timestamp: unique, traceable, and sorts by date.
                string storedName = "rx-" + orderId + "-" + DateTime.Now.ToString("yyyyMMddHHmmss") + extension;
                string destination = Path.Combine(UploadFolder, storedName);   // asking for the folder also creates it

                // Copy, not Move: the customer keeps their own photograph. true = overwrite.
                File.Copy(sourceImagePath, destination, true);

                // Store the RELATIVE path; an absolute one is meaningless on another machine.
                string relative = Path.Combine("Uploads", "Prescriptions", storedName);

                // const, so the text is fixed at compile time and cannot be rebuilt at run time.
                const string sql = @"
INSERT INTO Prescriptions (OrderId, CustomerId, ImagePath, DoctorName)   -- VerifyStatus defaults to 'Pending'
-- Placeholders, never values: the data travels separately as SqlParameters.
VALUES (@OrderId, @CustomerId, @ImagePath, @DoctorName);";

                // ExecuteNonQuery: nothing here needs the new PrescriptionId back.
                _db.ExecuteNonQuery(sql,
                    DbHelper.P("@OrderId", orderId),   // ties the image to the order the queue joins on
                    DbHelper.P("@CustomerId", customerId),   // who uploaded it
                    DbHelper.P("@ImagePath", relative),   // the portable path, not "destination"
                    // An empty doctor name becomes SQL NULL, so "not given" is one value not two.
                    DbHelper.P("@DoctorName", string.IsNullOrWhiteSpace(doctorName) ? null : doctorName.Trim()));

                // The wording warns that uploading is not the end of it.
                message = "Prescription uploaded. The pharmacy will verify it before dispatch.";
                return true;   // file is on disk AND the row is in the table
            }
            catch (Exception ex)   // broad: this service can fail at the file system as well as the database
            {
                // A locked file, a full disk or a database refusal all arrive as readable text.
                message = ex.Message;
                return false;   // same false as the gates, so the caller has one failure path
            }
        }

        /// <summary>Requirement 16: the pharmacy's own verification queue.</summary>
        public DataTable GetQueueForPharmacy(int pharmacyId, string verifyStatus)
        {
            // Returns a DataTable because the result is bound straight to a grid.
            const string sql = @"
SELECT  p.PrescriptionId, p.OrderId, u.FullName AS Customer, p.DoctorName,   -- who, and which order
        p.ImagePath, p.UploadedAt, p.VerifyStatus, o.Status AS OrderStatus,   -- path is resolved before display
        o.TotalAmount   -- the value riding on this decision, shown beside the image
FROM    Prescriptions p   -- one row per uploaded image is exactly one queue row
        INNER JOIN Orders o ON o.OrderId    = p.OrderId   -- Prescriptions has no PharmacyId of its own
        INNER JOIN Users  u ON u.UserId     = p.CustomerId   -- turns CustomerId into a name
WHERE   o.PharmacyId = @PharmacyId   -- isolation: one shop never sees another's prescriptions
  AND   (@VerifyStatus = '' OR p.VerifyStatus = @VerifyStatus)   -- '' means every status, so one query drives every tab
-- Pending first whatever the date, then oldest upload first within each group.
ORDER BY CASE p.VerifyStatus WHEN 'Pending' THEN 0 ELSE 1 END, p.UploadedAt;";

            // ExecuteTable returns an empty table, not null, so the grid still shows headers.
            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),   // from the signed-in session, never typed
                DbHelper.P("@VerifyStatus", verifyStatus ?? ""));   // null would match nothing, so it becomes ''
        }

        /// <summary>Approve or reject, scoped to the owner's own pharmacy.</summary>
        public bool SetVerifyStatus(int prescriptionId, int pharmacyId, string newStatus)
        {
            // Returns bool so the form can tell "approved" from "that was not yours".
            const string sql = @"
UPDATE  p SET p.VerifyStatus = @Status   -- 'p' names which table of the join is written
FROM    Prescriptions p INNER JOIN Orders o ON o.OrderId = p.OrderId   -- the join only reaches PharmacyId
-- Id picks the row, PharmacyId proves the caller is entitled to it.
WHERE   p.PrescriptionId = @Id AND o.PharmacyId = @PharmacyId;";

            // Rows affected is the proof: 1 means the row existed AND belonged to this shop.
            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@Status", newStatus),   // a CHECK constraint on the column refuses a typo
                DbHelper.P("@Id", prescriptionId),   // which row: the selected grid line
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // one row changed, so it was his
        }

        // The owner's dashboard tile: how much verification work is waiting.
        public int CountPending(int pharmacyId)
        {
            // ExecuteScalarInt turns an empty result into 0, so the tile is never blank.
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)   -- one number back, rather than shipping every pending row to count here
FROM    Prescriptions p INNER JOIN Orders o ON o.OrderId = p.OrderId   -- same join as the queue
-- This shop only, and only the work that is still outstanding.
WHERE   o.PharmacyId = @Id AND p.VerifyStatus = 'Pending';",
                DbHelper.P("@Id", pharmacyId));   // the signed-in owner's shop
        }

        // Every prescription on one order, for the customer's order-detail screen.
        public DataTable GetForOrder(int orderId)
        {
            // Inline SQL rather than a const: short, and used in exactly one place.
            return _db.ExecuteTable(@"
SELECT  PrescriptionId, OrderId, ImagePath, DoctorName, UploadedAt, VerifyStatus   -- path and status
-- Newest first: a re-upload after a rejection belongs at the top.
FROM    Prescriptions WHERE OrderId = @OrderId ORDER BY UploadedAt DESC;",
                DbHelper.P("@OrderId", orderId));   // no PharmacyId: the caller knows the order
        }

        // The gate dispatch asks before a prescription order can move on.
        public bool OrderHasApprovedPrescription(int orderId)
        {
            // Counted and compared to zero, so the caller gets a plain bool.
            return _db.ExecuteScalarInt(
                // 'Approved' specifically: a pending or rejected image must not dispatch.
                "SELECT COUNT(*) FROM Prescriptions WHERE OrderId = @Id AND VerifyStatus = 'Approved';",
                DbHelper.P("@Id", orderId)) > 0;   // at least one approved image is enough
        }

        /// <summary>The absolute path of a stored image, for a picture box.</summary>
        public static string ResolveImagePath(string storedRelativePath)
        {
            // static, so a form can call it without constructing a service.
            if (string.IsNullOrWhiteSpace(storedRelativePath)) return "";   // safe to assign to ImageLocation
            // Defensive: a value that is already absolute must not be combined again.
            if (Path.IsPathRooted(storedRelativePath)) return storedRelativePath;
            // Same BaseDirectory that Upload used, so a moved copy finds its own images.
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, storedRelativePath);
        }
    }
}
