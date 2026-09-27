using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PicRestore.Core.Abstractions;

namespace PicRestore.Ml;

/// <summary>
/// LaMa inpainting network (OpenCV Zoo ONNX export, "inpainting_lama_2025jan", Apache-2.0) run on the
/// CPU with ONNX Runtime. Inputs: "image" [1,3,512,512] B,G,R in 0..1 and "mask" [1,1,512,512] (1 = fill);
/// output: "output" [1,3,512,512] B,G,R in 0..255. The network already keeps unmasked pixels unchanged.
/// </summary>
public sealed class OnnxLamaModel : IInpaintingModel, IDisposable
{
    private const int Size = 512;
    private readonly InferenceSession _session;
    private readonly object _runLock = new();

    public OnnxLamaModel(string modelPath)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        _session = new InferenceSession(modelPath, options);
    }

    public int TileSize => Size;

    public void Run(float[] imageBgrChw, float[] mask, float[] outputBgrChw)
    {
        ArgumentNullException.ThrowIfNull(imageBgrChw);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(outputBgrChw);

        var image = new DenseTensor<float>(imageBgrChw, new[] { 1, 3, Size, Size });
        var maskTensor = new DenseTensor<float>(mask, new[] { 1, 1, Size, Size });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("image", image),
            NamedOnnxValue.CreateFromTensor("mask", maskTensor),
        };

        lock (_runLock)
        {
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);
            Tensor<float> output = results.First().AsTensor<float>();
            int i = 0;
            foreach (float value in output)
            {
                outputBgrChw[i++] = value;
            }
        }
    }

    public void Dispose() => _session.Dispose();
}
