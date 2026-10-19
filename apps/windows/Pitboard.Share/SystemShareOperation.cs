using Pitboard.Share.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;

namespace Pitboard.Share;

internal sealed class SystemShareOperation(ShareOperation operation) : IShareOperation
{
    public void ReportStarted() => operation.ReportStarted();

    public async Task<string?> WebLinkAsync() =>
        operation.Data.Contains(StandardDataFormats.WebLink) ? (await operation.Data.GetWebLinkAsync()).AbsoluteUri : null;

    public void StartPitboard(string link) => Handoff.Start(AppContext.BaseDirectory, link);

    public void ReportCompleted() => operation.ReportCompleted();

    public void ReportError(string sentence) => operation.ReportError(sentence);
}
