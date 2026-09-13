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
        public static int UserId;                // used by every customer side query
        public static string FullName = "";
        public static string Email = "";
        public static string UserType = "";      // 'SuperAdmin' | 'Admin' | 'Customer'

        // THE MOST IMPORTANT FIELD IN THE APPLICATION.
        // Every Admin side query carries WHERE PharmacyId = @PharmacyId and takes the
        // value from here, set once at login from the database row. Because it is never
        // read from a textbox, a combo box or a grid cell, there is nothing on any
        // screen a user could edit to widen their own scope. Data isolation is a
        // consequence of this one field being the only source.
        public static int PharmacyId;            // 0 when the user is not a pharmacy owner
        public static string PharmacyName = "";

        public static bool IsSuperAdmin => UserType == "SuperAdmin";
        public static bool IsAdmin => UserType == "Admin";
        public static bool IsCustomer => UserType == "Customer";

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
