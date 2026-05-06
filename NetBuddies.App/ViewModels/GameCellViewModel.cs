using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

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
    }
}
