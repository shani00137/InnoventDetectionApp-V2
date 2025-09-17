using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;

namespace HumanDetection.Utilites.Audio
{
    public class AudioManager : IAudioManager
    {
        private IWavePlayer waveOut;
        private AudioFileReader audioFileReader;

        public bool IsPlaying { get; private set; }

        public void Play(string filePath)
        {
            if (IsPlaying)
                return;

            Stop();

            waveOut = new WaveOut();
            audioFileReader = new AudioFileReader(filePath);


            var loop = new LoopStream(audioFileReader); // Wrap the reader

            waveOut.Init(loop);
            waveOut.Play();
            IsPlaying = true;

            waveOut.PlaybackStopped += (sender, args) =>
            {
                waveOut.Dispose();
                audioFileReader.Dispose();
                IsPlaying = false;
            };
        }



        public void Stop()
        {
            if (waveOut != null)
            {
                waveOut.Stop();
                IsPlaying = false;
            }
        }

        public void Pause()
        {
            if (waveOut != null && IsPlaying)
            {
                waveOut.Pause();
                IsPlaying = false;
            }
        }

        public void Resume()
        {
            if (waveOut != null && !IsPlaying)
            {
                waveOut.Play();
                IsPlaying = true;
            }
        }
    }
}
