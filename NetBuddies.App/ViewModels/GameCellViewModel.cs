using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using NetBuddies.App.Services;

namespace NetBuddies.App.ViewModels;

public sealed partial class GameCellViewModel(int index) : ViewModelBase
{
    public int Index { get; } = index;
    public int Row => Index / 8;
    public int Column => Index % 8;

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private IBrush _background = Brushes.White;

    [ObservableProperty]
    private IBrush _foreground = Brushes.Black;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _showX;

    [ObservableProperty]
    private bool _showO;

    [ObservableProperty]
    private bool _showChecker;

    [ObservableProperty]
    private bool _showKing;

    [ObservableProperty]
    private bool _showFlag;

    [ObservableProperty]
    private bool _showNumber;

    [ObservableProperty]
    private string _numberText = "";

    [ObservableProperty]
    private IBrush _numberBrush = Brushes.Navy;

    [ObservableProperty]
    private IBrush _pieceFill = Brushes.Transparent;

    [ObservableProperty]
    private IBrush _pieceStroke = Brushes.Transparent;

    [ObservableProperty]
    private IBrush _accentBrush = Brushes.Transparent;

    [ObservableProperty]
    private bool _showImage;

    [ObservableProperty]
    private string _imageSource = "";

    [ObservableProperty]
    private string _tileImageSource = "";

    [ObservableProperty]
    private Bitmap? _pieceImage;

    [ObservableProperty]
    private Bitmap? _tileImage;

    [ObservableProperty]
    private GameImageAsset? _pieceAsset;

    [ObservableProperty]
    private GameImageAsset? _tileAsset;

    private string _pieceAssetKey = "";
    private string _tileAssetKey = "";

    public void SetPieceAsset(string? relativePath, bool randomizeStart = false, int maxInitialDelayMilliseconds = 0)
    {
        SetAsset(relativePath, randomizeStart, maxInitialDelayMilliseconds, isPiece: true);
    }

    public void SetTileAsset(string? relativePath)
    {
        SetAsset(relativePath, randomizeStart: false, maxInitialDelayMilliseconds: 0, isPiece: false);
    }

    public void ClearVisuals()
    {
        Text = "";
        ShowX = false;
        ShowO = false;
        ShowChecker = false;
        ShowKing = false;
        ShowFlag = false;
        ShowNumber = false;
        NumberText = "";
        NumberBrush = Brushes.Navy;
        PieceFill = Brushes.Transparent;
        PieceStroke = Brushes.Transparent;
        AccentBrush = Brushes.Transparent;
        ShowImage = false;
        ImageSource = "";
        TileImageSource = "";
        PieceImage = null;
        TileImage = null;
    }

    private void SetAsset(string? relativePath, bool randomizeStart, int maxInitialDelayMilliseconds, bool isPiece)
    {
        var key = relativePath is null
            ? ""
            : $"{relativePath}|{randomizeStart}|{maxInitialDelayMilliseconds}";
        if (isPiece && key == _pieceAssetKey || !isPiece && key == _tileAssetKey)
        {
            return;
        }

        var asset = relativePath is null
            ? null
            : randomizeStart
                ? GameAssetService.LoadAnimatedInstance(
                    relativePath,
                    TimeSpan.FromMilliseconds(Random.Shared.Next(maxInitialDelayMilliseconds + 1)))
                : GameAssetService.LoadAnimated(relativePath);

        if (isPiece)
        {
            _pieceAssetKey = key;
            PieceAsset = asset;
        }
        else
        {
            _tileAssetKey = key;
            TileAsset = asset;
        }
    }
}
