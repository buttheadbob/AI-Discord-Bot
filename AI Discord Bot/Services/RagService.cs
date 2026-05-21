using System.IO;
using LLama.Common;
using LLama.Sampling;
using LLamaSharp.KernelMemory;
using Microsoft.KernelMemory;
using AI_Discord_Bot.Models;

namespace AI_Discord_Bot.Services;

public class RagService : IDisposable
{
    private MemoryServerless? _memory;
    private string _embedModelPath = "";
    private string _chatModelPath = "";
    private bool _disposed;

    public bool IsInitialized => _memory is not null;
    public int DocumentCount { get; private set; }

    public string EmbeddingModelPath => _embedModelPath;

    public event Action<string, LogLevel>? LogMessage;

    public void Initialize(string embedModelPath, string chatModelPath, int contextSize = 4096, int gpuLayerCount = 0)
    {
        if (_memory is not null)
            _memory = null;

        if (!File.Exists(embedModelPath))
            throw new FileNotFoundException("Embedding model not found.", embedModelPath);

        if (!File.Exists(chatModelPath))
            throw new FileNotFoundException("Chat model not found.", chatModelPath);

        _embedModelPath = embedModelPath;
        _chatModelPath = chatModelPath;

        var embedConfig = new LLamaSharpConfig(embedModelPath)
        {
            GpuLayerCount = gpuLayerCount
        };
        var chatConfig = new LLamaSharpConfig(chatModelPath)
        {
            ContextSize = (uint)contextSize,
            DefaultInferenceParams = new InferenceParams
            {
                MaxTokens = 2048,
                AntiPrompts = ["\n\n\n", "\n###", "<|im_end|>"],
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.3f,
                    RepeatPenalty = 1.2f
                }
            }
        };

        var builder = new KernelMemoryBuilder();
        builder.WithLLamaSharpTextEmbeddingGeneration(embedConfig);
        builder.WithLLamaSharpTextGeneration(chatConfig);
        _memory = builder.Build<MemoryServerless>();

        LogMessage?.Invoke($"RAG initialized. Embedding: {Path.GetFileName(embedModelPath)}, Chat: {Path.GetFileName(chatModelPath)}", LogLevel.Info);
    }

    public async Task IngestDocumentsAsync(IEnumerable<string> filePaths, IProgress<int>? progress = null)
    {
        if (_memory is null)
            throw new InvalidOperationException("RAG not initialized.");

        var paths = filePaths.ToList();
        var count = 0;

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                LogMessage?.Invoke($"Document not found: {path}", LogLevel.Warning);
                continue;
            }

            var docId = Path.GetFileNameWithoutExtension(path) + "_" + Guid.NewGuid().ToString("N")[..8];
            await _memory.ImportDocumentAsync(path, documentId: docId);
            count++;
            LogMessage?.Invoke($"Ingested: {Path.GetFileName(path)}", LogLevel.Info);
            progress?.Report(count * 100 / paths.Count);
        }

        DocumentCount = count;
        LogMessage?.Invoke($"Ingestion complete: {count} document(s)", LogLevel.Info);
    }

    public async Task<MemoryAnswer> AskAsync(string question)
    {
        if (_memory is null)
            throw new InvalidOperationException("RAG not initialized.");

        return await _memory.AskAsync(question);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _memory = null;
        _embedModelPath = "";
        _chatModelPath = "";
    }
}
