namespace ShimmerInterface;

public class Program
{
    static async Task Main(string[] args)
    {
        ShimmerBiosignals shimmer = new ShimmerBiosignals();
        await shimmer.StartReceivingData();
    }
}

