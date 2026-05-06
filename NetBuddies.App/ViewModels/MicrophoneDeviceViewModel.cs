namespace NetBuddies.App.ViewModels;

public sealed class MicrophoneDeviceViewModel(int deviceNumber, string name) : ViewModelBase
{
    public int DeviceNumber { get; } = deviceNumber;
    public string Name { get; } = name;
}
