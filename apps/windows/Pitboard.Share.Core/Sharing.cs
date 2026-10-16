namespace Pitboard.Share.Core;

// A share target's ShareOperation and Windows' launcher, which tests replace.
public interface IShareOperation
{
    void ReportStarted();

    Task<string?> WebLinkAsync();

    Task<string?> TextAsync();

    Task<bool> LaunchAsync(string link);

    void ReportCompleted();

    void ReportError(string sentence);
}

// Pitboard.exe checks the link it is handed again, as it does any link.
public static class Sharing
{
    public const string NotLaunched = "Windows found no Pitboard app to open the page in.";

    public static async Task HandOverAsync(IShareOperation share, string scheme)
    {
        ArgumentNullException.ThrowIfNull(share);
        share.ReportStarted();
        var text = await share.WebLinkAsync().ConfigureAwait(false) ?? await share.TextAsync().ConfigureAwait(false) ?? "";
        string link;
        try
        {
            link = PitboardShareFfiMethods.ShareLink(text, scheme);
        }
        catch (ShareRefusal.Refused refused)
        {
            share.ReportError(refused.reason);
            return;
        }

        if (await share.LaunchAsync(link).ConfigureAwait(false))
        {
            share.ReportCompleted();
        }
        else
        {
            share.ReportError(NotLaunched);
        }
    }
}
