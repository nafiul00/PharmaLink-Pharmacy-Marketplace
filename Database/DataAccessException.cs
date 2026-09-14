// Namespace matches the folder - only this layer knows SQL Server exists.
namespace PharmaLinkApp.Database // No using System; ImplicitUsings supplies it.
{
    /// <summary>A database error already turned into a readable message.</summary>
    public class DataAccessException : Exception // Inheritance: catch it on its own.
    {
        // Chains to Exception: Message and InnerException are read-only, set via base.
        public DataAccessException(string message, Exception inner)
            : base(message, inner) // Keeps the SqlException as inner for debugging.
        {
            // Empty on purpose - the base constructor already set both properties.
        }
    }
}
