namespace OwnPlanner.Application.Account;

/// <summary>
/// The result of building an account data export: a temporary archive file on disk that the caller
/// is responsible for streaming to the user and deleting once the response has been sent.
/// </summary>
/// <param name="FilePath">Absolute path to the generated archive in the server's temp directory.</param>
/// <param name="FileName">Suggested download file name (e.g. <c>ownplanner-export-20260627.zip</c>).</param>
/// <param name="ContentType">MIME type of the archive (<c>application/zip</c>).</param>
public sealed record AccountExport(string FilePath, string FileName, string ContentType);
