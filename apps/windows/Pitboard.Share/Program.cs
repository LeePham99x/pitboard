using Pitboard.Share;
using Pitboard.Share.Core;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

if (args is ["--register"])
{
    return await Registration.RegisterAsync();
}

if (Activation() is not ShareTargetActivatedEventArgs shared)
{
    return 2;
}

// Only a Release build, whose MSI registers the scheme, has a share target.
await Sharing.HandOverAsync(new SystemShareOperation(shared.ShareOperation), "pitboard");
return 0;

static IActivatedEventArgs? Activation()
{
    try
    {
        return AppInstance.GetActivatedEventArgs();
    }
    catch (InvalidOperationException)
    {
        // No package identity: started some other way than by Windows' share sheet.
        return null;
    }
}
