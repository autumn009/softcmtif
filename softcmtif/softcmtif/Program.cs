using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.ComTypes;
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

    class Program
    {
        static void Main(string[] args)
        {
            ChannelType channelType = ChannelType.Null;
            AudioFileReader audioStream = null;
            long currentBaseOffset = 0;
            float[] buffer = null;
            int bufferPointer = 0;
            long peakCount = 0;
            TextWriter peaklogWriter = null;
            float upperPeak, lowerPeak;
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Soft CMT Interface by autumn");
                Console.Error.WriteLine("usage: softcmtif INPUT_FILE_NAME [--verbose] [--right|--left|--average] [--peaklog FILE_NAME]");
                return;
            }
            bool bVerbose = false;
            bool peaklogWaiting = false;
            foreach (var item in args.Skip(1))
            {
                if(peaklogWaiting)
                {
                    peaklogWriter = File.CreateText(item);
                    peaklogWaiting = false;
                }
                else if (item == "--verbose") bVerbose = true;
                else if (item == "--right") channelType = ChannelType.Right;
                else if (item == "--left") channelType = ChannelType.Left;
                else if (item == "--average") channelType = ChannelType.Average;
                else if (item == "--peaklog") peaklogWaiting = true;
                else
                {
                    Console.WriteLine($"Unknwon option {item}");
                    return;
                }
            }
            if (bVerbose) Console.WriteLine($"File Length: {new FileInfo(args[0]).Length}");
            using (audioStream = new AudioFileReader(args[0]))
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

                // detect upper peak and lower peak
                audioStream.Position = 0;
                upperAndLowerPeak();

                // detect wave peaks
                audioStream.Position = 0;
                Tuple<float, long> peak = new Tuple<float, long>(0,0);
                long lastPeakOffset = 0;
                bool upper = true;
                for (; ; )
                {
                    var d = readUnit();
                    if (d == null) break;

                    if (peaklogWriter != null) peaklogWriter.WriteLine($"[{d.Item1} {d.Item2}]");
                    if (upper)
                    {
                        if (d.Item1 > peak.Item1) peak = d;
                        else if (d.Item1 < 0.0f)
                        {
                            notifyPeak(peak.Item2 - lastPeakOffset);
                            if (peaklogWriter != null) peaklogWriter.WriteLine($"PEAK {peak.Item2 - lastPeakOffset} {peak.Item2}");
                            lastPeakOffset = peak.Item2;
                            upper = false;
                            peak = d;
                        }
                    }
                    else
                    {
                        if (d.Item1 < peak.Item1) peak = d;
                        else if (d.Item1 > 0.0f)
                        {
                            notifyPeak(peak.Item2 - lastPeakOffset);
                            if (peaklogWriter != null) peaklogWriter.WriteLine($"PEAK {peak.Item2 - lastPeakOffset} {peak.Item2}");
                            lastPeakOffset = peak.Item2;
                            upper = true;
                            peak = d;
                        }
                    }
                }

                //float[] samples = new float[audioStream.Length / audioStream.BlockAlign * audioStream.WaveFormat.Channels];
                //audioStream.Read(samples, 0, samples.Length);



                // playback
                //var outputDevice = new WaveOutEvent();
                //outputDevice.Init(audioStream);
                //outputDevice.Play();
                //await Task.Delay(10000);

                if (bVerbose) Console.WriteLine($"Detected: {peakCount} peaks.");
            }
            if (peaklogWriter != null) peaklogWriter.Close();
            Console.WriteLine("Done");

            void notifyPeak(long timeOffset)
            {
                peakCount++;
                //if (bVerbose && peakCount < 20) Console.Write($"{timeOffset},");
            }

            float[] readBlock()
            {
                var buf = new float[256];
                currentBaseOffset = audioStream.Position;
                var bytes = audioStream.Read(buf, 0, buf.Length);
                if (bytes == 0) return null;
                return buf;
            }

            Tuple<float, long> readRawUnit()
            {
                if (buffer == null || bufferPointer >= buffer.Length)
                {
                    buffer = readBlock();
                    if (buffer == null) return null;
                    bufferPointer = 0;
                }
                var r = new Tuple<float, long>(buffer[bufferPointer], bufferPointer + currentBaseOffset);
                bufferPointer++;
                return r;
            }

            Tuple<float, long> readUnit()
            {
                Tuple<float, long> t = null;
                var l = readRawUnit();
                if (l == null) return null;
                if (audioStream.WaveFormat.Channels == 0)
                {
                    t = l;
                }
                else
                {
                    var r = readRawUnit();
                    if (r == null) return null;
                    switch (channelType)
                    {
                        case ChannelType.Left:
                            t = l;
                            break;
                        case ChannelType.Right:
                            t = r;
                            break;
                        default:
                            t = new Tuple<float, long>((r.Item1 + l.Item1) / 2, l.Item2);
                            break;
                    }
                }
                return t;
            }

            void upperAndLowerPeak()
            {
                upperPeak = 0.0f;
                lowerPeak = 0.0f;
                for (; ; )
                {
                    var pair = readUnit();
                    if (pair == null) break;
                    if (upperPeak < pair.Item1) upperPeak = pair.Item1;
                    if (lowerPeak > pair.Item1) lowerPeak = pair.Item1;
                }
                if (bVerbose) Console.WriteLine($"Detect upperPeak/lowerPeak: {upperPeak}/{lowerPeak}");
            }
        }
    }
}
