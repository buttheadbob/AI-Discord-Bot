using System.IO;
using System.Net.Http;

namespace AI_Discord_Bot.Services;

public class ModelDownloadService
{
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _downloadCts;

    public static readonly Dictionary<string, string> CuratedModels = new()
    {
        ["Llama 3.2 1B (Q4_K_M)"] = "https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q4_K_M.gguf",
        ["Llama 3.2 3B (Q4_K_M)"] = "https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf",
        ["Qwen 2.5 0.5B (Q4_K_M)"] = "https://huggingface.co/bartowski/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/Qwen2.5-0.5B-Instruct-Q4_K_M.gguf",
        ["Qwen 2.5 1.5B (Q4_K_M)"] = "https://huggingface.co/bartowski/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/Qwen2.5-1.5B-Instruct-Q4_K_M.gguf",
        ["Qwen 2.5 3B (Q4_K_M)"] = "https://huggingface.co/bartowski/Qwen2.5-3B-Instruct-GGUF/resolve/main/Qwen2.5-3B-Instruct-Q4_K_M.gguf",
        ["Qwen 2.5 7B (Q4_K_M)"] = "https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf",
        ["Qwen 2.5 Coder 1.5B (Q4_K_M)"] = "https://huggingface.co/bartowski/Qwen2.5-Coder-1.5B-Instruct-GGUF/resolve/main/Qwen2.5-Coder-1.5B-Instruct-Q4_K_M.gguf",
        ["Qwen 2.5 Coder 3B (Q4_K_M)"] = "https://huggingface.co/bartowski/Qwen2.5-Coder-3B-Instruct-GGUF/resolve/main/Qwen2.5-Coder-3B-Instruct-Q4_K_M.gguf",
        ["Qwen 2.5 Coder 7B (Q4_K_M)"] = "https://huggingface.co/bartowski/Qwen2.5-Coder-7B-Instruct-GGUF/resolve/main/Qwen2.5-Coder-7B-Instruct-Q4_K_M.gguf",
        ["Phi 3.5 Mini (Q4_K_M)"] = "https://huggingface.co/bartowski/Phi-3.5-mini-instruct-GGUF/resolve/main/Phi-3.5-mini-instruct-Q4_K_M.gguf",
        ["Gemma 2 2B (Q4_K_M)"] = "https://huggingface.co/bartowski/gemma-2-2b-it-GGUF/resolve/main/gemma-2-2b-it-Q4_K_M.gguf",
        ["Gemma 2 9B (Q4_K_M)"] = "https://huggingface.co/bartowski/gemma-2-9b-it-GGUF/resolve/main/gemma-2-9b-it-Q4_K_M.gguf",
        ["DeepSeek R1 Distill Qwen 1.5B (Q4_K_M)"] = "https://huggingface.co/bartowski/DeepSeek-R1-Distill-Qwen-1.5B-GGUF/resolve/main/DeepSeek-R1-Distill-Qwen-1.5B-Q4_K_M.gguf",
        ["DeepSeek R1 Distill Qwen 7B (Q4_K_M)"] = "https://huggingface.co/bartowski/DeepSeek-R1-Distill-Qwen-7B-GGUF/resolve/main/DeepSeek-R1-Distill-Qwen-7B-Q4_K_M.gguf",
        ["DeepSeek R1 Distill Llama 8B (Q4_K_M)"] = "https://huggingface.co/bartowski/DeepSeek-R1-Distill-Llama-8B-GGUF/resolve/main/DeepSeek-R1-Distill-Llama-8B-Q4_K_M.gguf",
        ["Mistral 7B Instruct v0.3 (Q4_K_M)"] = "https://huggingface.co/bartowski/Mistral-7B-Instruct-v0.3-GGUF/resolve/main/Mistral-7B-Instruct-v0.3-Q4_K_M.gguf",
        ["Phi 3 Mini 4K (Q4_K_M)"] = "https://huggingface.co/bartowski/Phi-3-mini-4k-instruct-GGUF/resolve/main/Phi-3-mini-4k-instruct-Q4_K_M.gguf",
        ["Phi 3.1 Mini 4K (Q4_K_M)"] = "https://huggingface.co/bartowski/Phi-3.1-mini-4k-instruct-GGUF/resolve/main/Phi-3.1-mini-4k-instruct-Q4_K_M.gguf",
        ["Llama 3.1 8B (Q4_K_M)"] = "https://huggingface.co/bartowski/Meta-Llama-3.1-8B-Instruct-GGUF/resolve/main/Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf",
        ["SmolLM2 1.7B (Q4_K_M)"] = "https://huggingface.co/bartowski/SmolLM2-1.7B-Instruct-GGUF/resolve/main/SmolLM2-1.7B-Instruct-Q4_K_M.gguf",
    };

    public bool IsDownloading => _downloadCts is not null;

    public ModelDownloadService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromHours(4)
        };
    }

    public async Task<string> DownloadAsync(string url, string saveFolder, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        _downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "model.gguf";

            var savePath = Path.Combine(saveFolder, fileName);

            if (!Directory.Exists(saveFolder))
                Directory.CreateDirectory(saveFolder);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            await using var contentStream = await response.Content.ReadAsStreamAsync(_downloadCts.Token);
            await using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

            var buffer = new byte[8192];
            var totalRead = 0L;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, _downloadCts.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), _downloadCts.Token);
                totalRead += bytesRead;

                if (totalBytes > 0)
                    progress?.Report((int)(totalRead * 100 / totalBytes));
            }

            return savePath;
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    public void CancelDownload()
    {
        _downloadCts?.Cancel();
    }
}
