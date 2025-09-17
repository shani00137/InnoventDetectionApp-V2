using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HumanDetection.Utilites.Audio
{
    public interface IAudioManager
    {
        void Play(string filePath);
        void Stop();
        void Pause();
        void Resume();
        bool IsPlaying { get; }
    }
}
