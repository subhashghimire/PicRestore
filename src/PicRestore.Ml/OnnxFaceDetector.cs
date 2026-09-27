using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PicRestore.Restoration.Identity;

namespace PicRestore.Ml;

/// <summary>
/// Creates the YuNet face detector (OpenCV Zoo "face_detection_yunet_2023mar", MIT licence, 232 KB) on
/// ONNX Runtime. The model ships with the app in its Models folder, so face locking works offline.
/// </summary>
public static class OnnxFaceDetector
{
    public const string FileName = "face_detection_yunet_2023mar.onnx";

    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, "Models", FileName);

    public static YuNetFaceDetector Create()
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        var session = new InferenceSession(ModelPath, options); // lives as long as the app
        var gate = new object();

        return new YuNetFaceDetector(tile =>
        {
            var input = new DenseTensor<float>(tile, new[] { 1, 3, YuNetFaceDetector.InputSize, YuNetFaceDetector.InputSize });
            lock (gate)
            {
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
                    session.Run(new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", input) });
                return results.ToDictionary(r => r.Name, r => r.AsTensor<float>().ToArray());
            }
        });
    }
}
