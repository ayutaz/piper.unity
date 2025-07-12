using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Piper
{
    public class PiperManager : MonoBehaviour
    {
        public Unity.InferenceEngine.ModelAsset modelAsset;
        public int sampleRate = 22050;

        // Piperが必要とする入力スケールなど
        public float scaleSpeed = 1.0f;
        public float scalePitch = 1.0f;
        public float scaleGlottal = 0.8f;

        // espeak-ngのdataフォルダ
        public string espeakNgRelativePath = "espeak-ng-data";
        public string voice = "en-us";

        private Unity.InferenceEngine.Model runtimeModel;
        private Unity.InferenceEngine.Worker worker;

        [SerializeField]
        private Unity.InferenceEngine.BackendType backendType = Unity.InferenceEngine.BackendType.GPUCompute;

        private void Awake()
        {
            // 1. PiperWrapperを初期化
            string espeakPath = Path.Combine(Application.streamingAssetsPath, espeakNgRelativePath);
            PiperWrapper.InitPiper(espeakPath);

            // 2. Sentisモデルを読み込み、Worker作成
            runtimeModel = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
            worker = new Unity.InferenceEngine.Worker(runtimeModel, backendType);
        }

        /// <summary>
        /// テキストをTTSし、AudioClipを返す非同期メソッド（Taskベース）。
        /// フレームをまたぐ際は Task.Yield() を使い、メインスレッド上でジョブを進めます。
        /// </summary>
        public async Task<AudioClip> TextToSpeechAsync(string text)
        {
            // 3. PiperWrapperでテキストをフォネマイズ
            var phonemeResult = PiperWrapper.ProcessText(text, voice);
            var allSamples = new List<float>();

            // 4. 文ごとに推論を実行 & 結合
            for (int s = 0; s < phonemeResult.Sentences.Length; s++)
            {
                var sentence = phonemeResult.Sentences[s];
                int[] phonemeIds = sentence.PhonemesIds;

                // 入力テンソル作成
                using var inputTensor =
                    new Unity.InferenceEngine.Tensor<int>(new Unity.InferenceEngine.TensorShape(1, phonemeIds.Length),
                        phonemeIds);
                using var inputLengthsTensor =
                    new Unity.InferenceEngine.Tensor<int>(new Unity.InferenceEngine.TensorShape(1),
                        new int[] { phonemeIds.Length });
                using var scalesTensor = new Unity.InferenceEngine.Tensor<float>(
                    new Unity.InferenceEngine.TensorShape(3),
                    new float[] { scaleSpeed, scalePitch, scaleGlottal }
                );

                // 入力名をモデルに合わせる (たとえば 0=input, 1=input_lengths, 2=scales)
                string inputName = runtimeModel.inputs[0].name;
                string inputLengthsName = runtimeModel.inputs[1].name;
                string scalesName = runtimeModel.inputs[2].name;

                worker.SetInput(inputName, inputTensor);
                worker.SetInput(inputLengthsName, inputLengthsTensor);
                worker.SetInput(scalesName, scalesTensor);

                // スケジュール実行
                worker.Schedule();

                // 4-1. ScheduleIterableでジョブをフレームまたぎ進行
                var enumerator = worker.ScheduleIterable();
                while (enumerator.MoveNext())
                {
                    // コルーチンの代わりに Task.Yield() で1フレーム中断
                    await Task.Yield();
                }

                // 4-2. 出力を取得
                Unity.InferenceEngine.Tensor<float> outputTensor =
                    worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
                float[] sentenceSamples = outputTensor.DownloadToArray();
                allSamples.AddRange(sentenceSamples);
            }

            // 5. 音声波形をまとめて AudioClip 作成
            AudioClip clip = AudioClip.Create("PiperTTS", allSamples.Count, 1, sampleRate, false);
            clip.SetData(allSamples.ToArray(), 0);

            return clip;
        }

        private void OnDestroy()
        {
            PiperWrapper.FreePiper();
            if (worker != null)
            {
                worker.Dispose();
            }
        }
    }
}