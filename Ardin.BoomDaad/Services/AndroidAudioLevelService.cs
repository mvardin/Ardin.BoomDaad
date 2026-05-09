namespace Ardin.BoomDaad.Services;

#if ANDROID
using Android.Media;
using System.Text;
using System.Threading.Channels;
using Encoding = Android.Media.Encoding;
public class AndroidAudioLevelService : IAudioLevelService
{
    private AudioRecord _recorder;
    private bool _isRecording;
    private const int SampleRate = 44100;

    public void Start()
    {
        try
        {
            int bufferSize = AudioRecord.GetMinBufferSize(
                SampleRate,
                ChannelIn.Mono,
                Encoding.Pcm16bit
            );

            _recorder = new AudioRecord(
                AudioSource.Mic,
                SampleRate,
                ChannelIn.Mono,
                Encoding.Pcm16bit,
                bufferSize
            );

            _recorder.StartRecording();
            _isRecording = true;

            Task.Run(() => ProcessAudio(bufferSize));
        }
        catch (Exception ex)
        {
        }
    }

    public event Action<double> OnLevelChanged;

    public void Stop()
    {
        _isRecording = false;
        _recorder?.Stop();
        _recorder?.Release();
    }

    private void ProcessAudio(int bufferSize)
    {
        try
        {
            var buffer = new short[bufferSize];

            while (_isRecording)
            {
                int read = _recorder.Read(buffer, 0, bufferSize);
                if (read > 0)
                {
                    double sum = 0;
                    for (int i = 0; i < read; i++)
                        sum += buffer[i] * buffer[i];

                    double rms = Math.Sqrt(sum / read);
                    double db = 20 * Math.Log10(rms);

                    OnLevelChanged?.Invoke(db);
                }
            }
        }
        catch (Exception ex)
        {
        }
    }
}
#endif
public interface IAudioLevelService
{
    void Start();
    void Stop();
    event Action<double> OnLevelChanged;
}
