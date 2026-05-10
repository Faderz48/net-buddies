using Avalonia.Media.Imaging;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NetBuddies.App.Services;
using NetBuddies.Core;

namespace NetBuddies.App.ViewModels;

public sealed partial class BuddyViewModel : ViewModelBase
{
    public BuddyViewModel(BuddyProfile profile)
    {
        Name = profile.Name;
        Status = string.IsNullOrWhiteSpace(profile.Status) ? "Online" : profile.Status;
        PersonalMessage = string.IsNullOrWhiteSpace(profile.PersonalMessage)
            ? Status
            : profile.PersonalMessage;
        ProfileImageBase64 = profile.ProfileImageBase64;
        ProfileImage = ImageFromBase64(ProfileImageBase64);
        ProfileImageAsset = AnimatedImageFromBase64(ProfileImageBase64, $"{Name}-profile");
    }

    public string Name { get; }
    public string Status { get; }
    public IBrush StatusBrush => Status switch
    {
        "Away" => new SolidColorBrush(Color.FromRgb(214, 154, 0)),
        "Busy" => new SolidColorBrush(Color.FromRgb(199, 55, 55)),
        "Invisible" => new SolidColorBrush(Color.FromRgb(123, 135, 148)),
        _ => new SolidColorBrush(Color.FromRgb(45, 170, 63))
    };
    public string PersonalMessage { get; }
    public string ProfileImageBase64 { get; }

    [ObservableProperty]
    private Bitmap? _profileImage;

    [ObservableProperty]
    private GameImageAsset? _profileImageAsset;

    private static Bitmap? ImageFromBase64(string imageBase64)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(imageBase64);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    private static GameImageAsset? AnimatedImageFromBase64(string imageBase64, string name)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(imageBase64);
            return GameAssetService.LoadAnimatedFromBytes(name, bytes);
        }
        catch
        {
            return null;
        }
    }
}
