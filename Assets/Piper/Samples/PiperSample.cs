using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace Piper
{
    public class PiperSample : MonoBehaviour
    {
        public PiperManager piper;
        public InputField input;
        public Button submitButton;
        public AudioSource source;
        public GameObject cube;

        void Awake()
        {
            submitButton.onClick.AddListener(OnButtonPressed);
        }

        void Update()
        {
            cube.transform.Rotate(Vector3.one * (Time.deltaTime * 10f));
        }

        private void OnButtonPressed()
        {
            string text = input.text;
            StartCoroutine(TextToSpeechAndPlay(text));
        }

        private IEnumerator TextToSpeechAndPlay(string text)
        {
            if (source.isPlaying) source.Stop();
            if (source.clip != null) Destroy(source.clip);

            // PiperManagerのコルーチンを呼び、生成完了後にAudioClipを受け取る
            yield return piper.TextToSpeechCoroutine(text, (clip) =>
            {
                source.clip = clip;
                source.Play();
            });
        }
    }
}