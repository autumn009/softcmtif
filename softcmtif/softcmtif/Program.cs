using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;

namespace softcmtif
{
    enum ChannelType
    {
        Null,
        Right,
        Left,
        Average
    }

    class MyWaveReader
    {


        public MyWaveReader()
        {

        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            ChannelType channelType = ChannelType.Null;
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Soft CMT Interface by autumn");
                Console.Error.WriteLine("usage: softcmtif INPUT_FILE_NAME [--verbose] [--right|--left|--average]");
                return;
            }
            bool bVerbose = false;
            foreach (var item in args.Skip(1))
            {
                if (item == "--verbose") bVerbose = true;
                else if (item == "--right") channelType = ChannelType.Right;
                else if (item == "--left") channelType = ChannelType.Left;
                else if (item == "--average") channelType = ChannelType.Average;
                else
                {
                    Console.WriteLine($"Unknwon option {item}");
                    return;
                }
            }
            if (bVerbose) Console.WriteLine($"File Length: {new FileInfo(args[0]).Length}");
            using (var audioStream = new AudioFileReader(args[0]))
            {
                audioStream.Position = 0;
                if (bVerbose)
                {
                    Console.WriteLine($"Audio Stream Length: {audioStream.Length}");
                    Console.WriteLine($"BlockAlign: {audioStream.BlockAlign}");
                    Console.WriteLine($"Channels: {audioStream.WaveFormat.Channels}");
                    Console.WriteLine($"BitsPerSample: {audioStream.WaveFormat.BitsPerSample}");
                    Console.WriteLine($"Encoding: {audioStream.WaveFormat.Encoding}");
                    Console.WriteLine($"ExtraSize: {audioStream.WaveFormat.ExtraSize}");
                    Console.WriteLine($"SampleRate: {audioStream.WaveFormat.SampleRate}");
                }
                if (audioStream.WaveFormat.Channels == 1)
                {
                    if (channelType != ChannelType.Null)
                    {
                        Console.WriteLine("Mono file not support --right|--left|--average");
                        return;
                    }
                }
                else if (audioStream.WaveFormat.Channels == 2)
                {
                    if (channelType == ChannelType.Null) channelType = ChannelType.Average;
                }
                else
                {
                    Console.WriteLine($"Not supported channels {audioStream.WaveFormat.Channels}");
                    return;
                }
                if (bVerbose) Console.WriteLine($"Channel selected: ChannelType:{channelType}");




                //float[] samples = new float[audioStream.Length / audioStream.BlockAlign * audioStream.WaveFormat.Channels];
                //audioStream.Read(samples, 0, samples.Length);



                    // playback
                    //var outputDevice = new WaveOutEvent();
                    //outputDevice.Init(audioStream);
                    //outputDevice.Play();
                    //await Task.Delay(10000);

            }
            Console.WriteLine("Done");
        }
    }

}
