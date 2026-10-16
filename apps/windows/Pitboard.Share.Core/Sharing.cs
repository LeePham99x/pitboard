namespace Pitboard.Share.Core;

// A share target's ShareOperation and Windows' launcher, which tests replace.
public interface IShareOperation
{
    void ReportStarted();

    Task<string?> WebLinkAsync();

    Task<bool> LaunchAsync(string link);

    void ReportCompleted();

    void ReportError(string sentence);
}

// Pitboard.exe checks the link it is handed again, as it does any link.
public static class Sharing
{
    public const string NotLaunched = "Windows found no Pitboard app to open the page in.";

    public const string NotRead = "Pitboard couldn’t read the link that was shared. Share the page again.";

    // An exception that left the program would end it without a word to the share sheet.
    public static async Task HandOverAsync(IShareOperation share, string scheme)
    {
        ArgumentNullException.ThrowIfNull(share);
        share.ReportStarted();
        string? webLink;
        try
        {
            webLink = await share.WebLinkAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            share.ReportError(NotRead);
            return;
        }

        string link;
        try
        {
            link = PitboardShareFfiMethods.ShareLink(webLink ?? "", scheme);
        }
        catch (ShareRefusal.Refused refused)
        {
            share.ReportError(refused.reason);
            return;
        }

        bool launched;
        try
        {
            launched = await share.LaunchAsync(link).ConfigureAwait(false);
        }
        catch (Exception)
        {
            launched = false;
        }

        if (launched)
        {
            share.ReportCompleted();
        }
        else
        {
            share.ReportError(NotLaunched);
        }
    }
}
