using System.Configuration;         // ConfigurationManager, to read the upload folder from App.config
using System.Data;                  // DataTable, the shape every read returns
using PharmaLinkApp.Database;       // DbHelper, the only class that opens a SqlConnection

namespace PharmaLinkApp.Services
{
    /// <summary>
    /// Prescription control (requirements 16 and 26).
    ///
    /// A medicine flagged RequiresRx cannot be dispatched until the customer has
    /// uploaded a photograph of the doctor's prescription and the pharmacy owner
    /// has approved that image.
    /// </summary>
    // The file itself lives on disk and only its PATH lives in the database. Storing the
    // image bytes in a column was rejected: a VARBINARY of photographs bloats every
    // backup and has to be streamed out again before a PictureBox can show it, whereas a
    // short relative path is cheap to read and the file can be opened directly.
    public class PrescriptionService
    {
        // One helper for the whole class; DbHelper opens and closes a connection per call.
        private readonly DbHelper _db = new DbHelper();

        /// <summary>Where uploaded images are copied to, taken from App.config.</summary>
        // static, because the folder is a property of the installation rather than of any
        // one service object, and ResolveImagePath below is static for the same reason -
        // a form showing an image needs neither a database connection nor an instance.
        public static string UploadFolder
        {
            // A get-only property rather than a field: the folder is recomputed on each
            // read, which is what makes the "create it if it is missing" line below
            // effective even if somebody deletes the folder while the program is running.
            get
            {
                // Read from App.config so the location can be changed without a rebuild.
                string configured = ConfigurationManager.AppSettings["PrescriptionFolder"];
                // IsNullOrWhiteSpace, not != null: a setting that is missing, empty, or
                // just spaces all mean the same thing - nothing usable was configured -
                // and a path of " " would otherwise be accepted and fail later.
                if (string.IsNullOrWhiteSpace(configured))
                    // A sensible default so a fresh copy of the application works with no
                    // configuration at all. Path.Combine rather than "Uploads\\Prescriptions"
                    // because the separator is the platform's business, not this code's.
                    configured = Path.Combine("Uploads", "Prescriptions");

                // BaseDirectory is the folder the executable is running from, so the
                // uploads end up beside the program rather than in whatever directory the
                // user happened to browse to last. Path.Combine also copes with the
                // configured value being absolute, in which case it wins outright.
                string full = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
                // Created on demand, so the first upload on a clean install works instead
                // of failing with "could not find a part of the path". CreateDirectory
                // creates every missing level and does nothing if the folder already
                // exists, so the Exists test is only there to keep the intent readable.
                if (!Directory.Exists(full)) Directory.CreateDirectory(full);
                return full;
            }
        }

        /// <summary>
        /// Copies the chosen image into the application's upload folder and
        /// records it against the order. Only JPG and PNG under 2 MB are taken.
        /// </summary>
        // out string message rather than a thrown exception: every failure here is
        // something the customer can fix - wrong file type, too big, file has moved - so
        // the method returns false with a sentence to show, and the caller needs no
        // try/catch to put text on the screen.
        public bool Upload(int orderId, int customerId, string sourceImagePath, string doctorName, out string message)
        {
            // The whole body is wrapped because this is the one service that touches the
            // file system as well as the database. File.Copy can fail for reasons no
            // validation can anticipate - a locked file, a full disk, a revoked network
            // share - and those must reach the customer as a message, not as a crash.
            try
            {
                // FileInfo is read once and then asked three questions below. Calling
                // File.Exists, then Path.GetExtension, then new FileInfo().Length would
                // hit the disk three times and could see three different states.
                FileInfo file = new FileInfo(sourceImagePath);

                // CHECK 1: the file is still there. The path came from a file dialog that
                // may have been open for minutes, and the customer could have moved or
                // deleted the photograph since choosing it.
                if (!file.Exists)
                {
                    message = "That file no longer exists.";
                    return false;   // false means nothing was copied and nothing was written
                }

                // ToLowerInvariant, not ToLower: the comparison must not depend on the
                // machine's culture. The invariant form is what keeps ".JPG" matching
                // ".jpg" identically on every regional setting.
                string extension = file.Extension.ToLowerInvariant();
                // CHECK 2: an image format a PictureBox can actually display. Both spellings
                // of the JPEG extension are accepted because cameras and phones use each.
                // An allow-list, never a block-list: listing what is permitted means a
                // format nobody thought of is refused rather than quietly let through.
                if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
                {
                    message = "Only JPG and PNG images are accepted.";
                    return false;
                }

                // CHECK 3: size. Written as 2 * 1024 * 1024 rather than 2097152 so the
                // limit reads as "2 MB" at a glance; the compiler folds it to a constant,
                // so spelling it out costs nothing at run time. Length is a long, so the
                // comparison does not overflow on a very large file.
                if (file.Length > 2 * 1024 * 1024)
                {
                    message = "The image must be smaller than 2 MB.";
                    return false;
                }

                // Build a unique stored name from the order number plus a timestamp to the
                // second. Keeping the customer's original filename would collide the
                // moment two people both uploaded "photo.jpg", and embedding the OrderId
                // means a file on disk can always be traced back to its order.
                // The date format is deliberately yyyyMMddHHmmss: fixed width, no
                // separators that are illegal in a filename, and it sorts chronologically
                // in a plain directory listing.
                string storedName = "rx-" + orderId + "-" + DateTime.Now.ToString("yyyyMMddHHmmss") + extension;
                // Combined against the property above, so the folder is created if needed
                // by the very act of asking where it is.
                string destination = Path.Combine(UploadFolder, storedName);

                // Copy the file INTO the application's own folder. The customer's original
                // stays where it was, and the application no longer depends on a path that
                // might be a USB stick or a file they later delete. true = overwrite.
                // Copy rather than Move for exactly that reason: taking the customer's
                // own photograph away from them would be surprising and irreversible.
                File.Copy(sourceImagePath, destination, true);

                // Store a RELATIVE path in the database, never the absolute one. An
                // absolute path from one computer is meaningless on any other machine,
                // so the relative path is resolved against the install folder at
                // display time by ResolveImagePath.
                // This is why the two halves are rebuilt here rather than reusing
                // "destination": destination is absolute by construction, and it is the
                // portable half - folder plus filename - that belongs in the column.
                string relative = Path.Combine("Uploads", "Prescriptions", storedName);

                const string sql = @"
-- A plain INSERT: the row is written only after all three checks above passed and the
-- file is safely on disk, so the database never points at a file that is not there.
-- VerifyStatus and UploadedAt are absent from the column list on purpose - the table
-- defaults them to 'Pending' and GETDATE(), so a new prescription always arrives in the
-- queue unverified and cannot be inserted as already approved.
INSERT INTO Prescriptions (OrderId, CustomerId, ImagePath, DoctorName)
VALUES (@OrderId, @CustomerId, @ImagePath, @DoctorName);";

                _db.ExecuteNonQuery(sql,
                    // OrderId ties the image to the order, which is what the owner's
                    // queue joins on; CustomerId records who uploaded it.
                    DbHelper.P("@OrderId", orderId),
                    DbHelper.P("@CustomerId", customerId),
                    // The relative path, not "destination".
                    DbHelper.P("@ImagePath", relative),
                    // DoctorName is optional. An empty text box must become SQL NULL
                    // rather than an empty string, so that "no name given" is one value in
                    // the column instead of two that look different in a grid. DbHelper.P
                    // turns the C# null into DBNull.Value on the way out.
                    DbHelper.P("@DoctorName", string.IsNullOrWhiteSpace(doctorName) ? null : doctorName.Trim()));

                // The wording sets the expectation that uploading is not the end of it:
                // the pharmacy still has to approve the image before the order moves.
                message = "Prescription uploaded. The pharmacy will verify it before dispatch.";
                return true;
            }
            catch (Exception ex)
            {
                // Everything else - a locked file, a full disk, a database refusal - is
                // reported rather than swallowed. ex.Message is already a readable
                // sentence for a database failure because DbHelper wrapped it in a
                // DataAccessException before it got here.
                message = ex.Message;
                return false;
            }
        }

        /// <summary>Requirement 16: the pharmacy's own verification queue.</summary>
        public DataTable GetQueueForPharmacy(int pharmacyId, string verifyStatus)
        {
            const string sql = @"
SELECT  p.PrescriptionId, p.OrderId, u.FullName AS Customer, p.DoctorName,
        -- The stored relative path. The form passes it through ResolveImagePath before
        -- handing it to a PictureBox, which is where it becomes absolute again.
        p.ImagePath, p.UploadedAt, p.VerifyStatus, o.Status AS OrderStatus,
        -- The order total is shown next to the image so the owner can see the value of
        -- what is waiting on this decision without opening the order.
        o.TotalAmount
FROM    Prescriptions p
        -- Orders is joined for the ownership test as much as for its columns:
        -- Prescriptions has no PharmacyId, so 'is this mine?' can only be asked of the
        -- order the prescription belongs to.
        INNER JOIN Orders o ON o.OrderId    = p.OrderId
        -- Users turns the stored CustomerId into a name, because a number on the queue
        -- would tell the owner nothing about who is waiting.
        INNER JOIN Users  u ON u.UserId     = p.CustomerId
WHERE   o.PharmacyId = @PharmacyId
  -- Optional filter: '' means every status, so one query drives the All tab and each
  -- of the Pending, Approved and Rejected tabs.
  AND   (@VerifyStatus = '' OR p.VerifyStatus = @VerifyStatus)
-- Pending first, whatever the date. The CASE turns the status into a sort key of 0 or 1
-- so the work that needs doing floats to the top, and UploadedAt then orders within each
-- group oldest first, so nobody is left waiting behind a later upload.
ORDER BY CASE p.VerifyStatus WHEN 'Pending' THEN 0 ELSE 1 END, p.UploadedAt;";

            return _db.ExecuteTable(sql,
                DbHelper.P("@PharmacyId", pharmacyId),
                // ?? "" again: SQL NULL would not equal '' and the filter would match
                // nothing, so a null argument has to become the empty string here.
                DbHelper.P("@VerifyStatus", verifyStatus ?? ""));
        }

        /// <summary>Approve or reject, scoped to the owner's own pharmacy.</summary>
        public bool SetVerifyStatus(int prescriptionId, int pharmacyId, string newStatus)
        {
            const string sql = @"
-- UPDATE with an alias and a FROM clause: 'p' says which table of the join is written to
-- while the join to Orders supplies the ownership test.
UPDATE  p SET p.VerifyStatus = @Status
FROM    Prescriptions p INNER JOIN Orders o ON o.OrderId = p.OrderId
-- Both halves matter: the PrescriptionId picks the row, the PharmacyId proves the caller
-- is entitled to it. An owner who guesses another shop's PrescriptionId changes nothing,
-- because the join cannot satisfy the second test and no row is affected.
WHERE   p.PrescriptionId = @Id AND o.PharmacyId = @PharmacyId;";

            return _db.ExecuteNonQuery(sql,
                // The permitted values are enforced by a CHECK constraint on the column
                // rather than by this method, so a typo is refused by the database and
                // arrives back as a translated message rather than being stored.
                DbHelper.P("@Status", newStatus),
                DbHelper.P("@Id", prescriptionId),
                DbHelper.P("@PharmacyId", pharmacyId)) == 1;   // one row changed, so it was his
        }

        // The number on the owner's dashboard tile: how much verification work is waiting.
        public int CountPending(int pharmacyId)
        {
            return _db.ExecuteScalarInt(@"
SELECT  COUNT(*)
-- Same join as the queue, so the tile counts precisely the rows the queue will show.
FROM    Prescriptions p INNER JOIN Orders o ON o.OrderId = p.OrderId
WHERE   o.PharmacyId = @Id AND p.VerifyStatus = 'Pending';",
                DbHelper.P("@Id", pharmacyId));
        }

        // Every prescription attached to one order, for the customer's order-detail
        // screen. No PharmacyId here: the caller already knows which order it is showing.
        public DataTable GetForOrder(int orderId)
        {
            return _db.ExecuteTable(@"
SELECT  PrescriptionId, OrderId, ImagePath, DoctorName, UploadedAt, VerifyStatus
-- No join needed: every column comes from Prescriptions itself, and adding Orders or
-- Users would only cost a join to fetch names this screen already has.
FROM    Prescriptions WHERE OrderId = @OrderId ORDER BY UploadedAt DESC;",
                // Newest first, because a re-upload after a rejection is the one the
                // customer wants to see at the top.
                DbHelper.P("@OrderId", orderId));
        }

        // The gate the dispatch step asks before allowing a prescription order to move on.
        public bool OrderHasApprovedPrescription(int orderId)
        {
            return _db.ExecuteScalarInt(
                // 'Approved' specifically, not merely 'uploaded'. An order with a pending
                // or a rejected image must not be dispatchable, which is why the status is
                // part of the WHERE rather than something the caller checks afterwards.
                "SELECT COUNT(*) FROM Prescriptions WHERE OrderId = @Id AND VerifyStatus = 'Approved';",
                // COUNT > 0, so the question is 'is there at least one', not 'how many' -
                // a customer may upload several images and only one needs to be approved.
                DbHelper.P("@Id", orderId)) > 0;
        }

        /// <summary>The absolute path of a stored image, for the picture box on the verify screen.</summary>
        // The other half of the relative-path decision made in Upload. Storing a portable
        // path is only useful if there is one place that turns it back into a real one,
        // and this is it - static, so a form can call it without constructing a service.
        public static string ResolveImagePath(string storedRelativePath)
        {
            // Nothing stored, nothing to resolve. "" rather than null so the caller can
            // assign the result to a PictureBox.ImageLocation or test .Length safely.
            if (string.IsNullOrWhiteSpace(storedRelativePath)) return "";
            // Defensive, for rows written before the relative-path rule or edited by hand:
            // if the stored value is already absolute, combining it with BaseDirectory
            // would be wrong, so it is handed back untouched.
            if (Path.IsPathRooted(storedRelativePath)) return storedRelativePath;
            // The normal case. The same BaseDirectory that UploadFolder used when the file
            // was written, so a copy of the application moved to another machine finds its
            // own images beside itself.
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, storedRelativePath);
        }
    }
}
