using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;

namespace softcmtif
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Soft CMT Interface by autumn");
                Console.Error.WriteLine("usage: softcmtif INPUT_FILE_NAME [--verbose]");
                return;
            }
            bool bVerbose = false;
            foreach (var item in args.Skip(1))
            {
                if (item == "--verbose") bVerbose = true;
                else
                {
                    Console.WriteLine($"Unknwon option {item}");
                    return;
                }
            }
            if (bVerbose) Console.WriteLine($"File Length: {new FileInfo(args[0]).Length}");
            var audioStream = new AudioFileReader(args[0]);
            audioStream.Position = 0;
            if (bVerbose) Console.WriteLine($"Audio Stream Length: {audioStream.Length}");

            //float[] samples = new float[audioStream.Length / audioStream.BlockAlign * audioStream.WaveFormat.Channels];
            //audioStream.Read(samples, 0, samples.Length);



            // playback
            //var outputDevice = new WaveOutEvent();
            //outputDevice.Init(audioStream);
            //outputDevice.Play();
            //await Task.Delay(10000);

            Console.WriteLine("Done");
        }
    }

}
