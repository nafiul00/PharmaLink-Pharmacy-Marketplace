using System.Configuration;
using System.Data;
using PharmaLinkApp.Database;

namespace PharmaLinkApp.Services
{
    /// <summary>
    /// Prescription control (requirements 16 and 26).
    ///
    /// A medicine flagged RequiresRx cannot be dispatched until the customer has
    /// uploaded a photograph of the doctor's prescription and the pharmacy owner
    /// has approved that image.
    /// </summary>
    public class PrescriptionService
    {
        private readonly DbHelper _db = new DbHelper();

        /// <summary>Where uploaded images are copied to, taken from App.config.</summary>
        public static string UploadFolder
        {
            get
            {
                string configured = ConfigurationManager.AppSettings["PrescriptionFolder"];
                if (string.IsNullOrWhiteSpace(configured))
                    configured = Path.Combine("Uploads", "Prescriptions");

                string full = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
                if (!Directory.Exists(full)) Directory.CreateDirectory(full);
                return full;
            }
        }

        /// <summary>
        /// Copies the chosen image into the application's upload folder and
        /// records it against the order. Only JPG and PNG under 2 MB are taken.
        /// </summary>
        public bool Upload(int orderId, int customerId, string sourceImagePath, string doctorName, out string message)
        {
            try
            {
                FileInfo file = new FileInfo(sourceImagePath);

                if (!file.Exists)
                {
                    message = "That file no longer exists.";
                    return false;
                }

                string extension = file.Extension.ToLowerInvariant();
                if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
                {
                    message = "Only JPG and PNG images are accepted.";
                    return false;
                }

                if (file.Length > 2 * 1024 * 1024)
                {
                    message = "The image must be smaller than 2 MB.";
                    return false;
                }

                // Build a unique stored name from the order number plus a timestamp to the
                // second. Keeping the customer's original filename would collide the
                // moment two people both uploaded "photo.jpg", and embedding the OrderId
                // means a file on disk can always be traced back to its order.
                string storedName = "rx-" + orderId + "-" + DateTime.Now.ToString("yyyyMMddHHmmss") + extension;
                string destination = Path.Combine(UploadFolder, storedName);

                // Copy the file INTO the application's own folder. The customer's original
                // stays where it was, and the application no longer depends on a path that
                // might be a USB stick or a file they later delete. true = overwrite.
                File.Copy(sourceImagePath, destination, true);

                // Store a RELATIVE path in the database, never the absolute one. An
                // absolute path from one computer is meaningless on any other machine,
                // so the relative path is resolved against the install folder at
                // display time by ResolveImagePath.
                string relative = Path.Combine("Uploads", "Prescriptions", storedName);

                const string sql = @"
INSERT INTO Prescriptions (OrderId, CustomerId, ImagePath, DoctorName)
VALUES (@OrderId, @CustomerId, @ImagePath, @DoctorName);";

                _db.ExecuteNonQuery(sql,
                    DbHelper.P("@OrderId", orderId),
                    DbHelper.P("@CustomerId", customerId),
                    DbHelper.P("@ImagePath", relative),
                    DbHelper.P("@DoctorName", string.IsNullOrWhiteSpace(doctorName) ? null : doctorName.Trim()));

                message = "Prescription uploaded. The pharmacy will verify it before dispatch.";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        /// <summary>Requirement 16: the pharmacy's own verification queue.</summary>
        public DataTable GetQueueForPharmacy(int pharmacyId, string verifyStatus)
        {
            const string sql = @"
SELECT  p.PrescriptionId, p.OrderId, u.FullName AS Customer, p.DoctorName,
        p.ImagePath, p.UploadedAt, p.VerifyStatus, o.Status AS OrderStatus,
        o.TotalAmount
FROM    Prescriptions p
        INNER JOIN Orders o ON o.OrderId    = p.OrderId
        INNER JOIN Users  u ON u.UserId     = p.CustomerId
WHERE   o.PharmacyId = @PharmacyId
  AND   (@VerifyStatus = '' OR p.VerifyStatus = @VerifyStatus)
ORDER BY CASE p.VerifyStatus WHEN 'Pending' THEN 0 ELSE 1 END, p.UploadedAt;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                DbHelper.P("@VerifyStatus", verifyStatus ?? ""));
        }

        /// <summary>Approve or reject, scoped to the owner's own pharmacy.</summary>
        public bool SetVerifyStatus(int prescriptionId, int pharmacyId, string newStatus)
        {
            const string sql = @"
UPDATE  p SET p.VerifyStatus = @Status
FROM    Prescriptions p INNER JOIN Orders o ON o.OrderId = p.OrderId
WHERE   p.PrescriptionId = @Id AND o.PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,
                DbHelper.P("@Status", newStatus),
                DbHelper.P("@Id", prescriptionId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;
        }

        public int CountPending(int pharmacyId)
        {
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)
FROM    Prescriptions p INNER JOIN Orders o ON o.OrderId = p.OrderId
WHERE   o.PharmacyId = @Id AND p.VerifyStatus = 'Pending';",
                DbHelper.P("@Id", pharmacyId));
        }

        public DataTable GetForOrder(int orderId)
        {
            return _db.ExecuteTable(@"
SELECT  PrescriptionId, OrderId, ImagePath, DoctorName, UploadedAt, VerifyStatus
FROM    Prescriptions WHERE OrderId = @OrderId ORDER BY UploadedAt DESC;",
                DbHelper.P("@OrderId", orderId));
        }

        public bool OrderHasApprovedPrescription(int orderId)
        {
            return _db.ExecuteScalarInt(
                "SELECT COUNT(*) FROM Prescriptions WHERE OrderId = @Id AND VerifyStatus = 'Approved';",
                DbHelper.P("@Id", orderId)) > 0;
        }

        /// <summary>The absolute path of a stored image, for the picture box on the verify screen.</summary>
        public static string ResolveImagePath(string storedRelativePath)
        {
            if (string.IsNullOrWhiteSpace(storedRelativePath)) return "";
            if (Path.IsPathRooted(storedRelativePath)) return storedRelativePath;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, storedRelativePath);
        }
    }
}
