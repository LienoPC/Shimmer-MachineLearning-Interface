namespace ShimmerInterface;
using MessagePack;

// Class for data serialization. It models the data that is sent to the python script
// that implements the neural network
[MessagePackObject]
public class BiosignalsData
{
    [Key(0)] public int HeartRate { get; set; }
    [Key(1)] public double Gsr { get; set; }
    [Key(2)] public double Ppg { get; set; }

    public BiosignalsData(int heartRate, double gsr, double ppg)
    {
        HeartRate = heartRate;
        Gsr = gsr;
        Ppg = ppg;
    }

}