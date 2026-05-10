using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetBuddies.App.Services;
using NetBuddies.Core;

namespace NetBuddies.App.ViewModels;

public enum NetBuddiesGameType
{
    TicTacToe,
    Checkers,
    MinesweeperFlags,
    BuddyPong
}

public partial class GameSessionViewModel : ViewModelBase
{
    private const int ClassicBoardSize = 8;
    private const int MinesweeperBoardSize = 16;
    private const int MinesweeperMineCount = 48;

    private static readonly IBrush TicTacToeBoardBrush = new SolidColorBrush(Color.FromRgb(241, 248, 255));
    private static readonly IBrush TicTacToeEmptyBrush = new SolidColorBrush(Color.FromRgb(252, 254, 255));
    private static readonly IBrush TicTacToeXBrush = new SolidColorBrush(Color.FromRgb(25, 126, 214));
    private static readonly IBrush TicTacToeOBrush = new SolidColorBrush(Color.FromRgb(44, 160, 78));
    private static readonly IBrush CheckerLightSquare = new SolidColorBrush(Color.FromRgb(238, 246, 255));
    private static readonly IBrush CheckerDarkSquare = new SolidColorBrush(Color.FromRgb(88, 137, 180));
    private static readonly IBrush CheckerSelectedSquare = new SolidColorBrush(Color.FromRgb(255, 218, 77));
    private static readonly IBrush CheckerLocalPiece = new SolidColorBrush(Color.FromRgb(196, 42, 45));
    private static readonly IBrush CheckerLocalStroke = new SolidColorBrush(Color.FromRgb(118, 24, 29));
    private static readonly IBrush CheckerRemotePiece = new SolidColorBrush(Color.FromRgb(30, 36, 48));
    private static readonly IBrush CheckerRemoteStroke = new SolidColorBrush(Color.FromRgb(9, 15, 25));
    private static readonly IBrush MineCoveredBrush = new SolidColorBrush(Color.FromRgb(175, 211, 244));
    private static readonly IBrush MineCoveredAltBrush = new SolidColorBrush(Color.FromRgb(156, 197, 235));
    private static readonly IBrush MineOpenBrush = new SolidColorBrush(Color.FromRgb(239, 246, 252));
    private static readonly IBrush MineFlagBrush = new SolidColorBrush(Color.FromRgb(216, 54, 61));

    private readonly BuddyClient _client;
    private readonly int[] _ticTacToe = new int[9];
    private readonly int[] _checkers = new int[64];
    private readonly bool[] _mines;
    private readonly bool[] _revealed;
    private int _selectedChecker = -1;
    private int _forcedChecker = -1;
    private int _movesPlayed;
    private int _myFlags;
    private int _buddyFlags;
    private bool _isGameOver;
    private bool _hasEndGameBeenSent;
    private HashSet<int> _winningCells = [];

    public GameSessionViewModel(
        BuddyClient client,
        string gameId,
        NetBuddiesGameType gameType,
        string buddyName,
        bool isHost)
    {
        _client = client;
        GameId = gameId;
        GameType = gameType;
        BuddyName = buddyName;
        IsHost = isHost;
        Cells = [];

        var cellCount = gameType switch
        {
            NetBuddiesGameType.TicTacToe => 9,
            NetBuddiesGameType.MinesweeperFlags => MinesweeperBoardSize * MinesweeperBoardSize,
            _ => ClassicBoardSize * ClassicBoardSize
        };
        _mines = gameType == NetBuddiesGameType.MinesweeperFlags
            ? new bool[cellCount]
            : new bool[ClassicBoardSize * ClassicBoardSize];
        _revealed = new bool[_mines.Length];

        for (var index = 0; index < cellCount; index++)
        {
            Cells.Add(new GameCellViewModel(index));
        }

        ResetLocalState();
    }

    public string GameId { get; }
    public NetBuddiesGameType GameType { get; }
    public string BuddyName { get; }
    public bool IsHost { get; }
    public string Title => $"{GameName} with {BuddyName}";
    public string GameName => GameType switch
    {
        NetBuddiesGameType.TicTacToe => "Tic Tac Toe",
        NetBuddiesGameType.Checkers => "Leapfrog Checkers",
        _ => "Minesweeper Flags"
    };
    public int BoardSize => GameType switch
    {
        NetBuddiesGameType.TicTacToe => 3,
        NetBuddiesGameType.MinesweeperFlags => MinesweeperBoardSize,
        _ => ClassicBoardSize
    };

    public ObservableCollection<GameCellViewModel> Cells { get; }
    public event Action<GameSessionViewModel>? CloseRequested;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isMyTurn;

    [ObservableProperty]
    private string _scoreText = "";

    public async Task SendInviteAsync()
    {
        var payload = "";
        if (GameType == NetBuddiesGameType.MinesweeperFlags)
        {
            GenerateMines();
            payload = JsonSerializer.Serialize(_mines);
        }

        await _client.SendGameAsync(BuddyName, GameId, GameType.ToString(), "Invite", payload);
        StatusText = $"Invited {BuddyName} to {GameName}.";
        RefreshBoard();
    }

    public async Task AcceptInviteAsync(string payload)
    {
        if (GameType == NetBuddiesGameType.MinesweeperFlags && !string.IsNullOrWhiteSpace(payload))
        {
            var mines = JsonSerializer.Deserialize<bool[]>(payload) ?? [];
            Array.Copy(mines, _mines, Math.Min(mines.Length, _mines.Length));
        }

        await _client.SendGameAsync(BuddyName, GameId, GameType.ToString(), "Accept");
        StatusText = $"{BuddyName} invited you to {GameName}.";
        RefreshBoard();
    }

    public void HandlePacket(NetBuddiesPacket packet)
    {
        if (packet.GameAction == "End")
        {
            EndGameLocally($"{BuddyName} ended the game.");
            return;
        }

        if (packet.GameAction == "Accept")
        {
            StatusText = $"{BuddyName} accepted. {TurnText()}";
            RefreshBoard();
            return;
        }

        if (packet.GameAction == "Decline")
        {
            IsMyTurn = false;
            StatusText = $"{BuddyName} declined the game request.";
            RefreshBoard();
            return;
        }

        if (packet.GameAction != "Move")
        {
            return;
        }

        var move = JsonSerializer.Deserialize<GameMove>(packet.Text);
        if (move is null)
        {
            return;
        }

        ApplyMove(move, fromRemote: true);
    }

    [RelayCommand]
    private async Task SelectCellAsync(GameCellViewModel? cell)
    {
        if (cell is null || !IsMyTurn)
        {
            return;
        }

        var move = CreateMove(cell.Index);
        if (move is null)
        {
            return;
        }

        ApplyMove(move, fromRemote: false);
        await _client.SendGameAsync(
            BuddyName,
            GameId,
            GameType.ToString(),
            "Move",
            JsonSerializer.Serialize(move));
    }

    [RelayCommand]
    private async Task EndGameAsync()
    {
        if (!_hasEndGameBeenSent)
        {
            _hasEndGameBeenSent = true;
            await _client.SendGameAsync(BuddyName, GameId, GameType.ToString(), "End");
        }

        EndGameLocally("You ended the game.");
    }

    private GameMove? CreateMove(int index)
    {
        return GameType switch
        {
            NetBuddiesGameType.TicTacToe => CreateTicTacToeMove(index),
            NetBuddiesGameType.Checkers => CreateCheckersMove(index),
            NetBuddiesGameType.MinesweeperFlags => CreateMinesweeperMove(index),
            _ => null
        };
    }

    private GameMove? CreateTicTacToeMove(int index)
    {
        if (index < 0 || index >= 9 || _ticTacToe[index] != 0)
        {
            return null;
        }

        return new GameMove { To = index };
    }

    private GameMove? CreateCheckersMove(int index)
    {
        if (index < 0 || index >= _checkers.Length)
        {
            return null;
        }

        if (_forcedChecker >= 0 && _selectedChecker < 0)
        {
            _selectedChecker = _forcedChecker;
        }

        if (_selectedChecker < 0)
        {
            if (CanSelectChecker(index))
            {
                _selectedChecker = index;
                RefreshBoard();
            }

            return null;
        }

        if (index == _selectedChecker)
        {
            _selectedChecker = -1;
            RefreshBoard();
            return null;
        }

        if (CanSelectChecker(index))
        {
            if (_forcedChecker >= 0 && index != _forcedChecker)
            {
                StatusText = "Keep jumping with the selected piece.";
                return null;
            }

            _selectedChecker = index;
            RefreshBoard();
            return null;
        }

        if (!IsLegalCheckerMove(_selectedChecker, index, LocalPlayer, out _))
        {
            StatusText = GetInvalidCheckerMoveText(_selectedChecker);
            return null;
        }

        var move = new GameMove { From = _selectedChecker, To = index };
        return move;
    }

    private GameMove? CreateMinesweeperMove(int index)
    {
        if (index < 0 || index >= _revealed.Length || _revealed[index])
        {
            return null;
        }

        return new GameMove { To = index };
    }

    private void ApplyMove(GameMove move, bool fromRemote)
    {
        switch (GameType)
        {
            case NetBuddiesGameType.TicTacToe:
                ApplyTicTacToeMove(move, fromRemote);
                break;
            case NetBuddiesGameType.Checkers:
                ApplyCheckersMove(move, fromRemote);
                break;
            case NetBuddiesGameType.MinesweeperFlags:
                ApplyMinesweeperMove(move, fromRemote);
                break;
        }

        RefreshBoard();
    }

    private void EndGameLocally(string message)
    {
        _isGameOver = true;
        IsMyTurn = false;
        StatusText = message;
        RefreshBoard();
        CloseRequested?.Invoke(this);
    }

    private void ApplyTicTacToeMove(GameMove move, bool fromRemote)
    {
        var player = fromRemote ? RemotePlayer : LocalPlayer;
        if (move.To < 0 || move.To >= 9 || _ticTacToe[move.To] != 0)
        {
            return;
        }

        _ticTacToe[move.To] = player;
        _movesPlayed++;

        var winner = GetTicTacToeWinner();
        if (winner != 0)
        {
            IsMyTurn = false;
            _isGameOver = true;
            StatusText = winner == LocalPlayer ? "You won!" : $"{BuddyName} won.";
            return;
        }

        if (_movesPlayed >= 9)
        {
            IsMyTurn = false;
            _isGameOver = true;
            StatusText = "Draw game.";
            return;
        }

        IsMyTurn = fromRemote;
        StatusText = TurnText();
    }

    private void ApplyCheckersMove(GameMove move, bool fromRemote)
    {
        if (move.From < 0 || move.From >= 64 || move.To < 0 || move.To >= 64)
        {
            return;
        }

        var player = fromRemote ? RemotePlayer : LocalPlayer;
        if (!PlayerOwnsChecker(move.From, player)
            || !IsLegalCheckerMove(move.From, move.To, player, out var isCapture))
        {
            return;
        }

        var piece = _checkers[move.From];
        _checkers[move.From] = 0;
        var crownedPiece = CrownIfNeeded(piece, move.To);
        var wasCrowned = crownedPiece != piece;
        _checkers[move.To] = crownedPiece;

        if (isCapture)
        {
            var middle = ((Row(move.From) + Row(move.To)) / 2 * BoardSize) + ((Column(move.From) + Column(move.To)) / 2);
            _checkers[middle] = 0;
        }

        var opponent = player == 1 ? 2 : 1;
        if (CountPiecesForPlayer(opponent) == 0)
        {
            FinishCheckers(player);
            return;
        }

        if (isCapture && !wasCrowned && HasCaptureFrom(move.To, player))
        {
            _forcedChecker = move.To;
            _selectedChecker = fromRemote ? -1 : move.To;
            IsMyTurn = !fromRemote;
            StatusText = fromRemote
                ? $"{BuddyName} must keep jumping."
                : "Keep jumping with the same piece.";
            return;
        }

        _forcedChecker = -1;
        _selectedChecker = -1;
        if (!HasAnyLegalMove(opponent))
        {
            FinishCheckers(player);
            return;
        }

        IsMyTurn = fromRemote;
        StatusText = TurnText();
    }

    private void ApplyMinesweeperMove(GameMove move, bool fromRemote)
    {
        if (move.To < 0 || move.To >= _revealed.Length || _revealed[move.To])
        {
            return;
        }

        _revealed[move.To] = true;
        if (_mines[move.To])
        {
            if (fromRemote)
            {
                _buddyFlags++;
            }
            else
            {
                _myFlags++;
            }
        }
        else
        {
            RevealEmptyArea(move.To);
        }

        var found = _myFlags + _buddyFlags;
        if (found >= _mines.Count(value => value))
        {
            IsMyTurn = false;
            _isGameOver = true;
            StatusText = _myFlags == _buddyFlags
                ? $"All flags found. Draw {_myFlags}-{_buddyFlags}."
                : _myFlags > _buddyFlags
                    ? $"You win {_myFlags}-{_buddyFlags}!"
                    : $"{BuddyName} wins {_buddyFlags}-{_myFlags}.";
            return;
        }

        IsMyTurn = fromRemote;
        StatusText = $"Flags: You {_myFlags}, {BuddyName} {_buddyFlags}. {TurnText()}";
    }

    private bool OwnsChecker(int index)
    {
        var piece = _checkers[index];
        if (piece == 0)
        {
            return false;
        }

        return IsHost
            ? piece is 1 or 3
            : piece is 2 or 4;
    }

    private bool CanSelectChecker(int index)
    {
        if (!OwnsChecker(index))
        {
            return false;
        }

        if (_forcedChecker >= 0)
        {
            return index == _forcedChecker;
        }

        return !HasAnyCapture(LocalPlayer) || HasCaptureFrom(index, LocalPlayer);
    }

    private string GetInvalidCheckerMoveText(int from)
    {
        if (_forcedChecker >= 0)
        {
            return "You must keep jumping with the same piece.";
        }

        if (HasAnyCapture(LocalPlayer) && !HasCaptureFrom(from, LocalPlayer))
        {
            return "A jump is available. Choose a piece that can capture.";
        }

        if (HasAnyCapture(LocalPlayer))
        {
            return "A jump is required.";
        }

        return TurnText();
    }

    private bool IsLegalCheckerMove(int from, int to, int player, out bool isCapture)
    {
        isCapture = false;
        if (from < 0 || from >= _checkers.Length || to < 0 || to >= _checkers.Length)
        {
            return false;
        }

        if (_forcedChecker >= 0 && from != _forcedChecker)
        {
            return false;
        }

        if (_checkers[to] != 0 || !PlayerOwnsChecker(from, player))
        {
            return false;
        }

        var piece = _checkers[from];
        var rowDelta = Row(to) - Row(from);
        var colDelta = Math.Abs(Column(to) - Column(from));
        var direction = piece is 1 or 3 ? -1 : 1;
        var isKing = piece is 3 or 4;

        if (colDelta == 1 && (rowDelta == direction || isKing && Math.Abs(rowDelta) == 1))
        {
            return _forcedChecker < 0 && !HasAnyCapture(player);
        }

        if (colDelta == 2 && (rowDelta == direction * 2 || isKing && Math.Abs(rowDelta) == 2))
        {
            var middle = ((Row(from) + Row(to)) / 2 * BoardSize) + ((Column(from) + Column(to)) / 2);
            isCapture = _checkers[middle] != 0 && PlayerOwnsChecker(middle, player == 1 ? 2 : 1);
            return isCapture;
        }

        return false;
    }

    private bool HasAnyCapture(int player)
    {
        for (var index = 0; index < _checkers.Length; index++)
        {
            if (HasCaptureFrom(index, player))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasCaptureFrom(int from, int player)
    {
        if (!PlayerOwnsChecker(from, player))
        {
            return false;
        }

        foreach (var (rowOffset, colOffset) in CheckerDirections(_checkers[from]))
        {
            var middleRow = Row(from) + rowOffset;
            var middleColumn = Column(from) + colOffset;
            var targetRow = Row(from) + rowOffset * 2;
            var targetColumn = Column(from) + colOffset * 2;
            if (!IsInsideClassicBoard(middleRow, middleColumn) || !IsInsideClassicBoard(targetRow, targetColumn))
            {
                continue;
            }

            var middle = middleRow * ClassicBoardSize + middleColumn;
            var target = targetRow * ClassicBoardSize + targetColumn;
            if (_checkers[target] == 0 && PlayerOwnsChecker(middle, player == 1 ? 2 : 1))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAnyLegalMove(int player)
    {
        if (HasAnyCapture(player))
        {
            return true;
        }

        for (var index = 0; index < _checkers.Length; index++)
        {
            if (!PlayerOwnsChecker(index, player))
            {
                continue;
            }

            foreach (var (rowOffset, colOffset) in CheckerDirections(_checkers[index]))
            {
                var targetRow = Row(index) + rowOffset;
                var targetColumn = Column(index) + colOffset;
                if (!IsInsideClassicBoard(targetRow, targetColumn))
                {
                    continue;
                }

                var target = targetRow * ClassicBoardSize + targetColumn;
                if (_checkers[target] == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private IEnumerable<(int RowOffset, int ColumnOffset)> CheckerDirections(int piece)
    {
        if (piece is 1 or 3 or 4)
        {
            yield return (-1, -1);
            yield return (-1, 1);
        }

        if (piece is 2 or 3 or 4)
        {
            yield return (1, -1);
            yield return (1, 1);
        }
    }

    private bool PlayerOwnsChecker(int index, int player)
    {
        if (index < 0 || index >= _checkers.Length)
        {
            return false;
        }

        return player == 1
            ? _checkers[index] is 1 or 3
            : _checkers[index] is 2 or 4;
    }

    private static bool IsInsideClassicBoard(int row, int column)
    {
        return row >= 0 && row < ClassicBoardSize && column >= 0 && column < ClassicBoardSize;
    }

    private void FinishCheckers(int winner)
    {
        IsMyTurn = false;
        _isGameOver = true;
        _forcedChecker = -1;
        _selectedChecker = -1;
        StatusText = winner == LocalPlayer ? "You won!" : $"{BuddyName} won.";
    }

    private int CrownIfNeeded(int piece, int index)
    {
        if (piece == 1 && Row(index) == 0)
        {
            return 3;
        }

        if (piece == 2 && Row(index) == ClassicBoardSize - 1)
        {
            return 4;
        }

        return piece;
    }

    private int GetTicTacToeWinner()
    {
        int[][] lines =
        [
            [0, 1, 2], [3, 4, 5], [6, 7, 8],
            [0, 3, 6], [1, 4, 7], [2, 5, 8],
            [0, 4, 8], [2, 4, 6]
        ];

        foreach (var line in lines)
        {
            var value = _ticTacToe[line[0]];
            if (value != 0 && value == _ticTacToe[line[1]] && value == _ticTacToe[line[2]])
            {
                _winningCells = line.ToHashSet();
                return value;
            }
        }

        return 0;
    }

    private void ResetLocalState()
    {
        IsMyTurn = IsHost;

        if (GameType == NetBuddiesGameType.Checkers)
        {
            for (var index = 0; index < _checkers.Length; index++)
            {
                var row = Row(index);
                var column = Column(index);
                if ((row + column) % 2 == 0)
                {
                    continue;
                }

                if (row <= 2)
                {
                    _checkers[index] = 2;
                }
                else if (row >= 5)
                {
                    _checkers[index] = 1;
                }
            }
        }

        if (GameType == NetBuddiesGameType.MinesweeperFlags && IsHost)
        {
            GenerateMines();
        }

        StatusText = IsHost ? $"Your turn. Playing {BuddyName}." : $"Waiting for {BuddyName}.";
        UpdateScoreText();
        RefreshBoard();
    }

    private void GenerateMines()
    {
        Array.Clear(_mines);
        var placed = 0;
        while (placed < MinesweeperMineCount)
        {
            var index = Random.Shared.Next(_mines.Length);
            if (_mines[index])
            {
                continue;
            }

            _mines[index] = true;
            placed++;
        }
    }

    private void RefreshBoard()
    {
        for (var index = 0; index < Cells.Count; index++)
        {
            var cell = Cells[index];
            cell.ClearVisuals();
            cell.IsEnabled = !_isGameOver;

            switch (GameType)
            {
                case NetBuddiesGameType.TicTacToe:
                    cell.Background = _winningCells.Contains(index)
                        ? new SolidColorBrush(Color.FromRgb(255, 234, 118))
                        : _ticTacToe[index] == 0 ? TicTacToeEmptyBrush : TicTacToeBoardBrush;
                    cell.SetTileAsset(_winningCells.Contains(index)
                        ? "TicTacToe/tile-winning.png"
                        : _ticTacToe[index] == 0
                            ? "TicTacToe/tile-empty.png"
                            : "TicTacToe/tile-played.png");
                    cell.AccentBrush = _ticTacToe[index] == 1 ? TicTacToeXBrush : TicTacToeOBrush;
                    cell.ShowImage = _ticTacToe[index] != 0;
                    cell.SetPieceAsset(_ticTacToe[index] == 1
                        ? "TicTacToe/x-piece.png"
                        : _ticTacToe[index] == 2
                            ? "TicTacToe/o-piece.png"
                            : null);
                    break;
                case NetBuddiesGameType.Checkers:
                    cell.Background = (Row(index) + Column(index)) % 2 == 0
                        ? CheckerLightSquare
                        : CheckerDarkSquare;
                    cell.SetTileAsset((Row(index) + Column(index)) % 2 == 0
                        ? "Checkers/tile-light.png"
                        : "Checkers/tile-dark.png");
                    if (index == _selectedChecker)
                    {
                        cell.Background = CheckerSelectedSquare;
                        cell.SetTileAsset("Checkers/tile-selected.png");
                    }

                    cell.ShowImage = _checkers[index] != 0;
                    cell.ShowKing = _checkers[index] is 3 or 4;
                    var checkerAsset = _checkers[index] switch
                    {
                        1 => "Checkers/checker-red.png",
                        2 => "Checkers/checker-black.png",
                        3 => "Checkers/checker-red-king.png",
                        4 => "Checkers/checker-black-king.png",
                        _ => null
                    };
                    cell.SetPieceAsset(checkerAsset, randomizeStart: checkerAsset is not null, maxInitialDelayMilliseconds: 3000);
                    cell.PieceFill = _checkers[index] is 1 or 3 ? CheckerLocalPiece : CheckerRemotePiece;
                    cell.PieceStroke = _checkers[index] is 1 or 3 ? CheckerLocalStroke : CheckerRemoteStroke;
                    cell.AccentBrush = OwnsChecker(index)
                        ? new SolidColorBrush(Color.FromRgb(255, 240, 135))
                        : new SolidColorBrush(Color.FromRgb(198, 221, 244));
                    break;
                case NetBuddiesGameType.MinesweeperFlags:
                    cell.Background = _revealed[index]
                        ? MineOpenBrush
                        : (Row(index) + Column(index)) % 2 == 0 ? MineCoveredBrush : MineCoveredAltBrush;
                    cell.SetTileAsset(_revealed[index]
                        ? "MinesweeperFlags/tile-open.png"
                        : (Row(index) + Column(index)) % 2 == 0
                            ? "MinesweeperFlags/tile-covered.png"
                            : "MinesweeperFlags/tile-covered-alt.png");
                    cell.ShowImage = _revealed[index] && _mines[index];
                    cell.SetPieceAsset(cell.ShowImage ? "MinesweeperFlags/flag.png" : null);
                    cell.AccentBrush = MineFlagBrush;
                    var adjacentMines = CountAdjacentMines(index);
                    cell.ShowNumber = _revealed[index] && !_mines[index] && adjacentMines > 0;
                    cell.NumberText = adjacentMines.ToString();
                    cell.NumberBrush = adjacentMines switch
                    {
                        1 => new SolidColorBrush(Color.FromRgb(25, 93, 191)),
                        2 => new SolidColorBrush(Color.FromRgb(33, 135, 63)),
                        3 => new SolidColorBrush(Color.FromRgb(193, 45, 50)),
                        4 => new SolidColorBrush(Color.FromRgb(87, 55, 158)),
                        _ => new SolidColorBrush(Color.FromRgb(31, 42, 57))
                    };
                    break;
            }
        }

        UpdateScoreText();
    }

    private void RevealEmptyArea(int startIndex)
    {
        if (CountAdjacentMines(startIndex) != 0)
        {
            return;
        }

        var queue = new Queue<int>();
        queue.Enqueue(startIndex);

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            foreach (var neighbor in GetNeighbors(index))
            {
                if (_revealed[neighbor] || _mines[neighbor])
                {
                    continue;
                }

                _revealed[neighbor] = true;
                if (CountAdjacentMines(neighbor) == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    private IEnumerable<int> GetNeighbors(int index)
    {
        for (var row = Row(index) - 1; row <= Row(index) + 1; row++)
        {
            for (var col = Column(index) - 1; col <= Column(index) + 1; col++)
            {
                if (row < 0 || row >= BoardSize || col < 0 || col >= BoardSize || row == Row(index) && col == Column(index))
                {
                    continue;
                }

                yield return row * BoardSize + col;
            }
        }
    }

    private void UpdateScoreText()
    {
        ScoreText = GameType switch
        {
            NetBuddiesGameType.TicTacToe => $"You are {(LocalPlayer == 1 ? "X" : "O")} | {BuddyName} is {(RemotePlayer == 1 ? "X" : "O")}",
            NetBuddiesGameType.MinesweeperFlags => $"Flags found: You {_myFlags}, {BuddyName} {_buddyFlags} | Remaining {_mines.Count(value => value) - _myFlags - _buddyFlags}",
            NetBuddiesGameType.Checkers => $"Pieces: You {CountOwnPieces()}, {BuddyName} {CountRemotePieces()}",
            _ => ""
        };
    }

    private int CountOwnPieces()
    {
        var count = 0;
        for (var index = 0; index < _checkers.Length; index++)
        {
            if (OwnsChecker(index))
            {
                count++;
            }
        }

        return count;
    }

    private int CountRemotePieces()
    {
        var count = 0;
        for (var index = 0; index < _checkers.Length; index++)
        {
            if (_checkers[index] != 0 && !OwnsChecker(index))
            {
                count++;
            }
        }

        return count;
    }

    private int CountPiecesForPlayer(int player)
    {
        var count = 0;
        for (var index = 0; index < _checkers.Length; index++)
        {
            if (PlayerOwnsChecker(index, player))
            {
                count++;
            }
        }

        return count;
    }

    private string TurnText()
    {
        if (GameType == NetBuddiesGameType.Checkers)
        {
            if (IsMyTurn && HasAnyCapture(LocalPlayer))
            {
                return "Your turn. A jump is required.";
            }

            if (!IsMyTurn && HasAnyCapture(RemotePlayer))
            {
                return $"{BuddyName}'s turn. A jump is required.";
            }
        }

        return IsMyTurn ? "Your turn." : $"{BuddyName}'s turn.";
    }
    private int LocalPlayer => IsHost ? 1 : 2;
    private int RemotePlayer => IsHost ? 2 : 1;
    private int Row(int index) => index / BoardSize;
    private int Column(int index) => index % BoardSize;

    private int CountAdjacentMines(int index)
    {
        var count = 0;
        for (var row = Row(index) - 1; row <= Row(index) + 1; row++)
        {
            for (var col = Column(index) - 1; col <= Column(index) + 1; col++)
            {
                if (row < 0 || row >= BoardSize || col < 0 || col >= BoardSize || row == Row(index) && col == Column(index))
                {
                    continue;
                }

                if (_mines[row * BoardSize + col])
                {
                    count++;
                }
            }
        }

        return count;
    }

    private sealed record GameMove
    {
        public int From { get; init; } = -1;
        public int To { get; init; }
    }
}
