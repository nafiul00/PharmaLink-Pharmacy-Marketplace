// The namespace deliberately matches the folder: PharmaLinkApp.Database holds the two
// classes that know SQL Server exists at all. Because DbHelper lives in this same
// namespace it can throw this type without a using directive, and everything above the
// data layer has to name PharmaLinkApp.Database explicitly to catch it, which makes the
// dependency visible in the using list at the top of each service file.
//
// There is no "using System;" line anywhere in this file even though Exception is
// System.Exception. The project sets <ImplicitUsings>enable</ImplicitUsings>, so the
// compiler supplies System and a handful of other common namespaces automatically.
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
    //
    // WHY A NEW TYPE AT ALL, rather than throwing Exception with a nicer message: the
    // type itself carries information the message cannot. "This is a database problem,
    // and the text has already been translated" is a fact about the exception, so it
    // belongs in the exception's type. Throwing a plain Exception would force every
    // catch block to inspect the message text to work out where the failure came from,
    // which is brittle the moment a message is reworded.
    //
    // WHY IT IS DELIBERATELY EMPTY: no extra fields, no error-code property, no
    // overridden members. Everything it needs - Message, InnerException, StackTrace,
    // ToString - is inherited from Exception and behaves correctly already. Adding a
    // copy of the SQL error number here would duplicate state that InnerException
    // already holds, and duplicated state is state that can disagree with itself.
    //
    // WHY IT IS NOT [Serializable] with the usual four constructors: this application
    // never sends an exception across a process or an AppDomain boundary. It is thrown
    // in DbHelper, caught a few frames up in a form or in Program.cs, and shown in a
    // MessageBox. Constructors that nothing calls are dead code, so only the one
    // overload DbHelper actually uses is declared below.
    public class DataAccessException : Exception
    {
        // CONSTRUCTOR CHAINING: ": base(...)" runs the Exception constructor before this
        // one's body. Message and InnerException are read-only properties on Exception,
        // so passing them up is the ONLY way to set them - this class stores nothing
        // itself. Keeping the original SqlException as inner means the friendly sentence
        // is shown to the user while the real error number survives for debugging.
        //
        // The two parameters are exactly what DbHelper has to hand at the throw site:
        // "message" is the output of DbHelper.Describe(ex), the sentence written for the
        // person at the keyboard, and "inner" is the SqlException that was caught. The
        // signature takes Exception rather than SqlException on purpose - the parameter
        // only needs to be storable as InnerException, and widening it means this type
        // is not welded to Microsoft.Data.SqlClient.
        public DataAccessException(string message, Exception inner)
            // base(message, inner) hands both values to Exception's own two-argument
            // constructor. Nothing else is needed: after this line Message returns the
            // translated sentence and InnerException returns the SqlException, which is
            // why the body below is empty rather than unfinished.
            : base(message, inner)
        {
            // Intentionally empty. Any work done here would run AFTER the base
            // constructor has already set both properties, and there is nothing left
            // to do. Logging from a constructor was rejected as well: an exception
            // object being built is not yet an exception that will be thrown, and
            // logging here would record failures that a caller goes on to handle.
        }
    }
}
