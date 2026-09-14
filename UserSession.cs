namespace PharmaLinkApp
{
    /// <summary>
    /// Holds the identity of the person who is logged in.
    ///
    /// PharmacyId is the important one: every Admin side query in the
    /// application carries "WHERE PharmacyId = @PharmacyId" and reads the
    /// value from here, which is what stops one pharmacy owner from ever
    /// seeing another owner's medicines, orders or earnings.
    /// It is 0 for a SuperAdmin and for a Customer, because neither of them
    /// owns a shop.
    /// </summary>
    public static class UserSession
    {
        // Static, so there is exactly one session for the whole process and any form can
        // read it without being handed a reference. A desktop application has one user
        // at a time, which is what makes that safe here - the same design in a web
        // application would leak one user's identity into another's request.
        //
        // WHAT STATIC MEANS FOR THE LIFETIME OF THESE VALUES: the fields belong to the
        // type, not to any object, so they come into existence when the class is first
        // touched and live until the process ends. Nobody ever writes "new UserSession()"
        // and there is nothing to dispose. In practice the values are written once by
        // LoginForm and read by every screen opened afterwards, which is why logging out
        // has to call Clear() explicitly - closing a form does not clear anything, and
        // the process is still running.
        public static int UserId;                // used by every customer side query
        public static string FullName = "";      // greeted on the dashboard header
        public static string Email = "";         // shown on the profile screen
        public static string UserType = "";      // 'SuperAdmin' | 'Admin' | 'Customer'

        // THE MOST IMPORTANT FIELD IN THE APPLICATION.
        // Every Admin side query carries WHERE PharmacyId = @PharmacyId and takes the
        // value from here, set once at login from the database row. Because it is never
        // read from a textbox, a combo box or a grid cell, there is nothing on any
        // screen a user could edit to widen their own scope. Data isolation is a
        // consequence of this one field being the only source.
        public static int PharmacyId;            // 0 when the user is not a pharmacy owner
        public static string PharmacyName = "";  // printed on the owner's screens and invoices

        // Three read only properties that ask the same question three ways, so forms can
        // write "if (UserSession.IsAdmin)" instead of comparing strings themselves. => is
        // expression-bodied syntax: each one is evaluated on every read, so it always
        // reflects the current UserType and can never be left stale. Keeping the string
        // comparison in one place also means a typo like "Admn" cannot spread.
        public static bool IsSuperAdmin => UserType == "SuperAdmin";
        public static bool IsAdmin => UserType == "Admin";
        public static bool IsCustomer => UserType == "Customer";

        // Logging out. Because the fields are static they survive the closing of every
        // form, so the only way to end a session is to overwrite them by hand. Every one
        // is reset, not just UserId: leaving PharmacyId behind would mean the next person
        // to log in on this machine could inherit the previous owner's scope, which is
        // the single worst thing that could happen to the isolation rule above.
        public static void Clear()
        {
            UserId = 0;
            FullName = "";
            Email = "";
            UserType = "";
            PharmacyId = 0;
            PharmacyName = "";
        }
    }
}
