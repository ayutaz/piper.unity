using UnityEngine;
using Unity.Sentis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Piper
{
    public class PiperManager : MonoBehaviour
    {
        public ModelAsset modelAsset;
        public int sampleRate = 22050;

        // Piperが必要とする入力スケールなど。Inspectorで調整可能に
        public float scaleSpeed   = 0.667f;
        public float scalePitch   = 1.0f;
        public float scaleGlottal = 0.8f;

        // espeak-ngデータのあるディレクトリ
        // (StreamingAssets/espeak-ng-data などに配置している想定)
        public string espeakNgRelativePath = "espeak-ng-data";
        public string voice = "en-us";

        private Model runtimeModel;
        private Worker worker;

        void Awake()
        {
            // 1. PiperWrapperを初期化（espeak-ngのデータパスを渡す）
            string espeakPath = Path.Combine(Application.streamingAssetsPath, espeakNgRelativePath);
            PiperWrapper.InitPiper(espeakPath);

            // 2. Sentisモデルを読み込み、Worker作成
            runtimeModel = ModelLoader.Load(modelAsset);
            worker = new Worker(runtimeModel, BackendType.CPU);
        }

        /// <summary>
        /// コルーチンでテキストをTTSし、完了したら onComplete にAudioClipを返す
        /// </summary>
        public IEnumerator TextToSpeechCoroutine(string text, Action<AudioClip> onComplete)
        {
            // 3. テキストをPiperWrapperでフォネマイズし、文ごとの音素IDを取得
            var phonemeResult = PiperWrapper.ProcessText(text, voice);
            var allSamples = new List<float>();

            // 4. 各文に対して推論を実行し、結果を結合
            for (int s = 0; s < phonemeResult.Sentences.Length; s++)
            {
                var sentence = phonemeResult.Sentences[s];
                int[] phonemeIds = sentence.PhonemesIds;

                // (a) 入力テンソル作成
                using var inputTensor = new Tensor<int>(new TensorShape(1, phonemeIds.Length), phonemeIds);

                using var inputLengthsTensor = new Tensor<int>(
                    new TensorShape(1), new int[] { phonemeIds.Length });

                using var scalesTensor = new Tensor<float>(
                    new TensorShape(3),
                    new float[] { scaleSpeed, scalePitch, scaleGlottal }
                );

                // (b) モデルの入力名を合わせる
                //     例: 0番目= "input", 1番目= "input_lengths", 2番目= "scales"
                //     実際に runtimeModel.inputs[x].name で確認してください
                string inputName         = runtimeModel.inputs[0].name;
                string inputLengthsName  = runtimeModel.inputs[1].name;
                string scalesName        = runtimeModel.inputs[2].name;

                worker.SetInput(inputName,         inputTensor);
                worker.SetInput(inputLengthsName,  inputLengthsTensor);
                worker.SetInput(scalesName,        scalesTensor);

                // (c) 推論開始
                worker.Schedule();

                // (d) フレームをまたぎながら実行してフリーズを防ぐ
                var enumSchedule = worker.ScheduleIterable();
                while (enumSchedule.MoveNext())
                    yield return null;

                // (e) 出力を取得
                Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
                float[] sentenceSamples = outputTensor.DownloadToArray();

                // (f) 全文を一つの波形リストに追加
                allSamples.AddRange(sentenceSamples);
            }

            // 5. 結合した音声波形で AudioClip を作成
            AudioClip clip = AudioClip.Create("PiperTTS", allSamples.Count, 1, sampleRate, false);
            clip.SetData(allSamples.ToArray(), 0);

            onComplete?.Invoke(clip);
        }

        void OnDestroy()
        {
            // PiperWrapperの解放 & Worker破棄
            PiperWrapper.FreePiper();
            if (worker != null)
                worker.Dispose();
        }
    }
}
