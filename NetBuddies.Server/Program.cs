using NetBuddies.Core;
using System.Security.Cryptography.X509Certificates;

var serverArgs = ServerArguments.Parse(args);
await using var server = new BuddyServer();
using var shutdown = new CancellationTokenSource();

Console.Title = "Net Buddies Server";
Console.WriteLine("Net Buddies standalone server");
Console.WriteLine($"Starting on port {serverArgs.Port}...");
Console.WriteLine(serverArgs.UseTls ? "TLS: enabled" : "TLS: disabled");
Console.WriteLine(string.IsNullOrWhiteSpace(serverArgs.InviteCode) ? "Invite code: not required" : "Invite code: required");
Console.WriteLine("Type /stop and press Enter, or press Ctrl+C, to stop hosting.");
Console.WriteLine();

server.StatusChanged += message => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    TryCancel(shutdown);
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => TryCancel(shutdown);

try
{
    await server.StartAsync(serverArgs.Port, serverArgs.ToOptions(), shutdown.Token);
    _ = ReadServerCommandsAsync(shutdown);
    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
    }
    catch (OperationCanceledException)
    {
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not start server: {ex.Message}");
    Environment.ExitCode = 1;
}
finally
{
    Console.WriteLine("Stopping server...");
    await server.StopAsync();
}

static async Task ReadServerCommandsAsync(CancellationTokenSource shutdown)
{
    while (!shutdown.IsCancellationRequested)
    {
        var line = await Console.In.ReadLineAsync();
        if (line is null)
        {
            return;
        }

        if (line.Trim().Equals("/stop", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Stop command received.");
            TryCancel(shutdown);
            return;
        }

        if (!string.IsNullOrWhiteSpace(line))
        {
            Console.WriteLine("Unknown command. Type /stop to stop the server.");
        }
    }
}

static void TryCancel(CancellationTokenSource shutdown)
{
    try
    {
        if (!shutdown.IsCancellationRequested)
        {
            shutdown.Cancel();
        }
    }
    catch (ObjectDisposedException)
    {
    }
}

internal sealed record ServerArguments(
    int Port,
    string CertificatePath,
    string CertificatePassword,
    string InviteCode)
{
    public bool UseTls => !string.IsNullOrWhiteSpace(CertificatePath);

    public BuddyServerOptions ToOptions()
    {
        return new BuddyServerOptions
        {
            Certificate = UseTls
                ? X509CertificateLoader.LoadPkcs12FromFile(CertificatePath, CertificatePassword)
                : null,
            InviteCode = InviteCode
        };
    }

    public static ServerArguments Parse(string[] args)
    {
        var port = 5050;
        var certificatePath = "";
        var certificatePassword = "";
        var inviteCode = "";

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (TryReadOption(args, ref index, "--port", "-p", out var portValue)
                && int.TryParse(portValue, out var optionPort))
            {
                port = ClampPort(optionPort);
                continue;
            }

            if (TryReadOption(args, ref index, "--cert", "-c", out var certValue))
            {
                certificatePath = certValue;
                continue;
            }

            if (TryReadOption(args, ref index, "--cert-password", "", out var certPasswordValue))
            {
                certificatePassword = certPasswordValue;
                continue;
            }

            if (TryReadOption(args, ref index, "--invite-code", "-i", out var inviteValue))
            {
                inviteCode = inviteValue;
                continue;
            }

            if (int.TryParse(arg, out var positionalPort))
            {
                port = ClampPort(positionalPort);
            }
        }

        return new ServerArguments(port, certificatePath, certificatePassword, inviteCode);
    }

    private static bool TryReadOption(
        string[] args,
        ref int index,
        string longName,
        string shortName,
        out string value)
    {
        var arg = args[index];
        value = "";

        if (arg.StartsWith($"{longName}=", StringComparison.OrdinalIgnoreCase))
        {
            value = arg[(longName.Length + 1)..];
            return true;
        }

        if (!string.IsNullOrWhiteSpace(shortName)
            && arg.StartsWith($"{shortName}=", StringComparison.OrdinalIgnoreCase))
        {
            value = arg[(shortName.Length + 1)..];
            return true;
        }

        if (arg.Equals(longName, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(shortName) && arg.Equals(shortName, StringComparison.OrdinalIgnoreCase)))
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"{longName} requires a value.");
            }

            index++;
            value = args[index];
            return true;
        }

        return false;
    }

    private static int ClampPort(int port)
    {
        return port is >= 1 and <= 65535
            ? port
            : throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");
    }
}
