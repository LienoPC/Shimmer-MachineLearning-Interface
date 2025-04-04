namespace ShimmerInterface;

public class Program
{
    static async Task Main(string[] args)
    {
        CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        ShimmerBiosignals shimmer = new ShimmerBiosignals();
        shimmer.StartReceivingData();
        Console.WriteLine("Press a key to exit");
        ConsoleKeyInfo input = Console.ReadKey();
        shimmer.OnApplicationQuit();
    }
}

