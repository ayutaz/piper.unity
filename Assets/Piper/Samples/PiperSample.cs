using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

namespace Piper
{
    public class PiperSample : MonoBehaviour
    {
        public PiperManager piper;
        public InputField input;
        public Button submitButton;
        public AudioSource source;

        private void Awake()
        {
            submitButton.onClick.AddListener(OnButtonPressed);
        }

        private async void OnButtonPressed()
        {
            string text = input.text;

            // 1. もしAudioSourceが再生中なら停止してクリップ破棄
            if (source.isPlaying) source.Stop();
            if (source.clip) Destroy(source.clip);

            // 2. 非同期でTTSを実行 (メインスレッド上で進行)
            AudioClip clip = await piper.TextToSpeechAsync(text);

            // 3. 再生
            source.clip = clip;
            source.Play();
        }
    }
}