namespace PharmaLinkApp.Database
{
    /// <summary>
    /// A database failure that has already been turned into a sentence a user
    /// can act on.
    ///
    /// Every SqlException raised inside DbHelper is wrapped in one of these, so
    /// no screen in the application ever shows raw SQL Server text. The original
    /// SqlException is kept as InnerException for debugging.
    /// </summary>
    // INHERITANCE, and the clearest example in the project outside the 28 Forms.
    // Deriving from Exception means this can be thrown and caught like any other
    // exception, and a catch(Exception) anywhere still works - but code that cares can
    // catch(DataAccessException) specifically, which is exactly what Program.ReportFatal
    // does to tell an already-translated database failure from a raw one.
    public class DataAccessException : Exception
    {
        // CONSTRUCTOR CHAINING: ": base(...)" runs the Exception constructor before this
        // one's body. Message and InnerException are read-only properties on Exception,
        // so passing them up is the ONLY way to set them - this class stores nothing
        // itself. Keeping the original SqlException as inner means the friendly sentence
        // is shown to the user while the real error number survives for debugging.
        public DataAccessException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
