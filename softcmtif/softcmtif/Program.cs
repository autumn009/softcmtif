using System;
using System.Threading.Tasks;
using NAudio.Wave;

namespace softcmtif
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if( args.Length != 1)
            {
                Console.Error.WriteLine("Soft CMT Interface by autumn");
                Console.Error.WriteLine("usage: softcmtif INPUT_FILE_NAME");
                return;
            }

            var audioStream = new AudioFileReader(args[0]);
            audioStream.Position = 0;
            var outputDevice = new WaveOutEvent();
            outputDevice.Init(audioStream);
            outputDevice.Play();
            await Task.Delay(10000);

            Console.WriteLine("Done");
        }
    }
}
