using System;
using System.ComponentModel.Design;
using System.Diagnostics;
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
        Average,
        Maximum
    }

    enum Speeds
    {
        s300bps, s600bps, s1200bps
    }

    enum DetectMode
    {
        WaitHeader,
        CountingD3,
        GettingFileName,
        GettingBody,
    }

    enum CDMode
    {
        CarrierDetecting,
        CarrierDetected,
        TransferMode
    }

    class Program
    {
        static void Main(string[] args)
        {
            const int NoiseSilencerEffect = 10;
            ChannelType channelType = ChannelType.Null;
            AudioFileReader audioStream = null;
            long currentBaseOffset = 0;
            float[] buffer = null;
            int bufferPointer = 0;
            long peakCount = 0;
            TextWriter peaklogWriter = null;
            TextWriter outRawWriter = null;
            string outputDirectory = null;
            float upperPeak, lowerPeak;
            Tuple<float, long> peak = new Tuple<float, long>(0, 0);
            long lastPeakOffset = 0;
            bool upper = true;
            Speeds speed = Speeds.s600bps;
            int OnePeaks;
            int ZeroPeaks;
            int ThresholdPeakCount;
            int TypicalOneCount;
            int TypicalZeroCount;
            int peakCount1 = 0;
            int peakCount0 = 0;
            bool?[] shiftRegister = new bool?[11];
            DetectMode currentMode = DetectMode.WaitHeader;
            int valueCounter = 0;
            string currentFileName = "";
            byte[] currentFileImage = new byte[32767];
            int currentFileImageSize = 0;
            CDMode carrierDetectMode = CDMode.CarrierDetecting;
            bool bitsInPeakLog = true;

            if (args.Length == 0)
            {
                Console.Error.WriteLine("Soft CMT Interface by autumn");
                Console.Error.WriteLine("usage: softcmtif INPUT_FILE_NAME [--verbose] [--300|--600|--1200] [--right|--left|--average|--maximum] [--peaklog FILE_NAME] [--bitsinpeaklog] [--outraw FILE_NAME] [--outdir PATH]");
                return;
            }
            bool bVerbose = false;
            bool peaklogWaiting = false;
            bool outRawWaiting = false;
            bool outDirWaiting = false;
            foreach (var item in args.Skip(1))
            {
                if (peaklogWaiting)
                {
                    peaklogWriter = File.CreateText(item);
                    peaklogWaiting = false;
                }
                else if (outRawWaiting)
                {
                    outRawWriter = File.CreateText(item);
                    outRawWaiting = false;
                }
                else if (outDirWaiting)
                {
                    outputDirectory = item;
                    outDirWaiting = false;
                }
                else if (item == "--verbose") bVerbose = true;
                else if (item == "--right") channelType = ChannelType.Right;
                else if (item == "--left") channelType = ChannelType.Left;
                else if (item == "--average") channelType = ChannelType.Average;
                else if (item == "--maximum") channelType = ChannelType.Maximum;
                else if (item == "--bitsinpeaklog") bitsInPeakLog = true;
                else if (item == "--peaklog") peaklogWaiting = true;
                else if (item == "--outraw") outRawWaiting = true;
                else if (item == "--outdir") outDirWaiting = true;
                else if (item == "--300") speed = Speeds.s300bps;
                else if (item == "--600") speed = Speeds.s600bps;
                else if (item == "--1200") speed = Speeds.s1200bps;
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
                setSpeed();
                clearShiftRegister();
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
                    if (channelType == ChannelType.Null) channelType = ChannelType.Maximum;
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
                for (; ; )
                {
                    var d = readUnit();
                    if (d == null) break;

                    //if (peaklogWriter != null) peaklogWriter.WriteLine($"[{d.Item1} {d.Item2}]");
                    if (upper)
                    {
                        if (d.Item1 > peak.Item1) peak = d;
                        else if (d.Item1 < 0.0f) setPeak(d, false);
                    }
                    else
                    {
                        if (d.Item1 < peak.Item1) peak = d;
                        else if (d.Item1 > 0.0f) setPeak(d, true);
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
            if (outRawWriter != null) outRawWriter.Close();
            Console.WriteLine("Done");

            void saveFile()
            {
                if (outputDirectory == null)
                {
                    Console.WriteLine("If you want to save this file, use --outdir option");
                    return;
                }
                var fullpath = Path.Combine(outputDirectory, DateTime.Now.ToString("yyyyMMddHHmmss") + " " + currentFileName + ".bin");
                using (var stream = File.Create(fullpath))
                {
                    stream.Write(currentFileImage, 0, currentFileImageSize - 9 + 2);
                }
                Console.WriteLine($"{fullpath} saved");
            }

            void fileDetector(int value)
            {
                if (currentMode == DetectMode.WaitHeader)
                {
                    if (value == 0xd3)
                    {
                        valueCounter++;
                        if (valueCounter >= 10)
                        {
                            valueCounter = 0;
                            currentFileName = "";
                            currentMode = DetectMode.GettingFileName;
                        }
                    }
                    else
                        valueCounter = 0;
                }
                else if (currentMode == DetectMode.GettingFileName)
                {
                    if (value != 0) currentFileName += (char)value;
                    valueCounter++;
                    if (valueCounter >= 6)
                    {
                        Console.WriteLine($"Found: {currentFileName} (N-BASIC Binary Image)");
                        valueCounter = 0;
                        currentMode = DetectMode.GettingBody;
                        currentFileImageSize = 0;
                        goToCarrierDetectMode();
                    }
                }
                else if (currentMode == DetectMode.GettingBody)
                {
                    currentFileImage[currentFileImageSize++] = (byte)value;
                    if (value == 0)
                    {
                        valueCounter++;
                        if (valueCounter == 12)
                        {
                            saveFile();
                            valueCounter = 0;
                            currentMode = DetectMode.WaitHeader;
                            goToCarrierDetectMode();
                        }
                    }
                    else
                    {
                        valueCounter = 0;
                    }
                }
            }

            void notifyByte(int value)
            {
                if (peaklogWriter != null) peaklogWriter.WriteLine($"BYTE {value:X2}");
                if (outRawWriter != null) outRawWriter.Write((char)value);

                fileDetector(value);

                // TBW
            }

            void clearShiftRegister()
            {
                for (int i = 0; i < shiftRegister.Length; i++) shiftRegister[i] = true;
            }

            void tapeReadError()
            {
                Console.WriteLine($"Tape Read Error [offset:{currentBaseOffset + bufferPointer}]");
                Process.GetCurrentProcess().Close();
            }

            void notifyBit(bool? bit)
            {
                if (bitsInPeakLog && peaklogWriter != null) peaklogWriter.WriteLine($"[{bit} {peakCount0} {peakCount1}]");
                for (int i = 0; i < shiftRegister.Length - 1; i++) shiftRegister[i] = shiftRegister[i + 1];
                shiftRegister[shiftRegister.Length - 1] = bit;
                // check start bit and two stop bit
                if (shiftRegister[0] == false && shiftRegister[9] == true && shiftRegister[10] == true)
                {
                    // found frame
                    var val = 0;
                    for (int j = 0; j < 8; j++)
                    {
                        val >>= 1;
                        if (shiftRegister[j + 1] == true) val |= 0x80;
                        else if (shiftRegister[j + 1] == null)
                        {
                            tapeReadError();
                            return;
                        } 
                    }
                    notifyByte(val);
                    clearShiftRegister();
                }
            }

            void setPeak(Tuple<float, long> d, bool upperValue)
            {
                var timeOffset = (peak.Item2 - lastPeakOffset) / audioStream.WaveFormat.Channels;
                notifyPeak(timeOffset);
                //if (peaklogWriter != null) peaklogWriter.WriteLine($"PEAK {timeOffset} {peak.Item2}");
                lastPeakOffset = peak.Item2;
                peak = d;
                upper = upperValue;
            }

            void goToCarrierDetectMode()
            {
                carrierDetectMode = CDMode.CarrierDetecting;
            }

            void notifyPeak(long timeOffset)
            {
                peakCount++;
                //if (bVerbose && peakCount < 20) Console.Write($"{timeOffset},");
                var b = timeOffset < ThresholdPeakCount;
                //if (peaklogWriter != null) peaklogWriter.WriteLine($"[{(b ? 1 : 0)}]");

                if (carrierDetectMode == CDMode.CarrierDetecting)
                {
                    if (b == false) return;
                    carrierDetectMode = CDMode.CarrierDetected;
                    return;
                }
                else if (carrierDetectMode == CDMode.CarrierDetected)
                {
                    if (b == true) return;
                    carrierDetectMode = CDMode.TransferMode;
                }

                if (b) peakCount1++; else peakCount0++;
                if (peakCount1 == OnePeaks && peakCount0 == 0)
                {
                    notifyBit(true);
                    peakCount1 = 0;
                    peakCount0 = 0;
                }
                else if (peakCount1 == 0 && peakCount0 == ZeroPeaks)
                {
                    notifyBit(false);
                    peakCount1 = 0;
                    peakCount0 = 0;
                }
                else if (peakCount1 + peakCount0 * 2 >= OnePeaks)
                {
                    notifyBit(null);
                    peakCount1 = 0;
                    peakCount0 = 0;
                }
            }

            float[] readBlock()
            {
                var buf = new float[256];
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
                    //currentBaseOffset = audioStream.Position;
                    currentBaseOffset += bufferPointer;
                    bufferPointer = 0;
                }
                var v = buffer[bufferPointer];

                // noise silencer
                if (v > 0 && v < upperPeak / NoiseSilencerEffect)
                {
                    v = 0;
                    //if (peaklogWriter != null) peaklogWriter.WriteLine("Detect upper cancel");
                }
                if (v < 0 && v > lowerPeak / NoiseSilencerEffect)
                {
                    v = 0;
                    //if (peaklogWriter != null) peaklogWriter.WriteLine("Detect lower cancel");
                }

                var r = new Tuple<float, long>(v, bufferPointer + currentBaseOffset);
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
                        case ChannelType.Maximum:
                            if (r.Item1 >= 0 && l.Item1 >= 0) t = new Tuple<float, long>(Math.Max(r.Item1, l.Item1), l.Item2);
                            else t = new Tuple<float, long>(Math.Min(r.Item1, l.Item1), l.Item2);
                            break;
                        default:
                            t = new Tuple<float, long>((r.Item1 + l.Item1) / 2, l.Item2);
                            break;
                    }
                }
                return t;
            }

            void setSpeed()
            {
                TypicalZeroCount = (int)(1.0 / 1200 / 2 * audioStream.WaveFormat.SampleRate);
                TypicalOneCount = (int)(1.0 / 2400 / 2 * audioStream.WaveFormat.SampleRate);
                ThresholdPeakCount = (TypicalZeroCount + TypicalOneCount) / 2;
                if (bVerbose) Console.WriteLine($"TypicalZeroCount:{TypicalZeroCount} TypicalOneCount:{TypicalOneCount} ThresholdPeakCount:{ThresholdPeakCount}");

                switch (speed)
                {
                    case Speeds.s300bps:
                        OnePeaks = 8 * 2;
                        ZeroPeaks = 4 * 2;
                        break;
                    case Speeds.s600bps:
                        OnePeaks = 4 * 2;
                        ZeroPeaks = 2 * 2;
                        break;
                    default:
                        OnePeaks = 2 * 2;
                        ZeroPeaks = 1 * 2;
                        break;
                }
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
