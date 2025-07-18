namespace ShimmerInterface;

public class Program
{
    static ShimmerBiosignals shimmer;
    static CancellationTokenSource cts;

    public static async Task Main(string[] args)
    {
        cts = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) => {
            e.Cancel = true;
            cts.Cancel();
        };

        AppDomain.CurrentDomain.ProcessExit += (s,e) => {
            cts.Cancel();
        };

        shimmer = new ShimmerBiosignals();
        shimmer.StartReceivingData();
        Console.WriteLine("Biosensor started. Waiting for shutdown...");

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException) { }

        shimmer.OnApplicationQuit();
        Console.WriteLine("Shutdown complete.");
    }
}

