using ShimmerAPI;
using ShimmerLibrary;
using MessagePack;
using System.Text;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.WebSockets;
using System.Text.Json;
using Zeroconf;

namespace ShimmerInterface;


public class ShimmerBiosignals
{
    // Low Pass Filter for PPG signal
    Filter _filterLPF_PPG;
    // High Pass Filter for PPG signal
    Filter _filterHPF_PPG; 
    // Algorithm that calculates the Heart Rate from PPG
    private PPGtoHRAlgorithm _ppGtoHeartRateCalculation; 
    int _numberOfHeartBeatsToAverage = 1;
    int _trainingPeriodPPG = 10; //10 second buffer
    double LPF_CORNER_FREQ_HZ = 5;
    double HPF_CORNER_FREQ_HZ = 0.5;
    ShimmerLogAndStreamSystemSerialPort _shimmer;
    double _samplingRate = 128;
    int _count = 0;
    bool _firstTime = true;
    public int HeartRate;
    //public string dataGSR;
    float _levelTime;
    //The index of the signals originating from ShimmerBluetooth
    int _indexGSR;
    int _indexPPG;
    Double _scl;
    int _indexTimeStamp;
    int _dataGSR;
    public double Gsr;
    public double Ppg;
    public double Ts;
    public int Hr;
    public string GsrTs;
    public string PpgTs;
    public string HrTs;
    public List<double> GsrList = new List<double>();
    public List<double> PpgList = new List<double>();
    public List<double> TsList = new List<double>();
    public List<int> HrList = new List<int>();
    public List<string> GsrListTs = new List<string>();
    public List<string> PpgListTs = new List<string>();
    public List<string> HrListTs = new List<string>();
    int _stage;
    private float _time1;
    private float _timer;
    private float _lookDownTime;
    private float _yLoc;
    private float _camDir;
    
    private Queue<BiosignalsData> _streamQueue = new Queue<BiosignalsData>();
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

    private NamedPipeServerStream _pipeServer = new NamedPipeServerStream("biosignals", PipeDirection.Out);
    
    private ClientWebSocket _clientWebSocket;

    private string _streamId = "ss"; 
    public ShimmerBiosignals()
    {
        _ppGtoHeartRateCalculation = new PPGtoHRAlgorithm(_samplingRate, _numberOfHeartBeatsToAverage, _trainingPeriodPPG);
        _filterLPF_PPG = new Filter(Filter.LOW_PASS, _samplingRate, new double[]
            { LPF_CORNER_FREQ_HZ });
        _filterHPF_PPG = new Filter(Filter.HIGH_PASS, _samplingRate, new double[]
            { HPF_CORNER_FREQ_HZ });
        int enabledSensors = ((int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_GSR
                              | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A13);
        
        _shimmer = new ShimmerLogAndStreamSystemSerialPort("Shimmer3-D38B ’RNI-SPP’",
            "COM4", _samplingRate, 0, ShimmerBluetooth.GSR_RANGE_AUTO, enabledSensors,
            false, false, false, 1, 0, Shimmer3Configuration.EXG_EMG_CONFIGURATION_CHIP1,
            Shimmer3Configuration.EXG_EMG_CONFIGURATION_CHIP2, true);
        _clientWebSocket = new ClientWebSocket();
    }
    
    // Start the data transmission between the shimmer and the AI model
    public async Task StartReceivingData()
    {
        _shimmer.UICallback += this.StreamCallback;
        await ConnectToWebService();
        // Asynchronously run the streamqueue process
        Task.Run(SocketProcessQueueAsync);
        //_shimmer.Connect();
        await MockStreaming();

    }
    
    // Function that actively search for an open WebSocket in the local network
    // and returns a list of service informations. It then selects the one with the specified Id
    public async Task<string> DiscoverAndGetServiceUri(string streamId)
    {
        // Query for services with the type _ws._tcp.local.
        var responses = await ZeroconfResolver.ResolveAsync("_ws._tcp.local.");
        
        foreach (var resp in responses)
        {
            Console.WriteLine($"Service found: {resp.DisplayName} at {resp.IPAddress}");
            
            // Check if the service has properties and if the "stream" property matches what we need
            if (resp.Services.Values.FirstOrDefault()?.Properties != null)
            {
                var properties = resp.Services.Values.First().Properties[0];
                if (properties.TryGetValue("stream", out var streamObj))
                {
                    string streamValue = streamObj.ToString();
                    if (streamValue.Equals(streamId, StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract the port and path from the advertisement. Then it can be used to connect and stream data trough websocket
                        int port = resp.Services.Values.First().Port;
                        properties.TryGetValue("path", out var pathObj);
                        string path = pathObj?.ToString() ?? "/ws";
                        
                        string uri = $"ws://{resp.IPAddress}:{port}{path}";
                        Console.WriteLine($"Selected URI for stream {streamId}: {uri}");
                        
                        
                        return uri;
                    }
                }
            }
        }
        
        Console.WriteLine($"No service found for stream {streamId}");
        return "";
    }

    // Startup for the websocket discovery and connection
    public async Task ConnectToWebService()
    {
        string foundUri = await DiscoverAndGetServiceUri(_streamId);
        if (foundUri != "")
        {
            await _clientWebSocket.ConnectAsync(new Uri(foundUri), CancellationToken.None);
            Console.WriteLine($"Connected to {foundUri}");
        }
    }
  
    // Start the data streaming through the named pipe
    public async Task PipeStartStreamingData()
    {
        // Create streaming named pipe to communicate with AI script
        Console.WriteLine("Waiting for connection...");
        await _pipeServer.WaitForConnectionAsync();
        Console.WriteLine("Client connected.");
        
    }
    
    public void StreamCallback(object? sender, EventArgs args)
    {
        DateTime now = DateTime.Now;
        CustomEventArgs eventArgs = (CustomEventArgs)args;
        int indicator = eventArgs.getIndicator();
        switch (indicator)
        {
            case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_STATE_CHANGE:
                Console.Write(((ShimmerBluetooth)sender).GetDeviceName()+
                                               " State= "+ ((ShimmerBluetooth)sender).GetStateString()+
                                               System.Environment.NewLine);
                //Console.WriteLine("State: ");
                int state = (int)eventArgs.getObject();
                if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
                {
                    Debug.WriteLine("Shimmer is Connected");
                }
                else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTING)
                {
                    Debug.WriteLine("Establishing Connection to Shimmer Device");
                }
                else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_NONE)
                {
                    Console.WriteLine("Shimmer is Disconnected");
                }
                else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_STREAMING)
                {
                    Debug.WriteLine("Shimmer is Streaming");
                }
                break;
            case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE:
                /*Console.Write(((ShimmerBluetooth)sender).GetDeviceName()+
                              " State= "+ ((ShimmerBluetooth)sender).GetStateString()+
                              System.Environment.NewLine);
                              */
                break;
            case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET:
                /*Console.Write(((ShimmerBluetooth)sender).GetDeviceName()+
                              " State= "+ ((ShimmerBluetooth)sender).GetStateString()+
                              System.Environment.NewLine);
                              */
                // Receiving data and filtering it
                ObjectCluster objectCluster = (ObjectCluster)eventArgs.getObject();
                _indexGSR =
                    objectCluster.GetIndex(Shimmer3Configuration.SignalNames.GSR,
                        ShimmerConfiguration.SignalFormats.CAL);
                _indexPPG =
                    objectCluster.GetIndex(Shimmer3Configuration.SignalNames.
                            INTERNAL_ADC_A13,
                        ShimmerConfiguration.SignalFormats.CAL);
                _indexTimeStamp =
                    objectCluster.GetIndex(ShimmerConfiguration.SignalNames.
                            SYSTEM_TIMESTAMP,
                        ShimmerConfiguration.SignalFormats.CAL);
                _firstTime = false;
                SensorData dataGSR = objectCluster.GetData(_indexGSR);
                SensorData dataPPG = objectCluster.GetData(_indexPPG);
                SensorData dataTS = objectCluster.GetData(_indexTimeStamp);
                //Process PPG signal and calculate heart rate
                double dataFilteredLP = _filterLPF_PPG.filterData(dataPPG.Data);
                double dataFilteredHP = _filterHPF_PPG.filterData(dataFilteredLP);
                HeartRate=(int)_ppGtoHeartRateCalculation.ppgToHrConversion(dataFilteredHP, dataTS.Data);
                
                // Storing Heart Rate and timestamp
                if (HeartRate > 2)
                {
                    Hr = (int)HeartRate;
                    HrList.Add(Hr);
                    HrTs = now.ToString("yyyyMMddHHmmssfff");
                    HrListTs.Add(HrTs);
                }
                
                // Storing measured GSR and timestamp
                Gsr = (double)dataGSR.Data;
                GsrList.Add(Gsr);
                GsrTs = now.ToString("yyyyMMddHHmmssfff");
                GsrListTs.Add(HrTs);
                
                // Storing measured PPG and timestamp
                Ppg = (double)dataPPG.Data;
                PpgList.Add(Ppg);
                PpgTs = now.ToString("yyyyMMddHHmmssfff");
                PpgListTs.Add(HrTs);
                Ts = (double)dataTS.Data;
                TsList.Add(Ts);
                if (_count % _samplingRate == 0) //only display data every second
                {
                    _scl = 1000 / dataGSR.Data;
                }
                _count++;
                
                // After filtering, pass it to the streaming pipe
                _streamQueue.Enqueue(CreateBioSignalsObject(Ppg, Gsr, HeartRate));
                break;
        }
        _shimmer.StartStreaming();
    }

    private BiosignalsData CreateBioSignalsObject(double ppg, double gsr, int heartRate)
    {
        BiosignalsData data = new BiosignalsData(heartRate, gsr, ppg);
        return data;
    }
    
    private async Task PipeProcessQueueAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            if (_streamQueue.TryDequeue(out BiosignalsData data))
            {
                await PipeStreamReceivedData(data);
            }

            await Task.Delay((int)(1000 / _samplingRate));
        }
    }
    
    
    
    // Prepare data packet to be sent to the model
    private async Task PipeStreamReceivedData(BiosignalsData dataToSend)
    {
        if (_pipeServer.IsConnected)
        {
            byte[] serializedData = MessagePackSerializer.Serialize(dataToSend);
            byte[] lengthPrefix = BitConverter.GetBytes(serializedData.Length);
            await _pipeServer.WriteAsync(lengthPrefix, 0, lengthPrefix.Length);
            await _pipeServer.WriteAsync(serializedData, 0, serializedData.Length);
            await _pipeServer.FlushAsync();
        }
        else
        {
            throw new Exception("Pipe server not connected");
        }
            

    }
    
    // Processes data queue for WebSocket streaming
    private async Task SocketProcessQueueAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            if (_streamQueue.TryDequeue(out BiosignalsData data))
            {
                await SocketStreamReceivedData(data);
            }

            await Task.Delay((int)(1000 / _samplingRate));
        }
    }
    
    // Prepare data packet to be sent to the model
    private async Task SocketStreamReceivedData(BiosignalsData dataToSend)
    {
     
        // Serialize object to JSON string
        string json = JsonSerializer.Serialize(dataToSend);
        byte[] encoded = Encoding.UTF8.GetBytes(json);
        var buffer = new ArraySegment<byte>(encoded);
        // Send JSON data over the WebSocket
        await _clientWebSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
        Console.WriteLine("Sent JSON: " + json);
        
    }

    private async Task MockStreaming()
    {
        for (int i = 0; i < 10; i++)
        {
            await SocketStreamReceivedData(new BiosignalsData(1 * i, 2 * i, 3 * i));
            await Task.Delay(1000);
        }
    }
    public async Task DisconnectSocket()
    {
        if (_clientWebSocket.State == WebSocketState.Open)
        {
            await _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            Console.WriteLine("Disconnected from WebSocket server.");
        }
    }
 
    // Manage application quitting and release all resources
    void OnApplicationQuit()
    {
        _shimmer.StopStreaming();
        _shimmer.Disconnect();
        // Stop the background queue streaming
        _cancellationTokenSource.Cancel(); 

    }
}




