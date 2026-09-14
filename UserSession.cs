// Root namespace: the session is not a service or a row, it is process state.
namespace PharmaLinkApp
{
    /// <summary>Who is logged in; PharmacyId isolates one owner's data.</summary>
    public static class UserSession
    {
        // Static: one session per process, which suits a one user desktop app.
        public static int UserId;                // used by every customer side query
        public static string FullName = "";      // greeted on the dashboard header
        public static string Email = "";         // shown on the profile screen
        public static string UserType = "";      // 'SuperAdmin' | 'Admin' | 'Customer'

        // Set once at login from the row, never a control, so scope cannot be widened.
        public static int PharmacyId;            // 0 when the user is not a pharmacy owner
        public static string PharmacyName = "";  // printed on the owner's screens and invoices

        // Three read only properties, so the role string is compared in one place only.
        public static bool IsSuperAdmin => UserType == "SuperAdmin";   // the platform operator; sees every shop
        public static bool IsAdmin => UserType == "Admin";             // a pharmacy owner, narrowed by PharmacyId
        public static bool IsCustomer => UserType == "Customer";       // a shopper; the only role with a cart

        // Static fields outlive every form, so logging out means overwriting them by hand.
        public static void Clear()
        {
            UserId = 0;          // no Users row has id 0, so a stale query matches nothing
            FullName = "";       // clears the greeting, so no half-reset session shows the old name
            Email = "";          // clears the profile screen's identifier
            UserType = "";       // empty closes all three role gates at once
            PharmacyId = 0;      // the critical one: the next login cannot inherit this owner's scope
            PharmacyName = "";   // cleared with the id it describes, or the header keeps the old shop
        }
    }
}
