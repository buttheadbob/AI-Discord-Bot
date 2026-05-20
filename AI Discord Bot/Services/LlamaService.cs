using System.Runtime.InteropServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace AI_Discord_Bot.Services;

public class LlamaService : IDisposable
{
    private LLamaWeights? _model;
    private ModelParams? _modelParams;
    private bool _disposed;

    public bool IsLoaded => _model is not null;
    public string ModelPath { get; private set; } = "";
    public string ActiveBackend { get; private set; } = "None";
    public int LoadedContextSize { get; private set; }

    private const int MinContext = 2048;
    private const int MaxContext = 262144;

    public static int DetectOptimalContextSize(int gpuLayerCount)
    {
        if (gpuLayerCount <= 0)
            return 4096;

        try
        {
            var gpuMemoryBytes = QueryGpuMemoryBytes();
            if (gpuMemoryBytes <= 0)
                return 8192;

            var gpuMemoryGb = gpuMemoryBytes / (1024.0 * 1024.0 * 1024.0);

            if (gpuMemoryGb < 4) return 4096;
            if (gpuMemoryGb < 6) return 8192;
            if (gpuMemoryGb < 10) return 16384;
            if (gpuMemoryGb < 16) return 65536;
            return 131072;
        }
        catch
        {
            return 8192;
        }
    }

    private static long QueryGpuMemoryBytes()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return 0;

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT AdapterRAM FROM Win32_VideoController WHERE AdapterRAM IS NOT NULL");

            long maxRam = 0;
            foreach (var obj in searcher.Get())
            {
                if (obj["AdapterRAM"] is long ram)
                    maxRam = Math.Max(maxRam, ram);
                else if (obj["AdapterRAM"] is uint ram32)
                    maxRam = Math.Max(maxRam, ram32);
            }

            return maxRam;
        }
        catch
        {
            return 0;
        }
    }

    public async Task LoadModelAsync(string modelPath, int gpuLayerCount, int contextSize, bool flashAttention, IProgress<string>? progress = null)
    {
        UnloadModel();

        ModelPath = modelPath;
        ActiveBackend = "Detecting...";
        LoadedContextSize = contextSize;
        progress?.Report($"Loading model (context: {contextSize})...");

        var parameters = new ModelParams(modelPath)
        {
            ContextSize = (uint)contextSize,
            BatchSize = 512,
            UBatchSize = 512,
            GpuLayerCount = gpuLayerCount,
            UseMemorymap = false,
            FlashAttention = flashAttention
        };

        _model = await Task.Run(() => LLamaWeights.LoadFromFile(parameters));
        _modelParams = parameters;

        ActiveBackend = gpuLayerCount > 0 ? "GPU (Vulkan/CUDA)" : "CPU";
        progress?.Report("Model loaded");
    }

    public void UnloadModel()
    {
        _model?.Dispose();
        _model = null;
        _modelParams = null;
        ActiveBackend = "None";
        ModelPath = "";
    }

    public async Task<string> PromptAsync(string systemPrompt, string userPrompt)
    {
        if (_model is null || _modelParams is null)
            throw new InvalidOperationException("Model not loaded.");

        return await Task.Run(() =>
        {
            var executor = new StatelessExecutor(_model, _modelParams);

            var prompt = $"<|im_start|>system\n{systemPrompt}<|im_end|>\n<|im_start|>user\n{userPrompt}<|im_end|>\n<|im_start|>assistant\n";

            var inferenceParams = new InferenceParams
            {
                MaxTokens = 1024,
                AntiPrompts = ["<|im_end|>", "<|im_start|>", "<|endoftext|>"],
                SamplingPipeline = new DefaultSamplingPipeline()
            };

            var sb = new StringBuilder();
            foreach (var text in executor.InferAsync(prompt, inferenceParams).ToBlockingEnumerable())
                sb.Append(text);

            return sb.ToString();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnloadModel();
    }
}
